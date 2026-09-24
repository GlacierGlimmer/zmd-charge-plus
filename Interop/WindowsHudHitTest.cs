using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace EndfieldChargePlus.Interop;

/// <summary>
/// Makes the HUD's native Win32 host window fully mouse-through.
/// The complete overlay is display-only: clicks, drag operations and wheel input
/// must reach whatever application/window is underneath it.
/// </summary>
internal sealed class WindowsHudHitTest : IDisposable
{
    private const uint WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private static readonly UIntPtr SubclassId = (UIntPtr)0xEC01u;

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _proc;
    private bool _attached;
    private bool _disposed;

    private WindowsHudHitTest(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = WindowProc;
        _attached = SetWindowSubclass(_hwnd, _proc, SubclassId, UIntPtr.Zero);

        // Avalonia can rewrite native extended styles when a window is shown or when
        // Topmost/layering changes, so apply them immediately and allow the owner to
        // re-apply them every time the HUD is presented.
        Reapply();
    }

    public static WindowsHudHitTest? TryAttach(IntPtr hwnd, Func<PixelPoint, bool> _)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return null;

        try
        {
            return new WindowsHudHitTest(hwnd);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Re-applies mouse-through extended styles. Safe to call repeatedly.
    /// </summary>
    public void Reapply()
    {
        if (_disposed || _hwnd == IntPtr.Zero)
            return;

        try
        {
            long current = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
            long wanted = current
                | WsExTransparent
                | WsExLayered
                | WsExNoActivate
                | WsExToolWindow;

            if (wanted != current)
                SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(wanted));

            // Force Windows to re-evaluate the non-client/frame style immediately.
            // Without this, a style written after Show()/Topmost changes can remain
            // ineffective until some later window-state transition.
            SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch
        {
            // Mouse-through is best effort; never crash the HUD because Explorer/DWM
            // temporarily rejects a native style update.
        }
    }

    private IntPtr WindowProc(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData)
    {
        if (msg == WmNcHitTest)
        {
            // The whole HUD is informational. Returning HTTRANSPARENT supplements
            // WS_EX_TRANSPARENT and prevents Avalonia's client area from winning hit tests.
            return new IntPtr(HtTransparent);
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_attached)
        {
            try { RemoveWindowSubclass(_hwnd, _proc, SubclassId); }
            catch { }
        }

        _attached = false;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr uIdSubclass,
        UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SubclassProc pfnSubclass,
        UIntPtr uIdSubclass,
        UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        SubclassProc pfnSubclass,
        UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
}
