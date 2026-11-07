#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} != --inside ]]; then
    executable=$(realpath -- "${1:?Usage: smoke-linux.sh /path/to/EndfieldChargePlus}")
    exec xvfb-run -a -s '-screen 0 1600x900x24' bash "$0" --inside "$executable"
fi
executable=$2
testdir=$(mktemp -d -t ecp-smoke-XXXXXXXX)
pid=
cleanup() {
    if [[ -n $pid ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
    rm -rf -- "$testdir"
}
trap cleanup EXIT
export XDG_DATA_HOME="$testdir/data" XDG_CONFIG_HOME="$testdir/config"
unset WAYLAND_DISPLAY
mkdir -p "$XDG_DATA_HOME/EndfieldChargePlus"
printf '%s\n' '{"AlwaysVisible":true,"StartWithWindows":false,"UiLanguage":"en-US"}' > "$XDG_DATA_HOME/EndfieldChargePlus/settings.json"
"$executable" --autostart > "$testdir/stdout.log" 2>&1 &
pid=$!
wait_window() {
    local pattern=$1 window
    for ((attempt=0; attempt<60; attempt++)); do
        kill -0 "$pid" 2>/dev/null || { cat "$testdir/stdout.log"; return 1; }
        # The private Xvfb display contains only this test. AppImage's extraction runtime
        # can fork, so the window-owning PID need not equal the launcher PID.
        window=$(xdotool search --onlyvisible --name "$pattern" 2>/dev/null | head -1 || true)
        if [[ -n $window ]]; then echo "$window"; return 0; fi
        sleep 0.25
    done
    cat "$testdir/stdout.log"
    echo "Timed out waiting for window: $pattern" >&2
    return 1
}
hud=$(wait_window '^Endfield Charge Plus$')
if xdotool search --onlyvisible --name '(Settings|设置)' >/dev/null 2>&1; then
    echo 'Autostart unexpectedly opened Settings.' >&2; exit 1
fi
# The second launch must exit promptly and activate the existing process.
timeout 15 "$executable"
settings=$(wait_window '(Settings|设置)')
timeout 15 "$executable" --autostart
kill -0 "$pid"
python3 - "$hud" <<'PY'
import ctypes, sys
x11 = ctypes.CDLL('libX11.so.6')
xext = ctypes.CDLL('libXext.so.6')
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XOpenDisplay.restype = ctypes.c_void_p
xext.XShapeGetRectangles.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int,
                                  ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int)]
xext.XShapeGetRectangles.restype = ctypes.c_void_p
x11.XFree.argtypes = [ctypes.c_void_p]
x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
d = x11.XOpenDisplay(None)
assert d, 'X11 connection failed'
count, order = ctypes.c_int(), ctypes.c_int()
rects = xext.XShapeGetRectangles(d, int(sys.argv[1]), 2, ctypes.byref(count), ctypes.byref(order))
if rects: x11.XFree(rects)
x11.XCloseDisplay(d)
assert count.value == 0, f'HUD must have an empty input region, got {count.value} rectangles'
PY
log="$XDG_DATA_HOME/EndfieldChargePlus/Logs/latest.log"
if grep -E 'FATAL|DllNotFoundException|PlatformNotSupportedException|FileNotFoundException' "$log" "$testdir/stdout.log"; then
    echo 'Platform failure in GUI smoke test.' >&2; exit 1
fi
printf 'PASS: %s (HUD, click-through, silent autostart, second-instance Settings activation)\n' "$executable"
