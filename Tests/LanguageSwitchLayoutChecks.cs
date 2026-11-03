using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class LanguageSwitchLayoutChecks
{
    private static readonly string Folder=Environment.GetEnvironmentVariable("ECP_LAYOUT_OUTPUT") ?? Path.Combine(Path.GetTempPath(),"ecp-language-layout");
    internal static void Run(Action<bool,string> check)
    {
        using var end=new CancellationTokenSource(TimeSpan.FromMinutes(8));Exception? error=null;bool complete=false;
        Dispatcher.UIThread.Post(async()=>{try{await Exercise(check);complete=true;}catch(Exception ex){error=ex;}finally{end.Cancel();}});
        Dispatcher.UIThread.MainLoop(end.Token);
        if(error is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        if(!complete)throw new TimeoutException("Language switch layout checks timed out.");
    }
    private static async Task Exercise(Action<bool,string> check)
    {
        Directory.CreateDirectory(Folder);
        check(GlobalAppearance.NormalizeFontScale(2)==1.25 && GlobalAppearance.NormalizeUiScale(2)==1.5,"Legacy 200% scale values are clamped to the new limits.");
        check(GlobalAppearance.NormalizeFontScale(.1)==.8 && GlobalAppearance.NormalizeUiScale(.1)==.8,"Fonts and UI remain readable at the lower bound.");
        check(GlobalAppearance.NormalizeFontScale(double.NaN)==1 && GlobalAppearance.NormalizeUiScale(double.PositiveInfinity)==1,"Invalid scales recover to the default.");
        var settings=new AppSettings{HudEnabled=false,UiLanguage="zh-CN"};
        LocalizationManager.Initialize(settings.UiLanguage);
        var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        var main=new SettingsWindow(settings,hud,runtime){Width=940,Height=680};main.PersistLanguagePreference=_=>{};main.Show();
        Console.WriteLine("Opened settings window.");
        var snapshots=new List<object>();var clipping=new List<object>();
        try
        {
            await Task.Delay(400);
            main.FindControl<Button>("LanguageButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(160);
            var picker=main.OwnedWindows.OfType<LanguageSelectionWindow>().Single();
            var choices=picker.GetLogicalDescendants().OfType<ListBox>().Single(c=>c.Name=="LanguageList");
            try
            {
                foreach(string code in new[]{"en-US","zh-CN","en-US"}.Concat(LocalizationManager.SupportedLanguages.Select(l=>l.Code)).Append("zh-CN"))
                {
                    choices.SelectedItem=choices.Items.Cast<object>().Single(c=>c.GetType().GetProperty("Code")!.GetValue(c)?.ToString()==code);
                    await Task.Delay(350);
                    Console.WriteLine("Checking switched language: "+code);
                    main.NavigateToSettingsSection("experimental");await Task.Delay(80);
                    if(code is "en-US" or "zh-CN" or "ar-SA")Capture(main,$"{snapshots.Count:D2}-{code}.png");
                    foreach(var tab in main.FindControl<ListBox>("WorkspaceNavigation")!.Items.OfType<ListBoxItem>())
                    {
                        var text=((Grid)tab.Content!).Children.OfType<TextBlock>().Single();
                        var formatted=new FormattedText(text.Text!,CultureInfo.CurrentCulture,text.FlowDirection,new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,Brushes.White){MaxTextWidth=Math.Max(1,text.Bounds.Width)};
                        // Use the actual renderer layout; FormattedText can choose a different wrap at fractional widths.
                        check(text.Bounds.Width>0 && text.Bounds.Height>=text.TextLayout.Height-1,"The sidebar label fits or wraps completely after switching to "+code+": "+text.Text);
                    }
                    foreach(string page in new[]{"display","hud","ring","cycle","rules","variables","hardware","sources","quick","experimental","manual","about"})
                    {
                        main.NavigateToSettingsSection(page);await Task.Delay(70);
                        Scan(main,code,page,clipping);
                        VerifyCaptions(main,code+"/"+page,check);
                        if(code is "en-US" or "de-DE" or "ar-SA")Capture(main,code+"-"+page+".png");
                    }
                    main.NavigateToSettingsSection("manual");
                    var manual=main.FindControl<UserManualView>("ManualView")!;
                    foreach(var chapter in UserManualContent.Chapters)
                    {
                        manual.Navigate(chapter.Id);await Task.Delay(35);Scan(main,code,"manual/"+chapter.Id,clipping);
                    }
                    Scan(picker,code,"language-picker",clipping);
                    await CheckTray(code,"tray",clipping);
                    File.WriteAllText(Path.Combine(Folder,"clipping.json"),JsonSerializer.Serialize(clipping,new JsonSerializerOptions{WriteIndented=true}));
                }
                foreach(var scales in new[]{(.8, .8),(1.25,1.5),(2d,2d),(1d,1d)})
                {
                    foreach(string code in new[]{"en-US","zh-CN","de-DE","ar-SA","hi-IN"})
                    {
                        choices.SelectedItem=choices.Items.Cast<object>().Single(c=>c.GetType().GetProperty("Code")!.GetValue(c)?.ToString()==code);
                        GlobalAppearance.Apply(main,scales.Item1,scales.Item2);GlobalAppearance.Apply(picker,scales.Item1,scales.Item2);await Task.Delay(220);
                        foreach(string page in new[]{"display","hud","ring","cycle","rules","variables","hardware","sources","quick","experimental","manual","about"})
                        {
                            main.NavigateToSettingsSection(page);await Task.Delay(70);
                            Scan(main,code,$"{page}@{scales.Item1}/{scales.Item2}",clipping);
                            VerifyCaptions(main,code+"/scaled/"+page,check);
                        }
                        Scan(picker,code,$"language-picker@{scales.Item1}/{scales.Item2}",clipping);
                        await CheckTray(code,$"tray@{scales.Item1}/{scales.Item2}",clipping);
                        main.NavigateToSettingsSection("experimental");await Task.Delay(70);Capture(main,$"scaled-{code}-{scales.Item1}.png");
                    }
                }
                await CheckWarningLayouts(check,clipping);
                File.WriteAllText(Path.Combine(Folder,"clipping.json"),JsonSerializer.Serialize(clipping,new JsonSerializerOptions{WriteIndented=true}));
                check(clipping.Count==0,"All settings pages, manual chapters, menus, dialogs and scale cases fit their text. See clipping.json for any failure.");
            }
            finally{picker.Close();}
        }
        finally{main.Close();hud.Close();LocalizationManager.Initialize("zh-CN");}
    }
    private static async Task CheckTray(string code,string page,List<object> clipping)
    {
#if !MACOS
        var tray=new TrayMenuWindow();tray.Show();await Task.Delay(80);Scan(tray,code,page,clipping);tray.Close();
#else
        await Task.CompletedTask; // macOS uses the operating system's native menu.
#endif
    }
    private static async Task CheckWarningLayouts(Action<bool,string> check,List<object> clipping)
    {
        const string scope="ui-language-layout-audit";
        var profile=new HudProfile{Id=scope,Name="HUD",PrimaryTemplate="{layout.warning.value}",TitleTemplate="",TaglineTemplate="",SecondaryTemplate="",RightTemplate="",RightSuffix="",ProgressVariable="=50"};
        var values=new Dictionary<string,object?>();
        Window? dialog=null;
        try
        {
            ProfileDataWarnings.Check(profile,values,scope);await Task.Delay(5100);ProfileDataWarnings.Check(profile,values,scope);await Task.Delay(750);
            dialog=(Window?)typeof(ProfileDataWarnings).GetField("_dialog",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null);
            check(dialog is {IsVisible:true},"The actual warning dialog opens for layout review without changing warning preferences.");
            foreach(var language in LocalizationManager.SupportedLanguages)
            {
                LocalizationManager.SetPreference(language.Code);GlobalAppearance.Apply(dialog!,1.25,1.5);await Task.Delay(100);
                Scan(dialog!,language.Code,"data-warning@1.25/1.5",clipping);
                if(language.Code is "en-US" or "ar-SA")Capture(dialog!,language.Code+"-data-warning.png");
            }
        }
        finally{dialog?.Close();ProfileDataWarnings.Forget(scope);}
    }
    private static void Scan(Window window,string language,string page,List<object> clipping)
    {
        foreach(var text in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if(!text.IsEffectivelyVisible || text.Bounds.Width<=0 || string.IsNullOrWhiteSpace(text.Text) || text.TextTrimming!=TextTrimming.None)continue;
            if(text is SelectableTextBlock || text.GetVisualAncestors().Any(a=>a is TextBox or ComboBox or NumericUpDown))continue;
            double needed=new FormattedText(text.Text!,CultureInfo.CurrentCulture,text.FlowDirection,new Typeface(text.FontFamily,text.FontStyle,text.FontWeight,text.FontStretch),text.FontSize,Brushes.White).Width;
            if(text.TextWrapping==TextWrapping.NoWrap && needed>text.Bounds.Width+1 || text.TextLayout.Height>text.Bounds.Height+1)
                clipping.Add(new{language,page,name=text.Name,text=text.Text,width=text.Bounds.Width,needed,height=text.Bounds.Height,neededHeight=text.TextLayout.Height,ancestors=text.GetVisualAncestors().Take(6).Select(a=>a.GetType().Name).ToArray()});
        }
    }
    private static void VerifyCaptions(Window window,string context,Action<bool,string> check)
    {
        foreach(var presenter in window.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
            if(presenter.IsEffectivelyVisible && presenter.Content is string caption && presenter.Child is TextBlock text)
                check(text.Text==caption,"A generated caption still follows its owner: "+context+" / "+caption);
    }
    private static void Capture(Window window,string name)
    {
        using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));bitmap.Render(window);bitmap.Save(Path.Combine(Folder,name));
    }
}
