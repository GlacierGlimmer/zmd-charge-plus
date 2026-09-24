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
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace EndfieldChargePlus.Customization;

/// <summary>
/// Advanced variable collector. Every variable exposed here has a real data source.
/// Hardware/driver dependent values are omitted when Windows/the driver does not expose them;
/// TemplateEngine then renders "--" rather than inventing a value.
/// </summary>
internal sealed class AdvancedVariableProvider : IDisposable
{
    private static readonly HttpClient FastHttp = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly object _gate = new();
    private readonly Queue<(DateTime At, double Usage)> _cpuSamples = new();
    private readonly Dictionary<int, ProcCpuSample> _processCpu = new();
    private DateTime _nextPerfOsRead = DateTime.MinValue;
    private PerfOsSnapshot _perfOs = new();
    private DateTime _nextMemoryStaticRead = DateTime.MinValue;
    private MemoryStaticSnapshot _memoryStatic = new();
    private DateTime _nextSystemStaticRead = DateTime.MinValue;
    private SystemStaticSnapshot _systemStatic = new();
    private DateTime _nextNetworkStaticRead = DateTime.MinValue;
    private NetworkStaticSnapshot _networkStatic = new();
    private DateTime _nextProcessRead = DateTime.MinValue;
    private ProcessSnapshot _process = new();
    private DateTime _nextDeveloperRead = DateTime.MinValue;
    private DeveloperSnapshot _developer = new();
    private DateTime _nextSecurityRead = DateTime.MinValue;
    private SecuritySnapshot _security = new();
    private DateTime _nextUsbRead = DateTime.MinValue;
    private UsbSnapshot _usb = new();
    private DateTime _nextPublicIpv4Read = DateTime.MinValue;
    private DateTime _nextPublicIpv6Read = DateTime.MinValue;
    private string _publicIpv4 = "";
    private string _publicIpv6 = "";
    private DateTime _nextDiskAdvancedRead = DateTime.MinValue;
    private Dictionary<char, DiskAdvancedSnapshot> _diskAdvanced = new();
    private DateTime _nextNvidiaRead = DateTime.MinValue;
    private List<NvidiaSnapshot> _nvidia = new();
    private DateTime _nextHardwareRead = DateTime.MinValue;
    private HardwareSnapshot _hardware = new();
    private Computer? _computer;
    private uint _lastClipboardSequence;
    private DateTime _clipboardLastUpdated = DateTime.MinValue;
    private AppLanguage _lastLanguage = LocalizationManager.Current;

    public async Task EnrichAsync(
        IDictionary<string, object?> vars,
        CustomHudSettings settings,
        HashSet<string>? requested,
        string? gpuAdapterId,
        CancellationToken ct)
    {
        if (_lastLanguage != LocalizationManager.Current)
        {
            _lastLanguage = LocalizationManager.Current;
            InvalidateLanguageSensitiveCaches();
        }

        bool Exact(string key) => requested is null || requested.Contains(key);
        bool NeedAdvanced(string prefix) => requested is null || requested.Any(k =>
            k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (VariableCatalog.IsAdvancedKey(k) || IsAdvancedDynamicDiskKey(k)));

        // Only collect an advanced category when the active profile actually references one of its advanced variables.
        // This keeps the ordinary CPU/RAM/GPU HUD as light as the core implementation.
        await Task.Run(() =>
        {
            if (NeedAdvanced("battery.")) AddBatteryAdvanced(vars);
            if (NeedAdvanced("cpu.")) AddCpuAdvanced(vars);
            if (NeedAdvanced("memory.")) AddMemoryAdvanced(vars);
            if (NeedAdvanced("gpu.")) AddGpuAdvanced(vars);
            if (NeedAdvanced("network.")) AddNetworkAdvanced(vars);
            if (NeedAdvanced("disk.")) AddDiskAdvanced(vars, requested);
            if (NeedAdvanced("system.")) AddSystemAdvanced(vars);
            if (NeedAdvanced("process.")) AddProcessAdvanced(vars);
            if (NeedAdvanced("app.")) AddAppAdvanced(vars, settings);
            if (NeedAdvanced("display.")) AddDisplayAdvanced(vars);
            if (NeedAdvanced("time.")) AddWorldTime(vars);
            if (NeedAdvanced("clipboard.")) AddClipboard(vars);
            if (NeedAdvanced("usb.") || NeedAdvanced("peripheral.")) AddUsbAndPeripherals(vars);
            if (NeedAdvanced("dev.")) AddDeveloper(vars);
            if (NeedAdvanced("security.")) AddSecurity(vars);
        }, ct).ConfigureAwait(false);

        if (Exact("network.public_ipv4") || Exact("network.public_ipv6"))
            await AddPublicIpAsync(vars, Exact("network.public_ipv4"), Exact("network.public_ipv6"), ct).ConfigureAwait(false);
    }

    private void InvalidateLanguageSensitiveCaches()
    {
        // Some cached snapshots contain app-generated localized status text. Force the
        // next requested read to rebuild those strings in the newly selected language.
        _nextPerfOsRead = DateTime.MinValue;
        _nextMemoryStaticRead = DateTime.MinValue;
        _nextSystemStaticRead = DateTime.MinValue;
        _nextNetworkStaticRead = DateTime.MinValue;
        _nextProcessRead = DateTime.MinValue;
        _nextDeveloperRead = DateTime.MinValue;
        _nextSecurityRead = DateTime.MinValue;
        _nextUsbRead = DateTime.MinValue;
        _nextDiskAdvancedRead = DateTime.MinValue;
        _nextNvidiaRead = DateTime.MinValue;
        _nextHardwareRead = DateTime.MinValue;
    }

    private static bool IsAdvancedDynamicDiskKey(string key)
    {
        return Regex.IsMatch(key, @"^disk\.[a-z]\.(read_bps|write_bps|io_bps|active_percent|queue_length|health|temperature|power_on_hours|trim_status|smart_status|partition_count)$", RegexOptions.IgnoreCase);
    }

    private void AddBatteryAdvanced(IDictionary<string, object?> v)
    {
        double remainingMwh = Number(v, "battery.remaining_mwh");
        double fullMwh = Number(v, "battery.full_mwh");
        double chargeW = Number(v, "battery.charge_rate_watts");
        double health = Number(v, "battery.health_percent");
        double emptySeconds = Number(v, "battery.time_remaining_seconds");

        if (emptySeconds > 0) v["battery.estimated_time_to_empty"] = emptySeconds;
        if (chargeW > 0 && fullMwh > remainingMwh)
            v["battery.estimated_time_to_full"] = (fullMwh - remainingMwh) / (chargeW * 1000d) * 3600d;
        if (health > 0) v["battery.design_vs_current_health"] = health;

        // BatteryTemperature is commonly reported as tenths of Kelvin in root\\WMI.
        try
        {
            using var s = new ManagementObjectSearcher("root\\WMI", "SELECT Temperature FROM BatteryTemperature");
            foreach (ManagementObject mo in s.Get())
            {
                double raw = SafeDouble(mo["Temperature"]);
                if (raw > 0)
                {
                    double c = raw > 1000 ? raw / 10d - 273.15d : raw;
                    if (c is > -50 and < 150) v["battery.temperature"] = c;
                }
                break;
            }
        }
        catch { }

        try
        {
            using var s = new ManagementObjectSearcher("SELECT Chemistry FROM Win32_Battery");
            foreach (ManagementObject mo in s.Get())
            {
                int chemistry = SafeInt(mo["Chemistry"]);
                if (chemistry > 0) v["battery.chemistry"] = BatteryChemistryName(chemistry);
                break;
            }
        }
        catch { }
    }

    private void AddCpuAdvanced(IDictionary<string, object?> v)
    {
        double usage = Number(v, "cpu.usage");
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            _cpuSamples.Enqueue((now, usage));
            while (_cpuSamples.Count > 0 && (now - _cpuSamples.Peek().At).TotalMinutes > 15.5)
                _cpuSamples.Dequeue();
            if (_cpuSamples.Count > 0)
            {
                v["cpu.usage_avg_1m"] = AverageSince(_cpuSamples, now.AddMinutes(-1));
                v["cpu.usage_avg_5m"] = AverageSince(_cpuSamples, now.AddMinutes(-5));
                v["cpu.usage_avg_15m"] = AverageSince(_cpuSamples, now.AddMinutes(-15));
                v["cpu.usage_max"] = _cpuSamples.Max(x => x.Usage);
            }
        }

        EnsurePerfOs();
        PutIfNumber(v, "cpu.context_switches", _perfOs.ContextSwitchesPerSec);
        PutIfNumber(v, "cpu.system_calls", _perfOs.SystemCallsPerSec);
        PutIfNumber(v, "cpu.interrupts", _perfOs.InterruptsPerSec);
        PutIfNumber(v, "cpu.dpc_time", _perfOs.DpcTimePercent);
        PutIfNumber(v, "cpu.instructions_per_second", _perfOs.InstructionsRetiredPerSec);

