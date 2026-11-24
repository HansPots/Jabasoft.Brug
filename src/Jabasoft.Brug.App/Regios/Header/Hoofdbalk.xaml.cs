using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Regios.Header;

/// <summary>De header van de Brug; zie Hoofdbalk.xaml.</summary>
public sealed partial class Hoofdbalk : UserControl
{
    public Hoofdbalk()
    {
        InitializeComponent();
    }

    /// <summary>Het deel waarmee je het venster versleept.</summary>
    public UIElement Sleepgebied => Titelstrook;
}
