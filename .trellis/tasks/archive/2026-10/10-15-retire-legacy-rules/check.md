# retire-legacy-rules 质检报告（tasks 5.4，R4）

> 范围：e31795e（第 1 组）… 01ab9a9（第 5 组文档）相对 a597bb0 的全部改动；本次质检的改动未提交。
> 依据：proposal（R1–R12、G1–G6、H1–H3）、design D1–D11、tasks、migration.md §3 / §4 / §6、`.trellis/spec/core/`。

## 七项结论

| # | 项 | 结论 |
|---|---|---|
| 1 | 逐名比对 §3 / §4 | **通过**。`g2/names-diff.txt`：2157 → 2124 差集 33 行 / 33 方法，与 §3 #1–#33 逐名一致（(c) 28、(j) 5，全 Fact，Theory 行 0）；`g4/names-diff.txt`：删 #34、#35（L18）、增 `中央入口与容差不再校验`、`回放接口拒绝非链接日志`，无其余增减。§4 八行逐个在现行树里找到新名（`两类终局条件Tests.势力悬殊不提前结束 / Pass计数跨回合重置`、`盲选插旗Tests.时限可配置`、`战场内合法落子范围Tests.构筑期内规则照常`、`前瞻模拟的公平信息Tests.构筑期内只在本战场回应`、`他战场玩家数值不进HUD视图模型Tests.同战场不遮蔽`、`地形派生数据不缓存Tests.合法落子范围含本局新架的桥`、`建局约束Tests`），旧名在 `tests/` 只剩注释里的"原名"说明；四个整删文件与 `对局模式Tests.cs`、`MatchFixtures.cs` 均不存在。 |
| 2 | (j) 5 条同义 | **通过（补回 3 处前提 / 后置断言）**。见下"(j) 逐条"。 |
| 3 | (c) 28 条 | **通过**：28 条所测规则在链接规则中均已不存在；链接下仍成立的两处含义（#8 的终局优先级、#17 / #18 的减员后按在局者判整轮 Pass）此前无直接用例，已按 Q1 / Q2 补测。见下"(c) 逐条"。 |
| 4 | 补测 Q1 / Q2 | **已补 2 条并做变异验证**。Q1 按现行实现确认真实优先级后钉住（与规格 / 设计文档 §12.4 一致的那条路径），另有一处规格措辞观察登记待裁决（见"需负责人裁决"N1）。 |
| 5 | §6 重钉表 / 第 3 组改写 / H1 | **通过**。抽查 27 行（AiDecision 8、流程 10、表现 6、Sim 3）另加 H1，用脚本对照 a597bb0 与现行的同名方法 Assert 行：差异全部是平移坐标、`MatchFixtures.Px → TestMaps.Px`、新增前提断言，或 §6 已记的数值重导，关系断言未变。第 3 组约 10 处断言改写与 H1 `RangeOf` 均同义。 |
| 6 | src 改动范围 | **通过**。`git diff a597bb0..HEAD --stat -- src` 共 36 个文件，逐文件都能对到 D4–D10（另 4 个纯注释文件）；重点文件中没有发现链接模式行为变化（`Mode == Link` 的门一律改成无条件、`else` / Legacy 分支删除，链接分支的语句与次序原样保留）。 |
| 7 | 残留 Grep | **通过**。`MatchMode`、`MatchFixtures`、`IsBlind` 在 `src/`、`tests/`（*.cs，不含 bin / obj）0 处（含注释）；`ComputeLink` 只剩 4 处"原 `ComputeLink` 更名"注释；`AllPassed` / `BoardFull` 只剩 `SettlementReason`（链接战场结算原因，合法）、`LogResult.LegacyAllPassed / LegacyBoardFull` 常量及其使用、`LinkModeAnalyzer.AllPassedDefeats` 字段名与注释。 |

## 1. 逐名比对

- 产物：`g2/diff_names.py` → `g2/names-diff.txt`（结论"逐名一致"）、`g4/names-diff.txt`。
- 第 4 节"改名 / 迁出"：现行树逐个 grep 方法签名命中（行号见上表所列文件）；`建局约束Tests` 保留 `链接模式不能配旧地图`、`链接模式只有四人局` 两条。

