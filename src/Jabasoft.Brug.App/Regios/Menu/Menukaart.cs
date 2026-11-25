namespace Jabasoft.Brug.App.Regios.Menu;

/// <summary>
/// Wat er in het menu van de Brug staat. Op een plek, zodat een nieuw
/// scherm of een nieuwe module hier een regel bijkrijgt en verder niets.
/// </summary>
public static class Menukaart
{
    public const string Startscherm = "hoofdscherm";

    public static IReadOnlyList<MenuOnderdeel> Onderdelen { get; } =
    [
        new("hoofdscherm", "Hoofdscherm", "",
            "Overzicht van de Brug, zoals het huidige Hoofdscherm van Jabasoft.", Fase: 2),

        new("broker", "Broker", "", Kinderen:
        [
            new("broker.nubezig", "Nu bezig", "",
                "Lopende verzoeken en de wachtrij, live vanuit de broker: wie vroeg wat, welk model, hoe lang al, en waarom iets wacht.", Fase: 3),
            new("broker.health", "Health", "",
                "Status van broker, database, AI-server, modellen, beeldservice en GPU.", Fase: 2),
            new("broker.tokens", "Tokens", "",
                "Tokenverbruik: totaal, grafiek, per onderdeel en per week.", Fase: 3),
            new("broker.modellen", "Modellen", "",
                "Elk model op de AI-server: waarvoor het is, waar het goed en minder goed in is, en voor welke taak het ingesteld staat.", Fase: 3),
            new("broker.logs", "Logs", "",
                "Geschiedenis van alle verzoeken, met filters op client, model, fouten en duur.", Fase: 3),
            new("broker.instellingen", "Instellingen", "",
                "AI-provider, Hermes, Goose, modellen, en de instellingen van elke module.", Fase: 3),
        ]),

        // Modules: staan er al, maar zijn pas te openen als ze in de Brug zitten.
        // ImageFunn is de eerste (fase 4); zijn schermen komen er een voor een bij.
        new("imagefunn", "ImageFunn", "", Kinderen:
        [
            new("imagefunn.tekstnaarbeeld", "Tekst naar beeld", "",
                "Een beeld maken uit een omschrijving, met galerij.", Fase: 4),
            new("imagefunn.beeldnaartekst", "Beeld naar tekst", "",
                "Een beeld laten beschrijven, uitlezen of omzetten naar een prompt.", Fase: 4),
            new("imagefunn.beeldnaarvideo", "Beeld naar video", "",
                "Een beeld laten bewegen.", Fase: 4),
            new("imagefunn.tekstnaarvideo", "Tekst naar video", "",
                "Een video maken uit een omschrijving.", Fase: 4),
            new("imagefunn.film", "Film maken", "",
                "Een verhaal in scènes, met cast, sleutelbeelden en de film.", Fase: 4),
        ]),
        new("aistudio", "AI Studio", "", Fase: 6, Beschikbaar: false),
        new("tabstudio", "TabStudio", "", Fase: 6, Beschikbaar: false),
        new("proefbank", "Proefbank", "", Fase: 6, Beschikbaar: false),
    ];

    /// <summary>Zoekt een onderdeel op sleutel, ook in submenu's.</summary>
    public static MenuOnderdeel? Zoek(string sleutel) => Zoek(Onderdelen, sleutel);

    private static MenuOnderdeel? Zoek(IEnumerable<MenuOnderdeel> lijst, string sleutel)
    {
        foreach (var onderdeel in lijst)
        {
            if (onderdeel.Sleutel == sleutel)
            {
                return onderdeel;
            }

            if (onderdeel.Kinderen is { } kinderen && Zoek(kinderen, sleutel) is { } gevonden)
            {
                return gevonden;
            }
        }

        return null;
    }
}
