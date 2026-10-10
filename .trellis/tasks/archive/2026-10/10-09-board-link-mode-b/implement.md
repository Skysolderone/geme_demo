# 10-09-board-link-mode-b 段 B 实施报告（前半：2.1–2.5）

对应 `openspec/changes/board-link-mode/tasks.md` 2.1–2.5（已勾上）。后半 2.6–2.10 未动。

## 1. 勾选与测试类（`tests/Siege.Core.Tests/`）

| task | 状态 | 测试类 |
|---|---|---|
| 2.1 盲选插旗 + 模式门 + 模式 ↔ 地图匹配 | 完成（冒险概率未删，见 §5-1） | `MatchSetup/对局模式Tests`（缺省为旧模式、链接模式不能配旧地图、链接图不能以旧模式建局、链接图以链接模式建局）、`MatchSetup/盲选插旗Tests`（旗帜互不可见、选中即锁定、多人同区、时限到时未选者由flag_pick均匀指定、调试依次插旗、AI均匀盲选可复现、选区不扰动其他随机、链接模式不用冒险概率） |
| 2.2 阶段机 | 完成（战场结算判定用桩，见 §4） | `Battlefield/阶段推进Tests`（构筑期三个大回合不转换、四人四区、两人同区加两人独占、提前打完的同区棋盘不进配对、三人同区加一人独占、四人同区、决赛链接、构筑期结算的两个同区战场在第3大回合结束进入决赛、挂接配对来源时当场配对、旧模式没有阶段转换） |
| 2.3 配对事件 | 完成（只有内存事件，日志 / 存档属 2.9） | `Battlefield/配对事件由对局外部提供Tests`（合法配对被接受、奇数候选需要等待者、非法配对被拒 ×5、等待者须是候选且不在配对里、决赛只接受唯一合法结果、列出顺序不影响结果、不在待配对时拒绝配对事件、脚本固定配对的确定性缺省、来源给出非法结果时停在待配对、无来源时跑到底报待配对） |
| 2.4 链接执行 + B6 信物 | 完成 | `Battlefield/链接执行Tests`（链接不动已有格〔含棋子与栅栏〕、公共棋盘信物由链接子流生成并追加进账本、信物子流独立〔两种先后次序〕、信物内容只由对局种子与战场对决定） |
| 2.5 战场内合法落子范围 | 完成 | `TurnSequence/战场内合法落子范围Tests`（构筑期内越区落子被拒、范围随链接扩大、链接后可进入公共棋盘、其他战场不可落子、等待者可落子、等待者无法越界、空盘面玩家重新进入、禁入格不在范围内、旧模式第4大回合仍全图解禁） |

夹具：`tests/Siege.Core.Tests/LinkModeFixtures.cs`（`link:1` 进程内只生成一次、Link 选项、按出生棋盘开局、整大回合 Pass 推进）。出生棋盘 / 战场编号一律 0 起（规格"棋盘 1–5"= 0–4）。

## 2. 新增 / 修改文件

新增：
- `src/Siege.Core/Match/MatchMode.cs` — `MatchMode { Legacy, Link }`（B1）与 `LinkStage { Building, Fighting, Final }`（进行中之内的阶段标签）。
- `src/Siege.Core/Match/Pairing.cs` — `PairingKind`、`PairingRequest`、`PairingDecision`（含规范次序 `Canonical`）、`IPairingSource`、`ScriptedPairingSource`（脚本优先、缺省按编号升序两两配 / 最大者等待 / 决赛唯一结果）、`PairingRecord`、`LinkRecord`。
- `src/Siege.Core/Match/MatchFlow.Link.cs` — 链接模式流程：首轮候选、决赛候选、`ApplyPairing`、校验、规范次序逐对链接、`ExecuteLink`（段 A 接线契约 + B6）、大回合结束的阶段转换 `AdvanceLinkPhase`、战场结算执行桩 `SettleBattlefield`。
- 测试 7 个文件（见 §1）。

