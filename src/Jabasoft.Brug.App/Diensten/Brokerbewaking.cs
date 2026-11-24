using System.Diagnostics;

namespace Jabasoft.Brug.App.Diensten;

/// <summary>Hoe het met de broker staat, zoals de Brug het ziet.</summary>
public enum Brokertoestand
{
    Onbekend,
    Online,
    Offline,
    Starten,
}

/// <summary>
/// Houdt bij of de broker bereikbaar is en kan hem starten.
///
/// De Brug stopt de broker NOOIT, ook niet bij het afsluiten: hij is een
/// losse dienst die ook Goose, Hermes en Claude gebruiken. Starten gaat op
/// dezelfde manier als het traypictogram: via de aanmeldtaak
/// "Jabasoft Broker" in Taakplanner (bouwen + starten, met opstart.log), en
/// als die taak er niet is via Beheer\Broker-starten.ps1. Zo draait de
/// broker altijd op dezelfde manier, los van de Brug.
///
/// Niet automatisch bij het openen van de Brug: de gebruiker houdt de regie.
/// </summary>
public sealed class Brokerbewaking(Activiteitenlog log)
{
    private const string Taaknaam = "Jabasoft Broker";
    private const string Startscript = @"D:\Repos\Jabasoft.Broker\Beheer\Broker-starten.ps1";

    /// <summary>Starten = eerst bouwen; dat kan na een grote wijziging even duren (zelfde ruimte als de tray).</summary>
    private static readonly TimeSpan MaxStarttijd = TimeSpan.FromSeconds(150);

    private DateTimeOffset _startTot;

    public Brokertoestand Toestand { get; private set; } = Brokertoestand.Onbekend;

    /// <summary>De toestand is veranderd. Komt op een achtergronddraad.</summary>
    public event EventHandler<Brokertoestand>? Gewijzigd;

    /// <summary>Wordt aangeroepen door de loglezer na elke poging om de broker te bereiken.</summary>
    public void Meld(bool bereikbaar)
    {
        if (bereikbaar)
        {
            if (Toestand == Brokertoestand.Starten)
            {
                log.Schrijf("brug", "Broker is gestart");
            }

            Zet(Brokertoestand.Online);
            return;
        }

        // Tijdens het starten is "niet bereikbaar" normaal; pas na de wachttijd is het mislukt.
        if (Toestand == Brokertoestand.Starten)
        {
            if (DateTimeOffset.Now < _startTot)
            {
                return;
            }

            log.Schrijf("brug", "Broker starten lukte niet binnen 150 s (zie opstart.log)");
        }

        Zet(Brokertoestand.Offline);
    }

    /// <summary>Start de broker, tenzij hij al draait of al aan het starten is.</summary>
    public async Task StartAsync()
    {
        if (Toestand is Brokertoestand.Online or Brokertoestand.Starten)
        {
            return;
        }

        _startTot = DateTimeOffset.Now + MaxStarttijd;
        Zet(Brokertoestand.Starten);
        log.Schrijf("brug", "Broker starten via de aanmeldtaak \"Jabasoft Broker\"");

        var gelukt = await Task.Run(() => Voer("schtasks.exe", "/run", "/tn", Taaknaam) == 0);
        if (gelukt)
        {
            return;
        }

        if (File.Exists(Startscript) && StartLos("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", Startscript))
        {
            log.Schrijf("brug", "Aanmeldtaak niet gevonden; gestart met Broker-starten.ps1");
            return;
        }

        log.Schrijf("brug", "Kon de broker niet starten (geen aanmeldtaak en geen startscript)");
        Zet(Brokertoestand.Offline);
    }

    private void Zet(Brokertoestand nieuw)
    {
        if (Toestand == nieuw)
        {
            return;
        }

        Toestand = nieuw;
        Gewijzigd?.Invoke(this, nieuw);
    }

    private static int Voer(string programma, params string[] argumenten)
    {
        try
        {
            var info = new ProcessStartInfo(programma) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in argumenten)
            {
                info.ArgumentList.Add(a);
            }

            using var proces = Process.Start(info);
            if (proces is null)
            {
                return -1;
            }

            proces.WaitForExit(15000);
            return proces.HasExited ? proces.ExitCode : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static bool StartLos(string programma, params string[] argumenten)
    {
        try
        {
            var info = new ProcessStartInfo(programma) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in argumenten)
            {
                info.ArgumentList.Add(a);
            }

            using var proces = Process.Start(info);
            return proces is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
