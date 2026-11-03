using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus.Customization;

/// <summary>PresentMon's displayed frames; one shared ETW reader, regardless of HUD count.</summary>
public static class FrameRateBackend
{
    private static readonly object Gate=new();
    private static Process? _process;
    private static long _lastDemand;
    private static long _nextStart;
    private static bool _starting;
    private static int _generation;
    private static string? _sessionName;
    private static readonly Dictionary<(int Pid,long Window,string Chain,int? Display),FrameEventWindow> Streams=new();
    private static string _status="等待采样 / Waiting for frames";
    private static string _readerError="";
    private static long _framesReceived;
    // Use OS architecture so emulated x64/x86 apps on Windows ARM launch
    // the native ARM collector rather than emulating the ETW decoder too.
    private static (string FileName,string Resource,string Sha256) CollectorFor(Architecture osArchitecture)
        => osArchitecture==Architecture.Arm64
            ? ("ECPFrames-arm64.exe","EndfieldChargePlus.PresentMon.Arm64","23D509EFEFDA8B4E3BD8DEC7FDACFF027BF19200EF5A1AFC44BA83503A53F176")
            : ("ECPFrames-x64.exe","EndfieldChargePlus.PresentMon","2A0F046B1F384BF3F2CB9E19C9A629A3B75EB9153553148C4C23830A8C690259");
    private delegate bool EnumProc(IntPtr handle,IntPtr data);
    public static IReadOnlyList<FrameTarget> Windows()
    {
        var result=new List<FrameTarget>();
        if (!OperatingSystem.IsWindows()) return result;
        EnumWindows((handle,_) => {
            if (!IsWindowVisible(handle)) return true;
            var title=new StringBuilder(1024); GetWindowText(handle,title,title.Capacity);
            GetWindowThreadProcessId(handle,out uint pid);
            int cloaked=0;
            _=DwmGetWindowAttribute(handle,14,out cloaked,sizeof(int));
            if (title.Length>0 && pid!=Environment.ProcessId && cloaked==0 && GetWindow(handle,4)==IntPtr.Zero) result.Add(new($"{handle.ToInt64():x}:{pid}",title.ToString()));
            return true;
        },IntPtr.Zero);
        return result.OrderBy(w => w.Name).ToArray();
    }
    public static IReadOnlyList<FrameTarget> Displays()
    {
        var result=new List<FrameTarget>();
        for (uint i=0; ;i++)
        {
            var device=new DisplayDevice { Size=Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null,i,ref device,0)) break;
            if ((device.Flags&1)==0) continue;
            var target=new FrameTarget(device.Name,$"{device.Name} · {device.Description}"); if((device.Flags&4)!=0) result.Insert(0,target); else result.Add(target);
        }
        return result;
    }
    public static FrameReading Read(string kind,string target)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitOperatingSystem)
            return new(null,target,"帧率采集需要 64 位 Windows / Requires 64-bit Windows");
        int? pid=null,display=null; long selectedWindow=0; string name=target;
        if (kind=="window")
        {
            var parts=target.Split(':');
            if (parts.Length!=2 || !long.TryParse(parts[0],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var windowId)
                || !int.TryParse(parts[1],out var processId))
                return new(null,target,"请选择正在运行的窗口 / Select a running window");
            var handle=new IntPtr(windowId);
            GetWindowThreadProcessId(handle,out uint currentPid);
            if (!IsWindowVisible(handle) || currentPid!=processId)
                return new(null,target,"目标窗口已关闭，请重新选择 / Target window closed; select another window");
            var title=new StringBuilder(1024); GetWindowText(handle,title,title.Capacity);
            name=title.ToString(); pid=processId; selectedWindow=windowId;
        }
        else
        {
            var selected=string.IsNullOrWhiteSpace(target) ? Displays().FirstOrDefault() : Displays().FirstOrDefault(d => d.Id==target);
            if (selected is null) return new(null,target,"显示器已断开 / Display disconnected");
            name=selected.Name;
            display=WindowsDisplaySources.SourceId(selected.Id);
            if (display is null) return new(null,name,"显示源映射不可用或存在多 GPU 歧义 / Display source mapping unavailable or ambiguous across GPUs");
        }
        EnsureStarted();
        lock (Gate)
        {
            long now=Environment.TickCount64;
            var candidates=Streams.Where(s => pid is null ? display is not null && s.Key.Display==display : s.Key.Window==selectedWindow)
                .Select(s => new { Fps=s.Value.Read(now), Count=s.Value.RecentCount(now), Last=s.Value.LastArrival })
                .Where(s=>s.Fps.HasValue).OrderByDescending(s=>s.Count).ThenByDescending(s=>s.Last).ToArray();
            // Choose the dominant observed presentation stream, not the highest arbitrary FPS.
            return candidates.Length>0 ? new(candidates[0].Fps,name,"OK") : new(null,name,_status=="OK" ? "所选目标尚无有效帧事件 / No valid frame events for this target" : _status);
        }
    }
    private static void EnsureStarted()
    {
        int generation;
        lock (Gate)
        {
            _lastDemand=Environment.TickCount64;
            if (_process is not null || _starting || _lastDemand<_nextStart) return;
            _starting=true; generation=_generation; _sessionName=$"ECP-FPS-{Environment.ProcessId}-{generation}";
            _nextStart=_lastDemand+30000;
        }
        // Extraction, hashing and process launch must never block the settings/HUD thread.
        _=Task.Run(() => StartReader(generation));
    }
    private static void StartReader(int generation)
    {
        Process? process=null;
        string session=$"ECP-FPS-{Environment.ProcessId}-{generation}";
        try
        {
            WindowsFrameTrace.CleanupOrphans();
            string folder=Path.Combine(AppPaths.DataDirectory,"collectors","ECPFrames-PresentMon-2.6.0"); Directory.CreateDirectory(folder);
            var collector=CollectorFor(RuntimeInformation.OSArchitecture);
            string path=Path.Combine(folder,collector.FileName);
            bool valid=false;
            if (File.Exists(path)) using(var existing=File.OpenRead(path)) valid=Convert.ToHexString(SHA256.HashData(existing))==collector.Sha256;
            if (!valid)
            {
                using var source=Assembly.GetExecutingAssembly().GetManifestResourceStream(collector.Resource) ?? throw new IOException("PresentMon resource missing");
                using(var output=File.Create(path)) source.CopyTo(output);
                using var check=File.OpenRead(path); if (Convert.ToHexString(SHA256.HashData(check))!=collector.Sha256) throw new IOException("PresentMon integrity check failed");
            }
            string licensePath=Path.Combine(folder,"LICENSE.txt");
            if(!File.Exists(licensePath)) { using var license=Assembly.GetExecutingAssembly().GetManifestResourceStream("EndfieldChargePlus.PresentMon.License"); if(license is not null) { using var output=File.Create(licensePath); license.CopyTo(output); } }
            var start=new ProcessStartInfo(path) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            foreach(var arg in new[] { "--session_name",session,"--owner",Environment.ProcessId.ToString(CultureInfo.InvariantCulture) }) start.ArgumentList.Add(arg);
            process=Process.Start(start) ?? throw new IOException("Could not start PresentMon");
            lock(Gate)
            {
                if (generation!=_generation) { WindowsFrameTrace.Stop(session); process.Kill(); process.Dispose(); return; }
                _process=process; _starting=false; _readerError=""; _framesReceived=0;
                _status="等待帧事件；无权限时请以管理员运行 / Waiting for frame events; administrator access may be needed";
            }
            _=ReadOutputAsync(process,generation);
            _=ReadErrorsAsync(process,generation);
            var owned=process;
            _=Task.Run(async () => {
                try { while (!owned.HasExited) { await Task.Delay(2000); lock(Gate) { if (!ReferenceEquals(_process,owned)) return; if (Environment.TickCount64-_lastDemand>6000) { Stop(); return; } } } }
                catch (InvalidOperationException) { }
            });
        }
        catch (Exception ex) { EndfieldChargePlus.Diagnostics.AppLog.Error("PresentMon could not start.",ex); lock(Gate) { if(generation==_generation) { _status=ex.Message; _process=null; _starting=false; } } process?.Dispose(); }
    }
    private static async Task ReadErrorsAsync(Process process,int generation)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                if (!line.Contains("error",StringComparison.OrdinalIgnoreCase)) continue;
                lock (Gate)
                {
                    if (generation!=_generation || !ReferenceEquals(_process,process)) return;
                    _readerError=line.Trim();
                    _status=line.Contains("access denied",StringComparison.OrdinalIgnoreCase)
                        ? "无法启动帧事件采集，请以管理员运行 / Access denied; run the app as administrator"
                        : "帧率采集失败："+_readerError;
                }
                EndfieldChargePlus.Diagnostics.AppLog.Warn("PresentMon: "+line.Trim());
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
    }
    private static async Task ReadOutputAsync(Process process,int generation)
    {
        var parser=new PresentMonCsvReader();
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (!parser.TryRead(line,out var frame) || frame.Pid==Environment.ProcessId) continue;
                lock (Gate)
                {
                    if (generation!=_generation || !ReferenceEquals(_process,process)) return;
                    var key=(frame.Pid,frame.Window,frame.Chain,frame.Display);
                    if (!Streams.TryGetValue(key,out var events)) Streams[key]=events=new();
                    events.Add(frame.Timestamp,Environment.TickCount64); _status="OK"; _framesReceived++;
                    if (Streams.Count>512)
                        foreach(var expired in Streams.Where(s=>s.Value.RecentCount(Environment.TickCount64)==0).Select(s=>s.Key).Take(128).ToArray()) Streams.Remove(expired);
                }
            }
            await process.WaitForExitAsync();
            // Drain error output before deciding the final failure message.
            await Task.Delay(50);
            lock(Gate)
            {
                if (generation==_generation && ReferenceEquals(_process,process))
                {
                    if (_readerError.Length==0)
                        _status=$"帧事件采集已退出（{process.ExitCode}） / Frame capture exited ({process.ExitCode})";
                    EndfieldChargePlus.Diagnostics.AppLog.Warn($"PresentMon ended: code={process.ExitCode}, frames={_framesReceived}, error={_readerError}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { }
        finally
        {
            lock(Gate) { if (ReferenceEquals(_process,process)) { _process=null; _starting=false; } }
            process.Dispose();
        }
    }
    public static void KeepAlive(string kind,string target) { lock(Gate) _lastDemand=Environment.TickCount64; }
    public static void Stop()
    {
        Process? process; string? session;
        lock(Gate)
        {
            process=_process; session=_sessionName; _sessionName=null; _process=null; _starting=false; _generation++; _nextStart=0;
            Streams.Clear(); _readerError="";
        }
        // Killing PresentMon alone leaves its ETW session consuming memory and losing events.
        // Stop the OS session first so the collector can drain and exit normally.
        if (session is not null) WindowsFrameTrace.Stop(session);
        if (process is not null) _=Task.Run(() =>
        {
            try { if (!process.WaitForExit(1500)) process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct DisplayDevice
    { public int Size; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Key; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string? device,uint index,ref DisplayDevice data,uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h,uint command);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle,StringBuilder value,int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint pid);
}
