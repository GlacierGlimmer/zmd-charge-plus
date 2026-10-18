using System.Globalization;
using Windows.Media.Control;

namespace EndfieldChargePlus.Customization;

// Read-only Windows GSMTC integration. One in-flight read is retained on timeout,
// so an unresponsive player cannot accumulate background reads on every sample.
internal sealed class MediaSessionProvider : IDisposable
{
    internal sealed record Reading(string? Title,string? Artist,string? Album,string? App,string Status,
        double Start=0,double End=0,double Position=0,DateTimeOffset Updated=default,double Rate=1,bool HasTimeline=false);
    private readonly Func<Task<Reading?>> _read;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Task<Reading?>? _pending;
    private Reading? _reading;
    private bool _disposed;
    internal MediaSessionProvider(Func<Task<Reading?>>? read=null) { _read=read ?? ReadAsync; }

    internal async Task EnrichAsync(IDictionary<string,object?> values,CancellationToken token)
    {
        if (_disposed) return;
        if (_pending is null || _pending.IsCompleted) _pending=Task.Run(_read);
        try { _reading=await _pending.WaitAsync(TimeSpan.FromMilliseconds(1500),token).ConfigureAwait(false); }
        catch (OperationCanceledException) when(token.IsCancellationRequested) { throw; }
        catch { _reading=new(null,null,null,null,"Unavailable"); }
        if(!_disposed)WriteCached(values);
    }

    private async Task<Reading?> ReadAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().ConfigureAwait(false);
            var sessions=_manager.GetSessions();
            var current=_manager.GetCurrentSession();
            bool Playing(GlobalSystemMediaTransportControlsSession s) => s.GetPlaybackInfo()?.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var session=current is not null && Playing(current) ? current : sessions.FirstOrDefault(Playing) ?? current ?? sessions.FirstOrDefault();
            if(session is null) return null;
            var info=session.GetPlaybackInfo();
            var properties=await session.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
            var timeline=session.GetTimelineProperties();
            return new(properties?.Title,properties?.Artist,properties?.AlbumTitle,session.SourceAppUserModelId,
                info?.PlaybackStatus.ToString() ?? "Unavailable",timeline?.StartTime.TotalSeconds ?? 0,
                timeline?.EndTime.TotalSeconds ?? 0,timeline?.Position.TotalSeconds ?? 0,
                timeline?.LastUpdatedTime ?? default,info?.PlaybackRate ?? 1,timeline is not null);
        }
        catch { _manager=null;return new(null,null,null,null,"Unavailable"); }
    }

    internal void WriteCached(IDictionary<string,object?> values) => Write(values,_reading,DateTimeOffset.UtcNow);

    internal static void Write(IDictionary<string,object?> v,Reading? s,DateTimeOffset now)
    {
        string status=s?.Status ?? "NoMedia";
        bool available=s is not null && status!="Unavailable";
        bool playing=available && status=="Playing";
        string statusText=status switch {
            "Playing"=>LocalizationManager.Text("播放中","Playing"),"Paused"=>LocalizationManager.Text("已暂停","Paused"),
            "Stopped"=>LocalizationManager.Text("已停止","Stopped"),"Changing"=>LocalizationManager.Text("切换中","Changing"),
            "Opened"=>LocalizationManager.Text("已就绪","Opened"),"Unavailable"=>LocalizationManager.Text("媒体信息不可用","Media unavailable"),
            _=>LocalizationManager.Text("未在播放","Nothing playing") };
        string? Text(string? value)=>available && !string.IsNullOrWhiteSpace(value) ? value : null;
        v["media.available"]=available;v["media.app"]=Text(s?.App);
        v["media.title"]=Text(s?.Title) ?? (s is null ? statusText : null);
        v["media.artist"]=Text(s?.Artist);v["media.album"]=Text(s?.Album);
        v["media.title_artist"]=Text(s?.Title) is { } title ? Text(s?.Artist) is { } artist ? artist+" - "+title : title : v["media.title"];
        v["media.status"]=statusText;v["media.status_raw"]=status;
        v["media.is_playing"]=playing;v["media.is_paused"]=available && status=="Paused";
        bool timeline=available && s!.HasTimeline && double.IsFinite(s.Start) && double.IsFinite(s.End)
            && double.IsFinite(s.Position) && s.End>s.Start;
        v["media.has_timeline"]=timeline;
        double? duration=timeline ? s!.End-s.Start : null;
        double? position=timeline ? Math.Clamp(s!.Position-s.Start,0,duration!.Value) : null;
        if(playing && timeline && s!.Updated!=default)
        {
            double elapsed=Math.Max(0,(now-s.Updated).TotalSeconds);
            double rate=double.IsFinite(s.Rate) && s.Rate>=0 ? s.Rate : 1;
            position=Math.Clamp(position!.Value+elapsed*rate,0,duration!.Value);
        }
        double? progress=timeline ? position/duration*100 : null;
        v["media.position"]=position;v["media.duration"]=duration;v["media.progress"]=progress;
        v["media.position_text"]=Clock(position);v["media.duration_text"]=Clock(duration);
        v["media.remaining_text"]=Clock(duration-position);
        v["media.progress_text"]=progress?.ToString("0",CultureInfo.InvariantCulture) is { } percent ? percent+"%" : null;
    }

    private static string? Clock(double? seconds)
    {
        if(seconds is null || !double.IsFinite(seconds.Value)) return null;
        long total=(long)Math.Clamp(Math.Floor(seconds.Value),0,long.MaxValue/2d);
        return total>=3600 ? $"{total/3600:00}:{total/60%60:00}:{total%60:00}" : $"{total/60:00}:{total%60:00}";
    }
    // Do not synchronously call IAsyncInfo.Cancel: a blocked media broker can
    // block inside that cancellation callback too. The single read stays bounded
    // in count and is never awaited during shutdown.
    public void Dispose() { _disposed=true;_reading=null; }
}
