namespace EndfieldChargePlus.Customization;

// Rebuild only when a source catalogue publishes a different immutable snapshot.
internal static class VariableCatalogSnapshot
{
    private static readonly object Gate=new();
    private static object? _fans,_hardware,_platform;
    private static IReadOnlyList<VariableDefinition>? _all,_indexed;
    private static Dictionary<string,VariableDefinition> _byKey=new(StringComparer.OrdinalIgnoreCase);
    internal static IReadOnlyList<VariableDefinition> Read(object fans,object hardware,object? platform,Func<IReadOnlyList<VariableDefinition>> build)
    {
        lock(Gate)
        {
            if(_all is not null && ReferenceEquals(_fans,fans) && ReferenceEquals(_hardware,hardware) && ReferenceEquals(_platform,platform))return _all;
            var result=build();_fans=fans;_hardware=hardware;_platform=platform;return _all=result;
        }
    }
    internal static VariableDefinition? Find(IReadOnlyList<VariableDefinition> source,string key)
    {
        if(string.IsNullOrEmpty(key))return null;
        lock(Gate)
        {
            if(!ReferenceEquals(_indexed,source)){_byKey=source.ToDictionary(d=>d.Key,StringComparer.OrdinalIgnoreCase);_indexed=source;}
            return _byKey.GetValueOrDefault(key);
        }
    }
}
