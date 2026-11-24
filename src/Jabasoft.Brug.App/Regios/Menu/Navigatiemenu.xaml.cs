using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Regios.Menu;

/// <summary>
/// Het menu links, opgebouwd uit een lijst <see cref="MenuOnderdeel"/>.
/// Een onderdeel met kinderen klapt uit en in; een onderdeel zonder kinderen
/// wordt "gekozen" en meldt dat via <see cref="Gekozen"/>.
/// </summary>
public sealed partial class Navigatiemenu : UserControl
{
    private const string ChevronDicht = "";
    private const string ChevronOpen = "";

    private readonly Dictionary<string, Button> _knoppen = [];
    private readonly Dictionary<string, StackPanel> _submenus = [];
    private readonly Dictionary<string, FontIcon> _chevrons = [];
    private readonly Dictionary<string, string> _ouders = [];
    private string? _actief;

    public Navigatiemenu()
    {
        InitializeComponent();
        VersieTekst.Text = $"JABASOFT.BRUG  {App.Versie}";
    }

    /// <summary>Er is een scherm gekozen; de waarde is de sleutel.</summary>
    public event EventHandler<string>? Gekozen;

    /// <summary>Bouwt het menu (opnieuw) op.</summary>
    public void Vul(IEnumerable<MenuOnderdeel> onderdelen)
    {
        Knoppen.Children.Clear();
        _knoppen.Clear();
        _submenus.Clear();
        _chevrons.Clear();
        _ouders.Clear();

        foreach (var onderdeel in onderdelen)
        {
            VoegToe(onderdeel, Knoppen, ouder: null);
        }
    }

    /// <summary>Kiest een scherm, alsof erop geklikt is. Klapt zo nodig het submenu eromheen open.</summary>
    public void Kies(string sleutel)
    {
        if (!_knoppen.ContainsKey(sleutel) || _submenus.ContainsKey(sleutel))
        {
            return;
        }

        if (_ouders.TryGetValue(sleutel, out var ouder))
        {
            KlapUit(ouder, open: true);
        }

        MarkeerActief(sleutel);
        Gekozen?.Invoke(this, sleutel);
    }

    private void VoegToe(MenuOnderdeel onderdeel, Panel doel, string? ouder)
    {
        var stijl = ouder is null ? "MenuKnopStijl" : "SubmenuKnopStijl";
        var knop = new Button
        {
            Style = (Style)Application.Current.Resources[stijl],
            Content = MaakInhoud(onderdeel),
            IsEnabled = onderdeel.Beschikbaar,
        };

        if (!onderdeel.Beschikbaar && onderdeel.Fase > 0)
        {
            ToolTipService.SetToolTip(knop, $"Komt in fase {onderdeel.Fase}");
        }

        doel.Children.Add(knop);
        _knoppen[onderdeel.Sleutel] = knop;
        if (ouder is not null)
        {
            _ouders[onderdeel.Sleutel] = ouder;
        }

        if (onderdeel.HeeftKinderen)
        {
            var submenu = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(20, 0, 0, 4),
                Visibility = Visibility.Collapsed,
            };

            foreach (var kind in onderdeel.Kinderen!)
            {
                VoegToe(kind, submenu, onderdeel.Sleutel);
            }

            doel.Children.Add(submenu);
            _submenus[onderdeel.Sleutel] = submenu;
            knop.Click += (_, _) => KlapUit(onderdeel.Sleutel, submenu.Visibility != Visibility.Visible);
        }
        else
        {
            knop.Click += (_, _) => Kies(onderdeel.Sleutel);
        }
    }

    private Grid MaakInhoud(MenuOnderdeel onderdeel)
    {
        var inhoud = new Grid { ColumnSpacing = 12 };
        inhoud.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inhoud.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inhoud.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icoon = new FontIcon
        {
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["IconFontFamily"],
            Glyph = onderdeel.Icoon,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
        inhoud.Children.Add(icoon);

        var titel = new TextBlock
        {
            Text = onderdeel.Titel,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(titel, 1);
        inhoud.Children.Add(titel);

        if (onderdeel.HeeftKinderen)
        {
            var chevron = new FontIcon
            {
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["IconFontFamily"],
                Glyph = ChevronDicht,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(chevron, 2);
            inhoud.Children.Add(chevron);
            _chevrons[onderdeel.Sleutel] = chevron;
        }

        return inhoud;
    }

    private void KlapUit(string sleutel, bool open)
    {
        if (_submenus.TryGetValue(sleutel, out var submenu))
        {
            submenu.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_chevrons.TryGetValue(sleutel, out var chevron))
        {
            chevron.Glyph = open ? ChevronOpen : ChevronDicht;
        }
    }

    /// <summary>
    /// De gekozen knop wordt wit. Terugzetten gaat met
    /// ClearValue, zodat de knop weer de kleur uit zijn stijl krijgt.
    /// </summary>
    private void MarkeerActief(string sleutel)
    {
        if (_actief is not null && _knoppen.TryGetValue(_actief, out var vorige))
        {
            vorige.ClearValue(Control.BackgroundProperty);
        }

        _actief = sleutel;
        if (_knoppen.TryGetValue(sleutel, out var knop))
        {
            knop.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["Text01Brush"];
        }
    }
}
