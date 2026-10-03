# Endfield Charge Plus · Endfield-style Status HUD+

[简体中文](README.md) · **English**　｜　**Windows 10 / 11 · Linux x64 · macOS 13+ · v0.1.0**

> **More than a battery notification: bring the information you care about to the top of your screen.**

Endfield Charge Plus (**ECP**) is a desktop status HUD developed as a modification and extension of [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge). It builds on the original Endfield-inspired battery animation with persistent or on-demand monitoring, customizable profiles, system metrics, DeepSeek API information, and your own HTTP/JSON sources.

**[⬇️ Windows: Microsoft Store (recommended)](https://apps.microsoft.com/detail/9p3pld3lx7w6)** · **[Windows / Linux downloads](https://github.com/GlacierGlimmer/zmd-charge-plus/releases)** · **[macOS downloads](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases)** · **[🌐 Website](https://zmd-bar.x-neko.com/)** · **[Upstream project](https://github.com/QinAnze/zmd-charge)**

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
| Data | Local battery information | **429 fixed built-in variables + 17 dynamic per-drive variable templates**, plus HTTP/JSON mapping |

## ⬇️ Download & first run

### Windows: Microsoft Store / winget (recommended)

ECP is now available on Microsoft Store. [**Get Endfield Charge Plus from Microsoft Store**](https://apps.microsoft.com/detail/9p3pld3lx7w6), follow the installation prompts, and launch the app when installation finishes.

You can also install it with **winget**. Paste and run this command in PowerShell or Windows Terminal:

```powershell
winget install --id 9P3PLD3LX7W6 --source msstore --exact
```

This installs the same app from the Microsoft Store source. Review any prompts shown on first use. If `winget` is not recognized, install or update App Installer from Microsoft Store, or use the Store link above.

### Windows: GitHub portable downloads

For a version that needs no installation, get the Portable ZIP for your device from [**GitHub Releases**](https://github.com/GlacierGlimmer/zmd-charge-plus/releases). Extract it to a folder and run `EndfieldChargePlus.exe`. No installer or separately installed .NET runtime is required.

| Download | Device |
| :-- | :-- |
| `EndfieldChargePlus-v0.1.0-win-x64-portable.zip` | Common Intel / AMD 64-bit Windows PCs (recommended) |
| `EndfieldChargePlus-v0.1.0-win-x86-portable.zip` | For 32-bit Windows devices |

**Getting started:**

1. Install and launch ECP through Microsoft Store or winget. For the GitHub portable version, extract the ZIP to a permanent folder and run `EndfieldChargePlus.exe`. ECP stays available in the system tray.
2. Open **Settings** from the tray. Choose the display, position and visibility mode under **Display & Position**.
3. Under **HUD Content & Data**, select a built-in profile or make your own, then use **Preview**.
4. Click **Save & Apply**. Settings persist across restarts; enable Windows auto-start if you want it.

The default profile is **System – Memory**. On first launch, Windows UI language selects Simplified Chinese for Chinese locales and English for others; you can switch manually.

### Linux: install from APT (recommended for Debian / Ubuntu / Kali)

The Linux edition is **Endfield Charge Plus For Linux**. Its source and detailed documentation are in the [Linux repository](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux); Linux packages are also available in [this repository's v0.1.0 Release](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/tag/v0.1.0).

Run these two commands in a Linux terminal. The first configures the signing key and [APT repository](https://apt.x-neko.com) and refreshes the package index; the second installs the app:

```bash
curl -fsSL https://apt.x-neko.com/install.sh | sudo bash
sudo apt-get install endfield-charge-plus-for-linux
```

Packages are currently available for **amd64 / x86_64**. To update the app after adding the repository:

```bash
sudo apt update
sudo apt install --only-upgrade endfield-charge-plus-for-linux
```

To uninstall:

```bash
sudo apt-get remove endfield-charge-plus-for-linux
```

Launch from the application menu or run `endfield-charge-plus-for-linux`.

### Linux: direct downloads

All four formats include the .NET runtime:

| Format | Download |
| :-- | :-- |
| `.tar.gz` | [EndfieldChargePlusForLinux-v0.1.0-linux-x64.tar.gz](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/EndfieldChargePlusForLinux-v0.1.0-linux-x64.tar.gz) |
| `.AppImage` | [EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage) |
| `.deb` | [EndfieldChargePlusForLinux-v0.1.0-linux-x64.deb](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/EndfieldChargePlusForLinux-v0.1.0-linux-x64.deb) |
| `.rpm` | [EndfieldChargePlusForLinux-v0.1.0-linux-x64.rpm](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/EndfieldChargePlusForLinux-v0.1.0-linux-x64.rpm) |

Use [`SHA256SUMS`](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/SHA256SUMS) for Linux packages and [`SHA256SUMS.txt`](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/download/v0.1.0/SHA256SUMS.txt) for Windows ZIPs. In your Linux download directory, run `sha256sum --ignore-missing -c SHA256SUMS`.

```bash
# Debian / Ubuntu / Kali: install a downloaded DEB
sudo apt install ./EndfieldChargePlusForLinux-v0.1.0-linux-x64.deb

# Fedora and other RPM distributions
sudo dnf install ./EndfieldChargePlusForLinux-v0.1.0-linux-x64.rpm

# AppImage
chmod +x EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage
./EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage

# Portable tar.gz
tar -xzf EndfieldChargePlusForLinux-v0.1.0-linux-x64.tar.gz
cd EndfieldChargePlusForLinux-v0.1.0-linux-x64
./EndfieldChargePlus
```

Without FUSE, use `./EndfieldChargePlusForLinux-v0.1.0-linux-x64.AppImage --appimage-extract-and-run`.

Linux requires **x86_64, glibc 2.35+, OpenSSL 3, and X11 or XWayland**. Top-edge activation and window stacking under Wayland depend on the compositor. ARM64, 32-bit, Alpine/musl and native Wayland builds are not provided. The Variable Library filters by actual hardware capabilities, excludes Windows-only and unimplemented items, and does not fabricate zero readings for missing sensors. Available variable counts vary by device. See the [Linux documentation](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/blob/main/README.en.md) and [validation scope](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/blob/main/docs/linux-validation.md).

### macOS: DMG packages (Beta)

The macOS edition is **Endfield Charge Plus For MacOS**. Source code, releases and platform-specific work are maintained in the [separate macOS repository](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos). The current release is **v0.1.1-beta**, requires **macOS 13+**, and provides two self-contained DMGs with no separate .NET runtime required:

| Download | Device |
| :-- | :-- |
| [`EndfieldChargePlusForMacOS-v0.1.1-beta-osx-arm64.dmg`](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases/download/v0.1.1/EndfieldChargePlusForMacOS-v0.1.1-beta-osx-arm64.dmg) | Apple Silicon (M1 / M2 / M3 / M4 / M5, recommended) |
| [`EndfieldChargePlusForMacOS-v0.1.1-beta-osx-x64.dmg`](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases/download/v0.1.1/EndfieldChargePlusForMacOS-v0.1.1-beta-osx-x64.dmg) | 64-bit Intel Macs |

Open the DMG, drag the app into **Applications**, then launch it. Current packages use **ad-hoc signing and are not signed/notarized with an Apple Developer ID**. On first launch, macOS may require choosing **Open Anyway** under System Settings → Privacy & Security. You do not need to disable Gatekeeper or SIP.

> ⚠️ **The current macOS edition has not yet been tested on physical Mac hardware.** CI runs automated checks on Apple Silicon and Intel macOS environments covering builds, native data collection, per-variable reads, profile rendering, UI, DMG mounting, launch and single-instance behavior, but this does not replace real-hardware testing. Please report model-, macOS-version-, notch-, multi-display-, scaling-, battery- or permission-related issues in the [macOS repository Issues](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/issues).

The macOS edition includes menu-bar residency, a click-through HUD, Chinese/English UI, profile management, time, network probing, HTTP/JSON and DeepSeek features. System data uses native Mach / sysctl / IOPowerSources / APFS / Metal APIs, with variables filtered by detected capabilities. See the [macOS README](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/blob/main/README.md) and [macOS Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases) for full details.

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

## 💻 Windows requirements & build

- **OS:** Windows 10 / 11, x64 and x86.
- **Portable packages:** each ZIP contains a self-contained EXE; no separately installed .NET runtime.
- **Building:** Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Build from source
dotnet build EndfieldChargePlus.csproj -c Release

# Example: publish a self-contained x64 single-file EXE
dotnet publish EndfieldChargePlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
```

Replace `win-x64` with `win-x86` to publish the x86 build.

## 📄 Origin & license

ECP is a modified derivative of **[QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge)**. Thanks to **QinAnze** for the original open-source work. New ECP contributions are under [MIT License](LICENSE); upstream code/assets retain applicable terms. See [NOTICE.md](NOTICE.md) and [PRIVACY.md](PRIVACY.md).

© 2026 GlacierGlimmer_冰川雪貓
