using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jabasoft.Brug.App.Diensten;
using Jabasoft.Brug.App.Layout;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// De lokale Python-beeldservice (service.py) voor ImageFunn: starten als hij
/// niet draait, opdrachten geven en de stand opvragen. Overgenomen uit de oude
/// ImageFunn-app (ImageServiceClient + ImageServiceLauncher), met één
/// verschil: alles wat de service maakt, komt in de gegevensmap van de Brug
/// (ImageFunn\uitvoer), en zijn log in ImageFunn\service.log.
///
/// Tot fase 5 praat de Brug hier rechtstreeks mee (poort 5330) en maakt hij
/// zelf de taalmodellen uit het videogeheugen los; daarna gaat dit via de
/// GPU-beurt van de broker.
/// </summary>
public static class Beeldservice
{
    // Niet "localhost": uvicorn luistert alleen op 127.0.0.1, en dan probeert Windows eerst ::1 (2 s per verbinding).
    public const string Adres = "http://127.0.0.1:5330";

    /// <summary>De Python-kant van ImageFunn: service.py met zijn eigen venv. Verhuist in fase 7 naar de Brug-repo.</summary>
    public const string ServiceMap = @"D:\Repos\JabaSoft.ImageFunn\ImageFunn.Image";

    /// <summary>Voorkeuren: het standaard beeldmodel (leeg = de standaard van de service) en na hoeveel seconden stilstand de service stopt.</summary>
    public const string VoorkeurBeeldmodel = "ImageFunn.Beeldmodel";
    public const string VoorkeurOntladenNa = "ImageFunn.OntladenNa";

    /// <summary>Waar de service zijn beelden, video's en films neerzet.</summary>
    public static string Uitvoermap => Gegevensmap.Map("ImageFunn", "uitvoer");

    private static string LogPad => Gegevensmap.Bestand("ImageFunn", "service.log");

    // Ruim: na een herstart van de pc staat er nog niets in de schijfcache en duurt alleen het laden van PyTorch al lang.
    private static readonly TimeSpan Opstarttijd = TimeSpan.FromMinutes(3);

