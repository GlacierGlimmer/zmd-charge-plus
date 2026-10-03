# Endfield Charge Plus · 终末地风格状态栏HUD+

**简体中文** · [English](README.en.md)　｜　**Windows 10 / 11 · Linux x64 · macOS 13+ · v0.1.0**

> **不止于电量提示，让需要的信息以你喜欢的方式出现在屏幕上。**

Endfield Charge Plus（**ECP**）是基于 [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge) 二次开发的桌面状态栏 HUD。它延续原项目的终末地风格电量动画，并将原本的电源提示扩展为可常驻、可唤出、可轮播的多数据 HUD：从 CPU / GPU 和网络状态，到 DeepSeek API 与自定义 HTTP/JSON 数据，都能组合成自己的显示方案。

**[⬇️ Windows：Microsoft Store（推荐）](https://apps.microsoft.com/detail/9p3pld3lx7w6)** · **[Windows / Linux 下载](https://github.com/GlacierGlimmer/zmd-charge-plus/releases)** · **[macOS 下载](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases)** · **[🌐 项目网站](https://zmd-bar.x-neko.com/)** · **[原项目](https://github.com/QinAnze/zmd-charge)**

> 本项目为非官方社区二次开发作品，与《明日方舟：终末地》开发方及发行方无隶属、授权或背书关系。

Windows 版在本仓库维护；Linux 版 **[Endfield Charge Plus For Linux](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux)** 提供 APT 命令安装与四种独立安装包；macOS 版 **[Endfield Charge Plus For MacOS](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos)** 在独立仓库维护，目前提供 Apple Silicon 与 Intel 两种 DMG。使用方法见下方下载章节。

> ⚠️ **macOS 版本目前尚未进行真实 Mac 实机测试。** 当前仅完成 CI macOS 环境中的构建、原生采集、界面、DMG 挂载与启动等自动化验证，因此可能仍存在与具体机型、macOS 版本、刘海屏、多显示器或系统权限相关的兼容性问题。

## ✨ Plus 版带来了什么？

下表与后文功能详解以 Windows 版为准；Linux 与 macOS 版本会按平台及设备能力调整可用变量和行为，详见各自安装章节与独立仓库说明。

| 方向 | 原项目的核心体验 | Endfield Charge Plus 的扩展 |
| :-- | :-- | :-- |
| 显示内容 | 以电源插拔和电池状态为中心 | 扩展为系统监控、时间、网络探测、API 与自定义数据 HUD |
| 显示方式 | 电源事件触发的短暂弹出 | **按需唤出 / 持续显示**；持续显示可选置顶或桌面层（不置顶） |
| 唤出交互 | 电源状态变化 | 保留电源插拔提示；鼠标移至目标屏幕顶部中央，还可唤出当前方案 |
| 自定义 | 电量主题与动画 | 方案管理、变量模板、进度环、左右图标、颜色规则和两种动画模式 |
| 多方案 | 以电池 HUD 为主 | 内置多类方案、自定义方案，以及可排序的自动轮播队列 |
| 扩展能力 | 本机电量信息 | **429 个固定内置变量 + 17 类动态磁盘变量模板**，并可接入 HTTP/JSON 数据源 |

## ⬇️ 下载与使用

### Windows：Microsoft Store / winget（推荐）

ECP 已上架微软商店。前往 [**Microsoft Store 获取 Endfield Charge Plus**](https://apps.microsoft.com/detail/9p3pld3lx7w6)，按页面提示安装，安装完成后启动应用即可。

也可以使用 **winget**，在 PowerShell 或 Windows 终端中粘贴并运行：

```powershell
winget install --id 9P3PLD3LX7W6 --source msstore --exact
```

该命令通过 Microsoft Store 源安装同一个应用；首次使用时按提示确认即可。如果系统找不到 `winget`，请安装或更新微软商店中的「应用安装程序」，也可以直接使用上方商店链接。

### Windows：GitHub 便携版

需要免安装版本时，前往 [**GitHub Releases**](https://github.com/GlacierGlimmer/zmd-charge-plus/releases) 下载适合你设备的便携版 ZIP。解压后直接运行其中的 `EndfieldChargePlus.exe`，无需安装，也无需另装 .NET 运行时。

| 下载文件 | 适用设备 |
| :-- | :-- |
| `EndfieldChargePlus-v0.1.0-win-x64-portable.zip` | 常见 Intel / AMD 64 位 Windows 电脑（推荐） |
| `EndfieldChargePlus-v0.1.0-win-x86-portable.zip` | 适用于 32 位 Windows 设备 |

**首次使用只需几步：**

1. 通过 Microsoft Store 或 winget 安装并启动 ECP；使用 GitHub 便携版时，将 ZIP 解压到固定目录后运行 `EndfieldChargePlus.exe`。程序通过系统托盘驻留。
2. 从托盘菜单打开**设置**，在「显示与位置」中选择目标显示器、HUD 位置和显示方式。
3. 进入「HUD 内容与数据」，选择内置方案，或创建自己的 HUD；点击**预览**查看效果。
4. 点击**保存并应用**。配置自动保存，重新启动后继续使用；可按需开启 Windows 开机启动。

默认会以**系统 - 内存**方案启动；首次界面语言跟随 Windows：中文系统使用简体中文，其他系统使用 English，也可以手动切换。

### Linux：APT 命令安装（推荐 Debian / Ubuntu / Kali 等）

Linux 版名称为 **Endfield Charge Plus For Linux**，源码与详细文档位于 [Linux 独立仓库](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux)。本仓库的 [v0.1.0 Release](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/tag/v0.1.0) 同时提供 Linux 安装包。

在 Linux 终端中依次执行以下两条命令。第一条配置 [APT 软件源](https://apt.x-neko.com) 的签名密钥和源地址并刷新索引，第二条安装应用：

```bash
curl -fsSL https://apt.x-neko.com/install.sh | sudo bash
sudo apt-get install endfield-charge-plus-for-linux
```

目前提供 **amd64 / x86_64** 包。添加软件源后，更新应用：

```bash
sudo apt update
sudo apt install --only-upgrade endfield-charge-plus-for-linux
```

卸载应用：

```bash
sudo apt-get remove endfield-charge-plus-for-linux
```

安装后从应用菜单或运行 `endfield-charge-plus-for-linux` 启动。

### Linux：直接下载安装包

四种格式均包含 .NET 运行时，无需另装 .NET：

| 格式 | 下载 |
| :-- | :-- |
| `.tar.gz` | [EndfieldChargePlusForLinux-v0.1.0-linux-x64.tar.gz](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/EndfieldChargePlusForLinux-v0.1.0-linux-x64.tar.gz) |
| `.AppImage` | [EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage) |
| `.deb` | [EndfieldChargePlusForLinux-v0.1.0-linux-x64.deb](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/EndfieldChargePlusForLinux-v0.1.0-linux-x64.deb) |
| `.rpm` | [EndfieldChargePlusForLinux-v0.1.0-linux-x64.rpm](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/EndfieldChargePlusForLinux-v0.1.0-linux-x64.rpm) |

Linux 包使用 [`SHA256SUMS`](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/SHA256SUMS) 校验；Windows ZIP 使用 [`SHA256SUMS.txt`](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/SHA256SUMS.txt)。在 Linux 下载目录中可运行 `sha256sum --ignore-missing -c SHA256SUMS`。

```bash
# Debian / Ubuntu / Kali：手动安装 DEB
sudo apt install ./EndfieldChargePlusForLinux-v0.1.0-linux-x64.deb

# Fedora 等 RPM 系
sudo dnf install ./EndfieldChargePlusForLinux-v0.1.0-linux-x64.rpm

# AppImage
chmod +x EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage
./EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage

# tar.gz 便携包
tar -xzf EndfieldChargePlusForLinux-v0.1.0-linux-x64.tar.gz
cd EndfieldChargePlusForLinux-v0.1.0-linux-x64
./EndfieldChargePlus
```

AppImage 缺少 FUSE 时，可用 `./EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage --appimage-extract-and-run`。

Linux 要求 **x86_64、glibc 2.35+、OpenSSL 3、X11 或 XWayland**；Wayland 下的顶部唤出、置顶等行为受桌面合成器限制。当前不提供 ARM64、32 位、Alpine/musl 或纯原生 Wayland 版本。变量库按实际硬件能力筛选，移除 Windows 专属与未实现项目，不用假零值填补缺失传感器；可用变量数量因设备而异。完整依赖、功能和测试范围见 [Linux 说明](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/blob/main/README.md) 与 [验证记录](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/blob/main/docs/linux-validation.md)。

### macOS：DMG 安装包（Beta）

macOS 版名称为 **Endfield Charge Plus For MacOS**，源码、发布与后续适配位于 [macOS 独立仓库](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos)。当前发布版本为 **v0.1.1-beta**，支持 **macOS 13+**，并提供两种自包含 DMG，无需另装 .NET：

| 下载文件 | 适用设备 |
| :-- | :-- |
| [`EndfieldChargePlusForMacOS-v0.1.1-beta-osx-arm64.dmg`](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases/download/v0.1.1/EndfieldChargePlusForMacOS-v0.1.1-beta-osx-arm64.dmg) | Apple Silicon（M1 / M2 / M3 / M4 / M5 等，推荐） |
| [`EndfieldChargePlusForMacOS-v0.1.1-beta-osx-x64.dmg`](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases/download/v0.1.1/EndfieldChargePlusForMacOS-v0.1.1-beta-osx-x64.dmg) | Intel 64 位 Mac |

打开 DMG 后，将应用拖入 **Applications** 再启动。当前安装包采用 **ad-hoc 签名，未经过 Apple Developer ID 签名及公证**；首次启动时可能需要在「系统设置 → 隐私与安全性」中选择「仍要打开」。无需关闭 Gatekeeper 或 SIP。

> ⚠️ **当前 macOS 版本尚未进行真实 Mac 实机测试。** CI 会在 Apple Silicon 与 Intel macOS 环境执行自动化构建、原生数据采集、逐变量读取、方案渲染、界面、DMG 挂载、启动和单实例检查，但这些自动化验证不能替代真实硬件测试。若遇到机型、macOS 版本、刘海屏、多显示器、缩放、电池或权限相关问题，请在 [macOS 仓库 Issues](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/issues) 反馈。

macOS 版提供菜单栏驻留、点击穿透 HUD、中英双语、方案管理、时间、网络探测、HTTP/JSON 与 DeepSeek 等功能；系统数据使用 Mach / sysctl / IOPowerSources / APFS / Metal 等原生接口，并按实际检测能力筛选变量。完整功能、限制、校验与安装说明请查看 [macOS README](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/blob/main/README.md) 与 [macOS Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases)。

## 🎛️ 功能一览（Windows）

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

## 💻 Windows 运行要求与源码构建

- **系统：** Windows 10 / 11，支持 x64 和 x86。
- **Portable 包：** ZIP 内为自包含单文件 EXE，不需要另行安装 .NET。
- **自行编译：** Windows 环境及 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
# 编译源码
dotnet build EndfieldChargePlus.csproj -c Release

# 示例：发布 x64 自包含单文件版本
dotnet publish EndfieldChargePlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
```

构建 x86 时将 `win-x64` 换为 `win-x86`。

## 📄 来源与许可

ECP 基于 **[QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge)** 修改与扩展。感谢原作者 **QinAnze** 的开源工作；原项目的来源和许可在此明确保留。本项目新增贡献采用 [MIT License](LICENSE)。详情参见 [NOTICE.md](NOTICE.md)，数据处理说明参见 [PRIVACY.md](PRIVACY.md)。

© 2026 GlacierGlimmer_冰川雪貓
