using System;
using Microsoft.Win32;

namespace EndfieldChargePlus.Settings;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Endfield Charge Plus";

    public static void Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            string? path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            key.SetValue(ValueName, $"\"{path}\" --autostart", RegistryValueKind.String);
        }
        catch
        {
            // Startup registration failure should never stop the HUD from running.
        }
    }
}
