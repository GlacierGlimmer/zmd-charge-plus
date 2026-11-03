using System.Runtime.InteropServices;
using Avalonia;
namespace EndfieldChargePlus.Interop;

/// <summary>The animated host stays fully mouse-through. A tiny owned capsule alone receives clicks.</summary>
internal sealed class WindowsHudInteraction : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProc(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
    private static readonly WindowProc ProcedureRoot=Procedure;
    private static readonly Dictionary<IntPtr,WindowsHudInteraction> Inputs=new();
    private static readonly string ClassName="ECP.HudInput.1";
    private static ushort _class;
    private readonly IntPtr _owner;
    private readonly Func<PixelPoint,bool> _contains;
    private readonly Func<PixelRect> _bounds;
    private readonly Action _clicked;
    private readonly Action<int>? _scrolled;
    private IntPtr _input,_parent;
    private bool _enabled,_pressed,_disposed;
    private PixelRect _lastBounds;
    private bool _lastTopmost;
    internal IntPtr InputHandle => _input;
    internal WindowsHudInteraction(IntPtr owner,Func<PixelPoint,bool> contains,Action clicked,Func<PixelRect>? bounds=null,Action<int>? scrolled=null)
    {
        _owner=owner;_contains=contains;_clicked=clicked;_scrolled=scrolled;
        _bounds=bounds ?? (()=> { GetWindowRect(owner,out var r);return new(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top); });
    }
    internal void SetEnabled(bool enabled)
    {
        if(_disposed) return;
        if(_enabled!=enabled) CancelPress();
        _enabled=enabled;
        Refresh();
    }
    internal void Refresh()
    {
        if(_disposed) return;
        if(!_enabled || !IsWindowVisible(_owner)) { CancelPress();if(_input!=IntPtr.Zero) ShowWindow(_input,0);return; }
        PixelRect bounds=_bounds();
        if(bounds.Width<=1 || bounds.Height<=1) { CancelPress();if(_input!=IntPtr.Zero) ShowWindow(_input,0);return; }
        bool child=(Style(_owner,-16)&0x40000000)!=0;
        IntPtr parent=child ? GetParent(_owner) : IntPtr.Zero;
        if(_input!=IntPtr.Zero && _parent!=parent) DestroyInput();
        if(_input==IntPtr.Zero)
        {
            if(_class==0) {
                var wc=new WindowClass { Size=(uint)Marshal.SizeOf<WindowClass>(),Instance=GetModuleHandle(null),
                    Procedure=Marshal.GetFunctionPointerForDelegate(ProcedureRoot),ClassName=ClassName,Brush=GetStockObject(4) };
                _class=RegisterClassEx(ref wc);
                if(_class==0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(),"Register HUD input class failed size="+wc.Size);
            }
            _parent=parent;
            _input=CreateWindowEx(0x08080080,ClassName,"",child ? 0x40000000u : 0x80000000u,0,0,1,1,child ? parent : _owner,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
            if(_input==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(),"Create HUD input failed owner="+_owner+", class="+_class+", parent="+parent);
            Inputs[_input]=this;
            // Alpha 0 would also make the capsule mouse-through; 1 is visually imperceptible under the HUD.
            SetLayeredWindowAttributes(_input,0,1,2);
            _lastBounds=default;
        }
        bool topmost=(Style(_owner,-20)&8)!=0;
        if(bounds==_lastBounds && topmost==_lastTopmost && IsWindowVisible(_input)) return;
        if(bounds.Width!=_lastBounds.Width || bounds.Height!=_lastBounds.Height) {
            var region=CreateRoundRectRgn(0,0,bounds.Width+1,bounds.Height+1,bounds.Height,bounds.Height);
            if(region!=IntPtr.Zero && SetWindowRgn(_input,region,false)==0) DeleteObject(region);
        }
        var origin=new PixelPoint(bounds.X,bounds.Y);
        if(child) ScreenToClient(parent,ref origin);
        if(!child) SetWindowPos(_input,topmost ? new(-1) : new(-2),0,0,0,0,0x13|0x200);
        // Immediately below the visual owner. The host itself remains transparent at every stage.
        SetWindowPos(_input,child ? IntPtr.Zero : _owner,origin.X,origin.Y,bounds.Width,bounds.Height,0x10|0x40|0x200);
        _lastBounds=bounds;_lastTopmost=topmost;
    }
    private static long Style(IntPtr hwnd,int index) => IntPtr.Size==8 ? GetWindowLongPtr(hwnd,index).ToInt64() : GetWindowLong(hwnd,index);
    private bool Inside(IntPtr point)
    {
        long value=point.ToInt64();var p=new PixelPoint(unchecked((short)value),unchecked((short)(value>>16)));
        return ClientToScreen(_input,ref p) && _contains(p);
    }
    private void CancelPress() { _pressed=false;if(_input!=IntPtr.Zero && GetCapture()==_input) ReleaseCapture(); }
    private static IntPtr Procedure(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam)
    {
        if(Inputs.TryGetValue(hwnd,out var owner)) {
            try {
                if(message==0x21) return new(3); // MA_NOACTIVATE.
                if(message==0x84) return new(1);
                if(message==0x20A && owner._enabled)
                {
                    long value=lparam.ToInt64();var point=new PixelPoint(unchecked((short)value),unchecked((short)(value>>16)));
                    if(owner._contains(point)){owner._scrolled?.Invoke(unchecked((short)(wparam.ToInt64()>>16)));return IntPtr.Zero;}
                }
                if(message==0x201) { owner._pressed=owner._enabled && owner.Inside(lparam);if(owner._pressed) SetCapture(hwnd);return IntPtr.Zero; }
                if(message==0x202) {
                    bool clicked=owner._enabled && owner._pressed && owner.Inside(lparam);
                    owner.CancelPress();if(clicked) owner._clicked();return IntPtr.Zero;
                }
                if(message is 0x215 or 0x1F or 0x82) owner.CancelPress();
            } catch(Exception ex) { owner.CancelPress();EndfieldChargePlus.Diagnostics.AppLog.Error("HUD capsule input failed.",ex); }
        }
        return DefWindowProc(hwnd,message,wparam,lparam);
    }
    private void DestroyInput()
    {
        CancelPress();
        if(_input!=IntPtr.Zero) { var h=_input;DestroyWindow(h);Inputs.Remove(h);_input=IntPtr.Zero; }
    }
    public void Dispose() { if(_disposed) return;_enabled=false;DestroyInput();_disposed=true;GC.KeepAlive(ProcedureRoot); }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct WindowClass {
        public uint Size,Style;public IntPtr Procedure;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Brush;
        public string? MenuName,ClassName;public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern ushort RegisterClassEx(ref WindowClass c);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l,int t,int r,int b,int ellipseW,int ellipseH);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr h,IntPtr r,bool redraw);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h,uint key,byte alpha,uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h,IntPtr insert,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr h,ref PixelPoint p);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h,ref PixelPoint p);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h,int i);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr h,int i);
}
