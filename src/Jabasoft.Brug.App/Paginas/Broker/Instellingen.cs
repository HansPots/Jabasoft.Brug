using System.Text.Json.Nodes;
using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Instellingen: de AI-instellingen die de broker voor de hele pc
/// bewaart (server, model per taak, zoeken, wachttijd), plus Goose en
/// Hermes. Zelfde onderdelen als het oude Instellingen-scherm.
///
/// De instellingen gaan als JSON-boom heen en weer: alleen de velden op dit
/// scherm worden veranderd, de rest gaat ongewijzigd terug naar de broker.
/// Module-instellingen (AI Studio, TabStudio, ImageFunn) komen hier ook,
/// zodra die modules in de Brug zitten.
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

    private JsonObject? _instellingen;
    private List<string> _modellen = [];

    protected override async Task LaadAsync()
    {
        var instellingen = await BrokerApi.InstellingenAsync();
        var modellen = await BrokerApi.ServerModellenAsync();
        var goose = await BrokerApi.GooseAsync();
        var hermes = await BrokerApi.HermesAsync();

        Lijf.Children.Clear();
        if (instellingen.Waarde is null)
        {
            Lijf.Children.Add(Bouw.Melding($"Instellingen niet op te halen: {instellingen.Fout}. Start de broker en ververs dit scherm."));
            return;
        }

        _instellingen = instellingen.Waarde;
        _modellen = modellen.Waarde?.Models?.Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];

        var raster = Bouw.Kolommen(2);
        Bouw.Zet(raster, AiPaneel(), 0, 0);
        var rechts = new StackPanel { Spacing = 24 };
        rechts.Children.Add(GoosePaneel(goose));
        rechts.Children.Add(HermesPaneel(hermes));
        rechts.Children.Add(Bouw.Paneel("Modules", Bouw.Melding("AI Studio, TabStudio en ImageFunn hebben nog geen eigen instellingen in de Brug. Die komen hier zodra de module in de Brug zit."), "fase 6"));
        Bouw.Zet(raster, rechts, 0, 1);
        Lijf.Children.Add(raster);
    }

    private Controls.Paneel AiPaneel()
    {
        var instellingen = _instellingen!;
        var provider = instellingen["provider"]?.GetValue<string>() ?? "LmStudio";
        var sectie = instellingen[provider == "Ollama" ? "ollama" : "lmStudio"] as JsonObject ?? new JsonObject();

        var inhoud = new StackPanel { Spacing = 14 };

        var server = Keuze("AI-server", ["LmStudio", "Ollama"], provider, bewerkbaar: false);
        server.SelectionChanged += (_, _) =>
        {
            if (server.SelectedItem is string gekozen && gekozen != provider)
            {
                instellingen["provider"] = gekozen;
                _ = VervangAsync();
            }
        };
        inhoud.Children.Add(server);

        var adres = new TextBox { Header = "Adres van de server", Text = sectie["url"]?.GetValue<string>() ?? "" };
        inhoud.Children.Add(adres);

        inhoud.Children.Add(Bouw.Kop("MODEL PER TAAK"));
        var taakvelden = new List<(string Veld, ComboBox Keuze)>();
        foreach (var (veld, label) in Taken)
        {
            var keuze = Keuze(label, _modellen, sectie[veld]?.GetValue<string>() ?? "", bewerkbaar: true);
            taakvelden.Add((veld, keuze));
            inhoud.Children.Add(keuze);
        }

        inhoud.Children.Add(Bouw.Kop("ZOEKEN EN CONTEXT"));
        var zoekServer = Keuze("Zoeken loopt via", ["LmStudio", "Ollama"], instellingen["zoekProvider"]?.GetValue<string>() ?? provider, bewerkbaar: false);
        inhoud.Children.Add(zoekServer);

        var ondergrens = Schuif("Ondergrens semantisch zoeken", 0, 100, 1,
            Math.Round((instellingen["minimumSemanticScore"]?.GetValue<double>() ?? 0.5) * 100), w => $"{w:0}%");
        inhoud.Children.Add(ondergrens.Paneel);

        var rondes = Schuif("Automatisch meer context ophalen", 0, 5, 1,
            instellingen["maxContextRondes"]?.GetValue<int>() ?? 0, w => $"max {w:0} extra ronde(s)");
        inhoud.Children.Add(rondes.Paneel);

        var wachttijd = Schuif("Wachttijd op een antwoord", 1, 30, 1,
            Math.Round((instellingen["chatTimeoutSeconden"]?.GetValue<int>() ?? 300) / 60.0), w => $"{w:0} minuten");
        inhoud.Children.Add(wachttijd.Paneel);

        var melding = Bouw.Melding("");
        inhoud.Children.Add(Bouw.Knop("Opslaan", async (_, _) =>
        {
            sectie["url"] = adres.Text.Trim();
            foreach (var (veld, keuze) in taakvelden)
            {
                sectie[veld] = Waarde(keuze);
            }

            instellingen[provider == "Ollama" ? "ollama" : "lmStudio"] = sectie.DeepClone();
            instellingen["zoekProvider"] = Waarde(zoekServer);
            instellingen["minimumSemanticScore"] = ondergrens.Schuif.Value / 100.0;
            instellingen["maxContextRondes"] = (int)rondes.Schuif.Value;
            instellingen["chatTimeoutSeconden"] = (int)wachttijd.Schuif.Value * 60;

            var fout = await BrokerApi.BewaarInstellingenAsync(instellingen);
            melding.Text = fout is null ? $"Bewaard om {DateTime.Now:HH:mm:ss}." : $"Niet bewaard: {fout}";
            App.AppLog.Schrijf("brug", fout is null ? "AI-instellingen bewaard" : $"AI-instellingen niet bewaard: {fout}");
        }));
        inhoud.Children.Add(melding);

        return Bouw.Paneel("AI", inhoud, provider == "Ollama" ? "Ollama" : "LM Studio");
    }

    private Controls.Paneel GoosePaneel(Meting<JsonObject> goose)
    {
        if (goose.Waarde is not { } instelling)
        {
            return Bouw.Paneel("Goose", Bouw.Melding($"Niet op te halen: {goose.Fout}"));
        }

        var model = Keuze("Model", _modellen, instelling["model"]?.GetValue<string>() ?? "", bewerkbaar: true);
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

    private Controls.Paneel HermesPaneel(Meting<JsonObject> hermes)
    {
        if (hermes.Waarde is not { } instelling)
        {
            return Bouw.Paneel("Hermes", Bouw.Melding($"Niet op te halen: {hermes.Fout}"));
        }

        var provider = new TextBox { Header = "Hoofdmodel via", Text = instelling["provider"]?.GetValue<string>() ?? "" };
        var model = Keuze("Model", _modellen, instelling["model"]?.GetValue<string>() ?? "", bewerkbaar: true);
        var lokaal = Keuze("Model voor bijtaken en als reserve", _modellen, instelling["lokaalModel"]?.GetValue<string>() ?? "", bewerkbaar: true);
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

    /// <summary>Na het wisselen van server: het AI-paneel opnieuw opbouwen met de velden van die server.</summary>
    private async Task VervangAsync()
    {
        await Task.Yield();
        if (Lijf.Children.FirstOrDefault() is Grid raster && raster.Children.FirstOrDefault() is Controls.Paneel oud)
        {
            raster.Children.Remove(oud);
            Bouw.Zet(raster, AiPaneel(), 0, 0);
        }
    }

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
