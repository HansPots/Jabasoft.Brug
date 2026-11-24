using System.Collections.ObjectModel;
using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Controls;

/// <summary>Infoblok met meelopende activiteitenregels; zie Activiteitenblok.xaml.</summary>
public sealed partial class Activiteitenblok : UserControl
{
    private const int MaxRegels = 200;

    private readonly ObservableCollection<Activiteit> _regels = [];
    private readonly DispatcherQueueTimer _lampTimer;

    public Activiteitenblok()
    {
        InitializeComponent();
        Regels.ItemsSource = _regels;

        _lampTimer = DispatcherQueue.CreateTimer();
        _lampTimer.Interval = TimeSpan.FromMilliseconds(350);
        _lampTimer.IsRepeating = false;
        _lampTimer.Tick += (_, _) => Lampje.Opacity = 0.25;
    }

    public static readonly DependencyProperty TitelProperty =
        DependencyProperty.Register(nameof(Titel), typeof(string), typeof(Activiteitenblok), new PropertyMetadata(string.Empty));

    public string Titel
    {
        get => (string)GetValue(TitelProperty);
        set => SetValue(TitelProperty, value);
    }

    /// <summary>Toont wat er al in het log staat en volgt daarna elke nieuwe regel.</summary>
    public void Volg(Activiteitenlog log)
    {
        foreach (var regel in log.Alles())
        {
            _regels.Add(regel);
        }

        log.Toegevoegd += (_, regel) => DispatcherQueue.TryEnqueue(() => VoegToe(regel));
        ScrollNaarOnder();
    }

    private void VoegToe(Activiteit regel)
    {
        // Alleen meescrollen als je al onderaan stond; wie terugleest, laten we met rust.
        var onderaan = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 4;

        _regels.Add(regel);
        while (_regels.Count > MaxRegels)
        {
            _regels.RemoveAt(0);
        }

        Lampje.Opacity = 1;
        _lampTimer.Start();

        if (onderaan)
        {
            ScrollNaarOnder();
        }
    }

    private void ScrollNaarOnder() =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Scroller.ChangeView(null, Scroller.ScrollableHeight, null, true));
}
