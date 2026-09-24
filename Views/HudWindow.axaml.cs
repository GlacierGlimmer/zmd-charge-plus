using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Platform;
using EndfieldChargePlus.Animations;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Views;

public partial class HudWindow : Window
{
    private CancellationTokenSource? _cts;
    private AppSettings _settings = new();
    private AnimationOptions _animOptions = AnimationOptions.Default;
    private bool _persistent;
    private WindowsHudHitTest? _hitTest;
    private HudRenderData? _lastRenderData;

    // The progress ring is deliberately animated independently from the existing HUD
    // summon/retract animations. Data updates therefore feel continuous without changing
    // any of the original layout or animation timelines.
    private readonly DispatcherTimer _progressTimer;
    private double _displayedProgress;
    private double _progressFrom;
    private double _progressTo;
    private DateTime _progressStartedUtc;
    private bool _progressInitialized;
    private static readonly TimeSpan ProgressTransitionDuration = TimeSpan.FromMilliseconds(320);

    public bool IsHudBusy { get; private set; }

    public HudWindow()
    {
        InitializeComponent();
        LocalizationManager.ApplyStaticText(this);

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _progressTimer.Tick += (_, _) => TickProgressAnimation();

        Closed += (_, _) =>
        {
            _progressTimer.Stop();
            _hitTest?.Dispose();
            _hitTest = null;
        };
        ResetToInitial();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _animOptions = AnimationOptions.FromSettings(settings);
        GlobalScale.RenderTransform = new ScaleTransform(settings.GlobalScale, settings.GlobalScale);
        Opacity = Math.Clamp(settings.HudOpacity, 0.0, 1.0);
        ApplyWindowLayer(_persistent);

        if (IsVisible)
            PositionHud();
    }

    private void ApplyWindowLayer(bool persistent)
    {
        // 临时 HUD 保持原行为：始终置顶。
        // 常驻 HUD 可以选择置顶，或作为普通非置顶窗口留在桌面层。
        Topmost = !persistent || _settings.PersistentLayer == PersistentHudLayer.Topmost;
        // Avalonia can update HWND styles after a layer change. Keep the HUD
        // mouse-through in both persistent (topmost/normal) and transient modes.
        if (IsVisible)
        {
            EnsureInputHitTest();
            Dispatcher.UIThread.Post(EnsureInputHitTest, DispatcherPriority.Loaded);
        }
    }

