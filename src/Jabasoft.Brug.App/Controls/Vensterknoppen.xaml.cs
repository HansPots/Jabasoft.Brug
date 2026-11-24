using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Controls;

/// <summary>
/// Minimaliseren, maximaliseren/herstellen en sluiten van het venster
/// waarin dit component staat. Het venster wordt met <see cref="Koppel"/>
/// doorgegeven; WinUI kent geen Window.GetWindow zoals WPF.
/// </summary>
public sealed partial class Vensterknoppen : UserControl
{
    private Window? _venster;

    public Vensterknoppen()
    {
        InitializeComponent();
    }

    /// <summary>Koppelt de knoppen aan het venster en houdt het herstelteken bij.</summary>
    public void Koppel(Window venster)
    {
        _venster = venster;
        venster.AppWindow.Changed += (_, e) =>
        {
            if (e.DidPresenterChange || e.DidSizeChange)
            {
                ToonHerstelTeken();
            }
        };
        ToonHerstelTeken();
    }

    private OverlappedPresenter? Presenter => _venster?.AppWindow.Presenter as OverlappedPresenter;

    private void ToonHerstelTeken()
    {
        var groot = Presenter?.State == OverlappedPresenterState.Maximized;
        MaximaliseerTeken.Visibility = groot ? Visibility.Collapsed : Visibility.Visible;
        HerstelTeken.Visibility = groot ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(HerstelKnop, groot ? "Vorige grootte" : "Maximaliseren");
    }

    private void Minimaliseer_Click(object sender, RoutedEventArgs e) => Presenter?.Minimize();

    private void Herstel_Click(object sender, RoutedEventArgs e)
    {
        if (Presenter is not { } presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }
    }

    private void Sluit_Click(object sender, RoutedEventArgs e) => _venster?.Close();
}
