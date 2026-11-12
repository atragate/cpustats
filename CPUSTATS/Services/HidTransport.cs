using CPUSTATS.Models;

namespace CPUSTATS.Services;

public sealed class HidTransport
{
    public const string VendorId = "5131";
    public const string ProductId = "2007";
    public const ushort ReportLength = L280Protocol.ReportLength;

    public static bool IsValidReport(ReadOnlySpan<byte> report) => TelemetryReport.IsValid(report);

    public static byte[] BuildTelemetryReport(TelemetrySnapshot values, DisplayPage page = DisplayPage.Celsius, byte sequence = 0) =>
        TelemetryReport.Encode(values, page, sequence);

    public L280DeviceInfo? FindDevice()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows()) return WindowsHidTransport.FindDevice();
#endif
        return LinuxHidTransport.FindDevice();
    }

    public Task SendAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken = default)
    {
        if (!IsValidReport(report.Span))
            throw new ArgumentException($"L280 reports must be exactly {ReportLength} bytes.", nameof(report));

#if WINDOWS
        if (OperatingSystem.IsWindows())
            return WindowsHidTransport.SendAsync(report, cancellationToken);
#endif
        return LinuxHidTransport.SendAsync(report, cancellationToken);
    }
}
