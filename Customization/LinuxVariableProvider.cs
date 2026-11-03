using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

/// <summary>Unprivileged Linux collectors. Unreadable/unsupported metrics are omitted.</summary>
internal sealed partial class LinuxVariableProvider
{
    private readonly string _root;
    private ulong[]? _lastCpu;
    private readonly Queue<(DateTime Time, double Value)> _cpuHistory = new();
    private (long Tick, double Switches, double Interrupts)? _lastScheduler;
    private readonly object _gate = new();

    // A filesystem root also allows deterministic procfs/sysfs fixture tests.
    internal LinuxVariableProvider(string root = "/") => _root = root;
    private string PathAt(string path) => Path.Combine(_root, path.TrimStart('/'));

    internal void Collect(IDictionary<string, object?> values, HashSet<string>? requested, string? gpuId, CustomHudSettings? settings = null)
    {
        bool Need(string prefix) => requested is null || requested.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        lock (_gate)
        {
            if (Need("cpu.")) AddCpu(values);
            if (Need("memory.")) AddMemory(values);
            if (Need("network.")) AddLinuxNetwork(values);
            if (Need("battery.")) AddBattery(values);
            if (Need("disk.")) CollectDisks(values);
            if (Need("gpu.")) AddGpu(values, gpuId);
            if (Need("process.") || Need("dev.")) AddProcesses(values, Need("process."), Need("dev."));
            if (Need("system.") || Need("security.")) AddLinuxSystem(values);
            if (Need("usb.") || Need("peripheral.")) AddDevices(values);
            if (Need("dev.")) AddDeveloper(values);
            if (Need("app."))
            {
                values["app.theme"] = "Dark";
                if (settings is not null)
                {
                    var profile = settings.Profiles.FirstOrDefault(p => p.Id == settings.ActiveProfileId);
                    if (profile is not null)
                    {
                        values["app.preset_name"] = BuiltInProfileLocalization.DisplayName(profile);
                        values["app.active_profile"] = profile.Id;
                    }
                }
            }
        }
    }

