using Jabasoft.Brug.App.Diensten;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// ImageFunn &gt; Beeld naar tekst: een beeld laten beschrijven, de tekst eruit
/// laten lezen of er een prompt van laten maken om het na te maken.
///
/// Het beeld komt van kiezen, slepen, plakken (Ctrl+V) of een ander scherm.
/// Het beeldmodel uit Broker › Instellingen leest het via de broker; eerst
/// geeft de beeldservice het videogeheugen vrij. Overgenomen uit de oude
/// ImageFunn-app (BeeldNaarTekst + Beeldlezer), met dezelfde instructies.
/// </summary>
public sealed class BeeldNaarTekst : UserControl, IOntvangtBeeld
{
    private static readonly (string Tag, string Naam)[] Doelen =
    [
        ("beschrijving", "Beschrijving"),
        ("ocr", "Tekst uitlezen (OCR)"),
        ("prompt", "Prompt voor de beeldgenerator"),
    ];

    private readonly Image _beeld = new() { Stretch = Stretch.Uniform, MaxHeight = 520 };
    private readonly TextBlock _beeldTekst = Bouw.Melding("Klik, sleep een beeld hierheen of plak er een met Ctrl+V.");
    private readonly ComboBox _doel = Onderdelen.Keuze("Wat wil je weten?", Doelen, "beschrijving");
    private readonly TextBox _wens = new() { Header = "Extra wens (mag leeg)", PlaceholderText = "Bijvoorbeeld: let vooral op de kleding" };
    private readonly Button _lees;
    private readonly TextBlock _status = Bouw.Melding("Klaar.");
    private readonly TextBox _resultaat;
    private readonly Button _kopieer;
    private readonly Button _naarBeeld;
    private string? _pad;

