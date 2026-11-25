using System.Diagnostics;
using Jabasoft.Brug.App.Layout;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// ImageFunn &gt; Tekst naar beeld: een beeld maken uit een omschrijving.
///
/// Links de opdracht (omschrijving, stijl, beeldmodel, formaat, seed), rechts
/// het gemaakte beeld, onderaan de galerij met alles wat eerder gemaakt is.
/// De beeldservice doet het werk (zie <see cref="Beeldservice"/>); dit scherm
/// start de opdracht, volgt de voortgang en toont het resultaat.
///
/// Opbouw volgens de afspraak voor elke pagina: alles staat er meteen, de
/// galerij toont "Ophalen…" en vult zich zodra de service antwoordt.
/// Nog niet overgenomen uit de oude app: bijsturen (tekenen op het beeld),
/// automatisch vertalen via het chatmodel en doorgeven naar de andere
/// schermen; die volgen in een volgende stap.
/// </summary>
public sealed class TekstNaarBeeld : UserControl, IVerversbaar
{
    private static readonly TimeSpan Peilen = TimeSpan.FromSeconds(0.7);

    private static readonly (string Tag, string Naam)[] Beeldmodellen =
    [
        ("realvis", "RealVisXL - fotorealistisch"),
        ("juggernaut", "Juggernaut XL - fotorealistisch"),
        ("flux", "FLUX.1-schnell - snel, 4 stappen"),
    ];

    private static readonly (string Tag, string Naam)[] Formaten =
    [
        ("1024x1024", "Vierkant 1024 × 1024"),
        ("1344x768", "Liggend 1344 × 768"),
        ("768x1344", "Staand 768 × 1344"),
        ("768x768", "Klein vierkant 768 × 768 (sneller)"),
    ];

    private readonly TextBox _omschrijving;
    private readonly ComboBox _stijl;
    private readonly ComboBox _model;
    private readonly ComboBox _formaat;
    private readonly TextBox _seed;
    private readonly Button _maak;
    private readonly Button _letterlijk;
    private readonly Button _stop;
    private readonly ProgressBar _voortgang;
    private readonly TextBlock _status;

    private readonly Image _beeld;
    private readonly TextBlock _geenBeeld;
    private readonly TextBlock _beeldInfo;
    private readonly Button _inMap;
    private readonly Button _verwijder;

    private readonly StackPanel _galerij;
    private readonly TextBlock _galerijMelding;
    private readonly Dictionary<string, BitmapImage> _miniaturen = [];
    private string? _galerijHandtekening;

    private BeeldJobStatus? _getoond;
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
        _stijl = Keuze("Stijl", Beeldstijl.Alle.Select(s => (s.Tag, s.Naam)), "realistisch");
        _model = Keuze("Beeldmodel", Beeldmodellen, Voorkeuren.Lees(Beeldservice.VoorkeurBeeldmodel, "realvis"));
        _formaat = Keuze("Formaat", Formaten, "1024x1024");
        _seed = new TextBox { Header = "Seed (leeg = willekeurig)", PlaceholderText = "willekeurig" };
        ToolTipService.SetToolTip(_seed, "Dezelfde omschrijving met dezelfde seed geeft hetzelfde beeld.");

        _maak = Bouw.Knop("Maak beeld", async (_, _) => await MaakAsync(letterlijk: false));
        _letterlijk = Bouw.Knop("Letterlijk", async (_, _) => await MaakAsync(letterlijk: true), "AccentBrushLight");
        ToolTipService.SetToolTip(_letterlijk, "Je tekst gaat precies zo naar het model, zonder stijltekst erbij.");
        _stop = Bouw.Knop("Stop", async (_, _) => await StopAsync(), "TextMutedBrush");
        _stop.IsEnabled = false;

