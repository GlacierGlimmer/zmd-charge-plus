using System.Runtime.InteropServices;
using System.Text;

namespace EndfieldChargePlus.Interop;

internal static class AppPaths
{
    internal static string DataDirectory
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string? family = PackageFamilyName;
                return family is null ? Path.Combine(local, "EndfieldChargePlus")
                    : Path.Combine(local, "Packages", family, "LocalState", "EndfieldChargePlus");
            }
            // GetFolderPath can return an empty string when the XDG directory doesn't exist yet.
            // Compute an absolute path explicitly so a first launch never writes beside the app.
            string? configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            string data = !string.IsNullOrEmpty(configured) && Path.IsPathRooted(configured)
                ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(data, "EndfieldChargePlus");
        }
    }

    internal static string? PackageFamilyName
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return null;
            uint length = 0;
            if (GetCurrentPackageFamilyName(ref length, null) != 122 || length == 0) return null;
            var name = new StringBuilder((int)length);
            return GetCurrentPackageFamilyName(ref length, name) == 0 ? name.ToString() : null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? name);
}