    public BeeldNaarTekst()
    {
        var kader = new Border
        {
            BorderBrush = Bouw.Kleur("BorderBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10, 10, 10, 10),
            Padding = new Thickness(10),
            Background = Bouw.Kleur("BackgroundBrush"),
            MinHeight = 320,
            Child = new Grid { Children = { _beeldTekst, _beeld } },
        };
        Onderdelen.MaakSleepvlak(kader, OntvangAsync);

        _lees = Bouw.Knop("Lees beeld", async (_, _) => await LeesAsync());
        var links = new StackPanel { Spacing = 16, Children = { kader, _doel, _wens, _lees, _status } };

        _resultaat = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = false,
            MinHeight = 420,
            PlaceholderText = "Hier komt wat het model ziet.",
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_resultaat, ScrollBarVisibility.Auto);
        _kopieer = Bouw.Knop("Kopieer", (_, _) => Kopieer());
        _naarBeeld = Bouw.Knop("Maak er een beeld van", (_, _) => Doorgeven.TekstNaar("imagefunn.tekstnaarbeeld", _resultaat.Text.Trim()));
        _kopieer.IsEnabled = _naarBeeld.IsEnabled = false;
        var rechts = new StackPanel { Spacing = 12, Children = { _resultaat, Onderdelen.Knoppen(_kopieer, _naarBeeld) } };

        var raster = new Grid { ColumnSpacing = 24 };
        raster.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(520) });
        raster.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var resultaat = Bouw.Paneel("Resultaat", rechts);
        Grid.SetColumn(resultaat, 1);
        raster.Children.Add(Bouw.Paneel("Beeld", links));
        raster.Children.Add(resultaat);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel { Margin = new Thickness(32, 24, 32, 32), Spacing = 24, Children = { raster } },
        };

        // Ctrl+V plakt een beeld van het klembord (bijvoorbeeld een screenshot).
        var plakken = new KeyboardAccelerator { Key = VirtualKey.V, Modifiers = VirtualKeyModifiers.Control };
        plakken.Invoked += async (_, e) =>
        {
            if (await PlakAsync())
            {
                e.Handled = true;
            }
        };
        KeyboardAccelerators.Add(plakken);
    }

    public async Task OntvangAsync(string pad)
    {
        var beeld = await Onderdelen.LaadBeeldAsync(pad);
        if (beeld is null)
        {
            _status.Text = "Dat beeld kon niet geopend worden (png, jpg, bmp of webp).";
            return;
        }

        _pad = pad;
        _beeld.Source = beeld;
        _beeldTekst.Visibility = Visibility.Collapsed;
        _status.Text = $"{Path.GetFileName(pad)} - {beeld.PixelWidth} × {beeld.PixelHeight}";
    }

    /// <summary>Bewaart een beeld van het klembord in de gegevensmap (ImageFunn\geplakt) en neemt het over.</summary>
    private async Task<bool> PlakAsync()
    {
        var inhoud = Clipboard.GetContent();
        if (!inhoud.Contains(StandardDataFormats.Bitmap))
        {
            return false;
        }

        var bron = await inhoud.GetBitmapAsync();
        using var stroom = await bron.OpenReadAsync();
        var pad = Gegevensmap.Bestand(Path.Combine("ImageFunn", "geplakt"), $"geplakt-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        // Omzetten naar png: het klembord geeft vaak een bmp.
        var lezer = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stroom);
        var pixels = await lezer.GetSoftwareBitmapAsync();
        var map = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(pad)!);
        var bestand = await map.CreateFileAsync(Path.GetFileName(pad), Windows.Storage.CreationCollisionOption.GenerateUniqueName);
        using (var uit = await bestand.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
        {
            var schrijver = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, uit);
            schrijver.SetSoftwareBitmap(pixels);
            await schrijver.FlushAsync();
        }

        await OntvangAsync(bestand.Path);
        return true;
    }

    private async Task LeesAsync()
    {
        if (_pad is null)
        {
            _status.Text = "Kies eerst een beeld.";
            return;
        }

        var doel = Onderdelen.Gekozen(_doel);
        _lees.IsEnabled = _kopieer.IsEnabled = _naarBeeld.IsEnabled = false;
        _resultaat.Text = "";
        _status.Text = "Videogeheugen vrijmaken…";
        try
        {
            // Het beeldmodel moet in het videogeheugen passen; de beeldservice houdt daar na een opdracht nog zijn
            // eigen modellen vast. Loopt er nog een beeld- of video-opdracht, dan toch proberen.
            var vrij = await Beeldservice.MaakVideogeheugenVrijAsync();
            _status.Text = "Het model bekijkt het beeld…";

            var vraag = string.IsNullOrWhiteSpace(_wens.Text)
                ? "Voer de opdracht uit op deze afbeelding."
                : $"Voer de opdracht uit op deze afbeelding. Extra wens van de gebruiker: {_wens.Text.Trim()}";
            var base64 = Convert.ToBase64String(await File.ReadAllBytesAsync(_pad));
            App.AppLog.Schrijf("imagefunn", $"Beeld lezen ({doel})");

            var (tekst, fout, model) = await Taalvraag.VraagAsync(
                "beeldModel",
                [
                    new("system", Instructie(doel) + (doel == "prompt" ? "" : " Antwoord in het Nederlands.")),
                    new("user", vraag, [base64]),
                ],
                $"Beeld naar tekst ({Doelen.First(d => d.Tag == doel).Naam})",
                temperatuur: doel == "ocr" ? 0.0 : 0.3,
                limiet: TimeSpan.FromMinutes(10));

            if (tekst is null)
            {
                _status.Text = $"Mislukt: {fout}" + (vrij ? "" : " Er liep nog een beeld- of video-opdracht; probeer het opnieuw als die klaar is.");
                return;
            }

            _resultaat.Text = tekst;
            _status.Text = $"Klaar - gelezen door {model}.";
            _kopieer.IsEnabled = _naarBeeld.IsEnabled = true;
        }
        catch (Exception fout)
        {
            _status.Text = $"Er ging iets mis: {fout.Message}";
        }
        finally
        {
            _lees.IsEnabled = true;
        }
    }

    private void Kopieer()
    {
        var pakket = new DataPackage();
        pakket.SetText(_resultaat.Text);
        Clipboard.SetContent(pakket);
        _status.Text = "Gekopieerd naar het klembord.";
    }

    private static string Instructie(string doel) => doel switch
    {
        "ocr" =>
            "Je leest tekst uit een afbeelding (OCR). Neem ALLE zichtbare tekst letterlijk over, teken voor teken, " +
            "in leesvolgorde (boven naar beneden, links naar rechts) en behoud regelafbreking, opsommingen en tabellen " +
            "(tabellen als markdown-tabel). Vertaal niets, corrigeer niets en vat niets samen. Is een stuk tekst " +
            "onleesbaar of afgekapt, schrijf dan [onleesbaar] op die plek in plaats van te gokken. Staat er geen " +
            "tekst in de afbeelding, zeg dat dan in één zin.",

        "prompt" =>
            "Je schrijft een prompt voor een tekst-naar-beeld-model (zoals FLUX of Stable Diffusion) waarmee dit " +
            "beeld zo getrouw mogelijk nagemaakt kan worden. Schrijf ÉÉN doorlopende prompt in het ENGELS, geen " +
            "opsomming en geen uitleg eromheen. Volgorde: onderwerp en handeling, details van personen/objecten " +
            "(kleding, kleuren, materialen), omgeving en achtergrond, compositie en camerastandpunt, lenskeuze en " +
            "scherptediepte, lichtopbouw en sfeer, kleurenpalet, en tot slot de stijl of het medium (foto, olieverf, " +
            "3D-render, illustratie…). Wees concreet en beschrijf alleen wat je ziet. Geef niets weer als tekst in " +
            "het beeld tenzij die echt onderdeel van het beeld is, dan tussen aanhalingstekens.",

        _ =>
            "Je beschrijft een afbeelding volledig en feitelijk. Behandel, in deze volgorde: (1) in één zin wat het " +
            "is (foto, screenshot, tekening, schilderij, diagram…); (2) het onderwerp en wat er gebeurt; (3) alle " +
            "personen, dieren en objecten met kleuren, kleding, houding en onderlinge posities; (4) de omgeving en " +
            "achtergrond; (5) compositie, licht, sfeer en stijl; (6) alle zichtbare tekst letterlijk, tussen " +
            "aanhalingstekens. Schrijf alleen wat je ziet, geef aan wat onzeker of onleesbaar is in plaats van te " +
            "gokken, en doe geen aannames over wat niet zichtbaar is.",
    };
}
