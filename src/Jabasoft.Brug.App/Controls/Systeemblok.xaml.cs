using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Controls;

/// <summary>Blok 2 in de header: VRAM, videokaart, CPU en RAM; zie Systeemblok.xaml.</summary>
public sealed partial class Systeemblok : UserControl
{
    public Systeemblok()
    {
        InitializeComponent();

        if (App.Meter.Laatste is { } laatste)
        {
            Toon(laatste);
        }

        App.Meter.Gemeten += (_, meting) => DispatcherQueue.TryEnqueue(() => Toon(meting));
    }

    private void Toon(Systeemmeting meting)
    {
        if (meting.EersteKaart is { } kaart)
        {
            Vram.Text = $"{Weergave.GbUitMiB(kaart.VramGebruiktMiB)} / {Weergave.GbUitMiB(kaart.VramTotaalMiB)} GB";
            KaartNaam.Text = kaart.Naam.Replace("NVIDIA GeForce ", string.Empty, StringComparison.Ordinal);
            KaartWaarde.Text = $"{Weergave.Procent(kaart.BelastingProcent)}  ·  {Math.Round(kaart.TemperatuurC):0} °C";
        }
        else
        {
            Vram.Text = "—";
            KaartNaam.Text = "GPU";
            KaartWaarde.Text = "geen NVIDIA-kaart";
        }

        CpuWaarde.Text = Weergave.Procent(meting.CpuProcent);
        RamWaarde.Text = $"{Weergave.Gb(meting.RamGebruikt)} / {Weergave.Gb(meting.RamTotaal)} GB";
    }
}
