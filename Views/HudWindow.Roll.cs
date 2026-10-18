using Avalonia.Media;
using EndfieldChargePlus.Animations;
using EndfieldChargePlus.Customization;

namespace EndfieldChargePlus.Views;

public partial class HudWindow
{
    private CancellationTokenSource? _rollRevision;
    internal bool IsRolling=>_rollRevision is not null && ReferenceEquals(_rollRevision,_cts);
    public async Task RollToPersistentAsync(HudRenderData data,Func<CancellationToken,Task<HudRenderData>>? refresh=null)
    {
        if (_closed) return;
        if (!IsVisible || !_persistent)
        {
            await ShowPersistentAsync(data with { SimpleAnimation=false },refresh);
            return;
        }
        _cts?.Cancel();_cts?.Dispose();
        var local=_cts=new CancellationTokenSource();
        _rollRevision=local;
        var token=local.Token;
        IsHudBusy=true;
        SetPersistentFinalState();
        try
        {
            await HudAnimations.RollOut().RunAsync(ContentHost,token);
            token.ThrowIfCancellationRequested();
            ApplyRenderData(data);
            ContentHost.RenderTransform=new TranslateTransform(0,HudAnimations.RollDistance);
            await HudAnimations.RollIn().RunAsync(ContentHost,token);
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if(ReferenceEquals(_rollRevision,local))_rollRevision=null;
            if (ReferenceEquals(_cts,local))
            {
                if (!_closed) SetPersistentFinalState();
                IsHudBusy=false;
            }
        }
    }
}
