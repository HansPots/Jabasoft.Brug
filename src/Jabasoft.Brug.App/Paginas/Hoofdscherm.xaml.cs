using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Jabasoft.Brug.App.Paginas;

/// <summary>
/// Het hoofdscherm met de brug-afbeelding (Afbeeldingen\brug.svg, getekend
/// door tools\maak_brug.py). De Brug is unpackaged, dus de SVG wordt uit de
/// uitvoermap gelezen in plaats van via ms-appx.
/// </summary>
public sealed partial class Hoofdscherm : UserControl
{
    public Hoofdscherm()
    {
        InitializeComponent();
        _ = LaadBrugAsync();
    }

    private async Task LaadBrugAsync()
    {
        var pad = Path.Combine(AppContext.BaseDirectory, "Afbeeldingen", "brug.svg");
        if (!File.Exists(pad))
        {
            App.AppLog.Schrijf("brug", "Brug-afbeelding niet gevonden");
            return;
        }

        try
        {
            using var stroom = File.OpenRead(pad);
            var bron = new SvgImageSource();
            await bron.SetSourceAsync(stroom.AsRandomAccessStream());
            Brug.Source = bron;
        }
        catch (Exception fout)
        {
            App.AppLog.Schrijf("brug", $"Brug-afbeelding laden mislukt: {fout.Message}");
        }
    }
}
