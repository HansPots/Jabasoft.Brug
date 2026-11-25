using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// De gemeenschappelijke vorm van de broker-schermen: een scrollend vlak met
/// dezelfde marges als de rest van de Brug, gegevens ophalen bij het openen
/// en met de verversknop, en desgewenst een klok die bijwerkt zolang het
/// scherm zichtbaar is (Nu bezig, Health).
///
/// De inhoud wordt in code opgebouwd (zie <see cref="Bouw"/>): het zijn
/// lijsten waarvan de lengte pas bekend is als de broker geantwoord heeft.
///
/// Afspraak voor elke pagina (ook nieuwe): asynchroon opbouwen. Teken
/// meteen de opbouw met een "Ophalen…"-plek per onderdeel, haal de bronnen
/// tegelijk op (niet na elkaar), en vul elk onderdeel zodra zijn eigen bron
/// binnen is. Gegevens die altijd klaar moeten staan, houdt een dienst op de
/// achtergrond bij (zoals App.Wachtrij en App.Gezondheid), zodat de pagina
/// bij openen niets hoeft af te wachten.
/// </summary>
public abstract class BrokerPagina : UserControl, IVerversbaar
{
    private readonly DispatcherTimer? _klok;
    private bool _bezig;

    protected BrokerPagina(TimeSpan? bijwerken = null)
    {
        Lijf = new StackPanel { Margin = new Thickness(32, 24, 32, 32), Spacing = 24 };
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Lijf };

        if (bijwerken is { } interval)
        {
            _klok = new DispatcherTimer { Interval = interval };
            _klok.Tick += async (_, _) => await VerversAsync();
        }

        Loaded += async (_, _) =>
        {
            _klok?.Start();
            await VerversAsync();
        };
        Unloaded += (_, _) => _klok?.Stop();
    }

    /// <summary>Het vlak waar de pagina zijn inhoud in zet.</summary>
    protected StackPanel Lijf { get; }

    public async Task VerversAsync()
    {
        if (_bezig)
        {
            return;
        }

        _bezig = true;
        try
        {
            await LaadAsync();
        }
        catch (Exception fout)
        {
            App.AppLog.Schrijf("brug", $"{GetType().Name}: {fout.Message}");
        }
        finally
        {
            _bezig = false;
        }
    }

    /// <summary>Haalt de gegevens op en zet ze op het scherm.</summary>
    protected abstract Task LaadAsync();
}
