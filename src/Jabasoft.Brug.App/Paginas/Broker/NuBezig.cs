using Jabasoft.Brug.App.Diensten;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Paginas.Broker;

/// <summary>
/// Broker &gt; Nu bezig: wat de broker op dit moment doet, elke seconde
/// bijgewerkt uit de wachtrij (GET /api/queue). Links wat loopt en wat
/// wacht, met het verloop van elke vraag en een knop om te annuleren;
/// rechts de videokaart en wat net klaar is (de broker toont afgeronde
/// vragen nog 30 seconden).
/// </summary>
public sealed class NuBezig : BrokerPagina
{
    public NuBezig() : base(TimeSpan.FromSeconds(1))
    {
    }

    protected override async Task LaadAsync()
    {
        var wachtrij = await BrokerApi.WachtrijAsync();

        Lijf.Children.Clear();
        if (wachtrij.Waarde is not { } items)
        {
            Lijf.Children.Add(Bouw.Melding($"Geen gegevens: {wachtrij.Fout}. Start de broker; dit scherm werkt zichzelf bij zodra hij er is."));
            Lijf.Children.Add(GpuPaneel());
            return;
        }

        var lopend = items.Where(i => i.Status == "Bezig").ToList();
        var wachtend = items.Where(i => i.Status == "Wachtend").OrderBy(i => i.Plek).ToList();
        var klaar = items.Where(i => i.Status is "Klaar" or "Mislukt" or "Geannuleerd").ToList();

        var kengetallen = Bouw.Kolommen(4);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Lopend", lopend.Count.ToString(Bouw.Nl), "AccentBrush"), 0, 0);
        Bouw.Zet(kengetallen, Bouw.Kengetal("In de wachtrij", wachtend.Count.ToString(Bouw.Nl), "TextMutedBrush"), 0, 1);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Net klaar", klaar.Count.ToString(Bouw.Nl), "HeaderSideGradientBrush"), 0, 2);
        Bouw.Zet(kengetallen, Bouw.Kengetal("Snelheid", Snelheid(lopend.FirstOrDefault()) ?? "—", "BorderBrush"), 0, 3);
        Lijf.Children.Add(kengetallen);

        var raster = new Grid { ColumnSpacing = 24 };
        raster.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        raster.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var links = new StackPanel { Spacing = 16 };
        links.Children.Add(Bouw.Kop("LOPEND"));
        if (lopend.Count == 0)
        {
            links.Children.Add(Bouw.Melding("Er loopt nu geen vraag. Zodra een app, Goose, Hermes of Claude de broker iets vraagt, staat het hier."));
        }

        foreach (var item in lopend)
        {
            links.Children.Add(VraagPaneel(item));
        }

        links.Children.Add(Bouw.Kop("WACHTRIJ"));
        if (wachtend.Count == 0)
        {
            links.Children.Add(Bouw.Melding("Niemand wacht."));
        }

        foreach (var item in wachtend)
        {
            links.Children.Add(VraagPaneel(item));
        }

        var rechts = new StackPanel { Spacing = 16 };
        rechts.Children.Add(Bouw.Kop("GPU"));
        rechts.Children.Add(GpuPaneel());
        rechts.Children.Add(Bouw.Kop("NET KLAAR"));
        if (klaar.Count == 0)
        {
            rechts.Children.Add(Bouw.Melding("Nog niets afgerond in de laatste 30 seconden."));
        }

        foreach (var item in klaar)
        {
            rechts.Children.Add(KlaarPaneel(item));
        }

        Grid.SetColumn(rechts, 1);
        raster.Children.Add(links);
        raster.Children.Add(rechts);
        Lijf.Children.Add(raster);
    }

