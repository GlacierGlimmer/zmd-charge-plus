using System;

namespace EndfieldChargePlus.Customization;

public static class BuiltInProfileLocalization
{
    public static HudProfile ForCurrentLanguage(HudProfile profile) =>
        ForLanguage(profile, LocalizationManager.Current);

    public static HudProfile ForLanguage(HudProfile profile, AppLanguage language)
    {
        if (language != AppLanguage.English || string.IsNullOrWhiteSpace(profile.BuiltInKey))
            return profile;

        return profile.BuiltInKey.ToLowerInvariant() switch
        {
            "system.battery" => profile with
            {
                Category = "System", Name = "Battery", TitleTemplate = "Battery"
            },
            "system.cpu" => profile with
            {
                Category = "System", Name = "CPU", TitleTemplate = "CPU"
            },
            "system.memory" => profile with
            {
                Category = "System", Name = "Memory", TitleTemplate = "Memory"
            },
            "system.gpu" => profile with
            {
                Category = "System", Name = "GPU", TitleTemplate = "GPU"
            },
            "system.network" => profile with
            {
                Category = "System", Name = "Network", TitleTemplate = "Network"
            },
            "system.disk" => profile with
            {
                Category = "System", Name = "System Disk", TitleTemplate = "System Disk"
            },
            "time.day-progress" => profile with
            {
                Category = "Time", Name = "Day Progress", TitleTemplate = "Day Progress"
            },
            "deepseek.balance-period" => profile with
            {
                Category = "DeepSeek API", Name = "Balance / Period",
                TitleTemplate = "DeepSeek {deepseek.period.name}"
            },
            "network.ping" => profile with
            {
                Category = "Network", Name = "Packet Probe", TitleTemplate = "Packet Probe",
                RightTemplate = "Loss {probe.loss_percent|0}"
            },
            _ => profile,
        };
    }

    public static string DisplayName(HudProfile profile)
    {
        if (profile.IsBuiltIn || !string.IsNullOrWhiteSpace(profile.BuiltInKey))
        {
            var p = ForCurrentLanguage(profile);
            return $"{p.Category} - {p.Name}";
        }

        return string.IsNullOrWhiteSpace(profile.Name)
            ? LocalizationManager.Text("自定义方案", "Custom Profile")
            : profile.Name;
    }
}
