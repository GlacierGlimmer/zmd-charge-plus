using System.Globalization;

namespace EndfieldChargePlus.Customization;

internal readonly record struct PresentMonFrame(int Pid,string Chain,int? Display,double Timestamp,long Window=0);

/// <summary>PresentMon 2.6 CSV: display attribution is optional, displayed timestamps are in milliseconds.</summary>
internal sealed class PresentMonCsvReader
{
    private Dictionary<string,int>? _columns;
    internal bool HasHeader => _columns is not null;
    internal bool TryRead(string line,out PresentMonFrame frame)
    {
        frame=default;
        var cells=FrameRateMetrics.ParseCsv(line);
        if (cells.Contains("ProcessID"))
        {
            _columns=cells.Select((value,index)=>(value,index)).GroupBy(p=>p.value)
                .ToDictionary(g=>g.Key,g=>g.First().index,StringComparer.OrdinalIgnoreCase);
            return false;
        }
        if (_columns is null) return false;
        string Get(string key)=>_columns.TryGetValue(key,out var index) && index<cells.Count ? cells[index].Trim() : "";
        if (!int.TryParse(Get("ProcessID"),out int pid) || pid<=0 || string.IsNullOrEmpty(Get("SwapChainAddress"))) return false;
        if (_columns.ContainsKey("DisplayedTimestamp"))
        {
            if (!double.TryParse(Get("DisplayedTimestamp"),NumberStyles.Float,CultureInfo.InvariantCulture,out var stamp) || !double.IsFinite(stamp) || stamp<=0) return false;
            string rawHandle=Get("WindowHandle");
            long.TryParse(rawHandle.StartsWith("0x",StringComparison.OrdinalIgnoreCase) ? rawHandle[2..] : rawHandle,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var hwnd);
            int? sourceId=int.TryParse(Get("VidPnSourceId"),out int id) && id>=0 ? id : null;
            frame=new(pid,Get("SwapChainAddress")+":"+Get("LayerIndex"),sourceId,stamp,hwnd);
            return true;
        }
        string start=Get("CPUStartQPCTime");
        if (start.Length==0) start=Get("CPUStartTime");
        if (!double.TryParse(start,NumberStyles.Float,CultureInfo.InvariantCulture,out double cpu) ||
            !double.TryParse(Get("DisplayLatency"),NumberStyles.Float,CultureInfo.InvariantCulture,out double latency) ||
            !double.IsFinite(cpu) || !double.IsFinite(latency) || latency<0) return false;
        // NA DisplayedTime denotes a dropped/non-displayed frame, regardless of available CPU data.
        string displayed=Get("DisplayedTime");
        if (displayed.Length>0 && (!double.TryParse(displayed,NumberStyles.Float,CultureInfo.InvariantCulture,out double duration) || duration<=0)) return false;
        string source=Get("VidPnSourceId"); int? display=null;
        if (uint.TryParse(source,out uint value) && value<int.MaxValue) display=(int)value;
        else if (source.StartsWith("0x",StringComparison.OrdinalIgnoreCase) && uint.TryParse(source[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value) && value<int.MaxValue) display=(int)value;
        frame=new(pid,Get("SwapChainAddress"),display,(cpu+latency)/1000d);
        return true;
    }
}