    private static readonly HttpClient Http = new() { BaseAddress = new Uri(Adres), Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private static readonly SemaphoreSlim StartSlot = new(1, 1);

    private static Process? _gestart;

    /// <summary>De tekst voor op het scherm bij een mislukte aanroep.</summary>
    public static string Foutmelding(Exception fout) => fout switch
    {
        BeeldserviceFout => fout.Message,
        HttpRequestException { StatusCode: not null } => $"De beeldservice weigerde dit: {fout.Message}",
        HttpRequestException or TaskCanceledException => $"Kon de beeldservice niet bereiken: {fout.Message}",
        _ => $"Er ging iets mis: {fout.Message}",
    };

    // ---------------------------------------------------------------- opdrachten

    /// <summary>Start een opdracht en geeft het job-id terug. Maakt eerst het videogeheugen vrij en start de service zo nodig.</summary>
    public static async Task<string> StartAsync(BeeldOpdracht opdracht, IProgress<string>? melding = null)
    {
        melding?.Report("Videogeheugen vrijmaken…");
        await Taalmodellen.OntlaadAsync().ConfigureAwait(false);
        await HerstartBijAnderModelAsync(opdracht, melding).ConfigureAwait(false);

        return await MetHerstelAsync(async () =>
        {
            using var antwoord = await Http.PostAsJsonAsync("/jobs", opdracht, Json).ConfigureAwait(false);
            await ControleerAsync(antwoord).ConfigureAwait(false);
            var lijf = await antwoord.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
            return lijf.TryGetProperty("job_id", out var id) && id.GetString() is { Length: > 0 } jobId
                ? jobId
                : throw new BeeldserviceFout("De beeldservice gaf geen job-id terug.");
        }, melding).ConfigureAwait(false);
    }

    /// <summary>De stand van een opdracht. Blijft de service onbereikbaar, dan is hij tijdens de opdracht gestopt.</summary>
    public static async Task<BeeldJobStatus> StatusAsync(string jobId)
    {
        for (var poging = 1; ; poging++)
        {
            try
            {
                return await Http.GetFromJsonAsync<BeeldJobStatus>($"/jobs/{jobId}").ConfigureAwait(false)
                    ?? throw new BeeldserviceFout("De beeldservice gaf geen stand terug.");
            }
            catch (HttpRequestException fout) when (fout.StatusCode is null)
            {
                if (poging == 3)
                {
                    throw new BeeldserviceFout(
                        "De beeldservice is tijdens deze opdracht gestopt, waarschijnlijk doordat het geheugen vol raakte. Probeer het opnieuw; de service start dan vanzelf weer.",
                        fout);
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Alle eerder gemaakte opdrachten van een soort ("beeld", "video", "film"), nieuwste eerst.</summary>
    public static async Task<List<BeeldJobStatus>> LijstAsync(string soort) =>
        await MetHerstelAsync(() => Http.GetFromJsonAsync<List<BeeldJobStatus>>($"/jobs?soort={soort}"), null).ConfigureAwait(false) ?? [];

    /// <summary>Verwijdert een afgeronde opdracht met al zijn bestanden. Kan niet ongedaan gemaakt worden.</summary>
    public static Task VerwijderAsync(string jobId) => MetHerstelAsync(async () =>
    {
        using var antwoord = await Http.DeleteAsync($"/jobs/{jobId}").ConfigureAwait(false);
        await ControleerAsync(antwoord).ConfigureAwait(false);
        return true;
    }, null);

    /// <summary>Vraagt een lopende opdracht te stoppen (na de huidige stap).</summary>
    public static async Task StopAsync(string jobId)
    {
        using var antwoord = await Http.PostAsync($"/jobs/{jobId}/stop", null).ConfigureAwait(false);
        await ControleerAsync(antwoord).ConfigureAwait(false);
    }

    /// <summary>Laat de service zijn modellen los (hij stopt dan zichzelf). False als er een opdracht loopt.</summary>
    public static async Task<bool> MaakVideogeheugenVrijAsync()
    {
        try
        {
            using var antwoord = await Http.PostAsync("/ontladen", null).ConfigureAwait(false);
            return antwoord.StatusCode != System.Net.HttpStatusCode.Conflict;
        }
        catch (Exception fout) when (fout is HttpRequestException or TaskCanceledException)
        {
            return true;
        }
    }

    /// <summary>De stand van de service, of null als hij niet draait.</summary>
    public static async Task<BeeldServiceGezondheid?> GezondheidAsync()
    {
        try
        {
            return await Http.GetFromJsonAsync<BeeldServiceGezondheid>("/health").ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task ControleerAsync(HttpResponseMessage antwoord)
    {
        if (!antwoord.IsSuccessStatusCode)
        {
            var reden = (await antwoord.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
            throw new HttpRequestException(reden.Length > 300 ? reden[..300] + "…" : reden, null, antwoord.StatusCode);
        }
    }

    /// <summary>
    /// Voert een aanroep uit en start de service als die niet bereikbaar is. Hij stopt zichzelf na een tijd zonder werk
    /// (IMAGEFUNN_ONTLAAD_NA) en kan net aan het stoppen zijn, vandaar meer dan één poging. Een weigering van de
    /// service zelf (met statuscode) wordt niet herhaald.
    /// </summary>
    private static async Task<T> MetHerstelAsync<T>(Func<Task<T>> actie, IProgress<string>? melding)
    {
        for (var poging = 1; ; poging++)
        {
            try
            {
                return await actie().ConfigureAwait(false);
            }
            catch (HttpRequestException fout) when (fout.StatusCode is null && poging < 3)
            {
                if (poging > 1)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                }

                if (await ZorgDatHijDraaitAsync(melding).ConfigureAwait(false) is { } startFout)
                {
                    throw new BeeldserviceFout(startFout, fout);
                }
            }
        }
    }

    /// <summary>
    /// Een ander model dan gevraagd geladen? Dan laat de service het eerst los (hij stopt daarbij). PyTorch geeft het
    /// werkgeheugen pas terug als het proces stopt; bij wisselen binnen één proces stapelt het op tot Windows vol zit.
    /// </summary>
    private static async Task HerstartBijAnderModelAsync(BeeldOpdracht opdracht, IProgress<string>? melding)
    {
        var gezond = await GezondheidAsync().ConfigureAwait(false);
        if (gezond is null || !HeeftAnderModel(gezond, opdracht))
        {
            return;
        }

        melding?.Report("Ander model: de beeldservice start opnieuw om het geheugen vrij te maken…");
        if (!await MaakVideogeheugenVrijAsync().ConfigureAwait(false))
        {
            return;
        }

        for (var i = 0; i < 20 && await GezondheidAsync().ConfigureAwait(false) is not null; i++)
        {
            await Task.Delay(250).ConfigureAwait(false);
        }
    }

    private static bool HeeftAnderModel(BeeldServiceGezondheid gezond, BeeldOpdracht opdracht)
    {
        var wilBeeld = opdracht.Soort != "video";
        var wilVideo = opdracht.Soort is "video" or "film";
        var beeldmodel = string.IsNullOrEmpty(opdracht.Beeldmodel) ? gezond.Model : opdracht.Beeldmodel;

        // Bijsturen kan niet met FLUX: de service gebruikt dan RealVisXL.
        if (opdracht.Soort == "beeld" && opdracht.BronBeeld is not null && beeldmodel == "flux")
        {
            beeldmodel = "realvis";
        }

        return gezond.ModelGeladen && (!wilBeeld || gezond.BeeldModel != beeldmodel)
               || gezond.VideoModelGeladen && (!wilVideo || gezond.VideoModel != opdracht.Videomodel);
    }

    // ---------------------------------------------------------------- starten

    /// <summary>Zorgt dat de service draait, met de code van nu en de uitvoermap van de Brug. Null = gelukt, anders wat er misging.</summary>
    public static async Task<string?> ZorgDatHijDraaitAsync(IProgress<string>? melding = null)
    {
        await StartSlot.WaitAsync().ConfigureAwait(false);
        try
        {
            var (draait, versie, uitvoer) = await LeesStandAsync().ConfigureAwait(false);
            if (draait)
            {
                // Een draaiende service kan van eerder zijn: oude code, of gestart door de oude ImageFunn-app met een
                // andere uitvoermap. Dan stoppen en opnieuw starten.
                if (versie == CodeVersie() && ZelfdeMap(uitvoer, Uitvoermap))
                {
                    return null;
                }

                App.AppLog.Schrijf("imagefunn", "De beeldservice draait met oude code of een andere uitvoermap - opnieuw starten.");
                await StopPoortAsync().ConfigureAwait(false);
            }

            var python = Path.Combine(ServiceMap, ".venv", "Scripts", "python.exe");
            if (!File.Exists(python))
            {
                return $"De Python-omgeving van de beeldservice ontbreekt ({python}).";
            }

            melding?.Report("De beeldservice start op, dit kan even duren…");
            if (_gestart is not { HasExited: false })
            {
                BereidLogVoor();
                var start = new ProcessStartInfo
                {
                    // Via cmd, zodat de uitvoer rechtstreeks naar het logbestand gaat, ook als de Brug wegvalt.
                    FileName = "cmd.exe",
                    Arguments = $"/d /s /c \"\"{python}\" -m uvicorn service:app --port 5330 >> \"{LogPad}\" 2>&1\"",
                    WorkingDirectory = ServiceMap,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                start.Environment["PYTHONUNBUFFERED"] = "1";
                start.Environment["PYTHONIOENCODING"] = "utf-8";
                start.Environment["IMAGEFUNN_UITVOER"] = Uitvoermap;
                if (Voorkeuren.Lees(VoorkeurBeeldmodel) is { Length: > 0 } model)
                {
                    start.Environment["IMAGEFUNN_MODEL"] = model;
                }

                if (Voorkeuren.Lees(VoorkeurOntladenNa, -1) is var na and >= 0)
                {
                    start.Environment["IMAGEFUNN_ONTLAAD_NA"] = na.ToString(CultureInfo.InvariantCulture);
                }

                try
                {
                    _gestart = Process.Start(start);
                    App.AppLog.Schrijf("imagefunn", "Beeldservice gestart");
                }
                catch (Exception fout)
                {
                    return Meld($"De beeldservice kon niet gestart worden: {fout.Message}");
                }
            }

            var tot = DateTime.UtcNow + Opstarttijd;
            while (DateTime.UtcNow < tot)
            {
                if (await GezondheidAsync().ConfigureAwait(false) is not null)
                {
                    return null;
                }

                if (_gestart is null or { HasExited: true })
                {
                    return Meld($"De beeldservice stopte tijdens het opstarten. {LaatsteLogregels()}");
                }

                await Task.Delay(500).ConfigureAwait(false);
            }

            return Meld($"De beeldservice is na {Opstarttijd.TotalMinutes:0} minuten nog niet opgestart. {LaatsteLogregels()}");
        }
        finally
        {
            StartSlot.Release();
        }
    }

    private static bool ZelfdeMap(string? a, string b) =>
        a is not null && string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>De versie van de code op schijf: de laatste wijziging van service.py of videomodellen.py (zoals service.py hem meldt).</summary>
    private static long CodeVersie()
    {
        var bestanden = new[] { "service.py", "videomodellen.py" }.Select(n => Path.Combine(ServiceMap, n)).Where(File.Exists).ToList();
        return bestanden.Count == 0 ? -1 : new DateTimeOffset(bestanden.Max(File.GetLastWriteTimeUtc)).ToUnixTimeSeconds();
    }

    private static async Task<(bool Draait, long? Versie, string? Uitvoer)> LeesStandAsync()
    {
        try
        {
            using var korteLijn = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var antwoord = await korteLijn.GetAsync($"{Adres}/health").ConfigureAwait(false);
            if (!antwoord.IsSuccessStatusCode)
            {
                return (false, null, null);
            }

            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync().ConfigureAwait(false));
            var wortel = doc.RootElement;
            long? versie = wortel.TryGetProperty("versie", out var v) && v.TryGetInt64(out var getal) ? getal : null;
            var uitvoer = wortel.TryGetProperty("uitvoer", out var u) ? u.GetString() : null;
            return (true, versie, uitvoer);
        }
        catch (Exception)
        {
            return (false, null, null);
        }
    }

    /// <summary>Stopt het proces dat op poort 5330 luistert (een service van eerder), en wacht tot de poort vrij is.</summary>
    private static async Task StopPoortAsync()
    {
        if (_gestart is { HasExited: false })
        {
            try
            {
                _gestart.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Al gestopt.
            }
        }

        _gestart = null;

        // Niet van ons: vriendelijk vragen om te stoppen (dat doet /ontladen), en anders via netstat het proces zoeken.
        await MaakVideogeheugenVrijAsync().ConfigureAwait(false);
        for (var i = 0; i < 20 && await GezondheidAsync().ConfigureAwait(false) is not null; i++)
        {
            await Task.Delay(250).ConfigureAwait(false);
        }

        if (await GezondheidAsync().ConfigureAwait(false) is null)
        {
            return;
        }

        foreach (var pid in ProcessenOpPoort(5330))
        {
            try
            {
                using var proces = Process.GetProcessById(pid);
                proces.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Al weg of geen rechten.
            }
        }

        await Task.Delay(500).ConfigureAwait(false);
    }

    private static IEnumerable<int> ProcessenOpPoort(int poort)
    {
        try
        {
            using var netstat = Process.Start(new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano -p tcp",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            var uitvoer = netstat?.StandardOutput.ReadToEnd() ?? "";
            netstat?.WaitForExit(5000);
            return uitvoer.Split('\n')
                .Select(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(d => d.Length >= 5 && d[1].EndsWith($":{poort}", StringComparison.Ordinal) && d[3] == "LISTENING")
                .Select(d => int.TryParse(d[4], out var pid) ? pid : 0)
                .Where(pid => pid > 0)
                .Distinct()
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string Meld(string fout)
    {
        App.AppLog.Schrijf("imagefunn", fout);
        return fout;
    }

    private static void BereidLogVoor()
    {
        try
        {
            if (File.Exists(LogPad) && new FileInfo(LogPad).Length > 5_000_000)
            {
                File.Delete(LogPad);
            }

            File.AppendAllText(LogPad, $"{Environment.NewLine}=== Gestart door de Brug: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch (IOException)
        {
            // Het logbestand mag het starten niet in de weg zitten.
        }
    }

    private static string LaatsteLogregels()
    {
        try
        {
            using var stroom = new FileStream(LogPad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var tekst = new StreamReader(stroom).ReadToEnd();
            var begin = tekst.LastIndexOf("=== Gestart door", StringComparison.Ordinal);
            var regels = tekst[Math.Max(begin, 0)..].Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Skip(1).TakeLast(3);
            return $"Zie {LogPad}. Laatste uitvoer: {string.Join(" ", regels)}";
        }
        catch (IOException)
        {
            return $"Zie {LogPad}.";
        }
    }
}

/// <summary>Een fout van de beeldservice met een tekst die zo op het scherm kan.</summary>
public sealed class BeeldserviceFout(string melding, Exception? oorzaak = null) : Exception(melding, oorzaak);
