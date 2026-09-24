using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

public static class TemplateEngine
{
    private static readonly Regex TokenRegex = new(@"\{(?<key>[a-zA-Z0-9_.-]+)(?:\|(?<fmt>[^}]+))?\}", RegexOptions.Compiled);
    private static readonly Regex ExpressionTokenRegex = new(@"\{=(?<body>[^{}]*)\}", RegexOptions.Compiled);

    public static string Render(string? template, IReadOnlyDictionary<string, object?> vars)
    {
        if (string.IsNullOrEmpty(template)) return "";

        // Advanced expression tokens are evaluated first. Existing {variable|format} tokens
        // stay fully backward-compatible and are processed afterwards.
        string rendered = ExpressionTokenRegex.Replace(template, m =>
        {
            string body = m.Groups["body"].Value;
            var (expression, format) = SplitExpressionAndFormat(body);
            if (!ExpressionEngine.TryEvaluate(expression, vars, out var value, out _)) return "--";
            if (value is null) return "--";
            return FormatPipeline(value, format, expression);
        });

        return TokenRegex.Replace(rendered, m =>
        {
            var key = m.Groups["key"].Value;
            var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : "";
            if (!vars.TryGetValue(key, out var value) || value is null) return "--";
            return FormatPipeline(value, fmt, key);
        });
    }

