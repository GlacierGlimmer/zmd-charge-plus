namespace EndfieldChargePlus.Settings;

internal static class WorkspaceManualContent
{
    internal static IReadOnlyList<ManualChapter> Adapt(IReadOnlyList<ManualChapter> chapters)
    {
        ManualText Text(string key)=>new(key,key,WorkspaceStrings.Get(key));
        ManualBlock Block(string id,string key,string body,string target)=>new(id,Text(key),new(body,body,body),Kind:"steps",Target:target);
        var result=chapters.ToList();
        result.Add(new("workspace",Text("Quick actions"),Text("Your changes apply after Save & Apply."),new[]{
            Block("guide","Radial switcher",WorkspaceStrings.Get("Hold the middle mouse button on the HUD, drag to a profile, then release to switch.")+"\n"+WorkspaceStrings.Get("Release in the centre or outside the ring to cancel.")+"\n"+WorkspaceStrings.Get("Press Esc to cancel."),"ring"),
            Block("gesture","HUD gestures",WorkspaceStrings.Get("Hold delay")+" / ms · "+WorkspaceStrings.Get("Drag threshold")+" / px · "+WorkspaceStrings.Get("Ring size")+" / px","quick"),
            Block("wheel","Auto Cycle",LocalizationManager.Text("点击或滚轮切换方案","Click or scroll to switch profiles")+"\n"+LocalizationManager.Text("向上滚动切换上一项，向下滚动切换下一项。","Scroll up for previous; scroll down for next."),"cycle"),
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
