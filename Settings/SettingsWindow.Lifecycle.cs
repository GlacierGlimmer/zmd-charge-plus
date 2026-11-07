namespace EndfieldChargePlus.Settings;
public partial class SettingsWindow
{
    private bool _applicationExitPrepared;
    internal void PrepareForApplicationExit()
    {
        if(_applicationExitPrepared)return;
        _applicationExitPrepared=true;
        _statusActive=false;_statusLifetime.Cancel();
        _hardwareTimer.Stop();_hardwareLifetime.Cancel();
        _recordingShortcut=null;
    }
}
