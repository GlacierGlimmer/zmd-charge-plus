using System.Globalization;
namespace EndfieldChargePlus.Customization;

public sealed record WorldClockZone(int Minutes,string Zh,string En)
{
    public string Label(bool english) => WorldClockTimeZones.OffsetText(Minutes)+" "+(english ? En : Zh);
    public override string ToString() => WorldClockTimeZones.OffsetText(Minutes)+" "+LocalizationManager.Text(Zh,En);
}

/// <summary>Fixed UTC offsets. Region names are examples; daylight saving does not change an offset.</summary>
public static class WorldClockTimeZones
{
    public static readonly IReadOnlyList<WorldClockZone> All=new WorldClockZone[] {
        new(-720,"国际日期变更线以西","International Date Line West"),
        new(-660,"美属萨摩亚","American Samoa"),
        new(-600,"夏威夷","Hawaii"),
        new(-570,"马克萨斯群岛","Marquesas Islands"),
        new(-540,"阿拉斯加","Alaska"),
        new(-480,"太平洋时间（美国和加拿大）","Pacific Time (US & Canada)"),
        new(-420,"山地时间（美国和加拿大）","Mountain Time (US & Canada)"),
        new(-360,"中部时间（美国和加拿大）","Central Time (US & Canada)"),
        new(-300,"东部时间（美国和加拿大）","Eastern Time (US & Canada)"),
        new(-240,"大西洋时间（加拿大）","Atlantic Time (Canada)"),
        new(-210,"纽芬兰","Newfoundland"),
        new(-180,"布宜诺斯艾利斯、巴西利亚","Buenos Aires, Brasilia"),
        new(-120,"南乔治亚岛","South Georgia"),
        new(-60,"佛得角","Cape Verde"),
        new(0,"协调世界时","Coordinated Universal Time"),
        new(60,"柏林、巴黎、罗马","Berlin, Paris, Rome"),
        new(120,"雅典、开罗、赫尔辛基","Athens, Cairo, Helsinki"),
        new(180,"莫斯科、利雅得","Moscow, Riyadh"),
        new(210,"德黑兰","Tehran"),
        new(240,"迪拜、阿布扎比","Dubai, Abu Dhabi"),
        new(270,"喀布尔","Kabul"),
        new(300,"伊斯兰堡、卡拉奇","Islamabad, Karachi"),
        new(330,"新德里、孟买","New Delhi, Mumbai"),
        new(345,"加德满都","Kathmandu"),
        new(360,"达卡、廷布","Dhaka, Thimphu"),
        new(390,"仰光","Yangon"),
        new(420,"曼谷、河内、雅加达","Bangkok, Hanoi, Jakarta"),
        new(480,"北京、香港、新加坡","Beijing, Hong Kong, Singapore"),
        new(525,"尤克拉","Eucla"),
        new(540,"东京、首尔","Tokyo, Seoul"),
        new(570,"阿德莱德、达尔文","Adelaide, Darwin"),
        new(600,"悉尼、布里斯班","Sydney, Brisbane"),
        new(630,"豪勋爵岛","Lord Howe Island"),
        new(660,"所罗门群岛、新喀里多尼亚","Solomon Islands, New Caledonia"),
        new(720,"奥克兰、斐济","Auckland, Fiji"),
        new(765,"查塔姆群岛","Chatham Islands"),
        new(780,"努库阿洛法、阿皮亚","Nukuʻalofa, Apia"),
        new(825,"查塔姆群岛夏季偏移","Chatham Islands summer offset"),
        new(840,"莱恩群岛","Line Islands"),
    };
    public static int Normalize(int minutes) => minutes is >= -720 and <= 840 && minutes%15==0 ? minutes : 0;
    public static bool UsesUtcOffset(HudProfile profile) => profile.TimeUtcOffsetMinutes.HasValue
        || string.Equals(profile.BuiltInKey,"time.world-clock",StringComparison.OrdinalIgnoreCase);
    public static string OffsetText(int minutes) => $"UTC{(minutes<0 ? "-" : "+")}{Math.Abs(minutes)/60:00}:{Math.Abs(minutes)%60:00}";
    public static DateTimeOffset ReadClock(HudProfile profile,DateTimeOffset instant) => UsesUtcOffset(profile)
        ? instant.ToOffset(TimeSpan.FromMinutes(Normalize(profile.TimeUtcOffsetMinutes ?? 0))) : instant.ToLocalTime();
    public static void AddCalendarValues(IDictionary<string,object?> values,DateTimeOffset clock)
    {
        var now=clock.DateTime;
        values["time.utc_offset"]=OffsetText((int)clock.Offset.TotalMinutes);
        values["time.current_12h"]=now.ToString("hh:mm:ss tt",CultureInfo.CurrentCulture);
        values["time.date"]=now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        values["time.datetime"]=now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);
        values["time.iso"]=clock.ToString("O",CultureInfo.InvariantCulture);
        values["time.year"]=now.Year;values["time.month"]=now.Month;values["time.day"]=now.Day;
        values["time.month_name"]=CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(now.Month);
        values["time.day_of_week"]=CultureInfo.GetCultureInfo("zh-CN").DateTimeFormat.GetDayName(now.DayOfWeek);
        values["time.day_of_week_en"]=now.DayOfWeek.ToString();values["time.day_of_year"]=now.DayOfYear;
        values["time.week_of_year"]=ISOWeek.GetWeekOfYear(now);values["time.hour"]=now.Hour;
        values["time.minute"]=now.Minute;values["time.second"]=now.Second;values["time.millisecond"]=now.Millisecond;
        values["time.is_weekend"]=now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        values["time.unix_seconds"]=clock.ToUnixTimeSeconds();values["time.unix_milliseconds"]=clock.ToUnixTimeMilliseconds();
        var weekStart=now.Date.AddDays(-(((int)now.DayOfWeek+6)%7));
        var monthStart=new DateTime(now.Year,now.Month,1);var yearStart=new DateTime(now.Year,1,1);
        foreach(var (prefix,start,end) in new[] { ("week",weekStart,weekStart.AddDays(7)),
            ("month",monthStart,monthStart.AddMonths(1)),("year",yearStart,yearStart.AddYears(1)) })
        {
            double elapsed=(now-start).TotalSeconds,total=(end-start).TotalSeconds;
            values[$"time.{prefix}.elapsed_seconds"]=elapsed;
            values[$"time.{prefix}.remaining_seconds"]=Math.Max(0,total-elapsed);
            values[$"time.{prefix}.progress"]=Math.Clamp(elapsed/total*100,0,100);
        }
    }
}

public static class BuiltInProfileOrder
{
    public static readonly IReadOnlyList<string> Keys=new[] {
        "system.battery",
        "system.cpu",
        "system.gpu",
        "system.memory",
        "system.disk",
        "system.network",
        "system.display-fps",
        "system.window-fps",
        "time.day-progress",
        "time.world-clock",
        "network.ping",
        "deepseek.balance-period",
    };
    public static int Rank(string key)
    {
        for(int i=0;i<Keys.Count;i++) if(string.Equals(Keys[i],key,StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }
}
