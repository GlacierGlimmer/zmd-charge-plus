using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;

namespace EndfieldChargePlus.Interop;

internal sealed class WindowsHudLayer : IDisposable
{
    private readonly IntPtr _handle;
    private readonly Func<IntPtr> _desktopHost;
    private bool? _desktopMode;
    private bool? _topmost;
    private IntPtr _lastDesktopHost;
    private const int Style = -16;
    private const uint Child = 0x40000000;
    private const uint Popup = 0x80000000;
    private bool _disposed;
    private static readonly HashSet<WindowsHudLayer> DesktopLayers=new();
    private static DispatcherTimer? _desktopWatch;

    internal WindowsHudLayer(IntPtr handle,Func<IntPtr>? desktopHost=null)
    {
        _handle=handle;
        _desktopHost=desktopHost ?? FindDesktopHost;
    }

    internal void Apply(bool desktop,bool topmost)
        => Apply(desktop,topmost,refreshDesktop:false);

    internal void Raise()
    {
        if(!OperatingSystem.IsWindows() || _disposed) return;
        SetWindowPos(_handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010);
        _topmost=true;
    }

    private void Apply(bool desktop,bool topmost,bool refreshDesktop)
    {
        if(!OperatingSystem.IsWindows() || _disposed) return;
        uint style=unchecked((uint)GetWindowLong(_handle,Style));
        bool wasChild=(style&Child)!=0;
        // Keep Avalonia's composited/legacy surface a top-level HWND. Reparenting
        // it into Explorer breaks software rendering and introduces cross-process
        // DPI changes. Desktop mode is a stable position in the normal Z band.
        if(wasChild)
        {
            SetParent(_handle,IntPtr.Zero);
            SetWindowLong(_handle,Style,unchecked((int)((style|Popup)&~Child)));
        }
        // Avalonia assigns its shared hidden owner to ShowInTaskbar=false
        // windows. Demoting one owned HWND also demotes that owner and its
        // other owned HUDs. Give each tool HUD independent native ownership;
        // its capsule input remains owned only by this visual HUD.
        if(GetWindow(_handle,4)!=IntPtr.Zero) SetOwner(_handle,IntPtr.Zero);
        bool effectiveTopmost=!desktop && topmost;
        bool actualTopmost=(GetWindowLong(_handle,-20)&8)!=0;
        var host=desktop ? _desktopHost() : IntPtr.Zero;
        if(!refreshDesktop && !wasChild && _desktopMode==desktop && _topmost==effectiveTopmost
            && actualTopmost==effectiveTopmost && _lastDesktopHost==host) return;

        const uint stable=0x0001|0x0002|0x0010|0x0200; // preserve size, position, focus and owner order
        uint flags=stable|(wasChild ? 0x0020u : 0u);
        // A desktop-order repair preserves the native band. Demoting an
        // already normal HUD first would briefly raise it over application windows.
        if(!refreshDesktop || wasChild || actualTopmost!=effectiveTopmost)
            SetWindowPos(_handle,new IntPtr(effectiveTopmost ? -1 : -2),0,0,0,0,flags);
        if(desktop)
        {
            // Insert immediately above the visible shell desktop and below
            // ordinary applications. HWND_BOTTOM can put a HUD behind wallpaper.
            var previous=host!=IntPtr.Zero ? GetWindow(host,3) : IntPtr.Zero; // GW_HWNDPREV
            if(previous!=_handle)
            {
                // With no normal window above the desktop, HWND_TOP retains the
                // already non-topmost band without promoting it to a topmost HWND.
                if(previous!=IntPtr.Zero && (GetWindowLong(previous,-20)&8)!=0) previous=IntPtr.Zero;
                SetWindowPos(_handle,host==IntPtr.Zero ? new IntPtr(1) : previous,0,0,0,0,stable);
            }
        }
        _desktopMode=desktop;_topmost=effectiveTopmost;_lastDesktopHost=host;
        if(desktop)
        {
            DesktopLayers.Add(this);
            if(_desktopWatch is null)
            {
                _desktopWatch=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
                _desktopWatch.Tick+=(_,_)=>
                {
                    foreach(var layer in DesktopLayers.ToArray()) layer.RepairDesktopOrder();
                };
            }
            _desktopWatch.Start();
        }
        else UnregisterDesktop();
    }

    private void RepairDesktopOrder()
    {
        if(_disposed || !_desktopMode.GetValueOrDefault()) return;
        var host=_desktopHost();
        bool covered=false;
        // Explorer reorders its wallpaper after the foreground notification.
        // Read native Z order from one shared watchdog; only write when the
        // desktop actually overtook this HUD (or Explorer replaced its host).
        int hops=0;
        for(var next=host==IntPtr.Zero ? IntPtr.Zero : GetWindow(host,2);next!=IntPtr.Zero && hops++<512;next=GetWindow(next,2))
        {
            if(next==_handle) { covered=true;break; }
        }
        if(covered || host!=_lastDesktopHost) Apply(true,false,refreshDesktop:true);
    }

    private void UnregisterDesktop()
    {
        DesktopLayers.Remove(this);
        if(DesktopLayers.Count==0) _desktopWatch?.Stop();
    }

    public void Dispose()
    {
        _disposed=true;
        UnregisterDesktop();
    }

    internal PixelPoint ToWindowPosition(PixelPoint screen) => screen;

    private static void SetOwner(IntPtr handle,IntPtr owner)
    {
        if(IntPtr.Size==8) SetWindowLongPtr(handle,-8,owner);
        else SetWindowLong(handle,-8,owner.ToInt32());
    }

    private static IntPtr FindDesktopHost()
    {
        var host=GetShellWindow();
        // Some Explorer versions place the icon view in WorkerW, some Progman.
        // EnumWindows returns Z order, so choose the highest genuine desktop host.
        EnumWindows((window,_)=>
        {
            if(!IsWindowVisible(window) || FindWindowEx(window,IntPtr.Zero,"SHELLDLL_DefView",null)==IntPtr.Zero) return true;
            host=window;return false;
        },IntPtr.Zero);
        return host;
    }
    private delegate bool EnumWindowProc(IntPtr window,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback,IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string? title);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr window,IntPtr parent);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] private static extern int SetWindowLong(IntPtr window,int index,int value);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
