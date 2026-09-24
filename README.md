# Endfield Charge Plus · 终末地风格状态栏HUD+

**简体中文** · [English](README.en.md)　｜　**Windows 10 / 11 · v0.1.0**

> **不止于电量提示，让需要的信息以你喜欢的方式出现在屏幕上。**

Endfield Charge Plus（**ECP**）是基于 [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge) 二次开发的 Windows 状态栏 HUD。它延续原项目的终末地风格电量动画，并将原本的电源提示扩展为可常驻、可唤出、可轮播的多数据 HUD：从 CPU / GPU 和网络状态，到 DeepSeek API 与自定义 HTTP/JSON 数据，都能组合成自己的显示方案。

**[⬇️ 下载最新版本](https://github.com/GlacierGlimmer/zmd-charge-plus/releases)** · **[🌐 项目网站](https://zmd-bar.x-neko.com/)** · **[原项目](https://github.com/QinAnze/zmd-charge)**

> 本项目为非官方社区二次开发作品，与《明日方舟：终末地》开发方及发行方无隶属、授权或背书关系。

## ✨ Plus 版带来了什么？

| 方向 | 原项目的核心体验 | Endfield Charge Plus 的扩展 |
| :-- | :-- | :-- |
| 显示内容 | 以电源插拔和电池状态为中心 | 扩展为系统监控、时间、网络探测、API 与自定义数据 HUD |
| 显示方式 | 电源事件触发的短暂弹出 | **按需唤出 / 持续显示**；持续显示可选置顶或桌面层（不置顶） |
| 唤出交互 | 电源状态变化 | 保留电源插拔提示；鼠标移至目标屏幕顶部中央，还可唤出当前方案 |
| 自定义 | 电量主题与动画 | 方案管理、变量模板、进度环、左右图标、颜色规则和两种动画模式 |
| 多方案 | 以电池 HUD 为主 | 内置多类方案、自定义方案，以及可排序的自动轮播队列 |
| 扩展能力 | 本机电量信息 | **429 个固定内置变量 + 17 类动态磁盘变量模板**，并可接入 HTTP/JSON 数据源 |

## ⬇️ 下载与使用

前往 [**GitHub Releases**](https://github.com/GlacierGlimmer/zmd-charge-plus/releases) 下载与你的 Windows 设备匹配的版本。**目前提供的发布形式为单文件 Portable EXE**，无需安装，也无需另装 .NET 运行时。

| 下载文件 | 适用设备 |
| :-- | :-- |
| `EndfieldChargePlus-v0.1.0-win-x64-portable.exe` | 常见 Intel / AMD 64 位 Windows 电脑 |
| `EndfieldChargePlus-v0.1.0-win-x86-portable.exe` | 32 位 Windows 设备 |
| `EndfieldChargePlus-v0.1.0-win-arm64-portable.exe` | Windows on ARM 设备 |

> 下载链接将在对应版本的 Release 发布后提供；上述文件名用于辨认架构。各架构以实际发布页面为准。

**首次使用只需几步：**

1. 下载并运行对应架构的 EXE；程序通过系统托盘驻留。
2. 从托盘菜单打开**设置**，在「显示与位置」中选择目标显示器、HUD 位置和显示方式。
3. 进入「HUD 内容与数据」，选择内置方案，或创建自己的 HUD；点击**预览**查看效果。
4. 点击右上角**保存并应用**。配置自动保存，重新启动后继续使用；可按需开启 Windows 开机启动。

默认会以**系统 - 内存**方案启动；首次界面语言跟随 Windows：中文系统使用简体中文，其他系统使用 English，也可以手动切换。

## 🎛️ 功能一览

| 功能 | 说明 |
| :-- | :-- |
| **两种显示方式** | 关闭「一直显示」时，HUD 按事件或鼠标唤出后自动收回；开启后持续更新当前方案。 |
| **置顶 / 桌面层** | 持续显示模式可选始终置顶或桌面层（不置顶）；HUD 支持点击穿透，不妨碍正常操作。 |
| **电源插拔提示** | 接通 / 断开电源时优先展示电池方案，插电使用完整动画、拔电使用简洁动画。 |
| **鼠标唤出** | 在非持续显示模式下，将鼠标移到目标屏幕顶部中央，即可临时显示当前非电池方案。 |
| **多显示器与位置** | 指定目标显示器，选九宫格预设位置或自定义 X/Y 坐标，并调整缩放与不透明度。 |
| **方案与动画** | 内置电池、CPU、GPU、内存、磁盘、网络、时间、DeepSeek 等方案；支持新建、另存、编辑与预览；完整 / 简洁动画可选。 |
| **自动轮播** | 按自定义队列依次切换方案，支持添加、删除、上移、下移，另设轮播间隔与切换动画。 |
| **高级变量库** | 429 个固定内置变量，另有 17 类按实际盘符生成的磁盘变量模板；覆盖硬件、系统、网络、进程、安全和开发者工具等。 |
| **自由组合显示** | 标题、主副数值、百分比圆环、左右图标均可按变量模板配置；支持格式化、表达式及条件颜色规则。 |
| **网络包探测** | 指定 IPv4 / IPv6 / 域名及端口，使用 ICMP、TCP 或 UDP 探测，查看延迟、丢包等状态。 |
| **DeepSeek API** | 配置自己的 API Key 后显示余额、当前高峰 / 低谷、时段进度与倒计时；以北京时间判定，并提供本地时区切换时间变量。 |
| **HTTP / JSON 数据源** | 配置请求地址、Header、刷新间隔和 JSON 字段映射，把自有接口数据接入 HUD。 |
| **日常维护** | 中英双语、托盘操作、开机启动、配置导入 / 导出 / 备份、日志和 GitHub 更新检查。 |

### 🧩 变量与自定义示例

变量不仅能单独显示，也可以参与模板、计算和进度显示。例如：

```text
{cpu.usage|0}                 CPU 使用率
{memory.used_bytes|gb:1}     已用内存（GB）
{network.download_bps|speed}  网络下载速度
{deepseek.balance|0.00}      DeepSeek API 余额
```

**变量不等于模拟数值。** 内置变量均有对应的读取或计算实现；实际能否读到取决于设备、驱动、权限、相关服务及第三方程序。无法获取时会显示不可用信息。完整 Key、格式和说明请在软件的「变量库」中查看。

DeepSeek API 需要用户自行提供 Key；HTTP/JSON 接口由用户自行配置。涉及密钥与自定义 Header 时，请参阅 [隐私说明](PRIVACY.md)。

## 💻 运行要求与源码构建

- **系统：** Windows 10 / 11；提供 x64、x86、ARM64 构建目标。
- **官方 Portable 构建：** 自包含单文件 EXE，不需要另行安装 .NET。
- **自行编译：** Windows 环境及 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
# 编译源码
dotnet build EndfieldChargePlus.csproj -c Release

# 示例：发布 x64 自包含单文件版本
dotnet publish EndfieldChargePlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
```

其他架构将 `win-x64` 换为 `win-x86` 或 `win-arm64`，并在相应 Windows 设备上测试。

## 📄 来源与许可

ECP 基于 **[QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge)** 修改与扩展。感谢原作者 **QinAnze** 的开源工作；原项目的来源和许可在此明确保留。本项目新增贡献采用 [MIT License](LICENSE)。详情参见 [NOTICE.md](NOTICE.md)，数据处理说明参见 [PRIVACY.md](PRIVACY.md)。

© 2026 GlacierGlimmer_冰川雪貓
