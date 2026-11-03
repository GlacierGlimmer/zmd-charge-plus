#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
packages=$(realpath -- "${1:-$root/dist/linux-x64}")
(cd "$packages" && sha256sum -c SHA256SUMS)
work=$(mktemp -d -t ecp-verify-XXXXXXXX)
trap 'rm -rf -- "$work"' EXIT
shopt -s nullglob
archives=("$packages"/*.tar.gz)
debs=("$packages"/*.deb)
rpms=("$packages"/*.rpm)
images=("$packages"/*.AppImage)
[[ ${#archives[@]} == 1 && ${#debs[@]} == 1 && ${#rpms[@]} == 1 && ${#images[@]} == 1 ]]
mkdir -p "$work"/{tar,deb,rpm,image}
tar -xzf "${archives[0]}" -C "$work/tar"
dpkg-deb --info "${debs[0]}"
dpkg-deb -x "${debs[0]}" "$work/deb"
rpm -qp --requires "${rpms[0]}"
(cd "$work/rpm" && rpm2cpio "${rpms[0]}" | cpio -idm --quiet)
(cd "$work/image" && "${images[0]}" --appimage-extract >/dev/null)
desktop-file-validate "$work/deb/usr/share/applications/endfield-charge-plus-for-linux.desktop"
desktop-file-validate "$work/rpm/usr/share/applications/endfield-charge-plus-for-linux.desktop"
desktop-file-validate "$work/image/squashfs-root/endfield-charge-plus-for-linux.desktop"
executables=("$work"/tar/*/EndfieldChargePlus "$work/deb/opt/endfield-charge-plus-for-linux/EndfieldChargePlus"
    "$work/rpm/opt/endfield-charge-plus-for-linux/EndfieldChargePlus" "$work/image/squashfs-root/usr/lib/endfield-charge-plus-for-linux/EndfieldChargePlus")
for executable in "${executables[@]}"; do
    [[ -x $executable ]]
    if ldd "$(dirname -- "$executable")/libSkiaSharp.so" | grep 'not found'; then exit 1; fi
    bash "$root/scripts/smoke-linux.sh" "$executable"
done
# Exercise the AppImage entry point and runtime without depending on FUSE.
APPIMAGE_EXTRACT_AND_RUN=1 bash "$root/scripts/smoke-linux.sh" "${images[0]}"
echo 'PASS: all four package payloads and AppImage runtime'
