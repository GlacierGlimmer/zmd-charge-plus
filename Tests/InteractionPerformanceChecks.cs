using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;

internal static class InteractionPerformanceChecks
{
    private sealed class Registration : IDisposable {internal bool Disposed; public void Dispose()=>Disposed=true;}
    internal static async Task Run(Window owner,Action<bool,string> check)
    {
        string original=LocalizationManager.Preference;
        var callbacks=new Dictionary<string,Action>();var registrations=new Dictionary<string,Registration>();
        var events=new List<string>();
        using(var keys=new HudQuickHotkeys(events.Add,(key,pressed)=>{callbacks[key]=pressed;return registrations[key]=new();}))
        {
            var settings=new AppSettings{QuickHotkeys=new(){["next"]="Alt+M"}};
            check(keys.TryApply(settings,()=>{}),"initial fake shortcut route registered");
            var pending=new TaskCompletionSource();
            var saving=keys.TryApplyAsync(settings with{QuickHotkeys=new(){["next"]="Ctrl+Alt+N"}},()=>pending.Task);
            callbacks["Alt+M"]();callbacks["Ctrl+Alt+N"]();await Task.Delay(25);
            check(!saving.IsCompleted && events.SequenceEqual(new[]{"next"}),"pending background save leaves existing shortcuts active and staged route inactive");
            pending.SetResult();check(await saving && registrations["Alt+M"].Disposed,"successful save commits new route and releases previous registration");
            callbacks["Ctrl+Alt+N"]();check(events.Count==2,"committed shortcut routes correctly");
            try{await keys.TryApplyAsync(settings,()=>Task.FromException(new IOException("Expected isolated persistence failure")));check(false,"expected write failure");}catch(IOException){}
            check(keys.BindingFor("next")=="Ctrl+Alt+N" && registrations["Alt+M"].Disposed,"failed async save retains previous route and disposes staged registration");
        }
        var before=VariableCatalog.AllBuiltIns;
        check(ReferenceEquals(before,VariableCatalog.AllBuiltIns),"stable catalogue snapshot is reused");
        var sensor=new HardwareReading("test","ecp-audit-stable","Audit","Value","Load","%",12d);
        HardwareTelemetryCatalog.Publish(new[]{sensor});var first=VariableCatalog.AllBuiltIns;
        HardwareTelemetryCatalog.Publish(new[]{sensor with{Value=67d}});
        check(ReferenceEquals(first,VariableCatalog.AllBuiltIns),"sensor value changes do not rebuild metadata catalogue");
        HardwareTelemetryCatalog.Publish(new[]{sensor with{Name="Updated audit"}});
        check(!ReferenceEquals(first,VariableCatalog.AllBuiltIns) && VariableCatalog.Find(sensor.Key+".value")?.Name.Contains("Updated audit")==true,"sensor metadata changes invalidate catalogue and lookup index");
        // The process continues with live-hardware completeness checks: remove only this synthetic fixture.
        var metadata=typeof(HardwareTelemetryCatalog);
        var gate=metadata.GetField("Gate",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        lock(gate)
        {
            var known=(Dictionary<string,VariableDefinition>)metadata.GetField("Known",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            foreach(var key in known.Keys.Where(k=>k.StartsWith(sensor.Key+".",StringComparison.Ordinal)).ToArray())known.Remove(key);
            metadata.GetField("_snapshot",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,null);
        }
        var translations=new Dictionary<string,string>();
        foreach(var code in new[]{"zh-CN","en-US","fr-FR","ja-JP","ar-SA","zh-TW"}){LocalizationManager.SetPreference(code);translations[code]=LocalizationManager.Text("保存并应用","Save & Apply");}
        LocalizationManager.TrimUnusedResources();
        foreach(var (code,text) in translations){LocalizationManager.SetPreference(code);check(LocalizationManager.Text("保存并应用","Save & Apply")==text,"locale survives idle cache eviction: "+code);}
        LocalizationManager.SetPreference("zh-CN");
        var picker=new LanguageSelectionWindow(()=>new AppSettings(),LocalizationManager.SetPreference,(_,_)=>Task.CompletedTask);picker.Show(owner);await Task.Delay(90);
        try
        {
            var list=picker.GetLogicalDescendants().OfType<ListBox>().Single(x=>x.Name=="LanguageList");
            list.ScrollIntoView(list.Items[1]!);await Task.Delay(35);
            var stable=list.ContainerFromIndex(1);
            foreach(var index in new[]{2,3,4,2,1})
            {
                list.SelectedIndex=index;await Task.Delay(35);
                check(ReferenceEquals(stable,list.ContainerFromIndex(1)),"language selection preserves existing language row containers");
                var realized=list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                check(realized.Count(x=>x.IsSelected)==1,"exactly one language container is selected");
                check(realized.SelectMany(x=>x.GetVisualDescendants().OfType<ContentPresenter>()).All(x=>x.Transitions is null || x.Transitions.Count==0),"language row template has no stale brush animation");
            }
            foreach(var name in new[]{"GlobalAppearanceButton","LanguageDoneButton"})
            {
                var button=picker.GetLogicalDescendants().OfType<Button>().Single(x=>x.Name==name);
                check(button.VerticalContentAlignment==VerticalAlignment.Center,"language footer content vertically centered: "+name);
            }
            if(OperatingSystem.IsWindows())
            {
                foreach(var index in new[]{2,3,4,2})
                {
                    var item=(ListBoxItem)list.ContainerFromIndex(index)!;
                    var target=item.PointToScreen(new Point(item.Bounds.Width/2,item.Bounds.Height/2));var origin=picker.PointToScreen(default);
                    RawMouse(picker,Avalonia.Input.Raw.RawPointerEventType.Move,picker.PointToClient(target));
                    await Task.Delay(30);
                    var hovered=list.GetVisualDescendants().OfType<ListBoxItem>().Where(x=>x.IsPointerOver).ToArray();
                    check(hovered.Length==1 && ReferenceEquals(hovered[0],item),"raw mouse pipeline highlights exactly the row under the pointer: target="+index+" hovered="+string.Join(",",hovered.Select(x=>list.IndexFromContainer(x)))+" targetScreen="+target+" origin="+origin+" bounds="+item.Bounds);
                }
                RawMouse(picker,Avalonia.Input.Raw.RawPointerEventType.LeaveWindow,new Point(-1,-1));await Task.Delay(30);
                check(!list.GetVisualDescendants().OfType<ListBoxItem>().Any(x=>x.IsPointerOver),"raw mouse leave clears all language hover states");
            }
            var close=picker.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="PromptWindowClose");
            check(close.Content is Avalonia.Controls.Shapes.Path && close.VerticalContentAlignment==VerticalAlignment.Center,"close icon is a centered vector instead of a font glyph");
            var example=new TextBlock{Text="Scale reset",FontSize=16};var sample=new Window{Content=new StackPanel{Children={example}},Width=300,Height=200};
            sample.Show(owner);GlobalAppearance.Apply(sample,1.25,1);await Task.Delay(50);
            check(Math.Abs(example.FontSize-20)<.01,"font scaling still applies with optimized default-size layout");
            GlobalAppearance.Apply(sample,1,1);await Task.Delay(50);
            check(Math.Abs(example.FontSize-16)<.01,"font size resets exactly to original metrics");sample.Close();
        }
        finally {picker.Close();LocalizationManager.SetPreference(original);}
    }
    private static void RawMouse(Window window,Avalonia.Input.Raw.RawPointerEventType type,Point point)
    {
        var impl=(Avalonia.Platform.IWindowImpl)typeof(TopLevel).GetProperty("PlatformImpl",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(window)!;
        var device=(Avalonia.Input.IInputDevice)impl.GetType().GetField("_mouseDevice",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(impl)!;
        var constructor=typeof(Avalonia.Input.Raw.RawPointerEventArgs).GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(c=>c.GetParameters().Length==6 && c.GetParameters()[4].ParameterType==typeof(Point));
        var input=(Action<Avalonia.Input.Raw.RawInputEventArgs>)typeof(Avalonia.Platform.ITopLevelImpl).GetProperty("Input",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(impl)!;
        input((Avalonia.Input.Raw.RawPointerEventArgs)constructor.Invoke(new object[]{device,(ulong)0,window,type,point,Avalonia.Input.RawInputModifiers.None}));
    }
}
