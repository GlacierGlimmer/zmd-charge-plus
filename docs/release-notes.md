# Endfield Charge Plus v0.2.0

## v0.2.0 · 2026-11-07

- 重新设计设置界面，保留深色风格与荧光黄强调色；提供方案管理、环形切换、自动轮播、触发规则、快捷操作和章节式使用手册。
- 提供 20 种常用语言的离线 .lang 语言资源，默认跟随系统语言；产品名称保持不翻译。
- 改善方案切换、错误详情、数据不可用提示、语言选择和窗口交互；按活动状态减少后台采样、重复布局和缓存分配，关闭设置时释放预览资源。
- 更新检查使用各平台自己的 GitHub Releases 仓库。

Windows：x64、x86、ARM64 三架构仅提供 portable.zip。完整解压后运行 EndfieldChargePlus0.2.0/EndfieldChargePlus0.2.0.exe，Runtime、Documents、Licenses 必须随主程序保留。配置存于用户数据目录。微软商店提供三架构散文件包，包版本 1.2.0.0；保留原应用与开机启动身份，不提供 Setup。

## English

v0.2.0, built on 2026-11-07. Redesigned settings, offline language packs, profile switching, configurable cycling and shortcuts, clearer error reports, and reduced idle work. Updates are checked against this platform’s own repository. Preserve the complete package contents and existing user data.

---

# 历史发布说明 / Earlier release notes

以下为历史记录；当前版本与交付方式以上方 v0.2.0 说明为准。

# 当前 Windows 打包方式（2026-10-27）

产品版本保持 v0.1.2。Windows 改为 x64、x86、ARM64 三架构自包含目录便携版，各提供 ZIP 和 7z。每个压缩包只有一层 `EndfieldChargePlus0.1.2` 文件夹，顶层主程序为 `EndfieldChargePlus0.1.2.exe`，DLL 和运行环境集中在 `Runtime`，说明在 `Documents`，许可在 `Licenses`；用户配置继续使用用户数据目录。当前暂停制作 Setup，其实验方案已归档到源码仓库外。详见 [Windows 便携版](windows-portable.md)。

下面保留 v0.1.1 的历史发布说明；其中单文件 EXE 的说明不代表当前打包方式。

# ECP For Windows v0.1.1

正式版 · v0.1.0 → v0.1.1 · 2026-10-13

## 简体中文：本次变化

- 新增可自定义数据采样间隔，多个 HUD 共用采样服务，避免重复采集；调整设置时保留各实例自己的配置。
- 新增「实验性功能 → 多实例 HUD」：每个 HUD 可独立选择显示器、方案、位置、字体、缩放与显示层。设置窗口存在且有多个 HUD 时，正在编辑的实例持续显示边框和「当前正在设置」提示。
- 修复多实例切换、关闭设置、删除实例时的状态与生命周期；各实例的常驻置顶／桌面层独立恢复。修复点击遮罩干扰、刷新闪烁及透明背景变黑。
- 新增并修正常驻点击 HUD 切换方案：只响应 HUD 本体点击，对所有实例有效；按被点击实例的轮播队列及动画切换，点击后重新计时。无需开启自动轮播。
- 修复字体选择不生效；增加工作区与屏幕绝对位置预设，切换锚点清除残留微调，避免 Y 意外保留 80。
- 新增实际显示器／窗口 FPS 变量与方案、有效目标选择及满量程 FPS 设置；清除已失效的目标。无法获取的数据汇总到一个提示框，不以刷新率或虚假零值代替。
- 设置窗口标题显示当前版本；实际提升权限后追加「（Endministrator Mode）」；中英文界面保持一致。
- 新增「时间 - 世界时钟」，39 个中英文固定 UTC 偏移选项，保留原有目标时间模式；内置方案按系统、时间、探测、DeepSeek 等类别排序，保留用户轮播顺序。
- DeepSeek 时段按官方通知与北京时间判断，周末和中国法定节假日为低谷；内置日历至 2030 年并支持后台更新。未公布年份只使用已确定的法定日期，不假定调休安排。
- 修复 Win+D／Explorer 桌面变化后桌面层 HUD 被壁纸覆盖，同时保留其他 HUD 的置顶状态；稳定采样不反复重设窗口层级。发行版提供 x64、x86、ARM64 自包含单文件 EXE 和仅含该 EXE 的 ZIP。

## English: changes since v0.1.0

- Add configurable sampling intervals shared by all HUDs, with independent instance settings.
- Add Experimental features / Multiple HUDs with per-instance monitor, profile, position, font, scale and layer. The selected HUD stays outlined and labelled while Settings is open.
- Fix instance lifecycle, layer restoration, click masks, refresh flicker and black transparency backgrounds.
- Enable persistent-HUD click cycling for every instance. Only the visible capsule accepts clicks; each instance uses its own rotation order and transition, and resets its timer after a click. Automatic rotation can remain off.
- Fix font application and stale position offsets; add work-area and absolute-screen anchors.
- Add real display/window FPS profiles, current target lists and configurable full-scale FPS. Aggregate unavailable variables into one dialog; never substitute refresh rate or fabricated zeroes.
- Show the version in Settings titles and append “（Endministrator Mode）” only for elevated sessions in both languages.
- Add World clock with 39 bilingual fixed UTC offsets and the existing target-time modes. Group built-ins by category without rewriting saved rotation queues.
- Use DeepSeek’s official Beijing-time schedule, excluding weekends and Chinese statutory holidays. Include calendars through 2030 plus validated background updates; unannounced years do not invent compensating-workday arrangements.
- Keep desktop HUDs visible after Win+D/Explorer changes while retaining other HUDs’ independent topmost state. Stable sampling does not repeatedly rewrite native layers. Ship self-contained x64, x86 and ARM64 EXEs and single-EXE ZIPs.

## 运行与兼容性 / Requirements and compatibility

实际 FPS 使用 Windows ETW 采集，需要管理员权限；硬件传感器依赖设备／驱动。ARM64 为交叉构建，尚未在物理 ARM64 电脑上验证。微软商店 x64/x86 包另行人工提交，本次 GitHub 发布不代表商店已经更新。

Real FPS uses Windows ETW and needs administrator privileges. Sensors depend on hardware/drivers. ARM64 is cross-built and not physically tested. Microsoft Store submission is handled separately; this release does not imply the Store has updated.

## 验证 / Validation

Windows 回归检查 261 项、软件合成检查 6 项、真实 Win+D 桌面层检查 7 项及 DeepSeek 日历检查 201 项通过。单文件包已验证架构、内置采集器与 ZIP 内容。

每个平台单独提供 SHA-256 校验文件。Use the platform-specific SHA256SUMS file to verify downloaded assets.

[完整源码变化 / Source comparison](https://github.com/GlacierGlimmer/zmd-charge-plus/compare/v0.1.0...v0.1.1)

## 各平台分别下载 / Separate platform downloads

**每个平台在不同仓库的 Releases 单独下载，本仓库的新发行资源仅对应 Windows。Each platform publishes its own assets in its own repository’s Releases.**

- [Windows Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases)
- [Linux Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/releases)
- [MacOS Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases)
- [Android Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-android/releases)
