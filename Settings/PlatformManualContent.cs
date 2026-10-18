using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Settings;

/// <summary>Keep native capability notes separate from shared UI and template instructions.</summary>
internal static class PlatformManualContent
{
    private static ManualText T(string zh,string en)=>new(zh,en);
    internal static IReadOnlyList<ManualChapter> Adapt(IReadOnlyList<ManualChapter> chapters)
    {
        bool native=!OperatingSystem.IsWindows();
        string platform=OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        return chapters.Select(chapter=>
        {
            if(chapter.Id=="display")return chapter with {Blocks=chapter.Blocks.Concat(new[]{new ManualBlock("global-appearance",
                T("全局字体大小和UI缩放","Global font size and UI scale"),
                T("Language → 全局字体大小和UI缩放。应用于整个设置界面和所有 HUD。单个 HUD 的大小设置会与全局 UI 缩放相乘。",
                    "Applies to all settings and every HUD. Individual HUD size is multiplied by global UI scale."))}).ToArray()};
            if(!native)return chapter;
            if(chapter.Id=="hardware")return chapter with {Blocks=new[]{new ManualBlock("guide",T("硬件数据","Hardware Data"),
                new("","",LocalizationManager.Text("使用系统原生硬件接口。数据取决于硬件、驱动和当前权限；不需要 PawnIO。",
                    "Using native system hardware interfaces. Available data depends on hardware, drivers and permissions; PawnIO is not required.")+"\n"+
                    LocalizationManager.Text("显示系统和驱动实际提供的硬件读数。未公开或无权限读取的传感器显示 --；可导出本地诊断。PawnIO 和 AIDA64 接口仅适用于 Windows。",
                    "Shows hardware readings provided by the system and drivers. Sensors that are not exposed or accessible show --. Export diagnostics for details. PawnIO and the AIDA64 interface are Windows-only.")),Target:"hardware")}};
            if(chapter.Id=="media")return chapter with {Summary=T("媒体数据当前不可用","Unavailable"),Blocks=new[]{new ManualBlock("guide",T("媒体","Media"),
                T("此平台的当前构建尚未提供原生媒体会话数据源，媒体字段会保持不可用。",
                    "This build has no native media-session data source on this platform. Media fields remain unavailable."),Target:"variables")}};
            if(chapter.Id is not ("sources" or "maintenance"))return chapter;
            ManualText Clean(ManualText text)
            {
                string value=text.Value.Replace("Windows",platform,StringComparison.Ordinal);
                if(chapter.Id=="sources")value=Regex.Replace(value,@"[^\n.!?。]*AIDA64[^\n.!?。]*[.!?。]?","").Trim();
                return text with {Localized=value};
            }
            return chapter with {Summary=chapter.Id=="sources" ? T("数据源配置","Data Source Configuration") : Clean(chapter.Summary),Blocks=chapter.Blocks.Where(b=>b.Id!="aida64").Select(b=>b with {Body=Clean(b.Body)}).ToArray()};
        }).ToArray();
    }
}
