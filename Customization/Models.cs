using System;
using System.Collections.Generic;
using System.Linq;

namespace EndfieldChargePlus.Customization;

public sealed record HudColorRule
{
    public string Variable { get; init; } = "";
    public string Operator { get; init; } = ">=";
    public double Value { get; init; }
    public string Color { get; init; } = "#C6CA4C";
}

public sealed record HudProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    // Built-in profiles are shipped with the application and are read-only.
    public bool IsBuiltIn { get; init; } = false;
    public string BuiltInKey { get; init; } = "";

    // Category/Name are retained internally for migration and built-in identity.
    // The settings UI presents one merged scheme name such as “系统 - CPU”.
    public string Category { get; init; } = "自定义";
    public string Name { get; init; } = "自定义 HUD";
    public string AnimationMode { get; init; } = "Full"; // Simple | Full

    public string TaglineTemplate { get; init; } = "/// SYSTEM MONITOR";
    public string TitleTemplate { get; init; } = "系统状态";
    public string PrimaryTemplate { get; init; } = "{cpu.frequency_ghz|0.00}";
    public string SecondaryTemplate { get; init; } = " GHz";
    public string RightTemplate { get; init; } = "{cpu.usage|0}";
    public string RightSuffix { get; init; } = "%";

    public string ProgressVariable { get; init; } = "cpu.usage";
    public double ProgressMin { get; init; } = 0;
    public double ProgressMax { get; init; } = 100;

    public string LeftIcon { get; init; } = "cpu";
    public string RightIcon { get; init; } = "cpu";
    public string AccentColor { get; init; } = "#C6CA4C";
    public List<HudColorRule> ColorRules { get; init; } = new();

    // Time category option: day progress by default; when enabled, show the remaining proportion to the next daily target time.
    public bool TimeTargetEnabled { get; init; } = false;
    public string TimeTarget { get; init; } = "10:00:00";

    // Optional per-profile GPU target. Empty means the first available adapter.
    public string GpuAdapterId { get; init; } = "";

    // Network profile presentation. All traffic values are system-wide totals across
    // active non-loopback interfaces. NetworkDisplayUnit: AutoBytes | Mbps.
    // NetworkPercentMode: Total | Download | Upload | Max.
    public string NetworkDisplayUnit { get; init; } = "AutoBytes";
    public string NetworkPercentMode { get; init; } = "Total";
    public double NetworkReferenceValue { get; init; } = 100d;
    public string NetworkReferenceUnit { get; init; } = "MB/s";

    // Network packet probe settings. Target accepts IPv4/IPv6 addresses or DNS host names.
    // The port is ignored for ICMP and used by TCP/UDP probes.
    public string PingTarget { get; init; } = "1.1.1.1";
    public string ProbeProtocol { get; init; } = "ICMP"; // ICMP | TCP | UDP
    public int ProbePort { get; init; } = 443;
}

public sealed record HttpFieldMapping
{
    public string Variable { get; init; } = "value";
    public string JsonPath { get; init; } = "";
}

public sealed record CustomHttpSource
{
    public string Name { get; init; } = "custom";
    public bool Enabled { get; init; } = true;
    public string Url { get; init; } = "";
    public int RefreshSeconds { get; init; } = 60;
    public Dictionary<string, string> Headers { get; init; } = new();
    public List<HttpFieldMapping> Fields { get; init; } = new();
}

public sealed record CustomHudSettings
{
    public bool AutoCycle { get; init; } = false;
    public int CycleSeconds { get; init; } = 10;
    // Ordered profile IDs used by automatic cycling. Null means a legacy settings file
    // that predates the explicit cycle queue; the normalizer migrates that case once.
    public List<string>? CycleProfileIds { get; init; } = null;
    // Simple | Full. When AutoCycle is enabled this overrides each profile's own
    // AnimationMode for transitions between queue entries.
    public string CycleAnimationMode { get; init; } = "Simple";
    public string ActiveProfileId { get; init; } = "";

    public string DeepSeekApiKeyProtected { get; init; } = "";
    public string DeepSeekPeakWindows { get; init; } = "09:00-12:00;14:00-18:00";

    public List<HudProfile> Profiles { get; init; } = new();
    public List<CustomHttpSource> HttpSources { get; init; } = new();

