using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class SuperLayerChecks
{
    internal static async Task Run(SettingsWindow owner,Action<bool,string> check,string output)
    {
        Directory.CreateDirectory(output);owner.Hide();await Task.Delay(80);
        var mode=new AppSettings{AlwaysVisible=true,PersistentLayer=PersistentHudLayer.SuperTopmost};
        var data=new HudRenderData("","Layer test","ECP","","1","%",.5,"cpu","cpu","#FFFA00",true);
        var super=new HudWindow();var desktop=new HudWindow();Process? fixture=null;
        try
        {
            super.ApplySettings(mode);await super.ShowPersistentAsync(data);
            desktop.ApplySettings(mode with {PersistentLayer=PersistentHudLayer.Desktop,HudOffsetX=300});await desktop.ShowPersistentAsync(data);
            await Task.Delay(600);
            var state=(SuperTopmostController)typeof(HudWindow).GetField("_superTopmost",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(super)!;
            check(state.IsEffective && super.Topmost && !desktop.Topmost,"enhanced and desktop-layer HUDs keep independent native modes");
            var handle=super.TryGetPlatformHandle()!.Handle;
            if(OperatingSystem.IsWindows())
            {
                var a=super.PointToScreen(default);string handshake=Path.Combine(output,Guid.NewGuid().ToString("N")+"-fixture-window.txt");
                var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};
                foreach(var arg in new[]{"--layer-fixture",handshake,a.X.ToString(),a.Y.ToString(),"1400","240"})start.ArgumentList.Add(arg);
                fixture=Process.Start(start)??throw new Exception("Native fixture did not start");
                for(int i=0;i<60 && !File.Exists(handshake);i++)await Task.Delay(50);
                var other=new IntPtr(long.Parse(File.ReadAllText(handshake)));
                GetWindowThreadProcessId(other,out uint pid);check(pid==fixture.Id,"native test window belongs to the newly created isolated fixture");
                bool Above(){int count=0;for(var w=GetWindow(handle,3);w!=IntPtr.Zero && count++<512;w=GetWindow(w,3))if(w==other)return false;return true;}
                for(int i=0;i<3;i++)
                {SetWindowPos(handle,other,0,0,0,0,1|2|16|512);check(!Above(),"foreign-process topmost fixture actually covers the HUD before recovery");await Task.Delay(650);check(Above(),"HUD recovers above a foreign-process topmost window without activation");}
                check((GetWindowLong(desktop.TryGetPlatformHandle()!.Handle,-20)&8)==0,"a second desktop HUD is not promoted by the enhanced HUD watcher");
                super.ApplySettings(mode with{PersistentLayer=PersistentHudLayer.Topmost});await Task.Delay(120);
                SetWindowPos(handle,other,0,0,0,0,1|2|16|512);check(!Above(),"ordinary HUD can be covered by another topmost window");await Task.Delay(650);
                check(!Above() && !state.IsEffective && !((System.Collections.IEnumerable)typeof(SuperTopmostController).GetField("Active",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().Contains(state),"ordinary topmost stops enhanced automatic stacking repair");
                File.WriteAllText(Path.Combine(output,"normal-stacking.txt"),"foreignWindowAbove="+!Above()+"; enhancedControllerEffective="+state.IsEffective);
            }
            else if(OperatingSystem.IsMacOS())
            {
                check(ecp_hud_level(handle)==25,"native macOS enhanced HUD uses NSStatusWindowLevel");
                var prompt=SaveErrorDialog.Create(new Exception("Native layer yield test"));prompt.Show();await Task.Delay(650);
                check(!state.IsEffective && ecp_hud_level(handle)==0,"enhanced macOS HUD yields while an ECP prompt is open");prompt.Close();await Task.Delay(650);
                check(state.IsEffective && ecp_hud_level(handle)==25,"enhanced macOS HUD restores its native level when the prompt closes");
                super.ApplySettings(mode with{PersistentLayer=PersistentHudLayer.Topmost});await Task.Delay(100);
                check(ecp_hud_level(handle)==3,"normal macOS topmost restores floating level");
            }
            else if(OperatingSystem.IsLinux())
            {
                // This asserts actual X server properties, independent of the managed selection value.
                var start=new ProcessStartInfo("xprop"){UseShellExecute=false,RedirectStandardOutput=true};
                foreach(var arg in new[]{"-id",handle.ToString(),"_NET_WM_WINDOW_TYPE"})start.ArgumentList.Add(arg);
                using var property=Process.Start(start)!;string text=await property.StandardOutput.ReadToEndAsync();await property.WaitForExitAsync();
                check(text.Contains("_NET_WM_WINDOW_TYPE_NOTIFICATION"),"native X11 enhanced HUD requests the notification window layer");
                super.ApplySettings(mode with{PersistentLayer=PersistentHudLayer.Topmost});await Task.Delay(100);
                using var restored=Process.Start(start)!;text=await restored.StandardOutput.ReadToEndAsync();await restored.WaitForExitAsync();
                check(!text.Contains("_NET_WM_WINDOW_TYPE_NOTIFICATION"),"disabling enhanced X11 restores the prior window type");
            }
        }
        finally
        {
            super.Close();desktop.Close();
            if(fixture is not null){if(!fixture.HasExited)fixture.Kill();fixture.Dispose();}
            owner.Show();
        }
        var active=(System.Collections.ICollection)typeof(SuperTopmostController).GetField("Hooks",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        check(active.Count==0,"all native event hooks are released when enhanced HUDs close");
    }
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("ecpmac")]static extern int ecp_hud_level(IntPtr window);
}
