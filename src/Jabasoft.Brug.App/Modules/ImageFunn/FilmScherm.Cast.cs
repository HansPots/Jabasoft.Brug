using System.Diagnostics;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// Het tweede deel van Film maken: de cast, een lijst met spelers, plekken en belangrijke voorwerpen, elk met een
/// eigen referentiebeeld. Bij de sleutelbeelden gaan het beeld van de speler, de plek en het voorwerp van die scène als
/// voorbeeld mee (IP-Adapter), zodat een personage er overal hetzelfde uitziet. Optioneel: zonder het vinkje werkt
/// alles zonder cast. Overgenomen uit de oude app (FilmMaker.Cast).
/// </summary>
public sealed partial class FilmScherm
{
    private sealed record CastKaart(CastItem Item, Image Beeld, CheckBox Opnieuw);

    private List<CastItem> _cast = [];
    private readonly List<CastKaart> _castKaarten = [];

    private readonly CheckBox _castGebruik = new() { Content = "Spelers, plekken en voorwerpen gebruiken (optioneel)" };
    private readonly StackPanel _castInhoud = new() { Spacing = 14 };
    private readonly StackPanel _castUit = new() { Spacing = 12 };
    private readonly VariableSizedWrapGrid _castLijst = new() { Orientation = Orientation.Horizontal, ItemWidth = 280, MaximumRowsOrColumns = 6 };
    private readonly TextBlock _castStatus = Bouw.Melding("");
    private readonly ProgressBar _castVoortgang = new() { Maximum = 1, Foreground = Bouw.Kleur("AccentBrush") };
    private Button[] _castKnoppen = [];
    private Button _castStop = null!;

    // ---- per scène: wie, waar, wat
    private readonly StackPanel _sceneCast = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly StackPanel _spelers = new() { Orientation = Orientation.Horizontal, Spacing = 14 };
    private readonly StackPanel _voorwerpen = new() { Orientation = Orientation.Horizontal, Spacing = 14 };
    private readonly ComboBox _plek = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    private UIElement MaakCastTab()
    {
        _castGebruik.Checked += (_, _) => ZetCastGebruik();
        _castGebruik.Unchecked += (_, _) => ZetCastGebruik();

        _castStop = Bouw.Knop("Stop", async (_, _) => await StopAsync(), "TextMutedBrush");
        _castStop.IsEnabled = false;
        _castKnoppen =
        [
            Bouw.Knop("+ Speler", (_, _) => VoegCastToe("speler", "Nieuwe speler"), "AccentBrushLight"),
            Bouw.Knop("+ Plek", (_, _) => VoegCastToe("plek", "Nieuwe plek"), "AccentBrushLight"),
            Bouw.Knop("+ Voorwerp", (_, _) => VoegCastToe("voorwerp", "Nieuw voorwerp"), "AccentBrushLight"),
            Bouw.Knop("Lijst uit verhaal laten maken", async (_, _) => await CastLijstAsync()),
            Bouw.Knop("Maak referentiebeelden", async (_, _) => await CastMaakAsync()),
        ];

        _castInhoud.Children.Add(_castLijst);
        _castInhoud.Children.Add(_castVoortgang);
        _castInhoud.Children.Add(_castStatus);
        _castInhoud.Children.Add(Onderdelen.Knoppen([.. _castKnoppen, _castStop]));
        _castUit.Children.Add(Bouw.Melding("De cast staat uit. Zet hem aan als personages, plekken of voorwerpen in elke scène hetzelfde moeten blijven."));

        return Bouw.Paneel("Cast", new StackPanel
        {
            Spacing = 14,
            Children =
            {
                _castGebruik,
                Bouw.Melding("Elke speler, plek en belangrijk voorwerp krijgt een eigen referentiebeeld: een speler op een neutrale achtergrond, een plek zonder personen, een voorwerp los. Die beelden gaan bij elke scène als voorbeeld mee."),
                _castInhoud,
                _castUit,
                Onderdelen.Knoppen(Bouw.Knop("◀ Scènes", (_, _) => ToonTab(1), "AccentBrushLight"), Bouw.Knop("Volgende: Beelden ▶", (_, _) => ToonTab(3))),
            },
        });
    }

