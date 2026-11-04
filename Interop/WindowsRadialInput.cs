using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus.Interop;

// WH_MOUSE_LL must never run on, wait for, or call into the UI thread. A stalled
// compositor otherwise stalls system mouse delivery too. Publish immutable hit
// regions from the UI; the hook only reads them and posts coalesced work.
internal sealed class WindowsRadialInput : IDisposable
{
    private static HookHost? _host;
    private readonly HookHost _owner;
    private readonly Action<int,PixelPoint,int> _input;
    private HitRegion _region = new(default, false, false, 0, false);
    private Move? _move;
    private int _movePosted, _wheelPosted, _wheel, _cyclePosted, _cycleDelta;
    private volatile bool _disposed;
    private sealed record HitRegion(PixelRect Bounds, bool Enabled, bool Wheel, long At, bool CycleWheel);
    private sealed record Move(PixelPoint Point);

    internal WindowsRadialInput(Action<int,PixelPoint,int> input)
    {
        Dispatcher.UIThread.VerifyAccess();
        _input=input;_owner=_host ??= new HookHost();_owner.Add(this);
    }
    internal void UpdateTarget(PixelRect bounds, bool enabled, bool wheel, bool cycleWheel=false)
    {
        Dispatcher.UIThread.VerifyAccess();
        Volatile.Write(ref _region,new(bounds,enabled,wheel,Stopwatch.GetTimestamp(),cycleWheel));
    }
    private bool Hit(PixelPoint point,bool cycle=false)
    {
        var region=Volatile.Read(ref _region);
        // Fail open when the UI stops publishing its heartbeat. Never claim
        // new gestures on stale HUD geometry during a render failure.
        if(_disposed || !(cycle?region.CycleWheel:region.Enabled) || Stopwatch.GetElapsedTime(region.At).TotalMilliseconds>500 ||
            region.Bounds.Width<2 || region.Bounds.Height<2 || !region.Bounds.Contains(point))return false;
        double radius=Math.Min(region.Bounds.Width,region.Bounds.Height)/2d;
        double x=point.X-region.Bounds.X,y=point.Y-region.Bounds.Y;
        double dx=x-Math.Clamp(x,radius,region.Bounds.Width-radius),dy=y-region.Bounds.Height/2d;
        return dx*dx+dy*dy<=radius*radius;
    }
    private void Deliver(int kind,PixelPoint point,int wheel=0)
    {
        if(_disposed)return;
        if(kind==1)
        {
            Volatile.Write(ref _move,new(point));
            if(Interlocked.Exchange(ref _movePosted,1)!=0)return;
            Dispatcher.UIThread.Post(()=>{
                Interlocked.Exchange(ref _movePosted,0);var latest=Volatile.Read(ref _move);
                if(latest is not null)Invoke(1,latest.Point,0);
            },DispatcherPriority.Input);
        }
        else if(kind==4)
        {
            Interlocked.Add(ref _cycleDelta,wheel);
            if(Interlocked.Exchange(ref _cyclePosted,1)!=0)return;
            Dispatcher.UIThread.Post(()=>{
                Interlocked.Exchange(ref _cyclePosted,0);int delta=Interlocked.Exchange(ref _cycleDelta,0);
                if(delta!=0)Invoke(4,point,delta);
            },DispatcherPriority.Input);
        }
        else if(kind==3)
        {
            Interlocked.Add(ref _wheel,wheel);
            if(Interlocked.Exchange(ref _wheelPosted,1)!=0)return;
            Dispatcher.UIThread.Post(()=>{
                Interlocked.Exchange(ref _wheelPosted,0);int delta=Interlocked.Exchange(ref _wheel,0);
                if(delta!=0)Invoke(3,point,delta);
            },DispatcherPriority.Input);
        }
        else Dispatcher.UIThread.Post(()=>Invoke(kind,point,0),DispatcherPriority.Input);
    }
    private void Invoke(int kind,PixelPoint point,int wheel)
    {
        if(_disposed)return;
        try{_input(kind,point,wheel);}
        catch(Exception ex){AppLog.Error("HUD gesture delivery failed.",ex);}
    }
    // Tests exercise the same dispatcher on a private desktop, without global input injection.
    internal static bool ProcessMessage(int message,PixelPoint point,int wheel)=>_host?.ProcessMessage(message,point,wheel)??false;
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if(_disposed)return;_disposed=true;_owner.Remove(this);
        if(_owner.IsEmpty && ReferenceEquals(_host,_owner)){_host=null;_owner.Stop();}
    }
    private sealed class HookHost
    {
        private WindowsRadialInput[] _entries=Array.Empty<WindowsRadialInput>();
        private WindowsRadialInput? _active;
        private readonly HookProc _callback;
        private readonly Thread _thread;
        private IntPtr _hook;
        private uint _threadId;
        private volatile bool _stopping;
        internal bool IsEmpty=>Volatile.Read(ref _entries).Length==0;
        internal HookHost()
        {
            _callback=Callback;_thread=new Thread(Run){IsBackground=true,Name="ECP mouse input"};_thread.Start();
        }
        internal void Add(WindowsRadialInput input)=>Volatile.Write(ref _entries,Volatile.Read(ref _entries).Append(input).ToArray());
        internal void Remove(WindowsRadialInput input)=>Volatile.Write(ref _entries,Volatile.Read(ref _entries).Where(e=>e!=input).ToArray());
        internal void Stop(){_stopping=true;uint id=Volatile.Read(ref _threadId);if(id!=0)PostThreadMessage(id,0x12,IntPtr.Zero,IntPtr.Zero);}
        private void Run()
        {
            try
            {
                // Create the native message queue before publishing its ID.
                PeekMessage(out _,IntPtr.Zero,0,0,0);Volatile.Write(ref _threadId,GetCurrentThreadId());
                if(_stopping)return;
                _hook=SetWindowsHookEx(14,_callback,GetModuleHandle(null),0);
                if(_hook==IntPtr.Zero)
                {
                    int error=Marshal.GetLastPInvokeError();
                    Dispatcher.UIThread.Post(()=>AppLog.Error("Unable to register middle-button HUD gesture.",new System.ComponentModel.Win32Exception(error)));return;
                }
                while(!_stopping && GetMessage(out var message,IntPtr.Zero,0,0)>0){TranslateMessage(ref message);DispatchMessage(ref message);}
            }
            finally
            {
                if(_hook!=IntPtr.Zero){UnhookWindowsHookEx(_hook);_hook=IntPtr.Zero;}GC.KeepAlive(_callback);
            }
        }
        private IntPtr Callback(int code,IntPtr message,IntPtr data)
        {
            if(code>=0 && !_stopping)
            {
                int msg=unchecked((int)message.ToInt64());
                if(_active is null && msg is not (0x207 or 0x20A))return CallNextHookEx(_hook,code,message,data);
                // Unrelated buttons never call UI code, marshal data or allocate.
                if(msg is 0x207 or 0x208 or 0x200 or 0x20A)
                {
                    try
                    {
                        var value=Marshal.PtrToStructure<MouseData>(data);
                        if(ProcessMessage(msg,new(value.X,value.Y),(short)(value.MouseDataValue>>16)))return new(1);
                    }
                    catch { /* Never throw or log synchronously through a system hook. */ }
                }
            }
            return CallNextHookEx(_hook,code,message,data);
        }
        internal bool ProcessMessage(int msg,PixelPoint point,int wheel)
        {
            if(_stopping)return false;
            if(msg==0x207 && WindowsObservedShortcut.Matches(4,WindowsObservedShortcut.CurrentModifiers))return false;
            if(msg==0x20A && _active is null)
            {
                var entries=Volatile.Read(ref _entries);
                for(int i=entries.Length-1;i>=0;i--)if(entries[i].Hit(point,true))
                {entries[i].Deliver(4,point,wheel);return true;}
            }
            if(msg==0x207 && _active is null)
            {
                var entries=Volatile.Read(ref _entries);
                for(int i=entries.Length-1;i>=0;i--)if(entries[i].Hit(point))
                {_active=entries[i];_active.Deliver(0,point);return true;}
            }
            else if(_active is {} active)
            {
                // Consume the matching up even if the HUD closed in between.
                if(msg==0x208){_active=null;active.Deliver(2,point);return true;}
                if(active._disposed)return false;
                if(msg==0x200)active.Deliver(1,point);
                else if(msg==0x20A && Volatile.Read(ref active._region).Wheel){active.Deliver(3,point,wheel);return true;}
            }
            return false;
        }
    }
    internal static bool EscapePressed=>OperatingSystem.IsWindows() && (GetAsyncKeyState(0x1B)&0x8000)!=0;
    internal static IntPtr Foreground=>OperatingSystem.IsWindows()?GetForegroundWindow():IntPtr.Zero;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData {public int X,Y;public uint MouseDataValue,Flags,Time;public UIntPtr ExtraInfo;}
    [StructLayout(LayoutKind.Sequential)] private struct Message {public IntPtr Hwnd;public uint Id;public UIntPtr WParam;public IntPtr LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int type,HookProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread,uint message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message,IntPtr hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message,IntPtr hwnd,uint min,uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
}
