> 历史交接资料：当前仅维护 20 种语言，平台字段已重新归类。最新规则见 docs/localization.md 和工作区 CODEX_HANDOFF_CURRENT.md；不要按本文旧的 88/146 语言流程执行。

# ECP 多语言第四阶段 · DSH → Codex 交接书（已按 2026-10-22 语言缩减更新）

> **2026-10-22 变更**：用户决定只保留 Microsoft Store 支持的语言（88 种），并暂缓 Google 批量翻译。
> 本交接书保留供以后需要时使用；下文统计已更新为 88 种语言。若不再执行翻译，可忽略第 5～9 节。


生成：2026-10-21（Asia/Shanghai）
交接方：DeepSeek Harness（DSH）
执行方：Codex
工作区根目录：`D:\ECP_Workspace`

> 本文件是完整交接上下文。Codex 必须按本文件与第四阶段任务书执行，不要重新设计语言架构。
> DSH 已完成第 1～3 阶段并停止；DSH 不参与实际翻译。

---

## 0. 当前环境事实（DSH 已核查，2026-10-21）

| 项目 | 状态 |
| --- | --- |
| Codex CLI (`codex`) | **当前 PATH 中不存在**，DSH 无法直接启动 Codex 会话 |
| Google Cloud 凭据 | 环境变量 `GOOGLE_APPLICATION_CREDENTIALS`、`GOOGLE_CLOUD_PROJECT`、`GOOGLE_TRANSLATE_API_KEY`、`GOOGLE_API_KEY` 均**未设置** |
| Python Google 客户端库 | 未安装（`google-cloud-translate` / `google-auth` 缺失） |
| 网络/API 预算 | 未确认任何 Google 免费额度或付费预算 |
| 翻译执行 | **未执行**。DSH 没有调用任何翻译 API，也没有生成任何新译文 |

**结论：** 本环境当前不具备执行 Google 翻译的条件。Codex 接手后必须先按第 6 节检查凭据/预算；不满足则停止并报告，不得用其他服务代替、不得用大模型生成译文。


### 0.1 DSH 直接交接尝试记录

- 2026-10-21：DSH 曾尝试通过 `subagent_codex` 启动 Codex 执行本交接（subagent id `f124d05c-5c81-4a0a-881c-c49337e33677`，指向本文件），该进程在启动阶段失败退出（exit code `3221225786` / `0xC000013A`），未留下任何结果或文件；当前 PATH 也无 `codex` 可执行文件，Google 凭据同样缺失。
- 因此按任务书要求，DSH 不假装完成交接，改以本文件作为**可直接发送给 Codex 的完整交接指令**。
- 用户将本文件与第四阶段任务书交给 Codex 后，由 Codex 从第 6 节（凭据/预算检查）开始执行。
---

## 1. 仓库与工作区

| 平台 | 仓库 | 本地路径 | 当前分支 / HEAD（2026-10-21） |
| --- | --- | --- | --- |
| Windows（维护源） | `GlacierGlimmer/zmd-charge-plus` | `D:\ECP_Workspace\zmd-charge-plus` | `main` `2697547c108d0f43e4f15858f38720a87b8043e6` |
| Linux | `GlacierGlimmer/zmd-charge-plus-for-linux` | `D:\ECP_Workspace\zmd-charge-plus-for-linux` | `main` `09e60e18254d0e9a084006f167bbaca0f379f5f2` |
| macOS | `GlacierGlimmer/zmd-charge-plus-for-macos` | `D:\ECP_Workspace\zmd-charge-plus-for-macos` | `main` `276b991712fd35b93d0cd252b2bbc198e646fe86` |
| Android | `GlacierGlimmer/zmd-charge-plus-for-android` | `D:\ECP_Workspace\zmd-charge-plus-for-android` | `main` `443474ea29d689b1f09419652b79a6a25f92d5fe` |

- 工作区存在大量此前阶段的未提交改动（122/81/81/54 个状态条目）。**不要丢弃、不要 reset、不要回滚这些改动。**
- 语言资源维护源固定为 **Windows 主仓库**；其他平台通过同步脚本取得资源。
- 产品版本（禁止修改）：Windows/Linux `0.1.2`、macOS `0.1.4`、Android `v0.1.3 / code 4`。
- 禁止 push GitHub、禁止创建 Release/标签、禁止发布安装包。

---

## 2. 当前语言系统（不可重新设计）

### 2.1 资源格式

```text
Localization/
  core.lang
  core.lang.sha256
  field-history.json
  <locale>.lang
  <locale>.lang.sha256
```

`core.lang` 与每个 `<locale>.lang` 首行：

```text
#YYYYMMDD$累计字段行数
```

当前统一基线（未变）：

```text
#20261020$2470
```

