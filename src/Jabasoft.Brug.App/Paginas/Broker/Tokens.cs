using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Tokens: het tokenverbruik zoals de broker het vastlegt.
/// Bovenaan de kengetallen, daaronder per week (klik een week voor de losse
/// aanroepen), per model en per onderdeel. Zelfde gegevens als het oude
/// Tokens-scherm van Jabasoft.App, uit dezelfde /api/usage-endpoints.
/// </summary>
public sealed class Tokens : BrokerPagina
{
    private const int DagenOnderdelen = 30;
    private const int MaxRegels = 250;

    private string? _gekozenWeek;
    private readonly StackPanel _regels = new() { Spacing = 0 };
    private Controls.Paneel? _regelPaneel;

    protected override async Task LaadAsync()
    {
        var weken = await BrokerApi.TokenWekenAsync();
        var modellen = await BrokerApi.TokenModellenAsync();
        var onderdelen = await BrokerApi.TokenOnderdelenAsync(DagenOnderdelen);

        Lijf.Children.Clear();
        if (!weken.Gelukt || weken.Waarde is null)
        {
            Lijf.Children.Add(Bouw.Melding($"Geen tokengegevens: {weken.Fout}. Start de broker en ververs dit scherm."));
            return;
        }

        var lijst = weken.Waarde;
        if (lijst.Count == 0)
        {
            Lijf.Children.Add(Bouw.Melding("Nog geen AI-aanroepen vastgelegd. Zodra een applicatie de broker gebruikt, verschijnt hier een week."));
            return;
        }

        // 1. Kengetallen
        var dezeWeek = lijst[0];
        var kengetallen = Bouw.Kolommen(4);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Totaal", Bouw.Getal(lijst.Sum(w => w.TotalTokens)), "AccentBrush"), 0, 0);
        Bouw.Zet(kengetallen, Bouw.Kengetal($"Week {WeekNummer(dezeWeek.Week)}", Bouw.Getal(dezeWeek.TotalTokens), "HeaderSideGradientBrush"), 0, 1);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Aanroepen deze week", Bouw.Getal(dezeWeek.Calls), "BorderBrush"), 0, 2);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Gemiddeld per aanroep", dezeWeek.Calls == 0 ? "—" : Bouw.Getal(dezeWeek.TotalTokens / dezeWeek.Calls), "TextMutedBrush"), 0, 3);
        Lijf.Children.Add(kengetallen);

        // 2. Per week links; per model en per onderdeel rechts
        var midden = Bouw.Kolommen(2);
        Bouw.Zet(midden, WekenPaneel(lijst), 0, 0);

        var rechts = new StackPanel { Spacing = 24 };
        rechts.Children.Add(ModellenPaneel(modellen));
        rechts.Children.Add(OnderdelenPaneel(onderdelen));
        Bouw.Zet(midden, rechts, 0, 1);
        Lijf.Children.Add(midden);

        // 3. De losse aanroepen van de gekozen week
        _regelPaneel = Bouw.Paneel("Aanroepen", _regels);
        Lijf.Children.Add(_regelPaneel);

        _gekozenWeek ??= dezeWeek.Week;
        await ToonWeekAsync(lijst.FirstOrDefault(w => w.Week == _gekozenWeek) ?? dezeWeek);
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
                week.Week == _gekozenWeek ? "AccentBrush" : "BorderBrush");

            var knop = new Button
            {
                Content = regel,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = null,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 4, 0, 4),
            };
            ToolTipService.SetToolTip(knop, "Toon de aanroepen van deze week");
            knop.Click += async (_, _) =>
            {
                _gekozenWeek = week.Week;
                await VerversAsync();
            };
            lijst.Children.Add(knop);
        }

        return Bouw.Paneel("Per week", lijst, $"{weken.Count} weken");
    }

    private static Controls.Paneel ModellenPaneel(Meting<List<TokenModel>> modellen)
    {
        var lijst = new StackPanel { Spacing = 10 };
        var waarden = modellen.Waarde ?? [];
        if (waarden.Count == 0)
        {
            lijst.Children.Add(Bouw.Melding(modellen.Fout ?? "Nog geen verbruik per model."));
        }

        var grootste = Math.Max(1, waarden.Count == 0 ? 1 : waarden.Max(m => m.TotalTokens));
        foreach (var model in waarden.OrderByDescending(m => m.TotalTokens).Take(10))
        {
            lijst.Children.Add(Bouw.Balkregel(
                string.IsNullOrWhiteSpace(model.Model) ? "(onbekend)" : model.Model,
                $"{Bouw.Getal(model.TotalTokens)} · {Bouw.Getal(model.Calls)}×",
                (double)model.TotalTokens / grootste));
        }

        return Bouw.Paneel("Per model", lijst, "alle weken");
    }

    private static Controls.Paneel OnderdelenPaneel(Meting<List<TokenOnderdeel>> onderdelen)
    {
        var lijst = new StackPanel { Spacing = 10 };
        var waarden = onderdelen.Waarde ?? [];
        if (waarden.Count == 0)
        {
            lijst.Children.Add(Bouw.Melding(onderdelen.Fout ?? "Nog geen verbruik per onderdeel."));
        }

        var totaal = Math.Max(1, waarden.Sum(o => o.TotalTokens));
        foreach (var onderdeel in waarden.OrderByDescending(o => o.TotalTokens).Take(10))
        {
            lijst.Children.Add(Bouw.Balkregel(
                $"{onderdeel.Application} › {onderdeel.Onderdeel}",
                $"{Bouw.Getal(onderdeel.TotalTokens)} · {Bouw.Getal(100.0 * onderdeel.TotalTokens / totaal, 0)}%",
                (double)onderdeel.TotalTokens / totaal,
                "HeaderSideGradientBrush"));
        }

        return Bouw.Paneel("Per onderdeel", lijst, $"laatste {DagenOnderdelen} dagen");
    }

    private async Task ToonWeekAsync(TokenWeek week)
    {
        if (_regelPaneel is null)
        {
            return;
        }

        _regelPaneel.Titel = $"AANROEPEN · WEEK {WeekNummer(week.Week)}";
        _regels.Children.Clear();
        _regels.Children.Add(Bouw.Melding("Ophalen…"));

        var regels = await BrokerApi.TokenRegelsAsync(week.Week);
        _regels.Children.Clear();
        if (regels.Waarde is not { Count: > 0 } lijst)
        {
            _regels.Children.Add(Bouw.Melding(regels.Fout ?? "Geen regels gevonden voor deze week."));
            return;
        }

        _regelPaneel.Rechts = lijst.Count > MaxRegels ? $"NIEUWSTE {MaxRegels} VAN {Bouw.Getal(lijst.Count)}" : $"{Bouw.Getal(lijst.Count)} AANROEPEN";
        _regels.Children.Add(Rij(true, "Tijd", "App", "Onderdeel", "Model", "Prompt", "Antwoord", "Totaal"));
        foreach (var regel in lijst.OrderByDescending(r => r.Timestamp).Take(MaxRegels))
        {
            _regels.Children.Add(Rij(false,
                regel.Timestamp.ToLocalTime().ToString("ddd d MMM HH:mm", Bouw.Nl),
                regel.Application,
                regel.Onderdeel ?? "—",
                regel.Model ?? "—",
                Bouw.Getal(regel.PromptTokens),
                Bouw.Getal(regel.CompletionTokens),
                Bouw.Getal(regel.TotalTokens)));
        }
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
