namespace EndfieldChargePlus.Customization;

public static class HardwareSensorCatalog
{
    private static VariableDefinition[] _fans = Array.Empty<VariableDefinition>();
    public static IReadOnlyList<VariableDefinition> FanDefinitions => Volatile.Read(ref _fans);

    internal static void PublishFans(IReadOnlyDictionary<string, (string Name, double Rpm)> fans)
    {
        var definitions = fans.OrderBy(x => x.Key, StringComparer.Ordinal).SelectMany(f => new[]
        {
            new VariableDefinition($"system.fan.{f.Key}.rpm", f.Value.Name, "系统", "此独立风扇传感器的转速。", "数值", "RPM", "左侧信息", "0"),
            new VariableDefinition($"system.fan.{f.Key}.name", f.Value.Name, "系统", "此独立风扇传感器的名称。", "文本", "", "左侧信息", ""),
        }).ToArray();
        Volatile.Write(ref _fans, definitions);
    }
}
