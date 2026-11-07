using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using EndfieldChargePlus.Diagnostics;
using System.Threading;

namespace EndfieldChargePlus;
public partial class App
{
    private int _exitPrepared;
    private void OnShutdownRequested(object? sender,ShutdownRequestedEventArgs e)
    {
        // Never cancel logout/shutdown, show dialogs, save an unvalidated draft,
        // or wait for native sampling from a UI/lifetime callback.
        PrepareForExit();
    }
    private void OnApplicationWindowClosing(object? sender,WindowClosingEventArgs e)
    {
        // Explicit Shutdown() bypasses ShutdownRequested in Avalonia 11.2.1.
        // Stop producers before Avalonia starts destroying native render windows.
        if(e.CloseReason==WindowCloseReason.ApplicationShutdown)PrepareForExit();
    }
    private void PrepareForExit()
    {
        if(Interlocked.Exchange(ref _exitPrepared,1)!=0)return;
        _exiting=true;
        AppLog.Info("Preparing application exit: stop input, sampling and HUDs before closing windows.");
        ExitStep("activation listener",()=>{_activationListenerCts?.Cancel();_activationListenerCts?.Dispose();_activationListenerCts=null;});
        ExitStep("settings sampling",()=>_settingsWindow?.PrepareForApplicationExit());
        ExitStep("shortcuts",()=>_quickHotkeys?.Dispose());_quickHotkeys=null;
        ExitStep("HUD input",()=>_hud?.PrepareForApplicationExit());
        ExitStep("HUD runtimes",()=>_runtime?.Dispose());_runtime=null;
        ExitStep("update reminder",()=>_updateReminder.Close());
        ExitStep("tray menu",CloseTrayMenu);
        ExitStep("tray icon",()=>_tray?.Dispose());_tray=null;
    }
    private static void ExitStep(string resource,Action action)
    {
        try{action();}
        catch(Exception ex){AppLog.Error("Exit cleanup failed: "+resource,ex);}
    }
}
