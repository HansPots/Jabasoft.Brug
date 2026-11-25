using Jabasoft.Brug.App.Paginas;
using Jabasoft.Brug.App.Regios.Menu;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

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

        // Een ImageFunn-scherm stuurt een beeld door (bijvoorbeeld "Laat dit beeld bewegen"): open dat scherm en geef het beeld mee.
        Modules.ImageFunn.Doorgeven.Gevraagd += async (_, vraag) =>
        {
            Menu.Kies(vraag.Scherm);
            if (_schermen.TryGetValue(vraag.Scherm, out var scherm) && scherm is Modules.ImageFunn.IOntvangtBeeld ontvanger)
            {
                await ontvanger.OntvangAsync(vraag.Pad);
            }
        };
        Modules.ImageFunn.Doorgeven.TekstGevraagd += (_, vraag) =>
        {
            Menu.Kies(vraag.Scherm);
            if (_schermen.TryGetValue(vraag.Scherm, out var scherm) && scherm is Modules.ImageFunn.IOntvangtTekst ontvanger)
            {
                ontvanger.Ontvang(vraag.Tekst);
            }
        };
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
        Acties.ZetScherm(sleutel, scherm);
        App.AppLog.Schrijf("brug", $"Scherm geopend: {Pad(sleutel)}");
    }

    /// <summary>"Broker › Nu bezig" voor een submenu-onderdeel, anders alleen de titel.</summary>
    private static string Pad(string sleutel)
    {
        var titel = Menukaart.Zoek(sleutel)?.Titel ?? sleutel;
        var punt = sleutel.IndexOf('.');
        return punt > 0 && Menukaart.Zoek(sleutel[..punt]) is { } ouder ? $"{ouder.Titel} › {titel}" : titel;
    }

    /// <summary>Het scherm bij een menu-onderdeel; wat nog niet gebouwd is, wordt een lege pagina.</summary>
    private static UIElement MaakScherm(MenuOnderdeel onderdeel) => onderdeel.Sleutel switch
    {
        "hoofdscherm" => new Hoofdscherm(),
        "broker.nubezig" => new Paginas.Broker.NuBezig(),
        "broker.health" => new Paginas.Broker.Health(),
        "broker.tokens" => new Paginas.Broker.Tokens(),
        "broker.modellen" => new Paginas.Broker.Modellen(),
        "broker.logs" => new Paginas.Broker.Logs(),
        "broker.instellingen" => new Paginas.Broker.Instellingen(),
        "imagefunn.tekstnaarbeeld" => new Modules.ImageFunn.TekstNaarBeeld(),
        "imagefunn.beeldnaarvideo" => new Modules.ImageFunn.VideoScherm(vanBeeld: true),
        "imagefunn.tekstnaarvideo" => new Modules.ImageFunn.VideoScherm(vanBeeld: false),
        "imagefunn.beeldnaartekst" => new Modules.ImageFunn.BeeldNaarTekst(),
        "imagefunn.film" => new Modules.ImageFunn.FilmScherm(),
        _ => new LegePagina(onderdeel),
    };

    /// <summary>
    /// Eigen titelbalk: de inhoud loopt door tot bovenin het venster en de
    /// kleurbalk in de header is het stuk waarmee je sleept. De knoppen van
    /// Windows worden verborgen (Collapsed); de header heeft eigen
    /// vensterknoppen, zoals Jabasoft.App.
    /// </summary>
    private void RichtVensterIn()
    {
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        SetTitleBar(Header.Sleepgebied);
        Header.KoppelVenster(this);

        if (AppWindow.Presenter is OverlappedPresenter venster)
        {
            venster.Maximize();
        }
    }
}
