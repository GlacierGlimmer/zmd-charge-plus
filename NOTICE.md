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

Windows low-level sensor access additionally requires the separately installed PawnIO driver. ECP's explicit installation button downloads the unmodified official PawnIO 2.2.0 setup from https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0 and verifies its published SHA-256 before launching its own installer. PawnIO is not embedded in the ECP executable and is subject to its upstream terms. AIDA64 integration reads its documented external-application exports; AIDA64 and CPUID's proprietary SDKs are not included.

### Runtime / transitive dependencies

Self-contained builds also carry the .NET runtime and transitive dependencies brought in by the packages above. Their original license metadata and upstream notices remain applicable. When preparing a public binary release, keep this notice together with the source release information and retain all license files required by the corresponding upstream components.


Windows FPS collector: Intel PresentMon 2.6.0 (MIT), https://github.com/GameTechDev/PresentMon/tree/v2.6.0 . ECP uses its ETW decoder with an output-only window attribution extension and a small support adapter; see native/ECPFrames/README.md and Assets/PresentMon/LICENSE.txt.

媒体与事件呼出的设计参考社区贡献 https://github.com/GlacierGlimmer/zmd-charge-plus/pull/7 ，翻滚交互参考 https://github.com/GlacierGlimmer/zmd-charge-plus/issues/12 。本轮按当前多 HUD 架构重新实现；Windows 媒体读取使用系统 GSMTC API，不包含第三方媒体桥接程序。

### Noto Serif Tibetan

Bundled unmodified from https://github.com/google/fonts/tree/main/ofl/notoseriftibetan for Tibetan glyph fallback. Original font and license are retained in Assets/Fonts. Font SHA-256: 060ec022b04c306de3f58d051fb0e1cf81a5b610c5910fbfc43bba154c057cda.

Copyright 2022 The Noto Project Authors (https://github.com/notofonts/tibetan)

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
https://scripts.sil.org/OFL


-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded, 
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
