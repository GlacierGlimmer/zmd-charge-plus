using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class RuntimeRegressionChecks
{
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static void Run(Action<bool,string> check)
    {
        using var finished=new CancellationTokenSource(TimeSpan.FromSeconds(180));
        Exception? failure=null;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await CheckAsync(check); await InteractionRegressionChecks.Run(check); }
            catch (Exception ex) { failure=ex; }
            finally { finished.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(finished.Token);
        if (failure is not null) throw failure;
    }
    private static async Task CheckAsync(Action<bool,string> check)
    {
        WindowsTrayIcon? tray=null;
        try { tray=new WindowsTrayIcon(()=>{},()=>{},"ECP native callback regression"); }
        catch(InvalidOperationException) { } // Explorer can reject an icon in a restricted test session.
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        await Task.Delay(200);
        tray?.Dispose();
        var hud=new HudWindow();
        var fontControls=new[] { "TagLineText","TitleText","PrimaryText","SecondaryText","RightText","RightSuffixText" };
        hud.ApplySettings(new() { HudFontFamily="Courier New" });
        check(fontControls.All(n=>hud.FindControl<TextBlock>(n)!.FontFamily.Name=="Courier New"),"Font changes reach all real XAML text controls before the window is shown.");
        var text=hud.FindControl<TextBlock>("PrimaryText")!; text.Text="11111111";
        text.Measure(Size.Infinity); var mono=text.DesiredSize.Width;
        hud.ApplySettings(new() { HudFontFamily="Arial" }); text.Measure(Size.Infinity);
        check(Math.Abs(text.DesiredSize.Width-mono)>1,"Changing HUD font changes the measured rendered text width.");
        hud.ApplySettings(new());
        check(text.FontFamily.Name.Contains("Inter"),"System default restores bundled font after customization.");
        var profiles=new List<HudProfile>();
        for (int i=0;i<3;i++) profiles.Add(new() { Id=$"native-test-{i}",Name=$"Test {i}",PrimaryTemplate=$"PROFILE{i}",SecondaryTemplate="",RightTemplate="50",TitleTemplate="Test",TaglineTemplate="",ProgressVariable="= 50",AnimationMode="Simple" });
        var options=new AppSettings { AlwaysVisible=true,ClickToCycle=true,PersistentLayer=PersistentHudLayer.Topmost,SamplingIntervalSeconds=.1,
            CustomHud=new() { Profiles=profiles,ActiveProfileId=profiles[0].Id,CycleProfileIds=profiles.Select(p=>p.Id).ToList(),CycleAnimationMode="Simple",AutoCycle=false } };
        using var runtime=new CustomHudRuntime(hud);
        hud.ApplySettings(options); runtime.ApplySettings(options); runtime.Start();
        await Task.Delay(1200);
        check(hud.IsVisible && text.Text=="PROFILE0","Persistent startup shows the selected actual HUD.");
        var visualHandle=hud.TryGetPlatformHandle()!.Handle;
        var interaction=(WindowsHudInteraction)typeof(HudWindow).GetField("_interaction",Private)!.GetValue(hud)!;
        var handle=interaction.InputHandle;
        check(handle!=IntPtr.Zero && (GetWindowLong(visualHandle,-20)&0x20)!=0,"Capsule input HWND exists while the larger animated host remains mouse-through.");

        var pill=hud.FindControl<Border>("Pill")!;
        var screen=pill.PointToScreen(new Point(pill.Bounds.Width/2,pill.Bounds.Height/2));
        var client=screen; ScreenToClient(handle,ref client);
        var point=new IntPtr((client.Y<<16) | (client.X&65535));
        SendMessage(handle,0x202,IntPtr.Zero,point);
        await Task.Delay(150);
        check(text.Text=="PROFILE0","A stray mouse-up cannot switch HUD profiles.");
        SendMessage(handle,0x201,new IntPtr(1),point); SendMessage(handle,0x202,IntPtr.Zero,point);
        await Task.Delay(900);
        check(text.Text=="PROFILE1","One real HWND down/up pair advances exactly one queue entry.");
        SendMessage(handle,0x201,new IntPtr(1),point); SendMessage(handle,0x202,IntPtr.Zero,new IntPtr(0));
        await Task.Delay(500);
        check(text.Text=="PROFILE1","Dragging/releasing outside the visible HUD cannot switch profiles.");
        for (int i=0;i<3;i++) { SendMessage(handle,0x201,new IntPtr(1),point); SendMessage(handle,0x202,IntPtr.Zero,point); }
        await Task.Delay(1200);
        check(text.Text=="PROFILE1","Rapid real clicks retain the exact requested queue order without duplicate events.");
        await Task.Delay(700);
        check(text.Text=="PROFILE1","No automatic switch occurs when automatic cycling is off.");
        for(int pass=0;pass<20;pass++)
        {
            var instances=Enumerable.Range(0,15).Select(i=>new HudInstanceSettings { Id=$"stress-{i}",Settings=options with { ClickToCycle=false,HudOffsetY=i*4,HudFontFamily=i%2==0 ? "Arial" : "Courier New" } }).ToList();
            runtime.ApplySettings(options with { MultiHudEnabled=true,HudInstances=instances });
            if (pass%4==0) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            await Task.Delay(20);
            var highlight=runtime.HighlightInstanceAsync("stress-10","Stress test");
            runtime.ApplySettings(options with { MultiHudEnabled=false });
            await highlight;
            check(!GetInstances(runtime).Cast<object>().Any(),"Removing all HUD instances leaves no live instance entries.");
        }
        await runtime.ApplySettingsWithTransitionAsync(options);
        check(hud.IsVisible && text.Text=="PROFILE0","Primary HUD still works after repeated multi-instance creation/highlight/deletion.");
        var before=FrameRateBackend.Windows().Count;
        var temp=CreateWindowEx(0,"STATIC","ECP target lifecycle",0x10000000,0,0,100,100,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        // HWND belongs to this process and is intentionally excluded from the normal picker.
        var reading=FrameRateBackend.Read("window",$"{temp.ToInt64():x}:{Environment.ProcessId}");
        DestroyWindow(temp);
        check(FrameRateBackend.Read("window",$"{temp.ToInt64():x}:{Environment.ProcessId}").Fps is null,"Closed HWND targets never reuse stale frames.");
        foreach(var display in FrameRateBackend.Displays()) Console.WriteLine($"Display mapping: {display.Id} -> {WindowsDisplaySources.SourceId(display.Id)}");
        runtime.Dispose(); hud.Close();
        await Task.Delay(400);
        var timer=(DispatcherTimer)typeof(CustomHudRuntime).GetField("_timer",Private)!.GetValue(runtime)!;
        check(!timer.IsEnabled && hud.IsClosed,"Disposed HUD cannot restart its timer or window.");
    }
    private static System.Collections.IEnumerable GetInstances(CustomHudRuntime runtime)=>(System.Collections.IEnumerable)typeof(CustomHudRuntime).GetField("_instances",Private)!.GetValue(runtime)!;
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr h,int i);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr h,ref PixelPoint point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
}
