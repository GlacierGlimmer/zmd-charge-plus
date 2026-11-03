#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
output=${1:-"$root/artifacts/linux-audit"}
mkdir -p "$output"
output=$(realpath -- "$output")
isolated=$(mktemp -d -t ecp-audit-XXXXXXXX)
trap 'rm -rf -- "$isolated"' EXIT
export XDG_DATA_HOME="$isolated/data" XDG_CONFIG_HOME="$isolated/config"
unset WAYLAND_DISPLAY
cd "$root"
timeout 120 xvfb-run -a -s '-screen 0 1600x1000x24' \
    dotnet run --project Tests/EndfieldChargePlus.PlatformTests.csproj -c Release -- --gui-audit "$output"
