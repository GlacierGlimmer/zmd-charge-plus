using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus.Interop;

// Desktop-native middle-button capture. Neither backend grabs keyboard input or
// posts synthetic events. X11 replays presses outside a HUD to their original owner.
internal sealed class UnixRadialInput : IDisposable
{
    private readonly Func<PixelPoint,bool> _hit;
    private readonly Action<int,PixelPoint,int> _input;
    internal Func<bool>? WantsWheel;
    private static readonly List<UnixRadialInput> Entries=new();
    private static UnixRadialInput? _active;
    private static readonly DispatcherTimer Poll=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private static IntPtr _display,_tap,_source,_mode;
    private static readonly TapCallback TapRoot=OnMacEvent;
    private static readonly XErrorCallback ErrorRoot=OnXError;
    private static XErrorCallback? _previousXError;
    private static bool _xErrorInstalled;
    [ThreadStatic] private static bool _forwardingXError;
    private static bool _escape;
    static UnixRadialInput(){Poll.Tick+=(_,_)=>PumpX11();}
    internal UnixRadialInput(Func<PixelPoint,bool> hit,Action<int,PixelPoint,int> input)
    {
        _hit=hit;_input=input;Entries.Add(this);
        try {
            if(OperatingSystem.IsMacOS() && _tap==IntPtr.Zero) {
                _tap=CGEventTapCreate(1,0,0,(1UL<<25)|(1UL<<26)|(1UL<<27)|(1UL<<22),TapRoot,IntPtr.Zero);
                if(_tap!=IntPtr.Zero) {
                    _mode=CFStringCreateWithCString(IntPtr.Zero,"kCFRunLoopCommonModes",0x08000100);
                    _source=CFMachPortCreateRunLoopSource(IntPtr.Zero,_tap,0);CFRunLoopAddSource(CFRunLoopGetCurrent(),_source,_mode);CGEventTapEnable(_tap,true);
                } else AppLog.Warn("Radial switching needs macOS input-monitoring/accessibility permission.");
            } else if(OperatingSystem.IsLinux() && _display==IntPtr.Zero) {
                XInitThreads();_display=XOpenDisplay(IntPtr.Zero);
                if(_display!=IntPtr.Zero) {
                    if(!_xErrorInstalled) {var old=XSetErrorHandler(ErrorRoot);if(old!=IntPtr.Zero)_previousXError=Marshal.GetDelegateForFunctionPointer<XErrorCallback>(old);_xErrorInstalled=true;}
                    XGrabButton(_display,2,1u<<15,XDefaultRootWindow(_display),false,4|8|64,0,1,IntPtr.Zero,IntPtr.Zero);XSync(_display,false);Poll.Start();
                }
            }
        } catch(Exception ex) {AppLog.Error("Native radial input is unavailable.",ex);}
    }
    internal static bool EscapePressed=>OperatingSystem.IsMacOS() ? CGEventSourceKeyState(0,53) : _escape;
    private static int OnXError(IntPtr display,IntPtr error)
    {
        if(display==_display || _forwardingXError)return 0;
        // X11 error handlers are process-wide. Another client may chain back to
        // us; never recursively forward the same native error until stack overflow.
        try{_forwardingXError=true;return _previousXError?.Invoke(display,error) ?? 0;}
        finally{_forwardingXError=false;}
    }
    private static void PumpX11()
    {
        if(_display==IntPtr.Zero)return;
        var memory=Marshal.AllocHGlobal(24*IntPtr.Size);
        try {
            // Drain a bounded batch so metrics/rendering retain UI thread time.
            for(int i=0;i<128 && XPending(_display)>0;i++) {
                XNextEvent(_display,memory);var e=Marshal.PtrToStructure<XButtonEvent>(memory);var point=new PixelPoint(e.RootX,e.RootY);
                if(e.Type==4 && e.Button==2) {
                    _active=Entries.LastOrDefault(entry=>HitSafe(entry,point));
                    XAllowEvents(_display,_active is null ? 2 : 0,UIntPtr.Zero);XFlush(_display);
                    _active?._input(0,point,0);
                } else if(e.Type==6) _active?._input(1,point,0);
                else if(e.Type==4 && _active is {} wheelOwner && wheelOwner.WantsWheel?.Invoke()==true && e.Button is 4 or 5)wheelOwner._input(3,point,e.Button==4 ? 1 : -1);
                else if(e.Type==5 && e.Button==2) {var active=_active;_active=null;active?._input(2,point,0);}
            }
            if(_active is not null) {
                var keys=new byte[32];XQueryKeymap(_display,keys);int escape=XKeysymToKeycode(_display,new UIntPtr(0xff1b));_escape=(keys[escape/8]&(1<<(escape%8)))!=0;
            } else _escape=false;
        } catch(Exception ex) {AppLog.Error("X11 radial input failed.",ex);}
        finally {Marshal.FreeHGlobal(memory);}
    }
    private static bool HitSafe(UnixRadialInput entry,PixelPoint point)
    {
        try{return entry._hit(point);}catch(Exception ex){AppLog.Error("Radial hit test failed.",ex);return false;}
    }
    private static IntPtr OnMacEvent(IntPtr proxy,uint type,IntPtr value,IntPtr context)
    {
        try {
            if(type>=0xFFFFFFFE) {if(_tap!=IntPtr.Zero)CGEventTapEnable(_tap,true);return value;}
            var location=CGEventGetLocation(value);var point=new PixelPoint((int)Math.Round(location.X),(int)Math.Round(location.Y));
            if(type==25 && CGEventGetIntegerValueField(value,3)==2) {
                _active=Entries.LastOrDefault(entry=>HitSafe(entry,point));if(_active is not null){_active._input(0,point,0);return IntPtr.Zero;}
            } else if(_active is {} active) {
                if(type==27 && CGEventGetIntegerValueField(value,3)==2){active._input(1,point,0);return IntPtr.Zero;}
                if(type==26 && CGEventGetIntegerValueField(value,3)==2){_active=null;active._input(2,point,0);return IntPtr.Zero;}
                if(type==22 && active.WantsWheel?.Invoke()==true){active._input(3,point,(int)CGEventGetIntegerValueField(value,11));return IntPtr.Zero;}
            }
        }catch(Exception ex){AppLog.Error("macOS radial input failed.",ex);}
        return value;
    }
    public void Dispose()
    {
        Entries.Remove(this);if(_active==this)_active=null;if(Entries.Count!=0)return;
        Poll.Stop();
        if(_display!=IntPtr.Zero){XUngrabButton(_display,2,1u<<15,XDefaultRootWindow(_display));XCloseDisplay(_display);_display=IntPtr.Zero;}
        if(_tap!=IntPtr.Zero){CGEventTapEnable(_tap,false);CFRunLoopRemoveSource(CFRunLoopGetCurrent(),_source,_mode);CFRelease(_source);CFRelease(_tap);CFRelease(_mode);_tap=_source=_mode=IntPtr.Zero;}
    }
    [StructLayout(LayoutKind.Sequential)] private struct CGPoint{public double X,Y;}
    [StructLayout(LayoutKind.Sequential)] private struct XButtonEvent{public int Type;public UIntPtr Serial;public int SendEvent;public IntPtr Display,Window,Root,Subwindow;public UIntPtr Time;public int X,Y,RootX,RootY;public uint State,Button;public int SameScreen;}
    private delegate IntPtr TapCallback(IntPtr proxy,uint type,IntPtr value,IntPtr context);
    private delegate int XErrorCallback(IntPtr display,IntPtr error);
    private const string CG="/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics",CF="/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",X11="libX11.so.6";
    [DllImport(CG)]private static extern IntPtr CGEventTapCreate(uint tap,uint place,uint options,ulong mask,TapCallback callback,IntPtr context);
    [DllImport(CG)]private static extern void CGEventTapEnable(IntPtr tap,[MarshalAs(UnmanagedType.I1)]bool enabled);
    [DllImport(CG)]private static extern CGPoint CGEventGetLocation(IntPtr value);
    [DllImport(CG)]private static extern long CGEventGetIntegerValueField(IntPtr value,uint field);
    [DllImport(CG)][return:MarshalAs(UnmanagedType.I1)]private static extern bool CGEventSourceKeyState(int state,ushort key);
    [DllImport(CF)]private static extern IntPtr CFStringCreateWithCString(IntPtr allocator,string value,uint encoding);
    [DllImport(CF)]private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator,IntPtr port,nint order);
    [DllImport(CF)]private static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CF)]private static extern void CFRunLoopAddSource(IntPtr loop,IntPtr source,IntPtr mode);
    [DllImport(CF)]private static extern void CFRunLoopRemoveSource(IntPtr loop,IntPtr source,IntPtr mode);
    [DllImport(CF)]private static extern void CFRelease(IntPtr value);
    [DllImport(X11)]private static extern int XInitThreads();
    [DllImport(X11)]private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)]private static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)]private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)]private static extern int XGrabButton(IntPtr display,uint button,uint modifiers,IntPtr window,bool ownerEvents,uint mask,int pointerMode,int keyboardMode,IntPtr confine,IntPtr cursor);
    [DllImport(X11)]private static extern int XUngrabButton(IntPtr display,uint button,uint modifiers,IntPtr window);
    [DllImport(X11)]private static extern int XAllowEvents(IntPtr display,int mode,UIntPtr time);
    [DllImport(X11)]private static extern int XFlush(IntPtr display);
    [DllImport(X11)]private static extern int XSync(IntPtr display,bool discard);
    [DllImport(X11)]private static extern int XPending(IntPtr display);
    [DllImport(X11)]private static extern int XNextEvent(IntPtr display,IntPtr value);
    [DllImport(X11)]private static extern IntPtr XSetErrorHandler(XErrorCallback callback);
    [DllImport(X11)]private static extern int XQueryKeymap(IntPtr display,byte[] keys);
    [DllImport(X11)]private static extern byte XKeysymToKeycode(IntPtr display,UIntPtr sym);
}
