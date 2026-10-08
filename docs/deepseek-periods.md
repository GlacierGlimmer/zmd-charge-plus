# DeepSeek 官方峰谷与中国节假日（ECP-78）

按 2026-10-08 核对的 [DeepSeek 官方模型与价格通知](https://api-docs.deepseek.com/zh-cn/quick_start/pricing)执行：北京时间周一至周五的 09:00–12:00、14:00–18:00 为高峰，中国节假日全天和周末全天为低谷。窗口起点包含、终点不包含。周末调休补班虽然属于中国工作日，DeepSeek 仍按周末低谷计费。

日历依据国务院办公厅的全年放假调休安排，包含放假日期和补班日期：

- [2024 年通知](https://www.gov.cn/zhengce/content/202310/content_6911527.htm)
- [2025 年通知（国家林草局转载）](https://www.forestry.gov.cn/c/www/szxx/594663.jhtml)
- [2026 年通知（北京市政府转载）](https://www.beijing.gov.cn/fuwu/bmfw/sy/jrts/202511/t20251104_4258838.html)

高峰、下一次切换、剩余时间和进度使用同一日历。春节等长假会跳过整段休假；倒计时不会跳到假期内的周一。计算统一使用北京时间，显示本地时间时转换同一个切换时刻。内置和缓存的日历在离线状态下也可使用。后台日历更新与余额请求独立，不发送 DeepSeek API 密钥，也不阻塞每次采样。

此版本内置日历覆盖 2024–2030 年：2024–2026 年为已公布的完整放假调休表；2027–2030 年按现行《全国年节及纪念日放假办法》提前收录元旦、除夕至正月初三、清明、五一至五二、端午、中秋、国庆前三天，标记为 `statutory-only`。农历和清明日期已核对香港天文台的年度公历与农历对照表。后续年度的连休、补假及补班日期须以国务院公布的年度安排为准。已知法定节日和周末可确定为低谷，尚未公布的其他日期不推测为工作日。

使用峰谷显示时，后台启动并每 24 小时检查上年、当年、次年；北京时间跨年立即重新检查。数据取自 [holiday-cn](https://github.com/NateScarlet/holiday-cn) 对国务院公告的每日整理，经 jsDelivr 或 GitHub Raw 获取。只接受年份匹配、政府 HTTPS 来源链接、七种节日齐全、无重复冲突且不超过 64 KiB 的完整年度数据；空文件和未公布年份不会覆盖内置表。数据源仍属第三方整理，校验来源链接不等于逐页审核公告正文。通过校验后无需升级应用即可启用新年安排，并原子缓存到应用数据目录的 `holiday-calendar` 文件夹。网络故障和坏数据保留内置或上一份可用缓存。

[法定节日依据](https://app.www.gov.cn/govdata/gov/202411/12/521605/article.html)；日期对照：[2027](https://www.hko.gov.hk/tc/gts/time/calendar/pdf/files/2027.pdf)、[2028](https://www.hko.gov.hk/tc/gts/time/calendar/pdf/files/2028.pdf)、[2029](https://www.hko.gov.hk/tc/gts/time/calendar/pdf/files/2029.pdf)、[2030](https://www.hko.gov.hk/tc/gts/time/calendar/pdf/files/2030.pdf)。

不在日历中的年份不会被当成普通周历：无法确定的高峰状态或边界会显示“节假日日历待更新”，不会发布假零进度或假倒计时。已能根据官方时刻或周末规则确定为低谷的时间仍显示低谷。

旧配置字段 `DeepSeekPeakWindows` 为导入兼容保留，官方峰谷显示不再受自定义窗口影响。设置页只显示官方时间，避免旧配置误导实际计费判断。

回归检查包含同一组 24 个桌面/Android 固定时刻用例，以及全部 2026 年补班周末、长假、跨年、UTC/海外时区、未知日历。桌面检查入口：`dotnet run --project Tests/DeepSeekPeriodTests.csproj -c Release`；Android 检查入口：`./gradlew :app:testDebugUnitTest --tests '*DeepSeekPeriodPolicyTest'`。

维护：国务院公布新年度安排后，在线更新会获取完整日历；应用发布时仍应校对并更新各平台的 `china-holidays.json` 离线基线，写入实际来源，再补充跨年与节假日用例。不要预测尚未收录的年度安排。DeepSeek 修改规则时也需重新核对官方通知。

参考 `dsh-whale-widget` 当前源码（`MeteorNOX/DeepSeek-Balance-Whale-Widget/lib/index.js`）：同样使用北京时间、周末全天低谷与内置 2026 节假日，并要求每年补下一年的日期。ECP 增加了截至 2030 年的法定节日基线与运行时年度更新，未沿用其未知年份当普通周历的行为。
