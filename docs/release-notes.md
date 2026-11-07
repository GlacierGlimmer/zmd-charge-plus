# ECP For Windows v0.2.0

**v0.1.1 → v0.2.0 · 构建日期 2026.11.07**

## 主要更新

- 重新设计的深色设置界面：左侧导航、荧光黄强调色和黄/绿/粉三色细节，区分当前系统与软件整体占用。
- 方案管理、自定义模板、变量库、HTTP / JSON 数据源与按章节定位的使用手册。
- 20 种随软件打包的离线语言，默认跟随系统语言；可手动选择并设置全局字体及 UI 缩放。
- 统一的异常与数据提示，保留具体原因、复制详情、日志入口及逐变量“不再提示”。
- 在 HUD 上长按鼠标中键，指向环形栏中的方案，松开切换；长按时间、拖动距离和圆盘大小可配置。
- 自动轮播与手动切换共用队列：点击向下切换；滚轮向上/向下切换上一/下一方案，配合对应方向动画。
- 轮播可选简洁、完整、向上翻滚、向下翻滚四种动画；快捷键可控制显示/隐藏、前后方案、暂停/继续、预览和设置。
- 多实例 HUD 保留各自的方案、队列、位置、字体、显示器与层级；共享的全局设置统一管理。

Windows 通过 LibreHardwareMonitor / PawnIO 读取受支持的底层传感器，并接入 AIDA64 导出的共享数据。温度、功耗等指标取决于硬件、驱动和权限；相关设置与诊断位于“硬件数据”。不提供 CPUID 专有 SDK。

## 主要修复

- 修复部分 CPU 温度和硬件数据无法获取的问题，加入驱动诊断、AIDA64 共享数据与按显卡区分的数据说明。
- 修复托盘/任务栏入口点击与菜单悬停问题、列表高亮残留、语言选择器拖动与按钮对齐。
- 修复切换方案仅使用简洁动画、方案管理未同步实际 HUD、保存操作后页面滚动偏移等问题。
- 优化启动、保存并应用、隐藏状态采样、目录/语言缓存和设置关闭时的资源释放。

## 安装与升级

从 [v0.2.0 Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/tag/v0.2.0) 下载与你的系统架构对应的 `portable.zip`。完整解压后运行：

```text
EndfieldChargePlus0.2.0/
├─ EndfieldChargePlus0.2.0.exe
├─ Runtime/
├─ Documents/
└─ Licenses/
```

主程序依赖配套目录，请勿只移动 EXE。缺少文件时，启动器会给出恢复说明。软件配置位于系统用户数据目录，不存放在解压目录；更新时退出旧版，完整解压到新的空目录，原配置会继续使用。

也可以从 [Microsoft Store](https://apps.microsoft.com/detail/9p3pld3lx7w6) 安装，或使用：

```powershell
winget install --id 9P3PLD3LX7W6 --source msstore --exact
```

商店可用版本及更新时间以商店页面为准。v0.2.0 的 Windows 便携包仅提供 ZIP；不提供 Setup、7z 或独立单文件 EXE。商店包采用三架构散文件布局，保留原应用与开机启动身份。

**支持语言（20）：** 简体中文、English、繁體中文、日本語、한국어、Deutsch、Français、Español、Português、Русский、العربية、हिंदी、বাংলা、اردو、Bahasa Indonesia、Tiếng Việt、ไทย、Türkçe、Italiano、فارسی。

## English — changes since v0.1.1

- Redesigned dark settings with a vertical navigation rail, a fluorescent-yellow accent and three-colour details. System usage and the application’s overall usage are shown separately.
- Profile management, template variables, HTTP / JSON sources and a chapter-based offline manual.
- Twenty bundled offline languages, system-language selection and adjustable global font/UI scale.
- Consistent error and data prompts with concrete reasons, copyable details, log access and per-variable dismissal.
- Hold the middle mouse button on the HUD, point to a profile and release to switch. Hold delay, drag distance and ring size are configurable.
- Manual and automatic cycling share a queue. Clicking moves forward; scrolling up/down selects the previous/next profile with the corresponding transition direction.
- Four cycle animations: Simple, Full, Roll Up and Roll Down. Bind show/hide, previous/next, pause/resume, preview and Settings actions.
- Multiple desktop HUDs retain independent profiles, queues, placement, fonts, displays and layers while sharing global settings.
- Improve CPU temperature and hardware-data collection, driver diagnostics, AIDA64 sharing and per-adapter GPU explanations.
- Fix tray/taskbar interactions, menu and list hover feedback, language-window dragging and button alignment.
- Fix profile animation selection, editor/HUD selection synchronization and unexpected scrolling after profile actions.
- Reduce repeated startup/save work, idle sampling, catalogue and language allocations, and resources retained after Settings closes.

Download the matching x64, x86 or ARM64 `portable.zip` from [v0.2.0 Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/tag/v0.2.0). Extract the entire archive and run `EndfieldChargePlus0.2.0/EndfieldChargePlus0.2.0.exe`. Keep `Runtime`, `Documents` and `Licenses` beside the executable. The launcher explains how to recover missing companion files.

Settings remain in the system user-data directory. To update, exit the old application and extract the complete new package into a new empty folder.

[Microsoft Store](https://apps.microsoft.com/detail/9p3pld3lx7w6) and `winget install --id 9P3PLD3LX7W6 --source msstore --exact` are also available; the Store controls its own available version and rollout. Windows portable releases contain ZIP archives, with no Setup, 7z or standalone single-file executable. The three-architecture Store package retains the existing application and startup identities.

**Languages:** Simplified Chinese, English, Traditional Chinese, Japanese, Korean, German, French, Spanish, Portuguese, Russian, Arabic, Hindi, Bengali, Urdu, Indonesian, Vietnamese, Thai, Turkish, Italian, Persian.

Hardware metrics depend on the platform, drivers and permissions. Check the SHA-256 file before installing. [Website](https://zmd-bar.x-neko.com/) · [Issues](https://ecp-bugs.x-neko.com/).
