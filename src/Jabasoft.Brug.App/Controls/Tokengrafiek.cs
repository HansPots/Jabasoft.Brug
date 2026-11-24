using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Jabasoft.Brug.App.Controls;

/// <summary>
/// Het verloop van het tokenverbruik: een lijn per model met de tokens per
/// dag, zoals de Tokengrafiek van het oude Jabasoft.App - nu in WinUI en
/// in de LCARS-themakleuren.
///
/// Dagen zonder verbruik tellen als nul mee, anders zou een lijn een stille
/// dag overbruggen alsof er iets gebeurd is. Wordt in code getekend op een
/// Canvas, en opnieuw als de breedte verandert.
/// </summary>
public sealed class Tokengrafiek : UserControl
{
    /// <summary>Lijnkleuren uit het thema; de zwaarste lijn krijgt de accentkleur.</summary>
    private static readonly string[] Kleuren =
        ["#FF9900", "#9999CC", "#CC6666", "#FFCC99", "#8E89FF", "#CC7A00", "#FFB16C", "#B63C50"];

    private const double MargeLinks = 64;
    private const double MargeRechts = 16;
    private const double MargeBoven = 10;
    private const double MargeOnder = 26;

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private readonly Canvas _vlak = new() { Height = 240 };
    private readonly StackPanel _legenda = new() { Orientation = Orientation.Horizontal, Spacing = 24 };

    private IReadOnlyDictionary<string, Dictionary<DateTime, long>> _data = new Dictionary<string, Dictionary<DateTime, long>>();
    private DateTime _van;
    private DateTime _tot;

    public Tokengrafiek()
    {
        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _vlak,
                new ScrollViewer
                {
                    Content = _legenda,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
            },
        };

