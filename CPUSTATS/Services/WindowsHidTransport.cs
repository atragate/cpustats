using System.Management;
using System.Runtime.Versioning;
using HidSharp;
using CPUSTATS.Models;

namespace CPUSTATS.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsHidTransport
{
    private const int VendorId = 0x5131;
    private const int ProductId = 0x2007;

    public static L280DeviceInfo? FindDevice()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE 'HID\\\\VID_5131&PID_2007%'");
        var device = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
        if (device is null || !string.Equals(device["Status"]?.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
            return null;

        return new L280DeviceInfo(HidTransport.VendorId, HidTransport.ProductId,
            device["DeviceID"]?.ToString() ?? string.Empty, 0xFF00, 0x0001,
            HidTransport.ReportLength, HidTransport.ReportLength, 0);
    }

    public static Task SendAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var device in DeviceList.Local.GetHidDevices(VendorId, ProductId)
            .Where(device => device.GetMaxOutputReportLength() == HidTransport.ReportLength))
        {
            try
            {
                using var stream = device.Open();
                stream.WriteTimeout = 1000;
                stream.Write(report.ToArray(), 0, report.Length);
                return Task.CompletedTask;
            }
            catch (IOException) { }
        }

        throw new IOException("The L280 HID output interface could not be opened.");
    }
}
