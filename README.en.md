# Endfield Charge Plus

**v0.2.0 · 2026.11.07** · Windows 10 / 11 · x64 / x86 / ARM64

[简体中文](README.md) · **English**

An Endfield-style island HUD for system metrics, time, network information and your own data.

## Features in v0.2.0

- Redesigned dark settings with a vertical navigation rail, a fluorescent-yellow accent and three-colour details. System usage and the application’s overall usage are shown separately.
- Profile management, template variables, HTTP / JSON sources and a chapter-based offline manual.
- Twenty bundled offline languages, system-language selection and adjustable global font/UI scale.
- Consistent error and data prompts with concrete reasons, copyable details, log access and per-variable dismissal.
- Hold the middle mouse button on the HUD, point to a profile and release to switch. Hold delay, drag distance and ring size are configurable.
- Manual and automatic cycling share a queue. Clicking moves forward; scrolling up/down selects the previous/next profile with the corresponding transition direction.
- Four cycle animations: Simple, Full, Roll Up and Roll Down. Bind show/hide, previous/next, pause/resume, preview and Settings actions.
- Multiple desktop HUDs retain independent profiles, queues, placement, fonts, displays and layers while sharing global settings.

Windows uses LibreHardwareMonitor / PawnIO for supported low-level sensors and can read data exported by AIDA64. Sensor availability depends on hardware, drivers and permissions. See Hardware Data for setup and diagnostics. No proprietary CPUID SDK is bundled.

## Download and update

Download the matching x64, x86 or ARM64 `portable.zip` from [v0.2.0 Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases/tag/v0.2.0). Extract the entire archive and run `EndfieldChargePlus0.2.0/EndfieldChargePlus0.2.0.exe`. Keep `Runtime`, `Documents` and `Licenses` beside the executable. The launcher explains how to recover missing companion files.

Settings remain in the system user-data directory. To update, exit the old application and extract the complete new package into a new empty folder.

[Microsoft Store](https://apps.microsoft.com/detail/9p3pld3lx7w6) and `winget install --id 9P3PLD3LX7W6 --source msstore --exact` are also available; the Store controls its own available version and rollout. Windows portable releases contain ZIP archives, with no Setup, 7z or standalone single-file executable. The three-architecture Store package retains the existing application and startup identities.

SHA-256 checksum files are provided with Releases. About checks this platform’s own release repository; Microsoft Store installations use the Store’s update process.

## Supported languages

**20 languages:** Simplified Chinese, English, Traditional Chinese, Japanese, Korean, German, French, Spanish, Portuguese, Russian, Arabic, Hindi, Bengali, Urdu, Indonesian, Vietnamese, Thai, Turkish, Italian, Persian.

The `.lang` language packs work offline. The initial selection follows the system UI language; a manual selection is saved. Unsupported languages fall back to English. Language labels do not restrict use to a particular region. **Endfield Charge Plus** is never translated.

## Getting started

Select and preview a built-in profile, adjust its position and content, and use **Save & Apply**. Open the in-app **User Manual** for templates, data sources, rules, permissions and troubleshooting. Hardware and OS restrictions may make individual metrics unavailable; the application reports the reason rather than fabricating a value.

## Platform repositories

| Platform | Source | Downloads |
| --- | --- | --- |
| Windows | [GitHub](https://github.com/GlacierGlimmer/zmd-charge-plus) | [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus/releases) |
| Linux | [GitHub](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux) | [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-linux/releases) |
| macOS | [GitHub](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos) | [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-macos/releases) |
| Android | [GitHub](https://github.com/GlacierGlimmer/zmd-charge-plus-for-android) | [Releases](https://github.com/GlacierGlimmer/zmd-charge-plus-for-android/releases) |

## Project

Maintained by **GlacierGlimmer冰川雪貓**. [Website](https://zmd-bar.x-neko.com/) · [Issue tracker](https://ecp-bugs.x-neko.com/) · [Release notes](docs/release-notes.md) · [BiliBili](https://space.bilibili.com/354548348) · [YouTube](https://www.youtube.com/@glacierglimmer925).

MIT licensed, based on [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge). Third-party notices are retained. This is an unofficial community project and is not affiliated with or endorsed by the game’s developer or publisher.
