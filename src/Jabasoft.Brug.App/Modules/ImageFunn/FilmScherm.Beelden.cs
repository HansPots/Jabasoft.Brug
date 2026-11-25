using System.Diagnostics;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// Het derde deel van Film maken: eerst alleen de sleutelbeelden maken, die laten controleren en de opmerkingen in het
/// verhaal verwerken (het chatmodel past stijl en beeldomschrijvingen aan), en pas daarna animeren. Zo zorg je dat
/// het in elke scène hetzelfde personage is voordat er uren rekentijd in gaan. Overgenomen uit de oude app
/// (FilmMaker.Beoordeling).
/// </summary>
public sealed partial class FilmScherm
{
    private sealed record Kaart(int Index, Image Beeld, TextBox Opmerking, CheckBox Opnieuw);

    private readonly List<Kaart> _kaarten = [];
    private readonly VariableSizedWrapGrid _beoordeling = new() { Orientation = Orientation.Horizontal, ItemWidth = 330, MaximumRowsOrColumns = 6 };
    private readonly TextBlock _beoordelingUitleg = Bouw.Melding("");
    private readonly TextBlock _beoordelingStatus = Bouw.Melding("");
    private readonly ProgressBar _beeldenVoortgang = new() { Maximum = 1, Foreground = Bouw.Kleur("AccentBrush") };
    private readonly TextBox _algemeen = new()
    {
        Header = "Opmerking voor het hele verhaal (bijvoorbeeld: het moet steeds dezelfde robot zijn)",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 70,
    };
    private Button[] _beoordelingKnoppen = [];
    private Button _beoordelingStop = null!;

    private UIElement MaakBeeldenTab()
    {
        _beoordelingStop = Bouw.Knop("Stop", async (_, _) => await StopAsync(), "TextMutedBrush");
        _beoordelingStop.IsEnabled = false;
        _beoordelingKnoppen =
        [
            Bouw.Knop("Maak sleutelbeelden", async (_, _) => await BeeldenMaakAsync()),
            Bouw.Knop("Pas opmerkingen toe en maak opnieuw", async (_, _) => await NogmaalsAsync(), "AccentBrushLight"),
            Bouw.Knop("Goedkeuren en animeren", async (_, _) => await GoedkeurenAsync()),
            Bouw.Knop("◀ Cast", (_, _) => ToonTab(2), "AccentBrushLight"),
            Bouw.Knop("Naar de film ▶", (_, _) => ToonTab(4), "AccentBrushLight"),
        ];

        return Bouw.Paneel("Controleer de sleutelbeelden", new StackPanel
        {
            Spacing = 14,
            Children =
            {
                _beoordelingUitleg,
                _beoordeling,
                _algemeen,
                _beeldenVoortgang,
                _beoordelingStatus,
                Onderdelen.Knoppen([.. _beoordelingKnoppen, _beoordelingStop]),
            },
        });
    }

    /// <summary>De scènes waarvan het beeld beoordeeld kan worden: bij "doorlopend" alleen de eerste, de rest begint op het einde van de vorige.</summary>
    private List<int> BeoordeelbareScenes() =>
        Onderdelen.Gekozen(_modus) == "doorlopend" ? [0] : Enumerable.Range(0, _scenes.Count).ToList();

    /// <summary>De scènes die nog geen sleutelbeeld hebben (of waarvan het bestand weg is).</summary>
    private List<int> NodigeSleutelbeelden() =>
        BeoordeelbareScenes().Where(i => _scenes[i].Sleutel is not { } pad || !File.Exists(pad)).ToList();

    // ---------------------------------------------------------------- engelse tekst afleiden

