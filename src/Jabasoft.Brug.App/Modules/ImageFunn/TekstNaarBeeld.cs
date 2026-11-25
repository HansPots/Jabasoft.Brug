using Jabasoft.Brug.App.Layout;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// ImageFunn &gt; Tekst naar beeld: een beeld maken uit een omschrijving, en het daarna bijsturen.
///
/// Links de opdracht (omschrijving, stijl, beeldmodel, formaat, seed), rechts
/// het gemaakte beeld met rechts ernaast de beeldinfo en het bijsturen (zie TekstNaarBeeld.Bijsturen.cs),
/// onderaan de galerij. Het beeld kan door naar Beeld naar video, Beeld naar
/// tekst en Film maken. De beeldservice doet het werk (<see cref="Beeldservice"/>).
///
/// Opbouw volgens de afspraak voor elke pagina: alles staat er meteen, de
/// galerij toont "Ophalen…" en vult zich zodra de service antwoordt.
/// </summary>
public sealed partial class TekstNaarBeeld : UserControl, IVerversbaar, IOntvangtTekst
{
    private static readonly TimeSpan Peilen = TimeSpan.FromSeconds(0.7);

    public static readonly (string Tag, string Naam)[] Beeldmodellen =
    [
        ("realvis", "RealVisXL - fotorealistisch"),
        ("juggernaut", "Juggernaut XL - fotorealistisch"),
        ("flux", "FLUX.1-schnell - snel, 4 stappen"),
        ("zimage", "Z-Image Turbo - fotografisch, 8 stappen, tekst in beeld"),
        ("fluxdev", "FLUX.1-dev - hoogste kwaliteit, trager"),
    ];

    private static readonly (string Tag, string Naam)[] Formaten =
    [
        ("1024x1024", "Vierkant 1024 × 1024"),
        ("1344x768", "Liggend 1344 × 768"),
        ("768x1344", "Staand 768 × 1344"),
        ("768x768", "Klein vierkant 768 × 768 (sneller)"),
        ("1536x1536", "Groot vierkant 1536 × 1536 (FLUX en Z-Image)"),
        ("1920x1088", "Groot liggend 1920 × 1088 (FLUX en Z-Image)"),
        ("1088x1920", "Groot staand 1088 × 1920 (FLUX en Z-Image)"),
    ];

    /// <summary>De grote formaten: RealVisXL en Juggernaut zijn op ±1 megapixel getraind en gaan daarboven dingen dubbel tekenen.</summary>
    private static bool IsGroot(string formaat) => formaat is "1536x1536" or "1920x1088" or "1088x1920";

    private readonly TextBox _omschrijving;
    private readonly ComboBox _stijl;
    private readonly ComboBox _model;
    private readonly ComboBox _formaat;
    private readonly TextBox _seed;
    private readonly TextBox _negatief;
    private readonly Button _maak;
    private readonly Button _verbeterKnop;
    private readonly Button _stijlKnop;
    private readonly Button _stop;
    private readonly ProgressBar _voortgang = Onderdelen.Voortgang();
    private readonly TextBlock _status = Bouw.Melding("Klaar voor een opdracht.");
    private readonly TextBlock _tip = Bouw.Melding("");

