using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
internal static class ChromeGestureChecks
{
    internal static void Run(SettingsWindow window,Action<bool,string> check)
    {
        var edge=typeof(SettingsWindow).GetMethod("ResizeEdgeAt",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var dimensions=window.Bounds.Size;
        foreach(var item in new[]{(new Point(3,dimensions.Height/2),WindowEdge.West),(new Point(dimensions.Width-3,dimensions.Height/2),WindowEdge.East),
            (new Point(dimensions.Width/2,3),WindowEdge.North),(new Point(dimensions.Width/2,dimensions.Height-3),WindowEdge.South),
            (new Point(3,3),WindowEdge.NorthWest),(new Point(dimensions.Width-3,3),WindowEdge.NorthEast),
            (new Point(3,dimensions.Height-3),WindowEdge.SouthWest),(new Point(dimensions.Width-3,dimensions.Height-3),WindowEdge.SouthEast)})
            check(Equals(edge.Invoke(window,new object[]{item.Item1}),item.Item2),"resizable custom chrome edge: "+item.Item2);
        window.NavigateToSettingsSection("ring");
        var editor=window.FindControl<HudCustomizerView>("Customizer")!;
        var options=editor.GetLogicalDescendants().OfType<StackPanel>().Single(x=>x.Name=="RadialGestureOptions");
        check(options.GetLogicalAncestors().Contains(editor.FindControl<TabItem>("RadialSwitcherPage")!),"all ring gesture settings belong to Radial switcher");
        var quick=window.FindControl<StackPanel>("QuickActionsPanel")!;
        check(quick.Children.Count==1 && !quick.GetLogicalDescendants().OfType<NumericUpDown>().Any(),"Quick actions contains one keyboard card, no ring parameters");
        var boxes=options.GetLogicalDescendants().OfType<NumericUpDown>().ToDictionary(x=>x.Name!);
        check(boxes.Count==3,"one hold-delay, drag-distance and ring-size editor");
        var field=typeof(SettingsWindow).GetField("_workspaceDraft",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var original=(WorkspacePreferences)field.GetValue(window)!;
        boxes["RadialHoldDelayBox"].Value=520;boxes["RadialDragDistanceBox"].Value=24;boxes["RadialDiameterBox"].Value=420;
        var draft=(WorkspacePreferences)field.GetValue(window)!;
        check(draft.HoldMilliseconds==520 && draft.DragDistance==24 && draft.RingDiameter==420,"ring editors update the model used by Save & Apply");
        var preview=editor.GetLogicalDescendants().OfType<ProfileRingControl>().First();
        check(preview.Preferences.RingDiameter==420,"ring-size preview updates with the model");
        boxes["RadialHoldDelayBox"].Value=original.HoldMilliseconds;boxes["RadialDragDistanceBox"].Value=original.DragDistance;boxes["RadialDiameterBox"].Value=original.RingDiameter;
    }
}
