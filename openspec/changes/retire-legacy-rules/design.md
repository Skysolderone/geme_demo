## Context

- 公开建局（`MatchFlow.Create`）与公开恢复（`MatchFlow.Restore`）自 `retire-legacy-maps` 起恒为链接模式；`MatchOptions.Mode` 的缺省值仍是 `Legacy`，只对测试专用的 `CreateUnvalidated` / `RestoreUnvalidated` 起作用。手写 9×9 夹具（`MatchFixtures`：四个 3×3 角落出生区、单连通、无棋盘清单、地图标识 `test-match-9x9`）经它们以 Legacy 规则建局。
- Legacy 与链接的规则差异全部挂在 `Mode` 上：合法范围（保护期出生区 / 第 4 大回合全图 vs 所在战场）、终局（整轮 Pass / 填满即终局 vs 战场结算）、名次（完赛 > 弃赛 > 出局 vs 完赛 > 出局 > 弃赛）、插旗（看得见旗数、锁定前可改、超时分到旗数最少的区 vs 盲选）、存档（直接恢复盘面与信物账本 vs 先重放链接）、按座位视图（全图 + 全员同战场 vs 本战场）、AI（名册与两道链接门只在链接下生效）、Sim 日志字段。
- 受影响测试：迁移清单（`.trellis/tasks/10-15-retire-legacy-rules/migration.md`）134 个文件；R5 护栏是 `AiDecision/Fixtures/` 下 15 份链接图决策序列基线（5 条缺省、5 份 `*-omniscient.txt`、5 份 `*-k0.txt`），它们只跑 `link:1`，与 Legacy 分支无交集。
- 约束：不跑批量；链接模式逐字节不变；`dotnet test` 全量与 Godot 四条 headless 自检为最终回归。

## Goals / Non-Goals

**Goals:**
- 删除 `MatchMode` 与全部按模式分流的规则分支、只被 Legacy 用到的成员，Core 只剩一套链接规则。
- 迁移测试时不丢覆盖：(a) 期望不变、(b) 断言含义不变、(c) 逐名可对；链接规则下仍成立而只在旧载体上被测过的行为，迁到链接载体上继续测。
- L17（Sim 读模式分支）、W13、L18 在本 change 内收口。

**Non-Goals:**
- 不改链接模式任何行为与任何格式（存档、日志、地图文件、批次配置、终端转录）；不重钉基线。
- 不改 AI、地形规则、内容集 / 带入带出 / 匠人权重 / 地图摘要的缺字段回填（它们与 `MatchMode` 无关）。
- 不改平衡报告口径（R9 缺省）。

## Decisions

### D1 删除顺序与每步不变量

| 步 | 内容 | 退出条件（全部满足才进入下一步；每步单独提交） |
|---|---|---|
| 1 | 迁 (a)(b)(d)(e)(m) 测试：建新载体、逐文件改写；Legacy 分支**仍在**，所以每条迁移后的测试必须在新载体上通过——这一步证明"原断言在新载体上成立" | `dotnet test` 全绿；**通过数不变**（迁移不增删方法，只换载体；合并类 (j) 在本步不删）；15 份基线逐字节通过；除清单 (c) / (j) 方法外，`MatchFixtures` 建局助手（`Create` / `Started` / `TwoPlayer`）、`AiFixtures.Round5` 旧实现与 `CreateUnvalidated` 的 Legacy 选项调用点归零（Grep 输出列出剩余调用点，逐个对上 (c) / (j)） |
| 2 | 删 (c) 与 (j) 测试，删 `MatchFixtures.cs` | 全绿；通过数减少量 = 清单 (c) 28 + (j) 5 = 33，**逐名对上**清单第 3 节；Theory 行数减少同样逐条对上 |
| 3 | 删 Legacy 分支与 `MatchMode`（D4–D8）、W13（D9） | `dotnet build` 零警告零错误；全绿；**通过数不变**；15 份基线逐字节通过；链接存档往返 / 链接日志往返 / 终端链接局转录相关测试逐字节通过 |
| 4 | L17（Sim，D5 / D10）、L18（D10） | 全绿；通过数减少 = L18 两条（逐名）；`link:1`–`link:20` 初始图导出文件与日志首部地图摘要与第 3 步结束时逐字节相同；15 份基线逐字节通过 |
| 5 | 文档与最终回归 | `dotnet test` 全量；Godot 四条自检退出 0（Godot 不在解决方案里，`Names.cs` 的编译错误只在这里暴露） |

