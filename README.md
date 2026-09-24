# Endfield Charge Plus

**简体中文** | [English](README.en.md)


Endfield Charge Plus 是基于 zmd-charge HUD 交互扩展的 Avalonia / .NET 8 Windows HUD 项目。保留原 HUD 胶囊布局和动画，同时加入系统状态、时间、DeepSeek API、自定义变量与方案等功能。

> **非官方项目说明：** Endfield Charge Plus 是社区项目，与《明日方舟：终末地》的开发方、发行方不存在隶属、授权或背书关系；相关游戏名称、标识与商标归各自权利人所有。

## 获取与安装

- **源码**：本仓库的 `main` 分支。请勿将 `dist/`、`bin/`、`obj/` 或本机的 `settings.json` 上传到源码仓库。
- **可执行文件**：正式发布后，在 [GitHub Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases) 下载对应架构的 Portable EXE。目前源代码上传和 Release 二进制发布是两个独立步骤。
- **系统要求**：Windows 10 / 11；Portable 成品使用 self-contained .NET 8 发布模式，不要求用户另装 .NET 运行时。高级硬件数据可能需要相应驱动或权限。
- **设置与隐私**：应用数据保存在 `%LOCALAPPDATA%\EndfieldChargePlus`，详见 [PRIVACY.md](PRIVACY.md)。

## 版本

当前产品版本为 **0.1.0**；程序集版本与文件版本均为 **0.1.0.0**。

## 本次功能

- 系统托盘常驻图标。
- 变量库：分类改为固定的功能顺序，中英文保持一致；分类内从常用状态/实时指标到详细信息排序；合并网络探测子分类，并明确标记 Ping 兼容变量与 ECP 应用变量。
- 变量实现完整性：所有内置变量都必须存在真实运行时生产代码；硬件、驱动或系统接口无法提供某项数据时显示不可用/`--`，不使用虚假占位值。发布构建会先运行 `verify-variable-coverage.ps1`，若变量库中出现没有实现代码的变量则直接中止构建。
- 左键或右键单击托盘图标都会打开同一套自定义深色菜单。
- 托盘菜单使用上游项目的视觉规格：`#1E1E20` 背景、`#2A2A2C` 描边、圆角与 hover 高亮。
- 托盘菜单项目：`预览 HUD`、`设置`、`退出`。
- 关闭设置窗口后程序继续驻留托盘，不会退出。
- HUD 整个原生窗口强制鼠标穿透：HUD 下方的窗口、按钮、标题栏和其他可交互区域仍可直接点击、拖动和滚动。


## 变量库

变量库现在分为“核心变量 + 进阶变量 + 动态盘符变量 + 自定义 HTTP/JSON 变量”。本版坚持 **只注册存在运行时生产/计算代码的变量**：驱动或硬件不提供某个传感器时，该变量会显示为不可用（模板为 `--`），不会用固定值或伪造值占位。

已加入的进阶数据包括：

- 电池：预计耗尽/充满时间、健康度、温度（硬件支持时）、电池化学类型。
- CPU：1/5/15 分钟滚动平均、运行期峰值、温度、功率、核心平均温度、电压、总线频率、指令/上下文切换/中断/DPC/系统调用等性能计数。
- 内存：Standby/Modified、硬件保留、内存频率、插槽数量/已用插槽、形态和 DDR/LPDDR 类型。
- GPU：热点/显存温度、实时功率、功率限制（支持时）、电压、核心/显存频率、PCIe 代际/通道、驱动版本/日期、VBIOS、NVIDIA/AMD 工具可用状态。
- 网络：Wi-Fi 信号近似 dBm、公共 IPv4/IPv6、VPN、系统代理、DNS 解析延迟、TCP/UDP 端点数、Wi-Fi 信道/频段/标准。公共 IP 变量仅在实际使用时查询外部 IP 服务并缓存。
- 磁盘：每个固定盘符动态生成实时读写/总 I/O、活动率、队列、健康、温度、通电时间、TRIM、存储状态和分区数量。盘符不限 C/D。
- 系统：电源计划、BIOS、主板、Windows Update、Defender、防火墙、BitLocker、Hyper-V、WSL、主板温度和风扇（传感器支持时）。
- 进程：后台进程数量、CPU/内存/磁盘/GPU 当前最高进程。
- 应用自身：当前主题、当前方案、本程序 GPU 使用率，以及原有 PID/内存/线程/句柄/CPU 时间。
- 显示器：主显示器名称/色深/刷新率、第二显示器名称/分辨率/刷新率，以及原有多屏/DPI 数据。
- 时间：纽约/伦敦/东京/北京世界时间，加上原有日/周/月/年进程和目标时间。
- 网络包探测器：DNS 解析时间、TCP 建连时间，并保留 ICMP/TCP/UDP 延迟、丢包、抖动等变量。
- DeepSeek API：峰谷判定固定以北京时间（UTC+08:00）为基准，并同时提供北京时间与 Windows 本地时区的下一次切换时间变量；本地时间自动考虑夏令时。
- 剪贴板：文本/图片/文件状态、文本长度/预览、图片尺寸、文件数量、最近变化时间。
- USB/外设：USB 设备/可移动存储数量和列表、鼠标/键盘名称、XInput 手柄数量/名称/电量（可读时）。
- 开发者工具：Docker、WSL、Git、Node/Python/Java/Go/Rust 版本、VS Code/终端/IDE、本地 LLM 进程状态。
- 安全：Defender、Firewall、BitLocker、Secure Boot、TPM、UAC、SmartScreen、Windows Update、VPN、代理。

