using System.Diagnostics;
using System.Globalization;

namespace EndfieldChargePlus.Customization;

internal sealed partial class LinuxVariableProvider
{
    private readonly Dictionary<int, (long Start, double Cpu, double? Io)> _processSamples = new();
    private long _lastProcessTick;
    private DateTime _nextDeveloperRead;
    private readonly Dictionary<string, object?> _developerValues = new();

    private void AddLinuxSystem(IDictionary<string, object?> v)
    {
        var uptime = Parse(Read(PathAt("proc/uptime"))?.Split(' ')[0]);
        if (uptime.HasValue)
        {
            v["system.uptime_seconds"] = uptime.Value;
            v["system.uptime_text"] = TimeSpan.FromSeconds(uptime.Value).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture);
            v["system.boot_time"] = DateTime.Now.AddSeconds(-uptime.Value).ToString("yyyy-MM-dd HH:mm:ss");
        }
        Put(v, "system.kernel_version", Read(PathAt("proc/sys/kernel/osrelease")));
        var release = (Read(PathAt("etc/os-release")) ?? "").Split('\n')
            .FirstOrDefault(s => s.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
        if (release is not null) v["system.os_description"] = release[12..].Trim('"');
        foreach (var (key, file) in new[] { ("system.bios_version", "bios_version"), ("system.bios_date", "bios_date"),
                     ("system.motherboard_manufacturer", "board_vendor"), ("system.motherboard_model", "board_name") })
            Put(v, key, Read(PathAt("sys/class/dmi/id/" + file)));
        var profile = Read(PathAt("sys/firmware/acpi/platform_profile"));
        if (profile is not null)
        {
            v["system.power_plan"] = profile;
            v["system.power_plan_name"] = profile;
        }
        if (File.Exists(PathAt("etc/debian_version")))
            v["system.reboot_required"] = File.Exists(PathAt("var/run/reboot-required"));
        if (Directory.Exists(PathAt("sys/class/tpm")))
        {
            var devices = Directories(PathAt("sys/class/tpm"));
            v["security.tpm.present"] = devices.Length > 0;
            if (devices.FirstOrDefault() is { } tpm)
                Put(v, "security.tpm.version", Read(Path.Combine(tpm, "tpm_version_major")));
        }
        foreach (var file in Files(PathAt("sys/firmware/efi/efivars"), "SecureBoot-*"))
        {
            try { var bytes = File.ReadAllBytes(file); if (bytes.Length >= 5) v["security.secure_boot"] = bytes[4] == 1; }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var fans = Directories(PathAt("sys/class/hwmon")).SelectMany(d => Files(d, "fan*_input"))
            .Select(Number).Where(n => n.HasValue).ToArray();
        if (fans.Length > 0) v["system.fan_speed"] = fans.Max(n => n!.Value);
    }

    private void AddProcesses(IDictionary<string, object?> v, bool metrics, bool tools)
    {
        var now = Stopwatch.GetTimestamp();
        double elapsed = _lastProcessTick == 0 ? 0 : Stopwatch.GetElapsedTime(_lastProcessTick, now).TotalSeconds;
        var rows = new List<(int Pid, string Name, double Memory, double? Cpu, double? Io)>();
        var current = new Dictionary<int, (long Start, double Cpu, double? Io)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string name = process.ProcessName;
                    long start = process.StartTime.ToUniversalTime().Ticks;
                    double cpu = process.TotalProcessorTime.TotalSeconds;
                    var ioFields = (Read(PathAt($"proc/{process.Id}/io")) ?? "").Split('\n').Select(l => l.Split(':', 2))
                        .Where(p => p.Length == 2).ToDictionary(p => p[0], p => Parse(p[1]));
                    double? io = ioFields.GetValueOrDefault("read_bytes") + ioFields.GetValueOrDefault("write_bytes");
                    double? cpuRate = null, ioRate = null;
                    if (elapsed > 0 && _processSamples.TryGetValue(process.Id, out var old) && old.Start == start)
                    {
                        if (cpu >= old.Cpu) cpuRate = Math.Clamp((cpu - old.Cpu) / elapsed / Environment.ProcessorCount * 100, 0, 100);
                        if (io >= old.Io) ioRate = (io - old.Io) / elapsed;
                    }
                    rows.Add((process.Id, name, process.WorkingSet64, cpuRate, ioRate));
                    current[process.Id] = (start, cpu, io);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or IOException) { }
            }
        }
        if (metrics)
        {
            v["process.count"] = Directories(PathAt("proc")).Count(d => int.TryParse(Path.GetFileName(d), out _));
            if (rows.Count > 0)
            {
                var memory = rows.MaxBy(r => r.Memory);
                PutProcess(v, "memory", memory.Pid, memory.Name, memory.Memory);
                var cpuRows = rows.Where(r => r.Cpu.HasValue).ToArray();
                if (cpuRows.Length > 0) { var cpu = cpuRows.MaxBy(r => r.Cpu); PutProcess(v, "cpu", cpu.Pid, cpu.Name, cpu.Cpu!.Value); }
                var ioRows = rows.Where(r => r.Io.HasValue).ToArray();
                if (ioRows.Length > 0) { var io = ioRows.MaxBy(r => r.Io); PutProcess(v, "disk", io.Pid, io.Name, io.Io!.Value); }
            }
        }
        _processSamples.Clear();
        foreach (var pair in current) _processSamples[pair.Key] = pair.Value;
        _lastProcessTick = now;
        if (tools)
        {
            bool Has(params string[] names) => rows.Any(r => names.Contains(r.Name, StringComparer.OrdinalIgnoreCase));
            v["dev.docker.running"] = Has("dockerd", "docker-desktop");
            v["dev.vscode.running"] = Has("code", "code-insiders", "codium");
            v["dev.terminal.running"] = Has("gnome-terminal-server", "gnome-terminal-", "konsole", "alacritty", "kitty", "xterm", "xfce4-terminal", "foot", "ptyxis");
            v["dev.ide.running"] = Has("code", "codium", "idea", "pycharm", "rider", "clion", "eclipse");
            var llms = rows.Where(r => new[] { "ollama", "llama-server", "lm-studio", "LM Studio" }.Contains(r.Name, StringComparer.OrdinalIgnoreCase)).Select(r => r.Name).Distinct();
            v["dev.llm.local_status"] = llms.Any() ? string.Join(", ", llms) : LocalizationManager.Text("未运行", "Not running");
        }
    }

    private static void PutProcess(IDictionary<string, object?> v, string type, int pid, string name, double value)
    {
        v[$"process.top_{type}.name"] = name;
        v[$"process.top_{type}.pid"] = pid;
        v[$"process.top_{type}.usage"] = value;
    }

    private void AddDevices(IDictionary<string, object?> v)
    {
        var usb = Directories(PathAt("sys/bus/usb/devices"))
            .Where(d => File.Exists(Path.Combine(d, "idVendor")))
            .Select(d => Read(Path.Combine(d, "product")) ?? $"{Read(Path.Combine(d, "idVendor"))}:{Read(Path.Combine(d, "idProduct"))}")
            .ToArray();
        v["usb.device.count"] = usb.Length;
        v["usb.device.list"] = string.Join(", ", usb);
        var input = Read(PathAt("proc/bus/input/devices")) ?? "";
        var blocks = input.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        string? DeviceName(string handler) => blocks.Where(b => b.Split('\n').Any(l => l.StartsWith("H: Handlers=") && l.Contains(handler)))
            .Select(b => b.Split('\n').FirstOrDefault(l => l.StartsWith("N: Name="))?[8..].Trim('"')).FirstOrDefault();
        Put(v, "peripheral.mouse.name", DeviceName("mouse"));
        Put(v, "peripheral.keyboard.name", DeviceName("kbd"));
        var joysticks = Directories(PathAt("sys/class/input")).Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), "^js[0-9]+$")).ToArray();
        v["peripheral.gamepad.count"] = joysticks.Length;
        var names = joysticks.Select(d => Read(Path.Combine(d, "device/name"))).Where(n => n is not null).ToArray();
        if (names.Length > 0) v["peripheral.gamepad.name"] = string.Join(", ", names);
    }

    private void AddDeveloper(IDictionary<string, object?> v)
    {
        if (DateTime.UtcNow >= _nextDeveloperRead)
        {
            _nextDeveloperRead = DateTime.UtcNow.AddSeconds(30);
            _developerValues.Clear();
            foreach (var (key, command, args) in new[]
            {
                ("dev.node.version", "node", new[]{"--version"}), ("dev.python.version", "python3", new[]{"--version"}),
                ("dev.java.version", "java", new[]{"-version"}), ("dev.golang.version", "go", new[]{"version"}),
                ("dev.rust.version", "rustc", new[]{"--version"}),
                ("dev.git.branch", "git", new[]{"branch", "--show-current"}),
                ("dev.git.last_commit", "git", new[]{"log", "-1", "--format=%h %s"}),
            })
            {
                var value = LinuxCommand.Run(command, args);
                if (!string.IsNullOrWhiteSpace(value)) _developerValues[key] = value.Split('\n')[0];
            }
            if (LinuxCommand.Run("git", "status", "--porcelain") is { } status)
                _developerValues["dev.git.status"] = status.Length == 0 ? "clean" : $"{status.Split('\n').Length} changes";
            if (LinuxCommand.Run("docker", "ps", "-q") is { } containers)
                _developerValues["dev.docker.containers"] = containers.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            if (LinuxCommand.Run("docker", "images", "-q") is { } images)
                _developerValues["dev.docker.images"] = images.Split('\n', StringSplitOptions.RemoveEmptyEntries).Distinct().Count();
        }
        foreach (var pair in _developerValues) v[pair.Key] = pair.Value;
    }

    private static string[] Files(string path, string pattern)
    {
        try { return Directory.GetFiles(path, pattern); }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
