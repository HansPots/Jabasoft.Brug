using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Jabasoft.Brug.App.Paginas;

/// <summary>
/// Kleine bouwstenen voor schermen die hun inhoud in code opbouwen (lijsten
/// uit de broker): pilletjes, balken, knoppen en getallen in nl-NL. Alles in
/// de themakleuren uit Thema\Lcars.xaml.
/// </summary>
public static class Bouw
{
    public static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static Brush Kleur(string sleutel) => (Brush)Application.Current.Resources[sleutel];

    public static Style Stijl(string sleutel) => (Style)Application.Current.Resources[sleutel];

    public static double Maat(string sleutel) => (double)Application.Current.Resources[sleutel];

    public static string Getal(long waarde) => waarde.ToString("N0", Nl);

    public static string Getal(double waarde, int decimalen) => waarde.ToString($"N{decimalen}", Nl);

    /// <summary>Een duur kort en leesbaar: 0,4 s · 12 s · 3 min 05 s · 2 u 10 min.</summary>
    public static string Duur(TimeSpan duur) => duur.TotalSeconds switch
    {
        < 10 => $"{Getal(duur.TotalSeconds, 1)} s",
        < 60 => $"{duur.TotalSeconds:0} s",
        < 3600 => $"{(int)duur.TotalMinutes} min {duur.Seconds:00} s",
        _ => $"{(int)duur.TotalHours} u {duur.Minutes} min",
    };

    /// <summary>Een LCARS-pilletje: gekleurd vlak, zwarte hoofdletters.</summary>
    public static Border Pil(string tekst, string kleur = "AccentBrush") => new()
    {
        Background = Kleur(kleur),
        CornerRadius = new CornerRadius(10, 10, 10, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = tekst.ToUpper(Nl), Style = Stijl("PilletjeTekstStijl") },
    };

