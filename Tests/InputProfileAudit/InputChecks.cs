using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private static async Task Until(Func<bool> predicate,string why,int milliseconds=10000){var end=DateTime.UtcNow.AddMilliseconds(milliseconds);while(!predicate() && DateTime.UtcNow<end)await Task.Delay(20);Check(predicate(),why);}
    private static HudRenderData? Data(HudWindow hud)=>Field<HudRenderData?>(hud,"_lastRenderData");
    private static void Key(TextBox box,Avalonia.Input.Key key,KeyModifiers mods=0,bool up=false)=>box.RaiseEvent(new KeyEventArgs{RoutedEvent=up?InputElement.KeyUpEvent:InputElement.KeyDownEvent,Key=key,KeyModifiers=mods});
    private static async Task InputProfileChecks(string output)
    {
        foreach(var key in new[]{Avalonia.Input.Key.Up,Avalonia.Input.Key.Down,Avalonia.Input.Key.Left,Avalonia.Input.Key.Right,Avalonia.Input.Key.Insert,Avalonia.Input.Key.Delete,Avalonia.Input.Key.Back,Avalonia.Input.Key.Tab,Avalonia.Input.Key.Enter,Avalonia.Input.Key.Space,Avalonia.Input.Key.Home,Avalonia.Input.Key.End,Avalonia.Input.Key.PageUp,Avalonia.Input.Key.PageDown,Avalonia.Input.Key.F12,Avalonia.Input.Key.F24,Avalonia.Input.Key.NumPad0,Avalonia.Input.Key.NumPad9,Avalonia.Input.Key.OemPlus,Avalonia.Input.Key.OemMinus,Avalonia.Input.Key.LeftCtrl,Avalonia.Input.Key.RightAlt})
        {Check(HudHotkeyGesture.TryCapture(key,0,out var chord),"record keyboard key "+key);Check(HudHotkeyGesture.TryParse(chord.Text,out var roundTrip) && chord==roundTrip,"round-trip keyboard key "+key);}
        foreach(var text in new[]{"Ctrl+Alt+M","Shift+Up","Win+Insert","F12","Mouse1","Mouse2","Mouse3","Mouse4","Mouse5","Ctrl+Alt+Shift+Win+Mouse5"})Check(HudHotkeyGesture.TryParse(text,out _),"parse extended binding "+text);
        Check(!HudHotkeyGesture.TryParse("Ctrl+Ctrl+M",out _) && !HudHotkeyGesture.TryParse("NoSuchKey",out _),"invalid syntax rejected");
        int observed=0;using(var key=new WindowsGlobalHotkey(()=>observed++)){
            Check(key.TryUpdate("Mouse4"),"native mouse binding available on private desktop");WindowsObservedShortcut.ProcessForTest(5,true,0);WindowsObservedShortcut.ProcessForTest(5,true,0);await Task.Delay(30);Check(observed==1,"held mouse fires once");WindowsObservedShortcut.ProcessForTest(5,false,0);WindowsObservedShortcut.ProcessForTest(5,true,0);await Task.Delay(30);Check(observed==2,"next mouse press fires again");
            Check(key.TryUpdate("Ctrl+Alt+F12"),"reserved registration F12 has observed-input backend");WindowsObservedShortcut.ProcessForTest(123,true,3);await Task.Delay(30);Check(observed==3,"F12 with modifiers dispatches");Check(key.TryUpdate(""),"clearing native observation");WindowsObservedShortcut.ProcessForTest(123,false,3);WindowsObservedShortcut.ProcessForTest(123,true,3);await Task.Delay(30);Check(observed==3,"cleared binding no longer fires");
        }
        LocalizationManager.Initialize("zh-CN");
        var profiles=Enumerable.Range(0,3).Select(i=>new HudProfile{Id="input-profile-"+i,Name="Input fixture "+i,PrimaryTemplate="PROFILE"+i,SecondaryTemplate="",RightTemplate="50",ProgressVariable="=50",TitleTemplate="Title "+i,TaglineTemplate="Fixture",AnimationMode=i==1?"Full":"Simple"}).ToList();
        var basic=new AppSettings{HudEnabled=true,AlwaysVisible=true,DisplayDurationSeconds=3,ClickToCycle=true,Workspace=new(){RadialEnabled=false},CustomHud=new(){Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList(),CycleAnimationMode="Simple"}};
        var options=basic with{MultiHudEnabled=true,HudInstances=new(){new(){Id="child",Settings=basic with{CustomHud=basic.CustomHud with{ActiveProfileId=profiles[2].Id}}},new(){Id="off",Settings=basic with{HudEnabled=false}}}};
        var hud=new HudWindow();hud.ApplySettings(options);using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(options);runtime.Start();SettingsWindow? window=null;
        try{
            var child=runtime.GetInstanceHud("child")!;var off=runtime.GetInstanceHud("off")!;
            await Until(()=>hud.IsVisible && child.IsVisible && !hud.IsHudBusy && !child.IsHudBusy,"main and child start independently");Check(!off.IsVisible,"disabled child stays hidden");
            using(var leftShortcut=new WindowsGlobalHotkey(()=>{})){Check(leftShortcut.TryUpdate("Mouse1"),"left mouse shortcut registers");var before=runtime.CurrentProfileId();typeof(CustomHudRuntime).GetMethod("OnHudClicked",Private)!.Invoke(runtime,null);Check(runtime.CurrentProfileId()==before,"global left binding suppresses duplicate HUD click action");}
            var switched=options with{CustomHud=options.CustomHud with{ActiveProfileId=profiles[1].Id}};
            await runtime.ApplySavedSettingsAsync(switched);
            await Until(()=>Data(hud)?.PrimaryText=="PROFILE1","selected profile applied");Check(Data(hud)?.SimpleAnimation==false && hud.IsHudBusy,"profile Full starts real intro rather than immediate refresh");Check(runtime.CurrentProfileId("child")==profiles[2].Id,"unrelated child selection preserved");
            await Until(()=>!hud.IsHudBusy,"full intro completes");
            await runtime.ApplySavedSettingsAsync(switched with{GlobalScale=.95});await Task.Delay(120);Check(!hud.IsHudBusy,"same-profile preference save stays immediate");
            await runtime.ApplySavedSettingsAsync(options);await Until(()=>Data(hud)?.PrimaryText=="PROFILE0" && !hud.IsHudBusy,"Simple profile completes quick reveal");Check(Data(hud)?.SimpleAnimation==true,"per-profile Simple is honored");
            runtime.StepProfiles(1);Check(runtime.CurrentProfileId()==profiles[1].Id && runtime.CurrentProfileId("child")==profiles[0].Id,"global next advances every HUD relative to its own selection");
            runtime.ToggleAutoCyclePaused();Check(runtime.IsAutoCyclePaused,"global pause enabled");var children=Field<Dictionary<string,(HudWindow Hud,CustomHudRuntime Runtime)>>(runtime,"_instances");Check(children.Values.All(x=>x.Runtime.IsAutoCyclePaused),"all instances inherit pause");runtime.ToggleAutoCyclePaused();
            await runtime.ToggleUserVisibilityAsync();Check(!hud.IsVisible && !child.IsVisible && !off.IsVisible,"global hide covers all enabled HUDs");
            await runtime.ApplySavedSettingsAsync(options);await Task.Delay(100);Check(!hud.IsVisible && !child.IsVisible,"save cannot resurrect hidden HUDs");
            await runtime.ToggleUserVisibilityAsync();await Until(()=>hud.IsVisible && child.IsVisible,"global restore recovers enabled instances");Check(!off.IsVisible,"restore leaves disabled instance disabled");
            var auto=options with{CustomHud=options.CustomHud with{AutoCycle=true,ActiveProfileId=profiles[2].Id,CycleProfileIds=new(){profiles[0].Id,profiles[1].Id}}};
            await runtime.ApplySavedSettingsAsync(auto);await Until(()=>Data(hud)?.PrimaryText=="PROFILE2","manual selected profile outside auto queue still displays");
            window=new SettingsWindow(options with{HudEnabled=false},hud,runtime){PersistLanguagePreference=_=>{}};Field<CancellationTokenSource>(window,"_statusLifetime").Cancel();window.Show();window.NavigateToSettingsSection("quick");await Task.Delay(100);
            var editorBefore=window.FindControl<HudCustomizerView>("Customizer")!.FindControl<ComboBox>("ProfileCombo")!.SelectedIndex;
            children["child"].Runtime.SwitchProfile(1);await Task.Delay(30);
            var savedAfter=Field<AppSettings>(window,"_rootSettings");Check(savedAfter.HudInstances.Single(x=>x.Id=="child").Settings.CustomHud.ActiveProfileId==runtime.CurrentProfileId("child"),"non-selected HUD profile stays synchronized in Settings");Check(window.FindControl<HudCustomizerView>("Customizer")!.FindControl<ComboBox>("ProfileCombo")!.SelectedIndex==editorBefore,"another HUD does not hijack current editor");
            var boxes=Field<Dictionary<string,TextBox>>(window,"_quickHotkeyBoxes");var main=window.FindControl<TextBox>("HudHotkeyBox")!;
            window.BeginShortcutRecording(boxes["previous"]);Key(boxes["previous"],Avalonia.Input.Key.Up);Check(boxes["previous"].Text=="Up" && !window.IsRecordingHudHotkey,"arrow records without moving focus");
            window.BeginShortcutRecording(boxes["next"]);Key(boxes["next"],Avalonia.Input.Key.Insert);Check(boxes["next"].Text=="Insert","Insert records");
            window.BeginShortcutRecording(main);Key(main,Avalonia.Input.Key.M,KeyModifiers.Control|KeyModifiers.Alt);Check(main.Text=="Ctrl+Alt+M","Ctrl+Alt+M records");
            window.BeginShortcutRecording(main);Key(main,Avalonia.Input.Key.Tab);Check(main.Text=="Tab","Tab is bindable in recording mode");window.BeginShortcutRecording(main);Key(main,Avalonia.Input.Key.Escape);Check(main.Text=="","Esc unbinds");
            window.BeginShortcutRecording(main);Check(window.CaptureRegisteredShortcut("Mouse3") && main.Text=="Mouse3","registered mouse input records without executing actions");Check(window.CaptureRegisteredShortcut("Mouse3"),"the same recorded physical input cannot also execute its action");
            foreach(var box in boxes.Values)box.Text="Alt+X";Check(!window.RefreshShortcutValidation(),"duplicates rejected before save");
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text!=null && t.Text.StartsWith("快捷键冲突：") && !t.Text.Contains("{0}") && t.Text.Contains("打开设置")),"Chinese conflict explanation names the conflicting actions");Check(boxes.Values.All(x=>x.Foreground is ISolidColorBrush b && b.Color==Color.Parse("#FF6464")),"every conflicting binding is red");
            window.BeginShortcutRecording(boxes["next"]);Key(boxes["next"],Avalonia.Input.Key.Insert);Check(boxes["next"].Foreground is not ISolidColorBrush br || br.Color!=Color.Parse("#FF6464"),"resolving a conflict clears its warning immediately");
            foreach(var language in LocalizationManager.SupportedLanguages.Select(x=>x.Code)){
                LocalizationManager.Initialize(language);typeof(SettingsWindow).GetMethod("OnLanguageSelected",Private)!.Invoke(window,new object[]{language});
                Check(LocalizationManager.Text("快捷键冲突：{0}","Shortcut conflict: {0}")!="Shortcut conflict: {0}" || language=="en-US","conflict hint translated "+language);
                foreach(var scale in new[]{(.8,.8),(1d,1d),(1.25,1.5)}){
                    GlobalAppearance.Apply(window,scale.Item1,scale.Item2);window.NavigateToSettingsSection("hud");await Task.Delay(10);
                    var editor=window.FindControl<HudCustomizerView>("Customizer")!;var add=editor.FindControl<Button>("AddProfileBtn")!;var remove=editor.FindControl<Button>("DeleteProfileBtn")!;
                    var a=add.TranslatePoint(default,window);var b=remove.TranslatePoint(default,window);Check(a.HasValue && b.HasValue && Math.Abs(a.Value.Y-b.Value.Y)<1 && Math.Abs(add.Bounds.Height-remove.Bounds.Height)<1,"library buttons align "+language+scale);
                }
            }
            LocalizationManager.Initialize("zh-CN");typeof(SettingsWindow).GetMethod("OnLanguageSelected",Private)!.Invoke(window,new object[]{"zh-CN"});GlobalAppearance.Apply(window,1,1);window.NavigateToSettingsSection("quick");await Task.Delay(150);using(var image=new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize,window.RenderScaling))){image.Render(window);image.Save(Path.Combine(output,"quick-conflicts.png"));}
            window.Close();window=null;
            await runtime.ToggleUserVisibilityAsync();
            var many=options with{HudInstances=Enumerable.Range(0,16).Select(i=>new HudInstanceSettings{Id="limit-"+i,Settings=basic with{HudEnabled=false}}).ToList()};
            await runtime.ApplySavedSettingsAsync(many);Check(runtime.GetInstanceHud("limit-14") is not null && runtime.GetInstanceHud("limit-15") is null,"at most fifteen children plus the primary HUD");
            Check(Field<Dictionary<string,(HudWindow Hud,CustomHudRuntime Runtime)>>(runtime,"_instances").Values.All(x=>x.Runtime.IsUserHidden && !x.Hud.IsVisible),"all new instances inherit session suppression");
            Check(child.IsClosed && off.IsClosed,"removed instances release their windows");
            await runtime.ApplySavedSettingsAsync(options);Check(runtime.GetInstanceHud("limit-0") is null,"removing instances also removes stale runtime routes");
        }finally{window?.Close();runtime.Dispose();hud.Close();}
    }
}
