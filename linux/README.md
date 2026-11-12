# Intel integrated GPU temperature on Linux

Arrow Lake-S exposes a graphics thermal sensor even when i915 does not publish
an hwmon temperature. This optional bridge reads that sensor once per second.
The Avalonia app remains unprivileged and consumes a small JSON file in `/run`.
The app rejects missing, invalid, mismatched, future-dated, or older-than-five-second samples.
Ordinary GPU hwmon sensors remain the preferred backend.

Currently supported hardware: Intel host bridge `8086:7d1b` and integrated GPU
`8086:7d67`, at PCI addresses `0000:00:00.0` and `0000:00:02.0`.
Other hardware is deliberately rejected; register layouts must be verified before extending support.

## Install on Bazzite or another systemd Linux host

From the repository root:

```sh
sudo bash linux/install-gpu-temperature.sh
systemctl status l280studio-gpu-temperature.service
cat /run/l280studio/gpu-temperature.json
```

The installer first verifies a valid live reading. It installs root-owned files in
`/etc/l280studio` and `/etc/systemd/system`, compatible with Bazzite's immutable
`/usr`. The service starts at boot, opens `/dev/mem` read-only, maps one register
page read-only, and publishes only the temperature sample. It never modifies
hardware registers, device permissions, or kernel settings. The service receives
`CAP_SYS_RAWIO` and `CAP_SYS_ADMIN` only during initialization (the latter is
required for reading PCI configuration past byte 64), then drops all effective,
permitted, and inheritable capabilities after mapping the read-only register page.
It uses a protected runtime directory and a restricted filesystem.

Kernel lockdown or strict memory-access policies may prohibit the read. The
installer then fails without changing those policies; this is not a reason to
run the desktop app as root. Inspect failures with:

```sh
journalctl -u l280studio-gpu-temperature.service -n 30
```

Uninstall:

```sh
sudo systemctl disable --now l280studio-gpu-temperature.service
sudo rm /etc/systemd/system/l280studio-gpu-temperature.service
sudo rm /etc/l280studio/intel_gpu_temperature.py
sudo systemctl daemon-reload
```

## Sensor definition and validation

Intel's Core Ultra 200S / 200HX register manual (document **835538**) defines:

- [MCHBAR, PCI offset 0x48](https://edc.intel.com/content/www/jp/ja/design/publications/core-ultra-p200s-series-processors-cfg-mem-registers/001/mchbar-base-address-register-mchbar-0-0-0-pci-offset-48/): enable bit 0, base address bits 41:17.
- [Temperature target, MCHBAR + 0x599c](https://edc.intel.com/content/www/jp/ja/design/publications/core-ultra-p200s-series-processors-cfg-mem-registers/001/temperature-target-temperature-target-0-0-0-mchbar-pcu-offset-599c/): TjMax in bits 23:16.
- [GT thermal status, MCHBAR + 0x59c0](https://edc.intel.com/content/www/us/en/design/publications/core-ultra-p200s-series-processors-cfg-mem-registers/thermal-status-gt-therm-status-gt-0-0-0-mchbar-pcu-offset-59c0/): valid bit 31, degrees below TjMax in bits 23:16.

Graphics temperature is `TjMax - GT delta`, only when the valid bit is set.
This is a graphics sensor reading, not a substitution of CPU package temperature.
On this Bazzite Arrow Lake-S host, the initial five reads returned target
`0x80691480` and GT status `0x88470000`: **105 - 71 = 34°C**.
The installed systemd service was subsequently verified active and enabled,
with zero effective/permitted/inheritable capabilities after startup. The
unprivileged Avalonia app reported 34°C, cleared its missing-temperature warning,
and successfully wrote to the USB panel. Linux validation passed 30 .NET tests
and four Python decoder tests; the Release app build had no warnings or errors.

Decoder tests: `python3 -m unittest discover -s linux -p 'test_*.py'`.
The .NET test project covers freshness, device matching, and malformed/unavailable samples.
