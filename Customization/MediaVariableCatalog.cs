namespace EndfieldChargePlus.Customization;

internal static class MediaVariableCatalog
{
    private static readonly (string Key,string Zh,string En,string Type,string Unit)[] Items = {
        ("available","有媒体会话","Media available","布尔",""),("app","媒体来源应用","Media source app","文本",""),
        ("title","曲名","Track title","文本",""),("artist","歌手","Artist","文本",""),("album","专辑","Album","文本",""),
        ("title_artist","歌手 - 曲名","Artist - title","文本",""),("status","播放状态","Playback status","文本",""),
        ("status_raw","原始播放状态","Raw playback status","文本",""),("is_playing","正在播放","Is playing","布尔",""),
        ("is_paused","已暂停","Is paused","布尔",""),("position","已播放秒数","Playback position","数值","秒"),
        ("duration","曲目总秒数","Track duration","数值","秒"),("progress","播放进度","Playback progress","数值","%"),
        ("position_text","已播放时间","Playback position text","文本",""),("duration_text","总时长","Track duration text","文本",""),
        ("remaining_text","剩余时间","Time remaining text","文本",""),("progress_text","播放进度文字","Playback progress text","文本",""),
        ("has_timeline","有进度信息","Timeline available","布尔","") };
    internal static IEnumerable<VariableDefinition> All=>Items.Select(i=>new VariableDefinition("media."+i.Key,i.Zh,"媒体",
        "Windows 媒体会话的真实播放信息；播放进度由上报时间轴计算。不支持的字段保持不可用。",i.Type,i.Unit,
        "HUD 内容 / 事件触发",i.Type=="数值" ? "0 或 0.0" : "无需格式化"));
    internal static VariableDefinition English(VariableDefinition item)
    {
        var definition=Items.First(i=>"media."+i.Key==item.Key);
        return item with { Name=definition.En,Category="Media",Description="Actual playback information from Windows media sessions. Timeline positions are extrapolated while playing; unsupported fields remain unavailable.",
            ValueType=definition.Type switch { "文本"=>"Text","布尔"=>"Boolean",_=>"Number" },Unit=definition.Unit=="秒" ? "s" : definition.Unit,
            RecommendedUse="HUD content / event triggers",RecommendedFormats=definition.Type=="数值" ? "0 or 0.0" : "None" };
    }
}
