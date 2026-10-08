using System.Globalization;
using System.Text.Json;

namespace EndfieldChargePlus.Customization;

/// <summary>Official DeepSeek billing windows, evaluated against State Council holiday notices.</summary>
public static class DeepSeekPeriodCalendar
{
    public const string OfficialWindows = "09:00-12:00;14:00-18:00";
    public const string RuleSource = "https://api-docs.deepseek.com/zh-cn/quick_start/pricing";
    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    private static readonly (TimeSpan Start, TimeSpan End)[] Windows =
    {
        (TimeSpan.FromHours(9), TimeSpan.FromHours(12)),
        (TimeSpan.FromHours(14), TimeSpan.FromHours(18))
    };
    private static readonly Dictionary<int, YearCalendar> Years = Load();

    public sealed record PeriodState(DateTime Beijing, bool? IsPeak, DateTime? SegmentStart, DateTime? NextTransition)
    {
        public double? RemainingSeconds => NextTransition is { } next ? Math.Max(0, (next - Beijing).TotalSeconds) : null;
        public double? Progress => SegmentStart is { } start && NextTransition is { } next && next > start
            ? Math.Clamp((Beijing - start).TotalSeconds / (next - start).TotalSeconds * 100, 0, 100) : null;
    }

    private static readonly object Gate = new();
    private sealed record YearCalendar(HashSet<DateOnly> Holidays, HashSet<DateOnly> MakeupWorkdays, bool Complete = true);

    public static bool? IsChinaWorkday(DateOnly day)
    {
        lock (Gate)
        {
            if (!Years.TryGetValue(day.Year, out var calendar)) return null;
            if (calendar.Holidays.Contains(day)) return false;
            if (calendar.MakeupWorkdays.Contains(day)) return true;
            return calendar.Complete ? !IsWeekend(day) : null;
        }
    }

    public static bool? IsPeakDay(DateOnly day)
    {
        // DeepSeek explicitly makes ALL weekends off-peak, including makeup workdays.
        if (IsWeekend(day)) return false;
        return IsChinaWorkday(day);
    }

    public static PeriodState Evaluate(DateTimeOffset instant)
    {
        var now = instant.ToOffset(BeijingOffset).DateTime;
        var eligible = IsPeakDay(DateOnly.FromDateTime(now));
        bool inWindow = Windows.Any(w => now.TimeOfDay >= w.Start && now.TimeOfDay < w.End);
        if (eligible is null && inWindow) return new(now, null, null, null);
        bool peak = eligible == true && inWindow;
        DateTime? start = peak
            ? now.Date + Windows.First(w => now.TimeOfDay >= w.Start && now.TimeOfDay < w.End).Start
            : FindBoundary(now, forward: false);
        return new(now, peak, start, FindBoundary(now, forward: true));
    }

    private static DateTime? FindBoundary(DateTime now, bool forward)
    {
        // Holiday breaks exceed a week. Stop at an unannounced year instead of
        // inventing a transition using an ordinary Monday-to-Friday calendar.
        for (int d = 0; d < 370; d++)
        {
            var date = now.Date.AddDays(forward ? d : -d);
            var eligible = IsPeakDay(DateOnly.FromDateTime(date));
            if (eligible is null) return null;
            if (!eligible.Value) continue;
            var boundaries = Windows.SelectMany(w => new[] { date + w.Start, date + w.End });
            foreach (var boundary in forward ? boundaries : boundaries.Reverse())
                if (forward ? boundary > now : boundary <= now) return boundary;
        }
        return null;
    }

