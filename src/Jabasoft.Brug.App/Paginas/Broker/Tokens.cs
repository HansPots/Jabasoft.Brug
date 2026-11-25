using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Tokens: het tokenverbruik zoals de broker het vastlegt.
///
/// Bovenaan de grafiek: tokens per dag per model, over vier weken of over
/// de dagen van de gekozen week.
///
/// Links de weken. Zonder gekozen week toont rechts het totaaloverzicht
/// (per model over alle weken, per onderdeel over de laatste 30 dagen).
/// Klik je een week aan, dan toont rechts het overzicht van die week en
/// komen onderaan de losse aanroepen; klik je dezelfde week nog een keer,
/// dan ga je terug naar het totaal.
/// </summary>
public sealed class Tokens : BrokerPagina
{
    private const int DagenOnderdelen = 30;
    private const int MaxRegels = 250;

    /// <summary>Hoeveel dagen de grafiek zonder gekozen week laat zien (zoals het oude scherm: vier weken).</summary>
    private const int GrafiekDagen = 28;

    /// <summary>
    /// Losse aanroepen per afgelopen week: die veranderen niet meer, dus één
    /// keer ophalen is genoeg. De lopende week wordt altijd opnieuw gevraagd.
    /// </summary>
    private readonly Dictionary<string, List<TokenRegel>> _weekCache = [];

    private string? _gekozenWeek;

    private async Task<List<TokenRegel>> RegelsAsync(TokenWeek week, TokenWeek lopendeWeek)
    {
        if (week.Week != lopendeWeek.Week && _weekCache.TryGetValue(week.Week, out var bewaard))
        {
            return bewaard;
        }

        var regels = await BrokerApi.TokenRegelsAsync(week.Week);
        if (regels.Waarde is not { } lijst)
        {
            return [];
        }

        if (week.Week != lopendeWeek.Week)
        {
            _weekCache[week.Week] = lijst;
        }

        return lijst;
    }

