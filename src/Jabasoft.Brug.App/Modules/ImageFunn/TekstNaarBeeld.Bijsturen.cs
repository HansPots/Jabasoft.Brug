using Jabasoft.Brug.App.Diensten;
using Jabasoft.Brug.App.Paginas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>
/// Het tweede deel van Tekst naar beeld: het getoonde beeld bijsturen in plaats van opnieuw beginnen.
///
/// Zonder gebied is het beeld naar beeld: het hele beeld wordt deels opnieuw opgebouwd, zoveel als de schuif
/// toelaat (laag = de indeling blijft bijna gelijk). Met een aangetekend gebied is het inpainting: alleen dat
/// gebied wordt opnieuw gemaakt. Elke aanwijzing blijft in een lijst staan (aan/uit, weg te halen) en elke ronde
/// bouwt voort op de oorspronkelijke prompt plus alle aanwijzingen die aanstaan. Alle versies blijven bewaard
/// (<see cref="BeeldSessies"/>), zodat je terug kunt naar een eerdere stand. Overgenomen uit de oude app.
/// </summary>
public sealed partial class TekstNaarBeeld
{
    private readonly TextBox _wijziging = new()
    {
        Header = "Bijsturen - wat moet er anders aan dit beeld?",
        PlaceholderText = "Bijvoorbeeld: de persoon iets groter, of een rode fiets tegen een boom op de achtergrond",
    };

    private readonly Slider _sterkte = new() { Minimum = 10, Maximum = 95, Value = 40, StepFrequency = 5, Header = "Hoeveel mag er veranderen: 40%" };
    private readonly CheckBox _gebied = new() { Content = "Gebied aanwijzen" };
    private readonly Slider _penseel = new() { Minimum = 10, Maximum = 160, Value = 60, Width = 160, Header = "Penseel" };
    private readonly CheckBox _gum = new() { Content = "Gum (tik een streep weg)" };
    private readonly StackPanel _penseelRij = new() { Orientation = Orientation.Horizontal, Spacing = 16, Visibility = Visibility.Collapsed };
    private readonly StackPanel _aanwijzingen = new() { Spacing = 4 };
    private readonly StackPanel _versies = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private Button _pasToe = null!;
    private Border _bijstuurpaneel = null!;

    private BeeldSessie? _sessie;
    private BeeldVersie? _versie;

