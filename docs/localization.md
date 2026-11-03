# 本地化维护说明（2026-10-23）

只维护最初的 20 种语言：zh-CN, en-US, zh-TW, ja-JP, ko-KR, de-DE, fr-FR, es-ES, pt-BR, ru-RU, ar-SA, hi-IN, bn-BD, ur-PK, id-ID, vi-VN, th-TH, tr-TR, it-IT, fa-IR。

## 通用语言与显示名称

20 种语言面向各地使用者，语言选择仅显示原生语言名，不附国家或地区。简体与繁体按文字系统区分；English、繁體中文、Português 等均不限定使用地区。英文采用通用技术文案，其他译文也避免把语言与单一国家绑定。

en-US、zh-TW、pt-BR 等旧资源代码仅用于兼容已有语言包、配置和系统语言匹配，不是使用范围限制；en-GB/en-AU/en-IN 等共用英语，zh-Hant/zh-HK/zh-MO 共用繁体中文，其他语言同样按语言族匹配。时区名称、地理数据和服务实际计费时区仍保留准确含义。

## 资源格式

运行时读取 core.lang、语言 .lang 和对应 SHA-256，头部为 #20261020$2470。UTF-8 无 BOM、LF；一行对应一个字段。字段内换行用 \n，反斜杠用 \\。旧 JSON 译文保留作历史维护资料，不能覆盖已审校 .lang。

本轮原位归类 339 个平台专属字段；译文行号、内容和总数不变。加上原有标题，共 Windows 94、Linux 18、macOS 45、Android 186 个专属字段。2127 个共用字段不改名。

| 平台 | 示例 |
| --- | --- |
| Windows | windows.pawnio.install_or_update、windows.aida64.setup_instructions |
| Linux | linux.frames.x11_required、linux.cpu.procfs_description |
| macOS | macos.permission.screen_recording_required、macos.gpu.unified_memory |
| Android | android.overlay、android.permission.overlay、android.display.avoid_cutout、android.island.native_android |

platform-fields.json 记录新名、原始英文键、行号与来源；加载器兼容旧文案调用和新字段名。en-US 显示文案不一定等于原始查找键，不能只靠英文值反推映射。

专属字段和共用字段仍放在同一个语言包中。Endfield Charge Plus 和各平台完整产品名保留原文；变量、公式、API 和技术枚举不翻译。

## 自动语言

新配置为 Auto；手动选择优先。桌面使用 CultureInfo.CurrentUICulture，安卓使用系统语言列表第一项。先精确匹配，再按文字系统、地区及语言族回退。中文 Hans/Hant 优先于地区；未指定文字系统时 TW/HK/MO 用繁体。完全不支持的系统语言回退英语。

例：de-AT → de-DE，fr-CA → fr-FR，pt-PT → pt-BR，zh-Hans-HK → zh-CN，zh-Hant-CN → zh-TW。不支持的保存选项回到 Auto；桌面初始化或重新应用偏好时解析系统语言。

## 检查与维护

20 种语言各有 2470 个非空值。本轮修正 1225 个语言字段，补齐原有 16 处空白。更新提醒、产品名、变量和格式占位符、平台别名均纳入检查。

Windows 20 语言界面检查通过，安卓相关核心和应用测试通过；Linux/macOS 托管构建通过。本机没有本轮原生 Linux/macOS GUI 或安卓真机实测。

langtool.py verify 校验结构、平台映射和哈希；audit 检查实际 .lang 字段及占位符。sync_platform_resources.py 同步核心、值文件和平台元数据，并保护目标独有译文。core 维护保留已发布索引，不重复加入已归类的旧英文键。

产品版本不因翻译、归类或构建自动递增。本轮没有发布 Release 或导出新成品。