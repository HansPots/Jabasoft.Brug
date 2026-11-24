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

    /// <summary>Wat de broker doet, opgehaald via /logs (laatste infoblok in de header).</summary>
    public static Activiteitenlog BrokerLog { get; } = new();

    public App()
    {
        InitializeComponent();
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

        _brokerLezer = new BrokerLoglezer(BrokerLog);
        _brokerLezer.Start();

        _venster = new MainWindow();
        _venster.Closed += (_, _) => _brokerLezer?.Dispose();
        _venster.Activate();
    }
}
