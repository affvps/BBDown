# 重构计划（REFACTOR_PLAN）：H/I 组剩余项（第 19 轮）

> **来源**：`docs/REVIEW_PLAN.md` 状态总览（2026-09-30 回填）中 **H 组剩余 9 项 + I 组剩余 14 项 = 23 项**（J1/J2 为跟踪项，见 §5 定案）。
> **定位**：本文件是**执行计划**——批次划分、范围、不可动契约、验收口径；完成情况回填到 `REVIEW_PLAN.md` 的 H/I 行与 `REVIEW_FINDINGS.md` 的状态列。
> **立项**：2026-09-30（第 18 轮消纳后）。**已决策**：① 7 批 7 PR（原子性与可回滚性优先）；② `MuxAV` 改参数对象时**保留旧签名兼容重载**；③ 先落文档再开工。

---

## 0. 结论摘要

**7 批 / 8 个 PR / 约 6.5~9.5 人日**（批 1 按依赖拆为 1a/1b：I7 属行为邻近面，与纯改名的 1b 分开以便独立回滚）。

**进度（2026-10-01）**：批 1a（I7，PR #60）、批 1b（I11/I14/I15/I3，PR #61）、批 4（I2，PR #63）、批 6（H1，PR #64）已完成并验收；批 3 按依赖拆为 **3a（H2/H3 参数对象）** 与 **3b（I5/I13 结构收敛）**，两者均已完成；剩余批次按 §6 顺序推进，**下一批为批 5**（下载管线拆解：I1/H4/H5）。批 3 收尾后剩余（参数对象与结构收敛：H2/H3/I13/I5）。

风险分级：**R1** 纯机械（编译器全程护航，无行为变化）· **R2** 结构改动（无逻辑变化）· **R3** 复杂逻辑拆解（需拆前/拆后对照验证）。

| 批次 | 包含项 | 主题 | 风险 | 主要安全网 | 估算 |
|:---:|---|---|:---:|---|---|
| 1a | **I7** | 异常过滤策略收口（94 处 when-过滤器 → 9 条具名策略 / 64 处） | **R1** | 真值表 + 全量单测 | 0.5 天 |
| 1b | I11、I14、I15、I3 | 命名/重复/常量一致化 | **R1** | 全量单测 + 编译期 | 0.5 天 |
| 2 | H8、H9、H10 | 命名 / 魔法数 / 持久化单入口 | **R1~R2** | 全量单测 | 0.5~1 天 |
| 3 | H2、H3、I13、I5 | 参数对象与结构收敛 | **R2** | MuxerArgs 9 · 下载 34 · *Entity 需先补* | 1~1.5 天 |
| 4 | **I2**（+I16 可选） | Parser 巨方法拆解 | **R3** | **夹具回放 18 文件 / 15 用例** | 1.5~2 天 |
| 5 | I1、H4、H5 | 下载管线拆解 + 深层嵌套 + 重复簇 | **R3** | DownloadPipelineTests 34 | 1~1.5 天 |
| 6 | H1（+H10） | `BBDownApiServer` 1683 行拆解 | **R3** | Serve 51 例 | 1.5~2 天 |
| 7 | H6、I6、I9、I12 | 剩余中项收尾 | **R2** | Live 24 / 登录 / OptionDefaults / UrlResolver | ~1 天 |

---

## 1. 实测校正（2026-09-30，逐项当场测量）

`REVIEW_PLAN.md` 的 H/I 条目描述写于 2026-08，**多处与实际规模脱节**，直接改变排期判断：

