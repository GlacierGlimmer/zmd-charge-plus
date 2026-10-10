using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using EndfieldChargePlus.Diagnostics;
using Windows.ApplicationModel;

namespace EndfieldChargePlus.Settings;

public readonly record struct StartupApplyResult(
    bool IsPackaged, bool IsEnabled, bool Succeeded, string State);

public static class StartupManager
{
    // Must match the TaskId in the MSIX AppxManifest.xml.
    public const string StoreStartupTaskId = "EndfieldChargePlusStartup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Endfield Charge Plus";
    private const int ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, IntPtr fullName);

    public static bool IsPackaged
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                int length = 0;
                return GetCurrentPackageFullName(ref length, IntPtr.Zero) == ErrorInsufficientBuffer;
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not determine Windows package identity.", ex);
                return false;
            }
        }
    }

    // Called on app launch. Never reconcile MSIX startup from a stale JSON setting:
    // Windows Startup Apps / Task Manager are authoritative for packaged apps.
    public static void Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows() || IsPackaged) return;
        ApplyPortable(enabled);
    }

    public static async Task<StartupApplyResult> ReadStatusAsync()
    {
        if (!IsPackaged)
            return new StartupApplyResult(false, IsPortableEnabled(), true, "Portable");

        try
        {
            StartupTask task = await StartupTask.GetAsync(StoreStartupTaskId);
            return new StartupApplyResult(true,
                task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy,
                true, task.State.ToString());
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to read packaged startup-task status.", ex);
            return new StartupApplyResult(true, false, false, "ReadError");
        }
    }

    public static async Task<StartupApplyResult> ApplyAsync(bool enabled)
    {
        if (!IsPackaged)
        {
            bool ok = ApplyPortable(enabled);
            return new StartupApplyResult(false, IsPortableEnabled(), ok, ok ? "Portable" : "WriteError");
        }

        try
        {
            StartupTask task = await StartupTask.GetAsync(StoreStartupTaskId);
            StartupTaskState state = task.State;
            if (enabled && state == StartupTaskState.Disabled)
                state = await task.RequestEnableAsync();
            else if (!enabled && state == StartupTaskState.Enabled)
            {
                task.Disable();
                state = task.State;
            }

            var result = FromStoreState(state, enabled);
            AppLog.Info($"MSIX startup request: requested={enabled}, state={result.State}, enabled={result.IsEnabled}.");
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to apply packaged startup task. Check the MSIX manifest.", ex);
            // Never claim the startup toggle worked when Windows rejected the call.
            return new StartupApplyResult(true, false, false, "ApplyError");
        }
    }

    private static StartupApplyResult FromStoreState(StartupTaskState state, bool requested)
    {
        bool isEnabled = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        return new StartupApplyResult(true, isEnabled, isEnabled == requested, state.ToString());
    }

    private static bool IsPortableEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not read Portable startup registry value.", ex);
            return false;
        }
    }

    private static bool ApplyPortable(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            string? path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return false;
            key.SetValue(ValueName, $"\"{path}\" --autostart", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to update Portable startup registry value.", ex);
            return false;
        }
    }
}
