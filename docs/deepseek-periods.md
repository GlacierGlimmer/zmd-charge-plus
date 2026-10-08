# DeepSeek 官方峰谷与中国节假日（ECP-78）

按 2026-10-08 核对的 [DeepSeek 官方模型与价格通知](https://api-docs.deepseek.com/zh-cn/quick_start/pricing)执行：北京时间周一至周五的 09:00–12:00、14:00–18:00 为高峰，中国节假日全天和周末全天为低谷。窗口起点包含、终点不包含。周末调休补班虽然属于中国工作日，DeepSeek 仍按周末低谷计费。

日历依据国务院办公厅的全年放假调休安排，包含放假日期和补班日期：

- [2024 年通知](https://www.gov.cn/zhengce/content/202310/content_6911527.htm)
- [2025 年通知（国家林草局转载）](https://www.forestry.gov.cn/c/www/szxx/594663.jhtml)
- [2026 年通知（北京市政府转载）](https://www.beijing.gov.cn/fuwu/bmfw/sy/jrts/202511/t20251104_4258838.html)

高峰、下一次切换、剩余时间和进度使用同一日历。春节等长假会跳过整段休假；倒计时不会跳到假期内的周一。计算统一使用北京时间，显示本地时间时转换同一个切换时刻。该日历在离线状态下也可使用，不依赖第三方节假日接口。

此版本日历覆盖 2024–2026 年。不在日历中的年份不会被当成普通周历：无法确定的高峰状态或边界会显示“节假日日历待更新”，不会发布假零进度或假倒计时。已能根据官方时刻或周末规则确定为低谷的时间仍显示低谷。

旧配置字段 `DeepSeekPeakWindows` 为导入兼容保留，官方峰谷显示不再受自定义窗口影响。设置页只显示官方时间，避免旧配置误导实际计费判断。

回归检查包含同一组 24 个桌面/Android 固定时刻用例，以及全部 2026 年补班周末、长假、跨年、UTC/海外时区、未知日历。桌面检查入口：`dotnet run --project Tests/DeepSeekPeriodTests.csproj -c Release`；Android 检查入口：`./gradlew :app:testDebugUnitTest --tests '*DeepSeekPeriodPolicyTest'`。

维护：国务院公布新年度安排后，校对并更新各平台的 `china-holidays.json`，写入实际来源，再补充跨年与节假日用例。不要预测尚未收录的年度安排。DeepSeek 修改规则时也需重新核对官方通知。
