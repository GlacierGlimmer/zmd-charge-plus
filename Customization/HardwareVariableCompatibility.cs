using System.Security.Cryptography;
using System.Text;

namespace EndfieldChargePlus.Customization;

// Library visibility is separate from runtime support: old templates, presets,
// expressions and imported configurations must retain their original semantics.
internal static class HardwareVariableCompatibility
{
    private static readonly object Gate=new();
    private static readonly Dictionary<string,string> Replaced=new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Summaries=new(StringComparer.OrdinalIgnoreCase){
        "cpu.temperature_max","cpu.core.temperature_avg","cpu.power_max","system.motherboard_temperature","system.fan_speed","system.fan_speed_percent"};
    internal static bool IsSummary(string key)=>Summaries.Contains(key);
    internal static bool IsDuplicate(string key)
    {
        if(key is "cpu.temperature" or "cpu.temperature_c")return true;
        lock(Gate)return Replaced.ContainsKey(key);
    }
    internal static void Observe(HardwareReading reading)
    {
        if(reading.Value is null || reading.Value is double number && !double.IsFinite(number))return;
        string id=reading.Id,name=reading.Name;
        bool cpu=id.Contains("cpu",StringComparison.OrdinalIgnoreCase),gpu=id.Contains("/gpu",StringComparison.OrdinalIgnoreCase);
        lock(Gate)
        {
            void Replace(string legacy,string suffix="value")=>Replaced[legacy]=reading.Key+"."+suffix;
            bool Has(string token)=>name.Contains(token,StringComparison.OrdinalIgnoreCase);
            if(reading.Type=="Native" && reading.Source is "linux" or "macos")
            {
                // These fields are direct projections of the same canonical
                // metric, so their original key remains usable but is listed once.
                Replace(id);return;
            }
            if(reading.Source=="inventory")
            {
                switch(id.ToLowerInvariant())
                {
                    case "bios.version":Replace("system.bios_version");break;
                    case "bios.date":Replace("system.bios_date");break;
                    case "board.manufacturername":Replace("system.motherboard_manufacturer");break;
                    case "board.productname":Replace("system.motherboard_model");break;
                    case "processor.0.version":Replace("cpu.name");break;
                    case "cpuid.vendor":Replace("cpu.manufacturer");break;
                }
                return;
            }
            if(reading.Type=="Fan" && reading.Value is double rpm && rpm>=0)
            {
                string original=reading.Source=="lhm" ? id : reading.Source+":"+id;
                string fanId=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original)))[..12].ToLowerInvariant();
                Replace($"system.fan.{fanId}.rpm");Replace($"system.fan.{fanId}.name","name");
            }
            if(reading.Source=="aida64")
            {
                if(id.Equals("VCPU",StringComparison.OrdinalIgnoreCase))Replace("cpu.core.voltage");
                if(id.Equals("SCPUFSB",StringComparison.OrdinalIgnoreCase))Replace("cpu.bus_speed");
            }
            else if(cpu)
            {
                if(reading.Type=="Voltage" && Has("Core"))Replace("cpu.core.voltage");
                if(reading.Type=="Clock" && (Has("Bus") || Has("BCLK")))Replace("cpu.bus_speed");
            }
            if(!gpu)return;
            switch(reading.Type)
            {
                case "Temperature":
                    if(Has("Core"))Replace("gpu.temperature");
                    if(Has("Hot Spot") || Has("Hotspot"))Replace("gpu.hotspot_temperature");
                    if(Has("Memory"))Replace("gpu.memory_junction_temperature");break;
                case "Voltage":if(Has("Core"))Replace("gpu.voltage_v");break;
                case "Clock":
                    if(Has("Core"))Replace("gpu.core_clock_mhz");
                    if(Has("Memory"))Replace("gpu.memory_clock_mhz");break;
                case "Power":if(Has("Total") || Has("Package"))Replace("gpu.power_w");break;
            }
        }
    }
}
