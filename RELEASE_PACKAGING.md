# Endfield Charge Plus v0.1.0 发布结构

当前发布路线只保留两条：

1. 官网 / GitHub：三架构真·单文件 EXE。
2. Microsoft Store：x64 / x86 / ARM64 商店包；WinGet 直接通过 `msstore` 源安装商店版本。

## 官网 / GitHub

`dist/Portable/` 最终严格只包含：

```text
EndfieldChargePlus-v0.1.0-win-x64-portable.exe
EndfieldChargePlus-v0.1.0-win-x86-portable.exe
EndfieldChargePlus-v0.1.0-win-arm64-portable.exe
```

不再生成 ZIP、7z、Setup.exe 或 MSI。

`dist/SHA256SUMS.txt` 会同时生成，记录三个 EXE 的 SHA-256。

每个文件本身都是 self-contained 的单文件程序，无需用户另外安装 .NET Runtime，EXE 同目录不依赖 Assets、DLL、JSON 等旁文件。源代码仓库保留 `LICENSE`、`ORIGIN_NOTICE.md` 与 `THIRD_PARTY_NOTICES.md`。

## Microsoft Store / WinGet

Store 版本按 x64 / x86 / ARM64 维护，并由 Microsoft Store 负责安装、开始菜单注册、卸载和更新。

应用正式上架后，WinGet 不再维护独立的 `winget-pkgs` 安装器；直接通过 Microsoft Store (`msstore`) 源搜索和安装 Endfield Charge Plus。
