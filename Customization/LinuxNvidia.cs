using System.Globalization;

namespace EndfieldChargePlus.Customization;

internal static class LinuxNvidia
{
    private static readonly object Gate = new();
    private static DateTime _expires;
    private static IReadOnlyList<Dictionary<string, object?>> _cache = Array.Empty<Dictionary<string, object?>>();

    internal static IReadOnlyList<Dictionary<string, object?>> Read()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow < _expires) return _cache;
            _expires = DateTime.UtcNow.AddSeconds(2);
            string? csv = LinuxCommand.Run("nvidia-smi",
                "--query-gpu=index,uuid,name,memory.total,memory.used,utilization.gpu,temperature.gpu,power.draw,power.limit,clocks.gr,clocks.mem,driver_version,vbios_version,pcie.link.gen.current,pcie.link.width.current",
                "--format=csv,noheader,nounits");
            _cache = Parse(csv ?? "");
            return _cache;
        }
    }

    internal static IReadOnlyList<Dictionary<string, object?>> Parse(string csv)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split(',').Select(s => s.Trim().Trim('"')).ToArray();
            if (p.Length != 15 || !int.TryParse(p[0], out int index) || !p[1].StartsWith("GPU-")) continue;
            var v = new Dictionary<string, object?>
            {
                ["gpu.adapter_id"] = "nvidia:" + p[1], ["gpu.name"] = p[2], ["gpu.physical_index"] = index,
                ["gpu.nvidia_smi_available"] = true,
            };
            void Metric(string key, int column, double scale = 1)
            {
                if (double.TryParse(p[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) && n >= 0)
                    v[key] = n * scale;
            }
            Metric("gpu.memory_total_bytes", 3, 1024 * 1024);
            Metric("gpu.memory_used_bytes", 4, 1024 * 1024);
            Metric("gpu.usage", 5); Metric("gpu.temperature", 6); Metric("gpu.power_w", 7); Metric("gpu.power_limit_w", 8);
            Metric("gpu.core_clock_mhz", 9); Metric("gpu.memory_clock_mhz", 10);
            Metric("gpu.pcie_gen", 13); Metric("gpu.pcie_lanes", 14);
            if (p[11] != "[N/A]") v["gpu.driver_version"] = p[11];
            if (p[12] != "[N/A]") v["gpu.bios_version"] = p[12];
            if (v.TryGetValue("gpu.memory_total_bytes", out var total))
            {
                v["gpu.vram_bytes"] = total; v["gpu.dedicated_total_bytes"] = total;
                if (v.TryGetValue("gpu.memory_used_bytes", out var used))
                {
                    v["gpu.dedicated_used_bytes"] = used;
                    if (Convert.ToDouble(total) > 0) v["gpu.dedicated_usage"] = Convert.ToDouble(used) / Convert.ToDouble(total) * 100;
                }
            }
            rows.Add(v);
        }
        return rows;
    }
}
