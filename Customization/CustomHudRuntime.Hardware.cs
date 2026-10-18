namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    internal Task<Dictionary<string,object?>> SnapshotHardwareAsync(CancellationToken token)=>
        _variables.SnapshotAsync(_settings,new[]{"hardware.sensor_count"},ct:token);
}
