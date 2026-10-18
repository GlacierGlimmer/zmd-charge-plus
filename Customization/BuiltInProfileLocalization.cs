using System;

namespace EndfieldChargePlus.Customization;

public static class BuiltInProfileLocalization
{
    public static HudProfile ForCurrentLanguage(HudProfile profile) =>
        ForLanguage(profile, LocalizationManager.Current);

    public static HudProfile ForLanguage(HudProfile profile, AppLanguage language)
    {
        var canonical=Canonical(profile,language==AppLanguage.SimplifiedChinese ? AppLanguage.SimplifiedChinese : AppLanguage.English);
        if(language is AppLanguage.SimplifiedChinese or AppLanguage.English || string.IsNullOrWhiteSpace(profile.BuiltInKey))return canonical;
        string T(string value)=>LocalizationManager.TranslateEnglish(value,language);
        string name=profile.BuiltInKey is "system.display-fps" or "system.window-fps"
            ? T(profile.BuiltInKey=="system.display-fps" ? "Display FPS" : "Window FPS")+" ("+T("Run as administrator")+")" : T(canonical.Name);
        string title=T(canonical.TitleTemplate);
        if(profile.BuiltInKey=="deepseek.balance-period")title=title.Replace("{deepseek.period.name}","{deepseek.period.name_zh}",StringComparison.Ordinal);
        return canonical with {
            Category=T(canonical.Category),Name=name,TaglineTemplate=T(canonical.TaglineTemplate),TitleTemplate=title,
            PrimaryTemplate=T(canonical.PrimaryTemplate),SecondaryTemplate=T(canonical.SecondaryTemplate),RightTemplate=T(canonical.RightTemplate),RightSuffix=T(canonical.RightSuffix)};
    }

    private static HudProfile Canonical(HudProfile profile,AppLanguage language)
    {
        if (profile.BuiltInKey is "system.display-fps" or "system.window-fps")
        {
            bool display=profile.BuiltInKey=="system.display-fps",english=language==AppLanguage.English;
            string name=english ? (display ? "Display FPS" : "Window FPS") : (display ? "显示器帧率" : "窗口帧率");
            return profile with { Category=english ? "System" : "系统",Name=name+(english ? " (Run as administrator required)" : "（需管理员身份运行）"),
                TitleTemplate=english ? name : (display ? "显示器帧率" : "窗口帧率") };
        }
        if (language != AppLanguage.English || string.IsNullOrWhiteSpace(profile.BuiltInKey))
            return profile;

        return profile.BuiltInKey.ToLowerInvariant() switch
        {
            "system.display-fps" => profile with { Category="System",Name="Display FPS",TitleTemplate="Display FPS" },
            "system.window-fps" => profile with { Category="System",Name="Window FPS",TitleTemplate="Window FPS" },
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
            "time.world-clock" => profile with { Category="Time",Name="World Clock",TitleTemplate="World Clock" },
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
