using System.Globalization;
using System.Text.Json;
using EndfieldChargePlus.Customization;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
DateTime? ExpectedDate(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null
    : DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture).ToOffset(TimeSpan.FromHours(8)).DateTime;
using var stream = typeof(DeepSeekPeriodCalendar).Assembly.GetManifestResourceStream("DeepSeekPeriodCases.json")!;
using var document = JsonDocument.Parse(stream);
foreach (var item in document.RootElement.EnumerateArray())
{
    string name = item.GetProperty("name").GetString()!;
    var now = DateTimeOffset.Parse(item.GetProperty("instant").GetString()!, CultureInfo.InvariantCulture);
    var result = DeepSeekPeriodCalendar.Evaluate(now);
    bool? expectedPeak = item.GetProperty("peak").ValueKind == JsonValueKind.Null ? null : item.GetProperty("peak").GetBoolean();
    Check(result.IsPeak == expectedPeak, name + ": period");
    Check(result.NextTransition == ExpectedDate(item.GetProperty("next")), name + ": next transition");
    Check(result.SegmentStart == ExpectedDate(item.GetProperty("start")), name + ": segment start");
    if (result.NextTransition is { } next && result.SegmentStart is { } start)
    {
        Check(Math.Abs(result.RemainingSeconds!.Value - (next - result.Beijing).TotalSeconds) < 0.001, name + ": countdown");
        Check(Math.Abs(result.Progress!.Value - (result.Beijing - start).TotalSeconds / (next - start).TotalSeconds * 100) < 0.001, name + ": progress");
    }
    else Check(result.Progress is null, name + ": unknown progress must not be zero");
}
foreach (var text in new[] { "2026-01-04", "2026-02-14", "2026-02-28", "2026-05-09", "2026-09-20", "2026-10-10" })
{
    var day = DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    Check(DeepSeekPeriodCalendar.IsChinaWorkday(day) == true, text + ": statutory makeup workday");
    Check(DeepSeekPeriodCalendar.IsPeakDay(day) == false, text + ": official weekends remain off-peak");
}
Check(DeepSeekPeriodCalendar.IsChinaWorkday(new(2027, 1, 4)) is null, "Unannounced year cannot be guessed");
using (var embedded = typeof(DeepSeekPeriodCalendar).Assembly.GetManifestResourceStream("EndfieldChargePlus.china-holidays.json")!)
using (var baseline = JsonDocument.Parse(embedded))
{
    foreach (int year in Enumerable.Range(2027, 4))
    {
        var fields = baseline.RootElement.GetProperty("years").GetProperty(year.ToString());
        Check(fields.GetProperty("coverage").GetString() == "statutory-only", $"{year}: unannounced schedule marked partial");
        foreach (var range in fields.GetProperty("holidays").EnumerateArray())
        {
            var ends = range.GetString()!.Split('/');
            var first = DateOnly.Parse(ends[0]); var last = DateOnly.Parse(ends[^1]);
            for (var day = first; day <= last; day = day.AddDays(1))
                Check(DeepSeekPeriodCalendar.IsPeakDay(day) == false, $"{day}: statutory holiday idle offline");
        }
    }
}
using var fixtureStream = typeof(DeepSeekPeriodCalendar).Assembly.GetManifestResourceStream("HolidayAnnualFixture.json")!;
string annual = new StreamReader(fixtureStream).ReadToEnd(); // Synthetic test year, not a future forecast.
Check(DeepSeekPeriodCalendar.IsValidAnnualJson(annual, 2040), "Complete synthetic annual fixture valid");
Check(!DeepSeekPeriodCalendar.IsValidAnnualJson(annual, 2041), "Wrong response year rejected");
Check(!DeepSeekPeriodCalendar.TryInstallAnnualJson("{\"year\":2040,\"papers\":[],\"days\":[]}", 2040), "Unannounced placeholder rejected");
Check(!DeepSeekPeriodCalendar.TryInstallAnnualJson(annual.Replace("www.gov.cn", "gov.cn.example.com"), 2040), "Non-government source rejected");
Check(!DeepSeekPeriodCalendar.TryInstallAnnualJson("{broken", 2040), "Malformed data rejected");
Check(!DeepSeekPeriodCalendar.TryInstallAnnualJson(annual.Replace("\"name\": \"元旦\"", "\"name\": null"), 2040), "Null holiday name rejected without interrupting refresh");
Check(!DeepSeekPeriodCalendar.TryInstallAnnualJson(annual.Replace("国庆节", "Other"), 2040), "Incomplete annual schedule rejected");
string cache = Path.Combine(Environment.CurrentDirectory, "Tests", "obj-period", "calendar-tests-" + Guid.NewGuid().ToString("N"));
try
{
    int calls = 0;
    var updater = new DeepSeekCalendarUpdater(cache, year => { calls++; return Task.FromResult(year == 2040 ? annual : null); });
    var now = new DateTimeOffset(2040, 12, 31, 12, 0, 0, TimeSpan.FromHours(8));
    await updater.RequestRefresh(now);
    Check(calls == 3, "Checks previous/current/next annual files");
    Check(File.Exists(Path.Combine(cache, "2040.json")), "Validated annual response cached");
    Check(DeepSeekPeriodCalendar.IsChinaWorkday(new(2040, 1, 4)) == true, "New annual schedule applied without restart");
    Check(DeepSeekPeriodCalendar.IsPeakDay(new(2040, 1, 2)) == false, "Downloaded holiday is idle");
    await updater.RequestRefresh(now.AddHours(1));
    Check(calls == 3, "No per-tick calendar requests");
    await updater.RequestRefresh(now.AddHours(13));
    Check(calls == 6, "Beijing year rollover bypasses daily throttle");
    var changed = annual.Replace("2040-01-03", "2040-01-04");
    Check(DeepSeekPeriodCalendar.TryInstallAnnualJson(changed, 2040), "Synthetic replacement accepted");
    var offline = new DeepSeekCalendarUpdater(cache, _ => throw new HttpRequestException("offline"));
    await offline.RequestRefresh(now);
    Check(DeepSeekPeriodCalendar.IsChinaWorkday(new(2040, 1, 4)) == true, "Offline startup restores cached calendar before fetching");
    Check(File.ReadAllText(Path.Combine(cache, "2040.json")) == annual, "Offline failures preserve last complete cache");
}
finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
if (args.Length == 1)
{
    string live = File.ReadAllText(args[0]);
    Check(DeepSeekPeriodCalendar.TryInstallAnnualJson(live, 2026), "Live public 2026 calendar accepted");
    Check(DeepSeekPeriodCalendar.IsChinaWorkday(new(2026, 10, 10)) == true, "Live calendar makeup workday retained");
    Check(DeepSeekPeriodCalendar.IsPeakDay(new(2026, 10, 10)) == false, "Live makeup weekend remains DeepSeek off-peak");
    Check(DeepSeekPeriodCalendar.IsPeakDay(new(2026, 2, 23)) == false, "Live Spring Festival weekday is off-peak");
}
Console.WriteLine($"PASS: {checks} DeepSeek checks (2030 statutory baseline, annual validation, cache/offline/rollover, boundaries and time zones).");
