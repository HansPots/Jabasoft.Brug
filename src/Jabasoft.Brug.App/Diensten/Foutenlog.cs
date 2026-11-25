namespace Jabasoft.Brug.App.Diensten;

/// <summary>
/// Onafgevangen fouten op schijf: %LOCALAPPDATA%\Jabasoft\Brug\fouten.log,
/// met de volledige stack trace. Zo is na een crash terug te vinden wat er
/// misging. Het bestand wordt boven 1 MB opnieuw begonnen.
/// </summary>
public static class Foutenlog
{
    public static string Pad { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jabasoft", "Brug", "fouten.log");

    private static readonly Lock Slot = new();

    public static void Schrijf(string wat, Exception? fout)
    {
        try
        {
            lock (Slot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Pad)!);
                if (File.Exists(Pad) && new FileInfo(Pad).Length > 1_000_000)
                {
                    File.Delete(Pad);
                }

                File.AppendAllText(Pad, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {App.Versie} {wat}{Environment.NewLine}{fout}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Loggen mag zelf nooit een fout geven.
        }
    }
}
