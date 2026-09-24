# Endfield Charge Plus · 终末地风格状态栏HUD+

**简体中文** | [English](README.en.md)

Endfield Charge Plus（ECP）是面向 Windows 的可自定义状态栏 HUD。项目基于 [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge) 进行二次开发，在原有电量 HUD 基础上修改并扩展了系统数据、界面、动画和自定义方案。

> 非官方社区项目，与《明日方舟：终末地》的开发方及发行方无隶属、授权或背书关系。

## 主要功能

- 电池、CPU、GPU、内存、磁盘、网络等系统信息与自定义 HUD 方案。
- 终末地风格动态 HUD；点击穿透、位置与透明度调整、预览及方案轮播。
- DeepSeek API 余额与峰谷时段、网络探测、自定义 HTTP/JSON 数据源。
- 简体中文 / English；首次运行按 Windows 系统语言自动选择。
- 系统托盘、开机自启、配置导入导出与更新检查。

## 下载与运行

正式版发布后，在 [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases) 下载对应架构的单文件 Portable EXE（`win-x64` / `win-x86` / `win-arm64`）。支持 Windows 10 / 11；官方发布的自包含单文件版本无需另装 .NET 运行时。

## 源码构建

使用 Windows 与 .NET 8 SDK：

```powershell
dotnet restore EndfieldChargePlus.csproj
dotnet build EndfieldChargePlus.csproj -c Release
```

如需自行发布 x64 自包含单文件 EXE：

```powershell
dotnet publish EndfieldChargePlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o publish/win-x64
```

其他架构将 `win-x64` 改为 `win-x86` 或 `win-arm64`。发布前仍需对相应架构进行实机测试。

## 变量说明

变量库内置变量具有相应采集或计算代码；能否取得有效数值取决于硬件、驱动、权限和第三方工具。详细变量 Key 与用法见程序内变量库；不可用时不伪造数据。DeepSeek 峰谷时段以北京时间为基准，另提供本地时区时间变量。

## 来源与许可

本项目基于 [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge) 修改扩展，感谢原作者 **QinAnze**。本项目新增贡献采用 [MIT License](LICENSE)，原项目代码与资源遵循其原有许可。来源、第三方依赖与许可见 [NOTICE.md](NOTICE.md)；隐私说明见 [PRIVACY.md](PRIVACY.md)。
