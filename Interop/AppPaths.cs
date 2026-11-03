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
                if(PackageFamilyName is not null)
                {
                    // Known-folder virtualization differs between packaged
                    // Win32 versions. WinRT gives this package's actual stable
                    // LocalState instead of composing it from a virtual path.
                    string state=Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                    if(!Path.IsPathFullyQualified(state))
                        throw new InvalidOperationException("The package's user data directory is unavailable.");
                    return Path.Combine(state,"EndfieldChargePlus");
                }
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!Path.IsPathFullyQualified(local))
                    throw new InvalidOperationException("The current user's local application-data directory is unavailable. Configuration cannot be stored beside the executable.");
                return Path.Combine(local, "EndfieldChargePlus");
            }
            // GetFolderPath can return an empty string when the XDG directory doesn't exist yet.
            // Compute an absolute path explicitly so a first launch never writes beside the app.
            string? configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            string data = !string.IsNullOrEmpty(configured) && Path.IsPathRooted(configured)
                ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            if (!Path.IsPathFullyQualified(data))
                throw new InvalidOperationException("The current user's data directory is unavailable. Configuration cannot use a relative application path.");
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
