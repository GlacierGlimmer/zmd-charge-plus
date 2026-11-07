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

internal static partial class Audit
{
    private static int _checks;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);_checks++;}
    private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    [STAThread] static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--layer-fixture")return NativeLayerFixture.Run(args.Skip(1).ToArray());
        string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        if(args.Length==1)return Launch(output);
        if(args.Length>=3 && args[1]=="--run-isolated")return Launch(output,args[2],args.Skip(3).ToArray());
        var current=DesktopName(GetThreadDesktop(GetCurrentThreadId()));
        var input=OpenInputDesktop(0,false,1);var inputName=DesktopName(input);CloseDesktop(input);
        if(!current.StartsWith("ECP-Audit-") || current==inputName)return 3;
        File.WriteAllText(Path.Combine(output,"desktop.txt"),$"worker={current}\ninput={inputName}\nNo desktop switching, no injected global input.\n");
        EndfieldChargePlus.Program.BuildAvaloniaApp().SetupWithoutStarting();
        using var done=new CancellationTokenSource(TimeSpan.FromMinutes(4));Exception? error=null;bool completed=false;
        Dispatcher.UIThread.Post(async()=>{try{await Run(output);completed=true;}catch(Exception ex){error=ex;}finally{done.Cancel();}});
        Dispatcher.UIThread.MainLoop(done.Token);
        if(!completed && error is null)error=new TimeoutException("Input/profile checks did not finish.");
        if(error is not null){File.WriteAllText(Path.Combine(output,"FAIL.txt"),error.ToString());Console.Error.WriteLine(error);return 1;}
        File.WriteAllText(Path.Combine(output,"result.txt"),$"PASS {_checks} targeted stability assertions\n");return 0;
    }
    private static async Task Run(string output) { await LayerChecks(output); }
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
            try{WaitForSingleObject(process.Process,600000);GetExitCodeProcess(process.Process,out uint code);Console.WriteLine($"Isolated worker exit {code}; output: {output}");return (int)code;}
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
