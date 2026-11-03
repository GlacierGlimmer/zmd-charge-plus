using System.Globalization;

namespace EndfieldChargePlus.Customization;

public sealed record FrameTarget(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record FrameReading(double? Fps, string TargetName, string Status);

/// <summary>Counts real completed frame events using a monotonic clock. Zero is valid only after a real event.</summary>
public sealed class FrameEventWindow
{
    private readonly Queue<(long Arrived, double Timestamp)> _events = new();
    private readonly object _gate = new();
    private double _lastTimestamp = double.NaN;
    public bool Primed { get; private set; }
    public long LastArrival { get; private set; }
    public int RecentCount(long now) { lock (_gate) { Trim(now); return _events.Count; } }
    public void Add(double timestamp, long now)
    {
        if (!double.IsFinite(timestamp)) return;
        lock (_gate)
        {
            if (timestamp == _lastTimestamp) return;
            if (timestamp < _lastTimestamp) _events.Clear();
            _lastTimestamp=timestamp; LastArrival=now; Primed=true; _events.Enqueue((now,timestamp));
            Trim(now);
        }
    }
    private void Trim(long now) { while (_events.Count>0 && now-_events.Peek().Arrived>2000) _events.Dequeue(); }
    public double? Read(long now)
    {
        lock (_gate)
        {
            Trim(now);
            if (!Primed) return null;
            if (_events.Count<2) return _events.Count==0 ? 0 : null;
            var events=_events.ToArray(); double span=events[^1].Timestamp-events[0].Timestamp;
            return span>0 ? (events.Length-1)/span : null;
        }
    }
}

public static class FrameRateMetrics
{
    public static void Add(IDictionary<string,object?> values, string kind, FrameReading reading, double fullScale)
    {
        string prefix=$"frame.{kind}.";
        values[prefix+"status"]=FrameRateStatus.Localize(reading.Status);
        values[prefix+"name"]=reading.TargetName;
        values[prefix+"fps"]=reading.Fps;
        values[prefix+"percent"]=reading.Fps is double fps && double.IsFinite(fps) && fps>=0 && double.IsFinite(fullScale) && fullScale>0
            ? Math.Clamp(fps/fullScale*100,0,100) : null;
    }
    public static IReadOnlyList<string> ParseCsv(string line)
    {
        var result=new List<string>(); var value=new System.Text.StringBuilder(); bool quoted=false;
        for (int i=0;i<line.Length;i++)
        {
            char c=line[i];
            if (c=='"') { if (quoted && i+1<line.Length && line[i+1]=='"') { value.Append('"'); i++; } else quoted=!quoted; }
            else if (c==',' && !quoted) { result.Add(value.ToString()); value.Clear(); }
            else value.Append(c);
        }
        result.Add(value.ToString()); return result;
    }
}