## 2. (j) 逐条

| # | 删除条 | 目标条 | 判定 |
|---|---|---|---|
| 9 | `三类终局条件Tests.唯一参赛者获胜` | `两类终局条件Tests.唯一参赛者获胜` | 同一 Scenario（2 人出局 + 1 人弃赛 → 唯一参赛者获胜、原因只剩一名、弃赛者组别 Resigned）。弃赛者名次 2 → 4 是名次组序随链接口径变化（链接：完赛 > 出局 > 弃赛），不是丢覆盖。**补回**旧条的后置断言"终局后 `BeginTurn` 抛 `SiegeRuleException`"（全仓无其他用例钉住）。 |
| 10 | `三类终局条件Tests.轮数再多也不结束` | `两类终局条件Tests.轮数再多也不结束` | 同义：第 40 大回合跑完仍在进行、进入第 41 大回合。 |
| 29 | `合法落子范围的对外契约Tests.禁入格不在范围内` | `战场内合法落子范围Tests.禁入格不在范围内` | 同义且更强：旧条断言 B 不含两眼、计数 79 / 81；新条断言 B 的范围 == 战场格 − 两眼（集合相等），A 含眼。 |
| 31 | `构筑保护期的落子限制Tests.保护期内越区落子被拒` | `战场内合法落子范围Tests.构筑期内越区落子被拒` | 同义（构筑期越出所在范围 → `OutOfLegalRange`，范围内照常）。**补回**旧条的"说明违反合法落子范围"消息断言。 |
| 33 | `第4大回合的全图解禁Tests.空盘面玩家重新进入` | `战场内合法落子范围Tests.空盘面玩家重新进入` | 同义（盘面空的玩家征募后可在所在战场任意合法空格落子；链接口径把"全图"换成"所在战场，含链接对方棋盘深处"）。**补回**旧条的两条前提："手牌为空"`IsHandEmpty`、"征募后持有手牌"。 |

## 3. (c) 逐条