修改：
- `Match/FlagPlanting.cs` — `MatchOptions.Mode`；`FlagPlanting` 盲选（`IsBlind`：改选即拒、公开视图不含旗数、`ViewFor(seat)` 只给本座位与锁定后同棋盘者、时限到时 `flag-pick` 兜底）；新增 `SeatFlagView`。
- `Match/PrototypeZoneAssignment.cs` — `BlindPicks`（`flag-pick` 子流每人一次）、`AssignBlind`；旧 `Assign` 不动。
- `Determinism/GameSeed.cs` — 子流名 `FlagPick = "flag-pick"`、`RelicLink(x, y) = "relic-link:<min>:<max>"`。
- `Match/MatchPhases.cs` — `MatchPhase` 追加 `AwaitingPairing` / `AwaitingFinalLink`；`FlowEventKind` 追加 `PairingApplied` / `BattlefieldsLinked`（都追加在末尾，旧值不变）。
- `Match/PublicRules.cs` — `LegalRange` 加可选参数 `mode`（缺省 Legacy，旧分支逐字节不变）；Link 下 = 含其出生棋盘的战场格；新增 `BattlefieldOf(board, zone)`。禁入扣除仍只此一处。
- `Match/MatchFlow.cs` — 建局 `RequireModeMatchesMap`；构造 `FlagPlanting` 传模式与种子；`LockFlags` 记锁定旗数；`PlantPrototype` 按模式分流；`LegalRangeFor` 传模式；`CheckEndConditions` 在 Link 下跳过"填满 / 整轮 Pass"（只留"只剩一名"）；`EndMajorRound` 末尾在 Link 下调 `AdvanceLinkPhase`；`Publish` 填 `Mode`。
- `Match/MatchPublicView.cs` — 非位置的 `Mode { get; init; }`（缺省 Legacy，既有构造写法不变）。
- `Match/MatchDebugAccess.cs` — 测试接缝 `SettleBattlefield(id, winner)`、`Link(x, y)`。
- `Match/MatchRunner.cs` — `RunToEnd` 停在待配对时抛明确错误（原来会返回 null 结果）。
- `Relics/RelicGenerator.cs` — `GenerateLinked`（复用私有 `Draw`、只消费 `relic-link:<X>:<Y>`、无出生区校正）。
- `Relics/RelicLedger.cs` — `Append`（追加公共棋盘信物，坐标已存在即拒）、`LinkedPlacements`。
- `Ai/ExpertLookahead.cs` — 两处 `PublicRules.LegalRange(...)` 调用补传 `view.Mode`（见 §5-3）。
- `openspec/changes/board-link-mode/tasks.md` — 2.1–2.5 勾上。

## 3. 测试结果

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2188 条，通过 2178，跳过 10，失败 0**（最后一次全量）。新增 49 条（含 Theory 展开）。既有测试断言一条未改。
- 注意：全量共跑 4 次。前 2 次各有 1 条 `MapGeneration.棋盘档布局规则Tests.生成耗时` 失败（墙钟断言"每张图 < 1 s"，单独跑通过；同机 HEAD 临时工作树全量 2139 条 0 失败）；第 3 次全绿；随后把 `AI均匀盲选可复现` 的 40 种子循环改为直接调选区实现（不再建 40 局）减轻负载，第 4 次（最终）全绿。判断为负载敏感的计时测试偶发，见 §6-1。

变异验证（实跑，脚本备份 → 改 → 构建 → 只跑相关类 → 写回原字节并 `os.utime` 刷新时间戳 → 逐字节校验）：

| 编号 | 变异 | 红 |
|---|---|---|
| M-B1 模式门 | 删 `Build` 里的 `RequireModeMatchesMap` | `对局模式Tests` 2 |
| M-B2 模式门 | `LegalRange` 忽略 `mode` | `战场内合法落子范围Tests` 4（旧模式 `第4大回合的全图解禁Tests` 2 条仍绿） |
| M-B3 子流隔离 | `FlagPick` 改为 `"setup"` | `盲选插旗Tests` 2（钉住子流名） |
| M-B4 子流隔离 | `relic-link` 子流随链接序号变化 | `链接执行Tests.信物子流独立` 1 |
| M-B5 | 配对校验删"漏了候选" | `非法配对被拒(漏了候选)` 1 |
| M-B6 子流隔离 | 原型盲选时与人同区的 AI 多抽一次 setup 子流 | `选区不扰动其他随机` 1（首次 0 红：原测试只比两种人工选择、该种子下不触发耦合；已改为取遍 5 块棋盘后重跑红 1） |
| M-B7 | 首轮候选按"当前只剩 1 人"而非"锁定时旗数为 1" | `阶段推进Tests` 2 |
| M-B8 | 按事件列出顺序而非规范次序链接 | `列出顺序不影响结果` 1 |
| M-B9 | 对 `Board.Map`（含改造）而非 `LinkedMap` 调生成器 | `链接不动已有格` 1 |

