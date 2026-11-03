using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class WindowFrameChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var end=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Exception? error=null;bool complete=false;
        Dispatcher.UIThread.Post(async()=>{try{await Exercise(check);complete=true;}catch(Exception ex){error=ex;}finally{end.Cancel();}});
        Dispatcher.UIThread.MainLoop(end.Token);
        if(error is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        if(!complete)throw new TimeoutException("Window frame checks timed out.");
    }
    private static async Task Exercise(Action<bool,string> check)
    {
        var settings=new AppSettings{HudEnabled=false,UiLanguage="zh-TW"};
        LocalizationManager.Initialize(settings.UiLanguage);
        var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        var main=new SettingsWindow(settings,hud,runtime);
        main.PersistLanguagePreference=_=>{};
        main.Show();
        try
        {
            await Task.Delay(400);
            var viewport=(ScrollViewer)main.Content!;
            var transform=(LayoutTransformControl)viewport.Content!;
            var root=transform.Child!;
            async Task EdgeToEdge(double ui)
            {
                GlobalAppearance.Apply(main,1,ui);
                await Task.Delay(250);
                var start=root.TranslatePoint(default,main)!.Value;
                var finish=root.TranslatePoint(new Point(root.Bounds.Width,root.Bounds.Height),main)!.Value;
                check(Math.Abs(start.X)<1 && Math.Abs(start.Y)<1,$"Settings content starts at the client origin at UI scale {ui}: {start}.");
                check(Math.Abs(finish.X-main.ClientSize.Width)<1 && Math.Abs(finish.Y-main.ClientSize.Height)<1,$"Settings content fills the client area at UI scale {ui}: {finish}, {main.ClientSize}.");
                check(viewport.Extent.Width<=viewport.Viewport.Width+1 && viewport.Extent.Height<=viewport.Viewport.Height+1,"No unnecessary outer scrollbars at a fitting scale.");
            }
            await EdgeToEdge(1);
            Capture(main,"traditional-edge-to-edge.png");
            await EdgeToEdge(.8);
            GlobalAppearance.Apply(main,1.25,1.5);await Task.Delay(250);
            check(viewport.Extent.Width>viewport.Viewport.Width,"Large UI remains horizontally reachable through scrolling.");
            await EdgeToEdge(1);
            var header=((Grid)root).Children.OfType<Border>().First();
            check(header.Padding==new Thickness(24,16),"Header's authored inner spacing is preserved.");
            main.FindControl<Button>("LanguageButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(160);
            var selector=main.OwnedWindows.OfType<LanguageSelectionWindow>().Single();
            try
            {
                var list=selector.GetLogicalDescendants().OfType<ListBox>().Single(c=>c.Name=="LanguageList");
                check(list.Items.Count==21,"Language picker retains Auto and 20 languages.");
                foreach(var choice in list.Items.Cast<object>().Skip(1))
                {
                    string code=choice.GetType().GetProperty("Code")!.GetValue(choice)!.ToString()!;
                    string label=choice.GetType().GetProperty("Label")!.GetValue(choice)!.ToString()!;
                    check(label==LocalizationManager.SupportedLanguages.Single(l=>l.Code==code).NativeName,"Actual language picker row has no regional suffix: "+code);
                }
                var search=selector.GetLogicalDescendants().OfType<TextBox>().Single(c=>c.Name=="LanguageSearch");
                check(search.Watermark?.ToString()=="搜尋語言","Traditional Chinese search caption has no region wording.");
                Capture(selector,"global-language-names.png");
            }
            finally{selector.Close();}
        }
        finally{main.Close();hud.Close();LocalizationManager.Initialize("zh-CN");}
    }
    private static void Capture(Window window,string name)
    {
        if(Environment.GetEnvironmentVariable("ECP_FRAME_OUTPUT") is not {Length:>0} folder)return;
        Directory.CreateDirectory(folder);
        using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));bitmap.Render(window);bitmap.Save(Path.Combine(folder,name));
    }
}
