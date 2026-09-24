# Endfield Charge Plus v0.1.0 发布检查
> First Windows verification step: run `publish-x64-test.bat` and test the generated x64 single-file EXE before building all architectures.

- [ ] `verify-variable-coverage.ps1` 通过：变量库中不存在仅声明、没有运行时生产代码的内置变量；动态磁盘变量模板也全部有生产代码。
## 三架构 Portable EXE

- [ ] Windows Explorer “Language” may display as language-neutral for the bilingual single binary; UI language support is Simplified Chinese + English and is tested in-app.
- [ ] For GitHub public EXE distribution, apply a trusted Authenticode signature if a production signing identity is available; do not use a self-signed certificate for public release.
- [ ] `EndfieldChargePlus.csproj`：Product Version = `0.1.0`，Assembly/File Version = `0.1.0.0`
- [ ] `app.manifest`：assemblyIdentity = `0.1.0.0`
- [ ] `app.manifest`：Windows 10/11 supportedOS GUID = `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`
- [ ] `LICENSE`、`ORIGIN_NOTICE.md`、`THIRD_PARTY_NOTICES.md`、`PRIVACY.md` 已检查
- [ ] `https://github.com/GlacierGlimmer/zmd-charge-plus` 已公开可访问，并可供应用更新检查读取 Release / Tag
- [ ] `win-x64` 发布目录严格只有 `EndfieldChargePlus.exe`
- [ ] `win-x86` 发布目录严格只有 `EndfieldChargePlus.exe`
- [ ] `win-arm64` 发布目录严格只有 `EndfieldChargePlus.exe`
- [ ] `dist/Portable` 最终严格只有三个文件：
  - `EndfieldChargePlus-v0.1.0-win-x64-portable.exe`
  - `EndfieldChargePlus-v0.1.0-win-x86-portable.exe`
  - `EndfieldChargePlus-v0.1.0-win-arm64-portable.exe`
- [ ] 不生成 ZIP / 7z / Setup.exe / MSI
- [ ] `dist/SHA256SUMS.txt` 包含三个最终 EXE 的 SHA-256
- [ ] x64 关于页显示 `Windows · x64`
- [ ] x86 关于页显示 `Windows · x86`
- [ ] ARM64 关于页显示 `Windows · ARM64`
- [ ] CompanyName / Publisher 元数据为 `GlacierGlimmer_冰川雪貓`
- [ ] 普通启动：设置窗口 + 一次 HUD 启动动画
- [ ] 开机启动：不弹设置，只播放 HUD 启动动画并按常驻状态继续
- [ ] 单实例：重复普通启动唤出已有设置窗口；重复开机启动静默退出
- [ ] 托盘图标正确
- [ ] 应用图标 PNG/ICO 为标准正方形资源；ICO 包含 16–256 px 常用尺寸
- [ ] HUD 鼠标穿透正常
- [ ] 配置保存、恢复、日志、数据源和方案功能正常

## 中英文 / Language

- [ ] 首次启动且语言未手动选择：Windows UI 语言为任意 `zh-*` 时默认简体中文
- [ ] 首次启动且语言未手动选择：非中文 Windows UI 语言时默认 English
- [ ] 顶栏语言控件显示 `简体中文 | English`，左侧中文、右侧 English
- [ ] 手动选择语言后立即保存，下次启动保持用户选择，不再跟随系统语言变化
- [ ] English 模式：设置界面、托盘菜单、内置方案名称、内置 HUD、状态提示、变量库和 HTTP/JSON 默认示例不残留应用内置中文
- [ ] 简体中文模式：设置界面、托盘菜单、内置 HUD 和状态提示恢复中文
- [ ] 用户自定义的方案名称/模板/HTTP 数据内容按用户原文保留，不擅自翻译

## Microsoft Store

- [ ] x64 Store 包
- [ ] x86 Store 包
- [ ] ARM64 Store 包
- [ ] Store 版开始菜单注册正常
- [ ] Store 版卸载正常
- [ ] Store 版更新由 Microsoft Store 管理
- [ ] 上架后验证 `winget search` / `winget install ... -s msstore`

## 架构实测

- [ ] x64 在 x64 Windows 实测
- [ ] x86 在 x86 或兼容 x64 Windows 实测
- [ ] ARM64 在 ARM64 Windows 实测
