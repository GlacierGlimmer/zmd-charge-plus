Endfield Charge Plus
微软商店版使用说明 / Microsoft Store edition

从开始菜单打开 Endfield Charge Plus，通过托盘进入设置与使用手册。
安装后只注册 Endfield Charge Plus 本体，和 v0.1.1 一样，不增加管理员或卸载应用图标。

安装目录按用途分类：
App/EndfieldChargePlus.exe：保留原位置的应用入口。
Runtime：主程序、完整 .NET 与 DLL。
Documents：使用、隐私和来源说明。
Licenses：许可原文；Assets 为系统应用图标。
安装、升级和卸载由 Windows/微软商店管理，请保持安装目录完整。

开机启动保留原应用的 EndfieldChargePlusStartup 任务标识及入口路径。
用户配置、日志与备份保存在该商店应用的用户 LocalState，不保存在受保护的安装目录或 Runtime 中。
手动导出使用你选择的位置；详细位置与密钥处理见 PRIVACY.md。
Runtime 中 .deps.json 和 .runtimeconfig.json 是运行环境描述，不保存个人设置。

Start Endfield Charge Plus from the Start menu and open Settings/User Manual from its tray.
As in v0.1.1, installation registers only Endfield Charge Plus itself, without separate administrator or uninstall app icons.

The original entry remains at App/EndfieldChargePlus.exe. Dependencies are under Runtime, with Documents, Licenses and system Assets in separate folders. Windows/Microsoft Store manages installation, updates and removal.
Login startup retains the original EndfieldChargePlusStartup identity and executable path.
Settings, logs and backups use this package's user LocalState, outside the installation and Runtime directories. Explicit exports use your selected location. Read PRIVACY.md for data and secret handling.

源码与反馈 / Source and issues: https://github.com/GlacierGlimmer/zmd-charge-plus
开发与维护 / Developer and maintainer: GlacierGlimmer冰川雪貓