    private static Controls.Paneel VraagPaneel(WachtrijItem item)
    {
        var nu = DateTimeOffset.Now;
        var inhoud = new Grid { ColumnSpacing = 24, RowSpacing = 14 };
        inhoud.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inhoud.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var kop = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        kop.Children.Add(Bouw.Pil(item.Applicatie, "AccentBrushLight"));
        if (!string.IsNullOrWhiteSpace(item.Onderdeel))
        {
            kop.Children.Add(Bouw.Pil(item.Onderdeel, "BorderBrush"));
        }

        kop.Children.Add(Bouw.Tekst(item.Model));
        kop.Children.Add(Bouw.Tekst(item.Soort, "LabelStijl"));
        Bouw.Zet(inhoud, kop, 0, 0);

        var annuleer = Bouw.Knop("Annuleren", async (s, _) =>
        {
            if (s is Button knop)
            {
                knop.IsEnabled = false;
            }

            var gelukt = await BrokerApi.AnnuleerAsync(item.Id);
            App.AppLog.Schrijf("brug", gelukt ? $"Vraag #{item.Id} geannuleerd" : $"Vraag #{item.Id} kon niet geannuleerd worden");
        }, "TextMutedBrush");
        Bouw.Zet(inhoud, annuleer, 0, 1);

        Bouw.Zet(inhoud, new Controls.Verloop { Stap = Stap(item) }, 1, 0, 2);

        var velden = Bouw.Kolommen(4, 32);
        var wachttijd = (item.Gestart ?? nu) - item.Aangemeld;
        Bouw.Zet(velden, Bouw.Veld("Wachttijd", Bouw.Duur(wachttijd)), 0, 0);
        Bouw.Zet(velden, Bouw.Veld("Bezig", item.Gestart is { } start ? Bouw.Duur(nu - start) : "—"), 0, 1);
        Bouw.Zet(velden, Bouw.Veld("Inlezen", item.Inlezen is { } inlezen ? $"{inlezen * 100:0}%" : "—"), 0, 2);
        Bouw.Zet(velden, Bouw.Veld("Tokens uit", Bouw.Getal(item.Tokens)), 0, 3);
        Bouw.Zet(inhoud, velden, 2, 0, 2);

        var rechts = item.Status == "Wachtend"
            ? $"plek {item.Plek} · {Bouw.Duur(nu - item.Aangemeld)}"
            : $"{FaseTekst(item)} · {Bouw.Duur(nu - (item.Gestart ?? item.Aangemeld))}";
        return Bouw.Paneel($"Vraag #{item.Id}", inhoud, rechts);
    }

    private static Controls.Paneel KlaarPaneel(WachtrijItem item)
    {
        var (kleur, tekst) = item.Status switch
        {
            "Klaar" => ("HealthOkBrush", "Klaar"),
            "Geannuleerd" => ("HealthBusyBrush", "Geannuleerd"),
            _ => ("HealthErrorBrush", "Mislukt"),
        };

        var regel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        regel.Children.Add(Bouw.Bolletje(kleur, 10));
        regel.Children.Add(Bouw.Tekst(item.Fout is { Length: > 0 } fout && item.Status != "Klaar" ? $"{tekst}: {fout}" : tekst, "LabelStijl"));

        var duur = item.Afgerond is { } eind && item.Gestart is { } start ? Bouw.Duur(eind - start) : "";
        var inhoud = new StackPanel { Spacing = 6, Children = { Bouw.Tekst($"{item.Model} · {item.Soort}"), regel } };
        return Bouw.Paneel($"#{item.Id} · {item.Applicatie}", inhoud, $"{Bouw.Getal(item.Tokens)} tok · {duur}");
    }

    private static Controls.Paneel GpuPaneel()
    {
        var meting = App.Meter.Laatste;
        var inhoud = new StackPanel { Spacing = 10 };
        if (meting is null || meting.Kaarten.Count == 0)
        {
            inhoud.Children.Add(Bouw.Melding("Nog geen meting van de videokaart."));
            return Bouw.Paneel("Videokaart", inhoud);
        }

        foreach (var kaart in meting.Kaarten)
        {
            inhoud.Children.Add(Bouw.Tekst(kaart.Naam));
            inhoud.Children.Add(Bouw.Balkregel("VRAM", $"{Weergave.GbUitMiB(kaart.VramGebruiktMiB)} / {Weergave.GbUitMiB(kaart.VramTotaalMiB)} GB",
                kaart.VramTotaalMiB == 0 ? 0 : (double)kaart.VramGebruiktMiB / kaart.VramTotaalMiB));
            inhoud.Children.Add(Bouw.Balkregel("Belasting", $"{kaart.BelastingProcent} % · {kaart.TemperatuurC:0} °C", kaart.BelastingProcent / 100.0, "HeaderSideGradientBrush"));
        }

        return Bouw.Paneel("Videokaart", inhoud, meting.Kaarten.Count == 1 ? "1 kaart" : $"{meting.Kaarten.Count} kaarten");
    }

    /// <summary>Welk van de vijf blokjes van het verloop oranje is.</summary>
    private static int Stap(WachtrijItem item) => item.Status switch
    {
        "Wachtend" => 1,
        "Bezig" when item.Tokens > 0 => 4,
        "Bezig" when item.Inlezen is not null => 3,
        "Bezig" => 2,
        _ => 4,
    };

    private static string FaseTekst(WachtrijItem item) => Stap(item) switch
    {
        4 => "genereren",
        3 => $"prompt {(item.Inlezen ?? 0) * 100:0}%",
        _ => "model laden",
    };

    /// <summary>Ruwe snelheid van een lopende vraag: tokens sinds de start, gedeeld door de tijd.</summary>
    private static string? Snelheid(WachtrijItem? item)
    {
        if (item is not { Tokens: > 0, Gestart: { } start })
        {
            return null;
        }

        var seconden = (DateTimeOffset.Now - start).TotalSeconds;
        return seconden <= 0 ? null : $"{item.Tokens / seconden:0} tok/s";
    }
}
