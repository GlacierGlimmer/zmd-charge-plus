# Privacy / 隐私说明

Endfield Charge Plus v0.1.0 does not implement advertising, analytics telemetry, an application account system, or an application-operated cloud backend.

Endfield Charge Plus v0.1.0 本身不包含广告、分析遥测、应用账户系统，也没有由本项目运营的云端后端服务。

## Local data / 本地数据

The application stores settings, backups, and logs under the current Windows user's local application-data directory (`%LOCALAPPDATA%\EndfieldChargePlus`).

应用的设置、备份和日志保存在当前 Windows 用户的本地应用数据目录：`%LOCALAPPDATA%\EndfieldChargePlus`。

A configured DeepSeek API key is protected with Windows DPAPI using the current-user scope before it is written to settings. Exported configuration therefore contains the protected value rather than the plaintext key. A protected value may not be usable under a different Windows user or on a different Windows installation.

配置的 DeepSeek API Key 在写入设置前会使用 Windows DPAPI 的当前用户范围进行保护；导出的配置中保存的是受保护值而不是明文 Key。该受保护值换到其他 Windows 用户或其他 Windows 安装后可能无法解密。

When a selected variable needs it, the application may read local Windows/device state such as hardware and sensor information, battery status, network-interface data (including local IP/MAC/SSID when exposed by Windows), host/user names, process statistics/names, security/update status, and clipboard metadata or text. These values are used locally for HUD variables and are not uploaded by Endfield Charge Plus through its built-in update, public-IP, or DeepSeek balance requests.

当所选变量需要时，应用可能读取本机 Windows / 设备状态，例如硬件与传感器、电池状态、网络接口信息（包括 Windows 能提供时的本地 IP / MAC / SSID）、主机名/用户名、进程名称与统计、安全/更新状态，以及剪贴板的类型、尺寸、文件数量或文本。这些信息用于本地 HUD 变量，Endfield Charge Plus 不会通过内置的更新检查、公网 IP 查询或 DeepSeek 余额请求上传这些本地变量值。

Custom HTTP/JSON request headers are stored exactly as the user configures them. For secrets, use the supported `${env:VARIABLE_NAME}` form so the saved/exported configuration stores the environment-variable reference instead of the secret itself. If a secret is typed directly into a custom header, that literal value will also be present in settings and exported configuration.

自定义 HTTP/JSON 请求头会按用户输入的内容保存。敏感值建议使用支持的 `${env:VARIABLE_NAME}` 形式，这样设置与导出配置保存的是环境变量引用，而不是密钥本身；如果直接把密钥写入自定义 Header，该明文也会随设置保存并出现在导出的配置中。

## Network access / 网络访问

The application may make the following outbound requests:

- GitHub API: startup/manual update checks for `GlacierGlimmer/zmd-charge-plus`.
- `api.ipify.org` / `api6.ipify.org`: only when public-IP variables are actually requested; results are cached.
- DeepSeek API: only when the user configures an API key and uses the related data source/profile.
- Custom HTTP/JSON sources: only endpoints explicitly configured by the user.

应用可能进行以下出站网络请求：

- GitHub API：启动时或手动检查 `GlacierGlimmer/zmd-charge-plus` 更新。
- `api.ipify.org` / `api6.ipify.org`：仅当实际使用公网 IP 变量时请求，并进行缓存。
- DeepSeek API：仅当用户配置 API Key 并使用相关数据源/方案时请求。
- 自定义 HTTP/JSON 数据源：仅请求用户自己明确配置的地址。

These external services receive normal connection metadata such as the source IP address as part of ordinary network communication and are governed by their own privacy policies.

这些外部服务在正常网络通信过程中会获得来源 IP 等连接元数据，并受各自隐私政策约束。