    private static bool IsWeekend(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static Dictionary<int, YearCalendar> Load()
    {
        var result = new Dictionary<int, YearCalendar>();
        try
        {
            using var stream = typeof(DeepSeekPeriodCalendar).Assembly.GetManifestResourceStream("EndfieldChargePlus.china-holidays.json");
            if (stream is null) return result;
            using var document = JsonDocument.Parse(stream);
            foreach (var year in document.RootElement.GetProperty("years").EnumerateObject())
            {
                int number = int.Parse(year.Name, CultureInfo.InvariantCulture);
                var holidays = new HashSet<DateOnly>();
                foreach (var range in year.Value.GetProperty("holidays").EnumerateArray())
                {
                    var ends = range.GetString()!.Split('/');
                    var first = DateOnly.ParseExact(ends[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var last = DateOnly.ParseExact(ends.Length > 1 ? ends[1] : ends[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (first.Year != number || last.Year != number || last < first) throw new FormatException("Invalid holiday range.");
                    for (var day = first; day <= last; day = day.AddDays(1)) holidays.Add(day);
                }
                var workdays = year.Value.GetProperty("makeupWorkdays").EnumerateArray()
                    .Select(x => DateOnly.ParseExact(x.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToHashSet();
                if (workdays.Any(d => d.Year != number || holidays.Contains(d))) throw new FormatException("Invalid makeup workday.");
                bool complete = !year.Value.TryGetProperty("coverage", out var coverage) || coverage.GetString() != "statutory-only";
                result[number] = new(holidays, workdays, complete);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            // A missing or damaged calendar must not publish fabricated peak data.
            result.Clear();
        }
        return result;
    }

    public static bool IsValidAnnualJson(string json, int expectedYear) => ParseAnnual(json, expectedYear) is not null;

    public static bool TryInstallAnnualJson(string json, int expectedYear)
    {
        var parsed = ParseAnnual(json, expectedYear);
        if (parsed is null) return false;
        lock (Gate)
        {
            // Only complete, source-linked annual notices can replace the embedded baseline.
            Years[expectedYear] = new(parsed.Holidays.Where(d => d.Year == expectedYear).ToHashSet(),
                parsed.MakeupWorkdays.Where(d => d.Year == expectedYear).ToHashSet());
            // Next year's notice can also change December dates in the preceding year.
            int previous = expectedYear - 1;
            if (Years.TryGetValue(previous, out var prior))
            {
                foreach (var day in parsed.Holidays.Where(d => d.Year == previous)) { prior.Holidays.Add(day); prior.MakeupWorkdays.Remove(day); }
                foreach (var day in parsed.MakeupWorkdays.Where(d => d.Year == previous)) { prior.MakeupWorkdays.Add(day); prior.Holidays.Remove(day); }
            }
        }
        return true;
    }

    private static YearCalendar? ParseAnnual(string json, int expectedYear)
    {
        try
        {
            if (json.Length > 65536) return null;
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("year").GetInt32() != expectedYear) return null;
            var papers = root.GetProperty("papers").EnumerateArray().ToArray();
            if (papers.Length == 0 || papers.Any(p => !Uri.TryCreate(p.GetString(), UriKind.Absolute, out var u)
                || u.Scheme != "https" || !(u.Host == "gov.cn" || u.Host.EndsWith(".gov.cn", StringComparison.OrdinalIgnoreCase)))) return null;
            var holidays = new HashSet<DateOnly>(); var workdays = new HashSet<DateOnly>(); var names = new HashSet<string>();
            foreach (var item in root.GetProperty("days").EnumerateArray())
            {
                var day = DateOnly.ParseExact(item.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (day.Year != expectedYear && !(day.Year == expectedYear - 1 && day.Month == 12)) return null;
                if (holidays.Contains(day) || workdays.Contains(day)) return null;
                string? name = item.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name)) return null;
                if (item.GetProperty("isOffDay").GetBoolean()) { holidays.Add(day); if (day.Year == expectedYear) names.Add(name); }
                else workdays.Add(day);
            }
            if (holidays.Count < 13 || !new[] { "元旦", "春节", "清明节", "劳动节", "端午节", "中秋节", "国庆节" }.All(h => names.Any(n => n.Contains(h, StringComparison.Ordinal)))) return null;
            return new(holidays, workdays);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException) { return null; }
    }
}
