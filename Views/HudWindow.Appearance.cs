using Avalonia;
using Avalonia.Controls;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Views;

public partial class HudWindow
{
    private void UpdateContentGeometry()
    {
        static double Measure(TextBlock block)
        {
            block.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            return block.DesiredSize.Width;
        }
        double left=Measure(PrimaryText)+Measure(SecondaryText)+2;
        double right=Measure(RightText)+Measure(RightSuffixText)+1;
        double title=Math.Max(Measure(TitleText),Measure(TagLineText))+60;
        _contentWidth=Math.Max(560,Math.Min(1800,Math.Max(left+right+155,title)));
        if(MinimumContentWidth>0)
            _contentWidth=Math.Max(_contentWidth,Math.Min(1800,MinimumContentWidth));
        Pill.Width=NumHost.Width=ContentClip.Width=_contentWidth;
        ContentClip.CornerRadius=new CornerRadius(30*_animOptions.FontHeightFactor);
        if(_editingFrame is not null)
        {
            _editingFrame.Width=_contentWidth+24;
            _editingFrame.Height=60*_animOptions.FontHeightFactor+24;
            _editingFrame.CornerRadius=new CornerRadius(30*_animOptions.FontHeightFactor+8);
            _editingHint!.FontSize=12*GlobalAppearance.NormalizeFontScale(_settings.GlobalFontScale);
        }
        double scale=_settings.GlobalScale*GlobalAppearance.NormalizeUiScale(_settings.GlobalUiScale);
        Width=Math.Max(1200,(_contentWidth+60)*scale);Root.Width=Width;
        if(IsVisible)PositionHud();
    }
}
