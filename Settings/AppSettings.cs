using EndfieldChargePlus.Customization;

namespace EndfieldChargePlus.Settings;

public sealed record AppSettings
{
    public const double DefaultGlobalScale = 0.8;
    public const double DefaultDisplayDurationSeconds = 6.0;
    public const double DefaultBounceStrength = 0.275;
    public const double DefaultRippleIntensity = 1.0;
    public const double DefaultRippleSpread = 1.0;
    public const double DefaultHudOpacity = 1.0;
    // Master switch. It is forced on at every application launch, while it can still be
    // turned off temporarily during the current session from the settings window.
    public bool HudEnabled { get; init; } = true;
    public bool StartWithWindows { get; init; } = false;
    public string HudToggleHotkey { get; init; } = "";
    public string HudHotkeyShowAnimation { get; init; } = "Full";
    // Auto or a supported BCP-47 language code. Auto follows the Windows UI language.
    public string UiLanguage { get; init; } = "Auto";

    public double GlobalScale { get; init; } = DefaultGlobalScale;
    // Independent accessibility preferences, shared by Settings and all HUD instances.
    public double GlobalFontScale { get; init; } = 1.0;
    public double GlobalUiScale { get; init; } = 1.0;
    public double DisplayDurationSeconds { get; init; } = DefaultDisplayDurationSeconds;
    public double BounceStrength { get; init; } = DefaultBounceStrength;
    public double RippleIntensity { get; init; } = DefaultRippleIntensity;
    public double RippleSpread { get; init; } = DefaultRippleSpread;
    public double HudOpacity { get; init; } = DefaultHudOpacity;
    public double SamplingIntervalSeconds { get; init; } = 1;

    public bool MultiHudEnabled { get; init; } = false;
    public List<HudInstanceSettings> HudInstances { get; init; } = new();

    public string HudFontFamily { get; init; } = "";
    public bool ClickToCycle { get; init; } = false;

    public bool AlwaysVisible { get; init; } = false;
    public bool HotZoneEnabled { get; init; } = true;
    public HudPosition HotZonePosition { get; init; } = HudPosition.TopCenter;
    public int HotZoneWidth { get; init; } = 240;
    public int HotZoneHeight { get; init; } = 20;
    public int HotZoneOffsetX { get; init; }
    public int HotZoneOffsetY { get; init; }
    public int HotZoneDwellMilliseconds { get; init; }
    public bool HotZoneAdvanceQueue { get; init; }
    public PersistentHudLayer PersistentLayer { get; init; } = PersistentHudLayer.Desktop;

    public HudPositionMode PositionMode { get; init; } = HudPositionMode.Preset;
    public HudPosition HudPosition { get; init; } = HudPosition.TopCenter;
    public int HudOffsetX { get; init; } = 0;
    public int HudOffsetY { get; init; } = 0;
    public int HudCustomX { get; init; } = 0;
    public int HudCustomY { get; init; } = 0;
    public int MonitorIndex { get; init; } = -1;

    public CustomHudSettings CustomHud { get; init; } = CustomHudSettings.CreateDefault();
}

public enum PersistentHudLayer
{
    Topmost,
    Desktop,
}

public enum HudPositionMode
{
    Preset,
    CustomCoordinates,
}

public enum HudPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    ScreenTopLeft,
    ScreenTopCenter,
    ScreenTopRight,
    ScreenCenterLeft,
    ScreenCenter,
    ScreenCenterRight,
    ScreenBottomLeft,
    ScreenBottomCenter,
    ScreenBottomRight,
}
