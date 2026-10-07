using System.Runtime.InteropServices;
using Avalonia;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus.Interop;

internal sealed class WindowsHudLayer
{
    private readonly IntPtr _handle;
    private IntPtr _desktop;
    private const int Style = -16;
    private const uint Child = 0x40000000;
    private const uint Popup = 0x80000000;

    internal WindowsHudLayer(IntPtr handle) => _handle = handle;

    internal void Apply(bool desktop, bool topmost)
    {
        if (!OperatingSystem.IsWindows()) return;
        var parent = desktop ? GetShellWindow() : IntPtr.Zero;
        if (desktop && parent == IntPtr.Zero) return;
        if (_desktop != parent || GetParent(_handle) != parent)
        {
            uint before = unchecked((uint)GetWindowLong(_handle, Style));
            if (desktop) SetWindowLong(_handle, Style, unchecked((int)((before | Child) & ~Popup)));
            Marshal.SetLastPInvokeError(0);
            SetParent(_handle, parent);
            if (Marshal.GetLastPInvokeError() != 0)
            {
                SetWindowLong(_handle, Style, unchecked((int)before));
                AppLog.Warn($"Unable to attach HUD to the Windows desktop: {Marshal.GetLastPInvokeError()}.");
                return;
            }
            if (!desktop) SetWindowLong(_handle, Style, unchecked((int)((before | Popup) & ~Child)));
            _desktop = parent;
        }
        SetWindowPos(_handle, desktop ? IntPtr.Zero : new IntPtr(topmost ? -1 : -2), 0, 0, 0, 0,
            0x0001 | 0x0002 | 0x0010 | 0x0020);
    }

    internal PixelPoint ToWindowPosition(PixelPoint screen)
    {
        if (_desktop == IntPtr.Zero) return screen;
        var point = new NativePoint { X = screen.X, Y = screen.Y };
        MapWindowPoints(IntPtr.Zero, _desktop, ref point, 1);
        return new PixelPoint(point.X, point.Y);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref NativePoint point, uint count);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