## 4. 后半 2.6–2.10 接线点

- **战场结算（2.6）**：`MatchFlow.Link.cs` 的 `SettleBattlefield(id, winner)` 是执行桩（标记已结算、胜者外的在局者出局、共享一个出局序号、重算派生量），现只由 `Debug.SettleBattlefield` 调用。2.6 要做的：在 `EndMajorRound`（`AdvanceLinkPhase` 之前）对每个战场判定"整轮 Pass / 无可落子空格"，在出局 / 弃赛后判定"减员只剩 1 人"（即时标记）；按势力 + 判定链排出胜者再调它；需要"本大回合内全员 Pass"的逐战场记录（现有 `_passStreak` 是全局的，不能直接用）。出局原因"战场落败"与落败时势力记录要加到 `PlayerRecord`。
- **决赛触发**：`FinalReady()` 现按"每个有人战场只剩 1 名在局者且在局 ≥ 2"判定（与"全部多人战场已结算"等价：结算 / 减员后都只剩 1 人），不读 `_settledBattlefields`；2.6 若引入"结算后仍有多人"的情形须改这里。
- **终局（2.7）**：`CheckEndConditions` 的 Link 分支现只留"只剩一名参赛玩家"；"只剩一个战场且已结算"在结算即淘汰落败者的口径下被它覆盖。`EndReason` 的注释写着 MUST NOT 加成员——2.7 若要新原因（如"战场结算"）需裁决（S4）。`FinalStandings` 的排序（决赛胜者 → 决赛其他 → 落败者倒序 → 弃赛）在 `Finish` 里接。
- **视图（2.8）**：`FlagPlanting.ViewFor(seat)` 已是插旗阶段的按座位视图雏形；`MatchPublicView.Mode` 已在。`ForSeat` 用 `BattlefieldOf(player)` / `PublicRules.BattlefieldOf` 过滤格。
- **存档与日志（2.9）**：读 `Pairings`（`PairingRecord`）、`Links`（`LinkRecord`：序号、所属配对、X/Y、版本号、退化、摘要、新信物格）、`LockedFlagCounts`、`SettledBattlefields`、`Relics.LinkedPlacements`、`Options.Mode`。恢复顺序：建局（`BaseMap`）→ 按 `Links` 顺序 `ExecuteLink` 重放（信物同时重生）→ 再重放改造。`_firstPairingStepDone` / `_finalLinked` / `_pendingPairing` 三个阶段字段要入存档或由记录推出。**现在保存一局 Link 对局会静默丢掉模式与链接**——2.9 之前不要对 Link 局调 `Serialize`。
- **带入带出（2.10）**：`SettleBattlefield` 出局的玩家走现行出局结算即可。
- **存档恢复路径**：`MatchFlow.Persistence` 的恢复走私有构造函数（`RestoreCore` → `new MatchFlow(...)`），**不经过** `Build`，因此不做模式 ↔ 地图校验、`Options.Mode` 恒为缺省 Legacy；2.9 要在那里从存档读入模式并补做校验。
- **Sim 入口（段 C 3.1）**：`MatchSession.Create` 用 `MatchOptions.Immediate`（Legacy）建局，所以现在 `run / play --map=link:N` 会在 `MatchFlow.Create` 抛"链接图只能以链接模式建局"，而不是像以前那样以旧规则在链接图上跑——这是 B1 的预期后果，不是回归；段 C 解析到 `link:` 标识时须设 `Mode = Link` 并挂接配对来源。

## 5. 偏离与需裁决事项

