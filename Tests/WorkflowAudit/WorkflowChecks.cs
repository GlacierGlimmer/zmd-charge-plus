using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static partial class Audit
{
    private sealed class BindingLease(Action dispose):IDisposable{public void Dispose()=>dispose();}
    private static async Task WorkflowChecks(string output)
    {
        var keys=new Dictionary<string,Action>();int disposed=0,persisted=0;string executed="";
        using(var hotkeys=new HudQuickHotkeys(action=>executed=action,(chord,pressed)=>{
            if(chord=="Ctrl+Q")return null;keys.Add(chord,pressed);return new BindingLease(()=>{keys.Remove(chord);disposed++;});
        }))
        {
            AppSettings Bind(string previous,string next)=>new(){QuickHotkeys=new(){{"previous",previous},{"next",next}}};
            Check(hotkeys.TryApply(Bind("Ctrl+A","Ctrl+B"),()=>persisted++),"register initial action map");
            keys["Ctrl+A"]();Check(executed=="previous","first action dispatch");
            Check(hotkeys.TryApply(Bind("Ctrl+B","Ctrl+A"),()=>persisted++),"swap two owned shortcuts atomically");
            keys["Ctrl+A"]();Check(executed=="next" && disposed==0 && keys.Count==2,"swapped routes reuse registrations");
            Check(!hotkeys.TryApply(Bind("Ctrl+A","Ctrl+A"),()=>persisted++),"duplicate actions rejected");
            Check(!hotkeys.TryApply(Bind("Ctrl+C","Ctrl+Q"),()=>persisted++),"external conflict rejected");
            Check(persisted==2 && keys.Count==2 && !keys.ContainsKey("Ctrl+C"),"failed update leaves old keys and removes staged keys");
            try {hotkeys.TryApply(Bind("Ctrl+C","Ctrl+B"),()=>throw new IOException("fixture"));throw new Exception("expected persistence failure");}
            catch(IOException) { }
            keys["Ctrl+A"]();Check(executed=="next" && keys.Count==2,"persistence failure retains prior routes");
            Check(hotkeys.TryApply(new(),()=>persisted++),"clear every shortcut");
            Check(keys.Count==0,"clearing releases owned registrations");
        }
        double gib=1024d*1024*1024;
        var adapters=new[]{new GpuAdapterInfo("card-a","Identical GPU",0,4*gib,0,new[]{"luid_a","luid_alias"},Array.Empty<string>()),new GpuAdapterInfo("card-b","Identical GPU",1,8*gib,0,new[]{"luid_b"},Array.Empty<string>())};
        var system=new Dictionary<string,double>{{"luid_a_phys_0",gib},{"luid_alias_phys_0",gib},{"luid_b_phys_0",6*gib}};
        var processes=new Dictionary<string,double>{{"pid_42_luid_a_phys_0",64*1024*1024},{"pid_42_luid_alias_phys_0",64*1024*1024},{"pid_42_luid_b_phys_0",128*1024*1024}};
        var gpu=WindowsStatusMetrics.BuildAdapters(adapters,system,processes,42);var summary=WindowsStatusMetrics.AggregateAdapters(gpu);
        Check(gpu.Count==2 && gpu[0].Used==gib,"LUID aliases do not duplicate one GPU");
        Check(Math.Abs(summary.System!.Value-100d*7/12)<.001,"multi-GPU percentage is capacity weighted");
        Check(summary.Bytes==192*1024*1024,"process memory sums across physical GPUs");
        var missing=WindowsStatusMetrics.BuildAdapters(adapters,new Dictionary<string,double>{{"luid_a_phys_0",gib}},null,42);
        Check(WindowsStatusMetrics.AggregateAdapters(missing).System is null && WindowsStatusMetrics.AggregateAdapters(missing).Bytes is null,"partial/unsupported readings are unavailable, never fabricated zeros");
        var unknown=new[]{new GpuStatusReading("unknown","Unknown capacity",null,null,128,false)};
        Check(WindowsStatusMetrics.AggregateAdapters(unknown).Bytes==128 && WindowsStatusMetrics.AggregateAdapters(unknown).Process is null,"known absolute VRAM does not require known capacity");
        CheckGpuIdentity();
        LocalizationManager.Initialize("zh-CN");
        var profiles=Enumerable.Range(0,3).Select(i=>new HudProfile{Id="workflow-"+i,Name="Fixture "+i,IsBuiltIn=false,PrimaryTemplate="SAFE",ProgressVariable="=50",AnimationMode="Simple"}).ToList();
        var settings=new AppSettings{HudEnabled=false,AlwaysVisible=true,ClickToCycle=true,CustomHud=new(){Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList()},Workspace=new(){RadialEnabled=false}};
        var hud=new HudWindow();hud.ApplySettings(settings);using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        runtime.SwitchProfile(-1);Check(Field<string>(runtime,"_manualProfileId")==profiles[2].Id,"up scroll wraps to previous profile");
        runtime.SwitchProfile(1);Check(Field<string>(runtime,"_manualProfileId")==profiles[0].Id,"down scroll wraps to next profile");
        runtime.SwitchProfile(5);Check(Field<string>(runtime,"_manualProfileId")==profiles[2].Id,"coalesced fast scroll preserves all steps");
        runtime.ToggleAutoCyclePaused();Check(runtime.IsAutoCyclePaused,"pause shortcut changes runtime state");runtime.ToggleAutoCyclePaused();Check(!runtime.IsAutoCyclePaused && Field<DateTime>(runtime,"_nextPersistentCycle")>DateTime.UtcNow,"resume restarts interval without catch-up bursts");
        var delivered=new List<(int Kind,int Delta)>();using(var input=new WindowsRadialInput((kind,point,delta)=>delivered.Add((kind,delta))))
        {
            input.UpdateTarget(new(100,100,300,50),false,false,true);
            Check(!WindowsRadialInput.ProcessMessage(0x207,new(200,125),0),"middle button passes when only click/scroll cycling is enabled");
            Check(WindowsRadialInput.ProcessMessage(0x20A,new(200,125),120),"wheel on HUD claimed with radial switching disabled");
            Check(!WindowsRadialInput.ProcessMessage(0x20A,new(0,0),120),"wheel outside HUD passes to other apps");await Task.Delay(50);
            Check(delivered.Count==1 && delivered[0]==(4,120),"cycle wheel delivered once on UI thread");
        }
        var window=new SettingsWindow(settings,hud,runtime){PersistLanguagePreference=_=>{}};Field<CancellationTokenSource>(window,"_statusLifetime").Cancel();window.Show();await Task.Delay(100);
        var editor=window.FindControl<HudCustomizerView>("Customizer")!;
        window.NavigateToSettingsSection("hud");await Task.Delay(80);
        Check(editor.GetVisualDescendants().OfType<ListBox>().Any(c=>c.Name=="ProfileWorkspaceList"),"profile manager uses searchable icon list");
        var combo=editor.FindControl<ComboBox>("ProfileCombo")!;var name=editor.FindControl<TextBox>("ProfileNameBox")!;
        File.WriteAllText(Path.Combine(output,"selection.txt"),$"initial {combo.SelectedIndex}/{Field<int>(editor,"_selectedIndex")} {name.Text}\n");
        int original=combo.SelectedIndex;int other=Field<List<HudProfile>>(editor,"_profiles").FindIndex(p=>p.Id==profiles[1].Id);
        name.Text="Retained draft";combo.SelectedIndex=other;await Task.Delay(120);
        File.AppendAllText(Path.Combine(output,"selection.txt"),$"second {combo.SelectedIndex}/{Field<int>(editor,"_selectedIndex")} {name.Text}\n");
        combo.SelectedIndex=original;await Task.Delay(120);
        File.AppendAllText(Path.Combine(output,"selection.txt"),$"return {combo.SelectedIndex}/{Field<int>(editor,"_selectedIndex")} {name.Text}\n"+string.Join("\n",Field<List<HudProfile>>(editor,"_profiles").Select(p=>p.Id+" "+p.Name)));
        Check(name.Text=="Retained draft","changing profile retains editor changes in this window");
        window.NavigateToSettingsSection("cycle");await Task.Delay(30);
        var click=editor.FindControl<CheckBox>("PersistentClickSwitch")!;
        var cycleClick=Field<CheckBox>(editor,"_cycleClickSwitch");
        click.IsChecked=false;Check(cycleClick.IsChecked==false,"quick action updates cycle duplicate");cycleClick.IsChecked=true;Check(click.IsChecked==true,"cycle duplicate updates quick action");
        var gauges=Field<StatusGauge[]>(window,"_statusGauges");
        var sample=new SettingsStatusSample(25,50,summary.System,2,1,summary.Process,512*1024*1024,summary.Bytes){TotalMemoryBytes=8*gib,Adapters=gpu};
        window.ApplyStatusSample(sample);var tip=ToolTip.GetTip(gauges[2]);window.ApplyStatusSample(sample with{Cpu=30});Check(ReferenceEquals(tip,ToolTip.GetTip(gauges[2])),"live samples retain the same open tooltip object");
        Check(tip is TextBlock text && text.Text!.Contains("GPU 0") && text.Text.Contains("GPU 1") && text.Text.Contains("Identical GPU"),"GPU tooltip distinguishes identical cards and shows device details");
        for(int i=0;i<6;i++)
        {
            var point=gauges[i].TranslatePoint(new Point(gauges[i].Bounds.Width-2,30),window)!.Value;
            Check(ReferenceEquals(window.GetVisualAt(point),gauges[i]),"full gauge hit region "+i);
        }
        window.NavigateToSettingsSection("quick");await Task.Delay(50);
        var recorder=window.GetVisualDescendants().OfType<TextBox>().Single(b=>b.Name=="Shortcut_next");window.BeginShortcutRecording(recorder);
        Check(window.CaptureRegisteredShortcut("Ctrl+Alt+J") && recorder.Text=="Ctrl+Alt+J","already registered shortcuts are captured without firing their action");
        foreach(var language in LocalizationManager.SupportedLanguages.Select(l=>l.Code))
        foreach(var scale in new[]{(.8,.8),(1d,1d),(1.25,1.5)})
        {
            LocalizationManager.Initialize(language);
            typeof(SettingsWindow).GetMethod("OnLanguageSelected",Private)!.Invoke(window,new object[]{language});
            GlobalAppearance.Apply(window,scale.Item1,scale.Item2);window.NavigateToSettingsSection("about");await Task.Delay(20);
            var rows=window.FindControl<StackPanel>("AboutGeneralLinks")!.Children.OfType<Grid>().ToArray();
            Check(rows.Length==3 && rows.Zip(rows.Skip(1)).All(pair=>pair.First.Bounds.Bottom<=pair.Second.Bounds.Top),"About rows do not overlap "+language+scale);
            Check(rows.All(row=>row.Bounds.Height>=35),"About row hit targets remain readable "+language+scale);
            window.NavigateToSettingsSection("hud");await Task.Delay(10);
            Check(editor.GetVisualDescendants().OfType<ListBox>().Any(l=>l.Name=="ProfileWorkspaceList" && l.Bounds.Width>100),"profile pane visible "+language+scale);
            window.NavigateToSettingsSection("cycle");await Task.Delay(10);Check(cycleClick.Content is TextBlock label && label.Text==LocalizationManager.Text("点击或滚轮切换方案","Click or scroll to switch profiles"),"cycle duplicate localized "+language+scale);
            window.NavigateToSettingsSection("quick");await Task.Delay(10);Check(window.GetVisualDescendants().OfType<TextBox>().Count(t=>t.Name?.StartsWith("Shortcut_")==true)==5,"five new shortcut editors "+language+scale);
        }
        LocalizationManager.Initialize("zh-CN");typeof(SettingsWindow).GetMethod("OnLanguageSelected",Private)!.Invoke(window,new object[]{"zh-CN"});GlobalAppearance.Apply(window,1,1);
        foreach(string page in new[]{"hud","cycle","quick","about"}){window.NavigateToSettingsSection(page);await Task.Delay(350);using var image=new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize,window.RenderScaling));image.Render(window);image.Save(Path.Combine(output,page+".png"));}
        window.Close();hud.Close();
    }
    private static void CheckGpuIdentity()
    {
        var catalog=typeof(GpuAdapterCatalog);var flags=BindingFlags.NonPublic|BindingFlags.Static;
        var type=catalog.GetNestedType("DxgiCandidate",BindingFlags.NonPublic)!;var wmi=catalog.GetNestedType("WmiController",BindingFlags.NonPublic)!;
        var dxgi=Array.CreateInstance(type,2);var controllers=Array.CreateInstance(wmi,2);
        for(int i=0;i<2;i++)
        {
            dxgi.SetValue(Activator.CreateInstance(type,new object[]{"dxgi:"+i,"Identical GPU",i,0,8d*1024*1024*1024,0d,"luid_"+i,0x10deu,1u,1u,1u}),i);
            controllers.SetValue(Activator.CreateInstance(wmi,new object[]{"Identical GPU","PCI\\GPU-"+i,8d*1024*1024*1024,0x10deu,1u,1u}),i);
        }
        var method=catalog.GetMethod("BuildPhysicalCatalog",flags)!;
        var result=(IReadOnlyList<GpuAdapterInfo>)method.Invoke(null,new object[]{dxgi,controllers})!;
        Check(result.Count==2 && result.Select(g=>g.Id).Distinct().Count()==2,"identical model cards retain distinct PNP identities");
        var single=Array.CreateInstance(wmi,1);single.SetValue(controllers.GetValue(0),0);result=(IReadOnlyList<GpuAdapterInfo>)method.Invoke(null,new object[]{dxgi,single})!;
        Check(result.Count==1 && result[0].PerfLuidTokens.Count==2,"one physical controller retains duplicate logical LUID aliases without duplicate cards");
    }
}
