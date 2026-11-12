#!/bin/sh
set -eu

case "${1:-}" in
    install|upgrade|remove) ;;
    *) exit 0 ;;
esac

running=0
for proc in /proc/[0-9]*; do
    executable=$(readlink "$proc/exe" 2>/dev/null) || continue
    name=${executable##*/}
    name=${name% (deleted)}
    case "$name" in
        cpustats|CPUSTATS|CPUSTATS-v*-linux-x64|L280Studio|RigBeat)
            printf 'CPUSTATS setup found %s running (PID %s).\n' "$name" "${proc##*/}" >&2
            running=1
            ;;
    esac
done

if [ "$running" -eq 1 ]; then
    echo "Quit it from the system tray, or use 'kill PID' for the process shown above, then retry." >&2
    exit 1
fi
