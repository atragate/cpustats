using CPUSTATS.Models;

namespace CPUSTATS.Services;

internal static class LinuxHidTransport
{
    private static readonly byte[] Descriptor =
        Convert.FromHexString("0600FF0901A101090215002600FF750895408106090215002600FF750895409106C0");

    public static L280DeviceInfo? FindDevice()
    {
        foreach (var path in FindInterfaces())
        {
            try
            {
                if (!MatchesDescriptor(path)) continue;
                return new L280DeviceInfo(HidTransport.VendorId, HidTransport.ProductId,
                    Path.Combine("/dev", Path.GetFileName(path)), 0xFF00, 0x0001,
                    HidTransport.ReportLength, HidTransport.ReportLength, 0);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return null;
    }

    public static async Task SendAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
    {
        var device = FindDevice() ?? throw new IOException("The L280 HID output interface could not be found.");
        await using var stream = new FileStream(device.DeviceId, FileMode.Open, FileAccess.Write,
            FileShare.ReadWrite, 1, FileOptions.Asynchronous);
        await stream.WriteAsync(report, cancellationToken);
    }

    private static IEnumerable<string> FindInterfaces()
    {
        if (!Directory.Exists("/sys/class/hidraw")) return [];
        try { return Directory.GetDirectories("/sys/class/hidraw", "hidraw*"); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static bool MatchesDescriptor(string path)
    {
        var device = Path.Combine(path, "device");
        if (!File.ReadLines(Path.Combine(device, "uevent"))
            .Any(line => line.Equals("HID_ID=0003:00005131:00002007", StringComparison.OrdinalIgnoreCase)))
            return false;

        using var stream = File.OpenRead(Path.Combine(device, "report_descriptor"));
        Span<byte> bytes = stackalloc byte[Descriptor.Length + 1];
        var length = stream.ReadAtLeast(bytes, bytes.Length, false);
        return length == Descriptor.Length && bytes[..length].SequenceEqual(Descriptor);
    }
}
