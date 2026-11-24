using Jabasoft.Brug.App.Regios.Menu;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas;

/// <summary>Plaatshouder voor een scherm dat in een latere fase gevuld wordt.</summary>
public sealed partial class LegePagina : UserControl
{
    public LegePagina(MenuOnderdeel onderdeel)
    {
        InitializeComponent();
        TitelTekst.Text = onderdeel.Titel.ToUpperInvariant();
        FaseTekst.Text = onderdeel.Fase > 0 ? $"VOLGT IN FASE {onderdeel.Fase}" : "NOG LEEG";
        OmschrijvingTekst.Text = string.IsNullOrWhiteSpace(onderdeel.Omschrijving)
            ? "Dit scherm wordt later gevuld."
            : onderdeel.Omschrijving;
    }
}
