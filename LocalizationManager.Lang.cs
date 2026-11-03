using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace EndfieldChargePlus;

/// <summary>
/// Embedded .lang resource loader: core.lang is the single key index, every
/// locale file stores only values in the same order, and each file has a
/// SHA-256 sidecar used by the build/verification tooling.
/// </summary>
public static partial class LocalizationManager
{
    private const string LangResourcePrefix = "EndfieldChargePlus.lang.";
    private const string LangExtension = ".lang";

    private sealed class LangIndex
    {
        public required string Version { get; init; }
        public required string[] Keys { get; init; }
        public required Dictionary<string,int> Positions { get; init; }
    }

    private sealed record LangDocument(string Version,int Declared,string[] Values);

    private static readonly Lazy<LangIndex> CoreLanguageIndex = new(LoadCoreLanguageIndex);
    private static readonly Lazy<string[]> EmbeddedLangCodes = new(DiscoverEmbeddedLangCodes);
    private static readonly Lazy<IReadOnlyDictionary<string,string>> PlatformMessageSources = new(LoadPlatformMessageSources);

    private static IReadOnlyDictionary<string,string> LoadPlatformMessageSources()
    {
        using Stream stream=typeof(LocalizationManager).Assembly.GetManifestResourceStream("EndfieldChargePlus.PlatformFields")
            ?? throw new InvalidDataException("The embedded platform field definitions are missing.");
        using var document=JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("fields").EnumerateArray().ToDictionary(
            field=>field.GetProperty("key").GetString()!,field=>field.GetProperty("source").GetString()!,StringComparer.Ordinal);
    }

    internal static IReadOnlyDictionary<string,string> PlatformFieldSources=>PlatformMessageSources.Value;

    internal static string CoreLanguageVersion=>CoreLanguageIndex.Value.Version;
    internal static int CoreLanguageFieldCount=>CoreLanguageIndex.Value.Keys.Length;

    internal static IReadOnlyList<string> AvailableLanguageCodes=>EmbeddedLangCodes.Value;

    internal static bool HasLanguageResource(string code)=>EmbeddedLangCodes.Value.Contains(code,StringComparer.OrdinalIgnoreCase);

