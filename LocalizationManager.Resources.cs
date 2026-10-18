using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;

namespace EndfieldChargePlus;

public sealed record AppLanguageInfo(AppLanguage Language,string Code,string NativeName,string EnglishName,bool RightToLeft=false,string Region="",string LanguageWord="Language",string? MinecraftCode=null)
{
    public string DisplayName=>string.IsNullOrEmpty(Region) ? NativeName : $"{NativeName} ({Region})";
    public override string ToString()=>DisplayName;
}

public static partial class LocalizationManager
{
    internal static Avalonia.Media.FontManagerOptions FontOptions=>new() {
        FontFallbacks=new[] {new Avalonia.Media.FontFallback {
            FontFamily=new Avalonia.Media.FontFamily("avares://EndfieldChargePlus/Assets/Fonts#Noto Serif Tibetan"),
            UnicodeRange=Avalonia.Media.UnicodeRange.Parse("U+0F00-0FFF")
        }}
    };
    private static IReadOnlyList<AppLanguageInfo> OriginalLanguages {get;}=Array.AsReadOnly(new[] {
        new AppLanguageInfo(AppLanguage.SimplifiedChinese,"zh-CN","简体中文","Simplified Chinese"),
        new AppLanguageInfo(AppLanguage.TraditionalChinese,"zh-TW","繁體中文","Traditional Chinese"),
        new AppLanguageInfo(AppLanguage.English,"en-US","English","English"),
        new AppLanguageInfo(AppLanguage.Japanese,"ja-JP","日本語","Japanese"),
        new AppLanguageInfo(AppLanguage.Korean,"ko-KR","한국어","Korean"),
        new AppLanguageInfo(AppLanguage.German,"de-DE","Deutsch","German"),
        new AppLanguageInfo(AppLanguage.French,"fr-FR","Français","French"),
        new AppLanguageInfo(AppLanguage.Spanish,"es-ES","Español","Spanish"),
        new AppLanguageInfo(AppLanguage.Portuguese,"pt-BR","Português (Brasil)","Portuguese"),
        new AppLanguageInfo(AppLanguage.Russian,"ru-RU","Русский","Russian"),
        new AppLanguageInfo(AppLanguage.Arabic,"ar-SA","العربية","Arabic",true),
        new AppLanguageInfo(AppLanguage.Hindi,"hi-IN","हिन्दी","Hindi"),
        new AppLanguageInfo(AppLanguage.Bengali,"bn-BD","বাংলা","Bengali"),
        new AppLanguageInfo(AppLanguage.Urdu,"ur-PK","اردو","Urdu",true),
        new AppLanguageInfo(AppLanguage.Indonesian,"id-ID","Bahasa Indonesia","Indonesian"),
        new AppLanguageInfo(AppLanguage.Vietnamese,"vi-VN","Tiếng Việt","Vietnamese"),
        new AppLanguageInfo(AppLanguage.Thai,"th-TH","ไทย","Thai"),
        new AppLanguageInfo(AppLanguage.Turkish,"tr-TR","Türkçe","Turkish"),
        new AppLanguageInfo(AppLanguage.Italian,"it-IT","Italiano","Italian"),
        new AppLanguageInfo(AppLanguage.Persian,"fa-IR","فارسی","Persian",true),
        new AppLanguageInfo(AppLanguage.Tibetan,"bo-CN","བོད་ཡིག","Tibetan"),
    });
    public static IReadOnlyList<AppLanguageInfo> SupportedLanguages {get;}=LoadLanguageCatalog();
    public static AppLanguageInfo CurrentLanguage=>SupportedLanguages.First(l=>l.Language==Current);
    public static CultureInfo DataCulture
    {
        get
        {
            try {return CultureInfo.GetCultureInfo(CurrentLanguage.Code);}
            catch(CultureNotFoundException)
            {
                try {return CultureInfo.GetCultureInfo(CurrentLanguage.Code.Split('-')[0]);}
                catch(CultureNotFoundException) {return CultureInfo.InvariantCulture;}
            }
        }
    }
    private static IReadOnlyList<AppLanguageInfo> LoadLanguageCatalog()
    {
        using var stream=typeof(LocalizationManager).Assembly.GetManifestResourceStream("EndfieldChargePlus.LanguageCatalog");
        if(stream is null)throw new InvalidOperationException("The bundled language catalog is missing.");
        using var doc=JsonDocument.Parse(stream);
        var result=doc.RootElement.GetProperty("languages").EnumerateArray().Select(e=>new AppLanguageInfo(
            (AppLanguage)e.GetProperty("id").GetInt32(),e.GetProperty("code").GetString()!,e.GetProperty("nativeName").GetString()!,
            OriginalLanguages.FirstOrDefault(l=>l.Code==e.GetProperty("code").GetString())?.EnglishName ?? e.GetProperty("nativeName").GetString()!,
            e.GetProperty("rightToLeft").GetBoolean(),e.GetProperty("region").GetString()!,e.GetProperty("languageWord").GetString()!,
            e.TryGetProperty("minecraftCode",out var mc) ? mc.GetString() : null)).ToArray();
        if(result.Select(l=>l.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=result.Length || result.Select(l=>l.Language).Distinct().Count()!=result.Length)
            throw new InvalidOperationException("Duplicate language catalog entry.");
        return Array.AsReadOnly(result);
    }
    public static string Preference {get;private set;}="Auto";
    public static bool IsRightToLeft=>SupportedLanguages.First(l=>l.Language==Current).RightToLeft;
    private static readonly object ResourceGate=new();
    private static readonly Dictionary<string,IReadOnlyDictionary<string,string>> Resources=new(StringComparer.Ordinal);
    private static readonly Lazy<Dictionary<string,string>> LiteralKeys=new(BuildLiteralKeys);
    private static readonly ConditionalWeakTable<Control,Dictionary<string,(string Key,string Chinese,string Rendered)>> ControlSources=new();

    internal static AppLanguage? LanguageForCulture(string culture)
    {
        if(SupportedLanguages.FirstOrDefault(l=>l.MinecraftCode?.Equals(culture,StringComparison.OrdinalIgnoreCase)==true) is {} minecraft)return minecraft.Language;
        string code=culture.Replace('_','-');
        var exact=SupportedLanguages.FirstOrDefault(l=>l.Code.Equals(code,StringComparison.OrdinalIgnoreCase));
        if(exact is not null)return exact.Language;
        var parts=code.Split('-');
        if(parts.Length>2 && SupportedLanguages.FirstOrDefault(l=>l.Code.Equals(parts[0]+"-"+parts[^1],StringComparison.OrdinalIgnoreCase)) is {} regional)return regional.Language;
        for(int length=parts.Length-1;length>1;length--)
            if(SupportedLanguages.FirstOrDefault(l=>l.Code.Equals(string.Join('-',parts.Take(length)),StringComparison.OrdinalIgnoreCase)) is {} parent)return parent.Language;
        if(code.StartsWith("zh",StringComparison.OrdinalIgnoreCase))
        {
            if(code.EndsWith("-HK",StringComparison.OrdinalIgnoreCase))return SupportedLanguages.First(l=>l.Code=="zh-HK").Language;
            return code.Contains("Hant",StringComparison.OrdinalIgnoreCase) || code.EndsWith("-TW",StringComparison.OrdinalIgnoreCase) || code.EndsWith("-MO",StringComparison.OrdinalIgnoreCase) ? AppLanguage.TraditionalChinese : AppLanguage.SimplifiedChinese;
        }
        if(code.StartsWith("en-",StringComparison.OrdinalIgnoreCase))return AppLanguage.English;
        string primary=code.Split('-')[0];
        if(primary.Equals("nb",StringComparison.OrdinalIgnoreCase))return SupportedLanguages.First(l=>l.Code=="no-NO").Language;
        if(primary.Equals("iw",StringComparison.OrdinalIgnoreCase))return SupportedLanguages.First(l=>l.Code=="he-IL").Language;
        if(primary.Equals("in",StringComparison.OrdinalIgnoreCase))return AppLanguage.Indonesian;
        if(OriginalLanguages.FirstOrDefault(l=>l.Code.Split('-')[0].Equals(primary,StringComparison.OrdinalIgnoreCase)) is {} original)return original.Language;
        return SupportedLanguages.FirstOrDefault(l=>l.Code.Split('-')[0].Equals(primary,StringComparison.OrdinalIgnoreCase))?.Language;
    }
    internal static AppLanguage ResolvePreference(string preference,string systemCulture)=>LanguageForCulture(preference=="Auto" ? systemCulture : preference) ?? AppLanguage.English;
    public static void SetPreference(string preference)
    {
        Preference=NormalizePreference(preference);
        var next=ResolvePreference(Preference,CultureInfo.CurrentUICulture.Name);
        if(next==Current)return;
        Current=next;LanguageChanged?.Invoke();
    }
    internal static IReadOnlyDictionary<string,string> ReadResource(string code)
    {
        lock(ResourceGate)
        {
            if(Resources.TryGetValue(code,out var known))return known;
            using var stream=typeof(LocalizationManager).Assembly.GetManifestResourceStream("EndfieldChargePlus.Localization."+code+".json");
            var loaded=stream is null ? new Dictionary<string,string>(StringComparer.Ordinal) : JsonSerializer.Deserialize<Dictionary<string,string>>(stream) ?? new();
            return Resources[code]=loaded;
        }
    }
    public static string TranslateEnglish(string english,AppLanguage? language=null)
    {
        if(string.IsNullOrEmpty(english))return english;
        var selected=language ?? Current;
        if(selected==AppLanguage.English)return english;
        var translated=ReadResource(PreferenceFor(selected)).GetValueOrDefault(english);
        return string.IsNullOrWhiteSpace(translated) ? english : translated;
    }
    public static string Formatted(string zh,FormattableString english)
    {
        if(UsesChineseSource)return zh;
        string format=TranslateEnglish(english.Format);
        try{return string.Format(CultureInfo.CurrentCulture,format,english.GetArguments());}
        catch(FormatException){return english.ToString(CultureInfo.CurrentCulture);}
    }
    internal static string EnglishKey(string value)=>ZhToEn.GetValueOrDefault(value,LiteralKeys.Value.GetValueOrDefault(value,value));
    private static Dictionary<string,string> BuildLiteralKeys()
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var item in ReadResource("en-US"))result.TryAdd(item.Key,item.Key);
        foreach(var item in ReadResource("zh-CN"))result.TryAdd(item.Value,item.Key);
        // Controls retain their canonical source across switches. This map only
        // recognizes messages created dynamically in the currently selected language.
        foreach(var language in SupportedLanguages)
            foreach(var item in ReadResource(language.Code))result.TryAdd(item.Value,item.Key);
        return result;
    }
    private static string TranslateControlLiteral(Control control,string property,string value)
    {
        var sources=ControlSources.GetOrCreateValue(control);
        bool unchanged=sources.TryGetValue(property,out var old) && old.Rendered==value;
        string key=unchanged ? old.Key : EnglishKey(value);
        string chinese=unchanged ? old.Chinese : EnToZh.GetValueOrDefault(key,ReadResource("zh-CN").GetValueOrDefault(key,value));
        // Different Chinese captions may share one English word (e.g. OK).
        // Keep each control's actual source instead of reversing that many-to-one map.
        if(!unchanged && UsesChineseSource && value.Any(c=>c is >= '\u3400' and <= '\u9fff'))chinese=value;
        string rendered=UsesChineseSource ? chinese : TranslateEnglish(key);
        sources[property]=(key,chinese,rendered);
        return rendered;
    }
}
