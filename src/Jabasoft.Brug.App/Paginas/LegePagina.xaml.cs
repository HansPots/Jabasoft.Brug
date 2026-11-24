using Jabasoft.Brug.App.Regios.Menu;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas;

/// <summary>Plaatshouder voor een scherm dat in een latere fase gevuld wordt.</summary>
public sealed partial class LegePagina : UserControl
{
    public LegePagina(MenuOnderdeel onderdeel)
    {
        InitializeComponent();
        // Geen paginatitel meer; de naam staat in de kaartbalk, het menu laat zien waar je bent.
        var naam = onderdeel.Titel.ToUpperInvariant();
        FaseTekst.Text = onderdeel.Fase > 0 ? $"{naam} · VOLGT IN FASE {onderdeel.Fase}" : $"{naam} · NOG LEEG";
        OmschrijvingTekst.Text = string.IsNullOrWhiteSpace(onderdeel.Omschrijving)
            ? "Dit scherm wordt later gevuld."
            : onderdeel.Omschrijving;
    }
}
