namespace EndfieldChargePlus.Customization;

internal static class HardwareStatusVariables
{
    private static readonly (string Key,string Zh,string En,bool Number)[] Fields={
        ("hardware.driver.installed","传感器驱动已安装","Sensor driver installed",false),
        ("hardware.driver.version","传感器驱动版本","Sensor driver version",false),
        ("hardware.driver.status","传感器驱动状态","Sensor driver status",false),
        ("hardware.elevated","当前为管理员身份","Running as administrator",false),
        ("hardware.sensor_count","有效传感器数量","Available sensor count",true),
        ("hardware.aida64.available","AIDA64 数据可用","AIDA64 data available",false),
        ("hardware.aida64.transport","AIDA64 数据接口","AIDA64 transport",false),
        ("hardware.cpu_temperature_status","CPU 温度采集状态","CPU temperature status",false)};
    internal static IReadOnlyList<VariableDefinition> All=>Fields.Where(f=>OperatingSystem.IsWindows() ||
        !f.Key.StartsWith("hardware.driver.",StringComparison.Ordinal) && !f.Key.StartsWith("hardware.aida64.",StringComparison.Ordinal))
        .Select(f=>new VariableDefinition(f.Key,f.Zh,"硬件传感器","真实采集状态及不可用原因。",f.Number ? "数值" : f.Key.EndsWith("installed") || f.Key.EndsWith("available") || f.Key.EndsWith("elevated") ? "布尔" : "文本","","状态",f.Number ? "0" : "无需格式化")).ToArray();
    internal static VariableDefinition English(VariableDefinition item)=>item with {Name=Fields.First(f=>f.Key==item.Key).En,Category="Hardware Sensors",Description="Actual collection status and unavailable reason.",ValueType=item.ValueType switch {"数值"=>"Number","布尔"=>"Boolean",_=>"Text"},RecommendedUse="Status",RecommendedFormats=item.RecommendedFormats.Replace("无需格式化","None")};
}
