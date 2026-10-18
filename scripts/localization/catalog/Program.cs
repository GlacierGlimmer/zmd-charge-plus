using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if(args.Length<2)throw new ArgumentException("Usage: LocaleTools <repository> <output-directory> [--rewrite]");
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
var catalog=new SortedDictionary<string,string>(StringComparer.Ordinal);
var origins=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
void Add(string? zh,string? en,string origin)
{
    if(string.IsNullOrWhiteSpace(en) || en.Length>18000)return;
    if(zh?.StartsWith("{}",StringComparison.Ordinal)==true)zh=zh[2..];
    if(!catalog.ContainsKey(en) || HasChinese(zh) && !HasChinese(catalog[en]))catalog[en]=string.IsNullOrEmpty(zh) ? en : zh;
    if(!origins.TryGetValue(en,out var set))origins[en]=set=new();set.Add(origin);
}
bool HasChinese(string? value)=>value is not null && Regex.IsMatch(value,@"[\u3400-\u9fff]");
string? Value(ExpressionSyntax? e)=>e switch {
    LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression)=>l.Token.ValueText,
    InterpolatedStringExpressionSyntax i=>string.Concat(i.Contents.Select((c,index)=>c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : "{"+i.Contents.Take(index).Count(x=>x is InterpolationSyntax)+(c is InterpolationSyntax p ? p.AlignmentClause?.ToString()+p.FormatClause?.ToString() : "")+"}")),
    CastExpressionSyntax c=>Value(c.Expression),
    ParenthesizedExpressionSyntax p=>Value(p.Expression),
    _=>null};
