using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Interop;

// Registrations belong to chords, not actions. Swapping two actions can retain
// their existing native registrations, and a failed update never drops old keys.
internal sealed class HudQuickHotkeys : IDisposable
{
    internal static readonly (string Id,string Zh,string En)[] Actions={
        ("previous","上一方案","Previous profile"),("next","下一方案","Next profile"),
        ("pause","暂停／继续轮播","Pause / resume auto cycle"),
        ("preview","预览当前方案","Preview current profile"),("settings","打开设置","Open Settings")};
    private readonly Action<string> _execute;
    private readonly Func<string,Action,IDisposable?> _bind;
    private readonly Dictionary<string,IDisposable> _registrations=new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string,string> _routes=new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    internal string ErrorGesture {get;private set;}="";
    internal HudQuickHotkeys(Action<string> execute,Func<string,Action,IDisposable?>? bind=null)
    {
        _execute=execute;_bind=bind??((chord,pressed)=>{
            var service=new DesktopGlobalHotkey(pressed);
            if(service.TryUpdate(chord))return service;service.Dispose();return null;
        });
    }
    internal static Dictionary<string,string> Normalize(Dictionary<string,string>? values)=>Actions.ToDictionary(a=>a.Id,a=>
        values is not null && values.TryGetValue(a.Id,out var v) && HudHotkeyGesture.TryParse(v,out var chord)?chord.Text:"");
    internal string BindingFor(string action)=>_routes.FirstOrDefault(p=>p.Value==action).Key??"";
    internal bool TryApply(AppSettings settings,Action persist)
    {
        if(_disposed)return false;ErrorGesture="";
        var wanted=Actions.ToDictionary(a=>a.Id,a=>settings.QuickHotkeys?.GetValueOrDefault(a.Id)??"");wanted["toggle"]=settings.HudToggleHotkey;
        var routes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var (id,text) in wanted)
        {
            if(string.IsNullOrWhiteSpace(text))continue;
            if(!HudHotkeyGesture.TryParse(text,out var chord) || !routes.TryAdd(chord.Text,id)){ErrorGesture=text;return false;}
        }
        var staged=new Dictionary<string,IDisposable>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach(var chord in routes.Keys.Where(c=>!_registrations.ContainsKey(c)))
            {
                var key=chord;var registration=_bind(key,()=>{if(!_disposed && _routes.TryGetValue(key,out var action))_execute(action);});
                if(registration is null){ErrorGesture=key;return false;}staged.Add(key,registration);
            }
            persist();
            foreach(var item in staged)_registrations.Add(item.Key,item.Value);
            staged.Clear();_routes=routes;
            foreach(var key in _registrations.Keys.Where(c=>!routes.ContainsKey(c)).ToArray()){_registrations[key].Dispose();_registrations.Remove(key);}
            return true;
        }
        finally{foreach(var registration in staged.Values)registration.Dispose();}
    }
    public void Dispose(){if(_disposed)return;_disposed=true;foreach(var item in _registrations.Values)item.Dispose();_registrations.Clear();_routes.Clear();}
}
