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
        ("chatModel", "Daily chat"),
        ("codeModel", "Code"),
        ("controleModel", "Controle"),
        ("beeldModel", "Beeld"),
        ("embedModel", "Zoeken"),
        ("gooseModel", "Goose"),
        ("hermesModel", "Hermes"),
        ("hermesLokaalModel", "Hermes lokaal"),
    ];

    protected override async Task LaadAsync()
    {
        var beschrijvingen = await BrugDatabase.ModellenAsync();
        var server = await BrokerApi.ServerModellenAsync();
        var instellingen = await BrokerApi.InstellingenAsync();
        var verbruik = await BrokerApi.TokenModellenAsync();

        var serverActief = Lees(instellingen.Waarde);
        var opServer = server.Waarde?.Models ?? [];
        var verborgen = (serverActief?["verborgen"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToHashSet() ?? [];
        var tokens = (verbruik.Waarde ?? []).ToDictionary(m => m.Model ?? "", StringComparer.OrdinalIgnoreCase);

        var taken = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (veld, taak) in Taken)
        {
            if (serverActief?[veld]?.GetValue<string>() is { Length: > 0 } model)
            {
                (taken.TryGetValue(model, out var lijst) ? lijst : taken[model] = []).Add(taak);
            }
        }

        // Alle namen: wat op de server staat, plus wat we beschreven hebben.
        var info = beschrijvingen.ToDictionary(m => m.Naam, StringComparer.OrdinalIgnoreCase);
        var namen = opServer.Concat(beschrijvingen.Select(m => m.Naam)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Lijf.Children.Clear();

        if (!server.Gelukt)
        {
            Lijf.Children.Add(Bouw.Melding($"De modellijst van de AI-server is niet op te halen ({server.Fout}). Hieronder de beschreven modellen."));
        }

        if (!BrugDatabase.Beschikbaar)
        {
            Lijf.Children.Add(Bouw.Melding($"Database {BrugDatabase.Naam} niet bereikbaar ({BrugDatabase.Fout}): de ingebouwde beschrijvingen worden getoond en aanpassen kan nu niet."));
        }

        var kengetallen = Bouw.Kolommen(4);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Op de server", server.Gelukt ? opServer.Count.ToString(Bouw.Nl) : "—", "AccentBrush"), 0, 0);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Met een taak", taken.Count.ToString(Bouw.Nl), "HeaderSideGradientBrush"), 0, 1);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Verborgen", verborgen.Count.ToString(Bouw.Nl), "TextMutedBrush"), 0, 2);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Beschreven", namen.Count(info.ContainsKey).ToString(Bouw.Nl), "BorderBrush"), 0, 3);
        Lijf.Children.Add(kengetallen);

        var groepen = new (string Kop, Func<string, bool> Hoort)[]
        {
            ("In gebruik", n => taken.ContainsKey(n)),
            ("Beschikbaar", n => !taken.ContainsKey(n) && !verborgen.Contains(n) && (!server.Gelukt || opServer.Contains(n))),
            ("Verborgen", n => !taken.ContainsKey(n) && verborgen.Contains(n)),
            ("Niet op de server", n => !taken.ContainsKey(n) && !verborgen.Contains(n) && server.Gelukt && !opServer.Contains(n)),
        };

        foreach (var (kop, hoort) in groepen)
        {
            var leden = namen.Where(hoort).OrderBy(n => info.TryGetValue(n, out var i) ? i.Soort : "~").ThenBy(n => n).ToList();
            if (leden.Count == 0)
            {
                continue;
            }

            Lijf.Children.Add(Bouw.Kop($"{kop.ToUpper(Bouw.Nl)} · {leden.Count}"));
            var raster = Bouw.Kolommen(2);
            for (var i = 0; i < leden.Count; i++)
            {
                var naam = leden[i];
                var kaart = new Modelkaart(
                    naam,
                    info.GetValueOrDefault(naam),
                    taken.GetValueOrDefault(naam) ?? [],
                    verborgen.Contains(naam),
                    !server.Gelukt || opServer.Contains(naam),
                    tokens.GetValueOrDefault(naam));
                kaart.Bewaard += async (_, _) => await VerversAsync();
                Bouw.Zet(raster, kaart, i / 2, i % 2);
            }

            Lijf.Children.Add(raster);
        }
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
            Content = Bouw.Paneel(_naam, inhoud, _info?.Soort ?? "");
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
            Content = Bouw.Paneel(_naam, inhoud, "Bewerken");
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
