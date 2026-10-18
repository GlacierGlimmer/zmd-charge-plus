# Windows 硬件数据采集

2026-10-16 本地开发，产品版本保持 v0.1.1。

## CPU 温度修复

ECP 使用 LibreHardwareMonitorLib 0.9.6。该版本通过 PawnIO 访问 CPU MSR/SMN 等底层接口；管理员身份不能替代缺失的驱动。之前的发布没有提供驱动检查和安装入口，因而部分设备即使以管理员身份运行也没有 CPU 温度。

现在打开「设置 → 数据管理 → 硬件数据」可以查看驱动、权限和实际传感器状态，安装或更新官方 PawnIO，并以管理员身份重新启动 ECP。安装操作只会在点击按钮后执行；下载固定为官方 2.2.0 安装程序，校验官方发布的 SHA-256 后打开安装向导。安装会出现 Windows UAC。

安装后需要完全退出 ECP 再重新启动：上游库在进程中缓存驱动检测结果。重启按钮等待旧进程退出后再获取 ECP 单实例锁。驱动已安装但设备无法打开、权限不足、驱动缺失、需要重启和硬件不支持会分别提示。没有有效读数时保持 `--`，不会将底层读取失败产生的零值当作 0°C。

CPU 温度汇总同时修正多 CPU 累积和核心平均值；距 TjMax 的温差不会被当作实际温度。主板的 Super I/O、EC 子节点会参与主板温度汇总，风扇仍独立列出。

## 内置硬件数据

「硬件数据」页可搜索、查看实时值、复制变量和导出本地诊断 JSON。使用与 HUD 共用的采样器；按变量引用、变量库首次发现和该页面的需要采集。全部可枚举传感器进入变量库，不再只保留少数汇总字段。

- CPU、GPU、主板、Super I/O、EC、内存、存储、电池、控制器、电源等由上游实际支持的硬件。
- 温度、电压、电流、功耗、频率、每核心负载、风扇、流量、控制占比、容量、吞吐、时间、时序、能量、噪声、电导率、湿度及上游提供的其他类型。
- 每个传感器的当前值、可用的最小/最大值、原始 ID、名称和单位。
- 上游硬件对象提供的全部属性，例如支持设备的 BIOS、PCIe、显存等描述信息。
- SMBIOS 主板、BIOS、整机、机箱、每个处理器、每个缓存和每根内存条的公开字段，包括型号、厂商、插槽、容量、速度和配置电压。
- CPUID 厂商、家族、型号、步进、签名和当前运行时可用的指令集。后者是 CPU 与操作系统共同启用的能力，不等于完整的硬件指令位清单。

变量形式为 `sensor.<来源>.<稳定 ID 摘要>.value`，以及 `.min`、`.max`、`.name`、`.id`、`.unit`。来源包括 `lhm`、`aida64`、`inventory`，兼容外部 LibreHardwareMonitor/OpenHardwareMonitor 的 WMI 回退。键根据原始 ID 生成，修改显示名称、语言或设备排序不会改变键。设备暂时消失后保留该会话已发现的变量定义，读数变为不可用。

变量库位于「数据管理 → 变量库」。发现对应硬件项目后，重复的旧温度、电压、时钟、固件及独立风扇条目会从库中移除，改用明确设备的 `sensor.*` 变量。最高温度、核心平均温度、最大功率和最高风扇转速等汇总指标保留并标注“汇总”。已有方案引用的旧变量继续兼容读取，无需重写模板；CPU 等缺失时仍可由 AIDA64 的对应原始 ID 补充，动态变量不会覆盖不同来源的原始值。

## AIDA64 接入

在 AIDA64「设置 → 硬件监控 → 外部程序」选择需要导出的项目，开启共享内存、注册表或 WMI。ECP 按共享内存、注册表、WMI 的顺序读取第一个可用导出；不会启动 AIDA64，也不会更改其设置。

共享内存读取 `AIDA64_SensorValues` 中的零结尾 XML 片段；兼容额外根节点、UTF-8 和系统 ANSI 编码。并发更新导致片段不完整时有限重试。注册表读取 HKCU/HKLM 的 `Software\FinalWire\AIDA64\SensorValues`，兼容 32/64 位视图。WMI 读取 `root\WMI:AIDA64_SensorValues`。

所有实际导出的 ID 都可枚举，包括温度、风扇、电压、电流、功率、频率、利用率、SMART、内存时序和文本状态；不使用固定白名单屏蔽新项目。硬件/许可证/共享勾选未提供的数据不会自行补成数值。AIDA64 必须保持运行；ECP 的内置采集独立工作。

CPUID 的 System Information / Monitoring SDK 属于单独授权的开发产品。按本轮确认，先完成内置采集和 AIDA64 导出接入；未捆绑 CPU-Z、AIDA64 或 CPUID SDK，也未声称取得它们全部专有识别能力。SMBIOS 内存信息不冒充完整 SPD/XMP/EXPO 数据；需要 AIDA64 导出实际提供的时序等字段时，可以直接选择相应动态变量。

## 来源

- [LibreHardwareMonitor 当前 NuGet 构建对应源码](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701)
- [PawnIO 官方发布 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0)，安装文件 SHA-256：`1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`
- [AIDA64 官方外部程序接口](https://www.aida64.com/user-manual/hardware-monitoring/external-applications)
- [AIDA64 完整导出 ID 列表](https://www.aida64.com/user-manual/hardware-monitoring/external-applications/complete-sensor-value-list)
- [CPUID System Information Development Kit](https://www.cpuid-pro.com/products-system-information-kit.php)

本批仅更改 Windows 采集及设置界面。Linux/macOS/Android 保持既有平台采集方式。