| # | 方法 | 判定（链接规则中是否仍存在） |
|---|---|---|
| 1、2 | `链接模式下的决胜一手Tests.旧模式不变`、`链接模式下落后者的停手阈值Tests.旧模式不变` | 只测"模式门关闭时不触发"；门已删（D4），链接侧行为由同类其余条目守。 |
| 3 | `旧模式副本与按生成记录恢复逐项相同` | 对照对象 `RelicLedger.Restore` 删除（R12）；`Copy()` 在拼接后的逐项相同由同文件其余条目守。 |
| 4 | `阶段推进Tests.旧模式没有阶段转换` | 旧模式专属。 |
| 5–7 | `三类终局条件Tests.整轮Pass / 棋盘填满 / 棋盘填满优先于整轮Pass` | 删的是"全局整轮 Pass / 填满即终局"与旧优先级链；整轮 Pass / 填满作为**战场结算原因**仍在，由 `战场结算Tests.整轮Pass结算`、`填满结算`、`决赛倒计时Tests`（到期排在前两条之后）守。 |
| 8 | `只剩一名参赛玩家优先于棋盘填满` | 旧优先级链已无；链接口径的对应优先级"只剩一名 > 最后战场结算"此前只有 `唯一参赛者获胜` / `出局判定Tests.同时归零同时出局` 间接覆盖（未钉"两条同时成立"的前提）→ **补 Q1**。 |
| 11 | `两类终局条件Tests.旧模式终局原因不含最后战场结算` | 旧模式专属。 |
| 12、13 | `终局名次与并列判定Tests.整轮Pass时按同一规则排名`、`旧模式流程中弃赛者排在势力归零出局者之前` | 旧组序（完赛 > 弃赛 > 出局）与旧终局；链接组序由 `链接模式终局名次Tests` 与 (a) 类 8 条守。 |
| 14、15 | `按座位视图Tests.旧模式全图展开`、`视图模型按座位过滤Tests.旧模式全图可见…` | 旧模式专属（全图 + 全员同战场分支已删）。 |
| 16 | `先手顺序预测_链接模式Tests.旧模式预测不变` | 预测的模式门已删；链接预测由同类其余条目守。 |
| 17、18 | `流程回归Tests.出局 / 弃赛改变参赛人数后…Pass计数…重比` | 全局连续 Pass 计数不再决定终局；链接口径的等价含义"减员后按战场剩余在局者判整轮 Pass"此前无直接用例（`主动弃赛Tests.弃赛名次不影响最终名次` 只间接经过）→ **补 Q2**。 |
| 19 | `匿名同时插旗Tests.身份匿名` | 旗数可见已删；"公开视图无身份"由 `盲选插旗Tests.旗帜互不可见` 以 `GetProperties() == [IsLocked]` 严格覆盖（比旧的反射断言更强）。 |
| 20 | `锁定前可更换` | 规则相反（盲选选中即锁定，`盲选插旗Tests.选中即锁定`）。 |
| 21 | `多人同区` | 旧条的旗数与"共享出生区范围"已不存在；多人同区接受由 `盲选插旗Tests.多人同区` 守，同战场共享范围由 `合法落子范围的对外契约Tests.共享出生区内的禁入` 守。 |
| 22–24 | `对局模式Tests.缺省为旧模式 / 公开建局恒为链接模式 / 链接图以链接模式建局` | 模式枚举已删；链接图 / 4 人约束由 `建局约束Tests` 两条守。 |
| 25、26 | `链接模式存档Tests.旧模式存档…` 两条 | Legacy 存档路径已删；缺模式字段被拒由 `链接图上缺模式字段的存档被拒` 守。 |
| 27 | `对局日志的记录内容_链接模式Tests.旧模式日志不写链接模式字段` | 旧模式专属；非链接日志被回放拒绝由 `回放接口拒绝非链接日志` 守。 |
| 28、30、32 | `范围随大回合切换`、`旧模式第4大回合仍全图解禁`、`解禁后全图可落子` | 保护期 3×3 与第 4 大回合全图解禁已删；链接口径"范围随链接扩大"由 `战场内合法落子范围Tests.范围随链接扩大` 守。 |

## 4. 补测

| 编号 | 测试 | 内容 | 变异验证（质检实跑，备份 → 变异 → 还原 → `touch` → `cmp` 逐字节一致） |
|---|---|---|---|
| Q1 | `EliminationEndgame/两类终局条件Tests.只剩一名优先于最后战场结算` | 平坦载体四人同锁棋盘 0，P0 一个批次提光 P1、P2；P3 在小回合边界弃赛 → 棋盘 0 减员结算（Attrition，胜者 P0）、只剩一个含在局玩家的战场且已结算，同时只剩一名参赛玩家 → 原因 `LastPlayerStanding`，结果与活对象的大回合均为 5。钉了"两条同时成立"的前提（弃赛前未结算、ActiveCount 2；弃赛后已结算、占用战场唯一）。 | M-Q1：`Resign` 减员结算后先置 `_pendingEnd = LastBattlefieldSettled` → 本条、`唯一参赛者获胜`、`出局判定Tests.同时归零同时出局` 红 3。 |
| Q2 | `Battlefield/战场结算Tests.弃赛后按剩余在局者判整轮Pass` | `Started([0,0,0,3])` 第 1 大回合：P0、P1 Pass，P2 未行动即弃赛（三人战场剩 2，不触发减员），P3 单人战场 Pass → 战场 0 以 `AllPassed` 结算、条目只有 P0 / P1、落败者 `BattlefieldDefeat`，P2 仍为 Resigned 且无出局原因，对局继续。先查 `战场结算Tests`：整轮 Pass（2 人）、三人战场（3 人全 Pass）、减员结算（剩 1 人）均不覆盖"减员后剩 ≥ 2 人按在局者判"。 | M-Q2：`JudgeAtRoundEnd` 的整轮 Pass 改按战场内全部未出局玩家（含弃赛者）判 → 本条与 `主动弃赛Tests.弃赛名次不影响最终名次` 红 2。 |