    private UIElement MaakSceneCast()
    {
        _plek.SelectionChanged += (_, _) =>
        {
            if (_vullen || Gekozen is not { } scene || _plek.SelectedItem is null)
            {
                return;
            }

            scene.Plek = _plek.SelectedIndex > 0 ? (string)_plek.SelectedItem : null;
            MaakOngeldig(scene, sleutelOok: true);
            Teken();
        };

        _sceneCast.Children.Add(Bouw.Melding("Wie speelt mee in deze scène?"));
        _sceneCast.Children.Add(_spelers);
        _sceneCast.Children.Add(Bouw.Melding("Waar speelt het?"));
        _sceneCast.Children.Add(_plek);
        _sceneCast.Children.Add(Bouw.Melding("Welke belangrijke voorwerpen komen erin voor? (het eerste krijgt een eigen voorbeeldbeeld)"));
        _sceneCast.Children.Add(_voorwerpen);
        return _sceneCast;
    }

    /// <summary>Hoe sterk een speler, plek en voorwerp op hun referentiebeeld moeten lijken, afgeleid van 'Gelijkenis'.</summary>
    private (double Speler, double Plek, double Voorwerp) CastSterkte() => _gelijkenis.SelectedIndex switch
    {
        0 => (0.25, 0.25, 0.1),
        2 => (0.5, 0.45, 0.25),
        _ => (0.35, 0.35, 0.15),
    };

    /// <summary>Een kopie van de scène met de paden van de referentiebeelden van haar spelers, plek en voorwerp erin.</summary>
    private FilmScene KopieVoorService(FilmScene scene)
    {
        var kopie = Kopie(scene);
        if (_castGebruik.IsChecked != true)
        {
            return kopie;
        }

        string? Pad(string soort, string? naam) =>
            _cast.FirstOrDefault(c => c.Soort == soort && c.Naam == naam)?.Referentie is { } p && File.Exists(p) ? p : null;

        kopie.SpelerRefs = scene.Spelers.Select(n => Pad("speler", n)).OfType<string>().ToList();
        kopie.PlekRef = Pad("plek", scene.Plek);
        kopie.VoorwerpRef = scene.Voorwerpen.Select(n => Pad("voorwerp", n)).FirstOrDefault(p => p is not null);
        return kopie;
    }

    private void ZetCastGebruik()
    {
        var aan = _castGebruik.IsChecked == true;
        _castInhoud.Visibility = _sceneCast.Visibility = aan ? Visibility.Visible : Visibility.Collapsed;
        _castUit.Visibility = aan ? Visibility.Collapsed : Visibility.Visible;
        ToonSceneCast(Gekozen);
        VernieuwStappen();
    }

    private void ToonSceneCast(FilmScene? scene)
    {
        var oud = _vullen;
        _vullen = true;
        try
        {
            VulVinkjes(_spelers, "speler", scene?.Spelers, "Nog geen spelers - maak ze bij stap 3 (Cast)", namen => scene!.Spelers = namen);
            VulVinkjes(_voorwerpen, "voorwerp", scene?.Voorwerpen, "Nog geen voorwerpen - maak ze bij stap 3 (Cast)", namen => scene!.Voorwerpen = namen);

            _plek.Items.Clear();
            _plek.Items.Add("(geen vaste plek)");
            foreach (var plek in _cast.Where(c => c.Soort == "plek"))
            {
                _plek.Items.Add(plek.Naam);
            }

            _plek.SelectedIndex = scene?.Plek is { } naam && _plek.Items.Contains(naam) ? _plek.Items.IndexOf(naam) : 0;
        }
        finally
        {
            _vullen = oud;
        }
    }

    private void VulVinkjes(StackPanel paneel, string soort, List<string>? gekozen, string leegTekst, Action<List<string>> zet)
    {
        paneel.Children.Clear();
        foreach (var item in _cast.Where(c => c.Soort == soort))
        {
            var vak = new CheckBox { Content = item.Naam, IsChecked = gekozen?.Contains(item.Naam) == true };
            void Bijwerken()
            {
                if (_vullen || Gekozen is not { } scene)
                {
                    return;
                }

                zet(paneel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList());
                MaakOngeldig(scene, sleutelOok: true);
                Teken();
            }

            vak.Checked += (_, _) => Bijwerken();
            vak.Unchecked += (_, _) => Bijwerken();
            paneel.Children.Add(vak);
        }

        if (!_cast.Any(c => c.Soort == soort))
        {
            paneel.Children.Add(Bouw.Melding(leegTekst));
        }
    }