不变量的通用形式：每步结束时"通过数变化 = 本步清单里列名的删除条数"，不允许出现清单外的增减；任何一份基线有一个字节不同即回退本步。

### D2 迁移载体（R6 缺省 A）

- **规则层 (a)**：9×9 地图数据（`Map` / `Relics` 与常量 `P0..P3` / `All` / `Seed`）迁入 `TestMaps`；方法改为 `GameBoard.LoadUnvalidated(9×9)` + 规则层入口：`BatchRehearsal.Rehearse`（完整八步，额度与库存给足）、`PowerCalculator.Compute`、`LifeShapeReport.Analyze`、`CoverageMap.Compute`、`RelicLedger.SnapshotFor`、`FinalStandings.Compute`、`InitiativeOrder`、`PublicRules`。坐标原样，期望原样。
- **链接夹具 (b)**：缺省 `LinkModeFixtures.StartedFlat([0,0,0,0])`——FlatMap（`link:90003`，合法链接档图、过校验、K = 0）上四人同锁棋盘 0：单一战场、锁定旗数 4 → 不进首轮配对、没有链接；第 1 大回合起合法范围即整块棋盘 0；全员 Pass 的那个大回合结束时战场结算即终局（`LastBattlefieldSettled`）。这是旧 9×9"第 4 大回合起全图"阶段最近的等价物。`AtRound` / `Stones` / `Place` / `PassRound` / `PlayRound` 扩展集中到 `LinkModeFixtures`；`AiFixtures.Round5`、`PresentationFixtures.StartedWithZonedRelics` 改建在这套载体上。
  - 需要跨棋盘 / 单人战场：`Started([0,1,2,3])` 等指定棋盘。
  - 需要新地表、深水、林地、高差、栅栏、特殊尺寸或行号：手写链接档图（`LinkMapFixtures.Build`，地图标识 `link:9xxxx`，经 `CreateUnvalidated`；校验器禁止出生棋盘内新地表，这类图只能不经校验）。
  - 6×6 棋盘放不下的局面：新增 8×8 棋盘 0 的平坦变体（仍是合法链接档图）。
- `CreateUnvalidated` / `RestoreUnvalidated` 保留为测试专用口，但只剩"跳过地图校验、手工信物"的含义，建局约束（链接图标识、恰 4 人）照常执行。

备选 B（R6）：把 9×9 原样嵌成链接档画布上的棋盘 0（原点、坐标不变）、其余 4 块小棋盘随便放，四人同锁棋盘 0。优点是约 100 个文件的坐标与大部分数值期望不用改；缺点是棋盘 9×9 超出链接档 6–8 的边长、出生区从四角变成中央核心区、图只能不经校验——测试跑在生产中不可能出现的地图形状上，正是本 change 要去掉的那类债务。不选。

### D3 规则层能测什么、不能测什么

规则层入口给不了：结算第 3–5 步的信物揭示 → 控制重算 → 势力重算的顺序、"曾建立正势力"标记置位、手牌扣减、`BoardHistory` 同形历史的积累、流程事件。断言依赖其中任何一项的方法不能归 (a)，归 (b)；清单里 (a) 只收了纯函数（名次、带出结算、阈值解析）与"有无棋盘清单逐步相同"一条（以 `BatchRehearsal` + 结算层在两张图上逐步比对）。实施中发现某条 (a) 构造不出局面，降为 (b) 并在清单回填，不改期望。

### D4 删除清单（Legacy 分支与专用成员）