    private static string[] DiscoverEmbeddedLangCodes()
    {
        var assembly=typeof(LocalizationManager).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name=>name.StartsWith(LangResourcePrefix,StringComparison.Ordinal)
                && name.EndsWith(LangExtension,StringComparison.Ordinal)
                && !name.EndsWith(".sha256",StringComparison.Ordinal))
            .Select(name=>name[LangResourcePrefix.Length..^LangExtension.Length])
            .Where(code=>code.Length>0 && code!="core")
            .OrderBy(code=>code,StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LangIndex LoadCoreLanguageIndex()
    {
        LangDocument document=ReadLangDocument("core");
        var positions=new Dictionary<string,int>(StringComparer.Ordinal);
        for(int index=0;index<document.Values.Length;index++)
        {
            string key=document.Values[index];
            if(!positions.TryAdd(key,index))
                throw new InvalidDataException($"core.lang contains duplicate key '{key}'.");
        }
        return new LangIndex {Version=document.Version,Keys=document.Values,Positions=positions};
    }

    private static LangDocument ReadLangDocument(string code)
    {
        var assembly=typeof(LocalizationManager).Assembly;
        string name=LangResourcePrefix+code+LangExtension;
        using Stream? stream=assembly.GetManifestResourceStream(name);
        if(stream is null)
            throw new InvalidDataException($"The embedded language resource '{name}' is missing.");
        using var reader=new StreamReader(stream,Encoding.UTF8,detectEncodingFromByteOrderMarks:true);
        string text=reader.ReadToEnd();
        if(text.Length>0 && text[0]=='\ufeff')text=text[1..];
        text=text.Replace("\r\n","\n").Replace('\r','\n');
        var lines=new List<string>(text.Split('\n'));
        // The trailing newline is a file-format terminator, not a field.
        if(lines.Count>0 && lines[^1].Length==0)lines.RemoveAt(lines.Count-1);
        if(lines.Count==0)throw new InvalidDataException($"{name} is empty.");
        string header=lines[0];
        if(!TryParseResourceHeader(header,out string version,out int declared))
            throw new InvalidDataException($"{name} has an invalid header: {header}");
        var values=new List<string>(declared);
        for(int index=1;index<lines.Count;index++)
            values.Add(UnescapeLangValue(lines[index]));
        if(values.Count!=declared)
            throw new InvalidDataException($"{name} declares {declared} fields but contains {values.Count} lines.");
        return new LangDocument(version,declared,values.ToArray());
    }

    /// <summary>Parses the '#YYYYMMDD$count' resource header (product versions are independent).</summary>
    internal static bool TryParseResourceHeader(string header,out string date,out int fieldCount)
    {
        date="";fieldCount=0;
        if(!header.StartsWith("#",StringComparison.Ordinal))return false;
        int separator=header.IndexOf('$');
        if(separator!=9)return false;
        string candidate=header[1..separator];
        if(candidate.Length!=8 || !candidate.All(char.IsDigit))return false;
        if(!DateTime.TryParseExact(candidate,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))return false;
        if(!int.TryParse(header[(separator+1)..],out fieldCount) || fieldCount<0)return false;
        date=candidate;
        return true;
    }

    /// <summary>True when a resource date is newer than the reference date.</summary>
    internal static bool IsNewerResourceDate(string candidate,string reference)
    {
        if(candidate.Length==8 && reference.Length==8 && candidate.All(char.IsDigit) && reference.All(char.IsDigit))
            return string.CompareOrdinal(candidate,reference)>0;
        return false;
    }

    private static readonly object ValueCacheGate=new();
    private static readonly Dictionary<string,string[]> ValueCache=new(StringComparer.Ordinal);

    private static string[] LanguageValues(string code)
    {
        lock(ValueCacheGate)
        {
            if(ValueCache.TryGetValue(code,out var known))return known;
            var values=ReadLangDocument(code).Values;
            return ValueCache[code]=values;
        }
    }

    /// <summary>
    /// Reads a named field (for example "windows.title") from the current language,
    /// falling back through the language chain and then the English source.
    /// </summary>
    public static string Field(string fieldName)=>FieldForLanguage(fieldName,PreferenceFor(Current));

    private static string FieldForLanguage(string fieldName,string languageCode)
    {
        if(string.IsNullOrWhiteSpace(fieldName))return fieldName;
        var core=CoreLanguageIndex.Value;
        if(!core.Positions.TryGetValue(fieldName,out int index))return fieldName;
        foreach(string candidate in LanguageLookupChain(languageCode))
        {
            if(!HasLanguageResource(candidate))continue;
            var values=LanguageValues(candidate);
            if(index<values.Length && !string.IsNullOrWhiteSpace(values[index]))return values[index];
        }
        var english=LanguageValues("en-US");
        if(index<english.Length && !string.IsNullOrWhiteSpace(english[index]))return english[index];
        return fieldName;
    }

    /// <summary>Product title for the running platform, driven by the shared .lang files.</summary>
    public static string PlatformTitle=>Field(OperatingSystem.IsLinux() ? "linux.title" : OperatingSystem.IsMacOS() ? "macos.title" : "windows.title");

    private static string UnescapeLangValue(string value)
    {
        if(value.IndexOf('\\')<0)return value;
        var builder=new StringBuilder(value.Length);
        for(int index=0;index<value.Length;index++)
        {
            char character=value[index];
            if(character=='\\' && index+1<value.Length)
            {
                char next=value[index+1];
                if(next=='n'){builder.Append('\n');index++;continue;}
                if(next=='\\'){builder.Append('\\');index++;continue;}
            }
            builder.Append(character);
        }
        return builder.ToString();
    }

    /// <summary>Loads one locale as the legacy English-key → translation map.</summary>
    internal static IReadOnlyDictionary<string,string> LoadLangPack(string code)
    {
        LangIndex core=CoreLanguageIndex.Value;
        LangDocument document=ReadLangDocument(code);
        if(document.Declared>core.Keys.Length)
            throw new InvalidDataException($"Language '{code}' declares {document.Declared} fields but core.lang defines {core.Keys.Length}.");
        // Native field IDs retain their original row. Existing literal-addressed
        // call sites continue to use the original English lookup ID from the
        // manifest, which may differ from the improved en-US display wording.
        string[] lookupKeys=core.Keys.Select(key=>PlatformMessageSources.Value.GetValueOrDefault(key,key)).ToArray();
        return BuildPackFromValues(code,lookupKeys,document.Values);
    }

    /// <summary>Maps a (possibly older/shorter) value list onto the core key prefix.</summary>
    internal static IReadOnlyDictionary<string,string> BuildPackFromValues(string code,string[] keys,string[] values)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int index=0;index<values.Length && index<keys.Length;index++)
        {
            string key=keys[index];
            string value=values[index];
            if(code.Equals("en-US",StringComparison.OrdinalIgnoreCase))
            {
                result[key]=string.IsNullOrWhiteSpace(value) ? key : value;
                continue;
            }
            if(string.IsNullOrWhiteSpace(value))continue;
            result[key]=value;
        }
        return result;
    }

    /// <summary>Regional/locale parent used before falling back to English.</summary>
    internal static string? FallbackLanguageCode(string code)
    {
        if(string.IsNullOrEmpty(code))return null;
        string primary=code.Split('-')[0];
        var candidates=AvailableLanguageCodes
            .Where(other=>!other.Equals(code,StringComparison.OrdinalIgnoreCase)
                && other.Split('-')[0].Equals(primary,StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if(candidates.Length==0)return null;
        // Prefer a base locale (de-CH → de-DE) and otherwise the first available.
        return candidates.FirstOrDefault(other=>other.Equals(primary,StringComparison.OrdinalIgnoreCase))
            ?? candidates[0];
    }
}
