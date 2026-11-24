using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Logs: alles wat de broker (of de Brug zelf) gemeld heeft sinds
/// de Brug openging, met zoeken en een filter op fouten. Nieuwe regels komen
/// er live bij, bovenaan.
///
/// De broker houdt zijn log alleen in het geheugen; de Brug bewaart de
/// laatste 5.000 regels (App.BrokerLog). Een echt archief met filters op
/// client, model en duur volgt met de live stroom (fase 4).
/// </summary>
public sealed class Logs : BrokerPagina
{
    private const int MaxZichtbaar = 1000;

    private readonly TextBox _zoek = new() { PlaceholderText = "Zoeken in de log…", Width = 360 };
    private readonly ComboBox _bron = new() { ItemsSource = new[] { "Alles", "Broker", "Brug" }, SelectedIndex = 0, Width = 160 };
    private readonly ToggleSwitch _fouten = new() { OffContent = "Alles", OnContent = "Alleen fouten" };
    private readonly TextBlock _teller = Bouw.Tekst("", "LabelStijl");
    private readonly StackPanel _regels = new() { Spacing = 0 };
    private Activiteitenlog[] _gevolgd = [];

    public Logs()
    {
        _zoek.TextChanged += (_, _) => Toon();
        _bron.SelectionChanged += (_, _) => Volg();
        _fouten.Toggled += (_, _) => Toon();
        Unloaded += (_, _) => Ontkoppel();
        Loaded += (_, _) => Volg();

        // Eigen opbouw in plaats van het scrollende vlak van BrokerPagina:
        // de zoekbalk en het kader blijven staan, alleen de regels in het
        // kader LOG scrollen.
        var balk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        balk.Children.Add(_zoek);
        balk.Children.Add(_bron);
        balk.Children.Add(_fouten);
        _teller.VerticalAlignment = VerticalAlignment.Center;
        balk.Children.Add(_teller);

        var scroller = new ScrollViewer
        {
            Content = _regels,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var paneel = Bouw.Paneel("Log", scroller);
        paneel.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(paneel, 1);

        var scherm = new Grid { Margin = new Thickness(32, 24, 32, 32), RowSpacing = 24 };
        scherm.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        scherm.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        scherm.Children.Add(balk);
        scherm.Children.Add(paneel);
        Content = scherm;
    }

    /// <summary>De gekozen log(s): Alles = broker en Brug samen.</summary>
    private Activiteitenlog[] Logboeken => _bron.SelectedIndex switch
    {
        1 => [App.BrokerLog],
        2 => [App.AppLog],
        _ => [App.BrokerLog, App.AppLog],
    };

    /// <summary>Alle regels van de gekozen log(s), op tijd, oudste eerst.</summary>
    private List<Activiteit> AlleRegels() =>
        [.. Logboeken.SelectMany(l => l.Alles()).OrderBy(r => r.Tijd)];

    protected override Task LaadAsync()
    {
        Toon();
        return Task.CompletedTask;
    }

    private void Volg()
    {
        Ontkoppel();
        _gevolgd = Logboeken;
        foreach (var log in _gevolgd)
        {
            log.Toegevoegd += Nieuw;
        }

        Toon();
    }

    private void Ontkoppel()
    {
        foreach (var log in _gevolgd)
        {
            log.Toegevoegd -= Nieuw;
        }

        _gevolgd = [];
    }

    private void Nieuw(object? sender, Activiteit regel) => DispatcherQueue.TryEnqueue(() =>
    {
        if (Past(regel))
        {
            _regels.Children.Insert(0, Rij(regel));
            while (_regels.Children.Count > MaxZichtbaar)
            {
                _regels.Children.RemoveAt(_regels.Children.Count - 1);
            }

            ZetTeller();
        }
    });

    private void Toon()
    {
        _regels.Children.Clear();
        var alles = AlleRegels();
        var passend = alles.Where(Past).Reverse().Take(MaxZichtbaar).ToList();
        foreach (var regel in passend)
        {
            _regels.Children.Add(Rij(regel));
        }

        if (passend.Count == 0)
        {
            _regels.Children.Add(Bouw.Melding(alles.Count == 0 ? "Nog niets gelogd sinds de Brug openging." : "Geen regels die passen bij het filter."));
        }

        ZetTeller();
    }

    private void ZetTeller() =>
        _teller.Text = $"{Bouw.Getal(_regels.Children.OfType<Grid>().Count())} van {Bouw.Getal(Logboeken.Sum(l => l.Alles().Count))} regels";

    private bool Past(Activiteit regel)
    {
        if (_fouten.IsOn && !IsFout(regel.Bericht))
        {
            return false;
        }

        var zoek = _zoek.Text.Trim();
        return zoek.Length == 0
            || regel.Bericht.Contains(zoek, StringComparison.OrdinalIgnoreCase)
            || regel.Bron.Contains(zoek, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Een regel telt als fout bij een HTTP-status van 400 of hoger, of als er "fout", "mislukt" of "niet bereikbaar" in staat.</summary>
    private static bool IsFout(string bericht) =>
        System.Text.RegularExpressions.Regex.IsMatch(bericht, @"-> [45]\d\d\b")
        || bericht.Contains("fout", StringComparison.OrdinalIgnoreCase)
        || bericht.Contains("mislukt", StringComparison.OrdinalIgnoreCase)
        || bericht.Contains("niet bereikbaar", StringComparison.OrdinalIgnoreCase)
        || bericht.Contains("error", StringComparison.OrdinalIgnoreCase)
        || bericht.Contains("exception", StringComparison.OrdinalIgnoreCase);

    private static Grid Rij(Activiteit regel)
    {
        var rij = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 2, 0, 2) };
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        rij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var tijd = Bouw.Tekst(regel.TijdTekst, "LabelStijl");
        var bron = Bouw.Tekst(regel.Bron, "LabelStijl");
        var tekst = Bouw.Tekst(regel.Bericht, omloop: true);
        tekst.IsTextSelectionEnabled = true; // selecteren en kopiëren (Ctrl+C)
        if (IsFout(regel.Bericht))
        {
            // Themakleur (roze), geen los rood: statuskleur hoort alleen in bolletjes.
            tekst.Foreground = Bouw.Kleur("TextMutedBrush");
        }

        Grid.SetColumn(bron, 1);
        Grid.SetColumn(tekst, 2);
        rij.Children.Add(tijd);
        rij.Children.Add(bron);
        rij.Children.Add(tekst);
        return rij;
    }
}