        _vlak.SizeChanged += (_, _) => Teken();
    }

    /// <summary>Per model de tokens per dag, over de dagen <paramref name="van"/> tot en met <paramref name="tot"/>.</summary>
    public void Toon(IReadOnlyDictionary<string, Dictionary<DateTime, long>> perModel, DateTime van, DateTime tot)
    {
        _data = perModel;
        _van = van.Date;
        _tot = tot.Date;
        Teken();
    }

    private static SolidColorBrush Kwast(string hex) => new(Microsoft.UI.ColorHelper.FromArgb(
        0xFF,
        Convert.ToByte(hex[1..3], 16),
        Convert.ToByte(hex[3..5], 16),
        Convert.ToByte(hex[5..7], 16)));

    private static Brush Thema(string sleutel) => (Brush)Application.Current.Resources[sleutel];

    private void Teken()
    {
        _vlak.Children.Clear();
        _legenda.Children.Clear();

        var breedte = _vlak.ActualWidth;
        var hoogte = _vlak.Height;
        var dagen = (_tot - _van).Days + 1;

        if (breedte < 200 || dagen < 2)
        {
            return;
        }

        if (_data.Count == 0 || _data.Values.All(d => d.Values.Sum() == 0))
        {
            var leeg = new TextBlock { Text = "Geen verbruik in deze periode.", Foreground = Thema("TextMutedBrush") };
            Canvas.SetLeft(leeg, MargeLinks);
            Canvas.SetTop(leeg, hoogte / 2 - 10);
            _vlak.Children.Add(leeg);
            return;
        }

        var lijnKleur = Thema("BorderBrush");
        var tekstKleur = Thema("TextMutedBrush");
        var vlakBreedte = breedte - MargeLinks - MargeRechts;
        var vlakHoogte = hoogte - MargeBoven - MargeOnder;
        var top = MooiMaximum(_data.Values.SelectMany(d => d.Values).DefaultIfEmpty(0).Max());

        double X(int dag) => MargeLinks + (dag * vlakBreedte / (dagen - 1));
        double Y(long waarde) => MargeBoven + vlakHoogte - (waarde * vlakHoogte / top);

        // Hulplijnen met de waarde erbij.
        for (var stap = 0; stap <= 4; stap++)
        {
            var waarde = top * stap / 4;
            var y = Y(waarde);
            _vlak.Children.Add(new Line
            {
                X1 = MargeLinks, X2 = MargeLinks + vlakBreedte, Y1 = y, Y2 = y,
                Stroke = lijnKleur, StrokeThickness = stap == 0 ? 2 : 1, Opacity = stap == 0 ? 1 : 0.4,
            });

            var label = new TextBlock
            {
                Text = Afkorten(waarde), Foreground = tekstKleur, FontSize = 12,
                Width = MargeLinks - 10, TextAlignment = TextAlignment.Right,
            };
            Canvas.SetTop(label, y - 9);
            _vlak.Children.Add(label);
        }

        // Datums onderaan: een week = elke dag met de dagnaam; langer = eens per week en de laatste dag.
        var elkeDag = dagen <= 14;
        for (var dag = 0; dag < dagen; dag += elkeDag ? 1 : 7)
        {
            ZetDatum(_van.AddDays(dag), X(dag), hoogte, tekstKleur, elkeDag);
        }

        if (!elkeDag && (dagen - 1) % 7 != 0)
        {
            ZetDatum(_tot, X(dagen - 1), hoogte, tekstKleur, false);
        }

        // Een lijn per model, de zwaarste eerst; daarna de stippen (met tooltip) erbovenop.
        var modellen = _data.OrderByDescending(m => m.Value.Values.Sum()).ToList();
        var stippen = new List<UIElement>();
        for (var nummer = 0; nummer < modellen.Count; nummer++)
        {
            var (naam, perDag) = (modellen[nummer].Key, modellen[nummer].Value);
            var kleur = Kwast(Kleuren[nummer % Kleuren.Length]);
            var punten = new PointCollection();

            for (var dag = 0; dag < dagen; dag++)
            {
                var datum = _van.AddDays(dag);
                var waarde = perDag.TryGetValue(datum, out var gevonden) ? gevonden : 0;
                punten.Add(new Point(X(dag), Y(waarde)));

                if (waarde > 0)
                {
                    var stip = new Ellipse { Width = 8, Height = 8, Fill = kleur };
                    ToolTipService.SetToolTip(stip, $"{naam}\n{datum.ToString("ddd d MMM", Nl)}: {waarde.ToString("N0", Nl)} tokens");
                    Canvas.SetLeft(stip, X(dag) - 4);
                    Canvas.SetTop(stip, Y(waarde) - 4);
                    stippen.Add(stip);
                }
            }

            _vlak.Children.Add(new Polyline { Points = punten, Stroke = kleur, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round });
            _legenda.Children.Add(LegendaItem(naam, perDag.Values.Sum(), kleur));
        }

        foreach (var stip in stippen)
        {
            _vlak.Children.Add(stip);
        }
    }

    private void ZetDatum(DateTime datum, double x, double hoogte, Brush kleur, bool metDag)
    {
        var label = new TextBlock
        {
            Text = datum.ToString(metDag ? "ddd d" : "d MMM", Nl),
            Foreground = kleur,
            FontSize = 12,
        };
        Canvas.SetLeft(label, x - 16);
        Canvas.SetTop(label, hoogte - MargeOnder + 6);
        _vlak.Children.Add(label);
    }

    private static StackPanel LegendaItem(string model, long totaal, Brush kleur) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new Rectangle { Width = 14, Height = 4, Fill = kleur, RadiusX = 2, RadiusY = 2, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock
            {
                Text = $"{(string.IsNullOrWhiteSpace(model) ? "(onbekend)" : model)}  {totaal.ToString("N0", Nl)}",
                FontSize = 12,
                Foreground = Thema("TextPrimaryBrush"),
            },
        },
    };

    /// <summary>
    /// De bovenkant van de as: net boven de hoogste waarde, op een rond getal
    /// (1, 1,2, 1,5, 2, 2,5, 3, 4, 5, 6, 8 of 10 keer een macht van tien per
    /// vak, vier vakken) - zelfde aanpak als in het oude Jabasoft.App.
    /// </summary>
    private static long MooiMaximum(long hoogste)
    {
        if (hoogste <= 0)
        {
            return 4;
        }

        var perVak = hoogste / 4.0;
        var macht = Math.Pow(10, Math.Floor(Math.Log10(perVak)));
        foreach (var factor in new[] { 1.0, 1.2, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0 })
        {
            var vak = factor * macht;
            if (vak * 4 >= hoogste)
            {
                return (long)Math.Max(4, Math.Ceiling(vak * 4));
            }
        }

        return (long)Math.Ceiling(40 * macht);
    }

    private static string Afkorten(long waarde) => waarde switch
    {
        >= 1_000_000 => (waarde / 1_000_000.0).ToString("0.#", Nl) + " M",
        >= 1_000 => (waarde / 1_000.0).ToString("0.#", Nl) + " K",
        _ => waarde.ToString(Nl),
    };
}
