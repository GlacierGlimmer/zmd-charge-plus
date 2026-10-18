using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class NextVersionRegressionChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static void Run(Action<bool,string> check)
    {
        Models(check);Rules(check);Media(check);HotZone(check);
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(120));Exception? failure=null;bool completed=false;
        Dispatcher.UIThread.Post(async ()=> {try {await Animations(check);await Events(check);await MediaTimeout(check);completed=true;} catch(Exception ex){failure=ex;} finally{finish.Cancel();}});
        Dispatcher.UIThread.MainLoop(finish.Token);
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();if(!completed)throw new TimeoutException("Next-version UI checks timed out.");
    }
    private static void Models(Action<bool,string> check)
    {
        foreach(string mode in new[] {"Simple","Full","Roll"})
            check(HudSettingsNormalizer.Normalize(CustomHudSettings.CreateDefault() with {CycleAnimationMode=mode}).CycleAnimationMode==mode,"Explicit carousel animation survives migration: "+mode);
        check(CustomHudSettings.CreateDefault().CycleAnimationMode=="Roll","New configurations default to the in-place roll animation.");
        var original=new AppSettings {HudHotkeyShowAnimation="Simple",HotZonePosition=HudPosition.BottomRight,HotZoneWidth=300,HotZoneHeight=45,HotZoneDwellMilliseconds=700,
            CustomHud=CustomHudSettings.CreateDefault() with {Triggers=new(){new(){Mode="Change",Variable="media.title"}}}};
        var restored=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original))!;
        check(restored.HudHotkeyShowAnimation=="Simple" && restored.HotZoneDwellMilliseconds==700 && restored.CustomHud.Triggers.Single().Variable=="media.title","Animation, hot zone and rules round trip through JSON.");
        check(JsonSerializer.Deserialize<CustomHudSettings>("{}")!.Triggers.Count==0,"Legacy configurations do not acquire enabled event rules.");
        var rules=HudTriggerRule.Normalize(new[] {new HudTriggerRule {Id="duplicate",Mode="change",CooldownSeconds=0},new HudTriggerRule {Id="duplicate",Mode="unknown",Value=double.NaN,CooldownSeconds=int.MaxValue}});
        check(rules.Select(r=>r.Id).Distinct().Count()==2 && rules[0].Mode=="Change" && rules[0].CooldownSeconds==1 && rules[1].CooldownSeconds==86400 && double.IsFinite(rules[1].Value),"Malformed imported rules normalize without sharing identities or invalid numeric state.");
    }
    private static void Rules(Action<bool,string> check)
    {
        var evaluator=new HudTriggerEvaluator();var a=new HudTriggerRule {Id="a",Mode="Change",Variable="value",CooldownSeconds=2};var b=a with {Id="b"};
        Dictionary<string,object?> Value(object? value)=>new(StringComparer.OrdinalIgnoreCase){{"value",value}};
        var rules=new[]{a,b};
        check(evaluator.Evaluate(rules,Value("first"),0).Count==0,"Initial rule samples establish a baseline without a popup.");
        var hits=evaluator.Evaluate(rules,Value("second"),1000);
        check(hits.Count==2,"Two change rules watching the same variable both observe the same previous sample.");evaluator.Presented(hits,1000);
        check(evaluator.Evaluate(rules,Value("third"),1500).Count==0 && evaluator.Evaluate(rules,Value("fourth"),3100).Count==2,"Each rule respects its own monotonic cooldown.");
        evaluator.Evaluate(rules,Value(null),4000);
        check(evaluator.Evaluate(rules,Value("recovered"),5000).Count==0,"A missing sample followed by recovery cannot create a track-change event.");
        evaluator.Reset();var threshold=new HudTriggerRule {Id="threshold",Variable="value",Value=0};
        evaluator.Evaluate(new[]{threshold},Value(-1d),0);
        check(evaluator.Evaluate(new[]{threshold},Value(0d),1).Count==1,"A legitimate zero participates in threshold comparisons.");
        foreach(var unavailable in new object?[]{null,double.NaN,double.PositiveInfinity,"--","N/A"})
            check(evaluator.Evaluate(new[]{threshold},Value(unavailable),2).Count==0,"Unavailable data cannot become a synthetic zero threshold event.");
        evaluator.Reset();var edge=a with {Mode="BecomesTrue"};evaluator.Evaluate(new[]{edge},Value("false"),0);
        check(evaluator.Evaluate(new[]{edge},Value("true"),1).Count==1 && evaluator.Evaluate(new[]{edge},Value("true"),2).Count==0,"Boolean text creates a single false-to-true edge.");
        evaluator.Reset();evaluator.Evaluate(new[]{a},Value(1d),0);
        check(evaluator.Evaluate(new[]{a},Value("1.0"),1).Count==0,"Equivalent numeric representations do not produce change events.");
    }
    private static void Media(Action<bool,string> check)
    {
        var values=new Dictionary<string,object?>();var now=DateTimeOffset.UtcNow;
        var playing=new MediaSessionProvider.Reading("Track","Artist","Album","test","Playing",0,100,10,now.AddSeconds(-2),1.5,true);
        MediaSessionProvider.Write(values,playing,now);
        check(Convert.ToDouble(values["media.position"])==13 && Convert.ToDouble(values["media.progress"])==13,"Media playback uses the reported timeline timestamp and playback rate.");
        MediaSessionProvider.Write(values,playing with {Status="Paused"},now.AddHours(1));
        check(Convert.ToDouble(values["media.position"])==10 && (bool)values["media.is_paused"]!,"Paused timelines do not advance with wall-clock time.");
        MediaSessionProvider.Write(values,playing with {Start=50,End=150,Position=50,Updated=now},now);
        check(values["media.position_text"]?.ToString()=="00:00" && Convert.ToDouble(values["media.duration"])==100,"Valid zero positions format as 00:00 and nonzero start offsets are respected.");
        MediaSessionProvider.Write(values,playing,now.AddHours(1));
        check(Convert.ToDouble(values["media.progress"])==100,"Extrapolated playback is clamped to the reported endpoint.");
        MediaSessionProvider.Write(values,null,now);
        check(!(bool)values["media.available"]! && values["media.progress"] is null && values["media.position"] is null,"No session produces availability state without fabricated timeline measurements.");
        MediaSessionProvider.Write(values,playing with {End=0,HasTimeline=false},now);
        check(values["media.progress"] is null && !(bool)values["media.has_timeline"]!,"A live stream with no duration does not invent a progress percentage.");
        check(VariableCatalog.AllBuiltIns.Count(v=>v.Key.StartsWith("media."))==18,"The media variable library exposes all 18 fields.");
        LocalizationManager.SetLanguage(AppLanguage.English);
        check(VariableLocalization.Localize(VariableCatalog.Find("media.title")!).Name=="Track title","Media variable names and descriptions have explicit English localization.");
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
    }
    private static void HotZone(Action<bool,string> check)
    {
        var screen=new PixelRect(-1920,-100,1920,1080);var options=new AppSettings();
        check(HudHotZone.Bounds(screen,options)==new PixelRect(-1080,-100,240,20),"Default hot zone uses full-screen bounds, including menu and negative monitor origins.");
        check(HudHotZone.Bounds(screen,options with {HotZonePosition=HudPosition.BottomRight,HotZoneOffsetX=int.MaxValue,HotZoneOffsetY=int.MaxValue})==new PixelRect(-240,960,240,20),"Custom hotspot offsets stay reachable within the target screen.");
        check(HudHotZone.Bounds(screen,options with {HotZoneWidth=int.MaxValue,HotZoneHeight=int.MaxValue})==screen,"Oversized hotspot dimensions are clipped to the screen.");
        var gate=new HotZoneDwellGate();gate.Observe(true,1000);
        check(!gate.Ready(1499,500) && gate.Ready(1500,500),"Dwell requires the entire configured interval.");gate.Presented();
        check(!gate.Ready(9999,500),"Holding the pointer in the zone cannot repeatedly summon HUDs.");gate.Observe(false,10000);gate.Observe(true,10100);
        check(!gate.Ready(10599,500) && gate.Ready(10600,500),"Leaving and re-entering, including during an animation, re-arms the dwell timer.");
    }
    private static async Task Until(Func<bool> condition,int milliseconds=6000)
    {
        var end=Environment.TickCount64+milliseconds;while(!condition() && Environment.TickCount64<end)await Task.Delay(20);
        if(!condition())throw new TimeoutException("HUD did not settle in time.");
    }
    internal static async Task LiveMedia(Action<bool,string> check)
    {
        using var provider=new MediaSessionProvider();var values=new Dictionary<string,object?>();
        await provider.EnrichAsync(values,CancellationToken.None);
        check(values.Count==18,"The real Windows media collector returns a complete typed snapshot.");
        check(values["media.status_raw"]?.ToString()!="Unavailable","Windows GSMTC access succeeds in the live desktop session.");
        Console.WriteLine($"Live GSMTC: available={values["media.available"]}, timeline={values["media.has_timeline"]}, status={values["media.status_raw"]}");
    }
    internal static void RunLiveMedia(Action<bool,string> check)
    {
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(15));Exception? failure=null;bool completed=false;
        Dispatcher.UIThread.Post(async()=>{try{await LiveMedia(check);completed=true;}catch(Exception ex){failure=ex;}finally{finish.Cancel();}});
        Dispatcher.UIThread.MainLoop(finish.Token);
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if(!completed)throw new TimeoutException("Live GSMTC read timed out.");
    }
    private static async Task MediaTimeout(Action<bool,string> check)
    {
        var pending=new TaskCompletionSource<MediaSessionProvider.Reading?>(TaskCreationOptions.RunContinuationsAsynchronously);int reads=0;
        var provider=new MediaSessionProvider(()=>{reads++;return pending.Task;});var values=new Dictionary<string,object?>();
        var clock=System.Diagnostics.Stopwatch.StartNew();await provider.EnrichAsync(values,CancellationToken.None);
        check(clock.ElapsedMilliseconds<2500 && values["media.progress"] is null,"An unresponsive media broker is bounded and cannot leave stale progress values.");
        using var cancelled=new CancellationTokenSource(30);
        try{await provider.EnrichAsync(values,cancelled.Token);}catch(OperationCanceledException){}
        clock.Restart();provider.Dispose();
        check(reads==1 && clock.ElapsedMilliseconds<100,"Repeated samples retain one pending media read and shutdown never waits for it.");pending.SetResult(null);
    }
    private static async Task Events(Action<bool,string> check)
    {
        using var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;string url=$"http://127.0.0.1:{port}/";
        int measured=0;
        var server=Task.Run(async ()=>
        {
            try {while(true){using var client=await listener.AcceptTcpClientAsync();using var stream=client.GetStream();using var reader=new StreamReader(stream,leaveOpen:true);
                while(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)) is {Length:>0}){}
                var body=System.Text.Encoding.UTF8.GetBytes("{\"value\":"+Volatile.Read(ref measured)+"}");
                var head=System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");await stream.WriteAsync(head);await stream.WriteAsync(body);}}
            catch(System.Net.Sockets.SocketException){}catch(ObjectDisposedException){}
        });
        var profiles=Enumerable.Range(0,2).Select(i=>new HudProfile {Id="event-"+i,Name="Event "+i,PrimaryTemplate="EVENT"+i,SecondaryTemplate="",RightTemplate="50",TitleTemplate="Event",TaglineTemplate="",ProgressVariable="=50",AnimationMode="Simple"}).ToList();
        var options=new AppSettings {DisplayDurationSeconds=3,HotZoneEnabled=false,SamplingIntervalSeconds=.1,CustomHud=new(){Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList(),CycleAnimationMode="Simple",
            HttpSources=new(){new(){Name="events",Url=url,RefreshSeconds=5,Fields=new(){new(){Variable="level",JsonPath="value"}}}},Triggers=new(){new(){Variable="custom.events.level",Value=50,CooldownSeconds=1}}}};
        var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);hud.ApplySettings(options);runtime.ApplySettings(options);
        var probe=CustomHudSettings.CreateDefaultProfiles().Single(p=>p.BuiltInKey=="network.ping");
        var active=profiles[0] with {FrameWindowId="active-window",GpuAdapterId="active-gpu"};
        runtime.ApplySettings(options with {CustomHud=options.CustomHud with {Profiles=new(){active,profiles[1],probe},CycleProfileIds=new(){active.Id,profiles[1].Id,probe.Id}}});
        var context=runtime.GetTriggerSamplingContext();
        check(context.Settings.ActiveProfileId==active.Id && context.Active?.FrameWindowId=="active-window" && context.Active.GpuAdapterId=="active-gpu" && context.Probe?.Id==probe.Id,"Probe rules retain their queue target without replacing the active GPU or frame-capture context.");
        runtime.ApplySettings(options);
        var poll=typeof(CustomHudRuntime).GetMethod("TickTriggerRulesAsync",Private)!;
        Task<bool> Poll() {typeof(CustomHudRuntime).GetField("_nextTriggerPoll",Private)!.SetValue(runtime,0L);return (Task<bool>)poll.Invoke(runtime,null)!;}
        try
        {
            check(!await Poll() && !hud.IsVisible,"A real HTTP trigger source establishes its baseline without summoning.");
            Volatile.Write(ref measured,100);await Task.Delay(5100);var first=Poll();await Until(()=>hud.IsVisible);
            check(hud.FindControl<TextBlock>("PrimaryText")!.Text=="EVENT0","A real threshold crossing summons the first queued HUD profile.");check(await first,"The triggered HUD completes its normal transient animation.");
            Volatile.Write(ref measured,0);await Task.Delay(5100);check(!await Poll(),"A threshold that no longer matches does not summon.");
            Volatile.Write(ref measured,100);await Task.Delay(5100);var second=Poll();await Until(()=>hud.IsVisible);
            check(hud.FindControl<TextBlock>("PrimaryText")!.Text=="EVENT1","A later real event advances to the next queued profile.");await second;
        }
        finally {runtime.Dispose();hud.Close();listener.Stop();await server;}
    }
    private static async Task Animations(Action<bool,string> check)
    {
        var profiles=Enumerable.Range(0,2).Select(i=>new HudProfile{Id="next-"+i,Name="Next "+i,PrimaryTemplate="PROFILE"+i,SecondaryTemplate="",RightTemplate="50",TitleTemplate="Test",TaglineTemplate="",ProgressVariable="=50",AnimationMode="Simple"}).ToList();
        var options=new AppSettings {AlwaysVisible=true,ClickToCycle=true,DisplayDurationSeconds=3,HudHotkeyShowAnimation="Simple",HotZoneEnabled=false,
            PersistentLayer=PersistentHudLayer.Topmost,CustomHud=new(){Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList(),CycleAnimationMode="Roll"}};
        var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);SettingsWindow? window=null;
        try
        {
            hud.ApplySettings(options);runtime.ApplySettings(options);runtime.Start();await Until(()=>hud.IsVisible && !hud.IsHudBusy);
            var hide=runtime.ToggleUserVisibilityAsync();await Task.Delay(65);
            var scale=(ScaleTransform)hud.FindControl<Grid>("ScaleHost")!.RenderTransform!;
            check(hud.IsVisible && scale.ScaleX>0 && scale.ScaleX<1,"Shortcut hiding visibly animates the capsule before hiding its HWND.");await hide;
            check(!hud.IsVisible,"Shortcut hiding finishes with the window hidden.");
            var show=runtime.ToggleUserVisibilityAsync();await Task.Delay(200);
            check(hud.IsVisible && hud.IsHudBusy && hud.FindControl<StackPanel>("TitleHost")!.Opacity==0,"Simple shortcut reveal uses the simple intro.");await show;
            runtime.ApplySettings(options with {HudHotkeyShowAnimation="Full"});await runtime.ToggleUserVisibilityAsync();show=runtime.ToggleUserVisibilityAsync();await Task.Delay(1700);
            check(hud.IsVisible && hud.FindControl<Border>("Pill")!.Height>65 && hud.FindControl<StackPanel>("TitleHost")!.Opacity>0,"Full shortcut reveal uses the expanded title and shape-changing intro.");await show;
            runtime.ApplySettings(options);await Until(()=>!hud.IsHudBusy);
            int hidden=0;hud.PropertyChanged+=(_,e)=>{if(e.Property==Window.IsVisibleProperty && e.NewValue is false)hidden++;};
            var handle=hud.TryGetPlatformHandle()!.Handle;var pill=hud.FindControl<Border>("Pill")!;var bounds=pill.Bounds;
            typeof(CustomHudRuntime).GetMethod("OnHudClicked",Private)!.Invoke(runtime,null);await Task.Delay(220);
            var offset=(TranslateTransform)hud.FindControl<Grid>("ContentHost")!.RenderTransform!;
            check(offset.Y<0 && offset.Y>-60 && hud.IsVisible,"Rolling moves all content upward inside the clipped capsule.");await Task.Delay(190);
            check(hud.FindControl<TextBlock>("PrimaryText")!.Text=="PROFILE1" && ((TranslateTransform)hud.FindControl<Grid>("ContentHost")!.RenderTransform!).Y>0,"The next profile replaces hidden content and enters from below.");await Until(()=>!hud.IsHudBusy);
            check(hidden==0 && hud.TryGetPlatformHandle()!.Handle==handle && pill.Bounds==bounds && ((TranslateTransform)hud.FindControl<Grid>("ContentHost")!.RenderTransform!).Y==0,"A roll preserves native window, capsule geometry and exact final content position.");
            for(int i=0;i<3;i++)typeof(CustomHudRuntime).GetMethod("OnHudClicked",Private)!.Invoke(runtime,null);
            await ((Task)typeof(CustomHudRuntime).GetField("_lastOperation",Private)!.GetValue(runtime)!).WaitAsync(TimeSpan.FromMilliseconds(1800));
            check(hidden==0 && hud.IsVisible && ((TranslateTransform)hud.FindControl<Grid>("ContentHost")!.RenderTransform!).Y==0,"Interrupted successive rolls remain in-place and settle without falling back to a full summon.");
            window=new SettingsWindow(options,hud,runtime);var view=window.FindControl<HudCustomizerView>("Customizer")!;
            check(view.FindControl<ComboBox>("CycleAnimationModeCombo")!.SelectedIndex==2 && window.FindControl<ComboBox>("HudHotkeyAnimationCombo")!.SelectedIndex==0,"Settings exposes independent roll and shortcut-reveal animation choices.");
            view.FindControl<Button>("AddTriggerBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FindControl<TextBox>("TriggerVariableBox")!.Text="media.title";view.FindControl<ComboBox>("TriggerModeCombo")!.SelectedIndex=1;
            var exported=view.ExportSettings();check(exported.Triggers.Single().Mode=="Change" && exported.Triggers.Single().Variable=="media.title","The rule editor exports enabled changes without hand-editing JSON.");
            check(!window.FindControl<Border>("HotZonePanel")!.IsEnabled,"Persistent instances clearly disable transient hotspot controls.");
        }
        finally {window?.Close();runtime.Dispose();hud.Close();}
    }
}
