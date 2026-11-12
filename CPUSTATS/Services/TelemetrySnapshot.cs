namespace CPUSTATS.Services;

public sealed record TelemetrySnapshot(int? CpuTemperature, int? CpuUsage, int? GpuTemperature, int? GpuUsage, int? RamUsage)
{
    public DateTimeOffset SampledAt { get; init; } = DateTimeOffset.Now;
    public string Source { get; init; } = "Hardware sensors";
    public string? Warning { get; init; }
    public string? CpuName { get; init; }
    public string? GpuName { get; init; }
}
