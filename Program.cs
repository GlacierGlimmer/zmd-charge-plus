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
        IsAutoStart = args.Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase));

        bool isPrimaryInstance;
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out isPrimaryInstance);
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
            .LogToTrace();
}
