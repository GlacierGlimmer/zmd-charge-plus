using System.Net;
using System.Text;

namespace EndfieldChargePlus.Customization;

/// <summary>Public annual holiday notices, cached independently of app releases and API credentials.</summary>
public sealed class DeepSeekCalendarUpdater
{
    private const int MaxBytes = 65536;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly string directory;
    private readonly Func<int, Task<string?>> fetch;
    private readonly object gate = new();
    private DateTimeOffset nextAttempt;
    private int requestedYear;
    private Task pending = Task.CompletedTask;

    public DeepSeekCalendarUpdater(string cacheDirectory, Func<int, Task<string?>>? fetchAnnual = null)
    {
        directory = cacheDirectory;
        fetch = fetchAnnual ?? DownloadAsync;
    }

    // The sampler never waits for the network. Year rollover bypasses the daily throttle.
    public Task RequestRefresh(DateTimeOffset instant)
    {
        int year = instant.ToOffset(TimeSpan.FromHours(8)).Year;
        lock (gate)
        {
            if (!pending.IsCompleted) return pending;
            if (year == requestedYear && instant < nextAttempt) return pending;
            requestedYear = year;
            nextAttempt = instant.AddDays(1);
            pending = Task.Run(() => RefreshAsync(year));
            return pending;
        }
    }

    private async Task RefreshAsync(int year)
    {
        // Read all three caches first: an offline request must not delay the following year's cache.
        foreach (int y in new[] { year - 1, year, year + 1 })
        {
            try
            {
                string file = Path.Combine(directory, $"{y}.json");
                if (File.Exists(file) && new FileInfo(file).Length <= MaxBytes)
                    DeepSeekPeriodCalendar.TryInstallAnnualJson(await File.ReadAllTextAsync(file), y);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (int y in new[] { year - 1, year, year + 1 })
        {
            string? temporary = null;
            try
            {
                string? json = await fetch(y);
                if (json is null || Encoding.UTF8.GetByteCount(json) > MaxBytes || !DeepSeekPeriodCalendar.TryInstallAnnualJson(json, y)) continue;
                Directory.CreateDirectory(directory);
                temporary = Path.Combine(directory, Path.GetRandomFileName());
                await File.WriteAllTextAsync(temporary, json);
                File.Move(temporary, Path.Combine(directory, $"{y}.json"), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException) { }
            finally { if (temporary is not null) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
        }
    }

    private static async Task<string?> DownloadAsync(int year)
    {
        // Both endpoints distribute the same public holiday-cn dataset; no DeepSeek key is sent.
        foreach (string prefix in new[] { "https://cdn.jsdelivr.net/gh/NateScarlet/holiday-cn@master/", "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/" })
        {
            try
            {
                using var response = await Http.GetAsync($"{prefix}{year}.json", HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == HttpStatusCode.NotFound) continue;
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes) continue;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var content = new MemoryStream();
                var buffer = new byte[4096];
                int read;
                while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (content.Length + read > MaxBytes) throw new IOException("Holiday response too large.");
                    content.Write(buffer, 0, read);
                }
                string json = Encoding.UTF8.GetString(content.ToArray());
                if (DeepSeekPeriodCalendar.IsValidAnnualJson(json, year)) return json;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException) { }
        }
        return null;
    }
}
