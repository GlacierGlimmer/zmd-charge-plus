using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;

internal static class LinuxGuiAudit
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        SettingsManager.Save(SettingsManager.CreateDefaults() with { AlwaysVisible = true, UiLanguage = "zh-CN" });
        bool passed = false;
        EndfieldChargePlus.Program.BuildAvaloniaApp().AfterSetup(_ => DispatcherTimer.RunOnce(async () =>
        {
            var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            try
            {
                var window = desktop.Windows.OfType<SettingsWindow>().Single();
                Assert(window.Title!.Contains(ProductInfo.LinuxName), "Settings title was not renamed.");
                await window.Clipboard!.SetTextAsync("Linux clipboard audit");
                Assert(await window.Clipboard.GetTextAsync() == "Linux clipboard audit", "Clipboard copy/read round trip failed on a fresh X server.");
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var http = new HttpClient(new AuditHttpHandler());
                using var hub = new VariableHub(http);
                var settings = CustomHudSettings.CreateDefault() with { DeepSeekApiKeyProtected = SecretStore.Protect("audit-key") };
                var definitions = VariableCatalog.AllBuiltIns;
                Assert(!definitions.Any(d => d.Key.StartsWith("security.defender") || d.Key.StartsWith("dev.wsl") || d.Key.Contains("bitlocker")), "Windows-only variables remain exposed.");
                var keys = definitions.Select(d => d.Key).ToArray();
                await hub.SnapshotAsync(settings, keys, pingTarget: "localhost", probeProtocol: "TCP", probePort: port);
                await Task.Delay(250);
                var values = await hub.SnapshotAsync(settings, keys, pingTarget: "localhost", probeProtocol: "TCP", probePort: port);
                var effective = HudProfileRenderer.BuildEffectiveVariables(new HudProfile
                { PrimaryTemplate = "{time.current}{network.download_bps}" }, values);
                var missing = definitions.Where(d => !effective.TryGetValue(d.Key, out var value) || value is null).Select(d => d.Key).ToArray();
                var report = new StringBuilder("# Linux variable audit\n\n");
                report.AppendLine($"Advertised variables: {definitions.Count}. Missing values: {missing.Length}.\n");
                report.AppendLine("Local metrics use the running Linux system. Public-IP and DeepSeek requests use deterministic HTTP fixtures; network probes use a real loopback TCP listener. GUI/clipboard are tested on Xvfb.\n");
                report.AppendLine("| Variable | Rendered value |\n| --- | --- |");
                foreach (var item in definitions)
                {
                    string rendered = TemplateEngine.Render(item.TemplateToken, effective).Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");
                    report.AppendLine($"| `{item.Key}` | {rendered} |");
                }
                report.AppendLine("\n## Removed or unavailable on this machine\n");
                foreach (var item in VariableCatalog.PlatformIndependentDefinitions.Where(d => !LinuxVariableCatalog.Supports(d.Key)))
                    report.AppendLine($"- `{item.Key}`");
                File.WriteAllText(Path.Combine(output, "VARIABLE-AUDIT.md"), report.ToString());
                Assert(missing.Length == 0, "Advertised variables missing values: " + string.Join(", ", missing));
                Assert(definitions.All(d => TemplateEngine.Render(d.TemplateToken, effective) != "--"), "A catalog variable still renders a placeholder.");
                Assert(Convert.ToDouble(values["clipboard.text_length"]) == 21, "Linux clipboard text was not read: " + values["clipboard.text_length"]);
                Assert(Convert.ToDouble(values["deepseek.balance"]) == 12.5, "DeepSeek fixture was not parsed.");
                foreach (var profile in settings.Profiles)
                {
                    var rendered = HudProfileRenderer.Render(profile, effective);
                    Assert(!new[] { rendered.PrimaryText, rendered.SecondaryText, rendered.RightText }.Any(s => s.Contains("--")), "Built-in profile still renders placeholders: " + profile.BuiltInKey);
                }
                var imported = LinuxVariableCatalog.RemoveUnsupportedReferences(new HudProfile
                { PrimaryTemplate = "{security.defender.status}{memory.usage}", ProgressVariable = "security.bitlocker.encryption_percent" });
                Assert(imported.PrimaryTemplate == "{memory.usage}" && imported.ProgressVariable == "", "Old unsupported references were not cleaned.");
                await AuditFailuresAndProtocols(settings);
                foreach (var adapter in LinuxVariableProvider.GetGpuAdapters())
                {
                    var gpuDefinitions = LinuxVariableCatalog.Build(VariableCatalog.PlatformIndependentDefinitions, adapter.Id).Where(d => d.Key.StartsWith("gpu.")).ToArray();
                    var gpuValues = await hub.SnapshotAsync(settings, gpuDefinitions.Select(d => d.Key), gpuAdapterId: adapter.Id);
                    Assert(gpuDefinitions.All(d => gpuValues.ContainsKey(d.Key)), "GPU catalog differs from selected adapter capabilities.");
                }
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(output, "linux-settings.png"));
                window.FindControl<Button>("LanguageEnglishBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Assert(window.Title!.Contains(ProductInfo.LinuxName) && !window.Title.Contains("For Linux For Linux"), "English branding failed.");
                bitmap.Render(window);
                bitmap.Save(Path.Combine(output, "linux-settings-en.png"));
                window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
                await Task.Delay(100);
                var customizer = window.FindControl<HudCustomizerView>("Customizer")!;
                customizer.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
                await Task.Delay(100);
                var list = customizer.FindControl<ListBox>("VariableList")!;
                Assert(list.ItemCount >= definitions.Count, "UI variable library is missing supported entries.");
                list.SelectedIndex = list.ItemCount - 1;
                list.ScrollIntoView(list.SelectedItem!);
                var refresh = customizer.FindControl<Button>("RefreshLinuxVariablesBtn")!;
                Assert(refresh.IsVisible && refresh.Content?.ToString() == "Detect Linux variables again", "Linux capability refresh control is missing or untranslated.");
                await Task.Delay(100);
                bitmap.Render(window);
                bitmap.Save(Path.Combine(output, "linux-variable-library.png"));
                Console.WriteLine($"PASS: all {definitions.Count} advertised variables rendered without placeholders; built-in profiles, API fixtures, clipboard, branding and import migration");
                passed = true;
                window.FindControl<Button>("ExitBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                desktop.Shutdown(1);
            }
        }, TimeSpan.FromSeconds(2))).StartWithClassicDesktopLifetime(Array.Empty<string>());
        return passed ? 0 : 1;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static async Task AuditFailuresAndProtocols(CustomHudSettings settings)
    {
        using var successHttp = new HttpClient(new AuditHttpHandler());
        using var hub = new VariableHub(successHttp);
        var missingKey = await hub.SnapshotAsync(settings with { DeepSeekApiKeyProtected = "" }, new[] { "deepseek.balance" });
        Assert(missingKey["deepseek.balance"] is string, "Missing credentials must show a configuration state.");
        var icmp = await hub.SnapshotAsync(settings, new[] { "probe.online" }, pingTarget: "127.0.0.1", probeProtocol: "ICMP");
        Assert(Equals(icmp["probe.online"], true), "Unprivileged Linux ICMP probe failed: " + icmp["probe.error"]);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var echo = Task.Run(async () => { var request = await udp.ReceiveAsync(); await udp.SendAsync(request.Buffer, request.RemoteEndPoint); });
        var udpValues = await hub.SnapshotAsync(settings, new[] { "probe.online", "probe.tcp_connect_time" }, pingTarget: "127.0.0.1", probeProtocol: "UDP", probePort: ((IPEndPoint)udp.Client.LocalEndPoint!).Port);
        await echo.WaitAsync(TimeSpan.FromSeconds(3));
        Assert(Equals(udpValues["probe.online"], true) && udpValues["probe.tcp_connect_time"] is string, "UDP echo or protocol-specific status failed.");
        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start(); int port = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();
        var failedProbe = await hub.SnapshotAsync(settings, new[] { "probe.latency_ms" }, pingTarget: "127.0.0.1", probeProtocol: "TCP", probePort: port);
        Assert(Equals(failedProbe["probe.online"], false) && failedProbe["probe.latency_ms"] is string && failedProbe["probe.avg_latency_ms"] is string, "Failed TCP probes must not fabricate latency numbers.");
        var sources = settings with { HttpSources = new() { new() { Name = "audit", Url = "https://audit.invalid/data", Fields = new() { new() { Variable = "value", JsonPath = "data.items[0].value" }, new() { Variable = "missing", JsonPath = "missing" } } } } };
        var custom = await hub.SnapshotAsync(sources, new[] { "custom.audit.value", "custom.audit.missing" });
        Assert(Convert.ToDouble(custom["custom.audit.value"]) == 42 && custom["custom.audit.missing"] is string, "Custom HTTP/JSON mapping or missing-field state failed.");
        using var failedHttp = new HttpClient(new FailedHttpHandler());
        using var failedHub = new VariableHub(failedHttp);
        var failed = await failedHub.SnapshotAsync(sources, new[] { "deepseek.balance", "network.public_ipv4", "network.public_ipv6", "custom.audit.value" });
        Assert(new[] { "deepseek.balance", "network.public_ipv4", "network.public_ipv6", "custom.audit.value" }.All(k => failed[k] is string), "HTTP failures must show status instead of fake data or placeholders.");
        Console.WriteLine("PASS: unprivileged ICMP, TCP, UDP, failure states, custom HTTP/JSON and missing API key");
    }

    private sealed class FailedHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class AuditHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.RequestUri!.Host switch
            {
                "api.ipify.org" => "203.0.113.10",
                "api6.ipify.org" => "2001:db8::10",
                "audit.invalid" => "{\"data\":{\"items\":[{\"value\":42}]}}",
                "api.deepseek.com" => "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"12.50\",\"granted_balance\":\"2.50\",\"topped_up_balance\":\"10.00\"}]}",
                _ => throw new Exception("Unexpected audit request: " + request.RequestUri),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
