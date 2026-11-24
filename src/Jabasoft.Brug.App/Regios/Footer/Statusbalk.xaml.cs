using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Jabasoft.Brug.App.Regios.Footer;

/// <summary>De footer met statuspillen; zie Statusbalk.xaml.</summary>
public sealed partial class Statusbalk : UserControl
{
    public Statusbalk()
    {
        InitializeComponent();
        VersieTekst.Text = $"JABASOFT.BRUG  {App.Versie}";

        ToonBroker(App.Broker.Toestand);
        App.Broker.Gewijzigd += (_, toestand) => DispatcherQueue.TryEnqueue(() => ToonBroker(toestand));
    }

    /// <summary>SYSTEM HEALTH volgt voorlopig alleen de broker; in fase 2 komen database, AI-server en modellen erbij.</summary>
    private void ToonBroker(Brokertoestand toestand)
    {
        var (kleur, tekst) = toestand switch
        {
            Brokertoestand.Online => ("HealthOkBrush", "SYSTEM HEALTH  ·  BROKER ONLINE"),
            Brokertoestand.Starten => ("HealthBusyBrush", "SYSTEM HEALTH  ·  BROKER START…"),
            Brokertoestand.Offline => ("HealthErrorBrush", "SYSTEM HEALTH  ·  BROKER OFFLINE"),
            _ => ("AccentForegroundBrush", "SYSTEM HEALTH  —"),
        };

        HealthBolletje.Fill = (Brush)Application.Current.Resources[kleur];
        HealthTekst.Text = tekst;
    }
}