    private readonly Image _beeld = new() { Stretch = Stretch.Uniform, MaxHeight = 720, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly Tekenlaag _tekenlaag = new() { Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _geenBeeld = Bouw.Melding("Nog geen beeld. Maak er een, of kies er een uit de galerij.");
    private readonly TextBlock _beeldInfo = Bouw.Melding("");
    private Border _infopaneel = null!;
    private readonly Button _inMap;
    private readonly Button _verwijder;
    private readonly Button _naarVideo;
    private readonly Button _naarTekst;
    private readonly Button _naarFilm;
    private readonly Button _vergroot;

    private readonly StackPanel _galerij = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    private readonly TextBlock _galerijMelding = Bouw.Melding("Ophalen…");
    private readonly Dictionary<string, BitmapImage> _miniaturen = [];
    private string? _galerijHandtekening;

    /// <summary>Het bestand en de prompt van het beeld dat nu groot staat.</summary>
    private string? _bestand;
    private string? _prompt;
    private string? _lopend;

    public TekstNaarBeeld()
    {
        // ---- opdracht
        _omschrijving = new TextBox
        {
            Header = "Omschrijving",
            PlaceholderText = "Bijvoorbeeld: a red fox in a snowy forest at dawn",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 170,
        };
        _stijl = Onderdelen.Keuze("Stijl", Beeldstijl.Alle.Select(s => (s.Tag, s.Naam)), "realistisch");
        _negatief = new TextBox
        {
            Header = "Negatief - wat er niet in het beeld mag (FLUX en Z-Image doen hier niets mee)",
            PlaceholderText = "leeg = geen negatieve prompt",
            TextWrapping = TextWrapping.Wrap,
            Text = Beeldstijl.Van(Onderdelen.Gekozen(_stijl)).Negatief,
        };
        _model = Onderdelen.Keuze("Beeldmodel", Beeldmodellen, Voorkeuren.Lees(Beeldservice.VoorkeurBeeldmodel, "realvis"));
        _formaat = Onderdelen.Keuze("Formaat", Formaten, "1024x1024");
        _seed = new TextBox { Header = "Seed (leeg = willekeurig)", PlaceholderText = "willekeurig" };
        ToolTipService.SetToolTip(_seed, "Dezelfde omschrijving met dezelfde seed geeft hetzelfde beeld.");

        // Maak beeld stuurt precies wat er in Omschrijving en Negatief staat. De twee andere knoppen passen die tekst
        // zichtbaar aan, zodat je altijd kunt lezen (en bijwerken) wat er naar het beeldmodel gaat.
        _maak = Bouw.Knop("Maak beeld", async (_, _) => await MaakAsync());
        ToolTipService.SetToolTip(_maak, "Stuurt de omschrijving en de negatieve prompt precies zoals ze er staan naar het beeldmodel.");
        _verbeterKnop = Bouw.Knop("Verbeter / vertaal", async (_, _) => await VerbeterAsync(), "AccentBrushLight");
        ToolTipService.SetToolTip(_verbeterKnop, "Het chatmodel maakt van je omschrijving een Engelse beeldprompt en zet die in Omschrijving.");
        _stijlKnop = Bouw.Knop("Stijl toevoegen", (_, _) => VoegStijlToe(), "AccentBrushLight");
        ToolTipService.SetToolTip(_stijlKnop, "Zet de tekst van de gekozen stijl achter de omschrijving (een eerdere stijltekst wordt vervangen) en vult Negatief.");
        _stop = Bouw.Knop("Stop", async (_, _) => await StopAsync(), "TextMutedBrush");
        _stop.IsEnabled = false;

        var keuzes = Bouw.Kolommen(2, 16);
        Bouw.Zet(keuzes, _stijl, 0, 0);
        Bouw.Zet(keuzes, _model, 0, 1);
        Bouw.Zet(keuzes, _formaat, 1, 0);
        Bouw.Zet(keuzes, _seed, 1, 1);

        var opdracht = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                _omschrijving, _negatief, Onderdelen.Knoppen(_verbeterKnop, _stijlKnop), keuzes, _tip,
                Onderdelen.Knoppen(_maak, _stop), _voortgang, _status,
            },
        };
        _stijl.SelectionChanged += (_, _) => ZetTip();
        _model.SelectionChanged += (_, _) => ZetTip();
        _formaat.SelectionChanged += (_, _) => ZetTip();
        ZetTip();

        // ---- beeld (de tekenlaag ligt precies over het getoonde beeld, voor het aanwijzen van een gebied)
        _beeld.SizeChanged += (_, _) =>
        {
            _tekenlaag.Width = _beeld.ActualWidth;
            _tekenlaag.Height = _beeld.ActualHeight;
        };
        _inMap = Bouw.Knop("Toon in map", (_, _) => Onderdelen.ToonInMap(_bestand));
        _verwijder = Bouw.Knop("Verwijderen", async (_, _) => await VerwijderAsync(), "TextMutedBrush");
        _naarVideo = Bouw.Knop("Laat bewegen", (_, _) => Geef("imagefunn.beeldnaarvideo"), "AccentBrushLight");
        _naarTekst = Bouw.Knop("Beschrijf", (_, _) => Geef("imagefunn.beeldnaartekst"), "AccentBrushLight");
        _naarFilm = Bouw.Knop("Gebruik in film", (_, _) => Geef("imagefunn.film"), "AccentBrushLight");
        ToolTipService.SetToolTip(_naarFilm, "Voegt dit beeld toe aan de cast van Film maken, als speler, plek of voorwerp.");
        _vergroot = Bouw.Knop("Vergroot 2×", async (_, _) => await VergrootAsync(), "AccentBrushLight");
        ToolTipService.SetToolTip(_vergroot, "Maakt dit beeld 2× zo groot (bijvoorbeeld 2048 × 2048) met Real-ESRGAN: scherpere randen, geen nieuwe details. Het origineel blijft staan.");
        ZetBeeldknoppen(false);

        var beeldvlak = new Grid { MinHeight = 420 };
        beeldvlak.Children.Add(_geenBeeld);
        beeldvlak.Children.Add(_beeld);
        beeldvlak.Children.Add(_tekenlaag);
        var beeldkolom = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                beeldvlak,
                Onderdelen.Knoppen(_naarVideo, _naarTekst, _naarFilm, _vergroot, _inMap, _verwijder),
            },
        };

        // Rechts naast de foto: de beeldinfo (formaat, model, seed, prompt) in een eigen kader, daaronder het bijsturen.
        // Zonder beeld zijn beide verborgen en krijgt de foto (of de melding "nog geen beeld") de hele breedte.
        _beeldInfo.IsTextSelectionEnabled = true;
        _infopaneel = new Border { Visibility = Visibility.Collapsed, Child = Bouw.Paneel("Beeldinfo", _beeldInfo) };
        var rechterkolom = new StackPanel
        {
            Width = 440,
            Spacing = 24,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _infopaneel, MaakBijstuurpaneel() },
        };
        var beeld = new Grid { ColumnSpacing = 24 };
        beeld.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        beeld.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(rechterkolom, 1);
        beeld.Children.Add(beeldkolom);
        beeld.Children.Add(rechterkolom);

        // ---- galerij
        var galerij = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _galerijMelding,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollMode = ScrollMode.Enabled,
                    VerticalScrollMode = ScrollMode.Disabled,
                    Content = _galerij,
                },
            },
        };

        var boven = new Grid { ColumnSpacing = 24 };
        boven.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480) });
        boven.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rechts = Bouw.Paneel("Beeld", beeld);
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
                Children = { boven, Bouw.Paneel("Galerij", galerij, "eerder gemaakt") },
            },
        };

        Loaded += async (_, _) => await VerversAsync();
    }

    /// <summary>Een tip als de gekozen stijl slecht past bij het beeldmodel (getekende stijlen met een fotomodel).</summary>
    private void ZetTip()
    {
        var model = Onderdelen.Gekozen(_model);
        _tip.Text = IsGroot(Onderdelen.Gekozen(_formaat)) && model is "realvis" or "juggernaut"
            ? "Tip: grote formaten werken alleen goed met FLUX en Z-Image. RealVisXL en Juggernaut gaan daarboven dingen dubbel tekenen; kies een gewoon formaat en gebruik Vergroot 2×."
            : Beeldstijl.Van(Onderdelen.Gekozen(_stijl)).Tip(model);
        _tip.Visibility = _tip.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _tip.Foreground = Bouw.Kleur("AccentBrush");
    }

    /// <summary>Een prompt van een ander scherm (bijvoorbeeld Beeld naar tekst › Maak er een beeld van).</summary>
    public void Ontvang(string tekst)
    {
        _omschrijving.Text = tekst;
        _status.Text = "Prompt overgenomen - pas hem aan en druk op Maak beeld.";
    }

    /// <summary>Haalt de galerij opnieuw op (ook via de verversknop in de actiebalk).</summary>
    public async Task VerversAsync()
    {
        try
        {
            var lijst = (await Beeldservice.LijstAsync("beeld")).Where(t => t.Bestand is not null && File.Exists(t.Bestand)).ToList();
            var handtekening = string.Join(",", lijst.Select(t => t.JobId));
            if (handtekening == _galerijHandtekening)
            {
                return;
            }

            _galerijHandtekening = handtekening;
            _galerijMelding.Text = lijst.Count == 0 ? "Nog niets gemaakt." : $"{lijst.Count} beelden, nieuwste links. Klik op een beeld om het groot te zien.";
            _galerij.Children.Clear();
            foreach (var taak in lijst)
            {
                _galerij.Children.Add(await GalerijItemAsync(taak));
            }
        }
        catch (Exception fout)
        {
            _galerijMelding.Text = $"De galerij kon niet geladen worden. {Beeldservice.Foutmelding(fout)}";
        }
    }

    private async Task<Button> GalerijItemAsync(BeeldJobStatus taak)
    {
        if (!_miniaturen.TryGetValue(taak.Bestand!, out var miniatuur) && await Onderdelen.LaadBeeldAsync(taak.Bestand, 220) is { } geladen)
        {
            _miniaturen[taak.Bestand!] = miniatuur = geladen;
        }

        var knop = new Button
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Bouw.Kleur("BackgroundBrush"),
            CornerRadius = new CornerRadius(6, 6, 6, 6),
            Content = new Image { Source = miniatuur, Height = 110, Stretch = Stretch.Uniform },
        };
        ToolTipService.SetToolTip(knop, taak.Prompt);
        knop.Click += async (_, _) =>
        {
            await ToonAsync(taak.Bestand!, taak.Prompt, taak);
            _omschrijving.Text = taak.Prompt;
        };
        return knop;
    }

    // ---------------------------------------------------------------- maken

    /// <summary>Vergroot het beeld dat nu groot staat 2× (Real-ESRGAN); de uitkomst komt als nieuw beeld in de galerij.</summary>
    private async Task VergrootAsync()
    {
        if (_bestand is null)
        {
            return;
        }

        ZetBezig(true);
        try
        {
            var prompt = _prompt ?? _omschrijving.Text.Trim();
            await VoerUitAsync(new BeeldOpdracht
            {
                Prompt = prompt.Length > 0 ? prompt : "Vergroot",
                BronBeeld = _bestand,
                Vergroot = true,
            }, null, async stand =>
            {
                await ToonAsync(stand.Bestand!, prompt, stand);
                _status.Text = $"Vergroot naar {stand.Breedte} × {stand.Hoogte}. Het origineel staat nog in de galerij.";
            });
        }
        finally
        {
            ZetBezig(false);
        }
    }

    /// <summary>Laat het chatmodel de omschrijving vertalen en verbeteren, en zet het resultaat terug in Omschrijving.</summary>
    private async Task VerbeterAsync()
    {
        var tekst = _omschrijving.Text.Trim();
        if (tekst.Length == 0)
        {
            _status.Text = "Typ eerst een omschrijving van het beeld.";
            return;
        }

        ZetBezig(true);
        try
        {
            _status.Text = "Omschrijving vertalen en verbeteren…";
            var (verbeterd, fout) = await Promptschrijver.VerbeterAsync(tekst, Promptsoort.Beeld, Onderdelen.Gekozen(_model));
            if (verbeterd is not null)
            {
                _omschrijving.Text = verbeterd;
                _status.Text = "Verbeterd - lees en pas aan, en druk dan op Maak beeld.";
            }
            else
            {
                _status.Text = $"Verbeteren mislukt ({fout}). Je eigen tekst staat er nog.";
            }
        }
        finally
        {
            ZetBezig(false);
        }
    }

    /// <summary>Zet de stijltekst van de gekozen stijl achter de omschrijving en de negatieve prompt van die stijl in Negatief.</summary>
    private void VoegStijlToe()
    {
        var stijl = Beeldstijl.Van(Onderdelen.Gekozen(_stijl));
        _omschrijving.Text = stijl.VervangAchter(_omschrijving.Text.Trim());
        _negatief.Text = stijl.Negatief;
        _status.Text = $"Stijl {stijl.Naam} toegevoegd.";
    }

    private async Task MaakAsync()
    {
        var tekst = _omschrijving.Text.Trim();
        if (tekst.Length == 0)
        {
            _status.Text = "Typ eerst een omschrijving van het beeld.";
            return;
        }

        if (!LeesSeed(out var seed))
        {
            return;
        }

        ZetBezig(true);
        string? opmerking = null;
        try
        {
            // Precies wat er staat: niets vertaald, geen stijltekst erbij (daarvoor zijn de knoppen Verbeter en Stijl toevoegen).
            var prompt = tekst;
            var formaat = Onderdelen.Gekozen(_formaat).Split('x');

            await VoerUitAsync(new BeeldOpdracht
            {
                Beeldmodel = Onderdelen.Gekozen(_model),
                Prompt = prompt,
                Negatief = _negatief.Text.Trim(),
                Breedte = int.Parse(formaat[0], Bouw.Nl),
                Hoogte = int.Parse(formaat[1], Bouw.Nl),
                Seed = seed,
            }, opmerking, async stand =>
            {
                await ToonAsync(stand.Bestand!, prompt, stand);
                _status.Text = $"{opmerking}Klaar - seed {stand.Seed}.";
            });
        }
        finally
        {
            ZetBezig(false);
        }
    }

    /// <summary>Start een opdracht bij de beeldservice en volgt hem tot hij klaar of mislukt is.</summary>
    private async Task VoerUitAsync(BeeldOpdracht opdracht, string? opmerking, Func<BeeldJobStatus, Task> klaar)
    {
        _status.Text = opmerking + "Opdracht starten…";
        try
        {
            _lopend = await Beeldservice.StartAsync(opdracht, new Progress<string>(t => _status.Text = opmerking + t));
            _stop.IsEnabled = true;
            App.AppLog.Schrijf("imagefunn", $"{(opdracht.BronBeeld is null ? "Beeld maken" : "Beeld bijsturen")} ({opdracht.Beeldmodel})");

            while (true)
            {
                var stand = await Beeldservice.StatusAsync(_lopend);
                _status.Text = opmerking + stand.Stap;
                _voortgang.Value = stand.Voortgang;

                if (stand.IsKlaar && stand.Bestand is not null)
                {
                    await klaar(stand);
                    App.AppLog.Schrijf("imagefunn", $"Beeld klaar (seed {stand.Seed})");
                    await VerversAsync();
                    return;
                }

                if (stand.IsMislukt)
                {
                    _status.Text = opmerking + stand.Eindmelding;
                    App.AppLog.Schrijf("imagefunn", $"Beeld: {stand.Eindmelding}");
                    return;
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
            _stop.IsEnabled = false;
        }
    }

    private bool LeesSeed(out int? seed)
    {
        seed = null;
        if (string.IsNullOrWhiteSpace(_seed.Text))
        {
            return true;
        }

        if (int.TryParse(_seed.Text.Trim(), out var s) && s >= 0)
        {
            seed = s;
            return true;
        }

        _status.Text = "De seed moet een heel getal zijn, of leeg.";
        return false;
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
        _maak.IsEnabled = _verbeterKnop.IsEnabled = _stijlKnop.IsEnabled = _pasToe.IsEnabled = _verwerk.IsEnabled = !bezig;
        _voortgang.Value = 0;
        _voortgang.Visibility = bezig ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- tonen, doorgeven en verwijderen

    private async Task ToonAsync(string bestand, string prompt, BeeldJobStatus? taak = null)
    {
        if (await Onderdelen.LaadBeeldAsync(bestand) is not { } beeld)
        {
            return;
        }

        _bestand = bestand;
        _prompt = prompt;
        _beeld.Source = beeld;
        _geenBeeld.Visibility = Visibility.Collapsed;
        _tekenlaag.Wis();
        _beeldInfo.Text = taak is null
            ? prompt
            : $"{taak.Breedte} × {taak.Hoogte} · {ModelNaam(taak.Beeldmodel)} · seed {taak.Seed}\n\n{prompt}";
        _infopaneel.Visibility = Visibility.Visible;
        ZetBeeldknoppen(true);
        ZetSessie(bestand, prompt);
    }

    private void ZetBeeldknoppen(bool aan)
    {
        _inMap.IsEnabled = _verwijder.IsEnabled = _naarVideo.IsEnabled = _naarTekst.IsEnabled = _naarFilm.IsEnabled = _vergroot.IsEnabled = aan;
    }

    private void Geef(string scherm)
    {
        if (_bestand is not null)
        {
            Doorgeven.Naar(scherm, _bestand);
        }
    }

    private async Task VerwijderAsync()
    {
        if (_bestand is null)
        {
            return;
        }

        var taak = (await Beeldservice.LijstAsync("beeld")).FirstOrDefault(t => string.Equals(t.Bestand, _bestand, StringComparison.OrdinalIgnoreCase));
        if (taak is null
            || !await Onderdelen.BevestigAsync(XamlRoot, "Dit beeld verwijderen?", $"{Onderdelen.Kort(taak.Prompt, 120)}\n\nHet bestand wordt definitief verwijderd; dit kan niet ongedaan gemaakt worden."))
        {
            return;
        }

        try
        {
            await Beeldservice.VerwijderAsync(taak.JobId);
        }
        catch (Exception fout)
        {
            _status.Text = $"Verwijderen mislukt: {Beeldservice.Foutmelding(fout)}";
            return;
        }

        _bestand = _prompt = null;
        _beeld.Source = null;
        _beeldInfo.Text = "";
        _infopaneel.Visibility = Visibility.Collapsed;
        _geenBeeld.Visibility = Visibility.Visible;
        ZetBeeldknoppen(false);
        WisSessie();
        _status.Text = "Beeld verwijderd.";
        await VerversAsync();
    }

    private static string ModelNaam(string tag) =>
        Beeldmodellen.FirstOrDefault(m => m.Tag == tag).Naam?.Split(" - ")[0] ?? (tag.Length > 0 ? tag : "standaardmodel");
}