- 日期是**语言资源维护日期**，与软件产品版本无关；不得在构建/打包/同步时按系统当天日期重写。
- 第 2 行起一行一个字段；`core.lang` 行是字段名，语言文件行是译文。
- 空行 = 缺失译文，必须保留原位置；不得过滤。
- UTF-8 无 BOM、LF、文件末尾单个 LF；字段内换行用 `\n` 转义、反斜杠用 `\\`。
- `.lang.sha256` 是 `.lang` 原始字节的 64 位小写 SHA-256。

### 2.2 字段规则

- `core.lang` 现有 **2470** 个字段；前 2466 个为历史字段，末尾 4 个为平台标题：
  - `windows.title`、`linux.title`、`macos.title`、`android.title`
- **本阶段禁止修改 core.lang 的字段名、顺序、日期、数量。** 只补 `<locale>.lang` 中已有字段的空白译文。
- 平台字段仍在同一语言文件内；禁止创建 `ja-JP.windows.lang` 之类的平台专属语言包。

### 2.3 运行时/UI 现状

- 桌面加载器：`LocalizationManager.Lang.cs`；Android 加载器：`core/src/main/kotlin/com/glacierglimmer/endfieldchargeplus/core/i18n/LanguageCatalog.kt`。
- 语言列表由实际内嵌 `.lang` 自动发现；缺失译文回退 `en-US`。
- 桌面 `ProductInfo.Name` 读取平台 title 字段；Android `ProductInfo.displayName()` 读取 `android.title`。
- 不要把产品专名（`Endfield Charge Plus` 等）翻译成别的文字。

---

## 3. 当前翻译覆盖（2026-10-22 统计，88 语言）

统计文件：

- `D:\ECP_Workspace\zmd-charge-plus\docs\localization-reports\completion-20261020.csv`
- `...\completion-20261020.md`
- `...\localization-audit.json`
- 机器可读交接统计：`D:\ECP_Workspace\.smoke\codex-handoff-stats.json`（含每语言缺失字段数与缺失源字符数）

总览：

- 语言：**88**（Microsoft Store 支持子集）
- 核心字段：**2470**
- 全部字段非空：**15** 种（英语族、简繁中文、ja/ko/fr/de-CH/mn/ta/uk 等）
- 主要 UI 字段已覆盖（≥2336 非空）：**10** 种 ui-complete + **5** 种 complete
- 仅少量译文：**73** 种 partial
- 总缺失字段：**51,054**
- 缺失源文本字符数（未去重）：**2,611,518**
- 已有 161 条文档示例/命名占位符人工复核预警；已有译文与英文相同可能是技术术语/产品专名，不能一概判为未翻译。

> 预算提示：2.61M 字符远超常见免费额度。Google NMT 价格与免费额度以官方定价页为准（https://cloud.google.com/products/translate/pricing）；未获用户明确批准不得产生付费。实际字符数须由 Codex 用去重后的真实任务清单重新计算并在执行前报告。

---

## 4. 现成工具（优先复用，不要重写本地化框架）

位置：`D:\ECP_Workspace\zmd-charge-plus\scripts\localization\`

### 4.1 `langtool.py`

```text
python scripts/localization/langtool.py core            # 重建 core.lang（本阶段禁用）
python scripts/localization/langtool.py migrate         # 旧 JSON 迁移；默认不覆盖手工 .lang
python scripts/localization/langtool.py hash            # 生成/刷新全部 .sha256
python scripts/localization/langtool.py verify          # 校验头部/字段数/空行/哈希
python scripts/localization/langtool.py check           # 结构检查
python scripts/localization/langtool.py audit           # 完整审计（含日期/历史/占位符/迁移一致性）
python scripts/localization/langtool.py report          # 输出完成度 CSV/MD
python scripts/localization/langtool.py stats           # 终端统计
python scripts/localization/langtool.py platform-upgrade --date YYYYMMDD  # 基线升级（本阶段禁用）
```

- 所有命令默认以所在仓库的 `Localization/`（Android 为 `core/src/main/resources/localization/`）为根。
- `migrate` 默认不覆盖现有 `.lang`；`hash` 与 `verify` 独立。
- `field-history.json` 记录字段基线，供前缀兼容判断；不要手动改。

### 4.2 `langimport.py`

```text
python scripts/localization/langimport.py check <code> <file>
python scripts/localization/langimport.py apply <code> <file> --yes [--allow-warnings]
python scripts/localization/langimport.py fill <code> <file> --yes [--allow-warnings]
python scripts/localization/langimport.py export <code> --out <file>
python scripts/localization/langimport.py export-missing <code> --out <file> --map <json> [--platform android]
python scripts/localization/langimport.py apply-missing <code> <file> --map <json> --yes [--allow-warnings]
```

- `export-missing` 输出：`#<core-date>$<缺失数>` + 对应英文字段值（已按 `.lang` 转义规则编码），并生成映射 JSON（`indices`/`fields`/`coreFieldCount`）。
- 推荐回填路径：**`export-missing` → 只翻译文件值 → `apply-missing`**。它按索引映射，拒绝覆盖已有译文、拒绝数量/占位符不符，自动备份并重算哈希。
- `fill` 用于整文件候选，只填空行，不覆盖已有译文。
- 写入都会生成 `docs/localization-reports/` 差异报告；`check` 不写文件。
- 发现错误时工具拒绝写入；警告需人工 `--allow-warnings`，写入必须 `--yes`。

