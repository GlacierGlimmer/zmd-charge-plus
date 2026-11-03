Endfield Charge Plus
使用说明 / Quick start

一、开始使用
1. 选择适合电脑的 x64、x86 或 ARM64 压缩包，完整解压到固定目录。
2. 双击顶层带版本号的 EndfieldChargePlus EXE。不要在压缩软件内直接运行。
3. 桌面启动请右键 EXE 创建快捷方式；不要把 EXE 单独复制或移动到桌面。
4. 从系统托盘打开设置，在左侧选择“显示与位置”“方案管理”“环形切换栏”“自动轮播”“变量库”等页面。
5. 点击“保存并应用”。完整功能说明可在设置中的“使用手册”按章节查阅。

设置界面固定为深色；黄色为操作强调色，页面标题旁带黄、绿、粉三段细线。语言页面仍提供全局字体大小与 UI 缩放。
在 HUD 上长按鼠标中键，拖向环形栏内的方案后松开即可切换；在中心或圆环外松开可取消。

二、目录说明
顶层 EXE：直接启动程序；无需额外安装 .NET，也不会先解压运行库。
Runtime：应用程序集、DLL、.NET 运行环境与配套文件。请完整保留。
其中 .deps.json、.runtimeconfig.json 是程序依赖和运行环境描述，不保存你的个人设置。
Documents：本说明、隐私说明及来源与第三方组件说明。
Licenses：本项目及所附运行环境、采集器、字体的许可原文。
移动时请移动整个版本文件夹，不要单独移动或改名 EXE、DLL 或 Runtime。

三、配置、升级与开机启动
日常配置、备份、日志和应用缓存位于当前用户的：
%LOCALAPPDATA%\EndfieldChargePlus
这些文件不会自动保存在解压目录或 Runtime 中。手动导出的文件保存在你选择的位置。
升级时先从托盘退出旧版，再完整解压并启动新版；原有用户配置继续使用。
移动目录或升级到新的版本文件夹后，如需开机启动，请在新位置运行并重新开启开机启动。
删除程序文件夹不会删除个人配置。微软商店版使用独立的系统应用数据目录。

四、权限、数据与退出
普通启动默认不提权。部分硬件数据或真实 FPS 采集需要管理员权限、设备支持或独立驱动。
无法读取的指标会显示不可用；请先查看“数据管理”和相应提示。
网络功能及密钥处理见 PRIVACY.md；共享配置或日志前请先检查其中的敏感内容。
请通过托盘菜单退出 ECP，再移动、覆盖或删除程序目录。

五、项目与反馈
源码与更新：https://github.com/GlacierGlimmer/zmd-charge-plus
问题反馈：https://github.com/GlacierGlimmer/zmd-charge-plus/issues
项目网站：https://zmd-bar.x-neko.com
开发与维护：GlacierGlimmer冰川雪貓

English

1. Choose x64, x86 or ARM64 and extract the entire archive to a permanent folder.
2. Run the versioned EndfieldChargePlus EXE in the top-level folder. Do not run it inside an archive viewer.
3. Use the tray to open Settings, customize the HUD, then choose Save & Apply. The in-app User Manual provides detailed chapters.

Settings use a fixed dark appearance with yellow accents and a slim yellow/green/pink marker. Global font and UI scaling remain available from the language page.
Hold the middle mouse button over the HUD, drag to a profile in the radial switcher, then release to select. Release in the centre or outside the ring to cancel.

Keep Runtime intact: it contains the application and its bundled .NET runtime. The top-level EXE starts the application directly without a separate launcher process or runtime extraction.
The .deps.json and .runtimeconfig.json files describe program dependencies/runtime requirements; they do not store personal settings.
Documents contains this guide, privacy information and third-party notices. Licenses contains the corresponding license texts.

Settings, backups, logs and application caches use %LOCALAPPDATA%\EndfieldChargePlus, outside the extracted program folder. Files you explicitly export use the location you select. The Microsoft Store edition uses its own application-data directory.

Exit ECP from its tray before upgrading or moving the entire program folder. Existing user settings are retained. After changing the EXE location, re-enable startup from the new copy if desired. Deleting the program folder does not delete user settings.

Normal launch does not request elevation. Some hardware sensors and real FPS collection require administrative rights, device support or a separate driver. Unavailable readings are reported as unavailable.
Read PRIVACY.md before using network sources or sharing settings and logs. Project, update and issue links are listed above.

Developer and maintainer: GlacierGlimmer冰川雪貓

文件缺失时怎么办 / Missing accompanying files

EXE 不是单文件版。启动前会检查 Runtime；缺少或截断文件时，即使主界面无法加载，也会直接显示恢复说明。提示列出缺失文件、当前 EXE 及预期 Runtime 位置。
将 EXE 移回原版本文件夹，或重新完整解压 ZIP，保留所有原有目录。桌面等位置只放快捷方式。
重新解压只恢复程序文件，不会清除用户数据目录里的配置。

The EXE is not a single-file edition. Before .NET starts, it checks the accompanying Runtime files and shows recovery instructions even when the main interface cannot load. The message identifies missing/incomplete files, the current EXE location and the expected Runtime folder.
Move the EXE back to its original version folder or fully extract the ZIP again, retaining the complete folder structure. Put a shortcut on the desktop instead of copying the EXE alone. Re-extracting the program does not erase settings in your user data folder.
