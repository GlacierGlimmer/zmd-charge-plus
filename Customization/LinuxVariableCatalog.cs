using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

/// <summary>The Linux library is an allowlist backed by collectors and detected capabilities.</summary>
internal static class LinuxVariableCatalog
{
    private static readonly object Gate = new();
    private static HashSet<string>? _detected;
    private static Dictionary<string, HashSet<string>> _gpuKeys = new(StringComparer.OrdinalIgnoreCase);
    private static string _defaultGpu = "";
    private static readonly HashSet<string> Shared = new((
        "system.time system.date system.datetime system.uptime_seconds system.uptime_text system.boot_time " +
        "system.machine_name system.host_name system.user_name system.os_description system.os_version " +
        "system.os_architecture system.process_architecture system.framework_version system.processor_count " +
        "system.timezone_id system.timezone_name system.utc_offset_hours system.culture " +
        "app.name app.version app.pid app.start_time app.uptime_seconds app.uptime_text app.working_set_bytes " +
        "app.private_memory_bytes app.virtual_memory_bytes app.thread_count app.handle_count app.cpu_time_seconds " +
        "app.theme app.preset_name app.active_profile " +
        "network.download_bps network.upload_bps network.total_bps network.download_mbps network.upload_mbps network.total_mbps " +
        "network.display_download network.display_upload network.profile_percent network.profile_percent_text network.profile_percent_bps network.profile_percent_mode " +
        "network.total_received_bytes network.total_sent_bytes network.total_transferred_bytes network.active_interface_count " +
        "network.interface_names network.interface_types network.ipv4_addresses network.ipv6_addresses network.default_gateways " +
        "network.dns_servers network.available network.packets_received network.packets_sent network.receive_errors network.send_errors " +
        "network.public_ipv4 network.public_ipv6 " +
        "display.primary_width_px display.primary_height_px display.virtual_x_px display.virtual_y_px display.virtual_width_px " +
        "display.virtual_height_px display.monitor_count display.system_dpi display.scale_percent " +
        "clipboard.has_text clipboard.text_length clipboard.preview clipboard.has_image clipboard.last_updated")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlySet<string> Detected
    {
        get { lock (Gate) { if (_detected is null) Refresh(); return _detected!; } }
    }

    internal static void Refresh()
    {
        if (!OperatingSystem.IsLinux()) return;
        var collector = new LinuxVariableProvider();
        var measured = new Dictionary<string, object?>();
        collector.Collect(measured, null, null);
        // CPU/process/disk rates require a real second sample, never a fabricated initial zero.
        Thread.Sleep(80);
        collector.Collect(measured, null, null);
        var detected = measured.Where(p => p.Value is not null).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adapters = LinuxVariableProvider.GetGpuAdapters();
        var gpuKeys = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
        {
            var gpu = new Dictionary<string, object?>();
            collector.Collect(gpu, new HashSet<string> { "gpu.name" }, adapter.Id);
            gpuKeys[adapter.Id] = gpu.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            detected.UnionWith(gpu.Keys);
        }
        lock (Gate)
        {
            _gpuKeys = gpuKeys;
            _defaultGpu = adapters.FirstOrDefault()?.Id ?? "";
            _detected = detected;
        }
    }

    internal static IReadOnlySet<string> GpuKeys(string? adapterId)
    {
        _ = Detected;
        lock (Gate) return _gpuKeys.GetValueOrDefault(adapterId ?? "")
            ?? _gpuKeys.GetValueOrDefault(_defaultGpu) ?? new HashSet<string>();
    }

    internal static bool Supports(string key)
    {
        if (key.StartsWith("custom.", StringComparison.OrdinalIgnoreCase)) return true;
        if (Shared.Contains(key) || Detected.Contains(key)) return true;
        if (!VariableCatalog.PlatformIndependentDefinitions.Any(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) return false;
        if (key.StartsWith("time.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("deepseek.", StringComparison.OrdinalIgnoreCase)) return true;
        return (key.StartsWith("probe.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("ping.", StringComparison.OrdinalIgnoreCase))
            && key is not ("probe.ttl" or "ping.ttl");
    }

    internal static IReadOnlyList<VariableDefinition> Build(IEnumerable<VariableDefinition> original, string? gpuId = null)
    {
        var gpu = GpuKeys(gpuId);
        var definitions = original.Where(d => Supports(d.Key) && (!d.Key.StartsWith("gpu.") || gpu.Contains(d.Key)))
            .Select(d => d with { Description = Description(d) }).ToList();
        foreach (var (key, name, category, unit, type) in new[]
        {
            ("memory.swap_total_bytes", "交换空间总量", "内存", "Byte", "数值"),
            ("memory.swap_available_bytes", "可用交换空间", "内存", "Byte", "数值"),
            ("memory.swap_used_bytes", "已用交换空间", "内存", "Byte", "数值"),
            ("memory.swap_usage", "交换空间使用率", "内存", "%", "数值"),
            ("process.count", "进程总数", "进程", "个", "整数"),
            ("system.kernel_version", "Linux 内核版本", "系统", "", "文本"),
            ("system.reboot_required", "系统需要重启", "系统", "", "布尔"),
        })
            if (Detected.Contains(key)) definitions.Add(new(key, name, category, $"从 Linux procfs 读取的{name}。", type, unit, "按数据含义决定", type == "文本" ? "无需格式化" : "0 / gb:1"));
        foreach (var mount in LinuxVariableProvider.Mounts())
        {
            foreach (var source in definitions.Where(d => d.Key.StartsWith("disk.system.")).ToArray())
            {
                string suffix = source.Key[12..];
                string key = mount.Prefix + "." + suffix;
                if (!Detected.Contains(key)) continue;
                definitions.Add(source with { Key = key, Name = mount.Point + " · " + source.Name,
                    Description = $"挂载点 {mount.Point}（{mount.Device}）的真实文件系统容量或块设备 I/O 统计。" });
            }
        }
        return definitions.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
    }

    private static string Description(VariableDefinition d)
    {
        if (d.Key.StartsWith("display.")) return $"{d.Name}。从当前 X11/XWayland 会话的 Avalonia 屏幕信息读取。";
        if (d.Key.StartsWith("clipboard.")) return $"{d.Name}。读取当前图形会话的真实剪贴板；last_updated 为本程序首次观察到内容变化的时间。";
        if (d.Key.StartsWith("deepseek.") && !d.Key.StartsWith("deepseek.period."))
            return $"{d.Name}。需配置有效 API Key；未配置或请求失败时显示明确状态，不返回伪造余额。";
        if (d.Key.StartsWith("probe.") || d.Key.StartsWith("ping."))
            return $"{d.Name}。来自当前方案的实际 ICMP/TCP/UDP 探测；失败或协议不适用时显示状态文字。{d.Description}";
        if (!Detected.Contains(d.Key)) return d.Description;
        string source = d.Key.Split('.')[0] switch
        {
            "cpu" => "/proc/stat、/proc/cpuinfo、cpufreq 或 hwmon",
            "memory" => "/proc/meminfo（可用内存使用 MemAvailable）",
            "battery" => "/sys/class/power_supply（容量统一换算为 mWh / Wh）",
            "gpu" => "DRM/hwmon 或 NVIDIA nvidia-smi",
            "disk" => "当前挂载点的文件系统统计或 /sys/dev/block/*/stat",
            "process" => "/proc 中当前用户有权限读取的进程；CPU 使用率按逻辑处理器数归一化",
            "dev" => "Linux 进程或本机已安装工具的实际查询结果",
            "usb" or "peripheral" => "Linux sysfs 或 /proc/bus/input/devices",
            "security" => "Linux sysfs 或 EFI 变量",
            _ => "Linux procfs、sysfs 或当前运行状态",
        };
        return $"{d.Name}。数据来源：{source}。仅在当前设备和权限下检测到采集能力时列出。";
    }

    internal static List<HudProfile> AdaptProfiles(IEnumerable<HudProfile> source)
    {
        var result = new List<HudProfile>();
        foreach (var p in source)
        {
            var profile = p;
            if (p.BuiltInKey == "system.battery")
            {
                if (!Detected.Contains("battery.percent")) continue;
                if (!Detected.Contains("battery.full_mwh") || !Detected.Contains("battery.remaining_mwh"))
                    profile = p with { PrimaryTemplate = "{battery.status_text}", SecondaryTemplate = "" };
            }
            if (p.BuiltInKey == "system.cpu" && !Detected.Contains("cpu.frequency_ghz"))
                profile = p with { PrimaryTemplate = "{cpu.logical_processors}", SecondaryTemplate = " CPU" };
            if (p.BuiltInKey == "system.gpu")
            {
                var gpu = GpuKeys(p.GpuAdapterId);
                if (!gpu.Contains("gpu.name")) continue;
                if (!gpu.Contains("gpu.memory_used_bytes") || !gpu.Contains("gpu.memory_total_bytes"))
                    profile = profile with { PrimaryTemplate = "{gpu.name}", SecondaryTemplate = "" };
                if (!gpu.Contains("gpu.usage"))
                    profile = profile with { RightTemplate = "{gpu.count}", RightSuffix = " GPU", ProgressVariable = "", ProgressMax = 1 };
            }
            if (p.BuiltInKey == "deepseek.balance-period")
                profile = profile with { PrimaryTemplate = "{deepseek.balance_text}" };
            if (p.BuiltInKey == "network.ping")
                profile = profile with { PrimaryTemplate = "{probe.latency_text}" };
            result.Add(profile);
        }
        return result;
    }

    // Imported custom profiles are not destroyed. Invalid references are removed from display
    // templates/rules; settings.previous.json and import backups preserve the original content.
    internal static HudProfile RemoveUnsupportedReferences(HudProfile p)
    {
        var gpu = GpuKeys(p.GpuAdapterId);
        bool Available(string key) => Supports(key) && (!key.StartsWith("gpu.", StringComparison.OrdinalIgnoreCase) || gpu.Contains(key));
        string Clean(string template) => Regex.Replace(template, @"\{[^{}]+\}", match =>
            TemplateEngine.ExtractKeys(match.Value).Any(k => !Available(k)) ? "" : match.Value);
        return p with
        {
            TaglineTemplate = Clean(p.TaglineTemplate), TitleTemplate = Clean(p.TitleTemplate),
            PrimaryTemplate = Clean(p.PrimaryTemplate), SecondaryTemplate = Clean(p.SecondaryTemplate),
            RightTemplate = Clean(p.RightTemplate), RightSuffix = Clean(p.RightSuffix),
            ProgressVariable = TemplateEngine.ExtractExpressionKeys(p.ProgressVariable).Any(k => !Available(k)) ? "" : p.ProgressVariable,
            ColorRules = p.ColorRules.Where(r => Available(r.Variable)).ToList(),
        };
    }
}
