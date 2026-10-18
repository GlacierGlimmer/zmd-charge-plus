using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus.Customization;

internal sealed partial class AdvancedVariableProvider
{
    private readonly Aida64SensorSource _aida64=new();
    private readonly HardwareInventoryProvider _inventory=new();
    private long _nextExternalWmi;
    private IReadOnlyList<HardwareReading> _externalWmi=Array.Empty<HardwareReading>();
    private void AddHardwareTelemetry(IDictionary<string,object?> values)
    {
        EnsureHardware();
        var inventory=_inventory.Read();HardwareTelemetryCatalog.Publish(inventory);
        HardwareTelemetryCatalog.Write(values,_hardware.Sensors);HardwareTelemetryCatalog.Write(values,inventory);
        HardwareTelemetryCatalog.Write(values,_hardware.Properties);
        PutIfNumber(values,"cpu.temperature",_hardware.CpuTemperatureMax);PutIfNumber(values,"cpu.temperature_c",_hardware.CpuTemperatureMax);
        PutIfNumber(values,"cpu.temperature_max",_hardware.CpuTemperatureMax);PutIfNumber(values,"cpu.core.temperature_avg",_hardware.CpuCoreTemperatureAvg);
        var driver=HardwareSensorDriver.Inspect();
        values["hardware.driver.installed"]=driver.Installed;values["hardware.driver.version"]=driver.Version;
        values["hardware.elevated"]=driver.Elevated;values["hardware.driver.status"]=driver.Description;
        values["hardware.sensor_count"]=_hardware.Sensors.Count(s=>s.Value is not null);
        values["hardware.aida64.available"]=_aida64.Transport!="Unavailable";values["hardware.aida64.transport"]=_aida64.Transport;
        values["hardware.cpu_temperature_status"]=_hardware.CpuTemperatureMax.HasValue
            ? LocalizationManager.Text("可用","Available") : driver.Status!="Ready" ? driver.Description
            : LocalizationManager.Text("CPU 传感器不可用：硬件或驱动未返回有效温度。","CPU sensor unavailable: the hardware or driver returned no valid temperature.");
    }
    private void ReadExternalSensors(HardwareSnapshot result)
    {
        result.Sensors.AddRange(_aida64.Read());
        if(result.CpuTemperatureMax is null && Environment.TickCount64>=_nextExternalWmi)
        {
            _nextExternalWmi=Environment.TickCount64+5000;
            var readings=new List<HardwareReading>();
            foreach(string scope in new[]{@"root\LibreHardwareMonitor",@"root\OpenHardwareMonitor"})
            try {
                using var query=new ManagementObjectSearcher(scope,"SELECT Identifier, Name, SensorType, Value FROM Sensor");query.Options.Timeout=TimeSpan.FromSeconds(2);
                foreach(ManagementObject sensor in query.Get())
                using(sensor) {
                    string id=Convert.ToString(sensor["Identifier"]) ?? "";string type=Convert.ToString(sensor["SensorType"]) ?? "";
                    if(string.IsNullOrWhiteSpace(id) || sensor["Value"] is null)continue;
                    if(!double.TryParse(Convert.ToString(sensor["Value"],CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out var value) || !double.IsFinite(value))continue;
                    readings.Add(new(scope.EndsWith("LibreHardwareMonitor") ? "lhmwmi" : "ohmwmi",id,scope,Convert.ToString(sensor["Name"]) ?? id,type,HardwareTelemetryCatalog.Unit(type),value));
                }
                if(readings.Count>0)break;
            } catch { }
            _externalWmi=readings;
        }
        if(result.CpuTemperatureMax is null)result.Sensors.AddRange(_externalWmi);
    }
    private static void MergeExternalAliases(HardwareSnapshot result)
    {
        var external=result.Sensors.Where(s=>s.Source!="lhm" && s.Value is double).ToArray();
        result.CpuTemperatureMax ??= FindExternalCpuTemperature(external);
        var cores=external.Where(s=>s.Source=="aida64" && s.Id.StartsWith("TCC-",StringComparison.OrdinalIgnoreCase)).Select(s=>(double)s.Value!).Where(v=>v is >= -50 and <= 150).ToArray();
        if(result.CpuCoreTemperatureAvg is null)result.CpuCoreTemperatureAvg=cores.Length>0 ? cores.Average() : result.CpuTemperatureMax;
        double? Aida(string id)=>external.FirstOrDefault(s=>s.Source=="aida64" && s.Id.Equals(id,StringComparison.OrdinalIgnoreCase))?.Value as double?;
        result.CpuPowerMax ??= Aida("PCPUPKG") ?? Aida("PCPU");result.CpuVoltage ??= Aida("VCPU");result.CpuBusClockMhz ??= Aida("SCPUFSB");
        result.MotherboardTemperatureMax ??= Aida("TMOBO");
        foreach(var fan in external.Where(s=>s.Type=="Fan" && (double)s.Value!>=0))
        {
            string id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fan.Source+":"+fan.Id)))[..12].ToLowerInvariant();
            result.Fans[id]=(fan.Device+" / "+fan.Name,(double)fan.Value!);result.FanRpmMax=Math.Max(result.FanRpmMax ?? 0,(double)fan.Value!);
        }
    }
    internal static double? FindExternalCpuTemperature(IEnumerable<HardwareReading> readings)
    {
        var cpu=readings.Where(s=>s.Type=="Temperature" && s.Value is double && (s.Source=="aida64"
            ? Regex.IsMatch(s.Id,@"^(TCPU(\d*(PKG)?|DIO|SOCK|PKG|TCTL|IAC)|TCC-\d+-\d+)$",RegexOptions.IgnoreCase)
            : s.Id.Contains("cpu",StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Distance",StringComparison.OrdinalIgnoreCase)))
            .Select(s=>(double)s.Value!).Where(v=>double.IsFinite(v) && v is >= -50 and <= 150).ToArray();
        return cpu.Length>0 ? cpu.Max() : null;
    }
}
