using Avalonia.Input;
using Avalonia.Interactivity;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private DesktopGlobalHotkey? _hudHotkey;
    private string? _hotkeyStatusZh, _hotkeyStatusEn;
    private object?[]? _hotkeyStatusArguments;
    internal bool IsRecordingHudHotkey => _recordingShortcut is not null;

    private void InitializeHudHotkeyEditor()
    {
        RebuildHotkeyAnimationChoices();
        var warning=new Avalonia.Controls.TextBlock{TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=12,IsVisible=false};
        if(HudHotkeyBox.Parent?.Parent is Avalonia.Controls.StackPanel panel)panel.Children.Insert(2,warning);
        ConfigureShortcutRecorder("toggle",HudHotkeyBox,warning);InitializeShortcutPointerRecorder();
        ClearHudHotkeyBtn.Click += (_, _) =>
        {
            HudHotkeyBox.Text = "";
            SetHudHotkeyStatus("保存并应用后关闭快捷键。", "Save & Apply to disable the shortcut.");
        };
    }

    internal void AttachHudHotkey(DesktopGlobalHotkey? hotkey)
    {
        _hudHotkey = hotkey;
        RefreshHudHotkeyRegistrationStatus();
    }

    private void RefreshHudHotkeyRegistrationStatus()
    {
        string requested = _persistedRootSettings.HudToggleHotkey;
        if (string.IsNullOrWhiteSpace(requested))
            SetHudHotkeyStatus("未设置快捷键。", "No shortcut configured.");
        else if ((_quickHotkeys?.BindingFor("toggle") ?? _hudHotkey?.Gesture) == requested)
            SetHudHotkeyStatus($"已启用：{requested}", (System.FormattableString)$"Enabled: {requested}");
        else
            SetHudHotkeyStatus("快捷键未能注册，可能已被其他应用占用。请更换后保存并应用。", "Could not register the shortcut; another app may be using it. Choose another and Save & Apply.");
    }

    private void SetHudHotkeyStatus(string zh, string en)
    {
        _hotkeyStatusZh = zh;
        _hotkeyStatusEn = en;
        _hotkeyStatusArguments=null;
        RefreshHudHotkeyLocalization();
    }

    private void SetHudHotkeyStatus(string zh,FormattableString en)
    {
        _hotkeyStatusZh=zh;_hotkeyStatusEn=en.Format;_hotkeyStatusArguments=en.GetArguments();RefreshHudHotkeyLocalization();
    }

    private void RefreshHudHotkeyLocalization()
    {
        RefreshQuickHotkeyLabels();
        RebuildHotkeyAnimationChoices();
        if (_hotkeyStatusZh is not null && _hotkeyStatusEn is not null)
            HudHotkeyStatusText.Text = _hotkeyStatusArguments is null ? LocalizationManager.Text(_hotkeyStatusZh, _hotkeyStatusEn)
                : LocalizationManager.Formatted(_hotkeyStatusZh,System.Runtime.CompilerServices.FormattableStringFactory.Create(_hotkeyStatusEn,_hotkeyStatusArguments));
    }

    private void RebuildHotkeyAnimationChoices()
    {
        int index=HudHotkeyAnimationCombo.SelectedIndex;
        HudHotkeyAnimationCombo.Items.Clear();
        HudHotkeyAnimationCombo.Items.Add(LocalizationManager.Text("简洁","Simple"));
        HudHotkeyAnimationCombo.Items.Add(LocalizationManager.Text("完整","Full"));
        HudHotkeyAnimationCombo.SelectedIndex=index==0 ? 0 : 1;
    }

    private void SaveSettingsWithHudHotkey(AppSettings settings)
    {
        if(!RefreshShortcutValidation())throw new InvalidOperationException(LocalizationManager.Text("快捷键冲突：{0}","Shortcut conflict: {0}").Replace("{0}",string.Join(", ",_shortcutEditors.Values.Where(x=>x.Hint.IsVisible).Select(x=>x.Box.Text))));
        if(_quickHotkeys is not null)
        {
            if(_quickHotkeys.TryApply(settings,()=>SettingsManager.Save(settings)))return;
            _registrationErrorChord=_quickHotkeys.ErrorGesture;RefreshShortcutValidation();
            SetHudHotkeyStatus("无法注册此快捷键，可能已被占用。原有快捷键未更改，请更换组合键。", "Could not register this shortcut; it may be in use. The previous shortcut is unchanged. Choose another combination.");
            throw new InvalidOperationException(HudHotkeyStatusText.Text+" "+_quickHotkeys.ErrorGesture);
        }
        string previous = _hudHotkey?.Gesture ?? "";
        if (_hudHotkey is null ? !string.IsNullOrWhiteSpace(settings.HudToggleHotkey) : !_hudHotkey.TryUpdate(settings.HudToggleHotkey))
        {
            SetHudHotkeyStatus("无法注册此快捷键，可能已被占用。原有快捷键未更改，请更换组合键。", "Could not register this shortcut; it may be in use. The previous shortcut is unchanged. Choose another combination.");
            throw new InvalidOperationException(LocalizationManager.Text(_hotkeyStatusZh!, _hotkeyStatusEn!));
        }
        try { SettingsManager.Save(settings); }
        catch { _hudHotkey?.TryUpdate(previous); throw; }
    }
    private async Task SaveSettingsWithHudHotkeyAsync(AppSettings settings)
    {
        if(!RefreshShortcutValidation())throw new InvalidOperationException(LocalizationManager.Text("快捷键冲突：{0}","Shortcut conflict: {0}").Replace("{0}",string.Join(", ",_shortcutEditors.Values.Where(x=>x.Hint.IsVisible).Select(x=>x.Box.Text))));
        if(_quickHotkeys is not null)
        {
            if(await _quickHotkeys.TryApplyAsync(settings,()=>Task.Run(()=>SettingsManager.Save(settings))))return;
            _registrationErrorChord=_quickHotkeys.ErrorGesture;RefreshShortcutValidation();
            SetHudHotkeyStatus("无法注册此快捷键，可能已被占用。原有快捷键未更改，请更换组合键。", "Could not register this shortcut; it may be in use. The previous shortcut is unchanged. Choose another combination.");
            throw new InvalidOperationException(HudHotkeyStatusText.Text+" "+_quickHotkeys.ErrorGesture);
        }
        string previous = _hudHotkey?.Gesture ?? "";
        if (_hudHotkey is null ? !string.IsNullOrWhiteSpace(settings.HudToggleHotkey) : !_hudHotkey.TryUpdate(settings.HudToggleHotkey))
        {
            SetHudHotkeyStatus("无法注册此快捷键，可能已被占用。原有快捷键未更改，请更换组合键。", "Could not register this shortcut; it may be in use. The previous shortcut is unchanged. Choose another combination.");
            throw new InvalidOperationException(LocalizationManager.Text(_hotkeyStatusZh!, _hotkeyStatusEn!));
        }
        try { await Task.Run(()=>SettingsManager.Save(settings)); }
        catch { _hudHotkey?.TryUpdate(previous); throw; }
    }
}
