using System.Text.Json.Nodes;
using Jabasoft.Brug.App.Data;
using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Modellen: elk model op de AI-server, met waarvoor het is, waar
/// het goed en minder goed in is, voor welke taak het nu is ingesteld en
/// hoeveel tokens het al gebruikte.
///
/// De beschrijvingen staan in de eigen database van de Brug
/// (JabasoftBrug.dbo.Modellen) en zijn per kaart aan te passen. Welke taak
/// een model heeft en of het verborgen is, komt uit de broker-instellingen.
/// </summary>
public sealed class Modellen : BrokerPagina
{
    /// <summary>
    /// De plekken waar een model ingesteld kan staan: sleutel, kort label (pilletje
    /// in de lijst) en naam (knop in het detail). De vijf taakvelden staan in de
    /// broker-instellingen (in beide serversecties); "goose", "hermes" en
    /// "hermesLokaal" gaan via /api/goose en /api/hermes, net als op Instellingen.
    /// </summary>
    private static readonly (string Plek, string Kort, string Naam)[] Plekken =
    [
        ("chatModel", "Chat", "Daily chat"),
        ("codeModel", "Code", "Code (gesprek)"),
        ("controleModel", "Ctrl", "Controle"),
        ("beeldModel", "Beeld", "Beeld lezen"),
        ("embedModel", "Zoek", "Zoeken"),
        ("goose", "Goose", "Goose"),
        ("hermes", "Hermes", "Hermes"),
        ("hermesLokaal", "Hermes lok.", "Hermes bijtaken"),
    ];

    private static readonly string[] TaakVelden = ["chatModel", "codeModel", "controleModel", "beeldModel", "embedModel"];

    /// <summary>
    /// Plekken die niet leeg kunnen: de broker negeert een leeg Goose-model,
    /// en Hermes werkt niet zonder hoofdmodel. Daar alleen wisselen.
    /// </summary>
    private static readonly string[] MoetGevuld = ["goose", "hermes"];

    private static string Toon(string model) => model.Length == 0 ? "leeg" : model;

    /// <summary>Welk model nu op elke plek staat, en wat er in het detail nieuw gekozen is maar nog niet bewaard.</summary>
    private Dictionary<string, string> _houders = [];
    private readonly Dictionary<string, string> _nieuw = [];
    private string _plekMelding = "";
    private bool _bezig;

    /// <summary>Vaste kolombreedtes, zodat taak, soort en tokens in elke regel en elke groep onder elkaar staan.</summary>
    private static readonly GridLength KolomTaken = new(230);
    private static readonly GridLength KolomSoort = new(140);
    private static readonly GridLength KolomTokens = new(90);
    private static readonly GridLength KolomKwaliteit = new(100);

    /// <summary>Zelf ingestelde kwaliteit per model, en eigen favorieten (naam in kleine letters, onderdeel).</summary>
    private Dictionary<string, int> _sterren = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<(string Naam, string Onderdeel)> _favorieten = [];

    /// <summary>De kwaliteit van een model: de eigen waarde als die er is, anders de schatting.</summary>
    private (int Aantal, bool Eigen) Kwaliteit(string naam) =>
        _sterren.TryGetValue(naam, out var eigen) ? (eigen, true) : (Modelsterren.Schat(naam), false);

    // Wat er het laatst is opgehaald; een klik op een regel tekent opnieuw zonder alles op te halen.
    private Dictionary<string, Modelinfo> _info = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _taken = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, TokenModel> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _verborgen = [];
    private List<string> _opServer = [];
    private List<string> _namen = [];
    private bool _serverGelukt;
    private string? _serverFout;
    private string? _gekozen;
    private bool _verborgenOpen;