### 4.3 `sync_platform_resources.py`

```text
python zmd-charge-plus/scripts/localization/sync_platform_resources.py D:\ECP_Workspace
```

- 以 Windows 为维护源，复制全部 `.lang`/`.sha256`/`core.lang`/`field-history.json` 到 Linux/macOS/Android。
- 只允许“旧值是新值前缀”的安全升级，其余冲突会停止；同步后逐文件字节校验并逐仓库 `verify`。
- 不要修改产品版本；不要自动运行旧 JSON 迁移。

---

## 5. 建议的翻译执行设计（Codex 负责实现，禁止大模型逐条翻译）

新增一个**最小化 Google 翻译执行器**（放 `scripts/localization/`，例如 `google_translate.py`），职责仅限：

1. 读取 `core.lang`、`en-US.lang` 与目标语言文件，找出空白字段。
2. 生成“任务清单”：目标语言、字段索引、源文本、字符数、去重键（同一语言内相同源文本只请求一次）。
3. 查询 Google 支持语言列表（`projects.locations.supportedLanguages` 或 v2 `languages` 端点），建立 `ECP code → Google code` 映射。
4. 占位符保护：发送前把下列片段替换为唯一哨兵并在返回后恢复/校验：
   - `{0}`、`{1}` 等数字槽；
   - `{variable|format}`、`{deepseek.*}` 等命名占位符；
   - `${env:...}`；
   - `custom.<source>.<variable>` 等路径；
   - 独立技术标识（`cpu.usage`、`gpu.usage`、`fps`、`api` 等）；
   - 真实换行、`\n`、`\\` 转义序列。
   不能保证安全的字段 → 加入人工复核队列，不导入。
5. 分批调用 Google Cloud Translation API（NMT，v3 优先；v2 仅作兼容），遵守配额/限流，失败退避重试，限流降速。
6. 缓存：按 `目标语言 + 源文本 + 模型/术语版本` 保存；断点续传进度文件；请求前查缓存避免重复付费。
7. 预算门禁：环境变量（建议）`ECP_GOOGLE_MAX_CHARS`、`ECP_GOOGLE_MAX_USD`（默认 0）、`ECP_GOOGLE_DRY_RUN=1`；超过预算/免费额度立即停止并报告。
8. 结果校验后写回：按语言生成完整候选或缺失字段文件，用 `apply-missing`（推荐）或 `fill` 回填；再 `langtool.py hash`、`langtool.py verify`。
9. 全部通过后再同步四平台：`sync_platform_resources.py`。
10. 输出报告：支持语言数、实际翻译语言数、每语言新增条数、剩余缺失、请求字符数、费用、缓存命中、人工复核字段、未支持语言、构建测试结果。

详细任务书见用户消息中的《ECP 第四阶段：DSH → Codex 完整交接与 Google 翻译自动化任务》，本文件不重复其全部条文；两者冲突时以任务书为准。

---

## 6. 凭据与预算检查（必须先做，缺失即停止）

可选配置方式（不要把密钥写入仓库/日志/报告）：

```powershell
# 方式 A：服务账号（推荐，v3）
$env:GOOGLE_APPLICATION_CREDENTIALS = "C:\path\to\service-account.json"
$env:GOOGLE_CLOUD_PROJECT          = "your-project-id"
# 需启用：Cloud Translation API (translate.googleapis.com)
# Python: pip install google-cloud-translate

# 方式 B：API Key（v2 REST）
$env:GOOGLE_TRANSLATE_API_KEY = "..."   # 仅当前进程/会话，不写入文件
```

检查清单：

- [ ] 凭据存在且可鉴权。
- [ ] 已启用 Cloud Translation API。
- [ ] 已查询支持语言列表并保存映射。
- [ ] 已确认本月剩余免费字符额度/已批准预算（无法确认 → 视为无免费额度）。
- [ ] 已计算去重后的预计字符数与最坏费用，并获用户明确批准（超过 $0 时）。
- [ ] 先跑 1 种语言的小批量测试并通过校验。
- [ ] 以上任一项不满足 → 停止，不得使用其他付费服务或大模型替代。

