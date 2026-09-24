# Endfield Charge Plus · Endfield-style Status HUD+

[简体中文](README.md) · **English**　｜　**Windows 10 / 11 · v0.1.0**

> **More than a battery notification: bring the information you care about to the top of your screen.**

Endfield Charge Plus (**ECP**) is a Windows status HUD developed as a modification and extension of [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge). It builds on the original Endfield-inspired battery animation with persistent or on-demand monitoring, customizable profiles, system metrics, DeepSeek API information, and your own HTTP/JSON sources.

**[⬇️ Get the latest release](https://github.com/GlacierGlimmer/zmd-charge-plus/releases)** · **[🌐 Website](https://zmd-bar.x-neko.com/)** · **[Upstream project](https://github.com/QinAnze/zmd-charge)**

> Unofficial community derivative. Not affiliated with, authorized by, or endorsed by the developers or publishers of Arknights: Endfield.

## ✨ What Plus adds

| Area | Original focus | Endfield Charge Plus |
| :-- | :-- | :-- |
| Content | Battery and power events | System metrics, time, network probing, API and custom-data HUDs |
| Visibility | Brief event-triggered notification | **On-demand or persistent HUD**, optionally topmost or on the desktop layer |
| Interaction | Power connection changes | Keeps battery notifications; moving the pointer to the top center of the selected monitor can show the active profile |
| Customization | Battery display and animation | Profiles, variable templates, progress ring, left/right icons, color rules and animation modes |
| Multiple profiles | Battery-focused HUD | Built-in and custom profiles with an ordered automatic cycle queue |
| Data | Local battery information | **429 fixed built-in variables + 17 dynamic per-drive variable templates**, plus HTTP/JSON mapping |

## ⬇️ Download & first run

Get the build for your device from [**GitHub Releases**](https://github.com/GlacierGlimmer/zmd-charge-plus/releases). The planned distribution format is a **self-contained single-file Portable EXE**: no installer and no separate .NET runtime required.

| Download | Device |
| :-- | :-- |
| `EndfieldChargePlus-v0.1.0-win-x64-portable.exe` | Most 64-bit Intel / AMD Windows PCs |
| `EndfieldChargePlus-v0.1.0-win-x86-portable.exe` | 32-bit Windows PCs |
| `EndfieldChargePlus-v0.1.0-win-arm64-portable.exe` | Windows on ARM devices |

> Download files become available when the corresponding Release is published. Check the actual release page for available architectures.

**Getting started:**

1. Run the EXE for your architecture; ECP stays available in the system tray.
2. Open **Settings** from the tray. Choose the display, position and visibility mode under **Display & Position**.
3. Under **HUD Content & Data**, select a built-in profile or make your own, then use **Preview**.
4. Click **Save & Apply**. Settings persist across restarts; enable Windows auto-start if you want it.

The default profile is **System – Memory**. On first launch, Windows UI language selects Simplified Chinese for Chinese locales and English for others; you can switch manually.

## 🎛️ Features

| Feature | What it does |
| :-- | :-- |
| **On-demand / persistent** | Event/pointer-triggered display that retracts automatically, or an always-visible live HUD. |
| **Topmost / desktop layer** | Choose topmost or non-topmost desktop layer in persistent mode; the HUD is click-through. |
| **Power events** | On AC connection or disconnection, the battery profile takes priority with full or simple animation respectively. |
| **Pointer hot zone** | Move to the top center of the selected monitor to show the active non-battery profile while not in persistent mode. |
| **Display & layout** | Choose monitor, nine position presets or custom X/Y, scaling and opacity. |
| **Profiles & animation** | Built-in battery, CPU, GPU, memory, disk, network, time and DeepSeek profiles; create, edit, save and preview profiles with full/simple animations. |
| **Automatic cycle** | Arrange a cycle queue with add/remove/reorder controls, interval and transition animation settings. |
| **Variable Library** | 429 fixed built-in variables plus 17 types of per-drive templates for hardware, system, network, process, security and developer tools. |
| **Flexible layouts** | Variable-driven title, numbers, progress ring and icons; formatting, expressions and conditional color rules. |
| **Network probe** | Probe IPv4/IPv6/hostnames over ICMP, TCP or UDP; display latency and loss. |
| **DeepSeek API** | With your own API key: balance, peak/off-peak, progress and countdown. Beijing Time drives the schedule; local-time switch variables are available. |
| **HTTP / JSON** | Configure an endpoint, headers, refresh interval and JSON field mappings to display your own data. |
| **Everyday controls** | Chinese/English UI, tray, auto-start, import/export/backup, logs and GitHub update checks. |

### 🧩 Variables in action

Variables can populate templates and drive computed values or the progress ring:

```text
{cpu.usage|0}                 CPU utilization
{memory.used_bytes|gb:1}     Used memory (GB)
{network.download_bps|speed}  Network download speed
{deepseek.balance|0.00}      DeepSeek API balance
```

Built-in keys have corresponding runtime collection/calculation code, but actual availability depends on hardware, drivers, permissions, relevant services and external tools. Missing readings are shown as unavailable rather than fabricated. See the in-app Variable Library for the full key list, formats and explanations.

DeepSeek requires your API key; custom HTTP/JSON requests target endpoints you configure. See [Privacy](PRIVACY.md) for details on API keys and request headers.

## 💻 Requirements & build

- **OS:** Windows 10 / 11; build targets for x64, x86 and ARM64.
- **Official Portable builds:** self-contained EXEs; no separately installed .NET runtime.
- **Building:** Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Build from source
dotnet build EndfieldChargePlus.csproj -c Release

# Example: publish a self-contained x64 single-file EXE
dotnet publish EndfieldChargePlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
```

Replace `win-x64` with `win-x86` or `win-arm64` for other architectures and test on the relevant Windows hardware.

## 📄 Origin & license

ECP is a modified derivative of **[QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge)**. Thanks to **QinAnze** for the original open-source work. New ECP contributions are under [MIT License](LICENSE); upstream code/assets retain applicable terms. See [NOTICE.md](NOTICE.md) and [PRIVACY.md](PRIVACY.md).

© 2026 GlacierGlimmer_冰川雪貓
