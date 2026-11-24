using Jabasoft.Brug.App.Diensten;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Regios.Actie;

/// <summary>
/// De actiebalk rechts; zie Actiebalk.xaml. Wat een knop kan, hangt af van
/// het open scherm (<see cref="ZetScherm"/>) en, voor de startknop, van de
/// broker.
/// </summary>
public sealed partial class Actiebalk : UserControl
{
    private string _sleutel = string.Empty;
    private IVerversbaar? _verversbaar;

    public Actiebalk()
    {
        InitializeComponent();
        App.Broker.Gewijzigd += (_, _) => DispatcherQueue.TryEnqueue(Bijwerken);
    }

    /// <summary>Het scherm dat nu open staat. MainWindow roept dit aan bij elke wissel.</summary>
    public void ZetScherm(string sleutel, UIElement scherm)
    {
        _sleutel = sleutel;
        _verversbaar = scherm as IVerversbaar;
        Bijwerken();
    }

    private bool OpBrokerScherm => _sleutel.StartsWith("broker.", StringComparison.Ordinal);

    private void Bijwerken()
    {
        var toestand = App.Broker.Toestand;
        StartKnop.IsEnabled = OpBrokerScherm && toestand == Brokertoestand.Offline;
        ToolTipService.SetToolTip(StartKnop, !OpBrokerScherm
            ? "Broker starten (op de broker-schermen)"
            : toestand switch
            {
                Brokertoestand.Offline => "Broker starten",
                Brokertoestand.Starten => "Broker wordt gestart…",
                Brokertoestand.Online => "Broker draait",
                _ => "Broker wordt gecontroleerd…",
            });

        VerversKnop.IsEnabled = _verversbaar is not null;
        ToolTipService.SetToolTip(VerversKnop, _verversbaar is not null ? "Verversen" : "Verversen (niet op dit scherm)");
    }

    private async void StartKnop_Click(object sender, RoutedEventArgs e) => await App.Broker.StartAsync();

    private async void VerversKnop_Click(object sender, RoutedEventArgs e)
    {
        if (_verversbaar is not null)
        {
            await _verversbaar.VerversAsync();
        }
    }
}
