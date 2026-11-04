using System.Runtime.InteropServices;
using Avalonia.Threading;
namespace EndfieldChargePlus.Interop;

// Only mouse/F12/modifier bindings use hooks. One dedicated message thread keeps
// callbacks bounded and independent of the UI; callbacks never suppress input.
internal sealed class WindowsObservedShortcut:IDisposable
{
    private static Host? _host;
    private readonly Host _owner;private readonly Action _pressed;
    internal readonly HudHotkeyGesture Chord;private volatile bool _disposed;private int _posted;private bool _down;
    internal bool Available=>_owner.Available;
    internal WindowsObservedShortcut(HudHotkeyGesture chord,Action pressed){Chord=chord;_pressed=pressed;_owner=_host??=new Host();_owner.Add(this);}
    private void Process(uint key,bool down,uint mods){
        if(_disposed || key!=Chord.VirtualKey)return;if(!down){_down=false;return;}if(_down)return;_down=true;
        if(key is 160 or 161)mods&=~4u;if(key is 162 or 163)mods&=~2u;if(key is 164 or 165)mods&=~1u;if(key is 91 or 92)mods&=~8u;
        if(mods!=Chord.Modifiers || Interlocked.Exchange(ref _posted,1)!=0)return;
        Dispatcher.UIThread.Post(()=>{Interlocked.Exchange(ref _posted,0);if(!_disposed)_pressed();},DispatcherPriority.Input);
    }
    internal static void ProcessForTest(uint key,bool down,uint modifiers){foreach(var e in _host?.Entries??Array.Empty<WindowsObservedShortcut>())e.Process(key,down,modifiers);}
    internal static uint CurrentModifiers=>(Down(0x11)?2u:0)|(Down(0x12)?1u:0)|(Down(0x10)?4u:0)|(Down(0x5B)||Down(0x5C)?8u:0);
    internal static bool Matches(uint key,uint mods)=>(_host?.Entries??Array.Empty<WindowsObservedShortcut>()).Any(e=>!e._disposed && e.Chord.VirtualKey==key && e.Chord.Modifiers==mods);
    private static bool Down(int key)=>(GetAsyncKeyState(key)&0x8000)!=0;
    public void Dispose(){if(_disposed)return;_disposed=true;_owner.Remove(this);if(_owner.Entries.Length==0 && ReferenceEquals(_host,_owner)){_host=null;_owner.Stop();}}
    private sealed class Host{
        private WindowsObservedShortcut[] _entries=Array.Empty<WindowsObservedShortcut>();internal WindowsObservedShortcut[] Entries=>Volatile.Read(ref _entries);
        private readonly Hook _callback;private uint _threadId;private IntPtr _mouse,_keyboard;private volatile bool _stop;internal bool Available{get;private set;}
        internal Host(){_callback=Callback;var ready=new ManualResetEventSlim();new Thread(()=>{
            _threadId=GetCurrentThreadId();PeekMessage(out _,IntPtr.Zero,0,0,0);
            _mouse=SetWindowsHookEx(14,_callback,GetModuleHandle(null),0);_keyboard=SetWindowsHookEx(13,_callback,GetModuleHandle(null),0);Available=_mouse!=IntPtr.Zero && _keyboard!=IntPtr.Zero;ready.Set();
            try{while(!_stop && GetMessage(out var m,IntPtr.Zero,0,0)>0){TranslateMessage(ref m);DispatchMessage(ref m);}}
            finally{if(_mouse!=IntPtr.Zero)UnhookWindowsHookEx(_mouse);if(_keyboard!=IntPtr.Zero)UnhookWindowsHookEx(_keyboard);}
        }){IsBackground=true,Name="ECP shortcut input"}.Start();ready.Wait(TimeSpan.FromSeconds(2));}
        internal void Add(WindowsObservedShortcut e)=>Volatile.Write(ref _entries,Entries.Append(e).ToArray());internal void Remove(WindowsObservedShortcut e)=>Volatile.Write(ref _entries,Entries.Where(x=>x!=e).ToArray());
        internal void Stop(){_stop=true;if(_threadId!=0)PostThreadMessage(_threadId,0x12,IntPtr.Zero,IntPtr.Zero);}
        private IntPtr Callback(int code,IntPtr message,IntPtr data){
            if(code>=0 && !_stop){uint msg=(uint)message.ToInt64(),key=0;bool down=false;
                if(msg is 0x100 or 0x101 or 0x104 or 0x105){key=(uint)Marshal.ReadInt32(data);down=msg is 0x100 or 0x104;}
                else{key=msg switch{0x201 or 0x202=>1u,0x204 or 0x205=>2u,0x207 or 0x208=>4u,0x20B or 0x20C=>(uint)((Marshal.ReadInt32(data,8)>>16)==1?5:6),_=>0};down=msg is 0x201 or 0x204 or 0x207 or 0x20B;}
                if(key!=0){uint mods=CurrentModifiers;foreach(var entry in Entries)entry.Process(key,down,mods);}}
            return CallNextHookEx(IntPtr.Zero,code,message,data);
        }
    }
    private delegate IntPtr Hook(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)]private struct Message{public IntPtr Window;public uint Id;public UIntPtr WParam;public IntPtr LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("user32.dll",EntryPoint="SetWindowsHookExW",SetLastError=true)]private static extern IntPtr SetWindowsHookEx(int id,Hook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",EntryPoint="GetMessageW")]private static extern int GetMessage(out Message message,IntPtr window,uint min,uint max);
    [DllImport("user32.dll",EntryPoint="PeekMessageW")]private static extern bool PeekMessage(out Message message,IntPtr window,uint min,uint max,uint flags);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll",EntryPoint="DispatchMessageW")]private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll",EntryPoint="PostThreadMessageW")]private static extern bool PostThreadMessage(uint thread,uint message,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll",EntryPoint="GetModuleHandleW")]private static extern IntPtr GetModuleHandle(string? name);
}
