using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus.Settings;
public partial class SettingsWindow
{
    private HudQuickHotkeys? _quickHotkeys;
    private readonly Dictionary<string,TextBox> _quickHotkeyBoxes=new();
    private readonly List<(TextBlock Text,string Zh,string En)> _quickHotkeyLabels=new();
    internal void AttachQuickHotkeys(HudQuickHotkeys? manager){_quickHotkeys=manager;RefreshHudHotkeyRegistrationStatus();}
    private void InitializeQuickHotkeyEditors()
    {
        var body=new StackPanel{Spacing=12};
        foreach(var action in HudQuickHotkeys.Actions)
        {
            var label=new TextBlock{Text=LocalizationManager.Text(action.Zh,action.En),TextWrapping=Avalonia.Media.TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};_quickHotkeyLabels.Add((label,action.Zh,action.En));
            var box=new TextBox{Name="Shortcut_"+action.Id,Text=_rootSettings.QuickHotkeys.GetValueOrDefault(action.Id)??"",IsReadOnly=true,VerticalAlignment=VerticalAlignment.Center};
            _quickHotkeyBoxes[action.Id]=box;
            var clearLabel=new TextBlock{Text=LocalizationManager.Text("清除快捷键","Clear shortcut")};_quickHotkeyLabels.Add((clearLabel,"清除快捷键","Clear shortcut"));
            var clear=new Button{Content=clearLabel,VerticalAlignment=VerticalAlignment.Center};clear.Click+=(_,_)=>box.Text="";
            var warning=new TextBlock{TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=12,IsVisible=false};
            ConfigureShortcutRecorder(action.Id,box,warning);
            var row=new Grid{ColumnDefinitions=new("160,*,Auto"),RowDefinitions=new("Auto,Auto")};row.Children.Add(label);Grid.SetColumn(box,1);box.Margin=new(8,0);row.Children.Add(box);Grid.SetColumn(clear,2);row.Children.Add(clear);Grid.SetRow(warning,1);Grid.SetColumnSpan(warning,3);warning.Margin=new(0,4,0,0);row.Children.Add(warning);body.Children.Add(row);
        }
        var hint=new TextBlock{Text=WorkspaceStrings.Get("Your changes apply after Save & Apply."),TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=12};
        _quickHotkeyLabels.Add((hint,"点击“保存并应用”后更改生效。","Your changes apply after Save & Apply."));body.Children.Add(hint);
        if(QuickActionsPanel.Children[0] is Border {Child:StackPanel keyboard})
        {
            keyboard.Children.Add(new Border{Height=1,Background=Avalonia.Media.Brush.Parse("#3B3B3B"),Margin=new(0,8)});
            keyboard.Children.Add(body);
        }
    }
    private Dictionary<string,string> CollectQuickHotkeys()=>_quickHotkeyBoxes.ToDictionary(p=>p.Key,p=>p.Value.Text?.Trim()??"");
    private void LoadQuickHotkeys(AppSettings settings){foreach(var item in _quickHotkeyBoxes)item.Value.Text=settings.QuickHotkeys.GetValueOrDefault(item.Key)??"";}
    private void RefreshQuickHotkeyLabels()
    {
        foreach(var (text,zh,en) in _quickHotkeyLabels)text.Text=LocalizationManager.Text(zh,en);
        foreach(var box in _quickHotkeyBoxes.Values)box.Watermark=LocalizationManager.Text("点击后按下组合键","Click and press a shortcut");
        RefreshShortcutValidation();
    }
    internal bool CaptureRegisteredShortcut(string chord)
    {
        var box=_recordingShortcut;
        if(string.IsNullOrEmpty(chord))return false;
        if(box is null)return JustCaptured(chord);
        // RegisterHotKey can consume an already-bound chord before the focused
        // TextBox receives KeyDown. Route that chord to the recorder instead.
        FinishShortcutRecording(chord=="Escape"?"":chord);_ignoreRecordedPointer=HudHotkeyGesture.TryParse(chord,out var captured) && captured.IsMouse;SetHudHotkeyStatus("保存并应用后启用此快捷键。","Save & Apply to enable this shortcut.");return true;
    }
}
