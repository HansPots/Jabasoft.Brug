namespace Jabasoft.Brug.App.Regios.Menu;

/// <summary>
/// Een regel in het menu. Heeft hij <see cref="Kinderen"/>, dan klapt hij
/// uit in plaats van een scherm te openen (zoals Broker).
/// </summary>
/// <param name="Sleutel">Unieke naam, ook gebruikt om het scherm te vinden.</param>
/// <param name="Titel">Wat er op de knop staat.</param>
/// <param name="Icoon">Teken uit Segoe Fluent Icons, bijvoorbeeld "".</param>
/// <param name="Omschrijving">Wat er op dit scherm komt; getoond zolang het nog leeg is.</param>
/// <param name="Fase">De fase waarin het scherm gevuld wordt.</param>
/// <param name="Beschikbaar">False = het scherm is er nog niet: de knop ziet er gewoon uit, met een tooltip "komt in fase …", en opent de lege pagina.</param>
public sealed record MenuOnderdeel(
    string Sleutel,
    string Titel,
    string Icoon,
    string Omschrijving = "",
    int Fase = 0,
    bool Beschikbaar = true,
    IReadOnlyList<MenuOnderdeel>? Kinderen = null)
{
    public bool HeeftKinderen => Kinderen is { Count: > 0 };
}
