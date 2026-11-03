#!/usr/bin/env bash
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
python3 "$root/scripts/localization/langtool.py" verify
version=${VERSION:-$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$root/EndfieldChargePlus.csproj" | tr -d '\r')}
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'VERSION must be major.minor.patch' >&2; exit 1; }
[[ $(uname -m) == x86_64 ]] || { echo 'This packaging script currently targets Linux x64.' >&2; exit 1; }
out=${OUTPUT_DIR:-"$root/dist/linux-x64"}
mkdir -p "$out"
out=$(cd -- "$out" && pwd)
for tool in dotnet dpkg-deb rpmbuild desktop-file-validate curl sha256sum tar; do
    command -v "$tool" >/dev/null || { echo "Missing build tool: $tool" >&2; exit 1; }
done
# Build on a Linux filesystem so executable bits and symlinks survive WSL/NTFS checkouts.
work=$(mktemp -d -t ecp-package-XXXXXXXX)
trap 'rm -rf -- "$work"' EXIT
publish="$work/publish"
if [[ -n ${PUBLISH_DIR:-} ]]; then
    mkdir -p "$publish"
    cp -a "$PUBLISH_DIR/." "$publish/"
else
    dotnet publish "$root/EndfieldChargePlus.csproj" -c Release -r linux-x64 --self-contained true \
        -p:Version="$version" -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$publish"
fi
[[ -f "$publish/EndfieldChargePlus" && -f "$publish/libcoreclr.so" ]] || { echo 'Expected a self-contained Linux publish.' >&2; exit 1; }
chmod 755 "$publish/EndfieldChargePlus"
cp "$root/LICENSE" "$root/NOTICE.md" "$root/PRIVACY.md" "$root/README.linux.md" "$publish/"
find "$publish" -name '*.pdb' -delete
base="EndfieldChargePlusForLinux-v$version-linux-x64"
mkdir -p "$work/$base"
cp -a "$publish/." "$work/$base/"
tar --owner=0 --group=0 -czf "$out/$base.tar.gz" -C "$work" "$base"

stage="$work/package"
mkdir -p "$stage/opt/endfield-charge-plus-for-linux" "$stage/usr/bin" "$stage/usr/share/applications" \
    "$stage/usr/share/pixmaps" "$stage/usr/share/doc/endfield-charge-plus-for-linux"
cp -a "$publish/." "$stage/opt/endfield-charge-plus-for-linux/"
ln -s /opt/endfield-charge-plus-for-linux/EndfieldChargePlus "$stage/usr/bin/endfield-charge-plus-for-linux"
install -m 644 "$root/packaging/linux/endfield-charge-plus-for-linux.desktop" "$stage/usr/share/applications/"
install -m 644 "$root/Assets/tray_bolt.png" "$stage/usr/share/pixmaps/endfield-charge-plus-for-linux.png"
install -m 644 "$root/LICENSE" "$stage/usr/share/doc/endfield-charge-plus-for-linux/copyright"
desktop-file-validate "$stage/usr/share/applications/endfield-charge-plus-for-linux.desktop"
find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/opt/endfield-charge-plus-for-linux/EndfieldChargePlus"

mkdir -p "$stage/DEBIAN"
cat > "$stage/DEBIAN/control" <<EOF
Package: endfield-charge-plus-for-linux
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: GlacierGlimmer <glacierglimmer@users.noreply.github.com>
Homepage: https://github.com/GlacierGlimmer/zmd-charge-plus
Installed-Size: $(du -sk "$stage/opt" | cut -f1)
Depends: libc6 (>= 2.35), libgcc-s1, libstdc++6, zlib1g, libssl3, libicu70 | libicu72 | libicu74 | libicu76 | libicu78, libx11-6, libice6, libsm6, libfontconfig1, libxext6, libxrender1, libxcb1, libxrandr2, libxi6, libxcursor1, iputils-ping
Recommends: fonts-noto-cjk, xdg-utils
Description: Endfield Charge Plus For Linux - customizable system HUD
 Displays Linux system metrics with customizable animated HUD profiles.
 Includes the .NET runtime. Requires an X11 or XWayland desktop session.
EOF
dpkg-deb --root-owner-group --build "$stage" "$out/$base.deb"

mkdir -p "$work/rpm"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}
cat > "$work/rpm/SPECS/endfield-charge-plus-for-linux.spec" <<EOF
Name: endfield-charge-plus-for-linux
Version: $version
Release: 1
Summary: Endfield Charge Plus For Linux - customizable system HUD
License: MIT
URL: https://github.com/GlacierGlimmer/zmd-charge-plus
BuildArch: x86_64
AutoReqProv: no
Requires: glibc >= 2.35, libgcc, libstdc++, zlib, openssl-libs, libicu, libX11, libICE, libSM, fontconfig, libXext, libXrender, libxcb, libXrandr, libXi, libXcursor, iputils
Recommends: google-noto-sans-cjk-fonts, xdg-utils
%description
Displays Linux system metrics with customizable animated HUD profiles.
Includes the .NET runtime. Requires an X11 or XWayland desktop session.
%install
mkdir -p %{buildroot}
cp -a "$stage/opt" "$stage/usr" %{buildroot}/
%files
/opt/endfield-charge-plus-for-linux
/usr/bin/endfield-charge-plus-for-linux
/usr/share/applications/endfield-charge-plus-for-linux.desktop
/usr/share/pixmaps/endfield-charge-plus-for-linux.png
%dir /usr/share/doc/endfield-charge-plus-for-linux
%license /usr/share/doc/endfield-charge-plus-for-linux/copyright
EOF
rpmbuild --define "_topdir $work/rpm" --define '_build_id_links none' \
    --define '__os_install_post %{nil}' -bb "$work/rpm/SPECS/endfield-charge-plus-for-linux.spec"
cp "$work/rpm/RPMS/x86_64/endfield-charge-plus-for-linux-$version-1.x86_64.rpm" "$out/$base.rpm"

appdir="$work/EndfieldChargePlus.AppDir"
mkdir -p "$appdir/usr/lib/endfield-charge-plus-for-linux"
cp -a "$publish/." "$appdir/usr/lib/endfield-charge-plus-for-linux/"
install -m 755 "$root/packaging/linux/AppRun" "$appdir/AppRun"
install -m 644 "$root/packaging/linux/endfield-charge-plus-for-linux.desktop" "$appdir/"
install -m 644 "$root/Assets/tray_bolt.png" "$appdir/endfield-charge-plus-for-linux.png"
ln -s endfield-charge-plus-for-linux.png "$appdir/.DirIcon"
# Pinned official appimagetool release, verified before execution.
tool=${APPIMAGETOOL:-"$root/artifacts/tools/appimagetool-x86_64.AppImage"}
if [[ ! -f "$tool" ]]; then
    mkdir -p "$(dirname -- "$tool")"
    curl --fail --location --retry 3 --connect-timeout 20 --max-time 300 \
        https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage -o "$tool"
fi
echo "ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0  $tool" | sha256sum -c -
chmod +x "$tool"
# Extracting the build tool avoids requiring FUSE in CI/WSL containers.
(cd "$work" && "$tool" --appimage-extract >/dev/null)
ARCH=x86_64 "$work/squashfs-root/AppRun" --no-appstream "$appdir" "$out/$base.AppImage"
chmod +x "$out/$base.AppImage"
(cd "$out" && sha256sum "$base.tar.gz" "$base.AppImage" "$base.deb" "$base.rpm" > SHA256SUMS)
printf 'Linux packages written to %s\n' "$out"
