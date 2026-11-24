using Jabasoft.Brug.App.Paginas;
using Jabasoft.Brug.App.Regios.Menu;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Jabasoft.Brug.App;

/// <summary>
/// Het enige venster van de Brug. Het menu kiest, dit venster toont.
///
/// Schermen worden een keer gemaakt en daarna bewaard: ga je terug naar
/// een scherm, dan staat het er nog precies zoals je het verliet. Dat is
/// het hele punt van de Brug - wisselen is navigeren, niet opnieuw opstarten.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, UIElement> _schermen = [];

    public MainWindow()
    {
        InitializeComponent();
        RichtVensterIn();

        Menu.Gekozen += (_, sleutel) => Toon(sleutel);
        Menu.Vul(Menukaart.Onderdelen);
        Menu.Kies(Menukaart.Startscherm);
    }

    private void Toon(string sleutel)
    {
        if (!_schermen.TryGetValue(sleutel, out var scherm))
        {
            var onderdeel = Menukaart.Zoek(sleutel);
            if (onderdeel is null)
            {
                return;
            }

            scherm = MaakScherm(onderdeel);
            _schermen[sleutel] = scherm;
        }

        Schil.MainContent = scherm;
    }

    /// <summary>Het scherm bij een menu-onderdeel; wat nog niet gebouwd is, wordt een lege pagina.</summary>
    private static UIElement MaakScherm(MenuOnderdeel onderdeel) => onderdeel.Sleutel switch
    {
        "broker.nubezig" => new Paginas.Broker.NuBezig(),
        "broker.health" => new Paginas.Broker.Health(),
        _ => new LegePagina(onderdeel),
    };

    /// <summary>
    /// Eigen titelbalk: de inhoud loopt door tot bovenin het venster en de
    /// titelstrook in de header is het stuk waarmee je sleept. De
    /// vensterknoppen van Windows blijven, in LCARS-kleuren.
    /// </summary>
    private void RichtVensterIn()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Header.Sleepgebied);

        var balk = AppWindow.TitleBar;
        var oranje = Color.FromArgb(0xFF, 0xFF, 0x99, 0x00);
        balk.ButtonBackgroundColor = Colors.Transparent;
        balk.ButtonInactiveBackgroundColor = Colors.Transparent;
        balk.ButtonForegroundColor = oranje;
        balk.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0xCC, 0x66, 0x66);
        balk.ButtonHoverBackgroundColor = Color.FromArgb(0xFF, 0x33, 0x22, 0x00);
        balk.ButtonHoverForegroundColor = oranje;
        balk.ButtonPressedBackgroundColor = Color.FromArgb(0xFF, 0x55, 0x33, 0x00);
        balk.ButtonPressedForegroundColor = oranje;

        if (AppWindow.Presenter is OverlappedPresenter venster)
        {
            venster.Maximize();
        }
    }
}