### 未注册为占位变量的建议项

以下建议目前**没有**塞进变量库，因为在当前工程和 Windows 通用接口下不能保证得到真实、统一、可解释的数据：内存 CL/tRCD/tRP/tRAS 时序、磁盘累计主机写入字节、单进程网络流量排行、本程序插件统计/网络流量/历史崩溃统计、显示器夜间模式/色彩空间/HDR、没有实际计时器状态的 stopwatch/timer、探测器 HTTPS/HTTP 指标、DeepSeek 账户级 token/费用/请求历史、环境 CO₂/湿度/光照等传感器、厂商专属鼠标 DPI/电池/键盘灯效/耳机电量、天气、系统音频/SMTC 媒体状态。后续只有在加入对应真实数据源或配置接口后才会注册。

### 模板格式与运算

除原有 `kb:n`、`mb:n`、`gb:n`、`tb:n`、`speed`、`mbps:n`、`kbps:n`、`percent:n`、`duration`、`duration-long` 外，现在支持连续管道处理：

```text
{cpu.usage|math:mul:2|math:round}
{memory.used_bytes|auto:1}
{system.boot_time|time:relative}
{time.datetime|time:yyyy-MM-dd}
{cpu.name|sub:0:16}
{probe.status_text|replace:在线:OK}
```

数学：`math:add` / `sub` / `mul` / `div` / `round` / `floor` / `ceil` / `abs`。
时间：`time:relative` 或任意 .NET 日期时间格式。
智能单位：`auto` / `auto:n`。
文本：`sub:start:length`、`replace:A:B`、`upper`、`lower`。

完整变量名称、说明、类型、单位、推荐位置和格式均可直接在应用内“变量库”查看与复制。

## 设置界面补充

- “尺寸与动画”卡片右上角提供“恢复默认”，只恢复 HUD 缩放、总时长、回弹强度、波纹强度和波纹幅度，不影响位置、方案或不透明度。
- HUD 不透明度放在“显示方式”下方，范围 10%–100%；100% 为完全显示/完全不透明，数值越低越透明，首次安装默认 100%。
- “关于”页展示软件图标、`终末地风格状态栏 HUD`、Windows x64/x86/ARM64、版本 `v0.1.0` 与构建日期 `2026.09.23`。
- 关于页支持从 GitHub `GlacierGlimmer/zmd-charge-plus` 检查最新 Release；若尚无 Release，则回退检查最新版本标签，并显示当前版本、线上版本与检查状态。
- 关于页项目与协议信息合并为一个卡片：MIT、本项目 GitHub、原项目与项目网站。

## Visual Studio 运行

1. 安装 Visual Studio 2022 与 .NET 8 SDK，并勾选“.NET 桌面开发”。
2. 打开 `EndfieldChargePlus.sln`。
3. 等待 NuGet 恢复后按 `F5`。

## VS Code 运行

安装 .NET 8 SDK 与 C# Dev Kit，然后在工程目录运行：

```powershell
dotnet restore
dotnet build
dotnet run --project .\EndfieldChargePlus.csproj
```

也可以运行 `run-vscode.bat`。

## 界面语言

- 设置窗口顶部提供 `简体中文 | English` 分段语言切换。
- 未手动选择时，启动会读取 Windows UI 语言：任意 `zh-*`（简体/繁体中文）统一使用简体中文，其余语言默认 English。
- 用户手动选择后会立即保存语言偏好，后续启动保持该选择。
- 设置界面、托盘菜单、内置方案、内置 HUD、状态提示、变量库以及 HTTP/JSON 默认注释示例均跟随语言切换。
- 用户自行填写的自定义方案名称、模板和外部 HTTP/JSON 数据保持原文，不自动改写用户内容。

## Release 构建

当前正式发布构建只生成三架构单文件 EXE，不再生成 ZIP / 7z / Setup.exe / MSI。

在项目根目录运行：

```powershell
.\build-release.ps1
```

最终输出：

```text
dist\Portable\EndfieldChargePlus-v0.1.0-win-x64-portable.exe
dist\Portable\EndfieldChargePlus-v0.1.0-win-x86-portable.exe
dist\Portable\EndfieldChargePlus-v0.1.0-win-arm64-portable.exe
dist\SHA256SUMS.txt
```

每个 EXE 都是 self-contained 真·单文件版本；`SHA256SUMS.txt` 用于发布后校验下载文件完整性。Microsoft Store / WinGet(msstore) 发布链路单独维护。


## 源码仓库与正式发行

首次上传源代码请先阅读 [GitHub Desktop 上传指南](docs/GITHUB_FIRST_UPLOAD.md)。本仓库不包含编译后的 EXE、用户配置、API Key 或数字签名证书。正式 Release 的二进制文件与 `SHA256SUMS.txt` 应作为 GitHub Release 附件上传，而不是提交进 Git。

源码提交到 `main` 会触发 Windows 编译检查；此检查**不会**自动创建 Release、打 tag 或签名程序。

## 许可证与来源

- Endfield Charge Plus：MIT，见 `LICENSE`。
- 上游 HUD 来源与归属说明：见 `ORIGIN_NOTICE.md`。
- 第三方依赖和字体许可：见 `THIRD_PARTY_NOTICES.md`。
- 本地数据与网络访问说明：见 `PRIVACY.md`。
