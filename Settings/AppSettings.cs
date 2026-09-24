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
    // Auto | zh-CN | en-US. Auto follows Windows UI culture; all zh-* cultures use Simplified Chinese.
    public string UiLanguage { get; init; } = "Auto";

    public double GlobalScale { get; init; } = DefaultGlobalScale;
    public double DisplayDurationSeconds { get; init; } = DefaultDisplayDurationSeconds;
    public double BounceStrength { get; init; } = DefaultBounceStrength;
    public double RippleIntensity { get; init; } = DefaultRippleIntensity;
    public double RippleSpread { get; init; } = DefaultRippleSpread;
    public double HudOpacity { get; init; } = DefaultHudOpacity;

    public bool AlwaysVisible { get; init; } = false;
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
}
