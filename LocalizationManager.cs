using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace EndfieldChargePlus;

public enum AppLanguage
{
    SimplifiedChinese,
    English,
}

/// <summary>
/// Runtime UI/HUD language state. "Auto" follows the Windows UI culture; all zh-* cultures
/// resolve to Simplified Chinese, everything else resolves to English.
/// </summary>
public static class LocalizationManager
{
    private static readonly Dictionary<string, string> ZhToEn = new(StringComparer.Ordinal)
    {
        // Settings shell
        ["Endfield Charge Plus 设置"] = "Endfield Charge Plus Settings",
        ["配置文件夹"] = "Config Folder",
        ["保存并应用"] = "Save & Apply",
        ["显示与位置"] = "Display & Position",
        ["HUD 内容与数据"] = "HUD Content & Data",
        ["关于"] = "About",
        ["HUD 总开关"] = "HUD Master Switch",
        ["控制 HUD 的全部显示功能。"] = "Controls all HUD display functions.",
        ["开机启动"] = "Start with Windows",
        ["登录 Windows 后自动启动 Endfield Charge Plus。"] = "Start Endfield Charge Plus after Windows sign-in.",
        ["显示方式"] = "Display Mode",
        ["一直显示"] = "Always Visible",
        ["开启：HUD 持续显示。\n关闭：电源插拔显示电池；鼠标移到目标屏幕顶部中央可唤出当前方案。"] = "On: keep the HUD visible.\nOff: power changes show Battery; move the pointer to the top-center of the target display to summon the active profile.",
        ["常驻状态"] = "Persistent",
        ["显示层级"] = "Layer",
        ["HUD 不透明度"] = "HUD Opacity",
        ["100% 为完全不透明。"] = "100% is fully opaque.",
        ["位置"] = "Position",
        ["目标显示器"] = "Target Display",
        ["定位方式"] = "Positioning",
        ["预设位置"] = "Preset",
        ["X 微调 / px"] = "X Offset / px",
        ["Y 微调 / px"] = "Y Offset / px",
        ["X 向右为正，Y 向下为正。"] = "+X right, +Y down.",
        ["X 坐标 / px"] = "X / px",
        ["Y 坐标 / px"] = "Y / px",
        ["尺寸与动画"] = "Size & Animation",
        ["恢复默认"] = "Reset",
        ["HUD 缩放"] = "HUD Scale",
        ["总时长 / 秒"] = "Duration / s",
        ["回弹强度"] = "Bounce",
        ["波纹强度"] = "Ripple Strength",
        ["波纹幅度"] = "Ripple Spread",
        ["终末地风格状态栏 HUD"] = "Endfield-style Status HUD",
        ["构建日期  2026.09.23"] = "Build  2026.09.23",
        ["检查更新"] = "Check Updates",
        ["当前版本：v0.1.0"] = "Current: v0.1.0",
        ["最新版本：尚未获取"] = "Latest: not checked",
        ["状态：尚未检查"] = "Status: not checked",
        ["项目与协议"] = "Project & License",
        ["开源协议"] = "License",
        ["本项目 GitHub"] = "Project GitHub",
        ["原项目 GitHub"] = "Upstream GitHub",
        ["项目网站"] = "Website",
        ["打开"] = "Open",
        ["配置与维护"] = "Config & Maintenance",
        ["导出配置"] = "Export",
        ["导入配置"] = "Import",
        ["备份当前配置"] = "Backup",
        ["日志文件夹"] = "Logs",
        ["恢复全部默认"] = "Reset All",

        // HUD customizer - profile tab
        ["HUD 配置"] = "HUD Profiles",
        ["方案管理"] = "Profile Management",
        ["选择当前显示方案。内置方案修改后会另存为新方案，原预设保持不变。"] = "Select the active HUD profile. Editing a built-in profile creates a copy; the original stays unchanged.",
        ["预览当前方案"] = "Preview",
        ["当前方案"] = "Active Profile",
        ["新建"] = "New",
        ["保存"] = "Save",
        ["删除"] = "Delete",
        ["DeepSeek API 数据源"] = "DeepSeek API Source",
        ["此方案需要先在“数据源 → DeepSeek API”中配置 DeepSeek API Key；峰谷时段也可在该数据源中设置。未配置 API Key 时，余额相关数据无法获取。"] = "Configure the DeepSeek API key under Data Sources → DeepSeek API first. Peak/off-peak windows are set there too. Balance data is unavailable without a key.",
        ["目标时间模式"] = "Target-time Mode",
        ["关闭时显示当天已过进度；开启后显示距离下一个每日目标时间的剩余比例。"] = "Off: show today's elapsed progress. On: show the remaining share until the next daily target time.",
        ["每日目标时间"] = "Daily Target",
        ["GPU 设备"] = "GPU Device",
        ["检测到多个 GPU 时，选择此方案要显示的图形处理器。"] = "Choose the GPU used by this profile when multiple adapters are detected.",
        ["网络显示单位"] = "Network Unit",
        ["Mbps：按兆比特每秒显示；KB/s / MB/s：根据速度大小自动切换。"] = "Mbps uses megabits/s; KB/s / MB/s switches automatically by rate.",
        ["百分比计算方式"] = "Percent Basis",
        ["使用系统全部有效网络接口的流量。可按总吞吐量、仅下载、仅上传或上下行较大值计算右侧百分比。"] = "Uses all active interfaces. Calculate the right-side percentage from total traffic, download, upload, or the larger direction.",
        ["100% 对应网络速度"] = "100% Reference Speed",
        ["当前所选百分比模式的速度达到这里设定的值时显示 100%；计算前统一换算为 Byte/s。"] = "The selected rate reaches 100% at this value. Rates are normalized to Byte/s before calculation.",
        ["数值"] = "Value",
        ["单位"] = "Unit",
        ["检测地址"] = "Probe Target",
        ["支持 IPv4、IPv6 或域名。"] = "IPv4, IPv6, or hostname.",
        ["例如：1.1.1.1、2606:4700:4700::1111 或 example.com"] = "e.g. 1.1.1.1, 2606:4700:4700::1111, or example.com",
        ["检测协议"] = "Protocol",
        ["ICMP 使用标准 Ping；TCP 测量端口连接延迟；UDP 会发送探测数据并等待目标服务响应。"] = "ICMP uses Ping; TCP measures connect latency; UDP sends probe data and waits for a service response.",
        ["检测端口"] = "Port",
        ["TCP / UDP 使用该端口；ICMP 不使用端口。"] = "Used by TCP / UDP; ignored by ICMP.",
        ["方案名称"] = "Profile Name",
        ["例如：我的服务器 - 在线状态"] = "e.g. My Server - Online",
        ["动画模式"] = "Animation",
        ["简洁：直接展开最终 HUD，适合频繁查看。"] = "Simple: quickly reveal the final HUD.",
        ["完整：包含标题、波纹与形态切换，再进入最终 HUD。"] = "Full: title, ripple, and shape transition before the final HUD.",
        ["选择框中的方案即当前使用方案；始终保持一个方案被选中。"] = "The selected profile is the active profile; one profile is always selected.",
        ["自动轮播"] = "Auto Cycle",
        ["开启后按照下方轮播队列的顺序循环切换方案。"] = "Cycle profiles in the queue below.",
        ["轮播队列"] = "Cycle Queue",
        ["从上到下依次切换，到达末尾后回到第一项。队列只引用当前存在的方案。"] = "Cycles top to bottom, then returns to the first item. The queue only references existing profiles.",
        ["轮播间隔 / 秒"] = "Interval / s",
        ["轮播动画"] = "Cycle Animation",
        ["开启自动轮播后，以这里的动画为准，忽略各方案自身的动画模式。"] = "When auto cycle is enabled, this animation overrides each profile's own animation setting.",
        ["简洁：收回上一项后，快速唤出下一项。"] = "Simple: retract the previous item, then quickly reveal the next.",
        ["完整：收回上一项后，以完整标题、波纹与形态动画唤出下一项。"] = "Full: retract the previous item, then reveal the next with the full title/ripple sequence.",
        ["添加"] = "Add",
        ["上移"] = "Up",
        ["下移"] = "Down",
        ["显示内容"] = "Display Content",
        ["左侧显示主体信息，右侧显示百分比、进度或状态类数据。"] = "Main information appears on the left; percentage, progress, or status appears on the right.",
        ["标题阶段"] = "Title Stage",
        ["上行文字"] = "Tagline",
        ["主标题"] = "Title",
        ["系统状态"] = "System Status",
        ["左侧主体信息"] = "Left Main Info",
        ["容量、当前频率、速率、余额、当前时间等主要信息。"] = "Capacity, frequency, rate, balance, time, and other primary values.",
        ["主值"] = "Primary",
        ["次值 / 补充文字"] = "Secondary / Note",
        ["右侧状态值"] = "Right Status",
        ["使用率、剩余比例、已过进度等状态信息，并可与圆环联动。"] = "Usage, remaining share, elapsed progress, and other status values; can drive the progress ring.",
        ["状态值"] = "Status",
        ["后缀"] = "Suffix",
        ["{}模板支持 {变量|格式}；高级表达式使用 {= 表达式 | 格式}，支持 + - * / % ^、比较、&& / || / !、?:、??、if()、min/max/avg/sum/clamp/round 等；进度变量也可填写 = 表达式。例：{= if(memory.usage >= 80, '高', '正常')}。"] = "Templates use {variable|format}. Advanced expressions use {= expression | format}; supported operators include + - * / % ^, comparisons, && / || / !, ?:, ??, and functions such as if(), min/max/avg/sum/clamp/round. Progress may also be an = expression. Example: {= if(memory.usage >= 80, 'High', 'OK')}.",
        ["图标与进度环"] = "Icons & Progress Ring",
        ["进度变量"] = "Progress Value",
        ["cpu.usage 或 = (cpu.usage + gpu.usage) / 2"] = "cpu.usage or = (cpu.usage + gpu.usage) / 2",
        ["最小值"] = "Minimum",
        ["最大值"] = "Maximum",
        ["左侧图标"] = "Left Icon",
        ["右侧图标"] = "Right Icon",
        ["颜色"] = "Color",
        ["默认强调色"] = "Accent Color",
        ["条件变色 · 每行：变量 运算符 数值 => 颜色"] = "Color rules · one per line: variable operator value => color",

        // Variable library
        ["变量库"] = "Variables",
        ["按名称、变量名或分类查找，选择变量后查看完整说明。"] = "Search by name, key, or category; select a variable for details.",
        ["按功能分类；分类内优先显示常用状态与实时指标，再显示详细信息。支持按名称、变量名或分类查找。"] = "Grouped by function; common status and live metrics come first, followed by detailed values. Search by name, key, or category.",
        ["搜索：CPU 使用率 / cpu.usage"] = "Search: CPU usage / cpu.usage",
        ["选择一个变量"] = "Select a variable",
        ["从左侧列表选择变量后，这里会显示变量含义和使用建议。"] = "Select a variable on the left to view its meaning and usage guidance.",
        ["变量名"] = "Variable Key",
        ["复制变量名"] = "Copy Key",
        ["模板写法"] = "Template",
        ["复制模板"] = "Copy Template",
        ["数据属性"] = "Data Properties",
        ["类型"] = "Type",
        ["使用建议"] = "Recommended Use",
        ["常用格式"] = "Formats",

        // Data sources
        ["数据源"] = "Data Sources",
        ["配置余额查询与峰谷时段。API Key 使用 Windows 当前用户加密存储。"] = "Configure balance queries and peak/off-peak windows. The API key is encrypted for the current Windows user.",
        ["工作日高峰窗口（北京时间，分号分隔）"] = "Weekday peak windows (Beijing time; separate with semicolons)",
        ["将 GET JSON 字段映射为 custom.source.variable；Header 可引用环境变量。"] = "Map GET JSON fields to custom.source.variable; headers may reference environment variables.",

        // Tray
        ["预览 HUD"] = "Preview HUD",
        ["设置"] = "Settings",
        ["退出"] = "Exit",
    };