foreach(string path in Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories).Where(p=>!Regex.IsMatch(Path.GetRelativePath(root,p),@"^(bin|obj|Tests|artifacts|publish|packaging|scripts)[\\/]|[\\/]Linux[^\\/]*\.cs$")))
{
    var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(path));var syntax=tree.GetRoot();string origin=Path.GetRelativePath(root,path);
    foreach(var inv in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        var a=inv.ArgumentList.Arguments.Select(x=>x.Expression).ToArray();string name=inv.Expression.ToString();
        if((name.EndsWith("LocalizationManager.Text") || name.EndsWith("LocalizationManager.Formatted")) && a.Length==2)Add(Value(a[0]),Value(a[1]),origin);
        if(name is "B" && a.Length>=5){Add(Value(a[1]),Value(a[2]),origin);Add(Value(a[3]),Value(a[4]),origin);}
        else if(name is "C" && a.Length>=5){Add(Value(a[1]),Value(a[2]),origin);Add(Value(a[3]),Value(a[4]),origin);}
        else if(name is "InstanceText" && a.Length>=3)Add(Value(a[1]),Value(a[2]),origin);
        else if(a.Length>=2 && (name is "L" or "T" || name.StartsWith("Set",StringComparison.Ordinal) && name.EndsWith("Status",StringComparison.Ordinal)))
            for(int n=0;n<a.Length-1;n++)if(HasChinese(Value(a[n])) && Value(a[n+1]) is { } en && !HasChinese(en))Add(Value(a[n]),en,origin);
    }
    foreach(var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        if(assignment.Left is ImplicitElementAccessSyntax access && access.ArgumentList.Arguments.Count==1 && HasChinese(Value(access.ArgumentList.Arguments[0].Expression)))Add(Value(access.ArgumentList.Arguments[0].Expression),Value(assignment.Right),origin);
    // Paired arrays used by choices and position presets.
    foreach(var block in syntax.DescendantNodes().OfType<BlockSyntax>())
    {
        var declarations=block.Statements.OfType<LocalDeclarationStatementSyntax>().SelectMany(s=>s.Declaration.Variables).ToDictionary(v=>v.Identifier.Text,v=>v.Initializer?.Value);
        if(declarations.TryGetValue("zh",out var zh) && declarations.TryGetValue("en",out var en))
        {
            IEnumerable<ExpressionSyntax>? Elements(ExpressionSyntax? value)=>value switch {
                CollectionExpressionSyntax c=>c.Elements.OfType<ExpressionElementSyntax>().Select(x=>x.Expression),
                ImplicitArrayCreationExpressionSyntax a=>a.Initializer.Expressions,
                ArrayCreationExpressionSyntax a=>a.Initializer?.Expressions,
                InitializerExpressionSyntax a=>a.Expressions,
                _=>null};
            var z=Elements(zh)?.ToArray();var e=Elements(en)?.ToArray();
            if(z is not null && e is not null && z.Length==e.Length)for(int n=0;n<z.Length;n++)Add(Value(z[n]),Value(e[n]),origin);
        }
    }
    if(args.Contains("--rewrite"))
    {
        var rewritten=new Formats().Visit(syntax)!.ToFullString();
        if(rewritten!=syntax.ToFullString())File.WriteAllText(path,rewritten,new UTF8Encoding(false));
    }
}
// Capture complete existing variable metadata and world-clock names without reading user settings.
string binaries=Path.Combine(root,@"Tests\bin\Release\net8.0-windows10.0.19041.0");
if(args.Contains("--app-binaries"))binaries=Path.Combine(root,@"bin\Release\net8.0-windows10.0.19041.0");
if(!Directory.Exists(binaries))binaries=Path.Combine(root,@"bin\Release\net8.0");
AssemblyLoadContext.Default.Resolving+=(_,name)=>{string p=Path.Combine(binaries,name.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};
var assembly=Assembly.LoadFrom(Path.Combine(binaries,"EndfieldChargePlus.dll"));
var localization=assembly.GetType("EndfieldChargePlus.LocalizationManager")!;
var language=assembly.GetType("EndfieldChargePlus.AppLanguage")!;
localization.GetMethod("SetLanguage")!.Invoke(null,new[]{Enum.Parse(language,"English")});
var variableCatalog=assembly.GetType("EndfieldChargePlus.Customization.VariableCatalog")!;
var variableLocalizer=assembly.GetType("EndfieldChargePlus.Customization.VariableLocalization")!.GetMethod("Localize")!;
var variableMetadata=new SortedDictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
foreach(var item in (IEnumerable)variableCatalog.GetProperty("AllBuiltIns")!.GetValue(null)!)
{
    object english=variableLocalizer.Invoke(null,new[]{item})!;
    var fields=new Dictionary<string,string>();
    foreach(string property in new[]{"Name","Category","Description","ValueType","RecommendedUse","RecommendedFormats"})Add(item.GetType().GetProperty(property)!.GetValue(item)?.ToString(),english.GetType().GetProperty(property)!.GetValue(english)?.ToString(),"VariableCatalog");
    foreach(string property in new[]{"Name","Category","Description","ValueType","RecommendedUse","RecommendedFormats"})fields[property]=english.GetType().GetProperty(property)!.GetValue(english)?.ToString() ?? "";
    variableMetadata[item.GetType().GetProperty("Key")!.GetValue(item)!.ToString()!]=fields;
}
foreach(var zone in (IEnumerable)assembly.GetType("EndfieldChargePlus.Customization.WorldClockTimeZones")!.GetField("All")!.GetValue(null)!)
    Add(zone.GetType().GetProperty("Zh")!.GetValue(zone)?.ToString(),zone.GetType().GetProperty("En")!.GetValue(zone)?.ToString(),"WorldClockTimeZones");
var profiles=(IEnumerable)assembly.GetType("EndfieldChargePlus.Customization.CustomHudSettings")!.GetMethod("CreateDefaultProfiles")!.Invoke(null,null)!;
var profileLocalizer=assembly.GetType("EndfieldChargePlus.Customization.BuiltInProfileLocalization")!.GetMethod("ForLanguage")!;
foreach(var profile in profiles)
{
    var zh=profileLocalizer.Invoke(null,new[]{profile,Enum.Parse(language,"SimplifiedChinese")})!;
    var en=profileLocalizer.Invoke(null,new[]{profile,Enum.Parse(language,"English")})!;
    foreach(string property in new[]{"Name","Category","TaglineTemplate","TitleTemplate","PrimaryTemplate","SecondaryTemplate","RightTemplate","RightSuffix"})
        Add(profile.GetType().GetProperty(property)!.GetValue(zh)?.ToString(),profile.GetType().GetProperty(property)!.GetValue(en)?.ToString(),"BuiltInProfiles");
}
Directory.CreateDirectory(output);
Add("硬件信息","Hardware Info","HardwareTelemetry");
Add("HUD 内容 / 规则","HUD content / rules","HardwareTelemetry");
Add("需管理员身份运行","Run as administrator","BuiltInProfiles");
Add("开启","On","ToggleSwitch");
string extra=Path.Combine(root,"Localization","reviewed-source.json");
if(File.Exists(extra))foreach(var item in JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(extra))!)Add(item.Key,item.Value,"Reviewed source");
var knownChinese=catalog.Values.ToHashSet(StringComparer.Ordinal);
var missingXaml=new SortedSet<string>(StringComparer.Ordinal);
foreach(string path in Directory.EnumerateFiles(root,"*.axaml",SearchOption.AllDirectories).Where(p=>!Regex.IsMatch(Path.GetRelativePath(root,p),@"^(bin|obj|Tests|artifacts|publish|packaging)[\\/]")))
    foreach(var attribute in XDocument.Load(path).Descendants().Attributes().Where(a=>a.Name.LocalName is "Text" or "Content" or "Header" or "Watermark" or "Title"))
    {
        string value=attribute.Value.StartsWith("{}") ? attribute.Value[2..] : attribute.Value;
        if(HasChinese(value) && !value.StartsWith("©",StringComparison.Ordinal) && !knownChinese.Contains(value))missingXaml.Add(value);
    }
