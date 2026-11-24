using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Jabasoft.Brug.App.Controls;

/// <summary>
/// Een greep tussen twee kolommen van een Grid: slepen maakt de kolom links
/// breder en die rechts smaller (of andersom). Dubbelklikken zet alle
/// genoemde kolommen weer even breed.
///
/// Zelf gemaakt in plaats van de GridSplitter uit het CommunityToolkit: het
/// is een handvol regels en zo hangt de Brug niet aan nog een pakket.
///
/// De greep ligt in de tussenruimte van 6 tussen twee blokken, net als de
/// splitters in Jabasoft.App. De kolommen blijven "sterren" (verhoudingen),
/// zodat ze bij een ander venster mee schalen.
/// </summary>
public sealed partial class Schuifgreep : Grid
{
    private const double MinimaleBreedte = 120;

    private Grid? _rooster;
    private double _vorigeX;

    public Schuifgreep()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        PointerPressed += Begin;
        PointerMoved += Sleep;
        PointerReleased += Einde;
        PointerCaptureLost += (_, _) => _rooster = null;
        DoubleTapped += (_, _) => Gelijk();
    }

    /// <summary>De kolom links van de greep.</summary>
    public int LinkerKolom { get; set; }

    /// <summary>De kolom rechts van de greep.</summary>
    public int RechterKolom { get; set; }

    /// <summary>Alle kolommen die meedoen (voor dubbelklikken = gelijk maken).</summary>
    public int[] Kolommen { get; set; } = [];

    /// <summary>De breedtes zijn veranderd (na slepen of dubbelklikken).</summary>
    public event EventHandler? Verschoven;

    private void Begin(object sender, PointerRoutedEventArgs e)
    {
        if (Parent is not Grid rooster)
        {
            return;
        }

        _rooster = rooster;
        _vorigeX = e.GetCurrentPoint(rooster).Position.X;

        // Eerst alle sterren gelijkzetten aan de huidige breedte in pixels,
        // zodat een sleep van 10 px ook echt 10 px verschuift.
        foreach (var k in Kolommen)
        {
            var kolom = rooster.ColumnDefinitions[k];
            kolom.Width = new GridLength(kolom.ActualWidth, GridUnitType.Star);
        }

        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Sleep(object sender, PointerRoutedEventArgs e)
    {
        if (_rooster is null)
        {
            return;
        }

        var x = e.GetCurrentPoint(_rooster).Position.X;
        var dx = x - _vorigeX;
        var links = _rooster.ColumnDefinitions[LinkerKolom];
        var rechts = _rooster.ColumnDefinitions[RechterKolom];
        var nieuwLinks = links.Width.Value + dx;
        var nieuwRechts = rechts.Width.Value - dx;
        if (nieuwLinks < MinimaleBreedte || nieuwRechts < MinimaleBreedte)
        {
            return;
        }

        links.Width = new GridLength(nieuwLinks, GridUnitType.Star);
        rechts.Width = new GridLength(nieuwRechts, GridUnitType.Star);
        _vorigeX = x;
        e.Handled = true;
    }

    private void Einde(object sender, PointerRoutedEventArgs e)
    {
        if (_rooster is null)
        {
            return;
        }

        _rooster = null;
        ReleasePointerCapture(e.Pointer);
        Verschoven?.Invoke(this, EventArgs.Empty);
    }

    private void Gelijk()
    {
        if (Parent is not Grid rooster)
        {
            return;
        }

        foreach (var k in Kolommen)
        {
            rooster.ColumnDefinitions[k].Width = new GridLength(1, GridUnitType.Star);
        }

        Verschoven?.Invoke(this, EventArgs.Empty);
    }
}
