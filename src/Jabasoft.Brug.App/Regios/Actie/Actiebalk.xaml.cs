using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Regios.Actie;

/// <summary>De actiebalk rechts; zie Actiebalk.xaml.</summary>
public sealed partial class Actiebalk : UserControl
{
    public Actiebalk()
    {
        InitializeComponent();

        ToonBroker(App.Broker.Toestand);
        App.Broker.Gewijzigd += (_, toestand) => DispatcherQueue.TryEnqueue(() => ToonBroker(toestand));
    }

    private void ToonBroker(Brokertoestand toestand)
    {
        StartKnop.IsEnabled = toestand == Brokertoestand.Offline;
        ToolTipService.SetToolTip(StartKnop, toestand switch
        {
            Brokertoestand.Offline => "Broker starten",
            Brokertoestand.Starten => "Broker wordt gestart…",
            Brokertoestand.Online => "Broker draait",
            _ => "Broker wordt gecontroleerd…",
        });
    }

    private async void StartKnop_Click(object sender, RoutedEventArgs e) => await App.Broker.StartAsync();
}
