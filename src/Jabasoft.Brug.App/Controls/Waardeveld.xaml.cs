using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Controls;

/// <summary>Een regel label + waarde; zie Waardeveld.xaml.</summary>
public sealed partial class Waardeveld : UserControl
{
    public Waardeveld()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(Waardeveld), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty WaardeProperty =
        DependencyProperty.Register(nameof(Waarde), typeof(string), typeof(Waardeveld), new PropertyMetadata(string.Empty));

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
}
