#!/usr/bin/python3
"""Read-only Arrow Lake-S GT sensor bridge; see README.md for Intel sources."""
import ctypes
import json
import mmap
import os
from pathlib import Path
import struct
import sys
import time

PCI_ADDRESS = '0000:00:02.0'
OUTPUT = Path('/run/l280studio/gpu-temperature.json')


def decode(target, status):
    tjmax = (target >> 16) & 0xff
    delta = (status >> 16) & 0xff
    if (target == 0xffffffff or status == 0xffffffff or not status & (1 << 31)
            or not 50 <= tjmax <= 125 or not 0 <= tjmax - delta <= 125):
        return None
    return tjmax - delta


def open_registers():
    host = Path('/sys/bus/pci/devices/0000:00:00.0')
    gpu = Path('/sys/bus/pci/devices') / PCI_ADDRESS
    for device, expected in ((host, '0x7d1b'), (gpu, '0x7d67')):
        if ((device / 'vendor').read_text().strip() != '0x8086'
                or (device / 'device').read_text().strip() != expected):
            raise RuntimeError('Unsupported hardware: this bridge requires Arrow Lake-S 8086:7d1b / 8086:7d67.')
    with (host / 'config').open('rb', buffering=0) as config:
        config.seek(0x48)
        data = config.read(8)
        if len(data) != 8:
            raise RuntimeError('PCI configuration read was restricted; startup requires CAP_SYS_ADMIN.')
        bar, = struct.unpack('<Q', data)
    base = bar & 0x3fffffe0000
    if not bar & 1 or not base:
        raise RuntimeError('MCHBAR is disabled or invalid; no register changes will be made.')
    fd = os.open('/dev/mem', os.O_RDONLY | os.O_SYNC)
    try:
        return mmap.mmap(fd, mmap.PAGESIZE, flags=mmap.MAP_SHARED,
                         prot=mmap.PROT_READ, offset=base + 0x5000)
    finally:
        os.close(fd)


def sample(registers):
    target, = struct.unpack_from('<I', registers, 0x99c)
    status, = struct.unpack_from('<I', registers, 0x9c0)
    return {'version': 1, 'pciAddress': PCI_ADDRESS, 'vendorId': '0x8086',
            'deviceId': '0x7d67', 'sampledAtUnixMs': time.time_ns() // 1_000_000,
            'celsius': decode(target, status), 'target': hex(target), 'gtStatus': hex(status)}


def drop_capabilities():
    # PCI config beyond byte 64 requires SYS_ADMIN, mapping /dev/mem requires
    # SYS_RAWIO. Neither is needed after the read-only mapping is established.
    class Header(ctypes.Structure):
        _fields_ = [('version', ctypes.c_uint32), ('pid', ctypes.c_int)]
    class Data(ctypes.Structure):
        _fields_ = [('effective', ctypes.c_uint32), ('permitted', ctypes.c_uint32), ('inheritable', ctypes.c_uint32)]
    libc = ctypes.CDLL(None, use_errno=True)
    header = Header(0x20080522, 0)
    data = (Data * 2)()
    if libc.capset(ctypes.byref(header), ctypes.byref(data)) != 0:
        raise OSError(ctypes.get_errno(), 'Cannot drop startup capabilities')


def main():
    if sys.argv[1:] not in ([], ['--once']):
        raise SystemExit('Usage: intel_gpu_temperature.py [--once]')
    with open_registers() as registers:
        drop_capabilities()
        if sys.argv[1:] == ['--once']:
            print(json.dumps(sample(registers)))
            return
        # systemd creates this directory, owned by root and not writable by users.
        info = OUTPUT.parent.stat()
        if info.st_uid != 0 or info.st_mode & 0o022:
            raise RuntimeError('Output directory must be owned by root without group/other write access.')
        while True:
            payload = json.dumps(sample(registers)) + '\n'
            temporary = OUTPUT.with_suffix('.tmp')
            fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o644)
            with os.fdopen(fd, 'w') as stream:
                stream.write(payload)
            os.replace(temporary, OUTPUT)
            time.sleep(1)


if __name__ == '__main__':
    main()
