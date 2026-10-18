using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace EndfieldChargePlus.Customization;

internal sealed class Aida64SensorSource
{
    private readonly string _sharedMemoryName;
    internal Aida64SensorSource(string sharedMemoryName="AIDA64_SensorValues") { _sharedMemoryName=sharedMemoryName; }
    private long _nextWmi;
    private IReadOnlyList<HardwareReading> _wmi=Array.Empty<HardwareReading>();
    internal string Transport { get; private set; }="Unavailable";
    internal IReadOnlyList<HardwareReading> Read()
    {
        // No process is started, no AIDA64 settings are changed. All transports
        // are official read-only exports selected by the user in AIDA64.
        try {
            using var map=MemoryMappedFile.OpenExisting(_sharedMemoryName,MemoryMappedFileRights.Read);
            using var view=map.CreateViewStream(0,0,MemoryMappedFileAccess.Read);
            int length=(int)Math.Min(view.Length,1024*1024);var bytes=new byte[length];
            // AIDA64 has no reader lock. Retry a bounded number of times if a
            // concurrent producer update leaves an incomplete XML fragment.
            for(int attempt=0;attempt<3;attempt++)
            {
                view.Position=0;int used=0,read;
                while(used<length && (read=view.Read(bytes,used,length-used))>0)used+=read;
                int end=Array.IndexOf(bytes,(byte)0,0,used);if(end<0)end=used;
                var result=Parse(Decode(bytes,end));
                if(result.Count>0) { Transport="SharedMemory";return result; }
                Thread.Yield();
            }
        } catch { }
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
        foreach(var registryView in new[]{RegistryView.Registry64,RegistryView.Registry32})
        try {
            using var root=RegistryKey.OpenBaseKey(hive,registryView);
            using var key=root.OpenSubKey(@"Software\FinalWire\AIDA64\SensorValues");
            if(key is null)continue;
            var entries=key.GetValueNames().ToDictionary(n=>n,n=>Convert.ToString(key.GetValue(n),CultureInfo.InvariantCulture) ?? "",StringComparer.OrdinalIgnoreCase);
            var result=ParseRegistry(entries);
            if(result.Count>0) { Transport="Registry";return result; }
        } catch { }
        if(Environment.TickCount64>=_nextWmi)
        {
            _nextWmi=Environment.TickCount64+5000;var entries=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            try {
                using var query=new ManagementObjectSearcher(@"root\WMI","SELECT * FROM AIDA64_SensorValues");
                query.Options.Timeout=TimeSpan.FromSeconds(2);
                foreach(ManagementObject item in query.Get())
                {
                    using(item) {
                        // AIDA64 exposes one record per sensor (ID/Label/Value).
                        string? id=Convert.ToString(item["ID"]);
                        if(!string.IsNullOrWhiteSpace(id)) { entries["Value."+id]=Convert.ToString(item["Value"],CultureInfo.InvariantCulture) ?? "";entries["Label."+id]=Convert.ToString(item["Label"]) ?? id; }
                    }
                }
            } catch { }
            _wmi=ParseRegistry(entries);
        }
        Transport=_wmi.Count>0 ? "WMI" : "Unavailable";
        return _wmi;
    }

    internal static IReadOnlyList<HardwareReading> Parse(string fragment)
    {
        if(string.IsNullOrWhiteSpace(fragment) || fragment.Length>1024*1024)return Array.Empty<HardwareReading>();
        try {
            using var reader=XmlReader.Create(new StringReader("<sensors>"+fragment+"</sensors>"),new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024+32});
            var document=XDocument.Load(reader);
            var readings=new Dictionary<string,HardwareReading>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in document.Root!.Descendants())
            {
                string? Field(string name)=>item.Elements().FirstOrDefault(e=>e.Name.LocalName.Equals(name,StringComparison.OrdinalIgnoreCase))?.Value;
                string id=Field("id") ?? "";string value=Field("value") ?? "";
                var reading=Create(id,Field("label") ?? id,value,item.Name.LocalName);
                if(reading is not null)readings[id]=reading;
            }
            return readings.Values.ToArray();
        } catch(XmlException) { return Array.Empty<HardwareReading>(); }
    }
    private static string Decode(byte[] bytes,int length)
    {
        try{return new UTF8Encoding(false,true).GetString(bytes,0,length);}
        catch(DecoderFallbackException) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)GetACP()).GetString(bytes,0,length);
        }
    }
    [DllImport("kernel32.dll")]private static extern uint GetACP();
    internal static IReadOnlyList<HardwareReading> ParseRegistry(IReadOnlyDictionary<string,string> values)
    {
        var readings=new List<HardwareReading>();
        foreach(var pair in values.Where(p=>p.Key.StartsWith("Value.",StringComparison.OrdinalIgnoreCase)))
        {
            string id=pair.Key[6..];values.TryGetValue("Label."+id,out var label);
            var reading=Create(id,label ?? id,pair.Value,"");if(reading is not null)readings.Add(reading);
        }
        return readings;
    }
    private static HardwareReading? Create(string id,string label,string text,string type)
    {
        if(string.IsNullOrWhiteSpace(id) || id.Length>256 || string.IsNullOrWhiteSpace(text))return null;
        // IDs, not translated/custom labels, determine physical quantity.
        type=type.ToLowerInvariant() switch {"temp"=>"Temperature","fan"=>"Fan","volt"=>"Voltage","pwr"=>"Power","curr"=>"Current",_=>type};
        if(string.IsNullOrWhiteSpace(type))type=char.ToUpperInvariant(id[0]) switch {'T'=>"Temperature",'F'=>"Fan",'V'=>"Voltage",'P'=>"Power",'C'=>"Current",_=>"System"};
        if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number) || double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out number))
            return double.IsFinite(number) ? new("aida64",id,"AIDA64",label,type,HardwareTelemetryCatalog.Unit(type),number) : null;
        // Statuses, memory timings, dates and ratios are legitimate AIDA64 exports.
        if(type is "Temperature" or "Voltage" or "Fan" or "Power" or "Current")return null;
        return new("aida64",id,"AIDA64",label,type,"",text);
    }
}
