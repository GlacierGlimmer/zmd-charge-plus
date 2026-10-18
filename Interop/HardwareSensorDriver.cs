using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace EndfieldChargePlus.Interop;

internal sealed record HardwareDriverState(bool Installed,string Version,bool Elevated,bool RestartRequired,bool Accessible=true,int NativeError=0)
{
    internal string Status => RestartRequired ? "RestartRequired" : !Installed ? "DriverMissing" : !Elevated ? "AdministratorRequired" : !Accessible ? "DriverInaccessible" : "Ready";
    internal string Description => Status switch {
        "RestartRequired"=>LocalizationManager.Text("驱动已安装，请完全退出并重新启动 ECP。","The driver is installed. Exit and restart ECP."),
        "DriverMissing"=>LocalizationManager.Text("未安装 PawnIO：CPU 温度、功耗、电压及部分主板传感器需要此驱动。","PawnIO is missing: CPU temperature, power, voltage and some motherboard sensors require it."),
        "AdministratorRequired"=>LocalizationManager.Text("PawnIO 已安装；请以管理员身份运行 ECP 以读取底层传感器。","PawnIO is installed; run ECP as administrator to read low-level sensors."),
        "DriverInaccessible"=>LocalizationManager.Formatted($"PawnIO 已安装但无法访问（错误 {NativeError}）；请检查驱动加载状态或修复驱动安装。",(System.FormattableString)$"PawnIO is installed but cannot be accessed (error {NativeError}); check driver loading or repair its installation."),
        _=>LocalizationManager.Text("PawnIO 已安装且当前为管理员身份；可读取硬件实际提供的传感器。","PawnIO is installed and ECP is elevated; sensors supported by the hardware can be read.") };
}

internal static class HardwareSensorDriver
{
    internal const string DownloadUrl="https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    internal const string InstallerSha256="1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    internal static HardwareDriverState Inspect()
    {
        string version="";
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        try {
            using var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);
            using var key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            if(System.Version.TryParse(key?.GetValue("DisplayVersion") as string,out var parsed)) { version=parsed.ToString();break; }
        } catch { }
        bool installed=System.Version.TryParse(version,out var current) && current>=new Version(2,0);
        bool elevated=ProcessPrivileges.IsElevated,loaded=false;
        try{loaded=LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;}catch{}
        bool accessible=true;int error=0;
        if(installed && elevated && loaded)
        {
            using var handle=CreateFile(@"\\?\GLOBALROOT\Device\PawnIO",0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
            accessible=!handle.IsInvalid;if(!accessible)error=Marshal.GetLastWin32Error();
        }
        return new(installed,version,elevated,installed && !loaded,accessible,error);
    }
    internal static bool VerifyInstaller(string path)
    {
        using var stream=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(InstallerSha256,StringComparison.Ordinal);
    }
    internal static async Task<int> InstallAsync(CancellationToken token)
    {
        string directory=Path.Combine(AppPaths.DataDirectory,"HardwareDriver");Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"PawnIO-2.2.0-setup.exe");
        if(!File.Exists(path) || !VerifyInstaller(path))
        {
            string partial=path+"."+Guid.NewGuid().ToString("N")+".download";
            try {
                using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(90)};
                using var response=await client.GetAsync(DownloadUrl,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);response.EnsureSuccessStatusCode();
                await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    await response.Content.CopyToAsync(output,token).ConfigureAwait(false);
                if(!VerifyInstaller(partial))throw new InvalidDataException(LocalizationManager.Text("驱动安装文件校验失败。","The driver installer checksum is invalid."));
                File.Move(partial,path,true);
            } finally { if(File.Exists(partial))File.Delete(partial); }
        }
        // The explicit Install button starts the official interactive installer.
        // Its UAC/license UI remains visible to the user; never install at startup.
        using var process=Process.Start(new ProcessStartInfo(path){UseShellExecute=true,Verb="runas"})
            ?? throw new IOException("Could not start the PawnIO installer.");
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        return process.ExitCode;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
}
