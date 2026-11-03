using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private static readonly IReadOnlyDictionary<WindowEdge, Cursor> ResizeCursors = new Dictionary<WindowEdge, Cursor>
    {
        [WindowEdge.North] = new(StandardCursorType.TopSide), [WindowEdge.South] = new(StandardCursorType.BottomSide),
        [WindowEdge.West] = new(StandardCursorType.LeftSide), [WindowEdge.East] = new(StandardCursorType.RightSide),
        [WindowEdge.NorthWest] = new(StandardCursorType.TopLeftCorner), [WindowEdge.NorthEast] = new(StandardCursorType.TopRightCorner),
        [WindowEdge.SouthWest] = new(StandardCursorType.BottomLeftCorner), [WindowEdge.SouthEast] = new(StandardCursorType.BottomRightCorner)
    };
    private void InitializeWindowChrome()
    {
        if (OperatingSystem.IsWindows())
        {
            // SystemDecorations.None removes WS_SYSMENU along with the native
            // caption. Keep the Shell's taskbar menu and standard window commands
            // without adding a visible title bar. Avalonia reapplies this callback
            // whenever it updates styles (including minimize/restore).
            Win32Properties.AddWindowStylesCallback(this, (style, extendedStyle) =>
                (style | 0x00080000u, extendedStyle));
        }
        InitializeStatusBar();
        MinimizeWindowButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeWindowButton.Click += (_, _) => ToggleWindowMaximize();
        CloseWindowButton.Click += (_, _) => Close();
        TitleBarDragArea.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleWindowMaximize();
            else BeginMoveDrag(e);
            e.Handled = true;
        };
        AddHandler(PointerPressedEvent, OnWindowFramePressed, RoutingStrategies.Tunnel);
        LayoutUpdated += (_, _) =>
        {
            // Continue the two content surfaces through the caption, even after UI scaling.
            var edge = WorkspaceSidebar.TranslatePoint(new Point(WorkspaceSidebar.Bounds.Width, 0), SettingsTitleBar);
            if (edge is { } point)
            {
                double width = Math.Clamp(point.X, 0, SettingsTitleBar.Bounds.Width);
                if (Math.Abs(TitleBarSidebarBackground.Width - width) > .1) TitleBarSidebarBackground.Width = width;
            }
        };
        PointerMoved += (_, e) => Cursor = ResizeEdgeAt(e.GetPosition(this)) is { } edge ? ResizeCursors[edge] : null;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) RefreshWindowChrome();
        };
        RefreshWindowChrome();
    }

    private void ToggleWindowMaximize()
    {
        if (CanResize) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private WindowEdge? ResizeEdgeAt(Point point)
    {
        if (!CanResize || WindowState != WindowState.Normal) return null;
        const double edge = 6;
        bool left = point.X <= edge, right = point.X >= Bounds.Width - edge;
        bool top = point.Y <= edge, bottom = point.Y >= Bounds.Height - edge;
        if (top) return left ? WindowEdge.NorthWest : right ? WindowEdge.NorthEast : WindowEdge.North;
        if (bottom) return left ? WindowEdge.SouthWest : right ? WindowEdge.SouthEast : WindowEdge.South;
        return left ? WindowEdge.West : right ? WindowEdge.East : null;
    }

    private void OnWindowFramePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && ResizeEdgeAt(e.GetPosition(this)) is { } edge)
        {
            BeginResizeDrag(edge, e);
            e.Handled = true;
        }
    }

    private void RefreshWindowChrome()
    {
        RefreshStatusLocalization();
        bool maximized = WindowState == WindowState.Maximized;
        SettingsWindowFrame.CornerRadius = new CornerRadius(maximized ? 0 : 8);
        SettingsWindowFrame.BorderThickness = new Thickness(maximized ? 0 : 1);
        MaximizeWindowGlyph.Data = Geometry.Parse(maximized ? "M1,3 H3 V1 M5,1 V3 H7 M7,5 H5 V7 M3,7 V5 H1" : "M1,3 V1 H3 M5,1 H7 V3 M7,5 V7 H5 M3,7 H1 V5");
        SetCaption(MinimizeWindowButton, LocalizationManager.Text("最小化窗口", "Minimize window"));
        SetCaption(MaximizeWindowButton, maximized ? LocalizationManager.Text("还原窗口", "Restore window") : LocalizationManager.Text("最大化窗口", "Maximize window"));
        SetCaption(CloseWindowButton, LocalizationManager.Text("关闭窗口", "Close window"));
    }

    private static void SetCaption(Button button, string text)
    {
        ToolTip.SetTip(button, text);
        AutomationProperties.SetName(button, text);
    }
}