File.WriteAllLines(Path.Combine(output,"missing-xaml.txt"),missingXaml);
var options=new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
File.WriteAllText(Path.Combine(output,"catalog.zh-CN.json"),JsonSerializer.Serialize(catalog,options));
File.WriteAllText(Path.Combine(output,"catalog.origins.json"),JsonSerializer.Serialize(origins,options));
File.WriteAllText(Path.Combine(output,"catalog.variables.json"),JsonSerializer.Serialize(variableMetadata,options));
if(args.Contains("--manual"))
{
    var manualType=assembly.GetType("EndfieldChargePlus.Settings.UserManualContent")!;
    object? Property(object obj,string name)=>obj.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(obj);
    string Text(object obj)=>Property(obj,"Value")?.ToString() ?? "";
    foreach(var item in (IEnumerable)localization.GetProperty("SupportedLanguages")!.GetValue(null)!)
    {
        string code=Property(item,"Code")!.ToString()!;
        localization.GetMethod("SetPreference")!.Invoke(null,new object[]{code});
        var chapters=((IEnumerable)manualType.GetProperty("Chapters",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().Select(c=>new {
            id=Property(c,"Id"),title=Text(Property(c,"Title")!),summary=Text(Property(c,"Summary")!),
            blocks=((IEnumerable)Property(c,"Blocks")!).Cast<object>().Select(b=>new {
                id=Property(b,"Id"),title=Text(Property(b,"Title")!),body=Text(Property(b,"Body")!),kind=Property(b,"Kind"),code=Property(b,"Code"),target=Property(b,"Target")}).ToArray() }).ToArray();
        File.WriteAllText(Path.Combine(output,"manual.full."+code+".json"),JsonSerializer.Serialize(chapters,options));
    }
}
Console.WriteLine($"Catalog: {catalog.Count} strings; {catalog.Keys.Sum(s=>s.Length)} English characters.");

sealed class Formats:CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var visited=(InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        if(visited.ArgumentList.Arguments.Count==2 && visited.ArgumentList.Arguments[1].Expression is InterpolatedStringExpressionSyntax en &&
            (visited.Expression is MemberAccessExpressionSyntax member && member.ToString().EndsWith("LocalizationManager.Text") || visited.Expression is IdentifierNameSyntax id && id.Identifier.Text is "SetMaintenanceStatus" or "SetHudHotkeyStatus"))
        {
            var cast=SyntaxFactory.CastExpression(SyntaxFactory.ParseTypeName("System.FormattableString"),en);
            if(visited.Expression is MemberAccessExpressionSyntax m)visited=visited.WithExpression(m.WithName(SyntaxFactory.IdentifierName("Formatted")));
            return visited.WithArgumentList(visited.ArgumentList.WithArguments(visited.ArgumentList.Arguments.Replace(visited.ArgumentList.Arguments[1],visited.ArgumentList.Arguments[1].WithExpression(cast))));
        }
        return visited;
    }
}