    public static CustomHudSettings CreateDefault()
    {
        var profiles = CreateDefaultProfiles();
        var defaultProfile = profiles.FirstOrDefault(p => string.Equals(p.BuiltInKey, "system.memory", StringComparison.OrdinalIgnoreCase))
                             ?? profiles.FirstOrDefault();
        return new CustomHudSettings
        {
            Profiles = profiles,
            ActiveProfileId = defaultProfile?.Id ?? "",
            CycleProfileIds = profiles
                .Where(p => string.Equals(p.Category, "系统", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(p.BuiltInKey, "time.day-progress", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToList(),
            CycleAnimationMode = "Simple"
        };
    }

    public static List<HudProfile> CreateDefaultProfiles() => new()
    {
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "system.battery",
            Category = "系统",
            Name = "电池",
            AnimationMode = "Full",
            TaglineTemplate = "/// BATTERY",
            TitleTemplate = "电池",
            PrimaryTemplate = "{battery.remaining_mwh|0}",
            SecondaryTemplate = "/{battery.full_mwh|0}",
            RightTemplate = "{battery.percent|0}",
            RightSuffix = "%",
            ProgressVariable = "battery.percent",
            LeftIcon = "bolt",
            RightIcon = "battery",
            ColorRules = new()
            {
                new() { Variable = "battery.percent", Operator = "<=", Value = 20, Color = "#FF4D4F" }
            }
        },
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "system.cpu",
            Category = "系统",
            Name = "CPU",
            AnimationMode = "Full",
            TaglineTemplate = "/// CPU",
            TitleTemplate = "CPU",
            PrimaryTemplate = "{cpu.frequency_ghz|0.00}",
            SecondaryTemplate = " GHz",
            RightTemplate = "{cpu.usage|0}",
            RightSuffix = "%",
            ProgressVariable = "cpu.usage",
            LeftIcon = "cpu",
            RightIcon = "cpu",
            ColorRules = new()
            {
                new() { Variable = "cpu.usage", Operator = ">=", Value = 90, Color = "#FF4D4F" },
                new() { Variable = "cpu.usage", Operator = ">=", Value = 75, Color = "#FFB84D" },
            }
        },
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "system.memory",
            Category = "系统",
            Name = "内存",
            AnimationMode = "Full",
            TaglineTemplate = "/// MEMORY",
            TitleTemplate = "内存",
            PrimaryTemplate = "{memory.used_bytes|gb:1}",
            SecondaryTemplate = "/{memory.total_bytes|gb:1} GB",
            RightTemplate = "{memory.usage|0}",
            RightSuffix = "%",
            ProgressVariable = "memory.usage",
            LeftIcon = "memory",
            RightIcon = "memory"
        },
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "system.gpu",
            Category = "系统",
            Name = "GPU",
            AnimationMode = "Full",
            TaglineTemplate = "/// GPU",
            TitleTemplate = "GPU",
            PrimaryTemplate = "{gpu.memory_used_bytes|gb:1}",
            SecondaryTemplate = "/{gpu.memory_total_bytes|gb:1} GB",
            RightTemplate = "{gpu.usage|0}",
            RightSuffix = "%",
            ProgressVariable = "gpu.usage",
            LeftIcon = "gpu",
            RightIcon = "gpu"
        },
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "system.network",
            Category = "系统",
            Name = "网络",
            AnimationMode = "Full",
            TaglineTemplate = "/// NETWORK",
            TitleTemplate = "网络",
            PrimaryTemplate = "↓ {network.display_download}",
            SecondaryTemplate = "  ↑ {network.display_upload}",
            RightTemplate = "{network.profile_percent_text}",
            RightSuffix = "",
            ProgressVariable = "network.profile_percent",
            ProgressMin = 0,
            ProgressMax = 100,
            LeftIcon = "network",
            RightIcon = "network",
            NetworkDisplayUnit = "AutoBytes",
            NetworkPercentMode = "Total",
            NetworkReferenceValue = 500d,
            NetworkReferenceUnit = "Mbps"
        },
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "system.disk",
            Category = "系统",
            Name = "系统盘",
            AnimationMode = "Full",
            TaglineTemplate = "/// SYSTEM DISK",
            TitleTemplate = "系统盘",
            PrimaryTemplate = "{disk.system.used_bytes|gb:1}",
            SecondaryTemplate = "/{disk.system.total_bytes|gb:1} GB",
            RightTemplate = "{disk.system.usage|0}",
            RightSuffix = "%",
            ProgressVariable = "disk.system.usage",
            LeftIcon = "disk",
            RightIcon = "disk"
        },
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "time.day-progress",
            Category = "时间",
            Name = "日进程",
            AnimationMode = "Full",
            TaglineTemplate = "/// TIME",
            TitleTemplate = "日进程",
            PrimaryTemplate = "{time.current}",
            SecondaryTemplate = "",
            RightTemplate = "{time.display.status_text}",
            RightSuffix = "",
            ProgressVariable = "time.display.progress",
            ProgressMin = 0,
            ProgressMax = 100,
            LeftIcon = "clock",
            RightIcon = "clock",
            TimeTargetEnabled = false,
            TimeTarget = "10:00:00"
        },
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "deepseek.balance-period",
            Category = "DeepSeek API",
            Name = "余额 / 时段",
            AnimationMode = "Full",
            TaglineTemplate = "/// DEEPSEEK API",
            TitleTemplate = "DeepSeek 当前{deepseek.period.name_zh}",
            PrimaryTemplate = "¥{deepseek.balance|0.00}",
            SecondaryTemplate = "  {deepseek.period.remaining_text}",
            RightTemplate = "{deepseek.period.progress_text}",
            RightSuffix = "",
            ProgressVariable = "deepseek.period.progress",
            LeftIcon = "api",
            RightIcon = "clock"
        },
        new()
        {
            IsBuiltIn = true,
            BuiltInKey = "network.ping",
            Category = "网络",
            Name = "网络包探测器",
            AnimationMode = "Full",
            TaglineTemplate = "/// NETWORK PROBE",
            TitleTemplate = "网络包探测器",
            PrimaryTemplate = "{probe.latency_ms|0}ms",
            SecondaryTemplate = "",
            RightTemplate = "丢包{probe.loss_percent|0}",
            RightSuffix = "%",
            ProgressVariable = "probe.loss_percent",
            ProgressMin = 0,
            ProgressMax = 100,
            LeftIcon = "signal",
            RightIcon = "gauge",
            PingTarget = "1.1.1.1",
            ProbeProtocol = "ICMP",
            ProbePort = 443,
            ColorRules = new()
            {
                new() { Variable = "probe.loss_percent", Operator = ">=", Value = 30, Color = "#FF4D4F" },
                new() { Variable = "probe.loss_percent", Operator = ">=", Value = 10, Color = "#FFB84D" },
            }
        }
    };
}

public sealed record HudRenderData(
    string Tagline,
    string Title,
    string PrimaryText,
    string SecondaryText,
    string RightText,
    string RightSuffix,
    double Progress,
    string LeftIcon,
    string RightIcon,
    string AccentColor,
    bool SimpleAnimation);
