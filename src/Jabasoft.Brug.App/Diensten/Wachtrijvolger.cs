namespace Jabasoft.Brug.App.Diensten;

/// <summary>
/// Volgt de wachtrij van de broker (GET /api/queue) elke seconde, ook als
/// Nu bezig niet openstaat. De broker houdt afgeronde vragen maar 30
/// seconden vast; de volger onthoudt ze <see cref="KlaarBewaren"/>, zodat je
/// bij terugkomst nog ziet wat er in de tussentijd klaar is gekomen.
/// </summary>
public sealed class Wachtrijvolger : IDisposable
{
    public static readonly TimeSpan KlaarBewaren = TimeSpan.FromMinutes(5);

    private readonly Lock _slot = new();
    private readonly Dictionary<long, WachtrijItem> _klaar = [];
    private readonly CancellationTokenSource _stop = new();
    private List<WachtrijItem> _actueel = [];
    private Task? _lus;

    /// <summary>Of de laatste vraag aan de broker lukte, en zo niet waarom.</summary>
    public string? Fout { get; private set; } = "Nog niet opgehaald";

    public void Start() => _lus ??= Task.Run(() => LusAsync(_stop.Token));

    /// <summary>Wat nu loopt of wacht, zoals de broker het het laatst meldde.</summary>
    public List<WachtrijItem> Actueel()
    {
        lock (_slot)
        {
            return [.. _actueel];
        }
    }

    /// <summary>Wat de afgelopen 5 minuten klaar is gekomen, nieuwste eerst.</summary>
    public List<WachtrijItem> Klaar()
    {
        lock (_slot)
        {
            return [.. _klaar.Values.OrderByDescending(i => i.Afgerond ?? i.Aangemeld)];
        }
    }

    private async Task LusAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var meting = await BrokerApi.WachtrijAsync().ConfigureAwait(false);
            lock (_slot)
            {
                Fout = meting.Fout;
                if (meting.Waarde is { } items)
                {
                    _actueel = [.. items.Where(i => i.Status is "Bezig" or "Wachtend")];
                    foreach (var item in items.Where(i => i.Status is "Klaar" or "Mislukt" or "Geannuleerd"))
                    {
                        _klaar[item.Id] = item;
                    }
                }

                var grens = DateTimeOffset.Now - KlaarBewaren;
                foreach (var oud in _klaar.Values.Where(i => (i.Afgerond ?? i.Aangemeld) < grens).Select(i => i.Id).ToList())
                {
                    _klaar.Remove(oud);
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
