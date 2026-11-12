# CPUSTATS: developer notes

CPUSTATS is an unofficial Avalonia desktop app for the built-in display on the darkFlash L280 ATX PC Case. See the [project README](../README.md) for compatibility and setup.
The same C# project targets Windows and Linux, with platform-specific sensor and HID access.

## Build and run

Install the .NET 8 SDK, then from the repository root:

```sh
dotnet restore CPUSTATS.sln
dotnet build CPUSTATS.sln -c Release
dotnet test CPUSTATS.sln -c Release
dotnet run --project CPUSTATS/CPUSTATS.csproj -c Release
```

Publish self-contained builds for either platform from the same project:

```sh
dotnet publish CPUSTATS/CPUSTATS.csproj -c Release -r linux-x64 --self-contained true
dotnet publish CPUSTATS/CPUSTATS.csproj -c Release -r win-x64 --self-contained true
```

### Build release installers

From the repository root, publish a self-contained executable for the target platform. Use the
same version for the executable and installer. On Windows, set `ElevatedLaunch=true` to embed
the installed application's UAC manifest.

```powershell
dotnet publish CPUSTATS/CPUSTATS.csproj -c Release -r win-x64 --self-contained true -p:Version=1.1.0 -p:ElevatedLaunch=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o work/installer-win
dotnet tool install wix --version 4.0.6 --tool-path work/wix
$env:DOTNET_ROLL_FORWARD = 'Major'
.\work\wix\wix.exe extension add WixToolset.UI.wixext/4.0.6 WixToolset.Util.wixext/4.0.6
.\work\wix\wix.exe build -arch x64 -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -d Version=1.1.0 -d "SourceRoot=$PWD" -d "PublishDir=$PWD/work/installer-win" -o work/CPUSTATS-v1.1.0-windows-x64.msi packaging/windows/CPUSTATS.wxs
```

```sh
dotnet publish CPUSTATS/CPUSTATS.csproj -c Release -r linux-x64 --self-contained true -p:Version=1.1.0 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o work/installer-linux
sh packaging/linux/build-deb.sh 1.1.0 work/installer-linux/CPUSTATS work/cpustats_1.1.0_amd64.deb
```

### Linux telemetry and device access

On Linux, the L280 HID device must be readable and writable by the logged-in user.
For systemd distributions, a udev rule can grant access after reconnecting the device:

```udev
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="5131", ATTRS{idProduct}=="2007", TAG+="uaccess"
```

CPU usage and RAM come from procfs on Linux. CPU temperature is read from supported hwmon drivers.
GPU usage uses the driver's utilization reading when available. Intel i915 integrated graphics use
GT idle-residency deltas for utilization, including Arrow Lake-S. GPU temperature uses the
graphics driver's hwmon sensor, with an optional [Intel Arrow Lake-S temperature bridge](../linux/README.md)
when i915 does not expose that sensor.
Windows uses LibreHardwareMonitor and can fill missing readings from a fresh Loving Cool telemetry
feed. Missing readings are shown as dashes on the panel and as unavailable in the app.

## Windows temperatures

Install [PawnIO](https://github.com/namazso/PawnIO.Setup/releases) and run CPUSTATS as administrator
to let LibreHardwareMonitor read CPU package temperature. Close any running copy of CPUSTATS
before restarting it with **Run as administrator**.

If the Intel driver does not expose GPU temperature on Arrow Lake-S (Intel PCI host bridge
`8086:7d1b` and iGPU `8086:7d67`), Windows can read the same GT thermal sensor as the Linux bridge.
From PowerShell in the repository root, download the optional signed PawnIO module before building
or publishing the Windows app:

```powershell
.\CPUSTATS\Windows\Get-IntelMchbar.ps1
dotnet build CPUSTATS.sln -c Release
```

The script checks the [PawnIO Modules release](https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.11)
and module checksums. It does not install a driver. The build copies `IntelMCHBAR.bin` beside the
Windows executable. CPUSTATS verifies the module again and reads only the temperature target
and GT thermal status registers. Other GPUs still use their existing sensor backends.

## Panel behavior

The app sends a 65-byte L280 report every 200 ms and samples sensors once per second.
The default mode alternates Celsius and usage every six seconds. Celsius, Fahrenheit, and usage
can also be selected as fixed display modes. The status log is written to `CPUSTATS/last-status.json`
under the current user's local application data directory.

See [protocol notes](../docs/L280-PROTOCOL.md) for the segment layout and reference test vectors.