Q1 真实优先级的确认（按现行实现，未改实现）：
- 小回合内（批次 / Pass 后的 `CheckEliminations`、`Resign`）：减员结算 `SettleByAttrition` 之后只调 `CheckEndConditions`（只判只剩一名），不判最后战场结算；两条同时成立时记 `LastPlayerStanding`——与规格「两类终局条件」"只剩一名参赛玩家 > 最后战场结算"、设计文档 §12.4 一致，故钉住。
- 大回合末（`EndMajorRound`）：`SettleAtRoundEnd` 之后先判 `LastBattlefieldSettled()` 并以 `LastBattlefieldSettled` 终局。由于每一次"最后战场结算"都恰好留下 1 名胜者，这条路径上"只剩一名"也同时成立；记"最后战场结算"是规格 Scenario「决赛结算即终局」「四人同区一次终局」与设计文档 §12.4"4 人同区时一次结算即终局"的要求，已由既有两条测试钉住。见 N1。

## 5. §6 重钉表、第 3 组改写与 H1

- 抽查脚本：对每个方法取 a597bb0 与现行树的方法体，只比 `Assert` 行（脚本在会话临时目录，不入库）。抽查 27 行：
  - AiDecision：`停手阈值Tests.零收益不落子`、`无落点被保留则Pass`（只多一条 P2 前提断言）、`活形硬约束Tests.为提子而填眼放行`（A1 → C3、J9 → K10，四角贴角）、`前瞻中的下一名对手Tests.专家在预测顺序中居首`、`前瞻的近似两层加分Tests.按下一大回合的口径枚举`（E5 → K10，口径改"本战场"）、`前瞻模拟的公平信息Tests.构筑期内只在本战场回应`（多一条"战场确是棋盘 0"样本断言，⊆ 出生区 → ⊆ 战场）、`专家难度的一层前瞻Tests.模拟回应全为Pass`、`候选格上限Tests.小图不限制`（多一条 125 格前提，黄金值按 §6 重钉）。
  - 流程：`主动弃赛Tests.弃赛后停止行动`（E5 → G7）、`出局判定Tests.同时归零同时出局`、`大回合的定义与推进Tests.出局者不再获得小回合`、`合法落子范围的对外契约Tests.共享出生区内的禁入`（出生区 − A1 / C1 → 棋盘 0 − C3 / E3）、`对局持久化Tests.已结束对局与终局结果可恢复`、`流程回归Tests.最后一手抢到的先锋本大回合结束即生效`（多势力前提 [15,10,5,0]，修正 / 名次 / 先手值 / 顺序不变）、`排名的作用范围Tests.落后不获补偿`（多独立复算前提）、`计分信物连营与犄角Tests.失去控制立即失效`（9 → 6 不变）、`非法批次必须说明原因并高亮Tests.活棋禁入说明`（E5 → F7）、`禁入格Tests.弃赛者的活形仍禁入`。
  - 表现：`始终公开的信息Tests.领地分公开`、`结算节拍序列的生成Tests.相同输入得到相同序列`（F6/E6 → H8/G8）、`活形与禁入格的标示Tests.禁入格在默认棋盘上可见`（E5/G5 → F7/H7，环平移 (+1, +2)）、`所有者看到的是可落子`、`盘面层视图模型带地形Tests.每格带高度地表与桥`（断言一字未改）、`势力层领地与高地Tests.势力层显示高地加值`；另 H1（见下）。
  - Sim：`出生区编号显示Tests.六区图的插旗棋盘快照`（20 行 / "." / 24 格 → 18 行 / 空白 / 25 格，§6 已记"链接终端画出生棋盘占位"）、`终端对局Tests.状态栏大数势力缩写不撑破一行`、`活形记录与统计Tests.活形确立可查`（G1：贴地形小空区 0 → 2、可落子格 81 → 208，§6 已记）。
  - 结论：差异全部可对到 §6 对应行的"旧期望 → 新期望 → 原因"，各行"关系断言不变"属实。
