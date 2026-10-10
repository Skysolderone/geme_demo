# 10-10-board-link-mode-c 段 C 实施报告（前半：3.0–3.3）

> **更新（C7 / C9 落实之后，见文末小节）：全量已全绿**（2298 条，通过 2287，跳过 11，失败 0）。以下为落实前的原始结论，保留备查。
>
> **先说结论：全量不是全绿。** `dotnet test tests/Siege.Core.Tests -c Release`：2299 条，通过 2277，跳过 11，**失败 11**。这 11 条全是既有的 Legacy 测试，都走 Sim 命令行、用旧地图标识或钉了旧报错文案 / 旧计数。它们是 C1 / C2 的预期后果。按派发要求，这些测试一条都没改，等负责人裁决 **C7**（见 §5）。除这 11 条外全部通过，新增的 50 条（含 Theory 展开）全绿。

对应 `openspec/changes/board-link-mode/tasks.md` 3.0–3.3，已勾上。3.0 注明"按 C1 范围"。3.4–3.6 未动。

## 1. 勾选与测试类

| task | 状态 | 测试类（`tests/Siege.Core.Tests/`） |
|---|---|---|
| 3.0 入口收口（C1 / C2 范围） | 完成。既有测试的改动待 C7 | `SimulationHarness/各入口按地图标识选图_链接模式Tests`：缺省地图、旧标识报错 ×6、旧标识写在配置文件里同样报错、未知标识报错、链接图建局为链接模式并挂配对来源、冒险概率选项已删除、链接图批量跑局（1 局，截断 14 个小回合）<br>`各入口支持生成图_链接模式Tests`：终端版开新局、终端版链接图能开局并按座位渲染<br>`MatchSetup/链接模式存档Tests.链接恢复须四人`（手改 3 人存档，段 B 质检遗留） |
| 3.1 配对来源、回放与日志字段 | 完成 | `各入口支持生成图_链接模式Tests`：裸link取种子并打印完整标识、每局换图的批次（缩小为 3 局）、批量配对可重复、命令行配对种子写入配置记录、回放重建链接、生成器变了<br>`可复现回放_链接模式Tests`：纯AI局可凭种子加配对复现、失败局可复现、replay命令拒绝旧日志、配对事件记录来源、Sim的配对来源给出合法结果 ×3、回放按记录配对而不是按种子推导<br>`随机子流隔离_链接模式Tests`：配对不扰动子流、配对来源不是对局子流<br>`MatchTelemetry/对局日志的记录内容_链接模式Tests`：配对与链接可查、战场结算可查、等待时长可查、日志往返且配对可从日志重建、旧模式日志不写链接模式字段 |
| 3.2 `map` 子命令 | 完成 | `各入口支持生成图_链接模式Tests`：map子命令打印链接初始图、map子命令模拟链接（`--links 1-4,2-5`，并验证 `1-4,4-2` 已合并战场的指代）、map子命令的链接参数校验 ×5、map子命令仍可打印旧标识 |
| 3.3 C4 全知开关与投影视图 | 完成 | `AiDecision/正式对战AI的信息边界_链接模式Tests`：候选不越战场（全知 / 关闭全知 ×2）、全知例外集中可关、关闭全知时评价代码照常运行、视野开关缺省不写出、全知例外只在一处分流 |

共用夹具：`SimulationHarness/LinkSimFixtures.cs`，内容如下：
- `link:1` 的跑局配置；
- 走 `Program.Execute` 的命令行调用；
- "四人四区"种子（在 1 起的种子里找，首轮恰两对、第 12 小回合两次链接），以及在该种子上截断到 13 个小回合的共享日志；
- 只会 Pass 的控制者。

## 2. 新增 / 修改文件

新增：
- `src/Siege.Sim/Running/PairingSources.cs`：
  - `PairingSourceNames`，来源名 `random` / `pairing-seed`；
  - `PairingDraw`，洗牌后两两相配，奇数时最后一名等待，决赛取唯一结果；
  - `SeededPairingSource`，配对种子来源：SplitMix64 + xoshiro，与对局种子一起派生，**不是 GameSeed 子流**；
  - `RandomPairingSource`，真随机来源（`RandomNumberGenerator`）；
  - `RecordedPairingSource`，回放来源：按日志原样给出，来源名也原样带回；请求对不上即 `SimAssertionException`；
  - `RecordedPairing`。
- `src/Siege.Core/Match/MatchFlow.SeatProjection.cs`：
  - `PublishSeatProjection(seat)`：按座位投影的 `MatchPublicView`。去掉本战场之外的棋子；他场信物呈未揭示、无人控制；他场玩家手牌类型为空。势力用 `PowerCalculator` 对投影盘面重算，活形用 `LifeShapeReport.Analyze` 重算。旧模式与插旗阶段原样返回。
  - `ProjectRehearsal(seat, result)`：把预演结果投影到同一口径。
- 测试 7 个文件（6 个新测试类 + `LinkSimFixtures.cs`）。

修改（Core）：
- `Ai/AiDifficulty.cs`：`AiSearchConfig` 末尾加 `bool SeatVision = false`，`WhenWritingDefault`。`false` 即全知（缺省、不写出），`true` 即关闭全知。
- `Ai/HeuristicAi.cs`：新增唯一分流点 `VisionFor(match, player, config)`，返回 `AiVisionPorts(Observe, Rehearsal)`。两个 `Create` 都只经它取读取口。
- `Ai/HeuristicTurnController.cs`：新增 `internal RehearsalView` 属性；`Deploy` 入口加了几行，在 `RehearsalView` 非空时给 `rehearse` 套一层投影（全知缺省下为 `null`，不套）。这是决策编排处的改动；评价代码 `BatchEvaluator` / `GroupSafety` / `EvaluationWeights` 零改动，权重未动。

