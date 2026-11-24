using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Regios.Footer;

/// <summary>De footer met statuspillen; zie Statusbalk.xaml.</summary>
public sealed partial class Statusbalk : UserControl
{
    public Statusbalk()
    {
        InitializeComponent();
        VersieTekst.Text = $"JABASOFT.BRUG  {App.Versie}";
    }
}