- 第 3 组断言改写（4f6339f 的 tests 部分）：`盲选插旗Tests` 两处（旗数字段为空 → 公开视图属性只有 `IsLocked` / 等于 `new FlagPublicView(..)`）、`链接模式存档Tests` 两处（`restored.Mode == Link` → 再存档的 `Mode` 字段为 `"Link"`；`AssertSameState` 去掉已不存在的 `Mode` 比对）、`各入口按地图标识选图_链接模式Tests` 两处（`Match.Mode == Link` → 地图是链接图；`nameof(MatchMode.Link)` → 字面 `"Link"`）、`决策序列基线_链接模式Tests` 首部 `mode={match.Mode}` → 字面 `mode=Link`（15 份基线首部本就是 `mode=Link`）、`LinkModeFixtures.RequireLink`（模式断言 → 链接图标识断言）、`公开规则纯函数Tests` 调用签名、`收敛口径Tests` / `平衡分析方向Tests` 的原因名改用字面相同的字符串常量。均同义（被断言的成员已不存在时改为断言其可观察的等价物）。
- H1：`单子禁手的标示Tests.查询.RangeOf` 原调 `PublicRules.LegalRange(board, majorRound: 5, birthZone: 0, …)`（缺省 Legacy，第 5 大回合走"全图可落子格"分支：`board.AllCoords().Where(c => board[c].Terrain == Terrain.Playable)` 扣禁入格）；现为同一表达式直接计算 → 逐格相同，同义。

## 6. src 改动范围（a597bb0..HEAD）

| 文件 | 对应 |
|---|---|
| `Match/MatchMode.cs` → `LinkStage.cs` | D4（删枚举，`LinkStage` 保留） |
| `Match/FlagPlanting.cs` | D7（删 `Mode` / `IsBlind` / `LeastPopulatedZone` / 旗数；改选恒拒、`LockAll` 恒取 flag-pick） |
| `Match/MatchFlow.cs` | D4 / D6（建局、`RequireLinkMatch`、弃赛 / 出局 occupancy 恒算、`Publish` 去 Mode、Pass 名单恒记、`CheckEndConditions` 只剩一名、`Finish` 恒链接口径、`EndMajorRound` 两处门） |
| `Match/MatchFlow.Link.cs` / `.Countdown.cs` / `.Preview.cs` / `.SeatProjection.cs` / `.Persistence.Link.cs` | D4（门删除、`LinkStage` 非空、`RequireLinkMode` 删） |
| `Match/MatchFlow.Persistence.cs` | D5 / D9（恒写 `"Link"`、两口都要求模式字段、只剩链接恢复路径、W13 窄解） |
| `Match/FinalStandings.cs` | D6（旧 `Compute` 删、`ComputeLink` → `Compute`） |
| `Match/MatchPhases.cs` | D6 / R9（`EndReason` 两成员删） |
| `Match/MatchPublicView.cs` / `SeatPublicView.cs` | D4（去 `Mode`、`PublishFor` 全图分支删） |
| `Match/PublicRules.cs` | D8（`LegalRange` 去 `majorRound` / `mode`） |
| `Relics/RelicLedger.cs` | R12（`Restore` 删） |
| `Ai/ExpertLookahead.cs` / `HeuristicTurnController.cs` | D4（三处 + 两处模式门删；`among` 改非空） |
| `Board/MapValidator.cs` | D10 / L18 |
| `Presentation/Visibility/DefaultBoardView.cs` / `Worlds.cs` | D4 |
| `Sim/Running/MatchSession.cs` / `Replayer.cs`、`Sim/Play/BoardRenderer.cs` / `PlayCommand.cs`、`Sim/Logging/MatchLog.cs`、`Sim/Analysis/BalanceAnalyzer.cs` / `ReportWriter.cs`、`godot/scripts/Names.cs` | D4 / D5 / D10（L17）、R9 |
| `Board/MapData.cs`、`Determinism/GameSeed.cs`、`Match/BattlefieldSettlement.cs`、`Match/PlayerFlowState.cs`、`Relics/RelicLedgerState.cs`、`Presentation/Layers/LayerContents.cs`、`Sim/Program.cs` | 只改注释 |

