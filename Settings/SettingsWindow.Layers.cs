namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private void RefreshLayerDescription()
    {
        string T(string zh,string en)=>LocalizationManager.Text(zh,en);
        string text=T("桌面层：位于普通窗口后面。始终置顶：使用普通置顶层级。","Desktop: behind ordinary windows. Always on Top: the normal topmost layer.");
        if(PersistentLayerCombo.SelectedIndex==2)
        {
            text+="\n"+T("超级置顶：不抢焦点，在被其他置顶窗口覆盖时恢复。打开 ECP 设置、菜单或提示框时暂时让位。","Super Topmost restores the HUD above other topmost windows without taking focus. It yields while ECP settings, menus or dialogs are open.");
            text+="\n"+(OperatingSystem.IsMacOS()
                ?T("macOS：使用系统状态窗口层级；受保护的系统界面仍可能位于其上方。","macOS: uses the system status-window level. Protected system screens may remain above it.")
                :OperatingSystem.IsLinux()
                ?T("Linux：增强 X11 置顶；Wayland 下的效果取决于桌面合成器。","Linux: enhanced X11 stacking. Under Wayland, behavior depends on the compositor.")
                :T("Windows：尝试保持在任务管理器等置顶窗口上方；受保护的系统层和独占全屏仍可能覆盖 HUD。","Windows: attempts to stay above topmost windows such as Task Manager. Protected system layers and exclusive fullscreen may still cover the HUD."));
        }
        LayerDescription.Text=text;
    }
}