偏离（已按裁决或规范取舍，供知悉）：
1. **冒险概率未删**：tasks.md 2.1 写"删除冒险概率（配置、日志首部、`--flag-risk`）"，裁决 B2 写"Legacy 保留冒险概率、`--flag-risk` 段 C 删"——按 B2 执行，`MatchOptions.FlagRisk` 与旧路径原样；Link 下不读它。
2. **模式 ↔ 地图按标识判定**：B1 写"Link 只能配链接档地图"。`MatchFlow` 不能读 `Profile`（守门 `地图规格档Tests.规格档只被地图数据文件格式与校验器引用` 扫全 `src/`），改用 `LinkMapId.IsLinkMap(map.Id)`；链接本身也要从标识取地图种子，两者一致。后果：手写夹具 `LinkMapFixtures`（规格档 Link、标识 `test-link-5`）不能以 Link 模式建局，可以以 Legacy 建局。
3. **改了一处 AI 代码**：`ExpertLookahead` 两处前瞻上下文的 `LegalRange` 补传 `view.Mode`（只是参数透传，评价代码未动）。不传的话 Link 局里专家前瞻会按旧口径（第 4 大回合起全图）模拟对手落到别的战场。
4. **配对校验多一条"全覆盖"**：规格写"每个候选至多出现一次"，实现要求每个候选**恰**出现一次（在某一对里或作为等待者），否则 {(1,4)} 这类只配一对的事件会被接受、另两名候选永远不配对。
5. 2.3 的"写日志与存档"、2.4 的日志部分只做到内存事件（`Pairings` / `Links` / `FlowEvent`），序列化属 2.9。
6. 构筑期 / 交战期都保持 `MatchPhase.InProgress`，区分靠 `LinkStage`；只有两个"待…"阶段是会阻塞小回合的顶层阶段（既有代码大量以 `InProgress` 判定可行动）。

需负责人裁决：

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| S1 | Link 模式是否强制 4 人（proposal 裁决 3b） | 未强制，2–4 人都能建局 | 段 B 后半或段 C 在 `RequireModeMatchesMap` 旁加"Link 须 4 人"；本段测试全用 4 人 |
| S2 | 决赛候选 > 2 个战场时的合法结果 | 实现为以最小者为轴依次链接（(c0,c1),(c0,c2)…）。五种开局下决赛恒为 2 个战场；> 2 只在等待期内有人弃赛等边缘情形才出现，且生成器的序列核对没覆盖这种链接序列 | 接受；或规定 > 2 时先打掉多余者 |
| S3 | 待配对 / 待决赛链接阶段能否弃赛 | 不能（`Resign` 要求进行中） | 接受（阶段在大回合边界、外部随即提交事件）；或放开 |
| S4 | `EndReason` 是否允许新增"战场结算"类原因 | 未加；注释写 MUST NOT 加成员 | 2.7 前裁决：沿用 `LastPlayerStanding`，或新增原因并改注释 |

## 6. 遗留与风险

1. `棋盘档布局规则Tests.生成耗时` 是墙钟断言（每张图 < 1 s），新增 49 条测试（`link:1` 首次生成约 1 s、多次建局）加重并行负载后前 3 次全量里红了 2 次，单独跑与后 2 次全量都绿（其间已把一处 40 局循环改为直接调选区实现）。不是规则回归，但会让全量偶发红；可考虑把它移入 `[PerfTheory]` 类别或放宽阈值（需裁决，本段未改既有测试）。
2. 挂接配对来源时，来源给出非法结果会从 `Confirm()` / `Pass()` 里抛出（此时该小回合已完成、对局停在待配对），调用方须能接住；无来源时 `MatchRunner.RunToEnd` 抛明确错误。
3. `FirstRoundCandidates` 依赖 `LockedFlagCounts`，后者只在 `LockFlags` 时记录；存档恢复（2.9）须把它带上，否则恢复后首轮候选为空。
4. 非规范次序的链接（测试接缝 `Debug.Link`）在 `link:1` 上能生成，但生成器只对规范次序做过穷举核对；对局流程只走规范次序。
5. **盲选下 `FlagPublicView` 没有旗数**：`Flags.PublicView().FlagCountByZone` 为空、`FlagsAt(zone)` 会抛越界。表现层 / Godot 若在 Link 局读旗数会崩，段 D 须改读 `Flags.ViewFor(seat)`。
6. 战场按 `Board.Map`（含改造）求连通分量；链接档上改造不能把不可落子格变成可落子格（段 A 不变量），所以与按 `LinkedMap` 求等价。