| 项 | 计划原描述 | **实测（2026-09-30）** | 影响 |
|---|---|---|---|
| I1 | `DownloadPageAsync` ~520 行 | **192 行**（`Download.cs` 404 行；`Application/` 已拆为 20 个文件 3083 行） | **降级**：P0-1 的部分拆分已消化大半，归入批 5 且不再是主线 |
| I2 | `ExtractTracksAsync` ~430 行 | **≈532 行**（`Parser.cs` 784 行，占 68%） | **升级**：单项最大存量，批 4 主线 |
| I7 | 异常过滤器 or 链 "6+ 处" | **30 处** | **升级**：批 1 主线，收益远高于预估 |
| I11 | Medium，"门面双命名体系" | `Config.cs` 仅 **84 行 / 12 个门面成员**，仓内引用点 <40，且含拼写错误 `qualitys` | **降级**：归入批 1（半天内可完成） |
| I13 | `Page` 阶梯构造器（8/9/10/12 参） | **5 个构造器**（含拷贝构造 `Page(int index, Page page)`）；`EntityTests` **仅 3 例** | 风险点：**安全网最弱**，批 3 需先补断言 |
| H2 | `MuxAV` 20 参 / `MuxByMp4box` 15 参 | ✅ 确认（`BBDownMuxer.cs:202` / `:74`），仓内 **13 处**调用点 | 因非对外发布包，低风险 |
| H3 | `RangeDownloadToTmpAsync` 10 参 | ✅ 确认（`BBDownDownloadUtil.cs:76`），**4 处**调用 | 同上 |
| H9 | 魔法数集中 | **部分已具名常量**：`MaxQueuedPerConcurrent = 8`（ApiServer:64）、`MaxConcurrentQueryHandlers = 8`（:74）、`MinCompleteFlvHeaderBytes = 13`（LiveStreamUtil:196）；**剩余未具名的内联值实测 6 处**：`FromSeconds(30)`（ApiServer:453）、`FromMinutes(2)`（ApiServer:1413 / LiveStreamUtil:603）、`1048576 / 4`（DownloadUtil:174）、`3000 * (1<<n)`（LiveStreamUtil:234）、`0.8`（LiveStreamUtil:561） | 范围缩小：只做剩余 6 处 |
| H10 | 同一历史文件两套异常语义 | 已有 `LoadCoreAsync`（`:126`）单入口雏形 | 范围缩小：抽 `ReadHistoryLocked()` 统一语义 |
| H1 | `BBDownApiServer` God 类 | **1683 行 / 52 方法**，`SetupServer()` 独占 :146–:397（**≈250 行**）；类**已是 `partial`** | 可按文件零风险切分 |
| I16 | MergeWithConfig 4 次手工扫参 | **7 处**扫参循环（`:31/:58/:89/:149/:163/:181/:191`）；别名表已由 `CliOptionIndex` 收口 | 范围缩小 |
| I14 | `AudioMaterial` 同名冲突 | ✅ 确认：`AppHelper.cs:497 internal` vs `Entity.cs:240 public` | 真实冲突 |
| I12 | `ResolveAsync` 200 行 13 分支 | `UrlResolver.cs:15` → `:225`，**≈210 行** | 与描述一致 |
| **I11 补充**（2026-10-01） | "门面双命名体系，12 个成员" | **13 个成员中 9 个全库零引用**（`COOKIE`/`TOKEN`/`DEBUG_LOG`/`HOST`/`EPHOST`/`TVHOST`/`AREA`/`SKIP_SSL_CHECK`/`qualitys`）；存活 4 个：`WBI`(4 引用)/`COOKIE_FLOW`(2)/`SET_CLOCK_OFFSET`(2)/`WBI_FLOW`(1) | **改口径**：无可统一的"第二套命名体系"，实际动作是**删死代码 + 存活项改名**（H7/I17 先例），见 §6.2 |
| **I15 补充**（2026-10-01） | `.Replace("[] ", "")` ×4 | **实测 3 处**（`Display.cs:48`/`:71`、`DownloadTrackPreparation.cs:109`） | 范围 -1；带宽估算公式实测 **6 处**，与描述一致 |
| **I3 补充**（2026-10-01） | appkey/盐"散落" | **实测：2 份 `GetSign` + 2 份 `GetTimeStamp` + 5 处字面量**（盐 ×3：`Parser.cs:775` 两把、`BBDownUtil.cs:160` 一把；appkey ×2：`Parser.cs:58`/`:138`）| 收敛为 `BiliApiKeys` 单实现；`ParserFixtureTests.cs:169` 的 `appkey=4409e2ce8ffd12b8` 断言即现成安全网 |
| **I2 补充**（2026-10-01） | `ExtractTracksAsync` ≈430~532 行 | ✅ **实测 532 行**（`Parser.cs:152-683`），占该文件 68% | 与描述一致；拆解后主方法 **39 行**，新增 15 个私有方法 + 3 个私有类型（见 §6 批 4） |
| **H3 补充**（2026-10-01） | "`RangeDownloadToTmpAsync`（10 参），**4 处调用**" | ✅ 10 参确认；调用点实测 **2 处**（`BBDownDownloadUtil.cs:481`/`:816`），其余命中均为注释 | 范围 -2；无兼容重载需求 |
| **H2 补充**（2026-10-01） | "`MuxAV` 20 参 / `MuxByMp4box` 15 参，仓内 13 处调用点" | ✅ `MuxAV` **20 参**确认；`MuxByMp4box` 实测 **16 参**；`MuxAV` 调用点 **13 处**中 12 处在测试（经兼容重载，零改动），1 处生产（`Download.cs`）已迁到参数对象 | 参数对象 + 兼容重载按计划落地，见 §6 批 3a |
| **I1 补充**（2026-10-01） | "\`DownloadPageAsync\` ~520 行；弹幕块 ~55 行重复 / CoverOnly 分支 / 已有产物跳过" | 实测 **182 行**（批 3b 后）；登记所述三块**已在早前批次拆入** \`DownloadPageExecutor\`（\`DownloadPageExecution.cs:84-111\`：弹幕 → CoverOnly → 跳过已有产物）与 \`DownloadPageAssets\`，主方法内已无这些块 | **本批只拆剩余两块**：执行上下文装配（34 行）→ \`BuildPageExecutionContext\`、解析失败诊断（24 行）→ \`ReportNoTrackFailure\`；主方法 **182 → 141 行** |
| **I5 补充**（2026-10-01） | "`SetUpWork` 10 元组" | 实测 **9 元组**；透传链 **4 层**：`SetUpWork` → `DownloadPagesAsync`（**11 参**）→ `DownloadPageAsync`（**15 参**）→ 各阶段 | 三层一并收敛：`DownloadContext`（9 字段）+`DownloadPagesAsync` 4 参+`DownloadPageAsync(PageDownloadRequest)` |
| **I13 补充**（2026-10-01） | "`Page` 5 个阶梯构造器（8/9/10/12 参）；`EntityTests` 仅 3 例" | ✅ 5 个构造器 = 4 个阶梯 + 1 个拷贝构造；调用点实测 **11 处**（8 生产 + 3 测试），其中 **2 处**用拷贝构造（保留）；`EntityTests` 3 → **6 例** | 删 4 阶梯 + 无参构造；11 处全改初始化器（`required` 由编译器强制） |
| **H1 补充**（2026-10-01） | "God 类 1683 行 / 52 方法" | 实测 **1683 行 / 72 个成员块**（字段+方法+类型），类确为 `partial`；文件尾部另堆着 **6 个顶层类型**（含 2 个 AOT 源生成上下文） | **改为文件级切分**（7 文件，成员逐字搬运）：不新建 `ServeSecurityMiddleware` 等 4 个独立类型——这些成员共享同一份实例状态（任务列表 / 锁 / 闸门），外置状态属行为风险改动，超出"零风险按成员切分"范围 |
| **ApiMode 偏差**（2026-10-01） | "`PickDataRoot`/`PickTrackBaseUrl` 纯函数 + `ApiMode` 枚举" | 三 bool（tv/intl/app）的组合语义**无法用单一枚举等价表达**：`tvApi && appApi` 同时为真时，两处 `!tvApi` 门控（杜比/Hi-Res 跳过）与"归一化优先级（Intl > App > Tv）"不等价；且 `Workflow.cs:159` 的 `apiType` 用的是**另一套**优先级（TV > APP > INTL > WEB） | **不引入 `ApiMode`**：改为私有 `PlayRequest` 收敛长参数，避免在可达组合上改变行为；两处优先级口径不一致记为本批 Info 观察 |

**测量方法**（可复现）：`wc -l` 逐文件；方法规模用相邻方法定义行号差；调用点用 `grep -rn` 排除 `obj/`；"是否落地"用重构产物符号存在性核验（见 `REVIEW_PLAN.md` 状态总览说明）。

---

## 2. 安全网现状（排序依据）

拆解类改动的风险由**现有测试能否发现回归**决定，故先摸清各域覆盖：

| 领域 | 主要测试 | 用例数 | 强度 |
|---|---|---:|---|
| Parser 解析 | `ParserFixtureTests`（**18 个夹具 JSON**）、`ParserTests`、`ParserPlayLimitTests` | 15 + 2 文件 | **强** |
| 下载管线 | `DownloadPipelineTests`、`DownloadPathLockTests`、`DownloadProgressAggregationTests`、`DownloadTaskSnapshotTests` | 34 + 3 文件 | **强** |
| serve API | `ServeApiHttpTests`、`ServeApiSecurityTests`、`ServeCommandTests` | 27 + 24 + 1 文件 | **强** |
| 直播录制 | `LiveStreamUtilTests` | 24 | **强** |
| muxer | `MuxerArgsTests` | 9 | 中 |
| 订阅 | `SubscriptionStoreTests`、`SubCheckDirNameTests`、`SubCheck*ScanTests`、`SubCheckPathSelectionTests` | 5 文件 | 中强 |
| CLI / 配置 | `Config*Tests`、`OptionDefaultsBindingTests`、`CliArgJoiner`、`CliEntryPoint`、`AotCliBinding`、`NumericOptionValidation` | 8 文件 | 中强 |
| 登录 | `BBDownLoginUtilMergeTests` | 1 文件 | 中 |
| **Entity** | `EntityTests` | **3** | **弱（I13 需先补）** |
| 全库基线 | PR gate 过滤器 | **772** | — |

> 基线数：2026-09-30 全库 **772/772 全绿**；每批 PR 描述须记录开批基线数。

---

## 3. 批次明细

### 批 1 — 一致化收口（R1）

#### 批 1a — I7 异常过滤策略（`refactor/exception-policies`）

> ✅ **已完成**（PR #60）；验收记录见 §6。

**开工前审计实测（2026-09-30）**：全库 `catch (Exception ex) when (…)` 共 **94 处 / 40 种类型集合**（计划原述"30 处"口径不准），分为：

| 层 | 站点数 | 处置 |
|---|---:|---|
| 集合完全一致且无附加条件的族 | **64**（9 族） | 抽为具名策略（本批） |
| 过滤器级带附加条件（如 `ex is TaskCanceledException && !ct.IsCancellationRequested`） | 2 | **保留原地**（抽无条件谓词会吞掉用户取消） |
| 集合唯一（28 种） | 28 | **保留原地**（2~4 类型条件本身即最优表达） |

九条具名策略（`BBDown.Core/Util/ExceptionPolicies.cs`）与站点数：`IsBestEffortFailure`(44)、`IsJsonOrIoFailure`(4)、`IsSubtitleFetchFailure`(3)、`IsTransportFailure`(3)、`IsMissingResponseNodeFailure`(2)、`IsTaskStoreFailure`(2)、`IsParseDowngradeFailure`(2)、`IsProbeRequestFailure`(2)、`IsSkippableItemFailure`(2)。

纪律：① 每族集合与迁移前**逐字一致**（零行为变更）；② 站点自有守卫不并入谓词；③ `ExceptionPolicyTests` 真值表逐类型钉住集合（子类型感知：`ArgumentException` 会命中 `ArgumentOutOfRangeException`），增删类型或写成近似集合即失败。

> **待决策（本批不做）**：长链"单条目可跳过"族沿 4 个集合漂移（`SubCommand` 10 型 / `WatchLater` 9 型 / 下载页 11 型 / `DownloadPageExecution` 10 型），代码注释自称"与下载页过滤器同步扩充"——**合并为一个集合会改变 4 个站点的捕获面**（行为变更），登记为后续决策项，本批保留现状。

#### 批 1b — 命名/重复/常量一致化（`refactor/consistency-cleanup`）

> ✅ **已完成**（2026-10-01）；落地内容与两处偏差（I11 改为"删死代码 + 存活项改名"、I15 的 `.Replace` 实测 3 处）见 §6.2。

| 项 | 实测现状 | 做法 | 注意 |
|---|---|---|---|
| **I11** | `Config.cs` 84 行；`SET_CLOCK_OFFSET`/`COOKIE`/`WBI`/`COOKIE_FLOW`… 12 个门面成员 + `qualitys` 拼写错误 | 门面统一 PascalCase | `AppSettings` 已是 PascalCase，不动；**CLI/JSON 契约字段绝对不动** |
| **I14** | `AppHelper.cs:497 internal AudioMaterial` 与 `Entity.cs:240 public AudioMaterial` 同名 | 前者改 `AppRoleAudioDto` | 仅 AppHelper 内引用 |
| **I15** | `pDur * bandwidth * 1024 / 8` ×6、`.Replace("[] ", "")` ×4 | 抽 `EstimatedBytes(bandwidth, seconds)` + 展示行组装 | 纯展示层 |
| **I3** | `BBDownUtil.GetSign` 与 `Parser.GetSign(x, bool)` 两套；`appkey`/盐散落 | 集中 `BiliApiKeys` 常量 + 单实现 | **签名算法本身不得改动**（风控面） |

**验收**：build 0 警告 0 错误 → 全量单测（基线 772）→ format → CI 9 项；重命名靠编译期暴露遗漏。

### 批 2 — 命名 / 魔法数 / 持久化（`refactor/naming-and-constants`，R1~R2）

- **H8**：`ReadLinesThrottled`（名实不符）、`_savePathLock`、`MyOptionBindingResult<T>`、`QualityName` 档位映射顺序、`nowId` 逐项改名或补注释说明
- **H9**：剩余 **6 处**内联魔法数具名（`FromSeconds(30)`、`FromMinutes(2)` ×2、`1048576/4`、`3000·2^n` 退避、`0.8` 完整性阈值）；已具名的同族常量（`MaxQueuedPerConcurrent` / `MaxConcurrentQueryHandlers` / `MinCompleteFlvHeaderBytes`）不动
- **H10**：`SubscriptionStore` 抽 `ReadHistoryLocked()` 单入口，统一"损坏 → 隔离 + 抛 `SubscriptionDataCorruptException`"语义

### 批 3 — 参数对象与结构收敛（`refactor/parameter-objects`，R2）

> **拆为 3a / 3b 两个 PR**（沿批 1a/1b 先例，各自可独立回滚）：3a = H2 + H3（参数对象，PR #65）；3b = I5 + I13（结构收敛，PR #66）。批 3 已全部完成（I13 前置 `EntityTests` 断言）。

#### 批 3a — 参数对象（已 ✅）

- **H2**：`MuxAV`（20 参）/ `MuxByMp4box`（15 参）→ `MuxRequest` record。**决策：保留旧签名兼容重载**（旧 20/15 参签名转发到新版本，避免一次改动 13 处调用点，也为 `BBDown.Core` 的潜在外部消费方留缓冲）
- **H3**：`RangeDownloadToTmpAsync`（10 参）→ `RangeDownloadRequest`（4 处调用，无兼容重载需求）
- **I13**：`Page` 5 个阶梯构造器 → 无参构造 + 初始化器（**先补 `EntityTests` 构造/字段断言**，当前仅 3 例）
- **I5**：`SetUpWork` 10 元组 → `DownloadContext` record（4 层透传收敛）

### 批 4 — Parser 巨方法拆解（`refactor/parser-extract-tracks`，R3）

> ✅ **已完成**（PR #63，2026-10-01）；夹具回放基线逐字节一致，偏差（不引入 `ApiMode`、I16 未并批）见 §6 批 4 记录。

- **I2**：`ExtractTracksAsync` **≈532 行** → `PickDataRoot` / `PickTrackBaseUrl` 纯函数 + `ApiMode` 枚举 + 按阶段分段（数据根定位 / 轨道解析 / 二次重取接管）；合并数据节点定位的 3 份漂移变体
- **前置（强制）**：先跑夹具回放基线并**存档结果**（18 个夹具 JSON + `FakeBilibiliApiServer`），拆解后逐字节比对轨道集合（id/qn/bandwidth/codecid/URL 选择）
- 可选 **I16**：`BBDownConfigParser` 7 处手工扫参收敛为 `SkipOptionValue`（同属"解析层"，可与本批合并）

### 批 5 — 下载管线拆解（`refactor/download-pipeline`，R3）

- **I1**：`DownloadPageAsync` 192 行 → 4 个 helper（弹幕块 ~55 行重复、CoverOnly 分支、已有产物跳过）
- **H4**：`BBDownDownloadUtil` 两处 170/200 行、6~7 层嵌套 → 预检决策方法 + `DownloadClipWithRetryAsync`
- **H5**：重复簇抽 6 个辅助（任务收尾四元组 ×4、`IsLoopback`、SSRF 字面 IP ×2、DNS + 逐地址校验 ×3、头块复用）

### 批 6 — serve 拆解（`refactor/serve-decomposition`，R3）

> ✅ **已完成**（PR #64，2026-10-01）：按文件级切分为 7 文件（成员逐字搬运）；未新建独立类型，理由见 §1 的 H1 补充行与 §6 批 6 记录。

- **H1**：`BBDownApiServer.cs` **1683 行 / 52 方法** → 按文件切分为 `ServeSecurityMiddleware`（鉴权 / 限速 / 固定时间比较）、`TaskRouteMapper`（端点路由）、`TaskFileStore`（任务持久化 / 溢出裁剪）、`CallbackGuard`（webhook）。**类已 `partial`，可零风险按成员切分**
- 纪律：`[JsonSerializable]` 源生成上下文随类型迁移；CI 的 Native AOT smoke 必过

### 批 7 — 剩余中项收尾（`refactor/remaining-structure`，R2）

- **H6**：`LiveStreamUtil` 异常消息文本契约 → `LiveRoomClosedException` 专用异常（`LiveStreamUtilTests` 24 例兜底）
- **I6**：`LoginWEB`（:72，133 行）/ `LoginTV`（:205，128 行）复制 → 2 helper + `QrPollCode` 常量组
- **I9**：`WatchLaterSettings`（9 个 `[CommandOption]`）与 `SubCheckSettings`（11 个）复制 → 公共基类；`SubCommand.cs:358` / `WatchLaterCommand.cs:164` 两份 `BuildOption` 收口
- **I12**：`UrlResolver.ResolveAsync` ≈210 行 13 分支 → `ResolveHttpUrl` / `ResolveBareId`（**若要动，先补 http 分支夹具**）

---

## 4. 执行纪律（每批硬约束）

1. **不可动的契约**：`MyOption` / `ServeRequestOptions` / 各 `*Settings` 的**属性名与类型**（Spectre 绑定 + JSON 契约 + CLI 参数名来源）、`BBDown.config` 格式、CLI 参数名与退出码语义
2. 一批一 PR：`refactor/` 前缀 + Conventional Commits，**不夹带功能改动**；PR 描述记录开批基线与验收结果
3. 每批：`dotnet build -c Release` 0 警告 0 错误 → 全量单测全绿 → `dotnet format --verify-no-changes` → CI 9 项全绿
4. **R3 批次先验基线**：拆解前后以同输入（夹具回放 / HTTP 冒烟）对照，输出必须一致；基线结果写入 PR 描述
5. 收敛点若可能改变行为（如 I7 异常分类）→ 必须补**变异验证**用例（沿用 RF-88 / RF-91 / RF-92 纪律）
6. AOT 面：不新增反射；`[JsonSerializable]` / `[DynamicDependency]` 随类型迁移；Native AOT smoke 必过
7. 每批收尾：更新 `REVIEW_PLAN.md` 的 H/I 行与 §6 进度表；产生新发现时登记 `REVIEW_FINDINGS.md`
8. **回滚单元 = 单个 PR**（纯重构，`git revert` 即恢复）

**统一验收命令**：

```bash
dotnet build BBDown.sln -c Release
dotnet test BBDown.sln -c Release --no-build --filter "Category!=Integration&Category!=NetworkIntegration&Category!=LocalIntegration"
dotnet format BBDown.sln --verify-no-changes
```

---

## 5. 明确不做 / 维持现状（定案，避免每轮重复评估）

| 项 | 处置 | 理由 |
|---|---|---|
| **J1**（`release.yml` ubuntu:18.04 EOL） | ⭕ 维持现状 + 设触发条件 | glibc 2.27 兼容是刻意选择；**触发条件**：apt 源失效或镜像下线时迁移基镜像 |
| **J2**（Actions 未 SHA 固定） | ⭕ 维持现状 | Dependabot 周更兜底；严格供应链硬化另开 `deps` 批 |
| **I10**（`BBDownUtil` god 工具类整体拆分） | ❌ 不做 | 签名 / 时间戳部分已被批 1 的 I3 收口；其余拆分会大面积改调用点，收益低于风险 |
| **H9 中已常量化的 8 处** | ❌ 不动 | 已完成，无重复劳动 |

---

## 6. 执行顺序与进度追踪

```
✅ 批 1a（I7 异常策略：64 处收口，真值表钉住）— PR #60
  → ✅ 批 1b（命名/常量/重复收敛）— PR #61
  → ✅ 批 4（存量最大：532 行，护栏最强）— PR #63（夹具回放逐字节一致）
  → ✅ 批 6（文件最大：1683 行，护栏 51 例）— PR #64（7 文件切分，无代码行丢失/重复）
  → ✅ 批 3a（H2/H3 参数对象）— PR #65
  → ✅ 批 3b（I5 DownloadContext / I13 Page 初始化器）— PR #66
  → ✅ 批 5a（I1 拆解：上下文装配 + 失败诊断）— PR #67
  → 批 5b（H4 深层嵌套：预检决策 + DownloadClipWithRetry）  ← 下一批
  → 批 5c（H5 重复簇 6 个辅助）→ 批 2 → 批 7
