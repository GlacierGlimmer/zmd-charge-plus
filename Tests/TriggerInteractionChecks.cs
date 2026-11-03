using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class TriggerInteractionChecks
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,Private)!.GetValue(value)!;
    static void Set(object value,string name,object field)=>value.GetType().GetField(name,Private)!.SetValue(value,field);
    static async Task Observe(CustomHudRuntime runtime)
    {
        // Stopping the timer does not cancel an already running asynchronous read.
        // A forced test sample must wait for that read before establishing a new
        // baseline, matching the independent observer's next scheduled sample.
        long end=Environment.TickCount64+6000;
        while(Field<int>(runtime,"_triggerObservationBusy")!=0 && Environment.TickCount64<end)await Task.Delay(10);
        if(Field<int>(runtime,"_triggerObservationBusy")!=0)throw new TimeoutException("The previous trigger read did not complete.");
        Set(runtime,"_nextTriggerPoll",0L);
        await (Task)typeof(CustomHudRuntime).GetMethod("ObserveTriggerRulesAsync",Private)!.Invoke(runtime,null)!;
    }
    static int Pending(CustomHudRuntime runtime)=>Field<Dictionary<string,HudTriggerRule>>(runtime,"_pendingTriggerRules").Count;
    static Task Tick(CustomHudRuntime runtime)
    {
        Func<Task> action=()=>(Task)typeof(CustomHudRuntime).GetMethod("TickTransientTriggersAsync",Private)!.Invoke(runtime,null)!;
        return (Task)typeof(CustomHudRuntime).GetMethod("RunSerializedAsync",Private)!.Invoke(runtime,new object[]{action,true})!;
    }
    static async Task Until(Func<bool> ready)
    {
        long end=Environment.TickCount64+6000;while(!ready() && Environment.TickCount64<end)await Task.Delay(20);
        Check(ready(),"HUD presentation begins within the expected interval.");
    }
    [STAThread] static int Main()
    {
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().With(LocalizationManager.FontOptions).SetupWithoutStarting();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(100));Exception? failure=null;bool done=false;
        Dispatcher.UIThread.Post(async()=>{try{Models();await Exercise();done=true;}catch(Exception ex){failure=ex;}finally{timeout.Cancel();}});
        Dispatcher.UIThread.MainLoop(timeout.Token);
        if(failure is not null){Console.Error.WriteLine(failure);return 1;}
        if(!done){Console.Error.WriteLine("Trigger checks timed out.");return 1;}
        Console.WriteLine($"PASS {checks} hot-zone/rule interaction checks");return 0;
    }
    static void Models()
    {
        var evaluator=new HudTriggerEvaluator();var rule=new HudTriggerRule{Id="typed",Variable="v",Value=5};
        Dictionary<string,object?> Values(object? value)=>new(){{"v",value}};
        Check(evaluator.Evaluate(new[]{rule},Values("not a number"),0).Count==0,"A type mismatch is invalid rather than a baseline.");
        Check(evaluator.Evaluate(new[]{rule},Values(9),1).Count==0,"The first recovered numeric reading only establishes a baseline.");
        Check(evaluator.Evaluate(new[]{rule},Values(9),2).Count==1,"A sustained valid threshold remains supported.");
        evaluator.Reset();rule=rule with{Mode="BecomesTrue"};
        evaluator.Evaluate(new[]{rule},Values("invalid"),0);
        Check(evaluator.Evaluate(new[]{rule},Values(true),1).Count==0,"A recovered boolean reading does not invent a false-to-true edge.");
        evaluator.Evaluate(new[]{rule},Values(false),2);
        Check(evaluator.Evaluate(new[]{rule},Values(true),3).Count==1,"A real boolean edge still fires.");
    }
    static async Task Exercise()
    {
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
        var profiles=Enumerable.Range(0,3).Select(i=>new HudProfile{Id="interaction-"+i,Name="Interaction "+i,PrimaryTemplate="HUD"+i,SecondaryTemplate="",RightTemplate="",TitleTemplate="Test",TaglineTemplate="",ProgressVariable="=50",AnimationMode="Simple"}).ToList();
        var rule=new HudTriggerRule{Id="clock-change",Variable="time.unix_milliseconds",Value=0,CooldownSeconds=1};
        var options=new AppSettings{DisplayDurationSeconds=3,SamplingIntervalSeconds=.1,HotZoneAdvanceQueue=true,CustomHud=new(){Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList(),CycleAnimationMode="Simple",Triggers=new(){rule}}};
        var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);SettingsWindow? settings=null;
        try
        {
            hud.ApplySettings(options);runtime.ApplySettings(options);
            Set(runtime,"_nextPowerPoll",DateTime.MaxValue);Set(runtime,"_nextDeepSeekPeriodPoll",DateTime.MaxValue);
            await Observe(runtime);Check(Pending(runtime)==0,"The first observed sample is only a baseline.");
            await Task.Delay(110);await Observe(runtime);Check(Pending(runtime)==1,"A sustained real threshold is queued.");
            var gate=Field<HotZoneDwellGate>(runtime,"_hotZone");gate.Observe(true,Environment.TickCount64-1000);
            var hot=Tick(runtime);await Until(()=>hud.IsVisible);
            Check(hud.FindControl<TextBlock>("PrimaryText")!.Text=="HUD1","The hot zone takes priority and advances beyond the selected profile.");
            Check(Pending(runtime)==1,"Hot-zone priority preserves the pending rule.");await hot;
            Check(!gate.Ready(Environment.TickCount64,0),"The hot-zone request is consumed once per entry.");
            var triggered=Tick(runtime);await Until(()=>hud.IsVisible);
            Check(hud.FindControl<TextBlock>("PrimaryText")!.Text=="HUD0","Rule progression starts independently of hot-zone progression.");
            var timer=Field<DispatcherTimer>(runtime,"_timer");var observation=Field<DispatcherTimer>(runtime,"_observationTimer");timer.Start();observation.Start();
            await Task.Delay(1400);
            Check(hud.IsHudBusy && Pending(runtime)==1,"The real timer observes and retains new changes during a serialized HUD presentation.");
            await triggered;timer.Stop();observation.Stop();
            Check(Pending(runtime)==1,"Repeated busy-period changes coalesce into one pending rule.");
            Check(Field<int>(runtime,"_hotZoneCycleIndex")==1,"Rule activity cannot advance the hot-zone cursor.");
            var changes=options with{CustomHud=options.CustomHud with{Triggers=new(){rule with{Mode="Change"}}}};
            runtime.ApplySettings(changes);Set(runtime,"_nextPowerPoll",DateTime.MaxValue);Set(runtime,"_nextDeepSeekPeriodPoll",DateTime.MaxValue);
            await Observe(runtime);await Task.Delay(110);await Observe(runtime);
            var changed=Tick(runtime);await Until(()=>hud.IsVisible);timer.Start();observation.Start();await Task.Delay(1400);
            Check(hud.IsHudBusy && Pending(runtime)==1,"Change-mode events are retained while an earlier change is being displayed.");
            await changed;timer.Stop();observation.Stop();
            // A continuously due rule must not starve a due automatic rotation.
            var auto=options with{CustomHud=options.CustomHud with{AutoCycle=true,CycleSeconds=3}};
            runtime.ApplySettings(auto);Set(runtime,"_nextPowerPoll",DateTime.MaxValue);Set(runtime,"_nextDeepSeekPeriodPoll",DateTime.MaxValue);
            await Observe(runtime);await Task.Delay(110);await Observe(runtime);Set(runtime,"_nextPersistentCycle",DateTime.MinValue);
            await Tick(runtime);Check(Field<bool>(runtime,"_lastTransientWasRule"),"A due rule may run before automatic rotation.");
            await Observe(runtime);Set(runtime,"_nextPersistentCycle",DateTime.MinValue);
            var rotation=Tick(runtime);await Until(()=>hud.IsVisible);
            Check(hud.FindControl<TextBlock>("PrimaryText")!.Text=="HUD1","A due automatic rotation gets its turn after a rule.");await rotation;
            Check(!Field<bool>(runtime,"_lastTransientWasRule"),"Automatic rotation resets the alternation state.");
            await Observe(runtime);runtime.Stop();Check(Pending(runtime)==0,"Stopping clears delayed rule requests.");
            runtime.ApplySettings(options with{HudEnabled=false});await Observe(runtime);Check(Pending(runtime)==0,"The master switch suppresses independent rule observation.");
            runtime.ApplySettings(options with{AlwaysVisible=true});await Observe(runtime);Check(Pending(runtime)==0,"Persistent mode suppresses transient rules.");
            runtime.ApplySettings(options);await Observe(runtime);Check(Pending(runtime)==0,"Reapplying settings resets the baseline and pending queue.");
            settings=new SettingsWindow(options,hud,runtime);
            settings.Show();settings.NavigateToSettingsSection("rules");await Task.Delay(150);
            var editor=settings.FindControl<HudCustomizerView>("Customizer")!;
            var name=editor.FindControl<TextBox>("TriggerNameBox")!;
            var scroll=name.GetVisualAncestors().OfType<ScrollViewer>().First();
            name.Focus();await Task.Delay(80);scroll.Offset=new Vector(0,80);await Task.Delay(80);var before=scroll.Offset;
            name.Text="Renamed rule";name.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));await Task.Delay(150);
            Check((scroll.Offset-before).Length<1,"Renaming a rule cannot pull the page back to the rule list.");
            var variable=editor.FindControl<TextBox>("TriggerVariableBox")!;
            var mode=editor.FindControl<ComboBox>("TriggerModeCombo")!;
            var status=editor.FindControl<TextBlock>("TriggerValidationText")!;
            variable.Text="cpu.usage.typo";
            await Task.Delay(60);
            Check(status.IsVisible && status.Text!.Contains("cpu.usage.typo"),"An unknown variable produces immediate inline feedback.");
            variable.Text="media.title";mode.SelectedIndex=0;
            await Task.Delay(60);
            Check(status.Text!.Contains("media.title"),"A text variable used as a threshold is identified.");
            foreach(var locale in LocalizationManager.SupportedLanguages)
            {
                LocalizationManager.SetLanguage(locale.Language);editor.ApplyLocalization();
                variable.Text="cpu.usage.typo";
                Check(editor.FindControl<TextBlock>("TriggerRulesHint")!.Text==LocalizationManager.Text("仅在关闭“一直显示”时生效。满足规则后按轮播队列呼出下一项；空队列使用当前方案。首次采样和数据恢复只建立基线。鼠标热区优先响应；热区和规则各自推进队列。HUD 展示期间仍按全局与数据源设置的间隔采样，忙碌时检测到的触发会合并保留。","Active when Always Visible is off. Matching rules summon the next carousel item, or the current profile if the queue is empty. Initial and recovered samples only establish a baseline."),"The complete rules hint follows "+locale.Code);
                await Task.Delay(30);
                Check(status.Text!.StartsWith(LocalizationManager.Text("未找到匹配变量。","No matching variable.")),"Validation feedback follows "+locale.Code+": "+status.Text);
                Check(editor.FindControl<Button>("CheckTriggerVariableBtn")!.Content?.ToString()==LocalizationManager.Text("刷新","Refresh"),"Read-check action follows "+locale.Code+": "+editor.FindControl<Button>("CheckTriggerVariableBtn")!.Content);
            }
            LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);editor.ApplyLocalization();
            variable.Text="time.unix_milliseconds";mode.SelectedIndex=0;
            var selector=editor.FindControl<ComboBox>("ProfileCombo")!;var selected=selector.SelectedItem;
            editor.FindControl<TextBox>("ProfileNameBox")!.Text="Unsaved read-check name";
            editor.FindControl<TextBox>("DeepSeekKeyBox")!.Text="UNSAVED-AUDIT-KEY";
            await (Task)typeof(HudCustomizerView).GetMethod("CheckTriggerVariableAsync",Private)!.Invoke(editor,null)!;
            Check(status.Text==LocalizationManager.Text("类型","Type")+": "+LocalizationManager.Text("数值","Number"),"Refresh performs a real single-variable read and reports the actual type.");
            Check(Equals(selector.SelectedItem,selected),"Reading a rule variable must not save or rename the edited profile.");
            variable.Text="deepseek.balance.total";await Task.Delay(30);
            await (Task)typeof(HudCustomizerView).GetMethod("CheckTriggerVariableAsync",Private)!.Invoke(editor,null)!;
            Check(status.Text=="DeepSeek API: "+LocalizationManager.Text("保存并应用","Save & Apply"),"An unsaved credential requests Save & Apply rather than writing to the credential store.");
            editor.FindControl<TextBox>("DeepSeekKeyBox")!.Text="";variable.Text="time.unix_milliseconds";await Task.Delay(30);
            await (Task)typeof(HudCustomizerView).GetMethod("CheckTriggerVariableAsync",Private)!.Invoke(editor,null)!;
            if(Environment.GetEnvironmentVariable("ECP_TRIGGER_SCREENSHOT") is {Length:>0} path)
            {
                await Task.Delay(100);using var image=new RenderTargetBitmap(new PixelSize((int)settings.Bounds.Width,(int)settings.Bounds.Height));image.Render(settings);image.Save(path);
            }
            await runtime.ToggleUserVisibilityAsync();await Observe(runtime);
            Check(runtime.IsUserHidden && Pending(runtime)==0,"User hiding clears pending events and prevents later background summons.");
            var startupHud=new HudWindow();using var startup=new CustomHudRuntime(startupHud);
            try
            {
                var first=changes with{HotZoneEnabled=false};startupHud.ApplySettings(first);startup.ApplySettings(first);startup.Start();
                await Until(()=>startupHud.IsVisible);await Task.Delay(450);
                Check(startupHud.IsHudBusy && Pending(startup)==1,"Observation also continues during the initial startup presentation.");
                startup.Stop();await Field<Task>(startup,"_lastOperation");
            }
            finally{startup.Dispose();startupHud.Close();}
        }
        finally{settings?.Close();runtime.Dispose();hud.Close();LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);}
    }
}
