using EndfieldChargePlus.Customization;
namespace EndfieldChargePlus.Settings;

public sealed record HudInstanceSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "HUD";
    public bool Enabled { get; init; } = true;
    public AppSettings Settings { get; init; } = new();
    public static AppSettings ForNewInstance(AppSettings source,IEnumerable<AppSettings> existing)
    {
        // Separate new HUDs using an explicit available anchor; do not silently
        // turn the user's fine adjustment into a growing 80px instance counter.
        int domain=source.PositionMode==HudPositionMode.Preset && (int)source.HudPosition>=9 ? 9 : 0;
        int current=(int)source.HudPosition%9;
        var occupied=existing.Where(s=>s.MonitorIndex==source.MonitorIndex && s.PositionMode==HudPositionMode.Preset)
            .Select(s=>(int)s.HudPosition%9).ToHashSet();
        int next=Enumerable.Range(1,9).Select(i=>(current+i)%9).FirstOrDefault(i=>!occupied.Contains(i),current);
        return source with { MultiHudEnabled=false,HudInstances=new(),AlwaysVisible=true,
            PositionMode=HudPositionMode.Preset,HudPosition=(HudPosition)(domain+next),HudOffsetX=0,HudOffsetY=0 };
    }
}
