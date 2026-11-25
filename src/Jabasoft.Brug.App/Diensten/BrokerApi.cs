using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jabasoft.Brug.App.Diensten;

/// <summary>Eén vraag in de wachtrij van de broker (GET /api/queue).</summary>
public sealed record WachtrijItem(
    long Id,
    string Applicatie,
    string? Onderdeel,
    string Model,
    string Soort,
    string Status,
    int Plek,
    double? Inlezen,
    long Tokens,
    DateTimeOffset Aangemeld,
    DateTimeOffset? Gestart,
    DateTimeOffset? Afgerond,
    string? Fout);

public sealed record TokenWeek(string Week, DateTime Start, DateTime End, long PromptTokens, long CompletionTokens, long TotalTokens, int Calls);

public sealed record TokenModel(string Model, long TotalTokens, int Calls);

public sealed record TokenOnderdeel(string Application, string Onderdeel, long TotalTokens, int Calls);

public sealed record TokenRegel(Guid Id, string Application, DateTimeOffset Timestamp, string? Model, long PromptTokens, long CompletionTokens, long TotalTokens, string? Onderdeel);

public sealed record TokenStatus(bool Available, string? Message);

public sealed record ModellenLijst(bool Success, List<string>? Models, string? ErrorMessage);

/// <summary>Uitkomst van een losse controle: gelukt of niet, hoe lang het duurde, en wat er misging.</summary>
public sealed record Meting<T>(T? Waarde, TimeSpan Duur, string? Fout)
{
    public bool Gelukt => Fout is null;
}

/// <summary>
/// De HTTP-kant van de broker (:5310) voor de schermen van de Brug. Elke
/// aanroep vangt zijn eigen fouten af: een scherm krijgt dan een lege
/// uitkomst met de reden, en hoeft zelf niets af te vangen.
///
/// Instellingen gaan als JSON-boom heen en weer (JsonObject), zodat velden
/// die de Brug nog niet kent bij het opslaan gewoon blijven staan.
/// </summary>
public static class BrokerApi
{
    public const string BasisUrl = "http://localhost:5310";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Task<Meting<bool>> HealthAsync() => MeetAsync(async () =>
    {
        using var antwoord = await Http.GetAsync($"{BasisUrl}/health");
        antwoord.EnsureSuccessStatusCode();
        return true;
    });

    public static Task<Meting<List<WachtrijItem>>> WachtrijAsync() => HaalAsync<List<WachtrijItem>>("/api/queue");

    public static async Task<bool> AnnuleerAsync(long id)
    {
        try
        {
            using var antwoord = await Http.PostAsync($"{BasisUrl}/api/queue/{id}/annuleren", null);
            return antwoord.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static Task<Meting<TokenStatus>> TokenStatusAsync() => HaalAsync<TokenStatus>("/api/usage/status");

    public static Task<Meting<List<TokenWeek>>> TokenWekenAsync() => HaalAsync<List<TokenWeek>>("/api/usage/weeks");

    public static Task<Meting<List<TokenModel>>> TokenModellenAsync() => HaalAsync<List<TokenModel>>("/api/usage/models");

    public static Task<Meting<List<TokenOnderdeel>>> TokenOnderdelenAsync(int dagen) => HaalAsync<List<TokenOnderdeel>>($"/api/usage/onderdelen?dagen={dagen}");

    public static Task<Meting<List<TokenRegel>>> TokenRegelsAsync(string week) => HaalAsync<List<TokenRegel>>($"/api/usage/entries?week={Uri.EscapeDataString(week)}");

    /// <summary>De modellen op de ingestelde AI-server (LM Studio of Ollama).</summary>
    public static Task<Meting<ModellenLijst>> ServerModellenAsync() => HaalAsync<ModellenLijst>("/api/settings/models");

    /// <summary>De modellen op een bepaalde server (provider "LmStudio" of "Ollama"), ook als die niet de actieve is.</summary>
    public static Task<Meting<ModellenLijst>> ModellenVanAsync(string provider, string serverUrl) =>
        HaalAsync<ModellenLijst>($"/api/models?provider={provider}&serverUrl={Uri.EscapeDataString(serverUrl)}");

    public static Task<Meting<JsonObject>> InstellingenAsync() => HaalAsync<JsonObject>("/api/settings");

    public static Task<Meting<JsonObject>> GooseAsync() => HaalAsync<JsonObject>("/api/goose");

    public static Task<Meting<JsonObject>> HermesAsync() => HaalAsync<JsonObject>("/api/hermes");

    public static Task<string?> BewaarInstellingenAsync(JsonObject instellingen) => PutAsync("/api/settings", instellingen);

    public static Task<string?> BewaarGooseAsync(JsonObject instelling) => PutAsync("/api/goose", instelling);

    public static Task<string?> BewaarHermesAsync(JsonObject instelling) => PutAsync("/api/hermes", instelling);

    /// <summary>De beeldservice van ImageFunn (:5330). Draait alleen als er beeldwerk is, dus "niet bereikbaar" is geen fout.</summary>
    public static Task<Meting<bool>> BeeldserviceAsync() => MeetAsync(async () =>
    {
        using var antwoord = await Http.GetAsync("http://127.0.0.1:5330/health");
        antwoord.EnsureSuccessStatusCode();
        return true;
    });

    private static Task<Meting<T>> HaalAsync<T>(string pad) =>
        MeetAsync(async () => await Http.GetFromJsonAsync<T>($"{BasisUrl}{pad}", Json));

    /// <summary>Null als het gelukt is, anders de reden.</summary>
    private static async Task<string?> PutAsync(string pad, JsonObject inhoud)
    {
        try
        {
            using var antwoord = await Http.PutAsJsonAsync($"{BasisUrl}{pad}", inhoud, Json);
            if (antwoord.IsSuccessStatusCode)
            {
                return null;
            }

            var tekst = await antwoord.Content.ReadAsStringAsync();
            return $"{(int)antwoord.StatusCode}: {tekst}";
        }
        catch (Exception fout)
        {
            return fout.Message;
        }
    }

    private static async Task<Meting<T>> MeetAsync<T>(Func<Task<T?>> werk)
    {
        var klok = Stopwatch.StartNew();
        try
        {
            var waarde = await werk();
            return new Meting<T>(waarde, klok.Elapsed, null);
        }
        catch (HttpRequestException fout) when (fout.StatusCode is { } code)
        {
            // De broker antwoordde wel, maar met een fout: dat is iets anders dan onbereikbaar.
            return new Meting<T>(default, klok.Elapsed, $"de broker gaf een fout ({(int)code} {code})");
        }
        catch (Exception fout)
        {
            return new Meting<T>(default, klok.Elapsed, fout switch
            {
                HttpRequestException => "Broker niet bereikbaar",
                TaskCanceledException => "de broker gaf te laat antwoord",
                _ => fout.Message,
            });
        }
    }
}
