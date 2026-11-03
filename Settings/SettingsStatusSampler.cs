using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using EndfieldChargePlus.Customization;

namespace EndfieldChargePlus.Settings;

internal sealed record GpuStatusReading(string Id,string Name,double? Capacity,double? Used,double? ProcessUsed,bool Unified);
internal sealed record SettingsStatusSample(double? Cpu, double? Memory, double? Gpu,
    double? AppCpu, double? AppMemory, double? AppGpu, double? AppMemoryBytes, double? AppGpuBytes)
{
    internal double? TotalMemoryBytes {get;init;}
    internal IReadOnlyList<GpuStatusReading> Adapters {get;init;}=Array.Empty<GpuStatusReading>();
}

// One sampler per open settings window. No hardware sensors, network or elevation.
internal sealed partial class SettingsStatusSampler : IDisposable
{
    private readonly Process _process=Process.GetCurrentProcess();
    private readonly WindowsStatusMetrics _windows=new();
    private IReadOnlyList<GpuStatusReading> _otherAdapters=Array.Empty<GpuStatusReading>();
    private double? _lastCpu;
    private long _lastAt;
    internal SettingsStatusSample Read()
    {
        double? cpu=null, memory=null, totalMemory=null, gpu=null, appGpu=null, appGpuBytes=null;
        if(OperatingSystem.IsWindows())
        {
            (cpu,memory,totalMemory,gpu,appGpu,appGpuBytes)=_windows.Read();
        }
        else
        {
            var values=ReadOtherSystem();
            cpu=Number(values,"cpu.usage");memory=Number(values,"memory.usage");totalMemory=Number(values,"memory.total_bytes");
            gpu=Number(values,"gpu.dedicated_usage");appGpu=Number(values,"app.gpu_usage");appGpuBytes=Number(values,"app.gpu_bytes");
        }
        double? appCpu=null,appBytes=null;
        try
        {
            _process.Refresh();long at=Stopwatch.GetTimestamp();double time=_process.TotalProcessorTime.TotalSeconds;
            if(_lastCpu is {} previous && at>_lastAt && time>=previous)
                appCpu=Percent((time-previous)/((at-_lastAt)/(double)Stopwatch.Frequency)/Environment.ProcessorCount*100);
            _lastCpu=time;_lastAt=at;appBytes=_process.WorkingSet64;
        }
        catch(InvalidOperationException) { }
        return new(Percent(cpu),Percent(memory),Percent(gpu),appCpu,Ratio(appBytes,totalMemory),Percent(appGpu),appBytes,appGpuBytes)
            {TotalMemoryBytes=totalMemory,Adapters=OperatingSystem.IsWindows()?_windows.Adapters:_otherAdapters};
    }
    internal static double? Number(IDictionary<string,object?> values,string key)=>values.TryGetValue(key,out var v) && v is not null && double.TryParse(Convert.ToString(v,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out var n) && double.IsFinite(n)?n:null;
    internal static double? Ratio(double? used,double? total)=>used>=0 && total>0 ? Percent(used/total*100):null;
    internal static double? Percent(double? n)=>n is {} value && double.IsFinite(value)?Math.Clamp(value,0,100):null;
    private partial IDictionary<string,object?> ReadOtherSystem();
    public void Dispose(){_windows.Dispose();_process.Dispose();}
}

// Windows performance counters are language-neutral and sampled on a worker.
// Per-adapter aliases are collapsed before adding physical dedicated VRAM totals.
internal sealed class WindowsStatusMetrics : IDisposable
{
    internal IReadOnlyList<GpuStatusReading> Adapters {get;private set;}=Array.Empty<GpuStatusReading>();
    private IntPtr _query,_systemGpu,_processGpu;
    private bool _attempted;
    private ulong _idle,_kernel,_user;
    private bool _primed;
    internal (double? Cpu,double? Memory,double? TotalMemory,double? Gpu,double? AppGpu,double? AppGpuBytes) Read()
    {
        double? cpu=null,memory=null,total=null,gpu=null,appGpu=null,appBytes=null;
        // A failed counter refresh must not leave old per-device readings in the tooltip.
        Adapters=Adapters.Select(a=>a with {Used=null,ProcessUsed=null}).ToArray();
        if(GetSystemTimes(out var idle,out var kernel,out var user))
        {
            if(_primed && idle>=_idle && kernel>=_kernel && user>=_user)
            {double sum=(double)(kernel-_kernel)+(user-_user);if(sum>0)cpu=100*(1-(idle-_idle)/sum);}
            _idle=idle;_kernel=kernel;_user=user;_primed=true;
        }
        var mem=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(GlobalMemoryStatusEx(ref mem)){total=mem.TotalPhysical;memory=mem.TotalPhysical==0?null:100d*(mem.TotalPhysical-mem.AvailablePhysical)/mem.TotalPhysical;}
        try
        {
            if(!_attempted){_attempted=true;if(PdhOpenQuery(null,UIntPtr.Zero,out _query)==0){PdhAddEnglishCounter(_query,@"\GPU Adapter Memory(*)\Dedicated Usage",UIntPtr.Zero,out _systemGpu);PdhAddEnglishCounter(_query,@"\GPU Process Memory(*)\Dedicated Usage",UIntPtr.Zero,out _processGpu);}}
            if(_query!=IntPtr.Zero && PdhCollectQueryData(_query)==0)
            {
                var global=Values(_systemGpu);var processes=Values(_processGpu);
                Adapters=BuildAdapters(GpuAdapterCatalog.GetAdapters(),global,processes,Environment.ProcessId);
                (gpu,appGpu,appBytes)=AggregateAdapters(Adapters);
            }
        }
        catch(Exception ex) when(ex is not OutOfMemoryException) { /* Unsupported counter/driver: unavailable, not zero. */ }
        return(cpu,memory,total,gpu,appGpu,appBytes);
    }
    internal static (double? System,double? Process,double? Bytes) AggregateAdapters(IReadOnlyList<GpuStatusReading> adapters)
    {
        double? system=null,process=null,bytes=null;
        var physical=adapters.Where(a=>a.Capacity>0).ToArray();
        if(physical.Length>0 && physical.All(a=>a.Used is not null))system=SettingsStatusSampler.Ratio(physical.Sum(a=>a.Used!.Value),physical.Sum(a=>a.Capacity!.Value));
        if(adapters.Count>0 && adapters.All(a=>a.ProcessUsed is not null))
        {
            bytes=adapters.Sum(a=>a.ProcessUsed!.Value);
            if(adapters.All(a=>a.Capacity>0))process=SettingsStatusSampler.Ratio(bytes,adapters.Sum(a=>a.Capacity!.Value));
        }
        return(system,process,bytes);
    }
    internal static IReadOnlyList<GpuStatusReading> BuildAdapters(IReadOnlyList<GpuAdapterInfo> adapters,
        IReadOnlyDictionary<string,double>? global,IReadOnlyDictionary<string,double>? processes,int pid)
    {
        return adapters.Select(a=>
        {
            var system=global?.Where(p=>a.MatchesCounterName(p.Key)).Select(p=>p.Value).ToArray();
            bool processSupported=processes is not null && processes.Keys.Any(a.MatchesCounterName);
            double? own=processSupported?processes!.Where(p=>p.Key.StartsWith($"pid_{pid}_",StringComparison.OrdinalIgnoreCase) && a.MatchesCounterName(p.Key)).Select(p=>p.Value).DefaultIfEmpty(0).Max():null;
            return new GpuStatusReading(a.Id,a.Name,a.DedicatedMemoryBytes>0?a.DedicatedMemoryBytes:null,
                system is {Length:>0}?system.Max():null,own,a.UsesUnifiedMemory);
        }).ToArray();
    }
    private static Dictionary<string,double>? Values(IntPtr counter)
    {
        if(counter==IntPtr.Zero)return null;uint size=0,count=0;
        const uint MoreData=0x800007D2,Format=0x200|0x8000;
        if(PdhGetFormattedCounterArray(counter,Format,ref size,out count,IntPtr.Zero)!=MoreData || size==0 || size>16*1024*1024)return null;
        var buffer=Marshal.AllocHGlobal((int)size);
        try
        {
            if(PdhGetFormattedCounterArray(counter,Format,ref size,out count,buffer)!=0)return null;
            int stride=Marshal.SizeOf<CounterItem>();if((ulong)stride*count>size)return null;
            var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<count;i++){var item=Marshal.PtrToStructure<CounterItem>(buffer+i*stride);if(item.Data.Status>1 || !double.IsFinite(item.Data.Value) || item.Data.Value<0)continue;var name=Marshal.PtrToStringUni(item.Name);if(name is not null)values[name]=item.Data.Value;}
            return values;
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }
    public void Dispose(){if(_query!=IntPtr.Zero){PdhCloseQuery(_query);_query=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus{public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,AvailableExtended;}
    [StructLayout(LayoutKind.Explicit,Size=16)] private struct CounterValue{[FieldOffset(0)]public uint Status;[FieldOffset(8)]public double Value;}
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem{public IntPtr Name;public CounterValue Data;}
    [DllImport("kernel32.dll")]private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32.dll")]private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("pdh.dll",EntryPoint="PdhOpenQueryW",CharSet=CharSet.Unicode)]private static extern uint PdhOpenQuery(string? source,UIntPtr data,out IntPtr query);
    [DllImport("pdh.dll",EntryPoint="PdhAddEnglishCounterW",CharSet=CharSet.Unicode)]private static extern uint PdhAddEnglishCounter(IntPtr query,string path,UIntPtr data,out IntPtr counter);
    [DllImport("pdh.dll")]private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll",EntryPoint="PdhGetFormattedCounterArrayW")]private static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint size,out uint count,IntPtr buffer);
    [DllImport("pdh.dll")]private static extern uint PdhCloseQuery(IntPtr query);
}
