using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace CPUSTATS.Services;

public static class LovingCoolFeed
{
    private const int FileSize = 65536;
    private const int HeaderSize = sizeof(int);

    public static TelemetrySnapshot? ReadFresh()
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "LOVINGCOOL_MONITOR_INTERNAL.bin");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length != FileSize)
                return null;

            var first = new byte[FileSize];
            var second = new byte[FileSize];
            stream.ReadExactly(first);
            stream.Position = 0;
            stream.ReadExactly(second);
            return first.AsSpan().SequenceEqual(second) ? Parse(first, DateTimeOffset.Now) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static TelemetrySnapshot? Parse(byte[] bytes, DateTimeOffset now)
    {
        if (bytes.Length < HeaderSize)
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (length <= 0 || length > bytes.Length - HeaderSize || length > FileSize - HeaderSize)
            return null;

        try
        {
            using var document = JsonDocument.Parse(bytes.AsMemory(HeaderSize, length));
            var root = document.RootElement;
            if (!root.TryGetProperty("systemTime", out var time) ||
                !DateTimeOffset.TryParseExact(time.GetString(), "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var sampledAt))
                return null;

            if (now - sampledAt > TimeSpan.FromSeconds(5) || sampledAt - now > TimeSpan.FromSeconds(2))
                return null;

            return new TelemetrySnapshot(
                Find(root, "cpuLayout", "temperature_list", "CPU Package", "Core Max", ": DTS :"),
                Find(root, "cpuLayout", "utilization_list", "Total CPU Usage"),
                Find(root, "graphics", "temperature_list", "GPU Temperature", "GPU Core", "Graphics"),
                Find(root, "graphics", "utilization_list", "GPU Core Load", "GPU D3D Usage"),
                null)
            {
                SampledAt = sampledAt,
                Source = "Loving Cool Monitor"
            };
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static int? Find(JsonElement root, string group, string list, params string[] names)
    {
        if (!root.TryGetProperty(group, out var section) ||
            !section.TryGetProperty(list, out var sensors) || sensors.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var name in names)
        {
            foreach (var sensor in sensors.EnumerateArray())
            {
                if (sensor.TryGetProperty("sensorname", out var label) &&
                    label.GetString()?.Contains(name, StringComparison.OrdinalIgnoreCase) == true &&
                    sensor.TryGetProperty("value", out var reading) &&
                    reading.TryGetDouble(out var value) && double.IsFinite(value) && value is >= 0 and <= 150)
                    return (int)Math.Floor(value);
            }
        }

        return null;
    }
}
