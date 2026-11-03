using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using EndfieldChargePlus.Customization;

internal static class LiveFrameChecks
{
    internal static int Run(string[] args)
    {
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        int result=1;
        if(args[0]=="--frame-source")
        {
            using var native=new NativeFrameSource();
            using var slow=new NativeFrameSource(580,"ECP second same-process FPS source");
            var info=new { Id=$"{native.Window.ToInt64():x}:{Environment.ProcessId}", SlowId=$"{slow.Window.ToInt64():x}:{Environment.ProcessId}" };
            File.WriteAllText(args[1],JsonSerializer.Serialize(info));
            var render=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(16.67) };
            render.Tick+=(_,_) => native.Render(); render.Start();
            var slowRender=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) }; slowRender.Tick+=(_,_)=>slow.Render(); slowRender.Start();
            var close=new DispatcherTimer { Interval=TimeSpan.FromSeconds(20) };
            close.Tick+=(_,_) => { render.Stop();slowRender.Stop();close.Stop();finish.Cancel(); }; close.Start();
            Dispatcher.UIThread.MainLoop(finish.Token); return 0;
        }
        Dispatcher.UIThread.Post(async () =>
        {
            Process? source=null;
            try
            {
                var targetFile=args[1]+".target.json";
                var launch=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true };
                launch.ArgumentList.Add("--frame-source");launch.ArgumentList.Add(targetFile);
                source=Process.Start(launch);
                for(int i=0;i<50 && !File.Exists(targetFile);i++) await Task.Delay(100);
                string target=JsonDocument.Parse(File.ReadAllText(targetFile)).RootElement.GetProperty("Id").GetString()!;
                string slowTarget=JsonDocument.Parse(File.ReadAllText(targetFile)).RootElement.GetProperty("SlowId").GetString()!;
                var observations=new List<object>(); bool windowSuccess=false,displaySuccess=false,separateWindows=false;
                for(int i=0;i<30;i++)
                {
                    var window=FrameRateBackend.Read("window",target);
                    var display=FrameRateBackend.Read("display","");
                    var slowWindow=FrameRateBackend.Read("window",slowTarget);
                    if(i>10 && window.Fps>15 && slowWindow.Fps is >3 and <15 && window.Fps>slowWindow.Fps*1.4) separateWindows=true;
                    if(window.Fps>0) windowSuccess=true;
                    if(display.Fps>0) displaySuccess=true;
                    observations.Add(new { At=DateTime.UtcNow,Window=window,SlowWindow=slowWindow,Display=display });
                    await Task.Delay(400);
                }
                var admin=new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                File.WriteAllText(args[1],JsonSerializer.Serialize(new { Administrator=admin,WindowSuccess=windowSuccess,DisplaySuccess=displaySuccess,SeparateWindows=separateWindows,Observations=observations },new JsonSerializerOptions { WriteIndented=true }));
                result=admin && windowSuccess && displaySuccess && separateWindows ? 0 : 1;
            }
            catch(Exception ex) { File.WriteAllText(args[1],ex.ToString()); }
            finally { FrameRateBackend.Stop(); if(source is { HasExited:false }) source.Kill(); source?.Dispose(); finish.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(finish.Token);return result;
    }
}
