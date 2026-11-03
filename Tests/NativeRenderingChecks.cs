using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class NativeRenderingChecks
{
    private delegate IntPtr SubclassProc(IntPtr h,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
    private static readonly SubclassProc PositionObserver=ObservePosition;
    private static int _positionChanges;
    private static IntPtr ObservePosition(IntPtr h,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data)
    {
        if(message==0x47) _positionChanges++;
        return DefSubclassProc(h,message,w,l);
    }
    internal static void Run(Action<bool,string> check)
    {
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Exception? failure=null;
        Dispatcher.UIThread.Post(async ()=> { try { await CheckAsync(check); } catch(Exception e) { failure=e; } finally { finish.Cancel(); } });
        Dispatcher.UIThread.MainLoop(finish.Token);
        if(failure is not null) throw failure;
    }
    private static async Task CheckAsync(Action<bool,string> check)
    {
        var background=new Window { Width=1500,Height=650,Position=new PixelPoint(50,80),Background=new SolidColorBrush(Color.FromRgb(53,119,167)),Topmost=true,SystemDecorations=SystemDecorations.None,ShowInTaskbar=false,ShowActivated=false };
        var hud=new HudWindow();
        var profile=new HudProfile { Id="render-test",Name="Render test",PrimaryTemplate="NATIVE",SecondaryTemplate="",RightTemplate="50",ProgressVariable="=50",AnimationMode="Simple" };
        var settings=new AppSettings { AlwaysVisible=true,ClickToCycle=true,SamplingIntervalSeconds=.1,PersistentLayer=PersistentHudLayer.Topmost,PositionMode=HudPositionMode.CustomCoordinates,HudCustomX=550,HudCustomY=180,CustomHud=new() { Profiles=new() { profile },ActiveProfileId=profile.Id } };
        settings=settings with { MultiHudEnabled=true,HudInstances=new() { new() { Id="render-secondary",Settings=settings with { HudCustomY=330 } } } };
        using var runtime=new CustomHudRuntime(hud);
        try {
            background.Show();await Task.Delay(500);
            var sample=new PixelPoint(230,190);
            uint expected=Pixel(sample);check(expected==0xA77735,$"Controlled native background is visible: 0x{expected:X6}.");
            hud.ApplySettings(settings);runtime.ApplySettings(settings);runtime.Start();await Task.Delay(1200);
            var outside=hud.PointToScreen(new Point(20,80));
            var secondary=runtime.GetInstanceHud("render-secondary")!;
            var secondaryOutside=secondary.PointToScreen(new Point(20,80));
            uint actual=Pixel(outside);
            Console.WriteLine($"Native surface transparency: hint={hud.ActualTransparencyLevel}, exterior=0x{actual:X6}, expected=0x{expected:X6}, styles=0x{GetWindowLong(hud.TryGetPlatformHandle()!.Handle,-20):X8}");
            check(actual==expected && Pixel(secondaryOutside)==expected,"Real desktop pixels outside both HUD capsules stay transparent, including their animation hosts.");
            await runtime.HighlightInstanceAsync("","Test");
            await Task.Delay(300);
            var handle=hud.TryGetPlatformHandle()!.Handle;
            check(SetWindowSubclass(handle,PositionObserver,new UIntPtr(701),UIntPtr.Zero),"Attach a native observer to the isolated test HWND.");
            var secondaryHandle=secondary.TryGetPlatformHandle()!.Handle;
            if(!SetWindowSubclass(secondaryHandle,PositionObserver,new UIntPtr(701),UIntPtr.Zero)) throw new Exception("Unable to observe the secondary native HUD.");
            _positionChanges=0;
            int flashes=0;
            try { for(int i=0;i<100;i++) { await Task.Delay(20); if(Pixel(outside)!=expected || Pixel(secondaryOutside)!=expected) flashes++; } }
            finally { RemoveWindowSubclass(handle,PositionObserver,new UIntPtr(701));RemoveWindowSubclass(secondaryHandle,PositionObserver,new UIntPtr(701)); }
            check(flashes==0,$"100 native compositor samples during live refresh/highlighting contain no black background or flicker ({flashes} failures).");
            check(_positionChanges==0,$"Steady data refresh does not rebuild or reposition the native window ({_positionChanges} mutations).");
            // Substitute an isolated opaque desktop host for pixel checks;
            // regular user windows cannot obscure or counterfeit HUD rendering.
            background.Topmost=false;
            var layerField=typeof(HudWindow).GetField("_windowLayer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            (layerField.GetValue(hud) as IDisposable)?.Dispose();
            var layer=new EndfieldChargePlus.Interop.WindowsHudLayer(handle,()=>background.TryGetPlatformHandle()!.Handle);
            layerField.SetValue(hud,layer);
            hud.ApplySettings(settings with { PersistentLayer=PersistentHudLayer.Desktop });
            await Task.Delay(400);
            var childExterior=hud.PointToScreen(new Point(20,80));
            var pill=hud.FindControl<Border>("Pill")!;
            var childInterior=pill.PointToScreen(new Point(pill.Bounds.Width/2,pill.Bounds.Height/2));
            uint interior=Pixel(childInterior);
            int delta=Enumerable.Range(0,3).Max(i=>Math.Abs((int)((interior>>(i*8))&255)-(int)((expected>>(i*8))&255)));
            bool desktopTransparent=Pixel(childExterior)==expected && delta>20 && (GetWindowLong(handle,-20)&8)==0;
            Console.WriteLine($"Desktop surface: exterior=0x{Pixel(childExterior):X6}, capsule=0x{interior:X6}, contrast={delta}");
            hud.ApplySettings(settings);await Task.Delay(600);
            check(desktopTransparent && Pixel(outside)==expected,"Desktop HUD remains genuinely rendered with transparent exterior, and desktop-to-topmost changes preserve its surface.");
        } finally { runtime.Dispose();hud.Close();background.Close(); }
    }
    private static uint Pixel(PixelPoint point) { var dc=GetDC(IntPtr.Zero);try { return GetPixel(dc,point.X,point.Y); } finally { ReleaseDC(IntPtr.Zero,dc); } }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc,int x,int y);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr h,int i);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr h,SubclassProc proc,UIntPtr id,UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr h,SubclassProc proc,UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr h,IntPtr parent);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
}
