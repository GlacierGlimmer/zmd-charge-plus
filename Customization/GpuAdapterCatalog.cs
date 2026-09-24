using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

public sealed record GpuAdapterInfo(
    string Id,
    string Name,
    int PhysicalIndex,
    double DedicatedMemoryBytes,
    double SharedMemoryBytes,
    IReadOnlyList<string> PerfLuidTokens,
    IReadOnlyList<string> LegacyIds)
{
    public string PerfLuidToken => PerfLuidTokens.FirstOrDefault() ?? "";

    // Keep this as metadata only. The HUD/selector intentionally reports dedicated VRAM,
    // because shared system-memory limits are not the GPU's physical VRAM capacity.
    public bool UsesUnifiedMemory => DedicatedMemoryBytes < 2d * 1024 * 1024 * 1024
                                     && SharedMemoryBytes > DedicatedMemoryBytes;

    public double DisplayMemoryBytes => Math.Max(0, DedicatedMemoryBytes);

    public bool MatchesId(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (string.Equals(Id, candidate, StringComparison.OrdinalIgnoreCase)) return true;
        return LegacyIds.Any(x => string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase));
    }

    public bool MatchesCounterName(string counterName) =>
        PerfLuidTokens.Any(token => !string.IsNullOrWhiteSpace(token)
                                    && counterName.Contains(token, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => Name;
}

/// <summary>
/// Enumerates physical GPUs. DXGI can expose duplicate logical adapter entries on hybrid/
/// switchable graphics systems, so the raw DXGI list is first collapsed by hardware identity
/// and then reconciled with Win32_VideoController. Each physical GPU keeps every observed DXGI
/// LUID as a performance-counter alias, allowing usage/memory counters to stay accurate without
/// showing duplicate devices in the settings UI.
/// </summary>
public static class GpuAdapterCatalog
{
    private static readonly object Gate = new();
    private static DateTime _expiresAt = DateTime.MinValue;
    private static IReadOnlyList<GpuAdapterInfo> _cache = Array.Empty<GpuAdapterInfo>();

    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;
    private static readonly Guid IidDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public static IReadOnlyList<GpuAdapterInfo> GetAdapters(bool forceRefresh = false)
    {
        lock (Gate)
        {
            if (!forceRefresh && DateTime.UtcNow < _expiresAt && _cache.Count > 0)
                return _cache;

            var dxgi = EnumerateDxgiCandidates();
            var wmi = EnumerateWmiControllers();
            var physical = BuildPhysicalCatalog(dxgi, wmi);

            _cache = physical.Count > 0 ? physical : EnumerateWmiFallback(wmi);
            _expiresAt = DateTime.UtcNow.AddMinutes(5);
            return _cache;
        }
    }

    private static IReadOnlyList<GpuAdapterInfo> BuildPhysicalCatalog(
        IReadOnlyList<DxgiCandidate> dxgi,
        IReadOnlyList<WmiController> wmi)
    {
        if (dxgi.Count == 0) return Array.Empty<GpuAdapterInfo>();

        // Several DXGI logical entries can refer to the same physical adapter. Collapse them
        // before exposing them to the UI, while retaining all LUIDs for performance sampling.
        var groups = dxgi
            .GroupBy(x => x.HardwareKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DxgiGroup(g.ToList()))
            .ToList();

        var usedGroups = new HashSet<DxgiGroup>();
        var result = new List<GpuAdapterInfo>();

        foreach (var controller in wmi)
        {
            var matches = FindMatchingGroups(controller, groups, usedGroups);
            if (matches.Count == 0) continue;

            foreach (var match in matches)
                usedGroups.Add(match);

            // Merge every DXGI logical view that maps to this one WMI/PNP physical device.
            // This is the key fix for hybrid systems that previously showed the same NVIDIA GPU
            // two or three times in the selector.
            var mergedGroup = new DxgiGroup(matches.SelectMany(x => x.Candidates).ToList());
            result.Add(CreateAdapter(controller, mergedGroup));
        }

        // If WMI omits a valid active adapter, still expose the deduplicated DXGI device.
        foreach (var group in groups.Where(x => !usedGroups.Contains(x)))
            result.Add(CreateAdapter(null, group));

        // One final safety dedupe handles rare driver stacks that expose duplicate WMI rows.
        return result
            .GroupBy(x => PhysicalIdentityKey(x), StringComparer.OrdinalIgnoreCase)
            .Select(MergeAdapters)
            .OrderBy(x => x.PhysicalIndex < 0 ? int.MaxValue : x.PhysicalIndex)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string PhysicalIdentityKey(GpuAdapterInfo x)
    {
        // PNP IDs are the strongest stable identity. If unavailable, use the physical name plus
        // dedicated VRAM size. The latter intentionally collapses duplicate DXGI logical views.
        if (x.Id.StartsWith("pnp:", StringComparison.OrdinalIgnoreCase)) return x.Id;
        long mib = (long)Math.Round(x.DedicatedMemoryBytes / 1024d / 1024d);
        return $"gpu:{NormalizeName(x.Name)}:{mib}";
    }

    private static GpuAdapterInfo MergeAdapters(IGrouping<string, GpuAdapterInfo> group)
    {
        var first = group.First();
        var tokens = group.SelectMany(x => x.PerfLuidTokens)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var legacy = group.SelectMany(x => x.LegacyIds.Append(x.Id))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return first with
        {
            DedicatedMemoryBytes = group.Max(x => x.DedicatedMemoryBytes),
            SharedMemoryBytes = group.Max(x => x.SharedMemoryBytes),
            PerfLuidTokens = tokens,
            LegacyIds = legacy
        };
    }

    private static GpuAdapterInfo CreateAdapter(WmiController? controller, DxgiGroup group)
    {
        string name = !string.IsNullOrWhiteSpace(controller?.Name) ? controller!.Name : group.Name;
        string id = !string.IsNullOrWhiteSpace(controller?.PnpDeviceId)
            ? "pnp:" + controller!.PnpDeviceId
            : group.PrimaryId;

        // Prefer the larger dedicated-VRAM report. Some iGPU drivers report only a small
        // DXGI dedicated segment while Win32_VideoController exposes the configured reserved
        // framebuffer; dGPU DXGI values remain authoritative when WMI AdapterRAM truncates.
        double dedicated = Math.Max(
            Math.Max(0, group.DedicatedMemoryBytes),
            Math.Max(0, controller?.AdapterRamBytes ?? 0));

        var legacy = group.Candidates.Select(x => x.Id)
            .Concat(string.IsNullOrWhiteSpace(controller?.PnpDeviceId)
                ? Array.Empty<string>()
                : new[] { "pnp:" + controller!.PnpDeviceId })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GpuAdapterInfo(
            id,
            string.IsNullOrWhiteSpace(name) ? "GPU" : name,
            group.PhysicalIndex,
            dedicated,
            group.SharedMemoryBytes,
            group.PerfLuidTokens,
            legacy);
    }

    private static IReadOnlyList<DxgiGroup> FindMatchingGroups(
        WmiController controller,
        IReadOnlyList<DxgiGroup> groups,
        ISet<DxgiGroup> used)
    {
        var available = groups.Where(x => !used.Contains(x)).ToList();
        if (available.Count == 0) return Array.Empty<DxgiGroup>();

        if (controller.VendorId is not null && controller.DeviceId is not null)
        {
            var exact = available.Where(x =>
                x.VendorId == controller.VendorId
                && x.DeviceId == controller.DeviceId
                && (controller.SubSysId is null || x.SubSysId == 0 || x.SubSysId == controller.SubSysId))
                .ToList();
            if (exact.Count > 0) return exact;

            var vendorDevice = available.Where(x =>
                x.VendorId == controller.VendorId && x.DeviceId == controller.DeviceId)
                .ToList();
            if (vendorDevice.Count > 0) return vendorDevice;
        }

        string normalized = NormalizeName(controller.Name);
        var byName = available.Where(x => NormalizeName(x.Name) == normalized).ToList();
        if (byName.Count > 0) return byName;

        var fuzzy = available.Where(x =>
            NormalizeName(x.Name).Contains(normalized, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(NormalizeName(x.Name), StringComparison.OrdinalIgnoreCase))
            .ToList();
        return fuzzy;
    }

    private static IReadOnlyList<DxgiCandidate> EnumerateDxgiCandidates()
    {
        var result = new List<DxgiCandidate>();
        IntPtr factory = IntPtr.Zero;
        try
        {
            Guid iid = IidDxgiFactory1;
            int hr = CreateDXGIFactory1(ref iid, out factory);
            if (hr < 0 || factory == IntPtr.Zero)
                return result;

            var enumAdapters1 = GetVTableDelegate<EnumAdapters1Delegate>(factory, 12);
            for (uint ordinal = 0; ordinal < 32; ordinal++)
            {
                IntPtr adapter = IntPtr.Zero;
                hr = enumAdapters1(factory, ordinal, out adapter);
                if (hr == DxgiErrorNotFound) break;
                if (hr < 0 || adapter == IntPtr.Zero) continue;

                try
                {
                    var getDesc1 = GetVTableDelegate<GetDesc1Delegate>(adapter, 10);
                    hr = getDesc1(adapter, out var desc);
                    if (hr < 0 || (desc.Flags & DxgiAdapterFlagSoftware) != 0)
                        continue;

                    string name = (desc.Description ?? "GPU").TrimEnd('\0').Trim();
                    string luid = FormatLuid(desc.AdapterLuid);
                    int physicalIndex = ResolvePhysicalIndex(luid);
                    double dedicated = desc.DedicatedVideoMemory.ToUInt64();
                    double shared = desc.SharedSystemMemory.ToUInt64();
                    string id = $"dxgi:{luid}";

                    result.Add(new DxgiCandidate(
                        id,
                        string.IsNullOrWhiteSpace(name) ? $"GPU {ordinal}" : name,
                        (int)ordinal,
                        physicalIndex,
                        dedicated,
                        shared,
                        luid,
                        desc.VendorId,
                        desc.DeviceId,
                        desc.SubSysId,
                        desc.Revision));
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        catch
        {
            result.Clear();
        }
        finally
        {
            if (factory != IntPtr.Zero)
                Marshal.Release(factory);
        }

        return result;
    }

    private static IReadOnlyList<WmiController> EnumerateWmiControllers()
    {
        var result = new List<WmiController>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, AdapterRAM, ConfigManagerErrorCode FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
            {
                int errorCode = -1;
                try { errorCode = Convert.ToInt32(mo["ConfigManagerErrorCode"] ?? -1); }
                catch { }
                if (errorCode > 0) continue;

                string name = Convert.ToString(mo["Name"])?.Trim() ?? "GPU";
                string pnp = Convert.ToString(mo["PNPDeviceID"])?.Trim() ?? "";
                double adapterRam = ToDouble(mo["AdapterRAM"]);
                ParsePnpIds(pnp, out uint? vendor, out uint? device, out uint? subsys);
                result.Add(new WmiController(name, pnp, adapterRam, vendor, device, subsys));
            }
        }
        catch { }

        return result
            .GroupBy(x => string.IsNullOrWhiteSpace(x.PnpDeviceId)
                ? $"{NormalizeName(x.Name)}:{Math.Round(x.AdapterRamBytes / 1024d / 1024d)}"
                : x.PnpDeviceId,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static IReadOnlyList<GpuAdapterInfo> EnumerateWmiFallback(IReadOnlyList<WmiController> controllers)
    {
        var result = new List<GpuAdapterInfo>();
        var physicalIndices = ReadPhysicalIndices();
        int ordinal = 0;
        foreach (var controller in controllers)
        {
            int physicalIndex = ordinal < physicalIndices.Count ? physicalIndices[ordinal] : ordinal;
            string id = !string.IsNullOrWhiteSpace(controller.PnpDeviceId)
                ? "pnp:" + controller.PnpDeviceId
                : $"wmi:{ordinal}:{NormalizeName(controller.Name)}";
            result.Add(new GpuAdapterInfo(
                id,
                controller.Name,
                physicalIndex,
                Math.Max(0, controller.AdapterRamBytes),
                0,
                Array.Empty<string>(),
                new[] { id }));
            ordinal++;
        }

        if (result.Count == 0)
            result.Add(new GpuAdapterInfo("phys:0", "GPU 0", 0, 0, 0, Array.Empty<string>(), new[] { "phys:0" }));
        return result;
    }

    private static int ResolvePhysicalIndex(string luidToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
            foreach (ManagementObject mo in searcher.Get())
            {
                string name = Convert.ToString(mo["Name"]) ?? "";
                if (!name.Contains(luidToken, StringComparison.OrdinalIgnoreCase)) continue;
                var m = Regex.Match(name, @"(?:^|_)phys_(?<n>\d+)(?:_|$)", RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups["n"].Value, out int n))
                    return n;
            }
        }
        catch { }
        return -1;
    }

    private static List<int> ReadPhysicalIndices()
    {
        var set = new SortedSet<int>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
            foreach (ManagementObject mo in searcher.Get())
            {
                string name = Convert.ToString(mo["Name"]) ?? "";
                var m = Regex.Match(name, @"(?:^|_)phys_(?<n>\d+)(?:_|$)", RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups["n"].Value, out int n))
                    set.Add(n);
            }
        }
        catch { }
        return set.ToList();
    }

    private static void ParsePnpIds(string pnp, out uint? vendor, out uint? device, out uint? subsys)
    {
        vendor = ParsePnpHex(pnp, "VEN_", 4);
        device = ParsePnpHex(pnp, "DEV_", 4);
        subsys = ParsePnpHex(pnp, "SUBSYS_", 8);
    }

    private static uint? ParsePnpHex(string text, string prefix, int digits)
    {
        var m = Regex.Match(text ?? "", Regex.Escape(prefix) + $"(?<v>[0-9A-Fa-f]{{{digits}}})", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return uint.TryParse(m.Groups["v"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string NormalizeName(string? value) =>
        Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    private static string FormatLuid(Luid luid) =>
        $"luid_0x{unchecked((uint)luid.HighPart):x8}_0x{luid.LowPart:x8}";

    private static T GetVTableDelegate<T>(IntPtr comObject, int slot) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    private static double ToDouble(object? value)
    {
        try { return Convert.ToDouble(value ?? 0); }
        catch { return 0d; }
    }

    private sealed record DxgiCandidate(
        string Id,
        string Name,
        int Ordinal,
        int PhysicalIndex,
        double DedicatedMemoryBytes,
        double SharedMemoryBytes,
        string LuidToken,
        uint VendorId,
        uint DeviceId,
        uint SubSysId,
        uint Revision)
    {
        public string HardwareKey =>
            $"{VendorId:x4}:{DeviceId:x4}:{SubSysId:x8}:{Revision:x8}:{NormalizeName(Name)}:{Math.Round(DedicatedMemoryBytes / 1024d / 1024d)}";
    }

    private sealed class DxgiGroup
    {
        public DxgiGroup(List<DxgiCandidate> candidates)
        {
            Candidates = candidates;
            var first = candidates[0];
            Name = first.Name;
            VendorId = first.VendorId;
            DeviceId = first.DeviceId;
            SubSysId = first.SubSysId;
            DedicatedMemoryBytes = candidates.Max(x => x.DedicatedMemoryBytes);
            SharedMemoryBytes = candidates.Max(x => x.SharedMemoryBytes);
            PhysicalIndex = candidates.Where(x => x.PhysicalIndex >= 0).Select(x => x.PhysicalIndex).DefaultIfEmpty(first.Ordinal).Min();
            PerfLuidTokens = candidates.Select(x => x.LuidToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            PrimaryId = candidates[0].Id;
        }

        public List<DxgiCandidate> Candidates { get; }
        public string Name { get; }
        public uint VendorId { get; }
        public uint DeviceId { get; }
        public uint SubSysId { get; }
        public double DedicatedMemoryBytes { get; }
        public double SharedMemoryBytes { get; }
        public int PhysicalIndex { get; }
        public IReadOnlyList<string> PerfLuidTokens { get; }
        public string PrimaryId { get; }
    }

    private sealed record WmiController(
        string Name,
        string PnpDeviceId,
        double AdapterRamBytes,
        uint? VendorId,
        uint? DeviceId,
        uint? SubSysId);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr @this, uint adapter, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr @this, out DxgiAdapterDesc1 desc);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }
}
