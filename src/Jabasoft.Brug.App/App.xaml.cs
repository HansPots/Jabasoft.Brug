using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;

namespace Jabasoft.Brug.App;

/// <summary>
/// Het startpunt van de Brug. Er is maar een venster; alle onderdelen
/// (broker-schermen, modules) worden daarin getoond.
/// </summary>
public partial class App : Application
{
    private Window? _venster;
    private BrokerLoglezer? _brokerLezer;

    /// <summary>Wat de Brug zelf doet (eerste infoblok in de header).</summary>
    public static Activiteitenlog AppLog { get; } = new();

    /// <summary>Wat de broker doet, opgehaald via /logs (laatste infoblok in de header en Broker &gt; Logs).</summary>
    public static Activiteitenlog BrokerLog { get; } = new(5000);

    /// <summary>Of de broker bereikbaar is, en hem starten. De Brug stopt hem nooit.</summary>
    public static Brokerbewaking Broker { get; } = new(AppLog);

    /// <summary>CPU, RAM en videokaarten, elke 2 s (systeemblok en footer).</summary>
    public static Systeemmeter Meter { get; } = new();

    /// <summary>De wachtrij van de broker, elke seconde, ook als Nu bezig dicht is.</summary>
    public static Wachtrijvolger Wachtrij { get; } = new();

    /// <summary>De gezondheid van broker, databases, AI-server en beeldservice, elke 30 s op de achtergrond.</summary>
    public static Gezondheidsbewaking Gezondheid { get; } = new();

    public App()
    {
        InitializeComponent();

        // Een fout die niemand afvangt: vastleggen in fouten.log (en in het
        // activiteitenblok) en doorgaan, in plaats van stil te verdwijnen.
        UnhandledException += (_, e) =>
        {
            Foutenlog.Schrijf("Onafgevangen fout", e.Exception);
            AppLog.Schrijf("brug", $"Fout: {e.Exception.GetType().Name}: {e.Exception.Message} (zie fouten.log)");
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Foutenlog.Schrijf("Onafgevangen fout in een taak", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Foutenlog.Schrijf("Fatale fout", e.ExceptionObject as Exception);
    }

    /// <summary>De versie van de Brug, zoals in het projectbestand (Version).</summary>
    public static string Versie
    {
        get
        {
            var v = typeof(App).Assembly.GetName().Version;
            return v is null ? "v?" : $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Schrijf("brug", $"Jabasoft.Brug {Versie} gestart");

        _brokerLezer = new BrokerLoglezer(BrokerLog, Broker);
        _brokerLezer.Start();

        Meter.Start();
        Wachtrij.Start();
        Gezondheid.Start();

        // Eigen database (JabasoftBrug) op de achtergrond klaarzetten; de
        // schermen werken ook zonder, met de ingebouwde modelbeschrijvingen.
        _ = Data.BrugDatabase.VoorbereidenAsync();

        // Voorverwarmen: de eerste aanroep via BrokerApi (verbinding, JSON)
        // kost het meeste tijd; doe die nu, dan opent Tokens daarna vlot.
        _ = Task.Run(Diensten.BrokerApi.TokenWekenAsync);

        _venster = new MainWindow();
        _venster.Closed += (_, _) =>
        {
            _brokerLezer?.Dispose();
            Meter.Dispose();
            Wachtrij.Dispose();
            Gezondheid.Dispose();
        };
        _venster.Activate();
    }
}
