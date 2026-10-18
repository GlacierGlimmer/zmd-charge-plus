using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;

internal static class DataWarningRegressionChecks
{
    private const BindingFlags StaticPrivate=BindingFlags.Static|BindingFlags.NonPublic;
    private static Window? Dialog => (Window?)typeof(ProfileDataWarnings).GetField("_dialog",StaticPrivate)!.GetValue(null);
    private static string Details => ((TextBlock?)typeof(ProfileDataWarnings).GetField("_details",StaticPrivate)!.GetValue(null))?.Text ?? "";
    internal static void Run(Action<bool,string> check)
    {
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(60)); Exception? failure=null; bool completed=false;
        Dispatcher.UIThread.Post(async ()=> { try { await CheckAsync(check);completed=true; } catch(Exception ex) { failure=ex; } finally { finish.Cancel(); } });
        Dispatcher.UIThread.MainLoop(finish.Token);
        if(failure is not null) throw failure;
        if(!completed) throw new TimeoutException("Warning preference checks timed out.");
    }
    private static async Task CheckAsync(Action<bool,string> check)
    {
        string path=Path.Combine(AppContext.BaseDirectory,$"ignored-warning-test-{Guid.NewGuid():N}.json");
        var field=typeof(ProfileDataWarnings).GetField("_preferences",StaticPrivate)!;
        var original=field.GetValue(null);
        var app=Application.Current!;var previousTheme=app.RequestedThemeVariant;
        var theme=new Avalonia.Themes.Fluent.FluentTheme();app.Styles.Add(theme);app.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
        var empty=new Dictionary<string,object?>();
        HudProfile Profile(string id,string template)=>new() { Id=id,Name=id,TaglineTemplate="",TitleTemplate="",PrimaryTemplate=template,SecondaryTemplate="",RightTemplate="",RightSuffix="",ProgressVariable="= 50" };
        var a=Profile("warning-a","{metric.one}");var b=Profile("warning-b","{metric.two}");
        try
        {
            var preferences=new DataWarningPreferences(path);preferences.Ignore(new[] { "metric.old" });preferences.Ignore(new[] { "metric.other" });
            var reloaded=new DataWarningPreferences(path);
            check(reloaded.IsIgnored("METRIC.OLD") && reloaded.IsIgnored("metric.other") && !reloaded.IsIgnored("metric.new"),"Ignored variables persist across reloads, merge and compare case-insensitively.");
            var broken=new DataWarningPreferences(Path.Combine(path,"blocked.json"));bool failed=false;
            try { broken.Ignore(new[] { "metric.failed" }); } catch(IOException) { failed=true; }
            check(failed && !broken.IsIgnored("metric.failed"),"A failed write never pretends that a variable was permanently ignored.");
            field.SetValue(null,reloaded);LocalizationManager.Initialize("zh-CN");
            ProfileDataWarnings.Check(a,empty,"warning-test-a");await Task.Delay(5100);ProfileDataWarnings.Check(a,empty,"warning-test-a");await Task.Delay(700);
            check(Dialog is { IsVisible:true } && Details.Contains("metric.one"),"An unsuppressed variable produces a warning after warmup.");
            var dialog=Dialog!;var checkbox=dialog.GetLogicalDescendants().OfType<CheckBox>().Single(c=>c.Name=="IgnoreCurrentDataWarningCheckBox");
            var close=dialog.GetLogicalDescendants().OfType<Button>().Single(c=>c.Content?.ToString()=="知道了");
            var p=checkbox.TranslatePoint(default,dialog);var q=close.TranslatePoint(default,dialog);
            check(checkbox.Content?.ToString()=="此数据不再提示" && checkbox.IsChecked!=true && p!.Value.X<q!.Value.X,"The exact requested unchecked option sits to the left of OK in the footer.");
            LocalizationManager.SetLanguage(AppLanguage.French);
            check(close.Content?.ToString()=="OK" && Details.Contains("metric.one"),"An open warning follows the selected language while keeping variable identifiers.");
            LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
            check(close.Content?.ToString()=="知道了" && checkbox.Content?.ToString()=="此数据不再提示","The warning restores the requested Chinese captions after a language round trip.");
            if(Environment.GetEnvironmentVariable("ECP_WARNING_AUDIT") is { Length:>0 } audit)
            {
                Directory.CreateDirectory(audit);
                using var bitmap=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(dialog.ClientSize.Width),(int)Math.Ceiling(dialog.ClientSize.Height)));
                bitmap.Render(dialog);bitmap.Save(Path.Combine(audit,"data-warning-checkbox.png"));
            }
            // Arrive after the last rendered body: this variable must not be selected accidentally.
            ProfileDataWarnings.Check(b,empty,"warning-test-b");checkbox.IsChecked=true;await Task.Delay(700);
            check(new DataWarningPreferences(path).IsIgnored("metric.one") && !new DataWarningPreferences(path).IsIgnored("metric.two"),"Only the variables actually displayed when checked are permanently suppressed.");
            ProfileDataWarnings.Check(a,empty,"warning-test-a");ProfileDataWarnings.Check(b,empty,"warning-test-b");await Task.Delay(5100);
            ProfileDataWarnings.Check(b,empty,"warning-test-b");await Task.Delay(700);
            check(Dialog is { IsVisible:true } && Details.Contains("metric.two") && !Details.Contains("metric.one"),"A different variable still opens its own warning after the first is ignored.");
            check(Dialog!.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked!=true,"A subsequent warning starts unchecked.");
            Dialog!.Close();ProfileDataWarnings.Check(b,empty,"warning-test-b");await Task.Delay(700);
            check(Dialog is null,"Closing without checking acknowledges only the current failure episode.");
            var expanded=b with { PrimaryTemplate="{metric.two} {metric.three}" };
            ProfileDataWarnings.Check(expanded,empty,"warning-test-b");await Task.Delay(700);
            check(Dialog is { IsVisible:true } && Details.Contains("metric.three"),"Acknowledging an existing failure does not silence a different new variable.");
            Dialog!.Close();ProfileDataWarnings.Forget("warning-test-a");ProfileDataWarnings.Forget("warning-test-b");
            field.SetValue(null,new DataWarningPreferences(path));
            ProfileDataWarnings.Check(a,empty,"warning-test-a");await Task.Delay(700);
            check(Dialog is null,"Reloading preferences still suppresses the chosen variable after its incident is forgotten.");
            var key=typeof(ProfileDataWarnings).GetMethod("PreferenceKey",StaticPrivate)!;
            string ka=(string)key.Invoke(null,new object[] { a,"进度表达式 / Progress expression" })!;
            string kb=(string)key.Invoke(null,new object[] { b,"进度表达式 / Progress expression" })!;
            check(ka!=kb,"Expression warnings use exact profile identities instead of muting every progress expression.");
        }
        finally
        {
            Dialog?.Close();ProfileDataWarnings.Forget("warning-test-a");ProfileDataWarnings.Forget("warning-test-b");
            field.SetValue(null,original);if(File.Exists(path)) File.Delete(path);
            app.Styles.Remove(theme);app.RequestedThemeVariant=previousTheme;
        }
    }
}
