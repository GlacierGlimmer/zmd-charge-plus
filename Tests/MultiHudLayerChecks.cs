using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class MultiHudLayerChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task Run(Action<bool,string> check)
    {
        var profile=new HudProfile { Id="layer-profile",Name="Layer",PrimaryTemplate="LAYER",SecondaryTemplate="",RightTemplate="50",TitleTemplate="Layer",TaglineTemplate="",ProgressVariable="=50",AnimationMode="Simple" };
        foreach(var (primaryLayer,secondaryLayer) in new[] {
            (PersistentHudLayer.Desktop,PersistentHudLayer.Topmost),
            (PersistentHudLayer.Topmost,PersistentHudLayer.Desktop),
            (PersistentHudLayer.Topmost,PersistentHudLayer.Topmost),
            (PersistentHudLayer.Desktop,PersistentHudLayer.Desktop) })
        {
            var local=new AppSettings { AlwaysVisible=true,ClickToCycle=true,SamplingIntervalSeconds=.1,
                CustomHud=new() { Profiles=new() { profile },ActiveProfileId=profile.Id },
                PositionMode=HudPositionMode.CustomCoordinates,HudCustomX=100,HudCustomY=100 };
            var childSettings=local with { HudCustomY=300,PersistentLayer=secondaryLayer };
            var settings=local with { PersistentLayer=primaryLayer,MultiHudEnabled=true,
                HudInstances=new() { new() { Id="layer-secondary",Name="Layer HUD 2",Settings=childSettings } } };
            var hud=new HudWindow();hud.ApplySettings(settings);
            using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);runtime.Start();
            SettingsWindow? window=null;
            try
            {
                var second=runtime.GetInstanceHud("layer-secondary")!;
                for(int i=0;i<100 && (!hud.IsVisible || !second.IsVisible || hud.IsHudBusy || second.IsHudBusy);i++) await Task.Delay(50);
                CheckLayers("initial",hud,second,primaryLayer,secondaryLayer,check);
                for(int selected=0;selected<2;selected++)
                {
                    window=new SettingsWindow(settings,hud,runtime);window.Show();await Task.Delay(350);
                    var choice=(ComboBox)typeof(SettingsWindow).GetField("_instanceChoice",Private)!.GetValue(window)!;
                    choice.SelectedIndex=selected;await Task.Delay(350);
                    check((selected==0 ? hud : second).IsEditingHighlighted && !(selected==0 ? second : hud).IsEditingHighlighted,
                        "Only the selected instance is highlighted in a mixed-layer configuration.");
                    CheckLayers("editing",hud,second,selected==0 ? PersistentHudLayer.Topmost : primaryLayer,
                        selected==1 ? PersistentHudLayer.Topmost : secondaryLayer,check);
                    var draft=(AppSettings)typeof(SettingsWindow).GetMethod("CollectSettingsFromUi",Private)!.Invoke(window,null)!;
                    check(draft.PersistentLayer==primaryLayer && draft.HudInstances.Single().Settings.PersistentLayer==secondaryLayer,
                        "Collecting the selected editor does not copy its layer into another instance.");
                    await runtime.ApplySettingsWithTransitionAsync(draft);await Task.Delay(200);
                    check((selected==0 ? hud : second).IsEditingHighlighted,"Save/apply retains the selected editing override without changing saved layer choices.");
                    CheckLayers("save",hud,second,selected==0 ? PersistentHudLayer.Topmost : primaryLayer,
                        selected==1 ? PersistentHudLayer.Topmost : secondaryLayer,check);
                    check(!await runtime.HighlightInstanceAsync("unapplied","Not created") && !hud.IsEditingHighlighted && !second.IsEditingHighlighted,
                        "Selecting an unapplied instance removes the old highlight rather than marking the wrong HUD.");
                    await runtime.ApplySettingsWithTransitionAsync(draft);
                    check(!hud.IsEditingHighlighted && !second.IsEditingHighlighted,"Applying settings with a missing editing target does not silently highlight the primary.");
                    await runtime.HighlightInstanceAsync(selected==0 ? "" : "layer-secondary","Layer target");
                    var data=HudProfileRenderer.Render(profile,new Dictionary<string,object?>());
                    var preview=runtime.PreviewInstanceAsync(selected==0 ? "" : "layer-secondary",data,_=>Task.FromResult(data));
                    await Task.Delay(30);
                    window.Close();window=null;await preview;await Task.Delay(500);
                    CheckLayers("close",hud,second,primaryLayer,secondaryLayer,check);
                    check(!hud.IsEditingHighlighted && !second.IsEditingHighlighted,"Closing during a preview clears both editing overrides and leaves no stale highlight.");
                }
            }
            finally { window?.Close();runtime.Dispose();hud.Close(); }
        }
    }
    private static void CheckLayers(string stage,HudWindow primary,HudWindow secondary,PersistentHudLayer first,PersistentHudLayer second,Action<bool,string> check)
    {
        foreach(var (hud,layer) in new[] { (primary,first),(secondary,second) })
        {
            bool desired=layer==PersistentHudLayer.Topmost;
            var handle=hud.TryGetPlatformHandle()!.Handle;
            bool native=!OperatingSystem.IsWindows() || ((GetWindowLong(handle,-20)&8)!=0)==desired;
            if(OperatingSystem.IsWindows())
            {
                var input=typeof(HudWindow).GetField("_interaction",Private)!.GetValue(hud)!;
                var inputHandle=(IntPtr)input.GetType().GetProperty("InputHandle",Private|BindingFlags.Public)!.GetValue(input)!;
                native=native && GetWindow(handle,4)==IntPtr.Zero && inputHandle!=IntPtr.Zero && GetWindow(inputHandle,4)==handle
                    && ((GetWindowLong(inputHandle,-20)&8)!=0)==desired;
            }
            if(OperatingSystem.IsWindows()) Console.WriteLine($"Layer {stage}: expected={layer}, managed={hud.Topmost}, style=0x{GetWindowLong(handle,-20):X8}, owner={GetWindow(handle,4):X}, hwnd={handle:X}");
            check(hud.IsVisible && hud.Topmost==desired && native,$"{stage}: each actual HUD HWND retains its own {layer} mode.");
        }
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h,uint command);
}