    protected override async Task LaadAsync()
    {
        // Bij de eerste keer meteen iets laten zien in plaats van een leeg scherm.
        if (Lijf.Children.Count == 0)
        {
            Lijf.Children.Add(Bouw.Meldingpaneel("Tokens", "Ophalen…"));
        }

        var weken = await BrokerApi.TokenWekenAsync();

        Lijf.Children.Clear();
        if (!weken.Gelukt || weken.Waarde is null)
        {
            Lijf.Children.Add(Bouw.Meldingpaneel("Geen tokengegevens", $"{weken.Fout}. Start de broker en ververs dit scherm."));
            return;
        }

        var lijst = weken.Waarde;
        if (lijst.Count == 0)
        {
            Lijf.Children.Add(Bouw.Meldingpaneel("Tokens", "Nog geen AI-aanroepen vastgelegd. Zodra een applicatie de broker gebruikt, verschijnt hier een week."));
            return;
        }

        var gekozen = lijst.FirstOrDefault(w => w.Week == _gekozenWeek);
        if (gekozen is null)
        {
            _gekozenWeek = null;
        }

        // Eerst de opbouw: weken staan er meteen; grafiek, per model, per
        // onderdeel en de aanroepen krijgen een plek met "Ophalen…" en vullen
        // zich elk zodra hun eigen gegevens binnen zijn.
        var grafiekPlek = Plek("Verloop");
        var modelPlek = Plek("Per model");
        var onderdeelPlek = Plek("Per onderdeel");

        Lijf.Children.Add(grafiekPlek);
        var midden = Bouw.Kolommen(2);
        Bouw.Zet(midden, WekenPaneel(lijst), 0, 0);
        Bouw.Zet(midden, new StackPanel { Spacing = 24, Children = { modelPlek, onderdeelPlek } }, 0, 1);
        Lijf.Children.Add(midden);

        if (gekozen is not null)
        {
            var regelPlek = Plek($"Aanroepen · week {WeekNummer(gekozen.Week)}");
            Lijf.Children.Add(regelPlek);

            // Eén vraag levert alles voor deze week.
            var weekRegels = await RegelsAsync(gekozen, lijst[0]);
            var bereik = $"week {WeekNummer(gekozen.Week)}";
            grafiekPlek.Content = GrafiekPaneel(weekRegels, gekozen.Start.Date, gekozen.End.Date,
                $"Verloop per dag · week {WeekNummer(gekozen.Week)}",
                $"{Bouw.Getal(gekozen.TotalTokens)} tokens · {Bouw.Getal(gekozen.Calls)} aanroepen");
            modelPlek.Content = ModellenPaneel([.. weekRegels
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Model) ? "" : r.Model)
                .Select(g => new TokenModel(g.Key, g.Sum(r => r.TotalTokens), g.Count()))], null, bereik);
            onderdeelPlek.Content = OnderdelenPaneel([.. weekRegels
                .GroupBy(r => (r.Application, Onderdeel: string.IsNullOrWhiteSpace(r.Onderdeel) ? "—" : r.Onderdeel))
                .Select(g => new TokenOnderdeel(g.Key.Application, g.Key.Onderdeel, g.Sum(r => r.TotalTokens), g.Count()))], null, bereik);
            regelPlek.Content = RegelPaneel(gekozen, weekRegels);
            return;
        }

        // Totaaloverzicht: drie losse bronnen, tegelijk opgehaald, elk vult zijn eigen plek.
        var tot = DateTime.Today;
        var van = tot.AddDays(1 - GrafiekDagen);
        var totaal = Bouw.Getal(lijst.Sum(w => w.TotalTokens));

        async Task VulGrafiekAsync()
        {
            var regels = await Task.WhenAll(lijst.Where(w => w.End.Date >= van).Select(w => RegelsAsync(w, lijst[0])));
            grafiekPlek.Content = GrafiekPaneel([.. regels.SelectMany(r => r)], van, tot, "Verloop per model",
                string.Create(Bouw.Nl, $"{van:d MMM} – {tot:d MMM} · totaal {totaal}"));
        }

        async Task VulModellenAsync()
        {
            var modellen = await BrokerApi.TokenModellenAsync();
            modelPlek.Content = ModellenPaneel(modellen.Waarde ?? [], modellen.Fout, "alle weken");
        }

        async Task VulOnderdelenAsync()
        {
            var onderdelen = await BrokerApi.TokenOnderdelenAsync(DagenOnderdelen);
            onderdeelPlek.Content = OnderdelenPaneel(onderdelen.Waarde ?? [], onderdelen.Fout, $"laatste {DagenOnderdelen} dagen");
        }

        await Task.WhenAll(VulGrafiekAsync(), VulModellenAsync(), VulOnderdelenAsync());
    }

    /// <summary>Een plek op het scherm die eerst "Ophalen…" toont en later zijn echte paneel krijgt.</summary>
    private static ContentControl Plek(string titel) => new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Content = Bouw.Meldingpaneel(titel, "Ophalen…"),
    };

    /// <summary>De grafiek: tokens per dag per model over de dagen van tot en met tot.</summary>
    private static Controls.Paneel GrafiekPaneel(List<TokenRegel> regels, DateTime van, DateTime tot, string titel, string rechts)
    {
        var perModelPerDag = regels
            .Where(r => r.Timestamp.ToLocalTime().Date >= van && r.Timestamp.ToLocalTime().Date <= tot)
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Model) ? "" : r.Model)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.Timestamp.ToLocalTime().Date).ToDictionary(d => d.Key, d => d.Sum(r => r.TotalTokens)));

        // Elke keer een nieuwe grafiek: een element kan maar in één paneel tegelijk staan.
        var grafiek = new Controls.Tokengrafiek();
        grafiek.Toon(perModelPerDag, van, tot);
        return Bouw.Paneel(titel, grafiek, rechts);
    }

    private Controls.Paneel WekenPaneel(List<TokenWeek> weken)
    {
        var grootste = Math.Max(1, weken.Max(w => w.TotalTokens));
        var lijst = new StackPanel { Spacing = 6 };
        foreach (var week in weken)
        {
            var regel = Bouw.Balkregel(
                string.Create(Bouw.Nl, $"Week {WeekNummer(week.Week)}  ·  {week.Start:d MMM} – {week.End:d MMM yyyy}"),
                $"{Bouw.Getal(week.TotalTokens)} tokens · {Bouw.Getal(week.Calls)} aanroepen",
                (double)week.TotalTokens / grootste,
                // Alleen de gekozen week donkergeel, de rest lila.
                week.Week == _gekozenWeek ? "AccentBrushDark" : "BorderBrush");

            var knop = new Button
            {
                Content = regel,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = null,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 4, 0, 4),
            };
            ToolTipService.SetToolTip(knop, week.Week == _gekozenWeek
                ? "Terug naar het totaaloverzicht"
                : "Toon het overzicht van deze week");
            knop.Click += async (_, _) =>
            {
                // Nog een keer op dezelfde week = terug naar het totaal.
                _gekozenWeek = _gekozenWeek == week.Week ? null : week.Week;
                await VerversAsync();
            };
            lijst.Children.Add(knop);
        }

        return Bouw.Paneel("Per week", lijst, _gekozenWeek is null ? $"{weken.Count} weken" : $"week {WeekNummer(_gekozenWeek)} gekozen");
    }

    private static Controls.Paneel ModellenPaneel(List<TokenModel> waarden, string? fout, string bereik)
    {
        var lijst = new StackPanel { Spacing = 10 };
        if (waarden.Count == 0)
        {
            lijst.Children.Add(Bouw.Melding(fout ?? "Geen verbruik per model."));
        }

        var grootste = Math.Max(1, waarden.Count == 0 ? 1 : waarden.Max(m => m.TotalTokens));
        foreach (var model in waarden.OrderByDescending(m => m.TotalTokens).Take(10))
        {
            lijst.Children.Add(Bouw.Balkregel(
                string.IsNullOrWhiteSpace(model.Model) ? "(onbekend)" : model.Model,
                $"{Bouw.Getal(model.TotalTokens)} · {Bouw.Getal(model.Calls)}×",
                (double)model.TotalTokens / grootste));
        }

        return Bouw.Paneel("Per model", lijst, bereik);
    }

    private static Controls.Paneel OnderdelenPaneel(List<TokenOnderdeel> waarden, string? fout, string bereik)
    {
        var lijst = new StackPanel { Spacing = 10 };
        if (waarden.Count == 0)
        {
            lijst.Children.Add(Bouw.Melding(fout ?? "Geen verbruik per onderdeel."));
        }

        var totaal = Math.Max(1, waarden.Sum(o => o.TotalTokens));
        foreach (var onderdeel in waarden.OrderByDescending(o => o.TotalTokens).Take(10))
        {
            lijst.Children.Add(Bouw.Balkregel(
                $"{onderdeel.Application} › {onderdeel.Onderdeel}",
                $"{Bouw.Getal(onderdeel.TotalTokens)} · {Bouw.Getal(100.0 * onderdeel.TotalTokens / totaal, 0)}%",
                (double)onderdeel.TotalTokens / totaal));
        }

        return Bouw.Paneel("Per onderdeel", lijst, bereik);
    }

    private static Controls.Paneel RegelPaneel(TokenWeek week, List<TokenRegel> lijst)
    {
        var regels = new StackPanel();
        if (lijst.Count == 0)
        {
            regels.Children.Add(Bouw.Melding("Geen regels gevonden voor deze week."));
            return Bouw.Paneel($"Aanroepen · week {WeekNummer(week.Week)}", regels);
        }

        regels.Children.Add(Rij(true, "Tijd", "App", "Onderdeel", "Model", "Prompt", "Antwoord", "Totaal"));
        foreach (var regel in lijst.OrderByDescending(r => r.Timestamp).Take(MaxRegels))
        {
            regels.Children.Add(Rij(false,
                regel.Timestamp.ToLocalTime().ToString("ddd d MMM HH:mm", Bouw.Nl),
                regel.Application,
                regel.Onderdeel ?? "—",
                regel.Model ?? "—",
                Bouw.Getal(regel.PromptTokens),
                Bouw.Getal(regel.CompletionTokens),
                Bouw.Getal(regel.TotalTokens)));
        }

        var rechts = lijst.Count > MaxRegels ? $"nieuwste {MaxRegels} van {Bouw.Getal(lijst.Count)}" : $"{Bouw.Getal(lijst.Count)} aanroepen";
        return Bouw.Paneel($"Aanroepen · week {WeekNummer(week.Week)}", regels, rechts);
    }

    private static readonly double[] Breedtes = [1.4, 1.2, 1.4, 2.4, 1, 1, 1];

    private static Grid Rij(bool kop, params string[] cellen)
    {
        var rij = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 3, 0, 3) };
        for (var i = 0; i < cellen.Length; i++)
        {
            rij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Breedtes[i], GridUnitType.Star) });
            var tekst = Bouw.Tekst(kop ? cellen[i].ToUpper(Bouw.Nl) : cellen[i], kop ? "LabelStijl" : "WaardeStijl");
            if (i >= 4)
            {
                tekst.HorizontalAlignment = HorizontalAlignment.Right;
            }

            Grid.SetColumn(tekst, i);
            rij.Children.Add(tekst);
        }

        return rij;
    }

    /// <summary>"2026-W48" → "48".</summary>
    private static string WeekNummer(string week) =>
        week.Contains("-W", StringComparison.Ordinal) ? week[(week.IndexOf("-W", StringComparison.Ordinal) + 2)..].TrimStart('0') : week;
}
