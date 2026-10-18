using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EndfieldChargePlus.Customization;

internal sealed record HardwareReading(string Source,string Id,string Device,string Name,string Type,string Unit,
    object? Value,double? Minimum=null,double? Maximum=null)
{
    internal string Key => "sensor."+Source+"."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Id)))[..24].ToLowerInvariant();
    internal bool Numeric => Value is not string;
}

// Device names are display labels only. Keys are derived from the upstream ID,
// so renaming a sensor, changing language or reordering devices cannot break a HUD.
internal static class HardwareTelemetryCatalog
{
    private static readonly object Gate=new();
    private static readonly Dictionary<string,VariableDefinition> Known=new(StringComparer.OrdinalIgnoreCase);
    internal static IReadOnlyList<VariableDefinition> Definitions { get { lock(Gate)return Known.Values.ToArray(); } }
    internal static void Publish(IEnumerable<HardwareReading> readings)
    {
        lock(Gate)
        foreach(var r in readings)
        {
            HardwareVariableCompatibility.Observe(r);
            string name=r.Device+" / "+r.Name;
            Add(r.Key+".value",name,r.Numeric ? "数值" : "文本",r.Unit);
            if(r.Minimum.HasValue)Add(r.Key+".min",name+" / Min","数值",r.Unit);
            if(r.Maximum.HasValue)Add(r.Key+".max",name+" / Max","数值",r.Unit);
            Add(r.Key+".name",name+" / Name","文本","");
            Add(r.Key+".id",name+" / ID","文本","");
            Add(r.Key+".unit",name+" / Unit","文本","");
            void Add(string key,string label,string type,string unit) => Known[key]=new(key,label,
                r.Source=="aida64" ? "AIDA64" : r.Source=="inventory" ? "硬件信息" : "硬件传感器",
                $"{r.Source}: {r.Id} ({TypeLabel(r.Type)}). "+Topic(r)+" "+LocalizationManager.Text("硬件或数据源不提供时不可用。","Unavailable when the hardware or source does not provide it."),
                type,unit,"按数据含义决定",type=="数值" ? "0 / 0.0 / 0.00" : "无需格式化");
        }
    }
    private static string TypeLabel(string type)=>type switch {
        "Temperature"=>"温度 / Temperature","Voltage"=>"电压 / Voltage","Current"=>"电流 / Current","Power"=>"功耗 / Power",
        "Clock" or "Frequency"=>"频率 / "+type,"Load"=>"负载 / Load","Fan"=>"风扇 / Fan","Flow"=>"流量 / Flow",
        "Control" or "Level"=>"比例 / "+type,"Data" or "SmallData"=>"容量 / "+type,"Throughput"=>"吞吐 / Throughput",
        "Timing"=>"时序 / Timing","Energy"=>"能量 / Energy","Noise"=>"噪声 / Noise","Humidity"=>"湿度 / Humidity",_=>type};
    private static string Topic(HardwareReading reading)=>reading.Source!="inventory" ? "" : reading.Id.Split('.')[0] switch {
        "memory"=>"内存条 / Memory module","processor" or "cpuid"=>"处理器 / CPU","cache"=>"缓存 / Cache","board"=>"主板 / Motherboard",
        "bios"=>"BIOS 固件","system"=>"整机 / System","enclosure"=>"机箱 / Enclosure",_=>"硬件属性 / Hardware property"};
    internal static void Write(IDictionary<string,object?> values,IEnumerable<HardwareReading> readings)
    {
        foreach(var r in readings)
        {
            if(r.Value is double number && !double.IsFinite(number))continue;
            values[r.Key+".value"]=r.Value;
            if(r.Minimum is { } min && double.IsFinite(min))values[r.Key+".min"]=min;
            if(r.Maximum is { } max && double.IsFinite(max))values[r.Key+".max"]=max;
            values[r.Key+".name"]=r.Device+" / "+r.Name;values[r.Key+".id"]=r.Id;values[r.Key+".unit"]=r.Unit;
        }
    }
    internal static string Unit(string type)=>type switch {
        "Voltage"=>"V","Current"=>"A","Power"=>"W","Clock"=>"MHz","Temperature"=>"°C","Load" or "Control" or "Level"=>"%",
        "Frequency"=>"Hz","Fan"=>"RPM","Flow"=>"L/h","Data"=>"GiB","SmallData"=>"MiB","Throughput"=>"B/s",
        "TimeSpan"=>"s","Timing"=>"ns","Energy"=>"mWh","Noise"=>"dBA","Conductivity"=>"µS/cm","Humidity"=>"%",_=>"" };
}
