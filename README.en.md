# Endfield Charge Plus · Endfield-style Status HUD+

[简体中文](README.md) | **English**

Endfield Charge Plus (ECP) is a customizable Windows status HUD, modified and expanded from [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge). It extends the original battery HUD with system data, UI, animation, and customizable profiles.

> This is an unofficial community project, not affiliated with, authorized, or endorsed by the developers or publishers of Arknights: Endfield.

## Features

- Battery, CPU, GPU, memory, disks, networking, and customizable HUD profiles.
- Endfield-inspired animation; click-through, positioning, opacity, preview, and profile cycling.
- DeepSeek API balance and peak/off-peak status, network probing, custom HTTP/JSON sources.
- Simplified Chinese / English; initial language follows Windows UI language.
- Tray controls, startup option, configuration import/export, and update checks.

## Download

Once available, get the architecture-specific Portable EXE (`win-x64`, `win-x86`, or `win-arm64`) from [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases). Windows 10/11; official self-contained single-file builds do not need a separately installed .NET runtime.

## Build from source

On Windows with the .NET 8 SDK:

```powershell
dotnet restore EndfieldChargePlus.csproj
dotnet build EndfieldChargePlus.csproj -c Release
```

To publish a self-contained single-file x64 EXE:

```powershell
dotnet publish EndfieldChargePlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o publish/win-x64
```

Replace `win-x64` with `win-x86` or `win-arm64` for other architectures. Test on appropriate Windows hardware before public release.

## Variables

The built-in Variable Library contains corresponding collector/computation code. Actual values depend on hardware, drivers, permissions and third-party tools; unavailable readings are not fabricated. DeepSeek peak/off-peak rules use Beijing Time, with separate local-time variables.

## Origin and license

This project modifies and expands [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge); thanks to its author **QinAnze**. New contributions are under [MIT License](LICENSE); upstream code/assets retain applicable upstream terms. See [NOTICE.md](NOTICE.md) and [PRIVACY.md](PRIVACY.md).
