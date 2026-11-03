using System.Text.Json;

namespace EndfieldChargePlus.Settings;

/// <summary>Use authored native instructions; never rewrite OS names in Windows prose.</summary>
internal static class PlatformManualContent
{
    private static ManualText T(string zh,string en)=>new(zh,en);
    private static JsonDocument? Load(string code)
    {
        using var stream=typeof(PlatformManualContent).Assembly.GetManifestResourceStream("EndfieldChargePlus.Localization.manual.platform."+code+".json");
        return stream is null ? null : JsonDocument.Parse(stream);
    }
    internal static IReadOnlyList<ManualChapter> Adapt(IReadOnlyList<ManualChapter> chapters)
    {
        bool native=!OperatingSystem.IsWindows();
        string platform=OperatingSystem.IsMacOS() ? "macos" : "linux";
        string code=LocalizationManager.PreferenceFor(LocalizationManager.Current);
        using var authored=native ? Load(code) : null;
        using var fallback=native && authored is null ? Load("en-US") : null;
        var document=authored ?? fallback;
        JsonElement translations=default;
        string[] labels=Array.Empty<string>();
        if(document is not null)
        {
            translations=document.RootElement.GetProperty(platform);
            labels=document.RootElement.GetProperty("_labels").EnumerateArray().Select(v=>v.GetString()!).ToArray();
        }
        return chapters.Select(chapter=>
        {
            if(chapter.Id=="display")return chapter with {Blocks=chapter.Blocks.Concat(new[]{new ManualBlock("global-appearance",
                T("全局字体大小和UI缩放","Global font size and UI scale"),
                T("Language → 全局字体大小和UI缩放。应用于整个设置界面和所有 HUD。单个 HUD 的大小设置会与全局 UI 缩放相乘。",
                    "Applies to all settings and every HUD. Individual HUD size is multiplied by global UI scale."))}).ToArray()};
            if(!native)return chapter;
            if(document is null || !translations.TryGetProperty(chapter.Id,out var entry))return chapter;
            string[] text=entry.EnumerateArray().Select(v=>v.GetString()!).ToArray();
            if(text.Length!=4 || labels.Length!=5)throw new InvalidDataException("Invalid native manual chapter: "+platform+"/"+chapter.Id);
            ManualText Value(string value)=>new("","",value);
            string? target=chapter.Id switch {"hardware"=>"hardware","sources"=>"sources","special"=>"hud","media"=>"variables","maintenance"=>"about","troubleshooting"=>"display",_=>null};
            var blocks=new List<ManualBlock>{
                new("guide",Value(labels[0]),Value(text[2]),"steps",Target:target),
                new("details",Value(labels[1]),Value(text[3]))};
            int example=0;
            // Copy syntax examples verbatim, without retaining Windows-only body text.
            foreach(var block in chapter.Blocks.Where(b=>b.Code.Length>0))
                blocks.Add(block with {Title=Value(labels[2]+" "+(++example)),Body=Value(labels[3])});
            return chapter with {Title=Value(text[0]),Summary=Value(text[1]),Blocks=blocks};
        }).ToArray();
    }
}