    protected override async Task LaadAsync()
    {
        // Vier losse bronnen, tegelijk opgehaald. Na elke bron die binnen is
        // wordt het scherm opnieuw getekend: de lijst staat er zodra de
        // beschrijvingen er zijn, taken en tokens vullen zich daarna aan.
        if (Lijf.Children.Count == 0)
        {
            Teken();
        }

        async Task BeschrijvingenAsync()
        {
            var beschrijvingen = await BrugDatabase.ModellenAsync();
            _info = beschrijvingen.ToDictionary(m => m.Naam, StringComparer.OrdinalIgnoreCase);
            (_sterren, _favorieten) = await BrugDatabase.SterrenAsync();
            _beschrijvingenBinnen = true;
            Teken();
        }

        async Task ServerAsync()
        {
            var server = await BrokerApi.ServerModellenAsync();
            _serverGelukt = server.Gelukt;
            _serverFout = server.Fout;
            _opServer = server.Waarde?.Models ?? [];
            _serverBinnen = true;
            Teken();
        }

        async Task InstellingenAsync()
        {
            var instellingenTaak = BrokerApi.InstellingenAsync();
            var gooseTaak = BrokerApi.GooseAsync();
            var hermesTaak = BrokerApi.HermesAsync();
            var serverActief = Lees((await instellingenTaak).Waarde);
            var goose = (await gooseTaak).Waarde;
            var hermes = (await hermesTaak).Waarde;
            _verborgen = (serverActief?["verborgen"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

            var houders = new Dictionary<string, string>();
            foreach (var veld in TaakVelden)
            {
                houders[veld] = Waarde(serverActief, veld);
            }

            houders["goose"] = Waarde(goose, "model");
            houders["hermes"] = Waarde(hermes, "model");
            houders["hermesLokaal"] = Waarde(hermes, "lokaalModel");
            _houders = houders.Where(h => h.Value.Length > 0).ToDictionary();

            var taken = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (plek, kort, _) in Plekken)
            {
                if (_houders.TryGetValue(plek, out var model))
                {
                    (taken.TryGetValue(model, out var lijst) ? lijst : taken[model] = []).Add(kort);
                }
            }

            _taken = taken;
            _takenBinnen = true;
            Teken();
        }

        async Task VerbruikAsync()
        {
            var verbruik = await BrokerApi.TokenModellenAsync();
            _tokens = (verbruik.Waarde ?? []).GroupBy(m => m.Model ?? "").ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Teken();
        }

        await Task.WhenAll(BeschrijvingenAsync(), ServerAsync(), InstellingenAsync(), VerbruikAsync());
    }

    // Welke bronnen al binnen zijn; zolang niet, toont het scherm "…" in plaats van een fout.
    private bool _beschrijvingenBinnen;
    private bool _serverBinnen;
    private bool _takenBinnen;

    /// <summary>Links de lijst per groep, rechts het detail van het gekozen model.</summary>
    private void Teken()
    {
        _namen = [.. _opServer.Concat(_info.Keys).Distinct(StringComparer.OrdinalIgnoreCase)];

        Lijf.Children.Clear();
        Lijf.Children.Add(Bouw.Band(
            ("Op de server", !_serverBinnen ? "…" : _serverGelukt ? _opServer.Count.ToString(Bouw.Nl) : "—"),
            ("Met een taak", _takenBinnen ? _taken.Count.ToString(Bouw.Nl) : "…"),
            ("Verborgen", _takenBinnen ? _verborgen.Count.ToString(Bouw.Nl) : "…"),
            ("Beschreven", _beschrijvingenBinnen ? _namen.Count(_info.ContainsKey).ToString(Bouw.Nl) : "…")));

        if (_namen.Count == 0)
        {
            Lijf.Children.Add(Bouw.Meldingpaneel("Modellen", "Ophalen…"));
            return;
        }

        if (_serverBinnen && !_serverGelukt)
        {
            Lijf.Children.Add(Bouw.Meldingpaneel("AI-server", $"De modellijst is niet op te halen ({_serverFout}). Hieronder de beschreven modellen."));
        }

        if (!BrugDatabase.Beschikbaar)
        {
            Lijf.Children.Add(Bouw.Meldingpaneel("Database", $"{BrugDatabase.Naam} niet bereikbaar ({BrugDatabase.Fout}): de ingebouwde beschrijvingen worden getoond en aanpassen kan nu niet."));
        }

        var groepen = new (string Kop, List<string> Leden)[]
        {
            ("In gebruik", Leden(n => _taken.ContainsKey(n))),
            ("Beschikbaar", Leden(n => !_taken.ContainsKey(n) && !_verborgen.Contains(n) && (!_serverGelukt || _opServer.Contains(n)))),
            ("Verborgen", Leden(n => !_taken.ContainsKey(n) && _verborgen.Contains(n))),
            ("Niet op de server", Leden(n => !_taken.ContainsKey(n) && !_verborgen.Contains(n) && _serverGelukt && !_opServer.Contains(n))),
        };

        // Bij het openen: het eerste model dat in gebruik is (of anders het eerste dat er is).
        // Pas kiezen als bekend is welke modellen een taak hebben, anders zou het eerste "beschikbare" winnen.
        if (_takenBinnen && (_gekozen is null || !_namen.Contains(_gekozen, StringComparer.OrdinalIgnoreCase)))
        {
            _gekozen = groepen.SelectMany(g => g.Leden).FirstOrDefault();
        }

        var links = new StackPanel { Spacing = 16 };
        foreach (var (kop, leden) in groepen)
        {
            if (leden.Count == 0)
            {
                continue;
            }

            var lijst = new StackPanel();
            var ingeklapt = kop == "Verborgen" && !_verborgenOpen && !leden.Contains(_gekozen ?? "");
            if (!ingeklapt)
            {
                foreach (var naam in leden)
                {
                    lijst.Children.Add(Regel(naam));
                }
            }

            if (kop == "Verborgen")
            {
                var knop = Bouw.Knop(ingeklapt ? $"Toon {leden.Count} verborgen modellen" : "Inklappen", (_, _) =>
                {
                    _verborgenOpen = ingeklapt;
                    Teken();
                }, "BorderBrush");
                knop.Margin = new Thickness(0, ingeklapt ? 0 : 8, 0, 0);
                lijst.Children.Add(knop);
            }

            links.Children.Add(Bouw.Groep(kop, leden.Count.ToString(Bouw.Nl), lijst, "BorderBrush"));
        }

        var raster = new Grid { ColumnSpacing = 24 };
        raster.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        raster.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        raster.Children.Add(links);

        if (_gekozen is { } gekozen)
        {
            var detail = new Modelkaart(
                gekozen,
                _info.GetValueOrDefault(gekozen),
                _taken.GetValueOrDefault(gekozen) ?? [],
                _verborgen.Contains(gekozen),
                !_serverGelukt || _opServer.Contains(gekozen),
                _tokens.GetValueOrDefault(gekozen),
                () => new StackPanel { Spacing = 10, Children = { GebruiktBlok(gekozen), SterrenBlok(gekozen) } })
            {
                VerticalAlignment = VerticalAlignment.Top,
            };
            detail.Bewaard += async (_, _) => await VerversAsync();
            Grid.SetColumn(detail, 1);
            raster.Children.Add(detail);
        }
        else
        {
            var wacht = Bouw.Groep("Model", "", Bouw.Melding("Ophalen…"), "AccentBrushDark");
            wacht.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(wacht, 1);
            raster.Children.Add(wacht);
        }

        Lijf.Children.Add(raster);
    }

    /// <summary>
    /// GEBRUIKT VOOR: een knop per plek waar dit model past. Donkergeel = staat
    /// hier nu; lila = kan hier; oranje = nieuw gekozen; rose = wordt vervangen
    /// door een ander gekozen model. Kiezen gebeurt eerst alleen op het scherm;
    /// pas Opslaan schrijft het weg (zonder extra vraag). Een aangevinkte plek
    /// uitvinken maakt hem leeg, behalve bij Goose en Hermes (zie MoetGevuld).
    /// </summary>
    private StackPanel GebruiktBlok(string naam)
    {
        var blok = new StackPanel { Spacing = 6 };
        blok.Children.Add(Bouw.Tekst("GEBRUIKT VOOR", "LabelStijl"));

        if (!_takenBinnen)
        {
            blok.Children.Add(Bouw.Melding("Ophalen…"));
            return blok;
        }

        var geschikt = Geschikt(naam);
        if (geschikt.Count == 0)
        {
            blok.Children.Add(Bouw.Melding("Geen taakmodel: dit model wordt niet via een taak ingesteld."));
        }
        else
        {
            var knoppen = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var (plek, _, plekNaam) in Plekken.Where(p => geschikt.Contains(p.Plek)))
            {
                var huidig = _houders.GetValueOrDefault(plek);
                var nieuw = _nieuw.GetValueOrDefault(plek);
                var ditModel = Gelijk(huidig, naam);
                var gekozen = Gelijk(nieuw, naam);
                var vervangen = ditModel && nieuw is not null && !gekozen;
                var leegbaar = !MoetGevuld.Contains(plek);

                var (kleur, tip) = (gekozen, ditModel, vervangen) switch
                {
                    (true, _, _) => ("AccentBrush", $"Nieuw gekozen voor {plekNaam}, nog niet bewaard (nu: {huidig ?? "niets"}). Klik om terug te draaien."),
                    (_, _, true) when nieuw == "" => ("TextMutedBrush", $"Wordt bij Opslaan leeggemaakt. Klik om terug te draaien."),
                    (_, _, true) => ("TextMutedBrush", $"Wordt bij Opslaan vervangen door {nieuw}. Klik om terug te draaien."),
                    (_, true, _) when leegbaar => ("AccentBrushDark", $"Staat nu ingesteld voor {plekNaam}. Klik om uit te vinken: bij Opslaan wordt de plek leeg."),
                    (_, true, _) => ("AccentBrushDark", $"Staat nu ingesteld voor {plekNaam}. Deze plek kan niet leeg; wisselen: vink hem aan bij een ander model."),
                    _ => ("BorderBrush", $"Nu: {huidig ?? "niets"}. Klik om dit model voor {plekNaam} te kiezen."),
                };

                // Vinkje: aan = staat hier (of wordt hier gezet), uit = kan hier.
                var aan = gekozen || (ditModel && !vervangen);
                var knop = Bouw.Knop($"{(aan ? "☑" : "☐")}  {plekNaam}", (_, _) =>
                {
                    if (gekozen || vervangen)
                    {
                        _nieuw.Remove(plek);
                    }
                    else if (!ditModel)
                    {
                        _nieuw[plek] = naam;
                    }
                    else if (leegbaar)
                    {
                        // Uitvinken: leeg bewaren ("" = leegmaken).
                        _nieuw[plek] = "";
                    }
                    else
                    {
                        return;
                    }

                    _plekMelding = "";
                    Teken();
                }, kleur);
                ToolTipService.SetToolTip(knop, tip);
                knoppen.Children.Add(knop);
            }

            blok.Children.Add(knoppen);
        }

        if (_nieuw.Count > 0 || _plekMelding.Length > 0)
        {
            var rij = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
            if (_nieuw.Count > 0)
            {
                rij.Children.Add(Bouw.Knop("Opslaan", async (_, _) => await BewaarPlekkenAsync()));
                rij.Children.Add(Bouw.Knop("Annuleren", (_, _) =>
                {
                    _nieuw.Clear();
                    Teken();
                }, "TextMutedBrush"));
            }

            var tekst = _nieuw.Count > 0
                ? "Nog niet bewaard: " + string.Join(" · ", Plekken.Where(p => _nieuw.ContainsKey(p.Plek)).Select(p => $"{p.Naam} → {Toon(_nieuw[p.Plek])}"))
                : _plekMelding;
            var melding = Bouw.Melding(_plekMelding.Length > 0 && _nieuw.Count > 0 ? $"{_plekMelding} {tekst}" : tekst);
            melding.VerticalAlignment = VerticalAlignment.Center;
            rij.Children.Add(melding);
            blok.Children.Add(rij);
        }

        return blok;
    }

