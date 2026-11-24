using System.Collections.ObjectModel;
using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Jabasoft.Brug.App.Controls;

/// <summary>
/// Infoblok met meelopende activiteitenregels; zie Activiteitenblok.xaml.
///
/// Meescrollen: de laatste regel staat altijd in beeld, behalve terwijl je
/// zelf aan het scrollen bent. Zodra je even (<see cref="RustNaScrollen"/>)
/// niet meer scrolt, springt het blok weer naar de laatste regel.
/// </summary>
public sealed partial class Activiteitenblok : UserControl
{
    private const int MaxRegels = 200;
    private static readonly TimeSpan RustNaScrollen = TimeSpan.FromSeconds(5);

    private readonly ObservableCollection<Activiteit> _regels = [];
    private readonly DispatcherQueueTimer _lampTimer;
    private readonly DispatcherQueueTimer _terugTimer;
    private bool _gebruikerScrolt;

    public Activiteitenblok()
    {
        InitializeComponent();
        Regels.ItemsSource = _regels;

        _lampTimer = DispatcherQueue.CreateTimer();
        _lampTimer.Interval = TimeSpan.FromMilliseconds(350);
        _lampTimer.IsRepeating = false;
        _lampTimer.Tick += (_, _) => Lampje.Opacity = 0.25;

        _terugTimer = DispatcherQueue.CreateTimer();
        _terugTimer.Interval = RustNaScrollen;
        _terugTimer.IsRepeating = false;
        _terugTimer.Tick += (_, _) =>
        {
            _gebruikerScrolt = false;
            ScrollNaarOnder();
        };

        // Scrollen door de gebruiker herkennen aan muiswiel en klikken op de
        // schuifbalk. Met handledEventsToo, want de ScrollViewer handelt die
        // zelf al af en dan zouden we ze anders nooit zien.
        Scroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(GebruikerScrolt), true);
        Scroller.AddHandler(PointerPressedEvent, new PointerEventHandler(GebruikerScrolt), true);

        // Als het blok groter of kleiner wordt (schuifgreep), de laatste regel in beeld houden.
        Scroller.SizeChanged += (_, _) =>
        {
            if (!_gebruikerScrolt)
            {
                ScrollNaarOnder();
            }
        };
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

    private void GebruikerScrolt(object sender, PointerRoutedEventArgs e)
    {
        _gebruikerScrolt = true;
        _terugTimer.Stop();
        _terugTimer.Start();
    }

    private void VoegToe(Activiteit regel)
    {
        _regels.Add(regel);
        while (_regels.Count > MaxRegels)
        {
            _regels.RemoveAt(0);
        }

        Lampje.Opacity = 1;
        _lampTimer.Start();

        if (!_gebruikerScrolt)
        {
            ScrollNaarOnder();
        }
    }

    /// <summary>
    /// Eerst de lay-out bijwerken, anders kent de ScrollViewer de nieuwe
    /// regel nog niet en scrolt hij naar het oude einde.
    /// </summary>
    private void ScrollNaarOnder()
    {
        Scroller.UpdateLayout();
        Scroller.ChangeView(null, Scroller.ScrollableHeight, null, true);
    }
}
