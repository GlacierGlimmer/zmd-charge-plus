using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EndfieldChargePlus.Customization;

public sealed record VariableDefinition(
    string Key,
    string Name,
    string Category,
    string Description,
    string ValueType,
    string Unit,
    string RecommendedUse,
    string RecommendedFormats)
{
    public string TemplateToken => "{" + Key + "}";
    public override string ToString() => $"{Name}    {Key}";
}

public static class VariableCatalog
{
    private static VariableDefinition V(
        string key, string name, string category, string description,
        string valueType = "数值", string unit = "", string use = "按数据含义决定", string formats = "0 或 0.0") =>
        new(key, name, category, description, valueType, unit, use, formats);

    private static readonly IReadOnlyList<VariableDefinition> StaticBuiltIns = new List<VariableDefinition>
    {
        // ===== 电池 =====
        V("battery.percent", "电池电量", "电池", "当前电池剩余电量百分比。", unit: "%", use: "右侧状态 / 圆环"),
        V("battery.remaining_mwh", "当前电池容量", "电池", "当前剩余电池容量。", unit: "mWh", use: "左侧主值", formats: "0"),
        V("battery.full_mwh", "满充容量", "电池", "电池当前可充满的容量。", unit: "mWh", use: "左侧次值", formats: "0"),
        V("battery.design_mwh", "设计容量", "电池", "电池出厂设计容量。", unit: "mWh", use: "左侧信息", formats: "0"),
        V("battery.remaining_wh", "当前电池容量（Wh）", "电池", "当前剩余电池容量，换算为 Wh。", unit: "Wh", use: "左侧主值", formats: "0.0 / 0.00"),
        V("battery.full_wh", "满充容量（Wh）", "电池", "满充容量，换算为 Wh。", unit: "Wh", use: "左侧次值", formats: "0.0 / 0.00"),
        V("battery.design_wh", "设计容量（Wh）", "电池", "设计容量，换算为 Wh。", unit: "Wh", use: "左侧信息", formats: "0.0 / 0.00"),
        V("battery.health_percent", "电池健康度", "电池", "满充容量相对设计容量的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("battery.ac_online", "外接电源状态", "电池", "当前是否接入外部电源。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("battery.charging", "正在充电", "电池", "当前是否正在充电。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("battery.discharging", "正在放电", "电池", "当前是否正在使用电池放电。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("battery.rate_watts", "电池实时功率", "电池", "当前充电或放电功率的绝对值。不同机型可能不提供。", unit: "W", use: "左侧信息", formats: "0.0 / 0.00"),
        V("battery.charge_rate_watts", "充电功率", "电池", "当前充电功率；未充电时通常为 0。", unit: "W", use: "左侧信息", formats: "0.0 / 0.00"),
        V("battery.discharge_rate_watts", "放电功率", "电池", "当前放电功率；未放电时通常为 0。", unit: "W", use: "左侧信息", formats: "0.0 / 0.00"),
        V("battery.voltage_mv", "电池电压（mV）", "电池", "Windows 电池接口报告的实时电压。不同机型可能不提供。", unit: "mV", use: "左侧信息", formats: "0"),
        V("battery.voltage_v", "电池电压（V）", "电池", "电池实时电压，换算为伏特。", unit: "V", use: "左侧信息", formats: "0.00"),
        V("battery.time_remaining_seconds", "预计剩余使用时间", "电池", "Windows 估算的剩余电池使用时间；无法估算时为 0。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("battery.time_remaining_text", "预计剩余使用时间文字", "电池", "预计剩余使用时间，已格式化为 HH:MM:SS；无法估算时显示“未知”。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("battery.full_life_seconds", "预计满电续航", "电池", "Windows 报告的满电预计续航时间；不可用时为 0。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("battery.saver_on", "节电模式状态", "电池", "Windows 电池节电模式是否开启。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("battery.status_text", "电池状态文字", "电池", "根据电源状态生成“充电中 / 使用电池 / 已接通电源 / 未知”等文字。", "文本", use: "标题 / 右侧状态", formats: "无需格式化"),
        V("battery.power_source", "当前电源来源", "电池", "当前电源来源：“交流电源”或“电池”。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("battery.cycle_count", "电池循环次数", "电池", "固件/驱动提供的电池循环次数；部分设备可能返回 0。", "整数", "次", "左侧信息", "0"),

        // ===== CPU =====
        V("cpu.usage", "CPU 使用率", "CPU", "当前整机 CPU 总使用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.user_usage", "CPU 用户态使用率", "CPU", "CPU 时间中用于用户态代码的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.kernel_usage", "CPU 内核态使用率", "CPU", "CPU 时间中用于内核态且不含空闲时间的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.idle_percent", "CPU 空闲率", "CPU", "CPU 当前空闲时间比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.frequency_ghz", "CPU 当前频率", "CPU", "Windows 性能数据报告的当前实时有效频率。", unit: "GHz", use: "左侧主值", formats: "0.00"),
        V("cpu.frequency_mhz", "CPU 当前频率（MHz）", "CPU", "当前实时有效频率，单位 MHz。", unit: "MHz", use: "左侧主值", formats: "0"),
        V("cpu.max_frequency_ghz", "CPU 最大频率", "CPU", "Win32_Processor 报告的最大时钟频率。", unit: "GHz", use: "左侧次值", formats: "0.00"),
        V("cpu.max_frequency_mhz", "CPU 最大频率（MHz）", "CPU", "最大时钟频率，单位 MHz。", unit: "MHz", use: "左侧次值", formats: "0"),
        V("cpu.frequency_percent", "CPU 频率比例", "CPU", "当前有效频率相对最大频率的百分比，允许睿频时超过 100%。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.name", "CPU 名称", "CPU", "处理器完整型号名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("cpu.manufacturer", "CPU 制造商", "CPU", "处理器制造商。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("cpu.architecture", "CPU 架构", "CPU", "处理器架构，如 x64 / ARM64。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("cpu.physical_cores", "CPU 物理核心数", "CPU", "所有处理器插槽的物理核心总数。", "整数", "核", "左侧信息", "0"),
        V("cpu.logical_processors", "CPU 逻辑处理器数", "CPU", "系统可见的逻辑处理器总数。", "整数", "线程", "左侧信息", "0"),
        V("cpu.socket_count", "CPU 插槽数", "CPU", "系统中处理器插槽数量。", "整数", "个", "左侧信息", "0"),
        V("cpu.virtualization_enabled", "CPU 固件虚拟化", "CPU", "固件虚拟化功能是否启用；依赖 WMI 支持。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("cpu.l2_cache_kb", "CPU L2 缓存", "CPU", "处理器报告的 L2 缓存容量。", unit: "KB", use: "左侧信息", formats: "0"),
        V("cpu.l3_cache_kb", "CPU L3 缓存", "CPU", "处理器报告的 L3 缓存容量。", unit: "KB", use: "左侧信息", formats: "0"),

        // ===== 内存 =====
        V("memory.used_bytes", "已用物理内存", "内存", "当前已使用的物理内存。", unit: "Byte", use: "左侧主值", formats: "gb:1 / gb:2 / bytes"),
        V("memory.available_bytes", "可用物理内存", "内存", "当前可用物理内存。", unit: "Byte", use: "左侧信息", formats: "gb:1 / bytes"),
        V("memory.total_bytes", "物理内存总量", "内存", "物理内存总容量。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("memory.usage", "内存使用率", "内存", "已用物理内存占总物理内存的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("memory.free_percent", "内存空闲率", "内存", "可用物理内存占总物理内存的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("memory.commit_used_bytes", "已提交内存", "内存", "系统已提交内存量。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"),
        V("memory.commit_available_bytes", "剩余提交额度", "内存", "提交限制中尚可使用的额度。", unit: "Byte", use: "左侧信息", formats: "gb:1 / bytes"),
        V("memory.commit_limit_bytes", "提交限制", "内存", "Windows 当前提交上限，通常受物理内存与页面文件共同影响。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("memory.commit_usage", "提交使用率", "内存", "已提交内存占提交限制的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("memory.virtual_used_bytes", "已用虚拟内存", "内存", "当前系统虚拟地址空间已使用量。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"),
        V("memory.virtual_available_bytes", "可用虚拟内存", "内存", "当前系统可用虚拟地址空间。", unit: "Byte", use: "左侧信息", formats: "gb:1 / bytes"),
        V("memory.virtual_total_bytes", "虚拟内存总量", "内存", "系统报告的虚拟地址空间总量。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("memory.virtual_usage", "虚拟内存使用率", "内存", "已用虚拟内存占虚拟内存总量的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("memory.cache_bytes", "系统缓存内存", "内存", "Windows 性能计数器报告的系统缓存字节数。", unit: "Byte", use: "左侧信息", formats: "gb:1 / mb:0 / bytes"),
        V("memory.paged_pool_bytes", "分页池", "内存", "Windows 内核分页池内存。", unit: "Byte", use: "左侧信息", formats: "mb:0 / bytes"),
        V("memory.nonpaged_pool_bytes", "非分页池", "内存", "Windows 内核非分页池内存。", unit: "Byte", use: "左侧信息", formats: "mb:0 / bytes"),

        // ===== GPU =====
        V("gpu.usage", "GPU 总使用率", "GPU", "当前所选 GPU 的实时利用率，取最忙 GPU 引擎。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.usage_3d", "GPU 3D 使用率", "GPU", "当前所选 GPU 的 3D 引擎最高实时利用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.usage_compute", "GPU Compute 使用率", "GPU", "当前所选 GPU 的计算引擎最高实时利用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.usage_copy", "GPU Copy 使用率", "GPU", "当前所选 GPU 的复制引擎最高实时利用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.usage_video_decode", "GPU 视频解码使用率", "GPU", "当前所选 GPU 的视频解码引擎最高实时利用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.usage_video_encode", "GPU 视频编码使用率", "GPU", "当前所选 GPU 的视频编码引擎最高实时利用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.name", "GPU 名称", "GPU", "当前方案选择的显示适配器名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("gpu.adapter_id", "GPU 设备 ID", "GPU", "当前所选 GPU 的内部稳定标识。", "文本", use: "调试 / 自定义", formats: "无需格式化"),
        V("gpu.physical_index", "GPU 物理索引", "GPU", "Windows GPU 性能计数器对应的物理 GPU 索引。", "整数", use: "调试 / 左侧信息", formats: "0"),
        V("gpu.count", "GPU 数量", "GPU", "当前系统检测到的物理 GPU 数量。", "整数", "个", "左侧信息", "0"),
        V("gpu.vram_bytes", "GPU 专用显存总量", "GPU", "当前所选 GPU 的专用显存总容量。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("gpu.dedicated_total_bytes", "专用显存总量", "GPU", "当前所选 GPU 的专用显存容量。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("gpu.dedicated_used_bytes", "已用专用显存", "GPU", "当前所选 GPU 的专用显存实时使用量。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"),
        V("gpu.dedicated_usage", "专用显存使用率", "GPU", "已用专用显存占专用显存总量的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.shared_limit_bytes", "共享 GPU 内存上限", "GPU", "当前 GPU 可使用的共享系统内存上限。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("gpu.shared_used_bytes", "已用共享 GPU 内存", "GPU", "当前 GPU 使用的共享系统内存。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"),
        V("gpu.shared_usage", "共享 GPU 内存使用率", "GPU", "已用共享 GPU 内存占共享上限的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.memory_used_bytes", "GPU 当前显存占用", "GPU", "默认 HUD 使用的专用显存占用。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"),
        V("gpu.memory_total_bytes", "GPU 显存总量", "GPU", "默认 HUD 使用的专用显存总量。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("gpu.total_memory_used_bytes", "GPU 总内存占用", "GPU", "专用显存占用与共享 GPU 内存占用之和。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"),
        V("gpu.total_memory_limit_bytes", "GPU 总可用内存上限", "GPU", "专用显存总量与共享内存上限之和。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("gpu.total_memory_usage", "GPU 总内存使用率", "GPU", "GPU 总内存占用占总可用内存上限的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("gpu.uses_unified_memory", "GPU 是否主要使用共享内存", "GPU", "用于区分典型核显/统一内存设备与独立显卡的启发式标记。", "布尔", use: "标题 / 条件", formats: "无需格式化"),

        // ===== 网络 =====
        V("network.download_bps", "系统总下载速度", "网络", "所有已连接、非回环网络接口合计的实时下载速度。", unit: "Byte/s", use: "左侧主值", formats: "speed"),
        V("network.upload_bps", "系统总上传速度", "网络", "所有已连接、非回环网络接口合计的实时上传速度。", unit: "Byte/s", use: "左侧次值", formats: "speed"),
        V("network.total_bps", "系统总吞吐量", "网络", "系统总下载速度与总上传速度之和。", unit: "Byte/s", use: "状态计算", formats: "speed"),
        V("network.download_mbps", "系统总下载速度（Mbps）", "网络", "系统总下载速度，换算为 Mbps。", unit: "Mbps", use: "左侧主值", formats: "0.0 / 0.00"),
        V("network.upload_mbps", "系统总上传速度（Mbps）", "网络", "系统总上传速度，换算为 Mbps。", unit: "Mbps", use: "左侧次值", formats: "0.0 / 0.00"),
        V("network.total_mbps", "系统总吞吐量（Mbps）", "网络", "下载与上传合计，换算为 Mbps。", unit: "Mbps", use: "左侧信息", formats: "0.0 / 0.00"),
        V("network.display_download", "方案下载速度文字", "网络", "按当前网络方案选择的单位生成的下载速度文字。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("network.display_upload", "方案上传速度文字", "网络", "按当前网络方案选择的单位生成的上传速度文字。", "文本", use: "左侧次值", formats: "无需格式化"),
        V("network.profile_percent", "方案网络百分比", "网络", "按方案选择的百分比模式与“100% 对应速度”计算。", unit: "%", use: "右侧状态 / 圆环"),
        V("network.profile_percent_text", "方案网络百分比文字", "网络", "根据模式生成“50% / ↓ 50% / ↑ 50%”等文字；较大值模式不显示箭头。", "文本", use: "右侧状态", formats: "无需格式化"),
        V("network.profile_percent_bps", "百分比计算速度", "网络", "当前网络方案实际用于计算百分比的速度。", unit: "Byte/s", use: "状态计算", formats: "speed"),
        V("network.profile_percent_mode", "网络百分比模式", "网络", "当前方案百分比模式：Total / Download / Upload / Max。", "文本", use: "标题 / 条件", formats: "无需格式化"),
        V("network.link_speed_bps", "总链路速率", "网络", "所有活动网络接口报告的链路速率之和。", unit: "bit/s", use: "左侧信息", formats: "0"),
        V("network.max_link_speed_bps", "最高单接口链路速率", "网络", "所有活动网络接口中最高的单个链路速率。", unit: "bit/s", use: "左侧信息", formats: "0"),
        V("network.utilization_percent", "网络链路利用率", "网络", "系统总吞吐量与活动接口总链路速率的近似比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("network.total_received_bytes", "累计接收流量", "网络", "当前开机期间所有活动网络接口累计接收字节数之和。", unit: "Byte", use: "左侧信息", formats: "bytes / gb:1"),
        V("network.total_sent_bytes", "累计发送流量", "网络", "当前开机期间所有活动网络接口累计发送字节数之和。", unit: "Byte", use: "左侧信息", formats: "bytes / gb:1"),
        V("network.total_transferred_bytes", "累计总流量", "网络", "累计接收与发送流量之和。", unit: "Byte", use: "左侧信息", formats: "bytes / gb:1"),
        V("network.active_interface_count", "活动网络接口数量", "网络", "当前处于 Up 状态且非回环的网络接口数量。", "整数", "个", "左侧信息", "0"),
        V("network.interface_names", "活动网络接口名称", "网络", "所有活动网络接口名称，以逗号分隔。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("network.interface_types", "活动网络接口类型", "网络", "活动网络接口类型列表，如 Wireless80211 / Ethernet。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("network.ipv4_addresses", "本机 IPv4 地址", "网络", "活动网络接口上的 IPv4 单播地址列表。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.ipv6_addresses", "本机 IPv6 地址", "网络", "活动网络接口上的 IPv6 单播地址列表。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.default_gateways", "默认网关", "网络", "活动网络接口配置的网关地址列表。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.dns_servers", "DNS 服务器", "网络", "活动网络接口配置的 DNS 服务器地址列表。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.available", "网络可用状态", "网络", "Windows 是否检测到至少一个可用网络连接；不代表一定可访问互联网。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("network.packets_received", "累计接收数据包", "网络", "活动网络接口累计接收单播数据包数量。", "数值", "包", "左侧信息", "0"),
        V("network.packets_sent", "累计发送数据包", "网络", "活动网络接口累计发送单播数据包数量。", "数值", "包", "左侧信息", "0"),
        V("network.receive_errors", "接收错误", "网络", "活动网络接口累计接收错误数量。", "数值", "个", "状态 / 调试", "0"),
        V("network.send_errors", "发送错误", "网络", "活动网络接口累计发送错误数量。", "数值", "个", "状态 / 调试", "0"),

        // ===== Ping / 实时连通性 =====
        V("probe.target", "探测地址", "网络探测", "当前网络包探测器使用的 IPv4、IPv6 或域名。", "文本", use: "左侧信息 / 标题", formats: "无需格式化"),
        V("probe.address", "实际响应地址", "网络探测", "目标解析后实际参与检测或返回响应的 IP 地址。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("probe.protocol", "检测协议", "网络探测", "当前使用的检测协议：ICMP、TCP 或 UDP。", "文本", use: "左侧信息 / 标题", formats: "无需格式化"),
        V("probe.port", "检测端口", "网络探测", "TCP / UDP 检测端口；ICMP 模式返回 0。", "整数", use: "左侧信息", formats: "0"),
        V("probe.endpoint", "探测端点", "网络探测", "ICMP 显示目标地址；TCP / UDP 显示“地址:端口”。", "文本", use: "左侧信息 / 标题", formats: "无需格式化"),
        V("probe.online", "探测是否成功", "网络探测", "最近一次网络探测是否成功。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("probe.status_text", "探测状态文字", "网络探测", "最近一次探测状态，例如“在线”“超时”“端口不可达”。", "文本", use: "右侧状态 / 标题", formats: "无需格式化"),
        V("probe.reply_status", "探测原始状态", "网络探测", "协议探测返回的底层状态，例如 Success、Connected、Response、TimedOut。", "文本", use: "状态 / 调试", formats: "无需格式化"),
        V("probe.latency_ms", "探测延迟", "网络探测", "最近一次成功探测的往返/连接延迟；失败时按 999 ms 处理。", unit: "ms", use: "左侧主体信息", formats: "0 / 0.0"),
        V("probe.latency_text", "探测延迟文字", "网络探测", "成功时显示“20ms”；失败时显示超时或错误状态。", "文本", use: "左侧主体信息", formats: "无需格式化"),
        V("probe.latency_progress", "延迟进度", "网络探测", "0 ms 为 0%，999 ms 及以上为 100%；适合用作延迟圆环。", unit: "%", use: "圆环"),
        V("probe.full_scale_ms", "延迟 100% 对应值", "网络探测", "延迟圆环 100% 对应 999 ms。", unit: "ms", use: "状态计算", formats: "0"),
        V("probe.timeout_ms", "单次探测超时", "网络探测", "单次 ICMP/TCP/UDP 探测等待响应的超时时间。", unit: "ms", use: "状态计算", formats: "0"),
        V("probe.ttl", "ICMP TTL", "网络探测", "最近一次 ICMP 成功响应的 TTL；TCP/UDP 为 0。", "整数", use: "左侧信息", formats: "0"),
        V("probe.sent", "探测样本数", "网络探测", "当前目标最近滚动统计窗口内的探测样本数量。", "整数", "次", "左侧信息", "0"),
        V("probe.received", "成功样本数", "网络探测", "当前目标最近滚动统计窗口内成功的探测次数。", "整数", "次", "左侧信息", "0"),
        V("probe.lost", "失败样本数", "网络探测", "当前目标最近滚动统计窗口内失败/超时次数。", "整数", "次", "左侧信息", "0"),
        V("probe.loss_percent", "丢包率", "网络探测", "当前地址、协议和端口组合最近 20 次探测的失败/超时比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("probe.avg_latency_ms", "平均延迟", "网络探测", "当前探测配置最近 20 次成功样本的平均延迟。", unit: "ms", use: "左侧信息", formats: "0.0"),
        V("probe.min_latency_ms", "最低延迟", "网络探测", "当前探测配置最近 20 次成功样本中的最低延迟。", unit: "ms", use: "左侧信息", formats: "0"),
        V("probe.max_latency_ms", "最高延迟", "网络探测", "当前探测配置最近 20 次成功样本中的最高延迟。", unit: "ms", use: "左侧信息", formats: "0"),
        V("probe.jitter_ms", "延迟抖动", "网络探测", "相邻成功样本延迟差的平均绝对值，用于近似表示抖动。", unit: "ms", use: "左侧信息", formats: "0.0"),
        V("probe.last_success", "最近成功时间", "网络探测", "当前探测配置最近一次成功的本地时间。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("probe.error", "探测错误", "网络探测", "最近一次检测失败时的底层状态或错误。", "文本", use: "状态 / 调试", formats: "无需格式化"),

        V("ping.target", "Ping 目标", "Ping（兼容）", "当前方案正在检测的 IP 地址或域名。", "文本", use: "左侧主值 / 标题", formats: "无需格式化"),
        V("ping.address", "Ping 实际地址", "Ping（兼容）", "Ping 响应返回的实际 IP 地址；域名目标解析成功后可查看最终地址。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("ping.protocol", "Ping/探测协议", "Ping（兼容）", "兼容变量：当前网络探测协议 ICMP、TCP 或 UDP。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("ping.port", "Ping/探测端口", "Ping（兼容）", "兼容变量：TCP / UDP 端口；ICMP 为 0。", "整数", use: "左侧信息", formats: "0"),
        V("ping.endpoint", "Ping/探测端点", "Ping（兼容）", "兼容变量：当前地址和端口组合。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("ping.online", "Ping 是否在线", "Ping（兼容）", "最近一次 ICMP Ping 是否成功。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("ping.status_text", "Ping 状态文字", "Ping（兼容）", "最近一次检测状态，例如“在线”“超时”“不可达”。", "文本", use: "右侧状态 / 标题", formats: "无需格式化"),
        V("ping.reply_status", "Ping 原始状态", "Ping（兼容）", "System.Net.NetworkInformation.IPStatus 返回的原始状态名称。", "文本", use: "状态 / 调试", formats: "无需格式化"),
        V("ping.latency_ms", "Ping 延迟", "Ping（兼容）", "最近一次成功 Ping 的往返延迟；失败时按 999 ms 计入进度。", unit: "ms", use: "右侧状态", formats: "0 / 0.0"),
        V("ping.latency_text", "Ping 延迟文字", "Ping（兼容）", "成功时显示“20ms”一类文字；超时或不可达时显示对应状态。", "文本", use: "右侧状态", formats: "无需格式化"),
        V("ping.progress", "Ping 延迟进度", "Ping（兼容）", "延迟换算后的圆环进度：0 ms 为 0%，999 ms 及以上为 100%；超时/失败为 100%。", unit: "%", use: "右侧状态 / 圆环"),
        V("ping.full_scale_ms", "Ping 100% 对应延迟", "Ping（兼容）", "Ping 圆环 100% 对应的延迟，固定为 999 ms。", unit: "ms", use: "状态计算", formats: "0"),
        V("ping.timeout_ms", "Ping 超时时间", "Ping（兼容）", "单次 Ping 等待响应的超时时间。", unit: "ms", use: "状态计算", formats: "0"),
        V("ping.ttl", "Ping TTL", "Ping（兼容）", "最近一次成功响应返回的 TTL。", "整数", use: "左侧信息", formats: "0"),
        V("ping.sent", "Ping 统计发送次数", "Ping（兼容）", "当前目标最近滚动统计窗口内的 Ping 样本数量。", "整数", "次", "左侧信息", "0"),
        V("ping.received", "Ping 统计成功次数", "Ping（兼容）", "当前目标最近滚动统计窗口内成功的 Ping 样本数量。", "整数", "次", "左侧信息", "0"),
        V("ping.lost", "Ping 丢包次数", "Ping（兼容）", "当前目标最近滚动统计窗口内失败/超时的 Ping 样本数量。", "整数", "次", "左侧信息", "0"),
        V("ping.loss_percent", "Ping 丢包率", "Ping（兼容）", "当前目标最近 20 次检测的丢包率。", unit: "%", use: "右侧状态 / 圆环"),
        V("ping.avg_latency_ms", "Ping 平均延迟", "Ping（兼容）", "当前目标最近 20 次成功检测的平均延迟。", unit: "ms", use: "左侧信息", formats: "0.0"),
        V("ping.min_latency_ms", "Ping 最低延迟", "Ping（兼容）", "当前目标最近 20 次成功检测中的最低延迟。", unit: "ms", use: "左侧信息", formats: "0"),
        V("ping.max_latency_ms", "Ping 最高延迟", "Ping（兼容）", "当前目标最近 20 次成功检测中的最高延迟。", unit: "ms", use: "左侧信息", formats: "0"),
        V("ping.jitter_ms", "Ping 抖动", "Ping（兼容）", "当前目标最近成功样本相邻延迟差的平均绝对值，用于近似表示网络抖动。", unit: "ms", use: "左侧信息", formats: "0.0"),
        V("ping.last_success", "Ping 最近成功时间", "Ping（兼容）", "当前目标最近一次成功响应的本地时间。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("ping.error", "Ping 错误信息", "Ping（兼容）", "最近一次检测失败时的错误或状态说明。", "文本", use: "状态 / 调试", formats: "无需格式化"),

        // ===== 磁盘 =====
        V("disk.system.root", "系统盘盘符", "磁盘", "Windows 系统目录所在驱动器根路径，如 C:\\。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("disk.system.label", "系统盘卷标", "磁盘", "系统盘卷标。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("disk.system.filesystem", "系统盘文件系统", "磁盘", "系统盘文件系统，如 NTFS。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("disk.system.used_bytes", "系统盘已用空间", "磁盘", "Windows 系统盘已使用空间。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"),
        V("disk.system.free_bytes", "系统盘可用空间", "磁盘", "Windows 系统盘可用空间。", unit: "Byte", use: "左侧信息", formats: "gb:1 / bytes"),
        V("disk.system.total_bytes", "系统盘总容量", "磁盘", "Windows 系统盘总容量。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("disk.system.usage", "系统盘使用率", "磁盘", "系统盘已用空间占总容量的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("disk.system.free_percent", "系统盘空闲率", "磁盘", "系统盘可用空间占总容量的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("disk.system.read_bps", "系统盘读取速度", "磁盘", "Windows 逻辑磁盘性能计数器报告的系统盘读取速度。", unit: "Byte/s", use: "左侧主值", formats: "speed"),
        V("disk.system.write_bps", "系统盘写入速度", "磁盘", "Windows 逻辑磁盘性能计数器报告的系统盘写入速度。", unit: "Byte/s", use: "左侧次值", formats: "speed"),
        V("disk.system.io_bps", "系统盘总 IO 速度", "磁盘", "系统盘读取速度与写入速度之和。", unit: "Byte/s", use: "左侧信息", formats: "speed"),
        V("disk.system.active_percent", "系统盘活动时间", "磁盘", "系统盘性能计数器报告的磁盘活动时间百分比。", unit: "%", use: "右侧状态 / 圆环"),
        V("disk.system.queue_length", "系统盘队列长度", "磁盘", "系统盘当前磁盘队列长度。", unit: "项", use: "左侧信息", formats: "0.0"),
        V("disk.fixed.count", "固定磁盘数量", "磁盘", "当前已就绪的固定磁盘卷数量。", "整数", "个", "左侧信息", "0"),
        V("disk.fixed.used_bytes", "所有固定磁盘已用空间", "磁盘", "所有已就绪固定磁盘卷的已用空间总和。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"),
        V("disk.fixed.free_bytes", "所有固定磁盘可用空间", "磁盘", "所有已就绪固定磁盘卷的可用空间总和。", unit: "Byte", use: "左侧信息", formats: "gb:1 / bytes"),
        V("disk.fixed.total_bytes", "所有固定磁盘总容量", "磁盘", "所有已就绪固定磁盘卷的总容量。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"),
        V("disk.fixed.usage", "所有固定磁盘总体使用率", "磁盘", "所有固定磁盘已用空间占总容量的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("disk.fixed.list", "固定磁盘列表", "磁盘", "已就绪固定磁盘盘符列表。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),

        // ===== 系统 =====
        V("system.time", "当前时间", "系统", "本机当前时间，精确到秒。", "文本", use: "左侧信息 / 标题", formats: "无需格式化"),
        V("system.date", "当前日期", "系统", "本机当前日期。", "文本", use: "左侧信息 / 标题", formats: "无需格式化"),
        V("system.datetime", "当前日期时间", "系统", "本机当前日期与时间。", "文本", use: "左侧信息 / 标题", formats: "无需格式化"),
        V("system.uptime_seconds", "系统运行时间", "系统", "当前 Windows 自开机以来运行的秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("system.uptime_text", "系统运行时间文字", "系统", "系统运行时间的可读文字。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.boot_time", "系统启动时间", "系统", "根据系统运行时间估算的本次 Windows 启动时间。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.machine_name", "计算机名称", "系统", "Windows 计算机名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("system.host_name", "主机名", "系统", "DNS 主机名。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("system.user_name", "当前用户名", "系统", "当前 Windows 用户名。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("system.user_domain", "当前用户域", "系统", "当前用户所属 Windows 域/计算机名。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("system.os_description", "操作系统名称", "系统", ".NET RuntimeInformation 提供的 Windows 操作系统描述。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("system.os_version", "操作系统版本", "系统", "Windows 操作系统版本号。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.os_build", "Windows Build", "系统", "当前 Windows Build 号。", "整数", use: "左侧信息", formats: "0"),
        V("system.os_architecture", "操作系统架构", "系统", "当前 Windows 架构，如 X64 / Arm64。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.process_architecture", "应用进程架构", "系统", "Endfield Charge Plus 当前进程架构。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.framework_version", ".NET 版本", "系统", "当前进程使用的 .NET FrameworkDescription。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.processor_count", "系统逻辑处理器数", "系统", "Environment.ProcessorCount。", "整数", "个", "左侧信息", "0"),
        V("system.timezone_id", "时区 ID", "系统", "当前本地时区 ID。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("system.timezone_name", "时区名称", "系统", "当前本地时区显示名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("system.utc_offset_hours", "UTC 时差", "系统", "当前时区相对 UTC 的小时偏移。", unit: "小时", use: "左侧信息", formats: "0.0"),
        V("system.culture", "系统区域语言", "系统", "当前进程文化区域名称，如 zh-CN。", "文本", use: "左侧信息", formats: "无需格式化"),

        // ===== 应用进程 =====
        V("app.name", "应用进程名称", "ECP 应用", "当前 Endfield Charge Plus 进程名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("app.version", "应用版本", "ECP 应用", "Endfield Charge Plus 产品版本。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("app.pid", "应用 PID", "ECP 应用", "当前应用进程 ID。", "整数", use: "左侧信息", formats: "0"),
        V("app.start_time", "应用启动时间", "ECP 应用", "当前应用进程启动时间。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("app.uptime_seconds", "应用运行时间", "ECP 应用", "当前应用进程已运行的秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("app.uptime_text", "应用运行时间文字", "ECP 应用", "当前应用运行时间的可读文字。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("app.working_set_bytes", "应用工作集", "ECP 应用", "Endfield Charge Plus 当前工作集内存。", unit: "Byte", use: "左侧主值", formats: "mb:1 / bytes"),
        V("app.private_memory_bytes", "应用专用内存", "ECP 应用", "当前应用进程专用内存。", unit: "Byte", use: "左侧信息", formats: "mb:1 / bytes"),
        V("app.virtual_memory_bytes", "应用虚拟内存", "ECP 应用", "当前应用进程虚拟内存大小。", unit: "Byte", use: "左侧信息", formats: "mb:1 / bytes"),
        V("app.thread_count", "应用线程数", "ECP 应用", "当前应用进程线程数量。", "整数", "个", "左侧信息", "0"),
        V("app.handle_count", "应用句柄数", "ECP 应用", "当前应用进程打开的 Windows 句柄数量。", "整数", "个", "左侧信息", "0"),
        V("app.cpu_time_seconds", "应用累计 CPU 时间", "ECP 应用", "当前应用进程累计消耗的处理器时间。", unit: "秒", use: "左侧信息", formats: "duration"),

        // ===== 显示器 =====
        V("display.primary_width_px", "主显示器宽度", "显示器", "Windows 主显示器宽度。", "整数", "px", "左侧信息", "0"),
        V("display.primary_height_px", "主显示器高度", "显示器", "Windows 主显示器高度。", "整数", "px", "左侧信息", "0"),
        V("display.virtual_x_px", "虚拟桌面 X 起点", "显示器", "多显示器虚拟桌面的左边界坐标。", "整数", "px", "左侧信息", "0"),
        V("display.virtual_y_px", "虚拟桌面 Y 起点", "显示器", "多显示器虚拟桌面的上边界坐标。", "整数", "px", "左侧信息", "0"),
        V("display.virtual_width_px", "虚拟桌面宽度", "显示器", "多显示器虚拟桌面总宽度。", "整数", "px", "左侧信息", "0"),
        V("display.virtual_height_px", "虚拟桌面高度", "显示器", "多显示器虚拟桌面总高度。", "整数", "px", "左侧信息", "0"),
        V("display.monitor_count", "显示器数量", "显示器", "Windows 当前检测到的显示器数量。", "整数", "台", "左侧信息", "0"),
        V("display.system_dpi", "系统 DPI", "显示器", "Windows 系统 DPI。", "整数", "DPI", "左侧信息", "0"),
        V("display.scale_percent", "系统缩放比例", "显示器", "根据系统 DPI 换算的显示缩放百分比。", unit: "%", use: "右侧状态 / 圆环"),

        // ===== 时间 =====
        V("time.current", "当前时间（24 小时）", "时间", "本地当前时间，格式 HH:mm:ss。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("time.current_12h", "当前时间（12 小时）", "时间", "本地当前时间，12 小时制并包含 AM/PM。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("time.date", "当前日期", "时间", "本地当前日期，格式 yyyy-MM-dd。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("time.datetime", "当前日期时间", "时间", "本地当前日期时间。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("time.iso", "ISO 日期时间", "时间", "当前本地时间的 ISO 8601 表示。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("time.year", "年份", "时间", "当前年份。", "整数", "年", "左侧信息", "0"),
        V("time.month", "月份", "时间", "当前月份数字。", "整数", "月", "左侧信息", "0"),
        V("time.month_name", "月份名称", "时间", "当前文化区域下的月份名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("time.day", "日期（日）", "时间", "当前月中的日期。", "整数", "日", "左侧信息", "0"),
        V("time.day_of_week", "星期（中文）", "时间", "当前星期的中文名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("time.day_of_week_en", "星期（英文）", "时间", "当前星期英文名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("time.day_of_year", "一年中的第几天", "时间", "当前日期是一年中的第几天。", "整数", "天", "左侧信息", "0"),
        V("time.week_of_year", "周序号", "时间", "ISO 风格的当前周序号。", "整数", "周", "左侧信息", "0"),
        V("time.hour", "小时", "时间", "当前小时（0-23）。", "整数", "时", "左侧信息", "0"),
        V("time.minute", "分钟", "时间", "当前分钟。", "整数", "分", "左侧信息", "0"),
        V("time.second", "秒", "时间", "当前秒。", "整数", "秒", "左侧信息", "0"),
        V("time.millisecond", "毫秒", "时间", "当前毫秒。", "整数", "ms", "左侧信息", "0"),
        V("time.is_weekend", "是否周末", "时间", "当前日期是否为周六或周日。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("time.unix_seconds", "Unix 时间戳（秒）", "时间", "当前 Unix 时间戳，单位秒。", "数值", "秒", "左侧信息", "0"),
        V("time.unix_milliseconds", "Unix 时间戳（毫秒）", "时间", "当前 Unix 时间戳，单位毫秒。", "数值", "ms", "左侧信息", "0"),
        V("time.day.progress", "当天进程", "时间", "从当天 00:00:00 到次日 00:00:00 已经过的百分比。", unit: "%", use: "右侧状态 / 圆环"),
        V("time.day.elapsed_seconds", "当天已过时间", "时间", "当天已经过的秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.day.remaining_seconds", "当天剩余时间", "时间", "距离次日 00:00:00 的剩余秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.week.progress", "本周进程", "时间", "从本周周一 00:00 到下周周一 00:00 的已过百分比。", unit: "%", use: "右侧状态 / 圆环"),
        V("time.week.elapsed_seconds", "本周已过时间", "时间", "从本周周一开始已经过的秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.week.remaining_seconds", "本周剩余时间", "时间", "距离下周周一的剩余秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.month.progress", "本月进程", "时间", "从本月 1 日 00:00 到下月 1 日 00:00 的已过百分比。", unit: "%", use: "右侧状态 / 圆环"),
        V("time.month.elapsed_seconds", "本月已过时间", "时间", "本月已经过的秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.month.remaining_seconds", "本月剩余时间", "时间", "距离下月 1 日的剩余秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.year.progress", "本年进程", "时间", "从今年 1 月 1 日到明年 1 月 1 日的已过百分比。", unit: "%", use: "右侧状态 / 圆环"),
        V("time.year.elapsed_seconds", "本年已过时间", "时间", "今年已经过的秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.year.remaining_seconds", "本年剩余时间", "时间", "距离明年 1 月 1 日的剩余秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.display.progress", "时间方案显示进度", "时间", "时间方案用于圆环的进度；目标时间模式下为剩余比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("time.display.status_text", "时间方案状态文字", "时间", "普通模式显示当天进程；目标时间模式显示“剩余??%”。", "文本", use: "右侧状态", formats: "无需格式化"),
        V("time.target.value", "目标时间", "时间", "当前时间方案设置的每日目标时间。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("time.target.remaining_seconds", "距离目标时间", "时间", "距离下一次每日目标时间的剩余秒数。", unit: "秒", use: "左侧信息", formats: "duration"),
        V("time.target.remaining_percent", "目标时间剩余比例", "时间", "距离下一次目标时间的剩余时长占 24 小时的百分比。", unit: "%", use: "右侧状态 / 圆环"),
        V("time.target.progress", "目标时间已过比例", "时间", "每日目标时间周期中已经过的比例。", unit: "%", use: "右侧状态 / 圆环"),
        V("time.target.remaining_text", "目标时间剩余文字", "时间", "按“剩余??%”生成的目标时间状态文字。", "文本", use: "右侧状态", formats: "无需格式化"),

        // ===== DeepSeek API =====
        V("deepseek.balance", "DeepSeek 总余额", "DeepSeek API", "DeepSeek API 账户人民币总余额。", unit: "CNY", use: "左侧主值", formats: "0.00"),
        V("deepseek.balance_text", "DeepSeek 总余额文字", "DeepSeek API", "已格式化为“¥0.00”的总余额文字。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("deepseek.granted_balance", "DeepSeek 赠送余额", "DeepSeek API", "DeepSeek API 账户赠送余额。", unit: "CNY", use: "左侧信息", formats: "0.00"),
        V("deepseek.topped_up_balance", "DeepSeek 充值余额", "DeepSeek API", "DeepSeek API 账户充值余额。", unit: "CNY", use: "左侧信息", formats: "0.00"),
        V("deepseek.available", "DeepSeek API 可用状态", "DeepSeek API", "余额接口返回的账户可用状态。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("deepseek.available_text", "DeepSeek API 可用状态文字", "DeepSeek API", "根据可用状态生成“可用 / 不可用”。", "文本", use: "标题 / 右侧状态", formats: "无需格式化"),
        V("deepseek.period.name", "当前时段（英文）", "DeepSeek API", "当前为 PEAK 或 OFF-PEAK。", "文本", use: "标题", formats: "无需格式化"),
        V("deepseek.period.name_zh", "当前时段（中文）", "DeepSeek API", "当前时段中文名称：高峰或低谷。", "文本", use: "标题", formats: "无需格式化"),
        V("deepseek.period.is_peak", "是否高峰", "DeepSeek API", "当前是否处于高峰时段。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("deepseek.period.is_off_peak", "是否低谷", "DeepSeek API", "当前是否处于低谷时段。", "布尔", use: "标题 / 条件", formats: "无需格式化"),
        V("deepseek.period.remaining_seconds", "距离时段切换", "DeepSeek API", "距离下一次峰谷时段切换的剩余秒数。", unit: "秒", use: "左侧次值", formats: "duration"),
        V("deepseek.period.remaining_text", "时段剩余文字", "DeepSeek API", "“高峰时段剩余??:??:??”或“低谷时段剩余??:??:??”。", "文本", use: "左侧次值", formats: "无需格式化"),
        V("deepseek.period.progress", "当前时段已过进度", "DeepSeek API", "当前峰谷时段已经经过的百分比。", unit: "%", use: "右侧状态 / 圆环"),
        V("deepseek.period.progress_text", "时段已过文字", "DeepSeek API", "“高峰已过??%”或“低谷已过??%”。", "文本", use: "右侧状态", formats: "无需格式化"),
        V("deepseek.period.next_switch_time", "下次切换时间（北京时间）", "DeepSeek API", "下一次高峰/低谷切换的北京时间，格式 HH:mm:ss。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("deepseek.period.next_switch_datetime", "下次切换日期时间（北京时间）", "DeepSeek API", "下一次峰谷切换的北京时间日期与时间，格式 yyyy-MM-dd HH:mm:ss。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("deepseek.period.next_switch_time_local", "下次切换时间（本地）", "DeepSeek API", "将下一次峰谷切换时刻转换为当前 Windows 用户时区后的本地时间，格式 HH:mm:ss；自动考虑当地夏令时。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("deepseek.period.next_switch_datetime_local", "下次切换日期时间（本地）", "DeepSeek API", "将下一次峰谷切换时刻转换为当前 Windows 用户时区后的本地日期与时间，格式 yyyy-MM-dd HH:mm:ss；自动考虑当地夏令时。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("deepseek.period.timezone", "DeepSeek 峰谷基准时区", "DeepSeek API", "DeepSeek 峰谷规则使用的固定基准时区：北京时间 UTC+08:00。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("deepseek.period.local_timezone", "用户本地时区", "DeepSeek API", "当前 Windows 本地时区 ID，以及下一次切换时刻对应的 UTC 偏移；夏令时地区会按切换日期自动计算偏移。", "文本", use: "左侧信息", formats: "无需格式化"),
    };

    private static readonly IReadOnlyList<VariableDefinition> AdvancedBuiltIns = new List<VariableDefinition>
    {
        // ===== 电池进阶 =====
        V("battery.estimated_time_to_empty", "预计耗尽剩余时间", "电池", "Windows 电源管理估算的距离电池耗尽剩余秒数；系统无法估算时变量不可用。", unit: "秒", use: "左侧信息", formats: "duration / duration-long"),
        V("battery.estimated_time_to_full", "预计充满剩余时间", "电池", "根据当前剩余容量、满充容量和实时充电功率计算的预计充满时间；仅充电功率可用时提供。", unit: "秒", use: "左侧信息", formats: "duration / duration-long"),
        V("battery.design_vs_current_health", "设计容量健康度", "电池", "当前满充容量相对设计容量的比例，与电池健康度一致。", unit: "%", use: "右侧状态 / 圆环"),
        V("battery.temperature", "电池温度", "电池", "通过 Windows BatteryTemperature 接口读取；仅驱动/固件提供时可用。", unit: "°C", use: "左侧信息", formats: "0.0"),
        V("battery.chemistry", "电池化学类型", "电池", "Win32_Battery 报告的电池化学体系，如锂离子/锂聚合物。", "文本", use: "左侧信息", formats: "无需格式化"),

        // ===== CPU 进阶 =====
        V("cpu.usage_avg_1m", "CPU 1 分钟平均使用率", "CPU", "该变量启用后滚动统计最近 1 分钟 CPU 使用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.usage_avg_5m", "CPU 5 分钟平均使用率", "CPU", "该变量启用后滚动统计最近 5 分钟 CPU 使用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.usage_avg_15m", "CPU 15 分钟平均使用率", "CPU", "该变量启用后滚动统计最近 15 分钟 CPU 使用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.usage_max", "CPU 近期最高使用率", "CPU", "最近最多 15 分钟采样窗口内观察到的 CPU 最高使用率。", unit: "%", use: "右侧状态 / 圆环"),
        V("cpu.temperature_max", "CPU 最高温度", "CPU", "LibreHardwareMonitor 读取的 CPU 当前最高温度；硬件/驱动不支持时不可用。", unit: "°C", use: "右侧状态 / 圆环", formats: "0.0"),
        V("cpu.power_max", "CPU 功耗", "CPU", "LibreHardwareMonitor 当前 CPU 功率传感器的最高有效值。", unit: "W", use: "左侧信息", formats: "0.0"),
        V("cpu.core.temperature_avg", "CPU 核心平均温度", "CPU", "可用核心温度传感器的实时平均值。", unit: "°C", use: "左侧信息", formats: "0.0"),
        V("cpu.core.voltage", "CPU 核心电压", "CPU", "硬件监控传感器报告的 CPU 核心/最高有效电压。", unit: "V", use: "左侧信息", formats: "0.000"),
        V("cpu.bus_speed", "CPU 总线/BCLK", "CPU", "硬件监控传感器报告的 Bus/BCLK 频率。", unit: "MHz", use: "左侧信息", formats: "0.0"),
        V("cpu.instructions_per_second", "CPU 每秒退休指令", "CPU", "Windows Processor Information 性能计数器 InstructionsRetiredPersec；仅系统提供该计数器时可用。", unit: "instr/s", use: "左侧信息", formats: "auto:1"),
        V("cpu.context_switches", "上下文切换速率", "CPU", "Windows System 性能计数器 Context Switches/sec。", unit: "次/s", use: "左侧信息", formats: "0"),
        V("cpu.interrupts", "硬件中断速率", "CPU", "Windows Processor Information 性能计数器 Interrupts/sec。", unit: "次/s", use: "左侧信息", formats: "0"),
        V("cpu.dpc_time", "DPC 时间比例", "CPU", "Windows Processor Information 的 % DPC Time。", unit: "%", use: "右侧状态 / 圆环", formats: "0.0"),
        V("cpu.system_calls", "系统调用速率", "CPU", "Windows System 性能计数器 System Calls/sec。", unit: "次/s", use: "左侧信息", formats: "0"),

        // ===== 内存进阶 =====
        V("memory.standby_bytes", "Standby 缓存", "内存", "Windows Memory 性能计数器中 Standby Cache Core/Normal/Reserve 的合计。", unit: "Byte", use: "左侧信息", formats: "auto:1 / gb:1"),
        V("memory.modified_bytes", "Modified 页面", "内存", "Windows Modified Page List Bytes。", unit: "Byte", use: "左侧信息", formats: "auto:1 / mb:0"),
        V("memory.hardware_reserved_bytes", "硬件保留内存", "内存", "物理内存条安装容量与 Windows 可用物理内存总量之差。", unit: "Byte", use: "左侧信息", formats: "auto:1 / mb:0"),
        V("memory.speed_mhz", "内存工作频率", "内存", "Win32_PhysicalMemory 报告的最高 ConfiguredClockSpeed/Speed。", unit: "MHz", use: "左侧信息", formats: "0"),
        V("memory.slot_count", "内存插槽总数", "内存", "Win32_PhysicalMemoryArray 报告的内存设备插槽总数。", "整数", "个", "左侧信息", "0"),
        V("memory.slot_used", "已使用内存插槽", "内存", "当前枚举到的物理内存模块数量。", "整数", "个", "左侧信息", "0"),
        V("memory.form_factor", "内存形态", "内存", "内存模块 FormFactor，例如 DIMM / SODIMM。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("memory.type", "内存类型", "内存", "SMBIOS 报告的内存类型，例如 DDR4 / DDR5 / LPDDR5。", "文本", use: "左侧信息", formats: "无需格式化"),

        // ===== GPU 进阶 =====
        V("gpu.temperature", "GPU 核心温度", "GPU", "硬件监控传感器报告的 GPU Core 温度；硬件或驱动不提供时变量不可用。", unit: "°C", use: "左侧信息", formats: "0.0"),
        V("gpu.hotspot_temperature", "GPU 热点温度", "GPU", "硬件监控传感器报告的 GPU Hot Spot/核心热点温度。", unit: "°C", use: "右侧状态 / 圆环", formats: "0.0"),
        V("gpu.memory_junction_temperature", "GPU 显存结温", "GPU", "硬件监控传感器报告的显存/Memory Junction 温度。", unit: "°C", use: "左侧信息", formats: "0.0"),
        V("gpu.power_w", "GPU 当前功率", "GPU", "LibreHardwareMonitor 实时功率传感器值；仅硬件/驱动提供时可用。", unit: "W", use: "左侧信息", formats: "0.0"),
        V("gpu.power_limit_w", "GPU 功率限制", "GPU", "NVIDIA GPU 通过 nvidia-smi power.limit 读取；其他 GPU 若没有可靠功率限制接口则不提供。", unit: "W", use: "左侧信息", formats: "0.0"),
        V("gpu.voltage_v", "GPU 核心电压", "GPU", "硬件监控传感器报告的 GPU 核心电压。", unit: "V", use: "左侧信息", formats: "0.000"),
        V("gpu.core_clock_mhz", "GPU 核心频率", "GPU", "当前 GPU 核心实时频率。", unit: "MHz", use: "左侧信息", formats: "0"),
        V("gpu.memory_clock_mhz", "GPU 显存频率", "GPU", "当前 GPU 显存实时频率。", unit: "MHz", use: "左侧信息", formats: "0"),
        V("gpu.pcie_gen", "GPU PCIe 代际", "GPU", "nvidia-smi 提供的当前 PCIe Link Generation；NVIDIA 且 nvidia-smi 可用时提供。", "数值", "Gen", "左侧信息", "0"),
        V("gpu.pcie_lanes", "GPU PCIe 通道数", "GPU", "nvidia-smi 提供的当前 PCIe Link Width。", "数值", "lane", "左侧信息", "0"),
        V("gpu.driver_version", "GPU 驱动版本", "GPU", "Win32_VideoController 报告的所选 GPU 驱动版本。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("gpu.driver_date", "GPU 驱动日期", "GPU", "Win32_VideoController 报告的驱动日期。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("gpu.bios_version", "GPU VBIOS 版本", "GPU", "nvidia-smi 报告的 VBIOS 版本；支持时可用。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("gpu.nvidia_smi_available", "nvidia-smi 可用", "GPU", "系统是否存在可调用的 nvidia-smi.exe。", "布尔", use: "条件", formats: "无需格式化"),
        V("gpu.amd_adrenalin_available", "AMD Adrenalin 可用", "GPU", "检测 AMD Radeon Software 进程或默认安装路径。", "布尔", use: "条件", formats: "无需格式化"),

        // ===== 网络进阶 =====
        V("network.signal_dbm", "Wi-Fi 信号强度", "网络", "根据 Windows WLAN 信号质量换算的近似 dBm；有 Wi-Fi 连接时可用。", unit: "dBm", use: "左侧信息", formats: "0"),
        V("network.public_ipv4", "公网 IPv4", "网络", "通过 api.ipify.org 查询的公网 IPv4，5 分钟缓存；仅使用该变量时才发起请求。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.public_ipv6", "公网 IPv6", "网络", "通过 api6.ipify.org 查询的公网 IPv6，5 分钟缓存；没有 IPv6 时变量不可用。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.vpn_status", "VPN 状态", "网络", "根据活动 Tunnel/PPP/WireGuard/Wintun 等网络接口检测 VPN 是否活动。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("network.vpn_name", "VPN 名称", "网络", "当前检测到的活动 VPN 接口名称。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.proxy_status", "系统代理状态", "网络", "当前用户 Internet Settings 的 ProxyEnable 状态。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("network.proxy_address", "系统代理地址", "网络", "当前用户配置的 ProxyServer。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.dns_latency_ms", "系统 DNS 解析耗时", "网络", "本机系统解析 one.one.one.one 的耗时；可能受 DNS 缓存影响。", unit: "ms", use: "右侧状态 / 圆环", formats: "0.0"),
        V("network.tcp_connections", "活动 TCP 连接数", "网络", "IPGlobalProperties 返回的活动 TCP 连接数量。", "整数", "条", "左侧信息", "0"),
        V("network.udp_connections", "UDP 监听端点数", "网络", "IPGlobalProperties 返回的活动 UDP 监听端点数量。", "整数", "个", "左侧信息", "0"),
        V("network.wifi_channel", "Wi-Fi 信道", "网络", "当前 WLAN 接口的无线信道。", "数值", use: "左侧信息", formats: "0"),
        V("network.wifi_band", "Wi-Fi 频段", "网络", "当前 WLAN 接口的频段；新系统直接读取 Band，旧系统在可推断时回退。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("network.wifi_standard", "Wi-Fi 标准", "网络", "当前 WLAN Radio type，例如 802.11ax。", "文本", use: "左侧信息", formats: "无需格式化"),

        // ===== 系统进阶 =====
        V("system.power_plan", "电源计划 GUID", "系统", "powercfg 当前活动电源方案 GUID。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.power_plan_name", "电源计划名称", "系统", "当前 Windows 活动电源计划名称。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.bios_version", "BIOS 版本", "系统", "Win32_BIOS SMBIOSBIOSVersion。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.bios_date", "BIOS 日期", "系统", "Win32_BIOS ReleaseDate。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.motherboard_manufacturer", "主板制造商", "系统", "Win32_BaseBoard Manufacturer。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.motherboard_model", "主板型号", "系统", "Win32_BaseBoard Product。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.motherboard_temperature", "主板最高温度", "系统", "LibreHardwareMonitor 可读取的主板温度传感器最高值。", unit: "°C", use: "左侧信息", formats: "0.0"),
        V("system.fan_speed", "风扇最高转速", "系统", "LibreHardwareMonitor 当前可见风扇的最高 RPM。", unit: "RPM", use: "左侧信息", formats: "0"),
        V("system.fan_speed_percent", "风扇控制百分比", "系统", "硬件监控 Control 类型传感器的最高百分比。", unit: "%", use: "右侧状态 / 圆环", formats: "0"),
        V("system.update_pending", "Windows 更新待处理", "系统", "检测可用 Windows 更新以及 Windows Update / Component Based Servicing 的待重启状态。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("system.update_last_installed", "最近安装更新日期", "系统", "Win32_QuickFixEngineering 中最近的 InstalledOn。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("system.defender_status", "Microsoft Defender 状态", "系统", "Windows Defender WMI 报告的启用/实时保护状态。", "文本", use: "状态", formats: "无需格式化"),
        V("system.firewall_status", "Windows 防火墙状态", "系统", "Windows Firewall Policy 当前活动配置状态。", "文本", use: "状态", formats: "无需格式化"),
        V("system.bitlocker_status", "系统盘 BitLocker 状态", "系统", "Windows Volume Encryption 接口报告的系统盘保护状态。", "文本", use: "状态", formats: "无需格式化"),
        V("system.hyper_v_status", "Hyper-V 状态", "系统", "Windows OptionalFeature 中 Microsoft-Hyper-V-All 是否安装启用。", "布尔", use: "状态", formats: "无需格式化"),
        V("system.wsl_status", "WSL 状态", "系统", "检测 WSL 可执行文件/已注册发行版。", "布尔", use: "状态", formats: "无需格式化"),
        V("system.wsl_distro_count", "WSL 发行版数量", "系统", "当前用户已注册的 WSL 发行版数量。", "整数", "个", "左侧信息", "0"),

        // ===== 进程进阶 =====
        V("process.background.count", "后台进程数量", "进程", "当前没有主窗口句柄的进程数量。", "整数", "个", "左侧信息", "0"),
        V("process.top_cpu.name", "CPU 占用最高进程", "进程", "最近一次采样中 CPU 使用率最高的进程名。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("process.top_cpu.pid", "CPU 最高进程 PID", "进程", "CPU 使用率最高进程的 PID。", "整数", use: "左侧信息", formats: "0"),
        V("process.top_cpu.usage", "最高进程 CPU 使用率", "进程", "按进程 TotalProcessorTime 差值计算的 CPU 使用率。", unit: "%", use: "右侧状态 / 圆环", formats: "0.0"),
        V("process.top_memory.name", "内存占用最高进程", "进程", "工作集最大的进程名。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("process.top_memory.pid", "内存最高进程 PID", "进程", "工作集最大的进程 PID。", "整数", use: "左侧信息", formats: "0"),
        V("process.top_memory.usage", "最高进程内存占用", "进程", "工作集最大的进程当前 Working Set。", unit: "Byte", use: "左侧信息", formats: "auto:1 / mb:1"),
        V("process.top_disk.name", "磁盘 I/O 最高进程", "进程", "Windows PerfProc IODataBytesPersec 最大的进程名。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("process.top_disk.pid", "磁盘 I/O 最高进程 PID", "进程", "磁盘 I/O 最高进程 PID。", "整数", use: "左侧信息", formats: "0"),
        V("process.top_disk.usage", "最高进程磁盘 I/O", "进程", "该进程当前总 I/O 字节速率。", unit: "Byte/s", use: "左侧信息", formats: "auto:1 / speed"),
        V("process.gpu.top.name", "GPU 占用最高进程", "进程", "GPU Engine 性能计数器中 GPU 使用率最高的进程名。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("process.gpu.top.usage", "最高进程 GPU 使用率", "进程", "GPU Engine 性能计数器汇总后的进程 GPU 使用率。", unit: "%", use: "右侧状态 / 圆环", formats: "0.0"),

        // ===== 软件自身进阶 =====
        V("app.theme", "应用主题", "ECP 应用", "当前 Endfield Charge Plus 使用的主题。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("app.preset_name", "当前方案名称", "ECP 应用", "当前选中的 HUD 方案显示名称。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"),
        V("app.active_profile", "当前方案 ID", "ECP 应用", "当前 HUD 方案内部 ID。", "文本", use: "调试 / 条件", formats: "无需格式化"),
        V("app.gpu_usage", "本程序 GPU 使用率", "ECP 应用", "GPU Engine 性能计数器中当前 Endfield Charge Plus 进程的利用率。", unit: "%", use: "右侧状态 / 圆环", formats: "0.0"),

        // ===== 显示器进阶 =====
        V("display.primary.name", "主显示设备名称", "显示器", "Windows 当前活动显示设备名称。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("display.primary.color_depth", "主显示器色深", "显示器", "EnumDisplaySettings 返回的 BitsPerPel。", "整数", "bit", "左侧信息", "0"),
        V("display.primary.refresh_rate", "主显示器刷新率", "显示器", "当前显示模式刷新率。", "整数", "Hz", "左侧信息", "0"),
        V("display.secondary.name", "第二显示器名称", "显示器", "检测到第二个活动显示设备时提供。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("display.secondary.resolution", "第二显示器分辨率", "显示器", "第二显示器当前分辨率。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("display.secondary.refresh_rate", "第二显示器刷新率", "显示器", "第二显示器当前刷新率。", "整数", "Hz", "左侧信息", "0"),

        // ===== 世界时间 =====
        V("time.world.nyc", "纽约时间", "时间", "按 Windows Eastern Standard Time 实时换算。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("time.world.london", "伦敦时间", "时间", "按 Windows GMT Standard Time 实时换算。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("time.world.tokyo", "东京时间", "时间", "按 Windows Tokyo Standard Time 实时换算。", "文本", use: "左侧主值", formats: "无需格式化"),
        V("time.world.beijing", "北京时间", "时间", "按 Windows China Standard Time 实时换算。", "文本", use: "左侧主值", formats: "无需格式化"),

        // ===== 网络包探测器进阶 =====
        V("probe.dns_resolve_time", "DNS 解析耗时", "网络探测", "当检测地址为域名时，当前探测周期的 DNS 解析耗时；直接 IP 时为 0。", unit: "ms", use: "左侧信息", formats: "0.0"),
        V("probe.tcp_connect_time", "TCP 建连耗时", "网络探测", "TCP 模式下从发起连接到连接成功的耗时。", unit: "ms", use: "左侧信息", formats: "0.0"),

        // ===== DeepSeek API 进阶 =====
        V("deepseek.api.latency_ms", "DeepSeek API 延迟", "DeepSeek API", "最近一次官方 /user/balance 请求的 HTTP 往返耗时；余额请求 1 分钟缓存。", unit: "ms", use: "右侧状态 / 圆环", formats: "0.0"),

        // ===== 剪贴板 =====
        V("clipboard.has_text", "剪贴板含文本", "剪贴板", "当前 Windows 剪贴板是否包含 Unicode 文本。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("clipboard.text_length", "剪贴板文本长度", "剪贴板", "当前剪贴板文本字符数。", "整数", "字符", "左侧信息", "0"),
        V("clipboard.preview", "剪贴板文本预览", "剪贴板", "当前剪贴板文本去除多余空白后的前 80 个字符。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("clipboard.has_image", "剪贴板含图像", "剪贴板", "当前剪贴板是否包含 Bitmap/DIB 图像格式。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("clipboard.image_size", "剪贴板图像尺寸", "剪贴板", "DIB/DIBV5 图像可解析时显示宽×高。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("clipboard.file_count", "剪贴板文件数量", "剪贴板", "CF_HDROP 文件复制列表中的文件数量。", "整数", "个", "左侧信息", "0"),
        V("clipboard.last_updated", "剪贴板最后变化时间", "剪贴板", "应用观察到 Clipboard Sequence Number 变化的本地时间。", "文本", use: "左侧信息", formats: "time:HH:mm:ss"),

        // ===== USB / 外设 =====
        V("usb.device.count", "USB 设备数量", "USB / 外设", "Win32_PnPEntity 中 USB PNP 设备数量。", "整数", "个", "左侧信息", "0"),
        V("usb.device.list", "USB 设备列表", "USB / 外设", "当前枚举到的 USB PNP 设备名称列表。", "文本", use: "左侧信息", formats: "sub:0:80"),
        V("usb.storage.count", "可移动存储数量", "USB / 外设", "当前已就绪 DriveType.Removable 卷数量。", "整数", "个", "左侧信息", "0"),
        V("usb.storage.list", "可移动存储列表", "USB / 外设", "当前可移动存储盘符列表。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("peripheral.mouse.name", "鼠标名称", "USB / 外设", "Win32_PointingDevice 报告的首个指针设备名称。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("peripheral.keyboard.name", "键盘名称", "USB / 外设", "Win32_Keyboard 报告的首个键盘设备名称。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("peripheral.gamepad.count", "XInput 手柄数量", "USB / 外设", "XInput 0-3 控制器槽位中已连接的手柄数量。", "整数", "个", "左侧信息", "0"),
        V("peripheral.gamepad.name", "手柄名称", "USB / 外设", "当前检测到的 XInput 控制器槽位名称。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("peripheral.gamepad.battery", "手柄电量", "USB / 外设", "XInput 电池等级换算的近似百分比。", unit: "%", use: "右侧状态 / 圆环", formats: "0"),

        // ===== 开发者工具 =====
        V("dev.docker.running", "Docker 是否运行", "开发者工具", "检测 Docker Desktop / dockerd 后台进程。", "布尔", use: "状态", formats: "无需格式化"),
        V("dev.docker.containers", "Docker 运行容器数", "开发者工具", "docker ps -q 返回的运行中容器数量；Docker CLI 可用时提供。", "整数", "个", "左侧信息", "0"),
        V("dev.docker.images", "Docker 镜像数", "开发者工具", "docker images -q 的唯一镜像 ID 数量。", "整数", "个", "左侧信息", "0"),
        V("dev.wsl.running", "WSL 是否运行", "开发者工具", "检测 wsl/wslhost/vmmemWSL 相关进程。", "布尔", use: "状态", formats: "无需格式化"),
        V("dev.wsl.distro", "WSL 发行版列表", "开发者工具", "wsl -l -q 返回的发行版列表。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("dev.wsl.memory_usage", "WSL 内存占用", "开发者工具", "vmmemWSL/vmmem 工作集总量。", unit: "Byte", use: "左侧信息", formats: "auto:1 / gb:1"),
        V("dev.git.branch", "Git 当前分支", "开发者工具", "应用当前工作目录为 Git 仓库时返回分支。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("dev.git.status", "Git 工作区状态", "开发者工具", "当前工作目录 Git 状态，clean 或变更数量。", "文本", use: "状态", formats: "无需格式化"),
        V("dev.git.last_commit", "Git 最近提交", "开发者工具", "当前工作目录最近一次提交的短哈希和标题。", "文本", use: "左侧信息", formats: "sub:0:80"),
        V("dev.node.version", "Node.js 版本", "开发者工具", "PATH 中 node --version 的结果。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("dev.python.version", "Python 版本", "开发者工具", "PATH 中 python/py --version 的结果。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("dev.java.version", "Java 版本", "开发者工具", "PATH 中 java -version 的首行结果。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("dev.golang.version", "Go 版本", "开发者工具", "PATH 中 go version 的结果。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("dev.rust.version", "Rust 版本", "开发者工具", "PATH 中 rustc --version 的结果。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("dev.vscode.running", "VS Code 是否运行", "开发者工具", "检测 Code / Code - Insiders 进程。", "布尔", use: "状态", formats: "无需格式化"),
        V("dev.terminal.running", "终端是否运行", "开发者工具", "检测 Windows Terminal / PowerShell / cmd 进程。", "布尔", use: "状态", formats: "无需格式化"),
        V("dev.ide.running", "IDE 是否运行", "开发者工具", "检测 VS Code、Visual Studio、Rider、IntelliJ/PyCharm 等常见 IDE 进程。", "布尔", use: "状态", formats: "无需格式化"),
        V("dev.llm.local_status", "本地 LLM 状态", "开发者工具", "检测 Ollama、LM Studio、llama-server 等常见本地模型进程。", "文本", use: "状态", formats: "无需格式化"),

        // ===== 安全 =====
        V("security.defender.status", "Defender 状态", "安全", "Windows Defender WMI 实时保护状态。", "文本", use: "状态", formats: "无需格式化"),
        V("security.defender.last_scan", "Defender 最近扫描", "安全", "最近一次 Quick/Full Scan 完成时间。", "文本", use: "左侧信息", formats: "time:relative"),
        V("security.defender.threats", "Defender 当前威胁数", "安全", "MSFT_MpThreat 当前枚举到的威胁数量。", "整数", "项", "右侧状态", "0"),
        V("security.firewall.status", "防火墙状态", "安全", "Windows Firewall 当前活动配置是否启用。", "文本", use: "状态", formats: "无需格式化"),
        V("security.firewall.profile", "防火墙配置文件", "安全", "当前活动防火墙配置文件：域/专用/公用。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("security.bitlocker.status", "BitLocker 状态", "安全", "系统卷 BitLocker ProtectionStatus。", "文本", use: "状态", formats: "无需格式化"),
        V("security.bitlocker.encryption_percent", "BitLocker 加密进度", "安全", "系统卷 EncryptionPercentage。", unit: "%", use: "右侧状态 / 圆环", formats: "0"),
        V("security.secure_boot", "Secure Boot", "安全", "UEFISecureBootEnabled 注册表状态。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("security.tpm.present", "TPM 存在", "安全", "Win32_Tpm 是否可枚举。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("security.tpm.version", "TPM 版本", "安全", "Win32_Tpm SpecVersion。", "文本", use: "左侧信息", formats: "无需格式化"),
        V("security.uac_status", "UAC 状态", "安全", "EnableLUA 注册表状态。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("security.smartscreen_status", "SmartScreen 状态", "安全", "Windows Explorer SmartScreenEnabled 配置。", "文本", use: "状态", formats: "无需格式化"),
        V("security.windows_update.status", "Windows Update 状态", "安全", "综合待重启标志和可用更新搜索生成的状态。", "文本", use: "状态", formats: "无需格式化"),
        V("security.windows_update.pending_count", "待安装更新数量", "安全", "Microsoft.Update.Session 搜索到的未安装且未隐藏更新数量。", "整数", "项", "右侧状态", "0"),
        V("security.vpn.active", "VPN 活动状态", "安全", "与 network.vpn_status 同源的活动 VPN 检测。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
        V("security.proxy.enabled", "代理启用状态", "安全", "与 network.proxy_status 同源的系统代理状态。", "布尔", use: "状态 / 条件", formats: "无需格式化"),
    };

    private static readonly HashSet<string> AdvancedKeySet = new(AdvancedBuiltIns.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);

    internal static bool IsAdvancedKey(string key) => AdvancedKeySet.Contains(key);

    private static readonly Lazy<IReadOnlyList<VariableDefinition>> DynamicDriveBuiltIns = new(() =>
    {
        var list = new List<VariableDefinition>();
        AddDynamicDriveDefinitions(list);
        return list;
    });

    // Variable Library display order is intentionally semantic rather than alphabetical.
    // This keeps Chinese and English UI ordering identical and stable across localization.
    private static readonly IReadOnlyDictionary<string, int> CategoryDisplayRank =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["CPU"] = 0,
            ["GPU"] = 1,
            ["内存"] = 2,
            ["磁盘"] = 3,
            ["电池"] = 4,
            ["网络"] = 5,
            ["网络探测"] = 6,
            ["Ping（兼容）"] = 7,
            ["系统"] = 8,
            ["显示器"] = 9,
            ["时间"] = 10,
            ["进程"] = 11,
            ["ECP 应用"] = 12,
            ["DeepSeek API"] = 13,
            ["安全"] = 14,
            ["USB / 外设"] = 15,
            ["剪贴板"] = 16,
            ["开发者工具"] = 17,
            ["自定义数据"] = 18,
        };

    // Within a category, common live/status values are shown before detailed/static values.
    // Prefixes are only display hints; variable keys and runtime behavior are not changed.
    private static readonly IReadOnlyDictionary<string, string[]> VariableDisplayPrefixes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["CPU"] =
            [
                "cpu.usage", "cpu.idle", "cpu.frequency",
                "cpu.temperature", "cpu.power", "cpu.core", "cpu.bus",
                "cpu.name", "cpu.manufacturer", "cpu.architecture",
                "cpu.physical", "cpu.logical", "cpu.socket", "cpu.virtualization",
                "cpu.l2", "cpu.l3",
                "cpu.instructions", "cpu.context", "cpu.interrupts", "cpu.dpc", "cpu.system_calls"
            ],
            ["GPU"] =
            [
                "gpu.usage",
                "gpu.temperature", "gpu.hotspot", "gpu.memory_junction",
                "gpu.power", "gpu.voltage", "gpu.core_clock", "gpu.memory_clock",
                "gpu.dedicated_used", "gpu.dedicated_total", "gpu.dedicated_usage",
                "gpu.shared_used", "gpu.shared_limit", "gpu.shared_usage",
                "gpu.memory_used", "gpu.memory_total",
                "gpu.total_memory_used", "gpu.total_memory_limit", "gpu.total_memory_usage",
                "gpu.vram", "gpu.uses_unified_memory",
                "gpu.name", "gpu.count", "gpu.physical_index", "gpu.adapter_id",
                "gpu.pcie", "gpu.driver", "gpu.bios",
                "gpu.nvidia_smi", "gpu.amd_adrenalin"
            ],
            ["内存"] =
            [
                "memory.usage", "memory.used", "memory.available", "memory.total", "memory.free_percent",
                "memory.commit",
                "memory.virtual",
                "memory.cache", "memory.standby", "memory.modified",
                "memory.paged_pool", "memory.nonpaged_pool",
                "memory.hardware_reserved",
                "memory.speed", "memory.slot", "memory.form_factor", "memory.type"
            ],
            ["磁盘"] =
            [
                "disk.system.usage", "disk.system.used", "disk.system.free", "disk.system.total",
                "disk.system.read", "disk.system.write", "disk.system.io",
                "disk.system.active", "disk.system.queue",
                "disk.system.root", "disk.system.label", "disk.system.filesystem",
                "disk.fixed.usage", "disk.fixed.used", "disk.fixed.free", "disk.fixed.total",
                "disk.fixed.count", "disk.fixed.list"
            ],
            ["电池"] =
            [
                "battery.status_text", "battery.percent", "battery.power_source",
                "battery.ac_online", "battery.charging", "battery.discharging", "battery.saver_on",
                "battery.rate_watts", "battery.charge_rate_watts", "battery.discharge_rate_watts",
                "battery.remaining_wh", "battery.full_wh", "battery.design_wh",
                "battery.remaining_mwh", "battery.full_mwh", "battery.design_mwh",
                "battery.health_percent", "battery.design_vs_current_health",
                "battery.time_remaining", "battery.estimated_time_to_empty", "battery.estimated_time_to_full",
                "battery.full_life",
                "battery.voltage", "battery.temperature", "battery.cycle_count", "battery.chemistry"
            ],
            ["网络"] =
            [
                "network.available",
                "network.download", "network.upload", "network.total_bps",
                "network.download_mbps", "network.upload_mbps", "network.total_mbps",
                "network.display", "network.profile",
                "network.link_speed", "network.max_link_speed", "network.utilization",
                "network.active_interface", "network.interface",
                "network.ipv4", "network.ipv6", "network.default_gateways", "network.dns_servers",
                "network.public",
                "network.signal_dbm", "network.wifi",
                "network.vpn", "network.proxy",
                "network.dns_latency", "network.tcp_connections", "network.udp_connections",
                "network.total_received", "network.total_sent", "network.total_transferred",
                "network.packets", "network.receive_errors", "network.send_errors"
            ],
            ["网络探测"] =
            [
                "probe.target", "probe.endpoint", "probe.protocol", "probe.port", "probe.address",
                "probe.online", "probe.status_text", "probe.reply_status",
                "probe.latency_ms", "probe.latency_text", "probe.latency_progress",
                "probe.avg_latency", "probe.min_latency", "probe.max_latency", "probe.jitter",
                "probe.loss_percent", "probe.sent", "probe.received", "probe.lost",
                "probe.full_scale", "probe.timeout", "probe.ttl",
                "probe.dns_resolve_time", "probe.tcp_connect_time",
                "probe.last_success", "probe.error"
            ],
            ["Ping（兼容）"] =
            [
                "ping.target", "ping.endpoint", "ping.protocol", "ping.port", "ping.address",
                "ping.online", "ping.status_text", "ping.reply_status",
                "ping.latency_ms", "ping.latency_text", "ping.progress",
                "ping.avg_latency", "ping.min_latency", "ping.max_latency", "ping.jitter",
                "ping.loss_percent", "ping.sent", "ping.received", "ping.lost",
                "ping.full_scale", "ping.timeout", "ping.ttl",
                "ping.last_success", "ping.error"
            ],
            ["系统"] =
            [
                "system.os_description", "system.os_version", "system.os_build",
                "system.os_architecture", "system.process_architecture", "system.framework_version",
                "system.machine_name", "system.host_name", "system.user_name", "system.user_domain",
                "system.uptime", "system.boot_time",
                "system.timezone", "system.utc_offset", "system.culture",
                "system.processor_count",
                "system.power_plan_name", "system.power_plan",
                "system.bios", "system.motherboard", "system.fan",
                "system.update", "system.defender", "system.firewall", "system.bitlocker",
                "system.hyper_v", "system.wsl",
                "system.time", "system.date", "system.datetime"
            ],
            ["显示器"] =
            [
                "display.monitor_count",
                "display.primary.name", "display.primary_width", "display.primary_height",
                "display.primary.refresh_rate", "display.primary.color_depth",
                "display.system_dpi", "display.scale_percent",
                "display.virtual",
                "display.secondary"
            ],
            ["时间"] =
            [
                "time.current", "time.current_12h", "time.date", "time.datetime", "time.iso",
                "time.world",
                "time.year", "time.month", "time.month_name", "time.day",
                "time.day_of_week", "time.day_of_year", "time.week_of_year",
                "time.hour", "time.minute", "time.second", "time.millisecond",
                "time.is_weekend", "time.unix",
                "time.day.progress", "time.day.elapsed", "time.day.remaining",
                "time.week.progress", "time.week.elapsed", "time.week.remaining",
                "time.month.progress", "time.month.elapsed", "time.month.remaining",
                "time.year.progress", "time.year.elapsed", "time.year.remaining",
                "time.display",
                "time.target"
            ],
            ["进程"] =
            [
                "process.top_cpu", "process.top_memory", "process.top_disk", "process.gpu.top",
                "process.background"
            ],
            ["ECP 应用"] =
            [
                "app.name", "app.version", "app.theme", "app.preset_name", "app.active_profile",
                "app.pid", "app.start_time", "app.uptime",
                "app.cpu_time", "app.gpu_usage",
                "app.working_set", "app.private_memory", "app.virtual_memory",
                "app.thread_count", "app.handle_count"
            ],
            ["DeepSeek API"] =
            [
                "deepseek.available", "deepseek.api.latency",
                "deepseek.balance_text", "deepseek.balance", "deepseek.granted_balance", "deepseek.topped_up_balance",
                "deepseek.period.name", "deepseek.period.is_peak", "deepseek.period.is_off_peak",
                "deepseek.period.remaining", "deepseek.period.progress",
                "deepseek.period.next_switch_time_local", "deepseek.period.next_switch_datetime_local",
                "deepseek.period.local_timezone",
                "deepseek.period.next_switch_time", "deepseek.period.next_switch_datetime",
                "deepseek.period.timezone"
            ],
            ["安全"] =
            [
                "security.secure_boot", "security.tpm", "security.uac", "security.smartscreen",
                "security.defender", "security.firewall", "security.bitlocker",
                "security.windows_update", "security.vpn", "security.proxy"
            ],
            ["USB / 外设"] =
            [
                "usb.device", "usb.storage",
                "peripheral.mouse", "peripheral.keyboard", "peripheral.gamepad"
            ],
            ["剪贴板"] =
            [
                "clipboard.has_text", "clipboard.preview", "clipboard.text_length",
                "clipboard.has_image", "clipboard.image_size",
                "clipboard.file_count", "clipboard.last_updated"
            ],
            ["开发者工具"] =
            [
                "dev.ide", "dev.vscode", "dev.terminal",
                "dev.git",
                "dev.node", "dev.python", "dev.java", "dev.golang", "dev.rust",
                "dev.docker", "dev.wsl", "dev.llm"
            ],
        };

    private static int GetCategoryDisplayRank(string category) =>
        CategoryDisplayRank.TryGetValue(category, out int rank) ? rank : int.MaxValue;

    private static int GetVariablePrefixRank(VariableDefinition item)
    {
        if (!VariableDisplayPrefixes.TryGetValue(item.Category, out var prefixes))
            return int.MaxValue;

        for (int i = 0; i < prefixes.Length; i++)
        {
            if (item.Key.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue;
    }

    public static IReadOnlyList<VariableDefinition> AllBuiltIns { get; } =
        StaticBuiltIns.Concat(AdvancedBuiltIns).Concat(DynamicDriveBuiltIns.Value)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select((item, sourceIndex) => new { Item = item, SourceIndex = sourceIndex })
            .OrderBy(x => GetCategoryDisplayRank(x.Item.Category))
            .ThenBy(x => GetVariablePrefixRank(x.Item))
            .ThenBy(x => x.SourceIndex)
            .Select(x => x.Item)
            .ToList();

    public static VariableDefinition? Find(string key) =>
        AllBuiltIns.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public static VariableDefinition CreateCustom(string key) => new(
        key,
        key.Split('.').LastOrDefault() ?? key,
        "自定义数据",
        "来自 HTTP / JSON 数据源的自定义变量。实际含义由数据源映射决定。",
        "动态",
        "",
        "按数据含义决定",
        "0 / 0.0 / gb:1 / speed / duration 等");

    private static void AddDynamicDriveDefinitions(List<VariableDefinition> list)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                string letter = drive.Name.TrimEnd('\\', '/').TrimEnd(':').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(letter)) continue;
                string title = drive.Name.TrimEnd('\\', '/');
                list.Add(V($"disk.{letter}.used_bytes", $"{title} 已用空间", "磁盘", $"{title} 驱动器已使用空间。", unit: "Byte", use: "左侧主值", formats: "gb:1 / bytes"));
                list.Add(V($"disk.{letter}.free_bytes", $"{title} 可用空间", "磁盘", $"{title} 驱动器可用空间。", unit: "Byte", use: "左侧信息", formats: "gb:1 / bytes"));
                list.Add(V($"disk.{letter}.total_bytes", $"{title} 总容量", "磁盘", $"{title} 驱动器总容量。", unit: "Byte", use: "左侧次值", formats: "gb:1 / bytes"));
                list.Add(V($"disk.{letter}.usage", $"{title} 使用率", "磁盘", $"{title} 驱动器空间使用率。", unit: "%", use: "右侧状态 / 圆环"));
                list.Add(V($"disk.{letter}.label", $"{title} 卷标", "磁盘", $"{title} 驱动器卷标。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"));
                list.Add(V($"disk.{letter}.filesystem", $"{title} 文件系统", "磁盘", $"{title} 驱动器文件系统。", "文本", use: "标题 / 左侧信息", formats: "无需格式化"));
                list.Add(V($"disk.{letter}.read_bps", $"{title} 实时读取速度", "磁盘", $"{title} 逻辑卷 Windows 性能计数器实时读取速度。", unit: "Byte/s", use: "左侧信息", formats: "auto:1 / speed"));
                list.Add(V($"disk.{letter}.write_bps", $"{title} 实时写入速度", "磁盘", $"{title} 逻辑卷 Windows 性能计数器实时写入速度。", unit: "Byte/s", use: "左侧信息", formats: "auto:1 / speed"));
                list.Add(V($"disk.{letter}.io_bps", $"{title} 实时总 I/O", "磁盘", $"{title} 当前读取+写入总速率。", unit: "Byte/s", use: "左侧信息", formats: "auto:1 / speed"));
                list.Add(V($"disk.{letter}.active_percent", $"{title} 活动时间", "磁盘", $"{title} PercentDiskTime。", unit: "%", use: "右侧状态 / 圆环", formats: "0.0"));
                list.Add(V($"disk.{letter}.queue_length", $"{title} 队列长度", "磁盘", $"{title} CurrentDiskQueueLength。", unit: "项", use: "左侧信息", formats: "0.0"));
                list.Add(V($"disk.{letter}.health", $"{title} 磁盘健康状态", "磁盘", $"{title} 所在物理磁盘的 Windows Storage HealthStatus；Storage 接口支持时可用。", "文本", use: "状态", formats: "无需格式化"));
                list.Add(V($"disk.{letter}.temperature", $"{title} 磁盘温度", "磁盘", $"{title} 所在磁盘的 Storage Reliability Counter 温度；设备支持时可用。", unit: "°C", use: "左侧信息", formats: "0"));
                list.Add(V($"disk.{letter}.power_on_hours", $"{title} 通电时间", "磁盘", $"{title} 所在磁盘的 PowerOnHours；设备支持时可用。", unit: "小时", use: "左侧信息", formats: "0"));
                list.Add(V($"disk.{letter}.trim_status", $"{title} TRIM 状态", "磁盘", $"{title} 文件系统对应的 Windows DisableDeleteNotify 状态。", "文本", use: "状态", formats: "无需格式化"));
                list.Add(V($"disk.{letter}.smart_status", $"{title} 存储运行状态", "磁盘", $"{title} 所在物理磁盘的 Windows Storage OperationalStatus。", "文本", use: "状态", formats: "无需格式化"));
                list.Add(V($"disk.{letter}.partition_count", $"{title} 关联分区数量", "磁盘", $"{title} 逻辑卷通过 Win32_LogicalDiskToPartition 关联的分区数量。", "整数", "个", "左侧信息", "0"));
            }
        }
        catch { }
    }
}