重点复核（链接行为）：`MatchFlow*` 每处都是 `Mode == Link ? 链接 : 旧` → `链接`，链接分支语句与调用次序未动（`EndMajorRound` 仍是结算 → 最后战场判定 → 先手 → 阶段推进 → 倒计时；`Resign` / `CheckEliminations` 仍是快照 → 改状态 → 重算 → 减员结算 → 终局检查）；`FinalStandings.Compute` 方法体即原 `ComputeLink`；`FlagPlanting` 盲选路径原样；`PublicRules.LegalRange` 即原 Link 分支；`HeuristicTurnController` / `ExpertLookahead` 只去门；`MatchSession` 五处条件变无条件、首部 `Mode` 写 `"Link"`；`Replayer` 新增非链接拒绝（R10 / L17，链接日志路径不变）。佐证：第 3 / 4 组的存档快照 18 份、日志首部、`link:1`–`link:20` 导出 81 份逐字节相同，15 份基线随全量通过。

## 7. 残留 Grep（`src/`、`tests/`，不含 bin / obj）

- `MatchMode`：0 处（含注释）。`MatchFixtures`：0 处（含注释；第 2 组已把注释措辞改为"原 9×9 流程夹具"）。`IsBlind`：0。`ComputeLink`：只剩 4 处"原 `ComputeLink` 更名"注释（`FinalStandings.cs`、`CarryFixtures.cs`、`终局名次与并列判定Tests.cs`、`链接模式终局名次Tests.cs`）。
- `AllPassed` / `BoardFull`：`SettlementReason.AllPassed / BoardFull`（链接战场结算原因）及其使用；`LogResult.LegacyAllPassed / LegacyBoardFull` 常量及 `BalanceAnalyzer.RuleReasons`、`ReportWriter.EndReasonName`、`SimFixtures`、`收敛口径Tests`、`平衡分析方向Tests` 的使用；`LinkModeAnalyzer.AllPassedDefeats`（链接统计字段名）；`MatchPhases.cs` 说明注释。无 `EndReason.AllPassed / BoardFull` 残留。

## 本次直接修的（小、明确）

| 文件 | 修改 |
|---|---|
| `tests/.../EliminationEndgame/两类终局条件Tests.cs` | 类注释"旧模式的「三类终局条件」由既有测试原样守住"改为已删除说明；`唯一参赛者获胜` 补回 (j) 合并丢的 `BeginTurn` 抛错断言；新增 Q1 |
| `tests/.../Battlefield/战场结算Tests.cs` | 新增 Q2 |
| `tests/.../TurnSequence/战场内合法落子范围Tests.cs` | 类注释"旧模式的同名测试原样保留"改为已删除 / 接替说明，变异记录标注为历史；`构筑期内越区落子被拒` 补回消息断言；`空盘面玩家重新进入` 补回两条前提 |
| `tests/.../InformationVisibility/视图模型按座位过滤Tests.cs`、`tests/.../PresentationFixtures.cs` | 过时的"旧模式全图可见 / ForSeat 原样返回"注释 |
| `src/Siege.Core/Match/MatchFlow.cs` | 注释"对局只因三类条件结束"→"两类" |
| `src/Siege.Sim/Logging/MatchLog.cs` | 类注释"终局原因三类之一（LastPlayerStanding / BoardFull / AllPassed）"→ 链接两类 + 旧日志两类常量 |
| `src/Siege.Sim/Running/MatchSession.cs`（2 处）、`src/Siege.Sim/Config/RunConfig.cs` | "旧模式…逐字节不变 / 恒能取到"注释改为不提旧模式的等价说法 |

均只改注释或测试，src 无代码改动。

## 未改、留档的

