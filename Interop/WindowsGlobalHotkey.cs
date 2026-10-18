using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace EndfieldChargePlus.Interop;

// Owned by the application UI thread, independently of HUD HWNDs and Settings.
internal sealed class WindowsGlobalHotkey : IDisposable
{
    private readonly Action _pressed;
    private readonly WndProc _callback;
    private readonly string _className = $"EndfieldChargePlus.Hotkey.{Guid.NewGuid():N}";
    private readonly IntPtr _instance = GetModuleHandleW(null);
    private GCHandle _callbackRoot;
    private ushort _atom;
    private IntPtr _window;
    private int _registration;
    private bool _disposed;
    internal string Gesture { get; private set; } = "";
    internal int LastError { get; private set; }

    internal WindowsGlobalHotkey(Action pressed)
    {
        _pressed = pressed;
        _callback = WindowProc;
        _callbackRoot = GCHandle.Alloc(_callback);
        try
        {
            var wc = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = _instance,
                Callback = Marshal.GetFunctionPointerForDelegate(_callback), Name = _className };
            _atom = RegisterClassExW(ref wc);
            if (_atom == 0) throw new InvalidOperationException($"Hotkey window class: {Marshal.GetLastWin32Error()}");
            _window = CreateWindowExW(0, _className, "ECP Hotkey", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, _instance, IntPtr.Zero);
            if (_window == IntPtr.Zero) throw new InvalidOperationException($"Hotkey window: {Marshal.GetLastWin32Error()}");
        }
        catch { Dispose(); throw; }
    }

    internal bool TryUpdate(string? text)
    {
        LastError = 0;
        if (_disposed) return false;
        text = text?.Trim() ?? "";
        if (text.Length == 0)
        {
            if (_registration != 0) UnregisterHotKey(_window, _registration);
            _registration = 0;
            Gesture = "";
            return true;
        }
        if (!HudHotkeyGesture.TryParse(text, out var gesture)) return false;
        if (Gesture == gesture.Text) return true;
        // Register the replacement first: a conflict must leave the old shortcut usable.
        int next = _registration == 1 ? 2 : 1;
        if (!RegisterHotKey(_window, next, gesture.Modifiers | 0x4000, gesture.VirtualKey))
        {
            LastError = Marshal.GetLastWin32Error();
            return false;
        }
        if (_registration != 0) UnregisterHotKey(_window, _registration);
        _registration = next;
        Gesture = gesture.Text;
        return true;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!_disposed && message == 0x0312 && wParam.ToInt32() == _registration && _registration != 0)
        {
            int registration = _registration;
            long chord = lParam.ToInt64();
            string gesture = Gesture;
            Dispatcher.UIThread.Post(() =>
            {
                // Discard queued messages from a replaced or cleared binding.
                if (!_disposed && registration == _registration && Gesture == gesture
                    && HudHotkeyGesture.TryParse(gesture, out var current)
                    && ((uint)chord & 0xFFFF) == current.Modifiers && ((uint)(chord >> 16) & 0xFFFF) == current.VirtualKey)
                    _pressed();
            }, DispatcherPriority.Input);
            return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registration != 0) UnregisterHotKey(_window, _registration);
        if (_window != IntPtr.Zero) DestroyWindow(_window);
        if (_atom != 0) UnregisterClassW(_className, _instance);
        _window = IntPtr.Zero;
        _registration = 0;
        Gesture = "";
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClassW(string name, IntPtr instance);
}
