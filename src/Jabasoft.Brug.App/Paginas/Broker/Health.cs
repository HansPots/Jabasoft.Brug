using System.Text.Json.Nodes;
using Jabasoft.Brug.App.Data;
using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Health: de stand van alles waar de Brug op leunt. Bovenaan een
/// lila band met de totaalstand, daaronder een kaart per onderdeel. Wordt
/// elke 15 seconden opnieuw gecontroleerd, en met de verversknop meteen.
///
/// Kleur van het bolletje: groen = in orde, oranje = in rust of niet nodig,
/// rood = fout. De kleur staat alleen in de bolletjes; de rest blijft in de
/// themakleuren.
/// </summary>
public sealed class Health : BrokerPagina
{
    private enum Stand { Ok, Rust, Fout }

    private sealed record Kaart(string Titel, string Rechts, Stand Stand, string Status, (string Label, string Waarde)[] Velden);

    public Health() : base(TimeSpan.FromSeconds(15))
    {
    }

    protected override async Task LaadAsync()
    {
        var broker = await BrokerApi.HealthAsync();
        var kaarten = new List<Kaart>
        {
            await BrokerKaartAsync(broker),
            await BrokerDatabaseKaartAsync(broker.Gelukt),
            await BrugDatabaseKaartAsync(),
            await AiServerKaartAsync(broker.Gelukt),
            await BeeldserviceKaartAsync(),
            GpuKaart(),
        };

        Lijf.Children.Clear();
        Lijf.Children.Add(Band(kaarten));

        var raster = Bouw.Kolommen(3);
        for (var i = 0; i < kaarten.Count; i++)
        {
            Bouw.Zet(raster, Paneel(kaarten[i]), i / 3, i % 3);
        }

        Lijf.Children.Add(raster);
    }

    private static async Task<Kaart> BrokerKaartAsync(Meting<bool> broker)
    {
        if (!broker.Gelukt)
        {
            return new("Broker", ":5310", Stand.Fout, "Offline",
                [("Starten", "met de knop rechts in de actiebalk")]);
        }

        var wachtrij = await BrokerApi.WachtrijAsync();
        var lijst = wachtrij.Waarde ?? [];
        return new("Broker", ":5310", Stand.Ok, "Online",
            [
                ("Reactietijd", Ms(broker.Duur)),
                ("Lopend", lijst.Count(i => i.Status == "Bezig").ToString(Bouw.Nl)),
                ("Wachtend", lijst.Count(i => i.Status == "Wachtend").ToString(Bouw.Nl)),
            ]);
    }

    private static async Task<Kaart> BrokerDatabaseKaartAsync(bool brokerOnline)
    {
        if (!brokerOnline)
        {
            return new("Database broker", "JabasoftBroker", Stand.Rust, "Onbekend", [("Reden", "broker offline")]);
        }

        var status = await BrokerApi.TokenStatusAsync();
        var ok = status.Waarde?.Available == true;
        return new("Database broker", "JabasoftBroker", ok ? Stand.Ok : Stand.Fout, ok ? "Bereikbaar" : "Fout",
            [
                ("Tokentabel", ok ? "aanwezig" : status.Waarde?.Message ?? status.Fout ?? "—"),
                ("Reactietijd", Ms(status.Duur)),
                ("Instantie", @"(localdb)\MSSQLLocalDB"),
            ]);
    }

    private static async Task<Kaart> BrugDatabaseKaartAsync()
    {
        var (gelukt, duur, fout) = await BrugDatabase.ControleerAsync();
        return new("Database Brug", BrugDatabase.Naam, gelukt ? Stand.Ok : Stand.Fout, gelukt ? "Bereikbaar" : "Fout",
            [
                ("Reactietijd", gelukt ? Ms(duur) : "—"),
                ("Melding", fout ?? "modeltabel aanwezig"),
            ]);
    }