---

# 段 B 实施报告（后半：2.6–2.10）

前半已由主会话提交为 6c7c0bd。后半对应 `tasks.md` 2.6–2.10（已勾上），并落实负责人 2026-10-10 对前半 S1–S5 的裁决。

**前半报告的过期项**（以本节为准）：
- 前半 §4「现在保存一局 Link 对局会静默丢掉模式与链接——2.9 之前不要对 Link 局调 `Serialize`」→ 已由 2.9 解决。
- 前半 §4「`FinalReady()` … 不读 `_settledBattlefields`」→ 仍然成立，但 2.6 的真实结算已经接上。
- 前半 §5 S1–S4 → 负责人已裁决（见 proposal 裁决表），落实情况见本节 §1。
- 前半 §6 第 1 条（`生成耗时` 偶发红）→ 已按 S5 移入 Perf 类别。
- 前半 §6 第 3 条（`LockedFlagCounts` 须随存档）→ 已做。

## 1. 勾选与测试类（`tests/Siege.Core.Tests/`）

| task / 裁决 | 状态 | 测试类 |
|---|---|---|
| 2.6 战场结算 | 完成 | `Battlefield/战场结算Tests`（12 条，覆盖：整轮 Pass 结算、一方落子即不结算、他战场有人落子不妨碍本战场整轮 Pass 结算、填满结算、三人战场结算、减员结算、势力归零的减员结算、等待者 Pass 不结算〔含等待大回合数〕、构筑期也结算、出局者出序、已结算的战场不再结算、全零局面按种子兜底顺序定胜者） |
| 2.7 终局与名次 | 完成 | `EliminationEndgame/两类终局条件Tests`（唯一参赛者获胜、决赛结算即终局、非最后战场的整轮 Pass 不终局、四人同区一次终局、轮数再多也不结束、旧模式终局原因不含最后战场结算）；`EliminationEndgame/链接模式终局名次Tests`（决赛胜者第一、更早出局者按出局先后倒序、同一战场多名落败者按势力排、信物相同比独占空格数、逐级比较到棋子数、同次势力归零者共享名次、弃赛者最后、对局里战场落败者按结算时的比较量排名） |
| 2.8 按座位视图 | 完成（空格归属只给独占，见 S8） | `InformationVisibility/按座位视图Tests`（他人战场呈现为未展开、他人势力不可见、链接后立即可见、数据层过滤〔序列化〕、插旗阶段的可见信息〔含锁定后只见自己的棋盘〕、多人同区锁定后只见同棋盘者、战场结算结果全局公开、行动顺序与带入全局公开、旧模式全图展开） |
| 2.9 存档与日志 | 完成（Core 记录结构；日志写出属段 C） | `MatchSetup/链接模式存档Tests`（存档恢复地图、小回合边界存档续跑一致、待配对时存档、终局后存档、锁定结果可持久化、旧模式存档不写新字段且按旧模式读入、链接图上缺模式字段的存档被拒、链接重放与存档不符即拒绝）；`MatchTelemetry/链接模式对局记录Tests`（配对与链接可查、战场结算可查、锁定旗数与等待时长可查） |
| 2.10 带入带出 | 完成 | `CarryInOut/战场落败出局结算Tests.战场落败同样丢失`（含决赛落败者） |
| S1 Link 强制 4 人 | 完成 | `对局模式Tests.链接模式只有四人局` |
| S2 决赛候选 > 2 | 实现原样，补测试 | `配对事件由对局外部提供Tests.决赛候选超过两个时以最小编号为轴`（只测纯函数：4 人局在 S1 下流程里到不了这种局面） |
| S3 待配对阶段不可弃赛 | 维持现状，补测试 | `配对事件由对局外部提供Tests.待配对与待决赛链接阶段不可弃赛` |
| S4 终局原因"最后战场结算" | 完成 | `EndReason.LastBattlefieldSettled`（末位追加，注释已改）；见上 2.7 |
| S5 `生成耗时` 移入 Perf | 完成 | 新增 `PerfFactAttribute`（与 `PerfTheoryAttribute` 同一跳过方式，放在同文件）；`棋盘档布局规则Tests.生成耗时` 改用 `[PerfFact] [Trait("Category","Perf")]` 并加 `using`，断言不变 |
| 端到端 | 完成 | `Battlefield/链接模式端到端Tests.从插旗跑到终局` × 5 种开局：`MatchRunner` + 脚本固定配对 + 确定性控制器（前 N 个大回合每回合落 1 子，之后 Pass）；第 4 大回合开始时存档、恢复后挂同样的来源与控制器续跑；断言终局原因、名次完整、链接与结算次数，两局结果与再存档逐字节相同 |