修改（Sim）：
- `Program.cs`：
  - 新增 `DefaultMapId = "link:1"`；
  - 新增 `RequireLinkMap`，即入口门：`play` / `run` 只接受 `link:`，`run` 对 `--config` 里的 `MapId` 同样生效；
  - 裸 `link` 取种子；
  - `--pairing-seed`；
  - 删除 `--flag-risk`，同步改 `PrintUsage`；
  - `replay` 拒绝非 `link:` 日志；
  - `map` 新增 `--links`（`SimulateLinks`），旧标识显式经 `MapCatalog.Legacy`。
- `Config/RunConfig.cs`：
  - 新增 `PairingSeed`（`ulong?`，`WhenWritingNull`）；
  - `MapIdAt` / `Validated` 加 `link:` 每局换图分支，`gen:` 分支保留；
  - 链接图配置里出现 `FlagRisk` 即报错；
  - `ResolvedFor` 不给链接图落成冒险概率；
  - `PairingSeed` 只允许用于链接图。
- `Running/MatchSession.cs`：
  - 链接图以 `Mode = Link` 建局并挂配对来源（新建局用 `SeededPairingSource`，回放用传入的记录来源）；
  - 链接新增信物时 `before.Relics` 取不到，改为跳过；
  - 新增 match-telemetry 第 1、2、8–11 项的写出，只在 Link 局写出；
  - 首部信物写法抽成 `RelicEntryOf` 复用，Legacy 输出不变。
- `Running/Replayer.cs`：Link 日志挂 `RecordedPairingSource(original.RecordedPairings())`。
- `Logging/MatchLog.cs`：
  - 首部新增 `Mode`、`LockedFlagCounts`；
  - `PlayerEntry` 新增 `EliminationReason` / `PowerAtDefeat` / `WaitingRounds`；
  - `LogEvent` 新增 `Reason` / `Pairing` / `Link` / `Settlement`，以及 `PairingLogEntry` / `LinkLogEntry` / `SettlementLogEntry` / `SettlementRankEntry` 三种结构化载荷；
  - `StandingEntry.EliminationReason`；
  - 新增 `IsLinkMode`、`RecordedPairings()`；
  - 新字段一律属性级 `WhenWritingNull`。
- `Play/PlayCommand.cs`：
  - 链接图以 Link 模式建局并挂真随机来源，另留测试接缝 `pairingSource`；
  - 插旗只列 5 个占位（取自 `PublishFor(me).Placeholders`），盲选；锁定后只公开同棋盘的玩家；
  - 本机玩家读 `PublishSeatProjection(me)`；他场行动只报"在其他战场行动完毕"；
  - 播报配对、链接、战场结算；
  - 新增终局原因"最后一个战场结算"。
- `Play/BoardRenderer.cs`：
  - 链接模式只画本座位战场的外接矩形，矩形内非本战场格留空；插旗阶段若被调用只画占位编号；
  - 状态栏中他场玩家显示"其他战场（势力不可见）"。
  - 旧模式输出逐字不变，既有终端转录测试全绿。

修改（文档 / 测试）：
- `openspec/changes/board-link-mode/tasks.md`：3.0–3.3 勾上，3.0 注明 C1 范围与 C7。
- `tests/.../MatchSetup/链接模式存档Tests.cs`：只**追加**一个方法 `链接恢复须四人`。这是段 B 新建的文件，既有断言未动。

## 3. 测试结果

- `dotnet build siege.sln -c Release`：0 警告，0 错误（变异跑完、源码还原之后重建）。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2299 条，通过 2277，跳过 11，失败 11**。11 条失败清单见 §5 C7；其余全绿。
  - 第一轮全量其实红了 19 条。多出的 8 条是我引入的回归，已修实现，没有改测试：
    - 6 条 Legacy 快照哈希测试（带入带出配置 ×2、候选格上限、停手阈值阈值为0、对局内容集 v1、批量跑局缺省关闭带入）。原因是新日志字段只有全局 `WhenWritingNull`，而这些测试用别的序列化选项算哈希。修法：新字段一律补属性级 `[JsonIgnore(WhenWritingNull)]`，与既有新字段同一口径。
    - `难度分级Tests.高难度不越权`：开关原是枚举 `AiVision`，守门要求配置对象只含 int / bool，已改成 bool `SeatVision`。
    - `批量跑局Tests.Sim源码不含不受控随机与时间且浮点只在分析层`：`new RandomStream(` 命中了字面 token "new Random"，已改成目标类型 `new(...)`。
- 冒烟（手跑，没有跑批量）：
  - `play --map link:12345 --seed 5 --no-carry`：插旗列出 5 个占位，选 2 号后开局，只画该棋盘 5 列。
  - `run --count 1 --turn-limit 40 --difficulty Easy`（缺省 `link:1`）：退出码 0。日志里首部 `Mode=Link`，`LockedFlagCounts=[1,1,0,2,0]`；有一次首轮配对，来源 `pairing-seed`；一次链接，带新增信物与摘要；首部无 `FlagRisk`。
  - `replay --file` 回放该日志：162 行逐字节一致。
  - `map --map link:1 --links 1-4,2-5`：两次链接，校验通过。
- 耗时：全量由段 B 的约 1 分钟涨到约 2 分 30 秒到 2 分 50 秒。新增的 Sim 测试在 73×73 盘面上跑真实 AI；Easy 每小回合约 230 ms。

变异验证（`scratchpad/segc/mutate.py`）：
- 做法：备份（带时间戳）→ 按文件实际行尾改，并断言锚点恰命中 1 次 → 构建 → 只跑相关测试 → `finally` 写回原字节、`os.utime` → 逐字节校验。
- 跑完后重建，再确认相关测试与 6 条哈希测试 0 红，见下表末行。

