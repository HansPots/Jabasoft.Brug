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
    public static StackPanel Balkregel(string label, string waarde, double fractie, string kleur = "AccentBrush")
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

    /// <summary>Een balk van 10 px hoog, links gevuld in de gegeven kleur, rechts donker.</summary>
    public static Grid Balk(double fractie, string kleur = "AccentBrush", double hoogte = 10)
    {
        fractie = Math.Clamp(double.IsFinite(fractie) ? fractie : 0, 0, 1);
        var balk = new Grid { Height = hoogte };
        balk.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fractie, 0.0001), GridUnitType.Star) });
        balk.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fractie, 0.0001), GridUnitType.Star) });

        var rond = hoogte / 2;
        var vol = new Border { Background = Kleur(kleur), CornerRadius = new CornerRadius(rond, fractie >= 1 ? rond : 0, fractie >= 1 ? rond : 0, rond) };
        var leeg = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x2A, 0x2A, 0x3A)),
            CornerRadius = new CornerRadius(fractie <= 0 ? rond : 0, rond, rond, fractie <= 0 ? rond : 0),
        };
        Grid.SetColumn(leeg, 1);
        balk.Children.Add(vol);
        balk.Children.Add(leeg);
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

    public static Controls.Kengetal Kengetal(string label, string waarde, string kleur) => new()
    {
        Label = label.ToUpper(Nl),
        Waarde = waarde,
        Kleur = Kleur(kleur),
    };
}