| 位置 | 删除 / 改法 |
|---|---|
| `Match/MatchMode.cs` | 删 `enum MatchMode`；`LinkStage` 保留（文件可改名 `LinkStage.cs`） |
| `Match/FlagPlanting.cs` | `MatchOptions.Mode` 删；构造参数 `mode` 删；`IsBlind` 删（恒盲选）；`PublicView()` 的旗数删（`FlagPublicView.FlagCountByZone` 删，只剩 `IsLocked`）；`Plant` 的改选拒绝变无条件；`LockAll` 恒取 `flag-pick`，`LeastPopulatedZone` 删 |
| `Match/MatchFlow.cs` | `Create` 的 `with { Mode = Link }` 删；`RequireModeMatchesMap` 改为 `RequireLinkMatch`（恰 4 人、链接图标识）；弃赛 / 出局的 `occupancy` 恒算；`Publish` 去 `Mode`；`OnCheckEndConditions` 恒记本大回合 Pass 名单；`CheckEndConditions` 只剩"只剩一名"；`Finish` 恒走战场落败比较量 + 唯一名次实现；`EndMajorRound` 两处门去掉 |
| `Match/MatchFlow.Link.cs` | `Mode` 属性删；`LinkStage` 改非空；`RequireLinkMode` 与其调用删 |
| `Match/MatchFlow.Countdown.cs` | 两处 `Mode != Link` 门删 |
| `Match/MatchFlow.Persistence.cs` | 见 D5 |
| `Match/MatchFlow.Persistence.Link.cs` | `ExportLink` 的门删 |
| `Match/MatchFlow.Preview.cs` | 先手预测的门删 |
| `Match/MatchFlow.SeatProjection.cs` | `SeatField` 的模式条件删 |
| `Match/MatchPhases.cs` | `EndReason.AllPassed` / `BoardFull` 删（R9），注释改"两类终局条件" |
| `Match/MatchPublicView.cs` / `SeatPublicView.cs` | `Mode` 删；`PublishFor` 的 `link` 局部量与全图分支删 |
| `Match/PublicRules.cs` | `LegalRange` 去 `mode` 参数，恒为 `BattlefieldOf(board, birthZone).Cells` 扣禁入格；`majorRound` 参数随之不再被读，一并删（调用点由编译器找全）；`BuildProtectionRounds` 保留（构筑期、带出、倒计时仍用） |
| `Match/FinalStandings.cs` | `Compute`（Legacy 组序）删，`ComputeLink` 更名 `Compute` |
| `Relics/RelicLedger.cs` | `Restore(generation, state)` 删（唯一生产调用是 Legacy 恢复；测试里作对照的改用 `Copy()`）（R12） |
| `Ai/ExpertLookahead.cs` | `Project` / `NextOpponent` 的三处模式门删（名册恒限同战场）；两处 `LegalRange` 调用去模式参数 |
| `Ai/HeuristicTurnController.cs` | `TrailsInBattlefield` / `DecisiveTriggered` 的模式门删 |
| `Presentation/Visibility/DefaultBoardView.cs` | 旧模式原样返回分支删 |
| `Presentation/Visibility/Worlds.cs` | 四处 `Seat is { Mode: Link }` 改为 `Seat is { }`（`Seat` 为空仍表示无座位视图） |
| `Sim/Running/MatchSession.cs` | 第 92、706–708、782、941–946、1026 行的模式条件变无条件（L17） |
| `Sim/Play/BoardRenderer.cs` | `Viewport` 的旧模式全图分支、`SameBattlefield` 的旧模式恒真分支删 |
| `Sim/Play/PlayCommand.cs`、`godot/scripts/Names.cs` | 两类旧终局文案删 |
| `Sim/Running/Replayer.cs` | 配对来源的 `IsLinkMode` 三元式改为非链接日志直接拒绝（`replay` 命令本就只放行链接日志，实现裁决 C3） |