### 3a 变异表

24 条全部按预期变红；每条都只跑了相关测试。

| 编号 | 变异 | 红 |
|---|---|---|
| MC-1 | `Program.Play` 缺省改回 `MapCatalog.DefaultId` | `缺省地图` 1 |
| MC-2 | `RequireLinkMap` 恒放行 | `旧标识报错` 6 |
| MC-3 | `MatchSession.Create` 恒 `Mode = Legacy` | `链接图建局为链接模式并挂配对来源` 1 |
| MC-4 | `Program.Run` 仍读 `--flag-risk` | `冒险概率选项已删除` 1 |
| MC-5 | `ResolvedFor` 对链接图也落成冒险概率 | `冒险概率选项已删除` 1 |
| MC-6 | `PlayCommand` 链接模式仍画全图占位（不列占位清单） | `终端版开新局` 1 |
| MC-7 | `RunConfig.MapIdAt` 去掉 `link:` 分支 | `每局换图的批次` 1 |
| MC-8 | `SeededPairingSource` 忽略配对种子 | `批量配对可重复` 1 |
| MC-9 | `Replayer` 对链接日志不挂记录来源 | `回放重建链接` 1 |
| MC-10 | `SimulateLinks` 只执行第一项 | `map子命令模拟链接` 1 |
| MC-11 | `Program.Replay` 不检查地图标识 | `replay命令拒绝旧日志` 1 |
| MC-12 | `RecordedPairingSource` 把来源改记 `external` | `配对事件记录来源` 1 |
| MC-13 | `SeededPairingSource` 改从 `GameSeed` 派生命名子流 | `配对来源不是对局子流` 1 |
| MC-14 | 链接事件 `MapDigest` 写空串 | `配对与链接可查` 1 |
| MC-15 | `CopyFlowEvents` 漏掉 `PairingApplied` | `配对与链接可查` 1 |
| MC-16 | 结算名次势力恒写 0 | `战场结算可查` 1 |
| MC-17 | `WaitingRounds` 恒写 0 | `等待时长可查` 1 |
| MC-18 | 首部 `Mode` 对旧模式也写出 | `旧模式日志不写链接模式字段` 1 |
| MC-19 | `PublicRules.LegalRange` 链接分支改为全图可落子格 | `候选不越战场` 2 |
| MC-20 | `VisionFor` 关闭分支仍给 `match.Publish` | `全知例外集中可关` 1 |
| MC-21 | 投影不重算势力（沿用全知势力快照） | `全知例外集中可关` 1 |
| MC-22 | `SeatVision` 去掉 `WhenWritingDefault` | `视野开关缺省不写出` 1 |
| MC-23 | 恢复路径人数校验传恒 4 | `链接恢复须四人` 1 |
| MC-24 | `VisionFor` 关闭分支不给预演口径 | `关闭全知时评价代码照常运行` 1 |

还原确认：
- 跑变异时，MC-13 与 MC-22 的锚点曾 0 命中（实现改过写法）。脚本在写文件之前就断言失败，源码没被改动，留下的两份 `.bak` 已删。修正锚点后两条重跑都红。
- 全部变异跑完后：`find src tests -name "*.bak"` 为空；`dotnet build siege.sln -c Release` 0 警告；再跑全量 2299 条，通过 2277，跳过 11，失败 11，正是 §5 C7 的 11 条，与变异前一致。

## 4. 后半 3.4–3.6 的准备

- **3.4 基线怎么钉**：
  - 配置：`link:1` + `ScriptedPairingSource()`（缺省升序两两配，决赛唯一结果）+ Standard + 4 人。
  - 建局方式：经 `MatchSession.ForMatch` 或给 `MatchSession.Create` 传 `pairing`，在 Sim 层挂脚本来源，不用 `SeededPairingSource`，与 Sim 的配对种子派生解耦。
  - 截断：到第 12 大回合（`TurnLimit` 按小回合计；四人四区的话前 3 个大回合 12 手，之后人数随结算变化，建议按大回合在测试里截断，而不是写死小回合数）。
  - 资源：决策序列 ≤ 200 KB。
  - 口径注意（见 C11）：`link:1` 初始图可落子格 > 150，会自动取候选格上限 K = 24；而且 K 按开局格数定，链接后不变。`MatchSession.Create` 与 `MatchSession.ForMatch` 走同一个构造函数（`CellLimit = Config.CandidateCellLimit ?? DefaultCellLimitFor(...)`），两种建局方式 K 都是 24，除非配置显式给 `CandidateCellLimit = 0`。
  - Legacy 两条基线（v5 / board:1）已经通过 Core 测试直接建局，C1 不影响它们，现状全绿。
- **3.5 `analyze` 要读的新字段**：
  - 首部：`Mode`、`LockedFlagCounts`、`Zones` → 同区率；
  - 事件 `PairingApplied` 的 `Pairing`：`Kind`、`Candidates`、`Pairs`、`Waiter`、`Source`，用于首轮配对分布。注意事件的 `MajorRound` 是推进后的 4，`Turn` 是第 3 大回合最后一手；
  - 事件 `BattlefieldsLinked` 的 `Link.Degraded` → 退化链接率；`Link.MapVersion` / `CorridorCells` / `PublicBoard` 也在；
  - 事件 `BattlefieldSettled` 的 `Settlement`：原因、名次、势力 → 各阶段大回合数（构筑 1–3 / 首轮交战 / 决赛）；
  - 快照 `PlayersState[].WaitingRounds` → 等待时长，取最后一个快照；
  - `PlayerEliminated` 事件的 `Reason` 与 `Values.Power`（战场落败势力）；
  - `Result.Reason`，其中 `LastBattlefieldSettled` / `LastPlayerStanding` / `turn_limit`；`Standings[].EliminationReason`。
  - 注意：首部 `PlayableCells` 只是开局 5 块棋盘的格数，链接后的分母要从快照 `Life.PlayableCells`（按当时盘面）取。旧日志（非 `link:`）按 match-telemetry 拒绝解析，本次未做（属 3.5）。