    private void AddCpu(IDictionary<string, object?> v)
    {
        var stat = Read(PathAt("proc/stat"))?.Split('\n').FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        var counters = stat?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8)
            .Select(s => ulong.TryParse(s, out var n) ? n : 0).ToArray();
        // guest and guest_nice are already included in user/nice; never count them twice.
        if (counters is { Length: >= 4 })
        {
            if (_lastCpu is { } previous && previous.Length == counters.Length && counters.Zip(previous).All(p => p.First >= p.Second))
            {
                var delta = counters.Zip(previous, (a, b) => (double)(a - b)).ToArray();
                var total = delta.Sum();
                if (total > 0)
                {
                    double idle = delta[3] + (delta.Length > 4 ? delta[4] : 0);
                    v["cpu.usage"] = Math.Clamp(100 * (total - idle) / total, 0, 100);
                    v["cpu.user_usage"] = 100 * (delta[0] + delta[1]) / total;
                    v["cpu.kernel_usage"] = 100 * (delta[2] + delta.Skip(5).Take(2).Sum()) / total;
                    v["cpu.idle_percent"] = 100 * idle / total;
                }
            }
            _lastCpu = counters;
        }
        var info = Read(PathAt("proc/cpuinfo")) ?? "";
        var entries = info.Split('\n').Select(l => l.Split(':', 2)).Where(p => p.Length == 2).ToArray();
        string? First(string key) => entries.FirstOrDefault(p => p[0].Trim() == key)?[1].Trim();
        Put(v, "cpu.name", First("model name") ?? First("Hardware"));
        Put(v, "cpu.manufacturer", First("vendor_id"));
        v["cpu.architecture"] = RuntimeInformation.OSArchitecture.ToString();
        v["cpu.logical_processors"] = Environment.ProcessorCount;
        var mhz = entries.Where(p => p[0].Trim() == "cpu MHz").Select(p => Parse(p[1])).Where(n => n.HasValue).ToArray();
        if (mhz.Length > 0)
        {
            v["cpu.frequency_mhz"] = mhz.Average(n => n!.Value);
            v["cpu.frequency_ghz"] = mhz.Average(n => n!.Value) / 1000;
        }
        var maxKhz = Number(PathAt("sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq"));
        if (maxKhz > 0)
        {
            v["cpu.max_frequency_ghz"] = maxKhz.Value / 1_000_000;
            v["cpu.max_frequency_mhz"] = maxKhz.Value / 1000;
            if (mhz.Length > 0) v["cpu.frequency_percent"] = mhz.Average(n => n!.Value) / (maxKhz.Value / 1000) * 100;
        }
        var pairs = info.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(block =>
        {
            var fields = block.Split('\n').Select(l => l.Split(':', 2)).Where(p => p.Length == 2)
                .GroupBy(p => p[0].Trim()).ToDictionary(g => g.Key, g => g.First()[1].Trim());
            return fields.TryGetValue("core id", out var core) ? $"{fields.GetValueOrDefault("physical id", "0")}:{core}" : null;
        }).Where(s => s is not null).Distinct().Count();
        if (pairs > 0) v["cpu.physical_cores"] = pairs;
        int sockets = entries.Where(p => p[0].Trim() == "physical id").Select(p => p[1].Trim()).Distinct().Count();
        if (sockets > 0) v["cpu.socket_count"] = sockets;
        var temperatures = new List<double>();
        foreach (var hwmon in Directories(PathAt("sys/class/hwmon")))
        {
            if (Read(Path.Combine(hwmon, "name")) is not ("coretemp" or "k10temp" or "cpu_thermal")) continue;
            temperatures.AddRange(Files(hwmon, "temp*_input").Select(Number).Where(n => n.HasValue).Select(n => n!.Value / 1000));
        }
        if (temperatures.Count > 0) v["cpu.temperature_max"] = temperatures.Max();
        if (v.TryGetValue("cpu.usage", out var usage))
        {
            var now = DateTime.UtcNow;
            _cpuHistory.Enqueue((now, Convert.ToDouble(usage)));
            while (_cpuHistory.Count > 0 && _cpuHistory.Peek().Time < now.AddMinutes(-15)) _cpuHistory.Dequeue();
            foreach (int minutes in new[] { 1, 5, 15 })
                v[$"cpu.usage_avg_{minutes}m"] = _cpuHistory.Where(s => s.Time >= now.AddMinutes(-minutes)).Average(s => s.Value);
            v["cpu.usage_max"] = _cpuHistory.Max(s => s.Value);
        }
        var scheduler = (Read(PathAt("proc/stat")) ?? "").Split('\n');
        double? Counter(string key) => Parse(scheduler.FirstOrDefault(l => l.StartsWith(key + " "))?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1));
        var ctxt = Counter("ctxt"); var interrupts = Counter("intr");
        if (ctxt.HasValue && interrupts.HasValue)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastScheduler is { } old)
            {
                double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(old.Tick, now).TotalSeconds;
                if (seconds > 0 && ctxt >= old.Switches && interrupts >= old.Interrupts)
                {
                    v["cpu.context_switches"] = (ctxt.Value - old.Switches) / seconds;
                    v["cpu.interrupts"] = (interrupts.Value - old.Interrupts) / seconds;
                }
            }
            _lastScheduler = (now, ctxt.Value, interrupts.Value);
        }
    }

    private void AddMemory(IDictionary<string, object?> v)
    {
        var fields = (Read(PathAt("proc/meminfo")) ?? "").Split('\n').Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2).ToDictionary(p => p[0], p => Parse(p[1].Trim().Split(' ')[0]) * 1024);
        double? Get(string key) => fields.GetValueOrDefault(key);
        var total = Get("MemTotal");
        var available = Get("MemAvailable");
        Put(v, "memory.total_bytes", total);
        Put(v, "memory.available_bytes", available);
        if (total > 0 && available.HasValue)
        {
            v["memory.used_bytes"] = Math.Max(0, total.Value - available.Value);
            v["memory.usage"] = Math.Clamp((total.Value - available.Value) / total.Value * 100, 0, 100);
            v["memory.free_percent"] = Math.Clamp(available.Value / total.Value * 100, 0, 100);
        }
        Put(v, "memory.cache_bytes", Get("Cached"));
        Put(v, "memory.commit_limit_bytes", Get("CommitLimit"));
        Put(v, "memory.commit_used_bytes", Get("Committed_AS"));
        if (Get("CommitLimit") is > 0 and var limit && Get("Committed_AS") is { } committed)
        {
            v["memory.commit_available_bytes"] = Math.Max(0, limit - committed);
            v["memory.commit_usage"] = committed / limit * 100;
        }
        var swap = Get("SwapTotal");
        var freeSwap = Get("SwapFree");
        Put(v, "memory.swap_total_bytes", swap);
        Put(v, "memory.swap_available_bytes", freeSwap);
        if (swap.HasValue && freeSwap.HasValue)
        {
            v["memory.swap_used_bytes"] = Math.Max(0, swap.Value - freeSwap.Value);
            v["memory.swap_usage"] = swap > 0 ? (swap.Value - freeSwap.Value) / swap.Value * 100 : 0d;
        }
    }

    internal bool TryGetAcOnline(out bool online)
    {
        bool found = false;
        online = false;
        foreach (var supply in Directories(PathAt("sys/class/power_supply")))
        {
            if (Read(Path.Combine(supply, "type")) == "Battery") continue;
            var value = Number(Path.Combine(supply, "online"));
            if (value is not (0 or 1)) continue;
            found = true;
            online |= value == 1;
        }
        return found;
    }

    private void AddBattery(IDictionary<string, object?> v)
    {
        bool hasAc = TryGetAcOnline(out bool ac);
        if (hasAc) v["battery.ac_online"] = ac;
        var batteries = Directories(PathAt("sys/class/power_supply"))
            .Where(d => Read(Path.Combine(d, "type")) == "Battery" && Number(Path.Combine(d, "present")) != 0).ToArray();
        if (batteries.Length == 0) return;
        var statuses = batteries.Select(d => Read(Path.Combine(d, "status"))).ToArray();
        bool charging = statuses.Contains("Charging");
        bool discharging = statuses.Contains("Discharging");
        v["battery.charging"] = charging;
        v["battery.discharging"] = discharging;
        v["battery.status_text"] = charging && discharging ? LocalizationManager.Text("充放电混合", "Charging and discharging")
            : charging ? LocalizationManager.Text("充电中", "Charging")
            : discharging ? LocalizationManager.Text("使用电池", "On Battery")
            : statuses.All(s => s == "Full") ? LocalizationManager.Text("已充满", "Full")
            : LocalizationManager.Text("未充电", "Not charging");
        if (hasAc) v["battery.power_source"] = ac ? LocalizationManager.Text("交流电源", "AC") : LocalizationManager.Text("电池", "Battery");
        double? Sum(string attribute) => SumKnown(batteries.Select(d => Number(Path.Combine(d, attribute))));
        double? Energy(string suffix) => SumKnown(batteries.Select(d =>
        {
            var energy = Number(Path.Combine(d, "energy_" + suffix));
            if (energy.HasValue) return energy / 1000; // microWh -> mWh
            var charge = Number(Path.Combine(d, "charge_" + suffix));
            var voltage = Number(Path.Combine(d, "voltage_min_design")) ?? Number(Path.Combine(d, "voltage_now"));
            return charge * voltage / 1_000_000_000; // microAh * microV -> mWh
        }));
        var remaining = Energy("now");
        var full = Energy("full");
        var design = Energy("full_design");
        foreach (var (name, value) in new[] { ("remaining", remaining), ("full", full), ("design", design) })
        {
            Put(v, $"battery.{name}_mwh", value);
            Put(v, $"battery.{name}_wh", value / 1000);
        }
        if (full > 0 && remaining.HasValue) v["battery.percent"] = Math.Clamp(remaining.Value / full.Value * 100, 0, 100);
        else
        {
            var capacities = batteries.Select(d => Number(Path.Combine(d, "capacity"))).ToArray();
            if (capacities.All(n => n is >= 0 and <= 100)) v["battery.percent"] = capacities.Average(n => n!.Value);
        }
        if (design > 0 && full.HasValue) v["battery.health_percent"] = full.Value / design.Value * 100;
        var powers = batteries.Select(d => Number(Path.Combine(d, "power_now")) / 1_000_000
            ?? Number(Path.Combine(d, "current_now")) * Number(Path.Combine(d, "voltage_now")) / 1_000_000_000_000)
            .Select(n => n.HasValue ? (double?)Math.Abs(n.Value) : null).ToArray();
        var watts = SumKnown(powers);
        Put(v, "battery.rate_watts", watts);
        if (watts.HasValue)
        {
            v["battery.charge_rate_watts"] = powers.Where((_, index) => statuses[index] == "Charging").Sum(n => n!.Value);
            v["battery.discharge_rate_watts"] = powers.Where((_, index) => statuses[index] == "Discharging").Sum(n => n!.Value);
        }
        if (discharging && !charging && watts > 0 && remaining.HasValue)
        {
            double seconds = remaining.Value / (watts.Value * 1000) * 3600;
            v["battery.time_remaining_seconds"] = seconds;
            v["battery.estimated_time_to_empty"] = seconds;
            v["battery.time_remaining_text"] = $"{(int)(seconds / 3600)}:{(int)(seconds / 60) % 60:00}";
        }
        if (charging && !discharging && watts > 0 && full.HasValue && remaining.HasValue)
            v["battery.estimated_time_to_full"] = Math.Max(0, full.Value - remaining.Value) / (watts.Value * 1000) * 3600;
        if (watts.HasValue && remaining.HasValue)
        {
            string idle = LocalizationManager.Text("当前无法估算", "Estimate unavailable");
            string emptyStatus = !discharging ? LocalizationManager.Text("未放电", "Not discharging") : idle;
            foreach (var key in new[] { "battery.time_remaining_seconds", "battery.time_remaining_text", "battery.estimated_time_to_empty" })
                if (!v.ContainsKey(key)) v[key] = emptyStatus;
            if (full.HasValue && !v.ContainsKey("battery.estimated_time_to_full"))
                v["battery.estimated_time_to_full"] = !charging ? LocalizationManager.Text("未充电", "Not charging") : idle;
        }
        if (design > 0 && full.HasValue) v["battery.design_vs_current_health"] = full.Value / design.Value * 100;
        // Voltage/cycle count have no meaningful sum across multiple packs.
        if (batteries.Length == 1)
        {
            Put(v, "battery.voltage_mv", Sum("voltage_now") / 1000);
            Put(v, "battery.voltage_v", Sum("voltage_now") / 1_000_000);
            if (Sum("cycle_count") is >= 0 and var cycles) v["battery.cycle_count"] = cycles;
            Put(v, "battery.temperature", Number(Path.Combine(batteries[0], "temp")) / 10);
            var chemistry = Read(Path.Combine(batteries[0], "technology"));
            if (!string.IsNullOrEmpty(chemistry) && !chemistry.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) v["battery.chemistry"] = chemistry;
        }
    }

    private static double? SumKnown(IEnumerable<double?> values)
    {
        var array = values.ToArray();
        return array.Length > 0 && array.All(n => n.HasValue) ? array.Sum(n => n!.Value) : null;
    }

    internal static IReadOnlyList<GpuAdapterInfo> GetGpuAdapters(string root = "/")
    {
        var nvidia = root == "/" && OperatingSystem.IsLinux() ? LinuxNvidia.Read() : Array.Empty<Dictionary<string, object?>>();
        return Directories(Path.Combine(root, "sys/class/drm"))
            .Where(d => Regex.IsMatch(Path.GetFileName(d), "^card[0-9]+$") && Directory.Exists(Path.Combine(d, "device")))
            .Where(d => nvidia.Count == 0 || Read(Path.Combine(d, "device/vendor")) != "0x10de")
            .OrderBy(d => d, StringComparer.Ordinal).Select((d, index) =>
            {
                var device = Path.Combine(d, "device");
                var vendor = Read(Path.Combine(device, "vendor"));
                string brand = vendor switch { "0x1002" => "AMD", "0x10de" => "NVIDIA", "0x8086" => "Intel", _ => "GPU" };
                string name = Read(Path.Combine(device, "product_name")) ?? $"{brand} {Path.GetFileName(d)}";
                return new GpuAdapterInfo(Path.GetFileName(d), name, index, Number(Path.Combine(device, "mem_info_vram_total")) ?? 0,
                    0, Array.Empty<string>(), Array.Empty<string>());
            }).Concat(nvidia.Select(v => new GpuAdapterInfo((string)v["gpu.adapter_id"]!, (string)v["gpu.name"]!,
                Convert.ToInt32(v["gpu.physical_index"]), Convert.ToDouble(v.GetValueOrDefault("gpu.memory_total_bytes") ?? 0),
                0, Array.Empty<string>(), Array.Empty<string>()))).ToArray();
    }

    private void AddGpu(IDictionary<string, object?> v, string? gpuId)
    {
        var adapters = GetGpuAdapters(_root);
        var adapter = adapters.FirstOrDefault(a => a.Id == gpuId) ?? adapters.FirstOrDefault();
        if (adapter is null) return;
        v["gpu.name"] = adapter.Name;
        v["gpu.adapter_id"] = adapter.Id;
        v["gpu.physical_index"] = adapter.PhysicalIndex;
        v["gpu.count"] = adapters.Count;
        if (adapter.Id.StartsWith("nvidia:", StringComparison.Ordinal))
        {
            var measured = LinuxNvidia.Read().FirstOrDefault(d => Equals(d["gpu.adapter_id"], adapter.Id));
            if (measured is not null) foreach (var pair in measured) v[pair.Key] = pair.Value;
            return;
        }
        var device = PathAt($"sys/class/drm/{adapter.Id}/device");
        Put(v, "gpu.usage", Number(Path.Combine(device, "gpu_busy_percent")));
        var total = Number(Path.Combine(device, "mem_info_vram_total"));
        var used = Number(Path.Combine(device, "mem_info_vram_used"));
        Put(v, "gpu.memory_total_bytes", total);
        Put(v, "gpu.memory_used_bytes", used);
        Put(v, "gpu.vram_bytes", total);
        Put(v, "gpu.dedicated_total_bytes", total);
        Put(v, "gpu.dedicated_used_bytes", used);
        if (total > 0 && used.HasValue) v["gpu.dedicated_usage"] = used.Value / total.Value * 100;
        foreach (var hwmon in Directories(Path.Combine(device, "hwmon")))
        {
            Put(v, "gpu.temperature", Number(Path.Combine(hwmon, "temp1_input")) / 1000);
            Put(v, "gpu.power_w", Number(Path.Combine(hwmon, "power1_average")) / 1_000_000);
        }
    }

    private static void Put(IDictionary<string, object?> v, string key, object? value)
    {
        if (value is not null) v[key] = value;
    }
    private static double? Parse(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;
    private static double? Number(string path) => Parse(Read(path));
    private static string? Read(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    private static string[] Directories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