    public static IReadOnlyCollection<string> ExtractKeys(params string?[] templates)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates)
        {
            if (string.IsNullOrEmpty(template)) continue;
            foreach (Match m in TokenRegex.Matches(template)) result.Add(m.Groups["key"].Value);
            foreach (Match m in ExpressionTokenRegex.Matches(template))
            {
                var (expression, _) = SplitExpressionAndFormat(m.Groups["body"].Value);
                foreach (var key in ExpressionEngine.ExtractVariables(expression)) result.Add(key);
            }
        }
        return result;
    }

    public static double EvaluateNumber(string? source, IReadOnlyDictionary<string, object?> vars, double fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(source)) return fallback;
        string text = source.Trim();
        if (text.StartsWith("{=", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal))
            text = text[2..^1];
        else if (text.StartsWith("=", StringComparison.Ordinal))
            text = text[1..];
        else
            return Number(text, vars, fallback);

        var (expression, _) = SplitExpressionAndFormat(text);
        if (!ExpressionEngine.TryEvaluate(expression, vars, out var value, out _) || value is null) return fallback;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    public static IReadOnlyCollection<string> ExtractExpressionKeys(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return Array.Empty<string>();
        string text = source.Trim();
        if (text.StartsWith("{=", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal)) text = text[2..^1];
        else if (text.StartsWith("=", StringComparison.Ordinal)) text = text[1..];
        else return new[] { text };
        var (expression, _) = SplitExpressionAndFormat(text);
        return ExpressionEngine.ExtractVariables(expression);
    }

    public static double Number(string key, IReadOnlyDictionary<string, object?> vars, double fallback = 0)
    {
        if (!vars.TryGetValue(key, out var v) || v is null) return fallback;
        try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    private static (string Expression, string Format) SplitExpressionAndFormat(string body)
    {
        bool inString = false;
        char quote = '\0';
        int depth = 0;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) inString = false;
                continue;
            }
            if (c is '\'' or '"') { inString = true; quote = c; continue; }
            if (c == '(') { depth++; continue; }
            if (c == ')') { depth = Math.Max(0, depth - 1); continue; }
            if (c == '|' && depth == 0)
            {
                bool isLogical = (i > 0 && body[i - 1] == '|') || (i + 1 < body.Length && body[i + 1] == '|');
                if (!isLogical) return (body[..i].Trim(), body[(i + 1)..].Trim());
            }
        }
        return (body.Trim(), "");
    }

    private static string FormatPipeline(object value, string fmt, string key)
    {
        if (string.IsNullOrWhiteSpace(fmt)) return BaseFormat(value, "", key);
        object current = value;
        foreach (var token in fmt.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryMath(ref current, token)) continue;
            if (TryText(ref current, token)) continue;
            if (TryTime(ref current, token)) continue;
            if (token.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
            {
                int digits = ParseDigits(token, 1);
                current = SmartAuto(current, key, digits);
                continue;
            }
            current = BaseFormat(current, token, key);
        }
        return Convert.ToString(current, CultureInfo.InvariantCulture) ?? "";
    }

    private static bool TryMath(ref object value, string token)
    {
        if (!token.StartsWith("math:", StringComparison.OrdinalIgnoreCase)) return false;
        if (!TryDouble(value, out double n)) { value = "--"; return true; }
        var p = token.Split(':');
        string op = p.Length > 1 ? p[1].ToLowerInvariant() : "";
        double arg = p.Length > 2 && double.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 0d;
        value = op switch
        {
            "add" => n + arg,
            "sub" => n - arg,
            "mul" => n * arg,
            "div" => Math.Abs(arg) < double.Epsilon ? double.NaN : n / arg,
            "round" => Math.Round(n, p.Length > 2 && int.TryParse(p[2], out var d) ? Math.Clamp(d, 0, 8) : 0, MidpointRounding.AwayFromZero),
            "floor" => Math.Floor(n),
            "ceil" => Math.Ceiling(n),
            "abs" => Math.Abs(n),
            _ => n
        };
        return true;
    }

    private static bool TryText(ref object value, string token)
    {
        if (token.StartsWith("sub:", StringComparison.OrdinalIgnoreCase))
        {
            string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            var p = token.Split(':');
            int start = p.Length > 1 && int.TryParse(p[1], out var st) ? Math.Max(0, st) : 0;
            int len = p.Length > 2 && int.TryParse(p[2], out var le) ? Math.Max(0, le) : Math.Max(0, s.Length - start);
            value = start >= s.Length ? "" : s.Substring(start, Math.Min(len, s.Length - start));
            return true;
        }
        if (token.StartsWith("replace:", StringComparison.OrdinalIgnoreCase))
        {
            string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            var p = token.Split(':', 3);
            if (p.Length == 3) value = s.Replace(p[1], p[2], StringComparison.Ordinal);
            return true;
        }
        if (token.Equals("upper", StringComparison.OrdinalIgnoreCase)) { value = (Convert.ToString(value) ?? "").ToUpperInvariant(); return true; }
        if (token.Equals("lower", StringComparison.OrdinalIgnoreCase)) { value = (Convert.ToString(value) ?? "").ToLowerInvariant(); return true; }
        return false;
    }

    private static bool TryTime(ref object value, string token)
    {
        if (!token.StartsWith("time:", StringComparison.OrdinalIgnoreCase)) return false;
        string arg = token[5..];
        if (arg.Equals("relative", StringComparison.OrdinalIgnoreCase))
        {
            if (TryDateTime(value, out var dt)) value = RelativeTime(dt);
            else if (TryDouble(value, out var seconds)) value = RelativeDuration(seconds);
            else value = "--";
            return true;
        }
        if (TryDateTime(value, out var parsed))
        {
            try { value = parsed.ToString(arg, CultureInfo.CurrentCulture); } catch { value = parsed.ToString(CultureInfo.CurrentCulture); }
        }
        else value = "--";
        return true;
    }

    private static object SmartAuto(object value, string key, int digits)
    {
        if (!TryDouble(value, out var n)) return value;
        digits = Math.Clamp(digits, 0, 6);
        if (key.EndsWith("_bps", StringComparison.OrdinalIgnoreCase) || key.Contains(".read_bps", StringComparison.OrdinalIgnoreCase) || key.Contains(".write_bps", StringComparison.OrdinalIgnoreCase) || key.Contains(".io_bps", StringComparison.OrdinalIgnoreCase))
            return HumanBytes(n, true, digits);
        if (key.EndsWith("_bytes", StringComparison.OrdinalIgnoreCase) || key.Contains("bytes", StringComparison.OrdinalIgnoreCase))
            return HumanBytes(n, false, digits);
        if (key.EndsWith("_seconds", StringComparison.OrdinalIgnoreCase))
            return Duration(n);
        if (key.Contains("percent", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".usage", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".progress", StringComparison.OrdinalIgnoreCase))
            return n.ToString($"F{digits}", CultureInfo.InvariantCulture) + "%";
        return n.ToString($"F{digits}", CultureInfo.InvariantCulture);
    }

    private static string BaseFormat(object value, string fmt, string key)
    {
        if (value is DateTime dt)
            return string.IsNullOrWhiteSpace(fmt) ? dt.ToString("G") : dt.ToString(fmt.Replace("dt:", ""));
        if (value is DateTimeOffset dto)
            return string.IsNullOrWhiteSpace(fmt) ? dto.ToString("G") : dto.ToString(fmt.Replace("dt:", ""));
        if (value is bool b) return b ? "true" : "false";
        if (value is string s && string.IsNullOrWhiteSpace(fmt)) return s;

        if (TryDouble(value, out var n))
        {
            if (string.IsNullOrWhiteSpace(fmt)) return n.ToString("0.##", CultureInfo.InvariantCulture);
            if (fmt.Equals("bytes", StringComparison.OrdinalIgnoreCase)) return HumanBytes(n, false, 1);
            if (fmt.Equals("speed", StringComparison.OrdinalIgnoreCase)) return HumanBytes(n, true, 1);
            if (fmt.StartsWith("gb", StringComparison.OrdinalIgnoreCase)) return (n / Math.Pow(1024d, 3)).ToString($"F{ParseDigits(fmt, 1)}", CultureInfo.InvariantCulture);
            if (fmt.StartsWith("mb", StringComparison.OrdinalIgnoreCase)) return (n / Math.Pow(1024d, 2)).ToString($"F{ParseDigits(fmt, 1)}", CultureInfo.InvariantCulture);
            if (fmt.StartsWith("kb", StringComparison.OrdinalIgnoreCase)) return (n / 1024d).ToString($"F{ParseDigits(fmt, 1)}", CultureInfo.InvariantCulture);
            if (fmt.StartsWith("tb", StringComparison.OrdinalIgnoreCase)) return (n / Math.Pow(1024d, 4)).ToString($"F{ParseDigits(fmt, 2)}", CultureInfo.InvariantCulture);
            if (fmt.StartsWith("mbps", StringComparison.OrdinalIgnoreCase)) return (n * 8d / 1_000_000d).ToString($"F{ParseDigits(fmt, 1)}", CultureInfo.InvariantCulture);
            if (fmt.StartsWith("kbps", StringComparison.OrdinalIgnoreCase)) return (n * 8d / 1_000d).ToString($"F{ParseDigits(fmt, 1)}", CultureInfo.InvariantCulture);
            if (fmt.StartsWith("percent", StringComparison.OrdinalIgnoreCase)) return n.ToString($"F{ParseDigits(fmt, 0)}", CultureInfo.InvariantCulture) + "%";
            if (fmt.Equals("duration", StringComparison.OrdinalIgnoreCase)) return Duration(n);
            if (fmt.Equals("duration-long", StringComparison.OrdinalIgnoreCase)) return DurationLong(n);
            try { return n.ToString(fmt, CultureInfo.InvariantCulture); }
            catch { return n.ToString("0.##", CultureInfo.InvariantCulture); }
        }
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    private static bool TryDouble(object value, out double n)
    {
        try { n = Convert.ToDouble(value, CultureInfo.InvariantCulture); return !double.IsNaN(n); }
        catch { n = 0; return false; }
    }

    private static bool TryDateTime(object value, out DateTime dt)
    {
        if (value is DateTime d) { dt = d; return true; }
        if (value is DateTimeOffset dto) { dt = dto.LocalDateTime; return true; }
        string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt)
               || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt);
    }

    private static int ParseDigits(string fmt, int fallback)
    {
        var parts = fmt.Split(':', 2);
        return parts.Length == 2 && int.TryParse(parts[1], out var d) ? Math.Clamp(d, 0, 6) : fallback;
    }

    private static string HumanBytes(double bytes, bool perSecond, int digits)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (Math.Abs(bytes) >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return $"{bytes.ToString($"F{digits}", CultureInfo.InvariantCulture)} {units[i]}{(perSecond ? "/s" : "")}";
    }

    private static string Duration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, (long)seconds));
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static string DurationLong(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, (long)seconds));
        return ts.TotalDays >= 1
            ? LocalizationManager.Text($"{(int)ts.TotalDays}天 {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}", $"{(int)ts.TotalDays}d {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}")
            : $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static string RelativeTime(DateTime dt)
    {
        var delta = DateTime.Now - dt;
        bool future = delta.TotalSeconds < 0;
        var a = delta.Duration();
        if (LocalizationManager.IsEnglish)
        {
            string unitEn = a.TotalSeconds < 60 ? $"{Math.Max(1, (int)a.TotalSeconds)}s"
                : a.TotalMinutes < 60 ? $"{(int)a.TotalMinutes}m"
                : a.TotalHours < 24 ? $"{(int)a.TotalHours}h"
                : a.TotalDays < 30 ? $"{(int)a.TotalDays}d"
                : a.TotalDays < 365 ? $"{(int)(a.TotalDays / 30)}mo"
                : $"{(int)(a.TotalDays / 365)}y";
            return future ? $"in {unitEn}" : $"{unitEn} ago";
        }

        string unit = a.TotalSeconds < 60 ? $"{Math.Max(1, (int)a.TotalSeconds)}秒"
            : a.TotalMinutes < 60 ? $"{(int)a.TotalMinutes}分钟"
            : a.TotalHours < 24 ? $"{(int)a.TotalHours}小时"
            : a.TotalDays < 30 ? $"{(int)a.TotalDays}天"
            : a.TotalDays < 365 ? $"{(int)(a.TotalDays / 30)}个月"
            : $"{(int)(a.TotalDays / 365)}年";
        return future ? $"{unit}后" : $"{unit}前";
    }

    private static string RelativeDuration(double seconds)
    {
        var a = Math.Abs(seconds);
        if (LocalizationManager.IsEnglish)
        {
            if (a < 60) return $"{(int)a}s";
            if (a < 3600) return $"{(int)(a / 60)}m";
            if (a < 86400) return $"{(int)(a / 3600)}h";
            return $"{(int)(a / 86400)}d";
        }
        if (a < 60) return $"{(int)a}秒";
        if (a < 3600) return $"{(int)(a / 60)}分钟";
        if (a < 86400) return $"{(int)(a / 3600)}小时";
        return $"{(int)(a / 86400)}天";
    }
}
