using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EndfieldChargePlus.Views;

namespace EndfieldChargePlus.Interop;

/// <summary>One throttled watcher for all HUDs. Never activates a window or changes another app.</summary>
internal sealed class SuperTopmostController : IDisposable
{
    private readonly Window _window;
    private readonly Func<PixelRect> _bounds;
    private readonly Func<bool> _drawn;
    private bool _enabled,_disposed,_nativeEnabled;
    private IntPtr[]? _oldXType;
    internal bool IsEffective {get;private set;}
    private static readonly HashSet<SuperTopmostController> Active=new();
    private static readonly DispatcherTimer Watch=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private static readonly List<IntPtr> Hooks=new();
    private static readonly WinEvent Callback=OnWinEvent;
    private static long _lastCheck;
    private static int _queued;
    private static IntPtr _xDisplay;
    private static readonly XError ErrorRoot=OnXError;
    private static Delegate? _previousError;
    private static bool _errorInstalled;
    [ThreadStatic]private static bool _forwardingError;
    static SuperTopmostController(){Watch.Tick+=(_,_)=>RepairAll();}
    internal SuperTopmostController(Window window,Func<PixelRect> bounds,Func<bool> drawn)
    {
        _window=window;_bounds=bounds;_drawn=drawn;
        window.PropertyChanged+=OnWindowChanged;
        window.Closed+=OnClosed;
    }
    private void OnClosed(object? sender,EventArgs args)=>Dispose();
    private void OnWindowChanged(object? sender,AvaloniaPropertyChangedEventArgs args)
    {if(args.Property==Visual.IsVisibleProperty)RefreshRegistration();}
    internal void SetEnabled(bool enabled)
    {
        if(_disposed)return;
        _enabled=enabled;RefreshRegistration();
    }
    private void RefreshRegistration()
    {
        if(_enabled && _window.IsVisible && !_disposed){Active.Add(this);Start();Repair();}
        else {Active.Remove(this);Restore();if(Active.Count==0)Stop();}
    }
    private static void Start()
    {
        if(OperatingSystem.IsWindows() && Hooks.Count==0)
            foreach(uint code in new uint[]{3,0x8002,0x8004})
            {var hook=SetWinEventHook(code,code,IntPtr.Zero,Callback,0,0,2);if(hook!=IntPtr.Zero)Hooks.Add(hook);}
        Watch.Start();
    }
    private static void Stop()
    {
        Watch.Stop();foreach(var hook in Hooks)UnhookWinEvent(hook);Hooks.Clear();
        if(_xDisplay!=IntPtr.Zero){XCloseDisplay(_xDisplay);_xDisplay=IntPtr.Zero;}
    }
    private static void OnWinEvent(IntPtr hook,uint code,IntPtr window,int obj,int child,uint thread,uint time)
    {
        if(Interlocked.Exchange(ref _queued,1)!=0)return;
        Dispatcher.UIThread.Post(()=>{Interlocked.Exchange(ref _queued,0);if(Environment.TickCount64-_lastCheck>=100)RepairAll();},DispatcherPriority.Background);
    }
    private static void RepairAll()
    {
        _lastCheck=Environment.TickCount64;
        foreach(var item in Active.ToArray())item.Repair();
    }
    private bool OwnInterfaceVisible()=>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
        .Any(w=>w.IsVisible && w is not HudWindow && !ReferenceEquals(w,_window))==true;
    internal void Repair()
    {
        if(!_enabled || _disposed || !_window.IsVisible || !_drawn())return;
        var handle=_window.TryGetPlatformHandle();if(handle is null || handle.Handle==IntPtr.Zero)return;
        bool effective=!OwnInterfaceVisible();IsEffective=effective;
        if(_window.Topmost!=effective)_window.Topmost=effective;
        try
        {
            if(OperatingSystem.IsWindows())
            {
                // Yield while ECP settings, menus, a radial picker or an error window is open.
                // Ordinary topmost ordering then places the newly opened interface above the HUD.
                if(!effective){_nativeEnabled=false;return;}
                if(!_nativeEnabled || WindowsCovered(handle.Handle,_bounds()))
                    SetWindowPos(handle.Handle,new IntPtr(-1),0,0,0,0,0x1|0x2|0x10|0x200|0x4000);
                _nativeEnabled=true;
            }
            else if(OperatingSystem.IsMacOS())
            {ecp_hud_super_repair(handle.Handle,effective?2:0);_nativeEnabled=effective;}
            else if(OperatingSystem.IsLinux() && handle.HandleDescriptor=="XID")
            {
                if(_xDisplay==IntPtr.Zero)
                {
                    _xDisplay=XOpenDisplay(IntPtr.Zero);
                    if(_xDisplay!=IntPtr.Zero && !_errorInstalled)
                    {var previous=XSetErrorHandler(ErrorRoot);if(previous!=IntPtr.Zero)_previousError=Marshal.GetDelegateForFunctionPointer(previous,typeof(XError));_errorInstalled=true;}
                }
                if(_xDisplay==IntPtr.Zero)return;
                if(!effective){RestoreXType(handle.Handle);_nativeEnabled=false;return;}
                if(!_nativeEnabled)
                {
                    _oldXType=ReadAtoms(handle.Handle,"_NET_WM_WINDOW_TYPE");
                    XChangeProperty(_xDisplay,handle.Handle,Atom("_NET_WM_WINDOW_TYPE"),Atom("ATOM"),32,0,new[]{Atom("_NET_WM_WINDOW_TYPE_NOTIFICATION")},1);
                    var message=new ClientMessage{Type=33,Display=_xDisplay,Window=handle.Handle,MessageType=Atom("_NET_WM_STATE"),Format=32,D0=new IntPtr(1),D1=Atom("_NET_WM_STATE_ABOVE"),D3=new IntPtr(1)};
                    XSendEvent(_xDisplay,XDefaultRootWindow(_xDisplay),false,new IntPtr((1L<<19)|(1L<<20)),ref message);
                }
                if(!_nativeEnabled || XCovered(handle.Handle,_bounds()))XRaiseWindow(_xDisplay,handle.Handle);
                XFlush(_xDisplay);_nativeEnabled=true;
            }
        }
        catch(DllNotFoundException){IsEffective=false;}
        catch(EntryPointNotFoundException){IsEffective=false;}
    }
    private static bool WindowsCovered(IntPtr hwnd,PixelRect bounds)
    {
        if((GetWindowLong(hwnd,-20)&8)==0)return true;
        int count=0;
        for(var higher=GetWindow(hwnd,3);higher!=IntPtr.Zero && count++<512;higher=GetWindow(higher,3))
        {
            GetWindowThreadProcessId(higher,out uint pid);
            if(pid==(uint)Environment.ProcessId || !IsWindowVisible(higher) || !GetWindowRect(higher,out var rect))continue;
            // DWM-cloaked windows and empty input helper HWNDs do not cover the HUD.
            if(DwmGetWindowAttribute(higher,14,out int cloaked,4)==0 && cloaked!=0)continue;
            if(rect.Right>bounds.X && rect.Left<bounds.Right && rect.Bottom>bounds.Y && rect.Top<bounds.Bottom)return true;
        }
        return false;
    }
    private static bool XCovered(IntPtr hwnd,PixelRect bounds)
    {
        var root=XDefaultRootWindow(_xDisplay);var frame=hwnd;
        for(int i=0;i<16;i++)
        {
            if(XQueryTree(_xDisplay,frame,out _,out var parent,out var children,out _)==0)break;
            if(children!=IntPtr.Zero)XFree(children);
            if(parent==root || parent==IntPtr.Zero)break;frame=parent;
        }
        if(XQueryTree(_xDisplay,root,out _,out _,out var list,out uint length)==0)return false;
        try
        {
            bool above=false;
            for(int i=0;i<Math.Min(length,4096);i++)
            {
                var other=Marshal.ReadIntPtr(list,i*IntPtr.Size);if(other==frame){above=true;continue;}
                if(!above || XGetWindowAttributes(_xDisplay,other,out var attr)==0 || attr.MapState!=2)continue;
                if(attr.X+attr.Width>bounds.X && attr.X<bounds.Right && attr.Y+attr.Height>bounds.Y && attr.Y<bounds.Bottom)return true;
            }
            return false;
        }
        finally{if(list!=IntPtr.Zero)XFree(list);}
    }
    private static IntPtr Atom(string name)=>XInternAtom(_xDisplay,name,false);
    private delegate int XError(IntPtr display,IntPtr error);
    private static int OnXError(IntPtr display,IntPtr error)
    {
        // Root stacking can change between enumeration and the next X request.
        if(display==_xDisplay || _forwardingError)return 0;
        try{_forwardingError=true;return (int)(_previousError?.DynamicInvoke(display,error)??0);}
        catch{return 0;}finally{_forwardingError=false;}
    }
    private static IntPtr[] ReadAtoms(IntPtr window,string name)
    {
        XGetWindowProperty(_xDisplay,window,Atom(name),IntPtr.Zero,new IntPtr(64),false,Atom("ATOM"),out _,out int format,out var count,out _,out var data);
        try{return format==32 && data!=IntPtr.Zero?Enumerable.Range(0,(int)Math.Min(count.ToInt64(),64)).Select(i=>Marshal.ReadIntPtr(data,i*IntPtr.Size)).ToArray():Array.Empty<IntPtr>();}
        finally{if(data!=IntPtr.Zero)XFree(data);}
    }
    private void RestoreXType(IntPtr handle)
    {
        if(_oldXType is null || _xDisplay==IntPtr.Zero)return;
        if(_oldXType.Length==0)XDeleteProperty(_xDisplay,handle,Atom("_NET_WM_WINDOW_TYPE"));
        else XChangeProperty(_xDisplay,handle,Atom("_NET_WM_WINDOW_TYPE"),Atom("ATOM"),32,0,_oldXType,_oldXType.Length);
        _oldXType=null;XFlush(_xDisplay);
    }
    private void Restore()
    {
        var handle=_window.TryGetPlatformHandle();
        if(handle is not null && handle.Handle!=IntPtr.Zero)
        {
            if(OperatingSystem.IsLinux())RestoreXType(handle.Handle);
            if(OperatingSystem.IsMacOS() && _nativeEnabled && _window.IsVisible)ecp_hud_super_repair(handle.Handle,_window.Topmost?1:0);
        }
        _nativeEnabled=false;IsEffective=false;
    }
    public void Dispose()
    {
        if(_disposed)return;_enabled=false;RefreshRegistration();_disposed=true;
        _window.PropertyChanged-=OnWindowChanged;_window.Closed-=OnClosed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    private delegate void WinEvent(IntPtr hook,uint code,IntPtr window,int obj,int child,uint thread,uint time);
    [DllImport("user32.dll")]private static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]private static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr window,uint key,out int value,int bytes);
    [DllImport("ecpmac")]private static extern int ecp_hud_super_repair(IntPtr window,int mode);
    [StructLayout(LayoutKind.Sequential,Size=192)]private struct ClientMessage {public int Type;public IntPtr Serial;public int SendEvent;public IntPtr Display,Window,MessageType;public int Format;public IntPtr D0,D1,D2,D3,D4;}
    [StructLayout(LayoutKind.Sequential)]private struct XAttributes {public int X,Y,Width,Height,Border,Depth;public IntPtr Visual,Root;public int Class,BitGravity,WinGravity,BackingStore;public UIntPtr BackingPlanes,BackingPixel;public int SaveUnder;public IntPtr Colormap;public int MapInstalled,MapState;public IntPtr AllEvents,YourEvents,DoNotPropagate;public int OverrideRedirect;public IntPtr Screen;}
    [DllImport("libX11.so.6")]private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")]private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")]private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")]private static extern IntPtr XInternAtom(IntPtr display,string name,bool only);
    [DllImport("libX11.so.6")]private static extern int XChangeProperty(IntPtr display,IntPtr window,IntPtr property,IntPtr type,int format,int mode,IntPtr[] data,int count);
    [DllImport("libX11.so.6")]private static extern int XDeleteProperty(IntPtr display,IntPtr window,IntPtr property);
    [DllImport("libX11.so.6")]private static extern int XGetWindowProperty(IntPtr display,IntPtr window,IntPtr property,IntPtr offset,IntPtr length,bool delete,IntPtr type,out IntPtr actual,out int format,out IntPtr count,out IntPtr left,out IntPtr data);
    [DllImport("libX11.so.6")]private static extern int XRaiseWindow(IntPtr display,IntPtr window);
    [DllImport("libX11.so.6")]private static extern int XQueryTree(IntPtr display,IntPtr window,out IntPtr root,out IntPtr parent,out IntPtr children,out uint count);
    [DllImport("libX11.so.6")]private static extern int XGetWindowAttributes(IntPtr display,IntPtr window,out XAttributes attributes);
    [DllImport("libX11.so.6")]private static extern int XSendEvent(IntPtr display,IntPtr window,bool propagate,IntPtr mask,ref ClientMessage message);
    [DllImport("libX11.so.6")]private static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")]private static extern int XFree(IntPtr data);
    [DllImport("libX11.so.6")]private static extern IntPtr XSetErrorHandler(XError callback);
}
