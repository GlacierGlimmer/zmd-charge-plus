using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

// Opt-in because the real Explorer Show Desktop command temporarily minimizes
// other windows. Restore captured placements even if the user changes focus.
internal static class DesktopShellChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var done=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Exception? failure=null;
        Dispatcher.UIThread.Post(async ()=> { try { await CheckAsync(check); } catch(Exception ex) { failure=ex; } finally { done.Cancel(); } });
        Dispatcher.UIThread.MainLoop(done.Token);
        if(failure is not null) throw failure;
        var registered=typeof(EndfieldChargePlus.Interop.WindowsHudLayer).GetField("DesktopLayers",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        check((int)registered.GetType().GetProperty("Count")!.GetValue(registered)! == 0,"Closing the HUDs releases every registration in the shared desktop watchdog.");
    }
    private static async Task CheckAsync(Action<bool,string> check)
    {
        var foreground=GetForegroundWindow();
        Dictionary<IntPtr,WindowPlacement>? placements=null;
        var fixture=new Window { Title="ECP isolated Show Desktop audit",Width=160,Height=100,Position=new PixelPoint(30,400),ShowInTaskbar=true };
        var hud=new HudWindow();
        var profile=new HudProfile { Id="desktop-shell",Name="Desktop audit",PrimaryTemplate="DESKTOP",SecondaryTemplate="",RightTemplate="50",ProgressVariable="=50",AnimationMode="Simple" };
        var settings=new AppSettings { AlwaysVisible=true,PersistentLayer=PersistentHudLayer.Desktop,PositionMode=HudPositionMode.CustomCoordinates,HudCustomX=500,HudCustomY=180,
            CustomHud=new() { Profiles=new() { profile },ActiveProfileId=profile.Id },MultiHudEnabled=true,
            HudInstances=new() { new() { Id="desktop-second",Settings=new AppSettings { AlwaysVisible=true,PersistentLayer=PersistentHudLayer.Topmost,PositionMode=HudPositionMode.CustomCoordinates,HudCustomX=500,HudCustomY=300,CustomHud=new() { Profiles=new() { profile },ActiveProfileId=profile.Id } } } } };
        using var runtime=new CustomHudRuntime(hud);
        try
        {
            fixture.Show();fixture.Activate();
            hud.ApplySettings(settings);runtime.ApplySettings(settings);runtime.Start();
            await Task.Delay(1800);
            var second=runtime.GetInstanceHud("desktop-second")!;
            var handle=hud.TryGetPlatformHandle()!.Handle;
            var secondHandle=second.TryGetPlatformHandle()!.Handle;
            var point=hud.FindControl<Border>("Pill")!.PointToScreen(new Point(20,30));
            var secondPoint=second.FindControl<Border>("Pill")!.PointToScreen(new Point(20,30));
            uint before=Pixel(point),secondBefore=Pixel(secondPoint);
            var fixtureHandle=fixture.TryGetPlatformHandle()!.Handle;
            SetWindowPos(fixtureHandle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0010);
            await Task.Delay(300);
            var fixturePoint=fixture.PointToScreen(new Point(80,50));
            var beforeWindow=GetAncestor(WindowFromPoint(fixturePoint),2);
            check(secondBefore==0x302F31,"The topmost fixture capsule is visibly rendered before Explorer Show Desktop.");
            placements=CapturePlacements();
            ToggleDesktop();
            var desktopFrames=new List<uint>();
            var desktopZ=new List<string>();
            for(int i=0;i<60;i++)
            {
                await Task.Delay(30);
                var active=GetForegroundWindow();var cls=Class(active);
                if(i>=15 && (cls=="Progman" || cls=="WorkerW"))
                {
                    desktopFrames.Add(Pixel(point));
                    desktopZ.Add($"{active:X}:{GetWindow(handle,3):X}:{GetWindow(handle,2):X}");
                }
            }
            bool minimized=IsIconic(fixture.TryGetPlatformHandle()!.Handle);
            var afterWindow=GetAncestor(WindowFromPoint(fixturePoint),2);
            var fg=GetForegroundWindow();
            var layer=typeof(HudWindow).GetField("_windowLayer",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(hud)!;
            var host=(IntPtr)layer.GetType().GetField("_lastDesktopHost",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(layer)!;
            Console.WriteLine($"Desktop host={host:X} ({Class(host)}), foreground={fg:X} ({Class(fg)}), HUD prev={GetWindow(handle,3):X}, next={GetWindow(handle,2):X}, shell prev={GetWindow(host,3):X}");
            Console.WriteLine($"Stable desktop frames={desktopFrames.Count}; colors={string.Join(',',desktopFrames.Distinct().Select(x=>$"0x{x:X6}"))}; Z={string.Join(',',desktopZ.Distinct())}");
            uint after=Pixel(point),secondAfter=Pixel(secondPoint);
            Console.WriteLine($"Explorer Show Desktop: fixtureMinimized={minimized}, fixtureVisible={IsWindowVisible(fixtureHandle)}, fixture={fixtureHandle:X}, atFixtureBefore={beforeWindow:X}, atFixtureAfter={afterWindow:X}, foreground={fg:X}, desktopIconic={IsIconic(handle)}, topmostIconic={IsIconic(secondHandle)}, before=0x{before:X6}, after=0x{after:X6}, second=0x{secondAfter:X6}");
            check(beforeWindow==fixtureHandle && (minimized || !IsWindowVisible(fixtureHandle) || afterWindow!=fixtureHandle),"The real Explorer command covered or minimized the ordinary fixture.");
            check(desktopFrames.Count>=10,"At least ten stable samples were captured while the real Explorer desktop had focus.");
            check(!IsIconic(handle) && desktopFrames.All(x=>x==0x302F31),"ECP-18/45: desktop-mode HUD stays visibly rendered after actual Show Desktop.");
            check(!IsIconic(secondHandle) && secondAfter==secondBefore,"Topmost secondary HUD also survives actual Show Desktop.");
            RestorePlacements(placements);placements=null;
            fixture.Activate();
            await Task.Delay(1000);
            Console.WriteLine($"Restored: fixtureIconic={IsIconic(fixtureHandle)}, desktopStyle=0x{GetWindowLong(handle,-20):X8}, secondStyle=0x{GetWindowLong(secondHandle,-20):X8}");
            check(!IsIconic(fixtureHandle) && (GetWindowLong(handle,-20)&8)==0 && (GetWindowLong(secondHandle,-20)&8)!=0,"Restoring applications preserves both independent HUD layers.");
        }
        finally
        {
            if(placements is not null) RestorePlacements(placements);
            runtime.Dispose();hud.Close();fixture.Close();
            if(foreground!=IntPtr.Zero) SetForegroundWindow(foreground);
        }
    }
    private static uint Pixel(PixelPoint p) { var dc=GetDC(IntPtr.Zero);try { return GetPixel(dc,p.X,p.Y); } finally { ReleaseDC(IntPtr.Zero,dc); } }
    private static void ToggleDesktop()
    {
        keybd_event(0x5b,0,0,UIntPtr.Zero);keybd_event(0x44,0,0,UIntPtr.Zero);
        keybd_event(0x44,0,2,UIntPtr.Zero);keybd_event(0x5b,0,2,UIntPtr.Zero);
    }
    private static string Class(IntPtr h) { var b=new System.Text.StringBuilder(256);GetClassName(h,b,256);return b.ToString(); }
    private static Dictionary<IntPtr,WindowPlacement> CapturePlacements()
    {
        var result=new Dictionary<IntPtr,WindowPlacement>();
        EnumWindows((h,_)=>
        {
            var cls=Class(h);
            if(!IsWindowVisible(h) || IsIconic(h) || (GetWindowLong(h,-20)&0x08000080)!=0 || cls=="Progman" || cls=="WorkerW") return true;
            var p=new WindowPlacement { Length=Marshal.SizeOf<WindowPlacement>() };
            if(GetWindowPlacement(h,ref p)) result[h]=p;
            return true;
        },IntPtr.Zero);
        return result;
    }
    private static void RestorePlacements(Dictionary<IntPtr,WindowPlacement> placements)
    {
        foreach(var entry in placements)
            if(IsIconic(entry.Key)) { var placement=entry.Value;SetWindowPlacement(entry.Key,ref placement); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPlacement { public int Length,Flags,ShowCommand;public NativePoint Min,Max;public NativeRect Normal; }
    private delegate bool EnumProc(IntPtr h,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr h,ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr h,ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PixelPoint p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr h,System.Text.StringBuilder name,int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc,int x,int y);
}
