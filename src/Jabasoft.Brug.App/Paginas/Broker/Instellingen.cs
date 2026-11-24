using System.Text.Json.Nodes;
using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Instellingen.
///
/// Opbouw:
/// 1. AI: de actieve server, het adres van elke server, en één lijst
///    "model per taak". Een model hoort bij de taak, niet bij de server: de
///    broker stuurt de vraag naar de actieve server en die laadt het model
///    zelf als het nog niet geladen is. De broker bewaart per server een
///    eigen set taakmodellen; de Brug schrijft de ene lijst in beide sets,
///    zodat de broker zelf niet hoeft te veranderen.
/// 2. Applicaties: ondergrens, extra context en wachttijd. Die horen bij de
///    applicaties (AI Studio, Proefbank) en verhuizen naar hun module zodra
///    die in de Brug zit; tot dan staan ze hier.
/// 3. Goose, Hermes en (later) de modules.
///
/// De instellingen gaan als JSON-boom heen en weer: alleen de velden op dit
/// scherm worden veranderd, de rest gaat ongewijzigd terug naar de broker.
/// </summary>
public sealed class Instellingen : BrokerPagina
{
    private static readonly (string Veld, string Label)[] Taken =
    [
        ("chatModel", "Daily chat"),
        ("codeModel", "Code (gesprek)"),
        ("controleModel", "Controle"),
        ("beeldModel", "Beeld lezen"),
        ("embedModel", "Zoeken (embedding)"),
    ];

    /// <summary>De twee servers: naam in de broker, sectie in de instellingen, naam op het scherm.</summary>
    private static readonly (string Provider, string Sectie, string Naam)[] Servers =
    [
        ("LmStudio", "lmStudio", "LM Studio"),
        ("Ollama", "ollama", "Ollama"),
    ];

    protected override async Task LaadAsync()
    {
        var instellingen = await BrokerApi.InstellingenAsync();
        var goose = BrokerApi.GooseAsync();
        var hermes = BrokerApi.HermesAsync();

        Lijf.Children.Clear();
        if (instellingen.Waarde is not { } geheel)
        {
            Lijf.Children.Add(Bouw.Meldingpaneel("Instellingen", $"Niet op te halen: {instellingen.Fout}. Start de broker en ververs dit scherm."));
            return;
        }

        var actief = geheel["provider"]?.GetValue<string>() ?? "LmStudio";
        var actieveSectie = Sectie(geheel, Servers.First(s => s.Provider == actief).Sectie);

        // De modellijst van de actieve server; het scherm wacht er niet op.
        var lijstTaak = BrokerApi.ModellenVanAsync(actief, actieveSectie["url"]?.GetValue<string>() ?? "");

        var boven = Bouw.Kolommen(2);
        var (brugPaneel, keuzes, stand) = BrugPaneel(geheel, actief, actieveSectie);
        Bouw.Zet(boven, new StackPanel { Spacing = 24, Children = { BrokerPaneel(geheel, actief), brugPaneel } }, 0, 0);
        Bouw.Zet(boven, ApplicatiesPaneel(geheel), 0, 1);
        Lijf.Children.Add(boven);

        var lijst = await lijstTaak;
        var modellen = lijst.Waarde?.Success == true ? lijst.Waarde.Models?.Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [] : [];
        VulKeuzes(keuzes, modellen);
        stand.Text = modellen.Count > 0
            ? $"{modellen.Count} modellen op {Servers.First(s => s.Provider == actief).Naam}. Het gekozen model wordt geladen zodra er een vraag voor is."
            : $"Geen modellijst van de actieve server ({lijst.Waarde?.ErrorMessage ?? lijst.Fout}); je kunt de namen wel intypen.";

        var onder = Bouw.Kolommen(3);
        Bouw.Zet(onder, GoosePaneel(await goose, modellen), 0, 0);
        Bouw.Zet(onder, HermesPaneel(await hermes, modellen), 0, 1);
        Bouw.Zet(onder, Bouw.Paneel("Modules", Bouw.Melding("AI Studio, TabStudio en ImageFunn hebben nog geen eigen instellingen in de Brug. Die komen hier zodra de module in de Brug zit."), "fase 6"), 0, 2);
        Lijf.Children.Add(onder);
    }

