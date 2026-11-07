# Endfield Charge Plus

**v0.2.0 · 2026.11.07** · Windows 10 / 11 · x64 / x86 / ARM64

**简体中文** · [English](README.en.md)

终末地风格的灵动状态 HUD。把系统、网络、时间和自定义数据放在视线之内。

## v0.2.0 功能

- 重新设计的深色设置界面：左侧导航、荧光黄强调色和黄/绿/粉三色细节，区分当前系统与软件整体占用。
- 方案管理、自定义模板、变量库、HTTP / JSON 数据源与按章节定位的使用手册。
- 20 种随软件打包的离线语言，默认跟随系统语言；可手动选择并设置全局字体及 UI 缩放。
- 统一的异常与数据提示，保留具体原因、复制详情、日志入口及逐变量“不再提示”。
- 在 HUD 上长按鼠标中键，指向环形栏中的方案，松开切换；长按时间、拖动距离和圆盘大小可配置。
- 自动轮播与手动切换共用队列：点击向下切换；滚轮向上/向下切换上一/下一方案，配合对应方向动画。
- 轮播可选简洁、完整、向上翻滚、向下翻滚四种动画；快捷键可控制显示/隐藏、前后方案、暂停/继续、预览和设置。
- 多实例 HUD 保留各自的方案、队列、位置、字体、显示器与层级；共享的全局设置统一管理。

Windows 通过 LibreHardwareMonitor / PawnIO 读取受支持的底层传感器，并接入 AIDA64 导出的共享数据。温度、功耗等指标取决于硬件、驱动和权限；相关设置与诊断位于“硬件数据”。不提供 CPUID 专有 SDK。

## 下载与更新

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

Releases 内提供 SHA-256 校验文件。软件在“关于”中按本平台独立仓库检查更新；商店安装版本由商店管理更新。

## 支持语言

共 **20 种**：简体中文、English、繁體中文、日本語、한국어、Deutsch、Français、Español、Português、Русский、العربية、हिंदी、বাংলা、اردو、Bahasa Indonesia、Tiếng Việt、ไทย、Türkçe、Italiano、فارسی。

语言包采用离线 `.lang` 格式。首次启动跟随系统界面语言；手动选择会持久保存；未支持的语言回退 English。语言名称不限制使用地区。产品名称 **Endfield Charge Plus** 不翻译。

## 使用起步

1. 打开设置，选择内置方案并预览。
2. 按需调整显示位置、内容、动画与数据来源。
3. 点击“保存并应用”；遇到不可用数据时查看提示中的原因和硬件/权限说明。
4. 完整的配置、变量、规则、权限与排错说明见软件内“使用手册”。

## 各平台仓库

| 平台 | 源码 | 发行包 |
| --- | --- | --- |
| Windows | [GitHub](https://github.com/GlacierGlimmer/zmd-charge-plus) | [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases) |
| Linux | [GitHub](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux) | [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/releases) |
| macOS | [GitHub](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos) | [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases) |
| Android | [GitHub](https://github.com/GlacierGlimmer/zmd-charge-plus-for-android) | [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-android/releases) |

## 项目与反馈

- 作者：**GlacierGlimmer冰川雪貓**
- [官网](https://zmd-bar.x-neko.com/) · [问题追踪](https://ecp-bugs.x-neko.com/) · [本次更新说明](docs/release-notes.md)
- [作者 BiliBili](https://space.bilibili.com/354548348) · [作者 YouTube](https://www.youtube.com/@glacierglimmer925)
- MIT 许可；基于 [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge)，第三方许可见仓库与发行包。

本项目为非官方社区作品，与《明日方舟：终末地》开发方及发行方无隶属或背书关系。
