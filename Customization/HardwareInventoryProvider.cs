using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace EndfieldChargePlus.Customization;

internal sealed class HardwareInventoryProvider
{
    private IReadOnlyList<HardwareReading>? _cached;
    internal IReadOnlyList<HardwareReading> Read()
    {
        if(_cached is not null)return _cached;
        var values=new List<HardwareReading>();
        try {
            var bios=new SMBios();
            Add("bios",bios.Bios);Add("board",bios.Board);Add("system",bios.System);Add("enclosure",bios.SystemEnclosure);
            AddMany("memory",bios.MemoryDevices);AddMany("processor",bios.Processors);AddMany("cache",bios.ProcessorCaches);
        } catch(Exception ex) { Diagnostics.AppLog.Warn("SMBIOS inventory unavailable: "+ex.GetType().Name); }
        if(X86Base.IsSupported)
        {
            var leaf0=X86Base.CpuId(0,0);var leaf1=X86Base.CpuId(1,0);
            var vendor=new byte[12];BitConverter.GetBytes(leaf0.Ebx).CopyTo(vendor,0);BitConverter.GetBytes(leaf0.Edx).CopyTo(vendor,4);BitConverter.GetBytes(leaf0.Ecx).CopyTo(vendor,8);
            Cpuid("vendor",Encoding.ASCII.GetString(vendor));
            int family=(leaf1.Eax>>8)&15,model=(leaf1.Eax>>4)&15;
            if(family is 6 or 15)model|=((leaf1.Eax>>16)&15)<<4;
            if(family==15)family+=(leaf1.Eax>>20)&255;
            Cpuid("family",(double)family);Cpuid("model",(double)model);Cpuid("stepping",(double)(leaf1.Eax&15));
            var flags=new List<string>();
            foreach(var (name,set) in new (string,bool)[]{("SSE2",Sse2.IsSupported),("SSE3",Sse3.IsSupported),("SSSE3",Ssse3.IsSupported),
                ("SSE4.1",Sse41.IsSupported),("SSE4.2",Sse42.IsSupported),("AVX",Avx.IsSupported),("AVX2",Avx2.IsSupported),
                ("AVX512F",Avx512F.IsSupported),("AES",Aes.IsSupported),("FMA",Fma.IsSupported),("BMI1",Bmi1.IsSupported),("BMI2",Bmi2.IsSupported)})
                if(set)flags.Add(name);
            Cpuid("runtime_instructions",string.Join(", ",flags));
            Cpuid("signature","0x"+unchecked((uint)leaf1.Eax).ToString("X8",CultureInfo.InvariantCulture));
            void Cpuid(string key,object value)=>values.Add(new("inventory","cpuid."+key,"CPUID",key,"CPUID","",value));
        }
        return _cached=values;

        void AddMany(string group,IEnumerable? entries)
        {
            if(entries is null)return;int index=0;
            foreach(var entry in entries)Add(group+"."+index++,entry);
        }
        void Add(string group,object? item)
        {
            if(item is null)return;
            foreach(var property in item.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public))
            {
                if(!property.CanRead || property.GetIndexParameters().Length!=0)continue;
                object? value;try{value=property.GetValue(item);}catch{continue;}
                if(value is null || value is byte[])continue;
                string unit=property.Name switch {"CurrentSpeed" or "MaxSpeed" or "ExternalClock"=>"MHz","ConfiguredSpeed" or "Speed" when group.StartsWith("memory.")=>"MT/s",
                    "ConfiguredVoltage"=>"mV","Size" when group.StartsWith("memory.")=>"MiB","Size" when group.StartsWith("cache.")=>"KiB","Size" when group=="bios"=>"B",_=>""};
                object? scalar=value switch {string s when !string.IsNullOrWhiteSpace(s)=>s.Trim(),Enum e=>e.ToString(),DateTime date=>date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                    bool b=>b.ToString(),byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal=>Convert.ToDouble(value,CultureInfo.InvariantCulture),_=>null};
                if(scalar is null)continue;
                if(group.StartsWith("cache.") && property.Name=="Size" && scalar is double cacheSize)
                    scalar=cacheSize==65535 ? "Unknown" : ((int)cacheSize&0x8000)!=0 ? ((int)cacheSize&0x7fff)*64d : cacheSize;
                // SMBIOS reports unspecified/sentinel values verbatim; they are
                // firmware data, never substituted for live sensor measurements.
                values.Add(new("inventory",group+"."+property.Name,group,property.Name,"SMBIOS",unit,scalar));
            }
        }
    }
}