    /// <summary>Broker: naar welke server hij alles stuurt, en waar die servers zitten.</summary>
    private static Controls.Paneel BrokerPaneel(JsonObject geheel, string actief)
    {
        var inhoud = new StackPanel { Spacing = 14 };

        var server = Keuze("Actieve server", Servers.Select(s => s.Naam), Servers.First(s => s.Provider == actief).Naam, bewerkbaar: false);
        inhoud.Children.Add(server);

        var adressen = new List<(string Sectie, TextBox Adres)>();
        foreach (var (_, sectie, naam) in Servers)
        {
            var adres = new TextBox { Header = $"Adres van {naam}", Text = Sectie(geheel, sectie)["url"]?.GetValue<string>() ?? "" };
            adressen.Add((sectie, adres));
            inhoud.Children.Add(adres);
        }

        var melding = Bouw.Melding("");
        melding.VerticalAlignment = VerticalAlignment.Center;
        var opslaan = Bouw.Knop("Opslaan", async (_, _) =>
        {
            var gekozen = Servers.First(s => s.Naam == (server.SelectedItem as string)).Provider;
            geheel["provider"] = gekozen;
            geheel["zoekProvider"] = gekozen; // zoeken volgt de actieve server

            foreach (var (sectie, adres) in adressen)
            {
                var waarden = Sectie(geheel, sectie).DeepClone().AsObject();
                waarden["url"] = adres.Text.Trim();
                geheel[sectie] = waarden;
            }

            var fout = await BrokerApi.BewaarInstellingenAsync(geheel);
            melding.Text = fout is null ? $"Bewaard om {DateTime.Now:HH:mm:ss}." : $"Niet bewaard: {fout}";
            App.AppLog.Schrijf("brug", fout is null ? $"Broker-instellingen bewaard (actief: {gekozen})" : $"Broker-instellingen niet bewaard: {fout}");
        });
        inhoud.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { opslaan, melding } });

        return Bouw.Paneel("Broker", inhoud, Servers.First(s => s.Provider == actief).Naam);
    }

    /// <summary>Brug: één lijst model per taak; bij opslaan in beide serversecties van de broker.</summary>
    private static (Controls.Paneel Paneel, List<(string Veld, ComboBox Keuze)> Keuzes, TextBlock Stand) BrugPaneel(
        JsonObject geheel, string actief, JsonObject actieveSectie)
    {
        var inhoud = new StackPanel { Spacing = 14 };
        var stand = Bouw.Melding("Modellijst ophalen…");
        inhoud.Children.Add(stand);

        // De taakmodellen komen uit de actieve server; bij het opslaan gaan ze naar beide.
        var keuzes = new List<(string Veld, ComboBox Keuze)>();
        foreach (var (veld, label) in Taken)
        {
            var keuze = Keuze(label, [], actieveSectie[veld]?.GetValue<string>() ?? "", bewerkbaar: true);
            keuzes.Add((veld, keuze));
            inhoud.Children.Add(keuze);
        }

        var melding = Bouw.Melding("");
        melding.VerticalAlignment = VerticalAlignment.Center;
        var opslaan = Bouw.Knop("Opslaan", async (_, _) =>
        {
            // Eén lijst, in beide serversecties: het model hoort bij de taak, niet bij de server.
            foreach (var (_, sectie, _) in Servers)
            {
                var waarden = Sectie(geheel, sectie).DeepClone().AsObject();
                foreach (var (veld, keuze) in keuzes)
                {
                    waarden[veld] = Waarde(keuze);
                }

                geheel[sectie] = waarden;
            }

            var fout = await BrokerApi.BewaarInstellingenAsync(geheel);
            melding.Text = fout is null ? $"Bewaard om {DateTime.Now:HH:mm:ss}." : $"Niet bewaard: {fout}";
            App.AppLog.Schrijf("brug", fout is null ? "Model per taak bewaard" : $"Model per taak niet bewaard: {fout}");
        });
        inhoud.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { opslaan, melding } });

        return (Bouw.Paneel("Brug", inhoud, "model per taak"), keuzes, stand);
    }

    /// <summary>Instellingen die bij de applicaties horen; staan hier tot hun module in de Brug zit.</summary>
    private static Controls.Paneel ApplicatiesPaneel(JsonObject geheel)
    {
        var ondergrens = Schuif("Ondergrens semantisch zoeken", 0, 100, 1,
            Math.Round((geheel["minimumSemanticScore"]?.GetValue<double>() ?? 0.5) * 100), w => $"{w:0}%");
        var rondes = Schuif("Automatisch meer context ophalen", 0, 5, 1,
            geheel["maxContextRondes"]?.GetValue<int>() ?? 0, w => $"max {w:0} extra ronde(s)");
        var wachttijd = Schuif("Wachttijd op een antwoord", 1, 30, 1,
            Math.Round((geheel["chatTimeoutSeconden"]?.GetValue<int>() ?? 300) / 60.0), w => $"{w:0} minuten");

        var melding = Bouw.Melding("");
        melding.VerticalAlignment = VerticalAlignment.Center;
        var opslaan = Bouw.Knop("Opslaan", async (_, _) =>
        {
            geheel["minimumSemanticScore"] = ondergrens.Schuif.Value / 100.0;
            geheel["maxContextRondes"] = (int)rondes.Schuif.Value;
            geheel["chatTimeoutSeconden"] = (int)wachttijd.Schuif.Value * 60;
            var fout = await BrokerApi.BewaarInstellingenAsync(geheel);
            melding.Text = fout is null ? $"Bewaard om {DateTime.Now:HH:mm:ss}." : $"Niet bewaard: {fout}";
        });

        var inhoud = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Bouw.Melding("Voor AI Studio en Proefbank. Deze verhuizen naar hun eigen module zodra die in de Brug zit."),
                ondergrens.Paneel,
                rondes.Paneel,
                wachttijd.Paneel,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { opslaan, melding } },
            },
        };
        return Bouw.Paneel("Applicaties", inhoud, "voorlopig hier");
    }

    private static void VulKeuzes(List<(string Veld, ComboBox Keuze)> keuzes, List<string> modellen)
    {
        foreach (var (_, keuze) in keuzes)
        {
            var huidig = keuze.Text;
            var items = modellen.ToList();
            if (!string.IsNullOrEmpty(huidig) && !items.Contains(huidig))
            {
                items.Insert(0, huidig);
            }

            keuze.ItemsSource = items;
            keuze.Text = huidig;
        }
    }

    private static Controls.Paneel GoosePaneel(Meting<JsonObject> goose, List<string> modellen)
    {
        if (goose.Waarde is not { } instelling)
        {
            return Bouw.Paneel("Goose", Bouw.Melding($"Niet op te halen: {goose.Fout}"));
        }

        var model = Keuze("Model", modellen, instelling["model"]?.GetValue<string>() ?? "", bewerkbaar: true);
        var context = Getalveld("Maximale context (0 = standaard)", instelling["maxContext"]?.GetValue<int>() ?? 0);
        var melding = Bouw.Melding("");

        var inhoud = new StackPanel { Spacing = 14, Children = { model, context } };
        inhoud.Children.Add(Bouw.Knop("Opslaan", async (_, _) =>
        {
            instelling["model"] = Waarde(model);
            instelling["maxContext"] = (int)(double.IsNaN(context.Value) ? 0 : context.Value);
            var fout = await BrokerApi.BewaarGooseAsync(instelling);
            melding.Text = fout is null ? $"Bewaard om {DateTime.Now:HH:mm:ss}." : $"Niet bewaard: {fout}";
        }));
        inhoud.Children.Add(melding);
        return Bouw.Paneel("Goose", inhoud, "Block");
    }

    private static Controls.Paneel HermesPaneel(Meting<JsonObject> hermes, List<string> modellen)
    {
        if (hermes.Waarde is not { } instelling)
        {
            return Bouw.Paneel("Hermes", Bouw.Melding($"Niet op te halen: {hermes.Fout}"));
        }

        var provider = new TextBox { Header = "Hoofdmodel via", Text = instelling["provider"]?.GetValue<string>() ?? "" };
        var model = Keuze("Model", modellen, instelling["model"]?.GetValue<string>() ?? "", bewerkbaar: true);
        var lokaal = Keuze("Model voor bijtaken en als reserve", modellen, instelling["lokaalModel"]?.GetValue<string>() ?? "", bewerkbaar: true);
        var mcp = new ToggleSwitch { Header = "Jabasoft-tools (MCP) aan", IsOn = instelling["jabasoftMcp"]?.GetValue<bool>() ?? false };
        var context = Getalveld("Maximale context (0 = standaard)", instelling["maxContext"]?.GetValue<int>() ?? 0);
        var melding = Bouw.Melding("");

        var inhoud = new StackPanel { Spacing = 14, Children = { provider, model, lokaal, mcp, context } };
        inhoud.Children.Add(Bouw.Knop("Opslaan", async (_, _) =>
        {
            instelling["provider"] = provider.Text.Trim();
            instelling["model"] = Waarde(model);
            instelling["lokaalModel"] = Waarde(lokaal);
            instelling["jabasoftMcp"] = mcp.IsOn;
            instelling["maxContext"] = (int)(double.IsNaN(context.Value) ? 0 : context.Value);
            var fout = await BrokerApi.BewaarHermesAsync(instelling);
            melding.Text = fout is null ? $"Bewaard om {DateTime.Now:HH:mm:ss}." : $"Niet bewaard: {fout}";
        }));
        inhoud.Children.Add(melding);
        return Bouw.Paneel("Hermes", inhoud, "Agent");
    }

    private static JsonObject Sectie(JsonObject geheel, string sectie) => geheel[sectie] as JsonObject ?? new JsonObject();

    private static ComboBox Keuze(string kop, IEnumerable<string> keuzes, string huidig, bool bewerkbaar)
    {
        var lijst = keuzes.ToList();
        if (!string.IsNullOrEmpty(huidig) && !lijst.Contains(huidig))
        {
            lijst.Insert(0, huidig);
        }

        var keuze = new ComboBox
        {
            Header = kop,
            ItemsSource = lijst,
            IsEditable = bewerkbaar,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (lijst.Contains(huidig))
        {
            keuze.SelectedItem = huidig;
        }

        if (bewerkbaar)
        {
            keuze.Text = huidig;
        }

        return keuze;
    }

    private static string Waarde(ComboBox keuze) =>
        (keuze.IsEditable ? keuze.Text : keuze.SelectedItem as string)?.Trim() ?? "";

    private static NumberBox Getalveld(string kop, int waarde) => new()
    {
        Header = kop,
        Value = waarde,
        Minimum = 0,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        SmallChange = 1024,
        LargeChange = 8192,
    };

    private static (StackPanel Paneel, Slider Schuif) Schuif(string kop, double min, double max, double stap, double waarde, Func<double, string> tekst)
    {
        var label = Bouw.Tekst($"{kop} — {tekst(waarde)}", "LabelStijl");
        var schuif = new Slider { Minimum = min, Maximum = max, StepFrequency = stap, Value = Math.Clamp(waarde, min, max) };
        schuif.ValueChanged += (_, e) => label.Text = $"{kop} — {tekst(e.NewValue)}";
        return (new StackPanel { Spacing = 2, Children = { label, schuif } }, schuif);
    }
}