- **3.6 冒烟命令行**（C6）：`dotnet src/Siege.Sim/bin/Release/net8.0/Siege.Sim.dll run --out sim-out/board-link-mode/smoke --map link:1 --count 20 --seed 1 --difficulty Standard --turn-limit 600`。配对缺省由对局种子派生；不加 `--pairing-seed`；并行度用缺省值或 `--serial`，待定。

## 5. 偏离与需裁决事项

已按以下口径实现（供知悉）：
1. **AI 全知开关是 bool `SeatVision`，`true` = 关闭全知**。C4 字面是"开关缺省开启全知"，两者等价但方向相反。这是被迫的：守门 `难度分级Tests.高难度不越权` 要求 `AiSearchConfig` 只含 int / bool，且缺省值要能 `WhenWritingDefault` 不写出。
2. **关闭全知时，除了观察视图，预演结果也要投影**。只投影观察视图时，"预演前看不到、预演后看得到"的他人棋子会被 `BatchEvaluator` 当成敌方势力下降，AI 全员压到停手阈值以下而 Pass（实测）。所以 `VisionFor` 同时给出预演口径，由 `HeuristicTurnController.Deploy` 入口套用——`Deploy` 入口因此多了几行（决策编排，不是评价）；`BatchEvaluator` / `GroupSafety` / `EvaluationWeights` 零改动。
3. **终端人类玩家改读按座位投影**，并只画本战场。规格要求数据层隐藏，不能等到段 D。`Siege.Presentation` / Godot 未改。
4. **`map` 子命令缺省仍是 v5**（夹具用途，`MapCatalog.DefaultId`）。只有 `play` / `run` 缺省 `link:1`。
5. **`RunConfig.MapId` 的内部缺省仍是 v5**，供 Core 测试直接建会话、保住 Legacy 黄金值。`link:1` 只在 `Program.Run` / `Play` 落成；`run --config` 的文件没写 `MapId` 时同样落 `link:1`。
6. **`RunConfig.FlagRisk` 字段保留**（见 C8），只删了 CLI 选项与 `PrintUsage` 说明。
7. **`replay` 的地图不一致仍报在首部**：用的是既有 `Replayer` 的摘要比对。链接后每一步的地图一致性由链接事件的 `MapDigest` 逐行比对覆盖。
8. **`PairingSourceNames.Random` 用 `System.Security.Cryptography.RandomNumberGenerator`**，Sim 的不受控随机守门抓不到它（见 C9）。

需负责人裁决（编号从 C7 起）：

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| C7 | 11 条既有 Legacy 测试因 C1 / C2 红（明细见下表） | 未改，红着 | 按下表逐条改：删 1 条、改为不走 CLI 6 条、改报错文案 3 条、改计数 1 条 |
| C8 | `RunConfig.FlagRisk` 字段要不要删（C2 写"删除批次配置字段"） | 保留。CLI 选项已删；链接图配置里出现即报错；链接图首部不写 | 保留给 Legacy 夹具。理由：删字段会改 Legacy 首部黄金哈希（`各入口的地图专属AI权重Tests.未登记的地图首部逐字节不变` 四例，首部 `Config` 含 `FlagRisk`），也让 `冒险概率记录Tests` 的会话层断言编译失败，这与 C3"Legacy 日志逐字节不变"冲突。随 `retire-legacy-maps` 一起删 |
| C9 | `play` 的真随机来源用 `RandomNumberGenerator` | 守门 token 表没有它，现状能过 | 在 `批量跑局Tests.Sim源码不含不受控随机…` 里像 `TimeProvider` 那样把它登记为唯一例外（恰在 `PairingSources.cs` 一处）。需要改该守门测试 |
| C10 | `map` 子命令与 `RunConfig` 内部缺省仍是 v5 | 见偏离 4、5 | 接受到段 D / `retire-legacy-maps` |
| C11 | `link:1` 初始图可落子格 > 150，AI 自动取候选格上限 K = 24；K 按开局格数定，链接后不变 | 进入 3.4 基线与 3.6 冒烟的口径 | 接受（K 只影响剪枝，不影响合法性）；或对链接图另定 K |
| C12 | `ExpertLookahead` 在 `SeatVision = true` 下的模拟对手没有单独测试；他场玩家在投影里势力为 0、`HasEstablishedPower` 仍为真，前瞻按 `PublicRules.Eliminated` 会把他们当成已出局 | 不崩，评价代码未动；关闭全知不是缺省 | 记入后续"AI 视野收紧"change |

**C7 明细**（全部在 `tests/Siege.Core.Tests/`）：

