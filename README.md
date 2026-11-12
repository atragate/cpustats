# CPUSTATS

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="CPUSTATS/Assets/Brand/cpustats-mono-white.svg">
  <img alt="CPUSTATS logo, a pulse trace inside a PC case" src="CPUSTATS/Assets/Brand/cpustats-dark.svg" width="80" height="80">
</picture>

**CPU and GPU temperature and usage monitor for the darkFlash L280 case display on Windows and Linux.**

CPUSTATS is a desktop app for Windows and Linux that drives the built-in temperature and usage display on the [darkFlash L280 ATX PC Case](https://darkflash.com/product/l280). See CPU and GPU temperatures, usage, and memory use in one window, then choose what the case display shows.

> **Unofficial project.** CPUSTATS is an independent community tool. The L280 ATX PC Case and its built-in display are darkFlash products. For the case specifications or the official Windows driver, visit the [darkFlash L280 product page](https://darkflash.com/product/l280).

[Install](#install) · [Compatibility](#compatibility) · [Build and run](#build-and-run) · [Use CPUSTATS](#use-cpustats) · [Troubleshooting](#troubleshooting)

## What it does

- **PC overview:** Detected CPU and GPU models, temperatures, usage, and system memory usage.
- **Case display:** Show Celsius, Fahrenheit, or usage. Alternate between Celsius and usage every 3, 6, 10, 15, or 30 seconds.
- **Appearance:** Follow the system theme or pick Light or Dark. The logo adapts automatically and uses a single-color mark in system high contrast mode.

## Compatibility

| | Requirements |
| --- | --- |
| Case | darkFlash **L280 ATX PC Case** with its built-in segmented USB display. Other cases and coolers, including the L280mini, are unverified. |
| Windows | Windows 10 or newer, x64. Setup includes the driver for supported hardware temperature readings. GPU sensor availability depends on the hardware and driver. |
| Linux | Linux x64, a desktop session, and read/write access to the case display's HID device. Temperature and usage readings depend on the hardware and drivers. |

The desktop overview can show available readings even when the case display is disconnected. The physical panel shows CPU and GPU readings; memory usage is shown in the app.

## Install

Get the installer for your system from the [latest release](https://github.com/atragate/cpustats/releases/latest). Both packages include the .NET runtime.

### Windows x64

1. Open the Windows Setup executable and approve the administrator prompt. The wizard then opens and installs CPUSTATS and its sensor driver.
2. Open **CPUSTATS** from the Start menu. The installed sensor service supplies hardware readings while the desktop app runs with your regular permissions.

CPUSTATS starts in the notification area when you sign in. You can turn this off in **Settings → Apps → Startup**.

If Setup detects a running display utility, quit it or end the named process in Task Manager, then retry. Run the same Setup executable again to repair or remove an installed copy. Run a newer Setup executable to upgrade. Older portable copies can be deleted manually after you close them.

### Debian and Ubuntu x64

From the download directory, install the `.deb` with APT so it can resolve runtime dependencies:

```sh
sudo apt install ./cpustats_1.1.1_amd64.deb
```

Reconnect the case display after installation, then open **CPUSTATS** from the application menu. The installed udev rule grants the active local desktop session access to the L280 USB display. Run CPUSTATS as your regular desktop user.

If installation identifies a running CPUSTATS process, quit it and retry. To repair the current version, add `--reinstall` to the install command. To upgrade, install the newer package. To remove it, run `sudo apt remove cpustats`.

## Build and run

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or a compatible newer SDK. Then clone and build on the target OS.

```sh
git clone https://github.com/atragate/cpustats.git
cd cpustats
dotnet build CPUSTATS.sln -c Release
dotnet run --project CPUSTATS/CPUSTATS.csproj -c Release
```

### Windows

For CPU temperature, launch from an elevated terminal or close the app and use **Run as administrator** on the built executable. Only run one app that controls the display at a time.

On supported Intel Arrow Lake-S systems, the optional **IntelMCHBAR** module can supply GPU temperature when the Intel driver does not. With PawnIO installed, run this from PowerShell before building or publishing:

```powershell
.\CPUSTATS\Windows\Get-IntelMchbar.ps1
dotnet build CPUSTATS.sln -c Release
```

[Windows temperature setup and supported hardware](CPUSTATS/README.md#windows-temperatures)

### Linux

The logged-in user needs read/write access to the USB HID device. On a systemd desktop, add this udev rule before the distribution's seat rules (for example, in `/etc/udev/rules.d/70-l280-display.rules`), reload the rules, and reconnect the display:

```udev
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="5131", ATTRS{idProduct}=="2007", TAG+="uaccess"
```

```sh
sudo udevadm control --reload-rules
```

If i915 does not expose a temperature sensor on a supported Intel Arrow Lake-S system, see the [optional Linux GPU temperature bridge](linux/README.md).
Install your distribution's `pciutils` package to show PCI graphics model names on Linux. If the name cannot be resolved, CPUSTATS shows the adapter's PCI ID when available.

## Use CPUSTATS

1. Open CPUSTATS to see live readings in **PC overview**.
2. Open the gear to check the **Case display** connection or choose what the case shows.
3. Select **Alternate temp and usage** to enable **Switch every**. The default is six seconds per view. This setting only changes the case display.
4. Set **App theme** to **Match system**, **Light**, or **Dark**.

**Last updated** is the local time CPUSTATS last read your PC sensors. The app normally reads the sensors about once a second, independently of how often the case display switches views.

Closing the window keeps CPUSTATS running and updating the case display. From the tray, you can reopen CPUSTATS, switch the case display mode, pause or resume panel updates, reconnect the display, or quit. Pausing leaves the last frame on the panel and keeps the app’s sensor readings live. On Linux, the desktop needs a system tray with StatusNotifierItem or AppIndicator support. On GNOME, enable the AppIndicator extension before closing the window.

## Troubleshooting

- **The case display is disconnected:** Check the USB connection, close any other utility that controls the case display, then choose **Settings → Reconnect**. On Linux, check HID permissions.
- **A temperature shows a dash:** CPUSTATS could not get a reading from that sensor. Check the Windows or Linux setup above. Some drivers do not expose every temperature.
- **The case keeps switching between temperatures and percentages:** Choose a fixed mode under **Settings → Show on case**.

## Development

```sh
dotnet test CPUSTATS.sln -c Release
```

See the [build and publish notes](CPUSTATS/README.md), [display protocol](docs/L280-PROTOCOL.md), and [Linux temperature bridge](linux/README.md).

## License

[MIT](LICENSE).
