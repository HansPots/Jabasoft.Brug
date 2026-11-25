using System.Globalization;
using System.Text.RegularExpressions;

namespace Jabasoft.Brug.App.Data;

/// <summary>Een vastgestelde keuze: welk model (een stukje van de naam), voor welke onderdelen, en waarom.</summary>
public sealed record Aanbeveling(string Deel, IReadOnlyList<string> Onderdelen, string Reden);

/// <summary>
/// De sterren van een model, zoals in het oude Jabasoft.App:
///
/// 1. Kwaliteit 0-5: geschat uit de grootte (lila), of zelf ingesteld (oranje,
///    in JabasoftBrug.dbo.Modelsterren) - een eigen waarde wint.
/// 2. Per onderdeel: "vastgesteld" (lila ster) = samen als beste keuze
///    bepaald door meten en proberen, met de reden erbij - bewust in code en
///    niet instelbaar; "favoriet" (oranje ster) = je eigen keuze, aan en uit
///    te klikken (JabasoftBrug.dbo.Favorieten).
/// </summary>
public static partial class Modelsterren
{
    /// <summary>De onderdelen die een model kunnen gebruiken: sleutel (zoals in de broker) en naam op het scherm.</summary>
    public static IReadOnlyList<(string Sleutel, string Naam)> Onderdelen { get; } =
    [
        ("Goose", "Goose"),
        ("Hermes", "Hermes"),
        ("LocalAiStudio", "AI Studio"),
        ("ImageFunn", "ImageFunn"),
        ("TabStudio", "TabStudio"),
    ];

    /// <summary>Overgenomen uit Jabasoft.App (Controls\Aanbevelingen.cs). Komt er een nieuwe uitkomst bij, dan komt hij hier.</summary>
    private static readonly Aanbeveling[] Lijst =
    [
        new("qwen3.6-35b-a3b", ["Goose"],
            "Hoofdmodel voor Goose (november 2026). In de Puzzeltest de beste in Goose bij de agent-taken: rekent echt " +
            "met de tool, verzint niets, en bouwde de netste rekenmachine. Met de MoE-experts op de CPU (LM Studio: GPU " +
            "Offload 40, 'force MoE weights onto CPU' 40, Flash Attention) ~40 tokens/s op de RTX 4070 SUPER, ongeveer " +
            "drie keer sneller dan volledig op de GPU, en het past in 6 GB videogeheugen. Context via de broker: 65.536, " +
            "meegroeiend tot 131.072."),
        new("gemma-4-12b", ["Hermes"],
            "Hoofdmodel voor Hermes (november 2026). In de Puzzeltest het sterkst in redeneren: slak, januari-meisje en " +
            "als enige de bootvraag meteen goed. Niet voor bouwen: in Hermes bleef de rekenmachine een leeg sjabloon en " +
            "meldde het toch een volledige app - controleer wat het zegt gedaan te hebben. Past helemaal op de videokaart."),
        new("gemma-4-12b", ["LocalAiStudio", "ImageFunn"],
            "Chatmodel voor LocalAiStudio en ImageFunn (november 2026): het sterkst in redeneren in de Puzzeltest, past " +
            "helemaal op de videokaart (7 GB) en is dus snel genoeg voor gewone vragen. In ImageFunn vertaalt het de " +
            "Nederlandse prompt naar Engels voor het beeldmodel. Nog niet apart in deze apps vergeleken."),
        new("bge-m3", ["LocalAiStudio"],
            "Embeddingmodel voor het zoeken in LocalAiStudio: meertalig, dus ook Nederlandse tekst, en klein (0,6 GB). " +
            "Het andere embeddingmodel (nomic) leest alleen Engels goed."),
        new("juggernaut-xl-ragnarok", ["ImageFunn"],
            "Het beeldmodel van ImageFunn (standaard sinds oktober 2026): ImageFunn leest het rechtstreeks uit de " +
            "LM Studio-map. Geen chatmodel - daarom staat het in de modeltabel op niet tonen."),
    ];

    /// <summary>Wat er voor dit model bij dit onderdeel is vastgesteld - leeg als niets.</summary>
    public static IReadOnlyList<Aanbeveling> Vastgesteld(string model, string onderdeel) =>
        [.. Lijst.Where(a => model.Contains(a.Deel, StringComparison.OrdinalIgnoreCase) && a.Onderdelen.Contains(onderdeel))];

    /// <summary>
    /// Schatting op grootte, zoals in het oude Jabasoft.App: hoe meer
    /// parameters, hoe beter (en trager). Een sterk ingekorte kwantisatie
    /// (Q2, Q3) kost een ster. Een model zonder bekende grootte en een
    /// embeddingmodel krijgen drie - niet te veel, niet te weinig.
    /// </summary>
    public static int Schat(string model)
    {
        var laag = model.ToLowerInvariant();
        if (laag.Contains("embed", StringComparison.Ordinal) ||
            Grootte().Match(laag) is not { Success: true } treffer ||
            !double.TryParse(treffer.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var miljarden))
        {
            return 3;
        }

        var sterren = miljarden switch
        {
            < 2 => 1,
            < 5 => 2,
            < 13 => 3,
            < 25 => 4,
            _ => 5,
        };

        if (Kwantisatie().Match(laag) is { Success: true } kwant &&
            (kwant.Groups[1].Value.StartsWith("q2", StringComparison.Ordinal) || kwant.Groups[1].Value.StartsWith("q3", StringComparison.Ordinal)))
        {
            sterren--;
        }

        return Math.Clamp(sterren, 0, 5);
    }

    /// <summary>De sterren als tekst, bijvoorbeeld ★★★★☆.</summary>
    public static string Tekst(int sterren) => new string('★', sterren) + new string('☆', 5 - sterren);

    // Dezelfde patronen als Modelkeuze in het oude Jabasoft.App.
    [GeneratedRegex(@"(?<=^|[-:_/ ])(\d+(?:\.\d+)?)b(?![a-z0-9])")]
    private static partial Regex Grootte();

    [GeneratedRegex(@"(?<=^|[-:_/ ])(q\d[a-z0-9_]*)")]
    private static partial Regex Kwantisatie();
}
