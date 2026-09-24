# Third-party notices

Endfield Charge Plus uses open-source components. This file records the direct packages and the bundled UI typeface used by the current v0.1.0 source tree.

## Avalonia UI 11.2.1

Direct packages:

- `Avalonia` 11.2.1
- `Avalonia.Desktop` 11.2.1
- `Avalonia.Themes.Fluent` 11.2.1
- `Avalonia.Fonts.Inter` 11.2.1

Avalonia is distributed under the MIT license.

Project: https://github.com/AvaloniaUI/Avalonia

## Inter typeface

`Avalonia.Fonts.Inter` bundles the Inter typeface used by the application UI.

- Typeface: Inter
- Copyright: The Inter Project Authors
- License: SIL Open Font License 1.1 (OFL-1.1)
- Project: https://github.com/rsms/inter

The typeface is used unmodified by Endfield Charge Plus.

## Microsoft .NET packages

- `System.Management` 10.0.2 — MIT
- `System.Security.Cryptography.ProtectedData` 8.0.0 — MIT

Project: https://github.com/dotnet/runtime

## LibreHardwareMonitorLib 0.9.6

- Package: `LibreHardwareMonitorLib` 0.9.6
- License: Mozilla Public License 2.0 (MPL-2.0)
- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

It is used for hardware sensor values that Windows does not expose through a stable generic API, such as supported CPU/GPU temperatures, clocks, voltage, power, motherboard temperature and fan sensors. If a sensor is not exposed by the machine or driver, Endfield Charge Plus leaves the related value unavailable rather than manufacturing a value.

LibreHardwareMonitor itself includes additional third-party components under their own terms; see the upstream project's third-party notices for those transitive components.

## Runtime / transitive dependencies

Self-contained builds also carry the .NET runtime and transitive dependencies brought in by the packages above. Their original license metadata and upstream notices remain applicable. When preparing a public binary release, keep this notice together with the source release information and retain all license files required by the corresponding upstream components.