| 测试 | 红在哪 | 建议改法 |
|---|---|---|
| `SimulationHarness/冒险概率记录Tests.命令行冒险概率写入配置记录` | `run --flag-risk 37` 退出码 1（未知选项） | **删除**该方法：功能已按 C2 删。另有 `各入口按地图标识选图_链接模式Tests.冒险概率选项已删除` 守住 |
| `SimulationHarness/各入口按地图标识选图Tests.两人图可选_选中确认后建局恰有2名玩家` | `run --map siege-2p-base-v1` 被入口门拒绝 | 改为不走 CLI：`MapCatalog.Legacy.Resolve` + `BatchRunner` / `MatchSession.Create` 建局，守"缺省人数取地图上限"的 `RunConfig` 口径 |
| 同上 `.三人图可选_选中确认后建局恰有3名玩家` | `run --map siege-3p-base-v1` 被拒 | 同上 |
| 同上 `.参赛人数缺省取地图人数上限_显式人数照旧` | `run --map siege-2p-base-v1` 被拒 | 同上（`Program.Run` 的"按地图人数上限重建玩家列表"对 `link:` 恒为 4，可另加一条 link 断言） |
| `SimulationHarness/各入口的地图专属AI权重Tests.重建玩家列表后按覆盖取值` | `run --config`（`MapId` 为 2 人图）被拒 | 改为不走 CLI，直接 `RunConfig.ResolvedFor` + `BatchRunner` |
| `SimulationHarness/各入口支持生成图Tests.以裸gen批量跑局_配置记录与日志首部都是带种子的完整标识` | `run --map gen` 被拒 | 改为不走 CLI（`MaterializeMapRequest` + `BatchRunner.ExecuteToDirectory`），或删除并由裸 link 的新测试替代 |
| `MapGeneration/棋盘档生成图标识Tests.裸board不入记录` | `run --map board` 被拒 | 同上 |
| `SimulationHarness/各入口按地图标识选图Tests.未知标识报错` | 期望报错列出 `siege-4p-base-v5` 等可用标识，现在报"只接受 link:<地图种子>" | 改断言文案为新报错 |
| 同上 `.终端版未登记的地图选项拼写被拒绝` | 反面断言期望 `play --map no-such-map` 报"找不到地图" | 改断言文案为"只接受 link:<地图种子>" |
| `SimulationHarness/各入口支持生成图Tests.裸gen不进规则内核_非法标识报错并说明格式与范围` | `play --map gen:abc` 期望 gen 格式说明 | 改为期望入口门文案；gen 格式说明改由 `MapCatalog.Legacy` 层的测试守 |
| `SimulationHarness/各入口支持生成图Tests.生成器只经目录调用_裸gen只在入口最外层取种子` | `Program.cs` 里 `mapSeedSource()` 计数期望 2，实为 3（裸 link 新增一次取种子） | 计数改为 3，并加一条 `LinkMapId.Format(mapSeedSource()` 恰命中 1 次 |

## 6. 遗留与风险

1. **全量未全绿**，11 条等 C7（见 §5）。
2. **全量耗时**由约 1 分钟涨到约 2.5–3 分钟：新 Sim 测试在 73×73 盘面上跑真实 AI。`MapCatalog.Resolve("link:1")` 有生成器缓存，但每局仍建大盘面。
3. **终端链接模式只做到"能玩、数据层不泄露"**：
   - 插旗是占位列表，没有盘面图示；
   - 他场行动只报"行动完毕"；
   - 链接后的外接矩形可能很大（决赛 73 列量级）。
   - 段 D 之前这是 Sim 终端唯一的链接模式入口。
4. **配对种子来源**由 `(对局种子, 配对种子?)` 用 SplitMix64 派生，常量域与地图随机源不同。当前没有"跨版本固定序列"的参考向量测试；改动派生方式会让批次配对整体换一套，回放不受影响，因为回放读记录。
5. **`play` 的真随机来源不可复现**（规格如此）。终端没有日志，配对只在屏幕上播报。
6. **`各入口的难度选项Tests` 等几条既有 CLI 测试没给 `--map`**，现在静默跑在 `link:1` 的链接模式上，断言仍过；它们守的已是链接图上的行为。
7. **match-telemetry「旧日志被拒绝」只在 `replay` 命令层做了**；`analyze` / `MatchLog.Parse` 仍可读旧日志，属 3.5。

## C7 / C9 落实（负责人 2026-10-10 裁决 C7–C12 全部按建议）

C8 / C10 / C11 / C12 不改代码。只做了下面两件事，以及为它们必需的一处源码重构。

### 改动清单（11 + 1）

每条改动的测试注释里都写了一行"board-link-mode 裁决 C7：……"。除这 11 条（和 C9 那 1 条守门）外，没有改任何既有测试。

