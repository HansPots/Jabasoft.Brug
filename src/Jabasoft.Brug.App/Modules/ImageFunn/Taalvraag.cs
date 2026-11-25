using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jabasoft.Brug.App.Diensten;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// Een vraag aan een taalmodel via de broker (/api/chat/stream), voor
/// ImageFunn: omschrijvingen vertalen naar een Engelse prompt, een beeld laten
/// lezen, een verhaal in scènes laten schrijven. Altijd via de stroomroute:
/// dat is de enige die afbeeldingen aan het model meegeeft. De broker telt de
/// tokens (onder "ImageFunn" met het onderdeel erbij).
///
/// Welk model: het model dat in de broker voor die taak staat (chatModel of
/// beeldModel), op de actieve server.
/// </summary>
public static partial class Taalvraag
{
    public const string Applicatie = "ImageFunn";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Een bericht: rol "system" of "user", tekst, en eventueel afbeeldingen (base64).</summary>
    public sealed record Bericht(string Role, string Content, IReadOnlyList<string>? Images = null);

    /// <summary>
    /// Stelt de vraag aan het model voor <paramref name="taak"/> ("chatModel" of "beeldModel"; is beeldModel leeg,
    /// dan het chatmodel). Geeft het antwoord zonder denkblokken, of een fout die zo op het scherm kan.
    /// </summary>
    public static async Task<(string? Antwoord, string? Fout, string Model)> VraagAsync(
        string taak,
        IReadOnlyList<Bericht> berichten,
        string onderdeel,
        double temperatuur = 0.4,
        TimeSpan? limiet = null,
        CancellationToken stop = default)
    {
        // Om en om: de beeldservice en het taalmodel passen samen niet in het werkgeheugen.
        await Beeldservice.MaakRuimteVoorTaalmodelAsync().ConfigureAwait(false);

        var instellingen = await BrokerApi.InstellingenAsync().ConfigureAwait(false);
        if (instellingen.Waarde is not { } geheel)
        {
            return (null, $"De broker is niet bereikbaar ({instellingen.Fout}).", "");
        }

        var provider = geheel["provider"]?.GetValue<string>() ?? "LmStudio";
        var sectie = geheel[provider == "Ollama" ? "ollama" : "lmStudio"] as JsonObject;
        var url = sectie?["url"]?.GetValue<string>() ?? "";
        var model = sectie?[taak]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(model) && taak != "chatModel")
        {
            model = sectie?["chatModel"]?.GetValue<string>();
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return (null, "Er is geen model voor deze taak ingesteld (Broker › Instellingen).", "");
        }

        if (taak == "beeldModel" && model.Contains("embed", StringComparison.OrdinalIgnoreCase))
        {
            return (null, $"{model} is een embeddingmodel en kan geen beelden lezen. Kies een beeldmodel bij Broker › Instellingen.", model);
        }

        using var tijd = CancellationTokenSource.CreateLinkedTokenSource(stop);
        tijd.CancelAfter(limiet ?? TimeSpan.FromMinutes(5));

        var tekst = new StringBuilder();
        try
        {
            using var vraag = new HttpRequestMessage(HttpMethod.Post, $"{BrokerApi.BasisUrl}/api/chat/stream")
            {
                Content = JsonContent.Create(new
                {
                    provider,
                    serverUrl = url,
                    model,
                    messages = berichten,
                    application = Applicatie,
                    temperature = temperatuur,
                    onderdeel,
                }, options: Json),
            };

            using var antwoord = await Http.SendAsync(vraag, HttpCompletionOption.ResponseHeadersRead, tijd.Token).ConfigureAwait(false);
            if (!antwoord.IsSuccessStatusCode)
            {
                return (null, $"De broker gaf {(int)antwoord.StatusCode}.", model);
            }

            using var lezer = new StreamReader(await antwoord.Content.ReadAsStreamAsync(tijd.Token).ConfigureAwait(false));
            while (await lezer.ReadLineAsync(tijd.Token).ConfigureAwait(false) is { } regel)
            {
                if (regel.Length == 0)
                {
                    continue;
                }

                var stukje = JsonNode.Parse(regel);
                if (stukje?["errorMessage"]?.GetValue<string>() is { Length: > 0 } fout)
                {
                    return (null, fout, model);
                }

                if (stukje?["thinking"]?.GetValue<bool>() != true && stukje?["text"]?.GetValue<string>() is { } stuk)
                {
                    tekst.Append(stuk);
                }

                if (stukje?["done"]?.GetValue<bool>() == true)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return (null, "Het model deed er te lang over.", model);
        }
        catch (Exception fout) when (fout is HttpRequestException or IOException or JsonException)
        {
            return (null, $"Kon de broker niet bereiken: {fout.Message}", model);
        }

        var schoon = ZonderDenken(tekst.ToString());
        return schoon.Length == 0 ? (null, "Het model gaf geen antwoord.", model) : (schoon, null, model);
    }

    /// <summary>Haalt &lt;think&gt;…&lt;/think&gt;-blokken weg (ook een losse sluittag zonder opening).</summary>
    public static string ZonderDenken(string antwoord)
    {
        var schoon = HeleBlokken().Replace(antwoord, string.Empty);
        if (!schoon.Contains("<think", StringComparison.OrdinalIgnoreCase))
        {
            schoon = LosseSluittag().Replace(schoon, string.Empty);
        }

        schoon = schoon.Trim();
        return schoon.Length == 0 ? antwoord.Trim() : schoon;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeleBlokken();

    [GeneratedRegex(@"^.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LosseSluittag();
}
