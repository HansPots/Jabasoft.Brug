using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Jabasoft.Brug.App.Diensten;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// Haalt de taalmodellen uit het videogeheugen vlak voordat de beeldservice
/// begint. Ollama en LM Studio houden een model na een vraag nog minutenlang
/// geladen; een chatmodel van ±9 GB laat op 12 GB te weinig over voor beeld
/// en video. Het taalmodel wordt bij de volgende vraag vanzelf weer geladen.
/// Alles hierin is "zo goed als het gaat": mislukt het, dan gaat het maken
/// gewoon door. Overgenomen uit de oude ImageFunn-app (ModelOntlader); vervalt
/// in fase 5, als de broker de GPU-beurten verdeelt.
/// </summary>
public static class Taalmodellen
{
    private static readonly TimeSpan Limiet = TimeSpan.FromSeconds(8);

    public static async Task OntlaadAsync()
    {
        try
        {
            var instellingen = await BrokerApi.InstellingenAsync().ConfigureAwait(false);
            if (instellingen.Waarde is not { } geheel)
            {
                return;
            }

            if (geheel["provider"]?.GetValue<string>() == "Ollama")
            {
                var url = geheel["ollama"]?["url"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    await OntlaadOllamaAsync(url).ConfigureAwait(false);
                }
            }
            else
            {
                OntlaadLmStudio();
            }
        }
        catch (Exception)
        {
            // Niet bereikbaar of geen rechten: het maken zelf hoeft daar niet door te stoppen.
        }
    }

    /// <summary>Vraagt Ollama welke modellen geladen zijn en zet ze op keep_alive 0, wat ze meteen ontlaadt.</summary>
    private static async Task OntlaadOllamaAsync(string url)
    {
        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = Limiet };
        using var antwoord = await client.GetAsync("/api/ps").ConfigureAwait(false);
        if (!antwoord.IsSuccessStatusCode)
        {
            return;
        }

        using var document = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("models", out var modellen))
        {
            return;
        }

        foreach (var model in modellen.EnumerateArray())
        {
            if (model.TryGetProperty("name", out var naam) && naam.GetString() is { Length: > 0 } modelNaam)
            {
                using var _ = await client.PostAsJsonAsync("/api/generate", new { model = modelNaam, keep_alive = 0 }).ConfigureAwait(false);
                App.AppLog.Schrijf("imagefunn", $"Videogeheugen vrij: {modelNaam} ontladen");
            }
        }
    }

    /// <summary>LM Studio heeft een opdrachtregelprogramma (lms) dat alle geladen modellen ontlaadt.</summary>
    private static void OntlaadLmStudio()
    {
        var lms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio", "bin", "lms.exe");
        if (!File.Exists(lms))
        {
            return;
        }

        using var proces = Process.Start(new ProcessStartInfo
        {
            FileName = lms,
            Arguments = "unload --all",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        if (proces is not null && proces.WaitForExit((int)Limiet.TotalMilliseconds))
        {
            App.AppLog.Schrijf("imagefunn", "Videogeheugen vrij: LM Studio-modellen ontladen");
        }
        else
        {
            try
            {
                proces?.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Al gestopt.
            }
        }
    }
}
