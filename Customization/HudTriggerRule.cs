using System.Globalization;

namespace EndfieldChargePlus.Customization;

public sealed record HudTriggerRule
{
    public string Id { get; init; }=Guid.NewGuid().ToString("N");
    public bool Enabled { get; init; }=true;
    public string Name { get; init; }="";
    public string Variable { get; init; }="cpu.usage";
    public string Mode { get; init; }="Threshold";
    public string Operator { get; init; }=">=";
    public double Value { get; init; }=90;
    public int CooldownSeconds { get; init; }=30;

    internal static List<HudTriggerRule> Normalize(IEnumerable<HudTriggerRule>? rules)
    {
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (rules ?? Array.Empty<HudTriggerRule>()).Where(r=>r is not null).Take(64).Select(r=>r with {
            Id=!string.IsNullOrWhiteSpace(r.Id) && seen.Add(r.Id) ? r.Id : NewId(),Name=(r.Name ?? "").Trim(),Variable=(r.Variable ?? "").Trim(),
            Mode=r.Mode?.ToUpperInvariant() switch { "CHANGE"=>"Change","BECOMESTRUE"=>"BecomesTrue",_=>"Threshold" },
            Operator=r.Operator is ">=" or ">" or "<=" or "<" or "==" or "!=" ? r.Operator : ">=",
            Value=double.IsFinite(r.Value) ? r.Value : 0,CooldownSeconds=Math.Clamp(r.CooldownSeconds,1,86400) }).ToList();
        string NewId() { var id=Guid.NewGuid().ToString("N");seen.Add(id);return id; }
    }
}

internal sealed class HudTriggerEvaluator
{
    private sealed record State(object? Value,bool Valid,long NextAllowed);
    private readonly Dictionary<string,State> _states=new(StringComparer.OrdinalIgnoreCase);
    internal void Reset()=>_states.Clear();
    internal List<HudTriggerRule> Evaluate(IReadOnlyList<HudTriggerRule> rules,IReadOnlyDictionary<string,object?> values,long now)
    {
        var candidates=new List<HudTriggerRule>();
        foreach(var rule in rules.Where(r=>r.Enabled && !string.IsNullOrWhiteSpace(r.Variable)))
        {
            values.TryGetValue(rule.Variable,out var value);
            bool valid=IsUsable(rule,value);
            _states.TryGetValue(rule.Id,out var previous);
            bool hit=previous is { Valid:true } && valid && (rule.Mode switch {
                "Change"=>!Equal(previous.Value,value),
                "BecomesTrue"=>Boolean(previous.Value,out var before) && Boolean(value,out var after) && !before && after,
                _=>Number(value,out var number) && (rule.Operator switch { ">"=>number>rule.Value,"<"=>number<rule.Value,"<="=>number<=rule.Value,"=="=>number==rule.Value,"!="=>number!=rule.Value,_=>number>=rule.Value }) });
            _states[rule.Id]=new(value,valid,previous?.NextAllowed ?? long.MinValue);
            if(hit && now>=(previous?.NextAllowed ?? long.MinValue)) candidates.Add(rule);
        }
        var ids=rules.Select(r=>r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var id in _states.Keys.Where(k=>!ids.Contains(k)).ToArray()) _states.Remove(id);
        return candidates;
    }
    internal void Presented(IEnumerable<HudTriggerRule> rules,long now)
    {
        foreach(var rule in rules)
            if(_states.TryGetValue(rule.Id,out var state)) _states[rule.Id]=state with { NextAllowed=now+(long)rule.CooldownSeconds*1000 };
    }
    internal static bool IsUsable(HudTriggerRule rule,object? value)=>Valid(value) && (rule.Mode switch {
        "Change"=>true,"BecomesTrue"=>Boolean(value,out _),_=>Number(value,out _) });
    internal static bool Valid(object? value)=>value is not null && !(value is double d && !double.IsFinite(d)) && !(value is float f && !float.IsFinite(f))
        && !(value is string s && (string.IsNullOrWhiteSpace(s) || s is "--" or "N/A"));
    private static bool Number(object? value,out double number)=>double.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out number) && double.IsFinite(number);
    private static bool Boolean(object? value,out bool result)
    {
        if(value is bool b) { result=b;return true; }
        if(bool.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),out result)) return true;
        if(Number(value,out var number)) { result=number!=0;return true; }
        result=false;return false;
    }
    private static bool Equal(object? a,object? b)=>Number(a,out var x) && Number(b,out var y) ? x==y
        : string.Equals(Convert.ToString(a,CultureInfo.InvariantCulture),Convert.ToString(b,CultureInfo.InvariantCulture),StringComparison.Ordinal);
}