    /// <summary>
    /// Waar een model past, op grond van de soort in de catalogus: een
    /// embeddingmodel alleen voor Zoeken, een beeldmodel voor Beeld lezen en
    /// Hermes bijtaken (die krijgt de plaatjes), een beeldmaker nergens, de
    /// rest voor chat, code, controle, Goose en Hermes. Een plek waar het
    /// model nu al staat, komt er altijd bij.
    /// </summary>
    private HashSet<string> Geschikt(string naam)
    {
        var soort = (_info.TryGetValue(naam, out var info) ? info.Soort : "").ToLowerInvariant();
        var laag = naam.ToLowerInvariant();

        HashSet<string> plekken = soort switch
        {
            _ when soort.Contains("afbeelding") => [],
            _ when soort.Contains("embed") || laag.Contains("embed") => ["embedModel"],
            _ when soort.Contains("beeld") || laag.Contains("-vl") => ["beeldModel", "hermesLokaal"],
            _ => ["chatModel", "codeModel", "controleModel", "goose", "hermes", "hermesLokaal"],
        };

        foreach (var (plek, model) in _houders)
        {
            if (Gelijk(model, naam))
            {
                plekken.Add(plek);
            }
        }

        return plekken;
    }

    /// <summary>
    /// Schrijft de nieuw gekozen plekken weg, telkens op verse instellingen:
    /// taakvelden in beide serversecties (het model hoort bij de taak, niet bij
    /// de server), Goose en Hermes via hun eigen config. Wat lukt verdwijnt uit
    /// de keuzes; wat niet lukt blijft staan met de reden.
    /// </summary>
    private async Task BewaarPlekkenAsync()
    {
        if (_bezig)
        {
            return;
        }

        _bezig = true;
        var fouten = new List<string>();
        try
        {
            var taken = _nieuw.Where(n => TaakVelden.Contains(n.Key)).ToList();
            if (taken.Count > 0)
            {
                var fout = (await BrokerApi.InstellingenAsync()) is { Waarde: { } geheel }
                    ? await BewaarTakenAsync(geheel, taken)
                    : "instellingen niet op te halen";
                Verwerk(taken.Select(t => t.Key), "Model per taak", fout);
            }

            if (_nieuw.TryGetValue("goose", out var gooseModel))
            {
                var fout = (await BrokerApi.GooseAsync()) is { Waarde: { } goose }
                    ? await BrokerApi.BewaarGooseAsync(Met(goose, "model", gooseModel))
                    : "Goose-instelling niet op te halen";
                Verwerk(["goose"], "Goose-model", fout);
            }

            var hermesPlekken = _nieuw.Where(n => n.Key is "hermes" or "hermesLokaal").ToList();
            if (hermesPlekken.Count > 0)
            {
                string? fout;
                if ((await BrokerApi.HermesAsync()) is { Waarde: { } hermes })
                {
                    foreach (var (plek, model) in hermesPlekken)
                    {
                        hermes[plek == "hermes" ? "model" : "lokaalModel"] = model;
                    }

                    fout = await BrokerApi.BewaarHermesAsync(hermes);
                }
                else
                {
                    fout = "Hermes-instelling niet op te halen";
                }

                Verwerk(hermesPlekken.Select(h => h.Key), "Hermes-model", fout);
            }
        }
        finally
        {
            _bezig = false;
        }

        _plekMelding = fouten.Count == 0 ? $"Bewaard om {DateTime.Now:HH:mm:ss}." : $"Niet alles bewaard: {string.Join("; ", fouten)}.";
        await VerversAsync();

        void Verwerk(IEnumerable<string> plekken, string wat, string? fout)
        {
            var lijst = plekken.ToList();
            var beschrijving = string.Join(", ", lijst.Select(p => $"{Plekken.First(x => x.Plek == p).Naam} → {Toon(_nieuw[p])}"));
            if (fout is null)
            {
                App.AppLog.Schrijf("brug", $"{wat} bewaard: {beschrijving}");
                lijst.ForEach(p => _nieuw.Remove(p));
            }
            else
            {
                App.AppLog.Schrijf("brug", $"{wat} niet bewaard ({beschrijving}): {fout}");
                fouten.Add($"{wat}: {fout}");
            }
        }
    }

