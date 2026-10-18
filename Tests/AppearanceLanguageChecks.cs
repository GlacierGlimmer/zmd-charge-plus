using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Views;

internal static class AppearanceLanguageChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var end=new CancellationTokenSource(TimeSpan.FromSeconds(70));Exception? error=null;bool complete=false;
        Dispatcher.UIThread.Post(async()=> {try {await Exercise(check);complete=true;}catch(Exception ex){error=ex;}finally{end.Cancel();}});
        Dispatcher.UIThread.MainLoop(end.Token);
        if(error is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        if(!complete)throw new TimeoutException("Language/appearance checks timed out.");
    }
    private static T Named<T>(Control root,string name) where T:Control =>root.GetLogicalDescendants().OfType<T>().Single(c=>c.Name==name);
    private static async Task Exercise(Action<bool,string> check)
    {
        check(LocalizationManager.SupportedLanguages.Count==146 && LocalizationManager.SupportedLanguages.Count(l=>l.MinecraftCode is not null)==143,"The offline catalog includes every verified Java language and preserves the three additional ECP languages.");
        check(LocalizationManager.NormalizePreference("en_pt")=="en-x-pirate" && LocalizationManager.ResolvePreference("Auto","zh-Hant-HK")==LocalizationManager.SupportedLanguages.Single(l=>l.Code=="zh-HK").Language,"Minecraft identifiers and system script/region tags resolve correctly.");
        check(LocalizationManager.PreferenceFor(LocalizationManager.ResolvePreference("Auto","nb-NO"))=="no-NO" && LocalizationManager.PreferenceFor(LocalizationManager.ResolvePreference("Auto","sr-Cyrl-RS"))=="sr-Cyrl","Norwegian and Cyrillic Serbian do not select the wrong language.");
        LocalizationManager.Initialize("zh-CN");
        var settings=new AppSettings {HudEnabled=false,UiLanguage="zh-CN",MultiHudEnabled=true,HudInstances=new(){new() {Id="appearance-child",Name="Child HUD",Settings=new AppSettings{HudEnabled=false}}}};
        var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        var main=new SettingsWindow(settings,hud,runtime);AppSettings? persisted=null;
        main.PersistLanguagePreference=s=>persisted=s;
        main.Show();
        try
        {
            await Task.Delay(200);
            var customizer=main.FindControl<HudCustomizerView>("Customizer")!;
            customizer.FindControl<TextBox>("HttpSourcesJsonBox")!.Text="[] // pending editor text";
            customizer.FindControl<TextBox>("DeepSeekKeyBox")!.Text="draft-key-do-not-persist";
            var button=main.FindControl<Button>("LanguageButton") ?? throw new Exception("The language entry button is missing.");
            check(main.FindControl<TextBlock>("LanguageButtonLabel")!.Text=="Language / 语言","The universal language entry is a button with the requested bilingual caption.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(120);
            var selector=main.OwnedWindows.OfType<LanguageSelectionWindow>().Single();
            var list=Named<ListBox>(selector,"LanguageList");var search=Named<TextBox>(selector,"LanguageSearch");
            check(list.Items.Count==147,"The language page shows Auto plus every native language/region entry.");
            var firstLanguage=list.GetVisualDescendants().OfType<ListBoxItem>().First(c=>c.IsEffectivelyVisible);
            check(Math.Abs(firstLanguage.Bounds.Height-36)<1 && list.Bounds.Width<=560,"Language entries use the compact 36-pixel row and narrow centered list at default scale.");
            var notice=Named<TextBlock>(selector,"LanguageTranslationNotice");
            check(notice.Text=="（语言翻译不一定100%准确）" && notice.TextWrapping==TextWrapping.Wrap,"The language page has the requested Chinese notice and permits wrapping.");
            check(notice.TranslatePoint(default,selector)!.Value.Y < Named<Button>(selector,"LanguageDoneButton").TranslatePoint(default,selector)!.Value.Y,"The translation notice appears above the footer buttons.");
            Capture(selector,"compact-language-list.png");
            search.Text="ja-JP";await Task.Delay(80);
            check(list.Items.Count==1,"Language search filters by a stable language code. Matches: "+list.Items.Count);
            list.SelectedItem=list.Items[0];await Task.Delay(100);
            check(LocalizationManager.Current==AppLanguage.Japanese && persisted?.UiLanguage=="ja-JP","Choosing a native row applies and persists the language immediately.");
            check(customizer.FindControl<TextBox>("HttpSourcesJsonBox")!.Text=="[] // pending editor text" && customizer.FindControl<TextBox>("DeepSeekKeyBox")!.Text=="draft-key-do-not-persist" && persisted!.CustomHud.DeepSeekApiKeyProtected==settings.CustomHud.DeepSeekApiKeyProtected,"Language selection does not save or overwrite pending source/credential edits.");
            Named<Button>(selector,"GlobalAppearanceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
            var appearance=selector.OwnedWindows.Single();
            Named<NumericUpDown>(appearance,"GlobalFontScaleBox").Value=125;
            Named<NumericUpDown>(appearance,"GlobalUiScaleBox").Value=150;
            Named<Button>(appearance,"ApplyGlobalAppearanceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(350);
            check(persisted is {GlobalFontScale:1.25,GlobalUiScale:1.5} && persisted.CustomHud.DeepSeekApiKeyProtected==settings.CustomHud.DeepSeekApiKeyProtected,"The two independent global scales are saved without pending credentials.");
            foreach(var target in new[]{hud,runtime.GetInstanceHud("appearance-child")!})
            {
                check(Math.Abs(target.FindControl<TextBlock>("PrimaryText")!.FontSize-32.5)<.01,"Global text scaling reaches each HUD instance.");
                var transform=(ScaleTransform)target.FindControl<Grid>("GlobalScale")!.RenderTransform!;
                check(Math.Abs(transform.ScaleX-1.2)<.01,"Global UI scale multiplies each HUD's individual size.");
            }
            double before=main.FindControl<Button>("SaveBtn")!.GetVisualDescendants().OfType<TextBlock>().First().FontSize;
            GlobalAppearance.Apply(main,1.25,1.5);await Task.Delay(60);GlobalAppearance.Apply(main,1.25,1.5);await Task.Delay(60);
            double after=main.FindControl<Button>("SaveBtn")!.GetVisualDescendants().OfType<TextBlock>().First().FontSize;
            check(Math.Abs(before-after)<.01,"Repeated appearance refreshes do not compound font sizes.");
            Capture(selector,"language-page.png");Capture(main,"settings-scaled.png");
            foreach(var code in new[]{"uk-UA","ta-IN","mn-MN","ar-SA"})
            {
                search.Text=code;await Task.Delay(40);list.SelectedItem=list.Items[0];await Task.Delay(100);
                check(LocalizationManager.Preference==code && notice.Text!="(Language translations may not be 100% accurate)","The language notice updates with the selected screenshot locale: "+code);
                check(notice.FlowDirection==(code=="ar-SA" ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight),"The notice uses the selected language's writing direction: "+code);
                var chapters=UserManualContent.Chapters;
                check(chapters.Count==14 && chapters.All(c=>c.Title.Value!=c.Title.En),"Every chapter title uses the native screenshot language: "+code);
                check(chapters.All(c=>c.Blocks.Any(b=>b.Kind=="steps" && b.Body.Localized is {Length:>30})),"Each chapter has authored native instructions rather than English fallback: "+code);
                GlobalAppearance.Apply(selector,1,1);GlobalAppearance.Apply(main,1,1);await Task.Delay(80);
                Capture(selector,code+"-language-notice.png");
                main.FindControl<TabControl>("MainSettingsTabs")!.SelectedItem=main.FindControl<TabItem>("ManualPage");await Task.Delay(90);Capture(main,code+"-manual.png");
                main.FindControl<TabControl>("MainSettingsTabs")!.SelectedItem=main.FindControl<TabItem>("ExperimentalPage");await Task.Delay(90);Capture(main,code+"-experimental.png");
                main.FindControl<TabControl>("MainSettingsTabs")!.SelectedItem=main.FindControl<TabItem>("DisplayPage");await Task.Delay(90);Capture(main,code+"-display.png");
            }
            selector.Close();
        }
        finally {main.Close();hud.Close();LocalizationManager.Initialize("zh-CN");}
    }
    private static void Capture(Window window,string name)
    {
        if(Environment.GetEnvironmentVariable("ECP_APPEARANCE_OUTPUT") is not {Length:>0} output)return;
        Directory.CreateDirectory(output);
        using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));bitmap.Render(window);bitmap.Save(Path.Combine(output,name));
    }
}
