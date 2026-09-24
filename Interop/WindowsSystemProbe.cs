using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace EndfieldChargePlus.Interop;

internal static class WindowsSystemProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public static bool TryGetCursorPosition(out PixelPoint point)
    {
        point = default;
        if (!OperatingSystem.IsWindows()) return false;
        if (!GetCursorPos(out var p)) return false;
        point = new PixelPoint(p.X, p.Y);
        return true;
    }

    public static bool TryGetAcOnline(out bool online)
    {
        online = false;
        if (!OperatingSystem.IsWindows()) return false;
        if (!GetSystemPowerStatus(out var status) || status.ACLineStatus == 255) return false;
        online = status.ACLineStatus == 1;
        return true;
    }
}
