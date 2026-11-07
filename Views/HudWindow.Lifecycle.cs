namespace EndfieldChargePlus.Views;
public partial class HudWindow
{
    private bool _applicationExitPrepared;
    internal void PrepareForApplicationExit()
    {
        if(_applicationExitPrepared)return;
        _applicationExitPrepared=true;
        _progressTimer.Stop();_inputRegionTimer.Stop();
        DisposeRadialInput();CancelPresentation();
        _interaction?.Dispose();_interaction=null;
    }
}
