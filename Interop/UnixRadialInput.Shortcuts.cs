using Avalonia.Threading;
namespace EndfieldChargePlus.Interop;

internal sealed partial class UnixRadialInput
{
    private static readonly Dictionary<string,(HudHotkeyGesture Chord,Action Pressed)> MouseShortcuts=new();
    private static int _lastShortcutGrabError;
    private static uint XModifiers(uint state)=>((state&4)!=0?2u:0)|((state&8)!=0?1u:0)|((state&1)!=0?4u:0)|((state&64)!=0?8u:0);
    internal static bool ClaimsMouse(uint key)
    {
        if(MouseShortcuts.Count==0)return false;
        uint mods=0;
        if(OperatingSystem.IsMacOS()){ulong f=CGEventSourceFlagsState(0);mods=((f&(1UL<<18))!=0?2u:0)|((f&(1UL<<19))!=0?1u:0)|((f&(1UL<<17))!=0?4u:0)|((f&(1UL<<20))!=0?8u:0);}
        else if(_display!=IntPtr.Zero && XQueryPointer(_display,XDefaultRootWindow(_display),out _,out _,out _,out _,out _,out _,out uint state))mods=XModifiers(state);
        return MouseShortcuts.Values.Any(x=>x.Chord.VirtualKey==key && x.Chord.Modifiers==mods);
    }
    [System.Runtime.InteropServices.DllImport(CG)]private static extern ulong CGEventSourceFlagsState(int state);
    [System.Runtime.InteropServices.DllImport(X11)]private static extern bool XQueryPointer(IntPtr display,IntPtr window,out IntPtr root,out IntPtr child,out int rootX,out int rootY,out int x,out int y,out uint state);
    private static uint XButton(HudHotkeyGesture chord)=>chord.MouseButton switch{1=>1,2=>3,3=>2,4=>8,5=>9,_=>0};
    internal static IDisposable? TryBindMouse(HudHotkeyGesture chord,Action pressed)
    {
        if(!chord.IsMouse || MouseShortcuts.ContainsKey(chord.Text))return null;
        var owner=new UnixRadialInput(_=>false,(_,_,_)=>{});
        if(OperatingSystem.IsMacOS() ? _tap==IntPtr.Zero : _display==IntPtr.Zero){owner.Dispose();return null;}
        if(OperatingSystem.IsLinux() && XButton(chord)!=2)
        {
            uint mods=((chord.Modifiers&2)!=0?4u:0)|((chord.Modifiers&1)!=0?8u:0)|((chord.Modifiers&4)!=0?1u:0)|((chord.Modifiers&8)!=0?64u:0);
            _lastShortcutGrabError=0;
            foreach(uint locks in new uint[]{0,2,16,18})XGrabButton(_display,XButton(chord),mods|locks,XDefaultRootWindow(_display),false,4|8,0,1,IntPtr.Zero,IntPtr.Zero);
            XSync(_display,false);
            if(_lastShortcutGrabError!=0){foreach(uint locks in new uint[]{0,2,16,18})XUngrabButton(_display,XButton(chord),mods|locks,XDefaultRootWindow(_display));owner.Dispose();return null;}
        }
        MouseShortcuts[chord.Text]=(chord,pressed);return new MouseLease(chord,owner);
    }
    private static bool InvokeMouseShortcut(uint button,uint modifiers)
    {
        foreach(var entry in MouseShortcuts.Values)
            if(XButton(entry.Chord)==button && entry.Chord.Modifiers==modifiers)
            {
                string text=entry.Chord.Text;
                Dispatcher.UIThread.Post(()=>{if(MouseShortcuts.TryGetValue(text,out var active))active.Pressed();},DispatcherPriority.Input);
                return true;
            }
        return false;
    }
    private sealed class MouseLease(HudHotkeyGesture chord,UnixRadialInput owner):IDisposable
    {
        private bool _disposed;
        public void Dispose(){
            if(_disposed)return;_disposed=true;MouseShortcuts.Remove(chord.Text);
            if(_display!=IntPtr.Zero && XButton(chord)!=2){uint mods=((chord.Modifiers&2)!=0?4u:0)|((chord.Modifiers&1)!=0?8u:0)|((chord.Modifiers&4)!=0?1u:0)|((chord.Modifiers&8)!=0?64u:0);foreach(uint locks in new uint[]{0,2,16,18})XUngrabButton(_display,XButton(chord),mods|locks,XDefaultRootWindow(_display));XFlush(_display);}
            owner.Dispose();
        }
    }
}
