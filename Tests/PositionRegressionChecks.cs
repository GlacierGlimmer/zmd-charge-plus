using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class PositionRegressionChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(HudWindow hud,CustomHudRuntime runtime,Action<bool,string> check)
    {
        var saved=new AppSettings { AlwaysVisible=true,MultiHudEnabled=true,HudPosition=HudPosition.TopCenter,HudOffsetY=7,
            HudInstances=new() { new() { Id="secondary",Settings=new AppSettings { AlwaysVisible=true,HudPosition=HudPosition.TopCenter,HudOffsetY=80 } } } };
        var window=new SettingsWindow(saved,hud,runtime);
        try
        {
            var x=window.FindControl<NumericUpDown>("OffsetXBox")!;
            var y=window.FindControl<NumericUpDown>("OffsetYBox")!;
            var preset=window.FindControl<ComboBox>("PositionCombo")!;
            check(y.Value==7,"Loading the primary HUD preserves a saved manual Y adjustment.");
            var choice=(ComboBox)typeof(SettingsWindow).GetField("_instanceChoice",Private)!.GetValue(window)!;
            choice.SelectedIndex=1;await Task.Delay(100);
            check(y.Value==80,"Switching editors preserves existing instance offsets instead of altering saved settings.");
            x.Value=23;preset.SelectedIndex=0;
            check(x.Value==0 && y.Value==0,"Selecting Top Left (work area) clears inherited X/Y offsets, including the old automatic 80px offset.");
            x.Value=18;y.Value=80;preset.SelectedIndex=9;
            check(x.Value==0 && y.Value==0,"Selecting Screen Top Left also resets fine adjustments rather than moving down by 80px.");
            x.Value=-14;y.Value=29;
            typeof(SettingsWindow).GetMethod("RebuildLocalizedChoiceItems",Private)!.Invoke(window,null);
            check(preset.SelectedIndex==9 && x.Value==-14 && y.Value==29,"Rebuilding localized choices leaves explicit fine adjustments untouched.");
            var custom=saved.HudInstances[0].Settings with { HudPosition=HudPosition.BottomRight,HudOffsetX=40,HudOffsetY=-35 };
            typeof(SettingsWindow).GetMethod("ApplyEditorControls",Private)!.Invoke(window,new object[] { custom });
            check(preset.SelectedIndex==8 && x.Value==40 && y.Value==-35,"Loading/importing a different saved anchor retains its explicit offsets.");
            var add=window.GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="AddHudInstanceButton");
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var draft=(AppSettings)typeof(SettingsWindow).GetMethod("CollectSettingsFromUi",Private)!.Invoke(window,null)!;
            var added=draft.HudInstances.Last().Settings;
            check(draft.HudInstances.Count==2 && added.HudOffsetX==0 && added.HudOffsetY==0 && added.PositionMode==HudPositionMode.Preset
                && added.HudPosition!=HudPosition.BottomRight && added.HudPosition!=HudPosition.TopCenter,
                "The actual Add HUD button chooses an explicit unused preset with zero offsets, without hidden +80px increments.");
        }
        finally { window.Close(); }
    }
}