    /// <summary>
    /// De modellen lezen alleen Engels: scènes zonder Engelse tekst eerst laten afleiden. Het resultaat komt in de
    /// zichtbare vakken "Eerste beeld" en "Beweging en camera" van de scène; wat daar staat, gaat naar het model. False = mislukt.
    /// </summary>
    private async Task<bool> LeidAfMissendeAsync()
    {
        var afleiden = _scenes.Where(s => string.IsNullOrEmpty(s.Bestand) && string.IsNullOrWhiteSpace(s.Beeld)).ToList();
        for (var n = 0; n < afleiden.Count; n++)
        {
            _status.Text = _beoordelingStatus.Text = $"Engelse tekst afleiden voor scène {n + 1} van {afleiden.Count}…";
            _kop.Text = "VERHAAL VOORBEREIDEN";
            var (beeld, beweging, fout) = await Verhaalschrijver.LeidAfAsync(_stijl.Text, afleiden[n].Gebeurt, CancellationToken.None);
            if (beeld is null)
            {
                _status.Text = _beoordelingStatus.Text = $"Afleiden mislukt voor '{afleiden[n].Titel}': {fout}";
                return false;
            }

            afleiden[n].Beeld = beeld;
            afleiden[n].Beweging = beweging ?? string.Empty;
        }

        ToonScene();
        return true;
    }

    // ---------------------------------------------------------------- sleutelbeelden maken

    /// <summary>Laat de service de sleutelbeelden van deze scènes (opnieuw) maken. Hun eerder gemaakte clip klopt dan niet meer.</summary>
    private async Task<bool> RunSleutelbeeldenAsync(IEnumerable<int> opnieuw, Stopwatch klok)
    {
        var set = opnieuw.ToHashSet();
        foreach (var i in set)
        {
            _scenes[i].Opnieuw = true;
            MaakOngeldig(_scenes[i]);
        }

        Teken();
        var opdracht = Opdracht("sleutelbeelden");
        opdracht.Seed = Random.Shared.Next();

        _kop.Text = "SLEUTELBEELDEN MAKEN";
        _beoordelingStatus.Text = "Sleutelbeelden starten…";
        _jobId = await Beeldservice.StartAsync(opdracht, new Progress<string>(t => _beoordelingStatus.Text = t));
        _stop.IsEnabled = _beoordelingStop.IsEnabled = true;
        App.AppLog.Schrijf("imagefunn", $"Sleutelbeelden maken: {set.Count}");

        try
        {
            while (true)
            {
                var stand = await Beeldservice.StatusAsync(_jobId);
                if (stand.Scenes.Count == _scenes.Count)
                {
                    for (var i = 0; i < _scenes.Count; i++)
                    {
                        _scenes[i].Sleutel = stand.Scenes[i].Sleutel;
                    }
                }

                _beeldenVoortgang.Value = _voortgang.Value = stand.Voortgang;
                _beoordelingStatus.Text = _status.Text = stand.Stap;
                _tijd.Text = $"Verstreken {klok.Elapsed:h\\:mm\\:ss}";

                // Een nieuw beeld laat zich meteen zien, ook voordat de rest klaar is.
                foreach (var i in set.Where(i => _scenes[i].Sleutel is { } p && File.Exists(p)))
                {
                    await VernieuwKaartAsync(i);
                }

                if (stand.IsKlaar || stand.IsMislukt)
                {
                    foreach (var i in set)
                    {
                        _scenes[i].Opnieuw = false;
                    }

                    if (stand.IsMislukt)
                    {
                        _beoordelingStatus.Text = stand.Foutmelding ?? "Sleutelbeelden maken mislukt.";
                    }

                    return stand.IsKlaar;
                }

                await Task.Delay(Peilen);
            }
        }
        finally
        {
            _beoordelingStop.IsEnabled = false;
        }
    }

    // ---------------------------------------------------------------- het controlescherm

