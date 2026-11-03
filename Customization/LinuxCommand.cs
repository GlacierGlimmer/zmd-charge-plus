using System.Diagnostics;

namespace EndfieldChargePlus.Customization;

internal static class LinuxCommand
{
    internal static bool Exists(string command) => Resolve(command) is not null;
    private static string? Resolve(string command)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(Path.PathSeparator)
            .Where(Path.IsPathRooted);
        foreach (var path in paths)
        {
            var file = Path.Combine(path, command);
            if (!File.Exists(file)) continue;
            if (!OperatingSystem.IsLinux() || (File.GetUnixFileMode(file) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0) return file;
        }
        return null;
    }

    internal static string? Run(string command, params string[] args)
    {
        try
        {
            string? file = Resolve(command);
            if (file is null) return null;
            var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.Environment["LC_ALL"] = "C";
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(2500)) { process.Kill(entireProcessTree: true); return null; }
            if (process.ExitCode != 0) return null;
            return (string.IsNullOrWhiteSpace(stdout.Result) ? stderr.Result : stdout.Result).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }
}
