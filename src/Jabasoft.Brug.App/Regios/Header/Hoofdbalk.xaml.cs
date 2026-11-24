using Jabasoft.Brug.App.Controls;
using Jabasoft.Brug.App.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Regios.Header;

/// <summary>De header van de Brug; zie Hoofdbalk.xaml.</summary>
public sealed partial class Hoofdbalk : UserControl
{
    private const string IndelingSleutel = "header.infoblokken";
    private static readonly int[] Blokkolommen = [2, 4, 6, 8];

    public Hoofdbalk()
    {
        InitializeComponent();

        foreach (var greep in new[] { Greep1, Greep2, Greep3 })
        {
            greep.Kolommen = Blokkolommen;
            greep.Verschoven += (_, _) => BewaarBreedtes();
        }

        HerstelBreedtes();
    }

    /// <summary>Het deel waarmee je het venster versleept.</summary>
    public UIElement Sleepgebied => Titelstrook;

    /// <summary>Koppelt de vensterknoppen aan het venster.</summary>
    public void KoppelVenster(Window venster) => Knoppen.Koppel(venster);

    private void HerstelBreedtes()
    {
        if (Indeling.Lees(IndelingSleutel) is not { Length: 4 } breedtes || breedtes.Any(b => b <= 0))
        {
            return;
        }

        for (var i = 0; i < Blokkolommen.Length; i++)
        {
            Blokrij.ColumnDefinitions[Blokkolommen[i]].Width = new GridLength(breedtes[i], GridUnitType.Star);
        }
    }

    private void BewaarBreedtes() =>
        Indeling.Bewaar(IndelingSleutel, [.. Blokkolommen.Select(k => Blokrij.ColumnDefinitions[k].Width.Value)]);
}