    /// <summary>Bouwt de kaarten op uit de stand van het verhaal. Wat je al getypt of aangevinkt had, blijft staan.</summary>
    private void BouwBeoordelingKaarten()
    {
        var oud = _kaarten.ToDictionary(k => k.Index, k => (Tekst: k.Opmerking.Text, Opnieuw: k.Opnieuw.IsChecked == true));
        _kaarten.Clear();
        _beoordeling.Children.Clear();

        foreach (var i in BeoordeelbareScenes())
        {
            var beeld = new Image { Height = 180, Stretch = Stretch.Uniform };
            var opmerking = new TextBox
            {
                PlaceholderText = "Wat moet er anders aan dit beeld?",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 64,
                Text = oud.TryGetValue(i, out var vorig) ? vorig.Tekst : "",
            };
            var opnieuw = new CheckBox { Content = "Opnieuw maken", IsChecked = oud.TryGetValue(i, out var v) && v.Opnieuw };

            _beoordeling.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(8, 8, 8, 8),
                BorderThickness = new Thickness(2),
                BorderBrush = Bouw.Kleur("BorderBrush"),
                Background = Bouw.Kleur("BackgroundBrush"),
                Child = new StackPanel { Spacing = 8, Children = { Bouw.Tekst($"{i + 1}. {_scenes[i].Titel}"), beeld, opnieuw, opmerking } },
            });
            _kaarten.Add(new Kaart(i, beeld, opmerking, opnieuw));
            _ = VernieuwKaartAsync(i);
        }

        _beoordelingUitleg.Text = Onderdelen.Gekozen(_modus) == "doorlopend"
            ? "Bij 'doorlopend' begint elke volgende scène op het laatste beeld van de vorige; alleen het eerste beeld hoeft beoordeeld te worden. Klopt het niet, typ dan wat anders moet of vink 'Opnieuw maken' aan."
            : "Elke scène begint met zo'n beeld. Klopt het niet, typ dan bij het beeld wat anders moet (bijvoorbeeld: andere kleur, andere vorm) of vink 'Opnieuw maken' aan. Het chatmodel verwerkt je opmerkingen in het verhaal.";

        if (_jobId is null)
        {
            _beoordelingStatus.Text = NodigeSleutelbeelden().Count > 0
                ? "De sleutelbeelden zijn nog niet (allemaal) gemaakt. Druk op 'Maak sleutelbeelden'."
                : "Controleer de beelden. Tevreden? Dan kun je ze goedkeuren en begint het animeren.";
        }
    }

    private async Task VernieuwKaartAsync(int index)
    {
        if (_kaarten.FirstOrDefault(k => k.Index == index) is { } kaart && await Onderdelen.LaadBeeldAsync(_scenes[index].Sleutel, 640) is { } beeld)
        {
            kaart.Beeld.Source = beeld;
        }
    }

    private void BeoordelingBezig(bool bezig)
    {
        foreach (var knop in _beoordelingKnoppen)
        {
            knop.IsEnabled = !bezig;
        }

        foreach (var kaart in _kaarten)
        {
            kaart.Opmerking.IsEnabled = kaart.Opnieuw.IsEnabled = !bezig;
        }

        _algemeen.IsEnabled = !bezig;
    }

    /// <summary>Maakt de sleutelbeelden die nog ontbreken (en eerst de cast, als die aanstaat en nog niet klaar is).</summary>
    private async Task BeeldenMaakAsync()
    {
        if (_scenes.Any(s => string.IsNullOrWhiteSpace(s.Gebeurt) && string.IsNullOrWhiteSpace(s.Beeld)))
        {
            _beoordelingStatus.Text = "Elke scène heeft een omschrijving nodig ('Wat gebeurt er?' bij stap 2).";
            return;
        }

        if (NodigeSleutelbeelden().Count == 0)
        {
            _beoordelingStatus.Text = "Alle sleutelbeelden zijn al gemaakt. Wil je er een anders, typ dan een opmerking of vink 'Opnieuw maken' aan.";
            return;
        }

        BeoordelingBezig(true);
        var klok = Stopwatch.StartNew();
        try
        {
            if (!await LeidAfMissendeAsync())
            {
                return;
            }

            if (_castGebruik.IsChecked == true && _cast.Count > 0 && _cast.Any(c => c.Referentie is not { } p || !File.Exists(p)))
            {
                _beoordelingStatus.Text = "Eerst de referentiebeelden van de cast maken…";
                if (!await RunCastAsync(klok))
                {
                    _beoordelingStatus.Text = _castStatus.Text;
                    return;
                }
            }

            BouwBeoordelingKaarten();
            var gelukt = await RunSleutelbeeldenAsync(NodigeSleutelbeelden(), klok);
            if (gelukt)
            {
                _beoordelingStatus.Text = "Sleutelbeelden klaar. Controleer ze: klopt iets niet, typ een opmerking en laat ze opnieuw maken.";
            }
        }
        catch (Exception fout)
        {
            _beoordelingStatus.Text = Beeldservice.Foutmelding(fout);
        }
        finally
        {
            _jobId = null;
            BeoordelingBezig(false);
            VernieuwStappen();
        }
    }

    /// <summary>
    /// Verwerkt de opmerkingen in het verhaal en maakt de betrokken beelden opnieuw. Een opmerking voor het hele verhaal
    /// (of een gewijzigde personagebeschrijving) betekent dat alle beelden opnieuw gemaakt worden.
    /// </summary>
    private async Task NogmaalsAsync()
    {
        var algemeen = _algemeen.Text.Trim();
        var opmerkingen = _kaarten.Where(k => k.Opmerking.Text.Trim().Length > 0).ToDictionary(k => k.Index + 1, k => k.Opmerking.Text.Trim());
        var aangevinkt = _kaarten.Where(k => k.Opnieuw.IsChecked == true).Select(k => k.Index).ToList();
        if (algemeen.Length == 0 && opmerkingen.Count == 0 && aangevinkt.Count == 0)
        {
            _beoordelingStatus.Text = "Typ een opmerking, of vink bij een beeld 'Opnieuw maken' aan.";
            return;
        }

        BeoordelingBezig(true);
        var klok = Stopwatch.StartNew();
        try
        {
            var opnieuw = new HashSet<int>(aangevinkt);
            var alles = false;
            if (algemeen.Length > 0 || opmerkingen.Count > 0)
            {
                _beoordelingStatus.Text = "Het chatmodel verwerkt je opmerkingen in het verhaal…";
                var scenes = BeoordeelbareScenes().Select(i => (i + 1, _scenes[i].Titel, _scenes[i].Beeld)).ToList();
                var (stijl, beelden, fout) = await Verhaalschrijver.PasOpmerkingenToeAsync(_stijl.Text, scenes, algemeen, opmerkingen, CancellationToken.None);
                if (beelden is null)
                {
                    _beoordelingStatus.Text = $"Opmerkingen verwerken mislukt: {fout}";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(stijl) && stijl != _stijl.Text.Trim())
                {
                    _stijl.Text = stijl;
                    alles = true; // de personagebeschrijving staat in elk beeld
                }

                foreach (var (nr, beeld) in beelden.Where(b => b.Key >= 1 && b.Key <= _scenes.Count))
                {
                    _scenes[nr - 1].Beeld = beeld;
                    opnieuw.Add(nr - 1);
                }

                foreach (var nr in opmerkingen.Keys.Where(n => n >= 1 && n <= _scenes.Count))
                {
                    opnieuw.Add(nr - 1);
                }

                alles |= algemeen.Length > 0;
            }

            // Zonder gelijke personagebeschrijving zouden de andere beelden niet meer bij het nieuwe eerste beeld passen.
            if (Onderdelen.Gekozen(_consistentie) == "eerste" && opnieuw.Contains(0) && Onderdelen.Gekozen(_modus) != "doorlopend")
            {
                alles = true;
            }

            if (alles)
            {
                opnieuw = BeoordeelbareScenes().ToHashSet();
            }

            _beoordelingStatus.Text = $"{opnieuw.Count} beeld(en) opnieuw maken…";
            var gelukt = await RunSleutelbeeldenAsync(opnieuw.Order(), klok);

            // Opmerkingen zijn verwerkt: de vakjes weer leeg, de nieuwe beelden staan er.
            if (gelukt)
            {
                _algemeen.Text = "";
                foreach (var kaart in _kaarten)
                {
                    kaart.Opmerking.Text = "";
                    kaart.Opnieuw.IsChecked = false;
                }

                _beoordelingStatus.Text = "Nieuwe beelden staan klaar. Nog iets aan te passen, of goedkeuren?";
            }

            foreach (var kaart in _kaarten)
            {
                await VernieuwKaartAsync(kaart.Index);
            }
        }
        catch (Exception fout)
        {
            _beoordelingStatus.Text = Beeldservice.Foutmelding(fout);
        }
        finally
        {
            _jobId = null;
            BeoordelingBezig(false);
        }
    }

    private async Task GoedkeurenAsync()
    {
        ToonTab(4);
        ZetBezig(true);
        try
        {
            await RunFilmAsync(Stopwatch.StartNew());
        }
        catch (Exception fout)
        {
            _status.Text = Beeldservice.Foutmelding(fout);
        }
        finally
        {
            _jobId = null;
            ZetBezig(false);
            VernieuwStappen();
        }
    }
}
