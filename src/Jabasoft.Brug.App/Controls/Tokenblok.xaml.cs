using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Jabasoft.Brug.App.Controls;

/// <summary>
/// Tokenverbruik: totaal en de drie zwaarste modellen, elke 5 seconden
/// opgehaald bij de broker. Draait de broker niet, dan blijven de laatste
/// cijfers staan (of een streepje als er nog niets was).
/// </summary>
public sealed partial class Tokenblok : UserControl
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("http://localhost:5310"), Timeout = TimeSpan.FromSeconds(4) };

    private readonly DispatcherQueueTimer _timer;
    private bool _bezig;

    public Tokenblok()
    {
        InitializeComponent();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.Tick += async (_, _) => await VerversAsync();

        // Alleen ophalen zolang het blok in beeld is.
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await VerversAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task VerversAsync()
    {
        if (_bezig)
        {
            return;
        }

        _bezig = true;
        try
        {
            var modellen = await Http.GetFromJsonAsync<List<Modelverbruik>>("/api/usage/models", Json);
            if (modellen is not null)
            {
                Toon(modellen);
            }
        }
        catch (Exception)
        {
            // Broker even niet bereikbaar: laatste cijfers laten staan.
        }
        finally
        {
            _bezig = false;
        }
    }

    private void Toon(List<Modelverbruik> modellen)
    {
        var cultuur = CultureInfo.GetCultureInfo("nl-NL");
        Totaal.Text = modellen.Sum(m => m.TotalTokens).ToString("N0", cultuur);

        // De broker levert ze al gesorteerd, zwaarste eerst.
        Zet(0, Model1, Waarde1);
        Zet(1, Model2, Waarde2);
        Zet(2, Model3, Waarde3);

        void Zet(int plaats, TextBlock naam, TextBlock waarde)
        {
            var model = plaats < modellen.Count ? modellen[plaats] : null;
            naam.Text = model?.Model ?? string.Empty;
            waarde.Text = model?.TotalTokens.ToString("N0", cultuur) ?? string.Empty;
        }
    }

    /// <summary>Zoals de broker een regel van /api/usage/models teruggeeft.</summary>
    private sealed record Modelverbruik(string Model, long TotalTokens, int Calls);
}
