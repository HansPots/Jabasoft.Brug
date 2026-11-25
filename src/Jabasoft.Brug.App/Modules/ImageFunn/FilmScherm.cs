using System.Diagnostics;
using System.Text.Json;
using Jabasoft.Brug.App.Layout;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Line = Microsoft.UI.Xaml.Shapes.Line;
using Windows.Media.Core;
using Windows.Storage;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// ImageFunn &gt; Film maken: een verhaal in scènes, met (optioneel) een cast, eerst de sleutelbeelden controleren en
/// dan de film. Vijf stappen bovenin (Verhaal, Scènes, Cast, Beelden, Film); onderin altijd de tijdlijn en de
/// filmknoppen. De hele film gaat als één opdracht naar de beeldservice; die meldt per scène de stand terug, en
/// scènes die klaar zijn worden bij een volgende run hergebruikt.
///
/// Overgenomen uit de oude ImageFunn-app (FilmMaker). Dit bestand: stappen, verhaal, scènes, tijdlijn, film en
/// verhaal bewaren. FilmScherm.Cast.cs: de cast. FilmScherm.Beelden.cs: sleutelbeelden maken en beoordelen.
/// </summary>
public sealed partial class FilmScherm : UserControl, IVerversbaar, IOntvangtBeeld
{
    private static readonly TimeSpan Peilen = TimeSpan.FromSeconds(2);

    /// <summary>Grove schatting voor de melding vooraf: een scène kost ongeveer twee minuten (met de snelle Wan minder).</summary>
    private const int MinutenPerScene = 2;

    private static readonly JsonSerializerOptions JsonOpties = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static readonly string[] StapTitels = ["1  Verhaal", "2  Scènes", "3  Cast (optioneel)", "4  Beelden", "5  Film"];

    private List<FilmScene> _scenes = [NieuweScene()];
    private int _gekozen;
    private bool _vullen;
    private bool _bezig;
    private string? _jobId;
    private string? _filmBestand;
    private int _tab;

    // ---- stappen
    private readonly Button[] _stapKnoppen = new Button[5];
    private readonly UIElement[] _tabs = new UIElement[5];

    // ---- tab 1: verhaal
    private readonly ComboBox _lengte = Onderdelen.Keuze("Lengte", [("30", "Ongeveer 30 sec"), ("60", "Ongeveer 1 min"), ("120", "Ongeveer 2 min"), ("180", "Ongeveer 3 min")], "60");
    private readonly ComboBox _formaat = Onderdelen.Keuze("Formaat", [("704x480", "Liggend 704 × 480"), ("480x704", "Staand 480 × 704"), ("544x544", "Vierkant 544 × 544"), ("512x352", "Snel 512 × 352")], "704x480");
    private readonly ComboBox _beeldstijl = Onderdelen.Keuze("Beeldstijl", Beeldstijl.Alle.Select(s => (s.Tag, s.Naam)), "realistisch");
    private readonly ComboBox _modus = Onderdelen.Keuze("Aansluiting", [("sleutelbeelden", "Eigen beeld per scène"), ("doorlopend", "Doorlopend (volgt op het einde van de vorige)")], "sleutelbeelden");
    private readonly ComboBox _overgang = Onderdelen.Keuze("Overgang", [("zacht", "Zachte overgang"), ("hard", "Harde overgang")], "zacht");
    private readonly ComboBox _beeldmodel = Onderdelen.Keuze("Beeldmodel", TekstNaarBeeld.Beeldmodellen, Voorkeuren.Lees(Beeldservice.VoorkeurBeeldmodel, "realvis"));
    private readonly ComboBox _videomodel = Onderdelen.Keuze("Videomodel", Onderdelen.Videomodellen, "wan");
    private readonly ComboBox _consistentie = Onderdelen.Keuze("Personages gelijk houden", [("tekst", "Alleen tekst"), ("eerste", "Zelfde als scène 1"), ("eigen", "Eigen voorbeeldbeeld")], "eerste");
    private readonly ComboBox _gelijkenis = Onderdelen.Keuze("Gelijkenis", [("0.3", "Licht"), ("0.45", "Normaal"), ("0.6", "Sterk")], "0.45");
    private readonly CheckBox _controleer = new() { Content = "Sleutelbeelden eerst laten controleren voor het animeren", IsChecked = true };
    private readonly TextBox _idee = new()
    {
        Header = "Wat is het verhaal?",
        PlaceholderText = "Bijvoorbeeld: een jonge heks vliegt in het maanlicht naar een kasteel op een berg, landt op de toren en kijkt uit over het dal",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 140,
    };
    private readonly TextBox _stijl = new()
    {
        Header = "Stijl en personages (blijft in elke scène gelijk, Engels)",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 110,
    };
    private readonly StackPanel _voorbeeldRij = new() { Orientation = Orientation.Horizontal, Spacing = 12, Visibility = Visibility.Collapsed };
    private readonly TextBlock _voorbeeldTekst = Bouw.Melding("nog geen gekozen");
    private Button _tijdlijnKnop = null!;
    private string? _eigenVoorbeeld;

