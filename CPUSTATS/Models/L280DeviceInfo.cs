namespace CPUSTATS.Models;

public sealed record L280DeviceInfo(
    string VendorId,
    string ProductId,
    string DeviceId,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportLength,
    ushort OutputReportLength,
    ushort FeatureReportLength)
{
    public string VidPid => $"{VendorId}:{ProductId}";
}
