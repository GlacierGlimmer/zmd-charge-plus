using EndfieldChargePlus.Customization;

namespace EndfieldChargePlus.Views;
public partial class HudWindow
{
    internal void CompletePersistentPresentation()
    {
        if(_closed)return;
        _persistent=true;SetPersistentFinalState();IsHudBusy=false;
    }
    internal void ShowPersistentImmediate(HudRenderData data)
    {
        if(_closed)return;
        CancelPresentation();_persistent=true;
        ApplyRenderData(data);SetPersistentFinalState();
        if(!IsVisible)ShowPositioned();
        IsHudBusy=false;
    }
}
