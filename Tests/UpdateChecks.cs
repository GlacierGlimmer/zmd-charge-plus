using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class UpdateChecks
{
    [STAThread] static int Main(string[] args)
    {
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().With(LocalizationManager.FontOptions).SetupWithoutStarting();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));Exception? failure=null;bool done=false;int count=0;UpdateReminderPresenter? active=null;
        void Check(bool value,string text){if(!value)throw new Exception(text);count++;}
        Dispatcher.UIThread.Post(async()=>
        {
            try
            {
                Check(GitHubReleaseUpdate.Repository=="GlacierGlimmer/"+args[0],"Platform owns its release repository.");
                foreach(var sample in new[]{(HttpStatusCode.OK,"{\"tag_name\":\"v9.8.7\",\"draft\":false,\"prerelease\":false}","v9.8.7"),
                    (HttpStatusCode.NotFound,"",(string?)null),(HttpStatusCode.OK,"{\"tag_name\":null}",null),
                    (HttpStatusCode.OK,"{\"tag_name\":\"v99.0.0\",\"draft\":true}",null),
                    (HttpStatusCode.OK,"{\"tag_name\":\"v99.0.0\",\"prerelease\":true}",null)})
                {
                    var fake=new Reply(sample.Item1,sample.Item2);using var http=new HttpClient(fake);
                    Check(await GitHubReleaseUpdate.FetchTagAsync(http)==sample.Item3,"Only published stable releases are accepted.");
                    Check(fake.Urls.SequenceEqual(new[]{"https://api.github.com/repos/GlacierGlimmer/"+args[0]+"/releases/latest"}),"One platform release request, no tags or other repository fallback.");
                }
                using(var http=new HttpClient(new Reply(HttpStatusCode.Forbidden,"")))
                {
                    bool threw=false;try{await GitHubReleaseUpdate.FetchTagAsync(http);}catch(HttpRequestException){threw=true;}
                    Check(threw,"Rate limits and HTTP failures cannot masquerade as no update.");
                }
                var presenter=new UpdateReminderPresenter();active=presenter;
                var update=new SettingsWindow.UpdateCheckResult("0.1.0","9.8.7",SettingsWindow.UpdateStatusKind.UpdateAvailable,true,true);
                Check(!presenter.Present(update with{HasUpdate=false}),"No reminder without a newer version.");
                Check(presenter.Present(update),"A newer version opens an actual reminder window.");
                var window=presenter.Current!;
                foreach(var locale in LocalizationManager.SupportedLanguages)
                {
                    LocalizationManager.SetLanguage(locale.Language);await Task.Delay(40);
                    Check(window.Title==update.NotificationTitle,"Open reminder follows language changes: "+locale.Code);
                    Check(window.GetVisualDescendants().OfType<TextBlock>().Single(c=>c.Name=="UpdateReminderMessage").Text==update.NotificationMessage,"Reminder body is translated: "+locale.Code);
                    Check(window.Title?.Contains("Endfield Charge Plus")==true,"Product name remains intact: "+locale.Code);
                    Check(window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="ViewUpdateButton").Content is TextBlock view && view.Text==LocalizationManager.TranslateEnglish("View update"),"Release action translated: "+locale.Code);
                    Check(!LocalizationManager.TranslateEnglish("No published release or tag was found.").Contains("tag",StringComparison.OrdinalIgnoreCase),"No-release message describes releases only: "+locale.Code);
                }
                LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
                if(Environment.GetEnvironmentVariable("ECP_UPDATE_SCREENSHOT") is {Length:>0} path)
                {
                    await Task.Delay(120);using var image=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));image.Render(window);image.Save(path);
                }
                window.GetVisualDescendants().OfType<Button>().Single(c=>c.Name=="LaterUpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(presenter.Current is null,"Later closes the reminder without changing app settings.");
                Check(!presenter.Present(update),"The same version does not keep opening a reminder in one process.");
                Check(presenter.Present(update with{LatestVersion="9.8.8"}),"A different newer version may notify again.");presenter.Close();
                done=true;
            }
            catch(Exception ex){failure=ex;}
            finally{active?.Close();timeout.Cancel();}
        });
        Dispatcher.UIThread.MainLoop(timeout.Token);
        if(failure is not null){Console.Error.WriteLine(failure);return 1;}
        if(!done){Console.Error.WriteLine("Update checks timed out.");return 1;}
        Console.WriteLine($"PASS {count} platform release/reminder checks");return 0;
    }
    private sealed class Reply(HttpStatusCode status,string body):HttpMessageHandler
    {
        internal List<string> Urls=new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {Urls.Add(request.RequestUri!.ToString());return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body)});}
    }
}
