# 下一版开发进度

2026-10-15 本地开发。产品版本沿用现有配置，本轮尚未上传 Releases 或商店。

已完成以下实现：

1. Windows 快捷键显隐动画。在「显示与位置」为快捷键独立选择完整或简洁显示动画；隐藏时播放收回动画，再隐藏窗口。主、副 HUD 同时响应，显示时保留各自方案和位置。临时模式会按所选动画呼出一次，然后继续原有事件显示方式。快速连按以最后一次请求为准。
2. 桌面三端常驻轮播新增「向上翻滚」。只移动胶囊内部的图标、文字、圆环和徽章，保持窗口、层级和点击区域。新配置默认使用翻滚；已保存的简洁／完整设置保持不变。DeepSeek 峰谷事件保留完整动画，首次显示及临时模式保留显示／收回生命周期。
3. Windows 媒体信息。变量库增加 18 个 media.* 变量，读取 Windows 系统媒体会话的曲名、歌手、专辑、来源、播放状态、时间轴及进度。仅按需读取；播放位置按上报时间及播放速度外推，暂停不外推；缺失时间轴保持不可用。读取超时和退出都不会等待无响应的媒体服务，重复采样最多保留一个未完成请求。商店打包声明 globalMediaControl 能力。
4. 桌面三端事件呼出。在「HUD显示内容 → 触发规则」（Windows 新分类；其他桌面端仍为“HUD 内容与数据”）添加规则，支持阈值比较、取值变化和由假变真，每条规则独立冷却。默认没有启用的规则；首次采样和缺失数据恢复时只建立基线。仅在非持续显示模式下生效，按当前实例的轮播队列推进，空队列使用当前方案。主、副 HUD 的规则独立，采样仍共用。
5. 桌面三端热区配置。在「显示与位置」调整九宫格位置、宽高、X/Y 偏移和 0–3000 ms 停留时长。按目标显示器完整屏幕范围计算，并将区域限制在屏幕内。可选择每次热区呼出推进轮播。macOS 保留原先 40 px 的默认热区高度；未伪造刘海屏实机验证结果。

上一批的「需管理员身份运行」和按变量永久关闭数据提示继续保留。

## 使用示例

媒体 HUD 可在自定义方案中使用：

- 主值：`{media.title_artist}`
- 次值：`{media.position_text} / {media.duration_text}`
- 右侧：`{media.progress|0}`，后缀 `%`
- 进度变量：`media.progress`

换歌规则选择变量 `media.title`、模式「取值变化」，设置冷却秒数并保存。若播放器没有向 Windows 注册媒体会话，相关内容将不可用；没有时长的直播不会显示虚构进度。Linux/macOS 本轮未添加媒体采集器，其规则可使用各自变量库或 HTTP 数据源。

## 来源

交互需求参考 [Windows #11](https://github.com/GlacierGlimmer/zmd-charge-plus/issues/11)、[Windows #12](https://github.com/GlacierGlimmer/zmd-charge-plus/issues/12) 和 [macOS #1](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/issues/1)；媒体与规则方向参考 [社区 PR #7](https://github.com/GlacierGlimmer/zmd-charge-plus/pull/7)，按当前多 HUD 架构重新接入，未合并其旧版本运行时代码。

实际接口与能力声明依据 [Windows 媒体会话](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)、[媒体时间轴](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessiontimelineproperties)、[uap7:Capability](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap7-capability)。

验证记录与本地交付说明位于工作区 `.smoke/next-version-20261015` 和 `ECP_Release/Development20261015-Interactions`。macOS 原生构建和真实刘海屏、多显示器操作仍需在 Mac 上验收。
