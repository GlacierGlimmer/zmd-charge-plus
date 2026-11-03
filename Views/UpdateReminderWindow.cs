using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Diagnostics;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Views;

internal sealed class UpdateReminderPresenter
{
    private string? _notifiedVersion;
    internal UpdateReminderWindow? Current { get; private set; }

    internal bool Present(SettingsWindow.UpdateCheckResult result)
    {
        if (!result.HasUpdate || string.IsNullOrWhiteSpace(result.LatestVersion)
            || result.LatestVersion == _notifiedVersion) return false;
        _notifiedVersion = result.LatestVersion;
        Close();
        var window = new UpdateReminderWindow(result);
        Current = window;
        window.Closed += (_, _) => { if (ReferenceEquals(Current, window)) Current = null; };
        window.Show();
        return true;
    }

    internal void Close() => Current?.Close();
}

internal sealed class UpdateReminderWindow : Window
{
    private readonly SettingsWindow.UpdateCheckResult _result;
    private readonly TextBlock _title = new() { Name = "UpdateReminderTitle", FontSize = 17, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _message = new() { Name = "UpdateReminderMessage", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _viewLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _laterLabel = new() { TextWrapping = TextWrapping.Wrap };

    internal UpdateReminderWindow(SettingsWindow.UpdateCheckResult result)
    {
        _result = result;
        Background = Brush.Parse("#242426");
        Width = 520; Height = 280; MinWidth = 300; MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 18 };
        panel.Children.Add(_title); panel.Children.Add(_message);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var view = new Button { Name = "ViewUpdateButton", Content = _viewLabel, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0,0,8,0) };
        var later = new Button { Name = "LaterUpdateButton", Content = _laterLabel, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8,0,0,0) };
        view.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(GitHubReleaseUpdate.ReleasesUrl) { UseShellExecute = true });
                Close();
            }
            catch (Exception ex) { AppLog.Error("Unable to open the platform release page.", ex); }
        };
        later.Click += (_, _) => Close();
        buttons.Children.Add(view); Grid.SetColumn(later, 1); buttons.Children.Add(later);
        panel.Children.Add(buttons); Content = panel;
        RefreshLanguage();
        LocalizationManager.LanguageChanged += RefreshLanguage;
        Closed += (_, _) => LocalizationManager.LanguageChanged -= RefreshLanguage;
        Opened += (_, _) => { var scales = GlobalAppearance.CurrentScales; GlobalAppearance.Apply(this, scales.Font, scales.Ui); };
    }

    private void RefreshLanguage()
    {
        Title = _result.NotificationTitle; _title.Text = Title; _message.Text = _result.NotificationMessage;
        _viewLabel.Text = LocalizationManager.Text("查看更新", "View update");
        _laterLabel.Text = LocalizationManager.Text("稍后", "Later");
        FlowDirection = LocalizationManager.CurrentLanguage.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
}