    private Border MaakBijstuurpaneel()
    {
        _pasToe = Bouw.Knop("Pas toe", async (_, _) => await BijsturenAsync());
        ToolTipService.SetToolTip(_sterkte, "Laag: de indeling blijft bijna gelijk. Hoog: meer vrijheid, en nodig om iets toe te voegen.");
        ToolTipService.SetToolTip(_gebied, "Teken op het beeld waar iets moet veranderen. Alleen dat gebied wordt opnieuw gemaakt, de rest blijft zoals het is.");
        _sterkte.ValueChanged += (_, e) => _sterkte.Header = $"Hoeveel mag er veranderen: {e.NewValue:0}%";
        _penseel.ValueChanged += (_, e) => _tekenlaag.Penseel = e.NewValue;
        _gum.Checked += (_, _) => _tekenlaag.Gum = true;
        _gum.Unchecked += (_, _) => _tekenlaag.Gum = false;
        _gebied.Checked += (_, _) => ZetGebied(true);
        _gebied.Unchecked += (_, _) => ZetGebied(false);

        _penseelRij.Children.Add(_penseel);
        _penseelRij.Children.Add(_gum);
        _penseelRij.Children.Add(Bouw.Knop("Wis gebied", (_, _) => _tekenlaag.Wis(), "TextMutedBrush"));
        foreach (var deel in _penseelRij.Children.OfType<FrameworkElement>())
        {
            deel.VerticalAlignment = VerticalAlignment.Bottom;
        }

        var inhoud = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                _wijziging,
                _sterkte,
                _gebied,
                _penseelRij,
                _pasToe,
                Bouw.Kop("AANWIJZINGEN"),
                _aanwijzingen,
                Bouw.Kop("VERSIES"),
                _versies,
            },
        };

        _bijstuurpaneel = new Border { Visibility = Visibility.Collapsed, Child = Bouw.Paneel("Bijsturen", inhoud) };
        return _bijstuurpaneel;
    }

    private void ZetGebied(bool aan)
    {
        _tekenlaag.Visibility = aan ? Visibility.Visible : Visibility.Collapsed;
        _penseelRij.Visibility = aan ? Visibility.Visible : Visibility.Collapsed;
        _tekenlaag.Penseel = _penseel.Value;
        if (aan)
        {
            // Iets toevoegen vraagt veel vrijheid in het gebied: meteen een hogere stand.
            if (_sterkte.Value < 75)
            {
                _sterkte.Value = 85;
            }

            _status.Text = "Teken over het gebied dat moet veranderen, typ wat daar moet komen en druk op Pas toe.";
        }
    }

    // ---------------------------------------------------------------- sessie: aanwijzingen en versies

    /// <summary>Zoekt de bewaarde sessie bij dit beeld, of begint een nieuwe, en toont de aanwijzingen en versies.</summary>
    private void ZetSessie(string bestand, string prompt)
    {
        _sessie = BeeldSessies.Zoek(bestand) ?? BeeldSessies.Nieuw(bestand, prompt);
        _versie = _sessie.Versies.First(v => string.Equals(v.Bestand, bestand, StringComparison.OrdinalIgnoreCase));
        _bijstuurpaneel.Visibility = Visibility.Visible;
        BouwSessie();
    }

    private void WisSessie()
    {
        _sessie = null;
        _versie = null;
        _bijstuurpaneel.Visibility = Visibility.Collapsed;
        BouwSessie();
    }

    private void BouwSessie()
    {
        _aanwijzingen.Children.Clear();
        var aanwijzingen = _versie?.Aanwijzingen ?? [];
        if (aanwijzingen.Count == 0)
        {
            _aanwijzingen.Children.Add(Bouw.Melding("Nog geen. Elke aanwijzing die je geeft, blijft hier staan en telt mee zolang hij aan staat."));
        }

        foreach (var a in aanwijzingen)
        {
            var vak = new CheckBox { Content = a.Gebied ? $"{a.Tekst}  (gebied)" : a.Tekst, IsChecked = a.Aan };
            vak.Checked += (_, _) => { a.Aan = true; BeeldSessies.Bewaar(); };
            vak.Unchecked += (_, _) => { a.Aan = false; BeeldSessies.Bewaar(); };
            var weg = Bouw.Knop("✕", (_, _) =>
            {
                _versie!.Aanwijzingen.Remove(a);
                BeeldSessies.Bewaar();
                BouwSessie();
            }, "TextMutedBrush");
            ToolTipService.SetToolTip(weg, "Deze aanwijzing weghalen");
            _aanwijzingen.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { vak, weg } });
        }

        _versies.Children.Clear();
        var versies = _sessie?.Versies ?? [];
        for (var i = 0; i < versies.Count; i++)
        {
            var versie = versies[i];
            var huidig = ReferenceEquals(versie, _versie);
            var knop = Bouw.Knop(i == 0 ? "Origineel" : $"Versie {i + 1}", async (_, _) =>
            {
                if (File.Exists(versie.Bestand))
                {
                    await ToonAsync(versie.Bestand, versie.Prompt);
                    _status.Text = "Je zit nu op een eerdere versie. Wat je nu bijstuurt, bouwt hierop verder.";
                }
            }, huidig ? "AccentBrush" : "AccentBrushLight");
            knop.IsEnabled = !huidig;
            _versies.Children.Add(knop);
        }
    }

    /// <summary>De wijzigingen voor de prompt: alle aanwijzingen die aanstaan, plus wat je nu typt.</summary>
    private string WijzigingenTekst(string nieuw)
    {
        var regels = (_versie?.Aanwijzingen ?? []).Where(a => a.Aan).Select(a => a.Tekst).ToList();
        regels.Add(nieuw);
        return string.Join("\n", regels.Select(r => "- " + r));
    }

    private void VoegVersieToe(string bestand, string prompt, string nieuweAanwijzing, bool gebied)
    {
        if (_sessie is null || _versie is null)
        {
            return;
        }

        var aanwijzingen = _versie.Aanwijzingen.Select(a => new Aanwijzing { Tekst = a.Tekst, Aan = a.Aan, Gebied = a.Gebied }).ToList();
        aanwijzingen.Add(new Aanwijzing { Tekst = nieuweAanwijzing, Aan = true, Gebied = gebied });
        _sessie.Versies.Add(new BeeldVersie { Bestand = bestand, Prompt = prompt, Aanwijzingen = aanwijzingen });
        BeeldSessies.Bewaar();
    }

    // ---------------------------------------------------------------- bijsturen

    private async Task BijsturenAsync()
    {
        if (_bestand is null)
        {
            return;
        }

        var wijziging = _wijziging.Text.Trim();
        if (wijziging.Length == 0)
        {
            _status.Text = "Typ eerst wat er anders moet.";
            return;
        }

        var gebied = _gebied.IsChecked == true;
        if (gebied && !_tekenlaag.HeeftGebied)
        {
            _status.Text = "Teken eerst het gebied op het beeld, of zet 'Gebied aanwijzen' uit om het hele beeld bij te sturen.";
            return;
        }

        if (!LeesSeed(out var seed))
        {
            return;
        }

        ZetBezig(true);
        string? masker = null;
        try
        {
            // De prompt van het beeld bijwerken met de wijzigingen; het beeldmodel leest alleen Engels.
            _status.Text = "Wijziging verwerken in de prompt…";
            var basis = _sessie?.BasisPrompt ?? _prompt ?? _omschrijving.Text.Trim();
            var (nieuw, fout) = await Promptschrijver.VerbeterAsync($"ORIGINAL PROMPT: {basis}\nCHANGE REQUESTS:\n{WijzigingenTekst(wijziging)}", Promptsoort.Bewerk, Onderdelen.Gekozen(_model));
            var prompt = nieuw ?? $"{basis}, {wijziging}";
            var opmerking = nieuw is null ? $"Prompt bijwerken mislukt ({fout}), de wijziging is achter de prompt gezet. " : null;

            if (gebied && _beeld.Source is BitmapImage bron)
            {
                masker = Gegevensmap.Bestand(Path.Combine("ImageFunn", "tijdelijk"), $"masker-{Guid.NewGuid():N}.png");
                await _tekenlaag.MaakMaskerAsync(bron.PixelWidth, bron.PixelHeight, masker);
            }

            var bronBestand = _bestand;
            var stijl = Beeldstijl.Van(Onderdelen.Gekozen(_stijl));
            await VoerUitAsync(new BeeldOpdracht
            {
                Beeldmodel = Onderdelen.Gekozen(_model),
                Prompt = prompt,
                Negatief = stijl.Negatief,
                BronBeeld = bronBestand,
                Masker = masker,
                Sterkte = _sterkte.Value / 100,
                Seed = seed,
            }, opmerking, async stand =>
            {
                VoegVersieToe(stand.Bestand!, prompt, wijziging, gebied);
                await ToonAsync(stand.Bestand!, prompt, stand);
                _wijziging.Text = "";
                _status.Text = $"{opmerking}Bijgestuurd - seed {stand.Seed}. Het origineel staat nog in de galerij.";
            });
        }
        finally
        {
            ZetBezig(false);
            if (masker is not null)
            {
                try
                {
                    File.Delete(masker);
                }
                catch (IOException)
                {
                    // Een tijdelijk bestand dat blijft staan is geen ramp.
                }
            }
        }
    }
}
