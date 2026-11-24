using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Jabasoft.Brug.App.Diensten;

/// <summary>Een videokaart zoals nvidia-smi hem meldt.</summary>
public sealed record Videokaart(string Naam, long VramGebruiktMiB, long VramTotaalMiB, int BelastingProcent, double TemperatuurC);

/// <summary>Een meting van de pc.</summary>
public sealed record Systeemmeting(double CpuProcent, long RamGebruikt, long RamTotaal, IReadOnlyList<Videokaart> Kaarten)
{
    public Videokaart? EersteKaart => Kaarten.Count > 0 ? Kaarten[0] : null;
}

/// <summary>
/// Meet elke paar seconden CPU, RAM en per videokaart VRAM, belasting en
/// temperatuur. Een meter voor de hele Brug: het systeemblok in de header
/// en de pillen in de footer lezen allebei <see cref="Gemeten"/>.
///
/// Zelfde bronnen als WindowsSystemStatsService in Jabasoft.Base: CPU en RAM
/// via kernel32, de videokaarten via nvidia-smi (zit bij de NVIDIA-driver).
/// Alle kaarten worden gemeld, zodat de RTX 3060 er straks vanzelf bij komt.
/// </summary>
public sealed class Systeemmeter : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private (ulong Idle, ulong Kernel, ulong User)? _vorigeCpu;
    private Task? _lus;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    public Systeemmeting? Laatste { get; private set; }

    /// <summary>Nieuwe meting. Komt op een achtergronddraad.</summary>
    public event EventHandler<Systeemmeting>? Gemeten;

    public void Start() => _lus ??= Task.Run(() => LusAsync(_stop.Token));

    private async Task LusAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var (ramGebruikt, ramTotaal) = Ram();
                var meting = new Systeemmeting(Cpu(), ramGebruikt, ramTotaal, await KaartenAsync(stop).ConfigureAwait(false));
                Laatste = meting;
                Gemeten?.Invoke(this, meting);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Een meting die mislukt slaan we over; de volgende komt zo.
            }

            try
            {
                await Task.Delay(Interval, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private double Cpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var nu = (Getal(idle), Getal(kernel), Getal(user));
        var vorige = _vorigeCpu;
        _vorigeCpu = nu;
        if (vorige is null)
        {
            return 0;
        }

        var stil = nu.Item1 - vorige.Value.Idle;
        // "kernel" bevat de stille tijd al.
        var totaal = (nu.Item2 - vorige.Value.Kernel) + (nu.Item3 - vorige.Value.User);
        return totaal == 0 ? 0 : Math.Clamp((double)(totaal - stil) / totaal * 100, 0, 100);
    }

    private static (long Gebruikt, long Totaal) Ram()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return (0, 0);
        }

        return ((long)(status.ullTotalPhys - status.ullAvailPhys), (long)status.ullTotalPhys);
    }

    private static async Task<IReadOnlyList<Videokaart>> KaartenAsync(CancellationToken stop)
    {
        try
        {
            var info = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=name,memory.used,memory.total,utilization.gpu,temperature.gpu --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proces = Process.Start(info);
            if (proces is null)
            {
                return [];
            }

            using var limiet = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limiet.CancelAfter(TimeSpan.FromSeconds(3));
            var uitvoer = await proces.StandardOutput.ReadToEndAsync(limiet.Token).ConfigureAwait(false);
            await proces.WaitForExitAsync(limiet.Token).ConfigureAwait(false);
            if (proces.ExitCode != 0)
            {
                return [];
            }

            var kaarten = new List<Videokaart>();
            foreach (var regel in uitvoer.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var d = regel.Split(',', StringSplitOptions.TrimEntries);
                if (d.Length == 5
                    && long.TryParse(d[1], out var gebruikt)
                    && long.TryParse(d[2], out var totaal)
                    && int.TryParse(d[3], out var belasting)
                    && double.TryParse(d[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                {
                    kaarten.Add(new Videokaart(d[0], gebruikt, totaal, belasting, temp));
                }
            }

            return kaarten;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or IOException)
        {
            // Geen NVIDIA-kaart of nvidia-smi hing.
            return [];
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private static ulong Getal(System.Runtime.InteropServices.ComTypes.FILETIME t) => ((ulong)(uint)t.dwHighDateTime << 32) | (uint)t.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idle, out System.Runtime.InteropServices.ComTypes.FILETIME kernel, out System.Runtime.InteropServices.ComTypes.FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}

/// <summary>Getallen zoals de Brug ze toont (Nederlands: komma, punt voor duizendtallen).</summary>
public static class Weergave
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static string Gb(long bytes) => (bytes / 1024d / 1024 / 1024).ToString("0.0", Nl);

    public static string GbUitMiB(long mib) => (mib / 1024d).ToString("0.0", Nl);

    public static string Procent(double waarde) => $"{Math.Round(waarde):0} %";
}
