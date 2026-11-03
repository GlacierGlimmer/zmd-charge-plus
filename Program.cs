using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using EndfieldChargePlus.Diagnostics;

namespace EndfieldChargePlus;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\EndfieldChargePlus.SingleInstance";
    internal const string ActivationEventName = @"Local\EndfieldChargePlus.Activate";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _activationEvent;

    internal static EventWaitHandle? ActivationEvent => _activationEvent;
    internal static bool IsAutoStart { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--package-probe")
        {
            System.IO.File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(new {executable=Environment.ProcessPath,assembly=typeof(Program).Assembly.Location,programBase=AppContext.BaseDirectory,architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),startupExecutable=Settings.StartupManager.StartupExecutablePath,dataDirectory=Interop.AppPaths.DataDirectory,settingsPath=Settings.SettingsManager.SettingsPath,backupsDirectory=Settings.SettingsManager.BackupsDirectory,logsDirectory=AppLog.LogsDirectory,elevated=new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)}));
            return;
        }
        int restartIndex=Array.IndexOf(args,"--restart-from");
        if(restartIndex>=0)
        {
            if(restartIndex+1>=args.Length || !int.TryParse(args[restartIndex+1],out int previousPid) || previousPid<=0 || previousPid==Environment.ProcessId)return;
            try {using var previous=System.Diagnostics.Process.GetProcessById(previousPid);if(!previous.WaitForExit(15000))return;}
            catch(ArgumentException) { }
            catch {return;}
        }
        IsAutoStart = args.Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase));

        // Windows supplies an activation kind for the Store startup task instead
        // of the portable command-line flag. Read it once before Avalonia starts.
        if (!IsAutoStart && Settings.StartupManager.IsPackaged)
        {
            try
            {
                var activation = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
                IsAutoStart = activation?.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
            }
            catch (Exception ex)
            {
                AppLog.Error("Unable to inspect packaged activation kind.", ex);
            }
        }

        bool isPrimaryInstance;
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out isPrimaryInstance);
        }
        catch (UnauthorizedAccessException)
        {
            isPrimaryInstance = false;
        }
        catch
        {
            // If the OS refuses named-kernel-object creation for an unexpected reason,
            // keep the application usable rather than failing before Avalonia starts.
            isPrimaryInstance = true;
        }

        if (!isPrimaryInstance)
        {
            // Never start a second background/HUD process. A normal launch asks the
            // existing instance to surface Settings; an autostart launch exits silently.
            if (!IsAutoStart)
            {
                try
                {
                    using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
                    activation.Set();
                }
                catch
                {
                    // The first instance may still be inside very early startup. The key
                    // requirement is still satisfied: this second process exits.
                }
            }
            return;
        }

        try
        {
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        }
        catch
        {
            _activationEvent = null;
        }

        AppLog.Initialize();
        AppLog.Info($"Endfield Charge Plus process started. Version={typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown"}");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Fatal("Unhandled AppDomain exception.", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Fatal("Application terminated by an unhandled startup/runtime exception.", ex);
            throw;
        }
        finally
        {
            AppLog.Info("Endfield Charge Plus process ended.");

            try { _activationEvent?.Dispose(); } catch { }
            _activationEvent = null;

            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
            try { _singleInstanceMutex?.Dispose(); } catch { }
            _singleInstanceMutex = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(LocalizationManager.FontOptions)
            .LogToTrace();
}
