using System.Reflection;
using EndfieldChargePlus;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class InteractionRegressionChecks
{
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    internal static void Standalone(Action<bool,string> check)
    {
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(180));Exception? failure=null;
        Avalonia.Threading.Dispatcher.UIThread.Post(async ()=> { try { await Run(check); } catch(Exception ex) { failure=ex; } finally { finish.Cancel(); } });
        Avalonia.Threading.Dispatcher.UIThread.MainLoop(finish.Token);
        if(failure is not null) throw failure;
    }
    internal static async Task Run(Action<bool,string> check)
    {
        var screen=new PixelRect(-1920,-100,1920,1080);var work=new PixelRect(-1880,-70,1880,1010);
        var absolute=HudPlacement.VisibleOrigin(new() { HudPosition=HudPosition.ScreenBottomLeft },screen,work,448,48);
        var safe=HudPlacement.VisibleOrigin(new() { HudPosition=HudPosition.BottomLeft },screen,work,448,48);
        check(absolute==new PixelPoint(-1920,932),"Absolute bottom-left uses full monitor bounds, ignoring taskbar and negative monitor origin.");
        check(safe==new PixelPoint(-1864,876),"Existing bottom-left respects the work area, including a side/top/bottom reserved strip.");
        check((int)HudPosition.BottomRight==8 && (int)HudPosition.ScreenBottomRight==17,"Position additions preserve stored numeric values.");
        var fps=CustomHudSettings.CreateDefault().Profiles.Single(p=>p.BuiltInKey=="system.window-fps");
        check(BuiltInProfileLocalization.ForLanguage(fps,AppLanguage.SimplifiedChinese).Name.EndsWith("（需管理员身份运行）")
            && BuiltInProfileLocalization.ForLanguage(fps,AppLanguage.English).Name.Contains("administrator"),"Built-in FPS names explain required authorization in both languages.");

        var profiles=Enumerable.Range(0,3).Select(i=>new HudProfile { Id=$"interaction-{i}",Name=$"Profile {i}",PrimaryTemplate=$"LIVE{i}",TitleTemplate="Test",
            SecondaryTemplate="",RightTemplate="50",ProgressVariable="=50",AnimationMode="Simple" }).ToList();
        var settings=new AppSettings { AlwaysVisible=true,PersistentLayer=PersistentHudLayer.Topmost,ClickToCycle=true,SamplingIntervalSeconds=.1,
            PositionMode=HudPositionMode.CustomCoordinates,HudCustomX=100,HudCustomY=100,
            CustomHud=new() { Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList(),AutoCycle=false,CycleAnimationMode="Simple" },
            MultiHudEnabled=true,HudInstances=new() { new() { Id="secondary",Name="Secondary",Settings=new AppSettings { AlwaysVisible=true,PersistentLayer=PersistentHudLayer.Topmost,ClickToCycle=false,
                PositionMode=HudPositionMode.CustomCoordinates,HudCustomX=100,HudCustomY=300,CustomHud=new() { Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList(),CycleAnimationMode="Simple" } } } } };
        var hud=new HudWindow();hud.ApplySettings(settings);
        using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);runtime.Start();await Task.Delay(1100);
        var child=Field<Dictionary<string,(HudWindow Hud,CustomHudRuntime Runtime)>>(runtime,"_instances")["secondary"];
        check(Field<AppSettings>(child.Runtime,"_appSettings").ClickToCycle,"Global persistent click setting reaches secondary HUDs even with older per-instance flags.");
        if(OperatingSystem.IsWindows()) {
            var nativeInput=Field<object>(child.Hud,"_interaction");
            var input=(IntPtr)nativeInput.GetType().GetProperty("InputHandle",Private)!.GetValue(nativeInput)!;
            var pill=child.Hud.FindControl<Border>("Pill")!;
            var point=pill.PointToScreen(new Point(pill.Bounds.Width/2,pill.Bounds.Height/2));
            check(WindowFromPoint(point)==input,"Physical hit testing reaches only the child HUD capsule input window.");
            string targetFile=Path.Combine(Path.GetTempPath(),"ecp-hit-"+Guid.NewGuid().ToString("N")+".json");
            var sourceInfo=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true };
            sourceInfo.ArgumentList.Add("--frame-source");sourceInfo.ArgumentList.Add(targetFile);
            using(var source=System.Diagnostics.Process.Start(sourceInfo)!) {
                try {
                    for(int attempt=0;attempt<30 && !File.Exists(targetFile);attempt++) await Task.Delay(100);
                    await Task.Delay(100);
                    var outside=new PixelPoint(160,210);
                    var hit=WindowFromPoint(outside);GetWindowThreadProcessId(hit,out uint owner);
                    check(owner>0 && owner!=(uint)Environment.ProcessId,$"A different process receives input outside the HUD capsule, even inside the old transparent animation host (hit={hit}, owner={owner}, test={Environment.ProcessId}, source={source.Id}).");
                } finally { if(!source.HasExited) source.Kill(); if(File.Exists(targetFile)) File.Delete(targetFile); }
            }
            ScreenToClient(input,ref point);var packed=new IntPtr((point.Y<<16)|(point.X&65535));
            SendMessage(input,0x201,new IntPtr(1),packed);SendMessage(input,0x202,IntPtr.Zero,packed);await Task.Delay(850);
            check(child.Hud.FindControl<TextBlock>("PrimaryText")!.Text=="LIVE1" && hud.FindControl<TextBlock>("PrimaryText")!.Text=="LIVE0","Clicking the secondary HUD switches its own queue without switching the primary.");
        }
        var window=new SettingsWindow(settings,hud,runtime);window.Show();await Task.Delay(600);
        check(window.FindControl<TextBlock>("BuildDateText")!.Text==LocalizationManager.Text("构建日期  2026.10.18","Build  2026.10.18"),"The actual About control displays the current 2026.10.18 build date in the selected language.");
        string version=typeof(HudWindow).Assembly.GetName().Version!.ToString(3);
        check(window.Title.Contains("v"+version) && window.Title==ProcessPrivileges.SettingsTitle(version),"Settings title uses the localized HUD settings caption.");
        check(hud.IsEditingHighlighted && !child.Hud.IsEditingHighlighted,"Opening Settings with multiple HUDs highlights the primary automatically.");
        ClickHud(hud,runtime);
        await Task.Delay(850);
        check(hud.FindControl<TextBlock>("PrimaryText")!.Text=="LIVE1" && hud.IsEditingHighlighted,"The selected primary HUD still cycles while Settings keeps its highlight visible.");
        var choice=Field<ComboBox>(window,"_instanceChoice");choice.SelectedIndex=1;await Task.Delay(800);
        check(child.Hud.IsEditingHighlighted && !hud.IsEditingHighlighted,"Selecting an instance moves the persistent highlight without requiring Locate.");
        ClickHud(child.Hud,child.Runtime);
        await Task.Delay(850);
        check(child.Hud.FindControl<TextBlock>("PrimaryText")!.Text==(OperatingSystem.IsWindows()?"LIVE2":"LIVE1") && child.Hud.IsEditingHighlighted,"The selected secondary HUD cycles its own queue while the editor is open.");
        var revision=Field<CancellationTokenSource>(child.Runtime,"_revision");
        await runtime.HighlightInstanceAsync("secondary","Secondary");
        check(ReferenceEquals(revision,Field<CancellationTokenSource>(child.Runtime,"_revision")),"Highlighting the same instance again does not cancel a live transition or sample.");
        // Queue callbacks can arrive while a close animation is still running,
        // including a complete wrap back to the last displayed profile.
        string beforeRapid=child.Hud.FindControl<TextBlock>("PrimaryText")!.Text!;
        typeof(CustomHudRuntime).GetMethod("OnHudClicked",Private)!.Invoke(child.Runtime,null);
        await Task.Delay(60);
        typeof(CustomHudRuntime).GetMethod("OnHudClicked",Private)!.Invoke(child.Runtime,null);
        typeof(CustomHudRuntime).GetMethod("OnHudClicked",Private)!.Invoke(child.Runtime,null);
        await Task.Delay(1000);
        var scale=child.Hud.FindControl<Grid>("ScaleHost")!.RenderTransform!.Value;
        check(child.Hud.FindControl<TextBlock>("PrimaryText")!.Text==beforeRapid && child.Hud.IsVisible && Math.Abs(scale.M11-1)<.001 && child.Hud.FindControl<Border>("Pill")!.Opacity>.99,
            "Rapid clicks during an editing transition recover a fully visible capsule even when the queue wraps to the old profile.");
        var hint=Field<TextBlock>(child.Hud,"_editingHint");
        check(hint.Text==LocalizationManager.Text("当前正在设置","Currently editing"),"The lower-left highlight caption is localized.");
        var sampling=window.FindControl<NumericUpDown>("SamplingIntervalBox")!;check(sampling.IsEnabled,"Sampling stays editable while the secondary HUD is selected.");
        sampling.Value=.25m;
        var customizer=window.FindControl<HudCustomizerView>("Customizer")!;
        check(ReferenceEquals(Field<HudWindow>(customizer,"_hud"),child.Hud),"The content preview is bound to the selected child HUD, not the primary.");
        customizer.FindControl<CheckBox>("PersistentClickSwitch")!.IsChecked=false;
        var draft=(AppSettings)typeof(SettingsWindow).GetMethod("CollectSettingsFromUi",Private)!.Invoke(window,null)!;
        check(draft.SamplingIntervalSeconds==.25 && draft.HudInstances.All(i=>i.Settings.SamplingIntervalSeconds==.25),"Editing secondary sampling updates the global setting and every saved instance.");
        check(!draft.ClickToCycle && draft.HudInstances.All(i=>!i.Settings.ClickToCycle),"Editing the shared click switch updates every saved instance.");
        await runtime.ApplySettingsWithTransitionAsync(draft);
        check(child.Hud.IsEditingHighlighted && child.Hud.IsVisible,"Save/apply leaves the selected HUD visibly highlighted.");
        check(Field<VariableHub>(runtime,"_variables").SamplingIntervalSeconds==.25 && ReferenceEquals(Field<VariableHub>(runtime,"_variables"),Field<VariableHub>(child.Runtime,"_variables")),"The actual shared sampler uses the interval chosen from the child editor.");
        check(window.FindControl<ComboBox>("PositionCombo")!.Items.Count==18,"Both work-area and absolute monitor presets are exposed in Settings.");
        if(Environment.GetEnvironmentVariable("ECP_INTERACTION_AUDIT") is { Length:>0 } audit) {
            Directory.CreateDirectory(audit);
            using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)child.Hud.Width,(int)child.Hud.Height));
            bitmap.Render(child.Hud);bitmap.Save(Path.Combine(audit,"editing-highlight.png"));
        }
        window.Close();await Task.Delay(200);
        check(!hud.IsEditingHighlighted && !child.Hud.IsEditingHighlighted,"Closing Settings clears every editing highlight.");

        await PositionRegressionChecks.Run(hud,runtime,check);
        await MultiHudLayerChecks.Run(check);

        var a=new HudProfile { Id="bad-a",Name="Unavailable A",PrimaryTemplate="{unsupported.one}",ProgressVariable="=50" };
        var b=new HudProfile { Id="bad-b",Name="Unavailable B",PrimaryTemplate="{unsupported.two}",ProgressVariable="=50" };
        var empty=new Dictionary<string,object?>();
        ProfileDataWarnings.Check(a,empty,"test-a");ProfileDataWarnings.Check(b,empty,"test-b");await Task.Delay(5100);
        ProfileDataWarnings.Check(a,empty,"test-a");ProfileDataWarnings.Check(b,empty,"test-b");await Task.Delay(700);
        var dialog=(Window?)typeof(ProfileDataWarnings).GetField("_dialog",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);
        var details=(TextBlock?)typeof(ProfileDataWarnings).GetField("_details",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);
        check(dialog is { IsVisible:true } && details!.Text!.Contains("unsupported.one") && details.Text.Contains("unsupported.two"),"Failures from multiple HUDs appear together in one warning dialog.");
        dialog!.Close();
        ProfileDataWarnings.Check(a,empty,"test-a");ProfileDataWarnings.Check(b,empty,"test-b");await Task.Delay(700);
        check(typeof(ProfileDataWarnings).GetField("_dialog",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null) is null,"Acknowledging one failure episode does not open a queue of per-HUD dialogs.");
        ProfileDataWarnings.Forget("test-a");ProfileDataWarnings.Forget("test-b");
        runtime.Dispose();hud.Close();await Task.Delay(100);
        Console.WriteLine("PASS: shared multi-HUD interaction, editing lifetime, warning aggregation, privilege title and absolute placement");
    }
    private static void ClickHud(HudWindow hud,CustomHudRuntime runtime)
    {
        if(!OperatingSystem.IsWindows()) { typeof(CustomHudRuntime).GetMethod("OnHudClicked",Private)!.Invoke(runtime,null);return; }
        var input=Field<WindowsHudInteraction>(hud,"_interaction").InputHandle;
        var pill=hud.FindControl<Border>("Pill")!;
        var point=pill.PointToScreen(new Point(pill.Bounds.Width/2,pill.Bounds.Height/2));
        ScreenToClient(input,ref point);var packed=new IntPtr((point.Y<<16)|(point.X&65535));
        SendMessage(input,0x201,new IntPtr(1),packed);SendMessage(input,0x202,IntPtr.Zero,packed);
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr h,ref PixelPoint point);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
}