- `src/Siege.Core/Match/MatchFlow.Persistence.cs` 报错文案"（存档缺模式字段即旧模式）"：第 3 组按"报错文案沿用"逐字保留，属对外文本，不在质检里改。
- `src/Siege.Sim/Logging/MatchLog.cs:900`"旧模式不写"、`BalanceAnalyzer.cs:1262`"旧模式只有版本 0"：描述读旧日志的口径，仍成立。
- `src/godot/scripts/BoardView.cs:85`、`MatchSession.cs:315 / 316 / 326 / 715` 的"旧模式全图 / 旧模式一区一人"注释：约束不改图形版代码，未动，登记待后续顺手清理。
- 各测试类里引用已删测试名的变异验证历史记录（如 `链接模式存档Tests` M-B21 / M-C7、`链接模式终局名次Tests` M-B15）：历史记录，保留。

## 需负责人裁决

- **N1（规格措辞观察，不阻塞）**：`elimination-endgame`「两类终局条件」"同一时刻满足多条时……只剩一名参赛玩家 > 最后战场结算"一句只在小回合内路径（减员结算）有判别意义；大回合末每一次"最后战场结算"都伴随"只剩一名"，现行实现记"最后战场结算"，与同一 Requirement 的 Scenario「决赛结算即终局」「四人同区一次终局」及设计文档 §12.4 一致。建议后续 change 把该句改写为按路径区分（例如"小回合内因出局 / 弃赛只剩一名时即刻以'只剩一名参赛玩家'终局；大回合末由战场结算终局时记'最后战场结算'"），本 change 不改规格与实现。

## 5.5 最终回归（顺序单跑，真实退出码）

| 项 | 退出码 | 结果 |
|---|---|---|
| `dotnet build siege.sln --no-incremental` | 0 | 0 警告 0 错误 |
| `dotnet build src/godot/Siege.Godot.csproj --no-incremental` | 0 | 0 警告 0 错误 |
| `dotnet test siege.sln` 全量 | 0 | **通过 2119 / 跳过 7 / 总计 2126**（第 4 组末 2117 / 7 / 2124 + 本次补测 Q1、Q2 两条；(j) 补回的断言加在既有方法里，不改条数），3 m 32 s |
| Godot `-- --auto-demo` | 0 | `[flow]` 自检 6 条、失败 0（插旗锁定 → 构筑期 ×3 → 首轮配对 → 链接）；开局对准自检通过 |
| Godot `-- --auto-demo --pick-check --seed=5` | 0 | 开局一轮 42 / 42，本机参与首轮链接后第二轮 78 / 78，遮挡 0、失败 0 |
| Godot `-- --map-select --auto-demo` | 0 | 新局界面第 0–4 步全过（第 4 步建局后棋盘节点 225 = 进入时 220 + 主题名 5），演示 `[flow]` 6 条全过 |
| Godot `-- --auto-demo --rounds=0 --seed=5` | 0 | 七步齐全、`[flow]` 13 条全过，第 16 大回合 `LastBattlefieldSettled` 终局，开局对准自检通过 |

- Godot 用 `D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path src/godot`（HANDOFF「自检命令」），四条均自行退出（外层加了 `timeout` 兜底，未触发）；四份日志无 `ERROR` / `Exception`。
- 与 `retire-legacy-maps` 5.3 记录对照：`--auto-demo` 6 条、`--map-select` 五步、`--rounds=0 --seed=5` 13 条七步齐全——一致。**差异**：拾取计数 30 / 30、55 / 55 → 42 / 42、78 / 78，来自其后的 `link-map-terrain`（棋盘边长 6–8，HANDOFF 已注明）；与最近一次记录（`ai-seat-vision` / `final-countdown` implement：78 / 78、第 16 大回合终局；第 91 帧"战场 出生区 5 已结算，胜者 紫方(P3)"、第 115 帧到期结算本机"战场落败（第 2 名）"）逐项一致。
- 15 份基线：`git diff a597bb0 --stat -- tests/Siege.Core.Tests/AiDecision/Fixtures` 为空、`git status` 为空，随全量通过。
- HANDOFF「测试现状」已刷新为 2126 / 2119 / 7。
