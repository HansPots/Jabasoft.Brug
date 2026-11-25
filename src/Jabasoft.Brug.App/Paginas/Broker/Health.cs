using System.Text.Json.Nodes;
using Jabasoft.Brug.App.Data;
using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Health: de stand van alles waar de Brug op leunt. Bovenaan de
/// band met de totaalstand, daaronder een kaart per onderdeel.
///
/// De controles zelf doet App.Gezondheid op de achtergrond (elke 30 s, alle
/// tegelijk). Deze pagina wacht dus nergens op: hij tekent meteen wat er
/// bekend is, vraagt bij openen en met de verversknop een nieuwe ronde aan,
/// en tekent elke kaart opnieuw zodra die binnen is.
///
/// Kleur van het bolletje: groen = in orde, grijs = in rust of nog bezig met
/// controleren, rood = fout.
/// </summary>
public sealed class Health : BrokerPagina
{
    private readonly DispatcherTimer _klok = new() { Interval = TimeSpan.FromSeconds(2) };

    public Health()
    {
        // Elke 2 s opnieuw tekenen voor de videokaart (live uit de systeemmeter).
        _klok.Tick += (_, _) => Teken();
        Loaded += (_, _) =>
        {
            App.Gezondheid.Bijgewerkt += Bijgewerkt;
            _klok.Start();
        };
        Unloaded += (_, _) =>
        {
            App.Gezondheid.Bijgewerkt -= Bijgewerkt;
            _klok.Stop();
        };
    }

    /// <summary>Bij openen en met de verversknop: meteen tekenen en een nieuwe ronde controles aanvragen.</summary>
    protected override Task LaadAsync()
    {
        Teken();
        _ = App.Gezondheid.ControleerAsync();
        return Task.CompletedTask;
    }

    private void Bijgewerkt(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Teken);

    private void Teken()
    {
        var kaarten = App.Gezondheid.Kaarten().Append(Gezondheidsbewaking.GpuKaart()).ToList();

        Lijf.Children.Clear();
        Lijf.Children.Add(Band(kaarten));

        var raster = Bouw.Kolommen(3);
        for (var i = 0; i < kaarten.Count; i++)
        {
            Bouw.Zet(raster, Paneel(kaarten[i]), i / 3, i % 3);
        }

        Lijf.Children.Add(raster);
    }

    private static Border Band(List<Gezondheidskaart> kaarten)
    {
        var fouten = kaarten.Count(k => k.Stand == Gezondheidsstand.Fout);
        var rust = kaarten.Count(k => k.Stand == Gezondheidsstand.Rust);
        var bezig = kaarten.Count(k => k.Stand == Gezondheidsstand.Controleren);
        var tekst = fouten > 0
            ? $"{fouten} ONDERDE{(fouten == 1 ? "EL" : "LEN")} MET EEN FOUT"
            : bezig > 0 ? $"CONTROLEREN · NOG {bezig} BEZIG"
            : rust > 0 ? $"ALLES IN ORDE · {rust} IN RUST" : "ALLES IN ORDE";

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        // In rust (bijvoorbeeld de beeldservice zonder werk) is geen probleem: groen zolang er geen fout is.
        var bol = Bouw.Bolletje(fouten > 0 ? "HealthErrorBrush" : bezig > 0 ? "HealthRustBrush" : "HealthOkBrush", 16);
        bol.Stroke = Bouw.Kleur("AccentForegroundBrush");
        bol.StrokeThickness = 2;
        links.Children.Add(bol);
        links.Children.Add(new TextBlock
        {
            Text = tekst,
            FontSize = Bouw.Maat("FontSizeTitle"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            CharacterSpacing = 120,
            Foreground = Bouw.Kleur("AccentForegroundBrush"),
        });

        var rechts = new TextBlock
        {
            Text = App.Gezondheid.LaatstGecontroleerd is { } tijd ? $"GECONTROLEERD {tijd:HH:mm:ss}" : "EERSTE CONTROLE LOOPT",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = Bouw.Maat("FontSizeSmall"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            CharacterSpacing = 100,
            Foreground = Bouw.Kleur("AccentForegroundBrush"),
        };

        return new Border
        {
            Background = Bouw.Kleur("AccentBrushDark"),
            CornerRadius = new CornerRadius(22, 22, 22, 22),
            Height = 44,
            Child = new Grid { Margin = new Thickness(20, 0, 20, 0), Children = { links, rechts } },
        };
    }

    private static Controls.Paneel Paneel(Gezondheidskaart kaart)
    {
        var inhoud = new StackPanel { Spacing = 4 };
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 6) };
        status.Children.Add(Bouw.Bolletje(kaart.Stand switch
        {
            Gezondheidsstand.Ok => "HealthOkBrush",
            Gezondheidsstand.Fout => "HealthErrorBrush",
            _ => "HealthRustBrush",
        }));
        var statusTekst = Bouw.Tekst(kaart.Status);
        statusTekst.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        status.Children.Add(statusTekst);
        inhoud.Children.Add(status);

        foreach (var (label, waarde) in kaart.Velden)
        {
            inhoud.Children.Add(Bouw.Veld(label, waarde));
        }

        return Bouw.Paneel(kaart.Titel, inhoud, kaart.Rechts);
    }

    private static string Ms(TimeSpan duur) => $"{duur.TotalMilliseconds:0} ms";
}
