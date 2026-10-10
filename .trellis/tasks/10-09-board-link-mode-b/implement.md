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
