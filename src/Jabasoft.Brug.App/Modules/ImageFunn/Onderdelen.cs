using System.Diagnostics;
using Jabasoft.Brug.App.Layout;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// Kleine onderdelen die alle ImageFunn-schermen gebruiken: keuzelijsten met
/// een sleutel per keuze, beelden laden zonder het bestand vast te houden,
/// een beeld kiezen, bevestigen, de map openen en het vinkje "vertalen".
/// </summary>
public static class Onderdelen
{
    public static readonly string[] Beeldextensies = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];

    public static readonly (string Tag, string Naam)[] Videomodellen =
    [
        ("wan", "Wan 2.2 - beste kwaliteit, trager"),
        ("wan_snel", "Wan 2.2 snel - 3 stappen"),
    ];

    public static readonly (string Tag, string Naam)[] Duren =
    [
        ("49", "Kort (±2 sec)"),
        ("97", "Normaal (±4 sec)"),
        ("145", "Lang (±6 sec)"),
    ];

    /// <summary>Een keuzelijst; elke keuze draagt een sleutel (Tag). Lees de keuze met <see cref="Gekozen"/>.</summary>
    public static ComboBox Keuze(string kop, IEnumerable<(string Tag, string Naam)> keuzes, string gekozen)
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

        if (keuze.SelectedIndex < 0 && keuze.Items.Count > 0)
        {
            keuze.SelectedIndex = 0;
        }

        return keuze;
    }

    public static string Gekozen(ComboBox keuze) => (keuze.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>Zet de keuze met deze sleutel, als die er is.</summary>
    public static void Kies(ComboBox keuze, string tag)
    {
        if (keuze.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == tag) is { } item)
        {
            keuze.SelectedItem = item;
        }
    }

    /// <summary>
    /// "Vertalen en verbeteren via het chatmodel": standaard uit, en per scherm onthouden.
    /// De modellen lezen alleen Engels; dit laat het chatmodel de omschrijving eerst omschrijven.
    /// </summary>
    public static CheckBox Verbetervinkje(string scherm)
    {
        var sleutel = $"ImageFunn.Verbeteren.{scherm}";
        var vinkje = new CheckBox
        {
            Content = "Eerst vertalen en verbeteren (chatmodel)",
            IsChecked = Voorkeuren.Lees(sleutel) == "ja",
        };
        ToolTipService.SetToolTip(vinkje, "Het beeld- en videomodel begrijpen alleen Engels. Hiermee schrijft het chatmodel je omschrijving eerst om.");
        vinkje.Checked += (_, _) => Voorkeuren.Bewaar(sleutel, "ja");
        vinkje.Unchecked += (_, _) => Voorkeuren.Bewaar(sleutel, "nee");
        return vinkje;
    }

    /// <summary>Laadt een beeld zonder het bestand vast te houden; <paramref name="breedte"/> houdt miniaturen klein.</summary>
    public static async Task<BitmapImage?> LaadBeeldAsync(string? pad, int? breedte = null)
    {
        if (string.IsNullOrEmpty(pad) || !File.Exists(pad))
        {
            return null;
        }

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

    /// <summary>Laat de gebruiker een beeldbestand kiezen; null als er niets gekozen is.</summary>
    public static async Task<string?> KiesBeeldAsync()
    {
        var kiezer = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var extensie in Beeldextensies)
        {
            kiezer.FileTypeFilter.Add(extensie);
        }

        if (App.Hoofdvenster is { } venster)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(kiezer, WinRT.Interop.WindowNative.GetWindowHandle(venster));
        }

        var bestand = await kiezer.PickSingleFileAsync();
        return bestand?.Path;
    }

    /// <summary>Laat de gebruiker een JSON-bestand kiezen (openen) of een plek kiezen (opslaan).</summary>
    public static async Task<string?> KiesVerhaalAsync(bool opslaan, string voorstel = "verhaal")
    {
        var hwnd = App.Hoofdvenster is { } venster ? WinRT.Interop.WindowNative.GetWindowHandle(venster) : 0;
        if (opslaan)
        {
            var kiezer = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = voorstel };
            kiezer.FileTypeChoices.Add("Verhaal (JSON)", [".json"]);
            if (hwnd != 0)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(kiezer, hwnd);
            }

            return (await kiezer.PickSaveFileAsync())?.Path;
        }

        var open = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        open.FileTypeFilter.Add(".json");
        if (hwnd != 0)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(open, hwnd);
        }

        return (await open.PickSingleFileAsync())?.Path;
    }

    /// <summary>Het eerste beeldbestand uit een sleepactie, of null.</summary>
    public static async Task<string?> GesleeptBeeldAsync(DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return null;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        return items.OfType<StorageFile>()
            .Select(f => f.Path)
            .FirstOrDefault(p => Beeldextensies.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Maakt van een vlak een plek waar je een beeld op kunt slepen of op kunt klikken om er een te kiezen.</summary>
    public static void MaakSleepvlak(FrameworkElement vlak, Func<string, Task> beeldGekozen)
    {
        vlak.AllowDrop = true;
        vlak.DragOver += (_, e) => e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        vlak.Drop += async (_, e) =>
        {
            if (await GesleeptBeeldAsync(e) is { } pad)
            {
                await beeldGekozen(pad);
            }
        };
        vlak.Tapped += async (_, _) =>
        {
            if (await KiesBeeldAsync() is { } pad)
            {
                await beeldGekozen(pad);
            }
        };
    }

    /// <summary>Vraagt bevestiging voor iets dat niet ongedaan gemaakt kan worden.</summary>
    public static async Task<bool> BevestigAsync(XamlRoot root, string titel, string tekst, string ja = "Verwijderen")
    {
        var vraag = new ContentDialog
        {
            XamlRoot = root,
            Title = titel,
            Content = tekst,
            PrimaryButtonText = ja,
            CloseButtonText = "Annuleren",
            DefaultButton = ContentDialogButton.Close,
        };
        return await vraag.ShowAsync() == ContentDialogResult.Primary;
    }

    public static void ToonInMap(string? bestand)
    {
        if (bestand is not null && File.Exists(bestand))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{bestand}\"") { UseShellExecute = true });
        }
    }

    public static string Kort(string tekst, int lengte) => tekst.Length > lengte ? tekst[..lengte] + "…" : tekst;

    /// <summary>Een voortgangsbalk in de themakleur, verborgen tot er iets loopt.</summary>
    public static ProgressBar Voortgang() => new() { Maximum = 1, Foreground = Bouw.Kleur("AccentBrush"), Visibility = Visibility.Collapsed };

    /// <summary>Een rij knoppen naast elkaar.</summary>
    public static StackPanel Knoppen(params UIElement[] knoppen)
    {
        var rij = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var knop in knoppen)
        {
            rij.Children.Add(knop);
        }

        return rij;
    }
}

/// <summary>Een scherm dat een beeld van een ander ImageFunn-scherm kan overnemen (bijvoorbeeld "Laat dit beeld bewegen").</summary>
public interface IOntvangtBeeld
{
    Task OntvangAsync(string pad);
}

/// <summary>
/// Een beeld naar een ander ImageFunn-scherm sturen. Het venster opent dat
/// scherm (zie MainWindow) en geeft het beeld door.
/// </summary>
public static class Doorgeven
{
    public static event EventHandler<(string Scherm, string Pad)>? Gevraagd;

    public static void Naar(string scherm, string pad) => Gevraagd?.Invoke(null, (scherm, pad));
}