    private static Task<string?> BewaarTakenAsync(JsonObject geheel, List<KeyValuePair<string, string>> taken)
    {
        foreach (var sectie in new[] { "lmStudio", "ollama" })
        {
            var waarden = geheel[sectie] as JsonObject ?? new JsonObject();
            foreach (var (veld, model) in taken)
            {
                waarden[veld] = model;
            }

            geheel[sectie] = waarden;
        }

        return BrokerApi.BewaarInstellingenAsync(geheel);
    }

    private static JsonObject Met(JsonObject obj, string veld, string waarde)
    {
        obj[veld] = waarde;
        return obj;
    }

    private static string Waarde(JsonObject? obj, string veld) =>
        obj?[veld] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    private static bool Gelijk(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Kwaliteit en sterren per onderdeel in het detail. Kwaliteit: klik op de
    /// n-de ster om n sterren te geven (oranje = zelf ingesteld); "Schatting"
    /// zet hem terug (lila = geschat uit de grootte). Per onderdeel: lila ster =
    /// samen vastgesteld (reden in de tooltip), oranje ster = jouw favoriet -
    /// klikken zet of haalt je favoriet.
    /// </summary>
    private StackPanel SterrenBlok(string naam)
    {
        var (aantal, eigen) = Kwaliteit(naam);
        var blok = new StackPanel { Spacing = 6 };

        blok.Children.Add(Bouw.Tekst("KWALITEIT", "LabelStijl"));
        var rij = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var n = 1; n <= 5; n++)
        {
            var waarde = n;
            var ster = SterKnop(n <= aantal ? "★" : "☆", n <= aantal ? (eigen ? "AccentBrush" : "BorderBrush") : "TextMutedBrush",
                $"{waarde} van 5 sterren geven");
            ster.Click += async (_, _) => await ZetKwaliteitAsync(naam, waarde);
            rij.Children.Add(ster);
        }

        var uitleg = Bouw.Tekst(eigen ? "zelf ingesteld" : "geschat uit de grootte", "LabelStijl");
        uitleg.VerticalAlignment = VerticalAlignment.Center;
        uitleg.Margin = new Thickness(12, 0, 12, 0);
        rij.Children.Add(uitleg);
        if (eigen)
        {
            rij.Children.Add(Bouw.Knop("Schatting", async (_, _) => await ZetKwaliteitAsync(naam, null), "TextMutedBrush"));
        }

        blok.Children.Add(rij);

        blok.Children.Add(Bouw.Tekst("PER ONDERDEEL", "LabelStijl"));
        var onderdelen = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (var (sleutel, onderdeelNaam) in Modelsterren.Onderdelen)
        {
            var vastgesteld = Modelsterren.Vastgesteld(naam, sleutel);
            var favoriet = _favorieten.Contains((naam.ToLowerInvariant(), sleutel));

            var stuk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            if (vastgesteld.Count > 0)
            {
                var lila = Bouw.Tekst("★", "LabelStijl");
                lila.Foreground = Bouw.Kleur("BorderBrush");
                lila.FontSize = 18;
                lila.VerticalAlignment = VerticalAlignment.Center;
                ToolTipService.SetToolTip(lila, new TextBlock
                {
                    Text = $"Samen vastgesteld voor {onderdeelNaam}:\n\n{string.Join("\n\n", vastgesteld.Select(a => a.Reden))}",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520,
                });
                stuk.Children.Add(lila);
            }

            var eigenSter = SterKnop(favoriet ? "★" : "☆", favoriet ? "AccentBrush" : "TextMutedBrush",
                favoriet ? $"Jouw favoriet voor {onderdeelNaam} - klik om weg te halen" : $"Klik om favoriet te maken voor {onderdeelNaam}");
            eigenSter.Click += async (_, _) => await ZetFavorietAsync(naam, sleutel, !favoriet);
            stuk.Children.Add(eigenSter);

            var label = Bouw.Tekst(onderdeelNaam);
            label.VerticalAlignment = VerticalAlignment.Center;
            stuk.Children.Add(label);
            onderdelen.Children.Add(stuk);
        }

        blok.Children.Add(onderdelen);
        return blok;
    }

