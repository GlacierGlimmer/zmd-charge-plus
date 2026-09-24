using System;
using System.Runtime.InteropServices;
using Avalonia;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus.Interop;

/// <summary>
/// Makes the complete native HUD host window mouse-through. Do not rely on
/// Avalonia's IsHitTestVisible: the 1200x160 native window is larger than the
/// visible HUD, and it must not intercept input anywhere within that rectangle.
/// </summary>
internal sealed class WindowsHudHitTest : IDisposable
{
    private const int GwlExStyle = -20;
    private const uint WsExLayered = 0x00080000u;
    private const uint WsExTransparent = 0x00000020u;
    private const uint MouseThroughStyles = WsExLayered | WsExTransparent;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private readonly IntPtr _hwnd;
    private bool _disposed;
    private bool _failureLogged;

    public IntPtr Handle => _hwnd;

    private WindowsHudHitTest(IntPtr hwnd)
    {
        _hwnd = hwnd;
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
        catch (Exception ex)
        {
            AppLog.Error("Unable to initialize native HUD mouse-through.", ex);
            return null;
        }
    }

    /// <summary>
    /// Reapply after Show()/Topmost changes, which may rewrite HWND styles.
    /// This reproduces the independently verified x86 workaround: OR in
    /// WS_EX_LAYERED | WS_EX_TRANSPARENT, refresh the window frame and read back.
    /// No comctl32 window subclass is required; failure there must never block
    /// the essential click-through style change.
    /// </summary>
    public void Reapply()
    {
        if (_disposed || _hwnd == IntPtr.Zero)
            return;

        try
        {
            uint before = GetExtendedStyle();
            uint wanted = before | MouseThroughStyles;
            if (wanted != before)
                SetExtendedStyle(wanted);

            uint afterSet = GetExtendedStyle();
            if ((afterSet & MouseThroughStyles) != MouseThroughStyles)
            {
                LogFailure($"HUD mouse-through styles missing after set. ProcessBits={IntPtr.Size * 8}; " +
                           $"before=0x{before:X8}; wanted=0x{wanted:X8}; actual=0x{afterSet:X8}; " +
                           $"Win32Error={Marshal.GetLastPInvokeError()}.");
                return;
            }

            if (!SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged))
            {
                LogFailure($"HUD SetWindowPos failed. ProcessBits={IntPtr.Size * 8}; " +
                           $"Win32Error={Marshal.GetLastPInvokeError()}.");
                return;
            }

            uint afterFrameChange = GetExtendedStyle();
            if ((afterFrameChange & MouseThroughStyles) != MouseThroughStyles)
            {
                LogFailure($"HUD mouse-through styles lost after frame refresh. ProcessBits={IntPtr.Size * 8}; " +
                           $"actual=0x{afterFrameChange:X8}.");
                return;
            }

            _failureLogged = false;
        }
        catch (Exception ex)
        {
            if (_failureLogged)
                return;

            _failureLogged = true;
            AppLog.Error("Failed to apply native HUD mouse-through styles.", ex);
        }
    }

    private void LogFailure(string message)
    {
        if (_failureLogged)
            return;

        _failureLogged = true;
        AppLog.Warn(message);
    }

    private uint GetExtendedStyle() => IntPtr.Size == 4
        ? unchecked((uint)GetWindowLong32(_hwnd, GwlExStyle))
        : unchecked((uint)GetWindowLongPtr64(_hwnd, GwlExStyle).ToInt64());

    private void SetExtendedStyle(uint style)
    {
        if (IntPtr.Size == 4)
            SetWindowLong32(_hwnd, GwlExStyle, unchecked((int)style));
        else
            SetWindowLongPtr64(_hwnd, GwlExStyle, new IntPtr(unchecked((long)style)));
    }

    public void Dispose() => _disposed = true;

    // The *Ptr exports do not exist in 32-bit user32.dll; C# DllImport does
    // not apply the Windows SDK's GetWindowLongPtr -> GetWindowLong macro.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", ExactSpelling = true, SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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
