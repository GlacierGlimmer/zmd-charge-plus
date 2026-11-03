using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Input;
using Avalonia.Threading;

namespace EndfieldChargePlus.Views;

public partial class TrayMenuWindow : Window
{
    public event Action? PreviewClicked;
    public event Action? SettingsClicked;
    public event Action? ExitClicked;
    private readonly DispatcherTimer _dismissTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private IntPtr _initialForeground;
    private int _previousButtons;
    private bool _previousEscape;

    public TrayMenuWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows())
        {
            // Keep Explorer's notification overflow as the active window. A
            // normal activated top-level window makes that flyout collapse.
            Win32Properties.AddWindowStylesCallback(this, (style, extended) =>
                (style, (extended | 0x08000080u) & ~0x00040000u));
            Win32Properties.AddWndProcHookCallback(this, (IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE; keep the click.
                return IntPtr.Zero;
            });
        }
        LocalizationManager.ApplyStaticText(this);
        FlowDirection=LocalizationManager.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ApplyMenuAppearance();

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://EndfieldChargePlus/Assets/tray_bolt.png"));
            Icon = new WindowIcon(new Bitmap(stream));
        }
        catch { }

        if (!OperatingSystem.IsWindows()) Deactivated += (_, _) => Close();
        _dismissTimer.Tick += (_, _) => CheckDismissInput();
        Closed += (_, _) => _dismissTimer.Stop();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled=true; } };
        SetupMenuItem(MenuPreview, MenuPreviewText, () => PreviewClicked?.Invoke());
        SetupMenuItem(MenuSettings, MenuSettingsText, () => SettingsClicked?.Invoke());
        SetupMenuItem(MenuExit, MenuExitText, () => ExitClicked?.Invoke());
    }

    private void ApplyMenuAppearance()
    {
        var (font,ui)=Settings.GlobalAppearance.CurrentScales;
        double captionWidth=new[]{MenuPreviewText,MenuSettingsText,MenuExitText}.Max(text=>
            new FormattedText(text.Text ?? "",System.Globalization.CultureInfo.CurrentCulture,text.FlowDirection,
                new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),13*font,Brushes.White).Width);
        Width=Math.Max(220,Math.Ceiling(captionWidth)+40)*ui;
        Height=139*ui;
        Settings.GlobalAppearance.Apply(this,font,ui);
    }

    private static void SetupMenuItem(Border border, TextBlock text, Action onClick)
    {
        var normalBg = Brushes.Transparent;
        var hoverBg = new SolidColorBrush(Color.Parse("#444300"));
        var normalFg = new SolidColorBrush(Color.Parse("#E8E8E8"));
        var hoverFg = new SolidColorBrush(Color.Parse("#FFFA00"));

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
        bool pressed=false;
        border.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            pressed=true; e.Pointer.Capture(border);
            e.Handled = true;
        };
        border.PointerCaptureLost += (_, _) => pressed=false;
        border.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            bool clicked=pressed && new Rect(border.Bounds.Size).Contains(e.GetPosition(border));
            pressed=false; e.Pointer.Capture(null); e.Handled=true;
            if (clicked) onClick();
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
            Position = TrayMenuPosition.Calculate(hasCursor ? cursor : null, wa, winW, winH);
        }

        if (OperatingSystem.IsWindows())
        {
            _initialForeground = GetForegroundWindow();
            _previousButtons = ReadButtons();
            _previousEscape = IsDown(0x1B);
        }
        Show();
        if (OperatingSystem.IsWindows()) _dismissTimer.Start();
        else { Activate(); Focus(); }
    }
    private void CheckDismissInput()
    {
        if (!IsVisible) return;
        int buttons = ReadButtons();
        bool escape = IsDown(0x1B);
        PixelPoint? cursor = GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : null;
        var bounds = new PixelRect(Position, PixelSize.FromSize(ClientSize, RenderScaling));
        EvaluateDismissInput(cursor, bounds, buttons, escape, GetForegroundWindow());
    }
    // Sample only while this short-lived non-activating menu is visible. No
    // global hook, capture, foreground changes or intercepted system input.
    internal void EvaluateDismissInput(PixelPoint? cursor, PixelRect bounds, int buttons, bool escape, IntPtr foreground)
    {
        bool outsidePress = (buttons & ~_previousButtons) != 0 && cursor is {} p && !bounds.Contains(p);
        bool escapePressed = escape && !_previousEscape;
        _previousButtons = buttons; _previousEscape = escape;
        if (outsidePress || escapePressed || foreground != _initialForeground) Close();
    }
    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static int ReadButtons() => (IsDown(1) ? 1 : 0) | (IsDown(2) ? 2 : 0) | (IsDown(4) ? 4 : 0);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
