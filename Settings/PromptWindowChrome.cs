using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace EndfieldChargePlus.Settings;

internal static class PromptWindowChrome
{
    internal static void Attach(Window window,Control title,Control close)
    {
        if(OperatingSystem.IsWindows())
        {
            Win32Properties.AddWindowStylesCallback(window,(style,extended)=>
                ((style&~0x00050000u)|0x00080000u|(window.CanResize?0x00050000u:0u),extended));
            Win32Properties.AddWndProcHookCallback(window,HitTest);
            window.Closed+=(_,_)=>Win32Properties.RemoveWndProcHookCallback(window,HitTest);
        }
        else if(OperatingSystem.IsMacOS())
        {
            window.SystemDecorations=SystemDecorations.Full;
            window.ExtendClientAreaToDecorationsHint=true;
            window.ExtendClientAreaChromeHints=Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            window.ExtendClientAreaTitleBarHeightHint=0;
        }
        IntPtr HitTest(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(message!=0x84 || !window.IsVisible)return IntPtr.Zero;
            long packed=lParam.ToInt64();var point=window.PointToClient(new PixelPoint(unchecked((short)packed),unchecked((short)(packed>>16))));
            if(window.CanResize && window.WindowState==WindowState.Normal)
            {
                bool left=point.X<=7,right=point.X>=window.Bounds.Width-7,top=point.Y<=7,bottom=point.Y>=window.Bounds.Height-7;
                int edge=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;
                if(edge!=0){handled=true;return new IntPtr(edge);}
            }
            var button=close.TranslatePoint(default,window);var heading=title.TranslatePoint(default,window);
            if(button is {} b && new Rect(b,close.Bounds.Size).Inflate(3).Contains(point))return IntPtr.Zero;
            if(heading is {} h && point.Y>=0 && point.Y<h.Y+title.Bounds.Height+7)
            {handled=true;return new IntPtr(2);} // Native HTCAPTION: normal OS move/capture/DPI handling.
            return IntPtr.Zero;
        }
    }
}
