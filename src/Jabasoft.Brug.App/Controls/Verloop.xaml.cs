using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Jabasoft.Brug.App.Controls;

/// <summary>De vijf stappen van een verzoek als balk; zie Verloop.xaml.</summary>
public sealed partial class Verloop : UserControl
{
    private static readonly SolidColorBrush Nog = new(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x3A));

    public Verloop()
    {
        InitializeComponent();
        Kleur();
    }

    public static readonly DependencyProperty StapProperty =
        DependencyProperty.Register(nameof(Stap), typeof(int), typeof(Verloop),
            new PropertyMetadata(0, (d, _) => ((Verloop)d).Kleur()));

    /// <summary>De huidige stap, 0 (ontvangen) t/m 4 (genereren).</summary>
    public int Stap
    {
        get => (int)GetValue(StapProperty);
        set => SetValue(StapProperty, value);
    }

    private void Kleur()
    {
        var klaar = (Brush)Application.Current.Resources["BorderBrush"];
        var nu = (Brush)Application.Current.Resources["AccentBrush"];
        Border[] stappen = [Stap0, Stap1, Stap2, Stap3, Stap4];
        for (var i = 0; i < stappen.Length; i++)
        {
            stappen[i].Background = i < Stap ? klaar : i == Stap ? nu : Nog;
        }
    }
}
