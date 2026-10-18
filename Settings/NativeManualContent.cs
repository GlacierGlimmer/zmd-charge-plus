using System.Text.Json;

namespace EndfieldChargePlus.Settings;

// Native-language guides use concise numbered instructions and separate notes.
// They reuse the canonical examples verbatim, including stable section IDs.
internal static class NativeManualContent
{
    private static readonly Dictionary<string,IReadOnlyList<ManualChapter>> Cache=new(StringComparer.Ordinal);
    internal static bool HasNativeGuide(string code)=>typeof(NativeManualContent).Assembly.GetManifestResourceInfo("EndfieldChargePlus.Localization.manual."+code+".json") is not null;
    internal static IReadOnlyList<ManualChapter> Localize(IReadOnlyList<ManualChapter> source)
    {
        if(LocalizationManager.Current is AppLanguage.SimplifiedChinese or AppLanguage.TraditionalChinese or AppLanguage.English)return source;
        string code=LocalizationManager.PreferenceFor(LocalizationManager.Current);
        lock(Cache)
        {
            if(Cache.TryGetValue(code,out var existing))return existing;
            using var stream=typeof(NativeManualContent).Assembly.GetManifestResourceStream("EndfieldChargePlus.Localization.manual."+code+".json");
            if(stream is null)return source;
            var guide=JsonSerializer.Deserialize<Dictionary<string,string[]>>(stream)!;
            var labels=guide["_labels"];
            return Cache[code]=source.Select(chapter=>
            {
                var text=guide[chapter.Id];
                var blocks=new List<ManualBlock>{
                    new("guide",new("操作步骤","Steps",labels[0]),new(string.Join("\n",chapter.Blocks.Select(b=>b.Body.Zh)),string.Join("\n",chapter.Blocks.Select(b=>b.Body.En)),text[2]),"steps",Target:chapter.Blocks.FirstOrDefault(b=>b.Target is not null)?.Target),
                    new("details",new("说明与参数","Details",labels[1]),new(chapter.Summary.Zh,chapter.Summary.En,text[3]))};
                int example=0;
                foreach(var block in chapter.Blocks.Where(b=>b.Code.Length>0))
                    blocks.Add(block with {Title=new(block.Title.Zh,block.Title.En,labels[2]+" "+(++example)),Body=new(block.Body.Zh,block.Body.En,labels[3])});
                if(chapter.Id=="variables")blocks.Add(new("function-reference",new("函数与管道速查","Function reference",labels[4]),new("函数名称保持原样。","Function names stay unchanged.",labels[3]),Code:FunctionReference));
                return chapter with {Title=new(chapter.Title.Zh,chapter.Title.En,text[0]),Summary=new(chapter.Summary.Zh,chapter.Summary.En,text[1]),Blocks=blocks};
            }).ToArray();
        }
    }
    private const string FunctionReference="""
+ - * / % ^    > >= < <= == !=    && || !    ?:    ??
if(condition, yes, no)     between(value, min, max)
min(...) max(...) avg(...) sum(...) clamp(value, min, max)
round(value, digits) floor(value) ceil(value) abs(value) sign(value)
sqrt(value) pow(value, exponent) log(value) log10(value) exp(value)
sin(value) cos(value) tan(value) percent(value, total)
len(text) contains(text, part) startswith(text, part) endswith(text, part)
upper(text) lower(text) concat(...) number(value) string(value) bool(value)
isnull(value) isempty(value) isnan(value)

0 | 0.0 | 0.00 | auto:1 | bytes | speed | kb:1 | mb:1 | gb:2 | tb:2
duration | duration-long | time:HH:mm:ss | time:relative
upper | lower | sub:0:24 | replace:old:new | math:round:1
math:add:1 | math:sub:1 | math:mul:2 | math:div:2 | math:floor | math:ceil | math:abs
""";
}
