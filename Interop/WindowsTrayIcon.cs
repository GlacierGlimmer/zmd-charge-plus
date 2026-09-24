using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace EndfieldChargePlus.Interop;

/// <summary>
/// Windows notification-area icon implemented with Shell_NotifyIcon so both
/// left-click and right-click can open the same custom Avalonia tray menu.
/// </summary>
internal sealed class WindowsTrayIcon : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint TrayCallbackMessage = WmApp + 0x51;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Action _clicked;
    private readonly Action _doubleClicked;
    private readonly WndProc _wndProc;
    private readonly string _className;
    private readonly IntPtr _instance;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _ownsIcon;
    private ushort _classAtom;
    private bool _added;
    private bool _disposed;

    public WindowsTrayIcon(Action clicked, Action doubleClicked, string tooltip)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows tray icon is only available on Windows.");

        _clicked = clicked ?? throw new ArgumentNullException(nameof(clicked));
        _doubleClicked = doubleClicked ?? throw new ArgumentNullException(nameof(doubleClicked));
        _wndProc = WindowProc;
        _className = $"EndfieldChargePlus.Tray.{Guid.NewGuid():N}";
        _instance = GetModuleHandleW(null);

        RegisterWindowClass();
        CreateMessageWindow();
        LoadTrayIcon();
        AddIcon(tooltip);
    }

    private void RegisterWindowClass()
    {
        var wc = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            hInstance = _instance,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = _className,
        };

        _classAtom = RegisterClassExW(ref wc);
        if (_classAtom == 0)
            throw new InvalidOperationException($"RegisterClassExW failed: {Marshal.GetLastWin32Error()}");
    }

    private void CreateMessageWindow()
    {
        _hwnd = CreateWindowExW(
            0,
            _className,
            "Endfield Charge Plus Tray",
            0,
            0, 0, 0, 0,
            HwndMessage,
            IntPtr.Zero,
            _instance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");
    }

    private void LoadTrayIcon()
    {
        // Prefer extracting the icon embedded in the published executable.
        // This keeps the tray icon correct for single-file/self-contained builds
        // even when the external .ico asset is not present beside the executable.
        string? exePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
        {
            var small = new IntPtr[1];
            var large = new IntPtr[1];
            uint extracted = ExtractIconExW(exePath, 0, large, small, 1);

            _hIcon = small[0] != IntPtr.Zero ? small[0] : large[0];
            if (_hIcon != IntPtr.Zero)
            {
                _ownsIcon = true;

                // ExtractIconEx may return both icon handles. Keep only the one we use.
                IntPtr unused = _hIcon == small[0] ? large[0] : small[0];
                if (unused != IntPtr.Zero && unused != _hIcon)
                {
                    try { DestroyIcon(unused); }
                    catch { }
                }
            }
        }

        if (_hIcon == IntPtr.Zero)
        {
            // Final fallback only. This is a shared system icon and must not be destroyed.
            _hIcon = LoadIconW(IntPtr.Zero, new IntPtr(32512));
            _ownsIcon = false;
        }
    }

    private void AddIcon(string tooltip)
    {
        var data = CreateNotifyData(tooltip);
        _added = Shell_NotifyIconW(NimAdd, ref data);
        if (!_added)
            throw new InvalidOperationException($"Shell_NotifyIconW(NIM_ADD) failed: {Marshal.GetLastWin32Error()}");
    }

    private NotifyIconData CreateNotifyData(string tooltip) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = TrayCallbackMessage,
        hIcon = _hIcon,
        szTip = string.IsNullOrWhiteSpace(tooltip) ? "Endfield Charge Plus" : tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == TrayCallbackMessage)
        {
            uint mouseMessage = unchecked((uint)lParam.ToInt64()) & 0xFFFFu;
            if (mouseMessage == WmLButtonDblClk)
            {
                Dispatcher.UIThread.Post(_doubleClicked, DispatcherPriority.Input);
                return IntPtr.Zero;
            }

            if (mouseMessage is WmLButtonUp or WmRButtonUp or WmContextMenu)
            {
                Dispatcher.UIThread.Post(_clicked, DispatcherPriority.Input);
                return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void ShowNotification(string title, string message)
    {
        if (_disposed || !_added || _hwnd == IntPtr.Zero)
            return;

        var data = CreateNotifyData("Endfield Charge Plus");
        data.uFlags = NifInfo;
        data.szInfoTitle = TrimForNotify(title, 63);
        data.szInfo = TrimForNotify(message, 255);
        data.uTimeoutOrVersion = 5000;
        data.dwInfoFlags = NiifInfo;

        try { Shell_NotifyIconW(NimModify, ref data); }
        catch { }
    }

    private static string TrimForNotify(string? value, int maxLength)
    {
        string text = value ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added && _hwnd != IntPtr.Zero)
        {
            var data = CreateNotifyData("Endfield Charge Plus");
            try { Shell_NotifyIconW(NimDelete, ref data); }
            catch { }
            _added = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            try { DestroyWindow(_hwnd); }
            catch { }
            _hwnd = IntPtr.Zero;
        }

        if (_ownsIcon && _hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); }
            catch { }
        }
        _hIcon = IntPtr.Zero;

        if (_classAtom != 0)
        {
            try { UnregisterClassW(_className, _instance); }
            catch { }
            _classAtom = 0;
        }

        GC.SuppressFinalize(this);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(
        string szFileName,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        uint nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