## 2. 新增 / 修改文件

新增：
- `src/Siege.Core/Match/BattlefieldSettlement.cs`：`EliminationReason`（PowerZero / BattlefieldDefeat）、`SettlementReason`（AllPassed / BoardFull / Attrition）、`SettlementEntry`、`BattlefieldSettlementRecord`。
- `src/Siege.Core/Match/SeatPublicView.cs`：按座位视图 `SeatPublicView` 及其子记录；`MatchFlow.PublishFor(seat)`。
- `src/Siege.Core/Match/MatchFlow.Persistence.Link.cs`：存档 `Link` 段（`LinkSaveData` 等）、`ExportLink` / `RestoreLink`。
- 测试 8 个文件（见 §1）。

修改：
- `MatchFlow.Link.cs`：真实战场结算——`SettleAtRoundEnd`（逐战场整轮 Pass / 填满）、`SettleByAttrition`（减员）、`SettleBattlefield`（比较链排序、落败者出局并记录比较量）、`LastBattlefieldSettled`、等待大回合数；`ExecuteLink` 新增 `replay` 参数并返回记录；`Settlements`、`PassedThisRound`；Debug 接缝保留（原因记整轮 Pass，随后立即判终局）。
- `MatchFlow.cs`：S1 人数校验；Pass 逐人记录（只在 Link 下）；`CheckEliminations` / `Resign` 在 Link 下记出局原因并做减员结算；`EndMajorRound` 在 Link 下先结算、再判"最后战场结算"；`Finish` 在 Link 下用 `ComputeLink`，落败者用结算时的比较量；`PlayerRecord` 加出局原因、落败比较量、等待大回合数；`StateOf` 带出这三项。
- `FinalStandings.cs`：`StandingInput.Reason`（init 属性）、`ComputeLink`、`DefeatComparer`；旧 `Compute` 不动。
- `PlayerFlowState.cs`：init 属性 `EliminationReason` / `PowerAtDefeat` / `WaitingRounds`（旧模式恒为 null / null / 0）。
- `MatchPhases.cs`：`EndReason.LastBattlefieldSettled`（末位，S4）、`FlowEventKind.BattlefieldSettled`。
- `Pairing.cs`：`PairingDecision.Source`（脚本来源记 `script`、外部未注明记 `external`）、`PairingRecord.Source`；`LinkRecord` 加 `PublicBoard` / `CorridorCells` / `MapDigest` / `NewRelics`（match-telemetry 第 9 项）。
- `MatchFlow.Persistence.cs`：`Mode` 与 `Link` 两个字段只在 Link 下写出（`WhenWritingNull`，旧模式存档逐字节同前）；`StandingSaveData.Reason` 同理；Link 恢复先建空盘、再重放链接、再写盘面；恢复时也做模式与地图 / 人数校验（不符抛 `FormatException`）。
- `GameBoard.cs`：`internal LoadState`（在已重放链接的空盘上写入盘面）。
- `RelicLedger.cs`：`Restore` 拆出 `RestoreState`（Link 恢复在追加公共棋盘信物之后再恢复揭示），行为不变。
- `MatchDebugAccess.cs`：`SeedRankOf`。
- 前半测试按 2.6 的真实结算改写（不再依赖"全员 Pass 也不结算"）：`阶段推进Tests`、`配对事件由对局外部提供Tests`、`战场内合法落子范围Tests`、`对局模式Tests`；`LinkModeFixtures` 新增 `PlayRound` / `PlayRounds` / `PlaceOne`。
- 旧测试只动了两处，都是 S5 授权的：`LifeShape/活形分析性能基线Tests.cs` 新增 `PerfFactAttribute`；`MapGeneration/棋盘档布局规则Tests.cs` 换特性并加一行 `using`。断言一条未改。
- `openspec/changes/board-link-mode/tasks.md`：2.6–2.10 勾上。

