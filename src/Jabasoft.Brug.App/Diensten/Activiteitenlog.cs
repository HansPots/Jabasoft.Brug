using System.Collections.Concurrent;

namespace Jabasoft.Brug.App.Diensten;

/// <summary>Een regel in een activiteitenoverzicht.</summary>
/// <param name="Volgnummer">Oplopend; wie bijhaalt vraagt "alles na dit nummer".</param>
public sealed record Activiteit(long Volgnummer, DateTimeOffset Tijd, string Bron, string Bericht)
{
    public string TijdTekst => Tijd.ToLocalTime().ToString("HH:mm:ss");
}

/// <summary>
/// Houdt de laatste regels vast van wat er gebeurt. Alleen in het geheugen
/// en met een bovengrens: een kijkvenster op NU, geen archief.
///
/// Zelfde idee als ActivityLog in Jabasoft.Base, maar los gehouden: de Brug
/// hangt in deze fase nog niet aan Base (zie het plan, fase 2).
/// </summary>
public sealed class Activiteitenlog(int capaciteit = 200)
{
    private readonly ConcurrentQueue<Activiteit> _regels = new();
    private long _volgnummer;

    /// <summary>Nieuwe regel. Komt op de draad van de schrijver; een scherm springt zelf naar zijn eigen draad.</summary>
    public event EventHandler<Activiteit>? Toegevoegd;

    public Activiteit Schrijf(string bron, string bericht) => Voeg(new Activiteit(0, DateTimeOffset.Now, bron, bericht));

    /// <summary>Neemt een regel over die elders gemaakt is (met zijn eigen tijd), bijvoorbeeld van de broker.</summary>
    public Activiteit Voeg(Activiteit regel)
    {
        var nieuw = regel with { Volgnummer = Interlocked.Increment(ref _volgnummer) };
        _regels.Enqueue(nieuw);
        while (_regels.Count > capaciteit && _regels.TryDequeue(out _))
        {
            // Oudste regels vallen eruit.
        }

        Toegevoegd?.Invoke(this, nieuw);
        return nieuw;
    }

    /// <summary>Alles wat er nu in zit, oudste eerst.</summary>
    public IReadOnlyList<Activiteit> Alles() => [.. _regels];
}