    private static Button SterKnop(string teken, string kleur, string tip)
    {
        var knop = new Button
        {
            Content = new TextBlock { Text = teken, FontSize = 18, Foreground = Bouw.Kleur(kleur) },
            Background = null,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0, 2, 0),
            MinWidth = 0,
        };
        ToolTipService.SetToolTip(knop, tip);
        return knop;
    }

    private async Task ZetKwaliteitAsync(string naam, int? sterren)
    {
        var fout = await BrugDatabase.ZetSterrenAsync(naam, sterren);
        if (fout is not null)
        {
            App.AppLog.Schrijf("brug", $"Kwaliteit niet bewaard: {fout}");
            return;
        }

        if (sterren is { } aantal)
        {
            _sterren[naam] = aantal;
        }
        else
        {
            _sterren.Remove(naam);
        }

        Teken();
    }

    private async Task ZetFavorietAsync(string naam, string onderdeel, bool favoriet)
    {
        var fout = await BrugDatabase.ZetFavorietAsync(naam, onderdeel, favoriet);
        if (fout is not null)
        {
            App.AppLog.Schrijf("brug", $"Favoriet niet bewaard: {fout}");
            return;
        }

        if (favoriet)
        {
            _favorieten.Add((naam.ToLowerInvariant(), onderdeel));
        }
        else
        {
            _favorieten.Remove((naam.ToLowerInvariant(), onderdeel));
        }

        Teken();
    }

    private List<string> Leden(Func<string, bool> hoort) =>
        [.. _namen.Where(hoort).OrderBy(n => _info.TryGetValue(n, out var i) ? i.Soort : "~").ThenBy(n => n)];

    /// <summary>Eén model als regel met vaste kolommen: naam, taken, soort, tokens. Klik = tonen in het detail.</summary>
    private Button Regel(string naam)
    {
        var gekozen = string.Equals(naam, _gekozen, StringComparison.OrdinalIgnoreCase);
        var tekstKleur = Bouw.Kleur(gekozen ? "AccentForegroundBrush" : "TextPrimaryBrush");
        var bijKleur = Bouw.Kleur(gekozen ? "AccentForegroundBrush" : "TextMutedBrush");

        var rij = new Grid { ColumnSpacing = 12 };
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = KolomTaken });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = KolomKwaliteit });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = KolomSoort });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = KolomTokens });

        var naamTekst = Bouw.Tekst(naam);
        naamTekst.Foreground = tekstKleur;
        naamTekst.VerticalAlignment = VerticalAlignment.Center;
        rij.Children.Add(naamTekst);

        var pillen = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var taak in _taken.GetValueOrDefault(naam) ?? [])
        {
            // Op de donkergele gekozen regel zou een oranje pilletje wegvallen: daar lila.
            pillen.Children.Add(Bouw.Pil(taak, gekozen ? "BorderBrush" : "AccentBrush"));
        }

        Grid.SetColumn(pillen, 1);
        rij.Children.Add(pillen);

        // Kwaliteit: zelf ingesteld = oranje, geschat uit de grootte = lila (op de gekozen regel zwart).
        var (aantal, eigen) = Kwaliteit(naam);
        var sterren = Bouw.Tekst(Modelsterren.Tekst(aantal), "LabelStijl");
        sterren.Foreground = Bouw.Kleur(gekozen ? "AccentForegroundBrush" : eigen ? "AccentBrush" : "BorderBrush");
        sterren.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(sterren, eigen ? "Kwaliteit: zelf ingesteld" : "Kwaliteit: geschat uit de grootte");
        Grid.SetColumn(sterren, 2);
        rij.Children.Add(sterren);

        var soort = Bouw.Tekst(_info.TryGetValue(naam, out var info) ? info.Soort.ToLower(Bouw.Nl) : "—", "LabelStijl");
        soort.Foreground = bijKleur;
        soort.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(soort, 3);
        rij.Children.Add(soort);

        var tokens = Bouw.Tekst(_tokens.TryGetValue(naam, out var t) ? Bouw.Kort(t.TotalTokens) : "—", "LabelStijl");
        tokens.Foreground = bijKleur;
        tokens.HorizontalAlignment = HorizontalAlignment.Right;
        tokens.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tokens, 4);
        rij.Children.Add(tokens);

        var knop = new Button
        {
            Content = rij,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = gekozen ? Bouw.Kleur("AccentBrushDark") : null,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12, 12, 12, 12),
            Padding = new Thickness(10, 5, 10, 5),
        };
        knop.Click += (_, _) =>
        {
            _gekozen = naam;
            Teken();
        };
        return knop;
    }

    /// <summary>Het deel van de instellingen voor de server die nu gekozen is (lmStudio of ollama).</summary>
    private static JsonObject? Lees(JsonObject? instellingen) =>
        instellingen?["provider"]?.GetValue<string>() == "Ollama"
            ? instellingen["ollama"] as JsonObject
            : instellingen?["lmStudio"] as JsonObject;

    /// <summary>Eén model: lezen, en met BEWERKEN de beschrijving aanpassen.</summary>
    private sealed class Modelkaart : ContentControl
    {
        private readonly string _naam;
        private readonly Modelinfo? _info;
        private readonly IReadOnlyList<string> _taken;
        private readonly bool _verborgen;
        private readonly bool _opServer;
        private readonly TokenModel? _tokens;
        // Een maker in plaats van een kant-en-klaar blok: na Bewerken > Annuleren
        // hangt het oude blok nog in het vorige paneel, en hergebruiken gaf een COM-fout (0x800F1000).
        private readonly Func<UIElement>? _sterren;

        public Modelkaart(string naam, Modelinfo? info, IReadOnlyList<string> taken, bool verborgen, bool opServer, TokenModel? tokens, Func<UIElement>? sterren = null)
        {
            _sterren = sterren;
            _naam = naam;
            _info = info;
            _taken = taken;
            _verborgen = verborgen;
            _opServer = opServer;
            _tokens = tokens;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            ToonLezen();
        }

        public event EventHandler? Bewaard;

        private void ToonLezen()
        {
            var inhoud = new StackPanel { Spacing = 10 };

            var pillen = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var taak in _taken)
            {
                pillen.Children.Add(Bouw.Pil(taak, "AccentBrush"));
            }

            if (_verborgen)
            {
                pillen.Children.Add(Bouw.Pil("Verborgen", "TextMutedBrush"));
            }

            if (!_opServer)
            {
                pillen.Children.Add(Bouw.Pil("Niet op de server", "TextMutedBrush"));
            }

            if (pillen.Children.Count > 0)
            {
                inhoud.Children.Add(pillen);
            }

            if (_info is null)
            {
                inhoud.Children.Add(Bouw.Melding("Nog geen beschrijving. Klik op Beschrijven om vast te leggen waarvoor dit model is."));
            }
            else
            {
                inhoud.Children.Add(Bouw.Tekst(_info.Doel, omloop: true));
                inhoud.Children.Add(Blok("Sterk in", _info.SterkIn));
                inhoud.Children.Add(Blok("Minder goed in", _info.MinderGoedIn));
                if (!string.IsNullOrWhiteSpace(_info.Notitie))
                {
                    inhoud.Children.Add(Blok("Notitie", _info.Notitie));
                }

                var velden = Bouw.Kolommen(3);
                Bouw.Zet(velden, Bouw.Veld("Grootte", _info.Grootte), 0, 0);
                Bouw.Zet(velden, Bouw.Veld("Maker", _info.Maker), 0, 1);
                Bouw.Zet(velden, Bouw.Veld("Tokens", _tokens is null ? "—" : $"{Bouw.Getal(_tokens.TotalTokens)} · {Bouw.Getal(_tokens.Calls)}×"), 0, 2);
                inhoud.Children.Add(velden);
            }

            if (_sterren is not null)
            {
                inhoud.Children.Add(_sterren());
            }

            inhoud.Children.Add(Bouw.Knop(_info is null ? "Beschrijven" : "Bewerken", (_, _) => ToonBewerken(), "BorderBrush"));
            Content = Bouw.Groep(_naam, _info?.Soort ?? "", inhoud, "AccentBrushDark");
        }

        private void ToonBewerken()
        {
            var soort = Veld("Soort (chat, code, beeld lezen, embedding, afbeelding maken…)", _info?.Soort);
            var maker = Veld("Maker", _info?.Maker);
            var grootte = Veld("Grootte", _info?.Grootte);
            var doel = Veld("Waarvoor", _info?.Doel, meerRegels: true);
            var sterk = Veld("Sterk in", _info?.SterkIn, meerRegels: true);
            var minder = Veld("Minder goed in", _info?.MinderGoedIn, meerRegels: true);
            var notitie = Veld("Eigen notitie", _info?.Notitie, meerRegels: true);
            var melding = Bouw.Melding("");

            var knoppen = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            knoppen.Children.Add(Bouw.Knop("Opslaan", async (_, _) =>
            {
                var nieuw = new Modelinfo(
                    _naam,
                    Tekst(soort, "Onbekend"),
                    Tekst(maker, "—"),
                    Tekst(grootte, "—"),
                    Tekst(doel, ""),
                    Tekst(sterk, ""),
                    Tekst(minder, ""),
                    string.IsNullOrWhiteSpace(notitie.Text) ? null : notitie.Text.Trim());

                var fout = await BrugDatabase.BewaarModelAsync(nieuw);
                if (fout is null)
                {
                    App.AppLog.Schrijf("brug", $"Modelbeschrijving bewaard: {_naam}");
                    Bewaard?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    melding.Text = $"Niet bewaard: {fout}";
                }
            }));
            knoppen.Children.Add(Bouw.Knop("Annuleren", (_, _) => ToonLezen(), "TextMutedBrush"));

            var inhoud = new StackPanel { Spacing = 10, Children = { soort, maker, grootte, doel, sterk, minder, notitie, knoppen, melding } };
            Content = Bouw.Groep(_naam, "Bewerken", inhoud, "AccentBrushDark");
        }

        private static StackPanel Blok(string label, string tekst) => new()
        {
            Spacing = 2,
            Children = { Bouw.Tekst(label.ToUpper(Bouw.Nl), "LabelStijl"), Bouw.Tekst(tekst, omloop: true) },
        };

        private static TextBox Veld(string kop, string? waarde, bool meerRegels = false) => new()
        {
            Header = kop,
            Text = waarde ?? "",
            AcceptsReturn = meerRegels,
            TextWrapping = meerRegels ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = meerRegels ? 64 : 0,
        };

        private static string Tekst(TextBox veld, string standaard) =>
            string.IsNullOrWhiteSpace(veld.Text) ? standaard : veld.Text.Trim();
    }
}