        _voortgang = new ProgressBar { Maximum = 1, Foreground = Bouw.Kleur("AccentBrush"), Visibility = Visibility.Collapsed };
        _status = Bouw.Melding("Klaar voor een opdracht.");

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
                _omschrijving,
                keuzes,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _maak, _letterlijk, _stop } },
                _voortgang,
                _status,
            },
        };

        // ---- beeld
        _beeld = new Image { Stretch = Stretch.Uniform, MaxHeight = 720, HorizontalAlignment = HorizontalAlignment.Center };
        _geenBeeld = Bouw.Melding("Nog geen beeld. Maak er een, of kies er een uit de galerij.");
        _beeldInfo = Bouw.Melding("");
        _inMap = Bouw.Knop("Toon in map", (_, _) => ToonInMap());
        _verwijder = Bouw.Knop("Verwijderen", async (_, _) => await VerwijderAsync(), "TextMutedBrush");
        _inMap.IsEnabled = _verwijder.IsEnabled = false;

        var beeldvlak = new Grid { MinHeight = 420 };
        beeldvlak.Children.Add(_geenBeeld);
        beeldvlak.Children.Add(_beeld);
        var beeld = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                beeldvlak,
                _beeldInfo,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _inMap, _verwijder } },
            },
        };

        // ---- galerij
        _galerijMelding = Bouw.Melding("Ophalen…");
        _galerij = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
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
        var links = Bouw.Paneel("Opdracht", opdracht);
        var rechts = Bouw.Paneel("Beeld", beeld);
        Grid.SetColumn(rechts, 1);
        boven.Children.Add(links);
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
        if (!_miniaturen.TryGetValue(taak.Bestand!, out var miniatuur) && await LaadAsync(taak.Bestand!, 220) is { } geladen)
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
        knop.Click += async (_, _) => await ToonAsync(taak);
        return knop;
    }

    // ---------------------------------------------------------------- maken

    private async Task MaakAsync(bool letterlijk)
    {
        var tekst = _omschrijving.Text.Trim();
        if (tekst.Length == 0)
        {
            _status.Text = "Typ eerst een omschrijving van het beeld.";
            return;
        }

        int? seed = null;
        if (!string.IsNullOrWhiteSpace(_seed.Text))
        {
            if (!int.TryParse(_seed.Text.Trim(), out var s) || s < 0)
            {
                _status.Text = "De seed moet een heel getal zijn, of leeg.";
                return;
            }

            seed = s;
        }

        // De gekozen stijl komt vóór de omschrijving, met een eigen negatieve prompt. Letterlijk: niets erbij.
        var stijl = Beeldstijl.Van(letterlijk ? "geen" : Gekozen(_stijl));
        var prompt = stijl.MetStijl(tekst);
        var formaat = Gekozen(_formaat).Split('x');

        ZetBezig(true);
        _status.Text = "Opdracht starten…";
        try
        {
            var melding = new Progress<string>(t => _status.Text = t);
            _lopend = await Beeldservice.StartAsync(new BeeldOpdracht
            {
                Beeldmodel = Gekozen(_model),
                Prompt = prompt,
                Negatief = stijl.Negatief,
                Breedte = int.Parse(formaat[0], Bouw.Nl),
                Hoogte = int.Parse(formaat[1], Bouw.Nl),
                Seed = seed,
            }, melding);
            _stop.IsEnabled = true;
            App.AppLog.Schrijf("imagefunn", $"Beeld maken ({Gekozen(_model)})");

            while (true)
            {
                var stand = await Beeldservice.StatusAsync(_lopend);
                _status.Text = stand.Stap;
                _voortgang.Value = stand.Voortgang;

                if (stand.IsKlaar)
                {
                    await ToonAsync(stand);
                    _status.Text = $"Klaar - seed {stand.Seed}.";
                    App.AppLog.Schrijf("imagefunn", $"Beeld klaar (seed {stand.Seed})");
                    await VerversAsync();
                    break;
                }

                if (stand.IsMislukt)
                {
                    _status.Text = stand.Eindmelding;
                    App.AppLog.Schrijf("imagefunn", $"Beeld: {stand.Eindmelding}");
                    break;
                }

                await Task.Delay(Peilen);
            }
        }
        catch (Exception fout)
        {
            _status.Text = Beeldservice.Foutmelding(fout);
        }
        finally
        {
            _lopend = null;
            ZetBezig(false);
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

    // ---------------------------------------------------------------- tonen en verwijderen

    private async Task ToonAsync(BeeldJobStatus taak)
    {
        if (taak.Bestand is null || await LaadAsync(taak.Bestand, null) is not { } beeld)
        {
            return;
        }

        _getoond = taak;
        _beeld.Source = beeld;
        _geenBeeld.Visibility = Visibility.Collapsed;
        _beeldInfo.Text = $"{taak.Breedte} × {taak.Hoogte} · {ModelNaam(taak.Beeldmodel)} · seed {taak.Seed}\n{taak.Prompt}";
        _inMap.IsEnabled = _verwijder.IsEnabled = true;
    }

    private void ToonInMap()
    {
        if (_getoond?.Bestand is { } bestand && File.Exists(bestand))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{bestand}\"") { UseShellExecute = true });
        }
    }

    private async Task VerwijderAsync()
    {
        if (_getoond is not { } taak)
        {
            return;
        }

        var kort = taak.Prompt.Length > 120 ? taak.Prompt[..120] + "…" : taak.Prompt;
        var vraag = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Dit beeld verwijderen?",
            Content = $"{kort}\n\nHet bestand wordt definitief verwijderd; dit kan niet ongedaan gemaakt worden.",
            PrimaryButtonText = "Verwijderen",
            CloseButtonText = "Annuleren",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await vraag.ShowAsync() != ContentDialogResult.Primary)
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

        _getoond = null;
        _beeld.Source = null;
        _beeldInfo.Text = "";
        _geenBeeld.Visibility = Visibility.Visible;
        _inMap.IsEnabled = _verwijder.IsEnabled = false;
        _status.Text = "Beeld verwijderd.";
        await VerversAsync();
    }

    // ---------------------------------------------------------------- hulp

    /// <summary>Laadt een beeld zonder het bestand vast te houden; <paramref name="breedte"/> houdt miniaturen klein.</summary>
    private static async Task<BitmapImage?> LaadAsync(string pad, int? breedte)
    {
        try
        {
            var beeld = new BitmapImage();
            if (breedte is { } b)
            {
                beeld.DecodePixelWidth = b;
            }

            var bytes = await File.ReadAllBytesAsync(pad);
            using var stroom = new MemoryStream(bytes);
            await beeld.SetSourceAsync(stroom.AsRandomAccessStream());
            return beeld;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ComboBox Keuze(string kop, IEnumerable<(string Tag, string Naam)> keuzes, string gekozen)
    {
        var keuze = new ComboBox { Header = kop, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (tag, naam) in keuzes)
        {
            var item = new ComboBoxItem { Content = naam, Tag = tag };
            keuze.Items.Add(item);
            if (tag == gekozen)
            {
                keuze.SelectedItem = item;
            }
        }

        if (keuze.SelectedIndex < 0)
        {
            keuze.SelectedIndex = 0;
        }

        return keuze;
    }

    private static string Gekozen(ComboBox keuze) => (keuze.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private static string ModelNaam(string tag) =>
        Beeldmodellen.FirstOrDefault(m => m.Tag == tag).Naam?.Split(" - ")[0] ?? (tag.Length > 0 ? tag : "standaardmodel");
}
