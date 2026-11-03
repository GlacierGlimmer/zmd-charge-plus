using System.Globalization;

namespace EndfieldChargePlus.Customization;

/// <summary>Resolve collector messages when read so cached status follows the UI language.</summary>
internal static class FrameRateStatus
{
    internal static string Localize(string status)
    {
        if(status=="OK")return LocalizationManager.Text("帧采集正常","Frame capture active");
        const string failure="帧率采集失败：";
        if(status.StartsWith(failure,StringComparison.Ordinal))
            return LocalizationManager.Formatted($"帧率采集失败：{status[failure.Length..]}",$"Frame capture failed: {status[failure.Length..]}");
        int divider=status.LastIndexOf(" / ",StringComparison.Ordinal);
        string english=divider>=0 ? status[(divider+3)..] : status;
        const string exited="Frame capture exited (";
        if(english.StartsWith(exited,StringComparison.Ordinal) && english.EndsWith(')') &&
            int.TryParse(english.AsSpan(exited.Length,english.Length-exited.Length-1),NumberStyles.Integer,CultureInfo.InvariantCulture,out int code))
            return LocalizationManager.Formatted($"帧事件采集已退出（{code}）",$"Frame capture exited ({code})");
        return english switch
        {
            "Waiting for frames"=>LocalizationManager.Text("等待帧样本","Waiting for frames"),
            "Requires 64-bit Windows"=>LocalizationManager.Text("帧率采集需要 64 位 Windows","Requires 64-bit Windows"),
            "Select a running window"=>LocalizationManager.Text("请选择正在运行的窗口","Select a running window"),
            "Target window closed; select another window"=>LocalizationManager.Text("目标窗口已关闭，请重新选择","Target window closed; select another window"),
            "Display disconnected"=>LocalizationManager.Text("显示器已断开","Display disconnected"),
            "Display source mapping unavailable or ambiguous across GPUs"=>LocalizationManager.Text("显示源映射不可用或存在多 GPU 歧义","Display source mapping unavailable or ambiguous across GPUs"),
            "No valid frame events for this target"=>LocalizationManager.Text("所选目标尚无有效帧事件","No valid frame events for this target"),
            "Waiting for frame events; administrator access may be needed"=>LocalizationManager.Text("等待帧事件；无权限时请以管理员身份运行","Waiting for frame events; administrator access may be needed"),
            "Access denied; run the app as administrator"=>LocalizationManager.Text("无法启动帧事件采集，请以管理员身份运行","Access denied; run the app as administrator"),
            "Waiting for Present frames"=>LocalizationManager.Text("等待 Present 帧事件","Waiting for Present frames"),
            "An X11/XWayland session is required"=>LocalizationManager.Text("需要 X11／XWayland 会话","An X11/XWayland session is required"),
            "X server lacks Present"=>LocalizationManager.Text("X Server 不支持 Present 扩展","X server lacks Present"),
            "Install libxpresent1"=>LocalizationManager.Text("需要安装 libxpresent1","Install libxpresent1"),
            "Present API unavailable"=>LocalizationManager.Text("Present 采集接口不可用","Present API unavailable"),
            "Requires macOS"=>LocalizationManager.Text("需要 macOS","Requires macOS"),
            "Frame capture unavailable; check app components and Screen Recording permission"=>LocalizationManager.Text("帧率采集不可用，请检查应用组件与屏幕录制权限","Frame capture unavailable; check app components and Screen Recording permission"),
            "Selected target is unavailable"=>LocalizationManager.Text("所选窗口或显示器已不可用","Selected target is unavailable"),
            "Select a window"=>LocalizationManager.Text("请选择窗口","Select a window"),
            "Screen Recording permission required; relaunch after granting"=>LocalizationManager.Text("需要屏幕录制权限；授权后请重新启动应用","Screen Recording permission required; relaunch after granting"),
            "Requires macOS 12.3 or newer"=>LocalizationManager.Text("需要 macOS 12.3 或以上版本","Requires macOS 12.3 or newer"),
            "Unavailable"=>LocalizationManager.Text("不可用","Unavailable"),
            // OS/driver error details and application titles are external data.
            _=>status
        };
    }
}
