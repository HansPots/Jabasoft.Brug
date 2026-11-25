using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// Een doorzichtige laag over het getoonde beeld waarop je met de muis een gebied aantekent (rood, half doorzichtig),
/// voor bijsturen met een masker: alleen dat gebied wordt opnieuw gemaakt. WinUI 3 heeft geen InkCanvas, dus de
/// strepen zijn gewone lijnen; met de gum tik je een streep aan om hem weg te halen.
/// <see cref="MaakMaskerAsync"/> zet de strepen om in een zwart-wit png op de pixelmaat van het beeld (wit = mag veranderen).
/// </summary>
public sealed class Tekenlaag : Canvas
{
    private readonly List<(Polyline Lijn, List<Point> Punten)> _strepen = [];
    private (Polyline Lijn, List<Point> Punten)? _bezig;

    public Tekenlaag()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        PointerPressed += Begin;
        PointerMoved += Verder;
        PointerReleased += (_, e) => Stop(e);
        PointerCanceled += (_, e) => Stop(e);
    }

    /// <summary>Dikte van het penseel, in schermpunten.</summary>
    public double Penseel { get; set; } = 60;

    /// <summary>Aan: een tik haalt de streep eronder weg in plaats van te tekenen.</summary>
    public bool Gum { get; set; }

    public bool HeeftGebied => _strepen.Count > 0;

    public void Wis()
    {
        _strepen.Clear();
        Children.Clear();
    }

    private void Begin(object sender, PointerRoutedEventArgs e)
    {
        var punt = e.GetCurrentPoint(this).Position;
        if (Gum)
        {
            var raak = _strepen.LastOrDefault(s => s.Punten.Any(p => Afstand(p, punt) <= s.Lijn.StrokeThickness / 2 + 4));
            if (raak.Lijn is not null)
            {
                _strepen.Remove(raak);
                Children.Remove(raak.Lijn);
            }

            return;
        }

        var lijn = new Polyline
        {
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(150, 255, 60, 60)),
            StrokeThickness = Penseel,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        var punten = new List<Point> { punt, new(punt.X + 0.1, punt.Y + 0.1) };
        lijn.Points.Add(punten[0]);
        lijn.Points.Add(punten[1]);
        Children.Add(lijn);
        _bezig = (lijn, punten);
        CapturePointer(e.Pointer);
    }

    private void Verder(object sender, PointerRoutedEventArgs e)
    {
        if (_bezig is not { } streep)
        {
            return;
        }

        var punt = e.GetCurrentPoint(this).Position;
        streep.Lijn.Points.Add(punt);
        streep.Punten.Add(punt);
    }

    private void Stop(PointerRoutedEventArgs e)
    {
        if (_bezig is { } streep)
        {
            _strepen.Add(streep);
            _bezig = null;
            ReleasePointerCapture(e.Pointer);
        }
    }

    private static double Afstand(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// Schrijft het masker als png op <paramref name="breedte"/> × <paramref name="hoogte"/> pixels: de laag ligt
    /// precies over het getoonde beeld, dus schermpunten worden met dezelfde schaal pixels van het beeld.
    /// </summary>
    public async Task MaakMaskerAsync(int breedte, int hoogte, string pad)
    {
        var schaal = ActualWidth > 0 ? breedte / ActualWidth : 1.0;
        var masker = new byte[breedte * hoogte];
        foreach (var (lijn, punten) in _strepen)
        {
            var straal = Math.Max(1, lijn.StrokeThickness * schaal / 2);
            for (var i = 0; i < punten.Count; i++)
            {
                var a = punten[i];
                var b = punten[Math.Min(i + 1, punten.Count - 1)];
                var stappen = Math.Max(1, (int)(Afstand(a, b) * schaal / (straal / 3)));
                for (var s = 0; s <= stappen; s++)
                {
                    var t = (double)s / stappen;
                    Stempel(masker, breedte, hoogte, (a.X + (b.X - a.X) * t) * schaal, (a.Y + (b.Y - a.Y) * t) * schaal, straal);
                }
            }
        }

        var pixels = new byte[breedte * hoogte * 4];
        for (var i = 0; i < masker.Length; i++)
        {
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = masker[i];
            pixels[i * 4 + 3] = 255;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(pad)!);
        var map = await StorageFolder.GetFolderFromPathAsync(System.IO.Path.GetDirectoryName(pad)!);
        var bestand = await map.CreateFileAsync(System.IO.Path.GetFileName(pad), CreationCollisionOption.ReplaceExisting);
        using var stroom = await bestand.OpenAsync(FileAccessMode.ReadWrite);
        var schrijver = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stroom);
        schrijver.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)breedte, (uint)hoogte, 96, 96, pixels);
        await schrijver.FlushAsync();
    }

    private static void Stempel(byte[] masker, int breedte, int hoogte, double cx, double cy, double straal)
    {
        var r2 = straal * straal;
        for (var y = Math.Max(0, (int)(cy - straal)); y <= Math.Min(hoogte - 1, (int)(cy + straal)); y++)
        {
            for (var x = Math.Max(0, (int)(cx - straal)); x <= Math.Min(breedte - 1, (int)(cx + straal)); x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r2)
                {
                    masker[y * breedte + x] = 255;
                }
            }
        }
    }
}
