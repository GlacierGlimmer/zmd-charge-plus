using System;
using System.Runtime.InteropServices;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus.Interop;

// Hidden top-level window used only as a native WM_DEVICECHANGE receiver.
// It does not parse broadcast payloads; the USB watcher re-reads the device
// state after the event and derives arrivals/removals from the diff.
internal sealed class WindowsDeviceChangeMonitor : IDisposable
{
    private const uint WmDeviceChange = 0x0219;
    private const int DbtDevTypeDeviceInterface = 0x00000005;
    private const int DbtDevTypeVolume = 0x00000002;
    private const int DbtfMedia = 0x0001;
    private const int DeviceNotifyWindowHandle = 0;
    private static readonly Guid UsbDeviceInterfaceGuid = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private readonly Action _changed;
    private readonly WndProc _callback;
    private readonly string _className = $"EndfieldChargePlus.DeviceChange.{Guid.NewGuid():N}";
    private readonly IntPtr _instance = GetModuleHandleW(null);
    private GCHandle _callbackRoot;
    private ushort _atom;
    private IntPtr _window;
    private IntPtr _deviceNotification;
    private IntPtr _volumeNotification;
    private bool _disposed;

    internal WindowsDeviceChangeMonitor(Action changed)
    {
        _changed = changed;
        _callback = WindowProc;
        _callbackRoot = GCHandle.Alloc(_callback);
        try
        {
            var wc = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Instance = _instance,
                Callback = Marshal.GetFunctionPointerForDelegate(_callback),
                Name = _className,
            };
            _atom = RegisterClassExW(ref wc);
            if (_atom == 0) throw new InvalidOperationException($"Device-change window class: {Marshal.GetLastWin32Error()}");

            // Top-level, never shown. WM_DEVICECHANGE is broadcast to top-level windows;
            // the explicit registrations also cover USB device-interface and volume events.
            _window = CreateWindowExW(0, _className, "ECP Device Change", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
            if (_window == IntPtr.Zero) throw new InvalidOperationException($"Device-change window: {Marshal.GetLastWin32Error()}");

            RegisterNotifications();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal IntPtr WindowHandle => _window;

    private void RegisterNotifications()
    {
        var device = new DevBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf<DevBroadcastDeviceInterface>(),
            DeviceType = DbtDevTypeDeviceInterface,
            ClassGuid = UsbDeviceInterfaceGuid,
        };
        _deviceNotification = RegisterDeviceNotificationW(_window, ref device, DeviceNotifyWindowHandle);
        if (_deviceNotification == IntPtr.Zero)
            AppLog.Warn($"USB device-interface notification registration failed: {Marshal.GetLastWin32Error()}");

        var volume = new DevBroadcastVolume
        {
            Size = Marshal.SizeOf<DevBroadcastVolume>(),
            DeviceType = DbtDevTypeVolume,
            Flags = DbtfMedia,
        };
        _volumeNotification = RegisterDeviceNotificationW(_window, ref volume, DeviceNotifyWindowHandle);
        if (_volumeNotification == IntPtr.Zero)
            AppLog.Warn($"USB volume notification registration failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!_disposed && message == WmDeviceChange)
        {
            try { _changed(); }
            catch (Exception ex) { AppLog.Error("Device-change callback failed.", ex); }
            return new IntPtr(1);
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_deviceNotification != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_deviceNotification);
            _deviceNotification = IntPtr.Zero;
        }
        if (_volumeNotification != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_volumeNotification);
            _volumeNotification = IntPtr.Zero;
        }
        if (_window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
        if (_atom != 0)
        {
            UnregisterClassW(_className, _instance);
            _atom = 0;
        }
        if (_callbackRoot.IsAllocated) _callbackRoot.Free();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public IntPtr Callback;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? Menu, Name;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastDeviceInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastVolume
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public int UnitMask;
        public short Flags;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr RegisterDeviceNotificationW(IntPtr recipient, ref DevBroadcastDeviceInterface filter, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr RegisterDeviceNotificationW(IntPtr recipient, ref DevBroadcastVolume filter, int flags);
    [DllImport("user32.dll")] private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