| # | 测试 | 改法 |
|---|---|---|
| 1 | `SimulationHarness/冒险概率记录Tests.命令行冒险概率写入配置记录` | **删除**。原位置留一行裁决注释，说明由 `各入口按地图标识选图_链接模式Tests.冒险概率选项已删除` 守住 |
| 2 | `SimulationHarness/各入口按地图标识选图Tests.两人图可选_选中确认后建局恰有2名玩家` | 不走命令行，经 `MapCatalog.Legacy` 直接建局（新助手 `RunLegacy`，替换原来的 `RunOne`）；断言不变 |
| 3 | 同上 `.三人图可选_选中确认后建局恰有3名玩家` | 同上 |
| 4 | 同上 `.参赛人数缺省取地图人数上限_显式人数照旧` | 批量部分改走 `RunLegacy`，覆盖 8 种情形：不给人数 / 给难度 / 3 人图 / 4 人图 / 缺省地图 / 显式 3 人 / 配置文件不写 Players / 配置文件写 Players。终端与 Godot 两部分不变 |
| 5 | `SimulationHarness/各入口的地图专属AI权重Tests.重建玩家列表后按覆盖取值` | 不走命令行：读配置 → `Program.RebuildPlayers`（给了难度即丢弃文件里的权重）→ 经 `MapCatalog.Legacy` 落成 → 跑局；断言不变（批次配置与首部的权重 = 2 人图覆盖表）。类上"经 Program.Main 重定向 Console.Error"的注释随之删除 |
| 6 | `SimulationHarness/各入口支持生成图Tests.以裸gen批量跑局_配置记录与日志首部都是带种子的完整标识` | 不走命令行：裸 gen 经入口同一个落成函数 `Program.MaterializeMapRequest`，再经新助手 `RunLegacyToDirectory`（`MapCatalog.Legacy` 解析首局地图，按 `ExecuteToDirectory` 的口径写 `config.json` 与日志）。三个反例保留；"每局换图起止"的控制台文案改为断言 `MapIdAt(1)` |
| 7 | `MapGeneration/棋盘档生成图标识Tests.裸board不入记录` | 不走命令行：`MaterializeMapRequest("board")` → 经 `MapCatalog.Legacy` 建局 → 配置记录取落成后的 `Effective()`（即 `config.json` 的写法）；其余断言不变（含回放） |
| 8 | `SimulationHarness/各入口按地图标识选图Tests.未知标识报错` | 只改入口两处报错文案断言：`siege-4p-base-v5` → `只接受 link:<地图种子>`。可用旧标识清单仍由同一测试里的 `MapCatalog.Legacy` 断言守住 |
| 9 | 同上 `.终端版未登记的地图选项拼写被拒绝` | 只改反面断言文案：`找不到地图` → `只接受 link:<地图种子>` |
| 10 | `SimulationHarness/各入口支持生成图Tests.裸gen不进规则内核_非法标识报错并说明格式与范围` | 只改 `play --map gen:abc` 的报错文案断言；gen 格式说明仍由同一测试里的 `MapCatalog.Legacy` 断言守住 |
| 11 | `SimulationHarness/各入口支持生成图Tests.生成器只经目录调用_裸gen只在入口最外层取种子` | `mapSeedSource()` 计数 2 → 3，另加 `LinkMapId.Format(mapSeedSource()` 恰命中 1 次 |
| C9 | `SimulationHarness/批量跑局Tests.Sim源码不含不受控随机与时间且浮点只在分析层` | 把 `RandomNumberGenerator` 登记为唯一例外，写法同既有 `TimeProvider` 例外：<br>• 其他 Sim 文件一律不得出现；<br>• `Running/PairingSources.cs` 里恰一处，且位于 `RandomPairingSource` 之内；<br>• 附一行理由注释 |

为 C7 必需的源码重构：`src/Siege.Sim/Program.cs`。从 `Run` 抽出三个 internal 纯函数，`run` 行为不变：
- `RebuildPlayers(config, players?, difficulty?)`：给了人数或难度即重建玩家列表；
- `DefaultPlayerCount(config, firstMap)`：都没给人数时取地图人数上限；
- `DeclaresPlayers`：由 private 改为 internal。

理由：第 2–5 条要求"不走命令行而断言的业务含义不变"。这些口径原来只活在 `Program.Run` 里；测试若自己再写一份就成了第二实现，原有的变异 M-S5 / M-S6 也会失效。

### 测试结果

- `dotnet build siege.sln -c Release`：0 警告，0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2298 条，通过 2287，跳过 11，失败 0**。总数少 1 条，是删除的第 1 条。耗时约 4 分钟。

### 变异记录

做法同 §3a（备份 → 改 → 构建 → 只跑相关测试 → 还原 + 刷新时间戳 → 逐字节校验）。跑完重建并跑全量，结果即上面的全绿；`src` / `tests` 下没有残留 `.bak`。

| 编号 | 变异 | 红 |
|---|---|---|
| MC-25（C9 守门） | `Running/MatchSession.cs` 建局处加一处 `System.Security.Cryptography.RandomNumberGenerator.GetInt32(1)` | `Sim源码不含不受控随机与时间且浮点只在分析层` 1 |
| MC-26（C7 改写后的人数口径） | `Program.DefaultPlayerCount` 恒不改写（条件加 `\|\| maxPlayers > 0`），即原 M-S5 的同构 | `各入口按地图标识选图Tests` 3：两人图可选、三人图可选、参赛人数缺省取地图人数上限 |

### 遗留

- 第 6 条反例二（每局换图）中，第 1 局之后的地图仍由 `BatchRunner.Execute` 内部经 `MapCatalog.Resolve` 逐局解析，靠的是段 C 保留的旧标识转交。段 D 收口 `Resolve` 时，这条（以及 `Replayer` 回放旧日志）要改走 `MapCatalog.Legacy`，或给批量层一个解析器参数。第 7 条的回放断言同理。

---

# 段 C 实施报告（后半：3.4–3.6）

前半（含 C7 / C9）已提交为 d55127b。本节对应 `tasks.md` 3.4–3.6，三项都已勾上。

**结论：**
- `dotnet build siege.sln -c Release`：0 警告。
- 全量测试全绿：2305 条，通过 2294，跳过 11，失败 0。冒烟前后各跑一次全量，结果相同。
- 冒烟 20 局全部以"最后战场结算"终局，无截断、无失败。
- 冒烟实测到三件需要裁决的事（C14–C16）：对局很长；几乎所有战场落败都发生在"整轮 Pass 结算、落败者手里还有子"的情形下；同区率与等待时长都很高。

## 1. 勾选与测试类

| task | 状态 | 测试类（`tests/Siege.Core.Tests/`） |
|---|---|---|
| 3.4 决策序列基线（C5） | 完成 | `AiDecision/决策序列基线_链接模式Tests`：决策序列与基线相同、同种子重跑逐字节相同。基线文件 `AiDecision/Fixtures/decisions-link1-seed3.txt`，3956 字节。 |
| 3.5 遥测与分析 | 完成 | `MatchTelemetry/链接模式平衡分析Tests`：配对与链接可查、战场结算可查、等待时长可统计、真实跑局的链接日志可完整分析、旧日志被拒绝 |
| 3.6 20 局冒烟（C6） | 完成，只报告 | 见 §4 |