    private static readonly Dictionary<string, string> EnToZh = ZhToEn.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    public static AppLanguage Current { get; private set; } = AppLanguage.English;
    public static bool IsEnglish => Current == AppLanguage.English;
    public static event Action? LanguageChanged;

    public static void Initialize(string? preference)
    {
        string normalized = NormalizePreference(preference);
        Current = normalized switch
        {
            "zh-CN" => AppLanguage.SimplifiedChinese,
            "en-US" => AppLanguage.English,
            _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.SimplifiedChinese
                : AppLanguage.English,
        };
    }

    public static string NormalizePreference(string? preference)
    {
        if (string.Equals(preference, "zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (string.Equals(preference, "en-US", StringComparison.OrdinalIgnoreCase)) return "en-US";
        return "Auto";
    }

    public static string PreferenceFor(AppLanguage language) => language == AppLanguage.English ? "en-US" : "zh-CN";

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language) return;
        Current = language;
        LanguageChanged?.Invoke();
    }

    public static string Text(string zh, string en) => IsEnglish ? en : zh;

    public static string TranslateLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (IsEnglish)
            return ZhToEn.TryGetValue(value, out var en) ? en : value;
        return EnToZh.TryGetValue(value, out var zh) ? zh : value;
    }

    public static void ApplyStaticText(Control root)
    {
        IEnumerable<object> nodes = root.GetLogicalDescendants().Cast<object>().Prepend(root);
        foreach (object node in nodes)
        {
            switch (node)
            {
                case Window window:
                    window.Title = TranslateLiteral(window.Title);
                    break;
                case TabItem tab when tab.Header is string header:
                    tab.Header = TranslateLiteral(header);
                    break;
                case ToggleSwitch toggle:
                    if (toggle.OnContent is string on) toggle.OnContent = TranslateLiteral(on);
                    if (toggle.OffContent is string off) toggle.OffContent = TranslateLiteral(off);
                    break;
                case TextBox box:
                    if (box.Watermark is string watermark) box.Watermark = TranslateLiteral(watermark);
                    break;
                case TextBlock text when text.Text is not null:
                    text.Text = TranslateLiteral(text.Text);
                    break;
                case ContentControl content when content.Content is string s:
                    content.Content = TranslateLiteral(s);
                    break;
            }
        }
    }
}
