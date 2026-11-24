using System.Net.Http.Json;
using System.Text.Json;

namespace Jabasoft.Brug.App.Diensten;

/// <summary>
/// Haalt elke twee seconden de nieuwe regels van de broker op
/// (GET /logs?since=N) en zet ze in een <see cref="Activiteitenlog"/>.
///
/// Dit is het bestaande endpoint van de broker; in fase 4 komt er een
/// live stroom voor in de plaats. Draait de broker niet, dan staat er een
/// regel van, en een regel als hij weer terug is - niet elke twee seconden.
/// </summary>
public sealed class BrokerLoglezer(Activiteitenlog doel, string basisUrl = "http://localhost:5310") : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly CancellationTokenSource _stop = new();
    private long _laatste;
    private bool? _bereikbaar;
    private Task? _lus;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    public void Start() => _lus ??= Task.Run(() => LusAsync(_stop.Token));

    private async Task LusAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var regels = await _http
                    .GetFromJsonAsync<List<BrokerRegel>>($"{basisUrl}/logs?since={_laatste}", Json, stop)
                    .ConfigureAwait(false);

                MeldBereikbaar(true);
                foreach (var regel in regels ?? [])
                {
                    _laatste = Math.Max(_laatste, regel.Sequence);
                    doel.Voeg(new Activiteit(0, regel.Time, regel.Source, regel.Message));
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                MeldBereikbaar(false);
            }

            try
            {
                await Task.Delay(Interval, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void MeldBereikbaar(bool nu)
    {
        if (_bereikbaar == nu)
        {
            return;
        }

        _bereikbaar = nu;
        doel.Schrijf("brug", nu ? "Verbonden met de broker (:5310)" : "Broker niet bereikbaar op :5310");
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _http.Dispose();
    }

    /// <summary>Zoals de broker een regel teruggeeft (ActivityEntry in Jabasoft.Base).</summary>
    private sealed record BrokerRegel(long Sequence, DateTimeOffset Time, string Source, string Message);
}
