using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Controls;

/// <summary>Kader met titelbalk; zie Infoblok.xaml.</summary>
public sealed partial class Infoblok : UserControl
{
    public Infoblok()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty TitelProperty =
        DependencyProperty.Register(nameof(Titel), typeof(string), typeof(Infoblok), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TekstProperty =
        DependencyProperty.Register(nameof(Tekst), typeof(string), typeof(Infoblok), new PropertyMetadata(string.Empty));

    public string Titel
    {
        get => (string)GetValue(TitelProperty);
        set => SetValue(TitelProperty, value);
    }

    public string Tekst
    {
        get => (string)GetValue(TekstProperty);
        set => SetValue(TekstProperty, value);
    }
}