        EnsureHardware();
        PutIfNumber(v, "cpu.temperature_max", _hardware.CpuTemperatureMax);
        PutIfNumber(v, "cpu.core.temperature_avg", _hardware.CpuCoreTemperatureAvg);
        PutIfNumber(v, "cpu.power_max", _hardware.CpuPowerMax);
        PutIfNumber(v, "cpu.core.voltage", _hardware.CpuVoltage);
        PutIfNumber(v, "cpu.bus_speed", _hardware.CpuBusClockMhz);
    }

    private void AddMemoryAdvanced(IDictionary<string, object?> v)
    {
        EnsurePerfOs();
        PutIfNumber(v, "memory.standby_bytes", _perfOs.StandbyBytes);
        PutIfNumber(v, "memory.modified_bytes", _perfOs.ModifiedBytes);

        EnsureMemoryStatic();
        PutIfNumber(v, "memory.hardware_reserved_bytes", _memoryStatic.HardwareReservedBytes);
        PutIfNumber(v, "memory.speed_mhz", _memoryStatic.SpeedMhz);
        if (_memoryStatic.SlotCount > 0) v["memory.slot_count"] = _memoryStatic.SlotCount;
        if (_memoryStatic.SlotUsed > 0) v["memory.slot_used"] = _memoryStatic.SlotUsed;
        if (!string.IsNullOrWhiteSpace(_memoryStatic.FormFactor)) v["memory.form_factor"] = _memoryStatic.FormFactor;
        if (!string.IsNullOrWhiteSpace(_memoryStatic.MemoryType)) v["memory.type"] = _memoryStatic.MemoryType;
        // Timing variables are intentionally not exposed: Windows has no reliable standard SPD timing API.
    }

    private void AddGpuAdvanced(IDictionary<string, object?> v)
    {
        EnsureHardware();
        string selectedName = Text(v, "gpu.name");
        var g = _hardware.Gpus.FirstOrDefault(x => SimilarName(x.Name, selectedName))
                ?? (_hardware.Gpus.Count == 1 ? _hardware.Gpus[0] : null);
        if (g is not null)
        {
            PutIfNumber(v, "gpu.temperature", g.CoreTempC);
            PutIfNumber(v, "gpu.hotspot_temperature", g.HotspotTempC);
            PutIfNumber(v, "gpu.memory_junction_temperature", g.MemoryTempC);
            PutIfNumber(v, "gpu.voltage_v", g.VoltageV);
            PutIfNumber(v, "gpu.core_clock_mhz", g.CoreClockMhz);
            PutIfNumber(v, "gpu.memory_clock_mhz", g.MemoryClockMhz);
            PutIfNumber(v, "gpu.power_w", g.PowerW);
        }

        // Driver metadata from Win32_VideoController.
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController");
            ManagementObject? best = null;
            foreach (ManagementObject mo in s.Get())
            {
                string name = Convert.ToString(mo["Name"])?.Trim() ?? "";
                if (SimilarName(name, selectedName)) { best = mo; break; }
                best ??= mo;
            }
            if (best is not null)
            {
                string driver = Convert.ToString(best["DriverVersion"])?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(driver)) v["gpu.driver_version"] = driver;
                string rawDate = Convert.ToString(best["DriverDate"])?.Trim() ?? "";
                if (TryWmiDate(rawDate, out var dt)) v["gpu.driver_date"] = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }
        catch { }

        bool nvidiaSmi = CommandExists("nvidia-smi.exe");
        v["gpu.nvidia_smi_available"] = nvidiaSmi;
        v["gpu.amd_adrenalin_available"] = HasProcess("RadeonSoftware")
                                              || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AMD", "CNext", "CNext", "RadeonSoftware.exe"));
        if (nvidiaSmi)
        {
            EnsureNvidia();
            var n = _nvidia.FirstOrDefault(x => SimilarName(x.Name, selectedName))
                    ?? (_nvidia.Count == 1 ? _nvidia[0] : null);
            if (n is not null)
            {
                if (!string.IsNullOrWhiteSpace(n.BiosVersion)) v["gpu.bios_version"] = n.BiosVersion;
                PutIfNumber(v, "gpu.power_limit_w", n.PowerLimitW);
                PutIfNumber(v, "gpu.core_clock_mhz", n.CoreClockMhz);
                PutIfNumber(v, "gpu.memory_clock_mhz", n.MemoryClockMhz);
                PutIfNumber(v, "gpu.pcie_gen", n.PcieGen);
                PutIfNumber(v, "gpu.pcie_lanes", n.PcieWidth);
            }
        }
    }

    private void AddNetworkAdvanced(IDictionary<string, object?> v)
    {
        EnsureNetworkStatic();
        v["network.vpn_status"] = _networkStatic.VpnActive;
        if (!string.IsNullOrWhiteSpace(_networkStatic.VpnName)) v["network.vpn_name"] = _networkStatic.VpnName;
        v["network.proxy_status"] = _networkStatic.ProxyEnabled;
        if (!string.IsNullOrWhiteSpace(_networkStatic.ProxyAddress)) v["network.proxy_address"] = _networkStatic.ProxyAddress;
        v["network.tcp_connections"] = _networkStatic.TcpConnections;
        v["network.udp_connections"] = _networkStatic.UdpListeners;
        PutIfNumber(v, "network.signal_dbm", _networkStatic.WifiSignalDbm);
        PutIfNumber(v, "network.wifi_channel", _networkStatic.WifiChannel);
        if (!string.IsNullOrWhiteSpace(_networkStatic.WifiBand)) v["network.wifi_band"] = _networkStatic.WifiBand;
        if (!string.IsNullOrWhiteSpace(_networkStatic.WifiStandard)) v["network.wifi_standard"] = _networkStatic.WifiStandard;
        PutIfNumber(v, "network.dns_latency_ms", _networkStatic.DnsLatencyMs);
    }

    private void AddDiskAdvanced(IDictionary<string, object?> v, HashSet<string>? requested)
    {
        // Per-volume I/O counters are native Windows perf data and are refreshed every ~1s.
        try
        {
            using var perf = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name, DiskReadBytesPersec, DiskWriteBytesPersec, PercentDiskTime, CurrentDiskQueueLength FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk");
            foreach (ManagementObject mo in perf.Get())
            {
                string name = Convert.ToString(mo["Name"])?.Trim() ?? "";
                if (!Regex.IsMatch(name, "^[A-Za-z]:$")) continue;
                string letter = name[..1].ToLowerInvariant();
                v[$"disk.{letter}.read_bps"] = SafeDouble(mo["DiskReadBytesPersec"]);
                v[$"disk.{letter}.write_bps"] = SafeDouble(mo["DiskWriteBytesPersec"]);
                v[$"disk.{letter}.io_bps"] = SafeDouble(mo["DiskReadBytesPersec"]) + SafeDouble(mo["DiskWriteBytesPersec"]);
                v[$"disk.{letter}.active_percent"] = Math.Clamp(SafeDouble(mo["PercentDiskTime"]), 0d, 100d);
                v[$"disk.{letter}.queue_length"] = SafeDouble(mo["CurrentDiskQueueLength"]);
            }
        }
        catch { }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            string letter = drive.Name.TrimEnd('\\', '/').TrimEnd(':').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(letter)) continue;
            try
            {
                string id = drive.Name.TrimEnd('\\');
                using var s = new ManagementObjectSearcher($"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{id}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                int partitions = 0;
                foreach (ManagementObject _ in s.Get()) partitions++;
                v[$"disk.{letter}.partition_count"] = partitions;
            }
            catch { }
        }

        EnsureDiskAdvanced();
        foreach (var pair in _diskAdvanced)
        {
            string p = $"disk.{char.ToLowerInvariant(pair.Key)}.";
            var d = pair.Value;
            if (!string.IsNullOrWhiteSpace(d.Health)) v[p + "health"] = d.Health;
            PutIfNumber(v, p + "temperature", d.TemperatureC);
            PutIfNumber(v, p + "power_on_hours", d.PowerOnHours);
            if (!string.IsNullOrWhiteSpace(d.TrimStatus)) v[p + "trim_status"] = d.TrimStatus;
            if (!string.IsNullOrWhiteSpace(d.SmartStatus)) v[p + "smart_status"] = d.SmartStatus;
        }
        // total_written_bytes is intentionally omitted: MSFT_StorageReliabilityCounter has no standard host-bytes-written field.
    }

    private void AddSystemAdvanced(IDictionary<string, object?> v)
    {
        EnsureSystemStatic();
        if (!string.IsNullOrWhiteSpace(_systemStatic.PowerPlanGuid)) v["system.power_plan"] = _systemStatic.PowerPlanGuid;
        if (!string.IsNullOrWhiteSpace(_systemStatic.PowerPlanName)) v["system.power_plan_name"] = _systemStatic.PowerPlanName;
        if (!string.IsNullOrWhiteSpace(_systemStatic.BiosVersion)) v["system.bios_version"] = _systemStatic.BiosVersion;
        if (!string.IsNullOrWhiteSpace(_systemStatic.BiosDate)) v["system.bios_date"] = _systemStatic.BiosDate;
        if (!string.IsNullOrWhiteSpace(_systemStatic.BoardManufacturer)) v["system.motherboard_manufacturer"] = _systemStatic.BoardManufacturer;
        if (!string.IsNullOrWhiteSpace(_systemStatic.BoardModel)) v["system.motherboard_model"] = _systemStatic.BoardModel;
        v["system.update_pending"] = _systemStatic.UpdatePending;
        if (!string.IsNullOrWhiteSpace(_systemStatic.UpdateLastInstalled)) v["system.update_last_installed"] = _systemStatic.UpdateLastInstalled;
        v["system.hyper_v_status"] = _systemStatic.HyperVEnabled;
        v["system.wsl_status"] = _systemStatic.WslInstalled;
        v["system.wsl_distro_count"] = _systemStatic.WslDistroCount;
        if (!string.IsNullOrWhiteSpace(_systemStatic.DefenderStatus)) v["system.defender_status"] = _systemStatic.DefenderStatus;
        if (!string.IsNullOrWhiteSpace(_systemStatic.FirewallStatus)) v["system.firewall_status"] = _systemStatic.FirewallStatus;
        if (!string.IsNullOrWhiteSpace(_systemStatic.BitLockerStatus)) v["system.bitlocker_status"] = _systemStatic.BitLockerStatus;

        EnsureHardware();
        PutIfNumber(v, "system.motherboard_temperature", _hardware.MotherboardTemperatureMax);
        PutIfNumber(v, "system.fan_speed", _hardware.FanRpmMax);
        PutIfNumber(v, "system.fan_speed_percent", _hardware.FanPercentMax);
    }

    private void AddProcessAdvanced(IDictionary<string, object?> v)
    {
        EnsureProcesses();
        v["process.background.count"] = _process.BackgroundCount;
        if (!string.IsNullOrWhiteSpace(_process.TopCpuName))
        {
            v["process.top_cpu.name"] = _process.TopCpuName;
            v["process.top_cpu.pid"] = _process.TopCpuPid;
            v["process.top_cpu.usage"] = _process.TopCpuUsage;
        }
        if (!string.IsNullOrWhiteSpace(_process.TopMemoryName))
        {
            v["process.top_memory.name"] = _process.TopMemoryName;
            v["process.top_memory.pid"] = _process.TopMemoryPid;
            v["process.top_memory.usage"] = _process.TopMemoryBytes;
        }
        if (!string.IsNullOrWhiteSpace(_process.TopDiskName))
        {
            v["process.top_disk.name"] = _process.TopDiskName;
            v["process.top_disk.pid"] = _process.TopDiskPid;
            v["process.top_disk.usage"] = _process.TopDiskBps;
        }
        if (!string.IsNullOrWhiteSpace(_process.TopGpuName))
        {
            v["process.gpu.top.name"] = _process.TopGpuName;
            v["process.gpu.top.usage"] = _process.TopGpuUsage;
        }
        // Per-process network attribution requires ETW/WFP capture and is not exposed as a cheap standard counter.
    }

    private void AddAppAdvanced(IDictionary<string, object?> v, CustomHudSettings settings)
    {
        v["app.theme"] = LocalizationManager.Text("深色", "Dark");
        v["app.active_profile"] = settings.ActiveProfileId;
        var p = settings.Profiles.FirstOrDefault(x => string.Equals(x.Id, settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        if (p is not null)
            v["app.preset_name"] = BuiltInProfileLocalization.DisplayName(p);

        try
        {
            int pid = Environment.ProcessId;
            double gpu = 0;
            using var s = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            foreach (ManagementObject mo in s.Get())
            {
                string name = Convert.ToString(mo["Name"]) ?? "";
                if (Regex.IsMatch(name, $@"(?:^|_)pid_{pid}(?:_|$)", RegexOptions.IgnoreCase))
                    gpu += SafeDouble(mo["UtilizationPercentage"]);
            }
            v["app.gpu_usage"] = Math.Clamp(gpu, 0d, 100d);
        }
        catch { }
    }

    private static void AddDisplayAdvanced(IDictionary<string, object?> v)
    {
        try
        {
            var devices = EnumerateDisplays();
            if (devices.Count > 0)
            {
                var primary = devices[0];
                v["display.primary.color_depth"] = primary.BitsPerPixel;
                v["display.primary.refresh_rate"] = primary.RefreshRate;
                v["display.primary.name"] = primary.Name;
            }
            if (devices.Count > 1)
            {
                var second = devices[1];
                v["display.secondary.name"] = second.Name;
                v["display.secondary.resolution"] = $"{second.Width}×{second.Height}";
                v["display.secondary.refresh_rate"] = second.RefreshRate;
            }
        }
        catch { }
        // HDR/color-space/night-light are intentionally not exposed here: they require DisplayConfig/CloudStore APIs
        // and would otherwise be unreliable placeholders.
    }

    private static void AddWorldTime(IDictionary<string, object?> v)
    {
        PutWorld(v, "time.world.nyc", "Eastern Standard Time");
        PutWorld(v, "time.world.london", "GMT Standard Time");
        PutWorld(v, "time.world.tokyo", "Tokyo Standard Time");
        PutWorld(v, "time.world.beijing", "China Standard Time");
    }

    private void AddClipboard(IDictionary<string, object?> v)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            uint seq = GetClipboardSequenceNumber();
            if (seq != _lastClipboardSequence)
            {
                _lastClipboardSequence = seq;
                _clipboardLastUpdated = DateTime.Now;
            }
            v["clipboard.last_updated"] = _clipboardLastUpdated == DateTime.MinValue ? "" : _clipboardLastUpdated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            v["clipboard.has_text"] = IsClipboardFormatAvailable(CF_UNICODETEXT);
            v["clipboard.has_image"] = IsClipboardFormatAvailable(CF_DIB) || IsClipboardFormatAvailable(CF_BITMAP);
            v["clipboard.file_count"] = IsClipboardFormatAvailable(CF_HDROP) ? GetClipboardFileCount() : 0;

            if (IsClipboardFormatAvailable(CF_UNICODETEXT) && TryReadClipboardText(out var text))
            {
                v["clipboard.text_length"] = text.Length;
                string preview = Regex.Replace(text, @"\s+", " ").Trim();
                v["clipboard.preview"] = preview.Length <= 80 ? preview : preview[..80] + "…";
            }
            else
            {
                v["clipboard.text_length"] = 0;
                v["clipboard.preview"] = "";
            }

            if ((IsClipboardFormatAvailable(CF_DIB) || IsClipboardFormatAvailable(CF_DIBV5)) && TryReadClipboardDibSize(out int w, out int h))
                v["clipboard.image_size"] = $"{w}×{h}";
        }
        catch { }
    }

    private void AddUsbAndPeripherals(IDictionary<string, object?> v)
    {
        EnsureUsb();
        v["usb.device.count"] = _usb.DeviceCount;
        v["usb.device.list"] = _usb.DeviceList;
        v["usb.storage.count"] = _usb.StorageCount;
        v["usb.storage.list"] = _usb.StorageList;
        if (!string.IsNullOrWhiteSpace(_usb.MouseName)) v["peripheral.mouse.name"] = _usb.MouseName;
        if (!string.IsNullOrWhiteSpace(_usb.KeyboardName)) v["peripheral.keyboard.name"] = _usb.KeyboardName;
        v["peripheral.gamepad.count"] = _usb.GamepadCount;
        if (!string.IsNullOrWhiteSpace(_usb.GamepadName)) v["peripheral.gamepad.name"] = _usb.GamepadName;
        if (_usb.GamepadBattery >= 0) v["peripheral.gamepad.battery"] = _usb.GamepadBattery;
        // HID battery/DPI/backlight/headset battery are vendor-specific and intentionally not faked.
    }

    private void AddDeveloper(IDictionary<string, object?> v)
    {
        EnsureDeveloper();
        v["dev.docker.running"] = _developer.DockerRunning;
        if (_developer.DockerContainers >= 0) v["dev.docker.containers"] = _developer.DockerContainers;
        if (_developer.DockerImages >= 0) v["dev.docker.images"] = _developer.DockerImages;
        v["dev.wsl.running"] = _developer.WslRunning;
        if (!string.IsNullOrWhiteSpace(_developer.WslDistro)) v["dev.wsl.distro"] = _developer.WslDistro;
        PutIfNumber(v, "dev.wsl.memory_usage", _developer.WslMemoryBytes);
        if (!string.IsNullOrWhiteSpace(_developer.GitBranch)) v["dev.git.branch"] = _developer.GitBranch;
        if (!string.IsNullOrWhiteSpace(_developer.GitStatus)) v["dev.git.status"] = _developer.GitStatus;
        if (!string.IsNullOrWhiteSpace(_developer.GitLastCommit)) v["dev.git.last_commit"] = _developer.GitLastCommit;
        PutText(v, "dev.node.version", _developer.NodeVersion);
        PutText(v, "dev.python.version", _developer.PythonVersion);
        PutText(v, "dev.java.version", _developer.JavaVersion);
        PutText(v, "dev.golang.version", _developer.GoVersion);
        PutText(v, "dev.rust.version", _developer.RustVersion);
        v["dev.vscode.running"] = _developer.VsCodeRunning;
        v["dev.terminal.running"] = _developer.TerminalRunning;
        v["dev.ide.running"] = _developer.IdeRunning;
        v["dev.llm.local_status"] = _developer.LocalLlmRunning ? LocalizationManager.Text("运行中", "Running") : LocalizationManager.Text("未运行", "Not running");
    }

    private void AddSecurity(IDictionary<string, object?> v)
    {
        EnsureSecurity();
        PutText(v, "security.defender.status", _security.DefenderStatus);
        PutText(v, "security.defender.last_scan", _security.DefenderLastScan);
        if (_security.DefenderThreats >= 0) v["security.defender.threats"] = _security.DefenderThreats;
        PutText(v, "security.firewall.status", _security.FirewallStatus);
        PutText(v, "security.firewall.profile", _security.FirewallProfile);
        PutText(v, "security.bitlocker.status", _security.BitLockerStatus);
        PutIfNumber(v, "security.bitlocker.encryption_percent", _security.BitLockerPercent);
        v["security.secure_boot"] = _security.SecureBoot;
        v["security.tpm.present"] = _security.TpmPresent;
        PutText(v, "security.tpm.version", _security.TpmVersion);
        v["security.uac_status"] = _security.UacEnabled;
        PutText(v, "security.smartscreen_status", _security.SmartScreenStatus);
        PutText(v, "security.windows_update.status", _security.WindowsUpdateStatus);
        if (_security.WindowsUpdatePendingCount >= 0) v["security.windows_update.pending_count"] = _security.WindowsUpdatePendingCount;
        v["security.vpn.active"] = _networkStatic.VpnActive;
        v["security.proxy.enabled"] = _networkStatic.ProxyEnabled;
    }

    private async Task AddPublicIpAsync(IDictionary<string, object?> v, bool ipv4, bool ipv6, CancellationToken ct)
    {
        if (ipv4 && DateTime.UtcNow >= _nextPublicIpv4Read)
        {
            _nextPublicIpv4Read = DateTime.UtcNow.AddMinutes(5);
            try { _publicIpv4 = (await FastHttp.GetStringAsync("https://api.ipify.org", ct).ConfigureAwait(false)).Trim(); } catch { }
        }
        if (ipv6 && DateTime.UtcNow >= _nextPublicIpv6Read)
        {
            _nextPublicIpv6Read = DateTime.UtcNow.AddMinutes(5);
            try { _publicIpv6 = (await FastHttp.GetStringAsync("https://api6.ipify.org", ct).ConfigureAwait(false)).Trim(); } catch { }
        }
        if (ipv4 && IPAddress.TryParse(_publicIpv4, out var ip4) && ip4.AddressFamily == AddressFamily.InterNetwork) v["network.public_ipv4"] = _publicIpv4;
        if (ipv6 && IPAddress.TryParse(_publicIpv6, out var ip6) && ip6.AddressFamily == AddressFamily.InterNetworkV6) v["network.public_ipv6"] = _publicIpv6;
    }

    private void EnsurePerfOs()
    {
        if (DateTime.UtcNow < _nextPerfOsRead) return;
        _nextPerfOsRead = DateTime.UtcNow.AddMilliseconds(900);
        var next = new PerfOsSnapshot();
        try
        {
            using var sys = new ManagementObjectSearcher("root\\CIMV2", "SELECT ContextSwitchesPersec, SystemCallsPersec FROM Win32_PerfFormattedData_PerfOS_System");
            foreach (ManagementObject mo in sys.Get())
            {
                next.ContextSwitchesPerSec = NullableDouble(mo, "ContextSwitchesPersec");
                next.SystemCallsPerSec = NullableDouble(mo, "SystemCallsPersec");
                break;
            }
        }
        catch { }
        try
        {
            using var cpu = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
            foreach (ManagementObject mo in cpu.Get())
            {
                next.InterruptsPerSec = NullableDouble(mo, "InterruptsPersec");
                next.DpcTimePercent = NullableDouble(mo, "PercentDPCTime");
                next.InstructionsRetiredPerSec = NullableDouble(mo, "InstructionsRetiredPersec");
                break;
            }
        }
        catch { }
        try
        {
            using var mem = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_PerfFormattedData_PerfOS_Memory");
            foreach (ManagementObject mo in mem.Get())
            {
                double standby = 0;
                foreach (string p in new[] { "StandbyCacheCoreBytes", "StandbyCacheNormalPriorityBytes", "StandbyCacheReserveBytes" })
                    standby += NullableDouble(mo, p) ?? 0;
                next.StandbyBytes = standby > 0 ? standby : null;
                next.ModifiedBytes = NullableDouble(mo, "ModifiedPageListBytes");
                break;
            }
        }
        catch { }
        _perfOs = next;
    }

    private void EnsureMemoryStatic()
    {
        if (DateTime.UtcNow < _nextMemoryStaticRead) return;
        _nextMemoryStaticRead = DateTime.UtcNow.AddMinutes(10);
        var s = new MemoryStaticSnapshot();
        try
        {
            ulong installed = 0;
            var speeds = new List<double>();
            var forms = new List<string>();
            var types = new List<string>();
            int used = 0;
            using var mem = new ManagementObjectSearcher("SELECT Capacity, Speed, ConfiguredClockSpeed, FormFactor, SMBIOSMemoryType, MemoryType FROM Win32_PhysicalMemory");
            foreach (ManagementObject mo in mem.Get())
            {
                used++;
                try { installed += Convert.ToUInt64(mo["Capacity"] ?? 0, CultureInfo.InvariantCulture); } catch { }
                double mhz = Math.Max(SafeDouble(mo["ConfiguredClockSpeed"]), SafeDouble(mo["Speed"]));
                if (mhz > 0) speeds.Add(mhz);
                string f = MemoryFormFactorName(SafeInt(mo["FormFactor"])); if (!string.IsNullOrWhiteSpace(f)) forms.Add(f);
                int smbios = SafeInt(mo["SMBIOSMemoryType"]); int legacy = SafeInt(mo["MemoryType"]);
                string t = MemoryTypeName(smbios > 0 ? smbios : legacy); if (!string.IsNullOrWhiteSpace(t)) types.Add(t);
            }
            s.SlotUsed = used;
            s.SpeedMhz = speeds.Count > 0 ? speeds.Max() : null;
            s.FormFactor = string.Join(" / ", forms.Distinct());
            s.MemoryType = string.Join(" / ", types.Distinct());
            try
            {
                using var arr = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
                foreach (ManagementObject mo in arr.Get()) s.SlotCount += SafeInt(mo["MemoryDevices"]);
            }
            catch { }
            var ms = new MEMORYSTATUSEX_ADV { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX_ADV>() };
            if (GlobalMemoryStatusEx(ref ms) && installed > 0)
                s.HardwareReservedBytes = Math.Max(0d, installed - (double)ms.ullTotalPhys);
        }
        catch { }
        _memoryStatic = s;
    }

    private void EnsureHardware()
    {
        if (DateTime.UtcNow < _nextHardwareRead) return;
        _nextHardwareRead = DateTime.UtcNow.AddMilliseconds(900);
        var result = new HardwareSnapshot();
        try
        {
            lock (_gate)
            {
                _computer ??= new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsStorageEnabled = true,
                    IsMemoryEnabled = true
                };
                try { _computer.Open(); } catch { }
                foreach (var hw in _computer.Hardware)
                    ReadHardware(hw, result);
            }
        }
        catch { }
        _hardware = result;
    }

    private static void ReadHardware(IHardware hw, HardwareSnapshot output)
    {
        try { hw.Update(); } catch { }
        string type = hw.HardwareType.ToString();
        var sensors = hw.Sensors.ToList();
        if (type.Equals("Cpu", StringComparison.OrdinalIgnoreCase))
        {
            var temps = sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value.HasValue).ToList();
            var coreTemps = temps.Where(x => x.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)).Select(x => (double)x.Value!.Value).ToList();
            output.CpuTemperatureMax = temps.Count > 0 ? temps.Max(x => (double)x.Value!.Value) : null;
            output.CpuCoreTemperatureAvg = coreTemps.Count > 0 ? coreTemps.Average() : output.CpuTemperatureMax;
            output.CpuPowerMax = MaxSensor(sensors, SensorType.Power);
            output.CpuVoltage = FirstSensor(sensors, SensorType.Voltage, "Core") ?? MaxSensor(sensors, SensorType.Voltage);
            output.CpuBusClockMhz = FirstSensor(sensors, SensorType.Clock, "Bus") ?? FirstSensor(sensors, SensorType.Clock, "BCLK");
        }
        else if (type.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase))
        {
            var g = new GpuHardwareSnapshot { Name = hw.Name };
            g.CoreTempC = FirstSensor(sensors, SensorType.Temperature, "Core");
            g.HotspotTempC = FirstSensor(sensors, SensorType.Temperature, "Hot Spot") ?? FirstSensor(sensors, SensorType.Temperature, "Hotspot");
            g.MemoryTempC = FirstSensor(sensors, SensorType.Temperature, "Memory Junction") ?? FirstSensor(sensors, SensorType.Temperature, "Memory");
            g.VoltageV = FirstSensor(sensors, SensorType.Voltage, "Core") ?? MaxSensor(sensors, SensorType.Voltage);
            g.CoreClockMhz = FirstSensor(sensors, SensorType.Clock, "Core");
            g.MemoryClockMhz = FirstSensor(sensors, SensorType.Clock, "Memory");
            g.PowerW = FirstSensor(sensors, SensorType.Power, "Total") ?? FirstSensor(sensors, SensorType.Power, "Package") ?? MaxSensor(sensors, SensorType.Power);
            output.Gpus.Add(g);
        }
        else if (type.Equals("Motherboard", StringComparison.OrdinalIgnoreCase))
        {
            output.MotherboardTemperatureMax = MaxSensor(sensors, SensorType.Temperature);
            output.FanRpmMax = MaxSensor(sensors, SensorType.Fan);
            output.FanPercentMax = MaxSensor(sensors, SensorType.Control);
        }
        foreach (var sub in hw.SubHardware) ReadHardware(sub, output);
    }

    private void EnsureNvidia()
    {
        if (DateTime.UtcNow < _nextNvidiaRead) return;
        _nextNvidiaRead = DateTime.UtcNow.AddSeconds(5);
        var list = new List<NvidiaSnapshot>();
        string? text = Run("nvidia-smi.exe", "--query-gpu=name,vbios_version,power.limit,clocks.gr,clocks.mem,pcie.link.gen.current,pcie.link.width.current --format=csv,noheader,nounits", 2500);
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = line.Split(',').Select(x => x.Trim()).ToArray();
                if (p.Length < 7) continue;
                list.Add(new NvidiaSnapshot
                {
                    Name = p[0], BiosVersion = p[1], PowerLimitW = ParseNullable(p[2]), CoreClockMhz = ParseNullable(p[3]),
                    MemoryClockMhz = ParseNullable(p[4]), PcieGen = ParseNullable(p[5]), PcieWidth = ParseNullable(p[6])
                });
            }
        }
        _nvidia = list;
    }

    private void EnsureNetworkStatic()
    {
        if (DateTime.UtcNow < _nextNetworkStaticRead) return;
        _nextNetworkStaticRead = DateTime.UtcNow.AddSeconds(3);
        var s = new NetworkStaticSnapshot();
        try
        {
            var vpn = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
                n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && (n.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel
                    || Regex.IsMatch(n.Name + " " + n.Description, @"VPN|WireGuard|Tailscale|ZeroTier|OpenVPN|Wintun|IKEv2", RegexOptions.IgnoreCase)));
            s.VpnActive = vpn is not null;
            s.VpnName = vpn?.Name ?? "";
            var p = IPGlobalProperties.GetIPGlobalProperties();
            s.TcpConnections = p.GetActiveTcpConnections().Length;
            s.UdpListeners = p.GetActiveUdpListeners().Length;
        }
        catch { }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            s.ProxyEnabled = Convert.ToInt32(key?.GetValue("ProxyEnable") ?? 0, CultureInfo.InvariantCulture) != 0;
            s.ProxyAddress = Convert.ToString(key?.GetValue("ProxyServer")) ?? "";
        }
        catch { }
        try
        {
            var sw = Stopwatch.StartNew();
            Dns.GetHostAddresses("one.one.one.one");
            sw.Stop();
            s.DnsLatencyMs = sw.Elapsed.TotalMilliseconds;
        }
        catch { }
        ParseNetshWifi(s);
        _networkStatic = s;
    }

    private static void ParseNetshWifi(NetworkStaticSnapshot s)
    {
        string? output = Run("netsh.exe", "wlan show interfaces", 1800);
        if (string.IsNullOrWhiteSpace(output)) return;
        foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            int idx = line.IndexOf(':');
            if (idx <= 0) continue;
            string key = line[..idx].Trim();
            string val = line[(idx + 1)..].Trim();
            if (Regex.IsMatch(key, @"^(Signal|信号)$", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(val, @"(?<p>\d+)");
                if (m.Success && double.TryParse(m.Groups["p"].Value, out double quality))
                    s.WifiSignalDbm = Math.Clamp(quality / 2d - 100d, -100d, -50d); // documented WLAN quality approximation
            }
            else if (Regex.IsMatch(key, @"^(Channel|信道)$", RegexOptions.IgnoreCase) && double.TryParse(Regex.Match(val, @"\d+").Value, out double ch))
                s.WifiChannel = ch;
            else if (Regex.IsMatch(key, @"^(Band|波段)$", RegexOptions.IgnoreCase)) s.WifiBand = val;
            else if (Regex.IsMatch(key, @"^(Radio type|无线电类型)$", RegexOptions.IgnoreCase)) s.WifiStandard = val;
        }
        if (string.IsNullOrWhiteSpace(s.WifiBand) && s.WifiChannel.HasValue)
            s.WifiBand = s.WifiChannel <= 14 ? "2.4 GHz" : "5/6 GHz";
    }

    private void EnsureDiskAdvanced()
    {
        if (DateTime.UtcNow < _nextDiskAdvancedRead) return;
        _nextDiskAdvancedRead = DateTime.UtcNow.AddMinutes(1);
        var map = new Dictionary<char, DiskAdvancedSnapshot>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            char letter = char.ToUpperInvariant(drive.Name[0]);
            var d = new DiskAdvancedSnapshot();
            try
            {
                string? json = RunPowerShell($"$p=Get-Partition -DriveLetter '{letter}' -ErrorAction Stop | Select-Object -First 1; $disk=$p | Get-Disk; $r=$disk | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue; [pscustomobject]@{{Health=[string]$disk.HealthStatus;Operational=([string]($disk.OperationalStatus -join ','));Temperature=$r.Temperature;PowerOnHours=$r.PowerOnHours}} | ConvertTo-Json -Compress", 3500);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    d.Health = JsonText(root, "Health");
                    d.SmartStatus = JsonText(root, "Operational");
                    d.TemperatureC = JsonNumber(root, "Temperature");
                    d.PowerOnHours = JsonNumber(root, "PowerOnHours");
                }
            }
            catch { }
            try
            {
                string fs = drive.DriveFormat;
                string? trim = Run("fsutil.exe", "behavior query DisableDeleteNotify", 1800);
                if (!string.IsNullOrWhiteSpace(trim))
                {
                    string key = fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase) ? "ReFS DisableDeleteNotify" : "NTFS DisableDeleteNotify";
                    var m = Regex.Match(trim, Regex.Escape(key) + @"\s*=\s*(?<v>[01])", RegexOptions.IgnoreCase);
                    if (m.Success) d.TrimStatus = m.Groups["v"].Value == "0" ? LocalizationManager.Text("启用", "Enabled") : LocalizationManager.Text("禁用", "Disabled");
                }
            }
            catch { }
            map[letter] = d;
        }
        _diskAdvanced = map;
    }

    private void EnsureSystemStatic()
    {
        if (DateTime.UtcNow < _nextSystemStaticRead) return;
        _nextSystemStaticRead = DateTime.UtcNow.AddMinutes(2);
        var s = new SystemStaticSnapshot();
        try
        {
            string? power = Run("powercfg.exe", "/GETACTIVESCHEME", 1800);
            if (!string.IsNullOrWhiteSpace(power))
            {
                var g = Regex.Match(power, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                if (g.Success) s.PowerPlanGuid = g.Value;
                var n = Regex.Match(power, @"\((?<n>[^\r\n()]+)\)\s*$", RegexOptions.Multiline);
                if (n.Success) s.PowerPlanName = n.Groups["n"].Value.Trim();
            }
        }
        catch { }
        try
        {
            using var bios = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            foreach (ManagementObject mo in bios.Get())
            {
                s.BiosVersion = Convert.ToString(mo["SMBIOSBIOSVersion"])?.Trim() ?? "";
                if (TryWmiDate(Convert.ToString(mo["ReleaseDate"]), out var dt)) s.BiosDate = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            }
            using var board = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (ManagementObject mo in board.Get())
            {
                s.BoardManufacturer = Convert.ToString(mo["Manufacturer"])?.Trim() ?? "";
                s.BoardModel = Convert.ToString(mo["Product"])?.Trim() ?? "";
                break;
            }
        }
        catch { }
        try
        {
            using var hyper = new ManagementObjectSearcher("SELECT Name, InstallState FROM Win32_OptionalFeature WHERE Name='Microsoft-Hyper-V-All'");
            foreach (ManagementObject mo in hyper.Get()) { s.HyperVEnabled = SafeInt(mo["InstallState"]) == 1; break; }
        }
        catch { }
        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            s.WslDistroCount = lxss?.GetSubKeyNames().Length ?? 0;
            s.WslInstalled = s.WslDistroCount > 0;
            using var wslFeature = new ManagementObjectSearcher("SELECT InstallState FROM Win32_OptionalFeature WHERE Name='Microsoft-Windows-Subsystem-Linux'");
            foreach (ManagementObject mo in wslFeature.Get())
            {
                if (SafeInt(mo["InstallState"]) == 1) s.WslInstalled = true;
                break;
            }
        }
        catch { }
        s.UpdatePending = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null
                          || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null;
        try
        {
            using var hotfix = new ManagementObjectSearcher("SELECT InstalledOn FROM Win32_QuickFixEngineering");
            DateTime latest = DateTime.MinValue;
            foreach (ManagementObject mo in hotfix.Get())
            {
                string text = Convert.ToString(mo["InstalledOn"]) ?? "";
                if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt) && dt > latest) latest = dt;
            }
            if (latest != DateTime.MinValue) s.UpdateLastInstalled = latest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch { }
        var sec = ReadSecuritySnapshot();
        s.DefenderStatus = sec.DefenderStatus;
        s.FirewallStatus = sec.FirewallStatus;
        s.BitLockerStatus = sec.BitLockerStatus;
        if (sec.WindowsUpdatePendingCount > 0) s.UpdatePending = true;
        _systemStatic = s;
    }

    private void EnsureProcesses()
    {
        if (DateTime.UtcNow < _nextProcessRead) return;
        var now = DateTime.UtcNow;
        double elapsed = _nextProcessRead == DateTime.MinValue ? 1d : 1d;
        _nextProcessRead = now.AddSeconds(1);
        var result = new ProcessSnapshot();
        var nextCpu = new Dictionary<int, ProcCpuSample>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    bool background = p.MainWindowHandle == IntPtr.Zero;
                    if (background) result.BackgroundCount++;
                    long ws = p.WorkingSet64;
                    if (ws > result.TopMemoryBytes) { result.TopMemoryBytes = ws; result.TopMemoryName = p.ProcessName; result.TopMemoryPid = p.Id; }
                    var sample = new ProcCpuSample(p.TotalProcessorTime, now);
                    if (_processCpu.TryGetValue(p.Id, out var old))
                    {
                        double wall = Math.Max(0.05, (now - old.At).TotalSeconds);
                        double cpu = Math.Max(0d, (sample.Cpu - old.Cpu).TotalSeconds / wall / Math.Max(1, Environment.ProcessorCount) * 100d);
                        if (cpu > result.TopCpuUsage) { result.TopCpuUsage = cpu; result.TopCpuName = p.ProcessName; result.TopCpuPid = p.Id; }
                    }
                    nextCpu[p.Id] = sample;
                }
                catch { }
                finally { p.Dispose(); }
            }
            _processCpu.Clear(); foreach (var pair in nextCpu) _processCpu[pair.Key] = pair.Value;
        }
        catch { }
        try
        {
            using var s = new ManagementObjectSearcher("root\\CIMV2", "SELECT IDProcess, Name, IODataBytesPersec FROM Win32_PerfFormattedData_PerfProc_Process");
            foreach (ManagementObject mo in s.Get())
            {
                int pid = SafeInt(mo["IDProcess"]); double io = SafeDouble(mo["IODataBytesPersec"]); string name = Convert.ToString(mo["Name"]) ?? "";
                if (pid > 0 && io > result.TopDiskBps) { result.TopDiskBps = io; result.TopDiskName = name; result.TopDiskPid = pid; }
            }
        }
        catch { }
        try
        {
            var byPid = new Dictionary<int, double>();
            using var s = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            foreach (ManagementObject mo in s.Get())
            {
                string name = Convert.ToString(mo["Name"]) ?? "";
                var m = Regex.Match(name, @"(?:^|_)pid_(?<p>\d+)(?:_|$)");
                if (!m.Success || !int.TryParse(m.Groups["p"].Value, out int pid)) continue;
                byPid[pid] = Math.Min(100d, (byPid.TryGetValue(pid, out var old) ? old : 0d) + SafeDouble(mo["UtilizationPercentage"]));
            }
            foreach (var pair in byPid.OrderByDescending(x => x.Value).Take(1))
            {
                result.TopGpuUsage = pair.Value;
                try { using var p = Process.GetProcessById(pair.Key); result.TopGpuName = p.ProcessName; } catch { result.TopGpuName = pair.Key.ToString(CultureInfo.InvariantCulture); }
            }
        }
        catch { }
        _process = result;
    }

    private void EnsureDeveloper()
    {
        if (DateTime.UtcNow < _nextDeveloperRead) return;
        _nextDeveloperRead = DateTime.UtcNow.AddSeconds(15);
        var s = new DeveloperSnapshot();
        s.DockerRunning = HasProcess("Docker Desktop", "com.docker.backend", "dockerd");
        if (s.DockerRunning && CommandExists("docker.exe"))
        {
            s.DockerContainers = CountLines(Run("docker.exe", "ps -q", 1800));
            s.DockerImages = CountDistinctLines(Run("docker.exe", "images -q", 1800));
        }
        s.WslRunning = HasProcess("wsl", "wslhost", "vmmemWSL", "vmmem");
        if (CommandExists("wsl.exe"))
        {
            var distros = Run("wsl.exe", "-l -q", 1800)?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
            s.WslDistro = string.Join(", ", distros);
        }
        try { s.WslMemoryBytes = Process.GetProcesses().Where(p => p.ProcessName.Equals("vmmemWSL", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("vmmem", StringComparison.OrdinalIgnoreCase)).Sum(p => { try { return (double)p.WorkingSet64; } catch { return 0d; } }); } catch { }
        string cwd = Environment.CurrentDirectory;
        if (Directory.Exists(Path.Combine(cwd, ".git")) && CommandExists("git.exe"))
        {
            s.GitBranch = Run("git.exe", "rev-parse --abbrev-ref HEAD", 1200, cwd)?.Trim() ?? "";
            string status = Run("git.exe", "status --porcelain", 1200, cwd) ?? "";
            s.GitStatus = string.IsNullOrWhiteSpace(status) ? "clean" : $"{CountLines(status)} changed";
            s.GitLastCommit = Run("git.exe", "log -1 --pretty=%h%x20%s", 1200, cwd)?.Trim() ?? "";
        }
        s.NodeVersion = FirstLine(Run("node.exe", "--version", 1000));
        s.PythonVersion = FirstLine(Run("python.exe", "--version", 1000) ?? Run("py.exe", "--version", 1000));
        s.JavaVersion = FirstLine(Run("java.exe", "-version", 1000));
        s.GoVersion = FirstLine(Run("go.exe", "version", 1000));
        s.RustVersion = FirstLine(Run("rustc.exe", "--version", 1000));
        s.VsCodeRunning = HasProcess("Code", "Code - Insiders");
        s.TerminalRunning = HasProcess("WindowsTerminal", "wt", "pwsh", "powershell", "cmd");
        s.IdeRunning = s.VsCodeRunning || HasProcess("devenv", "idea64", "rider64", "pycharm64");
        s.LocalLlmRunning = HasProcess("ollama", "lmstudio", "LM Studio", "llama-server");
        _developer = s;
    }

    private void EnsureUsb()
    {
        if (DateTime.UtcNow < _nextUsbRead) return;
        _nextUsbRead = DateTime.UtcNow.AddSeconds(30);
        var s = new UsbSnapshot();
        try
        {
            var names = new List<string>();
            using var q = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'");
            foreach (ManagementObject mo in q.Get())
            {
                string n = Convert.ToString(mo["Name"])?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
            }
            s.DeviceCount = names.Count;
            s.DeviceList = string.Join(", ", names.Distinct().Take(30));
        }
        catch { }
        try
        {
            var names = new List<string>();
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Removable)) names.Add(d.Name.TrimEnd('\\'));
            s.StorageCount = names.Count; s.StorageList = string.Join(", ", names);
        }
        catch { }
        try
        {
            using var m = new ManagementObjectSearcher("SELECT Name FROM Win32_PointingDevice");
            foreach (ManagementObject mo in m.Get()) { s.MouseName = Convert.ToString(mo["Name"])?.Trim() ?? ""; if (!string.IsNullOrWhiteSpace(s.MouseName)) break; }
            using var k = new ManagementObjectSearcher("SELECT Name FROM Win32_Keyboard");
            foreach (ManagementObject mo in k.Get()) { s.KeyboardName = Convert.ToString(mo["Name"])?.Trim() ?? ""; if (!string.IsNullOrWhiteSpace(s.KeyboardName)) break; }
        }
        catch { }
        try
        {
            for (uint i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out _) == 0)
                {
                    s.GamepadCount++;
                    s.GamepadName = $"XInput Controller {i + 1}";
                    if (XInputGetBatteryInformation(i, 0, out var b) == 0)
                        s.GamepadBattery = XInputBatteryPercent(b.BatteryLevel);
                }
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        _usb = s;
    }

    private void EnsureSecurity()
    {
        if (DateTime.UtcNow < _nextSecurityRead) return;
        _nextSecurityRead = DateTime.UtcNow.AddMinutes(5);
        _security = ReadSecuritySnapshot();
        EnsureNetworkStatic();
    }

    private static SecuritySnapshot ReadSecuritySnapshot()
    {
        var s = new SecuritySnapshot { DefenderThreats = -1, WindowsUpdatePendingCount = -1, BitLockerPercent = null };
        try
        {
            using var q = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Defender", "SELECT AntivirusEnabled, RealTimeProtectionEnabled, QuickScanEndTime, FullScanEndTime FROM MSFT_MpComputerStatus");
            foreach (ManagementObject mo in q.Get())
            {
                bool enabled = SafeBool(mo["AntivirusEnabled"]); bool real = SafeBool(mo["RealTimeProtectionEnabled"]);
                s.DefenderStatus = enabled && real ? LocalizationManager.Text("已启用", "Enabled") : enabled ? LocalizationManager.Text("实时保护关闭", "Real-time protection off") : LocalizationManager.Text("未启用", "Disabled");
                DateTime latest = DateTime.MinValue;
                foreach (var name in new[] { "QuickScanEndTime", "FullScanEndTime" }) if (TryWmiDate(Convert.ToString(mo[name]), out var dt) && dt > latest) latest = dt;
                if (latest != DateTime.MinValue) s.DefenderLastScan = latest.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                break;
            }
            using var t = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Defender", "SELECT ThreatID FROM MSFT_MpThreat");
            int threats = 0; foreach (ManagementObject _ in t.Get()) threats++; s.DefenderThreats = threats;
        }
        catch { }
        try
        {
            Type? type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is not null)
            {
                dynamic fw = Activator.CreateInstance(type)!;
                int profile = (int)fw.CurrentProfileTypes;
                var names = new List<string>(); bool anyEnabled = false;
                foreach (var pair in new[] { (1, LocalizationManager.Text("域", "Domain")), (2, LocalizationManager.Text("专用", "Private")), (4, LocalizationManager.Text("公用", "Public")) })
                {
                    if ((profile & pair.Item1) == 0) continue;
                    names.Add(pair.Item2); try { anyEnabled |= (bool)fw.get_FirewallEnabled(pair.Item1); } catch { }
                }
                s.FirewallProfile = string.Join("/", names); s.FirewallStatus = anyEnabled ? LocalizationManager.Text("已启用", "Enabled") : LocalizationManager.Text("已关闭", "Off");
            }
        }
        catch { }
        try
        {
            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            using var q = new ManagementObjectSearcher("root\\CIMV2\\Security\\MicrosoftVolumeEncryption", $"SELECT ProtectionStatus, ConversionStatus, EncryptionPercentage FROM Win32_EncryptableVolume WHERE DriveLetter='{systemDrive}'");
            foreach (ManagementObject mo in q.Get())
            {
                int protection = SafeInt(mo["ProtectionStatus"]); s.BitLockerStatus = protection == 1 ? LocalizationManager.Text("已保护", "Protected") : LocalizationManager.Text("未保护", "Not protected");
                double pct = SafeDouble(mo["EncryptionPercentage"]); if (pct >= 0) s.BitLockerPercent = pct;
                break;
            }
        }
        catch { }
        try { s.SecureBoot = Convert.ToInt32(Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State")?.GetValue("UEFISecureBootEnabled") ?? 0, CultureInfo.InvariantCulture) == 1; } catch { }
        try
        {
            using var q = new ManagementObjectSearcher("root\\CIMV2\\Security\\MicrosoftTpm", "SELECT IsEnabled_InitialValue, SpecVersion FROM Win32_Tpm");
            foreach (ManagementObject mo in q.Get()) { s.TpmPresent = true; s.TpmVersion = Convert.ToString(mo["SpecVersion"])?.Split(',').FirstOrDefault()?.Trim() ?? ""; break; }
        }
        catch { }
        try { s.UacEnabled = Convert.ToInt32(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System")?.GetValue("EnableLUA") ?? 0, CultureInfo.InvariantCulture) != 0; } catch { }
        try
        {
            s.SmartScreenStatus = Convert.ToString(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer")?.GetValue("SmartScreenEnabled")) ?? LocalizationManager.Text("未知", "Unknown");
        }
        catch { }
        try
        {
            bool pending = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null
                           || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null;
            s.WindowsUpdateStatus = pending ? LocalizationManager.Text("等待重启", "Restart pending") : LocalizationManager.Text("正常", "OK");
            Type? sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is not null)
            {
                dynamic session = Activator.CreateInstance(sessionType)!; dynamic searcher = session.CreateUpdateSearcher(); dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0");
                s.WindowsUpdatePendingCount = (int)result.Updates.Count;
                if (s.WindowsUpdatePendingCount > 0 && !pending) s.WindowsUpdateStatus = LocalizationManager.Text("有可用更新", "Updates available");
            }
        }
        catch { }
        return s;
    }

    private static IReadOnlyList<DisplaySnapshot> EnumerateDisplays()
    {
        var list = new List<DisplaySnapshot>();
        for (uint i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & 0x1) == 0) continue;
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(dd.DeviceName, -1, ref dm))
            {
                var snapshot = new DisplaySnapshot(dd.DeviceString, (int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency, dm.dmBitsPerPel);
                if ((dd.StateFlags & 0x4) != 0) list.Insert(0, snapshot); // DISPLAY_DEVICE_PRIMARY_DEVICE
                else list.Add(snapshot);
            }
        }
        return list;
    }

    private static void PutWorld(IDictionary<string, object?> v, string key, string zone)
    {
        try { v[key] = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zone)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); } catch { }
    }

    private static double AverageSince(IEnumerable<(DateTime At, double Usage)> samples, DateTime since)
    {
        var items = samples.Where(x => x.At >= since).Select(x => x.Usage).ToList();
        return items.Count == 0 ? 0d : items.Average();
    }

    private static void PutIfNumber(IDictionary<string, object?> v, string key, double? value)
    {
        if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)) v[key] = value.Value;
    }
    private static void PutText(IDictionary<string, object?> v, string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) v[key] = value; }
    private static double Number(IDictionary<string, object?> v, string key) => v.TryGetValue(key, out var x) ? SafeDouble(x) : 0d;
    private static string Text(IDictionary<string, object?> v, string key) => v.TryGetValue(key, out var x) ? Convert.ToString(x) ?? "" : "";
    private static double SafeDouble(object? x) { try { return Convert.ToDouble(x ?? 0, CultureInfo.InvariantCulture); } catch { return 0d; } }
    private static int SafeInt(object? x) { try { return Convert.ToInt32(x ?? 0, CultureInfo.InvariantCulture); } catch { return 0; } }
    private static bool SafeBool(object? x) { try { return Convert.ToBoolean(x ?? false, CultureInfo.InvariantCulture); } catch { return false; } }
    private static double? NullableDouble(ManagementObject mo, string name) { try { var p = mo.Properties[name]; return p?.Value is null ? null : Convert.ToDouble(p.Value, CultureInfo.InvariantCulture); } catch { return null; } }
    private static double? ParseNullable(string? s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    private static bool SimilarName(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        string N(string x) => Regex.Replace(x.ToLowerInvariant(), @"[^a-z0-9]", "");
        string na = N(a), nb = N(b); return na.Contains(nb) || nb.Contains(na);
    }
    private static double? MaxSensor(IEnumerable<ISensor> sensors, SensorType type)
    {
        var values = sensors.Where(x => x.SensorType == type && x.Value.HasValue)
            .Select(x => (double)x.Value!.Value)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }
    private static double? FirstSensor(IEnumerable<ISensor> sensors, SensorType type, string name) => sensors.FirstOrDefault(x => x.SensorType == type && x.Value.HasValue && x.Name.Contains(name, StringComparison.OrdinalIgnoreCase))?.Value is float f ? f : null;
    private static string BatteryChemistryName(int c) => c switch
    {
        1 => LocalizationManager.Text("其他", "Other"),
        2 => LocalizationManager.Text("未知", "Unknown"),
        3 => LocalizationManager.Text("铅酸", "Lead-acid"),
        4 => LocalizationManager.Text("镍镉", "NiCd"),
        5 => LocalizationManager.Text("镍氢", "NiMH"),
        6 => LocalizationManager.Text("锂离子", "Li-ion"),
        7 => LocalizationManager.Text("锌空气", "Zinc-air"),
        8 => LocalizationManager.Text("锂聚合物", "Li-polymer"),
        _ => LocalizationManager.Text($"类型 {c}", $"Type {c}")
    };
    private static string MemoryFormFactorName(int x) => x switch { 8 => "DIMM", 9 => "TSOP", 12 => "SODIMM", 13 => "SRIMM", 15 => "FB-DIMM", _ => x > 0 ? $"FormFactor {x}" : "" };
    private static string MemoryTypeName(int x) => x switch { 20 => "DDR", 21 => "DDR2", 22 => "DDR2 FB-DIMM", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => x > 0 ? $"SMBIOS {x}" : "" };
    private static bool TryWmiDate(string? raw, out DateTime dt) { dt = default; try { if (!string.IsNullOrWhiteSpace(raw)) { dt = ManagementDateTimeConverter.ToDateTime(raw); return true; } } catch { } return false; }
    private static bool HasProcess(params string[] names)
    {
        try
        {
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try { if (set.Contains(p.ProcessName)) return true; }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
        return false;
    }
    private static bool CommandExists(string file) { if (Path.IsPathRooted(file)) return File.Exists(file); var path = Environment.GetEnvironmentVariable("PATH") ?? ""; return path.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(p => File.Exists(Path.Combine(p.Trim(), file))) || (file.Equals("nvidia-smi.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "nvidia-smi.exe"))); }
    private static string? Run(string file, string args, int timeoutMs, string? cwd = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = cwd ?? Environment.CurrentDirectory };
            using var p = Process.Start(psi); if (p is null) return null; string o = p.StandardOutput.ReadToEnd(); string e = p.StandardError.ReadToEnd(); if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return null; } return string.IsNullOrWhiteSpace(o) ? e : o;
        }
        catch { return null; }
    }
    private static string? RunPowerShell(string script, int timeoutMs) => Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "`\"") + "\"", timeoutMs);
    private static int CountLines(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
    private static int CountDistinctLines(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    private static string FirstLine(string? text) => string.IsNullOrWhiteSpace(text) ? "" : text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
    private static string JsonText(JsonElement e, string name) { try { return e.TryGetProperty(name, out var p) ? p.ToString() : ""; } catch { return ""; } }
    private static double? JsonNumber(JsonElement e, string name) { try { if (!e.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null) return null; if (p.TryGetDouble(out var d)) return d; return double.TryParse(p.ToString(), out d) ? d : null; } catch { return null; } }
    private static int XInputBatteryPercent(byte level) => level switch { 0 => 0, 1 => 25, 2 => 60, 3 => 100, _ => -1 };

    private bool TryReadClipboardText(out string text)
    {
        text = ""; if (!OpenClipboard(IntPtr.Zero)) return false;
        try { IntPtr h = GetClipboardData(CF_UNICODETEXT); if (h == IntPtr.Zero) return false; IntPtr p = GlobalLock(h); if (p == IntPtr.Zero) return false; try { text = Marshal.PtrToStringUni(p) ?? ""; return true; } finally { GlobalUnlock(h); } } finally { CloseClipboard(); }
    }
    private bool TryReadClipboardDibSize(out int width, out int height)
    {
        width = height = 0; if (!OpenClipboard(IntPtr.Zero)) return false;
        try { uint fmt = IsClipboardFormatAvailable(CF_DIBV5) ? CF_DIBV5 : CF_DIB; IntPtr h = GetClipboardData(fmt); if (h == IntPtr.Zero) return false; IntPtr p = GlobalLock(h); if (p == IntPtr.Zero) return false; try { width = Marshal.ReadInt32(p, 4); height = Math.Abs(Marshal.ReadInt32(p, 8)); return width > 0 && height > 0; } finally { GlobalUnlock(h); } } finally { CloseClipboard(); }
    }
    private int GetClipboardFileCount()
    {
        if (!OpenClipboard(IntPtr.Zero)) return 0;
        try { IntPtr h = GetClipboardData(CF_HDROP); return h == IntPtr.Zero ? 0 : (int)DragQueryFile(h, 0xFFFFFFFF, null, 0); } finally { CloseClipboard(); }
    }

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
    }

    private sealed class PerfOsSnapshot { public double? ContextSwitchesPerSec, SystemCallsPerSec, InterruptsPerSec, DpcTimePercent, InstructionsRetiredPerSec, StandbyBytes, ModifiedBytes; }
    private sealed class MemoryStaticSnapshot { public double? HardwareReservedBytes, SpeedMhz; public int SlotCount, SlotUsed; public string FormFactor = "", MemoryType = ""; }
    private sealed class HardwareSnapshot { public double? CpuTemperatureMax, CpuCoreTemperatureAvg, CpuPowerMax, CpuVoltage, CpuBusClockMhz, MotherboardTemperatureMax, FanRpmMax, FanPercentMax; public List<GpuHardwareSnapshot> Gpus { get; } = new(); }
    private sealed class GpuHardwareSnapshot { public string Name = ""; public double? CoreTempC, HotspotTempC, MemoryTempC, VoltageV, CoreClockMhz, MemoryClockMhz, PowerW; }
    private sealed class NvidiaSnapshot { public string Name = "", BiosVersion = ""; public double? PowerLimitW, CoreClockMhz, MemoryClockMhz, PcieGen, PcieWidth; }
    private sealed class NetworkStaticSnapshot { public bool VpnActive, ProxyEnabled; public string VpnName = "", ProxyAddress = "", WifiBand = "", WifiStandard = ""; public int TcpConnections, UdpListeners; public double? WifiSignalDbm, WifiChannel, DnsLatencyMs; }
    private sealed class DiskAdvancedSnapshot { public string Health = "", SmartStatus = "", TrimStatus = ""; public double? TemperatureC, PowerOnHours; }
    private sealed class SystemStaticSnapshot { public string PowerPlanGuid = "", PowerPlanName = "", BiosVersion = "", BiosDate = "", BoardManufacturer = "", BoardModel = "", UpdateLastInstalled = "", DefenderStatus = "", FirewallStatus = "", BitLockerStatus = ""; public bool UpdatePending, HyperVEnabled, WslInstalled; public int WslDistroCount; }
    private readonly record struct ProcCpuSample(TimeSpan Cpu, DateTime At);
    private sealed class ProcessSnapshot { public int BackgroundCount; public string TopCpuName = "", TopMemoryName = "", TopDiskName = "", TopGpuName = ""; public int TopCpuPid, TopMemoryPid, TopDiskPid; public double TopCpuUsage, TopMemoryBytes, TopDiskBps, TopGpuUsage; }
    private sealed class DeveloperSnapshot { public bool DockerRunning, WslRunning, VsCodeRunning, TerminalRunning, IdeRunning, LocalLlmRunning; public int DockerContainers = -1, DockerImages = -1; public double? WslMemoryBytes; public string WslDistro = "", GitBranch = "", GitStatus = "", GitLastCommit = "", NodeVersion = "", PythonVersion = "", JavaVersion = "", GoVersion = "", RustVersion = ""; }
    private sealed class UsbSnapshot { public int DeviceCount, StorageCount, GamepadCount; public int GamepadBattery = -1; public string DeviceList = "", StorageList = "", MouseName = "", KeyboardName = "", GamepadName = ""; }
    private sealed class SecuritySnapshot { public string DefenderStatus = "", DefenderLastScan = "", FirewallStatus = "", FirewallProfile = "", BitLockerStatus = "", TpmVersion = "", SmartScreenStatus = "", WindowsUpdateStatus = ""; public int DefenderThreats = -1, WindowsUpdatePendingCount = -1; public double? BitLockerPercent; public bool SecureBoot, TpmPresent, UacEnabled; }
    private readonly record struct DisplaySnapshot(string Name, int Width, int Height, int RefreshRate, int BitsPerPixel);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DISPLAY_DEVICE { public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString; public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DEVMODE { private const int CCHDEVICENAME = 32, CCHFORMNAME = 32; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)] public string dmDeviceName; public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput; public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight; }
    [StructLayout(LayoutKind.Sequential)] private struct MEMORYSTATUSEX_ADV { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)] private struct XINPUT_STATE { public uint PacketNumber; public XINPUT_GAMEPAD Gamepad; }
    [StructLayout(LayoutKind.Sequential)] private struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
    [StructLayout(LayoutKind.Sequential)] private struct XINPUT_BATTERY_INFORMATION { public byte BatteryType, BatteryLevel; }

    private const uint CF_BITMAP = 2, CF_UNICODETEXT = 13, CF_HDROP = 15, CF_DIB = 8, CF_DIBV5 = 17;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX_ADV lpBuffer);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);
}
