using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CPUSTATS.Services;

public sealed class TelemetryService : IDisposable
{
#if WINDOWS
    private WindowsTelemetry? _windows;
#endif
    private long? _previousLinuxTotal, _previousLinuxIdle;
    private long? _previousGpuIdleMilliseconds;
    private long _previousGpuSampleTimestamp;
    private string? _linuxCpuName, _linuxGpuDevice, _linuxGpuName;

    public TelemetrySnapshot Read()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
            return WindowsSensorService.ReadLatest() ?? (_windows ??= new()).Read();
#endif
        var gpuCard = LinuxDrmCards().FirstOrDefault();
        var gpuDevice = gpuCard is null ? null : Path.Combine(gpuCard, "device");
        if (gpuDevice != _linuxGpuDevice)
        {
            _linuxGpuDevice = gpuDevice;
            _linuxGpuName = gpuDevice is null ? null : ReadLinuxGpuName(gpuDevice);
        }
        var gpuTemperature = gpuDevice is null ? null : LinuxDirectories(Path.Combine(gpuDevice, "hwmon"))
            .Select(path => LinuxTemperature(Path.Combine(path, "temp1_input"))).FirstOrDefault(value => value.HasValue);
        if (gpuTemperature is null && gpuDevice is not null)
            gpuTemperature = LinuxIntelGpuTemperature.Read(gpuDevice);
        var gpuUsage = gpuCard is null ? null : ReadLinuxGpuUsage(gpuCard, gpuDevice!);
        return new TelemetrySnapshot(ReadLinuxCpuTemperature(), ReadLinuxCpuUsage(), gpuTemperature,
            gpuUsage, ReadLinuxRamUsage())
        {
            CpuName = _linuxCpuName ??= ReadLinuxCpuName(),
            GpuName = _linuxGpuName,
            Source = gpuCard is null ? "Linux procfs and sysfs" : "Linux procfs and GPU driver telemetry",
            Warning = gpuTemperature is null ? "GPU temperature unavailable from the current sensor backend." : null
        };
    }

    private static string ReadLinuxCpuName()
    {
        foreach (var line in (LinuxReadText("/proc/cpuinfo") ?? "").Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator >= 0 && line[..separator].Trim() == "model name")
                return line[(separator + 1)..].Trim();
        }
        return "";
    }

    private static string? ReadLinuxGpuName(string device)
    {
        var vendor = LinuxReadText(Path.Combine(device, "vendor"))?.Trim();
        var model = LinuxReadText(Path.Combine(device, "device"))?.Trim();
        var pciId = vendor is { Length: 6 } && model is { Length: 6 } ? $"PCI {vendor[2..]}:{model[2..]}" : null;
        try
        {
            var slot = new DirectoryInfo(device).ResolveLinkTarget(true)?.Name;
            if (slot is null || !Regex.IsMatch(slot, @"\A[0-9a-f]{4}:[0-9a-f]{2}:[0-9a-f]{2}\.[0-7]\z"))
                return pciId;

            var startInfo = new ProcessStartInfo("lspci")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                ArgumentList = { "-vmm", "-s", slot }
            };
            using var lookup = Process.Start(startInfo);
            if (lookup is null) return pciId;
            var output = lookup.StandardOutput.ReadToEndAsync();
            if (!lookup.WaitForExit(800))
            {
                lookup.Kill();
                return pciId;
            }

            if (lookup.ExitCode == 0)
                foreach (var line in output.GetAwaiter().GetResult().Split('\n'))
                    if (line.StartsWith("Device:\t", StringComparison.Ordinal))
                    {
                        var name = line["Device:\t".Length..].Trim();
                        if (name.Length > 0 && !name.StartsWith("Device ", StringComparison.Ordinal))
                            return name;
                    }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }
        return pciId;
    }

    private int? ReadLinuxGpuUsage(string card, string device)
    {
        var directUsage = LinuxNumber(Path.Combine(device, "gpu_busy_percent"));
        if (directUsage is >= 0 and <= 100) return (int)directUsage.Value;

        // i915 reports time spent in its GT idle state. Its delta gives the
        // integrated GPU's active share for the sampling interval.
        var idleMilliseconds = LinuxNumber(Path.Combine(card, "gt", "gt0", "rc6_residency_ms"));
        if (idleMilliseconds is null) return null;

        var sampleTimestamp = Stopwatch.GetTimestamp();
        var previousIdle = _previousGpuIdleMilliseconds;
        var previousTimestamp = _previousGpuSampleTimestamp;
        _previousGpuIdleMilliseconds = idleMilliseconds;
        _previousGpuSampleTimestamp = sampleTimestamp;
        if (previousIdle is null || sampleTimestamp <= previousTimestamp) return null;

        var intervalMilliseconds = Stopwatch.GetElapsedTime(previousTimestamp, sampleTimestamp).TotalMilliseconds;
        var idleDelta = idleMilliseconds.Value - previousIdle.Value;
        if (intervalMilliseconds <= 0 || idleDelta < 0) return null;

        var activePercent = 100 * (intervalMilliseconds - idleDelta) / intervalMilliseconds;
        return (int)Math.Clamp(Math.Round(activePercent), 0, 100);
    }

    private static IEnumerable<string> LinuxDrmCards() =>
        LinuxDirectories("/sys/class/drm", "card*")
            .Order(StringComparer.Ordinal)
            .Where(path => int.TryParse(Path.GetFileName(path).AsSpan(4), out _)
                && Directory.Exists(Path.Combine(path, "device")));

    private static int? ReadLinuxCpuTemperature()
    {
        foreach (var path in LinuxDirectories("/sys/class/hwmon"))
        {
            var driver = LinuxReadText(Path.Combine(path, "name"))?.Trim();
            if (driver is "k10temp" or "zenpower")
                return LinuxTemperature(Path.Combine(path, "temp1_input"));
            if (driver != "coretemp") continue;
            foreach (var label in Directory.EnumerateFiles(path, "temp*_label"))
                if (LinuxReadText(label)?.StartsWith("Package id ", StringComparison.Ordinal) == true)
                    return LinuxTemperature(label[..^5] + "input");
        }
        return null;
    }

    private int? ReadLinuxCpuUsage()
    {
        var fields = LinuxReadText("/proc/stat")?.Split('\n', 2)[0]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields is null || fields.Length < 9 || fields[0] != "cpu") return null;
        var counters = new long[8];
        for (var index = 0; index < counters.Length; index++)
            if (!long.TryParse(fields[index + 1], out counters[index])) return null;

        var total = counters.Sum();
        var idle = counters[3] + counters[4];
        var previousTotal = _previousLinuxTotal;
        var previousIdle = _previousLinuxIdle;
        _previousLinuxTotal = total;
        _previousLinuxIdle = idle;
        return previousTotal is null || total <= previousTotal.Value ? null
            : Percent((total - previousTotal.Value) - (idle - previousIdle!.Value), total - previousTotal.Value);
    }

    private static int? ReadLinuxRamUsage()
    {
        long? total = null, available = null;
        foreach (var line in (LinuxReadText("/proc/meminfo") ?? "").Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !long.TryParse(fields[1], out var value)) continue;
            if (fields[0] == "MemTotal:") total = value;
            if (fields[0] == "MemAvailable:") available = value;
        }
        return total > 0 && available.HasValue ? Percent(total.Value - available.Value, total.Value) : null;
    }

    private static int? Percent(long part, long total) => (int)Math.Clamp(100.0 * part / total, 0, 100);

    private static int? LinuxTemperature(string path) =>
        LinuxNumber(path) is >= 0 and <= 150000 and var value ? (int)(value / 1000) : null;

    private static long? LinuxNumber(string path) =>
        long.TryParse(LinuxReadText(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string[] LinuxDirectories(string path, string pattern = "*")
    {
        try { return Directory.GetDirectories(path, pattern); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static string? LinuxReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Dispose()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows()) _windows?.Dispose();
#endif
    }
}
