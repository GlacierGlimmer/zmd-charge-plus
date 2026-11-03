using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Views;

internal class BugRegressionTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _checks++;
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            // SetupWithoutStarting leaves ApplicationLifetime unset, so App loads
            // its actual theme without starting the tray, collectors or user settings.
            var builder=AppBuilder.Configure<EndfieldChargePlus.App>().UsePlatformDetect().WithInterFont().With(EndfieldChargePlus.LocalizationManager.FontOptions);
            if(Environment.GetEnvironmentVariable("ECP_RENDER_FALLBACK")=="1")
                builder.With(new Win32PlatformOptions { CompositionMode=new[] { Win32CompositionMode.RedirectionSurface },RenderingMode=new[] { Win32RenderingMode.Software } });
            builder.SetupWithoutStarting();
            if(args.Length>0 && args[0]=="--core-async") {SynchronizationContext.SetSynchronizationContext(null);RunAsync().GetAwaiter().GetResult();Console.WriteLine($"PASS {_checks} asynchronous core checks");return 0;}
            if(args.Length>0 && args[0]=="--appearance") {AppearanceLanguageChecks.Run(Check);Console.WriteLine($"PASS {_checks} appearance/language checks");return 0;}
            if(args.Length>0 && args[0]=="--window-frame") {WindowFrameChecks.Run(Check);Console.WriteLine($"PASS {_checks} window frame/language checks");return 0;}
            if(args.Length>0 && args[0]=="--language-layout") {LanguageSwitchLayoutChecks.Run(Check);Console.WriteLine($"PASS {_checks} language switch layout checks");return 0;}
            if(args.Length>0 && args[0]=="--profile-scroll") {ProfileScrollChecks.Run(Check);Console.WriteLine($"PASS {_checks} profile scroll checks");return 0;}
            if(args.Length>0 && args[0]=="--localization") {LocalizationRegressionChecks.Run(Check);Console.WriteLine($"PASS {_checks} localization checks");return 0;}
            if(args.Length>0 && args[0]=="--localization-models") {LocalizationRegressionChecks.Run(Check,gui:false);Console.WriteLine($"PASS {_checks} localization model checks");return 0;}
            if(args.Length>0 && args[0]=="--navigation") {NavigationManualChecks.Run(Check);Console.WriteLine($"PASS {_checks} settings navigation/manual checks");return 0;}
            if(args.Length>0 && args[0]=="--hardware") {HardwareTelemetryChecks.Run(Check);Console.WriteLine($"PASS {_checks} hardware checks");return 0;}
            if(args.Length>0 && args[0]=="--hardware-live") {HardwareTelemetryChecks.Run(Check,liveOnly:true);Console.WriteLine($"PASS {_checks} live hardware checks");return 0;}
            if(args.Length>0 && args[0]=="--media-live") { NextVersionRegressionChecks.RunLiveMedia(Check);Console.WriteLine($"PASS {_checks} live media checks");return 0; }
            if(args.Length>0 && args[0]=="--next") { NextVersionRegressionChecks.Run(Check); Console.WriteLine($"PASS {_checks} next-version checks"); return 0; }
            if(args.Length>0 && args[0]=="--hotkey") { HotkeyRegressionChecks.Run(Check); Console.WriteLine($"PASS {_checks} HUD shortcut checks"); return 0; }
            if(args.Length>0 && args[0]=="--warnings") { DataWarningRegressionChecks.Run(Check); Console.WriteLine($"PASS {_checks} variable warning checks"); return 0; }
            if(args.Length>0 && args[0]=="--usb") { UsbNotificationChecks.Run(Check); Console.WriteLine($"PASS {_checks} USB notification checks"); return 0; }
            if(args.Length>0 && args[0]=="--desktop-shell") { DesktopShellChecks.Run(Check); Console.WriteLine($"PASS {_checks} actual Explorer desktop checks"); return 0; }
            if(args.Length>0 && args[0]=="--rendering") { NativeRenderingChecks.Run(Check); Console.WriteLine($"PASS {_checks} native rendering checks"); return 0; }
            if(args.Length>0 && args[0]=="--interaction") { InteractionRegressionChecks.Standalone(Check); Console.WriteLine($"PASS {_checks} interaction checks"); return 0; }
            if(args.Length>0 && args[0]=="--layers") { RunLayerChecks(); Console.WriteLine($"PASS {_checks} multi-HUD layer checks"); return 0; }
            if(args.Length>0 && args[0].StartsWith("--frame")) return LiveFrameChecks.Run(args);
            if(args.Length>0 && args[0]=="--fps-live") return LiveFrameChecks.Run(args);
            var face = new Typeface(new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"));
            Check(face.GlyphTypeface.GetGlyph('A') != 0, "ECP-4: bundled Inter must resolve without an installed font.");
            NativeWindowChecks();
            FeatureRegressionChecks.Run(Check);
            UsbNotificationChecks.Run(Check);
            FrameCollectorResourceChecks.Run(Check);
            SettingsFeatureChecks();
            RuntimeRegressionChecks.Run(Check);
            HotkeyRegressionChecks.Run(Check);
            DataWarningRegressionChecks.Run(Check);
            NextVersionRegressionChecks.Run(Check);
            HardwareTelemetryChecks.Run(Check);
            NavigationManualChecks.Run(Check);
            NativeRenderingChecks.Run(Check);
            SynchronizationContext.SetSynchronizationContext(null);
            RunAsync().GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {_checks} checks (embedded font, native HUD styles/layers, HTTP, colors, fans, sampling).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void SettingsFeatureChecks()
    {
        WorldClockChecks.Editor(Check);
        EndfieldChargePlus.LocalizationManager.Initialize("zh-CN");
        var hud=new HudWindow();
        var settings=new EndfieldChargePlus.Settings.AppSettings();
        using var runtime=new CustomHudRuntime(hud);
        runtime.ApplySettings(settings);
        var window=new EndfieldChargePlus.Settings.SettingsWindow(settings,hud,runtime);
        Check(window.Title=="Endfield Charge Plus v0.1.2 设置","Settings title contains the frozen version without parentheses.");
        var panel=window.FindControl<Avalonia.Controls.StackPanel>("ExperimentalPanel")!;
        Check(panel.Children.Count>3,"Experimental tab contains a multi-instance editor.");
        Check(!window.FindControl<HudCustomizerView>("Customizer")!.FindControl<Avalonia.Controls.CheckBox>("PersistentClickSwitch")!.IsEnabled,"Click cycling is configurable only in persistent mode.");
        var view=window.FindControl<HudCustomizerView>("Customizer")!;
        var combo=view.FindControl<Avalonia.Controls.ComboBox>("ProfileCombo")!;
        combo.SelectedIndex=settings.CustomHud.Profiles.FindIndex(p=>p.BuiltInKey=="system.window-fps");
        Check(view.FindControl<Avalonia.Controls.Border>("FrameOptionsPanel")!.IsVisible && view.FindControl<Avalonia.Controls.StackPanel>("FrameWindowOptions")!.IsVisible,"Selecting Window FPS exposes the real target and full-scale settings.");
        combo.SelectedIndex=settings.CustomHud.Profiles.FindIndex(p=>p.BuiltInKey=="system.display-fps");
        Check(view.FindControl<Avalonia.Controls.StackPanel>("FrameDisplayOptions")!.IsVisible && !view.FindControl<Avalonia.Controls.StackPanel>("FrameWindowOptions")!.IsVisible,"Selecting Display FPS exposes the corresponding display settings.");
        EndfieldChargePlus.LocalizationManager.SetLanguage(EndfieldChargePlus.AppLanguage.English);
        typeof(EndfieldChargePlus.Settings.SettingsWindow).GetMethod("ApplyLocalization",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(window,null);
        Check(window.Title=="Endfield Charge Plus v0.1.2 Settings","English title has no parentheses.");
        window.Close(); hud.Close();
    }

    private static void RunLayerChecks()
    {
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(180));Exception? failure=null;bool completed=false;
        Avalonia.Threading.Dispatcher.UIThread.Post(async ()=> { try { await MultiHudLayerChecks.Run(Check);completed=true; } catch(Exception ex) { failure=ex; } finally { finish.Cancel(); } });
        Avalonia.Threading.Dispatcher.UIThread.MainLoop(finish.Token);
        if(failure is not null) throw failure;
        if(!completed) throw new TimeoutException("Multi-HUD layer tests did not complete.");
    }

    private static void NativeWindowChecks()
    {
        IntPtr window = CreateWindowEx(0x40000, "STATIC", "ECP regression", 0x80000000, 0, 0, 560, 90, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Check(window != IntPtr.Zero, "Create test HUD HWND.");
        try
        {
            using var input = WindowsHudHitTest.TryAttach(window, _ => false);
            Check(input is not null, "Native HUD attachment.");
            uint style = unchecked((uint)GetWindowLong(window, -20));
            Check((style & 0x080800A0) == 0x080800A0 && (style & 0x40000) == 0, "ECP-18/46: tool window, no activation, click-through, no Alt+Tab app style.");
            using var layer = new WindowsHudLayer(window);
            layer.Apply(true, false);
            Check((GetWindowLong(window,-16)&0x40000000)==0 && (GetWindowLong(window,-20)&8)==0,"Desktop HUD remains a non-topmost top-level render surface, avoiding Explorer child rendering/DPI failures.");
            layer.Apply(false, true);
            Check(GetParent(window) == IntPtr.Zero, "HUD returns to top-level mode.");
            Check((GetWindowLong(window, -20) & 8) != 0, "ECP-72: native topmost style applied.");
            layer.Apply(false, false);
            Check((GetWindowLong(window, -20) & 8) == 0, "Desktop layer switch removes topmost.");
            using var interaction=new WindowsHudInteraction(window,_=>true,()=>{});
            interaction.SetEnabled(true);
            Check((GetWindowLong(window,-20)&0x20)!=0,"The animated host stays fully mouse-through while click switching is enabled.");
            interaction.SetEnabled(false);
            Check((GetWindowLong(window,-20)&0x20)!=0,"Disabling click cycling restores click-through behavior.");
        }
        finally { DestroyWindow(window); }
    }

    private static async Task RunAsync()
    {
        var area = new PixelRect(80, 0, 1840, 1080);
        var leftTray = TrayMenuPosition.Calculate(new PixelPoint(40, 300), area, 200, 180);
        Check(leftTray == new PixelPoint(88, 120), "ECP-17/47: left vertical taskbar menu stays beside the click.");
        var rightTray = TrayMenuPosition.Calculate(new PixelPoint(1900, 300), new PixelRect(0, 0, 1840, 1080), 200, 180);
        Check(rightTray == new PixelPoint(1632, 120), "Right vertical taskbar stays inside its monitor.");
        Check(TrayMenuPosition.Calculate(new PixelPoint(-10, 0), new PixelRect(-1920, 40, 1920, 1040), 200, 180).Y == 48, "Top taskbar with negative monitor origin.");
        Check(Math.Abs(WindowsCpuFrequency.EffectiveGhz(3000, 150) - 4.5) < 0.001, "ECP-30: turbo is relative to the counter's nominal baseline, never the WMI boosted limit.");
        Check(WindowsCpuFrequency.EffectiveGhz(3000, 0) == 3 && double.IsNaN(WindowsCpuFrequency.EffectiveGhz(0, 150)), "Missing frequency inputs do not fabricate a high clock.");
        var rules = HudColorRuleParser.Parse("{cpu.usage} ≥ 80% => #FF4D4F\ncpu.usage < 80 => #C6CA4C");
        var profile = new HudProfile { ColorRules = rules };
        Check(HudProfileRenderer.Render(profile, new Dictionary<string, object?> { ["cpu.usage"] = 80 }).AccentColor == "#FF4D4F", "ECP-22: inclusive threshold with template syntax.");
        Check(HudProfileRenderer.Render(profile, new Dictionary<string, object?> { ["cpu.usage"] = 79 }).AccentColor == "#C6CA4C", "ECP-22: lower threshold.");
        Check(HudProfileRenderer.Render(profile, new Dictionary<string, object?>()).AccentColor == profile.AccentColor, "Missing measurements must not match rules.");
        bool rejected = false;
        try { HudColorRuleParser.Parse("cpu.usage > 80 => #1234567"); } catch (FormatException) { rejected = true; }
        Check(rejected, "Invalid color rules must report an error.");
        HardwareSensorCatalog.PublishFans(new Dictionary<string, (string, double)> { ["abc"] = ("CPU fan", 1200), ["def"] = ("Case fan", 800) });
        Check(VariableCatalog.AllBuiltIns.Any(x => x.Key == "system.fan.abc.rpm") && VariableCatalog.AllBuiltIns.Any(x => x.Key == "system.fan.def.rpm"), "ECP-10: independent fan variables.");
        Check(!VariableCatalog.AllBuiltIns.Any(x => x.Key == "system.fan.unknown.rpm"), "Only detected fans appear in the library.");

        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var listener = new HttpListener();
        string url = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(url); listener.Start();
        int requests = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    Interlocked.Increment(ref requests);
                    string value = context.Request.Headers["X-value"] ?? "";
                    if (value == "fail") context.Response.StatusCode = 401;
                    byte[] body = Encoding.UTF8.GetBytes("{\"data\":{\"value\":" + (value == "fail" ? "0" : value) + "}}");
                    await context.Response.OutputStream.WriteAsync(body); context.Response.Close();
                }
            }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
        });
        using var hub = new VariableHub { SamplingIntervalSeconds = 60 };
        var sources = HttpSourceConfiguration.Parse("[{\"name\":\"example\",\"enabled\":true,\"url\":\"" + url + "\",\"headers\":{\"X-value\":\"42\"},\"fields\":[{\"variable\":\"value\",\"jsonPath\":\"data.value\"}]}]");
        var settings = new CustomHudSettings { HttpSources = sources };
        var keys = new[] { "custom.example.value", "custom.example.error" };
        var snapshot = await hub.SnapshotAsync(settings, keys);
        Check(Convert.ToDouble(snapshot["custom.example.value"]) == 42, "ECP-16/48: lower-case sample JSON works end to end.");
        int firstCount = requests;
        await hub.SnapshotAsync(settings, keys);
        Check(requests == firstCount, "ECP-73: repeated refresh uses the configured sampling/cache interval.");
        sources[0].Headers["X-value"] = "43";
        snapshot = await hub.SnapshotAsync(settings, keys);
        Check(Convert.ToDouble(snapshot["custom.example.value"]) == 43 && requests == firstCount + 1, "Changed headers invalidate the source and snapshot caches.");
        sources[0].Headers["X-value"] = "fail";
        snapshot = await hub.SnapshotAsync(settings, keys);
        Check((string?)snapshot["custom.example.error"] == "HTTP 401" && !snapshot.ContainsKey("custom.example.value"), "HTTP errors are visible and never become fake zeroes.");
        firstCount = requests;
        await hub.SnapshotAsync(settings, keys);
        Check(requests == firstCount, "Failed requests are throttled.");
        sources[0].Headers["X-value"] = "42";
        sources[0].Fields[0] = sources[0].Fields[0] with { JsonPath = "data.missing" };
        snapshot = await hub.SnapshotAsync(settings, keys);
        Check(snapshot["custom.example.error"]?.ToString()?.Contains("data.missing") == true && !snapshot.ContainsKey("custom.example.value"), "A missing JSON path is visible and cannot reuse an old value.");
        sources[0].Headers["X-value"] = "broken";
        snapshot = await hub.SnapshotAsync(settings, keys);
        Check(!string.IsNullOrEmpty(snapshot["custom.example.error"]?.ToString()), "An invalid JSON response has an explicit error.");
        rejected = false;
        try { HttpSourceConfiguration.Parse("[{\"name\":\"x\",\"url\":\"\"}]"); } catch (FormatException) { rejected = true; }
        Check(rejected, "Invalid enabled HTTP sources cannot be silently accepted.");
        listener.Stop(); await server;
    }

    private sealed class FontTestApp : Application { public FontTestApp() { } }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
}
