using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EndfieldChargePlus.Views;

public partial class TrayMenuWindow : Window
{
    public event Action? PreviewClicked;
    public event Action? SettingsClicked;
    public event Action? ExitClicked;

    public TrayMenuWindow()
    {
        InitializeComponent();
        LocalizationManager.ApplyStaticText(this);

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://EndfieldChargePlus/Assets/tray_bolt.png"));
            Icon = new WindowIcon(new Bitmap(stream));
        }
        catch { }

        Deactivated += (_, _) => Close();
        SetupMenuItem(MenuPreview, MenuPreviewText, () => PreviewClicked?.Invoke());
        SetupMenuItem(MenuSettings, MenuSettingsText, () => SettingsClicked?.Invoke());
        SetupMenuItem(MenuExit, MenuExitText, () => ExitClicked?.Invoke());
    }

    private static void SetupMenuItem(Border border, TextBlock text, Action onClick)
    {
        var normalBg = Brushes.Transparent;
        var hoverBg = new SolidColorBrush(Color.Parse("#2A2A2D"));
        var normalFg = new SolidColorBrush(Color.Parse("#E8E8E8"));
        var hoverFg = new SolidColorBrush(Color.Parse("#FFFFFF"));

        border.PointerEntered += (_, _) =>
        {
            border.Background = hoverBg;
            text.Foreground = hoverFg;
        };
        border.PointerExited += (_, _) =>
        {
            border.Background = normalBg;
            text.Foreground = normalFg;
        };
        border.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Win32Point lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    public void ShowAtTray()
    {
        PixelPoint cursor = default;
        bool hasCursor = false;
        if (GetCursorPos(out var pt))
        {
            cursor = new PixelPoint(pt.X, pt.Y);
            hasCursor = true;
        }

        var screen = hasCursor
            ? Screens.All.FirstOrDefault(s =>
            {
                var b = s.Bounds;
                return cursor.X >= b.X && cursor.X < b.Right && cursor.Y >= b.Y && cursor.Y < b.Bottom;
            }) ?? Screens.Primary
            : Screens.Primary;
        screen ??= Screens.All.FirstOrDefault();

        if (screen is not null)
        {
            double scaling = screen.Scaling > 0 ? screen.Scaling : 1d;
            var wa = screen.WorkingArea;
            int winW = (int)Math.Round(Width * scaling);
            int winH = (int)Math.Round(Height * scaling);
            int x;
            int y;

            if (hasCursor)
            {
                // Match the upstream project: right edge follows the tray click point,
                // while keeping the whole menu inside the current monitor work area.
                x = cursor.X - winW;
                y = wa.Bottom - winH - 8;
            }
            else
            {
                x = wa.Right - winW - 8;
                y = wa.Bottom - winH - 8;
            }

            x = Math.Clamp(x, wa.X + 8, Math.Max(wa.X + 8, wa.Right - winW - 8));
            y = Math.Clamp(y, wa.Y + 8, Math.Max(wa.Y + 8, wa.Bottom - winH - 8));
            Position = new PixelPoint(x, y);
        }

        Show();
        Activate();
        Focus();
    }
}
