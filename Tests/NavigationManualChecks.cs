using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class NavigationManualChecks
{
    internal static void Run(Action<bool,string> check)
    {
        Models(check);
        var application=Application.Current!;var oldTheme=application.RequestedThemeVariant;
        application.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(90));Exception? failure=null;bool completed=false;
        Dispatcher.UIThread.Post(async()=>{try{await Gui(check);completed=true;}catch(Exception ex){failure=ex;}finally{finish.Cancel();}});
        try{Dispatcher.UIThread.MainLoop(finish.Token);}finally{application.RequestedThemeVariant=oldTheme;}
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if(!completed)throw new TimeoutException("Settings navigation/manual checks did not finish.");
    }
    private static void Models(Action<bool,string> check)
    {
        var gpu=new HardwareReading("lhm","/gpu-test/0/temperature/0","Test GPU","GPU Core","Temperature","°C",42d);
        var bios=new HardwareReading("inventory","bios.Version","BIOS","Version","SMBIOS","","test");
        var fan=new HardwareReading("lhm","/lpc/test/fan/0","Board","CPU Fan","Fan","RPM",1200d);
        string fanId=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fan.Id)))[..12].ToLowerInvariant();
        HardwareSensorCatalog.PublishFans(new Dictionary<string,(string,double)>{{fanId,("CPU Fan",1200)}});
        HardwareTelemetryCatalog.Publish(new[]{gpu,bios,fan});
        check(!VariableCatalog.VisibleBuiltIns.Any(v=>v.Key=="gpu.temperature") && VariableCatalog.Find("gpu.temperature") is not null,"Duplicate legacy GPU entries leave the library while old templates remain supported.");
        check(!VariableCatalog.VisibleBuiltIns.Any(v=>v.Key=="system.bios_version") && VariableCatalog.VisibleBuiltIns.Any(v=>v.Key==bios.Key+".value"),"Firmware information has one visible canonical hardware entry.");
        HardwareTelemetryCatalog.Publish(new[]{new HardwareReading("inventory","processor.0.Version","processor.0","Version","SMBIOS","","Test CPU"),new HardwareReading("inventory","cpuid.vendor","CPUID","vendor","CPUID","","AuthenticAMD")});
        check(!VariableCatalog.VisibleBuiltIns.Any(v=>v.Key is "cpu.name" or "cpu.manufacturer") && VariableCatalog.Find("cpu.name") is not null,"Processor model and vendor use their hardware inventory entries while legacy templates remain supported.");
        check(!VariableCatalog.VisibleBuiltIns.Any(v=>v.Key==$"system.fan.{fanId}.rpm") && VariableCatalog.VisibleBuiltIns.Any(v=>v.Key==fan.Key+".value"),"Each fan is listed once through its hardware sensor key.");
        check(VariableCatalog.VisibleBuiltIns.Any(v=>v.Key=="cpu.temperature_max") && VariableCatalog.VisibleBuiltIns.Any(v=>v.Key=="cpu.core.temperature_avg"),"Derived highest and average temperatures retain their distinct semantics.");
        check(!VariableCatalog.VisibleBuiltIns.Any(v=>v.Key is "cpu.temperature" or "cpu.temperature_c"),"Redundant CPU temperature aliases are not advertised as separate variables.");
        var native=new HardwareReading("linux","memory.total_bytes","linux","Memory total","Native","B",1024d);
        HardwareTelemetryCatalog.Publish(new[]{native});
        check(!VariableCatalog.VisibleBuiltIns.Any(v=>v.Key=="memory.total_bytes") && VariableCatalog.Find("memory.total_bytes") is not null,"An equivalent native hardware field is listed once while saved legacy keys remain supported.");
        check(HudProfileRenderer.Render(new HudProfile{PrimaryTemplate="{gpu.temperature|0.0}",ProgressVariable="=50"},new Dictionary<string,object?>{{"gpu.temperature",42d}}).PrimaryText=="42.0","Existing saved HUD tokens still render without migration or changed semantics.");
        var chapters=UserManualContent.Chapters;
        check(chapters.Count>=14 && chapters.Select(c=>c.Id).Distinct().Count()==chapters.Count,"The manual has a unique structured chapter index covering all settings areas.");
        foreach(var chapter in chapters)
            check(chapter.Blocks.Count>=2 && chapter.Blocks.Select(b=>b.Id).Distinct().Count()==chapter.Blocks.Count && chapter.Blocks.All(b=>b.Title.Zh.Length>0 && b.Title.En.Length>0 && b.Body.Zh.Length>0 && b.Body.En.Length>0),"Manual chapter has bilingual sections: "+chapter.Id);
        check(chapters.Single(c=>c.Id=="sources").Matches("HTTP") && chapters.Single(c=>c.Id=="hardware").Matches("温度"),"Manual search covers chapter bodies and both languages.");
        var sources=HttpSourceConfiguration.Parse(UserManualContent.HttpExample);
        check(sources.Count==1 && !sources[0].Enabled && sources[0].Fields.Count==2,"The manual HTTP example parses with the real source parser and is disabled by default.");
        var data=new Dictionary<string,object?>{{"cpu.usage",12.34},{"gpu.usage",23.46},{"cpu.frequency_ghz",3.58},{"network.download_bps",1024d}};
        check(TemplateEngine.Render("{cpu.usage|math:round:1|0.0}",data)=="12.3" && TemplateEngine.Render("{= (cpu.usage + gpu.usage) / 2 |0.0}",data)=="17.9","The manual's numeric examples work in the actual formatter.");
    }
    private static async Task Gui(Action<bool,string> check)
    {
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
        var settings=new AppSettings{HudEnabled=false};var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        var window=new SettingsWindow(settings,hud,runtime);window.Show();
        try
        {
            var tabs=window.FindControl<TabControl>("MainSettingsTabs")!;var dataTabs=window.FindControl<TabControl>("DataManagementTabs")!;var editor=window.FindControl<HudCustomizerView>("Customizer")!;
            check(tabs.Items.OfType<TabItem>().Select(t=>t.Header?.ToString()).SequenceEqual(new[]{"显示与位置","HUD显示内容","数据管理","实验性功能","使用手册","关于"}),"The six top-level tabs match the requested wording and order.");
            check(dataTabs.Items.OfType<TabItem>().Select(t=>t.Header?.ToString()).SequenceEqual(new[]{"变量库","硬件数据","数据源配置"}),"Data Management owns Variable Library, Hardware Data and Data Sources in that order.");
            check(editor.FindControl<TabControl>("CustomizerTabs")!.Items.Count==2,"HUD Content only retains profile editing and trigger rules.");
            window.NavigateToSettingsSection("sources");await Task.Delay(250);
            check(tabs.SelectedItem==window.FindControl<TabItem>("DataManagementPage") && dataTabs.SelectedIndex==2,"Settings navigation selects the real nested data-source page.");
            var http=editor.FindControl<TextBox>("HttpSourcesJsonBox")!;http.Text=UserManualContent.HttpExample;
            var key=editor.FindControl<TextBox>("DeepSeekKeyBox")!;key.Text="unsaved-ui-only";
            window.NavigateToSettingsSection("hud");window.NavigateToSettingsSection("sources");
            check(http.Text==UserManualContent.HttpExample && key.Text=="unsaved-ui-only","Moving between main pages preserves the one shared unsaved source editor.");
            var exported=editor.ExportSettings();check(exported.HttpSources.Count==1 && exported.HttpSources[0].Name=="demo","The relocated source editor still participates in Save & Apply export.");
            var manual=window.FindControl<UserManualView>("ManualView")!;window.NavigateToSettingsSection("manual");await Task.Delay(250);
            manual.Navigate("sources","http-example");await Task.Delay(150);
            check(manual.CurrentChapterId=="sources" && manual.SectionIds.Contains("http-example"),"Manual chapters and section anchors navigate without showing one giant document.");
            Capture(window,"manual-http-anchor-zh.png");
            var exampleHeading=manual.GetVisualDescendants().OfType<TextBlock>().First(t=>t.Text=="可复制的 JSON 结构" && t.GetVisualAncestors().OfType<ScrollViewer>().Any(s=>s.Name=="ManualReadingScroll"));
            var readingScroll=exampleHeading.GetVisualAncestors().OfType<ScrollViewer>().First(s=>s.Name=="ManualReadingScroll");
            var headingPosition=exampleHeading.TransformToVisual(readingScroll)?.Transform(default);
            check(headingPosition is {Y:>=0 and <100},"Section navigation brings the requested heading into the reading area.");Capture(window,"manual-http-anchor-zh.png");
            manual.SearchBox.Text="PawnIO";await Task.Delay(120);check(manual.ChapterList.ItemCount>0 && manual.ChapterList.ItemCount<UserManualContent.Chapters.Count,"Manual search filters to relevant chapters.");
            manual.SearchBox.Text="no-such-chapter-ecp-178";await Task.Delay(120);check(manual.ChapterList.ItemCount==0,"Manual search provides an empty state.");
            manual.SearchBox.Text="";await Task.Delay(120);
            foreach(var chapter in UserManualContent.Chapters) {manual.Navigate(chapter.Id);check(manual.SectionIds.Count==chapter.Blocks.Count,"Manual chapter renders separate cards: "+chapter.Id);}
            manual.Navigate("hardware");var jump=manual.GetLogicalDescendants().OfType<Button>().First(b=>b.Content?.ToString()=="打开对应设置 →");
            jump.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
            check(dataTabs.SelectedItem==window.FindControl<TabItem>("HardwarePage") && tabs.SelectedItem==window.FindControl<TabItem>("DataManagementPage"),"A manual action opens the corresponding real settings page.");
            window.NavigateToSettingsSection("manual");manual.Navigate("start");await Task.Delay(200);Capture(window,"manual-zh.png");
            LocalizationManager.SetLanguage(AppLanguage.English);typeof(SettingsWindow).GetMethod("ApplyLocalization",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);await Task.Delay(250);
            check(tabs.Items.OfType<TabItem>().Select(t=>t.Header?.ToString()).SequenceEqual(new[]{"Display & Position","HUD Content","Data Management","Experimental Features","User Manual","About"}),"All top-level pages localize in the requested order.");
            check(dataTabs.Items.OfType<TabItem>().Select(t=>t.Header?.ToString()).SequenceEqual(new[]{"Variable Library","Hardware Data","Data Source Configuration"}),"Relocated data pages localize through the main settings window.");
            check(http.Text==UserManualContent.HttpExample && key.Text=="unsaved-ui-only","Language changes do not discard relocated source edits.");
            check(manual.CurrentChapterId=="start" && manual.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text=="Getting started"),"Manual language switching retains the current chapter.");
            var minimum=new SettingsWindow(settings,hud,runtime){Width=940,Height=680};minimum.NavigateToSettingsSection("manual");minimum.Show();await Task.Delay(300);
            try
            {
                var minimumTabs=minimum.FindControl<TabControl>("MainSettingsTabs")!;var minimumManual=minimum.FindControl<UserManualView>("ManualView")!;
                var save=minimum.FindControl<Button>("SaveBtn")!;var savePoint=save.TranslatePoint(default,minimum)!.Value;
                var manualPoint=minimumManual.TranslatePoint(default,minimum)!.Value;
                Console.WriteLine($"Minimum layout: client={minimum.ClientSize}; tabs={minimumTabs.Bounds}; manual={minimumManual.Bounds}; save={savePoint}");
                check(minimumTabs.Items.OfType<TabItem>().All(t=>t.Bounds.Width>0 && t.Bounds.Right<=minimumTabs.Bounds.Width+1),"All six English main headers fit at the minimum window width.");
                check(savePoint.X+save.Bounds.Width<=minimum.ClientSize.Width && manualPoint.X+minimumManual.Bounds.Width<=minimum.ClientSize.Width && manualPoint.Y+minimumManual.Bounds.Height<=minimum.ClientSize.Height,"The save button and complete manual reading area fit inside the minimum viewport.");
                Capture(minimum,"manual-en-minimum.png");
            }
            finally{minimum.Close();}
            LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);typeof(SettingsWindow).GetMethod("ApplyLocalization",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);await Task.Delay(150);
            check(dataTabs.Items.OfType<TabItem>().Select(t=>t.Header?.ToString()).SequenceEqual(new[]{"变量库","硬件数据","数据源配置"}),"Data Management labels return to Chinese without losing their original names.");
            window.Width=1120;window.Height=790;window.NavigateToSettingsSection("variables");await Task.Delay(300);Capture(window,"data-management-zh.png");
        }
        finally {window.Close();hud.Close();LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);}
    }
    private static void Capture(Window window,string name)
    {
        if(Environment.GetEnvironmentVariable("ECP_NAVIGATION_TEST_OUTPUT") is not {Length:>0} output)return;
        Directory.CreateDirectory(output);using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));bitmap.Render(window);bitmap.Save(Path.Combine(output,name));
    }
}
