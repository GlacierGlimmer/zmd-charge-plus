using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EndfieldChargePlus.Settings;
namespace EndfieldChargePlus.Customization;
internal sealed class ProfileRingWindow : Window
{
    internal ProfileRingControl Ring {get;}
    internal event Action? CancelRequested;
    private bool _closing;
    internal ProfileRingWindow(IReadOnlyList<HudProfile> profiles,WorkspacePreferences preferences)
    {
        Width=Height=preferences.RingDiameter;CanResize=false;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        SystemDecorations=SystemDecorations.None;Background=Brushes.Transparent;TransparencyLevelHint=new[]{WindowTransparencyLevel.Transparent};
        Ring=new(){Profiles=profiles,Preferences=preferences};Content=Ring;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){CancelRequested?.Invoke();e.Handled=true;}};
        PointerWheelChanged+=(_,e)=>{Ring.ChangePage(e.Delta.Y>0?-1:1);e.Handled=true;};
    }
    internal void Dismiss(){if(_closing)return;_closing=true;Ring.PlayClose(Close);}
    internal void OpenAt(PixelPoint anchor)
    {
        var screen=Screens.ScreenFromPoint(anchor)??Screens.Primary;double scale=screen?.Scaling??1;
        var area=screen?.WorkingArea??new PixelRect(anchor.X-1000,anchor.Y-1000,2000,2000);
        Width=Height=Math.Max(120,Math.Min(Width,Math.Min(area.Width,area.Height)/scale-16));int size=(int)Math.Ceiling(Width*scale);
        Position=new PixelPoint(Math.Clamp(anchor.X-size/2,area.X,Math.Max(area.X,area.Right-size)),Math.Clamp(anchor.Y-size/2,area.Y,Math.Max(area.Y,area.Bottom-size)));
        Show();Ring.PlayOpen();
    }
    internal void Track(PixelPoint point)=>Ring.PointAt(this.PointToClient(point));
}
