using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;

if (args.Length > 0 && args[0] == "--gui-audit")
{
    Environment.ExitCode = LinuxGuiAudit.Run(Path.GetFullPath(args[1]));
    return;
}

string fixture = Path.Combine(Path.GetTempPath(), "ecp-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
void Write(string path, string data)
{
    var full = Path.Combine(fixture, path);
    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
    File.WriteAllText(full, data);
}
void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
double Get(Dictionary<string, object?> data, string key) => Convert.ToDouble(data[key]);
try
{
    var provider = new LinuxVariableProvider(fixture);
    var keys = new HashSet<string> { "cpu.usage", "memory.usage", "battery.percent", "gpu.usage" };
    Dictionary<string, object?> Snapshot()
    {
        var values = new Dictionary<string, object?>();
        provider.Collect(values, keys, null);
        return values;
    }
    var empty = Snapshot();
    Assert(!empty.ContainsKey("memory.usage") && !empty.ContainsKey("battery.percent") && !empty.ContainsKey("gpu.usage"), "Missing sensors must not become fake zeroes.");
    Write("proc/stat", "cpu 100 0 50 800 50 0 0 0 10 0\n");
    Write("proc/meminfo", "MemTotal: 1000 kB\nMemAvailable: 250 kB\nSwapTotal: 200 kB\nSwapFree: 100 kB\n");
    Assert(!Snapshot().ContainsKey("cpu.usage"), "CPU requires two samples.");
    Write("proc/stat", "cpu 120 0 60 860 60 0 0 0 30 0\n");
    var measured = Snapshot();
    Assert(Math.Abs(Get(measured, "cpu.usage") - 30) < 0.001, "CPU deltas must exclude guest duplication and include iowait as idle.");
    Assert(Get(measured, "memory.usage") == 75 && Get(measured, "memory.used_bytes") == 768000, "MemAvailable/kB conversion failed.");
    Write("proc/stat", "cpu 1 0 1 1 0 0 0 0\n");
    Assert(!Snapshot().ContainsKey("cpu.usage"), "Counter reset must re-prime, not underflow.");
    Write("sys/class/power_supply/AC/type", "Mains");
    Write("sys/class/power_supply/AC/online", "1");
    Write("sys/class/power_supply/BAT0/type", "Battery");
    Write("sys/class/power_supply/BAT0/status", "Charging");
    Write("sys/class/power_supply/BAT0/energy_now", "25000000");
    Write("sys/class/power_supply/BAT0/energy_full", "50000000");
    Write("sys/class/power_supply/BAT0/power_now", "10000000");
    Assert(provider.TryGetAcOnline(out bool ac) && ac, "AC state missing.");
    var battery = Snapshot();
    Assert(Get(battery, "battery.percent") == 50 && Get(battery, "battery.remaining_mwh") == 25000 && Get(battery, "battery.rate_watts") == 10, "Battery units failed.");
    Write("sys/class/power_supply/BAT1/type", "Battery");
    Write("sys/class/power_supply/BAT1/status", "Discharging");
    Write("sys/class/power_supply/BAT1/charge_now", "1000000");
    Write("sys/class/power_supply/BAT1/charge_full", "2000000");
    Write("sys/class/power_supply/BAT1/voltage_min_design", "10000000");
    Assert(Get(Snapshot(), "battery.full_mwh") == 70000, "Charge/energy multi-battery aggregation failed.");
    Write("sys/class/power_supply/BAT1/power_now", "4000000");
    var mixedBattery = Snapshot();
    Assert(Get(mixedBattery, "battery.charge_rate_watts") == 10 && Get(mixedBattery, "battery.discharge_rate_watts") == 4,
        "Mixed battery charge/discharge power must be aggregated separately.");
    Assert(mixedBattery["battery.estimated_time_to_full"] is string && mixedBattery["battery.estimated_time_to_empty"] is string,
        "Mixed battery states cannot produce a meaningful whole-system time estimate.");
    Write("sys/class/drm/card0/device/vendor", "0x1002");
    Write("sys/class/drm/card0/device/gpu_busy_percent", "42");
    Write("sys/class/drm/card0/device/mem_info_vram_total", "1000000");
    Write("sys/class/drm/card0/device/mem_info_vram_used", "250000");
    var gpu = Snapshot();
    Assert(Get(gpu, "gpu.usage") == 42 && Get(gpu, "gpu.dedicated_usage") == 25, "DRM GPU metrics failed.");
    Write("sys/class/drm/card1/device/vendor", "0x8086");
    var integrated = new Dictionary<string, object?>();
    provider.Collect(integrated, new HashSet<string> { "gpu.name" }, "card1");
    Assert(integrated.ContainsKey("gpu.name") && !integrated.ContainsKey("gpu.usage") && !integrated.ContainsKey("gpu.memory_total_bytes"), "A different GPU must not inherit unavailable metrics.");
    var nvidia = LinuxNvidia.Parse("0, GPU-fixture, NVIDIA Test, 4096, 1024, 37, 54, 20.5, [N/A], 900, 3000, 550.1, bios, 4, 16").Single();
    Assert(Get(nvidia, "gpu.memory_total_bytes") == 4294967296 && Get(nvidia, "gpu.dedicated_usage") == 25, "NVIDIA MiB conversion failed.");
    Assert(!nvidia.ContainsKey("gpu.power_limit_w") && LinuxNvidia.Parse("invalid response").Count == 0, "NVIDIA unsupported fields must not become zeroes.");
    Assert(LinuxVariableProvider.DecodeMount(@"/mnt/my\040disk\134test") == "/mnt/my disk\\test", "mountinfo escape handling failed.");
    Write("sys/class/power_supply/BAT1/present", "0");
    Write("sys/class/power_supply/BAT0/cycle_count", "-1");
    Assert(!Snapshot().ContainsKey("battery.cycle_count"), "Battery -1 sentinel is not a real cycle count.");
    Assert(Snapshot()["battery.time_remaining_seconds"] is string, "Charging must display an actual state instead of fabricated remaining time.");
    Assert(StartupManager.QuoteDesktopArgument("/tmp/ECP 100%.AppImage") == "\"/tmp/ECP 100%%.AppImage\"", "Desktop Exec quoting failed.");
    bool rejected = false;
    try { StartupManager.QuoteDesktopArgument("evil\nExec=other"); } catch (ArgumentException) { rejected = true; }
    Assert(rejected, "Desktop entry newline injection must fail.");
    Console.WriteLine("PASS: procfs/sysfs fixtures, counter reset, missing sensors, multi-battery, GPU, desktop quoting");

    if (OperatingSystem.IsLinux())
    {
        // Set isolated data paths before SettingsManager's static initialization.
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(fixture, "data"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(fixture, "config"));
        string encrypted = SecretStore.Protect("ecp-platform-test-secret");
        Assert(encrypted.StartsWith("linux-aesgcm-v1:"), "Linux encryption failed.");
        Assert(SecretStore.Unprotect(encrypted) == "ecp-platform-test-secret", "Linux secret round trip failed.");
        var bytes = Convert.FromBase64String(encrypted[16..]);
        bytes[^1] ^= 1;
        Assert(SecretStore.Unprotect("linux-aesgcm-v1:" + Convert.ToBase64String(bytes)) == "", "Tampered ciphertext must fail authentication.");
        var key = Path.Combine(SettingsManager.SettingsDirectory, "secrets/master.key");
        Assert(File.GetUnixFileMode(key) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), $"Master key must be mode 0600: {key} is {File.GetUnixFileMode(key)}.");
        string? originalAppImage = Environment.GetEnvironmentVariable("APPIMAGE");
        Environment.SetEnvironmentVariable("APPIMAGE", "/tmp/ECP 100%.AppImage");
        StartupManager.Apply(true);
        Assert(File.ReadAllText(StartupManager.AutostartPath).Contains("Name=Endfield Charge Plus For Linux"), "Autostart branding failed.");
        Assert(File.ReadAllText(StartupManager.AutostartPath).Contains("Exec=\"/tmp/ECP 100%%.AppImage\" --autostart"), "Autostart must retain original AppImage path.");
        StartupManager.Apply(false);
        Assert(!File.Exists(StartupManager.AutostartPath), "Autostart disabling failed.");
        Environment.SetEnvironmentVariable("APPIMAGE", originalAppImage);
        using var hub = new VariableHub();
        var request = new[] { "cpu.usage", "memory.usage", "disk.system.usage", "network.download_bps", "battery.percent", "gpu.usage", "security.defender.status", "time.current" };
        await hub.SnapshotAsync(CustomHudSettings.CreateDefault(), request);
        await Task.Delay(150);
        var live = await hub.SnapshotAsync(CustomHudSettings.CreateDefault(), request);
        foreach (var metric in new[] { "cpu.usage", "memory.usage", "disk.system.usage", "network.download_bps", "time.current" })
            Assert(live.ContainsKey(metric), "Live Linux metric missing: " + metric);
        Assert(!live.ContainsKey("security.defender.status"), "Windows-only collectors must remain inactive.");
        Console.WriteLine("PASS: Linux secrets, permissions, autostart and live VariableHub integration");
    }
}
finally { Directory.Delete(fixture, recursive: true); }
