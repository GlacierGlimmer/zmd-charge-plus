using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using EndfieldChargePlus.Interop;
namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private readonly Dictionary<string,(TextBox Box,TextBlock Hint)> _shortcutEditors=new();
    private TextBox? _recordingShortcut;
    private Key _pendingModifier;
    private string _recentCapturedShortcut="";
    private long _captureCompletedAt;
    private bool _ignoreRecordedPointer;
    private bool JustCaptured(string chord)=>chord==_recentCapturedShortcut && _captureCompletedAt!=0 && System.Diagnostics.Stopwatch.GetElapsedTime(_captureCompletedAt).TotalMilliseconds<180;
    private string _registrationErrorChord="";
    private const string CaptureHelp="Press a key or mouse button. Esc clears the binding.";
    private const string CaptureHelpZh="按下键盘或鼠标按键；Esc 清除此绑定。";
    private void ConfigureShortcutRecorder(string id,TextBox box,TextBlock hint)
    {
        _shortcutEditors[id]=(box,hint);
        box.AddHandler(PointerPressedEvent,(_,e)=>{if(_recordingShortcut is null){BeginShortcutRecording(box);e.Handled=true;}},RoutingStrategies.Tunnel);
        box.AddHandler(KeyDownEvent,(_,e)=>RecordShortcutKey(box,e),RoutingStrategies.Tunnel);
        box.AddHandler(KeyUpEvent,(_,e)=>{
            if(_recordingShortcut==box && e.Key==_pendingModifier && HudHotkeyGesture.TryCapture(e.Key,e.KeyModifiers,out var chord)){FinishShortcutRecording(chord.Text);e.Handled=true;}
        },RoutingStrategies.Tunnel);
        box.TextChanged+=(_,_)=>{_registrationErrorChord="";RefreshShortcutValidation();};
    }
    private void InitializeShortcutPointerRecorder()
    {
        AddHandler(PointerPressedEvent,(_,e)=>{
            if(_ignoreRecordedPointer && _captureCompletedAt!=0 && System.Diagnostics.Stopwatch.GetElapsedTime(_captureCompletedAt).TotalMilliseconds<180){_ignoreRecordedPointer=false;e.Handled=true;return;}
            if(_recordingShortcut is not null && HudHotkeyGesture.TryCapturePointer(e.GetCurrentPoint(this).Properties.PointerUpdateKind,e.KeyModifiers,out var chord)){FinishShortcutRecording(chord.Text);e.Handled=true;}
        },RoutingStrategies.Tunnel);
        Closed+=(_,_)=>_recordingShortcut=null;
        Deactivated+=(_,_)=>{_recordingShortcut=null;_pendingModifier=Key.None;RefreshShortcutValidation();};
    }
    internal void BeginShortcutRecording(TextBox box){_recordingShortcut=box;_pendingModifier=Key.None;box.Focus();RefreshShortcutValidation();}
    private void RecordShortcutKey(TextBox box,KeyEventArgs e)
    {
        if(_recordingShortcut!=box){if(HudHotkeyGesture.TryCapture(e.Key,e.KeyModifiers,out var recent) && JustCaptured(recent.Text)){e.Handled=true;return;}if(e.Key is Key.Enter or Key.Space){BeginShortcutRecording(box);e.Handled=true;}return;}
        e.Handled=true;
        if(e.Key==Key.Escape){FinishShortcutRecording("");return;}
        if(!HudHotkeyGesture.TryCapture(e.Key,e.KeyModifiers,out var chord))return;
        if(chord.IsModifier){_pendingModifier=e.Key;return;}
        FinishShortcutRecording(chord.Text);
    }
    private void FinishShortcutRecording(string text){var box=_recordingShortcut;_recordingShortcut=null;_pendingModifier=Key.None;_recentCapturedShortcut=text;_captureCompletedAt=System.Diagnostics.Stopwatch.GetTimestamp();_ignoreRecordedPointer=false;if(box is not null)box.Text=text;RefreshShortcutValidation();}
    private string ShortcutActionName(string id)=>id=="toggle"?LocalizationManager.Text("显示 / 隐藏全部 HUD 的快捷键","Shortcut to show / hide all HUDs"):HudQuickHotkeys.Actions.Where(x=>x.Id==id).Select(x=>LocalizationManager.Text(x.Zh,x.En)).FirstOrDefault()??id;
    internal bool RefreshShortcutValidation()
    {
        var bindings=_shortcutEditors.Select(e=>(e.Key,Chord:HudHotkeyGesture.TryParse(e.Value.Box.Text,out var chord)?chord.Text:"")).ToArray();
        bool valid=true;
        foreach(var (id,(box,hint)) in _shortcutEditors)
        {
            string chord=bindings.First(x=>x.Key==id).Chord;
            var others=bindings.Where(x=>chord.Length>0 && x.Key!=id && x.Chord==chord).Select(x=>ShortcutActionName(x.Key)).ToArray();
            bool unavailable=chord.Length>0 && chord==_registrationErrorChord;
            bool invalid=!string.IsNullOrWhiteSpace(box.Text) && chord.Length==0;
            bool error=others.Length>0 || unavailable || invalid;valid&=!error;
            bool recording=_recordingShortcut==box;
            if(error){box.Foreground=Brush.Parse("#FF6464");box.BorderBrush=Brush.Parse("#FF6464");}
            else{box.ClearValue(TextBox.ForegroundProperty);box.ClearValue(TextBox.BorderBrushProperty);if(recording)box.BorderBrush=Brush.Parse("#FFFA00");}
            hint.Foreground=Brush.Parse(error?"#FF6464":"#FFFA00");
            hint.Text=recording?LocalizationManager.Text(CaptureHelpZh,CaptureHelp):others.Length>0?string.Format(LocalizationManager.DataCulture,LocalizationManager.Text("快捷键冲突：{0}","Shortcut conflict: {0}"),string.Join(", ",others)):unavailable||invalid?string.Format(LocalizationManager.DataCulture,LocalizationManager.Text("快捷键不可用：{0}","Unavailable shortcut: {0}"),box.Text):"";
            hint.IsVisible=!string.IsNullOrEmpty(hint.Text);ToolTip.SetTip(box,hint.IsVisible?hint.Text:null);
        }
        return valid;
    }
}
