namespace EndfieldChargePlus.Settings;

// Gesture preferences only. Settings use one fixed dark visual style.
// Old Appearance/Accent/Style JSON members are ignored by System.Text.Json.
public sealed record WorkspacePreferences
{
    public const string DefaultAccent = "#FFFA00";
    public bool RadialEnabled { get; init; } = true;
    public int HoldMilliseconds { get; init; } = 220;
    public int DragDistance { get; init; } = 12;
    public int RingDiameter { get; init; } = 360;
    public static WorkspacePreferences Normalize(WorkspacePreferences? value)
    {
        value ??= new();
        return value with {
            HoldMilliseconds = Math.Clamp(value.HoldMilliseconds, 120, 800),
            DragDistance = Math.Clamp(value.DragDistance, 6, 40),
            RingDiameter = Math.Clamp(value.RingDiameter, 280, 480)
        };
    }
}
