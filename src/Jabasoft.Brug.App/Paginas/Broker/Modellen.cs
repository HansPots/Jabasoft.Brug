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
    /// <summary>De taakvelden in de broker-instellingen, met hoe ze op het scherm heten.</summary>
    private static readonly (string Veld, string Taak)[] Taken =
    [
        ("chatModel", "Chat"),
        ("codeModel", "Code"),
        ("controleModel", "Ctrl"),
        ("beeldModel", "Beeld"),
        ("embedModel", "Zoek"),
        ("gooseModel", "Goose"),
        ("hermesModel", "Hermes"),
        ("hermesLokaalModel", "Hermes lok."),
    ];

    /// <summary>Vaste kolombreedtes, zodat taak, soort en tokens in elke regel en elke groep onder elkaar staan.</summary>
    private static readonly GridLength KolomTaken = new(230);
    private static readonly GridLength KolomSoort = new(140);
    private static readonly GridLength KolomTokens = new(90);

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
        var beschrijvingenTaak = BrugDatabase.ModellenAsync();
        var serverTaak = BrokerApi.ServerModellenAsync();
        var instellingenTaak = BrokerApi.InstellingenAsync();
        var verbruikTaak = BrokerApi.TokenModellenAsync();
        await Task.WhenAll(beschrijvingenTaak, serverTaak, instellingenTaak, verbruikTaak);

        var beschrijvingen = beschrijvingenTaak.Result;
        var server = serverTaak.Result;
        var serverActief = Lees(instellingenTaak.Result.Waarde);

        _serverGelukt = server.Gelukt;
        _serverFout = server.Fout;
        _opServer = server.Waarde?.Models ?? [];
        _verborgen = (serverActief?["verborgen"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        _tokens = (verbruikTaak.Result.Waarde ?? []).GroupBy(m => m.Model ?? "").ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _info = beschrijvingen.ToDictionary(m => m.Naam, StringComparer.OrdinalIgnoreCase);
        _namen = [.. _opServer.Concat(beschrijvingen.Select(m => m.Naam)).Distinct(StringComparer.OrdinalIgnoreCase)];

        _taken = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (veld, taak) in Taken)
        {
            if (serverActief?[veld]?.GetValue<string>() is { Length: > 0 } model)
            {
                (_taken.TryGetValue(model, out var lijst) ? lijst : _taken[model] = []).Add(taak);
            }
        }

        Teken();
    }

    /// <summary>Links de lijst per groep, rechts het detail van het gekozen model.</summary>
    private void Teken()
    {
        Lijf.Children.Clear();
        Lijf.Children.Add(Bouw.Band(
            ("Op de server", _serverGelukt ? _opServer.Count.ToString(Bouw.Nl) : "—"),
            ("Met een taak", _taken.Count.ToString(Bouw.Nl)),
            ("Verborgen", _verborgen.Count.ToString(Bouw.Nl)),
            ("Beschreven", _namen.Count(_info.ContainsKey).ToString(Bouw.Nl))));

        if (!_serverGelukt)
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
        if (_gekozen is null || !_namen.Contains(_gekozen, StringComparer.OrdinalIgnoreCase))
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
                _tokens.GetValueOrDefault(gekozen))
            {
                VerticalAlignment = VerticalAlignment.Top,
            };
            detail.Bewaard += async (_, _) => await VerversAsync();
            Grid.SetColumn(detail, 1);
            raster.Children.Add(detail);
        }

        Lijf.Children.Add(raster);
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

        var soort = Bouw.Tekst(_info.TryGetValue(naam, out var info) ? info.Soort.ToLower(Bouw.Nl) : "—", "LabelStijl");
        soort.Foreground = bijKleur;
        soort.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(soort, 2);
        rij.Children.Add(soort);

        var tokens = Bouw.Tekst(_tokens.TryGetValue(naam, out var t) ? Bouw.Kort(t.TotalTokens) : "—", "LabelStijl");
        tokens.Foreground = bijKleur;
        tokens.HorizontalAlignment = HorizontalAlignment.Right;
        tokens.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tokens, 3);
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

        public Modelkaart(string naam, Modelinfo? info, IReadOnlyList<string> taken, bool verborgen, bool opServer, TokenModel? tokens)
        {
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
