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
                "Wat er geladen is, VRAM en RAM, contextgrootte, en wie de GPU nu heeft.", Fase: 3),
            new("broker.logs", "Logs", "",
                "Geschiedenis van alle verzoeken, met filters op client, model, fouten en duur.", Fase: 3),
            new("broker.instellingen", "Instellingen", "",
                "AI-provider, Hermes, Goose, modellen, en de instellingen van elke module.", Fase: 3),
        ]),

        // Modules: staan er al, maar zijn pas te openen als ze in de Brug zitten.
        new("aistudio", "AI Studio", "", Fase: 6, Beschikbaar: false),
        new("tabstudio", "TabStudio", "", Fase: 6, Beschikbaar: false),
        new("imagefunn", "ImageFunn", "", Fase: 6, Beschikbaar: false),
        new("proefbank", "Proefbank", "", Fase: 5, Beschikbaar: false),
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
