# Endfield Charge Plus

隐私与本地数据说明 / Privacy and local data — Windows

## 1. 数据处理原则 / Data handling

Endfield Charge Plus 未集成广告、产品分析遥测、应用账号系统或开发者运营的用户数据云端后台。HUD、预览、变量库和硬件页面所需的数据在本机读取和处理；内置更新检查不会上传 HUD 变量值、剪贴板内容、设备传感器读数或用户配置。

Endfield Charge Plus does not implement advertising, product analytics telemetry, an application account system or a developer-operated user-data cloud backend. HUD, preview, variable-library and hardware-page data is processed locally. Built-in update checks do not upload HUD values, clipboard content, sensor readings or user settings.

## 2. 配置与存储 / Settings and storage

本平台默认用户数据位置：`%LOCALAPPDATA%\EndfieldChargePlus`。配置、备份、日志和应用缓存使用用户数据位置，不自动写入解压后的程序目录。程序文件和用户数据相互独立；移动或替换程序文件不会自动清除个人设置。手动导出只写入用户选择的位置。

微软商店版使用 `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\EndfieldChargePlus`。卸载由 Windows／微软商店管理，按系统的应用数据删除规则处理；需要保留数据时可先手动导出。

Default data location: `%LOCALAPPDATA%\EndfieldChargePlus`. Settings, backups, logs and application caches use the user data location, independently of the extracted program folder. Moving/replacing application files does not automatically delete personal settings. Explicit exports are written to the location selected by the user. The Microsoft Store edition uses %LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\EndfieldChargePlus. Windows/Microsoft Store handles removal using the normal package-data rules; explicitly export data first if you want to retain it.

## 3. 密钥与导出 / Secrets and exports

Windows 使用当前用户范围的 DPAPI 保护 DeepSeek API Key。配置及导出文件保存受保护值；更换 Windows 用户或系统后通常需要重新输入。

Windows protects the DeepSeek API key with current-user DPAPI. Settings and exports contain the protected value; another Windows account or installation normally requires the key to be entered again.

自定义 HTTP/JSON 的 URL、参数和请求头可能包含密码、令牌或其他个人信息，并随配置保存或导出。支持环境变量引用的桌面配置可使用 `${env:VARIABLE_NAME}`，以保存引用代替直接填写秘密值。共享配置、备份或日志前，请先检查敏感内容；密钥加密不代表整个配置文件都已加密。

Custom HTTP/JSON URLs, parameters and headers may contain credentials or personal information and can be saved/exported with settings. Desktop configuration that supports environment references can use `${env:VARIABLE_NAME}` instead of storing a literal secret. Review settings, backups and logs before sharing them. Protecting an API key does not encrypt the entire configuration file.

## 4. 网络访问 / Network access

- 更新检查：启动时或手动访问 GitHub API，查询 `zmd-charge-plus` 对应的 `GlacierGlimmer/zmd-charge-plus` 已发布版本。
- 公网 IP：请求相应变量时访问 `api.ipify.org` 或 `api6.ipify.org`，结果会缓存。
- DeepSeek：启用相关数据源并配置 Key 后，向 API 发起余额等功能请求。默认接口为 `api.deepseek.com`。
- 节假日日历：相关时段功能可从 `cdn.jsdelivr.net` 或 `raw.githubusercontent.com` 获取公开的 `NateScarlet/holiday-cn` 年度数据，不发送 DeepSeek Key。
- HTTP/JSON 和网络探测：访问用户启用的数据源地址或探测目标。请求头、URL 中的参数和鉴权值按配置发给该目标。

Outbound connections are made for update checks, DeepSeek requests, public holiday-calendar refreshes, enabled custom HTTP/JSON sources and configured probe targets. Desktop public-IP variables additionally use api.ipify.org/api6.ipify.org. Update checks use only this platform's GitHub repository: GlacierGlimmer/zmd-charge-plus. Calendar refreshes request public holiday-cn data from jsDelivr or GitHub Raw without a DeepSeek key. DeepSeek credentials go to the API endpoint used by that platform; Android also allows a user-configured base URL.

外部服务会在正常连接中获得来源 IP 等连接信息，并获得该请求明确包含的数据；其处理方式受各自政策约束。打开项目、更新或帮助链接会交给系统浏览器，不代表浏览器和外部网站也由 ECP 控制。

External services receive normal connection metadata such as the source IP and data explicitly included in a request, subject to their own policies. Project/update/help links open external services in the system browser; their behavior is outside ECP's control.

## 5. 本机数据与权限 / Local data and permissions

为实现所选功能，软件可能读取设备/硬件状态、电池、网络接口、运行进程或窗口、媒体状态以及系统允许的其他指标。剪贴板内容在请求相应变量时读取。数据能否读取取决于系统权限、设备、驱动和接口支持，无法读取时会显示不可用。

Windows 的部分硬件传感器和真实 FPS 采集可能需要管理员权限。只有主动选择驱动安装功能时，才会从 PawnIO 官方 GitHub Releases 下载安装程序、校验后交由系统确认安装；ECP 不把该驱动嵌入便携包。AIDA64 接入读取用户启用的本机导出数据。

Features may read device/hardware status, battery/network state, processes/windows, media status and other system-permitted metrics. Clipboard content is read for requested clipboard variables. Availability depends on permissions, hardware, drivers and supported interfaces; unavailable data is reported as unavailable.

Some Windows sensors and real FPS collection require administrative rights. The explicit driver-install action downloads PawnIO from its official GitHub Releases, verifies the download and leaves installation approval to Windows. The portable package does not embed that driver. AIDA64 integration reads locally enabled exports.

## 6. 日志、清理与反馈 / Logs, removal and feedback

日志用于本地诊断，可能包含错误详情、路径、功能状态或自定义服务返回的信息。ECP 不会自动将诊断文件提交给开发者；请在主动分享前检查内容。清理前请退出软件，并按需要保留配置和密钥；删除密钥可能使原有受保护值无法使用。

Logs are local diagnostics and may include error details, paths, feature status or custom-service responses. ECP does not automatically submit diagnostic files to the developer. Review them before sharing. Exit the app before clearing data and retain settings/keys as needed; deleting a protection key can make saved protected values unusable.

开发与维护 / Developer and maintainer: **GlacierGlimmer冰川雪貓**  
源码与反馈 / Source and issues: https://github.com/GlacierGlimmer/zmd-charge-plus
