# L280 segmented display protocol

Verified on the user's supplied PC Monitor 1.0.0.0 assembly, shipped in the L280 driver package.
This is a segmented CPU/GPU panel, not the round cooler LCD that shares VID 5131 / PID 2007.
The earlier raw-temperature packet layout was incorrect for this case.

## HID output report

65 bytes: report ID 0 followed by a 64-byte payload.

| Report offset | Meaning |
| --- | --- |
| 0 | Report ID: 0 |
| 1 | Incrementing byte sequence, wrapping at 255 |
| 2 | Normal operation: 0 |
| 3 | Display update command: 2 |
| 25–40 | Compatibility segment bank emitted by PC Monitor |
| 41–56 | L280's 16-byte LCD segment bank |
| 57–64 | Zero padding |

Normal HID output writes are correct: the supplied CyUSB WriteOutput method uses Windows WriteFile.
The earlier claim that a proprietary transfer method was required was incorrect.
No PC Monitor or CyUSB binaries are needed by CPUSTATS.

## Rendering

The host must render the digits, unit indicators, labels and bars. Each decimal digit
occupies one bit plane across seven RAM byte positions. See L280Protocol.Wiring.
CPU digits use masks 1/2/4 and GPU digits use 8/16/32. The remaining bits address indicators and bars.
Leading zeros are suppressed except the ones digit.

The host chooses Celsius, Fahrenheit or usage. The firmware does not rotate pages automatically.
CPUSTATS defaults to alternating Celsius and usage every six seconds, with a packet every 200 ms.
The interval can be changed in Settings.
Bar count is ceil(value/10), capped at ten. In Fahrenheit mode the bars still use Celsius.
Unavailable readings render three dashes; they must not be encoded as real zero-temperature measurements.

## Verification

scratchpad/inspect-reference.ps1 invokes only the vendor's pure segment-rendering methods
on an uninitialized form object. It never constructs the UI, starts the vendor SDK, or writes USB.
The resulting independent reference vectors are tested by HidTransportTests.
Examples for CPU/GPU 42/38:

- Celsius bank: A005B600320274027601660574000000
- Usage bank: A007B604320474047605660774000000

The user confirmed seeing CPU/GPU temperatures after the corrected diagnostic transmission.
The rebuilt application's status log confirms continuous Celsius/usage writes, CPU temperatures
changing from 37 to 39 degrees, and changing CPU/GPU load. Physical confirmation of both
alternating pages remains pending. The independent Intel graphics API probe returned success
but gpuCurrentTemperature.bSupported=false for telemetry versions 0 and 1.

## Sensor prerequisites and diagnostics

LibreHardwareMonitor 0.9.6 uses PawnIO for CPU sensors; both the driver and administrator access are needed.
The Avalonia app runs at the caller's privilege level; close it before restarting as administrator.
For the optional Arrow Lake-S GPU temperature fallback, see
[Windows temperatures](../CPUSTATS/README.md#windows-temperatures).
Fresh Loving Cool snapshots may supplement missing sensors, but snapshots older than five seconds
are rejected. CPU usage and RAM come directly from Windows, including when a vendor feed is available.

last-status.json beside the running executable records the latest sampled values, source,
warnings, current page, last USB result and exact report bytes. A completed USB write
does not itself confirm the physical LCD's appearance.
