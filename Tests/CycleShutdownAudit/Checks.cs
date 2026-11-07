using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static partial class Audit
{
    private static async Task Until(Func<bool> f,string why,int timeout=12000)
    {
        var end=DateTime.UtcNow.AddMilliseconds(timeout);
        while(!f() && DateTime.UtcNow<end)await Task.Delay(15);
        Check(f(),why);
    }
    private static void Set(object target,string name,object? value)=>target.GetType().GetField(name,Private)!.SetValue(target,value);
    private static object? Invoke(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,Private)!.Invoke(target,args);
    private static double Offset(HudWindow hud)=>(hud.FindControl<Control>("ContentHost")!.RenderTransform as TranslateTransform)?.Y??0;
    private static void StopTimers(CustomHudRuntime runtime)
    {Field<DispatcherTimer>(runtime,"_timer").Stop();Field<DispatcherTimer>(runtime,"_observationTimer").Stop();}
    private static async Task RunChecks(string output)
    {
        LocalizationManager.Initialize("zh-CN");
        var modes=new[]{"Full","Simple","Roll","RollDown"};
        Check(CustomHudSettings.CreateDefault().CycleAnimationMode=="Simple","new configurations default to Simple");
        foreach(var mode in modes)Check(HudSettingsNormalizer.Normalize(new(){CycleAnimationMode=mode}).CycleAnimationMode==mode,"mode survives normalization: "+mode);
        Check(HudSettingsNormalizer.Normalize(new(){CycleAnimationMode="RollUp"}).CycleAnimationMode=="Roll","legacy upward alias migrates");
        Check(HudSettingsNormalizer.Normalize(new(){CycleAnimationMode="unknown"}).CycleAnimationMode=="Simple","invalid/missing mode falls back to Simple");
        var profiles=Enumerable.Range(0,3).Select(i=>new HudProfile{Id="cycle-"+i,Name="Cycle "+i,PrimaryTemplate="PROFILE "+i,RightTemplate="50",ProgressVariable="=50",TitleTemplate="Title "+i,TaglineTemplate="Cycle fixture",AnimationMode="Simple"}).ToList();
        AppSettings Options(string mode,bool auto=false)=>new(){HudEnabled=true,AlwaysVisible=true,ClickToCycle=true,SamplingIntervalSeconds=.1,Workspace=new(){RadialEnabled=false},CustomHud=new(){Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList(),CycleAnimationMode=mode,AutoCycle=auto,CycleSeconds=3}};
        foreach(var mode in modes)
        {
            var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);
            try
            {
                runtime.ApplySettings(Options(mode));runtime.Start();
                await Until(()=>Field<bool>(runtime,"_persistentShown") && !hud.IsHudBusy,"initial persistent HUD: "+mode);StopTimers(runtime);
                foreach(var gesture in new[]{("click",1),("wheel-up",-1),("wheel-down",1)})
                {
                    string previous=runtime.CurrentProfileId();int index=profiles.FindIndex(p=>p.Id==previous);
                    string target=profiles[(index+gesture.Item2+profiles.Count)%profiles.Count].Id;
                    if(gesture.Item1=="click")Invoke(runtime,"OnHudClicked");else Invoke(runtime,"OnHudScrolled",gesture.Item2);
                    Check(runtime.CurrentProfileId()==target,"manual queue order: "+mode+"/"+gesture.Item1);
                    await Until(()=>hud.IsRolling && Math.Abs(Offset(hud))>.01,"actual in-place manual roll starts: "+mode+"/"+gesture.Item1);
                    Check(Math.Sign(Offset(hud))==Math.Sign(gesture.Item2),"visible animation follows click/wheel direction: "+mode+"/"+gesture.Item1);
                    await Until(()=>!hud.IsHudBusy && Field<string>(runtime,"_persistentProfileId")==target,"manual presentation completes: "+mode+"/"+gesture.Item1);
                }
            }
            finally{runtime.Dispose();hud.Close();}
        }
        foreach(var mode in modes)
        {
            var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);
            try
            {
                runtime.ApplySettings(Options(mode,true));runtime.Start();
                await Until(()=>Field<bool>(runtime,"_persistentShown") && !hud.IsHudBusy,"auto initial HUD: "+mode);StopTimers(runtime);
                Set(runtime,"_nextPersistentCycle",DateTime.MinValue);
                var tick=(Task)Invoke(runtime,"TickAsync")!;
                if(mode is "Roll" or "RollDown")
                {
                    await Until(()=>hud.IsRolling && Math.Abs(Offset(hud))>.01,"automatic roll starts: "+mode);
                    Check(Math.Sign(Offset(hud))==(mode=="RollDown"?1:-1),"automatic roll direction: "+mode);
                }
                await tick;
                Check(Field<string>(runtime,"_persistentProfileId")==profiles[1].Id,"auto queue advances: "+mode);
                var data=Field<HudRenderData?>(hud,"_lastRenderData");
                Check(data is not null && data.SimpleAnimation==(mode=="Simple"),"full/simple render contract: "+mode);
            }
            finally{runtime.Dispose();hud.Close();}
        }

        var windowHud=new HudWindow();using var windowRuntime=new CustomHudRuntime(windowHud);
        var options=Options("Simple") with {HudEnabled=false};windowRuntime.ApplySettings(options);
        var window=new SettingsWindow(options,windowHud,windowRuntime){PersistLanguagePreference=_=>{}};
        Field<CancellationTokenSource>(window,"_statusLifetime").Cancel();window.Show();window.NavigateToSettingsSection("cycle");
        try
        {
            var editor=window.FindControl<HudCustomizerView>("Customizer")!;
            var click=editor.FindControl<CheckBox>("PersistentClickSwitch")!;
            Check(click.GetLogicalAncestors().Contains(editor.FindControl<TabItem>("AutomaticCyclePage")!),"single original click control belongs to Auto Cycle");
            Check(!window.FindControl<StackPanel>("QuickActionsPanel")!.GetLogicalDescendants().Contains(click),"no duplicate manual-switch control under Quick actions");
            var combo=editor.FindControl<ComboBox>("CycleAnimationModeCombo")!;
            Check(combo.Items.Count==4 && combo.SelectedIndex==1,"four choices with Simple selected by default");
            foreach(var code in LocalizationManager.SupportedLanguages.Select(x=>x.Code))
            {
                LocalizationManager.Initialize(code);Invoke(window,"OnLanguageSelected",code);
                for(int index=0;index<4;index++)
                {
                    combo.SelectedIndex=index;Check(editor.ExportSettings().CycleAnimationMode==modes[index],"choice export: "+code+"/"+modes[index]);
                }
                Check(LocalizationManager.Text("向下翻滚","Roll Down")!="Roll Down" || code=="en-US","down option translated: "+code);
            }
            LocalizationManager.Initialize("zh-CN");Invoke(window,"OnLanguageSelected","zh-CN");window.NavigateToSettingsSection("cycle");combo.SelectedIndex=1;GlobalAppearance.Apply(window,1,1);await Task.Delay(350);
            var viewport=(ScrollViewer)window.FindControl<ContentControl>("SettingsBodyHost")!.Content!;
            File.WriteAllText(Path.Combine(output,"layout.txt"),$"client={window.ClientSize}; scale={GlobalAppearance.CurrentScales}; viewport={viewport.Viewport}; extent={viewport.Extent}; offset={viewport.Offset}; root={((Avalonia.Controls.LayoutTransformControl)viewport.Content!).Child!.Bounds}; language={window.FindControl<Button>("LanguageButton")!.Bounds}\n");
            using(var bitmap=new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize,window.RenderScaling))){bitmap.Render(window);bitmap.Save(Path.Combine(output,"auto-cycle.png"));}
            // Simulate only the app's lifetime notification, never send OS shutdown
            // messages and never stop/restart the user's computer or NVIDIA app.
            var app=(App)Application.Current!;
            Set(app,"_hud",windowHud);Set(app,"_runtime",windowRuntime);Set(app,"_settingsWindow",window);
            var request=new ShutdownRequestedEventArgs();Invoke(app,"OnShutdownRequested",null,request);
            Check(!request.Cancel,"system shutdown request is never cancelled");
            Check(Field<bool>(windowRuntime,"_disposed"),"runtime stopped before native window destruction");
            Check(Field<bool>(windowHud,"_applicationExitPrepared"),"HUD animations/input stopped before close");
            Check(Field<bool>(window,"_applicationExitPrepared") && Field<CancellationTokenSource>(window,"_hardwareLifetime").IsCancellationRequested,"settings hardware sampling cancelled before close");
            Check(Field<bool>(app,"_exiting"),"late activation/shortcut callbacks are blocked");
            Invoke(app,"OnShutdownRequested",null,new ShutdownRequestedEventArgs());
            Invoke(app,"PrepareForExit");Check(true,"cleanup is idempotent");
        }
        finally{window.Close();windowRuntime.Dispose();windowHud.Close();}
    }
}
