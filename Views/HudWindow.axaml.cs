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
using Avalonia.VisualTree;
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
    private WindowsHudInteraction? _interaction;
    private readonly DispatcherTimer _inputRegionTimer=new() { Interval=TimeSpan.FromMilliseconds(16) };
    public event Action? HudClicked;
    private bool CanCycleOnClick => !_closed && IsVisible && _settings.AlwaysVisible && _settings.ClickToCycle ;

    private WindowsHudLayer? _windowLayer;
    private HudRenderData? _lastRenderData;
    private bool _closed;
    private bool _pointerPressedInside;
    public bool IsClosed => _closed;

    public void CancelPresentation()
    {
        _cts?.Cancel();
        IsHudBusy = false;
    }

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

    private Border? _editingFrame;
    private TextBlock? _editingHint;
    private bool _editingHighlight;
    private double _contentWidth=560;
    public bool IsEditingHighlighted => _editingHighlight;
    public void SetEditingHighlight(bool enabled, string name)
    {
        if (_editingFrame is null)
        {
            _editingFrame=new Border { Width=584, Height=84, BorderBrush=Brushes.Orange,
                BorderThickness=new Thickness(4), CornerRadius=new CornerRadius(38),
                HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center, IsHitTestVisible=false };
            _editingHint=new TextBlock { Name="EditingHint",FontSize=12,Foreground=Brushes.Orange,
                HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Bottom,
                Margin=new Thickness(20,0,0,3),IsHitTestVisible=false };
            _editingFrame.Child=_editingHint;
            GlobalScale.Children.Add(_editingFrame);
        }
        _editingHighlight=enabled;
        _editingHint!.Text=LocalizationManager.Text("当前正在设置","Currently editing");
        _editingFrame.IsVisible=enabled;
        Title=enabled ? $"Endfield Charge Plus · {name}" : "Endfield Charge Plus";
    }

    public bool IsHudBusy { get; private set; }

    public HudWindow()
    {
        InitializeComponent();
        LocalizationManager.ApplyStaticText(this);

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _progressTimer.Tick += (_, _) => TickProgressAnimation();

        Closed += (_, _) =>
        {
            _closed=true;
            CancelPresentation();
            _progressTimer.Stop();
            _inputRegionTimer.Stop();
            _interaction?.Dispose();
            _interaction=null;
            _hitTest?.Dispose();
            _hitTest = null;
            _windowLayer?.Dispose();
            _windowLayer = null;
        };
        PointerPressed += (_,e) =>
        {
            if (OperatingSystem.IsWindows()) return; // Native HWND owns Windows clicks.
            _pointerPressedInside=CanCycleOnClick && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && IsPointInsideVisibleHud(this.PointToScreen(e.GetPosition(this)));
        };
        PointerReleased += (_,e) =>
        {
            if (OperatingSystem.IsWindows()) return;
            bool clicked=_pointerPressedInside && CanCycleOnClick && e.InitialPressMouseButton==MouseButton.Left
                && IsPointInsideVisibleHud(this.PointToScreen(e.GetPosition(this)));
            _pointerPressedInside=false;
            if (clicked) { HudClicked?.Invoke(); e.Handled=true; }
        };
        _inputRegionTimer.Tick+=(_,_)=>_interaction?.Refresh();
        Root.IsHitTestVisible=true;
        ResetToInitial();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        if (_closed) return;
        // Named controls exist immediately after XAML loads; the visual tree does not.
        FontFamily numeric=new("avares://Avalonia.Fonts.Inter/Assets#Inter, Microsoft YaHei UI, PingFang SC, sans-serif");
        FontFamily title=numeric;
        if (!string.IsNullOrWhiteSpace(settings.HudFontFamily))
            numeric=title=new FontFamily(settings.HudFontFamily);
        foreach (var text in new[] { TagLineText, PrimaryText, SecondaryText, RightText, RightSuffixText })
            text.FontFamily=numeric;
        TitleText.FontFamily=title;
        _animOptions = AnimationOptions.FromSettings(settings);
        double scale=settings.GlobalScale*GlobalAppearance.NormalizeScale(settings.GlobalUiScale);
        GlobalScale.RenderTransform = new ScaleTransform(scale,scale);
        double fontScale=GlobalAppearance.NormalizeScale(settings.GlobalFontScale);
        TagLineText.FontSize=9*fontScale;TitleText.FontSize=26*fontScale;
        PrimaryText.FontSize=26*fontScale;SecondaryText.FontSize=14*fontScale;
        RightText.FontSize=22*fontScale;RightSuffixText.FontSize=13*fontScale;
        ContentClip.Height=60*Math.Max(1,fontScale);
        ContentClip.CornerRadius=new CornerRadius(ContentClip.Height/2);
        Width=Math.Max(1200,620*scale);Height=Math.Max(160,200*scale*Math.Max(1,fontScale));
        Root.Width=Width;Root.Height=100*Math.Max(1,fontScale);
        UpdateContentGeometry();
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
        if (_closed) return;
        if (IsVisible)
            await HideAnimatedAsync();
        if (_closed) return;
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
                    if (!_editingHighlight) Hide();
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
        if (_closed) return;
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
        if (_closed || !_persistent) return;

        // Steady sampling changes data only. Native layers and animation
        // transforms are configured on presentation/settings transitions.
        ApplyRenderData(data);
        if (!IsVisible)
        {
            SetPersistentFinalState();
            ShowPositioned();
        }
    }

    public void HidePersistent() => _ = HidePersistentAsync();

    public async Task HidePersistentAsync() => await HideAnimatedAsync();

    public async Task HideAnimatedAsync(bool force=false)
    {
        _cts?.Cancel();
        _cts=new CancellationTokenSource();
        var hideCts=_cts;
        _persistent = false;

        if (_closed || !IsVisible)
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
            await HudAnimations.CloseFromC().RunAsync(ScaleHost,hideCts.Token);
        }
        catch { }
        finally
        {
            if (ReferenceEquals(_cts,hideCts))
            {
                if (!_closed && IsVisible && (force || !_editingHighlight)) Hide();
                IsHudBusy = false;
            }
        }
    }

    private void ApplyRenderData(HudRenderData data)
    {
        var previous = _lastRenderData;
        var direction=LocalizationManager.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        foreach(var text in new[]{TagLineText,TitleText,PrimaryText,SecondaryText,RightText,RightSuffixText})text.FlowDirection=direction;

        if (previous?.Tagline != data.Tagline) TagLineText.Text = data.Tagline;
        if (previous?.Title != data.Title) TitleText.Text = data.Title;
        if (previous?.PrimaryText != data.PrimaryText) PrimaryText.Text = data.PrimaryText;
        if (previous?.SecondaryText != data.SecondaryText) SecondaryText.Text = data.SecondaryText;
        if (previous?.RightText != data.RightText) RightText.Text = data.RightText;
        if (previous?.RightSuffix != data.RightSuffix) RightSuffixText.Text = data.RightSuffix;
        UpdateContentGeometry();

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
        ContentHost.RenderTransform=new TranslateTransform();
        Root.Opacity = 1;
        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);
        Pill.Width = _contentWidth;
        Pill.Height = 60*_animOptions.FontHeightFactor;
        Pill.CornerRadius = new CornerRadius(30d*_animOptions.FontHeightFactor);
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
        Pill.Width = _contentWidth;
        Pill.Height = 60*_animOptions.FontHeightFactor;
        Pill.CornerRadius = new CornerRadius(30d*_animOptions.FontHeightFactor);
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
        RippleHost.Height = 60*_animOptions.FontHeightFactor;
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
        ContentHost.RenderTransform=new TranslateTransform();
        Root.Opacity = 1;
        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);

        Pill.Width = _contentWidth;
        Pill.Height = 60*_animOptions.FontHeightFactor;
        Pill.CornerRadius = new CornerRadius(30d*_animOptions.FontHeightFactor);
        Pill.Opacity = 1;
        Pill.RenderTransform = new ScaleTransform(1d, 1d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1d, 1d), new TranslateTransform(-245d, 0d) },
        };
        BoltIcon.Opacity = 1;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 1;

        RippleHost.Height = 60*_animOptions.FontHeightFactor;
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
        double desiredScale=_settings.GlobalScale*GlobalAppearance.NormalizeScale(_settings.GlobalUiScale);
        double fitWidth=Math.Max(1,area.Width/scaling-32)/(_contentWidth+24);
        double fitHeight=Math.Max(1,area.Height/scaling-32)/(90*_animOptions.FontHeightFactor+24);
        double actualScale=Math.Max(.05,Math.Min(desiredScale,Math.Min(fitWidth,fitHeight)));
        GlobalScale.RenderTransform=new ScaleTransform(actualScale,actualScale);
        Width=Math.Max(1200,(_contentWidth+60)*actualScale);
        Height=Math.Max(160,200*actualScale*_animOptions.FontHeightFactor);
        Root.Width=Width;


        // 窗口本身比 560px HUD 大，是为了保留原动画波纹空间。
        // 定位时按“可见 HUD”而不是透明窗口外框计算，所以坐标更符合用户直觉。
        double windowWidthPx = Width * scaling;
        double windowHeightPx = Height * scaling;
        double hudWidthPx = _contentWidth * actualScale * scaling;
        double hudHeightPx = 60d * _animOptions.FontHeightFactor * actualScale * scaling; // 最终可见胶囊高度
        double paddingX = Math.Max(0d, (windowWidthPx - hudWidthPx) / 2d);
        double paddingY = Math.Max(0d, (windowHeightPx - hudHeightPx) / 2d);

        var origin=HudPlacement.VisibleOrigin(_settings,screen.Bounds,screen.WorkingArea,hudWidthPx,hudHeightPx);
        double hudLeft=origin.X,hudTop=origin.Y;
        if(_editingHighlight) {
            // Keep the outline/caption visible at physical monitor edges; restore the saved position on close.
            double extra=12*actualScale*scaling;
            hudLeft=Math.Clamp(hudLeft,screen.Bounds.X+extra,Math.Max(screen.Bounds.X+extra,screen.Bounds.Right-hudWidthPx-extra));
            hudTop=Math.Clamp(hudTop,screen.Bounds.Y+extra,Math.Max(screen.Bounds.Y+extra,screen.Bounds.Bottom-hudHeightPx-extra));
        }
        int windowX = (int)Math.Round(hudLeft - paddingX);
        int windowY = (int)Math.Round(hudTop - paddingY);
        var position = new PixelPoint(windowX, windowY);
        Position = _windowLayer?.ToWindowPosition(position) ?? position;
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

    public bool IsPointInTopCenterHotZone(PixelPoint screenPoint, int width = 240, int height = 20)
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

    public bool IsPointInConfiguredHotZone(PixelPoint screenPoint)
    {
        var screen=ResolveScreen(_settings.MonitorIndex);
        return _settings.HotZoneEnabled && screen is not null && HudHotZone.Bounds(screen.Bounds,_settings).Contains(screenPoint);
    }

    private void EnsureInputHitTest()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Avalonia may refresh native HWND extended styles after Show(), Topmost changes
        // or other window-state transitions. Re-apply click-through every time the HUD
        // is presented instead of assuming a one-time style write remains forever.
        var handle = this.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;

        _windowLayer ??= new WindowsHudLayer(handle.Handle);
        _windowLayer.Apply(_persistent && _settings.PersistentLayer == PersistentHudLayer.Desktop, Topmost);

        if (_hitTest is not null && _hitTest.Handle != handle.Handle)
        {
            _hitTest.Dispose();
            _hitTest = null;
        }

        if (_hitTest is null)
            _hitTest = WindowsHudHitTest.TryAttach(handle.Handle, IsPointInsideVisibleHud);

        _hitTest?.Reapply();
        _interaction ??= new WindowsHudInteraction(handle.Handle,IsPointInsideVisibleHud,() => { if (CanCycleOnClick) HudClicked?.Invoke(); },GetVisibleHudBounds);
        _interaction.SetEnabled(CanCycleOnClick && _persistent);
        if (CanCycleOnClick && _persistent) _inputRegionTimer.Start(); else _inputRegionTimer.Stop();
    }

    private PixelRect GetVisibleHudBounds()
    {
        if (_closed || !IsVisible || Pill.Opacity<.02) return default;
        var a=Pill.PointToScreen(new Point(0,0));var b=Pill.PointToScreen(new Point(Pill.Bounds.Width,Pill.Bounds.Height));
        return new(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Abs(b.X-a.X),Math.Abs(b.Y-a.Y));
    }
    private bool IsPointInsideVisibleHud(PixelPoint screenPoint)
    {
        if (_closed || !IsVisible || !Pill.IsVisible) return false;
        var local=Pill.PointToClient(screenPoint);
        var bounds=new Rect(Pill.Bounds.Size);
        if (!bounds.Contains(local)) return false;
        double radius=Math.Min(bounds.Width,bounds.Height)/2;
        double centerX=Math.Clamp(local.X,radius,bounds.Width-radius);
        double centerY=bounds.Height/2;
        double dx=local.X-centerX,dy=local.Y-centerY;
        return dx*dx+dy*dy <= radius*radius;

    }
}
