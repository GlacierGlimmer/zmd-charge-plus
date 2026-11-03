# Endfield Charge Plus

来源、署名与第三方组件 / Origin, attribution and third-party components

## 项目来源 / Origin

本项目基于 [QinAnze/zmd-charge](https://github.com/QinAnze/zmd-charge) 修改和扩展。保留原项目 **QinAnze** 的来源与署名；上游 README 标示 MIT。ECP 自有新增贡献的许可见本仓库 LICENSE；原始代码、资源及第三方组件各自适用的版权和许可继续有效。

Endfield Charge Plus is a modified derivative of QinAnze/zmd-charge. Original attribution to QinAnze is retained; the upstream README identifies MIT. This repository's LICENSE covers ECP's own additions. Original code/assets and third-party components remain subject to their applicable terms.

本项目为社区独立项目，与《明日方舟：终末地》的开发、发行方不存在官方授权或隶属关系。相关商标属于各自权利人。

This is an independent community project, without official affiliation or endorsement from the developers/publishers of Arknights: Endfield. Trademarks belong to their respective owners.

开发与维护 / Developer and maintainer: **GlacierGlimmer冰川雪貓**  
项目网站 / Website: https://zmd-bar.x-neko.com

## 桌面 UI 与运行环境 / Desktop UI and runtime

- Filee 圆盘几何代码：Copyright (c) 2026 Myeonggu Kim (KnifeLemon) and Filee contributors，MIT。https://github.com/KnifeLemon/Filee 。许可原文随包放在 Licenses/Filee-MIT.txt。

The radial geometry is adapted from Filee under MIT; its copyright and license text are preserved in Licenses/Filee-MIT.txt.

- Avalonia、Avalonia.Desktop、Avalonia.Themes.Fluent、Avalonia.Fonts.Inter：本项目使用 11.2.1，MIT。https://github.com/AvaloniaUI/Avalonia
- Inter 字体：The Inter Project Authors，SIL Open Font License 1.1。https://github.com/rsms/inter
- .NET 自包含运行环境及随其分发的组件：适用 Microsoft .NET 对应的许可与第三方声明。https://github.com/dotnet/runtime

The four Avalonia packages above are used under MIT. The bundled Inter typeface retains its OFL terms. Self-contained builds also carry the .NET runtime; retain its license and third-party notices when redistributing it.

## 离线字体 / Offline fonts

附带的 Noto Serif Tibetan、Noto Sans Gothic、Noto Sans Deseret 为未修改的字体，用于相应字符回退。版权属于各字体的 The Noto Project Authors；各字体对应的 SIL Open Font License 1.1、版权署名和保留名称以原文为准。

The unmodified Noto fallback fonts retain their original author notices and OFL terms. See Assets/Fonts for source details and the adjacent OFL texts; directory portable builds place them in Licenses/Fonts.


## Windows 传感器与帧率组件 / Windows sensors and frame capture

- System.Management 10.0.2、System.Security.Cryptography.ProtectedData 8.0.0：MIT，https://github.com/dotnet/runtime
- LibreHardwareMonitorLib 0.9.6：MPL-2.0，https://github.com/LibreHardwareMonitor/LibreHardwareMonitor 。其内部第三方组件继续适用各自上游条款。无法取得的传感器值显示不可用。
- Intel PresentMon 2.6.0：MIT，https://github.com/GameTechDev/PresentMon/tree/v2.6.0 。ECP 使用其 ETW 解码器，并加入输出窗口归属和小型支持适配；源码及构建说明见 native/ECPFrames，原许可在 Assets/PresentMon/LICENSE.txt，目录便携包中为 Licenses/PresentMon-LICENSE.txt。
- PawnIO：可选、独立安装的底层访问驱动。只有用户选择安装时才从官方项目下载安装程序并校验。https://github.com/namazso/PawnIO.Setup 。ECP 便携包不嵌入该驱动。
- AIDA64 接入读取用户启用的本机导出；AIDA64 程序及 CPUID 专有 SDK 均未随 ECP 分发，也不因接口接入而获得其授权。

LibreHardwareMonitor retains its MPL-2.0 and transitive-component terms. PresentMon retains its original MIT notice. PawnIO is a separate optional installation. AIDA64 and CPUID proprietary software/SDKs are not bundled.

媒体与事件呼出的设计参考社区贡献 https://github.com/GlacierGlimmer/zmd-charge-plus/pull/7 ，翻滚交互参考 https://github.com/GlacierGlimmer/zmd-charge-plus/issues/12 。ECP 按当前多 HUD 架构实现，Windows 媒体数据使用系统 GSMTC API。

## 许可文件与再分发 / License files and redistribution

本仓库 LICENSE、字体 OFL、采集器许可及运行环境附带的许可原文不因本文的排版或说明调整而改变。Windows 目录便携包将它们集中放在 Licenses；其他平台请保留对应安装包内的许可目录及资源文件。第三方组件的版本号与许可证版本用于识别实际组件及条款，不是 Endfield Charge Plus 的产品版本。

License texts and original copyright notices are preserved. Windows directory portable packages collect them under Licenses; other platforms retain their corresponding packaged resources. Third-party/library/license versions identify those components and terms, independently of the ECP product version.

隐私、本地数据位置、联网功能与权限说明见 PRIVACY.md。请在再分发时保留所需版权和许可，不要将本文当作替代第三方完整许可的授权文本。

See PRIVACY.md for data locations, network access and permissions. Retain required copyright/license files when redistributing; this explanatory index does not replace full third-party license terms.


## Platform artwork and motion

Ring geometry, staggered opening, hover springs and page-reveal timing: adapted from KnifeLemon/Filee, copyright (c) 2026 Myeonggu Kim and Filee contributors, MIT. The original license is retained.
- Windows: Microsoft standard four-square SVG, https://commons.wikimedia.org/wiki/File:Windows_logo_-_2021.svg (public-domain geometry; Microsoft trademark).
- macOS: Apple navigation SVG, https://www.apple.com/ ; cropped viewport, official light-on-dark fill. Apple trademark.
- Linux: Tux by Larry Ewing, vector by Garrett LeSage, refined by IFo Hancroft; https://github.com/garrett/Tux (CC0/public domain).
- Android: https://developer.android.com/static/images/brand/android-head_flat.svg . The Android robot is reproduced or modified from work created and shared by Google and used according to terms described in the Creative Commons 3.0 Attribution License. https://creativecommons.org/licenses/by/3.0/ . Android is a trademark of Google LLC.
SVG rendering: Avalonia.Svg.Skia / Svg.Skia (MIT, Wieslaw Soltes), Svg.Custom / SVG.NET (MS-PL), ExCSS (MIT); AndroidSVG (Apache-2.0, Paul LeBeau). https://github.com/wieslawsoltes/Svg.Skia ; https://github.com/BigBadaboom/androidsvg .

About UI SVG assets: Lucide (ISC, https://github.com/lucide-icons/lucide; original notice in Lucide-LICENSE.txt) and Simple Icons (CC0, https://github.com/simple-icons/simple-icons; original notice in SimpleIcons-LICENSE.txt). BiliBili/YouTube/GitHub marks identify their linked services. Endfield Charge Plus uses its own supplied SVG brand asset.
