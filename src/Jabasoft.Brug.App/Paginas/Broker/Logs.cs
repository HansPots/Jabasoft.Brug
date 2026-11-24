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
    private readonly ComboBox _bron = new() { ItemsSource = new[] { "Broker", "Brug" }, SelectedIndex = 0, Width = 160 };
    private readonly ToggleSwitch _fouten = new() { OffContent = "Alles", OnContent = "Alleen fouten" };
    private readonly TextBlock _teller = Bouw.Tekst("", "LabelStijl");
    private readonly StackPanel _regels = new() { Spacing = 0 };
    private Activiteitenlog? _gevolgd;

    public Logs()
    {
        _zoek.TextChanged += (_, _) => Toon();
        _bron.SelectionChanged += (_, _) => Volg();
        _fouten.Toggled += (_, _) => Toon();
        Unloaded += (_, _) => Ontkoppel();
        Loaded += (_, _) => Volg();
    }

    private Activiteitenlog Log => _bron.SelectedIndex == 1 ? App.AppLog : App.BrokerLog;

    protected override Task LaadAsync()
    {
        if (Lijf.Children.Count == 0)
        {
            var balk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            balk.Children.Add(_zoek);
            balk.Children.Add(_bron);
            balk.Children.Add(_fouten);
            _teller.VerticalAlignment = VerticalAlignment.Center;
            balk.Children.Add(_teller);
            Lijf.Children.Add(balk);
            Lijf.Children.Add(Bouw.Paneel("Log", _regels));
        }

        Toon();
        return Task.CompletedTask;
    }

    private void Volg()
    {
        Ontkoppel();
        _gevolgd = Log;
        _gevolgd.Toegevoegd += Nieuw;
        Toon();
    }

    private void Ontkoppel()
    {
        if (_gevolgd is not null)
        {
            _gevolgd.Toegevoegd -= Nieuw;
            _gevolgd = null;
        }
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
        if (Lijf.Children.Count == 0)
        {
            return;
        }

        _regels.Children.Clear();
        var passend = Log.Alles().Where(Past).Reverse().Take(MaxZichtbaar).ToList();
        foreach (var regel in passend)
        {
            _regels.Children.Add(Rij(regel));
        }

        if (passend.Count == 0)
        {
            _regels.Children.Add(Bouw.Melding(Log.Alles().Count == 0 ? "Nog niets gelogd sinds de Brug openging." : "Geen regels die passen bij het filter."));
        }

        ZetTeller();
    }

    private void ZetTeller() =>
        _teller.Text = $"{Bouw.Getal(_regels.Children.OfType<Grid>().Count())} van {Bouw.Getal(Log.Alles().Count)} regels";

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
