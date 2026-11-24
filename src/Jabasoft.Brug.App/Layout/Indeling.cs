using System.Text.Json;

namespace Jabasoft.Brug.App.Layout;

/// <summary>
/// Onthoudt hoe de gebruiker de indeling heeft geschoven (de breedtes van
/// de infoblokken), in een klein JSON-bestand in
/// %LOCALAPPDATA%\Jabasoft\Brug\indeling.json. Per sleutel een rij getallen.
///
/// Bewust geen database: het is een voorkeur van dit scherm op deze pc, en
/// een kapot of ontbrekend bestand mag nooit het starten tegenhouden.
/// </summary>
public static class Indeling
{
    private static readonly string Bestand = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jabasoft", "Brug", "indeling.json");

    public static double[]? Lees(string sleutel)
    {
        try
        {
            if (!File.Exists(Bestand))
            {
                return null;
            }

            var alles = JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(Bestand));
            return alles is not null && alles.TryGetValue(sleutel, out var waarden) ? waarden : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Bewaar(string sleutel, double[] waarden)
    {
        try
        {
            Dictionary<string, double[]> alles = [];
            if (File.Exists(Bestand))
            {
                alles = JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(Bestand)) ?? [];
            }

            alles[sleutel] = waarden;
            Directory.CreateDirectory(Path.GetDirectoryName(Bestand)!);
            File.WriteAllText(Bestand, JsonSerializer.Serialize(alles));
        }
        catch (Exception)
        {
            // Niet kunnen bewaren is jammer, maar geen reden om iets te laten mislukken.
        }
    }
}