保留不动：`PassStreak`（全局连续 Pass 计数）——它不再决定终局，但链接存档、日志快照、终端状态栏与 `TurnEnded` 事件文本都写它，删了就改字节；`MatchDebugAccess.SetPassStreak` 随调用者归零时再删。

### D5 存档、日志与回放（R10 缺省 A）

- **写**：链接存档的 `"Mode": "Link"` 与日志首部的 `"Mode": "Link"` 照写（改为常量），`Link` 段、锁定旗数、棋盘清单、出局原因等原本"链接才写"的字段无条件写——对链接局输出逐字节不变。
- **读（存档）**：`MatchSaveData.Mode` 改为字符串或单值；公开 `Restore` 已经拒绝"非 `link:` 地图标识"与"缺模式字段"两类，保留；`RestoreUnvalidated` 同样要求链接模式字段（不再有"缺字段即旧模式"的读法）。恢复只剩一条路径：空盘 → 重放链接 → 写盘面 → 信物揭示 → 其余状态。
- **读（日志）**：`MatchLog.IsLinkMode` 保留为读端谓词（`sim-out/` 里仍有 Legacy 时代的日志，各分析器靠它跳过）；`Replayer` 对非链接日志报错。
- 备选 B：删掉两处 `Mode` 字段。会改变每一份链接存档与日志首部的字节，旧链接存档里的 `Mode` 会落进未知字段而被静默接受，不选。

### D6 终局与名次

- `CheckEndConditions` 只判"只剩一名参赛玩家"；"最后战场结算"仍在大回合结束时判（不变）。
- 名次只剩一套：完赛者比较链 → 出局者按出局先后倒序（同序号内战场落败者比结算时比较链）→ 弃赛者按弃赛时势力。
- `EndReason` 删 `AllPassed` / `BoardFull`（R9 缺省）：存档里的终局原因只可能来自链接局，不受影响；Sim 分析按字符串读日志原因，`BalanceAnalyzer.RuleReasons` 与 `ReportWriter` 的两类文案改用字符串常量（与已有的 `LogResult.LegacyMajorRoundLimit` 同一做法），**报告字节不变**。

### D7 插旗

恒盲选：选中即锁定、公开视图无旗数、超时未选者由 `flag-pick` 子流均匀指定。`SeatFlagView`（只见自己、锁定后见同棋盘者）不变。

### D8 合法落子范围

任何大回合都是"所在战场的可落子格扣除禁入格"（`turn-sequence`「合法落子范围的对外契约」现行条文）。专家前瞻中模拟对手与两层加分的合法范围走同一实现，规格里残留的"保护期内为出生区、此后为全图"条文按 specs 增量改写；对链接局，这些条文与实现本来就不一致（实现早已按战场），改写是对齐不是改行为。

### D9 W13（R7 缺省窄解）

删的是 Legacy 那一半："Legacy 存档没有 `Link` 段 → K 读成 0、`FinalCountdownBackfilled` 只在链接模式置位"的模式条件。链接存档缺 `FinalCountdownRounds` 时按 0 回填并留痕的规则保留：它守的是 `final-countdown` 之前保存的**链接**存档（`.trellis/spec/core/boundaries.md`"缺省值与缺项回填值必须分开"），删它会让那些存档恢复后静默换成 K = 2 或被拒，属链接行为变化，与 R5 冲突。宽解（缺 K 即拒绝恢复）见 Open Questions R7。

### D10 L18 与 L17

- **L18**：删 `MapValidator.ValidateLandmarks`（`CENTRAL_ENTRANCE_NOT_PLAYABLE`、`CHOKE_NOT_PLAYABLE`）与 `ValidateTolerance`（`TOLERANCE_NEGATIVE`、`TOLERANCE_RELAX_WITHOUT_REASON`）；`MapData.CentralEntrance` / `ChokePoints` / `DistanceTolerance` / `ToleranceRelaxReason`、`MapFile` 读写、`LinkMapGenerator` 的赋值与拼接平移、`MapValidator.DistanceTable` 全部不动。链接图上这两条规则本来就不会触发（生成器把中央入口放在出生棋盘核心区中心、容差恒为缺省 1），删除前后 `link:1`–`link:20` 的校验结果都是"通过"，导出文件逐字节相同。
- **L17**：见 D4 Sim 三行与 D5。

