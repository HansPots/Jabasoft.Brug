namespace Jabasoft.Brug.App.Paginas;

/// <summary>
/// Een scherm dat zijn gegevens opnieuw kan ophalen. Alleen op zo'n scherm
/// is de verversknop in de actiebalk actief (volle kleur); op alle andere
/// schermen staat hij er wel, maar zacht en zonder functie.
/// </summary>
public interface IVerversbaar
{
    Task VerversAsync();
}
