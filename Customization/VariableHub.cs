using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EndfieldChargePlus.Customization;

public sealed class VariableHub : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<string, HttpCacheEntry> _httpCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PingTargetState> _pingStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _pingGate = new();
    private readonly AdvancedVariableProvider _advanced = new();

    private const int PingTimeoutMs = 1000;
    private const double PingFullScaleMs = 999d;

    private DateTime _lastNetworkAt = DateTime.UtcNow;
    private long _lastRx;
    private long _lastTx;

    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _cpuPrimed;

    private string _cachedCpuName = "CPU";
    private string _cachedCpuManufacturer = "";
    private string _cachedCpuArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
    private double _cachedCpuGhz;
    private double _cachedCpuMaxGhz;
    private int _physicalCores;
    private int _logicalProcessors = Environment.ProcessorCount;
    private int _cpuSocketCount;
    private bool _cachedCpuVirtualizationEnabled;
    private double _cachedCpuL2Kb;
    private double _cachedCpuL3Kb;
    private DateTime _nextCpuInfoRead = DateTime.MinValue;
    private DateTime _nextCpuFrequencyRead = DateTime.MinValue;

    private double _cachedBatteryRemaining;
    private double _cachedBatteryFull;
    private double _cachedBatteryDesign;
    private double _cachedBatteryRateWatts;
    private double _cachedBatteryChargeRateWatts;
    private double _cachedBatteryDischargeRateWatts;
    private double _cachedBatteryVoltageMv;
    private int _cachedBatteryCycleCount;
    private bool _cachedBatteryCharging;
    private bool _cachedBatteryDischarging;
    private bool _cachedBatteryAcOnline;
    private DateTime _nextBatteryStatusRead = DateTime.MinValue;
    private DateTime _nextBatteryStaticRead = DateTime.MinValue;

    private double _cachedDiskTotal;
    private double _cachedDiskFree;
    private string _cachedDiskRoot = "";
    private string _cachedDiskLabel = "";
    private string _cachedDiskFileSystem = "";
    private double _cachedDiskReadBps;
    private double _cachedDiskWriteBps;
    private double _cachedDiskActivePercent;
    private double _cachedDiskQueueLength;
    private DateTime _nextDiskRead = DateTime.MinValue;
    private DateTime _nextDiskPerfRead = DateTime.MinValue;

    private string _cachedGpuAdapterId = "";
    private string _cachedGpuName = "GPU";
    private int _cachedGpuPhysicalIndex;
    private IReadOnlyList<string> _cachedGpuPerfLuidTokens = Array.Empty<string>();
    private double _cachedGpuVramBytes;
    private double _cachedGpuSharedLimitBytes;
    private double _cachedGpuUsage;
    private double _cachedGpuUsage3D;
    private double _cachedGpuUsageCompute;
    private double _cachedGpuUsageCopy;
    private double _cachedGpuUsageVideoDecode;
    private double _cachedGpuUsageVideoEncode;
    private double _cachedGpuDedicatedBytes;
    private double _cachedGpuSharedBytes;
    private DateTime _nextGpuInfoRead = DateTime.MinValue;
    private DateTime _nextGpuUsageRead = DateTime.MinValue;

    private DeepSeekCache? _deepSeekCache;
    private AppLanguage _lastLanguage = LocalizationManager.Current;

    public async Task<Dictionary<string, object?>> SnapshotAsync(
        CustomHudSettings settings,
        IEnumerable<string>? requestedVariables = null,
        string? gpuAdapterId = null,
        string? pingTarget = null,
        string? probeProtocol = null,
        int probePort = 443,
        CancellationToken ct = default)
    {
        if (_lastLanguage != LocalizationManager.Current)
        {
            _lastLanguage = LocalizationManager.Current;
            lock (_pingGate)
                _pingStates.Clear();
        }

        HashSet<string>? requested = requestedVariables is null
            ? null
            : new HashSet<string>(requestedVariables.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);

        var vars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // 时钟与系统基础信息非常轻量，直接读取，避免 1 秒定时器 + Task 调度造成跳秒。
        bool needSystem = NeedsPrefix(requested, "system.");
        bool needTime = NeedsPrefix(requested, "time.");
        bool needApp = NeedsPrefix(requested, "app.");
        bool needDisplay = NeedsPrefix(requested, "display.");
        if (needSystem || needTime)
            AddClockAndSystem(vars, needSystem, needTime);
        if (needApp)
            AddApp(vars);
        if (needDisplay)
            AddDisplay(vars);

        // WMI / 性能计数器 / 网络统计留在后台线程。
        bool needCpu = NeedsPrefix(requested, "cpu.");
        bool needMemory = NeedsPrefix(requested, "memory.");
        bool needBattery = NeedsPrefix(requested, "battery.");
        bool needNetwork = NeedsPrefix(requested, "network.");
        bool needDisk = NeedsPrefix(requested, "disk.");
        bool needGpu = NeedsPrefix(requested, "gpu.");
        bool needPing = NeedsPrefix(requested, "ping.") || NeedsPrefix(requested, "probe.");

        if (needCpu || needMemory || needBattery || needNetwork || needDisk || needGpu)
        {
            var background = await Task.Run(() =>
            {
                var local = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (needCpu || needMemory)
                    AddCpuAndMemory(local, needCpu, needMemory);
                if (needBattery)
                    AddBattery(local);
                if (needNetwork)
                    AddNetwork(local);
                if (needDisk)
                    AddDisk(local);
                if (needGpu)
                    AddGpu(local, gpuAdapterId);
                return local;
            }, ct).ConfigureAwait(false);

            foreach (var pair in background)
                vars[pair.Key] = pair.Value;
        }

        if (needPing)
            await AddPingAsync(vars, pingTarget, probeProtocol, probePort, ct).ConfigureAwait(false);

        // Advanced variables are real collectors layered over the core snapshot.
        // Hardware/driver-dependent fields are omitted when unavailable instead of returning fake zeros.
        await _advanced.EnrichAsync(vars, settings, requested, gpuAdapterId, ct).ConfigureAwait(false);

        if (NeedsPrefix(requested, "deepseek.period."))
            AddDeepSeekPeriod(vars, settings);

        if (NeedsPrefix(requested, "deepseek.")
            && !NeedsOnlyPeriodVariables(requested))
            await AddDeepSeekBalanceAsync(vars, settings, ct).ConfigureAwait(false);

        if (NeedsPrefix(requested, "custom."))
            await AddCustomHttpAsync(vars, settings, ct, requested).ConfigureAwait(false);

        return vars;
    }

    public static IReadOnlyList<string> BuiltInVariableKeys =>
        VariableCatalog.AllBuiltIns.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static void AddClockAndSystem(IDictionary<string, object?> v, bool includeSystem, bool includeTime)
    {
        var now = DateTime.Now;
        var uptimeSeconds = Math.Max(0d, Environment.TickCount64 / 1000d);

        if (includeSystem)
        {
            var tz = TimeZoneInfo.Local;
            var osVersion = Environment.OSVersion.Version;
            v["system.time"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            v["system.date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            v["system.datetime"] = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            v["system.uptime_seconds"] = uptimeSeconds;
            v["system.uptime_text"] = FormatDurationLong(uptimeSeconds);
            v["system.boot_time"] = now.AddSeconds(-uptimeSeconds).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            v["system.machine_name"] = Environment.MachineName;
            try { v["system.host_name"] = Dns.GetHostName(); } catch { v["system.host_name"] = Environment.MachineName; }
            v["system.user_name"] = Environment.UserName;
            v["system.user_domain"] = Environment.UserDomainName;
            v["system.os_description"] = RuntimeInformation.OSDescription.Trim();
            v["system.os_version"] = osVersion.ToString();
            v["system.os_build"] = osVersion.Build;
            v["system.os_architecture"] = RuntimeInformation.OSArchitecture.ToString();
            v["system.process_architecture"] = RuntimeInformation.ProcessArchitecture.ToString();
            v["system.framework_version"] = RuntimeInformation.FrameworkDescription;
            v["system.processor_count"] = Environment.ProcessorCount;
            v["system.timezone_id"] = tz.Id;
            v["system.timezone_name"] = tz.DisplayName;
            v["system.utc_offset_hours"] = tz.GetUtcOffset(now).TotalHours;
            v["system.culture"] = CultureInfo.CurrentCulture.Name;
        }

        if (includeTime)
        {
            var seconds = now.TimeOfDay.TotalSeconds;
            var dayTotal = TimeSpan.FromDays(1).TotalSeconds;
            var weekStart = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
            var weekEnd = weekStart.AddDays(7);
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var monthEnd = monthStart.AddMonths(1);
            var yearStart = new DateTime(now.Year, 1, 1);
            var yearEnd = yearStart.AddYears(1);
            double weekElapsed = Math.Max(0d, (now - weekStart).TotalSeconds);
            double weekTotal = Math.Max(1d, (weekEnd - weekStart).TotalSeconds);
            double monthElapsed = Math.Max(0d, (now - monthStart).TotalSeconds);
            double monthTotal = Math.Max(1d, (monthEnd - monthStart).TotalSeconds);
            double yearElapsed = Math.Max(0d, (now - yearStart).TotalSeconds);
            double yearTotal = Math.Max(1d, (yearEnd - yearStart).TotalSeconds);
            var dto = new DateTimeOffset(now);

            v["time.current"] = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            v["time.current_12h"] = now.ToString("hh:mm:ss tt", CultureInfo.CurrentCulture);
            v["time.date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            v["time.datetime"] = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            v["time.iso"] = dto.ToString("O", CultureInfo.InvariantCulture);
            v["time.year"] = now.Year;
            v["time.month"] = now.Month;
            v["time.month_name"] = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(now.Month);
            v["time.day"] = now.Day;
            v["time.day_of_week"] = DayOfWeekZh(now.DayOfWeek);
            v["time.day_of_week_en"] = now.DayOfWeek.ToString();
            v["time.day_of_year"] = now.DayOfYear;
            v["time.week_of_year"] = ISOWeek.GetWeekOfYear(now);
            v["time.hour"] = now.Hour;
            v["time.minute"] = now.Minute;
            v["time.second"] = now.Second;
            v["time.millisecond"] = now.Millisecond;
            v["time.is_weekend"] = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            v["time.unix_seconds"] = dto.ToUnixTimeSeconds();
            v["time.unix_milliseconds"] = dto.ToUnixTimeMilliseconds();

            v["time.day.elapsed_seconds"] = seconds;
            v["time.day.remaining_seconds"] = Math.Max(0d, dayTotal - seconds);
            v["time.day.progress"] = Math.Clamp(seconds / dayTotal * 100d, 0d, 100d);
            v["time.week.elapsed_seconds"] = weekElapsed;
            v["time.week.remaining_seconds"] = Math.Max(0d, weekTotal - weekElapsed);
            v["time.week.progress"] = Math.Clamp(weekElapsed / weekTotal * 100d, 0d, 100d);
            v["time.month.elapsed_seconds"] = monthElapsed;
            v["time.month.remaining_seconds"] = Math.Max(0d, monthTotal - monthElapsed);
            v["time.month.progress"] = Math.Clamp(monthElapsed / monthTotal * 100d, 0d, 100d);
            v["time.year.elapsed_seconds"] = yearElapsed;
            v["time.year.remaining_seconds"] = Math.Max(0d, yearTotal - yearElapsed);
            v["time.year.progress"] = Math.Clamp(yearElapsed / yearTotal * 100d, 0d, 100d);
        }
    }

    private static void AddApp(IDictionary<string, object?> v)
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version?.ToString(3) ?? "0.1.0";
            var start = p.StartTime;
            var uptime = Math.Max(0d, (DateTime.Now - start).TotalSeconds);
            v["app.name"] = p.ProcessName;
            v["app.version"] = version;
            v["app.pid"] = p.Id;
            v["app.start_time"] = start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            v["app.uptime_seconds"] = uptime;
            v["app.uptime_text"] = FormatDurationLong(uptime);
            v["app.working_set_bytes"] = (double)p.WorkingSet64;
            v["app.private_memory_bytes"] = (double)p.PrivateMemorySize64;
            v["app.virtual_memory_bytes"] = (double)p.VirtualMemorySize64;
            v["app.thread_count"] = p.Threads.Count;
            v["app.handle_count"] = p.HandleCount;
            v["app.cpu_time_seconds"] = p.TotalProcessorTime.TotalSeconds;
        }
        catch { }
    }

    private static void AddDisplay(IDictionary<string, object?> v)
    {
        try
        {
            int dpi = 96;
            try { dpi = (int)GetDpiForSystem(); } catch { }
            v["display.primary_width_px"] = GetSystemMetrics(0);
            v["display.primary_height_px"] = GetSystemMetrics(1);
            v["display.virtual_x_px"] = GetSystemMetrics(76);
            v["display.virtual_y_px"] = GetSystemMetrics(77);
            v["display.virtual_width_px"] = GetSystemMetrics(78);
            v["display.virtual_height_px"] = GetSystemMetrics(79);
            v["display.monitor_count"] = GetSystemMetrics(80);
            v["display.system_dpi"] = dpi;
            v["display.scale_percent"] = dpi / 96d * 100d;
        }
        catch { }
    }

    private void AddCpuAndMemory(IDictionary<string, object?> v, bool includeCpu, bool includeMemory)
    {
        if (includeCpu)
        {
            double usage = 0d, userUsage = 0d, kernelUsage = 0d, idlePercent = 0d;
            if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            {
                ulong idle = ToUInt64(idleFt), kernel = ToUInt64(kernelFt), user = ToUInt64(userFt);
                if (_cpuPrimed)
                {
                    ulong idleDelta = idle - _lastIdle;
                    ulong kernelDelta = kernel - _lastKernel;
                    ulong userDelta = user - _lastUser;
                    ulong totalDelta = kernelDelta + userDelta;
                    if (totalDelta > 0)
                    {
                        idlePercent = Math.Clamp(idleDelta / (double)totalDelta * 100d, 0d, 100d);
                        userUsage = Math.Clamp(userDelta / (double)totalDelta * 100d, 0d, 100d);
                        kernelUsage = Math.Clamp(Math.Max(0d, kernelDelta - idleDelta) / totalDelta * 100d, 0d, 100d);
                        usage = Math.Clamp(userUsage + kernelUsage, 0d, 100d);
                    }
                }

                _lastIdle = idle;
                _lastKernel = kernel;
                _lastUser = user;
                _cpuPrimed = true;
            }

            v["cpu.usage"] = usage;
            v["cpu.user_usage"] = userUsage;
            v["cpu.kernel_usage"] = kernelUsage;
            v["cpu.idle_percent"] = _cpuPrimed ? idlePercent : Math.Max(0d, 100d - usage);

            if (DateTime.UtcNow >= _nextCpuInfoRead)
            {
                _nextCpuInfoRead = DateTime.UtcNow.AddMinutes(10);
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Name, Manufacturer, Architecture, MaxClockSpeed, CurrentClockSpeed, NumberOfCores, NumberOfLogicalProcessors, VirtualizationFirmwareEnabled, L2CacheSize, L3CacheSize FROM Win32_Processor");
                    int sockets = 0, cores = 0, logical = 0;
                    double maxMhz = 0d;
                    double currentMhz = 0d;
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        sockets++;
                        if (sockets == 1)
                        {
                            _cachedCpuName = Convert.ToString(mo["Name"])?.Trim() ?? "CPU";
                            _cachedCpuManufacturer = Convert.ToString(mo["Manufacturer"])?.Trim() ?? "";
                            _cachedCpuArchitecture = CpuArchitectureName(mo["Architecture"]);
                            _cachedCpuVirtualizationEnabled = ToBool(mo["VirtualizationFirmwareEnabled"]);
                            _cachedCpuL2Kb = ToDoubleSafe(mo["L2CacheSize"]);
                            _cachedCpuL3Kb = ToDoubleSafe(mo["L3CacheSize"]);
                        }
                        cores += ToIntSafe(mo["NumberOfCores"]);
                        logical += ToIntSafe(mo["NumberOfLogicalProcessors"]);
                        maxMhz = Math.Max(maxMhz, ToDoubleSafe(mo["MaxClockSpeed"]));
                        currentMhz = Math.Max(currentMhz, ToDoubleSafe(mo["CurrentClockSpeed"]));
                    }

                    _cpuSocketCount = Math.Max(1, sockets);
                    _physicalCores = cores > 0 ? cores : Math.Max(1, Environment.ProcessorCount / 2);
                    _logicalProcessors = logical > 0 ? logical : Environment.ProcessorCount;
                    _cachedCpuMaxGhz = maxMhz / 1000d;
                    if (_cachedCpuGhz <= 0 && currentMhz > 0)
                        _cachedCpuGhz = currentMhz / 1000d;
                }
                catch { }
            }

            // Win32_Processor.CurrentClockSpeed is often nominal/static on modern CPUs.
            // Processor Information exposes Windows' live effective-performance ratio.
            if (DateTime.UtcNow >= _nextCpuFrequencyRead)
            {
                _nextCpuFrequencyRead = DateTime.UtcNow.AddMilliseconds(750);
                try
                {
                    using var perf = new ManagementObjectSearcher(
                        "root\\CIMV2",
                        "SELECT * FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
                    foreach (ManagementObject mo in perf.Get())
                    {
                        double mhz = ToDoubleSafe(mo.Properties["ProcessorFrequency"]?.Value);
                        double performance = ToDoubleSafe(mo.Properties["PercentProcessorPerformance"]?.Value);
                        if (_cachedCpuMaxGhz > 0 && performance > 0)
                            _cachedCpuGhz = _cachedCpuMaxGhz * performance / 100d;
                        else if (mhz > 0)
                            _cachedCpuGhz = mhz / 1000d;
                        break;
                    }
                }
                catch
                {
                    try
                    {
                        using var fallback = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
                        foreach (ManagementObject mo in fallback.Get())
                        {
                            double mhz = ToDoubleSafe(mo["CurrentClockSpeed"]);
                            if (mhz > 0) _cachedCpuGhz = mhz / 1000d;
                            break;
                        }
                    }
                    catch { }
                }
            }

            v["cpu.name"] = _cachedCpuName;
            v["cpu.manufacturer"] = _cachedCpuManufacturer;
            v["cpu.architecture"] = _cachedCpuArchitecture;
            v["cpu.frequency_ghz"] = _cachedCpuGhz;
            v["cpu.frequency_mhz"] = _cachedCpuGhz * 1000d;
            v["cpu.max_frequency_ghz"] = _cachedCpuMaxGhz;
            v["cpu.max_frequency_mhz"] = _cachedCpuMaxGhz * 1000d;
            v["cpu.frequency_percent"] = _cachedCpuMaxGhz <= 0d ? 0d : Math.Max(0d, _cachedCpuGhz / _cachedCpuMaxGhz * 100d);
            v["cpu.physical_cores"] = _physicalCores;
            v["cpu.logical_processors"] = _logicalProcessors;
            v["cpu.socket_count"] = _cpuSocketCount;
            v["cpu.virtualization_enabled"] = _cachedCpuVirtualizationEnabled;
            v["cpu.l2_cache_kb"] = _cachedCpuL2Kb;
            v["cpu.l3_cache_kb"] = _cachedCpuL3Kb;
        }

        if (includeMemory)
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                double total = mem.ullTotalPhys;
                double avail = mem.ullAvailPhys;
                double used = Math.Max(0, total - avail);
                double commitLimit = mem.ullTotalPageFile;
                double commitAvail = mem.ullAvailPageFile;
                double commitUsed = Math.Max(0d, commitLimit - commitAvail);
                double virtualTotal = mem.ullTotalVirtual;
                double virtualAvail = mem.ullAvailVirtual;
                double virtualUsed = Math.Max(0d, virtualTotal - virtualAvail);

                v["memory.total_bytes"] = total;
                v["memory.available_bytes"] = avail;
                v["memory.used_bytes"] = used;
                v["memory.usage"] = total <= 0 ? 0 : used / total * 100d;
                v["memory.free_percent"] = total <= 0 ? 0 : avail / total * 100d;
                v["memory.commit_limit_bytes"] = commitLimit;
                v["memory.commit_available_bytes"] = commitAvail;
                v["memory.commit_used_bytes"] = commitUsed;
                v["memory.commit_usage"] = commitLimit <= 0 ? 0 : commitUsed / commitLimit * 100d;
                v["memory.virtual_total_bytes"] = virtualTotal;
                v["memory.virtual_available_bytes"] = virtualAvail;
                v["memory.virtual_used_bytes"] = virtualUsed;
                v["memory.virtual_usage"] = virtualTotal <= 0 ? 0 : virtualUsed / virtualTotal * 100d;
            }

            try
            {
                using var perf = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT CacheBytes, PoolPagedBytes, PoolNonpagedBytes FROM Win32_PerfFormattedData_PerfOS_Memory");
                foreach (ManagementObject mo in perf.Get())
                {
                    v["memory.cache_bytes"] = ToDoubleSafe(mo["CacheBytes"]);
                    v["memory.paged_pool_bytes"] = ToDoubleSafe(mo["PoolPagedBytes"]);
                    v["memory.nonpaged_pool_bytes"] = ToDoubleSafe(mo["PoolNonpagedBytes"]);
                    break;
                }
            }
            catch
            {
                v["memory.cache_bytes"] = 0d;
                v["memory.paged_pool_bytes"] = 0d;
                v["memory.nonpaged_pool_bytes"] = 0d;
            }
        }
    }

    private void AddBattery(IDictionary<string, object?> v)
    {
        var now = DateTime.UtcNow;

        if (now >= _nextBatteryStatusRead)
        {
            _nextBatteryStatusRead = now.AddSeconds(5);
            try
            {
                using var statusSearcher = new ManagementObjectSearcher(
                    "root\\WMI",
                    "SELECT RemainingCapacity, ChargeRate, DischargeRate, Charging, Discharging, PowerOnline, Voltage FROM BatteryStatus");
                foreach (ManagementObject mo in statusSearcher.Get())
                {
                    _cachedBatteryRemaining = ToDoubleSafe(mo["RemainingCapacity"]);
                    _cachedBatteryChargeRateWatts = Math.Max(0d, ToDoubleSafe(mo["ChargeRate"])) / 1000d;
                    _cachedBatteryDischargeRateWatts = Math.Max(0d, ToDoubleSafe(mo["DischargeRate"])) / 1000d;
                    _cachedBatteryCharging = ToBool(mo["Charging"]);
                    _cachedBatteryDischarging = ToBool(mo["Discharging"]);
                    _cachedBatteryAcOnline = ToBool(mo["PowerOnline"]);
                    _cachedBatteryVoltageMv = Math.Max(0d, ToDoubleSafe(mo["Voltage"]));
                    _cachedBatteryRateWatts = _cachedBatteryCharging
                        ? _cachedBatteryChargeRateWatts
                        : (_cachedBatteryDischarging ? _cachedBatteryDischargeRateWatts : Math.Max(_cachedBatteryChargeRateWatts, _cachedBatteryDischargeRateWatts));
                    break;
                }
            }
            catch { }
        }

        if (now >= _nextBatteryStaticRead)
        {
            _nextBatteryStaticRead = now.AddMinutes(10);
            try
            {
                using var fullSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
                foreach (ManagementObject mo in fullSearcher.Get())
                {
                    _cachedBatteryFull = ToDoubleSafe(mo["FullChargedCapacity"]);
                    break;
                }
            }
            catch { }

            try
            {
                using var designSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT DesignedCapacity FROM BatteryStaticData");
                foreach (ManagementObject mo in designSearcher.Get())
                {
                    _cachedBatteryDesign = ToDoubleSafe(mo["DesignedCapacity"]);
                    break;
                }
            }
            catch { }

            try
            {
                using var cycleSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT CycleCount FROM BatteryCycleCount");
                foreach (ManagementObject mo in cycleSearcher.Get())
                {
                    _cachedBatteryCycleCount = ToIntSafe(mo["CycleCount"]);
                    break;
                }
            }
            catch { }
        }

        double percent = _cachedBatteryFull > 0
            ? Math.Clamp(_cachedBatteryRemaining / _cachedBatteryFull * 100d, 0d, 100d)
            : 0d;
        double lifeSeconds = 0d;
        double fullLifeSeconds = 0d;
        bool saverOn = false;

        if (GetSystemPowerStatus(out var ps))
        {
            _cachedBatteryAcOnline = ps.ACLineStatus == 1;
            if (ps.BatteryLifePercent != 255)
                percent = ps.BatteryLifePercent;
            if (ps.BatteryLifeTime >= 0) lifeSeconds = ps.BatteryLifeTime;
            if (ps.BatteryFullLifeTime >= 0) fullLifeSeconds = ps.BatteryFullLifeTime;
            saverOn = ps.SystemStatusFlag == 1;
        }

        double remaining = _cachedBatteryRemaining;
        if (remaining <= 0 && _cachedBatteryFull > 0)
            remaining = _cachedBatteryFull * percent / 100d;

        string status = _cachedBatteryCharging
            ? LocalizationManager.Text("充电中", "Charging")
            : (_cachedBatteryDischarging
                ? LocalizationManager.Text("使用电池", "On Battery")
                : (_cachedBatteryAcOnline
                    ? LocalizationManager.Text("已接通电源", "AC Connected")
                    : LocalizationManager.Text("未知", "Unknown")));

        v["battery.percent"] = percent;
        v["battery.remaining_mwh"] = remaining;
        v["battery.full_mwh"] = _cachedBatteryFull;
        v["battery.design_mwh"] = _cachedBatteryDesign;
        v["battery.remaining_wh"] = remaining / 1000d;
        v["battery.full_wh"] = _cachedBatteryFull / 1000d;
        v["battery.design_wh"] = _cachedBatteryDesign / 1000d;
        v["battery.ac_online"] = _cachedBatteryAcOnline;
        v["battery.charging"] = _cachedBatteryCharging;
        v["battery.discharging"] = _cachedBatteryDischarging;
        v["battery.rate_watts"] = _cachedBatteryRateWatts;
        v["battery.charge_rate_watts"] = _cachedBatteryChargeRateWatts;
        v["battery.discharge_rate_watts"] = _cachedBatteryDischargeRateWatts;
        v["battery.voltage_mv"] = _cachedBatteryVoltageMv;
        v["battery.voltage_v"] = _cachedBatteryVoltageMv / 1000d;
        v["battery.health_percent"] = _cachedBatteryDesign > 0 && _cachedBatteryFull > 0
            ? Math.Clamp(_cachedBatteryFull / _cachedBatteryDesign * 100d, 0d, 200d)
            : 0d;
        v["battery.time_remaining_seconds"] = lifeSeconds;
        v["battery.time_remaining_text"] = lifeSeconds > 0 ? FormatDuration(lifeSeconds) : LocalizationManager.Text("未知", "Unknown");
        v["battery.full_life_seconds"] = fullLifeSeconds;
        v["battery.saver_on"] = saverOn;
        v["battery.status_text"] = status;
        v["battery.power_source"] = _cachedBatteryAcOnline ? LocalizationManager.Text("交流电源", "AC") : LocalizationManager.Text("电池", "Battery");
        v["battery.cycle_count"] = _cachedBatteryCycleCount;
    }

    private void AddGpu(IDictionary<string, object?> v, string? requestedAdapterId)
    {
        var adapters = GpuAdapterCatalog.GetAdapters();
        var selected = !string.IsNullOrWhiteSpace(requestedAdapterId)
            ? adapters.FirstOrDefault(x => x.MatchesId(requestedAdapterId))
            : null;
        selected ??= adapters.FirstOrDefault();

        if (selected is not null && !string.Equals(_cachedGpuAdapterId, selected.Id, StringComparison.OrdinalIgnoreCase))
        {
            _cachedGpuAdapterId = selected.Id;
            _cachedGpuName = selected.Name;
            _cachedGpuPhysicalIndex = selected.PhysicalIndex;
            _cachedGpuPerfLuidTokens = selected.PerfLuidTokens.ToArray();
            _cachedGpuVramBytes = selected.DedicatedMemoryBytes;
            _cachedGpuSharedLimitBytes = selected.SharedMemoryBytes;
            _cachedGpuUsage = 0;
            _cachedGpuUsage3D = 0;
            _cachedGpuUsageCompute = 0;
            _cachedGpuUsageCopy = 0;
            _cachedGpuUsageVideoDecode = 0;
            _cachedGpuUsageVideoEncode = 0;
            _cachedGpuDedicatedBytes = 0;
            _cachedGpuSharedBytes = 0;
            _nextGpuInfoRead = DateTime.MinValue;
            _nextGpuUsageRead = DateTime.MinValue;
        }

        if (DateTime.UtcNow >= _nextGpuInfoRead)
        {
            _nextGpuInfoRead = DateTime.UtcNow.AddMinutes(2);
            var refreshed = GpuAdapterCatalog.GetAdapters(forceRefresh: true).FirstOrDefault(x =>
                x.MatchesId(_cachedGpuAdapterId));
            if (refreshed is not null)
            {
                _cachedGpuName = refreshed.Name;
                _cachedGpuPhysicalIndex = refreshed.PhysicalIndex;
                _cachedGpuPerfLuidTokens = refreshed.PerfLuidTokens.ToArray();
                if (refreshed.DedicatedMemoryBytes > 0)
                    _cachedGpuVramBytes = refreshed.DedicatedMemoryBytes;
                if (refreshed.SharedMemoryBytes > 0)
                    _cachedGpuSharedLimitBytes = refreshed.SharedMemoryBytes;
            }
        }

        if (DateTime.UtcNow >= _nextGpuUsageRead)
        {
            _nextGpuUsageRead = DateTime.UtcNow.AddMilliseconds(700);

            try
            {
                // GPU Engine exposes one entry per process/engine. Sum processes that use the
                // same engine, then use the busiest engine as the overall utilization, which
                // closely follows Task Manager and also covers Compute/Copy/Video engines.
                var engineTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var typeTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                using var engineSearcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
                foreach (ManagementObject mo in engineSearcher.Get())
                {
                    string name = Convert.ToString(mo["Name"]) ?? "";
                    if (!MatchesSelectedGpuCounter(name)) continue;

                    double utilization = ToDoubleSafe(mo["UtilizationPercentage"]);
                    var engine = Regex.Match(name, @"(?:^|_)eng_(?<engine>\d+)_engtype_(?<type>[^_]+)", RegexOptions.IgnoreCase);
                    var luid = Regex.Match(name, @"luid_0x[0-9a-f]+_0x[0-9a-f]+", RegexOptions.IgnoreCase);
                    string adapterKey = luid.Success ? luid.Value : $"phys:{_cachedGpuPhysicalIndex}";
                    string type = engine.Success ? engine.Groups["type"].Value : "Other";
                    string engineKey = engine.Success
                        ? $"{engine.Groups["engine"].Value}:{type}"
                        : name;
                    string key = $"{adapterKey}|{engineKey}";
                    engineTotals[key] = engineTotals.TryGetValue(key, out var oldValue)
                        ? oldValue + utilization
                        : utilization;

                    string typeKey = $"{adapterKey}|{type}";
                    typeTotals[typeKey] = typeTotals.TryGetValue(typeKey, out var oldTypeValue)
                        ? oldTypeValue + utilization
                        : utilization;
                }

                // Duplicate logical DXGI views of one physical GPU can expose equivalent engine
                // rows under different LUIDs. Using the busiest physical-engine view avoids
                // triple-counting those aliases while still matching Task Manager semantics.
                _cachedGpuUsage = engineTotals.Count == 0
                    ? 0d
                    : Math.Clamp(engineTotals.Values.Max(), 0d, 100d);
                _cachedGpuUsage3D = MaxGpuType(typeTotals, "3D");
                _cachedGpuUsageCompute = Math.Max(MaxGpuType(typeTotals, "Compute_0"), MaxGpuType(typeTotals, "Compute_1"));
                _cachedGpuUsageCopy = MaxGpuType(typeTotals, "Copy");
                _cachedGpuUsageVideoDecode = Math.Max(MaxGpuType(typeTotals, "VideoDecode"), MaxGpuType(typeTotals, "Video_Decode"));
                _cachedGpuUsageVideoEncode = Math.Max(MaxGpuType(typeTotals, "VideoEncode"), MaxGpuType(typeTotals, "Video_Encode"));
            }
            catch { }

            try
            {
                double dedicated = 0;
                double shared = 0;
                using var memSearcher = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    "SELECT Name, DedicatedUsage, SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
                foreach (ManagementObject mo in memSearcher.Get())
                {
                    string name = Convert.ToString(mo["Name"]) ?? "";
                    if (!MatchesSelectedGpuCounter(name)) continue;
                    // One physical adapter may be represented by multiple logical DXGI/LUID
                    // aliases. GPUAdapterMemory reports the same physical usage for such aliases,
                    // so take the maximum instead of summing duplicate rows.
                    dedicated = Math.Max(dedicated, Convert.ToDouble(mo["DedicatedUsage"] ?? 0));
                    shared = Math.Max(shared, Convert.ToDouble(mo["SharedUsage"] ?? 0));
                }
                _cachedGpuDedicatedBytes = Math.Max(0, dedicated);
                _cachedGpuSharedBytes = Math.Max(0, shared);
            }
            catch { }
        }

        double dedicatedTotal = Math.Max(0, _cachedGpuVramBytes);
        double sharedLimit = Math.Max(0, _cachedGpuSharedLimitBytes);

        // The default GPU HUD represents physical/dedicated VRAM. Shared system memory remains
        // available through gpu.shared_used_bytes / gpu.shared_limit_bytes, but is not added to
        // the displayed capacity (otherwise an iGPU with 512 MB reserved VRAM can misleadingly
        // appear as a 16 GB GPU on a 32 GB machine).
        double displayUsed = _cachedGpuDedicatedBytes;
        double displayTotal = dedicatedTotal;

        // Counter data can become available one sample before total-memory metadata. Avoid a
        // misleading zero denominator while keeping the sample visible.
        if (displayTotal <= 0 && displayUsed > 0)
            displayTotal = displayUsed;

        double totalUsed = Math.Max(0d, _cachedGpuDedicatedBytes + _cachedGpuSharedBytes);
        double totalLimit = Math.Max(0d, dedicatedTotal + sharedLimit);

        v["gpu.name"] = _cachedGpuName;
        v["gpu.adapter_id"] = _cachedGpuAdapterId;
        v["gpu.physical_index"] = _cachedGpuPhysicalIndex;
        v["gpu.count"] = adapters.Count;
        v["gpu.vram_bytes"] = dedicatedTotal;
        v["gpu.dedicated_total_bytes"] = dedicatedTotal;
        v["gpu.shared_limit_bytes"] = sharedLimit;
        v["gpu.usage"] = _cachedGpuUsage;
        v["gpu.usage_3d"] = _cachedGpuUsage3D;
        v["gpu.usage_compute"] = _cachedGpuUsageCompute;
        v["gpu.usage_copy"] = _cachedGpuUsageCopy;
        v["gpu.usage_video_decode"] = _cachedGpuUsageVideoDecode;
        v["gpu.usage_video_encode"] = _cachedGpuUsageVideoEncode;
        v["gpu.dedicated_used_bytes"] = _cachedGpuDedicatedBytes;
        v["gpu.shared_used_bytes"] = _cachedGpuSharedBytes;
        v["gpu.dedicated_usage"] = dedicatedTotal <= 0d ? 0d : Math.Clamp(_cachedGpuDedicatedBytes / dedicatedTotal * 100d, 0d, 100d);
        v["gpu.shared_usage"] = sharedLimit <= 0d ? 0d : Math.Clamp(_cachedGpuSharedBytes / sharedLimit * 100d, 0d, 100d);
        v["gpu.memory_used_bytes"] = displayUsed;
        v["gpu.memory_total_bytes"] = displayTotal;
        v["gpu.total_memory_used_bytes"] = totalUsed;
        v["gpu.total_memory_limit_bytes"] = totalLimit;
        v["gpu.total_memory_usage"] = totalLimit <= 0d ? 0d : Math.Clamp(totalUsed / totalLimit * 100d, 0d, 100d);
        v["gpu.uses_unified_memory"] = selected?.UsesUnifiedMemory ?? false;
    }

    private bool MatchesSelectedGpuCounter(string counterName)
    {
        if (_cachedGpuPerfLuidTokens.Any(token =>
                !string.IsNullOrWhiteSpace(token)
                && counterName.Contains(token, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Fallback for systems/drivers where DXGI LUID is not surfaced in the WMI instance name.
        return Regex.IsMatch(
            counterName,
            $@"(?:^|_)phys_{_cachedGpuPhysicalIndex}(?:_|$)",
            RegexOptions.IgnoreCase);
    }

    private async Task AddPingAsync(
        IDictionary<string, object?> v,
        string? configuredTarget,
        string? configuredProtocol,
        int configuredPort,
        CancellationToken ct)
    {
        string target = string.IsNullOrWhiteSpace(configuredTarget) ? "1.1.1.1" : configuredTarget.Trim();
        string protocol = NormalizeProbeProtocol(configuredProtocol);
        int port = Math.Clamp(configuredPort <= 0 ? 443 : configuredPort, 1, 65535);
        string stateKey = $"{protocol}|{target}|{(protocol == "ICMP" ? 0 : port)}";

        PingTargetState state;
        Task<PingProbeResult>? probeTask = null;
        var now = DateTime.UtcNow;

        lock (_pingGate)
        {
            if (!_pingStates.TryGetValue(stateKey, out var existingState))
            {
                state = new PingTargetState();
                _pingStates[stateKey] = state;
            }
            else
            {
                state = existingState;
            }

            if (state.LastProbe is null || now >= state.NextProbeUtc)
            {
                if (state.InFlight is null || state.InFlight.IsCompleted)
                {
                    state.InFlight = ProbeEndpointAsync(target, protocol, port, ct);
                    state.NextProbeUtc = now.AddMilliseconds(800);
                }
                probeTask = state.InFlight;
            }
        }

        if (probeTask is not null)
        {
            PingProbeResult result;
            try { result = await probeTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result = new PingProbeResult(false, IPStatus.Unknown, 0, "", 0, LocalizationManager.Text("检测失败", "Failed"), ex.Message, DateTime.Now);
            }

            lock (_pingGate)
            {
                state.LastProbe = result;
                state.InFlight = null;
                state.History.Enqueue(result);
                while (state.History.Count > 20)
                    state.History.Dequeue();
                if (result.Success)
                    state.LastSuccessLocal = result.LocalTime;
            }
        }

        PingProbeResult current;
        List<PingProbeResult> history;
        DateTime? lastSuccess;
        lock (_pingGate)
        {
            current = state.LastProbe ?? new PingProbeResult(false, IPStatus.TimedOut, 0, "", 0, LocalizationManager.Text("等待检测", "Waiting"), LocalizationManager.Text("尚未完成检测", "Probe not completed"), DateTime.Now);
            history = state.History.ToList();
            lastSuccess = state.LastSuccessLocal;
        }

        int sent = history.Count;
        int received = history.Count(x => x.Success);
        int lost = Math.Max(0, sent - received);
        double loss = sent <= 0 ? (current.Success ? 0d : 100d) : lost * 100d / sent;
        var successLatencies = history.Where(x => x.Success).Select(x => (double)x.LatencyMs).ToList();
        double avg = successLatencies.Count == 0 ? 0d : successLatencies.Average();
        double min = successLatencies.Count == 0 ? 0d : successLatencies.Min();
        double max = successLatencies.Count == 0 ? 0d : successLatencies.Max();
        double jitter = 0d;
        if (successLatencies.Count > 1)
        {
            double sum = 0d;
            for (int i = 1; i < successLatencies.Count; i++)
                sum += Math.Abs(successLatencies[i] - successLatencies[i - 1]);
            jitter = sum / (successLatencies.Count - 1);
        }

        double latencyForProgress = current.Success ? current.LatencyMs : PingFullScaleMs;
        double latencyProgress = Math.Clamp(latencyForProgress / PingFullScaleMs * 100d, 0d, 100d);
        double latencyMs = current.Success ? current.LatencyMs : PingFullScaleMs;
        string latencyText = current.Success ? $"{current.LatencyMs}ms" : current.StatusText;
        string endpoint = protocol == "ICMP" ? target : $"{target}:{port}";

        // Protocol-neutral variables used by the built-in network packet probe preset.
        v["probe.target"] = target;
        v["probe.address"] = current.Address;
        v["probe.protocol"] = protocol;
        v["probe.port"] = protocol == "ICMP" ? 0 : port;
        v["probe.endpoint"] = endpoint;
        v["probe.online"] = current.Success;
        v["probe.status_text"] = current.StatusText;
        v["probe.reply_status"] = current.ReplyStatus;
        v["probe.latency_ms"] = latencyMs;
        v["probe.latency_text"] = latencyText;
        v["probe.latency_progress"] = latencyProgress;
        v["probe.full_scale_ms"] = PingFullScaleMs;
        v["probe.timeout_ms"] = PingTimeoutMs;
        v["probe.ttl"] = current.Ttl;
        v["probe.sent"] = sent;
        v["probe.received"] = received;
        v["probe.lost"] = lost;
        v["probe.loss_percent"] = Math.Clamp(loss, 0d, 100d);
        v["probe.avg_latency_ms"] = avg;
        v["probe.min_latency_ms"] = min;
        v["probe.max_latency_ms"] = max;
        v["probe.jitter_ms"] = jitter;
        v["probe.last_success"] = lastSuccess?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
        v["probe.error"] = current.Success ? "" : current.Error;
        if (current.DnsResolveMs.HasValue) v["probe.dns_resolve_time"] = current.DnsResolveMs.Value;
        if (current.TcpConnectMs.HasValue) v["probe.tcp_connect_time"] = current.TcpConnectMs.Value;

        // Backward-compatible ping.* aliases remain available for existing custom schemes.
        v["ping.target"] = target;
        v["ping.address"] = current.Address;
        v["ping.protocol"] = protocol;
        v["ping.port"] = protocol == "ICMP" ? 0 : port;
        v["ping.endpoint"] = endpoint;
        v["ping.online"] = current.Success;
        v["ping.status_text"] = current.StatusText;
        v["ping.reply_status"] = current.ReplyStatus;
        v["ping.latency_ms"] = latencyMs;
        v["ping.latency_text"] = latencyText;
        v["ping.progress"] = latencyProgress;
        v["ping.full_scale_ms"] = PingFullScaleMs;
        v["ping.timeout_ms"] = PingTimeoutMs;
        v["ping.ttl"] = current.Ttl;
        v["ping.sent"] = sent;
        v["ping.received"] = received;
        v["ping.lost"] = lost;
        v["ping.loss_percent"] = Math.Clamp(loss, 0d, 100d);
        v["ping.avg_latency_ms"] = avg;
        v["ping.min_latency_ms"] = min;
        v["ping.max_latency_ms"] = max;
        v["ping.jitter_ms"] = jitter;
        v["ping.last_success"] = lastSuccess?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
        v["ping.error"] = current.Success ? "" : current.Error;
    }

    private static string NormalizeProbeProtocol(string? configuredProtocol)
    {
        string protocol = (configuredProtocol ?? "ICMP").Trim().ToUpperInvariant();
        return protocol is "TCP" or "UDP" ? protocol : "ICMP";
    }

    private static Task<PingProbeResult> ProbeEndpointAsync(string target, string protocol, int port, CancellationToken ct) =>
        protocol switch
        {
            "TCP" => ProbeTcpAsync(target, port, ct),
            "UDP" => ProbeUdpAsync(target, port, ct),
            _ => ProbeIcmpAsync(target, ct),
        };

    private static async Task<PingProbeResult> ProbeIcmpAsync(string target, CancellationToken ct)
    {
        double? dnsMs = null;
        try
        {
            string probeTarget = target;
            if (!IPAddress.TryParse(target, out _))
            {
                var dns = Stopwatch.StartNew();
                var addresses = await Dns.GetHostAddressesAsync(target).WaitAsync(ct).ConfigureAwait(false);
                dns.Stop();
                dnsMs = dns.Elapsed.TotalMilliseconds;
                var address = addresses.FirstOrDefault(x => x.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
                if (address is not null) probeTarget = address.ToString();
            }
            else dnsMs = 0d;

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(probeTarget, PingTimeoutMs).WaitAsync(ct).ConfigureAwait(false);
            bool ok = reply.Status == IPStatus.Success;
            return new PingProbeResult(
                ok,
                reply.Status,
                ok ? reply.RoundtripTime : 0,
                reply.Address?.ToString() ?? "",
                reply.Options?.Ttl ?? 0,
                ok ? LocalizationManager.Text("在线", "Online") : PingStatusText(reply.Status),
                reply.Status.ToString(),
                DateTime.Now,
                dnsMs,
                null);
        }
        catch (OperationCanceledException) { throw; }
        catch (PingException ex)
        {
            return new PingProbeResult(false, IPStatus.Unknown, 0, "", 0, LocalizationManager.Text("检测失败", "Failed"), ex.InnerException?.Message ?? ex.Message, DateTime.Now, dnsMs, null);
        }
        catch (Exception ex)
        {
            return new PingProbeResult(false, IPStatus.Unknown, 0, "", 0, LocalizationManager.Text("检测失败", "Failed"), ex.Message, DateTime.Now, dnsMs, null);
        }
    }

    private static async Task<PingProbeResult> ProbeTcpAsync(string target, int port, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        double? dnsMs = null;
        try
        {
            IPAddress address;
            if (IPAddress.TryParse(target, out var parsed) && parsed is not null)
            {
                address = parsed;
                dnsMs = 0d;
            }
            else
            {
                var dns = Stopwatch.StartNew();
                var addresses = await Dns.GetHostAddressesAsync(target).WaitAsync(ct).ConfigureAwait(false);
                dns.Stop();
                dnsMs = dns.Elapsed.TotalMilliseconds;
                address = addresses.FirstOrDefault(x => x.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                          ?? throw new SocketException((int)SocketError.HostNotFound);
            }

            using var tcp = new TcpClient(address.AddressFamily);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PingTimeoutMs);
            var connect = Stopwatch.StartNew();
            await tcp.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
            connect.Stop();
            total.Stop();
            string remote = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? address.ToString();
            return new PingProbeResult(true, IPStatus.Success, total.ElapsedMilliseconds, remote, 0, LocalizationManager.Text("在线", "Online"), "Connected", DateTime.Now, dnsMs, connect.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            total.Stop();
            return new PingProbeResult(false, IPStatus.TimedOut, 0, "", 0, LocalizationManager.Text("超时", "Timed out"), "TimedOut", DateTime.Now, dnsMs, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (SocketException ex)
        {
            total.Stop();
            return new PingProbeResult(false, IPStatus.Unknown, 0, "", 0, LocalizationManager.Text("连接失败", "Connection failed"), ex.SocketErrorCode.ToString(), DateTime.Now, dnsMs, null);
        }
        catch (Exception ex)
        {
            total.Stop();
            return new PingProbeResult(false, IPStatus.Unknown, 0, "", 0, LocalizationManager.Text("连接失败", "Connection failed"), ex.Message, DateTime.Now, dnsMs, null);
        }
    }

    private static async Task<PingProbeResult> ProbeUdpAsync(string target, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        double? dnsMs = null;
        try
        {
            IPAddress address;
            if (IPAddress.TryParse(target, out var parsedAddress) && parsedAddress is not null)
            {
                address = parsedAddress;
                dnsMs = 0d;
            }
            else
            {
                var dns = Stopwatch.StartNew();
                var addresses = await Dns.GetHostAddressesAsync(target).WaitAsync(ct).ConfigureAwait(false);
                dns.Stop();
                dnsMs = dns.Elapsed.TotalMilliseconds;
                address = addresses.FirstOrDefault(x => x.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                          ?? throw new SocketException((int)SocketError.HostNotFound);
            }

            using var udp = new UdpClient(address.AddressFamily);
            udp.Connect(new IPEndPoint(address, port));
            byte[] payload = { 0x00 };
            await udp.SendAsync(payload, payload.Length).ConfigureAwait(false);
            var result = await udp.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(PingTimeoutMs), ct).ConfigureAwait(false);
            sw.Stop();
            return new PingProbeResult(true, IPStatus.Success, sw.ElapsedMilliseconds, result.RemoteEndPoint.Address.ToString(), 0, LocalizationManager.Text("在线", "Online"), "Response", DateTime.Now, dnsMs, null);
        }
        catch (TimeoutException)
        {
            sw.Stop();
            return new PingProbeResult(false, IPStatus.TimedOut, 0, "", 0, LocalizationManager.Text("超时", "Timed out"), "TimedOut", DateTime.Now, dnsMs, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (SocketException ex)
        {
            sw.Stop();
            string status = ex.SocketErrorCode == SocketError.ConnectionReset ? LocalizationManager.Text("端口不可达", "Port unreachable") : LocalizationManager.Text("检测失败", "Failed");
            return new PingProbeResult(false, IPStatus.Unknown, 0, "", 0, status, ex.SocketErrorCode.ToString(), DateTime.Now, dnsMs, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new PingProbeResult(false, IPStatus.Unknown, 0, "", 0, LocalizationManager.Text("检测失败", "Failed"), ex.Message, DateTime.Now, dnsMs, null);
        }
    }

    private static string PingStatusText(IPStatus status) => status switch
    {
        IPStatus.Success => LocalizationManager.Text("在线", "Online"),
        IPStatus.TimedOut => LocalizationManager.Text("超时", "Timed out"),
        IPStatus.DestinationHostUnreachable => LocalizationManager.Text("主机不可达", "Host unreachable"),
        IPStatus.DestinationNetworkUnreachable => LocalizationManager.Text("网络不可达", "Network unreachable"),
        IPStatus.DestinationPortUnreachable => LocalizationManager.Text("端口不可达", "Port unreachable"),
        IPStatus.BadDestination => LocalizationManager.Text("目标无效", "Invalid target"),
        IPStatus.BadRoute => LocalizationManager.Text("路由无效", "Invalid route"),
        IPStatus.TtlExpired => LocalizationManager.Text("TTL 已过期", "TTL expired"),
        _ => LocalizationManager.Text("不可达", "Unreachable")
    };

    private void AddNetwork(IDictionary<string, object?> v)
    {
        try
        {
            long rx = 0, tx = 0;
            long linkSpeed = 0, maxLinkSpeed = 0;
            long packetsReceived = 0, packetsSent = 0, receiveErrors = 0, sendErrors = 0;
            int activeCount = 0;
            var names = new List<string>();
            var types = new List<string>();
            var ipv4 = new List<string>();
            var ipv6 = new List<string>();
            var gateways = new List<string>();
            var dns = new List<string>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                activeCount++;
                names.Add(nic.Name);
                types.Add(nic.NetworkInterfaceType.ToString());
                var s = nic.GetIPv4Statistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
                packetsReceived += s.UnicastPacketsReceived;
                packetsSent += s.UnicastPacketsSent;
                receiveErrors += s.IncomingPacketsWithErrors;
                sendErrors += s.OutgoingPacketsWithErrors;
                if (nic.Speed > 0)
                {
                    linkSpeed += nic.Speed;
                    maxLinkSpeed = Math.Max(maxLinkSpeed, nic.Speed);
                }

                try
                {
                    var props = nic.GetIPProperties();
                    foreach (var u in props.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            ipv4.Add(u.Address.ToString());
                        else if (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                                 && !u.Address.IsIPv6LinkLocal)
                            ipv6.Add(u.Address.ToString());
                    }
                    foreach (var g in props.GatewayAddresses)
                    {
                        var text = g.Address?.ToString();
                        if (!string.IsNullOrWhiteSpace(text) && text != "0.0.0.0" && text != "::")
                            gateways.Add(text);
                    }
                    foreach (var d in props.DnsAddresses)
                    {
                        var text = d.ToString();
                        if (!string.IsNullOrWhiteSpace(text)) dns.Add(text);
                    }
                }
                catch { }
            }

            var now = DateTime.UtcNow;
            var dt = Math.Max(0.05, (now - _lastNetworkAt).TotalSeconds);
            double download = _lastRx == 0 ? 0d : Math.Max(0, (rx - _lastRx) / dt);
            double upload = _lastTx == 0 ? 0d : Math.Max(0, (tx - _lastTx) / dt);
            double total = download + upload;
            double utilization = linkSpeed <= 0 ? 0d : Math.Clamp(total * 8d / linkSpeed * 100d, 0d, 100d);

            v["network.download_bps"] = download;
            v["network.upload_bps"] = upload;
            v["network.total_bps"] = total;
            v["network.download_mbps"] = download * 8d / 1_000_000d;
            v["network.upload_mbps"] = upload * 8d / 1_000_000d;
            v["network.total_mbps"] = total * 8d / 1_000_000d;
            v["network.link_speed_bps"] = (double)linkSpeed;
            v["network.max_link_speed_bps"] = (double)maxLinkSpeed;
            v["network.utilization_percent"] = utilization;
            v["network.total_received_bytes"] = (double)rx;
            v["network.total_sent_bytes"] = (double)tx;
            v["network.total_transferred_bytes"] = (double)rx + tx;
            v["network.active_interface_count"] = activeCount;
            v["network.interface_names"] = string.Join(", ", names.Distinct(StringComparer.OrdinalIgnoreCase));
            v["network.interface_types"] = string.Join(", ", types.Distinct(StringComparer.OrdinalIgnoreCase));
            v["network.ipv4_addresses"] = string.Join(", ", ipv4.Distinct(StringComparer.OrdinalIgnoreCase));
            v["network.ipv6_addresses"] = string.Join(", ", ipv6.Distinct(StringComparer.OrdinalIgnoreCase));
            v["network.default_gateways"] = string.Join(", ", gateways.Distinct(StringComparer.OrdinalIgnoreCase));
            v["network.dns_servers"] = string.Join(", ", dns.Distinct(StringComparer.OrdinalIgnoreCase));
            v["network.available"] = NetworkInterface.GetIsNetworkAvailable();
            v["network.packets_received"] = (double)packetsReceived;
            v["network.packets_sent"] = (double)packetsSent;
            v["network.receive_errors"] = (double)receiveErrors;
            v["network.send_errors"] = (double)sendErrors;

            _lastRx = rx;
            _lastTx = tx;
            _lastNetworkAt = now;
        }
        catch
        {
            v["network.download_bps"] = 0d;
            v["network.upload_bps"] = 0d;
            v["network.total_bps"] = 0d;
            v["network.download_mbps"] = 0d;
            v["network.upload_mbps"] = 0d;
            v["network.total_mbps"] = 0d;
            v["network.link_speed_bps"] = 0d;
            v["network.max_link_speed_bps"] = 0d;
            v["network.utilization_percent"] = 0d;
            v["network.available"] = false;
        }
    }

    private void AddDisk(IDictionary<string, object?> v)
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        string systemName = systemRoot.TrimEnd('\\', '/');

        if (DateTime.UtcNow >= _nextDiskRead)
        {
            _nextDiskRead = DateTime.UtcNow.AddSeconds(5);
            try
            {
                var d = new DriveInfo(systemRoot);
                if (d.IsReady)
                {
                    _cachedDiskTotal = d.TotalSize;
                    _cachedDiskFree = d.AvailableFreeSpace;
                    _cachedDiskRoot = d.Name;
                    _cachedDiskLabel = d.VolumeLabel;
                    _cachedDiskFileSystem = d.DriveFormat;
                }
            }
            catch { }
        }

        if (DateTime.UtcNow >= _nextDiskPerfRead)
        {
            _nextDiskPerfRead = DateTime.UtcNow.AddMilliseconds(900);
            try
            {
                string escaped = systemName.Replace("'", "''");
                using var perf = new ManagementObjectSearcher(
                    "root\\CIMV2",
                    $"SELECT DiskReadBytesPersec, DiskWriteBytesPersec, PercentDiskTime, CurrentDiskQueueLength FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk WHERE Name='{escaped}'");
                foreach (ManagementObject mo in perf.Get())
                {
                    _cachedDiskReadBps = ToDoubleSafe(mo["DiskReadBytesPersec"]);
                    _cachedDiskWriteBps = ToDoubleSafe(mo["DiskWriteBytesPersec"]);
                    _cachedDiskActivePercent = Math.Clamp(ToDoubleSafe(mo["PercentDiskTime"]), 0d, 100d);
                    _cachedDiskQueueLength = ToDoubleSafe(mo["CurrentDiskQueueLength"]);
                    break;
                }
            }
            catch { }
        }

        double used = Math.Max(0, _cachedDiskTotal - _cachedDiskFree);
        v["disk.system.root"] = string.IsNullOrWhiteSpace(_cachedDiskRoot) ? systemRoot : _cachedDiskRoot;
        v["disk.system.label"] = _cachedDiskLabel;
        v["disk.system.filesystem"] = _cachedDiskFileSystem;
        v["disk.system.total_bytes"] = _cachedDiskTotal;
        v["disk.system.free_bytes"] = _cachedDiskFree;
        v["disk.system.used_bytes"] = used;
        v["disk.system.usage"] = _cachedDiskTotal <= 0 ? 0 : used / _cachedDiskTotal * 100d;
        v["disk.system.free_percent"] = _cachedDiskTotal <= 0 ? 0 : _cachedDiskFree / _cachedDiskTotal * 100d;
        v["disk.system.read_bps"] = _cachedDiskReadBps;
        v["disk.system.write_bps"] = _cachedDiskWriteBps;
        v["disk.system.io_bps"] = _cachedDiskReadBps + _cachedDiskWriteBps;
        v["disk.system.active_percent"] = _cachedDiskActivePercent;
        v["disk.system.queue_length"] = _cachedDiskQueueLength;

        double allTotal = 0d, allFree = 0d;
        int count = 0;
        var driveNames = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                count++;
                double total = drive.TotalSize;
                double free = drive.AvailableFreeSpace;
                double driveUsed = Math.Max(0d, total - free);
                allTotal += total;
                allFree += free;
                driveNames.Add(drive.Name.TrimEnd('\\', '/'));

                string letter = drive.Name.TrimEnd('\\', '/').TrimEnd(':').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(letter)) continue;
                v[$"disk.{letter}.used_bytes"] = driveUsed;
                v[$"disk.{letter}.free_bytes"] = free;
                v[$"disk.{letter}.total_bytes"] = total;
                v[$"disk.{letter}.usage"] = total <= 0d ? 0d : driveUsed / total * 100d;
                v[$"disk.{letter}.label"] = drive.VolumeLabel;
                v[$"disk.{letter}.filesystem"] = drive.DriveFormat;
            }
        }
        catch { }

        double allUsed = Math.Max(0d, allTotal - allFree);
        v["disk.fixed.count"] = count;
        v["disk.fixed.total_bytes"] = allTotal;
        v["disk.fixed.free_bytes"] = allFree;
        v["disk.fixed.used_bytes"] = allUsed;
        v["disk.fixed.usage"] = allTotal <= 0d ? 0d : allUsed / allTotal * 100d;
        v["disk.fixed.list"] = string.Join(", ", driveNames);
    }

    private async Task AddDeepSeekBalanceAsync(IDictionary<string, object?> v, CustomHudSettings settings, CancellationToken ct)
    {
        var apiKey = SecretStore.Unprotect(settings.DeepSeekApiKeyProtected);
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        if (_deepSeekCache is { } cache && DateTime.UtcNow < cache.ExpiresAt)
        {
            CopyDeepSeek(v, cache);
            return;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            var apiTimer = Stopwatch.StartNew();
            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            apiTimer.Stop();
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            bool available = root.TryGetProperty("is_available", out var av) && av.ValueKind == JsonValueKind.True;
            double total = 0, granted = 0, topped = 0;

            if (root.TryGetProperty("balance_infos", out var infos) && infos.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in infos.EnumerateArray())
                {
                    if (item.TryGetProperty("currency", out var cur)
                        && !string.Equals(cur.GetString(), "CNY", StringComparison.OrdinalIgnoreCase))
                        continue;
                    total += ReadJsonNumber(item, "total_balance");
                    granted += ReadJsonNumber(item, "granted_balance");
                    topped += ReadJsonNumber(item, "topped_up_balance");
                }
            }

            _deepSeekCache = new DeepSeekCache(available, total, granted, topped, apiTimer.Elapsed.TotalMilliseconds, DateTime.UtcNow.AddMinutes(1));
            CopyDeepSeek(v, _deepSeekCache);
        }
        catch { }
    }

    private static void CopyDeepSeek(IDictionary<string, object?> v, DeepSeekCache c)
    {
        v["deepseek.available"] = c.Available;
        v["deepseek.available_text"] = c.Available ? LocalizationManager.Text("可用", "Available") : LocalizationManager.Text("不可用", "Unavailable");
        v["deepseek.balance"] = c.Total;
        v["deepseek.balance_text"] = $"¥{c.Total:0.00}";
        v["deepseek.granted_balance"] = c.Granted;
        v["deepseek.topped_up_balance"] = c.Topped;
        v["deepseek.api.latency_ms"] = c.LatencyMs;
    }

    public static string GetDeepSeekPeriodNameZh(CustomHudSettings settings)
    {
        var (_, _, peak) = GetDeepSeekPeriodState(settings);
        return peak ? "高峰" : "低谷";
    }

    private static (DateTime Beijing, List<(TimeSpan Start, TimeSpan End)> Windows, bool Peak) GetDeepSeekPeriodState(CustomHudSettings settings)
    {
        DateTime beijing;
        try
        {
            beijing = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).DateTime;
        }
        catch
        {
            beijing = DateTime.UtcNow.AddHours(8);
        }

        var windows = ParseWindows(settings.DeepSeekPeakWindows);
        bool weekday = beijing.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
        bool peak = weekday && windows.Any(w => beijing.TimeOfDay >= w.Start && beijing.TimeOfDay < w.End);
        return (beijing, windows, peak);
    }

    private static void AddDeepSeekPeriod(IDictionary<string, object?> v, CustomHudSettings settings)
    {
        var (beijing, windows, peak) = GetDeepSeekPeriodState(settings);
        var next = FindNextTransition(beijing, windows);
        var remaining = Math.Max(0, (next - beijing).TotalSeconds);
        var segmentStart = FindCurrentSegmentStart(beijing, windows, peak);
        var total = Math.Max(1, (next - segmentStart).TotalSeconds);
        var progress = Math.Clamp((beijing - segmentStart).TotalSeconds / total * 100d, 0d, 100d);

        string periodNameZh = peak ? "高峰" : "低谷";
        string periodNameEn = peak ? "PEAK" : "OFF-PEAK";
        string remainingText = LocalizationManager.Text(
            $"{periodNameZh}时段剩余{FormatDuration(remaining)}",
            $"{(peak ? "Peak" : "Off-peak")} left {FormatDuration(remaining)}");
        string progressText = LocalizationManager.Text(
            $"{periodNameZh}已过{Math.Round(progress, MidpointRounding.AwayFromZero):0}%",
            $"{(peak ? "Peak" : "Off-peak")} {Math.Round(progress, MidpointRounding.AwayFromZero):0}%");

        v["deepseek.period.name"] = periodNameEn;
        v["deepseek.period.name_zh"] = LocalizationManager.IsEnglish ? periodNameEn : periodNameZh;
        v["deepseek.period.is_peak"] = peak;
        v["deepseek.period.is_off_peak"] = !peak;
        v["deepseek.period.remaining_seconds"] = remaining;
        v["deepseek.period.remaining_text"] = remainingText;
        v["deepseek.period.progress"] = progress;
        v["deepseek.period.progress_text"] = progressText;

        // DeepSeek peak/off-peak rules are defined in Beijing Time (UTC+08:00).
        // Keep the existing next_switch_* variables in Beijing Time for compatibility,
        // and expose explicit local-time variants for users outside China.
        var nextBeijing = new DateTimeOffset(
            DateTime.SpecifyKind(next, DateTimeKind.Unspecified),
            TimeSpan.FromHours(8));
        var nextLocal = nextBeijing.ToLocalTime();

        v["deepseek.period.next_switch_time"] = nextBeijing.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        v["deepseek.period.next_switch_datetime"] = nextBeijing.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        v["deepseek.period.next_switch_time_local"] = nextLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        v["deepseek.period.next_switch_datetime_local"] = nextLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        v["deepseek.period.timezone"] = LocalizationManager.Text("北京时间 (UTC+08:00)", "Beijing Time (UTC+08:00)");
        v["deepseek.period.local_timezone"] = $"{TimeZoneInfo.Local.Id} (UTC{FormatUtcOffset(nextLocal.Offset)})";
    }

    private async Task AddCustomHttpAsync(
        IDictionary<string, object?> v,
        CustomHudSettings settings,
        CancellationToken ct,
        HashSet<string>? requested)
    {
        foreach (var source in settings.HttpSources.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Url)))
        {
            var sourcePrefix = $"custom.{Sanitize(source.Name)}.";
            if (requested is not null && !requested.Any(k => k.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            string cacheKey = source.Name + "|" + source.Url;
            if (!_httpCache.TryGetValue(cacheKey, out var cache) || DateTime.UtcNow >= cache.ExpiresAt)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, source.Url);
                    foreach (var h in source.Headers)
                    {
                        var value = ExpandEnvironment(h.Value);
                        req.Headers.TryAddWithoutValidation(h.Key, value);
                    }

                    using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
                    res.EnsureSuccessStatusCode();
                    cache = new HttpCacheEntry(
                        await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false),
                        DateTime.UtcNow.AddSeconds(Math.Clamp(source.RefreshSeconds, 5, 86400)));
                    _httpCache[cacheKey] = cache;
                }
                catch
                {
                    continue;
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(cache.Json);
                foreach (var f in source.Fields)
                {
                    if (TryJsonPath(doc.RootElement, f.JsonPath, out var value))
                        v[$"custom.{Sanitize(source.Name)}.{Sanitize(f.Variable)}"] = JsonToObject(value);
                }
            }
            catch { }
        }
    }

    private static bool NeedsPrefix(HashSet<string>? requested, string prefix)
    {
        if (requested is null) return true;
        return requested.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool NeedsAny(HashSet<string>? requested, params string[] keys)
    {
        if (requested is null) return true;
        return keys.Any(requested.Contains);
    }

    private static bool NeedsOnlyPeriodVariables(HashSet<string>? requested)
    {
        if (requested is null) return false;
        var deepSeekKeys = requested.Where(k => k.StartsWith("deepseek.", StringComparison.OrdinalIgnoreCase)).ToList();
        return deepSeekKeys.Count > 0 && deepSeekKeys.All(k => k.StartsWith("deepseek.period.", StringComparison.OrdinalIgnoreCase));
    }

    private static double ToDoubleSafe(object? value)
    {
        try { return Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture); }
        catch { return 0d; }
    }

    private static int ToIntSafe(object? value)
    {
        try { return Convert.ToInt32(value ?? 0, CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    private static bool ToBool(object? value)
    {
        try { return Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture); }
        catch { return false; }
    }

    private static string CpuArchitectureName(object? value)
    {
        int code = ToIntSafe(value);
        return code switch
        {
            0 => "x86",
            5 => "ARM",
            6 => "Itanium",
            9 => "x64",
            12 => "ARM64",
            _ => RuntimeInformation.ProcessArchitecture.ToString()
        };
    }

    private static double MaxGpuType(IReadOnlyDictionary<string, double> totals, string type)
    {
        double max = 0d;
        string suffix = "|" + type;
        foreach (var pair in totals)
        {
            if (pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                max = Math.Max(max, pair.Value);
        }
        return Math.Clamp(max, 0d, 100d);
    }

    private static string DayOfWeekZh(DayOfWeek day) => LocalizationManager.IsEnglish
        ? day.ToString()
        : day switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            DayOfWeek.Sunday => "星期日",
            _ => ""
        };

    private static string FormatDurationLong(double seconds)
    {
        long safe = Math.Max(0L, (long)Math.Floor(seconds));
        var ts = TimeSpan.FromSeconds(safe);
        if (ts.TotalDays >= 1)
            return LocalizationManager.Text($"{(int)ts.TotalDays}天 {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}", $"{(int)ts.TotalDays}d {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}");
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static string ExpandEnvironment(string value) =>
        System.Text.RegularExpressions.Regex.Replace(
            value ?? "",
            @"\$\{env:(?<n>[A-Za-z_][A-Za-z0-9_]*)\}",
            m => Environment.GetEnvironmentVariable(m.Groups["n"].Value) ?? "");

    private static string Sanitize(string s) =>
        new((s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

    private static bool TryJsonPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path) || path == "$" || path == ".") return true;
        var p = path.Trim().TrimStart('$').TrimStart('.');
        foreach (var part in p.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part;
            int bracket = token.IndexOf('[');
            string prop = bracket >= 0 ? token[..bracket] : token;
            if (!string.IsNullOrEmpty(prop))
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(prop, out value))
                    return false;
            }

            while (bracket >= 0)
            {
                int end = token.IndexOf(']', bracket + 1);
                if (end < 0
                    || !int.TryParse(token[(bracket + 1)..end], out var idx)
                    || value.ValueKind != JsonValueKind.Array
                    || idx < 0
                    || idx >= value.GetArrayLength())
                    return false;
                value = value[idx];
                bracket = token.IndexOf('[', end + 1);
            }
        }
        return true;
    }

    private static object? JsonToObject(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number when e.TryGetDouble(out var d) => d,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => e.GetRawText()
    };

    private static double ReadJsonNumber(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
        if (p.ValueKind == JsonValueKind.String
            && double.TryParse(p.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d))
            return d;
        return 0;
    }

    private static string FormatDuration(double seconds)
    {
        var safe = Math.Max(0L, (long)Math.Floor(seconds));
        var ts = TimeSpan.FromSeconds(safe);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static string FormatUtcOffset(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        offset = offset.Duration();
        return $"{sign}{(int)offset.TotalHours:00}:{offset.Minutes:00}";
    }

    private static List<(TimeSpan Start, TimeSpan End)> ParseWindows(string text)
    {
        var list = new List<(TimeSpan, TimeSpan)>();
        foreach (var part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var x = part.Split('-', 2, StringSplitOptions.TrimEntries);
            if (x.Length == 2
                && TimeSpan.TryParse(x[0], out var a)
                && TimeSpan.TryParse(x[1], out var b)
                && b > a)
                list.Add((a, b));
        }
        return list.OrderBy(x => x.Item1).ToList();
    }

    private static DateTime FindNextTransition(DateTime now, List<(TimeSpan Start, TimeSpan End)> windows)
    {
        for (int d = 0; d < 8; d++)
        {
            var day = now.Date.AddDays(d);
            bool weekday = day.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
            if (!weekday) continue;
            foreach (var w in windows)
            {
                var a = day + w.Start;
                var b = day + w.End;
                if (a > now) return a;
                if (b > now) return b;
            }
        }
        return now.AddHours(1);
    }

    private static DateTime FindCurrentSegmentStart(DateTime now, List<(TimeSpan Start, TimeSpan End)> windows, bool peak)
    {
        if (peak)
        {
            var w = windows.FirstOrDefault(x => now.TimeOfDay >= x.Start && now.TimeOfDay < x.End);
            return now.Date + w.Start;
        }

        for (int d = 0; d < 8; d++)
        {
            var day = now.Date.AddDays(-d);
            bool weekday = day.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
            if (!weekday) continue;
            foreach (var w in windows.OrderByDescending(x => x.End))
            {
                var end = day + w.End;
                if (end <= now) return end;
            }
        }
        return now.AddHours(-1);
    }

    private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    private sealed record DeepSeekCache(bool Available, double Total, double Granted, double Topped, double LatencyMs, DateTime ExpiresAt);
    private sealed class PingTargetState
    {
        public DateTime NextProbeUtc { get; set; } = DateTime.MinValue;
        public Task<PingProbeResult>? InFlight { get; set; }
        public PingProbeResult? LastProbe { get; set; }
        public Queue<PingProbeResult> History { get; } = new();
        public DateTime? LastSuccessLocal { get; set; }
    }

    private sealed record PingProbeResult(
        bool Success,
        IPStatus Status,
        long LatencyMs,
        string Address,
        int Ttl,
        string StatusText,
        string ReplyStatus,
        DateTime LocalTime,
        double? DnsResolveMs = null,
        double? TcpConnectMs = null)
    {
        public string Error => Success ? "" : ReplyStatus;
    }

    private sealed record HttpCacheEntry(string Json, DateTime ExpiresAt);

    public void Dispose() { _advanced.Dispose(); }
}