---

## 7. 语言映射注意事项

- ECP 88 种语言代码来自 Minecraft 语言目录，包含地区变体与特殊/虚构语言；Google 不可能全部支持。
- 必须先调用 Google「支持语言」接口，按返回代码建立映射；不得凭猜测假设地区变体都有独立模型。
- 地区变体只能用基础语言模型时，产物必须标记「待地区化审核」，不得声称准确完成。
- Google 不支持的语言：跳过、保留原文、不生成假译文；在报告中列出原因。
- 已知高风险代码（需 API 验证）：`en-x-*`、`tlh`、`qya`、`jbo`、`tok`、`got-DE`、`isv`、`lzh`、`nah`、`brb`、`fra-DE`、`go-FR`、`hn-NO`、`pls`、`qcb-ES`、`ovd`、`sxu`、`szl`、`vp-VL`、`vro`、`bar`、`ksh`、`nds-DE`、`li-LI`、`lmo`、`vec-IT`、`fur-IT` 等；以 Google 实际返回为准。

---

## 8. 翻译质量与验收

每个实际修改的语言文件必须满足：

- 头部仍为 `#20261020$2470`（除非任务书明确允许更新资源日期；本阶段不建议改）。
- 原有非空译文逐行未变（导入工具会保护；仍需 diff 核对）。
- 空行位置未移动。
- 占位符/技术标识完整恢复，无损坏 `{...}`、`${...}`、`\n`。
- 无异常重复、无超长合并文本、无乱码。
- `.lang.sha256` 匹配；`langtool.py verify` 通过。
- 四平台同步后资源一致。

必须区分：工具校验通过 ≠ 专业人工翻译审核通过；需人工复核的字段单独列出，不得静默接受。

---

## 9. 交付报告模板（Codex 填）

1. Google Translation API 是否接入；使用的接口/模型（v3 NMT / v2）。
2. Google 支持多少种 ECP 目标语言；映射表路径。
3. 实际完成补译语言数与每语言新增条数（文件：`docs/localization-reports/google-translation-<date>.md`）。
4. 剩余缺失条数与未执行原因。
5. 实际请求字符数、时间、费用（含免费额度使用）；预算门禁是否触发。
6. 缓存命中/避免重复翻译次数。
7. 现有译文是否全部保留；`core.lang` 是否保持 2470 字段和原顺序。
8. SHA-256 验证、四平台同步结果。
9. Google 不支持/跳过语言清单。
10. 人工复核字段清单。
11. 构建测试：Windows/Linux/macOS/Android 实际完成项与未完成项。
12. 异常、失败重试、限流与恢复说明。

---

## 10. 硬性禁止

- 不重新设计语言系统、不改 `core.lang` 字段/顺序/日期/数量。
- 不覆盖、不重新翻译已有有效译文；不把英文复制到空行冒充译文。
- 不使用 Codex/其他大模型生成译文；不调用 DeepSeek/Gemini/百度等替代 Google（除非用户单独批准）。
- 不把 API Key/服务账号写入仓库、日志、报告或提交。
- 不产生未经批准的付费；无法确认额度时默认 0 预算并停止。
- 不修改产品版本、不 push、不创建 Release、不发布安装包。
- 不因少数疑难字段阻塞可安全完成的翻译；但也不得静默接受损坏字段。

---

## 11. Codex 开工指令（可直接复制）

> 你在 `D:\ECP_Workspace` 工作。先完整阅读 `D:\ECP_Workspace\CODEX_HANDOFF.md` 与其中引用的任务书，检查 `zmd-charge-plus/scripts/localization/langtool.py`、`langimport.py`、`sync_platform_resources.py` 与 `Localization/field-history.json` 后开始：
> 1) 检查 Google 凭据与支持语言，建立映射；
> 2) 用去重后的缺失字段计算字符量与费用，未获批准预算不得付费；
> 3) 先做 1 种标准语言的小批量测试并通过 `apply-missing` + `hash` + `verify`；
> 4) 按剩余缺失字段规模分批，仅翻译 Google 支持语言；当前语言集合已缩减为 88 种 Store 支持语言；
> 5) 完成后 `verify`、同步四平台、执行可用构建/测试；
> 6) 按报告模板输出结果并停止。不要推 GitHub、不要发版本、不要修改 core.lang 与产品版本。

---

## 12. DSH 交接完成声明

- DSH 已整理并核实上述路径、工具、统计与环境状态；本文件是完整交接材料。
- 本环境没有 Codex CLI/Google 凭据，DSH 无法代替 Codex 执行，也未执行任何翻译。
- 用户将本文件与第四阶段任务书交给 Codex 后，由 Codex 按第 6 节先做凭据/预算检查再执行。
