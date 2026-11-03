using Avalonia;
using EndfieldChargePlus.Settings;
namespace EndfieldChargePlus.Views;
public static class HudPlacement
{
    public static bool UsesScreenBounds(HudPosition position) => (int)position>=9 && (int)position<=17;
    public static PixelPoint VisibleOrigin(AppSettings settings,PixelRect screen,PixelRect work,double width,double height)
    {
        var area=UsesScreenBounds(settings.HudPosition) && settings.PositionMode==HudPositionMode.Preset ? screen : work;
        int grid=(int)settings.HudPosition%9;
        double margin=UsesScreenBounds(settings.HudPosition) ? 0 : 16;
        double x,y;
        if(settings.PositionMode==HudPositionMode.CustomCoordinates) { x=area.X+settings.HudCustomX;y=area.Y+settings.HudCustomY; }
        else {
            x=grid%3==0 ? area.X+margin : grid%3==2 ? area.Right-width-margin : area.X+(area.Width-width)/2;
            y=grid/3==0 ? area.Y+margin : grid/3==2 ? area.Bottom-height-margin : area.Y+(area.Height-height)/2;
            x+=settings.HudOffsetX;y+=settings.HudOffsetY;
        }
        return new((int)Math.Round(Math.Clamp(x,area.X,Math.Max(area.X,area.Right-width))),
            (int)Math.Round(Math.Clamp(y,area.Y,Math.Max(area.Y,area.Bottom-height))));
    }
}
