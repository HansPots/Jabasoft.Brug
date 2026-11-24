using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Controls;

/// <summary>Het JabaSoft-beeldmerk; zie Beeldmerk.xaml.</summary>
public sealed partial class Beeldmerk : UserControl
{
    public Beeldmerk()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ApplicatienaamProperty =
        DependencyProperty.Register(nameof(Applicatienaam), typeof(string), typeof(Beeldmerk), new PropertyMetadata("the Application"));

    /// <summary>De kleine tekst onder "Soft"; standaard "the Application".</summary>
    public string Applicatienaam
    {
        get => (string)GetValue(ApplicatienaamProperty);
        set => SetValue(ApplicatienaamProperty, value);
    }
}