    private static async Task<Kaart> AiServerKaartAsync(bool brokerOnline)
    {
        if (!brokerOnline)
        {
            return new("AI-server", "", Stand.Rust, "Onbekend", [("Reden", "broker offline")]);
        }

        var instellingen = await BrokerApi.InstellingenAsync();
        var modellen = await BrokerApi.ServerModellenAsync();
        var provider = instellingen.Waarde?["provider"]?.GetValue<string>() ?? "LmStudio";
        var sectie = instellingen.Waarde?[provider == "Ollama" ? "ollama" : "lmStudio"] as JsonObject;
        var ok = modellen.Waarde?.Success == true;

        return new("AI-server", provider == "Ollama" ? "Ollama" : "LM Studio", ok ? Stand.Ok : Stand.Fout, ok ? "Bereikbaar" : "Niet bereikbaar",
            [
                ("Adres", sectie?["url"]?.GetValue<string>() ?? "—"),
                ("Modellen", ok ? (modellen.Waarde!.Models?.Count ?? 0).ToString(Bouw.Nl) : modellen.Waarde?.ErrorMessage ?? modellen.Fout ?? "—"),
                ("Daily chat", sectie?["chatModel"]?.GetValue<string>() is { Length: > 0 } chat ? chat : "—"),
                ("Reactietijd", Ms(modellen.Duur)),
            ]);
    }

    private static async Task<Kaart> BeeldserviceKaartAsync()
    {
        var beeld = await BrokerApi.BeeldserviceAsync();
        return beeld.Gelukt
            ? new("Beeldservice", ":5330", Stand.Ok, "Draait", [("Reactietijd", Ms(beeld.Duur))])
            : new("Beeldservice", ":5330", Stand.Rust, "In rust", [("Start", "bij de eerste beeldopdracht")]);
    }

    private static Kaart GpuKaart()
    {
        var meting = App.Meter.Laatste;
        if (meting?.EersteKaart is not { } kaart)
        {
            return new("GPU", "", Stand.Rust, "Geen meting", [("Bron", "nvidia-smi")]);
        }

        var velden = new List<(string, string)>
        {
            ("VRAM", $"{Weergave.GbUitMiB(kaart.VramGebruiktMiB)} / {Weergave.GbUitMiB(kaart.VramTotaalMiB)} GB"),
            ("Belasting", $"{kaart.BelastingProcent} %"),
            ("Temperatuur", $"{kaart.TemperatuurC:0} °C"),
            ("Tweede kaart", meting.Kaarten.Count > 1 ? meting.Kaarten[1].Naam : "nog niet aanwezig"),
        };

        var heet = kaart.TemperatuurC >= 85;
        return new("GPU", kaart.Naam, heet ? Stand.Fout : Stand.Ok, heet ? "Te warm" : "In orde", [.. velden]);
    }

    private static Border Band(List<Kaart> kaarten)
    {
        var fouten = kaarten.Count(k => k.Stand == Stand.Fout);
        var rust = kaarten.Count(k => k.Stand == Stand.Rust);
        var tekst = fouten > 0
            ? $"{fouten} ONDERDE{(fouten == 1 ? "EL" : "LEN")} MET EEN FOUT"
            : rust > 0 ? $"ALLES IN ORDE · {rust} IN RUST" : "ALLES IN ORDE";

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        // In rust (bijvoorbeeld de beeldservice zonder werk) is geen probleem: groen zolang er geen fout is.
        var bol = Bouw.Bolletje(fouten > 0 ? "HealthErrorBrush" : "HealthOkBrush", 16);
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
            Text = $"GECONTROLEERD {DateTime.Now:HH:mm:ss}",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = Bouw.Maat("FontSizeSmall"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            CharacterSpacing = 100,
            Foreground = Bouw.Kleur("AccentForegroundBrush"),
        };

        return new Border
        {
            Background = Bouw.Kleur("BorderBrush"),
            CornerRadius = new CornerRadius(22, 22, 22, 0),
            Height = 44,
            Child = new Grid { Margin = new Thickness(20, 0, 20, 0), Children = { links, rechts } },
        };
    }

    private static Controls.Paneel Paneel(Kaart kaart)
    {
        var inhoud = new StackPanel { Spacing = 4 };
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 6) };
        status.Children.Add(Bouw.Bolletje(kaart.Stand switch
        {
            Stand.Ok => "HealthOkBrush",
            Stand.Rust => "HealthRustBrush",
            _ => "HealthErrorBrush",
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
