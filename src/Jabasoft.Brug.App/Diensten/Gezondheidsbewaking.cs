using System.Globalization;
using System.Text.Json.Nodes;
using Jabasoft.Brug.App.Data;

namespace Jabasoft.Brug.App.Diensten;

public enum Gezondheidsstand { Controleren, Ok, Rust, Fout }

/// <summary>Eén kaart op Broker &gt; Health: titel, toevoeging rechts, stand, statustekst en een paar velden.</summary>
public sealed record Gezondheidskaart(
    string Titel,
    string Rechts,
    Gezondheidsstand Stand,
    string Status,
    (string Label, string Waarde)[] Velden);

/// <summary>
/// Houdt de gezondheid van alles waar de Brug op leunt bij, op de
/// achtergrond: bij het starten en daarna elke 30 seconden, ook als Health
/// dicht is. Open je Health, dan staat het laatste resultaat er meteen.
///
/// Alle controles lopen tegelijk; elke kaart wordt bijgewerkt zodra zijn
/// eigen controle klaar is (event <see cref="Bijgewerkt"/>), zodat een trage
/// controle de rest niet ophoudt. De regelmatige databasecontrole houdt
/// LocalDB bovendien wakker: die zet zichzelf anders na een paar minuten
/// zonder gebruik uit, en dan kost de eerstvolgende verbinding seconden.
/// </summary>
public sealed class Gezondheidsbewaking : IDisposable
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly Lock _slot = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Gezondheidskaart[] _kaarten =
    [
        Wacht("Broker", ":5310"),
        Wacht("Database broker", "JabasoftBroker"),
        Wacht("Database Brug", BrugDatabase.Naam),
        Wacht("AI-server", ""),
        Wacht("Beeldservice", ":5330"),
    ];

    private int _bezig;
    private Task? _lus;

    /// <summary>Een kaart is bijgewerkt. Komt op een achtergronddraad.</summary>
    public event EventHandler? Bijgewerkt;

    /// <summary>Wanneer de laatste ronde controles klaar was (null = nog geen ronde af).</summary>
    public DateTime? LaatstGecontroleerd { get; private set; }

    public void Start() => _lus ??= Task.Run(() => LusAsync(_stop.Token));

    /// <summary>De kaarten zoals ze nu zijn; een kaart die nog nooit gecontroleerd is, staat op Controleren.</summary>
    public Gezondheidskaart[] Kaarten()
    {
        lock (_slot)
        {
            return [.. _kaarten];
        }
    }

    /// <summary>Controleert alles nu, tegelijk. Loopt er al een ronde, dan gebeurt er niets extra.</summary>
    public async Task ControleerAsync()
    {
        if (Interlocked.Exchange(ref _bezig, 1) == 1)
        {
            return;
        }

        try
        {
            await Task.WhenAll(
                ZetAsync(0, BrokerAsync),
                ZetAsync(1, BrokerDatabaseAsync),
                ZetAsync(2, BrugDatabaseAsync),
                ZetAsync(3, AiServerAsync),
                ZetAsync(4, BeeldserviceAsync)).ConfigureAwait(false);

            LaatstGecontroleerd = DateTime.Now;
            Bijgewerkt?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Interlocked.Exchange(ref _bezig, 0);
        }
    }

    /// <summary>De videokaart, live uit de systeemmeter (die meet zelf al elke 2 seconden).</summary>
    public static Gezondheidskaart GpuKaart()
    {
        var meting = App.Meter.Laatste;
        if (meting?.EersteKaart is not { } kaart)
        {
            return new("GPU", "", Gezondheidsstand.Rust, "Geen meting", [("Bron", "nvidia-smi")]);
        }

        var heet = kaart.TemperatuurC >= 85;
        return new("GPU", kaart.Naam, heet ? Gezondheidsstand.Fout : Gezondheidsstand.Ok, heet ? "Te warm" : "In orde",
            [
                ("VRAM", $"{Weergave.GbUitMiB(kaart.VramGebruiktMiB)} / {Weergave.GbUitMiB(kaart.VramTotaalMiB)} GB"),
                ("Belasting", $"{kaart.BelastingProcent} %"),
                ("Temperatuur", $"{kaart.TemperatuurC:0} °C"),
                ("Tweede kaart", meting.Kaarten.Count > 1 ? meting.Kaarten[1].Naam : "nog niet aanwezig"),
            ]);
    }

    private async Task LusAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await ControleerAsync().ConfigureAwait(false);
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

    private async Task ZetAsync(int plek, Func<Task<Gezondheidskaart>> controle)
    {
        Gezondheidskaart kaart;
        try
        {
            kaart = await controle().ConfigureAwait(false);
        }
        catch (Exception fout)
        {
            var oud = Kaarten()[plek];
            kaart = new(oud.Titel, oud.Rechts, Gezondheidsstand.Fout, "Fout", [("Melding", fout.Message)]);
        }

        lock (_slot)
        {
            _kaarten[plek] = kaart;
        }

        Bijgewerkt?.Invoke(this, EventArgs.Empty);
    }

    private static Gezondheidskaart Wacht(string titel, string rechts) =>
        new(titel, rechts, Gezondheidsstand.Controleren, "Controleren…", []);

    private static async Task<Gezondheidskaart> BrokerAsync()
    {
        var broker = await BrokerApi.HealthAsync().ConfigureAwait(false);
        if (!broker.Gelukt)
        {
            return new("Broker", ":5310", Gezondheidsstand.Fout, "Offline", [("Starten", "met de knop rechts in de actiebalk")]);
        }

        var lijst = App.Wachtrij.Actueel();
        return new("Broker", ":5310", Gezondheidsstand.Ok, "Online",
            [
                ("Reactietijd", Ms(broker.Duur)),
                ("Lopend", lijst.Count(i => i.Status == "Bezig").ToString(Nl)),
                ("Wachtend", lijst.Count(i => i.Status == "Wachtend").ToString(Nl)),
            ]);
    }

    private static async Task<Gezondheidskaart> BrokerDatabaseAsync()
    {
        var status = await BrokerApi.TokenStatusAsync().ConfigureAwait(false);
        if (!status.Gelukt)
        {
            return new("Database broker", "JabasoftBroker", Gezondheidsstand.Rust, "Onbekend", [("Reden", "broker offline")]);
        }

        var ok = status.Waarde?.Available == true;
        return new("Database broker", "JabasoftBroker", ok ? Gezondheidsstand.Ok : Gezondheidsstand.Fout, ok ? "Bereikbaar" : "Fout",
            [
                ("Tokentabel", ok ? "aanwezig" : status.Waarde?.Message ?? "—"),
                ("Reactietijd", Ms(status.Duur)),
                ("Instantie", @"(localdb)\MSSQLLocalDB"),
            ]);
    }

    private static async Task<Gezondheidskaart> BrugDatabaseAsync()
    {
        var (gelukt, duur, fout) = await BrugDatabase.ControleerAsync().ConfigureAwait(false);
        return new("Database Brug", BrugDatabase.Naam, gelukt ? Gezondheidsstand.Ok : Gezondheidsstand.Fout, gelukt ? "Bereikbaar" : "Fout",
            [
                ("Reactietijd", gelukt ? Ms(duur) : "—"),
                ("Melding", fout ?? "modeltabel aanwezig"),
            ]);
    }

    private static async Task<Gezondheidskaart> AiServerAsync()
    {
        var instellingenTaak = BrokerApi.InstellingenAsync();
        var modellenTaak = BrokerApi.ServerModellenAsync();
        await Task.WhenAll(instellingenTaak, modellenTaak).ConfigureAwait(false);
        var instellingen = instellingenTaak.Result;
        var modellen = modellenTaak.Result;

        if (!instellingen.Gelukt)
        {
            return new("AI-server", "", Gezondheidsstand.Rust, "Onbekend", [("Reden", "broker offline")]);
        }

        var provider = instellingen.Waarde?["provider"]?.GetValue<string>() ?? "LmStudio";
        var sectie = instellingen.Waarde?[provider == "Ollama" ? "ollama" : "lmStudio"] as JsonObject;
        var ok = modellen.Waarde?.Success == true;

        return new("AI-server", provider == "Ollama" ? "Ollama" : "LM Studio", ok ? Gezondheidsstand.Ok : Gezondheidsstand.Fout, ok ? "Bereikbaar" : "Niet bereikbaar",
            [
                ("Adres", sectie?["url"]?.GetValue<string>() ?? "—"),
                ("Modellen", ok ? (modellen.Waarde!.Models?.Count ?? 0).ToString(Nl) : modellen.Waarde?.ErrorMessage ?? modellen.Fout ?? "—"),
                ("Daily chat", sectie?["chatModel"]?.GetValue<string>() is { Length: > 0 } chat ? chat : "—"),
                ("Reactietijd", Ms(modellen.Duur)),
            ]);
    }

    private static async Task<Gezondheidskaart> BeeldserviceAsync()
    {
        var beeld = await BrokerApi.BeeldserviceAsync().ConfigureAwait(false);
        return beeld.Gelukt
            ? new("Beeldservice", ":5330", Gezondheidsstand.Ok, "Draait", [("Reactietijd", Ms(beeld.Duur))])
            : new("Beeldservice", ":5330", Gezondheidsstand.Rust, "In rust", [("Start", "bij de eerste beeldopdracht")]);
    }

    private static string Ms(TimeSpan duur) => $"{duur.TotalMilliseconds:0} ms";

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
