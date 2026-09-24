using System;
using System.Collections.Generic;
using System.Linq;

namespace EndfieldChargePlus.Customization;

public static class HudSettingsNormalizer
{
    public static CustomHudSettings Normalize(CustomHudSettings? settings)
    {
        var source = settings ?? CustomHudSettings.CreateDefault();
        var sourceProfiles = source.Profiles?.Select(CloneProfile).ToList() ?? new List<HudProfile>();
        var defaults = CustomHudSettings.CreateDefaultProfiles();

        var normalized = new List<HudProfile>();
        var consumedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Built-ins are canonical and immutable. Preserve a legacy profile ID when possible so
        // the last selected scheme survives upgrades.
        foreach (var builtin in defaults)
        {
            HudProfile? existing = sourceProfiles.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.BuiltInKey)
                && string.Equals(p.BuiltInKey, builtin.BuiltInKey, StringComparison.OrdinalIgnoreCase));

            existing ??= sourceProfiles.FirstOrDefault(p =>
                string.IsNullOrWhiteSpace(p.BuiltInKey)
                && !consumedIds.Contains(p.Id)
                && IsLegacyMatch(p, builtin));

            var canonical = CloneProfile(builtin) with
            {
                Id = existing?.Id ?? builtin.Id,
                IsBuiltIn = true,
                BuiltInKey = builtin.BuiltInKey,
                // The built-in time scheme keeps its dedicated user option while all
                // structural/template fields remain canonical and read-only.
                TimeTargetEnabled = string.Equals(builtin.BuiltInKey, "time.day-progress", StringComparison.OrdinalIgnoreCase)
                    ? existing?.TimeTargetEnabled ?? builtin.TimeTargetEnabled
                    : builtin.TimeTargetEnabled,
                TimeTarget = string.Equals(builtin.BuiltInKey, "time.day-progress", StringComparison.OrdinalIgnoreCase)
                    ? NormalizeTargetTime(existing?.TimeTarget ?? builtin.TimeTarget)
                    : builtin.TimeTarget,
                GpuAdapterId = string.Equals(builtin.BuiltInKey, "system.gpu", StringComparison.OrdinalIgnoreCase)
                    ? existing?.GpuAdapterId ?? builtin.GpuAdapterId
                    : builtin.GpuAdapterId
            };

            normalized.Add(canonical);
            if (existing is not null)
                consumedIds.Add(existing.Id);
        }

        // Keep every user-created scheme editable, even when it shares a category with a built-in.
        foreach (var profile in sourceProfiles)
        {
            if (consumedIds.Contains(profile.Id)) continue;
            if (profile.IsBuiltIn || !string.IsNullOrWhiteSpace(profile.BuiltInKey)) continue;
            normalized.Add(NormalizeCustomProfile(profile with { IsBuiltIn = false, BuiltInKey = "" }));
        }

        string activeId = source.ActiveProfileId;
        if (string.IsNullOrWhiteSpace(activeId) || normalized.All(p => !string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase)))
            activeId = normalized.FirstOrDefault()?.Id ?? "";

        // Cycle queue is identity-based, so renaming a custom scheme does not break its
        // position. Missing/deleted schemes are removed automatically. A null queue means
        // this is a legacy settings file; migrate the old "all profiles in list order"
        // behaviour exactly once. An explicit empty list remains empty.
        var validIds = normalized.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> cycleIds;
        if (source.CycleProfileIds is null)
        {
            cycleIds = normalized.Select(p => p.Id).ToList();
        }
        else
        {
            cycleIds = source.CycleProfileIds
                .Where(id => !string.IsNullOrWhiteSpace(id) && validIds.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        string cycleAnimationMode = string.Equals(source.CycleAnimationMode, "Simple", StringComparison.OrdinalIgnoreCase)
            ? "Simple"
            : "Full";

        return source with
        {
            Profiles = normalized,
            ActiveProfileId = activeId,
            CycleSeconds = Math.Clamp(source.CycleSeconds, 3, 3600),
            CycleProfileIds = cycleIds,
            CycleAnimationMode = cycleAnimationMode
        };
    }

    public static HudProfile NormalizeProfile(HudProfile p)
    {
        if (p.IsBuiltIn || !string.IsNullOrWhiteSpace(p.BuiltInKey))
        {
            var builtin = CustomHudSettings.CreateDefaultProfiles().FirstOrDefault(x =>
                string.Equals(x.BuiltInKey, p.BuiltInKey, StringComparison.OrdinalIgnoreCase));
            if (builtin is not null)
                return CloneProfile(builtin) with
                {
                    Id = p.Id,
                    IsBuiltIn = true,
                    TimeTargetEnabled = string.Equals(builtin.BuiltInKey, "time.day-progress", StringComparison.OrdinalIgnoreCase)
                        ? p.TimeTargetEnabled
                        : builtin.TimeTargetEnabled,
                    TimeTarget = string.Equals(builtin.BuiltInKey, "time.day-progress", StringComparison.OrdinalIgnoreCase)
                        ? NormalizeTargetTime(p.TimeTarget)
                        : builtin.TimeTarget,
                    GpuAdapterId = string.Equals(builtin.BuiltInKey, "system.gpu", StringComparison.OrdinalIgnoreCase)
                        ? p.GpuAdapterId
                        : builtin.GpuAdapterId
                };
        }

        return NormalizeCustomProfile(p);
    }

    private static HudProfile NormalizeCustomProfile(HudProfile p)
    {
        string originalCategory = string.IsNullOrWhiteSpace(p.Category) ? "自定义" : p.Category.Trim();
        string originalName = string.IsNullOrWhiteSpace(p.Name) ? "自定义 HUD" : p.Name.Trim();
        // Legacy settings may contain a two-level custom category/name. Fold those values
        // once so users do not lose the meaning of older configurations.
        string name = string.Equals(originalCategory, "自定义", StringComparison.OrdinalIgnoreCase)
            ? originalName
            : $"{originalCategory} - {originalName}";

        return p with
        {
            IsBuiltIn = false,
            BuiltInKey = "",
            Category = "自定义",
            Name = name,
            TimeTarget = NormalizeTargetTime(p.TimeTarget),
            PingTarget = NormalizePingTarget(p.PingTarget),
            ProbeProtocol = NormalizeProbeProtocol(p.ProbeProtocol),
            ProbePort = NormalizeProbePort(p.ProbePort),
            ColorRules = p.ColorRules?.Select(x => x with { }).ToList() ?? new List<HudColorRule>()
        };
    }

    private static bool IsLegacyMatch(HudProfile candidate, HudProfile builtin) =>
        string.Equals(candidate.Category, builtin.Category, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Name, builtin.Name, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTargetTime(string? value)
    {
        if (TimeSpan.TryParse(value, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        return "10:00:00";
    }


    private static string NormalizePingTarget(string? value)
    {
        string target = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(target) ? "1.1.1.1" : target;
    }

    private static string NormalizeProbeProtocol(string? value)
    {
        string protocol = (value ?? "ICMP").Trim().ToUpperInvariant();
        return protocol is "TCP" or "UDP" ? protocol : "ICMP";
    }

    private static int NormalizeProbePort(int value) => Math.Clamp(value <= 0 ? 443 : value, 1, 65535);

    private static HudProfile CloneProfile(HudProfile p) =>
        p with { ColorRules = p.ColorRules?.Select(x => x with { }).ToList() ?? new List<HudColorRule>() };
}
