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
Check(DeepSeekPeriodCalendar.IsChinaWorkday(new(2027, 1, 1)) is null, "Unannounced year cannot be guessed");
Console.WriteLine($"PASS: {checks} DeepSeek checks (official boundaries, holiday breaks, makeup weekends, UTC conversion and unknown years).");