    public static TextBlock Tekst(string tekst, string stijl = "WaardeStijl", bool omloop = false) => new()
    {
        Text = tekst,
        Style = Stijl(stijl),
        TextWrapping = omloop ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = omloop ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Kop(string tekst) => new() { Text = tekst, Style = Stijl("SectieKopStijl") };

    /// <summary>Een grijze regel voor "niets te tonen" of "broker niet bereikbaar".</summary>
    public static TextBlock Melding(string tekst) => new()
    {
        Text = tekst,
        Style = Stijl("LabelStijl"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 4),
    };

    /// <summary>Label en waarde op een regel, daaronder een balk die <paramref name="fractie"/> (0-1) gevuld is.</summary>
    public static StackPanel Balkregel(string label, string waarde, double fractie, string kleur = "AccentBrushDark")
    {
        fractie = Math.Clamp(double.IsFinite(fractie) ? fractie : 0, 0, 1);

        var kop = new Grid { ColumnSpacing = 12 };
        kop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        kop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        kop.Children.Add(Tekst(label));
        var rechts = Tekst(waarde, "LabelStijl");
        Grid.SetColumn(rechts, 1);
        kop.Children.Add(rechts);

        return new StackPanel { Spacing = 4, Children = { kop, Balk(fractie, kleur) } };
    }

    /// <summary>
    /// Een balk van 10 px hoog: een donkere goot met daarop het gevulde deel,
    /// beide met vier ronde hoeken. Standaard donkergeel (AccentBrushDark).
    /// </summary>
    public static Grid Balk(double fractie, string kleur = "AccentBrushDark", double hoogte = 10)
    {
        fractie = Math.Clamp(double.IsFinite(fractie) ? fractie : 0, 0, 1);
        var balk = new Grid { Height = hoogte };
        balk.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fractie, 0.0001), GridUnitType.Star) });
        balk.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fractie, 0.0001), GridUnitType.Star) });

        var rond = new CornerRadius(hoogte / 2, hoogte / 2, hoogte / 2, hoogte / 2);
        var goot = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x2A, 0x2A, 0x3A)),
            CornerRadius = rond,
        };
        Grid.SetColumnSpan(goot, 2);
        balk.Children.Add(goot);

        if (fractie > 0)
        {
            // Minstens zo breed als hoog, zodat ook een klein deel een rond balkje blijft.
            balk.Children.Add(new Border { Background = Kleur(kleur), CornerRadius = rond, MinWidth = hoogte });
        }

        return balk;
    }

    /// <summary>Een kleine LCARS-knop (drie ronde hoeken) met tekst.</summary>
    public static Button Knop(string tekst, RoutedEventHandler klik, string kleur = "AccentBrush")
    {
        var knop = new Button
        {
            Style = Stijl("MenuKnopStijl"),
            Height = 32,
            FontSize = Maat("FontSizeSmall"),
            CornerRadius = new CornerRadius(14, 14, 14, 0),
            Padding = new Thickness(14, 0, 14, 0),
            Background = Kleur(kleur),
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Text = tekst.ToUpper(Nl), CharacterSpacing = 100 },
        };
        knop.Click += klik;
        return knop;
    }

    /// <summary>Een bolletje in een statuskleur (HealthOk/Busy/Error).</summary>
    public static Microsoft.UI.Xaml.Shapes.Ellipse Bolletje(string kleur, double maat = 12) => new()
    {
        Width = maat,
        Height = maat,
        Fill = Kleur(kleur),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Kolommen van gelijke breedte; vul ze met <see cref="Zet"/>.</summary>
    public static Grid Kolommen(int aantal, double tussenruimte = 24)
    {
        var grid = new Grid { ColumnSpacing = tussenruimte, RowSpacing = tussenruimte };
        for (var i = 0; i < aantal; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        return grid;
    }

    /// <summary>Zet een element in een raster, en maakt zo nodig rijen bij.</summary>
    public static void Zet(Grid grid, FrameworkElement element, int rij, int kolom, int kolomBreedte = 1)
    {
        while (grid.RowDefinitions.Count <= rij)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Grid.SetRow(element, rij);
        Grid.SetColumn(element, kolom);
        Grid.SetColumnSpan(element, kolomBreedte);
        grid.Children.Add(element);
    }

    /// <summary>Een Paneel (lila titelbalk) met inhoud.</summary>
    public static Controls.Paneel Paneel(string titel, UIElement inhoud, string rechts = "") => new()
    {
        Titel = titel.ToUpper(Nl),
        Rechts = rechts.ToUpper(Nl),
        Inhoud = inhoud,
    };

    public static Controls.Waardeveld Veld(string label, string waarde) => new() { Label = label.ToUpper(Nl), Waarde = waarde };

    /// <summary>
    /// Kengetallen in één donkergele band met vier ronde hoeken (bovenaan elke
    /// pagina, ook de band van Health): per vak een label en de waarde, zwart
    /// op donkergeel, gelijk verdeeld.
    /// </summary>
    public static Border Band(params (string Label, string Waarde)[] vakken)
    {
        var grid = new Grid { Margin = new Thickness(20, 0, 20, 0), ColumnSpacing = 24 };
        for (var i = 0; i < vakken.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var vak = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            vak.Children.Add(new TextBlock
            {
                Text = vakken[i].Label.ToUpper(Nl),
                FontSize = Maat("FontSizeSmall"),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                CharacterSpacing = 100,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Kleur("AccentForegroundBrush"),
            });
            vak.Children.Add(new TextBlock
            {
                Text = vakken[i].Waarde,
                FontSize = Maat("FontSizeTitle"),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Kleur("AccentForegroundBrush"),
            });
            Grid.SetColumn(vak, i);
            grid.Children.Add(vak);
        }

        return new Border
        {
            Background = Kleur("AccentBrushDark"),
            CornerRadius = new CornerRadius(22, 22, 22, 22),
            Height = 44,
            Child = grid,
        };
    }

    /// <summary>
    /// Een groep bij elkaar horende panelen, zoals het menu links: een roze
    /// vlak met vier ronde hoeken, bovenin de gele band met de naam van de
    /// groep (en een aantal), daaronder de inhoud. De panelen erin hebben een
    /// zwarte achtergrond, zodat ze los van het roze staan.
    /// </summary>
    public static Border Groep(string titel, string waarde, UIElement inhoud) => new()
    {
        Background = Kleur("TextMutedBrush"),
        CornerRadius = new CornerRadius(30, 30, 30, 30),
        Padding = new Thickness(12),
        Child = new StackPanel { Spacing = 12, Children = { Band((titel, waarde)), inhoud } },
    };

    /// <summary>Een Paneel met alleen een melding erin, voor "niets te tonen" of een fout bovenaan een scherm.</summary>
    public static Controls.Paneel Meldingpaneel(string titel, string tekst) => Paneel(titel, Melding(tekst));

    /// <summary>Een dunne scheidingslijn tussen regels binnen een paneel.</summary>
    public static Border Scheiding() => new()
    {
        Height = 2,
        Background = Kleur("BorderBrush"),
        Opacity = 0.35,
        Margin = new Thickness(0, 6, 0, 6),
    };

    public static Controls.Kengetal Kengetal(string label, string waarde, string kleur) => new()
    {
        Label = label.ToUpper(Nl),
        Waarde = waarde,
        Kleur = Kleur(kleur),
    };
}
