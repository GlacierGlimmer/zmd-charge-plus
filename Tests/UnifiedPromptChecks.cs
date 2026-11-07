using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Diagnostics;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class UnifiedPromptChecks
{
    private const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
    internal static async Task Run(SettingsWindow owner,Action<bool,string> check,string output)
    {
        Directory.CreateDirectory(output);
        string preference=LocalizationManager.Preference;
        var application=Application.Current!;
        var oldLifetime=application.ApplicationLifetime;
        var lifetime=oldLifetime as IClassicDesktopStyleApplicationLifetime ?? new ClassicDesktopStyleApplicationLifetime {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var opened=new List<Window>();
        using var openedEvents=Window.WindowOpenedEvent.AddClassHandler<Window>((window,_)=>opened.Add(window));
        var errorType=typeof(ApplicationErrors);
        var queue=(System.Collections.Concurrent.ConcurrentQueue<Exception>)errorType.GetField("Pending",Hidden)!.GetValue(null)!;
        while(queue.TryDequeue(out _)){}
        ApplicationErrors.Initialize();ApplicationErrors.Attach(lifetime);
        Window? notice=null;
        try
        {
            void Style(Window dialog,string name)
            {
                var frame=dialog.GetLogicalDescendants().OfType<Border>().Single(b=>b.Name=="EcpPromptFrame");
                check((dialog.SystemDecorations==SystemDecorations.None || OperatingSystem.IsMacOS() && dialog.ExtendClientAreaToDecorationsHint) && !dialog.CanResize,name+" uses the unified frameless dialog");
                check(frame.CornerRadius.TopLeft==8 && (frame.Background as ISolidColorBrush)?.Color==Color.Parse("#202020"),name+" has the same rounded dark surface");
                check(dialog.GetLogicalDescendants().OfType<TextBlock>().Any(t=>(t.Foreground as ISolidColorBrush)?.Color==Color.Parse("#FFFA00")),name+" uses a yellow heading");
            }
            LocalizationManager.Initialize("zh-CN");
            var error=new IOException("Cannot save Authorization: Bearer PRIVATE123",new UnauthorizedAccessException("Test directory is read-only"));
            notice=SaveErrorDialog.Create(error);notice.Show(owner);await Task.Delay(130);Style(notice,"Error prompt");
            check(!notice.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("PRIVATE123")==true),"short reason and full details both redact credentials");
            using(var image=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(notice.ClientSize.Width),(int)Math.Ceiling(notice.ClientSize.Height))))
            {image.Render(notice);image.Save(Path.Combine(output,"error-prompt.png"));}
            notice.Close();notice=null;
            var result=new SettingsWindow.UpdateCheckResult("0.2.0","0.2.1",SettingsWindow.UpdateStatusKind.UpdateAvailable,true,true);
            var update=new UpdateReminderWindow(result);update.Show(owner);await Task.Delay(100);Style(update,"Update prompt");update.Close();

            // Exercise the actual warning window and its persistent per-variable choice.
            var preferencesField=typeof(ProfileDataWarnings).GetField("_preferences",Hidden)!;
            var oldPreferences=preferencesField.GetValue(null);
            string warningPath=Path.Combine(output,Guid.NewGuid().ToString("N")+"-ignored-test-data.json");
            var profile=new HudProfile {Id="uniform-test",Name="Uniform warning",PrimaryTemplate="{test.variable.one}",TaglineTemplate="",TitleTemplate="",SecondaryTemplate="",RightTemplate="",RightSuffix="",ProgressVariable="= 50"};
            try
            {
                preferencesField.SetValue(null,new DataWarningPreferences(warningPath));
                ProfileDataWarnings.Check(profile,new Dictionary<string,object?>(),"uniform-test");await Task.Delay(5100);
                ProfileDataWarnings.Check(profile,new Dictionary<string,object?>(),"uniform-test");
                // The warning merges on a dispatcher timer. Await the observable window,
                // not a single frame deadline on a loaded native CI runner.
                var warningWait=System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    notice=(Window?)typeof(ProfileDataWarnings).GetField("_dialog",Hidden)!.GetValue(null);
                    if(notice?.IsVisible==true)break;
                    await Task.Delay(50);
                }while(warningWait.Elapsed<TimeSpan.FromSeconds(5));
                check(notice?.IsVisible==true,"unavailable HUD data opens the real warning");Style(notice!,"HUD data prompt");
                using(var image=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(notice!.ClientSize.Width),(int)Math.Ceiling(notice.ClientSize.Height))))
                {image.Render(notice);image.Save(Path.Combine(output,"hud-data-prompt.png"));}
                notice.GetLogicalDescendants().OfType<CheckBox>().Single(c=>c.Name=="IgnoreCurrentDataWarningCheckBox").IsChecked=true;
                await Task.Delay(80);notice=null;
                var stored=new DataWarningPreferences(warningPath);
                check(stored.IsIgnored("test.variable.one") && !stored.IsIgnored("test.variable.two"),"unified warning still permanently silences only the displayed variable");
            }
            finally {notice?.Close();notice=null;ProfileDataWarnings.Forget("uniform-test");preferencesField.SetValue(null,oldPreferences);}

            var background=new IOException("Background test operation failed");
            AppLog.Error("Background test",background);ApplicationErrors.Report(background);await Task.Delay(180);
            notice=opened.Single(w=>w.IsVisible && w.Name=="SaveErrorDialog");Style(notice,"Background error prompt");notice.Close();notice=null;
            await Task.Delay(100);check(!opened.Any(w=>w.IsVisible && w.Name=="SaveErrorDialog"),"one exception reported by log and handler appears once");
            ApplicationErrors.Report(new OperationCanceledException());await Task.Delay(80);
            check(!opened.Any(w=>w.IsVisible && w.Name=="SaveErrorDialog"),"normal cancellation does not produce an error");
            Dispatcher.UIThread.Post(()=>throw new IOException("Dispatched operation failed"));await Task.Delay(180);
            notice=opened.Single(w=>w.IsVisible && w.Name=="SaveErrorDialog");
            check(notice.GetLogicalDescendants().OfType<TextBox>().Any(t=>t.Text?.Contains("Dispatched operation failed")==true),"recoverable UI dispatcher failure is visible and returns control");notice.Close();notice=null;

            string crashPath=Path.Combine(output,"pending-test-error.json");
            ApplicationErrors.WritePending(new Exception(new string('测',100000)+" Authorization: Bearer PRIVATE123"),crashPath);
            var report=ApplicationErrors.ReadPending(crashPath);
            check(report is not null && new FileInfo(crashPath).Length<=262144 && report.Details.Length<=30000,"large non-ASCII fatal reports remain readable within the recovery size bound");
            ApplicationErrors.WritePending(error,crashPath);report=ApplicationErrors.ReadPending(crashPath);
            check(report?.Details.Contains("UnauthorizedAccessException")==true && !report.Summary.Contains("PRIVATE123") && !report.Details.Contains("PRIVATE123"),"stored fatal report keeps the cause and redacts both fields");
            notice=SaveErrorDialog.Create(new Exception(report!.Summary),title:LocalizationManager.Text("上次运行异常退出。","The previous run ended unexpectedly."),rawDetails:report.Details);notice.Show(owner);await Task.Delay(100);Style(notice,"Recovered crash prompt");notice.Close();notice=null;
            foreach(var language in LocalizationManager.SupportedLanguages)
            {
                LocalizationManager.Initialize(language.Code);notice=SaveErrorDialog.Create(new Exception("Test"));notice.Show(owner);await Task.Delay(35);
                check(notice.Bounds.Width>=280 && notice.Bounds.Height<=notice.MaxHeight+1,"error dialog stays reachable in "+language.Code);
                notice.Close();notice=null;
            }
        }
        finally
        {
            notice?.Close();errorType.GetField("_ready",Hidden)!.SetValue(null,false);
            LocalizationManager.Initialize(preference);
        }
    }
}
