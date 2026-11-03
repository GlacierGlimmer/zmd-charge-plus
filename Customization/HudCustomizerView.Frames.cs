using Avalonia.Controls;
namespace EndfieldChargePlus.Customization;
public partial class HudCustomizerView
{
    private string _frameDisplaySelected="", _frameWindowSelected="";
    private bool _frameNeedsDisplay, _frameNeedsWindow;
    private void LoadFrameOptions(HudProfile profile)
    {
        var keys=HudProfileRenderer.GetRequiredVariables(profile);
        _frameNeedsDisplay=keys.Any(k=>k.StartsWith("frame.display."));
        _frameNeedsWindow=keys.Any(k=>k.StartsWith("frame.window."));
        FrameOptionsPanel.IsVisible=_frameNeedsDisplay || _frameNeedsWindow;
        if(!FrameOptionsPanel.IsVisible) return;
        FrameDisplayOptions.IsVisible=_frameNeedsDisplay; FrameWindowOptions.IsVisible=_frameNeedsWindow;
        _frameDisplaySelected=profile.FrameDisplayId; _frameWindowSelected=profile.FrameWindowId;
        FrameFullScaleBox.Value=(decimal)profile.FrameFullScaleFps;
        FrameDisplayTargetCombo.SelectedItem=null; FrameWindowTargetCombo.SelectedItem=null;
        RefreshFrameTargets();
        // The English argument is a stable message ID; its bundled en-US value describes per-window attribution.
        FrameCaptureHint.Text=LocalizationManager.Text("读取实际显示帧事件。窗口按所选窗口的主呈现流统计，同一进程中的其他窗口分别统计；显示器按其主呈现流统计。需要性能日志权限。不是固定屏幕刷新率。","Reads displayed frame events: the selected window uses its process's dominant swap chain; displays use their dominant presentation stream. Performance logging access is required. This is not the fixed display refresh rate.");
    }
    private void RefreshFrameTargets()
    {
        if(_frameNeedsDisplay) _frameDisplaySelected=FillFrameTargets(FrameDisplayTargetCombo,"display",_frameDisplaySelected);
        if(_frameNeedsWindow) _frameWindowSelected=FillFrameTargets(FrameWindowTargetCombo,"window",_frameWindowSelected);
    }
    private static string FillFrameTargets(ComboBox combo,string kind,string previous)
    {
        string selected=(combo.SelectedItem as FrameTarget)?.Id ?? previous;
        var targets=kind=="window" ? FrameRateBackend.Windows() : FrameRateBackend.Displays();
        var items=new[] { new FrameTarget("",kind=="display" ? LocalizationManager.Text("主显示器（自动）","Primary display (auto)") : LocalizationManager.Text("请选择窗口","Select a window")) }
            .Concat(targets.GroupBy(t=>t.Id).Select(g=>g.First())).ToArray();
        if (!items.Any(t=>t.Id==selected)) selected="";
        combo.ItemsSource=items;
        combo.SelectedItem=items.First(t=>t.Id==selected);
        return selected;
    }
}
