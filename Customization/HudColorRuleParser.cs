using System.Globalization;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Customization;

public static class HudColorRuleParser
{
    private static readonly Regex Rule = new(@"^\s*\{?(?<var>[A-Za-z0-9_.-]+)\}?\s*(?<op>>=|<=|==|!=|>|<|=|≥|≤|≠)\s*(?<value>[-+]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+))\s*%?\s*=>\s*(?<color>#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?)\s*$");

    public static List<HudColorRule> Parse(string? text)
    {
        var rules = new List<HudColorRule>();
        int lineNumber = 0;
        foreach (string line in (text ?? "").Replace("\r", "").Split('\n'))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//")) continue;
            var match = Rule.Match(line);
            if (!match.Success)
                throw new FormatException(LocalizationManager.Text($"颜色条件第 {lineNumber} 行无效。例：cpu.usage >= 80 => #FF4D4F", $"Invalid color rule on line {lineNumber}. Example: cpu.usage >= 80 => #FF4D4F"));
            rules.Add(new HudColorRule
            {
                Variable = match.Groups["var"].Value,
                Operator = match.Groups["op"].Value.Replace("≥", ">=").Replace("≤", "<=").Replace("≠", "!="),
                Value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture),
                Color = match.Groups["color"].Value,
            });
        }
        return rules;
    }
}
