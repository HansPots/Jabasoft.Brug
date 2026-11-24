using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Jabasoft.Brug.App.Controls;

/// <summary>Label en grote waarde met een gekleurde balk; zie Kengetal.xaml.</summary>
public sealed partial class Kengetal : UserControl
{
    public Kengetal()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(Kengetal), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty WaardeProperty =
        DependencyProperty.Register(nameof(Waarde), typeof(string), typeof(Kengetal), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty KleurProperty =
        DependencyProperty.Register(nameof(Kleur), typeof(Brush), typeof(Kengetal), new PropertyMetadata(null));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Waarde
    {
        get => (string)GetValue(WaardeProperty);
        set => SetValue(WaardeProperty, value);
    }

    public Brush? Kleur
    {
        get => (Brush?)GetValue(KleurProperty);
        set => SetValue(KleurProperty, value);
    }
}
