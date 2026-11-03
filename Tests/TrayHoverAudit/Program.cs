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
        using var done=new CancellationTokenSource(TimeSpan.FromMinutes(3));Exception? error=null;
        Dispatcher.UIThread.Post(async()=>{try{await Run(output);}catch(Exception ex){error=ex;}finally{done.Cancel();}});
        Dispatcher.UIThread.MainLoop(done.Token);
        if(error is not null){File.WriteAllText(Path.Combine(output,"FAIL.txt"),error.ToString());Console.Error.WriteLine(error);return 1;}
        File.WriteAllText(Path.Combine(output,"result.txt"),$"PASS {_checks} targeted stability assertions\n");return 0;
    }
    private static async Task Run(string output)
    {
        LocalizationManager.Initialize("zh-CN");
        var menu=new TrayMenuWindow();
        var foreground=GetForegroundWindow();
        var active=GetActiveWindow();
        menu.ShowAtTray();await Task.Delay(100);
        Check(!menu.ShowActivated && !menu.ShowInTaskbar,"menu is shown without activation or a taskbar button");
        Check(GetForegroundWindow()==foreground && GetActiveWindow()==active,"show preserves foreground and active window");
        var handle=menu.TryGetPlatformHandle()!.Handle;
        uint style=unchecked((uint)GetWindowLong(handle,-20));
        Check((style&0x08000080u)==0x08000080u && (style&0x00040000u)==0,"native popup is no-activate tool window and never APPWINDOW");
        Check(SendMessage(handle,0x21,active,new IntPtr((0x201<<16)|1))==new IntPtr(3),"WM_MOUSEACTIVATE retains click without activating Explorer replacement");
        var item=menu.FindControl<Border>("MenuSettings")!;
        var pointer=new Avalonia.Input.Pointer(301,PointerType.Mouse,true);
        WorkspaceTheme.Apply(menu,WorkspaceTheme.Current);WorkspaceTheme.ApplyLegacyColours(menu);
        item.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent,item,pointer,item,new Point(5,5),0,new PointerPointProperties(),KeyModifiers.None));
        var hover=((ISolidColorBrush)item.Background!).Color;
        for(int i=0;i<5;i++) { WorkspaceTheme.ApplyLegacyColours(menu);await Task.Delay(220); }
        var after=((ISolidColorBrush)item.Background!).Color;
        File.WriteAllText(Path.Combine(output,"hover.txt"),$"entered={hover}\nafter-five-appearance-ticks={after}\n");
        Check(after==hover,"hover colour survives global appearance refresh while mouse stays still");
        Check(hover==Color.Parse("#444300"),"visible Endfield yellow hover background");
        Check(((ISolidColorBrush)menu.FindControl<TextBlock>("MenuSettingsText")!.Foreground!).Color==Color.Parse("#FFFA00"),"hover text uses Endfield accent");
        Check(GetForegroundWindow()==foreground && GetActiveWindow()==active && menu.IsVisible,"hover and appearance ticks do not activate or dismiss the popup");
        var position=menu.Position;
        Save(menu,Path.Combine(output,"menu-hover-final.png"));
        item.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent,item,pointer,item,new Point(-1,-1),0,new PointerPointProperties(),KeyModifiers.None));
        Check(((ISolidColorBrush)item.Background!).Color.A==0,"pointer exit clears hover");
        Check(menu.Position==position,"hover never repositions the menu");
        Save(menu,Path.Combine(output,"menu-normal-final.png"));
        menu.Close();
        Check(!((DispatcherTimer)typeof(TrayMenuWindow).GetField("_dismissTimer",Private)!.GetValue(menu)!).IsEnabled,"closed popup stops its input sampler");
        // Exercise the real popup HWND against a private stand-in for Explorer's
        // overflow flyout. No Explorer window or global input is touched.
        var flyout=new Window{Width=300,Height=200,ShowInTaskbar=false,Title="Private overflow fixture"};
        int collapse=0;flyout.Deactivated+=(_,_)=>collapse++;
        flyout.Show();flyout.Activate();await Task.Delay(30);
        var ownerActive=GetActiveWindow();var ownerForeground=GetForegroundWindow();
        var popup=new TrayMenuWindow();popup.ShowAtTray();await Task.Delay(100);
        Check(collapse==0 && GetActiveWindow()==ownerActive && GetForegroundWindow()==ownerForeground,"nonactivating popup leaves overflow owner active");
        int clicked=0;popup.SettingsClicked+=()=>clicked++;
        var choice=popup.FindControl<Border>("MenuSettings")!;
        choice.RaiseEvent(new PointerPressedEventArgs(choice,pointer,choice,new Point(5,5),0,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None,1));
        Check(clicked==0,"nonactivating menu waits for left release");
        choice.RaiseEvent(new PointerReleasedEventArgs(choice,pointer,choice,new Point(5,5),0,new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
        Check(clicked==1 && collapse==0,"click executes once without collapsing owner on activation");
        ((DispatcherTimer)typeof(TrayMenuWindow).GetField("_dismissTimer",Private)!.GetValue(popup)!).Stop();
        var bounds=new PixelRect(100,100,220,139);
        popup.EvaluateDismissInput(new PixelPoint(150,150),bounds,1,false,ownerForeground);
        Check(popup.IsVisible,"inside click keeps menu open");
        popup.EvaluateDismissInput(new PixelPoint(150,150),bounds,0,false,ownerForeground);
        popup.EvaluateDismissInput(new PixelPoint(10,10),bounds,1,false,ownerForeground);
        Check(!popup.IsVisible,"outside left press dismisses without stealing or consuming input");
        foreach(var reason in new[]{"right","escape","foreground"})
        {
            var dismiss=new TrayMenuWindow();dismiss.ShowAtTray();
            ((DispatcherTimer)typeof(TrayMenuWindow).GetField("_dismissTimer",Private)!.GetValue(dismiss)!).Stop();
            dismiss.EvaluateDismissInput(new PixelPoint(10,10),bounds,reason=="right"?2:0,reason=="escape",reason=="foreground"?new IntPtr(123):GetForegroundWindow());
            Check(!dismiss.IsVisible,"dismissal: "+reason);
        }
        flyout.Close();
        foreach(var language in LocalizationManager.SupportedLanguages)
        foreach(var scale in new[]{(.8,.8),(1d,1d),(1.25,1.5)})
        {
            LocalizationManager.Initialize(language.Code);GlobalAppearance.SetPreferences(scale.Item1,scale.Item2);
            var translated=new TrayMenuWindow();translated.Show();await Task.Delay(15);
            WorkspaceTheme.Apply(translated,WorkspaceTheme.Current);WorkspaceTheme.ApplyLegacyColours(translated);
            foreach(var name in new[]{"MenuPreview","MenuSettings","MenuExit"})
            {
                var entry=translated.FindControl<Border>(name)!;
                entry.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent,entry,pointer,entry,new Point(5,5),0,new PointerPointProperties(),KeyModifiers.None));
                WorkspaceTheme.ApplyLegacyColours(translated);
                Check(((ISolidColorBrush)entry.Background!).Color==Color.Parse("#444300"),"localized/scaled hover survives appearance refresh "+language.Code+name);
                Check(entry.Bounds.Width>0 && entry.Bounds.Height>=35,"full row hit region remains laid out "+language.Code+name);
                entry.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent,entry,pointer,entry,new Point(-1,-1),0,new PointerPointProperties(),KeyModifiers.None));
            }
            translated.Close();
        }
        GlobalAppearance.SetPreferences(1,1);LocalizationManager.Initialize("zh-CN");
    }
    private static void Save(Window window,string path)
    { using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(PixelSize.FromSize(window.ClientSize,window.RenderScaling));image.Render(window);image.Save(path); }
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
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern IntPtr GetActiveWindow();
}
