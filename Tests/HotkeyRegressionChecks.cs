using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class HotkeyRegressionChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static void Run(Action<bool, string> check)
    {
        using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Exception? failure = null; bool completed = false;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await CheckAsync(check); completed = true; }
            catch (Exception ex) { failure = ex; }
            finally { finish.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(finish.Token);
        if (failure is not null) throw failure;
        if (!completed) throw new TimeoutException("HUD shortcut checks timed out.");
    }
    private static async Task CheckAsync(Action<bool, string> check)
    {
        check(!HudHotkeyGesture.TryParse("H", out _) && !HudHotkeyGesture.TryParse("Shift+H", out _), "Typing and Shift-only shortcuts cannot be registered.");
        check(!HudHotkeyGesture.TryParse("Ctrl+F12", out _) && !HudHotkeyGesture.TryParse("Ctrl+Ctrl+H", out _) && !HudHotkeyGesture.TryParse("Win+H", out _), "Reserved and malformed shortcuts are rejected.");
        check(HudHotkeyGesture.TryParse(" alt + SHIFT + ctrl + f23 ", out var gesture) && gesture.Text == "Ctrl+Alt+Shift+F23", "Shortcut syntax normalizes consistently.");
        check(HudHotkeyGesture.TryCapture(Key.D3, KeyModifiers.Control | KeyModifiers.Shift, out gesture) && gesture.Text == "Ctrl+Shift+3", "Recording preserves digits and modifiers.");
        check(JsonSerializer.Deserialize<AppSettings>("{}")!.HudToggleHotkey == "" && JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { HudToggleHotkey="Ctrl+Alt+H" }))!.HudToggleHotkey=="Ctrl+Alt+H", "Existing settings stay unbound; configured shortcuts round trip.");
        int presses = 0;
        var first = new WindowsGlobalHotkey(() => presses++);
        using var second = new WindowsGlobalHotkey(() => { });
        try
        {
            bool registered=first.TryUpdate("Ctrl+Alt+Shift+F23");
            check(registered, $"Windows registers the configured global shortcut (error {first.LastError}).");
            check(!second.TryUpdate(first.Gesture) && second.LastError == 1409, "Windows detects another owner's shortcut.");
            check(second.TryUpdate("Ctrl+Alt+Shift+F24") && !first.TryUpdate(second.Gesture) && first.Gesture == "Ctrl+Alt+Shift+F23", "A conflict preserves the original OS binding.");
            PostHotkey(first); await Task.Delay(100);
            check(presses == 1, "Native hotkey messages dispatch once on the UI thread.");
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            PostHotkey(first); await Task.Delay(100);
            check(presses == 2, "Native callbacks survive garbage collection.");
            PostHotkey(first); check(first.TryUpdate(""), "Clearing unregisters the shortcut."); await Task.Delay(100);
            check(presses == 2 && second.TryUpdate("Ctrl+Alt+Shift+F23") && first.TryUpdate("Ctrl+Alt+Shift+F24"), "Queued stale messages are discarded and released bindings can be reused.");
            await RuntimeAsync(check, first, second);
        }
        finally { first.Dispose(); }
        check(second.TryUpdate("Ctrl+Alt+Shift+F24"), "Disposal releases the registration.");
    }
    private static async Task RuntimeAsync(Action<bool, string> check, WindowsGlobalHotkey first, WindowsGlobalHotkey second)
    {
        var profiles = Enumerable.Range(0, 2).Select(i => new HudProfile { Id=$"hotkey-{i}", Name=$"Test {i}", PrimaryTemplate=$"PROFILE{i}", SecondaryTemplate="", RightTemplate="50", TitleTemplate="Test", TaglineTemplate="", ProgressVariable="= 50", AnimationMode="Simple" }).ToList();
        var options = new AppSettings { AlwaysVisible=true, HudToggleHotkey=first.Gesture, PersistentLayer=PersistentHudLayer.Topmost, CustomHud=new() { Profiles=profiles, ActiveProfileId=profiles[0].Id, CycleProfileIds=profiles.Select(p=>p.Id).ToList(), AutoCycle=false, CycleAnimationMode="Simple" } };
        var childOptions = options with { CustomHud=options.CustomHud with { ActiveProfileId=profiles[1].Id } };
        options=options with { MultiHudEnabled=true, HudInstances=new() { new() { Id="second", Settings=childOptions }, new() { Id="disabled", Settings=childOptions with { HudEnabled=false } } } };
        var hud=new HudWindow(); using var runtime=new CustomHudRuntime(hud); SettingsWindow? window=null;
        try
        {
            hud.ApplySettings(options); runtime.ApplySettings(options); runtime.Start();
            var child=runtime.GetInstanceHud("second")!; var disabled=runtime.GetInstanceHud("disabled")!;
            await Task.Delay(1400);
            check(hud.IsVisible && child.IsVisible && !disabled.IsVisible, "Startup displays enabled instances only.");
            await runtime.ToggleUserVisibilityAsync();
            check(runtime.IsUserHidden && !hud.IsVisible && !child.IsVisible, "A toggle hides all HUDs together.");
            await runtime.PreviewActiveAsync(); await runtime.HighlightInstanceAsync("second","Second"); await Task.Delay(1200);
            check(!hud.IsVisible && !child.IsVisible, "Timers, previews and editor highlights cannot resurrect hidden HUDs."); runtime.EndInstanceEditing();
            var added=options with { HudInstances=options.HudInstances.Append(new HudInstanceSettings { Id="third", Settings=childOptions }).ToList() };
            await runtime.ApplySettingsWithTransitionAsync(added); await Task.Delay(300);
            check(!runtime.GetInstanceHud("third")!.IsVisible && !hud.IsVisible && !child.IsVisible, "New instances inherit suppression after Save & Apply.");
            await runtime.ToggleUserVisibilityAsync();
            check(hud.IsVisible && child.IsVisible && runtime.GetInstanceHud("third")!.IsVisible && !disabled.IsVisible, "Restore respects each instance's enabled state.");
            check(hud.FindControl<TextBlock>("PrimaryText")!.Text=="PROFILE0" && child.FindControl<TextBlock>("PrimaryText")!.Text=="PROFILE1", "Restore preserves each instance's selected profile.");
            var applying=runtime.ApplySettingsWithTransitionAsync(added with { GlobalScale=.9 }); await Task.Delay(30); await runtime.ToggleUserVisibilityAsync(); await applying;
            var applied=(AppSettings)typeof(CustomHudRuntime).GetField("_appSettings",Private)!.GetValue(runtime)!;
            check(runtime.IsUserHidden && !hud.IsVisible && applied.GlobalScale==.9, "Hiding during Save & Apply commits settings without showing stale presentations.");
            var rapid=new List<Task>(); for(int i=0;i<6;i++) rapid.Add(runtime.ToggleUserVisibilityAsync()); await Task.WhenAll(rapid); await Task.Delay(400);
            check(runtime.IsUserHidden && !hud.IsVisible && !child.IsVisible, "Rapid toggles settle in the last requested state.");
            await runtime.ToggleUserVisibilityAsync(); check(hud.IsVisible && child.IsVisible, "HUDs recover after interrupted animations.");
            await runtime.ApplySettingsWithTransitionAsync(options with { AlwaysVisible=false, MultiHudEnabled=false, DisplayDurationSeconds=3 });
            await runtime.ToggleUserVisibilityAsync(); await runtime.ToggleUserVisibilityAsync();
            check(!hud.IsVisible && !runtime.IsUserHidden, "Transient mode restores event-based behavior.");
            LocalizationManager.Initialize("zh-CN"); window=new SettingsWindow(options,hud,runtime); window.AttachHudHotkey(new DesktopGlobalHotkey(first));
            var box=window.FindControl<TextBox>("HudHotkeyBox")!;
            box.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent, Key=Key.H, KeyModifiers=KeyModifiers.Control|KeyModifiers.Alt });
            check(box.Text=="Ctrl+Alt+H" && first.Gesture==options.HudToggleHotkey, "Recording does not register unsaved changes.");
            typeof(SettingsWindow).GetMethod("ApplyEditorControls",Private)!.Invoke(window,new object[] { childOptions });
            var collected=(AppSettings)typeof(SettingsWindow).GetMethod("CollectSettingsFromUi",Private)!.Invoke(window,null)!;
            check(collected.HudToggleHotkey=="Ctrl+Alt+H", "Instance editing preserves the shared shortcut edit.");
            bool conflict=false;
            try { typeof(SettingsWindow).GetMethod("SaveSettingsWithHudHotkey",Private)!.Invoke(window,new object[] { options with { HudToggleHotkey=second.Gesture } }); }
            catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException) { conflict=true; }
            check(conflict && first.Gesture==options.HudToggleHotkey, "Conflicts block saving and retain the old shortcut.");
            LocalizationManager.SetLanguage(AppLanguage.English); typeof(SettingsWindow).GetMethod("ApplyLocalization",Private)!.Invoke(window,null);
            check(window.FindControl<Button>("ClearHudHotkeyBtn")!.Content?.ToString()=="Clear shortcut" && window.FindControl<TextBlock>("HudHotkeyStatusText")!.Text!.StartsWith("Could not register"), "Shortcut controls and errors localize together.");
        }
        finally { window?.Close(); runtime.Dispose(); hud.Close(); LocalizationManager.Initialize("zh-CN"); }
    }
    private static void PostHotkey(WindowsGlobalHotkey service)
    {
        var window=(IntPtr)typeof(WindowsGlobalHotkey).GetField("_window",Private)!.GetValue(service)!;
        var id=(int)typeof(WindowsGlobalHotkey).GetField("_registration",Private)!.GetValue(service)!;
        HudHotkeyGesture.TryParse(service.Gesture,out var gesture);
        if(!PostMessageW(window,0x0312,new IntPtr(id),new IntPtr((gesture.VirtualKey<<16)|gesture.Modifiers))) throw new InvalidOperationException("Hotkey notification failed.");
    }
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessageW(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
}
