using System.Runtime.InteropServices;
using Avalonia;

namespace EndfieldChargePlus.Interop;

/// <summary>X11/XWayland integration; native Wayland does not permit global pointer queries.</summary>
internal static class LinuxDesktop
{
    private static readonly object Gate = new();
    private static IntPtr _display;

    internal static void PrepareClipboard()
    {
        if (!OperatingSystem.IsLinux()) return;
        lock (Gate)
        {
            try
            {
                // Avalonia 11.2 looks up existing clipboard atoms. A fresh X server or
                // minimal desktop may have no clipboard manager to create them first.
                XInitThreads();
                if (!Connect()) return;
                foreach (var atom in new[] { "CLIPBOARD", "TARGETS", "UTF8_STRING", "UTF16_STRING", "MULTIPLE", "ATOM_PAIR", "SAVE_TARGETS" })
                    XInternAtom(_display, atom, false);
                XFlush(_display);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    private static bool Connect()
    {
        if (!OperatingSystem.IsLinux()) return false;
        if (_display == IntPtr.Zero) _display = XOpenDisplay(IntPtr.Zero);
        return _display != IntPtr.Zero;
    }

    internal static bool TryGetCursorPosition(out PixelPoint point)
    {
        point = default;
        lock (Gate)
        {
            try
            {
                if (!Connect()) return false;
                if (XQueryPointer(_display, XDefaultRootWindow(_display), out _, out _, out int x, out int y,
                        out _, out _, out _) == 0) return false;
                point = new PixelPoint(x, y);
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    internal static void SetClickThrough(IntPtr window)
    {
        lock (Gate)
        {
            try
            {
                if (window == IntPtr.Zero || !Connect() || XShapeQueryExtension(_display, out _, out _) == 0) return;
                // An empty ShapeInput region lets mouse events reach windows underneath the HUD.
                XShapeCombineRectangles(_display, window, 2, 0, 0, IntPtr.Zero, 0, 0, 0);
                XFlush(_display);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    internal static void Close()
    {
        lock (Gate)
        {
            if (_display != IntPtr.Zero) XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XInitThreads();
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XQueryPointer(IntPtr display, IntPtr window,
        out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
    [DllImport("libXext.so.6")] private static extern int XShapeQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window,
        int kind, int x, int y, IntPtr rectangles, int count, int operation, int ordering);
}
