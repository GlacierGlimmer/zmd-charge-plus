using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

internal sealed partial class LinuxVariableProvider
{
    internal sealed record LinuxMount(string Point, string Device, string FileSystem, string MajorMinor)
    {
        internal string Prefix => "disk.mount_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Point)))[..12].ToLowerInvariant();
    }
    private readonly Dictionary<string, (long Tick, double Read, double Write, double Busy)> _diskSamples = new();

    internal static IReadOnlyList<LinuxMount> Mounts()
    {
        var mounts = new List<LinuxMount>();
        foreach (var line in (Read("/proc/self/mountinfo") ?? "").Split('\n'))
        {
            var p = line.Split(' ');
            int dash = Array.IndexOf(p, "-");
            if (p.Length < 10 || dash < 6 || dash + 2 >= p.Length) continue;
            string point = DecodeMount(p[4]);
            string device = DecodeMount(p[dash + 2]);
            if (point != "/" && (!device.StartsWith("/dev/", StringComparison.Ordinal) || !Directory.Exists(point))) continue;
            mounts.Add(new LinuxMount(point, device, p[dash + 1], p[2]));
        }
        return mounts.GroupBy(m => m.Point).Select(g => g.First()).OrderBy(m => m.Point, StringComparer.Ordinal).ToArray();
    }

    internal static string DecodeMount(string path) => Regex.Replace(path, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());

    private void CollectDisks(IDictionary<string, object?> v)
    {
        var mounts = Mounts();
        var totals = new Dictionary<string, (double Total, double Free)>();
        var mountedNames = new List<string>();
        foreach (var mount in mounts)
        {
            try
            {
                var drive = new DriveInfo(mount.Point);
                if (!drive.IsReady) continue;
                double total = drive.TotalSize, free = drive.AvailableFreeSpace;
                void Capacity(string prefix)
                {
                    v[prefix + ".root"] = mount.Point;
                    v[prefix + ".label"] = mount.Device;
                    v[prefix + ".filesystem"] = mount.FileSystem;
                    v[prefix + ".total_bytes"] = total;
                    v[prefix + ".free_bytes"] = free;
                    v[prefix + ".used_bytes"] = Math.Max(0, total - free);
                    if (total > 0)
                    {
                        v[prefix + ".usage"] = (total - free) / total * 100;
                        v[prefix + ".free_percent"] = free / total * 100;
                    }
                }
                Capacity(mount.Prefix);
                if (mount.Point == "/") Capacity("disk.system");
                if (totals.TryAdd(mount.MajorMinor, (total, free))) mountedNames.Add(mount.Point);
                var stat = (Read(PathAt($"sys/dev/block/{mount.MajorMinor}/stat")) ?? "")
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToArray();
                if (stat.Length < 11 || stat.Take(11).Any(n => !n.HasValue)) continue;
                var now = Stopwatch.GetTimestamp();
                double read = stat[2]!.Value * 512, write = stat[6]!.Value * 512, busy = stat[9]!.Value;
                void Io(string prefix)
                {
                    v[prefix + ".queue_length"] = stat[8]!.Value;
                    if (_diskSamples.TryGetValue(mount.Point, out var old))
                    {
                        double seconds = Stopwatch.GetElapsedTime(old.Tick, now).TotalSeconds;
                        if (seconds <= 0 || read < old.Read || write < old.Write || busy < old.Busy) return;
                        v[prefix + ".read_bps"] = (read - old.Read) / seconds;
                        v[prefix + ".write_bps"] = (write - old.Write) / seconds;
                        v[prefix + ".io_bps"] = (read - old.Read + write - old.Write) / seconds;
                        v[prefix + ".active_percent"] = Math.Clamp((busy - old.Busy) / seconds / 10, 0, 100);
                    }
                }
                Io(mount.Prefix);
                if (mount.Point == "/") Io("disk.system");
                _diskSamples[mount.Point] = (now, read, write, busy);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        v["disk.fixed.count"] = totals.Count;
        v["disk.fixed.list"] = string.Join(", ", mountedNames);
        if (totals.Count > 0)
        {
            double total = totals.Values.Sum(m => m.Total), free = totals.Values.Sum(m => m.Free);
            v["disk.fixed.total_bytes"] = total;
            v["disk.fixed.free_bytes"] = free;
            v["disk.fixed.used_bytes"] = Math.Max(0, total - free);
            if (total > 0) v["disk.fixed.usage"] = (total - free) / total * 100;
        }
    }
}