- 3.4 基线的口径：`link:1`、对局种子 3、`ScriptedPairingSource` 缺省脚本（升序两两配）、4 名标准难度 AI、跑局层缺省配置、截断到第 12 大回合。按 C11，K = 24，链接后不变，未显式改动。
- 种子 3 是 `link:1` 上第一个四人四区的种子：首轮两对，配对脚本的次序会改变后续全部走法，所以变异才能把基线打红。
- 基线里除每小回合的弃牌、征募、落点与类型、Pass、被拒补救外，还记了配对（P）、链接（L，含地图摘要与新增信物）和战场结算（S）行。
- 旧图的 v5 与 `board:1` 两条基线未受扰动，`决策序列基线Tests` 2 条全绿。

## 2. 新增 / 修改文件

新增：
- `src/Siege.Sim/Analysis/LinkModeAnalyzer.cs`：`LinkModeSection` 与它的唯一计算实现。只统计链接模式的局；样本里一局都没有时为 `null`，报告不出这一段。
- `tests/.../AiDecision/决策序列基线_链接模式Tests.cs`（含导出器 `LinkDecisionSequence`）与 `AiDecision/Fixtures/decisions-link1-seed3.txt`。
- `tests/.../MatchTelemetry/链接模式平衡分析Tests.cs`。

修改：
- **Core**：
  - `Match/Pairing.cs`：`LinkRecord` 新增 `Corridors`，存通道的外接矩形。只作记录，不入存档。
  - `Match/MatchFlow.Link.cs`：`ExecuteLink` 填写 `Corridors`。
  - 两处都只是记录，不碰规则判定。
- **Sim**：
  - `Logging/MatchLog.cs`：
    - `LinkLogEntry` 新增 `CorridorLengths`（各条通道的较长边）与 `BattlefieldCells`（链接后合并战场的可落子格数，不含改造）；
    - 新增 `MatchLog.AllRelics`：首部信物加上各次链接新增的信物。
  - `Running/MatchSession.cs`：写出上面两个日志字段。
  - `Analysis/BalanceAnalyzer.cs`：
    - `BalanceReport` 新增 init 属性 `LinkMode`；
    - 信物选择段改读 `log.AllRelics`。这是**冒烟实测出的缺陷**：链接新增的信物一旦被控制，既有统计就会 `KeyNotFound`。旧日志没有链接事件，`AllRelics` 与首部相同，黄金哈希测试全绿。
  - `Analysis/ReportWriter.cs`：新增"链接模式"一段，只在有链接模式局时出现。
  - `Program.cs`：`analyze` 遇到非 `link:` 日志即报错，提示"属于已移除的地图模式"，并且不写报告。口径与 `replay` 一致（C3），内部分析接口仍能读旧日志。既有 analyze 测试没有一条变红，所以没有停下。
- **测试**：`SimulationHarness/LinkSimFixtures.cs` 新增共用的小样本 `PlayScripted` 与控制器 `StoneThenPass`。首部配置会写明本局内容集；不写的话分析端会把 v2 局当 v1 读。
- **文档**：`openspec/changes/board-link-mode/tasks.md` 3.4–3.6 勾上。

## 3. 测试结果与变异

- 冒烟之前：全量 2305 条，通过 2294，跳过 11，失败 0。
- 冒烟之后：修了信物选择段的缺陷并补了回归断言，重建再跑全量，结果同上。全量耗时约 2.5–3.5 分钟。

变异（`scratchpad/segc/mutate.py`）：备份 → 修改 → 构建 → 只跑相关测试 → 还原并刷新时间戳 → 逐字节校验。全部跑完后重建并跑全量，结果全绿；`src` / `tests` 下没有残留 `.bak`。

| 编号 | 变异 | 红 |
|---|---|---|
| MC-27 | 配对脚本换序：`ScriptedPairingSource.Default` 首轮改为首尾交错 (0,2)(1,3) | `决策序列基线_链接模式Tests.决策序列与基线相同` 1 |
| MC-28 | 同区判定改为旗数 ≥ 3 | `配对与链接可查` 1 |
| MC-29 | 首轮交战大回合数不减构筑期 | `战场结算可查` 1 |
| MC-30 | 过早 Pass 不看手牌 | `战场结算可查` 1（由"清空手牌"那条反面断言抓到） |
| MC-31 | 等待时长改取第一个快照 | `等待时长可统计` 1 |
| MC-32 | `Program.Analyze` 不检查地图标识 | `旧日志被拒绝` 1 |
| MC-33 | 信物选择段改回只读首部信物 | `真实跑局的链接日志可完整分析` 1 |

## 4. 3.6 冒烟报告（只报告，未调任何参数，只跑这一批）

**命令**：`dotnet src/Siege.Sim/bin/Release/net8.0/Siege.Sim.dll run --out sim-out/board-link-mode/smoke --map link:1 --count 20 --seed 1 --difficulty Standard --turn-limit 600`
- 配对种子缺省由对局种子派生；并行度取缺省 28。
- 随后跑了 `analyze --dir sim-out/board-link-mode/smoke`，报告在 `sim-out/board-link-mode/smoke/report.txt`。`sim-out/` 已在 `.gitignore` 里，不入库。

**终局**：
- 20 局全部是 `LastBattlefieldSettled`；截断 0，失败 0。
- 胜者：P0 7 局，P2 6 局，P3 5 局，P1 2 局。

**对局长度**：

| 量 | 最小 | 中位 | 最大 | 均值 |
|---|---|---|---|---|
| 大回合数 | 51 | 85 | 138 | 89.1 |
| 小回合数 | 114 | 235 | 361 | 238.55 |

