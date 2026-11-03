using System.Runtime.InteropServices;
using System.Security.Principal;
namespace EndfieldChargePlus.Interop;
public static class ProcessPrivileges
{
    public static bool IsElevated
    {
        get
        {
            try {
                if (OperatingSystem.IsWindows())
                    return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return geteuid()==0;
            } catch (Exception) { }
            return false;
        }
    }
    public static string SettingsTitle(string version,string productName="Endfield Charge Plus") =>
        "Endfield Charge Plus v"+version+" "+(LocalizationManager.Current==AppLanguage.English ? "HUD Settings" : LocalizationManager.Text("HUD设置","HUD SETTINGS"));
    [DllImport("libc")] private static extern uint geteuid();
}
