namespace EndfieldChargePlus.Settings;

internal static class WorkspaceManualContent
{
    internal static IReadOnlyList<ManualChapter> Adapt(IReadOnlyList<ManualChapter> chapters)
    {
        ManualText Text(string key)=>new(key,key,WorkspaceStrings.Get(key));
        ManualBlock Block(string id,string key,string body,string target)=>new(id,Text(key),new(body,body,body),Kind:"steps",Target:target);
        var result=chapters.ToList();
        string keyboardHelp=LocalizationManager.Text("支持键盘和鼠标按键，可搭配 Ctrl、Alt、Shift 或 Win/Command。系统保留按键可能不可用。","Keyboard and mouse buttons are supported, with optional Ctrl, Alt, Shift or Win/Command. System-reserved keys may be unavailable.");
        string captureHelp=LocalizationManager.Text("按下键盘或鼠标按键；Esc 清除此绑定。","Press a key or mouse button. Esc clears the binding.");
        for(int i=0;i<result.Count;i++)if(result[i].Id=="summons")result[i]=result[i] with{Blocks=result[i].Blocks.Select(b=>b.Id=="shortcut"?b with{Body=new ManualText("","",keyboardHelp+"\n"+captureHelp+"\n"+WorkspaceStrings.Get("Your changes apply after Save & Apply.")),Target="quick"}:b).ToArray()};
        result.Add(new("workspace",Text("Quick actions"),Text("Your changes apply after Save & Apply."),new[]{
            Block("guide","Radial switcher",WorkspaceStrings.Get("Hold the middle mouse button on the HUD, drag to a profile, then release to switch.")+"\n"+WorkspaceStrings.Get("Release in the centre or outside the ring to cancel.")+"\n"+WorkspaceStrings.Get("Press Esc to cancel."),"ring"),
            Block("gesture","HUD gestures",WorkspaceStrings.Get("Hold delay")+" / ms · "+WorkspaceStrings.Get("Drag threshold")+" / px · "+WorkspaceStrings.Get("Ring size")+" / px","ring"),
            Block("wheel","Auto Cycle",LocalizationManager.Text("点击或滚轮切换方案","Click or scroll to switch profiles")+"\n"+LocalizationManager.Text("点击向下切换下一方案；向上滚动向上切换上一方案，向下滚动向下切换下一方案。","Click switches to the next profile with a downward roll. Scroll up for the previous profile and down for the next, using the same animation direction.")+"\n"+string.Join(" / ",new[]{LocalizationManager.Text("完整","Full"),LocalizationManager.Text("简洁","Simple"),LocalizationManager.Text("向上翻滚","Roll Up"),LocalizationManager.Text("向下翻滚","Roll Down")}),"cycle"),
            Block("shortcuts","Quick actions",string.Join("\n",EndfieldChargePlus.Interop.HudQuickHotkeys.Actions.Select(a=>LocalizationManager.Text(a.Zh,a.En)))+"\n"+WorkspaceStrings.Get("Your changes apply after Save & Apply."),"quick"),
        }));
        return result;
    }
    internal static string UpdateNavigationLabels(string value)
    {
        string profile=WorkspaceStrings.Get("Profile management");
        string hud=LocalizationManager.Text("HUD显示内容","HUD Content"),configuration=LocalizationManager.Text("HUD 配置","HUD Configuration");
        foreach(var path in new[]{hud+" → "+configuration,hud+"→"+configuration,"HUD显示内容 → HUD 配置","HUD Content → HUD Configuration"})value=value.Replace(path,profile,StringComparison.Ordinal);
        string rules=LocalizationManager.Text("触发规则","Trigger Rules");
        foreach(var path in new[]{hud+" → "+rules,"HUD显示内容 → 触发规则","HUD Content → Trigger Rules"})value=value.Replace(path,rules,StringComparison.Ordinal);
        return value;
    }
}
