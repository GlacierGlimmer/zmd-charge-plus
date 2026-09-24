# Endfield Charge Plus

[简体中文](README.md) | **English**

A customizable Windows HUD built with Avalonia and .NET 8. The application is derived from / inspired by the HUD implementation of [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge) and adds profiles, data sources, variable templates, localization, maintenance tools and extended animations.

> **Unofficial community project.** It is not affiliated with, authorized, or endorsed by the developers or publishers of Arknights: Endfield. Names, logos, and trademarks belong to their respective owners.

## Version and platforms

- Product version: **0.1.0**; assembly and file versions: **0.1.0.0**.
- Windows 10 / Windows 11; release targets: `win-x64`, `win-x86`, and `win-arm64`.
- The Portable EXE is published self-contained with .NET 8. Architecture-specific real-machine testing is required before advertising a finished public release.
- Interface languages: Simplified Chinese and English. Any Windows UI culture starting with `zh-` defaults to Simplified Chinese when no manual preference is saved; other UI cultures default to English.

## Features

- Non-interfering, click-through HUD with customizable content, placement, animation, opacity, and profiles.
- Battery, CPU, memory, GPU, disk, networking, network probing, process, time, device and system status variables (availability depends on hardware, Windows interfaces, permissions, and external tools).
- DeepSeek API balance and Beijing-time peak/off-peak logic, with local-time variables for the next schedule switch.
- Custom HTTP/JSON data sources, variable formatting and expressions.
- Tray menu, startup settings, configuration export/import/backup, logs, and GitHub update checks.
- The built-in variable catalog has a static producer-code coverage check. This is **not** a guarantee that every value can be read on every device.

## Source versus downloads

The `main` branch holds source code only. Once an official release is published, get a matching Portable EXE from [GitHub Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases). Never upload local configuration files or API credentials to the repository. Application data is stored at `%LOCALAPPDATA%\EndfieldChargePlus`.

## Build from source

Requires a Windows machine with the .NET 8 SDK. Open `EndfieldChargePlus.sln` in Visual Studio, or use PowerShell from the repository root:

```powershell
dotnet restore .\EndfieldChargePlus.csproj
dotnet build .\EndfieldChargePlus.csproj -c Release
.\build-release.ps1 -Runtime win-x64   # x64 test build
.\build-release.ps1                    # all configured architectures
```

The release script produces Portable EXEs in `dist/Portable/` and `dist/SHA256SUMS.txt`. These generated outputs are gitignored. Source commits trigger a Windows build check but do not publish a Release automatically.

## Privacy, licenses and attribution

See [PRIVACY.md](PRIVACY.md), [LICENSE](LICENSE), [ORIGIN_NOTICE.md](ORIGIN_NOTICE.md), [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), and [CHANGELOG.md](CHANGELOG.md).

For details of the original HUD project, visit [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge).