    // ---- tab 2: scènes
    private readonly TextBlock _sceneKop = Bouw.Kop("SCÈNE");
    private readonly TextBox _titel = new() { Header = "Titel" };
    private readonly ComboBox _duur = Onderdelen.Keuze("Duur", Enumerable.Range(2, 5).Select(s => (s.ToString(Bouw.Nl), $"{s} sec")), "4");
    private readonly TextBox _gebeurt = new() { Header = "Wat gebeurt er?", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 100 };
    private readonly CheckBox _engelsToon = new() { Content = "Toon de Engelse tekst voor het model" };
    private readonly TextBox _beeldTekst = new() { Header = "Eerste beeld (leeg = automatisch afleiden)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70 };
    private readonly TextBox _bewegingTekst = new() { Header = "Beweging en camera", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70 };
    private readonly StackPanel _engels = new() { Spacing = 12, Visibility = Visibility.Collapsed };
    private readonly StackPanel _sceneEditor = new() { Spacing = 14 };

    // ---- onderin: tijdlijn en film
    private readonly TextBlock _tijdlijnKop = Bouw.Melding("");
    private readonly Canvas _tijdlijn = new() { Height = 96 };
    private readonly ScrollViewer _tijdlijnScroll;
    private readonly CheckBox _letterlijk = new() { Content = "Letterlijk (niet vertalen, geen stijltekst)" };
    private readonly Button _maak;
    private readonly Button _stop;
    private readonly Button _speelKlaar;
    private readonly Button _toonFilm;
    private readonly Button _inMap;
    private readonly StackPanel _galerij = new() { Spacing = 6 };
    private readonly TextBlock _galerijMelding = Bouw.Melding("Ophalen…");
    private string? _galerijHandtekening;

    // ---- tab 5: film
    private readonly MediaPlayerElement _speler = new() { AreTransportControlsEnabled = true, AutoPlay = true, MaxHeight = 560, Visibility = Visibility.Collapsed };
    private readonly Image _sleutelbeeld = new() { Stretch = Stretch.Uniform, MaxHeight = 560, Visibility = Visibility.Collapsed };
    private readonly TextBlock _spelerLabel = Bouw.Melding("");
    private readonly TextBlock _leeg = Bouw.Melding("Hier verschijnt je film.");
    private readonly TextBlock _fase = Bouw.Melding("1 Sleutelbeelden  ›  2 Scènes animeren  ›  3 Samenvoegen");
    private readonly TextBlock _kop = Bouw.Kop("KLAAR OM TE BEGINNEN");
    private readonly ProgressBar _voortgang = new() { Maximum = 1, Height = 14, Foreground = Bouw.Kleur("AccentBrush") };
    private readonly TextBlock _status = Bouw.Melding("");
    private readonly TextBlock _tijd = Bouw.Melding("");

    private string? _voorbeeldPad;
    private HashSet<int> _klaarGezien = [];
    private bool _clipInRun;
    private bool _handmatig;
    private bool _afspeellijst;
    private int _lijstIndex = -1;

    public FilmScherm()
    {
        // ---- stappenbalk
        var stappen = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        for (var i = 0; i < 5; i++)
        {
            var nr = i;
            _stapKnoppen[i] = Bouw.Knop(StapTitels[i], (_, _) => ToonTab(nr), "AccentBrushLight");
            stappen.Children.Add(_stapKnoppen[i]);
        }

        _tabs[0] = MaakVerhaalTab();
        _tabs[1] = MaakScenesTab();
        _tabs[2] = MaakCastTab();
        _tabs[3] = MaakBeeldenTab();
        _tabs[4] = MaakFilmTab();
        var tabvlak = new Grid();
        foreach (var tab in _tabs)
        {
            tabvlak.Children.Add(tab);
        }

        // ---- tijdlijn en filmknoppen
        _tijdlijnScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = _tijdlijn,
        };
        _tijdlijnScroll.SizeChanged += (_, _) => Teken();

        _maak = Bouw.Knop("Maak film", async (_, _) => await MaakFilmAsync());
        _stop = Bouw.Knop("Stop", async (_, _) => await StopAsync(), "TextMutedBrush");
        _speelKlaar = Bouw.Knop("Speel wat klaar is", (_, _) => SpeelKlaar(), "AccentBrushLight");
        ToolTipService.SetToolTip(_speelKlaar, "Speelt de scènes die al klaar zijn achter elkaar af, ook terwijl de film nog gemaakt wordt.");
        _toonFilm = Bouw.Knop("Toon de film", async (_, _) => await ToonFilmAsync(), "AccentBrushLight");
        _inMap = Bouw.Knop("Toon in map", (_, _) => Onderdelen.ToonInMap(_filmBestand), "AccentBrushLight");
        _stop.IsEnabled = _speelKlaar.IsEnabled = _toonFilm.IsEnabled = _inMap.IsEnabled = false;
        ToolTipService.SetToolTip(_letterlijk, "Je tekst gaat precies zo naar het model: niet vertaald, niet verbeterd en zonder stijltekst erbij. Schrijf dan zelf in het Engels. Geldt voor de scènes en de cast; de tijdlijn uit je idee schrijft het chatmodel altijd.");

        var filmknoppen = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Onderdelen.Knoppen(_maak, _stop, _speelKlaar, _toonFilm, _inMap),
                _letterlijk,
                Bouw.Kop("EERDER GEMAAKTE FILMS"),
                _galerijMelding,
                _galerij,
            },
        };

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(32, 24, 32, 32),
                Spacing = 24,
                Children =
                {
                    stappen,
                    tabvlak,
                    Bouw.Paneel("Tijdlijn", new StackPanel { Spacing = 6, Children = { _tijdlijnKop, _tijdlijnScroll } }),
                    Bouw.Paneel("Film", filmknoppen),
                },
            },
        };

        Loaded += async (_, _) =>
        {
            ToonTab(_tab);
            Selecteer(_gekozen);
            await VerversAsync();
        };
        Unloaded += (_, _) => _speler.MediaPlayer?.Pause();
    }

    private static FilmScene NieuweScene() => new() { Titel = "Nieuwe scène", Seconden = 4 };

    // ---------------------------------------------------------------- tabs opbouwen

    private UIElement MaakVerhaalTab()
    {
        _beeldstijl.SelectionChanged += (_, _) => _stijl.Text = HuidigeStijl().Wissel(_stijl.Text);
        _consistentie.SelectionChanged += (_, _) =>
            _voorbeeldRij.Visibility = Onderdelen.Gekozen(_consistentie) == "eigen" ? Visibility.Visible : Visibility.Collapsed;
        _modus.SelectionChanged += (_, _) => VernieuwStappen();
        ToolTipService.SetToolTip(_beeldstijl, "Hoe de beelden eruitzien. Realistisch vraagt het model nadrukkelijk om géén tekening of illustratie.");
        ToolTipService.SetToolTip(_beeldmodel, "Welk model de beelden maakt (sleutelbeelden en cast). FLUX is snel, maar houdt spelers en plekken niet gelijk (geen cast-gelijkenis).");
        ToolTipService.SetToolTip(_videomodel, "Wan 2.2 snel is hetzelfde model in 3 stappen: vele malen sneller, soms iets minder fijn - handig om eerst het hele verhaal te bekijken.");
        ToolTipService.SetToolTip(_gelijkenis, "Hoe sterk een nieuw beeld op het voorbeeld moet lijken. Sterker = meer hetzelfde personage, maar ook meer van de achtergrond van het voorbeeld.");

        _voorbeeldRij.Children.Add(Bouw.Knop("Voorbeeldbeeld kiezen", async (_, _) =>
        {
            if (await Onderdelen.KiesBeeldAsync() is { } pad)
            {
                _eigenVoorbeeld = pad;
                _voorbeeldTekst.Text = Path.GetFileName(pad);
            }
        }, "AccentBrushLight"));
        _voorbeeldRij.Children.Add(_voorbeeldTekst);

        var instellingen = Bouw.Kolommen(2, 16);
        Bouw.Zet(instellingen, _lengte, 0, 0);
        Bouw.Zet(instellingen, _formaat, 0, 1);
        Bouw.Zet(instellingen, _beeldstijl, 1, 0);
        Bouw.Zet(instellingen, _modus, 1, 1);
        Bouw.Zet(instellingen, _overgang, 2, 0);
        Bouw.Zet(instellingen, _beeldmodel, 2, 1);
        Bouw.Zet(instellingen, _videomodel, 3, 0);
        Bouw.Zet(instellingen, _consistentie, 4, 0);
        Bouw.Zet(instellingen, _gelijkenis, 4, 1);
        var links = new StackPanel { Spacing = 16, Children = { instellingen, _voorbeeldRij, _controleer } };

        _tijdlijnKnop = Bouw.Knop("Maak tijdlijn uit idee", async (_, _) => await MaakTijdlijnAsync());
        var rechts = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                _idee,
                _tijdlijnKnop,
                _stijl,
                Onderdelen.Knoppen(
                    Bouw.Knop("Verhaal opslaan", async (_, _) => await VerhaalOpslaanAsync(), "AccentBrushLight"),
                    Bouw.Knop("Verhaal openen", async (_, _) => await VerhaalOpenenAsync(), "AccentBrushLight"),
                    Bouw.Knop("Volgende: Scènes ▶", (_, _) => ToonTab(1))),
            },
        };

        return TweeKolommen(Bouw.Paneel("Hoe lang en hoe ziet het eruit?", links), Bouw.Paneel("Verhaal", rechts), 560);
    }

    private UIElement MaakScenesTab()
    {
        _titel.TextChanged += (_, _) => Wijzig(s => s.Titel = _titel.Text, ongeldig: false);
        _gebeurt.TextChanged += (_, _) =>
        {
            if (_vullen || Gekozen is not { } scene)
            {
                return;
            }

            // Nieuwe omschrijving: de Engelse tekst hoort daar niet meer bij en wordt bij het starten opnieuw afgeleid.
            scene.Gebeurt = _gebeurt.Text;
            scene.Beeld = scene.Beweging = string.Empty;
            MaakOngeldig(scene, sleutelOok: true);
            _vullen = true;
            _beeldTekst.Text = _bewegingTekst.Text = string.Empty;
            _vullen = false;
            Teken();
        };
        ToolTipService.SetToolTip(_gebeurt, "Wijzig je deze tekst, dan wordt de Engelse tekst bij het starten opnieuw afgeleid.");
        _beeldTekst.TextChanged += (_, _) => Wijzig(s => s.Beeld = _beeldTekst.Text, ongeldig: true, sleutelOok: true);
        _bewegingTekst.TextChanged += (_, _) => Wijzig(s => s.Beweging = _bewegingTekst.Text, ongeldig: true);
        _duur.SelectionChanged += (_, _) => Wijzig(s => s.Seconden = int.Parse(Onderdelen.Gekozen(_duur), Bouw.Nl), ongeldig: true);
        _engelsToon.Checked += (_, _) => _engels.Visibility = Visibility.Visible;
        _engelsToon.Unchecked += (_, _) => _engels.Visibility = Visibility.Collapsed;
        _engels.Children.Add(_beeldTekst);
        _engels.Children.Add(_bewegingTekst);

        var titelRij = new Grid { ColumnSpacing = 16 };
        titelRij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titelRij.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        Grid.SetColumn(_duur, 1);
        titelRij.Children.Add(_titel);
        titelRij.Children.Add(_duur);

        foreach (var deel in new UIElement[] { titelRij, _gebeurt, MaakSceneCast(), _engelsToon, _engels })
        {
            _sceneEditor.Children.Add(deel);
        }

        var knoppen = Onderdelen.Knoppen(
            Bouw.Knop("+ Scène", (_, _) => VoegSceneToe()),
            Bouw.Knop("− Scène", (_, _) => VerwijderScene(), "TextMutedBrush"),
            Bouw.Knop("◀", (_, _) => Verplaats(-1), "AccentBrushLight"),
            Bouw.Knop("▶", (_, _) => Verplaats(1), "AccentBrushLight"),
            Bouw.Knop("Deze scène opnieuw", (_, _) => SceneOpnieuw(), "AccentBrushLight"),
            Bouw.Knop("Volgende: Cast ▶", (_, _) => ToonTab(2)));

        return Bouw.Paneel("Scènes", new StackPanel
        {
            Spacing = 14,
            Children = { Bouw.Melding("Kies onderaan een scène op de tijdlijn en pas hem hier aan."), _sceneKop, _sceneEditor, knoppen },
        });
    }

    private UIElement MaakFilmTab()
    {
        var vlak = new Grid { MinHeight = 420 };
        vlak.Children.Add(_leeg);
        vlak.Children.Add(_speler);
        vlak.Children.Add(_sleutelbeeld);
        if (_speler.MediaPlayer is null)
        {
            _speler.SetMediaPlayer(new Windows.Media.Playback.MediaPlayer());
        }

        _speler.MediaPlayer!.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(async () =>
        {
            if (_afspeellijst)
            {
                await VolgendeVanLijstAsync();
            }
        });

        var voortgang = new StackPanel { Spacing = 8, Children = { _fase, _kop, _voortgang, _status, _tijd } };
        return TweeKolommen(
            Bouw.Paneel("Film", new StackPanel { Spacing = 8, Children = { vlak, _spelerLabel } }),
            Bouw.Paneel("Voortgang", voortgang),
            -1);
    }

    /// <summary>Twee panelen naast elkaar; <paramref name="links"/> een vaste breedte (of -1 = de helft).</summary>
    private static Grid TweeKolommen(UIElement links, UIElement rechts, double breedteLinks)
    {
        var raster = new Grid { ColumnSpacing = 24 };
        raster.ColumnDefinitions.Add(new ColumnDefinition { Width = breedteLinks > 0 ? new GridLength(breedteLinks) : new GridLength(3, GridUnitType.Star) });
        raster.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(breedteLinks > 0 ? 1 : 2, GridUnitType.Star) });
        Grid.SetColumn((FrameworkElement)rechts, 1);
        raster.Children.Add(links);
        raster.Children.Add(rechts);
        return raster;
    }

    // ---------------------------------------------------------------- stappen

    private void ToonTab(int index)
    {
        _tab = Math.Clamp(index, 0, 4);
        for (var i = 0; i < _tabs.Length; i++)
        {
            _tabs[i].Visibility = i == _tab ? Visibility.Visible : Visibility.Collapsed;
        }

        switch (_tab)
        {
            case 1:
                ToonScene();
                break;
            case 2:
                ZetCastGebruik();
                BouwCastKaarten();
                break;
            case 3:
                BouwBeoordelingKaarten();
                break;
        }

        VernieuwStappen();
    }

    /// <summary>Een vinkje bij wat klaar is, de huidige stap in volle kleur, en uit wat nog niet kan.</summary>
    private void VernieuwStappen()
    {
        if (_stapKnoppen[0] is null)
        {
            return;
        }

        var heeftScenes = _scenes.Count > 0 && _scenes.All(s => !string.IsNullOrWhiteSpace(s.Gebeurt) || !string.IsNullOrWhiteSpace(s.Beeld));
        var castAan = _castGebruik.IsChecked == true;
        var castKlaar = castAan && _cast.Count > 0 && _cast.All(c => c.Referentie is { } p && File.Exists(p));
        var beeldenKlaar = heeftScenes && BeoordeelbareScenes().All(i => _scenes[i].Sleutel is { } p && File.Exists(p));
        bool[] klaar = [heeftScenes, heeftScenes, castKlaar, beeldenKlaar, _filmBestand is not null];
        bool[] kan = [true, true, heeftScenes, heeftScenes && (!castAan || castKlaar), heeftScenes];

        for (var i = 0; i < 5; i++)
        {
            ((TextBlock)_stapKnoppen[i].Content).Text = ((klaar[i] ? "✓  " : "") + StapTitels[i]).ToUpper(Bouw.Nl);
            _stapKnoppen[i].IsEnabled = kan[i];
            _stapKnoppen[i].Background = Bouw.Kleur(i == _tab ? "AccentBrush" : "AccentBrushLight");
        }
    }

    // ---------------------------------------------------------------- scènes bewerken

    private FilmScene? Gekozen => _gekozen >= 0 && _gekozen < _scenes.Count ? _scenes[_gekozen] : null;

    private void Selecteer(int index)
    {
        _gekozen = _scenes.Count == 0 ? -1 : Math.Clamp(index, 0, _scenes.Count - 1);
        ToonScene();
        Teken();
    }

    private void ToonScene()
    {
        _vullen = true;
        try
        {
            var scene = Gekozen;
            _sceneEditor.IsHitTestVisible = scene is not null && !_bezig;
            _sceneEditor.Opacity = _sceneEditor.IsHitTestVisible ? 1 : 0.5;
            _sceneKop.Text = scene is null ? "SCÈNE" : $"SCÈNE {_gekozen + 1} VAN {_scenes.Count}";
            _titel.Text = scene?.Titel ?? "";
            _gebeurt.Text = scene?.Gebeurt ?? "";
            _beeldTekst.Text = scene?.Beeld ?? "";
            _bewegingTekst.Text = scene?.Beweging ?? "";
            Onderdelen.Kies(_duur, (scene?.Seconden ?? 4).ToString(Bouw.Nl));
            ToonSceneCast(scene);
        }
        finally
        {
            _vullen = false;
        }
    }

    private void Wijzig(Action<FilmScene> wijziging, bool ongeldig, bool sleutelOok = false)
    {
        if (_vullen || Gekozen is not { } scene)
        {
            return;
        }

        wijziging(scene);
        if (ongeldig)
        {
            MaakOngeldig(scene, sleutelOok);
        }

        Teken();
    }

    /// <summary>Een aangepaste scène moet opnieuw gemaakt worden; een nieuwe omschrijving of cast hoort ook bij een nieuw beginbeeld.</summary>
    private static void MaakOngeldig(FilmScene scene, bool sleutelOok = false)
    {
        scene.Bestand = null;
        scene.Status = "wachtend";
        if (sleutelOok)
        {
            scene.Sleutel = null;
        }
    }

    private void VoegSceneToe()
    {
        var plek = _gekozen < 0 ? _scenes.Count : _gekozen + 1;
        _scenes.Insert(plek, NieuweScene());
        Selecteer(plek);
    }

    private void VerwijderScene()
    {
        if (Gekozen is null)
        {
            return;
        }

        _scenes.RemoveAt(_gekozen);
        if (_scenes.Count == 0)
        {
            _scenes.Add(NieuweScene());
        }

        Selecteer(_gekozen);
    }

    private void Verplaats(int stap)
    {
        var naar = _gekozen + stap;
        if (Gekozen is null || naar < 0 || naar >= _scenes.Count)
        {
            return;
        }

        (_scenes[_gekozen], _scenes[naar]) = (_scenes[naar], _scenes[_gekozen]);

        // Bij "doorlopend" begint elke scène op het einde van de vorige: een andere volgorde betekent opnieuw maken.
        if (Onderdelen.Gekozen(_modus) == "doorlopend")
        {
            for (var i = Math.Min(_gekozen, naar); i < _scenes.Count; i++)
            {
                MaakOngeldig(_scenes[i]);
            }
        }

        Selecteer(naar);
    }

    private void SceneOpnieuw()
    {
        if (Gekozen is null)
        {
            return;
        }

        // Doorlopend: de volgende scènes beginnen op het einde van deze, dus die moeten mee opnieuw.
        var tot = Onderdelen.Gekozen(_modus) == "doorlopend" ? _scenes.Count : _gekozen + 1;
        for (var i = _gekozen; i < tot; i++)
        {
            MaakOngeldig(_scenes[i]);
        }

        Teken();
        _status.Text = tot - _gekozen > 1
            ? $"Scène {_gekozen + 1} t/m {tot} worden bij 'Maak film' opnieuw gemaakt (doorlopend)."
            : $"Scène {_gekozen + 1} wordt bij 'Maak film' opnieuw gemaakt.";
    }

    // ---------------------------------------------------------------- tijdlijn

    private void Teken()
    {
        _tijdlijn.Children.Clear();
        var totaal = Math.Max(1, _scenes.Sum(s => s.Seconden));
        var open = _scenes.Count(s => string.IsNullOrEmpty(s.Bestand));
        _tijdlijnKop.Text = $"{_scenes.Count} scènes, {totaal / 60}:{totaal % 60:00} - " +
                            (open == 0 ? "alles gemaakt" : $"nog {open} te maken, ±{open * MinutenPerScene} min rekentijd");

        const double marge = 12;
        var beschikbaar = Math.Max(300, _tijdlijnScroll.ActualWidth - 2 * marge);
        var schaal = Math.Max(14, beschikbaar / totaal);
        _tijdlijn.Width = totaal * schaal + 2 * marge;

        // Liniaal: een streepje per seconde, een label per 5 of 10 seconden.
        var labelStap = schaal >= 28 ? 5 : 10;
        for (var t = 0; t <= totaal; t++)
        {
            var x = marge + t * schaal;
            _tijdlijn.Children.Add(new Line { X1 = x, X2 = x, Y1 = t % 5 == 0 ? 6 : 12, Y2 = 20, Stroke = Bouw.Kleur("TextMutedBrush"), StrokeThickness = 1 });
            if (t % labelStap == 0)
            {
                var label = new TextBlock { Text = $"{t / 60}:{t % 60:00}", FontSize = 11, Foreground = Bouw.Kleur("TextMutedBrush") };
                Canvas.SetLeft(label, x + 3);
                _tijdlijn.Children.Add(label);
            }
        }

        double begin = 0;
        for (var i = 0; i < _scenes.Count; i++)
        {
            var scene = _scenes[i];
            var breedte = scene.Seconden * schaal;
            var (achtergrond, tekst) = scene.Status switch
            {
                "klaar" => ("HealthOkBrush", "AccentForegroundBrush"),
                "mislukt" => ("HealthErrorBrush", "AccentForegroundBrush"),
                _ => ("BackgroundBrush", "TextPrimaryBrush"),
            };

            var inhoud = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
            inhoud.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {scene.Titel}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Bouw.Kleur(tekst),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            inhoud.Children.Add(new TextBlock
            {
                Text = scene.Status == "bezig" ? $"{scene.Seconden} sec · {scene.Voortgang:P0}" : $"{scene.Seconden} sec",
                FontSize = 11,
                Foreground = Bouw.Kleur(tekst),
            });

            // Een scène die bezig is vult zich van links naar rechts naarmate hij vordert.
            var raster = new Grid();
            if (scene.Status == "bezig")
            {
                raster.Children.Add(new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = Math.Max(0, (breedte - 8) * Math.Clamp(scene.Voortgang, 0, 1)),
                    Background = Bouw.Kleur("HealthBusyBrush"),
                    CornerRadius = new CornerRadius(4, 4, 4, 4),
                    Opacity = 0.55,
                });
            }

            raster.Children.Add(inhoud);
            var gekozen = i == _gekozen;
            var blok = new Border
            {
                Width = Math.Max(4, breedte - 2),
                Height = 62,
                Background = Bouw.Kleur(achtergrond),
                BorderBrush = Bouw.Kleur(gekozen ? "AccentBrush" : "BorderBrush"),
                BorderThickness = new Thickness(gekozen ? 3 : 1),
                CornerRadius = new CornerRadius(6, 6, 6, 6),
                Child = raster,
            };
            ToolTipService.SetToolTip(blok, $"{scene.Titel}\n{scene.Gebeurt}".Trim());
            var nummer = i;
            blok.Tapped += async (_, _) => await KiesOpTijdlijnAsync(nummer);
            Canvas.SetLeft(blok, marge + begin * schaal + 1);
            Canvas.SetTop(blok, 26);
            _tijdlijn.Children.Add(blok);
            begin += scene.Seconden;
        }

        VernieuwStappen();
    }

    private async Task KiesOpTijdlijnAsync(int index)
    {
        Selecteer(index);

        // Een gemaakte scène laat zich meteen bekijken.
        if (_scenes[index].Bestand is { } bestand && File.Exists(bestand))
        {
            _afspeellijst = false;
            _handmatig = true;
            await SpeelAsync(bestand, $"Scène {index + 1}: {_scenes[index].Titel}");
        }
    }

    // ---------------------------------------------------------------- tijdlijn schrijven

    private async Task MaakTijdlijnAsync()
    {
        var idee = _idee.Text.Trim();
        if (idee.Length == 0)
        {
            _status.Text = "Typ eerst een verhaalidee.";
            return;
        }

        if (_scenes.Any(s => !string.IsNullOrWhiteSpace(s.Gebeurt) || s.Bestand is not null)
            && !await Onderdelen.BevestigAsync(XamlRoot, "Nieuwe tijdlijn?", "De huidige scènes worden vervangen door een nieuwe tijdlijn.", "Vervangen"))
        {
            return;
        }

        _tijdlijnKnop.IsEnabled = false;
        _tijdlijnKop.Text = "Het chatmodel schrijft de tijdlijn… (dit kan een minuut duren)";
        try
        {
            var (voorstel, fout) = await Verhaalschrijver.MaakTijdlijnAsync(idee, int.Parse(Onderdelen.Gekozen(_lengte), Bouw.Nl), HuidigeStijl().Prefix, CancellationToken.None);
            if (voorstel is null)
            {
                _tijdlijnKop.Text = $"Tijdlijn maken mislukt: {fout}";
                return;
            }

            _scenes = voorstel.Scenes;
            _stijl.Text = voorstel.Stijl;
            Selecteer(0);
            ToonTab(1);
        }
        finally
        {
            _tijdlijnKnop.IsEnabled = true;
        }
    }

    // ---------------------------------------------------------------- stijl, sterkte en de kopie voor de service

    private Beeldstijl HuidigeStijl() => Beeldstijl.Van(Onderdelen.Gekozen(_beeldstijl));

    private bool Letterlijk => _letterlijk.IsChecked == true;

    /// <summary>De stijl zoals de service hem krijgt: met de gekozen stijltekst ervoor, behalve bij Letterlijk.</summary>
    private string StijlVoorService() => Letterlijk ? _stijl.Text.Trim() : HuidigeStijl().MetStijl(_stijl.Text.Trim());

    private string NegatiefVoorService() => Letterlijk ? Beeldstijl.Van("geen").Negatief : HuidigeStijl().Negatief;

    /// <summary>Wat de service nodig heeft om de personages op elkaar te laten lijken.</summary>
    private (string? Referentie, bool Eerste, double Sterkte) Consistentie()
    {
        var sterkte = double.Parse(Onderdelen.Gekozen(_gelijkenis), System.Globalization.CultureInfo.InvariantCulture);
        return Onderdelen.Gekozen(_consistentie) switch
        {
            "eigen" when _eigenVoorbeeld is not null && File.Exists(_eigenVoorbeeld) => (_eigenVoorbeeld, false, sterkte),
            "eerste" => (null, true, sterkte),
            _ => (null, false, sterkte),
        };
    }

    /// <summary>Een opdracht met alles wat voor sleutelbeelden, cast en film gelijk is.</summary>
    private BeeldOpdracht Opdracht(string soort)
    {
        var formaat = Onderdelen.Gekozen(_formaat).Split('x');
        var (referentie, eerste, sterkte) = Consistentie();
        var (speler, plek, voorwerp) = CastSterkte();
        return new BeeldOpdracht
        {
            Soort = soort,
            Breedte = int.Parse(formaat[0], Bouw.Nl),
            Hoogte = int.Parse(formaat[1], Bouw.Nl),
            Videomodel = Onderdelen.Gekozen(_videomodel),
            Modus = Onderdelen.Gekozen(_modus),
            Overgang = Onderdelen.Gekozen(_overgang),
            Stijl = StijlVoorService(),
            Beeldmodel = Onderdelen.Gekozen(_beeldmodel),
            Negatief = NegatiefVoorService(),
            Prompt = _idee.Text.Trim(),
            Referentie = referentie,
            EersteAlsReferentie = eerste,
            ReferentieSterkte = sterkte,
            SpelerSterkte = speler,
            PlekSterkte = plek,
            VoorwerpSterkte = voorwerp,
            Scenes = _scenes.Select(KopieVoorService).ToList(),
        };
    }

    private static FilmScene Kopie(FilmScene s) => new()
    {
        Titel = s.Titel,
        Gebeurt = s.Gebeurt,
        Beeld = s.Beeld,
        Beweging = s.Beweging,
        Seconden = s.Seconden,
        Bestand = s.Bestand,
        Status = s.Status,
        Seed = s.Seed,
        Voortgang = s.Voortgang,
        Sleutel = s.Sleutel,
        Opnieuw = s.Opnieuw,
        Spelers = [.. s.Spelers],
        Voorwerpen = [.. s.Voorwerpen],
        VoorwerpRef = s.VoorwerpRef,
        Plek = s.Plek,
        SpelerRefs = [.. s.SpelerRefs],
        PlekRef = s.PlekRef,
    };

    // ---------------------------------------------------------------- film maken

    private async Task MaakFilmAsync()
    {
        if (_scenes.Any(s => string.IsNullOrWhiteSpace(s.Gebeurt) && string.IsNullOrWhiteSpace(s.Beeld)))
        {
            _status.Text = "Elke scène heeft een omschrijving nodig ('Wat gebeurt er?' bij stap 2).";
            ToonTab(4);
            return;
        }

        ToonTab(4);
        ZetBezig(true);
        var klok = Stopwatch.StartNew();
        try
        {
            if (!await LeidAfMissendeAsync())
            {
                return;
            }

            // Met een cast eerst de referentiebeelden, als die er nog niet zijn.
            if (_castGebruik.IsChecked == true && _cast.Count > 0 && _cast.Any(c => c.Referentie is not { } p || !File.Exists(p)) && !await RunCastAsync(klok))
            {
                return;
            }

            // Zijn de sleutelbeelden nog niet gemaakt en wil je ze controleren, dan eerst stap 4.
            if (_controleer.IsChecked == true && NodigeSleutelbeelden().Count > 0)
            {
                _status.Text = "De sleutelbeelden zijn nog niet gemaakt. Maak en controleer ze eerst bij stap 4 (Beelden), of zet 'Sleutelbeelden eerst laten controleren' uit bij stap 1.";
                ToonTab(3);
                return;
            }

            await RunFilmAsync(klok);
        }
        catch (Exception fout)
        {
            _status.Text = Beeldservice.Foutmelding(fout);
        }
        finally
        {
            _jobId = null;
            ZetBezig(false);
            VernieuwStappen();
        }
    }

    /// <summary>Stuurt de hele film naar de service en volgt de voortgang. Goedgekeurde sleutelbeelden en gemaakte scènes worden hergebruikt.</summary>
    private async Task RunFilmAsync(Stopwatch klok)
    {
        _status.Text = "Film starten…";
        _kop.Text = "FILM STARTEN";
        _jobId = await Beeldservice.StartAsync(Opdracht("film"), new Progress<string>(t => _status.Text = t));
        _stop.IsEnabled = true;
        App.AppLog.Schrijf("imagefunn", $"Film maken: {_scenes.Count} scènes ({Onderdelen.Gekozen(_videomodel)})");

        var teDoen = _scenes.Count(s => string.IsNullOrEmpty(s.Bestand));
        _klaarGezien = _scenes.Select((s, i) => (s, i)).Where(x => !string.IsNullOrEmpty(x.s.Bestand)).Select(x => x.i).ToHashSet();
        _clipInRun = _handmatig = _afspeellijst = false;
        _voorbeeldPad = null;
        var sceneKlok = new Stopwatch();
        var klaarBegin = _scenes.Count - teDoen;

        while (true)
        {
            var stand = await Beeldservice.StatusAsync(_jobId);
            Overnemen(stand);
            _speelKlaar.IsEnabled = _scenes.Any(s => !string.IsNullOrEmpty(s.Bestand));
            await ToonVoortgangAsync(stand, klok, sceneKlok, klaarBegin, teDoen);
            Teken();

            if (stand.IsKlaar && stand.Bestand is { } film)
            {
                _filmBestand = film;
                _afspeellijst = false;
                await SpeelAsync(film);
                _status.Text = $"Film klaar in {klok.Elapsed:h\\:mm\\:ss}.";
                App.AppLog.Schrijf("imagefunn", $"Film klaar in {klok.Elapsed:h\\:mm\\:ss}");
                await VerversAsync();
                return;
            }

            if (stand.IsMislukt)
            {
                _status.Text = $"{stand.Foutmelding} Druk op 'Maak film' om verder te gaan; gemaakte scènes worden hergebruikt.";
                return;
            }

            await Task.Delay(Peilen);
        }
    }

    /// <summary>Neemt de stand per scène over van wat de service meldt.</summary>
    private void Overnemen(BeeldJobStatus stand)
    {
        if (stand.Scenes.Count != _scenes.Count)
        {
            return;
        }

        for (var i = 0; i < _scenes.Count; i++)
        {
            _scenes[i].Status = stand.Scenes[i].Status;
            _scenes[i].Bestand = stand.Scenes[i].Bestand;
            _scenes[i].Seed = stand.Scenes[i].Seed;
            _scenes[i].Voortgang = stand.Scenes[i].Voortgang;
            _scenes[i].Sleutel = stand.Scenes[i].Sleutel;
        }
    }

    private void ZetBezig(bool bezig)
    {
        _bezig = bezig;
        foreach (var i in new[] { 0, 1, 2, 3 })
        {
            ((FrameworkElement)_tabs[i]).IsHitTestVisible = !bezig;
            ((FrameworkElement)_tabs[i]).Opacity = bezig ? 0.6 : 1;
        }

        _maak.IsEnabled = !bezig;
        _stop.IsEnabled = false;
        _voortgang.IsIndeterminate = false;
        if (bezig)
        {
            _voortgang.Value = 0;
            _tijd.Text = "";
        }

        _toonFilm.IsEnabled = _inMap.IsEnabled = !bezig && _filmBestand is not null;
        _speelKlaar.IsEnabled = _scenes.Any(s => !string.IsNullOrEmpty(s.Bestand));
        ToonScene();
    }

    private async Task StopAsync()
    {
        if (_jobId is null)
        {
            return;
        }

        _stop.IsEnabled = false;
        _status.Text = "Stoppen na de huidige stap… (de al gemaakte scènes blijven bewaard)";
        try
        {
            await Beeldservice.StopAsync(_jobId);
        }
        catch (Exception fout)
        {
            _status.Text = $"Stoppen mislukt: {Beeldservice.Foutmelding(fout)}";
        }
    }

    private static int Minuten(TimeSpan duur) => Math.Max(1, (int)Math.Round(duur.TotalMinutes));

    /// <summary>
    /// Zet de stand van de service op de voortgangskaart: fase, scène, percentage, verstreken tijd en een schatting van
    /// de rest. De schatting begint pas als er genoeg van de eerste scène af is.
    /// </summary>
    private async Task ToonVoortgangAsync(BeeldJobStatus stand, Stopwatch klok, Stopwatch sceneKlok, int klaarBegin, int teDoen)
    {
        var n = _scenes.Count;
        var klaarNu = _scenes.Count(s => s.Status == "klaar");
        var klaarInRun = Math.Max(0, klaarNu - klaarBegin);
        var actief = _scenes.FindIndex(s => s.Status == "bezig");
        var fase = stand.Fase;

        if (fase == "scenes" && !sceneKlok.IsRunning && (actief >= 0 || klaarInRun > 0))
        {
            sceneKlok.Start();
        }

        var faseNr = fase == "sleutelbeelden" ? 1 : fase is "model" or "scenes" ? 2 : fase is "samenvoegen" or "klaar" ? 3 : 0;
        _fase.Text = string.Join("  ›  ", new[] { "1 Sleutelbeelden", "2 Scènes animeren", "3 Samenvoegen" }.Select((t, i) => i + 1 == faseNr ? t.ToUpper(Bouw.Nl) : t));

        _kop.Text = (fase switch
        {
            "sleutelbeelden" => "Sleutelbeelden maken",
            "model" => "Videomodel laden",
            "scenes" when actief >= 0 => $"Scène {actief + 1} van {n}: {_scenes[actief].Titel}",
            "scenes" => $"Scène {Math.Min(n, klaarNu + 1)} van {n}",
            "samenvoegen" => "Film samenvoegen",
            "klaar" => "Film klaar",
            _ => "Bezig…",
        } + $"  ·  {stand.Voortgang:P0}").ToUpper(Bouw.Nl);

        var onbepaald = fase == "model" || (fase == "scenes" && actief >= 0 && _scenes[actief].Voortgang <= 0);
        _voortgang.IsIndeterminate = onbepaald;
        if (!onbepaald)
        {
            _voortgang.Value = stand.Voortgang;
        }

        _status.Text = stand.Stap;
        var tijd = $"Verstreken {klok.Elapsed:h\\:mm\\:ss}";
        if (fase == "scenes" && sceneKlok.IsRunning)
        {
            var gedaan = klaarInRun + (actief >= 0 ? Math.Clamp(_scenes[actief].Voortgang, 0, 1) : 0);
            if (gedaan >= 0.1)
            {
                var rest = (sceneKlok.Elapsed / gedaan * Math.Max(0, teDoen - gedaan)) + TimeSpan.FromSeconds(60);
                tijd += $"  ·  nog ±{Minuten(rest)} min  ·  klaar rond {DateTime.Now + rest:HH:mm}";
            }
            else
            {
                tijd += "  ·  tijdschatting volgt zodra de eerste scène op gang is";
            }
        }
        else if (fase == "samenvoegen")
        {
            tijd += "  ·  bijna klaar";
        }
        else if (fase is "sleutelbeelden" or "model")
        {
            tijd += $"  ·  daarna {teDoen} scène(s) animeren";
        }

        _tijd.Text = tijd;

        // Een scène die klaar komt speelt meteen; tot de eerste klaar is tonen we de sleutelbeelden zodra ze er zijn.
        for (var i = 0; i < n; i++)
        {
            if (_scenes[i].Status == "klaar" && _scenes[i].Bestand is { } clip && _klaarGezien.Add(i))
            {
                _clipInRun = true;
                if (!_handmatig)
                {
                    await SpeelAsync(clip, $"Scène {i + 1} is klaar");
                }
            }
        }

        if (!_clipInRun && !_handmatig)
        {
            var laatste = actief >= 0 && !string.IsNullOrEmpty(_scenes[actief].Sleutel) ? actief : _scenes.FindLastIndex(s => !string.IsNullOrEmpty(s.Sleutel));
            if (laatste >= 0)
            {
                await ToonVoorbeeldAsync(_scenes[laatste].Sleutel!, $"Beginbeeld van scène {laatste + 1}");
            }
        }
    }

    // ---------------------------------------------------------------- afspelen

    private async Task SpeelAsync(string bestand, string? label = null)
    {
        try
        {
            var bron = await StorageFile.GetFileFromPathAsync(bestand);
            _speler.Source = MediaSource.CreateFromStorageFile(bron);
            if (_speler.MediaPlayer is { } speler)
            {
                speler.IsLoopingEnabled = !_afspeellijst;
                speler.Play();
            }
        }
        catch (Exception fout)
        {
            _status.Text = $"De video kon niet afgespeeld worden: {fout.Message}";
            return;
        }

        _voorbeeldPad = null;
        _sleutelbeeld.Visibility = Visibility.Collapsed;
        _speler.Visibility = Visibility.Visible;
        _leeg.Visibility = Visibility.Collapsed;
        _spelerLabel.Text = label ?? "";
    }

    /// <summary>Laat een sleutelbeeld zien zolang er nog geen gemaakte clip is.</summary>
    private async Task ToonVoorbeeldAsync(string pad, string label)
    {
        if (_voorbeeldPad == pad || await Onderdelen.LaadBeeldAsync(pad) is not { } beeld)
        {
            return;
        }

        _speler.MediaPlayer?.Pause();
        _speler.Visibility = Visibility.Collapsed;
        _sleutelbeeld.Source = beeld;
        _sleutelbeeld.Visibility = Visibility.Visible;
        _leeg.Visibility = Visibility.Collapsed;
        _spelerLabel.Text = label;
        _voorbeeldPad = pad;
    }

    /// <summary>Speelt alle scènes die al klaar zijn achter elkaar.</summary>
    private void SpeelKlaar()
    {
        _afspeellijst = true;
        _handmatig = true;
        _lijstIndex = -1;
        ToonTab(4);
        _ = VolgendeVanLijstAsync();
    }

    private async Task VolgendeVanLijstAsync()
    {
        var klaar = _scenes.Select((s, i) => (s, i)).Where(x => x.s.Bestand is { } f && File.Exists(f)).ToList();
        if (klaar.Count == 0)
        {
            _afspeellijst = false;
            _status.Text = "Er is nog geen scène klaar.";
            return;
        }

        _lijstIndex = (_lijstIndex + 1) % klaar.Count;
        var (scene, nummer) = klaar[_lijstIndex];
        await SpeelAsync(scene.Bestand!, $"Scène {nummer + 1} van {_scenes.Count}: {scene.Titel}");
    }

    private async Task ToonFilmAsync()
    {
        if (_filmBestand is not null && File.Exists(_filmBestand))
        {
            _afspeellijst = false;
            _handmatig = true;
            ToonTab(4);
            await SpeelAsync(_filmBestand);
        }
    }

    // ---------------------------------------------------------------- eerder gemaakte films

    public async Task VerversAsync()
    {
        try
        {
            var lijst = (await Beeldservice.LijstAsync("film")).Where(t => t.Bestand is not null && File.Exists(t.Bestand)).ToList();
            var handtekening = string.Join(",", lijst.Select(t => t.JobId));
            if (handtekening == _galerijHandtekening)
            {
                return;
            }

            _galerijHandtekening = handtekening;
            _galerijMelding.Text = lijst.Count == 0 ? "Nog geen films gemaakt." : "Klik om een film te openen (de scènes komen terug op de tijdlijn).";
            _galerij.Children.Clear();
            foreach (var taak in lijst)
            {
                var open = new HyperlinkButton
                {
                    Content = $"{taak.GemaaktOp.LocalDateTime:dd-MM HH:mm}  ·  {taak.Scenes.Count} scènes  ·  {Onderdelen.Kort(taak.Prompt, 90)}",
                    Foreground = Bouw.Kleur("TextPrimaryBrush"),
                    Padding = new Thickness(0),
                };
                open.Click += async (_, _) => await OpenFilmAsync(taak);
                var weg = Bouw.Knop("✕", async (_, _) => await VerwijderFilmAsync(taak), "TextMutedBrush");
                ToolTipService.SetToolTip(weg, "Deze film verwijderen, met alle scènes en sleutelbeelden");
                _galerij.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { weg, open } });
            }
        }
        catch (Exception fout)
        {
            _galerijMelding.Text = $"De films konden niet geladen worden. {Beeldservice.Foutmelding(fout)}";
        }
    }

    /// <summary>Een eerder gemaakte film openen: de film speelt en de scènes kunnen terug op de tijdlijn.</summary>
    private async Task OpenFilmAsync(BeeldJobStatus taak)
    {
        if (_bezig)
        {
            return;
        }

        if (taak.Bestand is not null)
        {
            _filmBestand = taak.Bestand;
            _toonFilm.IsEnabled = _inMap.IsEnabled = true;
            _afspeellijst = false;
            _handmatig = true;
            ToonTab(4);
            await SpeelAsync(taak.Bestand);
        }

        if (taak.Scenes.Count == 0
            || (_scenes.Any(s => !string.IsNullOrWhiteSpace(s.Gebeurt) || s.Bestand is not null)
                && !await Onderdelen.BevestigAsync(XamlRoot, "Scènes overnemen?", "Ook de scènes van deze film op de tijdlijn zetten? De huidige scènes worden dan vervangen.", "Overnemen")))
        {
            return;
        }

        _scenes = taak.Scenes.Select(Kopie).ToList();
        _stijl.Text = taak.Stijl;
        _idee.Text = taak.Prompt;
        Onderdelen.Kies(_modus, taak.Modus);
        Onderdelen.Kies(_overgang, taak.Overgang);
        Onderdelen.Kies(_formaat, $"{taak.Breedte}x{taak.Hoogte}");
        Onderdelen.Kies(_videomodel, taak.Videomodel);
        Onderdelen.Kies(_beeldmodel, taak.Beeldmodel);
        Selecteer(0);
    }

    /// <summary>Verwijdert een film met zijn hele map: de film, alle scènes en de sleutelbeelden.</summary>
    private async Task VerwijderFilmAsync(BeeldJobStatus taak)
    {
        if (_bezig)
        {
            _status.Text = "Wacht tot het maken klaar is voor je een film verwijdert.";
            return;
        }

        if (!await Onderdelen.BevestigAsync(XamlRoot, "Deze film verwijderen?", $"{Onderdelen.Kort(taak.Prompt, 120)}\n\nDe film en alle bijbehorende scènes en sleutelbeelden worden definitief verwijderd; dit kan niet ongedaan gemaakt worden."))
        {
            return;
        }

        var map = Path.GetDirectoryName(taak.Bestand) ?? "";
        bool ZitIn(string? pad) => pad is not null && map.Length > 0 && pad.StartsWith(map, StringComparison.OrdinalIgnoreCase);

        // De speler houdt het bestand open: eerst loslaten.
        _speler.MediaPlayer?.Pause();
        _speler.Source = null;
        _speler.Visibility = Visibility.Collapsed;
        _leeg.Visibility = Visibility.Visible;
        _afspeellijst = false;
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

        if (ZitIn(_filmBestand))
        {
            _filmBestand = null;
            _toonFilm.IsEnabled = _inMap.IsEnabled = false;
        }

        foreach (var scene in _scenes)
        {
            if (ZitIn(scene.Bestand))
            {
                scene.Bestand = null;
                scene.Status = "wachtend";
            }

            if (ZitIn(scene.Sleutel))
            {
                scene.Sleutel = null;
            }
        }

        Teken();
        await VerversAsync();
        _status.Text = "Film verwijderd.";
    }

    // ---------------------------------------------------------------- verhaal bewaren

    private sealed class Verhaal
    {
        public string Idee { get; set; } = string.Empty;

        public string Stijl { get; set; } = string.Empty;

        public string Modus { get; set; } = "sleutelbeelden";

        public string Overgang { get; set; } = "zacht";

        public string Videomodel { get; set; } = "wan";

        public string Beeldmodel { get; set; } = "realvis";

        public string Formaat { get; set; } = "704x480";

        public string Beeldstijl { get; set; } = "realistisch";

        public bool CastGebruik { get; set; }

        public List<CastItem> Cast { get; set; } = [];

        public List<FilmScene> Scenes { get; set; } = [];
    }

    private async Task VerhaalOpslaanAsync()
    {
        if (await Onderdelen.KiesVerhaalAsync(opslaan: true) is not { } pad)
        {
            return;
        }

        var verhaal = new Verhaal
        {
            Idee = _idee.Text,
            Stijl = _stijl.Text,
            Modus = Onderdelen.Gekozen(_modus),
            Overgang = Onderdelen.Gekozen(_overgang),
            Videomodel = Onderdelen.Gekozen(_videomodel),
            Beeldmodel = Onderdelen.Gekozen(_beeldmodel),
            Formaat = Onderdelen.Gekozen(_formaat),
            Beeldstijl = Onderdelen.Gekozen(_beeldstijl),
            CastGebruik = _castGebruik.IsChecked == true,
            Cast = _cast,
            Scenes = _scenes.Select(Kopie).ToList(),
        };
        try
        {
            await File.WriteAllTextAsync(pad, JsonSerializer.Serialize(verhaal, JsonOpties));
            _tijdlijnKop.Text = $"Verhaal opgeslagen: {pad}";
        }
        catch (Exception fout) when (fout is IOException or UnauthorizedAccessException)
        {
            _tijdlijnKop.Text = $"Opslaan mislukt: {fout.Message}";
        }
    }

    private async Task VerhaalOpenenAsync()
    {
        if (await Onderdelen.KiesVerhaalAsync(opslaan: false) is not { } pad)
        {
            return;
        }

        try
        {
            var verhaal = JsonSerializer.Deserialize<Verhaal>(await File.ReadAllTextAsync(pad), JsonOpties);
            if (verhaal is null || verhaal.Scenes.Count == 0)
            {
                _tijdlijnKop.Text = "Dit bestand bevat geen scènes.";
                return;
            }

            _idee.Text = verhaal.Idee;
            _stijl.Text = verhaal.Stijl;
            Onderdelen.Kies(_modus, verhaal.Modus);
            Onderdelen.Kies(_overgang, verhaal.Overgang);
            Onderdelen.Kies(_videomodel, verhaal.Videomodel);
            Onderdelen.Kies(_beeldmodel, verhaal.Beeldmodel);
            Onderdelen.Kies(_formaat, verhaal.Formaat);
            Onderdelen.Kies(_beeldstijl, verhaal.Beeldstijl);
            _cast = verhaal.Cast;
            foreach (var item in _cast.Where(c => c.Referentie is not null && !File.Exists(c.Referentie)))
            {
                item.Referentie = null;
            }

            _castGebruik.IsChecked = verhaal.CastGebruik;

            // Een gemaakte scène telt alleen nog als het bestand er nog is.
            foreach (var scene in verhaal.Scenes.Where(s => s.Bestand is not null && !File.Exists(s.Bestand)))
            {
                MaakOngeldig(scene);
            }

            _scenes = verhaal.Scenes;
            Selecteer(0);
            _tijdlijnKop.Text = $"Verhaal geopend: {_scenes.Count} scènes.";
        }
        catch (Exception fout)
        {
            _tijdlijnKop.Text = $"Openen mislukt: {fout.Message}";
        }
    }
}