## 3. 测试结果

- `dotnet build siege.sln -c Release`：0 警告，0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2243 条，通过 2232，跳过 11，失败 0**（跳过 = 原 10 条 + 移入 Perf 的 `生成耗时`）。后半新增 55 条（含 Theory 展开）。全量后半跑了 3 次，最后两次全绿；第一次的 10 红都已处理：其中 2 条是旧守门测试（见下），改实现而非改测试；其余 8 条是前半测试，因 2.6 的真实结算而改写。
- 两条旧守门测试曾红，已改实现让它们原样通过：
  - `四邻接Tests.几何邻居枚举只在允许名单内直接调用`：座位视图里用了 `Board.Neighbors` 取栅栏，已改读 `TerrainData.Fences`。
  - `总势力Tests.领地计分直接取空格归属结果`：座位视图里读了覆盖表的空格归属，已改为从势力快照的独占空格取。连注释里出现该方法名也会命中，一并改掉。

变异验证（脚本：备份 → 改 → 构建 → 只跑相关类 → 写回原字节 + `os.utime` 刷新时间戳 → 逐字节校验）：

| 编号 | 变异 | 红 |
|---|---|---|
| M-B10 | 整轮 Pass 改读全局 `_passStreak >= ActiveCount` | 1（「他战场有人落子不妨碍本战场整轮Pass结算」） |
| M-B11 | 单人战场也参与结算（`< 2` → `< 1`） | 10（含「等待者Pass不结算」） |
| M-B12 | `Resign` 不做减员结算 | 1（「减员结算」） |
| M-B13 | 大回合结束的最后战场结算改记 `LastPlayerStanding` | 7（两类终局条件 2 + 端到端 5） |
| M-B14 | `DefeatComparer` 恒 0 | 4。「信物相同比独占空格数」首次 0 红：按编号兜底恰好与期望同序；已调换编号后重跑 |
| M-B15 | `Finish` 的 `ComputeLink` / `Compute` 模式门反转 | Link 侧 1（「唯一参赛者获胜」）；旧模式名次测试 0 红，见 §6 第 4 条 |
| M-B16 | `PublishFor` 可见格改为全图 | 4 |
| M-B17 | 同战场判定恒真 | 4 |
| M-B18 | `RestoreLink` 先写盘面再重放链接 | 3 |
| M-B19 | 存档不写 `PassedThisRound` | 1 |
| M-B20 | S1 人数校验改为 `> 4` | 1 |
| M-B21 | 旧模式存档也写 `Mode` | 1 |

## 4. 段 C / D 接线点

- **Sim（段 C 3.1）**
  - Core 侧已就绪：`MatchSession.Create` 解析到 `link:` 时，须以 `MatchOptions.Mode = Link` 建局，并挂接 `PairingSource`（`run` 用由对局种子派生的来源，`play` 用真随机），同时在 `PairingDecision` 上注明 `Source`。
  - 日志事件可从这些记录直接转写：`match.Pairings` / `Links` / `Settlements` / `PlayerStates`（出局原因、落败势力、等待大回合数）/ `LockedFlagCounts` / `Result.Reason`；`FlowEventKind` 新增 `PairingApplied` / `BattlefieldsLinked` / `BattlefieldSettled`。
  - Sim 现有的 `CopyFlowEvents` 用的是 switch 语句，新事件种类会落到缺省分支，不报错。
- **Godot / Presentation（段 D）**：按座位渲染读 `PublishFor(seat)`（`Cells` / `Fences` / `Relics` / `Groups` / `Players` / `Placeholders` / `Settlements` / `Links`）；插旗阶段读 `Flags.ViewFor(seat)`。

## 5. 偏离与需裁决事项