理由：纯命名收尾（批 2）放后，避免与批 3/5/6 触碰同一批文件产生冲突
```

| 批次 | 分支 | PR | 状态 |
|:---:|---|---|---|
| 1a | `refactor/exception-policies` | #60 | ✅ 已完成（2026-10-01 验收：9 条策略 / 生产 64 处站点 + 真值表 9 引用） |
| 1b | `refactor/consistency-cleanup` | #61 | ✅ 已完成（2026-10-01；基线 775 → 收批 784 全绿） |
| 4 | `refactor/parser-extract-tracks` | #63 | ✅ 已完成（2026-10-01；18 夹具回放逐字节一致） |
| 6 | `refactor/serve-decomposition` | #64 | ✅ 已完成（2026-10-01；成员逐字搬运，无代码行丢失/重复） |
| 3a | `refactor/parameter-objects` | #65 | ✅ 已完成（2026-10-01；`MuxRequest`/`RangeDownloadRequest` + 兼容重载等价性测试） |
| 3b | `refactor/context-and-page` | #66 | ✅ 已完成（2026-10-01；9 元组 → `DownloadContext`，`Page` 初始化器） |
| 5a | `refactor/download-pipeline` | #67 | ✅ 已完成（2026-10-01；主方法 182 → 141 行） |
| 5b | `refactor/download-pipeline`（续） | — | ⏳ 待开工（**下一批**：H4） |
| 5c | `refactor/download-pipeline`（续） | — | ⏳ 待开工（H5） |
| 2 | `refactor/naming-and-constants` | — | ⏳ 待开工 |
| 7 | `refactor/remaining-structure` | — | ⏳ 待开工 |

#### 已完成批次记录

**批 1a（I7）· PR #60**：`BBDown.Core/Util/ExceptionPolicies.cs` 9 条具名策略；生产代码 64 处站点全部改用具名谓词（Core 9 + App 55），`ExceptionPolicyTests` 以真值表逐类型钉住各策略集合（+9 引用）。未收口站点保留内联集合与站点自有守卫（`ct.IsCancellationRequested` 等）。⚠️ **1a 记录的"94 处 = 64 收口 + 2 带附加条件 + 28 唯一集合"与收批实测对不上**：按同一口径（`catch (Exception …) when (`，排除 bin/obj）实测生产 when-过滤器 **102 处 = 64 具名 + 38 内联**；1a 类文档内"66 处重复族"亦与 64/28/2 不自洽。不影响收口正确性（64 处逐字等价 + 真值表钉住），口径待重算统一——详见 `REVIEW_PLAN.md` 第 19 轮 Info 观察④。

**批 1b（I11/I14/I15/I3）· PR #61**：

| 项 | 落地内容 | 安全网 |
|---|---|---|
| I11 | 删 9 个零引用门面成员；存活 4 个改 PascalCase：`WBI`→`Wbi`、`WBI_FLOW`→`WbiFlow`、`COOKIE_FLOW`→`CookieFlow`、`SET_CLOCK_OFFSET`→`SetClockOffset`（调用点 4 文件 9 处同步）；类级文档写明"读写配置直接用 `Config.Current`，不再新增门面成员" | 编译期（重命名）+ 全量单测 |
| I14 | `AppHelper` 的 `internal AudioMaterial` → `AppRoleAudioDto`；JSON 字段名（`audio_id`/`title`/`person_name`/`audio`）与 `[JsonSerializable]` 同步 | 编译期；AOT 源生成上下文随类型迁移 |
| I15 | 新增 `Display.BuildTrackLine`（展示行组装，取代 3 处 `.Replace("[] ", "")`）与 `Display.EstimatedBytes`（6 处带宽估算公式）；去掉 `DownloadTrackAsync` 未用的 `bool video` 形参（`Func<>` 契约 + 5 调用点）；挂错方法的 XML 文档归位 | 新增 `TrackLineFormatTests` 9 例：与旧写法逐字符等价 + 算式/长整型钉住 |
| I3 | 新增 `BBDown.Core/Util/BiliApiKeys.cs`（TV/BiliPlus appkey ×2 + 盐 ×2 + 唯一 `GetSign`/`GetTimeStamp`）；删除 4 份重复实现；3 处 appkey 字面量改常量 | `ParserFixtureTests` 的 `appkey=4409e2ce8ffd12b8` 断言 + 全量单测 |

> 纪律遵守：**无用户可见行为变化**（组装/估算与旧写法逐字符、逐算式等价；签名算法与常量值一字未改），故不写 CHANGELOG、不动 wiki 与 README（`AGENTS.md` 只要求用户可见变更更新文档）。

**批 4（I2）· PR #63**：

| 项 | 落地内容 | 安全网 |
|---|---|---|
| 结构 | `ExtractTracksAsync` **532 行 → 39 行**；拆出 `ExtractIntlTracksAsync`（INTL 两轮合并）、`ExtractDashTracksAsync`、`TryReRequestForDashAsync`（免二压接管）、`ExtractDurlTracksAsync`（最高清晰度重发 + 分段映射）、`ExtractClipInfoPoints`，以及轨道映射纯函数 `MapDashVideoTracks`/`MapDashAudioTracks`/`MapDubbingTracks`/`MapRawAudioTrack`/`ExtractDrmInfo` | 18 夹具回放逐字节一致 |
| 收敛 | 数据根定位 **3 份漂移变体 → `PickDataRoot`**；轨道基址选择 **6 处重复 → `PickTrackBaseUrl`**；播放 JSON 摘要日志 → `LogPlayJsonSummary`；长参数列表 → 私有 `PlayRequest` | 同上 |
| 所有权 | 新增私有 `PlayResponse`（"响应文档 + 当前数据根"绑定）：两次重发的接管与释放收敛为 `TakeOver()`，消除拆解前 `respJson`/`root`/`newResp` 三变量的手工同步与释放分支 | 同上 + DRM/重发夹具 |
| 偏差 | ① **不引入 `ApiMode` 枚举**（三 bool 组合语义不可归一，见 §1）；② **I16 未并入**（跨子系统）；③ 顺带清理 FLV 分支冗余局部变量（`url` 恒为空串 → `baseUrl = ""`；`quality`/`videoCodecid`/`size`/`length` 改为声明即赋值） | — |
| 验证 | ① 拆解前转储 18 夹具 / 15 场景的回放结果（轨道全字段 + 分段 + 清晰度 + DRM + 请求序列，query 中 `wts`/`w_rid`/`sign`/`ts` 归一为 `<v>`），两次运行 **SHA-256 一致**（`75FE2843…`，确认转储可复现）；② 拆解后同一转储 **SHA-256 完全相同**（逐字节）；③ 9 条日志文案、`throw` 1 处、`catch` 8 处计数逐字不变；④ 单测 **784/784**（拆解期间含临时转储用例为 785，删除后 784）；⑤ build 0 警告 0 错误；⑥ format exit 0 | — |
| 规模 | `Parser.cs` 784 → 885 行（新增 XML 文档与所有权助手），主方法净减 **493 行** | — |

**批 6（H1）· PR #64**：

| 项 | 落地内容 | 安全网 |
|---|---|---|
| 文件切分 | `BBDownApiServer.cs` **1683 行 → 7 文件**：主文件 163（构造 / `SetupServer` 编排 / 监听校验 / `RunAsync`）、`Security` 456（中间件 + 鉴权/限速/回环与 Host/净化）、`Routes` 183（四组端点映射）、`Tasks` 321（注册表 + 闸门 + 入队与执行）、`TaskFileStore` 159（持久化）、`Callback` 283（webhook + SSRF）、`ServeApiModels` 235（DTO + 2 个 AOT 源生成上下文） | 编译期 + serve 51 例 + AOT smoke |
| 编排 | `SetupServer()`（原 245 行）拆为编排 + 5 个具名方法：`UseServeSecurityMiddleware` / `MapTaskQueryRoutes` / `MapAddTaskRoute` / `MapCancelRoute` / `MapFinishedRemovalRoutes`（端点 lambda 逐字保留） | 同上 |
| 成员搬运 | **逐字搬运**（不重排、不重命名、不重格式化）：有效行多重集比对，原 1060 行 vs 新 1201 行，**34 处差异全部是新脚手架**（每文件 using/namespace/partial 包装、5 个方法声明与其调用点）——**零代码行丢失/重复** | 多重集比对脚本 |
| DTO 与 AOT | 6 个顶层类型随文件迁移，`[JsonSerializable]` 源生成上下文与它们同文件（AOT 纪律"上下文随类型迁移"） | AOT smoke（CI） |
| 偏差 | 不新建 4 个独立类型（见 §1 H1 补充行）；端点处理器仍是内联 lambda——抽成可单测的具名处理器需外置状态，留作后续评估 | — |
| 验证 | ① build 0 警告 0 错误；② 单测 **784/784**；③ `dotnet format --verify-no-changes` exit 0；④ 7 个文件字节卫生（无 BOM / 纯 LF / 末尾换行）；⑤ CI 9 项全绿（含 Native AOT smoke 与 Docker smoke） | — |

**批 3a（H2/H3）· PR #65**：

| 项 | 落地内容 | 安全网 |
|---|---|---|
| H2 | `MuxRequest`（18 字段 + 派生 `Url`）落在 `BBDownMuxer` 内：`MuxAV(bool useMp4box, MuxRequest, CancellationToken)` 为唯一实现，`MuxByMp4box(MuxRequest, CancellationToken)`；**保留旧 20 参签名兼容重载**（逐字转发，13 处调用点中 12 处测试零改动），生产调用点 `Download.cs` 已迁到参数对象 | `MuxerArgsTests` 9 例 + **新增等价性测试**（同参数下两入口 argv 逐项相等 ×2 分支） |
| H2 细节 | 原实现的就地改写入参改为显式形态：`audioOnly/videoOnly` 归一化为局部变量并经 `request with { … }` 传给 mp4box 分支；mp4box 的 5 个转义值改为"转义后本地副本"（`EscapeString(request.X)`），语义与原先就地覆写等价 | 同上 |
| H3 | `RangeDownloadRequest`（9 字段，取消令牌仍独立）：原 10 参方法体改为 `request.X` 命名访问，2 处调用点改为命名构造 | 全量单测 + 下载管线 34 例 |
| 验证 | ① build 0 警告 0 错误；② 单测 **784 → 786**（+2 等价性 Theory 例）；③ format exit 0；④ **字面量多重集比对**：元数据键名（`title=`/`comment=`/`album=`…）在改写前后逐条一致（机械改写中曾误伤 3 处字符串键名与 5 行重复转义，均已修复并由该比对兜住） | — |

**批 3b（I5/I13）· PR #66**：

| 项 | 落地内容 | 安全网 |
|---|---|---|
| I5 | `SetUpWork` 9 元组 → `DownloadContext`（9 字段）；`DownloadPagesAsync` **11 参 → 4 参**（`myOption, vInfo, context, apiType` + 可选），两个调用方（`Program.DoWorkAsync`、serve 的 `ProcessDownloadTaskAsync`）不再逐值解构/透传；`DownloadPageAsync` **15 参 → `PageDownloadRequest` + token**，参数编排器调用点塌缩为一次转发 | 全量单测（含 DownloadPipelineTests 34 例）+ 编译期 |
| I13 | `Page` 删 4 个阶梯构造器（8/9/10/12 参），保留拷贝构造 + 新增无参构造；**11 处调用点**（8 个 fetcher + 3 处测试）改对象初始化器——`required` 字段由编译器强制，`aid/cid/epid` 的净化仍只在属性 setter 收口 | **前置**：`EntityTests` 先补 3 例断言（字段映射 / 净化 / 拷贝构造，3 → 6 例） |
| 机械改写兜底 | ✅ 纯字面量多重集比对（7 个 fetcher + `PathFormatTests` + `SubCheckPathSelectionTests`）**零差异**；生成器把 9 处行尾注释挂错字段（`epid = "", //epid` → 注释落到 `title`），已按"注释随原实参"修复，可读性保持不变 | — |
| 验证 | ① build 0 警告 0 错误；② 单测 **786 → 789**（+3 `EntityTests`）；③ `dotnet format --verify-no-changes` exit 0；④ `Page` 构造语义由 6 例断言钉住（重构前后同一批断言） | — |

**批 5a（I1）· PR #67**：

| 项 | 落地内容 | 安全网 |
|---|---|---|
| I1 | `DownloadPageAsync` **182 → 141 行**：拆出 `BuildPageExecutionContext`（34 行执行上下文装配，纯构造无副作用）与 `ReportNoTrackFailure`（24 行"无可用轨道"诊断，恒返回 false） | DownloadPipelineTests 34 例 + 全量单测 |
| 口径修正 | 登记所述的"弹幕块 ~55 行重复 / CoverOnly 分支 / 已有产物跳过"三块**已在早前批次拆入** `DownloadPageExecutor`（`DownloadPageExecution.cs:84-111`）与 `DownloadPageAssets`——本批只拆剩余两块（见 §1） | — |
| 验证 | ✅ build 0 警告 0 错误；单测 **789/789**（无新增用例：两块均为纯搬运，行为由既有 34 例管线测试兜底）；`dotnet format --verify-no-changes` exit 0 | — |




---

## 7. 与其它计划的关系

- **`REVIEW_PLAN.md`**：本计划消费其状态总览中 H/I 的剩余项；每批完成后回填该表（H 13→4/9、I 22→8/14，收口后应为 H 13/0、I 22/0）。
- **`OPTIMIZATION_PLAN.md`**：存在重叠项，评估时合并口径——`P0-1 ≈ I1/I2/I10`（已部分消纳）、`P1-1 ≈ H5`、`P1-2 ≈ I2`、`P1-3 ≈ J1`。本计划执行时若与 P 项重合，以本计划的批次与验收为准，并在 OPTIMIZATION_PLAN 对应条目补注。
- **`MAINTENANCE_PLAN.md`**：已结项（第 8 轮验收）；其产出的 `ParserFixtureTests` + `FakeBilibiliApiServer` 正是批 4 的安全网。
- **`REVIEW_FINDINGS.md`**：本计划执行中产生的新发现按 RF 编号登记；已定案的"不做项"（§5）登记为 ⭕ 维持现状。
