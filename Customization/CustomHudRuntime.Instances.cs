using Avalonia.Threading;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    private readonly Dictionary<string, (HudWindow Hud, CustomHudRuntime Runtime)> _instances = new();
    private bool _isChild;
    private bool _editingPreview;
    private bool _settingsEditing;
    private string _editingInstanceId="";
    private string _editingInstanceName="";
    private void SyncInstances(AppSettings settings)
    {
        if (_isChild || _disposed) return;
        var enabled = settings.MultiHudEnabled ? (settings.HudInstances ?? new()).Where(i => i is not null && i.Enabled && !string.IsNullOrWhiteSpace(i.Id)).GroupBy(i=>i.Id).Select(g=>g.First()).Take(15).ToList() : new();
        var ids = enabled.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _instances.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            var entry = _instances[id]; _instances.Remove(id);
            entry.Runtime.Dispose(); if (!entry.Hud.IsClosed) entry.Hud.Close();
        }
        foreach (var instance in enabled)
        {
            var local = instance.Settings with { MultiHudEnabled=false, HudInstances=new(),
                HudEnabled=settings.HudEnabled && instance.Settings.HudEnabled,
                SamplingIntervalSeconds=settings.SamplingIntervalSeconds, ClickToCycle=settings.ClickToCycle, UiLanguage=settings.UiLanguage,
                HudHotkeyShowAnimation=settings.HudHotkeyShowAnimation,
                GlobalFontScale=settings.GlobalFontScale,GlobalUiScale=settings.GlobalUiScale };
            if (_instances.TryGetValue(instance.Id, out var existing) && !existing.Hud.IsClosed)
            {
                if (System.Text.Json.JsonSerializer.Serialize(existing.Runtime._appSettings)!=System.Text.Json.JsonSerializer.Serialize(local))
                {
                    existing.Runtime.ApplySettings(local);
                    existing.Hud.ApplySettings(existing.Runtime._editingPreview ? local with { PersistentLayer=PersistentHudLayer.Topmost } : local);
                }
            }
            else
            {
                _instances.Remove(instance.Id);
                HudWindow? hud=null; CustomHudRuntime? child=null;
                try
                {
                    hud=new HudWindow(); hud.ApplySettings(local);
                    child=new CustomHudRuntime(hud,_variables) { _isChild=true };
                    child.IsUserHidden=IsUserHidden;
                    child.ApplySettings(local); _instances.Add(instance.Id,(hud,child)); child.Start();
                }
                catch (Exception ex)
                {
                    child?.Dispose(); if (hud is { IsClosed:false }) hud.Close();
                    EndfieldChargePlus.Diagnostics.AppLog.Error("Unable to create HUD instance.",ex);
                }
            }
        }
    }

    private void RestoreInstanceHighlight()
    {
        if (_isChild) return; // Only the coordinator owns the Settings highlight.
        if (!_settingsEditing || _instances.Count==0) { ClearInstanceHighlights(); return; }
        var target=string.IsNullOrEmpty(_editingInstanceId) ? (_hud,this) :
            _instances.TryGetValue(_editingInstanceId,out var found) ? (found.Hud,found.Runtime) : default;
        if(target.Item1 is null || target.Item1.IsClosed || target.Item2._disposed) { ClearInstanceHighlights(); return; }
        // An unapplied/deleted target must not leave an old HUD marked as current.
        if(!ReferenceEquals(target.Item2,this)) EndPreview(this);
        foreach(var instance in _instances.Values.ToArray())
            if(!ReferenceEquals(target.Item2,instance.Runtime)) EndPreview(instance.Runtime);
        target.Item2._editingPreview=true;
        target.Item1.SetEditingHighlight(true,_editingInstanceName);
        target.Item1.ApplySettings(target.Item2._appSettings with { PersistentLayer=PersistentHudLayer.Topmost });
    }
    public HudWindow? GetInstanceHud(string id) => string.IsNullOrEmpty(id) ? (_hud.IsClosed ? null : _hud) :
        _instances.TryGetValue(id,out var found) && !found.Hud.IsClosed ? found.Hud : null;
    public Task PreviewInstanceAsync(string id,HudRenderData data,Func<CancellationToken,Task<HudRenderData>> refresh)
    {
        var target=string.IsNullOrEmpty(id) ? this : _instances.TryGetValue(id,out var found) ? found.Runtime : null;
        if(target is null || target._disposed) return Task.CompletedTask;
        target.CancelRevision();
        return target.RunSerializedAsync(async ()=> {
            if (target.IsUserHidden) return;
            if(target._editingPreview) await target._hud.ShowPersistentAsync(data,refresh);
            else await target._hud.ShowCustomAsync(data,refresh);
            target._persistentShown=false;target._nextPersistentRefresh=DateTime.MinValue;
        },wait:true);
    }
    public async Task<bool> HighlightInstanceAsync(string id, string name)
    {
        if (_disposed) return false;
        _settingsEditing=true;_editingInstanceId=id;_editingInstanceName=name;
        if (_instances.Count==0) { ClearInstanceHighlights(); return false; }
        var target = string.IsNullOrEmpty(id) ? (_hud, this) :
            _instances.TryGetValue(id, out var found) ? (found.Hud, found.Runtime) : default;
        if (target.Item1 is null || target.Item1.IsClosed || target.Item2._disposed)
        {
            ClearInstanceHighlights();
            return false;
        }
        // Re-selecting or localizing the editor must not cancel a live profile
        // transition, reset its queue, or summon the same HUD again.
        if (target.Item2._editingPreview && target.Item1.IsEditingHighlighted)
        {
            target.Item1.SetEditingHighlight(true,name);
            return !target.Item1.IsClosed;
        }
        if (!ReferenceEquals(target.Item2,this)) EndPreview(this);
        foreach(var instance in _instances.Values.ToArray())
            if (!ReferenceEquals(target.Item2,instance.Runtime)) EndPreview(instance.Runtime);
        target.Item2._editingPreview=true;
        target.Item1.ApplySettings(target.Item2._appSettings with { PersistentLayer=PersistentHudLayer.Topmost });
        target.Item2._nextPersistentRefresh=DateTime.MinValue;
        target.Item1.SetEditingHighlight(true, name);
        await target.Item2.RunSerializedAsync(target.Item2.TickPersistentAsync,wait:true);
        return !target.Item1.IsClosed && !target.Item2._disposed;
    }

    public void EndInstanceEditing()
    {
        _settingsEditing=false;
        ClearInstanceHighlights();
    }
    private void ClearInstanceHighlights()
    {
        if (_disposed) return;
        EndPreview(this);
        foreach(var instance in _instances.Values.ToArray()) EndPreview(instance.Runtime);
    }
    private static void EndPreview(CustomHudRuntime runtime)
    {
        if (runtime._disposed) return;
        bool wasEditing=runtime._editingPreview;
        runtime._editingPreview=false;
        runtime._hud.SetEditingHighlight(false, "");
        if (!wasEditing) return;
        runtime.CancelRevision();
        runtime._hud.ApplySettings(runtime._appSettings);
        runtime._nextPersistentRefresh=DateTime.MinValue;
        runtime._lastRenderedLocalSecond=-1;
        runtime.RunSerializedAsync(runtime.TickAsync,wait:true);
    }

    private void DisposeInstances()
    {
        var instances=_instances.Values.ToArray(); _instances.Clear();
        foreach (var instance in instances) { instance.Runtime.Dispose(); if (!instance.Hud.IsClosed) instance.Hud.Close(); }
    }
}
