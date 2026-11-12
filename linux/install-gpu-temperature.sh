#!/usr/bin/bash
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then
    echo 'Run this installer with sudo or pkexec.' >&2
    exit 1
fi
source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
# Verify a valid live reading before installing or enabling anything.
/usr/bin/python3 -I "$source_dir/intel_gpu_temperature.py" --once |
    /usr/bin/python3 -I -c 'import json,sys; s=json.load(sys.stdin); print(s); sys.exit(0 if s["celsius"] is not None else 1)'
install -d -o root -g root -m 0755 /etc/l280studio
install -o root -g root -m 0644 "$source_dir/intel_gpu_temperature.py" /etc/l280studio/intel_gpu_temperature.py
install -o root -g root -m 0644 "$source_dir/l280studio-gpu-temperature.service" /etc/systemd/system/l280studio-gpu-temperature.service
systemctl daemon-reload
systemctl enable l280studio-gpu-temperature.service
systemctl reset-failed l280studio-gpu-temperature.service
systemctl restart l280studio-gpu-temperature.service