**各阶段平均大回合数**：

| 阶段 | 均值 | 口径 |
|---|---|---|
| 构筑 | 3 | 第 1–3 大回合 |
| 首轮交战 | 45.65 | 第 4 大回合起到决赛开始前；没有首轮配对的局也从第 4 大回合起算 |
| 决赛 | 40.45 | 20 局都到达了决赛 |

**开局与配对**：
- 同区率 18/20 = 90%。开局布局：2+1+1 14 局，2+2 3 局，1+1+1+1 2 局，3+1 1 局。
- 首轮配对分布：无 ×4，1-2 ×3，2-3 ×3，1-5 ×2，2-4 ×2，其余各 1。

**链接**：
- 链接 38 次，退化 0 次。
- 通道长度分布（R3 复核）：3–9 格共 43 条；21–32 格 24 条；48–54 格 9 条，最长 54。
- 决赛战场可落子格数（R4 复核）：304–800，中段多在 450–585。R4 的上限是 1600，没有越界。
- 终局全图可落子格均值 615.45。

**等待时长**：
- 全部 80 个玩家人次的均值为 9.3 个大回合。
- 等待过的 19 人次，均值 39.16，最大 71。

**出局**：
- 战场落败 58 人次，势力归零 2 人次。
- 整轮 Pass 结算中落败 58 人次，其中 **AI 过早 Pass 落败 57 人次**。
- 口径：在整轮 Pass 的战场结算中落败；结算那一大回合本人的小回合是 Pass；而且 Pass 后快照里本人的公开手牌类型仍非空。
- "手里仍有可落的空格"由结算原因间接保证（是整轮 Pass，不是填满）。没有逐格核对禁入格，所以严格说是"手里有子"的上界。

**耗时**：
- 单个小回合（含 AI 决策）中位 1266 ms，最大 38336 ms。
- 整局耗时合计 10717 s；批次墙钟 714 s（28 并行）。最长一局 712 s。

**观察**（只陈述数据，不调参）：
- 对局很长：中位 85 个大回合，首轮交战与决赛各约 40 多个大回合。
- 3.4 基线的走法显示，AI 在长通道上每回合只落一子往前推。长通道是 R3 的风险，冒烟里最长 54 格。
- 战场结算几乎全靠"整轮 Pass"（58/58），而且落败者几乎都是手里还有子时主动 Pass 的（57/58）。这就是 design Risks 里"AI 过早 Pass 落败"的实测。
- 盲选均匀分布下，同区率 90%，2+1+1 占七成；等待者等待时长均值约 39 个大回合。design Risks 里"等待者失衡"的量级由此可见。

## 5. 偏离与需裁决事项

已按以下口径实现（供知悉）：
1. **`analyze` 的旧日志拒绝只在命令层做**，与 `replay`（C3）同口径；`BalanceAnalyzer` 内部接口仍能读旧日志，既有分析测试不变。
2. **基线种子取 3（四人四区）**，没有取 1。C5 没有指定种子；取种子 1 时首轮只有一对，配对脚本换序无法让基线变红。
3. **基线按大回合截断**（`MajorRound ≤ 12`），没有按小回合截断。
4. **阶段口径**：首轮交战从第 4 大回合起算，到决赛链接事件记的大回合前一轮为止；没有首轮配对的局（2+2、3+1）同样从第 4 大回合起算。
5. **"过早 Pass"用公开手牌类型非空近似"仍有可下的子"**（见 §4）。
6. **Core `LinkRecord` 新增 `Corridors`**：纯记录，不入存档，不参与规则。

需负责人裁决（编号从 C13 起）：

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| C13 | `analyze` 只在命令层拒绝旧日志，内部接口仍可读 | 同 replay（C3） | 接受；随 `retire-legacy-maps` 一并收紧 |
| C14 | 对局过长：中位 85 个大回合（51–138），首轮交战约 46 轮、决赛约 40 轮；AI 在长通道上逐子推进，通道最长 54（R3） | 只报告 | 登记后续 change：评估通道长度上限（复核 R3）或链接几何；不在本 change 内调 |
| C15 | 战场落败 57/58 是"整轮 Pass 结算且手里有子"（AI 过早 Pass） | 只报告；停手阈值与评价权重未动 | 登记到"AI 视野收紧 / 停手阈值复核"的后续 change：AI 评价不知道 Pass 会触发战场结算（前半遗留 6-1） |
| C16 | 同区率 90%（2+1+1 占 70%），等待者等待均值 39、最大 71 个大回合 | 只报告 | 并入 5.4 登记的"等待者失衡评估"后续 change |
| C17 | `LinkRecord.Corridors` 与日志 `CorridorLengths` / `BattlefieldCells` 是新增的记录字段 | 已落地 | 知悉即可 |

## 6. 遗留与风险

1. **冒烟单局耗时偏高**：单个小回合最长 38 s，最长一局约 12 分钟。分析报告里的旧段（例如部署上限第 7 大回合以后的分布）在长局里样本会被拉偏，解读时需注意。
2. **`各阶段大回合数` 只给均值**，没有分布。若要看决赛轮数的分布，需要在 `LinkModeSection` 里补直方图。
3. **既有分析段对链接日志只跑通、未逐段复核口径**。例如"首次跨出生区冲突""冲突占用率"仍按首部 `PlayableCells`（开局 5 块棋盘的格数）作分母；链接模式下这些段的含义需要另行复核。冒烟报告里这些段照常输出，但未必有意义。
4. 前半遗留的 1–2、4–7 条仍然有效（前半第 1 条"全量未全绿"已由 C7 / C9 解决）；第 3 条终端只做到"能玩"的情况，在长对局下体验会更差。
