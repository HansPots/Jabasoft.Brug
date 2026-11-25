using System.Text.Json;
using Jabasoft.Brug.App.Diensten;

namespace Jabasoft.Brug.App.Layout;

/// <summary>
/// Kleine keuzes die de Brug onthoudt (bijvoorbeeld het standaard beeldmodel
/// van ImageFunn), als tekst per sleutel in de gegevensmap:
/// Brug\voorkeuren.json. Net als <see cref="Indeling"/> bewust geen
/// database: een kapot of ontbrekend bestand mag nooit iets tegenhouden.
/// </summary>
public static class Voorkeuren
{
    private static readonly Lock Slot = new();

    private static string Bestand => Gegevensmap.Bestand("Brug", "voorkeuren.json");

    public static string Lees(string sleutel, string standaard = "")
    {
        lock (Slot)
        {
            return LeesAlles().TryGetValue(sleutel, out var waarde) ? waarde : standaard;
        }
    }

    public static int Lees(string sleutel, int standaard) =>
        int.TryParse(Lees(sleutel, ""), out var getal) ? getal : standaard;

    public static void Bewaar(string sleutel, string waarde)
    {
        lock (Slot)
        {
            try
            {
                var alles = LeesAlles();
                alles[sleutel] = waarde;
                File.WriteAllText(Bestand, JsonSerializer.Serialize(alles, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception)
            {
                // Niet kunnen bewaren is jammer, maar geen reden om iets te laten mislukken.
            }
        }
    }

    private static Dictionary<string, string> LeesAlles()
    {
        try
        {
            return File.Exists(Bestand)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Bestand)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
