using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;

namespace CPUSTATS.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsTelemetry(bool allowVendorFeed = true) : IDisposable
{
    private readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    private WindowsIntelGpuTemperature? _intelGpuTemperature;
    private bool _triedIntelGpuTemperature;
    private bool _opened;
    private long _idle, _kernel, _user;

    public TelemetrySnapshot Read()
    {
        var warnings = new List<string>();
        var freshVendor = allowVendorFeed ? LovingCoolFeed.ReadFresh() : null;
        int? cpuTemp = null, gpuTemp = null, gpuUsage = null;
        string? cpuName = null, gpuName = null;
        var isArrowLakeGpu = false;
        var fromIntelRegisters = false;
        try
        {
            if (!_opened) { _computer.Open(); _opened = true; }
            foreach (var hardware in _computer.Hardware) Update(hardware);
            var hardwareList = _computer.Hardware.SelectMany(Flatten).ToArray();
            var processors = hardwareList.Where(h => h.HardwareType == HardwareType.Cpu).ToArray();
            var gpu = hardwareList.Where(h => h.HardwareType is HardwareType.GpuIntel or HardwareType.GpuNvidia or HardwareType.GpuAmd)
                .OrderBy(h => h.HardwareType == HardwareType.GpuIntel).FirstOrDefault();
            cpuName = string.Join(" / ", processors.Select(h => h.Name.Trim()).Where(name => name.Length > 0).Distinct());
            gpuName = gpu?.Name.Trim();
            var cpu = processors.SelectMany(h => h.Sensors);
            cpuTemp = Pick(cpu, SensorType.Temperature, "CPU Package", "Core Max", "Core Average", "CPU (Tctl/Tdie)");
            if (gpu is not null)
            {
                gpuTemp = Pick(gpu.Sensors, SensorType.Temperature, "GPU Core", "GPU Temperature");
                gpuUsage = Pick(gpu.Sensors, SensorType.Load, "GPU Core", "D3D 3D", "GPU Total");
                isArrowLakeGpu = gpu.HardwareType == HardwareType.GpuIntel && gpu is GenericGpu intel
                    && intel.DeviceId.Contains("VEN_8086&DEV_7D67&", StringComparison.OrdinalIgnoreCase);
                if (isArrowLakeGpu && gpuTemp is null)
                {
                    if (!_triedIntelGpuTemperature)
                    {
                        _triedIntelGpuTemperature = true;
                        _intelGpuTemperature = WindowsIntelGpuTemperature.TryOpen();
                    }
                    gpuTemp = _intelGpuTemperature?.Read();
                    fromIntelRegisters = gpuTemp.HasValue;
                }
            }
        }
        catch (Exception ex) { warnings.Add($"Sensor access: {ex.Message}"); }

        var fromVendor = false;
        if (cpuTemp is null && freshVendor?.CpuTemperature is not null) { cpuTemp = freshVendor.CpuTemperature; fromVendor = true; }
        if (gpuTemp is null && freshVendor?.GpuTemperature is not null) { gpuTemp = freshVendor.GpuTemperature; fromVendor = true; }
        gpuUsage ??= freshVendor?.GpuUsage;
        if (cpuTemp is null || (gpuTemp is null && isArrowLakeGpu))
        {
            using var identity = WindowsIdentity.GetCurrent();
            var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            if (!elevated) warnings.Add("For hardware temperatures, restart CPUSTATS as administrator.");
            else if (cpuTemp is null && !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO", "PawnIOLib.dll")))
                warnings.Add("The Windows sensor driver is missing. Run CPUSTATS Setup and choose Repair.");
            else if (cpuTemp is null) warnings.Add("CPU temperature is unavailable from the current sensor backend.");
            if (gpuTemp is null && isArrowLakeGpu && !File.Exists(Path.Combine(AppContext.BaseDirectory, "IntelMCHBAR.bin")))
                warnings.Add("Intel GPU temperature needs the optional IntelMCHBAR module.");
        }
        if (gpuTemp is null && (!isArrowLakeGpu || _intelGpuTemperature is not null))
            warnings.Add("GPU temperature unavailable from the current sensor backend.");
        else if (gpuTemp is null && File.Exists(Path.Combine(AppContext.BaseDirectory, "IntelMCHBAR.bin")))
            warnings.Add("The Intel GPU temperature module could not be opened.");
        return new TelemetrySnapshot(cpuTemp, ReadCpuUsage(), gpuTemp, gpuUsage, ReadRamUsage())
        {
            CpuName = cpuName,
            GpuName = gpuName,
            Source = fromIntelRegisters ? "Hardware sensors + Intel GT (PawnIO)" :
                fromVendor ? "Hardware sensors + fresh Loving Cool feed" : "Hardware sensors + Windows",
            Warning = warnings.Count == 0 ? null : string.Join(" ", warnings)
        };
    }

    private static int? Pick(IEnumerable<ISensor> sensors, SensorType type, params string[] names)
    {
        var available = sensors.Where(s => s.SensorType == type && s.Value is float v && float.IsFinite(v)
            && v >= 0 && v <= (type == SensorType.Load ? 100 : 150)
            && !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var name in names)
        {
            var preferred = available.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (preferred?.Value is float value) return (int)Math.Floor(value);
        }
        return type == SensorType.Temperature && available.Length == 1 ? (int?)available[0].Value : null;
    }

    private int? ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var i = idle.ToLong(); var k = kernel.ToLong(); var u = user.ToLong();
        var total = k - _kernel + u - _user;
        int? usage = _kernel == 0 || total <= 0 ? null : 100 - (int)Math.Clamp((i - _idle) * 100 / total, 0, 100);
        _idle = i; _kernel = k; _user = u;
        return usage;
    }

    private static int? ReadRamUsage()
    {
        var memory = new MemoryStatus();
        return GlobalMemoryStatusEx(memory) ? (int)memory.MemoryLoad : null;
    }

    private static IEnumerable<IHardware> Flatten(IHardware h) => new[] { h }.Concat(h.SubHardware.SelectMany(Flatten));
    private static void Update(IHardware hardware)
    {
        hardware.Update();
        foreach (var child in hardware.SubHardware) Update(child);
    }

    public void Dispose()
    {
        _intelGpuTemperature?.Dispose();
        if (_opened) _computer.Close();
        _opened = false;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool GlobalMemoryStatusEx(MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low; public uint High; public long ToLong() => ((long)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private sealed class MemoryStatus
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatus>();
        public uint MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
}
