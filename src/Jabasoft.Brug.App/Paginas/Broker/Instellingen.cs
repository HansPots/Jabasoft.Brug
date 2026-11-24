using System.Text.Json.Nodes;
using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Instellingen: de AI-instellingen die de broker voor de hele pc
/// bewaart, plus Goose en Hermes.
///
/// Opbouw:
/// 1. de actieve server - de broker stuurt alles (apps, Goose, Hermes én
///    zoeken) naar die ene server - met de algemene schuifjes;
/// 2. per server (LM Studio en Ollama) een eigen paneel met adres en het
///    model per taak, elk met de modellijst van die server;
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

        // De modellijst van elke server wordt tegelijk opgehaald, maar het
        // scherm wacht er niet op: een server die niet draait (Ollama) laat
        // de broker een paar seconden zoeken. De lijsten vullen zich later in.
        var lijstTaken = Servers
            .Select(s => BrokerApi.ModellenVanAsync(s.Provider, Sectie(geheel, s.Sectie)["url"]?.GetValue<string>() ?? ""))
            .ToArray();
        var actief = geheel["provider"]?.GetValue<string>() ?? "LmStudio";

        // 2. Per server een paneel met adres en model per taak.
        var serverVelden = new List<(string Sectie, TextBox Adres, List<(string Veld, ComboBox Keuze)> Taken)>();
        var serverRaster = Bouw.Kolommen(2);
        for (var i = 0; i < Servers.Length; i++)
        {
            var (provider, sectie, naam) = Servers[i];
            var waarden = Sectie(geheel, sectie);

            var inhoud = new StackPanel { Spacing = 14 };
            var adres = new TextBox { Header = "Adres van de server", Text = waarden["url"]?.GetValue<string>() ?? "" };
            var stand = Bouw.Melding("Modellijst ophalen…");
            inhoud.Children.Add(adres);
            inhoud.Children.Add(stand);

            var taken = new List<(string Veld, ComboBox Keuze)>();
            foreach (var (veld, label) in Taken)
            {
                var keuze = Keuze(label, [], waarden[veld]?.GetValue<string>() ?? "", bewerkbaar: true);
                taken.Add((veld, keuze));
                inhoud.Children.Add(keuze);
            }

            serverVelden.Add((sectie, adres, taken));
            var paneel = Bouw.Paneel(naam, inhoud, provider == actief ? "actief" : "");
            Bouw.Zet(serverRaster, paneel, 0, i);
            _ = VulServerAsync(lijstTaken[i], naam, provider == actief, paneel, stand, taken);
        }

        // 1. De actieve server en de algemene instellingen, met één Opslaan voor alles hierboven en hieronder.
        Lijf.Children.Add(ActiefPaneel(geheel, actief, serverVelden));
        Lijf.Children.Add(serverRaster);

        // 3. Goose, Hermes en de modules; hun keuzelijsten krijgen de modellen van de actieve server.
        var actieveLijst = await lijstTaken[Array.FindIndex(Servers, s => s.Provider == actief)];
        var modellen = actieveLijst.Waarde?.Models?.Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        var onder = Bouw.Kolommen(3);
        Bouw.Zet(onder, GoosePaneel(await goose, modellen), 0, 0);
        Bouw.Zet(onder, HermesPaneel(await hermes, modellen), 0, 1);
        Bouw.Zet(onder, Bouw.Paneel("Modules", Bouw.Melding("AI Studio, TabStudio en ImageFunn hebben nog geen eigen instellingen in de Brug. Die komen hier zodra de module in de Brug zit."), "fase 6"), 0, 2);
        Lijf.Children.Add(onder);
    }

    /// <summary>Zet de modellijst van een server in zijn keuzelijsten zodra hij binnen is; wat er al stond, blijft staan.</summary>
    private static async Task VulServerAsync(
        Task<Meting<ModellenLijst>> taak,
        string naam,
        bool actief,
        Controls.Paneel paneel,
        TextBlock stand,
        List<(string Veld, ComboBox Keuze)> taken)
    {
        var lijst = await taak;
        var modellen = lijst.Waarde?.Success == true ? lijst.Waarde.Models?.Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [] : [];

        if (modellen.Count == 0)
        {
            stand.Text = lijst.Waarde?.ErrorMessage is { Length: > 0 } fout
                ? $"Geen modellijst: {fout}"
                : $"{naam} is niet bereikbaar op dit adres; je kunt de modelnamen wel intypen.";
            paneel.Rechts = actief ? "ACTIEF · NIET BEREIKBAAR" : "NIET BEREIKBAAR";
            return;
        }

        stand.Visibility = Visibility.Collapsed;
        paneel.Rechts = actief ? $"ACTIEF · {modellen.Count} MODELLEN" : $"{modellen.Count} MODELLEN";
        foreach (var (_, keuze) in taken)
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

    private static Controls.Paneel ActiefPaneel(
        JsonObject geheel,
        string actief,
        List<(string Sectie, TextBox Adres, List<(string Veld, ComboBox Keuze)> Taken)> serverVelden)
    {
        var namen = Servers.Select(s => s.Naam).ToList();
        var server = Keuze("Actieve server", namen, Servers.First(s => s.Provider == actief).Naam, bewerkbaar: false);

        var ondergrens = Schuif("Ondergrens semantisch zoeken", 0, 100, 1,
            Math.Round((geheel["minimumSemanticScore"]?.GetValue<double>() ?? 0.5) * 100), w => $"{w:0}%");
        var rondes = Schuif("Automatisch meer context ophalen", 0, 5, 1,
            geheel["maxContextRondes"]?.GetValue<int>() ?? 0, w => $"max {w:0} extra ronde(s)");
        var wachttijd = Schuif("Wachttijd op een antwoord", 1, 30, 1,
            Math.Round((geheel["chatTimeoutSeconden"]?.GetValue<int>() ?? 300) / 60.0), w => $"{w:0} minuten");

        var melding = Bouw.Melding("");
        var opslaan = Bouw.Knop("Opslaan", async (_, _) =>
        {
            var gekozen = Servers.First(s => s.Naam == (server.SelectedItem as string)).Provider;
            geheel["provider"] = gekozen;

            // Zoeken volgt de actieve server: er is één plek die bepaalt waar alles heen gaat.
            geheel["zoekProvider"] = gekozen;

            foreach (var (sectie, adres, taken) in serverVelden)
            {
                var waarden = Sectie(geheel, sectie).DeepClone().AsObject();
                waarden["url"] = adres.Text.Trim();
                foreach (var (veld, keuze) in taken)
                {
                    waarden[veld] = Waarde(keuze);
                }

                geheel[sectie] = waarden;
            }

            geheel["minimumSemanticScore"] = ondergrens.Schuif.Value / 100.0;
            geheel["maxContextRondes"] = (int)rondes.Schuif.Value;
            geheel["chatTimeoutSeconden"] = (int)wachttijd.Schuif.Value * 60;

            var fout = await BrokerApi.BewaarInstellingenAsync(geheel);
            melding.Text = fout is null ? $"Bewaard om {DateTime.Now:HH:mm:ss}." : $"Niet bewaard: {fout}";
            App.AppLog.Schrijf("brug", fout is null ? $"AI-instellingen bewaard (actief: {gekozen})" : $"AI-instellingen niet bewaard: {fout}");
        });

        var uitleg = Bouw.Melding("De broker stuurt alles naar deze server: de apps, Goose, Hermes en het zoeken. " +
                                  "Wissel je van server en heeft die een ander zoekmodel, dan bouwt de broker de zoekindex op de achtergrond opnieuw op.");

        var raster = Bouw.Kolommen(2, 32);
        Bouw.Zet(raster, new StackPanel { Spacing = 10, Children = { server, uitleg } }, 0, 0);
        Bouw.Zet(raster, new StackPanel { Spacing = 10, Children = { ondergrens.Paneel, rondes.Paneel, wachttijd.Paneel } }, 0, 1);

        var inhoud = new StackPanel { Spacing = 14, Children = { raster, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { opslaan, melding } } } };
        melding.VerticalAlignment = VerticalAlignment.Center;
        return Bouw.Paneel("AI", inhoud, "Opslaan bewaart ook beide servers");
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
