using System.Text.Json;

namespace CPUSTATS.Services;

public static class LinuxIntelGpuTemperature
{
    public const string SamplePath = "/run/l280studio/gpu-temperature.json";

    public static int? Read(string device)
    {
        try
        {
            var pciAddress = new DirectoryInfo(device).ResolveLinkTarget(true)?.Name;
            if (pciAddress is null) return null;
            return Parse(File.ReadAllText(SamplePath), pciAddress,
                File.ReadAllText(Path.Combine(device, "vendor")).Trim(),
                File.ReadAllText(Path.Combine(device, "device")).Trim(), DateTimeOffset.UtcNow);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static int? Parse(string json, string pciAddress, string vendorId, string deviceId, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1
                || root.GetProperty("pciAddress").GetString() != pciAddress
                || vendorId != "0x8086" || deviceId != "0x7d67"
                || root.GetProperty("vendorId").GetString() != vendorId
                || root.GetProperty("deviceId").GetString() != deviceId) return null;
            var sampledAt = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("sampledAtUnixMs").GetInt64());
            var age = now - sampledAt;
            if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(5)) return null;
            return root.GetProperty("celsius").TryGetInt32(out var value) && value is >= 0 and <= 125 ? value : null;
        }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (FormatException) { return null; }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
