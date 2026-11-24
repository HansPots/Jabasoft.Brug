using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Tokens: het tokenverbruik zoals de broker het vastlegt.
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

    private string? _gekozenWeek;

    protected override async Task LaadAsync()
    {
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

        // Gegevens voor rechts: van de gekozen week, of het totaal.
        List<TokenModel> perModel;
        List<TokenOnderdeel> perOnderdeel;
        string? foutModel, foutOnderdeel, bereikModel, bereikOnderdeel;
        List<TokenRegel>? weekRegels = null;

        if (gekozen is not null)
        {
            var regels = await BrokerApi.TokenRegelsAsync(gekozen.Week);
            weekRegels = regels.Waarde ?? [];
            perModel = [.. weekRegels
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Model) ? "" : r.Model)
                .Select(g => new TokenModel(g.Key, g.Sum(r => r.TotalTokens), g.Count()))];
            perOnderdeel = [.. weekRegels
                .GroupBy(r => (r.Application, Onderdeel: string.IsNullOrWhiteSpace(r.Onderdeel) ? "—" : r.Onderdeel))
                .Select(g => new TokenOnderdeel(g.Key.Application, g.Key.Onderdeel, g.Sum(r => r.TotalTokens), g.Count()))];
            foutModel = foutOnderdeel = regels.Fout;
            bereikModel = bereikOnderdeel = $"week {WeekNummer(gekozen.Week)}";
        }
        else
        {
            var modellen = await BrokerApi.TokenModellenAsync();
            var onderdelen = await BrokerApi.TokenOnderdelenAsync(DagenOnderdelen);
            perModel = modellen.Waarde ?? [];
            perOnderdeel = onderdelen.Waarde ?? [];
            foutModel = modellen.Fout;
            foutOnderdeel = onderdelen.Fout;
            bereikModel = "alle weken";
            bereikOnderdeel = $"laatste {DagenOnderdelen} dagen";
        }

        // 1. Kengetallen: het totaal, en de gekozen week (of deze week).
        var week = gekozen ?? lijst[0];
        Lijf.Children.Add(Bouw.Band(
            ("Totaal", Bouw.Getal(lijst.Sum(w => w.TotalTokens))),
            ($"Week {WeekNummer(week.Week)}", Bouw.Getal(week.TotalTokens)),
            ("Aanroepen", Bouw.Getal(week.Calls)),
            ("Per aanroep", week.Calls == 0 ? "—" : Bouw.Getal(week.TotalTokens / week.Calls))));

        // 2. Weken links; per model en per onderdeel rechts
        var midden = Bouw.Kolommen(2);
        Bouw.Zet(midden, WekenPaneel(lijst), 0, 0);

        var rechts = new StackPanel { Spacing = 24 };
        rechts.Children.Add(ModellenPaneel(perModel, foutModel, bereikModel));
        rechts.Children.Add(OnderdelenPaneel(perOnderdeel, foutOnderdeel, bereikOnderdeel));
        Bouw.Zet(midden, rechts, 0, 1);
        Lijf.Children.Add(midden);

        // 3. De losse aanroepen, alleen als er een week gekozen is.
        if (gekozen is not null && weekRegels is not null)
        {
            Lijf.Children.Add(RegelPaneel(gekozen, weekRegels));
        }
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
