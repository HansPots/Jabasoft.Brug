using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Storage;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// ImageFunn &gt; Beeld naar video en Tekst naar video: één scherm in twee
/// varianten. Met een bronbeeld laat het videomodel dat beeld bewegen; zonder
/// maakt het een video uit alleen de tekst.
///
/// Links de opdracht, rechts de video (met tijdens het maken een voorproefje
/// van het middelste beeld, bij Wan), onderaan wat eerder gemaakt is.
/// Overgenomen uit de oude ImageFunn-app (BeeldNaarVideo, TekstNaarVideo).
/// </summary>
public sealed class VideoScherm : UserControl, IVerversbaar, IOntvangtBeeld
{
    private static readonly TimeSpan Peilen = TimeSpan.FromSeconds(1.5);

    private static readonly (string Tag, string Naam)[] FormatenBeeld =
    [
        ("384x384", "Snel (klein)"),
        ("640x480", "Normaal"),
        ("768x512", "Scherp (trager)"),
    ];

    private static readonly (string Tag, string Naam)[] FormatenTekst =
    [
        ("704x480", "Liggend 704 × 480"),
        ("480x704", "Staand 480 × 704"),
        ("512x512", "Vierkant 512 × 512"),
        ("384x384", "Klein vierkant 384 × 384 (sneller)"),
    ];

    private readonly bool _vanBeeld;
    private readonly string _scherm;

    private readonly Image _bron = new() { Stretch = Stretch.Uniform, MaxHeight = 260 };
    private readonly TextBlock _bronTekst;
    private string? _bronPad;

    private readonly TextBox _omschrijving;
    private readonly ComboBox _model;
    private readonly ComboBox _duur;
    private readonly ComboBox _formaat;
    private readonly CheckBox _verbeter;
    private readonly Button _maak;
    private readonly Button _letterlijk;
    private readonly Button _stop;
    private readonly ProgressBar _voortgang = Onderdelen.Voortgang();
    private readonly TextBlock _status = Bouw.Melding("Klaar voor een opdracht.");

    private readonly MediaPlayerElement _speler;
    private readonly Image _voorproef = new() { Stretch = Stretch.Uniform, MaxHeight = 560, Visibility = Visibility.Collapsed };
    private readonly TextBlock _geenVideo;
    private readonly TextBlock _videoInfo = Bouw.Melding("");
    private readonly Button _inMap;
    private readonly Button _verwijder;

    private readonly StackPanel _galerij = new() { Spacing = 6 };
    private readonly TextBlock _galerijMelding = Bouw.Melding("Ophalen…");
    private string? _galerijHandtekening;

    private BeeldJobStatus? _getoond;
    private string? _lopend;