已按以下口径实现：
1. **终局原因的读法（S4）**
   - 大回合结束时战场结算后只剩一个含在局玩家的战场（含结算淘汰落败者后只剩胜者一人）→ `LastBattlefieldSettled`。
   - 因弃赛或势力归零即时只剩一人 → `LastPlayerStanding`，即时生效、优先。
   - 规格 Scenario「决赛结算即终局」要求前者、「唯一参赛者获胜」要求后者，这样两条都满足。"只剩一名 > 最后战场结算"的优先级体现在：后者只在大回合结束判定，前者即时判定、先到先终。
2. **B5 优先于 match-setup「旧存档被拒绝」**：无模式字段的存档按旧模式读入；链接图上的旧模式存档会被模式校验拒绝。tasks.md 2.9 里"旧存档（非 `link:`）拒绝读取"留待段 C 删旧入口时再做。
3. **锁定旗数既存档、也可由各玩家的出生棋盘推出**：二者恒等，恢复时用存档值。
4. **match-telemetry 第 8–11 项只落在 Core 的记录结构里**，日志写出属段 C；Sim 没改。
5. **2.8 的入口是独立类型**：没有给 `MatchPublicView` 加过滤版 `ForSeat`，而是新增 `SeatPublicView`，入口 `MatchFlow.PublishFor(seat)`。原因有二：`GameBoard` 无法表示"未展开"的格；全知视图 `Publish()` 要对旧模式、AI 全知例外与遥测保持原样。

需负责人裁决（编号续 S6）：

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| S6 | 战场结算时比较链（势力 → 信物 → 独占空格 → 棋子）四级全同，谁是胜者 | 取种子兜底顺序（`SeedRank`，锁定时由 setup 子流排出）靠前者。全员没落子就 Pass 时每次都走这条 | 接受；或规定"全同则不结算、继续"（可能拖长对局） |
| S7 | 同一大回合结束时多个战场同批结算，各自的落败者如何排名 | 共享一个出局序号，名次按结算时的比较链跨战场排序。例：「决赛胜者第一」中 D 55 排在 B 42 前 | 接受；或按战场编号分先后 |
| S8 | 座位视图的空格归属只给"独占者"，没有区分"争议 / 中立"（规格第 3 项要三态） | 要读覆盖表的三态，须把 `SeatPublicView.cs` 加进旧守门 `总势力Tests.领地计分直接取空格归属结果` 的读者名单（改旧测试断言），本段未改 | 同意改名单（理由：只读投影、不统计领地分）则补上三态；或由段 D 用全知盘面算三态，只在可见格上显示 |
| S9 | 单人战场不做"填满"检查 | 规格（b）没限定人数，但单人战场结算不会有任何落败者，只多一条无意义记录 | 接受 |

## 6. 遗留与风险

1. **AI 前瞻没有战场结算的概念**：`ExpertLookahead` 模拟对手时不知道"全员 Pass 就结算"。AI 过早 Pass 会直接落败（design Risks 已列），段 C 冒烟时观察。
2. **控制器 Pass 的时机决定胜负**：全零局面按种子兜底定胜者（S6），批量统计时需注意。
3. **座位视图的性能**：`PublishFor` 每次全量算一遍活形与势力投影，未做缓存；段 D 每帧调用的话要看耗时。
4. **旧模式名次没有回归护栏**：M-B15 旧模式侧 0 红，即没有一条旧模式流程测试同时出现"弃赛者 + 出局者"。分组顺序由 `FinalStandings.Compute` 自己的单元测试守住，`Finish` 里的模式门没有旧模式护栏。
5. **决赛超过 2 个战场的序列生成器没核对过（S2）**：流程里在 S1 之下到不了，只有纯函数测试。
6. **`LinkRecord.MapDigest` 每次链接都算一次**（导出 73×73 图文本 + SHA-256），单次几毫秒，可忽略。
7. **先手顺序预测可能不准**：`MatchFlow.Preview.cs` 的 `ForecastInitiative` 不知道 Link 模式下"大回合结束会先做战场结算"。若本大回合结束时某战场将结算，预测的下一轮顺序可能多出即将落败的玩家（未逐行核实）。表现层只用于提示；段 D 若要显示，须改用结算后的顺序或注明。
