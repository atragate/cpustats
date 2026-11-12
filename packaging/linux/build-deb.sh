#!/bin/sh
set -eu

if [ "$#" -ne 3 ]; then
    echo "Usage: sh packaging/linux/build-deb.sh VERSION PUBLISHED_EXECUTABLE OUTPUT.deb" >&2
    exit 2
fi

version=$1
executable=$2
output=$3
dpkg --validate-version "$version"
source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
root=$(mktemp -d)
trap 'rm -rf -- "$root"' EXIT

install -d "$root/DEBIAN"
install -m755 "$source_dir/check-running.sh" "$root/DEBIAN/preinst"
install -m755 "$source_dir/check-running.sh" "$root/DEBIAN/prerm"
install -Dm755 "$executable" "$root/usr/bin/cpustats"
install -Dm644 "$source_dir/cpustats.desktop" "$root/usr/share/applications/cpustats.desktop"
install -Dm644 "$source_dir/70-cpustats-l280.rules" "$root/usr/lib/udev/rules.d/70-cpustats-l280.rules"
install -Dm644 "$source_dir/../../LICENSE" "$root/usr/share/doc/cpustats/copyright"
install -Dm644 "$source_dir/../../CPUSTATS/Assets/Brand/cpustats-dark.svg" \
    "$root/usr/share/icons/hicolor/scalable/apps/cpustats.svg"

cat > "$root/DEBIAN/control" <<EOF
Package: cpustats
Version: $version
Architecture: amd64
Maintainer: atragate <git-repo@atragate.com>
Section: utils
Priority: optional
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libssl3t64 | libssl3, libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu70, libfontconfig1, libx11-6, libice6, libsm6, libxrandr2, libxi6, libxcursor1, libxinerama1, udev
Recommends: pciutils
Homepage: https://github.com/atragate/cpustats
Description: CPU and GPU monitor for the darkFlash L280 case display
 Desktop application for CPU and GPU temperature and usage monitoring,
 with control of the built-in darkFlash L280 USB display.
EOF

for hook in postinst postrm; do
    cat > "$root/DEBIAN/$hook" <<'EOF'
#!/bin/sh
set -e

if [ -S /run/udev/control ]; then
    udevadm control --reload-rules || true
fi
EOF
    chmod 755 "$root/DEBIAN/$hook"
done

dpkg-deb --root-owner-group --build "$root" "$output"
