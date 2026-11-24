using Microsoft.UI.Xaml;

namespace Jabasoft.Brug.App;

/// <summary>
/// Het startpunt van de Brug. Er is maar een venster; alle onderdelen
/// (broker-schermen, modules) worden daarin getoond.
/// </summary>
public partial class App : Application
{
    private Window? _venster;

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
        _venster = new MainWindow();
        _venster.Activate();
    }
}
