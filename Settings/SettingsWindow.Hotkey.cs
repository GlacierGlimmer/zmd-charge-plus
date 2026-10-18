using Avalonia.Input;
using Avalonia.Interactivity;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private DesktopGlobalHotkey? _hudHotkey;
    private string? _hotkeyStatusZh, _hotkeyStatusEn;
    private object?[]? _hotkeyStatusArguments;
    internal bool IsRecordingHudHotkey => HudHotkeyBox.IsFocused;

    private void InitializeHudHotkeyEditor()
    {
        RebuildHotkeyAnimationChoices();
        HudHotkeyBox.AddHandler(KeyDownEvent, OnHudHotkeyKeyDown, RoutingStrategies.Tunnel);
        ClearHudHotkeyBtn.Click += (_, _) =>
        {
            HudHotkeyBox.Text = "";
            SetHudHotkeyStatus("保存并应用后关闭快捷键。", "Save & Apply to disable the shortcut.");
        };
    }

    private void OnHudHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == 0) return;
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (HudHotkeyGesture.TryCapture(e.Key, e.KeyModifiers, out var gesture))
        {
            HudHotkeyBox.Text = gesture.Text;
            SetHudHotkeyStatus("保存并应用后启用此快捷键。", "Save & Apply to enable this shortcut.");
        }
        else
            SetHudHotkeyStatus("请使用 Ctrl 或 Alt 加字母、数字或功能键（F12 除外）。", "Use Ctrl or Alt with a letter, digit or function key (except F12).");
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
        else if (_hudHotkey?.Gesture == requested)
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
        string previous = _hudHotkey?.Gesture ?? "";
        if (_hudHotkey is null ? !string.IsNullOrWhiteSpace(settings.HudToggleHotkey) : !_hudHotkey.TryUpdate(settings.HudToggleHotkey))
        {
            SetHudHotkeyStatus("无法注册此快捷键，可能已被占用。原有快捷键未更改，请更换组合键。", "Could not register this shortcut; it may be in use. The previous shortcut is unchanged. Choose another combination.");
            throw new InvalidOperationException(LocalizationManager.Text(_hotkeyStatusZh!, _hotkeyStatusEn!));
        }
        try { SettingsManager.Save(settings); }
        catch { _hudHotkey?.TryUpdate(previous); throw; }
    }
}