### D11 测试记账

- 清单分类以方法为单位；同一文件可以多类。(j) 合并只允许"迁过去后与既有链接测试逐条同义"（R11），目标条在清单写明。
- (b) 类改写后的字面期望（坐标、终端文本快照、AI 数值、名次数组）若与原值不同，按 R8 记入清单第 6 节"期望重钉表"（旧值 → 新值 → 原因 → 关系断言是否不变），**关系断言**（谁优于谁、选中哪一手、拒绝哪一类、名次先后）不得改变；(a) 类一律不许改期望。
- 质检逐名比对清单第 3 节与第 4 节，补测建议 Q1 / Q2 在质检中落实。

## 与其他 change 的接口契约

| 接口 | 输入 | 输出 | 触发时机 | 本 change 的变化 |
|---|---|---|---|---|
| `MatchFlow.Create` / `CreateUnvalidated` | 地图、种子、玩家、选项（不再有模式） | 插旗阶段的链接局 | 建局 | 选项去 `Mode`；约束不变（链接图标识、4 人） |
| `MatchFlow.Restore` / `RestoreUnvalidated` | 地图、存档文本 | 链接局 | 恢复 | 只剩链接路径；缺 K 回填按 R7 |
| `PublicRules.LegalRange` | 盘面、出生区、玩家、活形分析 | 合法范围 | 每次部署 / 前瞻 | 去 `mode`、`majorRound` 参数；值对链接局不变 |
| `MatchPublicView` / `SeatPublicView` | — | 公开快照 | 每次发布 | 去 `Mode`；其余字段与序列化不变 |
| 存档 / 日志首部 | — | JSON | 存档 / 写日志 | 链接局字节不变（R10） |
| `FinalStandings.Compute` | 名次输入 | 名次 | 终局、带出结算 | 只剩链接组序 |
| 平衡分析 | 日志 | 报告 | `analyze` | 字节不变（R9） |

## Risks / Trade-offs

- [某些 (a) 候选的局面要靠结算顺序才能构造（先揭示信物、再重算控制、再置"曾建立正势力"）] → 按 D3 降为 (b)，在清单回填；不在规则层手搓结算顺序。
- [AI 测试换到同区四人后，链接规则的两道门（落后者阈值取 0、决胜一手）会改变被测 AI 的决策] → 局面构造让被测 AI 居本战场势力首位，或让它独处；做不到的单列上报（清单 P2），不改期望。
- [6×6 棋盘比 9×9 小，局部形状 + 周边空区放不下；眼位 / 空区 / 领地等全局量随棋盘变] → 先尝试 8×8 平坦变体；数值类期望按 R8 记表，关系断言不变。
- [手写链接档图不经校验，可能造出生产中不可能的局面] → 只用于新地表与特殊地形（校验器本就禁止出生棋盘新地表），每张图在夹具注释写明偏离校验的哪一条。
- [终端文本快照随链接视口（只画本战场外接矩形）整段变化] → 快照按新载体重导，逐份记表；`终端对局Tests` 的链接局转录已有逐字节测试，不受影响。
- [Godot 不在解决方案里] → 第 5 步四条自检必跑；`Names.cs` 的两条 switch 分支在第 3 步同提交删除。
- [删 `RelicLedger.Restore` 后两处测试失去"按生成记录恢复"的对照] → 改用 `Copy()` 与正式账本逐项比；`Copy()` 本身已有拼接后的测试。
- [清单分类基于脚本信号，个别方法归类有误] → 第 1 步开始时按方法名复核并回填；归类变化只能在 (a)/(b)/(c)/(j) 之间按 R1 定义调整，调整进 (c) 的须写明理由并在质检时逐条确认。
- [通过数核对被 Theory 行数干扰] → 清单同时记方法数与 `InlineData` 行数，第 2、4 步按两者分别核对。

