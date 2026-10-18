using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace EndfieldChargePlus.Interop;

internal sealed class DesktopGlobalHotkey : IDisposable
{
    private readonly WindowsGlobalHotkey? _windows;
    private readonly UnixHotkey? _unix;
    internal string Gesture=>_windows?.Gesture ?? _unix?.Gesture ?? "";
    internal int LastError=>_windows?.LastError ?? _unix?.LastError ?? -1;
    internal DesktopGlobalHotkey(WindowsGlobalHotkey service)=>_windows=service;
    internal DesktopGlobalHotkey(Action pressed)
    {
        if(OperatingSystem.IsWindows())_windows=new WindowsGlobalHotkey(pressed);
        else _unix=new UnixHotkey(pressed);
    }
    internal bool TryUpdate(string? text)=>_windows?.TryUpdate(text) ?? _unix?.TryUpdate(text) ?? false;
    public void Dispose(){_windows?.Dispose();_unix?.Dispose();}

    private sealed class UnixHotkey : IDisposable
    {
        private const string X11="libX11.so.6",Carbon="/System/Library/Frameworks/Carbon.framework/Carbon";
        private readonly Action _pressed;
        private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(20)};
        private readonly CarbonHandler _carbonHandler;
        private IntPtr _display,_carbonRef,_eventHandler;
        private ulong _root;
        private int _key;
        private uint _mods,_id;
        private bool _down,_disposed;
        internal string Gesture {get;private set;}="";
        internal int LastError {get;private set;}
        internal UnixHotkey(Action pressed)
        {
            _pressed=pressed;_carbonHandler=CarbonEvent;
            if(OperatingSystem.IsLinux())
            {
                // X11 grabs are scoped to X11. Wayland compositors own global bindings.
                if(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")=="wayland")return;
                _display=XOpenDisplay(IntPtr.Zero);
                if(_display!=IntPtr.Zero){_root=XDefaultRootWindow(_display);XkbSetDetectableAutoRepeat(_display,true,out _);_timer.Tick+=(_,_)=>Poll();_timer.Start();}
            }
            else if(OperatingSystem.IsMacOS())
            {
                var types=new[]{new EventType(0x6b657962,6),new EventType(0x6b657962,9)};
                LastError=InstallEventHandler(GetApplicationEventTarget(),_carbonHandler,2,types,IntPtr.Zero,out _eventHandler);
            }
        }
        internal bool TryUpdate(string? text)
        {
            if(_disposed)return false;
            text=text?.Trim() ?? "";LastError=0;
            if(text.Length==0){Clear();return true;}
            if(!HudHotkeyGesture.TryParse(text,out var chord))return false;
            if(chord.Text==Gesture)return true;
            if(OperatingSystem.IsMacOS())
            {
                if(_eventHandler==IntPtr.Zero || !MacKeys.TryGetValue(chord.VirtualKey,out uint key)){LastError=-2;return false;}
                uint mods=((chord.Modifiers&2)!=0 ? 4096u : 0)|((chord.Modifiers&1)!=0 ? 2048u : 0)|((chord.Modifiers&4)!=0 ? 512u : 0);
                uint id=_id+1;
                LastError=RegisterEventHotKey(key,mods,new HotKeyId(0x45435048,id),GetApplicationEventTarget(),0,out var next);
                if(LastError!=0)return false;
                Clear();_carbonRef=next;_id=id;Gesture=chord.Text;return true;
            }
            if(_display==IntPtr.Zero){LastError=-3;return false;}
            string name=chord.VirtualKey is >= 0x70 and <=0x87 ? "F"+(chord.VirtualKey-0x70+1) : ((char)chord.VirtualKey).ToString().ToLowerInvariant();
            int nextKey=XKeysymToKeycode(_display,XStringToKeysym(name));
            uint nextMods=((chord.Modifiers&2)!=0 ? 4u : 0)|((chord.Modifiers&1)!=0 ? 8u : 0)|((chord.Modifiers&4)!=0 ? 1u : 0);
            if(nextKey==0){LastError=-2;return false;}
            int error=0;IntPtr previous=IntPtr.Zero;
            XErrorHandler handler=(display,evt)=>{
                if(display==_display){error=Marshal.ReadByte(evt,32);return 0;}
                return previous==IntPtr.Zero ? 0 : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previous)(display,evt);
            };
            // X errors are asynchronous: force receipt before deciding whether to replace.
            previous=XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(handler));
            try
            {
                foreach(uint locks in LockMasks)XGrabKey(_display,nextKey,nextMods|locks,_root,false,1,1);
                XSync(_display,false);
                if(error!=0){foreach(uint locks in LockMasks)XUngrabKey(_display,nextKey,nextMods|locks,_root);XSync(_display,false);LastError=error;return false;}
                Clear();_key=nextKey;_mods=nextMods;Gesture=chord.Text;XFlush(_display);return true;
            }
            finally {XSetErrorHandler(previous);GC.KeepAlive(handler);}
        }
        private static readonly uint[] LockMasks={0,2,16,18};
        private void Poll()
        {
            if(_disposed || _display==IntPtr.Zero)return;
            while(XPending(_display)>0)
            {
                XNextEvent(_display,out var e);
                if(e.KeyCode!=_key || (e.State&~18u)!=_mods)continue;
                if(e.Type==3){_down=false;continue;}
                if(e.Type==2 && !_down){_down=true;_pressed();}
            }
        }
        private int CarbonEvent(IntPtr call,IntPtr evt,IntPtr data)
        {
            if(_disposed || GetEventParameter(evt,0x2d2d2d2d,0x686b6964,IntPtr.Zero,8,IntPtr.Zero,out var id)!=0 || id.Signature!=0x45435048 || id.Id!=_id)return -9874;
            if(GetEventKind(evt)==9)_down=false;
            else if(!_down){_down=true;Dispatcher.UIThread.Post(()=>{if(!_disposed)_pressed();});}
            return 0;
        }
        private void Clear()
        {
            if(_carbonRef!=IntPtr.Zero){UnregisterEventHotKey(_carbonRef);_carbonRef=IntPtr.Zero;}
            if(_display!=IntPtr.Zero && _key!=0)foreach(uint locks in LockMasks)XUngrabKey(_display,_key,_mods|locks,_root);
            _key=0;_down=false;Gesture="";
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;_timer.Stop();Clear();
            if(_eventHandler!=IntPtr.Zero)RemoveEventHandler(_eventHandler);
            if(_display!=IntPtr.Zero)XCloseDisplay(_display);
            _display=_eventHandler=IntPtr.Zero;
        }
        private static readonly Dictionary<uint,uint> MacKeys=BuildMacKeys();
        private static Dictionary<uint,uint> BuildMacKeys()
        {
            var keys=new Dictionary<uint,uint>();
            uint[] letters={0,11,8,2,14,3,5,4,34,38,40,37,46,45,31,35,12,15,1,17,32,9,13,7,16,6};
            for(uint i=0;i<26;i++)keys['A'+i]=letters[i];
            uint[] digits={29,18,19,20,21,23,22,26,28,25};for(uint i=0;i<10;i++)keys['0'+i]=digits[i];
            uint[] functions={122,120,99,118,96,97,98,100,101,109,103,111,105,107,113,106,64,79,80,90};
            for(uint i=0;i<functions.Length;i++)keys[0x70+i]=functions[i];return keys;
        }
        [StructLayout(LayoutKind.Sequential)] private readonly record struct EventType(uint Class,uint Kind);
        [StructLayout(LayoutKind.Sequential)] private readonly record struct HotKeyId(uint Signature,uint Id);
        [StructLayout(LayoutKind.Sequential,Size=192)] private struct XEvent
        {
            public int Type;public ulong Serial;public int SendEvent;public IntPtr Display;public ulong Window,Root,Subwindow,Time;
            public int X,Y,XRoot,YRoot;public uint State,KeyCode;public int SameScreen;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int XErrorHandler(IntPtr display,IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CarbonHandler(IntPtr call,IntPtr evt,IntPtr data);
        [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(X11)] private static extern ulong XDefaultRootWindow(IntPtr display);
        [DllImport(X11)] private static extern ulong XStringToKeysym(string name);
        [DllImport(X11)] private static extern byte XKeysymToKeycode(IntPtr display,ulong symbol);
        [DllImport(X11)] private static extern int XGrabKey(IntPtr display,int key,uint modifiers,ulong window,[MarshalAs(UnmanagedType.Bool)]bool owner,int pointerMode,int keyboardMode);
        [DllImport(X11)] private static extern int XUngrabKey(IntPtr display,int key,uint modifiers,ulong window);
        [DllImport(X11)] private static extern int XPending(IntPtr display);
        [DllImport(X11)] private static extern int XNextEvent(IntPtr display,out XEvent evt);
        [DllImport(X11)] private static extern int XSync(IntPtr display,[MarshalAs(UnmanagedType.Bool)]bool discard);
        [DllImport(X11)] private static extern IntPtr XSetErrorHandler(IntPtr callback);
        [DllImport(X11)] private static extern int XFlush(IntPtr display);
        [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
        [DllImport(X11)] private static extern int XkbSetDetectableAutoRepeat(IntPtr display,[MarshalAs(UnmanagedType.Bool)]bool detectable,[MarshalAs(UnmanagedType.Bool)]out bool supported);
        [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
        [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target,CarbonHandler callback,uint count,[In] EventType[] types,IntPtr data,out IntPtr handler);
        [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint key,uint modifiers,HotKeyId id,IntPtr target,uint options,out IntPtr reference);
        [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr reference);
        [DllImport(Carbon)] private static extern int RemoveEventHandler(IntPtr reference);
        [DllImport(Carbon)] private static extern uint GetEventKind(IntPtr evt);
        [DllImport(Carbon)] private static extern int GetEventParameter(IntPtr evt,uint name,uint type,IntPtr actualType,uint size,IntPtr actualSize,out HotKeyId value);
    }
}
