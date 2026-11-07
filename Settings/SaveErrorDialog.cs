using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Settings;

internal static class SaveErrorDialog
{
    internal static string Details(Exception error) => Redact(error.ToString());
    internal static string Redact(string text) => Regex.Replace(Regex.Replace(text,
        @"(?i)((?:api[_-]?key|authorization|access[_-]?token)\s*[:=]\s*)(?:bearer\s+)?[^\s,""}]+", "$1[redacted]"),
        @"(?i)bearer\s+[^\s,""}]+", "Bearer [redacted]");

    internal static Task Show(Window? owner,Exception error,bool startup=false,string? title=null)
    {
        Diagnostics.ApplicationErrors.MarkShown(error);
        var dialog=Create(error,startup,title);
        dialog.ShowActivated=owner?.IsActive==true;
        if(owner?.IsVisible==true)return dialog.ShowDialog(owner);
        dialog.WindowStartupLocation=WindowStartupLocation.CenterScreen;dialog.ShowInTaskbar=true;
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed+=(_,_)=>done.TrySetResult();dialog.Show();return done.Task;
    }
    internal static Window Create(Exception error,bool startup=false,string? title=null,string? rawDetails=null)
    {
        string T(string zh,string en)=>LocalizationManager.Text(zh,en);
        string details=rawDetails??Details(error);
        var reason=new TextBlock{Name="SaveErrorReason",Text=Redact(error.Message),TextWrapping=TextWrapping.Wrap,FontSize=14};
        var body=new TextBox{Name="SaveErrorDetails",Text=details,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=140,MaxHeight=260};
        var close=new Button{Name="CloseSaveError",Content=T("关闭窗口","Close window"),HorizontalContentAlignment=HorizontalAlignment.Center};
        var copy=new Button{Name="CopySaveError",Content=T("复制异常详情","Copy error details"),HorizontalContentAlignment=HorizontalAlignment.Center};
        var logs=new Button{Content=T("日志文件夹","Logs Folder"),HorizontalContentAlignment=HorizontalAlignment.Center};
        foreach(var button in new[]{close,copy,logs}){button.MinHeight=36;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.Content=new TextBlock{Text=button.Content?.ToString(),TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};}
        logs.Margin=new Thickness(8,0);
        var actions=new Grid{ColumnDefinitions=new("*,*,*")};actions.Children.Add(copy);Grid.SetColumn(logs,1);actions.Children.Add(logs);Grid.SetColumn(close,2);actions.Children.Add(close);
        var panel=new StackPanel{Spacing=14,Children={PromptStyle.Heading(title??T("保存失败","Save failed")),reason,body}};
        if(startup && error.Message==T("请先把应用拖到 Applications 文件夹，再开启登录启动。","Move the app into Applications before enabling launch at login."))
        {
            var applications=new Button{Content="Applications ↗"};applications.Click+=(_,_)=>{try{Open("file:///Applications");}catch(Exception ex){ActionFailed(ex);}};panel.Children.Add(applications);
        }
        panel.Children.Add(actions);
        var dialog=new Window{Name="SaveErrorDialog",Title=title??T("保存失败","Save failed"),Width=620,SizeToContent=SizeToContent.Height,MaxHeight=660,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        PromptStyle.Apply(dialog,panel);
        close.Click+=(_,_)=>dialog.Close();dialog.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){dialog.Close();e.Handled=true;}};
        void ActionFailed(Exception ex)
        {
            Diagnostics.ApplicationErrors.MarkShown(ex);
            reason.Text=Redact(ex.Message);details=Details(ex);body.Text=details;
            Diagnostics.AppLog.Error("Error prompt action failed.",ex);
        }
        copy.Click+=async(_,_)=>{try{if(dialog.Clipboard is {} clipboard)await clipboard.SetTextAsync(details);}catch(Exception ex){ActionFailed(ex);}};
        logs.Click+=(_,_)=>{try{System.IO.Directory.CreateDirectory(Diagnostics.AppLog.LogsDirectory);Open(Diagnostics.AppLog.LogsDirectory);}catch(Exception ex){ActionFailed(ex);}};
        return dialog;
    }
    private static void Open(string path)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});
}
