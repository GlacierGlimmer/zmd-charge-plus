using System.Text.Json;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

public static class HttpSourceConfiguration
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static List<CustomHttpSource> Parse(string json)
    {
        var sources = JsonSerializer.Deserialize<List<CustomHttpSource>>(json, Options)
            ?? throw new FormatException(LocalizationManager.Text("数据源必须为 JSON 数组。", "Sources must be a JSON array."));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (source is null || !Regex.IsMatch(source.Name ?? "", @"^[A-Za-z0-9_-]+$") || !names.Add(source.Name!))
                throw new FormatException(LocalizationManager.Text("数据源名称须唯一，且只能含字母、数字、下划线和连字符。", "Source names must be unique and use letters, digits, underscores or hyphens."));
            if (source.Headers is null || source.Fields is null)
                throw new FormatException(LocalizationManager.Text("Header 和字段映射不能为 null。", "Headers and field mappings cannot be null."));
            if (!source.Enabled) continue;
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var url) || (url.Scheme != "http" && url.Scheme != "https"))
                throw new FormatException(LocalizationManager.Text($"{source.Name}：请输入完整 HTTP/HTTPS 地址。", $"{source.Name}: enter an absolute HTTP/HTTPS URL."));
            if (source.Headers is null || source.Fields is null || source.Fields.Count == 0 || source.Fields.Any(f => f is null || !Regex.IsMatch(f.Variable ?? "", @"^[A-Za-z0-9_-]+$") || string.IsNullOrWhiteSpace(f.JsonPath)))
                throw new FormatException(LocalizationManager.Text($"{source.Name}：请填写变量名和 JSON 路径。", $"{source.Name}: provide variable names and JSON paths."));
        }
        return sources;
    }
}
