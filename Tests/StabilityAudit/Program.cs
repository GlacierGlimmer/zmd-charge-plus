using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class Audit
{
    private static int _checks;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);_checks++;}
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    [STAThread] static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        if(args.Length==1)return Launch(output);
        var current=DesktopName(GetThreadDesktop(GetCurrentThreadId()));
        var input=OpenInputDesktop(0,false,1);var inputName=DesktopName(input);CloseDesktop(input);
        if(!current.StartsWith("ECP-Audit-") || current==inputName)return 3;
        File.WriteAllText(Path.Combine(output,"desktop.txt"),$"worker={current}\ninput={inputName}\nNo desktop switching, no injected global input.\n");
        AppBuilder.Configure<App>().UsePlatformDetect().With(new Win32PlatformOptions()).SetupWithoutStarting();
        using var done=new CancellationTokenSource(TimeSpan.FromMinutes(2));Exception? error=null;
        Dispatcher.UIThread.Post(async()=>{try{await Run(output);}catch(Exception ex){error=ex;}finally{done.Cancel();}});
        Dispatcher.UIThread.MainLoop(done.Token);
        if(error is not null){File.WriteAllText(Path.Combine(output,"FAIL.txt"),error.ToString());Console.Error.WriteLine(error);return 1;}
        File.WriteAllText(Path.Combine(output,"result.txt"),$"PASS {_checks} targeted stability assertions\n");return 0;
    }
    private static async Task Run(string output)
    {
        LocalizationManager.Initialize("zh-CN");
        var profiles=Enumerable.Range(0,25).Select(i=>new HudProfile{Id="stability-"+i,Name="Profile "+i,PrimaryTemplate="SAFE",ProgressVariable="=50",AnimationMode="Simple"}).ToList();
        // This exact source path crashed while disposing a popup. Exercise zero,
        // collapsed, narrow, empty and populated previews directly as well as live.
        foreach(int count in new[]{0,1,2,12,25})foreach(int size in new[]{0,1,4,16,31,32,80,280,360})
        {
            var ring=new ProfileRingControl{Profiles=profiles.Take(count).ToList()};
            ring.Measure(new(size,size));ring.Arrange(new(0,0,size,size));
            using(var context=new DrawingGroup().Open())ring.Render(context);
            ring.PlayOpen();ring.Advance(.1);ring.PlayClose(()=>{});ring.Advance(.5);
            ring.Arrange(default);using(var context=new DrawingGroup().Open())ring.Render(context);
            Check(true,$"render lifecycle {count}/{size}");
        }
        var invalidText=typeof(ProfileRingControl).GetMethod("DrawText",BindingFlags.Static|BindingFlags.NonPublic)!;
        foreach(double n in new[]{0d,-1,double.NaN,double.PositiveInfinity})
        {
            using(var dc=new DrawingGroup().Open())invalidText.Invoke(null,new object[]{dc,"safe",default(Point),n,13d,Colors.White,n});
            Check(true,"invalid text dimensions skipped");
        }
        // Keep sampling disabled for lifecycle stress: this run is about the
        // compositor and input, not hardware/driver stress on the user's machine.
        var settings=new AppSettings{HudEnabled=false,AlwaysVisible=true,CustomHud=new(){Profiles=profiles,ActiveProfileId=profiles[0].Id}};
        var hud=new HudWindow();hud.ApplySettings(settings);using var runtime=new CustomHudRuntime(hud);
        var taskbarWindow=new SettingsWindow(settings,hud,runtime){PersistLanguagePreference=_=>{}};
        Field<CancellationTokenSource>(taskbarWindow,"_statusLifetime").Cancel();
        taskbarWindow.Show();await Task.Delay(100);
        var taskbarHandle=taskbarWindow.TryGetPlatformHandle()!.Handle;
        uint taskbarStyle=unchecked((uint)GetWindowLong(taskbarHandle,-16));
        File.WriteAllText(Path.Combine(output,"taskbar-style.txt"),$"0x{taskbarStyle:X8}\n");
        Check((taskbarStyle & 0x00080000u)!=0,"frameless settings retains WS_SYSMENU for taskbar right-click");
        Check((taskbarStyle & 0x00030000u)==0x00030000u,"taskbar minimize/maximize capabilities remain enabled");
        Check((taskbarStyle & 0x00C00000u)==0,"native caption remains hidden");
        var systemMenu=GetSystemMenu(taskbarHandle,false);
        foreach(uint command in new uint[]{0xF020,0xF030,0xF060,0xF120})
            Check(systemMenu!=IntPtr.Zero && GetMenuState(systemMenu,command,0)!=uint.MaxValue,"taskbar system menu exposes standard command "+command);
        for(int i=0;i<4;i++)
        {
            SendMessage(taskbarHandle,0x112,new IntPtr(0xF020),IntPtr.Zero);await Task.Delay(35);
            Check(IsIconic(taskbarHandle) && taskbarWindow.WindowState==WindowState.Minimized,"native taskbar minimize reaches Avalonia");
            SendMessage(taskbarHandle,0x112,new IntPtr(0xF120),IntPtr.Zero);await Task.Delay(35);
            Check(!IsIconic(taskbarHandle) && taskbarWindow.WindowState==WindowState.Normal,"native taskbar restore reaches Avalonia");
        }
        SendMessage(taskbarHandle,0x112,new IntPtr(0xF030),IntPtr.Zero);await Task.Delay(35);
        Check(taskbarWindow.WindowState==WindowState.Maximized,"native taskbar maximize reaches Avalonia");
        SendMessage(taskbarHandle,0x112,new IntPtr(0xF020),IntPtr.Zero);await Task.Delay(35);
        SendMessage(taskbarHandle,0x112,new IntPtr(0xF120),IntPtr.Zero);await Task.Delay(35);
        Check(taskbarWindow.WindowState==WindowState.Maximized,"restore preserves previously maximized state");
        SendMessage(taskbarHandle,0x112,new IntPtr(0xF060),IntPtr.Zero);await Task.Delay(35);
        Check(!taskbarWindow.IsVisible,"native taskbar close disposes settings safely");
        for(int i=0;i<12;i++)
        {
            var window=new SettingsWindow(settings,hud,runtime){PersistLanguagePreference=_=>{}};
            Field<CancellationTokenSource>(window,"_statusLifetime").Cancel();
            window.Show();window.NavigateToSettingsSection("ring");await Task.Delay(35);
            window.NavigateToSettingsSection(i%2==0?"about":"display");
            window.NavigateToSettingsSection("ring");window.Close();await Task.Delay(25);
            Check(!window.IsVisible,"settings/preview closes during animation");
        }
        // No Shell_NotifyIcon in this fixture: even a private-desktop icon could
        // affect Explorer. Test real event routing with an unregistered object.
        var tray=(WindowsTrayIcon)RuntimeHelpers.GetUninitializedObject(typeof(WindowsTrayIcon));
        int clicks=0,doubles=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(35)};
        timer.Tick+=(_,_)=>{timer.Stop();clicks++;};
        typeof(WindowsTrayIcon).GetField("_clicked",Private)!.SetValue(tray,(Action)(()=>clicks++));
        typeof(WindowsTrayIcon).GetField("_doubleClicked",Private)!.SetValue(tray,(Action)(()=>doubles++));
        typeof(WindowsTrayIcon).GetField("_singleClick",Private)!.SetValue(tray,timer);
        typeof(WindowsTrayIcon).GetField("_version4",Private)!.SetValue(tray,true);
        tray.HandleMouseMessage(0x205);tray.HandleMouseMessage(0x7b);
        Check(clicks==1,"v4 right-click emits one menu, ignores duplicate raw release");
        tray.HandleMouseMessage(0x202);tray.HandleMouseMessage(0x400);await WaitUntil(()=>clicks==2);
        Check(clicks==2,"v4 left-click emits one menu");
        tray.HandleMouseMessage(0x400);tray.HandleMouseMessage(0x203);tray.HandleMouseMessage(0x400);await Task.Delay(70);
        Check(clicks==2 && doubles==1,"double click cancels single click and trailing select");
        tray.HandleMouseMessage(0x401);Check(clicks==3,"keyboard select opens menu once");
        tray.HandleMouseMessage(0x400);typeof(WindowsTrayIcon).GetField("_disposed",Private)!.SetValue(tray,true);timer.Stop();await Task.Delay(70);
        Check(clicks==3,"pending actions cancelled on disposal");
        var menu=new TrayMenuWindow();int settingsClicks=0;menu.SettingsClicked+=()=>settingsClicks++;
        menu.ShowAtTray();await Task.Delay(100);Check(menu.IsVisible,"custom menu stays open after show/activation");
        var item=menu.FindControl<Border>("MenuSettings")!;
        var pointer=new Avalonia.Input.Pointer(1001,PointerType.Mouse,true);
        item.RaiseEvent(new PointerPressedEventArgs(item,pointer,item,new Point(5,5),0,new PointerPointProperties(RawInputModifiers.RightMouseButton,PointerUpdateKind.RightButtonPressed),KeyModifiers.None,1));
        Check(settingsClicks==0,"right press on menu item cannot execute command");
        item.RaiseEvent(new PointerPressedEventArgs(item,pointer,item,new Point(5,5),0,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None,1));
        Check(settingsClicks==0,"left press waits for release");
        item.RaiseEvent(new PointerReleasedEventArgs(item,pointer,item,new Point(5,5),0,new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
        Check(settingsClicks==1,"left release invokes menu item once");menu.Close();
        // Stress a synthetic mouse burst WHILE this private UI thread is blocked.
        // The real OS cursor and the user's desktop are never touched.
        var delivered=new List<int>();using(var input=new WindowsRadialInput((kind,p,w)=>delivered.Add(kind)))
        {
            input.UpdateTarget(new(100,100,400,50),true,true);
            var host=typeof(WindowsRadialInput).GetField("_host",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var hookThread=Field<Thread>(host,"_thread");Check(hookThread.ManagedThreadId!=Environment.CurrentManagedThreadId,"native hook has a separate thread");
            var work=Task.Run(async()=>{
                Check(WindowsRadialInput.ProcessMessage(0x207,new(200,120),0),"middle down claimed on cached capsule");
                var clock=Stopwatch.StartNew();
                for(int i=0;i<20000;i++)
                {
                    Check(!WindowsRadialInput.ProcessMessage(0x201,new(200,120),0),"left down always passes");
                    Check(!WindowsRadialInput.ProcessMessage(0x205,new(200,120),0),"right up always passes");
                    WindowsRadialInput.ProcessMessage(0x200,new(220+i%10,120),0);
                    WindowsRadialInput.ProcessMessage(0x20A,new(220,120),120);
                }
                Check(WindowsRadialInput.ProcessMessage(0x208,new(240,120),0),"matching middle up consumed");
                double elapsed=clock.Elapsed.TotalMilliseconds;File.WriteAllText(Path.Combine(output,"input-burst-ms.txt"),elapsed.ToString());
                Check(elapsed<500,"input burst never waits for blocked UI");
                await Task.Delay(650);
                Check(!WindowsRadialInput.ProcessMessage(0x207,new(200,120),0),"stale heartbeat fails open");
            });
            Thread.Sleep(1000);await work;await Task.Delay(100);
            Check(delivered.First()==0 && delivered.Last()==2,"queued gesture preserves down/up order");
            Check(delivered.Count<=4,"20,000 moves and wheels produce bounded coalesced UI work");
            Check(delivered.Count(k=>k==1)==1 && delivered.Count(k=>k==3)==1,"latest motion and accumulated wheel delivered once");
            input.UpdateTarget(new(100,100,400,50),false,false);
            Check(!WindowsRadialInput.ProcessMessage(0x207,new(200,120),0),"disabled target never claims input");
        }
        await Task.Delay(100);
        Check(typeof(WindowsRadialInput).GetField("_host",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null) is null,"last registration releases hook host");
        hud.Close();
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        // Dispatcher timers may run after a fixed Task.Delay continuation when
        // concurrent builds load the test desktop. Wait for the real outcome.
        var deadline=Stopwatch.StartNew();
        while(!condition() && deadline.ElapsedMilliseconds<1500)await Task.Delay(20);
    }
    static int Launch(string output,string? executable=null,string[]? arguments=null)
    {
        string name="ECP-Audit-"+Guid.NewGuid().ToString("N");var desktop=CreateDesktop(name,null,IntPtr.Zero,0,0x1FF,IntPtr.Zero);
        if(desktop==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
        try{
            var info=new StartupInfo{Size=Marshal.SizeOf<StartupInfo>(),Desktop="winsta0\\"+name};
            var input=OpenInputDesktop(0,false,1);string inputName=DesktopName(input);CloseDesktop(input);
            if(name==inputName)throw new InvalidOperationException("Refusing the input desktop.");
            File.WriteAllText(Path.Combine(output,"launcher-desktop.txt"),$"worker={name}\ninput={inputName}\n");
            using var log=new FileStream(Path.Combine(output,"worker.log"),FileMode.Create,FileAccess.Write,FileShare.ReadWrite);
            var handle=log.SafeFileHandle.DangerousGetHandle();SetHandleInformation(handle,1,1);info.Flags=0x100;info.StdOutput=info.StdError=handle;
            string command=executable is null ? $"\"{Environment.ProcessPath}\" \"{output}\" --worker" : "\""+executable+"\" "+string.Join(" ",(arguments ?? Array.Empty<string>()).Select(a=>"\""+a.Replace("\"","\\\"")+"\""));
            if(!CreateProcess(null,new StringBuilder(command),IntPtr.Zero,IntPtr.Zero,true,0x08000000,IntPtr.Zero,executable is null ? AppContext.BaseDirectory : Path.GetDirectoryName(executable)!,ref info,out var process))throw new System.ComponentModel.Win32Exception();
            CloseHandle(process.Thread);
            try{WaitForSingleObject(process.Process,180000);GetExitCodeProcess(process.Process,out uint code);Console.WriteLine($"Isolated worker exit {code}; output: {output}");return (int)code;}
            finally{CloseHandle(process.Process);}
        }finally{CloseDesktop(desktop);}
    }
    static string DesktopName(IntPtr handle){var result=new StringBuilder(256);if(handle==IntPtr.Zero || !GetUserObjectInformation(handle,2,result,512,out _))throw new System.ComponentModel.Win32Exception();return result.ToString();}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartupInfo {public int Size;public string? Reserved,Desktop,Title;public uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;public short ShowWindow,ReservedSize;public IntPtr ReservedBytes,StdInput,StdOutput,StdError;}
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo{public IntPtr Process,Thread;public uint ProcessId,ThreadId;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateDesktop(string name,string? device,IntPtr mode,uint flags,uint access,IntPtr attributes);
    [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint id);
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder result,uint length,out uint needed);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcess(string? app,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
    [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint ms);
    [DllImport("kernel32.dll")]static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")]static extern IntPtr GetSystemMenu(IntPtr window,bool revert);
    [DllImport("user32.dll")]static extern uint GetMenuState(IntPtr menu,uint command,uint flags);
    [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
}
