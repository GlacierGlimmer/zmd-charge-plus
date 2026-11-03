# Endfield Charge Plus · Endfield-style Status HUD+

See this repository’s [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases) for current downloads and per-version changes. This README covers installation and general usage.

Multiple HUDs, custom fonts and sampling intervals, persistent-click profile cycling, real FPS, World clock and grouped built-ins; see Settings and the [change log](docs/release-notes.md).

[简体中文](README.md) · **English**　｜　**Windows 10 / 11 · Linux x64 · macOS 13+**

> **More than a battery notification: bring the information you care about to the top of your screen.**

Endfield Charge Plus (**ECP**) is a desktop status HUD developed as a modification and extension of [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge). It builds on the original Endfield-inspired battery animation with persistent or on-demand monitoring, customizable profiles, system metrics, DeepSeek API information, and your own HTTP/JSON sources.

**[⬇️ Windows: Microsoft Store (recommended)](https://apps.microsoft.com/detail/9p3pld3lx7w6)** · **[Windows downloads](https://github.com/GlacierGlimmer/zmd-charge-plus/releases)** · **[macOS downloads](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases)** · **[Linux downloads](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/releases)** · **[Android releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-android/releases)** · **[🌐 Website](https://zmd-bar.x-neko.com/)** · **[Upstream project](https://github.com/QinAnze/zmd-charge)**

> Unofficial community derivative. Not affiliated with, authorized by, or endorsed by the developers or publishers of Arknights: Endfield.

This repository maintains the Windows edition. **[Endfield Charge Plus For Linux](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux)** is maintained separately with APT installation and four package formats. **[Endfield Charge Plus For MacOS](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos)** is also maintained in a separate repository and currently provides DMGs for both Apple Silicon and Intel Macs.

> ⚠️ **The macOS edition has not yet been tested on physical Mac hardware.** It has passed automated CI checks in macOS environments for building, native data collection, UI, DMG mounting and launch, but hardware/model-specific compatibility issues may still exist.

## ✨ What Plus adds

The comparison and feature details below describe the Windows edition. Linux and macOS behavior and available variables are adapted to each platform and device; see their installation sections and separate repositories.

| Area | Original focus | Endfield Charge Plus |
| :-- | :-- | :-- |
| Content | Battery and power events | System metrics, time, network probing, API and custom-data HUDs |
| Visibility | Brief event-triggered notification | **On-demand or persistent HUD**, optionally topmost or on the desktop layer |
| Interaction | Power connection changes | Keeps battery notifications; moving the pointer to the top center of the selected monitor can show the active profile |
| Customization | Battery display and animation | Profiles, variable templates, progress ring, left/right icons, color rules and animation modes |
| Multiple profiles | Battery-focused HUD | Built-in and custom profiles with an ordered automatic cycle queue |
| Data | Local battery information | **Built-in variables, 17 dynamic per-drive variable templates and detected fan sensors**, plus HTTP/JSON mapping |

## ⬇️ Download & first run

### Windows: Microsoft Store / winget (recommended)

ECP is now available on Microsoft Store. [**Get Endfield Charge Plus from Microsoft Store**](https://apps.microsoft.com/detail/9p3pld3lx7w6), follow the installation prompts, and launch the app when installation finishes.

You can also install it with **winget**. Paste and run this command in PowerShell or Windows Terminal:

```powershell
winget install --id 9P3PLD3LX7W6 --source msstore --exact
```

This installs the same app from the Microsoft Store source. Review any prompts shown on first use. If `winget` is not recognized, install or update App Installer from Microsoft Store, or use the Store link above.

### Windows: GitHub portable downloads

For a version that needs no installation, download the ZIP for x64, x86 or ARM64 from [**GitHub Releases**](https://github.com/GlacierGlimmer/zmd-charge-plus/releases). Extract the entire archive and run the versioned EXE in its version folder. The top level contains that EXE plus Runtime, Documents and Licenses. Keep the whole folder intact; no separately installed .NET runtime is required.

**Getting started:**

1. Install and launch ECP through Microsoft Store or winget. For the portable version, extract the ZIP to a permanent location and run the top-level versioned EXE. ECP stays available in the system tray.
2. Open **Settings** from the tray. Choose the display, position and visibility mode under **Display & Position**.
3. Under **HUD Content**, select a built-in profile or make your own, then use **Preview**. Variables, hardware and data sources are under **Data Management**.
4. Click **Save & Apply**. Settings persist across restarts; enable Windows auto-start if you want it.

The default profile is **System – Memory**. First launch follows the system UI language among the 20 supported languages, with Traditional Chinese script/region matching and an English fallback. A manual language choice is saved; you can select system language again later.

### Linux: independent Releases

**Endfield Charge Plus For Linux** is maintained and released in the [Linux repository](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux).

Open [**Linux Releases**](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/releases) for available versions, packages and checksums. APT installation, package selection, dependencies and validation details are maintained in the [Linux README](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux#readme).

### macOS: independent Releases

**Endfield Charge Plus For MacOS** is maintained and released in the [macOS repository](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos).

Open [**macOS Releases**](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases) for available versions, Apple Silicon / Intel DMGs and checksums. Installation, signing and compatibility details are maintained in the [macOS README](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos#readme).

### Android: independent repository

The [**Endfield Charge Plus For Android** repository](https://github.com/GlacierGlimmer/zmd-charge-plus-for-android) has been created. Available builds and release status will be listed on [**Android Releases**](https://github.com/GlacierGlimmer/zmd-charge-plus-for-android/releases).

Other platform versions are distributed by their respective repositories; their latest versions are not tied to the Windows release tag in this repository.

## 🎛️ Features (Windows)

| Feature | What it does |
| :-- | :-- |
| **On-demand / persistent** | Event/pointer-triggered display that retracts automatically, or an always-visible live HUD. |
| **Topmost / desktop layer** | Choose topmost or non-topmost desktop layer in persistent mode; the HUD is click-through. |
| **Power events** | On AC connection or disconnection, the battery profile takes priority with full or simple animation respectively. |
| **Pointer hot zone** | Move to the top center of the selected monitor to show the active non-battery profile while not in persistent mode. |
| **Display & layout** | Choose monitor, nine position presets or custom X/Y, scaling and opacity. |
| **Profiles & animation** | Built-in battery, CPU, GPU, memory, disk, network, time and DeepSeek profiles; create, edit, save and preview profiles with full/simple animations. |
| **Automatic cycle** | Arrange a cycle queue with add/remove/reorder controls, interval and transition animation settings. |
| **Variable Library** | Built-in variables, 17 types of per-drive templates and individual detected fan sensors for hardware, system, network, process, security and developer tools. |
| **Flexible layouts** | Variable-driven title, numbers, progress ring and icons; formatting, expressions and conditional color rules. |
| **Network probe** | Probe IPv4/IPv6/hostnames over ICMP, TCP or UDP; display latency and loss. |
| **DeepSeek API** | With your own API key: balance, peak/off-peak, progress and countdown. Beijing Time drives the schedule; local-time switch variables are available. |
| **HTTP / JSON** | Configure an endpoint, headers, refresh interval and JSON field mappings to display your own data. |
| **Everyday controls** | 20 UI languages, chapter-based User Manual, tray, startup, import/export/backup, logs and this platform's GitHub update checks. |

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

## 💻 Windows requirements & build

- **OS:** Windows 10 / 11, x64, x86 and ARM64.
- **Portable packages:** x64, x86 and ARM64 each have ZIP archives. The top-level versioned EXE starts the application directly from Runtime; Documents contains the guide/privacy/notices and Licenses retains the license texts. No runtime extraction or separate .NET installation is required.
- **User data:** settings, backups, logs and application caches use `%LOCALAPPDATA%\EndfieldChargePlus`, outside the extracted program folder. The Store edition uses its package LocalState. Explicit exports use the location selected by the user.
- **Building:** Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Build from source
dotnet build EndfieldChargePlus.csproj -c Release

# Publish all three directory portable builds, each as ZIP (requires 7-Zip)
./scripts/package-windows.ps1

# Publish x64 only
./scripts/package-windows.ps1 -Runtime win-x64
```

Replace `win-x64` with `win-x86` or `win-arm64` for the other architectures. The folder and entry point use the project version automatically; all dependencies remain under Runtime. Move the entire program folder together and re-enable startup from the new location if needed. See [Windows portable layout](docs/windows-portable.md). Frame capture selects the embedded x64 or ARM64 collector according to the Windows OS architecture.


Setup production is paused; its experimental implementation is outside this source repository. Microsoft Store submission bundles independently contain the full **x64, x86 and ARM64** application/runtime payloads. Build them with:

```powershell
./scripts/package-store.ps1
```

The script uses Windows SDK MakeAppx to validate and export MSIXBUNDLE/MSIXUPLOAD with fresh staging. As in v0.1.1, Store installation registers only Endfield Charge Plus itself and retains the original App/StartupTask identity. Windows/Microsoft Store handles uninstall; there are no separate administrator or uninstall application icons. Store approval for elevation and installed-package acceptance are separate from build validation. The user version policy maps software a.b.c to Store (a+1).b.c.0 (currently 0.1.2 to 1.1.2.0). A software major-version increment also increases the Store major component; submission still must exceed the highest applicable uploaded version.

## 📄 Origin & license

ECP is a modified derivative of **[QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge)**. Thanks to **QinAnze** for the original open-source work. New ECP contributions are under [MIT License](LICENSE); upstream code/assets retain applicable terms. See [NOTICE.md](NOTICE.md) and [PRIVACY.md](PRIVACY.md).

© 2026 GlacierGlimmer冰川雪貓


Windows GitHub Releases attach only the three architecture-specific portable ZIPs. Local builds also generate only ZIP files. `scripts/prepare-github-windows.ps1` stages only ZIPs locally and never uploads or publishes them.
