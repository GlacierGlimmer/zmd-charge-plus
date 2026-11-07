using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Windows.Automation;
using Avalonia;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class Probe
{
    [STAThread]static int Main(string[] args)
    {
        var buffer=new StringBuilder(256);GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,buffer,512,out _);
        if(!buffer.ToString().StartsWith("ECP-Audit-"))throw new Exception("Run only through the private desktop launcher.");
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        if(Process.GetProcessesByName("Taskmgr").Length!=0)
        {File.WriteAllText(Path.Combine(output,"result.json"),"{\"tested\":false,\"reason\":\"Task Manager is already running; it was not touched.\"}");return 0;}
        AppBuilder.Configure<EndfieldChargePlus.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
        Exception? failure=null;using var done=new CancellationTokenSource(TimeSpan.FromSeconds(95));
        Dispatcher.UIThread.Post(async()=>{try{await Run(output);}catch(Exception ex){failure=ex;}finally{done.Cancel();}});
        Dispatcher.UIThread.MainLoop(done.Token);
        if(failure is not null){File.WriteAllText(Path.Combine(output,"FAIL.txt"),failure.ToString());return 1;}return 0;
    }
    private static async Task Run(string output)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"Taskmgr.exe")){UseShellExecute=false};
        start.Environment["__COMPAT_LAYER"]="RunAsInvoker";
        using var manager=Process.Start(start)??throw new IOException("Task Manager did not start.");
        HudWindow? hud=null;AutomationElement? toggle=null;ToggleState? original=null;NativeRect rect=default;bool moved=false;
        var evidence=new Dictionary<string,object>{{"tested",false},{"noGlobalInput",true}};
        try
        {
            IntPtr hwnd=IntPtr.Zero;
            for(int i=0;i<80 && hwnd==IntPtr.Zero;i++){if(manager.HasExited)break;manager.Refresh();hwnd=manager.MainWindowHandle;await Task.Delay(100);}
            if(hwnd==IntPtr.Zero)throw new Exception("No Task Manager window was created on the private desktop.");
            // The process must remain on the private desktop; do not automate an existing user's window.
            uint thread=GetWindowThreadProcessId(hwnd,out uint owner);var desktop=new StringBuilder(256);
            GetUserObjectInformation(GetThreadDesktop(thread),2,desktop,512,out _);
            if(owner!=manager.Id || !desktop.ToString().StartsWith("ECP-Audit-"))throw new Exception("Refusing a non-private Task Manager window.");
            toggle=await Task.Run(()=>FindTopmost(hwnd));
            if(toggle is null)throw new Exception("Task Manager topmost toggle was not exposed; no setting was changed.");
            var pattern=(TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern);original=pattern.Current.ToggleState;
            if(original!=ToggleState.On)await Task.Run(()=>pattern.Toggle());
            await Task.Delay(250);
            if(pattern.Current.ToggleState!=ToggleState.On || (GetWindowLong(hwnd,-20)&8)==0)throw new Exception("Actual Task Manager Always on top mode was not enabled.");
            GetWindowRect(hwnd,out rect);moved=true;MoveWindow(hwnd,200,0,1100,760,false);
            var settings=new AppSettings{AlwaysVisible=true,PersistentLayer=PersistentHudLayer.SuperTopmost,GlobalScale=.8};
            hud=new HudWindow();hud.ApplySettings(settings);
            await hud.ShowPersistentAsync(new HudRenderData("","Layer probe","ECP","","1","%",.5,"cpu","cpu","#FFFA00",true));
            await Task.Delay(900);
            var handle=hud.TryGetPlatformHandle()!.Handle;
            bool Above(){for(var next=GetWindow(handle,3);next!=IntPtr.Zero;next=GetWindow(next,3))if(next==hwnd)return false;return true;}
            var observed=new List<bool>();
            for(int i=0;i<3;i++){SetWindowPos(hwnd,new IntPtr(-1),0,0,0,0,0x1|0x2|0x10);await Task.Delay(650);observed.Add(Above());}
            evidence["tested"]=true;evidence["privateDesktop"]=desktop.ToString();evidence["actualTaskManagerTopmost"]=true;
            evidence["hudAboveTaskManager"]=observed.All(x=>x);evidence["samples"]=observed;
        }
        finally
        {
            hud?.Close();
            bool restored=original is null;
            if(toggle is not null && original is {} state)
            {try{restored=await Task.Run(()=>{var pattern=(TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern);if(pattern.Current.ToggleState!=state)pattern.Toggle();return pattern.Current.ToggleState==state;});}catch{}}
            if(moved && manager.MainWindowHandle!=IntPtr.Zero)MoveWindow(manager.MainWindowHandle,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,false);
            if(!manager.HasExited){manager.CloseMainWindow();await Task.Delay(400);if(!manager.HasExited)manager.Kill();}
            evidence["originalSettingRestored"]=restored;
            File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(evidence));
        }
    }
    private static AutomationElement? FindTopmost(IntPtr hwnd)
    {
        var root=AutomationElement.FromHandle(hwnd);
        AutomationElement[] All()=>root.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().ToArray();
        AutomationElement? Toggle()=>All().FirstOrDefault(e=>e.TryGetCurrentPattern(TogglePattern.Pattern,out _) &&
            (e.Current.AutomationId.Contains("AlwaysOnTop",StringComparison.OrdinalIgnoreCase)||e.Current.Name.Contains("Always on top",StringComparison.OrdinalIgnoreCase)||e.Current.Name.Contains("始终置顶")||e.Current.Name.Contains("置于顶层")));
        var result=Toggle();if(result is not null)return result;
        var settings=All().FirstOrDefault(e=>e.Current.Name is "Settings" or "设置" || e.Current.AutomationId is "SettingsButton" or "SettingsNavItem");
        if(settings is not null)
        {
            if(settings.TryGetCurrentPattern(InvokePattern.Pattern,out var invoke))((InvokePattern)invoke).Invoke();
            else if(settings.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var selection))((SelectionItemPattern)selection).Select();
            Thread.Sleep(300);return Toggle();
        }
        return null;
    }
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder text,uint size,out uint needed);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")]static extern bool MoveWindow(IntPtr hwnd,int x,int y,int width,int height,bool repaint);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}