## Migration Plan

1. 负责人过目迁移清单与 R6–R12（R4）。
2. 按 D1 五步实施，每步单独提交、可单独回退；第 1 步可再按目录分段（AiDecision / 流程 / 表现 / Sim）。
3. 回退：任一步不满足退出条件即回退该步提交；第 3 步（删分支）前所有测试已在链接载体上跑通，回退第 3 步不影响已迁测试。
4. 存档兼容：只影响 Legacy 存档（地图标识非 `link:`，早已被公开恢复拒绝）；链接存档读写不变。

## Open Questions

> 2026-10-15 负责人裁决：R6–R12 全按缺省建议（R7 = 窄解）。

| # | 事项 | 缺省建议 | 备选 |
|---|---|---|---|
| R6 | (b) 类的缺省载体 | **A**：合法链接档图——FlatMap 四人同区为主，指定棋盘 / 手写链接档图 / 8×8 变体为辅（D2） | B：9×9 原样嵌为棋盘 0（坐标不变、不经校验、超出边长上限） |
| R7 | W13 的范围 | **窄解**：只删 Legacy 存档不写 K 的模式分支，链接存档缺 K 按 0 回填留痕保留，`match-setup`「决赛倒计时配置」不变（D9）——这偏离 R2"删旧存档缺 K 的回填"的字面，请确认 | 宽解：缺 K 的存档一律拒绝恢复并点名，MODIFIED「决赛倒计时配置」删 Scenario「旧存档按不启用读取」 |
| R8 | (b) 类字面期望的重导 | 允许按新载体重导坐标 / 文本快照 / AI 数值 / 名次数组，逐条记入清单第 6 节，关系断言不变；(a) 类不许改 | 不许重导：重导不了的一律降为 (c) 并补新测试 |
| R9 | `EndReason.AllPassed` / `BoardFull` 与平衡报告 | Core 删两个成员；Sim 分析用字符串常量读旧日志原因，平衡报告"终局原因分布"保持三类旧口径、字节不变；报告改链接两类口径登记为后续观察 | 本 change 一并把报告改为"只剩一名 / 最后战场结算"两类，MODIFIED `match-telemetry`「平衡分析方向」第 8 项与 Scenario「终局原因分布」 |
| R10 | 存档 / 日志里的 `Mode` 字段 | **A**：照写常量 `"Link"`，读端缺字段即拒绝（存档）/ 跳过（分析）、回放报错（D5） | B：删字段（改变全部链接存档与日志首部字节） |
| R11 | 迁移中"合并"是否允许 | 允许：只限迁过去后与既有链接测试逐条同义者，共 5 条，逐名列表、质检与 (c) 一并比对 | 不允许：5 条全部迁过去保留（测试重复） |
| R12 | 只被 Legacy 用到的公开成员 | 一并删除：`RelicLedger.Restore`、`FinalStandings.Compute`（旧）、`FlagPlanting.IsBlind` / `LeastPopulatedZone`、`FlagPublicView.FlagCountByZone`、`MatchFixtures.TwoPlayer`（无调用者） | 保留为工具函数（`RelicLedger.Restore`） |

已知歧义与设计文档未覆盖的判定（建议裁决已含在上表与 D 节）：
- `ai-decision` 规格里有两处 3 人局 / "全图可落子格"的 Scenario，链接模式只有 4 人局、没有全图范围——按 specs 增量改写，不改实现。
- 设计文档 §3.3 仍写"Core 仍保留只服务手写 9×9 测试夹具的旧模式规则分支"，§19 第 6 项登记本 change——实施完成后改为已实施（v1.27）。
- `visual-style-baseline`"棋盘清单为空的地图（只可能是手写测试夹具）"在本 change 后仍成立（9×9 地图数据留在 `TestMaps`），但已无法在其上建局；对应测试按清单 P4 处理。
