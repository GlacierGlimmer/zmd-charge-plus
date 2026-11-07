using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Diagnostics;

internal sealed record StoredApplicationError(string Id,string Summary,string Details,string Timestamp);

internal static class ApplicationErrors
{
    private static readonly ConditionalWeakTable<Exception,object> Seen=new();
    private static readonly ConditionalWeakTable<Exception,object> Shown=new();
    private static readonly ConcurrentQueue<Exception> Pending=new();
    private static readonly HashSet<string> BackgroundFingerprints=new();
    private static volatile bool _ready;
    private static bool _showing;
    private static int _installed,_fatal;
    internal static string ReportPath=>Path.Combine(Interop.AppPaths.DataDirectory,"pending-application-error.json");

    internal static void Initialize()
    {
        if(Interlocked.Exchange(ref _installed,1)!=0)return;
        AppLog.ErrorRecorded+=(_,error,fatal)=> {if(fatal)Fatal(error);else Report(error);};
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>
        {if(e.ExceptionObject is Exception error)Fatal(error);};
    }
    internal static void Attach(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _ready=true;
        Dispatcher.UIThread.UnhandledException+=(_,e)=>
        {
            if(e.Exception is OperationCanceledException){e.Handled=true;return;}
            if(e.Exception is IOException or UnauthorizedAccessException or FormatException or TimeoutException)
            {AppLog.Error("Unhandled UI operation failed.",e.Exception);e.Handled=true;}
            // Unexpected programming/native failures unwind to Program.Main.
            // Do not pretend an unknown application state is safe to continue.
        };
        desktop.Exit+=(_,_)=>_ready=false;
        Dispatcher.UIThread.Post(Drain,DispatcherPriority.Background);
    }
    internal static void MarkShown(Exception error)
    {lock(Shown){if(!Shown.TryGetValue(error,out _))Shown.Add(error,new());}}
    internal static void Report(Exception error)
    {
        if(error is OperationCanceledException)return;
        lock(Shown){if(Shown.TryGetValue(error,out _))return;}
        lock(Seen){if(Seen.TryGetValue(error,out _))return;Seen.Add(error,new());}
        string key=error.GetType().FullName+"\n"+error.Message+"\n"+error.StackTrace?.Split('\n').FirstOrDefault();
        lock(BackgroundFingerprints)
        {
            if(BackgroundFingerprints.Contains(key))return;
            if(BackgroundFingerprints.Count>=128)BackgroundFingerprints.Clear();
            BackgroundFingerprints.Add(key);
        }
        if(Pending.Count<8)Pending.Enqueue(error);
        if(_ready)Dispatcher.UIThread.Post(Drain,DispatcherPriority.Background);
    }
    private static async void Drain()
    {
        if(!_ready||_showing||!Pending.TryDequeue(out var error))return;
        lock(Shown){if(Shown.TryGetValue(error,out _)){Dispatcher.UIThread.Post(Drain,DispatcherPriority.Background);return;}}
        _showing=true;
        try {await SaveErrorDialog.Show(PromptStyle.Owner(),error,title:LocalizationManager.Text("错误","Error"));}
        catch(Exception displayError){WritePending(new AggregateException(error,displayError));}
        finally{_showing=false;if(_ready&&!Pending.IsEmpty)Dispatcher.UIThread.Post(Drain,DispatcherPriority.Background);}
    }
    internal static void WritePending(Exception error,string? path=null)
    {
        try
        {
            path??=ReportPath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string details=SaveErrorDialog.Details(error);
            string summary=SaveErrorDialog.Redact(error.Message);
            // Default JSON escaping can use six bytes for each non-ASCII character.
            // Keep even an all-CJK report below the bounded recovery-file read limit.
            var data=new StoredApplicationError(Guid.NewGuid().ToString("N"),summary[..Math.Min(summary.Length,4000)],details[..Math.Min(details.Length,30000)],DateTimeOffset.Now.ToString("O"));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(data));File.Move(temp,path,true);
        }
        catch { /* A full/unwritable disk must not recursively fault the crash handler. */ }
    }
    internal static StoredApplicationError? ReadPending(string? path=null)
    {
        try {path??=ReportPath;if(!File.Exists(path)||new FileInfo(path).Length>262144)return null;return JsonSerializer.Deserialize<StoredApplicationError>(File.ReadAllText(path));}
        catch{return null;}
    }
    private static void Fatal(Exception error)
    {
        if(Interlocked.Exchange(ref _fatal,1)!=0)return;
        WritePending(error);
        try
        {
            if(ReadPending() is null||Environment.ProcessPath is not {} executable)return;
            var start=new System.Diagnostics.ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};
            if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--ecp-error-report");System.Diagnostics.Process.Start(start);
        }
        catch { /* The report remains available for the next manual launch. */ }
    }
    internal static bool TryShowRecovery(string[] args)
    {
        bool requested=args.Contains("--ecp-error-report");var report=ReadPending();
        if(report is null)return requested;
        RecoveryApplication.Report=report;
        try
        {
            LocalizationManager.Initialize("Auto");
            AppBuilder.Configure<RecoveryApplication>().UsePlatformDetect().WithInterFont().With(LocalizationManager.FontOptions).StartWithClassicDesktopLifetime(Array.Empty<string>());
        }
        catch {Environment.ExitCode=1;}
        return true;
    }
    private sealed class RecoveryApplication:Application
    {
        public RecoveryApplication(){}
        internal static StoredApplicationError Report=null!;
        public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;}
        public override void OnFrameworkInitializationCompleted()
        {
            var desktop=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
            var window=SaveErrorDialog.Create(new Exception(Report.Summary),title:LocalizationManager.Text("上次运行异常退出。","The previous run ended unexpectedly."),rawDetails:Report.Timestamp+"\n\n"+Report.Details);
            window.ShowInTaskbar=true;desktop.MainWindow=window;
            window.Opened+=(_,_)=>{try{if(ReadPending()?.Id==Report.Id)File.Delete(ReportPath);}catch{}};
            base.OnFrameworkInitializationCompleted();
        }
    }
}
