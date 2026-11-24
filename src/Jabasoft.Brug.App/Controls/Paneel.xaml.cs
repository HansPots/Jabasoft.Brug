using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Jabasoft.Brug.App.Controls;

/// <summary>
/// Kader met titelbalk en vrije inhoud. Wat je tussen &lt;Paneel&gt; en
/// &lt;/Paneel&gt; zet, komt in <see cref="Inhoud"/> (ContentProperty).
/// </summary>
[ContentProperty(Name = nameof(Inhoud))]
public sealed partial class Paneel : UserControl
{
    public Paneel()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TitelProperty =
        DependencyProperty.Register(nameof(Titel), typeof(string), typeof(Paneel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RechtsProperty =
        DependencyProperty.Register(nameof(Rechts), typeof(string), typeof(Paneel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InhoudProperty =
        DependencyProperty.Register(nameof(Inhoud), typeof(object), typeof(Paneel), new PropertyMetadata(null));

    public string Titel
    {
        get => (string)GetValue(TitelProperty);
        set => SetValue(TitelProperty, value);
    }

    /// <summary>Korte tekst rechts in de titelbalk.</summary>
    public string Rechts
    {
        get => (string)GetValue(RechtsProperty);
        set => SetValue(RechtsProperty, value);
    }

    public object? Inhoud
    {
        get => GetValue(InhoudProperty);
        set => SetValue(InhoudProperty, value);
    }
}
