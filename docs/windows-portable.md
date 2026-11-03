# Windows 目录便携版

当前提供 x64、x86、ARM64 三种架构，每种只提供 ZIP。选择适合设备的架构后，完整解压到固定目录。

```text
EndfieldChargePlus-v0.1.2-win-x64-portable.zip
└─ EndfieldChargePlus0.1.2
   ├─ EndfieldChargePlus0.1.2.exe
   ├─ Runtime/
   │  ├─ EndfieldChargePlus.dll
   │  ├─ EndfieldChargePlus.deps.json
   │  ├─ EndfieldChargePlus.runtimeconfig.json
   │  └─ DLL、完整 .NET 运行环境与配套文件
   ├─ Documents/
   │  ├─ README.txt
   │  ├─ PRIVACY.md
   │  └─ NOTICE.md
   └─ Licenses/
      ├─ LICENSE.txt、PresentMon-LICENSE.txt、README.txt
      ├─ DotNet/
      └─ Fonts/
```

文件夹与主 EXE 的名称均不含空格。顶层为本架构原生 apphost，直接加载 `Runtime/EndfieldChargePlus.dll`，与软件处于同一个进程；不会另起隐藏启动器，也不会每次先解压运行环境。DLL 和运行库保留原名并完整放在 Runtime，保持依赖的正常查找关系。

不要直接在压缩软件中运行，也不要只复制主 EXE。需要移动时，移动整个版本文件夹。需要管理员权限采集的项目，可以右键主 EXE，选择以管理员身份运行。

## 升级与开机启动

先从系统托盘退出旧版，再将新版解压到单独的版本文件夹后启动。个人配置、备份、日志与应用缓存继续使用 `%LOCALAPPDATA%\EndfieldChargePlus`，不会自动存储在解压目录或 Runtime 中。不要把配置复制进程序目录；手动导出的位置由用户自己选择。

开机启动登记的是实际 EXE 的绝对路径。移动目录或更换版本文件夹后，如需开机启动，请从新位置运行程序，并在设置里重新开启该选项。Windows 中由用户禁用的启动权限仍由系统管理，软件不会在启动时擅自恢复。

## 构建

需要 .NET SDK 和 7-Zip。构建脚本默认生成三架构的三个 ZIP 压缩包，也可以指定单一架构。

```powershell
./scripts/package-windows.ps1 -Output ../windows-products
./scripts/package-windows.ps1 -Runtime win-arm64 -Output ../windows-arm64-products
```

使用 `-SevenZipPath` 可以指定 7z.exe（仅用于生成 ZIP，不生成 .7z 成品）。每次使用全新构建暂存目录，拒绝覆盖已经存在的目标压缩包。输出包含 SHA-256 清单和构建记录；每个压缩包仅包含一个版本文件夹及其完整应用文件。

原生入口在加载 .NET 前检查 Runtime 文件是否齐全及长度是否正确。缺失、损坏或单独复制 EXE 时，显示随 EXE 内嵌的多语言恢复说明、当前 EXE 位置及预期 Runtime 路径；提示跟随系统界面语言。桌面启动请为主 EXE 创建快捷方式。

需要 Visual Studio C++ 构建工具（x64/x86/ARM64）。原生入口沿用 .NET apphost 的 self-contained 启动协议，在本进程中启动随包运行库。保留主程序图标、文件信息和普通权限清单。

当前暂停制作 Setup。商店包保持单个应用注册，安装、更新和卸载由 Windows/微软商店管理。

本地 ARM64 交叉编译工具未安装在系统时，可通过 `ECP_NATIVE_ARM64_COMPILER` 指定官方 ARM64 目标编译器，通过 `ECP_NATIVE_ARM64_LIB` 指定同工具集的 ARM64 C++ 链接库。构建时显式指定目标机器类型，并校验输出 PE 机器类型，禁止静默使用其他架构编译器。
