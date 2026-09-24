# Variable availability / 变量可用性

变量库的“存在生产代码”和“当前设备一定能读到值”是两件不同的事。

- 内置变量：`VariableCatalog.cs` 中声明；运行时生产/计算逻辑主要位于 `VariableHub.cs`、`AdvancedVariableProvider.cs`、`HudProfileRenderer.cs`。
- 磁盘动态变量：只为 Windows 中发现的实际固定盘符创建 `disk.{letter}.*`，不是固定列举 C、D 等盘符。
- `verify-variable-coverage.ps1` 是**静态覆盖检查**：确认已声明的固定 Key 和动态盘符模板存在生产代码形式；它不能替代运行时测试，也不能判断每个外部接口是否正常工作。
- `dev.docker.*`：取决于 Docker CLI、后台/引擎和当前环境；容器数指运行中容器，不保证远程 Docker Context 能被本地进程检测识别。
- `dev.git.*`：依赖当前 ECP 进程工作目录和本机 Git 环境，Portable 的一般启动目录未必是 Git 仓库。
- 深层硬件传感器：需要 Windows、硬件/驱动或 LibreHardwareMonitor 可提供相应数据；不存在时显示不可用值，不应伪造采样。
- `deepseek.period.next_switch_*`：原有变量为北京时间；后缀 `_local` 的变量为 Windows 用户时区对应的切换时刻；`local_timezone` 的偏移按目标时间计算，考虑适用的夏令时。
- 自定义 HTTP/JSON 数据源由用户配置，结果以实际响应与解析状态为准。

完整 Key、类型、格式和用途请在程序的“变量库 / Variable Library”中查看。
