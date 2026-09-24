using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EndfieldChargePlus.Customization;

/// <summary>
/// Small, sandboxed expression evaluator used by HUD templates.
/// It intentionally supports only variables, literals, math/logical operators and a whitelist of functions.
/// It cannot invoke .NET methods, access files, create objects or execute arbitrary code.
/// </summary>
public static class ExpressionEngine
{
    private static readonly HashSet<string> FunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "min", "max", "avg", "sum", "clamp", "round", "floor", "ceil", "abs",
        "sqrt", "pow", "log", "log10", "exp", "sin", "cos", "tan", "sign",
        "len", "contains", "startswith", "endswith", "upper", "lower", "concat",
        "isnull", "isempty", "isnan", "number", "string", "bool", "percent", "between"
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "null", "and", "or", "not"
    };

    public static bool TryEvaluate(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        out object? value,
        out string? error)
    {
        try
        {
            var parser = new Parser(expression, variables);
            value = parser.Parse();
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or DivideByZeroException)
        {
            value = null;
            error = ex.Message;
            return false;
        }
    }

    public static IReadOnlyCollection<string> ExtractVariables(string expression)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(expression)) return result;

        int i = 0;
        while (i < expression.Length)
        {
            char c = expression[i];
            if (c is '\'' or '"')
            {
                char quote = c;
                i++;
                while (i < expression.Length)
                {
                    if (expression[i] == '\\') { i += Math.Min(2, expression.Length - i); continue; }
                    if (expression[i++] == quote) break;
                }
                continue;
            }

            if (IsIdentifierStart(c))
            {
                int start = i++;
                while (i < expression.Length && IsIdentifierPart(expression[i])) i++;
                string id = expression[start..i];
                int j = i;
                while (j < expression.Length && char.IsWhiteSpace(expression[j])) j++;
                bool functionCall = j < expression.Length && expression[j] == '(';
                if (!functionCall && !Keywords.Contains(id) && !FunctionNames.Contains(id))
                    result.Add(id);
                continue;
            }

            i++;
        }

        return result;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '.';

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, object?> _vars;
        private int _pos;

        public Parser(string text, IReadOnlyDictionary<string, object?> vars)
        {
            _text = text ?? "";
            _vars = vars;
        }

        public object? Parse()
        {
            var value = ParseTernary();
            SkipWhite();
            if (_pos != _text.Length)
                throw Error(LocalizationManager.Text($"无法识别的内容：{_text[_pos..]}", $"Unrecognized content: {_text[_pos..]}"));
            return value;
        }

        private object? ParseTernary()
        {
            var condition = ParseCoalesce();
            SkipWhite();
            if (!Match("?")) return condition;
            var whenTrue = ParseTernary();
            Require(":");
            var whenFalse = ParseTernary();
            return ToBool(condition) ? whenTrue : whenFalse;
        }

        private object? ParseCoalesce()
        {
            var left = ParseOr();
            while (true)
            {
                SkipWhite();
                if (!Match("??")) return left;
                var right = ParseOr();
                if (left is null || (left is string s && string.IsNullOrEmpty(s))) left = right;
            }
        }

        private object? ParseOr()
        {
            var left = ParseAnd();
            while (true)
            {
                SkipWhite();
                if (Match("||") || MatchWord("or"))
                {
                    var right = ParseAnd();
                    left = ToBool(left) || ToBool(right);
                }
                else return left;
            }
        }

        private object? ParseAnd()
        {
            var left = ParseEquality();
            while (true)
            {
                SkipWhite();
                if (Match("&&") || MatchWord("and"))
                {
                    var right = ParseEquality();
                    left = ToBool(left) && ToBool(right);
                }
                else return left;
            }
        }

        private object? ParseEquality()
        {
            var left = ParseComparison();
            while (true)
            {
                SkipWhite();
                if (Match("==")) left = ValuesEqual(left, ParseComparison());
                else if (Match("!=")) left = !ValuesEqual(left, ParseComparison());
                else return left;
            }
        }

        private object? ParseComparison()
        {
            var left = ParseAdditive();
            while (true)
            {
                SkipWhite();
                if (Match(">=")) left = Compare(left, ParseAdditive()) >= 0;
                else if (Match("<=")) left = Compare(left, ParseAdditive()) <= 0;
                else if (Match(">")) left = Compare(left, ParseAdditive()) > 0;
                else if (Match("<")) left = Compare(left, ParseAdditive()) < 0;
                else return left;
            }
        }

        private object? ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                SkipWhite();
                if (Match("+"))
                {
                    var right = ParseMultiplicative();
                    if (left is string || right is string)
                        left = ToText(left) + ToText(right);
                    else
                        left = ToNumber(left) + ToNumber(right);
                }
                else if (Match("-")) left = ToNumber(left) - ToNumber(ParseMultiplicative());
                else return left;
            }
        }

        private object? ParseMultiplicative()
        {
            var left = ParsePower();
            while (true)
            {
                SkipWhite();
                if (Match("*")) left = ToNumber(left) * ToNumber(ParsePower());
                else if (Match("/"))
                {
                    double divisor = ToNumber(ParsePower());
                    if (Math.Abs(divisor) < double.Epsilon) throw new DivideByZeroException(LocalizationManager.Text("表达式除数不能为 0", "Expression divisor cannot be 0"));
                    left = ToNumber(left) / divisor;
                }
                else if (Match("%"))
                {
                    double divisor = ToNumber(ParsePower());
                    if (Math.Abs(divisor) < double.Epsilon) throw new DivideByZeroException(LocalizationManager.Text("表达式取模除数不能为 0", "Modulo divisor cannot be 0"));
                    left = ToNumber(left) % divisor;
                }
                else return left;
            }
        }

        private object? ParsePower()
        {
            var left = ParseUnary();
            SkipWhite();
            if (Match("^")) return Math.Pow(ToNumber(left), ToNumber(ParsePower()));
            return left;
        }

        private object? ParseUnary()
        {
            SkipWhite();
            if (Match("!")) return !ToBool(ParseUnary());
            if (MatchWord("not")) return !ToBool(ParseUnary());
            if (Match("+")) return ToNumber(ParseUnary());
            if (Match("-")) return -ToNumber(ParseUnary());
            return ParsePrimary();
        }

        private object? ParsePrimary()
        {
            SkipWhite();
            if (_pos >= _text.Length) throw Error(LocalizationManager.Text("表达式意外结束", "Unexpected end of expression"));

            if (Match("("))
            {
                var value = ParseTernary();
                Require(")");
                return value;
            }

            char c = _text[_pos];
            if (c is '\'' or '"') return ParseString();
            if (char.IsDigit(c) || (c == '.' && _pos + 1 < _text.Length && char.IsDigit(_text[_pos + 1])))
                return ParseNumber();

            if (IsIdentifierStart(c))
            {
                string id = ParseIdentifier();
                if (id.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (id.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                if (id.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;

                SkipWhite();
                if (Match("("))
                {
                    var args = new List<object?>();
                    SkipWhite();
                    if (!Match(")"))
                    {
                        do { args.Add(ParseTernary()); SkipWhite(); }
                        while (Match(","));
                        Require(")");
                    }
                    return Call(id, args);
                }

                return _vars.TryGetValue(id, out var value) ? value : null;
            }

            throw Error(LocalizationManager.Text($"无法识别字符 '{c}'", $"Unrecognized character '{c}'"));
        }

        private object? Call(string name, List<object?> args)
        {
            string n = name.ToLowerInvariant();
            return n switch
            {
                "if" => RequireCount(name, args, 3, () => ToBool(args[0]) ? args[1] : args[2]),
                "min" => RequireMinCount(name, args, 1, () => Numeric(args).Min()),
                "max" => RequireMinCount(name, args, 1, () => Numeric(args).Max()),
                "avg" => RequireMinCount(name, args, 1, () => Numeric(args).Average()),
                "sum" => Numeric(args).Sum(),
                "clamp" => RequireCount(name, args, 3, () => Math.Clamp(ToNumber(args[0]), ToNumber(args[1]), ToNumber(args[2]))),
                "round" => Round(args, name),
                "floor" => RequireCount(name, args, 1, () => Math.Floor(ToNumber(args[0]))),
                "ceil" => RequireCount(name, args, 1, () => Math.Ceiling(ToNumber(args[0]))),
                "abs" => RequireCount(name, args, 1, () => Math.Abs(ToNumber(args[0]))),
                "sqrt" => RequireCount(name, args, 1, () => Math.Sqrt(Math.Max(0d, ToNumber(args[0])))),
                "pow" => RequireCount(name, args, 2, () => Math.Pow(ToNumber(args[0]), ToNumber(args[1]))),
                "log" => RequireCount(name, args, 1, () => Math.Log(ToNumber(args[0]))),
                "log10" => RequireCount(name, args, 1, () => Math.Log10(ToNumber(args[0]))),
                "exp" => RequireCount(name, args, 1, () => Math.Exp(ToNumber(args[0]))),
                "sin" => RequireCount(name, args, 1, () => Math.Sin(ToNumber(args[0]))),
                "cos" => RequireCount(name, args, 1, () => Math.Cos(ToNumber(args[0]))),
                "tan" => RequireCount(name, args, 1, () => Math.Tan(ToNumber(args[0]))),
                "sign" => RequireCount(name, args, 1, () => Math.Sign(ToNumber(args[0]))),
                "len" => RequireCount(name, args, 1, () => ToText(args[0]).Length),
                "contains" => RequireCount(name, args, 2, () => ToText(args[0]).Contains(ToText(args[1]), StringComparison.OrdinalIgnoreCase)),
                "startswith" => RequireCount(name, args, 2, () => ToText(args[0]).StartsWith(ToText(args[1]), StringComparison.OrdinalIgnoreCase)),
                "endswith" => RequireCount(name, args, 2, () => ToText(args[0]).EndsWith(ToText(args[1]), StringComparison.OrdinalIgnoreCase)),
                "upper" => RequireCount(name, args, 1, () => ToText(args[0]).ToUpperInvariant()),
                "lower" => RequireCount(name, args, 1, () => ToText(args[0]).ToLowerInvariant()),
                "concat" => string.Concat(args.ConvertAll(ToText)),
                "isnull" => RequireCount(name, args, 1, () => args[0] is null),
                "isempty" => RequireCount(name, args, 1, () => string.IsNullOrWhiteSpace(ToText(args[0]))),
                "isnan" => RequireCount(name, args, 1, () => double.IsNaN(ToNumber(args[0]))),
                "number" => RequireCount(name, args, 1, () => ToNumber(args[0])),
                "string" => RequireCount(name, args, 1, () => ToText(args[0])),
                "bool" => RequireCount(name, args, 1, () => ToBool(args[0])),
                "percent" => RequireCount(name, args, 2, () => Math.Abs(ToNumber(args[1])) < double.Epsilon ? 0d : ToNumber(args[0]) / ToNumber(args[1]) * 100d),
                "between" => RequireCount(name, args, 3, () => ToNumber(args[0]) >= ToNumber(args[1]) && ToNumber(args[0]) <= ToNumber(args[2])),
                _ => throw Error(LocalizationManager.Text($"不支持的函数：{name}", $"Unsupported function: {name}"))
            };
        }

        private static object Round(List<object?> args, string name)
        {
            if (args.Count is < 1 or > 2) throw new FormatException(LocalizationManager.Text($"函数 {name} 需要 1~2 个参数", $"Function {name} requires 1-2 arguments"));
            int digits = args.Count == 2 ? Math.Clamp((int)ToNumber(args[1]), 0, 8) : 0;
            return Math.Round(ToNumber(args[0]), digits, MidpointRounding.AwayFromZero);
        }

        private static List<double> Numeric(List<object?> args) => args.ConvertAll(ToNumber);

        private static object? RequireCount(string name, List<object?> args, int count, Func<object?> value)
        {
            if (args.Count != count) throw new FormatException(LocalizationManager.Text($"函数 {name} 需要 {count} 个参数", $"Function {name} requires {count} arguments"));
            return value();
        }

        private static object? RequireMinCount(string name, List<object?> args, int count, Func<object?> value)
        {
            if (args.Count < count) throw new FormatException(LocalizationManager.Text($"函数 {name} 至少需要 {count} 个参数", $"Function {name} requires at least {count} arguments"));
            return value();
        }

        private double ParseNumber()
        {
            int start = _pos;
            bool hasExponent = false;
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (char.IsDigit(c) || c == '.') { _pos++; continue; }
                if ((c == 'e' || c == 'E') && !hasExponent)
                {
                    hasExponent = true;
                    _pos++;
                    if (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                    continue;
                }
                break;
            }
            string raw = _text[start.._pos];
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw Error(LocalizationManager.Text($"无效数字：{raw}", $"Invalid number: {raw}"));
            return value;
        }

        private string ParseString()
        {
            char quote = _text[_pos++];
            var chars = new List<char>();
            while (_pos < _text.Length)
            {
                char c = _text[_pos++];
                if (c == quote) return new string(chars.ToArray());
                if (c == '\\' && _pos < _text.Length)
                {
                    char e = _text[_pos++];
                    chars.Add(e switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\', '\'' => '\'', '"' => '"', _ => e });
                }
                else chars.Add(c);
            }
            throw Error(LocalizationManager.Text("字符串缺少结束引号", "Missing closing quote"));
        }

        private string ParseIdentifier()
        {
            int start = _pos++;
            while (_pos < _text.Length && IsIdentifierPart(_text[_pos])) _pos++;
            return _text[start.._pos];
        }

        private bool Match(string token)
        {
            SkipWhite();
            if (_pos + token.Length > _text.Length) return false;
            if (!_text.AsSpan(_pos, token.Length).SequenceEqual(token.AsSpan())) return false;
            _pos += token.Length;
            return true;
        }

        private bool MatchWord(string word)
        {
            SkipWhite();
            if (_pos + word.Length > _text.Length) return false;
            if (!_text.AsSpan(_pos, word.Length).Equals(word.AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
            int end = _pos + word.Length;
            if (end < _text.Length && IsIdentifierPart(_text[end])) return false;
            if (_pos > 0 && IsIdentifierPart(_text[_pos - 1])) return false;
            _pos = end;
            return true;
        }

        private void Require(string token)
        {
            if (!Match(token)) throw Error(LocalizationManager.Text($"缺少 '{token}'", $"Missing '{token}'"));
        }

        private void SkipWhite()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }

        private FormatException Error(string message) => new(LocalizationManager.Text($"表达式错误（位置 {_pos + 1}）：{message}", $"Expression error (position {_pos + 1}): {message}"));
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (TryNumber(a, out double na) && TryNumber(b, out double nb)) return Math.Abs(na - nb) < 0.0000001d;
        if (a is bool || b is bool) return ToBool(a) == ToBool(b);
        return string.Equals(ToText(a), ToText(b), StringComparison.OrdinalIgnoreCase);
    }

    private static int Compare(object? a, object? b)
    {
        if (TryNumber(a, out double na) && TryNumber(b, out double nb)) return na.CompareTo(nb);
        return string.Compare(ToText(a), ToText(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNumber(object? value, out double number)
    {
        if (value is null) { number = 0d; return false; }
        if (value is bool b) { number = b ? 1d : 0d; return true; }
        try
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(number);
        }
        catch
        {
            string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out number)
                   || double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out number);
        }
    }

    private static double ToNumber(object? value)
    {
        if (value is null) return 0d;
        if (TryNumber(value, out double number)) return number;
        throw new FormatException(LocalizationManager.Text($"'{ToText(value)}' 不是数值", $"'{ToText(value)}' is not numeric"));
    }

    private static bool ToBool(object? value)
    {
        if (value is null) return false;
        if (value is bool b) return b;
        if (TryNumber(value, out double n)) return Math.Abs(n) > double.Epsilon;
        string s = ToText(value).Trim();
        if (bool.TryParse(s, out bool parsed)) return parsed;
        return !string.IsNullOrEmpty(s)
               && !s.Equals("false", StringComparison.OrdinalIgnoreCase)
               && !s.Equals("off", StringComparison.OrdinalIgnoreCase)
               && !s.Equals("no", StringComparison.OrdinalIgnoreCase)
               && s != "0";
    }

    private static string ToText(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