    public async Task ShowCustomAsync(
        HudRenderData data,
        Func<CancellationToken, Task<HudRenderData>>? liveRefresh = null)
    {
        if (IsVisible)
            await HideAnimatedAsync();

        _persistent = false;
        ApplyWindowLayer(false);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var localCts = _cts;
        var ct = localCts.Token;
        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsHudBusy = true;

        ApplyRenderData(data);
        ResetToInitial();
        ShowPositioned();

        Task? refreshTask = liveRefresh is null
            ? null
            : RefreshLiveDataAsync(liveRefresh, refreshCts.Token, persistent: false);

        try
        {
            if (data.SimpleAnimation)
            {
                SetSimpleCState();
                await Task.WhenAll(
                    HudAnimations.SimplePillAppear(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.SimpleFadeIn(_animOptions).RunAsync(BoltIcon, ct),
                    HudAnimations.SimpleFadeIn(_animOptions).RunAsync(NumHost, ct),
                    HudAnimations.SimpleScaleOut(_animOptions).RunAsync(ScaleHost, ct));
            }
            else
            {
                await Task.WhenAll(
                    HudAnimations.PillCorner(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillAppear(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillHeight(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillHeight(_animOptions).RunAsync(RippleHost, ct),
                    HudAnimations.ScaleOut(_animOptions).RunAsync(ScaleHost, ct),
                    HudAnimations.BoltIcon(_animOptions).RunAsync(BoltIcon, ct),
                    HudAnimations.RippleHost(_animOptions).RunAsync(RippleHost, ct),
                    HudAnimations.CircleForm(_animOptions).RunAsync(CircleForm, ct),
                    HudAnimations.SquareForm(_animOptions).RunAsync(SquareForm, ct),
                    HudAnimations.TitleHost(_animOptions).RunAsync(TitleHost, ct),
                    HudAnimations.NumHost(_animOptions).RunAsync(NumHost, ct),
                    HudAnimations.Ripple(_animOptions, 1.5, 0.50).RunAsync(RippleInner, ct),
                    HudAnimations.Ripple(_animOptions, 2.0, 0.50).RunAsync(RippleMid, ct),
                    HudAnimations.Ripple(_animOptions, 2.5, 0.60).RunAsync(RippleOuter, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleInnerHost, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleMidHost, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleOuterHost, ct));
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            refreshCts.Cancel();
            if (refreshTask is not null)
            {
                try { await refreshTask; }
                catch (OperationCanceledException) { }
            }

            if (ReferenceEquals(_cts, localCts))
            {
                if (!ct.IsCancellationRequested && IsVisible)
                    Hide();
                IsHudBusy = false;
            }
        }
    }

    private async Task RefreshLiveDataAsync(
        Func<CancellationToken, Task<HudRenderData>> refresh,
        CancellationToken ct,
        bool persistent)
    {
        long lastSecond = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(50, ct).ConfigureAwait(false);
            long second = DateTime.Now.Ticks / TimeSpan.TicksPerSecond;
            if (second == lastSecond) continue;
            lastSecond = second;

            HudRenderData data;
            try
            {
                data = await refresh(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                continue;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested && IsVisible && _persistent == persistent)
                    ApplyRenderData(data);
            });
        }
    }

    public async Task ShowPersistentAsync(
        HudRenderData data,
        Func<CancellationToken, Task<HudRenderData>>? liveRefresh = null)
    {
        _persistent = true;
        ApplyWindowLayer(true);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var localCts = _cts;
        var ct = localCts.Token;
        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsHudBusy = true;

        ApplyRenderData(data);
        ResetToInitial();
        ShowPositioned();

        // Full animation reaches its final numeric state before the animation timeline itself
        // has finished. Keep sampling during that tail so clocks/CPU/GPU values do not appear
        // frozen for several seconds after the visible intro has completed.
        Task? refreshTask = liveRefresh is null
            ? null
            : RefreshLiveDataAsync(liveRefresh, refreshCts.Token, persistent: true);

        try
        {
            // 复用原动画，只去掉最后的 ScaleOut，因此最终停留在原动画 C 状态。
            if (data.SimpleAnimation)
            {
                SetSimpleCState();
                await Task.WhenAll(
                    HudAnimations.SimplePillAppear(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.SimpleFadeIn(_animOptions).RunAsync(BoltIcon, ct),
                    HudAnimations.SimpleFadeIn(_animOptions).RunAsync(NumHost, ct));
            }
            else
            {
                await Task.WhenAll(
                    HudAnimations.PillCorner(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillAppear(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillHeight(_animOptions).RunAsync(Pill, ct),
                    HudAnimations.PillHeight(_animOptions).RunAsync(RippleHost, ct),
                    HudAnimations.BoltIcon(_animOptions).RunAsync(BoltIcon, ct),
                    HudAnimations.RippleHost(_animOptions).RunAsync(RippleHost, ct),
                    HudAnimations.CircleForm(_animOptions).RunAsync(CircleForm, ct),
                    HudAnimations.SquareForm(_animOptions).RunAsync(SquareForm, ct),
                    HudAnimations.TitleHost(_animOptions).RunAsync(TitleHost, ct),
                    HudAnimations.NumHost(_animOptions).RunAsync(NumHost, ct),
                    HudAnimations.Ripple(_animOptions, 1.5, 0.50).RunAsync(RippleInner, ct),
                    HudAnimations.Ripple(_animOptions, 2.0, 0.50).RunAsync(RippleMid, ct),
                    HudAnimations.Ripple(_animOptions, 2.5, 0.60).RunAsync(RippleOuter, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleInnerHost, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleMidHost, ct),
                    HudAnimations.RippleRise(_animOptions).RunAsync(RippleOuterHost, ct));
            }

            SetPersistentFinalState();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            refreshCts.Cancel();
            if (refreshTask is not null)
            {
                try { await refreshTask; }
                catch (OperationCanceledException) { }
            }

            if (ReferenceEquals(_cts, localCts))
                IsHudBusy = false;
        }
    }

    public void UpdatePersistent(HudRenderData data)
    {
        if (!_persistent) return;

        ApplyRenderData(data);
        ApplyWindowLayer(true);
        SetPersistentFinalState();

        if (!IsVisible)
            ShowPositioned();
    }

    public void HidePersistent() => _ = HidePersistentAsync();

    public async Task HidePersistentAsync() => await HideAnimatedAsync();

    public async Task HideAnimatedAsync()
    {
        _cts?.Cancel();
        _persistent = false;

        if (!IsVisible)
        {
            IsHudBusy = false;
            return;
        }

        IsHudBusy = true;
        try
        {
            // A settings/profile switch always exits from the final C state, so the transition
            // is deterministic: old HUD retracts completely before the next HUD is shown.
            SetPersistentFinalState();
            await HudAnimations.CloseFromC().RunAsync(ScaleHost);
        }
        catch { }
        finally
        {
            if (IsVisible) Hide();
            IsHudBusy = false;
        }
    }

    private void ApplyRenderData(HudRenderData data)
    {
        var previous = _lastRenderData;

        if (previous?.Tagline != data.Tagline) TagLineText.Text = data.Tagline;
        if (previous?.Title != data.Title) TitleText.Text = data.Title;
        if (previous?.PrimaryText != data.PrimaryText) PrimaryText.Text = data.PrimaryText;
        if (previous?.SecondaryText != data.SecondaryText) SecondaryText.Text = data.SecondaryText;
        if (previous?.RightText != data.RightText) RightText.Text = data.RightText;
        if (previous?.RightSuffix != data.RightSuffix) RightSuffixText.Text = data.RightSuffix;

        if (previous?.LeftIcon != data.LeftIcon)
        {
            var geometry = IconCatalog.GetGeometry(data.LeftIcon);
            CircleGlyph.Data = geometry;
            SquareGlyph.Data = geometry;
        }

        bool useOriginalLaptop = string.Equals(data.RightIcon, "battery", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(data.RightIcon, "laptop", StringComparison.OrdinalIgnoreCase);

        if (previous?.RightIcon != data.RightIcon)
        {
            BadgeLaptop.IsVisible = useOriginalLaptop;
            BadgeElectrode.IsVisible = useOriginalLaptop;
            BadgeGlyph.IsVisible = !useOriginalLaptop;
            if (!useOriginalLaptop)
                BadgeGlyph.Data = IconCatalog.GetGeometry(data.RightIcon);
        }

        if (previous?.AccentColor != data.AccentColor)
        {
            var accent = TryColor(data.AccentColor, Color.Parse("#C6CA4C"));
            var accentBrush = new SolidColorBrush(accent);
            BadgeArc.Stroke = accentBrush;
            LaptopScreen.BorderBrush = accentBrush;
            LaptopBase.Background = accentBrush;
            BadgeElectrode.Background = accentBrush;
            BadgeGlyph.Foreground = accentBrush;
        }

        if (previous is null || Math.Abs(previous.Progress - data.Progress) > 0.0005)
            SetProgressTarget(data.Progress, animate: previous is not null && IsVisible);

        _lastRenderData = data;
    }

    private void SetProgressTarget(double value, bool animate)
    {
        double target = Math.Clamp(value, 0d, 1d);

        if (!_progressInitialized || !animate)
        {
            _progressTimer.Stop();
            _progressInitialized = true;
            _displayedProgress = target;
            _progressFrom = target;
            _progressTo = target;
            BadgeArc.Data = BuildRingGeometry(target, 46d, 4.5d);
            return;
        }

        if (Math.Abs(_displayedProgress - target) < 0.0005d)
        {
            _progressTimer.Stop();
            _displayedProgress = target;
            _progressFrom = target;
            _progressTo = target;
            BadgeArc.Data = BuildRingGeometry(target, 46d, 4.5d);
            return;
        }

        // If another sample arrives while the ring is already moving, continue from the
        // currently displayed value instead of snapping back to the previous target.
        _progressFrom = _displayedProgress;
        _progressTo = target;
        _progressStartedUtc = DateTime.UtcNow;
        _progressTimer.Start();
    }

    private void TickProgressAnimation()
    {
        if (!_progressInitialized)
        {
            _progressTimer.Stop();
            return;
        }

        double durationMs = Math.Max(1d, ProgressTransitionDuration.TotalMilliseconds);
        double t = Math.Clamp((DateTime.UtcNow - _progressStartedUtc).TotalMilliseconds / durationMs, 0d, 1d);
        // Cubic ease-out: quick response at the start, gentle settle at the target.
        double eased = 1d - Math.Pow(1d - t, 3d);
        _displayedProgress = _progressFrom + (_progressTo - _progressFrom) * eased;
        BadgeArc.Data = BuildRingGeometry(_displayedProgress, 46d, 4.5d);

        if (t >= 1d)
        {
            _displayedProgress = _progressTo;
            BadgeArc.Data = BuildRingGeometry(_displayedProgress, 46d, 4.5d);
            _progressTimer.Stop();
        }
    }

    private static Color TryColor(string? value, Color fallback)
    {
        try { return Color.Parse(string.IsNullOrWhiteSpace(value) ? "#C6CA4C" : value); }
        catch { return fallback; }
    }

    private async Task DismissAsync()
    {
        if (_persistent) return;
        await HideAnimatedAsync();
    }

    private static Geometry BuildRingGeometry(double fraction, double diameter, double thickness)
    {
        double radius = (diameter - thickness) / 2d;
        var center = new Point(diameter / 2d, diameter / 2d);
        double sweep = 360d * Math.Clamp(fraction, 0d, 1d);
        if (sweep < 0.5d) sweep = 0.5d;
        if (sweep > 359.5d) sweep = 359.5d;
        const double startAngle = -90d;
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweep);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments = new PathSegments
        {
            new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                RotationAngle = 0d,
                IsLargeArc = sweep > 180d,
                SweepDirection = SweepDirection.Clockwise,
            },
        };
        return new PathGeometry { Figures = new PathFigures { figure } };
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        double rad = degrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    private void ResetToInitial()
    {
        Root.Opacity = 1;
        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);
        Pill.Width = 560;
        Pill.Height = 60;
        Pill.CornerRadius = new CornerRadius(30d);
        Pill.Opacity = 0;
        Pill.RenderTransform = new ScaleTransform(0.6d, 0.6d);

        RippleHost.RenderTransform = new TranslateTransform(0d, 0d);
        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(0.4d, 0.4d), new TranslateTransform(0d, 0d) },
        };
        BoltIcon.Opacity = 0;

        RippleInnerHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleMidHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleOuterHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleInner.RenderTransform = new ScaleTransform(0d, 0d);
        RippleInner.Opacity = 0;
        RippleMid.RenderTransform = new ScaleTransform(0d, 0d);
        RippleMid.Opacity = 0;
        RippleOuter.RenderTransform = new ScaleTransform(0d, 0d);
        RippleOuter.Opacity = 0;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 0;
        TitleHost.RenderTransform = new TranslateTransform(0d, 0d);
        TitleHost.Opacity = 0;
        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 0;
    }

    private void SetSimpleCState()
    {
        Pill.Width = 560;
        Pill.Height = 60;
        Pill.CornerRadius = new CornerRadius(30d);
        Pill.Opacity = 0;
        Pill.RenderTransform = new ScaleTransform(0.6d, 0.6d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1d, 1d), new TranslateTransform(-245d, 0d) },
        };
        BoltIcon.Opacity = 0;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 1;
        TitleHost.Opacity = 0;
        RippleHost.Height = 60;
        RippleHost.RenderTransform = new TranslateTransform(0d, 0d);
        RippleInnerHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleMidHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleOuterHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleInner.Opacity = 0;
        RippleMid.Opacity = 0;
        RippleOuter.Opacity = 0;
        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 0;
    }

    private void SetPersistentFinalState()
    {
        Root.Opacity = 1;
        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);

        Pill.Width = 560;
        Pill.Height = 60;
        Pill.CornerRadius = new CornerRadius(30d);
        Pill.Opacity = 1;
        Pill.RenderTransform = new ScaleTransform(1d, 1d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1d, 1d), new TranslateTransform(-245d, 0d) },
        };
        BoltIcon.Opacity = 1;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 1;

        RippleHost.Height = 60;
        RippleHost.RenderTransform = new TranslateTransform(-245d, 0d);
        RippleInner.Opacity = 0;
        RippleMid.Opacity = 0;
        RippleOuter.Opacity = 0;
        TitleHost.Opacity = 0;
        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 1;
    }

    private void PositionHud()
    {
        var screen = ResolveScreen(_settings.MonitorIndex);
        if (screen is null) return;

        var area = screen.WorkingArea;
        double scaling = screen.Scaling > 0 ? screen.Scaling : 1d;

        // 窗口本身比 560px HUD 大，是为了保留原动画波纹空间。
        // 定位时按“可见 HUD”而不是透明窗口外框计算，所以坐标更符合用户直觉。
        double windowWidthPx = Width * scaling;
        double windowHeightPx = Height * scaling;
        double hudWidthPx = 560d * _settings.GlobalScale * scaling;
        double hudHeightPx = 60d * _settings.GlobalScale * scaling; // 最终可见胶囊高度
        double paddingX = Math.Max(0d, (windowWidthPx - hudWidthPx) / 2d);
        double paddingY = Math.Max(0d, (windowHeightPx - hudHeightPx) / 2d);
        const double margin = 16d;

        double hudLeft;
        double hudTop;

        if (_settings.PositionMode == HudPositionMode.CustomCoordinates)
        {
            hudLeft = area.X + _settings.HudCustomX;
            hudTop = area.Y + _settings.HudCustomY;
        }
        else
        {
            hudLeft = _settings.HudPosition switch
            {
                HudPosition.TopLeft or HudPosition.CenterLeft or HudPosition.BottomLeft => area.X + margin,
                HudPosition.TopRight or HudPosition.CenterRight or HudPosition.BottomRight => area.X + area.Width - hudWidthPx - margin,
                _ => area.X + (area.Width - hudWidthPx) / 2d,
            };

            hudTop = _settings.HudPosition switch
            {
                HudPosition.TopLeft or HudPosition.TopCenter or HudPosition.TopRight => area.Y + margin,
                HudPosition.BottomLeft or HudPosition.BottomCenter or HudPosition.BottomRight => area.Y + area.Height - hudHeightPx - margin,
                _ => area.Y + (area.Height - hudHeightPx) / 2d,
            };

            hudLeft += _settings.HudOffsetX;
            hudTop += _settings.HudOffsetY;
        }

        int windowX = (int)Math.Round(hudLeft - paddingX);
        int windowY = (int)Math.Round(hudTop - paddingY);
        Position = new PixelPoint(windowX, windowY);
    }

    private Avalonia.Platform.Screen? ResolveScreen(int monitorIndex)
    {
        var screens = Screens.All;
        var primary = Screens.Primary;
        if (monitorIndex < 0)
            return primary ?? screens.FirstOrDefault();
        if (monitorIndex < screens.Count)
            return screens[monitorIndex];
        return primary ?? screens.FirstOrDefault();
    }

    private void ShowPositioned()
    {
        PositionHud();
        if (!IsVisible)
            Show();

        EnsureInputHitTest();
        PositionHud();
        Dispatcher.UIThread.Post(() =>
        {
            PositionHud();
            EnsureInputHitTest();
        }, DispatcherPriority.Loaded);
    }

    public bool IsPointInTopCenterHotZone(PixelPoint screenPoint, int width = 240, int height = 5)
    {
        var screen = ResolveScreen(_settings.MonitorIndex);
        if (screen is null) return false;

        var bounds = screen.Bounds;
        int centerX = bounds.X + bounds.Width / 2;
        int halfWidth = Math.Max(40, width / 2);
        int hotHeight = Math.Max(2, height);

        return screenPoint.X >= centerX - halfWidth
            && screenPoint.X <= centerX + halfWidth
            && screenPoint.Y >= bounds.Y
            && screenPoint.Y <= bounds.Y + hotHeight;
    }

    private void EnsureInputHitTest()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Avalonia may refresh native HWND extended styles after Show(), Topmost changes
        // or other window-state transitions. Re-apply click-through every time the HUD
        // is presented instead of assuming a one-time style write remains forever.
        var handle = this.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;

        if (_hitTest is not null && _hitTest.Handle != handle.Handle)
        {
            _hitTest.Dispose();
            _hitTest = null;
        }

        if (_hitTest is null)
            _hitTest = WindowsHudHitTest.TryAttach(handle.Handle, IsPointInsideVisibleHud);

        _hitTest?.Reapply();
    }

    private bool IsPointInsideVisibleHud(PixelPoint screenPoint)
    {
        var screen = ResolveScreen(_settings.MonitorIndex);
        if (screen is null) return true;

        double scaling = screen.Scaling > 0 ? screen.Scaling : 1d;
        double windowWidthPx = Width * scaling;
        double windowHeightPx = Height * scaling;
        double hudWidthPx = 560d * _settings.GlobalScale * scaling;
        double currentPillHeight = double.IsNaN(Pill.Height) ? 60d : Math.Max(60d, Pill.Height);
        double hudHeightPx = currentPillHeight * _settings.GlobalScale * scaling;
        double left = Position.X + (windowWidthPx - hudWidthPx) / 2d;
        double top = Position.Y + (windowHeightPx - hudHeightPx) / 2d;
        const double tolerance = 4d;

        return screenPoint.X >= left - tolerance
            && screenPoint.X <= left + hudWidthPx + tolerance
            && screenPoint.Y >= top - tolerance
            && screenPoint.Y <= top + hudHeightPx + tolerance;
    }
}
