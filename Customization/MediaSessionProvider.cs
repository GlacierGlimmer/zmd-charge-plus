using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace EndfieldChargePlus.Customization;

/// <summary>
/// 通过 Windows GSMTC（Global System Media Transport Controls）原生读取"当前播放"媒体信息。
///
/// 这是 Windows 官方媒体会话 API：网易云音乐、Spotify、Edge 网页播放、Groove 等
/// 注册了系统媒体控制的播放器都会出现在这里。只读，不发送任何播放控制命令。
///
/// 位置外推：GSMTC 的 <c>Position</c> 是"最后一次上报的位置"，播放中需要按
/// <c>LastUpdatedTime</c> 补上经过的时间，否则进度环会卡在旧值上。
/// </summary>
internal sealed class MediaSessionProvider : IDisposable
{
    // 媒体元数据变化不频繁，1 秒刷新一次足够，也避免频繁跨进程调用。
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(900);

    // 单个 GSMTC 调用加超时：某个播放器卡死时不能让整个快照挂住。
    private static readonly TimeSpan CallTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private MediaSnapshot _snapshot = MediaSnapshot.Empty;
    private DateTime _nextRefresh = DateTime.MinValue;

    public async Task EnrichAsync(
        IDictionary<string, object?> v,
        HashSet<string>? requested,
        CancellationToken ct)
    {
        // 只有模板真的引用了 media.* 才去读，保持 VariableHub 的按需采集语义。
        if (requested is not null
            && !requested.Any(k => k.StartsWith("media.", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow >= _nextRefresh)
            {
                _snapshot = await ReadSnapshotAsync(ct).ConfigureAwait(false);
                _nextRefresh = DateTime.UtcNow.Add(RefreshInterval);
            }

            Write(v, _snapshot);
        }
        catch
        {
            // 媒体信息是可选的：读不到就保持上一次的值，绝不抛给调用方。
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MediaSnapshot> ReadSnapshotAsync(CancellationToken ct)
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync().AsTask().ConfigureAwait(false);

            var sessions = _manager.GetSessions();
            if (sessions is null || sessions.Count == 0)
                return MediaSnapshot.Empty;

            // 多个会话同时存在时优先选"正在播放"的那个。
            GlobalSystemMediaTransportControlsSession? session = null;
            foreach (var candidate in sessions)
            {
                try
                {
                    if (candidate.GetPlaybackInfo()?.PlaybackStatus
                        == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        session = candidate;
                        break;
                    }
                }
                catch { }
            }
            session ??= sessions[0];

            string title = "", artist = "", album = "";
            try
            {
                var props = await session.TryGetMediaPropertiesAsync()
                    .AsTask().WaitAsync(CallTimeout, ct).ConfigureAwait(false);
                if (props is not null)
                {
                    title = props.Title ?? "";
                    artist = props.Artist ?? "";
                    album = props.AlbumTitle ?? "";
                }
            }
            catch { }

            var status = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
            try
            {
                var info = session.GetPlaybackInfo();
                if (info is not null) status = info.PlaybackStatus;
            }
            catch { }

            double position = 0, duration = 0, progress = 0;
            bool hasTimeline = false;
            try
            {
                var tl = session.GetTimelineProperties();
                if (tl is not null)
                {
                    double start = tl.StartTime.TotalSeconds;
                    double end = tl.EndTime.TotalSeconds;
                    double pos = tl.Position.TotalSeconds;
                    duration = end - start;

                    if (duration > 0)
                    {
                        bool playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        if (playing)
                        {
                            double elapsed = (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
                            if (elapsed > 0 && elapsed < 3600) pos += elapsed;
                        }

                        if (pos < start) pos = start;
                        if (pos > end) pos = end;

                        position = pos - start;
                        progress = Math.Clamp(position / duration * 100d, 0d, 100d);
                        hasTimeline = true;
                    }
                }
            }
            catch { }

            string app = "";
            try { app = session.SourceAppUserModelId ?? ""; } catch { }

            return new MediaSnapshot(
                Available: true,
                App: app,
                Title: title,
                Artist: artist,
                Album: album,
                Status: status,
                PositionSeconds: position,
                DurationSeconds: duration,
                ProgressPercent: progress,
                HasTimeline: hasTimeline);
        }
        catch
        {
            // 会话管理器失效（媒体服务重启等）：丢弃缓存，下次重建。
            _manager = null;
            return MediaSnapshot.Empty;
        }
    }

    private static void Write(IDictionary<string, object?> v, MediaSnapshot s)
    {
        v["media.available"] = s.Available && !string.IsNullOrWhiteSpace(s.Title);
        v["media.app"] = s.App;
        v["media.title"] = string.IsNullOrWhiteSpace(s.Title)
            ? LocalizationManager.Text("未在播放", "Nothing playing")
            : s.Title;
        v["media.artist"] = s.Artist;
        v["media.album"] = s.Album;
        v["media.title_artist"] = string.IsNullOrWhiteSpace(s.Artist)
            ? v["media.title"]
            : string.Format(CultureInfo.InvariantCulture, "{0} - {1}", s.Artist, s.Title);

        v["media.status_raw"] = s.Status.ToString();
        v["media.status"] = s.Status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing =>
                LocalizationManager.Text("播放中", "Playing"),
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused =>
                LocalizationManager.Text("已暂停", "Paused"),
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped =>
                LocalizationManager.Text("已停止", "Stopped"),
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing =>
                LocalizationManager.Text("切换中", "Changing"),
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened =>
                LocalizationManager.Text("已就绪", "Opened"),
            _ => LocalizationManager.Text("无媒体", "No media")
        };

        bool playing = s.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        v["media.is_playing"] = playing;
        v["media.is_paused"] = s.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;

        v["media.position"] = Math.Round(s.PositionSeconds, 1);
        v["media.duration"] = Math.Round(s.DurationSeconds, 1);
        v["media.progress"] = Math.Round(s.ProgressPercent, 1);
        v["media.has_timeline"] = s.HasTimeline;

        v["media.position_text"] = FormatClock(s.PositionSeconds);
        v["media.duration_text"] = FormatClock(s.DurationSeconds);
        v["media.remaining_text"] = FormatClock(Math.Max(0d, s.DurationSeconds - s.PositionSeconds));

        v["media.progress_text"] = string.Format(
            CultureInfo.InvariantCulture, "{0:0}%", s.ProgressPercent);
    }

    private static string FormatClock(double seconds)
    {
        if (seconds <= 0) return "--:--";
        var ts = TimeSpan.FromSeconds(Math.Floor(seconds));
        return ts.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", ts.Minutes, ts.Seconds);
    }

    public void Dispose() => _gate.Dispose();

    private readonly record struct MediaSnapshot(
        bool Available,
        string App,
        string Title,
        string Artist,
        string Album,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus Status,
        double PositionSeconds,
        double DurationSeconds,
        double ProgressPercent,
        bool HasTimeline)
    {
        public static MediaSnapshot Empty { get; } = new(
            false, "", "", "", "",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed,
            0d, 0d, 0d, false);
    }
}
