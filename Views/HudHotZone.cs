using Avalonia;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Views;

internal static class HudHotZone
{
    internal static PixelRect Bounds(PixelRect screen,AppSettings settings)
    {
        if(screen.Width<=0 || screen.Height<=0)return default;
        int width=Math.Clamp(settings.HotZoneWidth,1,screen.Width),height=Math.Clamp(settings.HotZoneHeight,1,screen.Height);
        int anchor=Math.Clamp((int)settings.HotZonePosition,0,8);
        long x=(long)screen.X+(anchor%3)*(screen.Width-width)/2+settings.HotZoneOffsetX;
        long y=(long)screen.Y+(anchor/3)*(screen.Height-height)/2+settings.HotZoneOffsetY;
        return new((int)Math.Clamp(x,screen.X,(long)screen.Right-width),(int)Math.Clamp(y,screen.Y,(long)screen.Bottom-height),width,height);
    }
}

internal sealed class HotZoneDwellGate
{
    private long? _entered;
    private bool _latched;
    internal void Observe(bool inside,long now)
    {
        if(!inside) {Reset();return;}
        _entered ??= now;
    }
    internal bool Ready(long now,int dwell) => _entered is { } since && !_latched && now-since>=Math.Max(0,dwell);
    internal void Presented()=>_latched=true;
    internal void Reset() { _entered=null;_latched=false; }
}
