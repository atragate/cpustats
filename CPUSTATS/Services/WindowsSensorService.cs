using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace CPUSTATS.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSensorService : ServiceBase
{
    private const string SnapshotKey = @"SOFTWARE\atragate\CPUSTATS\Sensors";
    private const string SnapshotValue = "Snapshot";
    private CancellationTokenSource? _shutdown;
    private Task? _worker;

    public WindowsSensorService() => ServiceName = "CPUSTATSSensors";

    protected override void OnStart(string[] args)
    {
        _shutdown = new CancellationTokenSource();
        var token = _shutdown.Token;
        _worker = Task.Run(() => SampleAsync(token));
    }

    protected override void OnStop()
    {
        _shutdown?.Cancel();
        _worker?.Wait(TimeSpan.FromSeconds(5));
        _shutdown?.Dispose();
    }

    private static async Task SampleAsync(CancellationToken cancellation)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(SnapshotKey, true, RegistryOptions.Volatile);
            using var telemetry = new WindowsTelemetry(allowVendorFeed: false);
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    key.SetValue(SnapshotValue, JsonSerializer.Serialize(telemetry.Read()));
                    await Task.Delay(1000, cancellation);
                }
            }
            finally { key.DeleteValue(SnapshotValue, false); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { Environment.FailFast("The CPUSTATS sensor service could not publish readings.", error); }
    }

    public static TelemetrySnapshot? ReadLatest()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SnapshotKey);
            if (key?.GetValue(SnapshotValue) is string json &&
                JsonSerializer.Deserialize<TelemetrySnapshot>(json) is { } snapshot &&
                DateTimeOffset.Now - snapshot.SampledAt < TimeSpan.FromSeconds(5))
                return snapshot;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
        catch (JsonException) { }
        return null;
    }
}
