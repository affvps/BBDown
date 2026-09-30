# 重构计划（REFACTOR_PLAN）：H/I 组剩余项（第 19 轮）

> **来源**：`docs/REVIEW_PLAN.md` 状态总览（2026-09-30 回填）中 **H 组剩余 9 项 + I 组剩余 14 项 = 23 项**（J1/J2 为跟踪项，见 §5 定案）。
> **定位**：本文件是**执行计划**——批次划分、范围、不可动契约、验收口径；完成情况回填到 `REVIEW_PLAN.md` 的 H/I 行与 `REVIEW_FINDINGS.md` 的状态列。
> **立项**：2026-09-30（第 18 轮消纳后）。**已决策**：① 7 批 7 PR（原子性与可回滚性优先）；② `MuxAV` 改参数对象时**保留旧签名兼容重载**；③ 先落文档再开工。

---

## 0. 结论摘要

**7 批 / 8 个 PR / 约 6.5~9.5 人日**（批 1 按依赖拆为 1a/1b：I7 属行为邻近面，与纯改名的 1b 分开以便独立回滚）。

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

- **H2**：`MuxAV`（20 参）/ `MuxByMp4box`（15 参）→ `MuxRequest` record。**决策：保留旧签名兼容重载**（旧 20/15 参签名转发到新版本，避免一次改动 13 处调用点，也为 `BBDown.Core` 的潜在外部消费方留缓冲）
- **H3**：`RangeDownloadToTmpAsync`（10 参）→ `RangeDownloadRequest`（4 处调用，无兼容重载需求）
- **I13**：`Page` 5 个阶梯构造器 → 无参构造 + 初始化器（**先补 `EntityTests` 构造/字段断言**，当前仅 3 例）
- **I5**：`SetUpWork` 10 元组 → `DownloadContext` record（4 层透传收敛）

### 批 4 — Parser 巨方法拆解（`refactor/parser-extract-tracks`，R3）

- **I2**：`ExtractTracksAsync` **≈532 行** → `PickDataRoot` / `PickTrackBaseUrl` 纯函数 + `ApiMode` 枚举 + 按阶段分段（数据根定位 / 轨道解析 / 二次重取接管）；合并数据节点定位的 3 份漂移变体
- **前置（强制）**：先跑夹具回放基线并**存档结果**（18 个夹具 JSON + `FakeBilibiliApiServer`），拆解后逐字节比对轨道集合（id/qn/bandwidth/codecid/URL 选择）
- 可选 **I16**：`BBDownConfigParser` 7 处手工扫参收敛为 `SkipOptionValue`（同属"解析层"，可与本批合并）

### 批 5 — 下载管线拆解（`refactor/download-pipeline`，R3）

- **I1**：`DownloadPageAsync` 192 行 → 4 个 helper（弹幕块 ~55 行重复、CoverOnly 分支、已有产物跳过）
- **H4**：`BBDownDownloadUtil` 两处 170/200 行、6~7 层嵌套 → 预检决策方法 + `DownloadClipWithRetryAsync`
- **H5**：重复簇抽 6 个辅助（任务收尾四元组 ×4、`IsLoopback`、SSRF 字面 IP ×2、DNS + 逐地址校验 ×3、头块复用）

### 批 6 — serve 拆解（`refactor/serve-decomposition`，R3）

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
批 1a（I7 异常策略：64 处收口，真值表钉住）
  → 批 1b（命名/常量）
  → 批 4（存量最大：532 行，护栏最强）
  → 批 6（文件最大：1683 行，护栏 51 例）
  → 批 3 → 批 5 → 批 2 → 批 7
理由：纯命名收尾（批 2）放后，避免与批 3/5/6 触碰同一批文件产生冲突
```

| 批次 | 分支 | PR | 状态 |
|:---:|---|---|---|
| 1a | `refactor/exception-policies` | — | 🔄 实施中 |
| 1b | `refactor/consistency-cleanup` | — | ⏳ 待开工 |
| 4 | `refactor/parser-extract-tracks` | — | ⏳ 待开工 |
| 6 | `refactor/serve-decomposition` | — | ⏳ 待开工 |
| 3 | `refactor/parameter-objects` | — | ⏳ 待开工 |
| 5 | `refactor/download-pipeline` | — | ⏳ 待开工 |
| 2 | `refactor/naming-and-constants` | — | ⏳ 待开工 |
| 7 | `refactor/remaining-structure` | — | ⏳ 待开工 |

---

## 7. 与其它计划的关系

- **`REVIEW_PLAN.md`**：本计划消费其状态总览中 H/I 的剩余项；每批完成后回填该表（H 13→4/9、I 22→8/14，收口后应为 H 13/0、I 22/0）。
- **`OPTIMIZATION_PLAN.md`**：存在重叠项，评估时合并口径——`P0-1 ≈ I1/I2/I10`（已部分消纳）、`P1-1 ≈ H5`、`P1-2 ≈ I2`、`P1-3 ≈ J1`。本计划执行时若与 P 项重合，以本计划的批次与验收为准，并在 OPTIMIZATION_PLAN 对应条目补注。
- **`MAINTENANCE_PLAN.md`**：已结项（第 8 轮验收）；其产出的 `ParserFixtureTests` + `FakeBilibiliApiServer` 正是批 4 的安全网。
- **`REVIEW_FINDINGS.md`**：本计划执行中产生的新发现按 RF 编号登记；已定案的"不做项"（§5）登记为 ⭕ 维持现状。