    public VideoScherm(bool vanBeeld)
    {
        _vanBeeld = vanBeeld;
        _scherm = vanBeeld ? "beeldnaarvideo" : "tekstnaarvideo";

        // ---- opdracht
        var opdracht = new StackPanel { Spacing = 16 };
        _bronTekst = Bouw.Melding("Sleep een beeld hierheen, of klik om er een te kiezen.");
        if (vanBeeld)
        {
            var vlak = new Grid { MinHeight = 200 };
            vlak.Children.Add(_bronTekst);
            vlak.Children.Add(_bron);
            var kader = new Border
            {
                BorderBrush = Bouw.Kleur("BorderBrush"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10, 10, 10, 10),
                Padding = new Thickness(10),
                Background = Bouw.Kleur("BackgroundBrush"),
                Child = vlak,
            };
            Onderdelen.MaakSleepvlak(kader, OntvangAsync);
            opdracht.Children.Add(kader);
        }

        _omschrijving = new TextBox
        {
            Header = vanBeeld ? "Wat beweegt er (en hoe beweegt de camera)?" : "Wat gebeurt er in de scène?",
            PlaceholderText = vanBeeld
                ? "Bijvoorbeeld: de heks vliegt langzaam naar rechts, de wolken drijven voorbij en de camera zoomt rustig in"
                : "Bijvoorbeeld: een heks op een bezemsteel vliegt langs een grote volle maan, de camera volgt haar langzaam",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 130,
        };
        _model = Onderdelen.Keuze("Videomodel", Onderdelen.Videomodellen, "wan");
        _duur = Onderdelen.Keuze("Duur", Onderdelen.Duren, "97");
        _formaat = Onderdelen.Keuze(vanBeeld ? "Kwaliteit" : "Formaat", vanBeeld ? FormatenBeeld : FormatenTekst, vanBeeld ? "640x480" : "704x480");
        _verbeter = Onderdelen.Verbetervinkje(_scherm);

        _maak = Bouw.Knop("Maak video", async (_, _) => await MaakAsync(letterlijk: false));
        _letterlijk = Bouw.Knop("Letterlijk", async (_, _) => await MaakAsync(letterlijk: true), "AccentBrushLight");
        ToolTipService.SetToolTip(_letterlijk, "Je tekst gaat precies zo naar het model, niet vertaald of verbeterd. Schrijf dan zelf in het Engels.");
        _stop = Bouw.Knop("Stop", async (_, _) => await StopAsync(), "TextMutedBrush");
        _stop.IsEnabled = false;

        var keuzes = Bouw.Kolommen(3, 16);
        Bouw.Zet(keuzes, _model, 0, 0);
        Bouw.Zet(keuzes, _duur, 0, 1);
        Bouw.Zet(keuzes, _formaat, 0, 2);
        foreach (var deel in new UIElement[] { _omschrijving, keuzes, _verbeter, Onderdelen.Knoppen(_maak, _letterlijk, _stop), _voortgang, _status })
        {
            opdracht.Children.Add(deel);
        }

        // ---- video
        _speler = new MediaPlayerElement { AreTransportControlsEnabled = true, AutoPlay = true, MaxHeight = 560, Visibility = Visibility.Collapsed };
        _geenVideo = Bouw.Melding("Nog geen video. Maak er een, of kies er een uit de lijst hieronder.");
        _inMap = Bouw.Knop("Toon in map", (_, _) => Onderdelen.ToonInMap(_getoond?.Bestand));
        _verwijder = Bouw.Knop("Verwijderen", async (_, _) => await VerwijderAsync(), "TextMutedBrush");
        _inMap.IsEnabled = _verwijder.IsEnabled = false;

        var vlakVideo = new Grid { MinHeight = 420 };
        vlakVideo.Children.Add(_geenVideo);
        vlakVideo.Children.Add(_speler);
        vlakVideo.Children.Add(_voorproef);
        var video = new StackPanel { Spacing = 12, Children = { vlakVideo, _videoInfo, Onderdelen.Knoppen(_inMap, _verwijder) } };

        var boven = new Grid { ColumnSpacing = 24 };
        boven.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(520) });
        boven.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rechts = Bouw.Paneel("Video", video);
        Grid.SetColumn(rechts, 1);
        boven.Children.Add(Bouw.Paneel("Opdracht", opdracht));
        boven.Children.Add(rechts);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(32, 24, 32, 32),
                Spacing = 24,
                Children =
                {
                    boven,
                    Bouw.Paneel("Eerder gemaakt", new StackPanel { Spacing = 8, Children = { _galerijMelding, _galerij } }),
                },
            },
        };

        Loaded += async (_, _) => await VerversAsync();
        Unloaded += (_, _) => _speler.MediaPlayer?.Pause();
    }

    /// <summary>Een beeld om te laten bewegen, gekozen, gesleept of doorgegeven door een ander scherm.</summary>
    public async Task OntvangAsync(string pad)
    {
        if (!_vanBeeld)
        {
            return;
        }

        var beeld = await Onderdelen.LaadBeeldAsync(pad);
        if (beeld is null)
        {
            _status.Text = "Dat beeld kon niet geopend worden (png, jpg, bmp of webp).";
            return;
        }

        _bronPad = pad;
        _bron.Source = beeld;
        _bronTekst.Visibility = Visibility.Collapsed;
        _status.Text = $"{Path.GetFileName(pad)} - {beeld.PixelWidth} × {beeld.PixelHeight}";
    }

    public async Task VerversAsync()
    {
        try
        {
            var lijst = (await Beeldservice.LijstAsync("video"))
                .Where(t => (t.BronBeeld is not null) == _vanBeeld && t.Bestand is not null && File.Exists(t.Bestand))
                .ToList();
            var handtekening = string.Join(",", lijst.Select(t => t.JobId));
            if (handtekening == _galerijHandtekening)
            {
                return;
            }

            _galerijHandtekening = handtekening;
            _galerijMelding.Text = lijst.Count == 0 ? "Nog niets gemaakt." : $"{lijst.Count} video's, nieuwste bovenaan. Klik om af te spelen.";
            _galerij.Children.Clear();
            foreach (var taak in lijst)
            {
                var knop = new HyperlinkButton
                {
                    Content = $"{taak.GemaaktOp.LocalDateTime:dd-MM HH:mm}  ·  {Onderdelen.Kort(taak.Prompt, 110)}",
                    Foreground = Bouw.Kleur("TextPrimaryBrush"),
                    Padding = new Thickness(0),
                };
                ToolTipService.SetToolTip(knop, taak.Prompt);
                knop.Click += async (_, _) => await SpeelAsync(taak);
                _galerij.Children.Add(knop);
            }
        }
        catch (Exception fout)
        {
            _galerijMelding.Text = $"De lijst kon niet geladen worden. {Beeldservice.Foutmelding(fout)}";
        }
    }

    // ---------------------------------------------------------------- maken

    private async Task MaakAsync(bool letterlijk)
    {
        var tekst = _omschrijving.Text.Trim();
        if (_vanBeeld && _bronPad is null)
        {
            _status.Text = "Kies eerst een beeld om te laten bewegen.";
            return;
        }

        if (!_vanBeeld && tekst.Length == 0)
        {
            _status.Text = "Beschrijf eerst de scène.";
            return;
        }

        var formaat = Onderdelen.Gekozen(_formaat).Split('x');
        ZetBezig(true);
        _status.Text = "Opdracht starten…";
        string? opmerking = null;
        try
        {
            // Het videomodel leest alleen Engels. Lukt het omschrijven niet, dan gaat de eigen tekst mee.
            if (!letterlijk && _verbeter.IsChecked == true && tekst.Length > 0)
            {
                _status.Text = _vanBeeld ? "Beweging vertalen en verbeteren…" : "Scène vertalen en verbeteren…";
                var (verbeterd, fout) = await Promptschrijver.VerbeterAsync(tekst, _vanBeeld ? Promptsoort.Beweging : Promptsoort.Scene);
                if (verbeterd is not null)
                {
                    tekst = verbeterd;
                }
                else
                {
                    opmerking = $"Verbeteren mislukt ({fout}), je eigen tekst is gebruikt. ";
                }
            }

            _lopend = await Beeldservice.StartAsync(new BeeldOpdracht
            {
                Soort = "video",
                BronBeeld = _vanBeeld ? _bronPad : null,
                Prompt = tekst,
                Frames = int.Parse(Onderdelen.Gekozen(_duur), Bouw.Nl),
                Videomodel = Onderdelen.Gekozen(_model),
                Breedte = int.Parse(formaat[0], Bouw.Nl),
                Hoogte = int.Parse(formaat[1], Bouw.Nl),
            }, new Progress<string>(t => _status.Text = opmerking + t));
            _stop.IsEnabled = true;
            App.AppLog.Schrijf("imagefunn", $"Video maken ({Onderdelen.Gekozen(_model)}, {(_vanBeeld ? "vanaf beeld" : "uit tekst")})");

            var voorproefNr = 0;
            while (true)
            {
                var stand = await Beeldservice.StatusAsync(_lopend);
                _status.Text = opmerking + stand.Stap;
                _voortgang.Value = stand.Voortgang;
                if (stand.VoorproefNr != voorproefNr && await Onderdelen.LaadBeeldAsync(stand.Voorproef) is { } voorproef)
                {
                    voorproefNr = stand.VoorproefNr;
                    _voorproef.Source = voorproef;
                    _voorproef.Visibility = Visibility.Visible;
                    _geenVideo.Visibility = Visibility.Collapsed;
                }

                if (stand.IsKlaar)
                {
                    await SpeelAsync(stand);
                    _status.Text = $"{opmerking}Klaar - seed {stand.Seed}.";
                    App.AppLog.Schrijf("imagefunn", "Video klaar");
                    await VerversAsync();
                    break;
                }

                if (stand.IsMislukt)
                {
                    _status.Text = opmerking + stand.Eindmelding;
                    App.AppLog.Schrijf("imagefunn", $"Video: {stand.Eindmelding}");
                    break;
                }

                await Task.Delay(Peilen);
            }
        }
        catch (Exception fout)
        {
            _status.Text = opmerking + Beeldservice.Foutmelding(fout);
        }
        finally
        {
            _lopend = null;
            ZetBezig(false);
            _voorproef.Visibility = Visibility.Collapsed;
            if (_getoond is null)
            {
                _geenVideo.Visibility = Visibility.Visible;
            }
        }
    }

    private async Task StopAsync()
    {
        if (_lopend is null)
        {
            return;
        }

        _stop.IsEnabled = false;
        try
        {
            await Beeldservice.StopAsync(_lopend);
            _status.Text = "Stoppen na de huidige stap…";
        }
        catch (Exception fout)
        {
            _status.Text = $"Stoppen mislukt: {Beeldservice.Foutmelding(fout)}";
        }
    }

    private void ZetBezig(bool bezig)
    {
        _maak.IsEnabled = _letterlijk.IsEnabled = !bezig;
        _stop.IsEnabled = false;
        _voortgang.Value = 0;
        _voortgang.Visibility = bezig ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- afspelen en verwijderen

    private async Task SpeelAsync(BeeldJobStatus taak)
    {
        if (taak.Bestand is null || !File.Exists(taak.Bestand))
        {
            return;
        }

        try
        {
            var bestand = await StorageFile.GetFileFromPathAsync(taak.Bestand);
            _speler.Source = MediaSource.CreateFromStorageFile(bestand);
            if (_speler.MediaPlayer is { } speler)
            {
                speler.IsLoopingEnabled = true; // een korte animatie herhaalt zichzelf
            }
        }
        catch (Exception fout)
        {
            _status.Text = $"De video kon niet afgespeeld worden: {fout.Message}";
            return;
        }

        _getoond = taak;
        _speler.Visibility = Visibility.Visible;
        _geenVideo.Visibility = Visibility.Collapsed;
        _videoInfo.Text = $"{taak.Breedte} × {taak.Hoogte} · {taak.Videomodel} · seed {taak.Seed}\n{taak.Prompt}";
        _inMap.IsEnabled = _verwijder.IsEnabled = true;
    }

    private async Task VerwijderAsync()
    {
        if (_getoond is not { } taak
            || !await Onderdelen.BevestigAsync(XamlRoot, "Deze video verwijderen?", $"{Onderdelen.Kort(taak.Prompt, 120)}\n\nDe video wordt definitief verwijderd; dit kan niet ongedaan gemaakt worden."))
        {
            return;
        }

        // De speler houdt het bestand open: eerst loslaten.
        _speler.MediaPlayer?.Pause();
        _speler.Source = null;
        await Task.Delay(300);

        try
        {
            await Beeldservice.VerwijderAsync(taak.JobId);
        }
        catch (Exception fout)
        {
            _status.Text = $"Verwijderen mislukt: {Beeldservice.Foutmelding(fout)}";
            return;
        }

        _getoond = null;
        _speler.Visibility = Visibility.Collapsed;
        _geenVideo.Visibility = Visibility.Visible;
        _videoInfo.Text = "";
        _inMap.IsEnabled = _verwijder.IsEnabled = false;
        _status.Text = "Video verwijderd.";
        await VerversAsync();
    }
}
