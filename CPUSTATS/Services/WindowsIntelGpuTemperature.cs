using System.Buffers.Binary;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CPUSTATS.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsIntelGpuTemperature : IDisposable
{
    private const string ModuleSha256 = "3F82B832D99B4AAC37D2A20FDB7C9BAA2A3BC0488612C9019C9484EB0E8A6EAE";
    private const uint LoadBinary = (41394u << 16) | (0x821u << 2);
    private const uint ExecuteFunction = (41394u << 16) | (0x841u << 2);
    private readonly SafeFileHandle _handle;

    private WindowsIntelGpuTemperature(SafeFileHandle handle) => _handle = handle;

    public static WindowsIntelGpuTemperature? TryOpen()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "IntelMCHBAR.bin");
            if (!File.Exists(path)) return null;
            var module = File.ReadAllBytes(path);
            if (Convert.ToHexString(SHA256.HashData(module)) != ModuleSha256 || !HasArrowLakeHostBridge())
                return null;

            var handle = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (!handle.IsInvalid && DeviceIoControl(handle, LoadBinary, module, module.Length, null, 0, out _, IntPtr.Zero))
                return new WindowsIntelGpuTemperature(handle);
            handle.Dispose();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ManagementException) { }
        catch (COMException) { }
        return null;
    }

    public int? Read() =>
        ReadRegister(0x599c) is uint target && ReadRegister(0x59c0) is uint status
            ? IntelGtTemperature.Decode(target, status) : null;

    private uint? ReadRegister(uint offset)
    {
        // PawnIO passes a 32-byte function name followed by 64-bit cells.
        var input = new byte[40];
        Encoding.ASCII.GetBytes("ioctl_read_dword", input);
        BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(32), offset);
        var output = new byte[8];
        return DeviceIoControl(_handle, ExecuteFunction, input, input.Length, output, output.Length,
                   out var bytesRead, IntPtr.Zero) && bytesRead == output.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(output) : null;
    }

    private static bool HasArrowLakeHostBridge()
    {
        using var search = new ManagementObjectSearcher(
            @"SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'PCI\\VEN_8086&DEV_7D1B&%' AND ConfigManagerErrorCode = 0");
        using var matches = search.Get();
        foreach (ManagementBaseObject match in matches)
        {
            match.Dispose();
            return true;
        }
        return false;
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, byte[] input, int inputSize,
        [Out] byte[]? output, int outputSize, out uint bytesRead, IntPtr overlapped);
}

internal static class IntelGtTemperature
{
    // Intel document 835538: GT thermal status reports degrees below TjMax.
    public static int? Decode(uint target, uint status)
    {
        var tjMax = (int)((target >> 16) & 0xff);
        var delta = (int)((status >> 16) & 0xff);
        if (target == uint.MaxValue || status == uint.MaxValue || (status & 0x80000000) == 0
            || tjMax is < 50 or > 125 || delta > tjMax)
            return null;
        return tjMax - delta;
    }
}
