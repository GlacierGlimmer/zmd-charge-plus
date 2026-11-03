using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static partial class Audit
{
    private static void State(Control c,string name,bool enabled)
    {
        if(enabled)((IPseudoClasses)c.Classes).Add(name);else ((IPseudoClasses)c.Classes).Remove(name);
    }
    private static string Fill(Border b)=>(b.Background as ISolidColorBrush)?.Color.ToString()??"none";
    private static async Task Until(Func<bool> check,int milliseconds=8000)
    {
        var clock=Stopwatch.StartNew();while(!check() && clock.ElapsedMilliseconds<milliseconds)await Task.Delay(20);
        Check(check(),"bounded UI condition reached");
    }
    private static async Task PerformanceChecks(string output)
    {
        using var refreshTrace=new StringWriter();using var trace=new TextWriterTraceListener(refreshTrace);System.Diagnostics.Trace.Listeners.Add(trace);System.Diagnostics.Trace.AutoFlush=true;
        LocalizationManager.Initialize("zh-CN");
        Check(typeof(GlobalAppearance).GetField("_windowWatcher",Private|System.Reflection.BindingFlags.Static) is null,"no idle whole-window appearance polling");
        var profiles=Enumerable.Range(0,3).Select(i=>new HudProfile{Id="perf-"+i,Name="很长的方案名称 / A long profile name "+i,IsBuiltIn=false,PrimaryTemplate="SAFE "+i,SecondaryTemplate="",ProgressVariable="=50",AnimationMode="Simple",AccentColor=i==0?"#01FFA2":"#FF1AAC"}).ToList();
        var settings=new AppSettings{HudEnabled=false,AlwaysVisible=true,ClickToCycle=true,PersistentLayer=PersistentHudLayer.Topmost,CustomHud=new(){Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList()}};
        using(var usageOnly=new VariableHub())
        {
            var values=await usageOnly.SnapshotAsync(settings.CustomHud,new[]{"CPU.USAGE","cpu.user_usage","cpu.kernel_usage","cpu.idle_percent"});
            Check(values.ContainsKey("cpu.usage"),"CPU usage remains available without WMI metadata");
            Check(Field<DateTime>(usageOnly,"_nextCpuInfoRead")==DateTime.MinValue && Field<DateTime>(usageOnly,"_nextCpuFrequencyRead")==DateTime.MinValue,"usage-only sampling does not read processor metadata or frequency");
        }
        var hud=new HudWindow();hud.ApplySettings(settings);using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        var window=new SettingsWindow(settings,hud,runtime){PersistLanguagePreference=_=>{}};
        Field<CancellationTokenSource>(window,"_statusLifetime").Cancel();window.Show();await Task.Delay(150);
        var editor=window.FindControl<HudCustomizerView>("Customizer")!;
        int discoveries=0;editor.DiscoverHardware=()=>{discoveries++;return Task.CompletedTask;};
        window.NavigateToSettingsSection("hud");await Task.Delay(150);
        Check(discoveries==0,"opening profiles does not initialize all hardware sensors");
        var library=editor.GetVisualDescendants().OfType<ListBox>().First(l=>l.Name=="ProfileWorkspaceList");
        var label=library.Items.OfType<ListBoxItem>().First().GetLogicalDescendants().OfType<TextBlock>().First();
        Check(label.TextWrapping==TextWrapping.NoWrap && label.TextTrimming==TextTrimming.CharacterEllipsis,"long profile names stay on one line with ellipsis");
        window.NavigateToSettingsSection("quick");await Task.Delay(80);
        var group=(Border)window.FindControl<StackPanel>("QuickActionsPanel")!.Children[0];
        Check(window.FindControl<TextBox>("HudHotkeyBox")!.GetVisualAncestors().Contains(group),"toggle shortcut inside one keyboard card");
        Check(window.GetVisualDescendants().OfType<TextBox>().Where(c=>c.Name?.StartsWith("Shortcut_")==true).All(c=>c.GetVisualAncestors().Contains(group)),"all five extra keyboard actions inside same card");
        window.NavigateToSettingsSection("cycle");await Task.Delay(80);
        var queue=editor.FindControl<ListBox>("CycleProfileList")!;
        var items=queue.Items.OfType<ListBoxItem>().Take(3).ToArray();
        foreach(var item in items)
        {
            item.ApplyTemplate();var border=item.GetVisualDescendants().OfType<Border>().First();
            State(item,":selected",false);State(item,":pointerover",false);await Task.Delay(20);string normal=Fill(border);
            State(item,":pointerover",true);await Task.Delay(200);string hover=Fill(border);
            Check(hover!=normal,"pointer entry highlights the correct queue item");
            for(int i=0;i<4;i++){WorkspaceTheme.ApplyLegacyColours(window);GlobalAppearance.Apply(window,1,1);await Task.Delay(60);File.AppendAllText(Path.Combine(output,"pointer-colours.txt"),$"{normal} {hover} {Fill(border)} template={border.TemplatedParent?.GetType().Name} transitions={item.Transitions?.Count}\n");Check(Fill(border)==hover,"moving/stationary hover survives appearance updates");}
            State(item,":pointerover",false);await Task.Delay(20);Check(Fill(border)==normal,"pointer leave restores the row exactly");
        }
        foreach(var control in window.GetVisualDescendants().OfType<TemplatedControl>().Where(c=>c is Button or ComboBox or CheckBox or ToggleSwitch).Take(45))
        {
            State(control,":pointerover",true);await Task.Delay(220);
            var borders=control.GetVisualDescendants().OfType<Border>().Take(3).ToArray();var before=borders.Select(Fill).ToArray();
            WorkspaceTheme.ApplyLegacyColours(window);await Task.Delay(5);Check(before.SequenceEqual(borders.Select(Fill)),"template hover colours stay owned by styles: "+control.GetType().Name);
            State(control,":pointerover",false);
        }
        runtime.Start();var enabled=settings with{HudEnabled=true};
        var timing=Stopwatch.StartNew();await runtime.ApplySavedSettingsAsync(enabled);
        Check(timing.ElapsedMilliseconds<500,"Save applies preferences without awaiting full HUD intro");
        await Task.Delay(600);
        File.WriteAllText(Path.Combine(output,"applied-state.txt"),$"visible={hud.IsVisible} busy={hud.IsHudBusy} rolling={hud.IsRolling} shown={Field<bool>(runtime,"_persistentShown")} last={Field<string>(runtime,"_persistentProfileId")} instant={Field<bool>(runtime,"_instantSettingsRefresh")} timer={Field<DispatcherTimer>(runtime,"_timer").IsEnabled} transition={Field<int>(runtime,"_settingsTransitionBusy")} sampler={Field<int>(runtime,"_busy")} operation={Field<Task>(runtime,"_lastOperation").Status}\n");
        await Until(()=>hud.IsVisible && !hud.IsHudBusy && Field<string>(runtime,"_persistentProfileId")==profiles[0].Id && !Field<bool>(runtime,"_instantSettingsRefresh"));
        // Wheel steps use their own direction, independently of the auto-cycle animation option.
        window.NavigateToSettingsSection("hud");await Task.Delay(60);
        void Trace(string stage) { File.AppendAllText(Path.Combine(output,"wheel-state.txt"),$"{stage}: visible={hud.IsVisible} busy={hud.IsHudBusy} rolling={hud.IsRolling} shown={Field<bool>(runtime,"_persistentShown")} last={Field<string>(runtime,"_persistentProfileId")} manual={Field<string>(runtime,"_manualProfileId")} instant={Field<bool>(runtime,"_instantSettingsRefresh")} enabled={Field<AppSettings>(runtime,"_appSettings").HudEnabled} HUDpersistent={Field<bool>(hud,"_persistent")}\n"); }
        Trace("before");runtime.SwitchProfile(1,true);Trace("after");try{await Until(()=>hud.IsRolling);}catch{Trace("timeout");throw;}await Task.Delay(120);Trace("roll");
        var host=hud.FindControl<Grid>("ContentHost")!;
        Check(host.RenderTransform is TranslateTransform {Y:>0},"wheel down rolls content downward");
        await Until(()=>!hud.IsHudBusy);await Task.Delay(60);
        Check(editor.FindControl<TextBox>("ProfileNameBox")!.Text==profiles[1].Name,"wheel selection updates profile manager");
        runtime.SwitchProfile(-1,true);await Until(()=>hud.IsRolling);await Task.Delay(120);
        Check(host.RenderTransform is TranslateTransform {Y:<0},"wheel up rolls content upward");
        await Until(()=>!hud.IsHudBusy);await Task.Delay(60);
        Check(editor.FindControl<TextBox>("ProfileNameBox")!.Text==profiles[0].Name,"previous selection updates profile manager");
        Check(Field<HudRenderData?>(hud,"_lastRenderData")?.AccentColor=="#01FFA2","cycling back preserves the profile's saved colour");
        typeof(CustomHudRuntime).GetMethod("OnRadialFinished",Private)!.Invoke(runtime,new object?[]{profiles[2].Id});await Task.Delay(120);
        Check(editor.FindControl<TextBox>("ProfileNameBox")!.Text==profiles[2].Name,"radial selection updates profile manager");
        runtime.Stop();
        trace.Flush();File.WriteAllText(Path.Combine(output,"refresh-trace.txt"),refreshTrace.ToString());Check(!refreshTrace.ToString().Contains("Persistent HUD refresh failed"),"initial display and wheel refresh do not query coordinates before visual attachment");
        var clip=hud.FindControl<Border>("ContentClip")!;
        Check(clip.ClipToBounds && clip.CornerRadius.TopLeft>0,"dynamic content uses a rounded clip");
        bool held=true;hud.ShouldHoldTransient=()=>held;
        using(var cancellation=new CancellationTokenSource())
        {
            var hold=(Task)typeof(HudWindow).GetMethod("WaitForTransientHoverAsync",Private)!.Invoke(hud,new object[]{cancellation.Token})!;
            await Task.Delay(180);Check(!hold.IsCompleted,"hover extends transient visibility");held=false;
            await Task.Delay(120);Check(!hold.IsCompleted,"leaving restarts the display interval");
            cancellation.Cancel();try{await hold;}catch(OperationCanceledException){}
        }
        window.Close();hud.Close();System.Diagnostics.Trace.Listeners.Remove(trace);
    }
}
