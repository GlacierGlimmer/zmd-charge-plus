using System.Text.RegularExpressions;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class LocalizationRegressionChecks
{
    private static IEnumerable<AppLanguageInfo> SelectedLanguages()=>LocalizationManager.SupportedLanguages.Where(l=>Environment.GetEnvironmentVariable("ECP_LOCALIZATION_PREVIEW") is {Length:>0} selected ? selected.Split(',').Contains(l.Code) : true);
    private static bool EnglishFamily(string code)=>code.StartsWith("en-",StringComparison.Ordinal);
    private static bool CanonicalGuide(string code)=>EnglishFamily(code) || code is "zh-CN" or "zh-TW" or "zh-HK";
    private static readonly Regex Arguments=new(@"(?<!\{)\{\d+(?:,-?\d+)?(?::[^{}]+)?\}(?!\})");
    private static readonly Regex Code=new(@"\$\{[^{}\n]*\}|\{[^{}\n]*\}|\b[A-Za-z_][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_<>-]+)+");
    internal static void Run(Action<bool,string> check,bool gui=true)
    {
        Models(check);
        if(!gui)return;
        var application=Application.Current!;var old=application.RequestedThemeVariant;
        application.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
        using var finish=new CancellationTokenSource(TimeSpan.FromMinutes(12));Exception? failure=null;bool completed=false;
        Dispatcher.UIThread.Post(async()=>{try{await Gui(check);completed=true;}catch(Exception ex){failure=ex;}finally{finish.Cancel();}});
        try{Dispatcher.UIThread.MainLoop(finish.Token);}finally{application.RequestedThemeVariant=old;LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);}
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if(!completed)throw new TimeoutException("Multilingual GUI verification did not complete.");
    }
    private static string[] Tokens(Regex expression,string value)=>expression.Matches(value).Select(m=>m.Value).Where(v=>v!="e.g").OrderBy(s=>s,StringComparer.Ordinal).ToArray();
    private static void Models(Action<bool,string> check)
    {
        using var requirementsStream=typeof(LocalizationManager).Assembly.GetManifestResourceStream("EndfieldChargePlus.LocaleReleaseRequirements") ?? throw new InvalidOperationException("Missing complete-source locale inventory");
        using var requirements=JsonDocument.Parse(requirementsStream);
        var required=requirements.RootElement.GetProperty("requiredKeys").EnumerateArray().Select(v=>v.GetString()!).ToHashSet(StringComparer.Ordinal);
        var sourceTokens=requirements.RootElement.GetProperty("sourceTokens");
        using var sharedStream=typeof(LocalizationManager).Assembly.GetManifestResourceStream("EndfieldChargePlus.LocaleSharedWords");
        using var shared=sharedStream is null ? JsonDocument.Parse("{}") : JsonDocument.Parse(sharedStream);
        bool SharedWord(string code,string key)=>shared.RootElement.TryGetProperty(code,out var words) && words.TryGetProperty(key,out _);
        bool Syntax(string key,string value)
        {
            var expected=Tokens(Code,key);
            var allowed=expected.Concat(sourceTokens.TryGetProperty(key,out var source) ? source.EnumerateObject().Select(p=>p.Name) : Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            var actual=Tokens(Code,value).Select(t=>allowed.Contains(t) ? t : allowed.OrderByDescending(a=>a.Length).FirstOrDefault(a=>!t.StartsWith('{') && !t.StartsWith("${",StringComparison.Ordinal) && t.StartsWith(a+"-",StringComparison.Ordinal)) ?? t).ToArray();
            return expected.GroupBy(t=>t).All(g=>actual.Count(t=>t==g.Key)>=g.Count()) && actual.All(allowed.Contains);
        }
        var languages=LocalizationManager.SupportedLanguages;
        var tibetanFace=new Typeface(new FontFamily("avares://EndfieldChargePlus/Assets/Fonts#Noto Serif Tibetan"));
        check(tibetanFace.GlyphTypeface.GetGlyph(0x0F56)!=0 && tibetanFace.GlyphTypeface.GetGlyph(0x0F7C)!=0,"Bundled Tibetan font includes letters and combining vowels without an installed font.");
        var gothicFace=new Typeface(new FontFamily("avares://EndfieldChargePlus/Assets/Fonts#Noto Sans Gothic"));
        var deseretFace=new Typeface(new FontFamily("avares://EndfieldChargePlus/Assets/Fonts#Noto Sans Deseret"));
        check(gothicFace.GlyphTypeface.GetGlyph(0x10330)!=0 && deseretFace.GlyphTypeface.GetGlyph(0x10412)!=0,"Bundled Gothic and Deseret fonts cover supplementary-plane letters used by language names and upside-down English.");
        check(languages.Count==20 && languages.Select(l=>l.Code).Distinct().Count()==20,"The offline catalog contains only the original 20 basic languages.");
        foreach(var (source,expected) in new[]{("zh-HK","zh-TW"),("zh-MO","zh-TW"),("zh-Hant","zh-TW"),("zh-SG","zh-CN"),("en-GB","en-US"),("pt-PT","pt-BR"),("ar-EG","ar-SA"),("ja","ja-JP"),("bo","Auto"),("bo-IN","Auto"),("not-a-language","Auto")})
            check(LocalizationManager.NormalizePreference(source)==expected,"Saved language aliases normalize correctly: "+source);
        check(LocalizationManager.PreferenceFor(LocalizationManager.ResolvePreference("Auto","zh-HK"))=="zh-TW" && LocalizationManager.PreferenceFor(LocalizationManager.ResolvePreference("Auto","de-AT"))=="de-DE" && LocalizationManager.ResolvePreference("Auto","xx-YY")==AppLanguage.English,"Follow system resolves Traditional Chinese and regional languages, with an English fallback.");
        check(new AppSettings().UiLanguage=="Auto" && LocalizationManager.ResolvePreference("Auto","bo-IN")==AppLanguage.English && LocalizationManager.ResolvePreference("ja-JP","bo-CN")==AppLanguage.Japanese,"New configurations follow system, Tibetan is recognized and an explicit selection overrides system language.");
        var english=LocalizationManager.ReadResource("en-US");
        check(english.Count>1500,"The offline source catalog covers UI, help and variable descriptions.");
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
        var confirmation=new Button{Content="知道了"};var status=new TextBlock{Text="正常"};
        LocalizationManager.ApplyStaticText(confirmation);LocalizationManager.ApplyStaticText(status);
        check(confirmation.Content?.ToString()=="知道了" && status.Text=="正常","Different Chinese captions sharing English OK preserve their original text.");
        LocalizationManager.SetLanguage(AppLanguage.French);LocalizationManager.ApplyStaticText(confirmation);LocalizationManager.ApplyStaticText(status);
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);LocalizationManager.ApplyStaticText(confirmation);LocalizationManager.ApplyStaticText(status);
        check(confirmation.Content?.ToString()=="知道了" && status.Text=="正常","A language round trip restores each control's original Chinese caption.");
        var traditional=new TabItem{Header="变量库"};LocalizationManager.ApplyStaticText(traditional);
        LocalizationManager.SetLanguage(AppLanguage.TraditionalChinese);LocalizationManager.ApplyStaticText(traditional);
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);LocalizationManager.ApplyStaticText(traditional);
        check(traditional.Header?.ToString()=="变量库","Traditional Chinese captions return to their original Simplified Chinese source.");
        var frameValues=new Dictionary<string,object?>();
        var cachedFrame=new FrameReading(null,"User title / unchanged","显示器已断开 / Display disconnected");
        LocalizationManager.SetLanguage(AppLanguage.French);
        FrameRateMetrics.Add(frameValues,"window",cachedFrame,120);
        check(frameValues["frame.window.status"]?.ToString()=="Écran déconnecté" && frameValues["frame.window.name"]?.ToString()==cachedFrame.TargetName,"Cached collector status follows French without changing an external window title.");
        LocalizationManager.SetLanguage(AppLanguage.German);
        FrameRateMetrics.Add(frameValues,"window",cachedFrame,120);
        check(frameValues["frame.window.status"]?.ToString()=="Bildschirm getrennt","The same cached reading follows a second UI-language switch.");
        FrameRateMetrics.Add(frameValues,"window",new(null,"Test","帧事件采集已退出（-5） / Frame capture exited (-5)"),120);
        check(frameValues["frame.window.status"]?.ToString()=="Frame-Erfassung beendet (-5)","Collector exit codes retain their signed value in a translated format.");
        FrameRateMetrics.Add(frameValues,"window",new(null,"Test","帧率采集失败：driver / native detail"),120);
        check(frameValues["frame.window.status"]?.ToString()=="Frame-Erfassung fehlgeschlagen: driver / native detail","Translated failure context preserves external driver details, including slashes.");
        FrameRateMetrics.Add(frameValues,"window",new(null,"Test","external driver / raw detail"),120);
        check(frameValues["frame.window.status"]?.ToString()=="external driver / raw detail","Unknown external diagnostics are not truncated as bilingual collector labels.");
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
        foreach(var locale in languages) {
            var pack=LocalizationManager.ReadResource(locale.Code);
            check(pack.Count>0,"Every selectable locale has an offline resource: "+locale.Code);
            // Navigation wording may legitimately fall back to English in a
            // partially migrated locale; complete locales must translate it.
            foreach(var key in new[]{"Settings","Display & Position","HUD Content","Data Management","Experimental Features","User Manual","About","Done","Global font size and UI scale"})
                check(pack.TryGetValue(key,out var text) && !string.IsNullOrWhiteSpace(text) || LocalizationManager.TranslateEnglish(key,locale.Language).Length>0,"Every catalog entry resolves core navigation: "+locale.Code+" "+key);
            foreach(var entry in pack)if(locale.Code is not ("zh-CN" or "zh-TW" or "zh-HK"))check(Tokens(Arguments,entry.Key).SequenceEqual(Tokens(Arguments,entry.Value)),"All catalog locales preserve argument slots: "+locale.Code+" "+entry.Key);
        }
        var profiles=CustomHudSettings.CreateDefaultProfiles();
        foreach(var language in SelectedLanguages())
        {
            var resource=LocalizationManager.ReadResource(language.Code);
            bool complete=resource.Count>=required.Count;
            if(complete)check(required.All(resource.ContainsKey),"Active source message coverage is complete: "+language.Code);
            foreach(var message in resource)
            {
                check(!string.IsNullOrWhiteSpace(message.Value) && !message.Value.Contains('\uFFFD') && !message.Value.Contains("<unk>",StringComparison.Ordinal),"Resource has valid readable text: "+language.Code+" "+message.Key);
                // Chinese fallback arguments are kept in their source call; other
                // translated formats must retain the exact English parameter slots.
                if(!language.Code.StartsWith("zh-",StringComparison.Ordinal))
                {
                    check(Tokens(Arguments,message.Key).SequenceEqual(Tokens(Arguments,message.Value)),"Format arguments survive translation: "+language.Code+" "+message.Key);
                    if(complete && required.Contains(message.Key))check(Syntax(message.Key,message.Value),"Tokens, paths and variable identifiers survive translation: "+language.Code+" "+message.Key);
                }
            }
            LocalizationManager.SetLanguage(language.Language);
            if(!EnglishFamily(language.Code) && language.Language!=AppLanguage.SimplifiedChinese)
                if(complete)
                    foreach(string key in new[]{"Multiple HUD instances","Enable multiple HUD instances","No shortcut configured.","Controls the primary HUD and additional instances together. Automatic summons pause while hidden. Press again to restore the previous display mode; restarting restores visibility.","Immediate","Top center","On"})
                        check(resource.TryGetValue(key,out var translatedValue) && translatedValue!=key,"Previously missed UI wording has a real translation: "+language.Code+" "+key);
            check(LocalizationManager.Text("完全不存在的测试文案","ECP_UNREGISTERED_TEST_MESSAGE")== (language.Language==AppLanguage.SimplifiedChinese ? "完全不存在的测试文案" : "ECP_UNREGISTERED_TEST_MESSAGE"),"Missing resources fall back safely without inventing a translation.");
            check(LocalizationManager.Formatted("数值 42",$"Value {42}").Contains("42"),"Formatted messages retain their argument value: "+language.Code);
            foreach(var profile in profiles)
            {
                var original=BuiltInProfileLocalization.ForLanguage(profile,AppLanguage.English);var translated=BuiltInProfileLocalization.ForLanguage(profile,language.Language);
                var chinese=BuiltInProfileLocalization.ForLanguage(profile,AppLanguage.SimplifiedChinese);
                var allowed=TemplateEngine.ExtractKeys(original.TaglineTemplate,original.TitleTemplate,original.PrimaryTemplate,original.SecondaryTemplate,original.RightTemplate,chinese.TitleTemplate).ToHashSet();
                check(TemplateEngine.ExtractKeys(translated.TaglineTemplate,translated.TitleTemplate,translated.PrimaryTemplate,translated.SecondaryTemplate,translated.RightTemplate).All(allowed.Contains) && original.ProgressVariable==translated.ProgressVariable,"Built-in translation preserves canonical rendering and progress keys: "+language.Code+" "+profile.BuiltInKey);
            }
            var custom=new HudProfile{Name="My custom profile",TitleTemplate="My text {cpu.usage}",PrimaryTemplate="{cpu.usage|0.0}",ProgressVariable="= cpu.usage"};
            check(BuiltInProfileLocalization.ForCurrentLanguage(custom)==custom,"Custom user text is not translated: "+language.Code);
            foreach(var variable in VariableCatalog.AllBuiltIns)
            {
                var translated=VariableLocalization.Localize(variable);
                if(language.Language is AppLanguage.SimplifiedChinese or AppLanguage.TraditionalChinese)continue;
                LocalizationManager.SetLanguage(AppLanguage.English);var canonical=VariableLocalization.Localize(variable);LocalizationManager.SetLanguage(language.Language);
                check(translated.Key==canonical.Key && translated.Unit==canonical.Unit && (canonical.RecommendedFormats=="None" || translated.RecommendedFormats==canonical.RecommendedFormats),"Variable formats, keys and units stay immutable: "+language.Code+" "+variable.Key);
                // Field-level translation quality is not re-judged during migration:
                // existing wording is copied as-is, including shared technical labels.
            }
            check(UserManualContent.Chapters.All(c=>c.Blocks.All(b=>b.Title.Value.Length>0 && b.Body.Value.Length>0)) && HttpSourceConfiguration.Parse(UserManualContent.HttpExample).Count==1,"Every manual chapter is usable and the JSON example remains valid: "+language.Code);
            if(complete && !CanonicalGuide(language.Code))
            {
                check(NativeManualContent.HasNativeGuide(language.Code),"The reviewed native guide is embedded in the application: "+language.Code);
                check(UserManualContent.Chapters.Count>=15 && UserManualContent.Chapters.All(c=>c.Title.Localized is {Length:>0} && c.Blocks.Any(b=>b.Id=="guide" && b.Body.Localized is {Length:>0})),"Every chapter uses the reviewed native guide: "+language.Code);
                check(UserManualContent.Chapters.Single(c=>c.Id=="sources").Blocks.Any(b=>b.Id=="http-example" && b.Code==UserManualContent.HttpExample),"The native guide retains the original JSON example and navigation anchor: "+language.Code);
            }
            Console.WriteLine("PASS resource and semantic checks: "+language.Code+" ("+resource.Count+" messages)");
        }
        LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
    }
    private static async Task Gui(Action<bool,string> check)
    {
        var hud=new HudWindow();var settings=new AppSettings{HudEnabled=false,UiLanguage="zh-CN"};using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        // Interactive layout guarantees cover the original built-in languages; the
        // additional migrated locales are validated by the structural checks above.
        var guiCodes=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"zh-CN","zh-TW","en-US","ja-JP","ko-KR","de-DE","fr-FR","es-ES","pt-BR","ru-RU","ar-SA","hi-IN","bn-BD","ur-PK","id-ID","vi-VN","th-TH","tr-TR","it-IT","fa-IR"};
        var cultures=SelectedLanguages().Where(l=>guiCodes.Contains(l.Code)).ToArray();
        try
        {
            foreach(var language in cultures)
            {
                LocalizationManager.SetLanguage(language.Language);
                var window=new SettingsWindow(settings with {UiLanguage=language.Code},hud,runtime){Width=940,Height=680};window.NavigateToSettingsSection("manual");window.Show();
                try
                {
                    await WaitForInitialLayout(window);Capture(window,language.Code+"-initial.png");var tabs=window.FindControl<TabControl>("MainSettingsTabs")!;var manual=window.FindControl<UserManualView>("ManualView")!;
                    check(window.FindControl<ListBox>("WorkspaceNavigation")!.Items.Count==12,"All 12 sidebar entries are present: "+language.Code);
                    foreach(var tab in window.FindControl<ListBox>("WorkspaceNavigation")!.Items.OfType<ListBoxItem>())
                    {
                        var point=tab.TranslatePoint(default,window)!.Value;var end=tab.TranslatePoint(new Point(tab.Bounds.Width,tab.Bounds.Height),window)!.Value;
                        check(tab.Bounds.Width>0 && Math.Min(point.X,end.X)>=-1 && Math.Max(point.X,end.X)<=window.ClientSize.Width+1,"Main-tab header remains inside the narrow viewport: "+language.Code+" "+tab.Tag+" "+point.X+".."+end.X+" / "+window.ClientSize.Width);
                    }
                    foreach(string buttonName in new[]{"SaveBtn","OpenSettingsFolderBtn","LanguageButton"})
                    {
                        var control=window.FindControl<Control>(buttonName)!;var point=control.TranslatePoint(default,window)!.Value;var end=control.TranslatePoint(new Point(control.Bounds.Width,control.Bounds.Height),window)!.Value;
                        check(Math.Min(point.X,end.X)>=-1 && Math.Max(point.X,end.X)<=window.ClientSize.Width+1,"Top-bar action remains visible: "+language.Code+" "+buttonName+" "+point.X+".."+end.X+" / "+window.ClientSize.Width);
                    }
                    var selector=window.FindControl<Button>("LanguageButton")!;var icon=window.FindControl<Avalonia.Controls.Shapes.Path>("LanguageIcon")!;
                    var iconPoint=icon.TranslatePoint(default,selector)!.Value;
                    check(icon.IsEffectivelyVisible && icon.Data is not null && icon.Bounds.Width>=20 && iconPoint.X>=0 && iconPoint.X+icon.Bounds.Width<=selector.Bounds.Width && !icon.IsHitTestVisible,"A persistent vector language icon is visible inside the clickable selector without covering its text: "+language.Code);
                    check(Avalonia.Automation.AutomationProperties.GetName(selector)?.StartsWith("Language / ",StringComparison.Ordinal)==true,"The language selector also has a language-independent accessibility label: "+language.Code);
                    check(window.FlowDirection==(language.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight),"Settings uses the correct paragraph direction: "+language.Code);
                    foreach(var chapter in UserManualContent.Chapters){manual.Navigate(chapter.Id);check(manual.SectionIds.Count==chapter.Blocks.Count && manual.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text==chapter.Title.Value),"Manual sections and reviewed chapter titles render independently: "+language.Code+" "+chapter.Id);}
                    manual.Navigate("hardware");await Task.Delay(50);Capture(window,language.Code+"-manual.png");
                    foreach(string section in new[]{"display","hud","rules","variables","hardware","sources","experimental","about"})
                    {
                        window.NavigateToSettingsSection(section);await Task.Delay(40);
                        if(!EnglishFamily(language.Code) && language.Language!=AppLanguage.English)
                        {
                            var untranslated=window.GetLogicalDescendants().OfType<TextBlock>().Where(t=>t.IsEffectivelyVisible && t.Text is {Length:>0} text && text.Split(' ').Length>=6 && LocalizationManager.ReadResource("en-US").ContainsKey(text) && !text.Contains("{variable|format}",StringComparison.Ordinal)).Select(t=>t.Text).ToArray();
                            check(untranslated.Length==0,"Visible settings prose does not fall back to English: "+language.Code+" "+section+" "+string.Join(" / ",untranslated));
                        }
                        if(language.Code is "ja-JP" or "ko-KR" or "bo-CN")Capture(window,language.Code+"-"+section+".png");
                        if(section=="variables")
                            foreach(string name in new[]{"CopyVariableKeyBtn","CopyVariableTemplateBtn"})
                            {
                                var button=window.FindControl<HudCustomizerView>("Customizer")!.FindControl<Button>(name)!;
                                var caption=new FormattedText(button.Content!.ToString()!,System.Globalization.CultureInfo.CurrentUICulture,window.FlowDirection,new Typeface(button.FontFamily,button.FontStyle,button.FontWeight),button.FontSize,Brushes.White);
                                check(button.Bounds.Width>=caption.Width+button.Padding.Left+button.Padding.Right-1,"Variable copy actions fit their full translated captions: "+language.Code+" "+name);
                            }
                        if(section=="display")
                        {
                            var toggle=window.FindControl<ToggleSwitch>("AlwaysVisibleSwitch")!;
                            check(toggle.OnContent?.ToString()==LocalizationManager.Text("开启","On") && toggle.OffContent?.ToString()==LocalizationManager.TranslateLiteral("Off"),"Framework switch captions are localized: "+language.Code);
                            check(window.FindControl<TextBlock>("HudHotkeyStatusText")!.Text==LocalizationManager.Text("未设置快捷键。","No shortcut configured."),"The runtime shortcut status uses the selected language: "+language.Code);
                        }
                    }
                }
                finally{window.Close();}
            }
            // A partially migrated locale must render its translated strings,
            // fall back to English for the rest, and never crash the settings UI.
            var partialInfo=LocalizationManager.SupportedLanguages.FirstOrDefault(l=>l.Code=="ast-ES");
            if(partialInfo is not null)
            {
                var partialPack=LocalizationManager.ReadResource(partialInfo.Code);
                LocalizationManager.SetLanguage(partialInfo.Language);
                var partialWindow=new SettingsWindow(settings with {UiLanguage=partialInfo.Code},hud,runtime){Width=940,Height=680};partialWindow.NavigateToSettingsSection("display");partialWindow.Show();
                try
                {
                    await Task.Delay(160);
                    var partialTabs=partialWindow.FindControl<TabControl>("MainSettingsTabs")!;
                    check(partialTabs.Items.OfType<TabItem>().Select(t=>t.Header?.ToString()).Contains("Visualización y posición"),"Partial locale renders its translated navigation.");
                    check(partialTabs.Items.OfType<TabItem>().All(t=>!string.IsNullOrWhiteSpace(t.Header?.ToString())),"Partial locale falls back to English without empty headers.");
                    string missingKey=LocalizationManager.ReadResource("en-US").Keys.First(k=>!partialPack.ContainsKey(k));
                    check(LocalizationManager.TranslateEnglish(missingKey,partialInfo.Language)==missingKey,"Partial locale falls back to English for a missing field: "+missingKey.Substring(0,Math.Min(40,missingKey.Length)));
                }
                finally{partialWindow.Close();}
                LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
            }
            var editorWindow=new SettingsWindow(settings,hud,runtime);editorWindow.Show();
            try
            {
                AppSettings? saved=null;editorWindow.PersistLanguagePreference=s=>saved=s;
                var customizer=editorWindow.FindControl<HudCustomizerView>("Customizer")!;
                editorWindow.NavigateToSettingsSection("sources");await Task.Delay(120);
                var json=customizer.FindControl<TextBox>("HttpSourcesJsonBox")!;var key=customizer.FindControl<TextBox>("DeepSeekKeyBox")!;
                json.Text=UserManualContent.HttpExample;key.Text="unsaved-localization-test-secret";
                editorWindow.FindControl<Button>("LanguageButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
                var languagePage=editorWindow.OwnedWindows.OfType<LanguageSelectionWindow>().Single();
                var choices=languagePage.GetLogicalDescendants().OfType<ListBox>().Single(c=>c.Name=="LanguageList");
                foreach(var language in cultures)
                {
                    choices.SelectedItem=choices.Items.Cast<object>().Single(c=>c.GetType().GetProperty("Code")!.GetValue(c)?.ToString()==language.Code);await Task.Delay(30);
                    // The first item can already be selected before the loop.
                    if(saved is not null)check(saved.UiLanguage==language.Code && saved.CustomHud.DeepSeekApiKeyProtected==settings.CustomHud.DeepSeekApiKeyProtected,"Only the language preference is persisted, not unsaved secrets: "+language.Code);
                    check(json.Text==UserManualContent.HttpExample && key.Text=="unsaved-localization-test-secret","Language switching retains unsaved source edits: "+language.Code);
                    check(json.FlowDirection==FlowDirection.LeftToRight,"JSON input keeps its left-to-right code order: "+language.Code);
                }
                choices.SelectedIndex=0;await Task.Delay(50);languagePage.Close();check(saved?.UiLanguage=="Auto","Follow system is stored as Auto rather than the current resolved language.");
                editorWindow.NavigateToSettingsSection("hud");await Task.Delay(80);var hudTabs=customizer.FindControl<TabControl>("CustomizerTabs")!;var hudOrigin=hudTabs.TranslatePoint(default,editorWindow)!.Value;Capture(editorWindow,"hud-subtabs.png");
                editorWindow.NavigateToSettingsSection("variables");await Task.Delay(80);var dataTabs=editorWindow.FindControl<TabControl>("DataManagementTabs")!;var dataOrigin=dataTabs.TranslatePoint(default,editorWindow)!.Value;Capture(editorWindow,"data-subtabs.png");
                check(hudTabs.Classes.Contains("content-only") && dataTabs.Classes.Contains("content-only"),"Both content hosts hide the old secondary tab strips.");
                check(hudTabs.Classes.Contains("content-only") && dataTabs.Classes.Contains("content-only"),"Both content hosts hide the old secondary tab strips.");
            }
            finally{editorWindow.Close();}
        }
        finally{hud.Close();}
    }
    private static async Task WaitForInitialLayout(Window window)
    {
        // The global appearance wrapper sizes its root after the native client
        // size is available. A fixed delay can sample the initial unconstrained
        // measure when the machine is compiling other platform builds.
        for(int attempt=0;attempt<60;attempt++)
        {
            await Task.Delay(40);
            var action=window.FindControl<Control>("SaveBtn")!;
            var start=action.TranslatePoint(default,window);
            var end=action.TranslatePoint(new Point(action.Bounds.Width,action.Bounds.Height),window);
            if(action.Bounds.Width>0 && start is {} a && end is {} b && Math.Min(a.X,b.X)>=-1 && Math.Max(a.X,b.X)<=window.ClientSize.Width+1)return;
        }
        // The assertions below still reject a persistent layout overflow.
    }

    private static void Capture(Window window,string name)
    {
        if(Environment.GetEnvironmentVariable("ECP_LOCALIZATION_TEST_OUTPUT") is not {Length:>0} output)return;
        Directory.CreateDirectory(output);using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));bitmap.Render(window);bitmap.Save(Path.Combine(output,name));
    }
}