    // ---------------------------------------------------------------- de kaarten

    private static readonly (string Tag, string Naam)[] Soorten = [("speler", "Speler"), ("plek", "Plek"), ("voorwerp", "Voorwerp")];

    private void BouwCastKaarten()
    {
        _castKaarten.Clear();
        _castLijst.Children.Clear();

        foreach (var item in _cast)
        {
            var naam = new TextBox { Header = "Naam", Text = item.Naam };
            naam.LostFocus += (_, _) => HernoemCast(item, naam.Text.Trim());

            var soort = Onderdelen.Keuze("Soort", Soorten, item.Soort);
            soort.SelectionChanged += (_, _) => item.Soort = Onderdelen.Gekozen(soort);

            var beschrijving = new TextBox { Header = "Hoe ziet het eruit?", Text = item.Beschrijving, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90 };
            ToolTipService.SetToolTip(beschrijving, "Beschrijf alleen hoe de speler of het voorwerp eruitziet, of hoe de plek eruitziet (zonder personen). Hoe preciezer, hoe beter het blijft kloppen.");

            var beeld = new Image { Height = item.Soort == "speler" ? 200 : 140, Stretch = Stretch.Uniform };
            var opnieuw = new CheckBox { Content = "Opnieuw maken", IsChecked = item.Opnieuw };
            opnieuw.Checked += (_, _) => item.Opnieuw = true;
            opnieuw.Unchecked += (_, _) => item.Opnieuw = false;

            // Een andere omschrijving betekent een nieuw beeld: de Engelse tekst wordt opnieuw afgeleid.
            beschrijving.TextChanged += (_, _) =>
            {
                if (item.Beschrijving == beschrijving.Text)
                {
                    return;
                }

                item.Beschrijving = beschrijving.Text;
                item.Beeld = string.Empty;
                if (item.Referentie is not null)
                {
                    opnieuw.IsChecked = true;
                }
            };

            var inhoud = new StackPanel
            {
                Spacing = 8,
                Children = { naam, soort, beschrijving, beeld, opnieuw, Bouw.Knop("Verwijderen", (_, _) => VerwijderCast(item), "TextMutedBrush") },
            };
            _castLijst.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(8, 8, 8, 8),
                BorderThickness = new Thickness(2),
                BorderBrush = Bouw.Kleur("BorderBrush"),
                Background = Bouw.Kleur("BackgroundBrush"),
                Child = inhoud,
            });

