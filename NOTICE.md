# Notices / 来源与第三方许可

## 原项目 / Upstream

Endfield Charge Plus is a modified derivative of the HUD project
[QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge).
本项目基于 QinAnze 的 zmd-charge 进行二次开发，保留原项目来源和署名。

The original project's README identifies its license as MIT. The upstream
repository did not expose a standalone LICENSE file at the time this notice
was prepared; no unverified original copyright wording is asserted here.
The MIT LICENSE in this repository covers GlacierGlimmer's contributions;
original code/assets remain subject to their applicable upstream terms.

## Third-party components / 第三方组件

### Avalonia UI 11.2.1


Direct packages:

- `Avalonia` 11.2.1
- `Avalonia.Desktop` 11.2.1
- `Avalonia.Themes.Fluent` 11.2.1
- `Avalonia.Fonts.Inter` 11.2.1

Avalonia is distributed under the MIT license.

Project: https://github.com/AvaloniaUI/Avalonia

### Inter typeface

`Avalonia.Fonts.Inter` bundles the Inter typeface used by the application UI.

- Typeface: Inter
- Copyright: The Inter Project Authors
- License: SIL Open Font License 1.1 (OFL-1.1)
- Project: https://github.com/rsms/inter

The typeface is used unmodified by Endfield Charge Plus.

### Microsoft .NET packages

- `System.Management` 10.0.2 — MIT
- `System.Security.Cryptography.ProtectedData` 8.0.0 — MIT

Project: https://github.com/dotnet/runtime

### LibreHardwareMonitorLib 0.9.6

- Package: `LibreHardwareMonitorLib` 0.9.6
- License: Mozilla Public License 2.0 (MPL-2.0)
- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

It is used for hardware sensor values that Windows does not expose through a stable generic API, such as supported CPU/GPU temperatures, clocks, voltage, power, motherboard temperature and fan sensors. If a sensor is not exposed by the machine or driver, Endfield Charge Plus leaves the related value unavailable rather than manufacturing a value.

LibreHardwareMonitor itself includes additional third-party components under their own terms; see the upstream project's third-party notices for those transitive components.

### Runtime / transitive dependencies

Self-contained builds also carry the .NET runtime and transitive dependencies brought in by the packages above. Their original license metadata and upstream notices remain applicable. When preparing a public binary release, keep this notice together with the source release information and retain all license files required by the corresponding upstream components.
