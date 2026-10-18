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
        LocalizationManager.Formatted($"{productName} v{version} 设置",(System.FormattableString)$"{productName} v{version} Settings")
        + (IsElevated ? "（Endministrator Mode）" : "");
    [DllImport("libc")] private static extern uint geteuid();
}