            _castKaarten.Add(new CastKaart(item, beeld, opnieuw));
            _ = VernieuwCastBeeldAsync(item);
        }

        _castStatus.Text = _cast.Count == 0
            ? "Nog geen spelers, plekken of voorwerpen. Laat het chatmodel een lijst maken uit je verhaal, of voeg ze zelf toe."
            : "Controleer de lijst. Maak daarna de referentiebeelden en kijk of het is wat je bedoelt.";
    }

    private async Task VernieuwCastBeeldAsync(CastItem item)
    {
        var kaart = _castKaarten.FirstOrDefault(k => ReferenceEquals(k.Item, item));
        if (kaart is not null && await Onderdelen.LaadBeeldAsync(item.Referentie, 500) is { } beeld)
        {
            kaart.Beeld.Source = beeld;
        }
    }

    private void HernoemCast(CastItem item, string nieuw)
    {
        if (nieuw.Length == 0 || nieuw == item.Naam || _cast.Any(c => !ReferenceEquals(c, item) && c.Naam == nieuw))
        {
            return;
        }

        var oud = item.Naam;
        item.Naam = nieuw;
        foreach (var scene in _scenes)
        {
            scene.Spelers = scene.Spelers.Select(n => n == oud ? nieuw : n).ToList();
            scene.Voorwerpen = scene.Voorwerpen.Select(n => n == oud ? nieuw : n).ToList();
            if (scene.Plek == oud)
            {
                scene.Plek = nieuw;
            }
        }
    }

    private void VerwijderCast(CastItem item)
    {
        _cast.Remove(item);
        foreach (var scene in _scenes)
        {
            scene.Spelers = scene.Spelers.Where(n => n != item.Naam).ToList();
            scene.Voorwerpen = scene.Voorwerpen.Where(n => n != item.Naam).ToList();
            if (scene.Plek == item.Naam)
            {
                scene.Plek = null;
            }
        }

        BouwCastKaarten();
        VernieuwStappen();
    }

    private string UniekeNaam(string basis)
    {
        var naam = basis;
        for (var n = 2; _cast.Any(c => c.Naam == naam); n++)
        {
            naam = $"{basis} {n}";
        }

        return naam;
    }

    private void VoegCastToe(string soort, string naam)
    {
        _cast.Add(new CastItem { Naam = UniekeNaam(naam), Soort = soort });
        BouwCastKaarten();
        VernieuwStappen();
    }

    /// <summary>
    /// Een beeld van een ander scherm (Tekst naar beeld › Gebruik in film) als voorwerp in de cast, met dat beeld als
    /// referentie. Naam en soort pas je daarna aan op de kaart.
    /// </summary>
    public Task OntvangAsync(string pad)
    {
        _cast.Add(new CastItem { Naam = UniekeNaam("Uit Tekst naar beeld"), Soort = "voorwerp", Referentie = pad, Beschrijving = "(eigen beeld)", Beeld = "(eigen beeld)" });
        _castGebruik.IsChecked = true;
        ToonTab(2);
        _castStatus.Text = "Beeld toegevoegd aan de cast. Geef het een naam, kies of het een speler, plek of voorwerp is, en zet het bij stap 2 bij de scènes waar het in hoort.";
        return Task.CompletedTask;
    }

    private void CastBezig(bool bezig)
    {
        foreach (var knop in _castKnoppen)
        {
            knop.IsEnabled = !bezig;
        }

        _castStop.IsEnabled = bezig;
        foreach (var kaart in _castKaarten)
        {
            kaart.Opnieuw.IsEnabled = !bezig;
        }
    }

    // ---------------------------------------------------------------- lijst laten schrijven

    private async Task CastLijstAsync()
    {
        if (_scenes.All(s => string.IsNullOrWhiteSpace(s.Gebeurt) && string.IsNullOrWhiteSpace(s.Beeld)))
        {
            _castStatus.Text = "Schrijf eerst het verhaal (de scènes), dan kan het chatmodel zien wie en wat erin voorkomt.";
            return;
        }

        if (_cast.Count > 0 && !await Onderdelen.BevestigAsync(XamlRoot, "Nieuwe lijst?", "De huidige lijst wordt vervangen door een nieuwe.", "Vervangen"))
        {
            return;
        }

        CastBezig(true);
        _castStatus.Text = "Het chatmodel haalt de spelers, plekken en voorwerpen uit het verhaal… (dit kan een minuut duren)";
        try
        {
            var (voorstel, fout) = await Verhaalschrijver.MaakCastAsync(_stijl.Text, _scenes, CancellationToken.None);
            if (voorstel is null)
            {
                _castStatus.Text = $"Lijst maken mislukt: {fout}";
                return;
            }

            // Een bestaand referentiebeeld blijft staan als naam, soort en omschrijving gelijk zijn.
            foreach (var nieuw in voorstel.Cast)
            {
                if (_cast.FirstOrDefault(c => c.Naam == nieuw.Naam && c.Soort == nieuw.Soort && c.Beschrijving == nieuw.Beschrijving) is { } oud)
                {
                    nieuw.Referentie = oud.Referentie;
                }
            }

            _cast = voorstel.Cast;
            for (var i = 0; i < _scenes.Count; i++)
            {
                if (voorstel.Scenes.TryGetValue(i + 1, out var wie))
                {
                    _scenes[i].Spelers = wie.Spelers;
                    _scenes[i].Plek = wie.Plek;
                    _scenes[i].Voorwerpen = wie.Voorwerpen;
                    MaakOngeldig(_scenes[i], sleutelOok: true);
                }
            }

            BouwCastKaarten();
            _castStatus.Text = $"{_cast.Count(c => c.Soort == "speler")} spelers, {_cast.Count(c => c.Soort == "plek")} plekken en " +
                               $"{_cast.Count(c => c.Soort == "voorwerp")} voorwerpen gevonden. Pas aan wat je wilt en maak de referentiebeelden.";
            Teken();
        }
        finally
        {
            CastBezig(false);
        }
    }

    // ---------------------------------------------------------------- referentiebeelden maken

    private async Task CastMaakAsync()
    {
        if (_cast.Count == 0)
        {
            _castStatus.Text = "Er zijn nog geen spelers, plekken of voorwerpen.";
            return;
        }

        CastBezig(true);
        try
        {
            await RunCastAsync(Stopwatch.StartNew());
        }
        catch (Exception fout)
        {
            _castStatus.Text = Beeldservice.Foutmelding(fout);
        }
        finally
        {
            _jobId = null;
            CastBezig(false);
            VernieuwStappen();
        }
    }

    /// <summary>
    /// Maakt de referentiebeelden die nog ontbreken of opnieuw moeten. Scènes die een speler, plek of voorwerp met een
    /// nieuw beeld gebruiken krijgen daarna een nieuw sleutelbeeld, want dat hoort bij het oude voorbeeld.
    /// </summary>
    private async Task<bool> RunCastAsync(Stopwatch klok)
    {
        var nodig = _cast.Where(c => c.Opnieuw || c.Referentie is not { } p || !File.Exists(p)).ToList();
        if (nodig.Count == 0)
        {
            _castStatus.Text = "Alle referentiebeelden zijn al gemaakt.";
            return true;
        }

        // De beeldmodellen lezen alleen Engels: de omschrijving eerst laten omzetten. Bij Letterlijk is de omschrijving zelf de prompt.
        foreach (var item in nodig.Where(c => string.IsNullOrWhiteSpace(c.Beeld)))
        {
            if (string.IsNullOrWhiteSpace(item.Beschrijving))
            {
                _castStatus.Text = $"'{item.Naam}' heeft nog geen omschrijving.";
                return false;
            }

            if (Letterlijk)
            {
                item.Beeld = item.Beschrijving.Trim();
                continue;
            }

            _castStatus.Text = _status.Text = $"Omschrijving van '{item.Naam}' omzetten voor het beeldmodel…";
            var (beeld, fout) = await Verhaalschrijver.LeidCastBeeldAsync(item.Soort, StijlVoorService(), item.Beschrijving, CancellationToken.None);
            if (beeld is null)
            {
                _castStatus.Text = $"Omzetten mislukt voor '{item.Naam}': {fout}";
                return false;
            }

            item.Beeld = beeld;
        }

        var opdracht = Opdracht("cast");
        opdracht.Seed = Random.Shared.Next();
        opdracht.Cast = _cast;
        opdracht.Scenes = [];

        _kop.Text = "REFERENTIEBEELDEN MAKEN";
        _jobId = await Beeldservice.StartAsync(opdracht, new Progress<string>(t => _castStatus.Text = t));
        _castStop.IsEnabled = _stop.IsEnabled = true;

        while (true)
        {
            var stand = await Beeldservice.StatusAsync(_jobId);
            if (stand.Cast.Count == _cast.Count)
            {
                for (var i = 0; i < _cast.Count; i++)
                {
                    _cast[i].Referentie = stand.Cast[i].Referentie;
                }
            }

            _castStatus.Text = _status.Text = stand.Stap;
            _castVoortgang.Value = _voortgang.Value = stand.Voortgang;
            _tijd.Text = $"Verstreken {klok.Elapsed:h\\:mm\\:ss}";
            foreach (var item in nodig)
            {
                await VernieuwCastBeeldAsync(item);
            }

            if (stand.IsKlaar)
            {
                foreach (var item in nodig)
                {
                    item.Opnieuw = false;
                    foreach (var scene in _scenes.Where(s => s.Plek == item.Naam || s.Spelers.Contains(item.Naam) || s.Voorwerpen.Contains(item.Naam)))
                    {
                        MaakOngeldig(scene, sleutelOok: true);
                    }
                }

                foreach (var kaart in _castKaarten)
                {
                    kaart.Opnieuw.IsChecked = false;
                }

                _castVoortgang.Value = 1;
                _castStatus.Text = "Referentiebeelden klaar. Controleer ze: past het niet, pas de omschrijving aan en vink 'Opnieuw maken' aan.";
                Teken();
                return true;
            }

            if (stand.IsMislukt)
            {
                _castStatus.Text = stand.Foutmelding ?? "Referentiebeelden maken mislukt.";
                return false;
            }

            await Task.Delay(Peilen);
        }
    }
}
