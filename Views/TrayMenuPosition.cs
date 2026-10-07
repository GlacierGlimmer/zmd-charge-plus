using Avalonia;

namespace EndfieldChargePlus.Views;

public static class TrayMenuPosition
{
    public static PixelPoint Calculate(PixelPoint? cursor, PixelRect workArea, int width, int height)
    {
        int left = workArea.X + 8;
        int top = workArea.Y + 8;
        int right = Math.Max(left, workArea.Right - width - 8);
        int bottom = Math.Max(top, workArea.Bottom - height - 8);
        return new PixelPoint(Math.Clamp(cursor?.X - width ?? right, left, right),
            Math.Clamp(cursor?.Y - height ?? bottom, top, bottom));
    }
}
