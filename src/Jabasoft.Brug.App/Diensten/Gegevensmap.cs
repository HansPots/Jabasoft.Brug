namespace Jabasoft.Brug.App.Diensten;

/// <summary>
/// De map waarin alle bestanden van de Brug komen: voorkeuren, indeling,
/// foutenlog en wat de modules maken (beelden, video's, films). Standaard
/// D:\JabasoftDeBrug; instelbaar bij Broker &gt; Instellingen, paneel
/// Bestanden.
///
/// Welke map het is, staat als enige in %LOCALAPPDATA%\Jabasoft\Brug\
/// gegevensmap.txt: die plek moet al bekend zijn voordat de map zelf dat is.
/// Een andere map kiezen geldt na het opnieuw starten van de Brug; wat er al
/// in de oude map stond, blijft daar staan.
/// </summary>
public static class Gegevensmap
{
    public const string Standaard = @"D:\JabasoftDeBrug";

    private static readonly string Wijzer = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jabasoft", "Brug", "gegevensmap.txt");

    /// <summary>De map zoals die bij het starten van de Brug gold (vast voor deze sessie).</summary>
    public static string Pad { get; } = LeesWijzer() ?? Standaard;

    /// <summary>De gekozen map zoals die nu in de wijzer staat (kan na een wijziging afwijken van <see cref="Pad"/>).</summary>
    public static string Gekozen => LeesWijzer() ?? Standaard;

    /// <summary>Een submap van de gegevensmap, aangemaakt als hij er nog niet is.</summary>
    public static string Map(params string[] delen)
    {
        var map = Path.Combine([Pad, .. delen]);
        try
        {
            Directory.CreateDirectory(map);
        }
        catch (Exception)
        {
            // Bijvoorbeeld een schijf die er niet is: de aanroeper merkt het bij het schrijven.
        }

        return map;
    }

    /// <summary>Een bestand in een submap van de gegevensmap; de submap wordt aangemaakt.</summary>
    public static string Bestand(string submap, string naam) => Path.Combine(Map(submap), naam);

    /// <summary>
    /// Kiest een andere map. Geeft null als dat gelukt is, anders wat er misging.
    /// Geldt na het opnieuw starten van de Brug.
    /// </summary>
    public static string? Kies(string map)
    {
        map = map.Trim().TrimEnd('\\', '/');
        if (map.Length == 0 || !Path.IsPathFullyQualified(map))
        {
            return "Geef een volledig pad op, bijvoorbeeld D:\\JabasoftDeBrug.";
        }

        try
        {
            Directory.CreateDirectory(map);
            Directory.CreateDirectory(Path.GetDirectoryName(Wijzer)!);
            File.WriteAllText(Wijzer, map);
            return null;
        }
        catch (Exception fout)
        {
            return fout.Message;
        }
    }

    /// <summary>
    /// Zet een bestand dat vroeger in %LOCALAPPDATA%\Jabasoft\Brug stond eenmalig
    /// over naar de gegevensmap (als kopie: het oude blijft staan).
    /// </summary>
    public static string Overgezet(string submap, string naam)
    {
        var nieuw = Bestand(submap, naam);
        try
        {
            var oud = Path.Combine(Path.GetDirectoryName(Wijzer)!, naam);
            if (!File.Exists(nieuw) && File.Exists(oud))
            {
                File.Copy(oud, nieuw);
            }
        }
        catch (Exception)
        {
            // Overzetten is een gemak; zonder begint het bestand gewoon opnieuw.
        }

        return nieuw;
    }

    private static string? LeesWijzer()
    {
        try
        {
            return File.Exists(Wijzer) && File.ReadAllText(Wijzer).Trim() is { Length: > 0 } map ? map : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
