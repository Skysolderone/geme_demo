# 10-20-open-map-b 实施记录（段 B：删死代码与同步文档）

依据：`openspec/changes/open-map/`（proposal O1–O8、design D1–D10 / A1–A8 / **B1–B10**、tasks 2.1–2.5、13 份规格差分）；段 A 记录 `.trellis/tasks/10-19-open-map-a/notes.md`（1.5 Skip 清单）与 `check.md`。
硬约束遵守：未 commit / archive、未改 `openspec/` 正文、未动 `ShowTimeline.cs` / `VisualBaseline.cs` / SettlementShow 飘字测试；同一时间只跑一个 dotnet / Godot；没有跑任何批量（最大一次为测试里的 3 局）。

## 先记后做（与 design / 规范的冲突与自行决定）

- **N1 基线首部文案**：5 份缺省决策序列基线（`decisions-link1-*.txt`）首部第 3 / 5 行写着 `mode=Link pairing=script（缺省：升序两两配）` 与 `P 配对、L 链接、S 战场结算`。PRD 硬指标"5 份基线逐字节不变"与"删干净配对口径"冲突：按硬指标，导出器 `LinkDecisionSequence.Export` 这两行**字面保留**（配对 / 链接 / 倒计时的 P / L / C 行逻辑已删，只剩 S 行），注释写明是历史字样。**待裁决**：是否允许重钉 5 份基线只改首部两行（决策行不变）。
- **N2 拼接节拍类型残留**：`src/Siege.Presentation/Show/ShowTimeline.cs` 不在本次范围内，而它 `case BoardSlideBeat / OpponentRevealBeat` 并读 `ShowMask.Slide` / `CameraPull`，所以 `LinkShowBeats.cs` 里 `CoordRect` / `BoardSlideBeat` / `CameraPullBeat` / `OpponentRevealBeat` 四个类型定义保留（文件名未改），只删了 `LinkShowBeats` 静态类（Expansion / Splice / Settlements / 三条文案）；Godot 侧全部消费点已删。登记 HANDOFF 后续 change 第 4 项。
- **N3 Grep 零化的例外**：`Pairing|…|SeatVision|…` 在 `src/` 只剩四处字面量——`MatchFlow.RetiredLinkFields`、`MatchLog.RetiredConfigFields` / `RetiredEventFields` / `RetiredLinkEventTypes` / `RetiredLinkFields`、`RunConfig.RetiredKeys`、`CommandLine.RetiredOptions`（B2 要求读到旧字段 / 旧选项**点名**拒绝，字段名必须是字面量）；另 `HeuristicAi.cs` 一条注释"`SeatVision` 已删除"、`Program.cs` 一条注释"`--links` 随拼接删除"。扫描按**固定位置**查、不做全树递归（advisor 复核后收窄）：存档只看 `Link` 顶层键 + `Link.Settlements[*].Deadline` + `Link.Players[*].WaitingRounds`；日志只看首部 `Config` 顶层键、事件行 `Type` / 顶层载荷键 / `Settlement.Deadline`、快照 `PlayersState[*].WaitingRounds`——日后别处新增同名字段不会误伤。
- **N4 活用例数**：PRD 写"5 个文件里的 26 条活用例"，按测试用例数实为 **21 条**（阶段推进 3 + 端到端 Theory 4 行 + 存档 6 + 复盘 6 + 回放 2；check.md 的 26 = 14 恢复 + 12 新增，其中 5 条 K=0 实跑在 `决策序列基线Tests`、随 K 删除）。PRD 的"删后通过数 = 2032 − 0 + 新增"也对不上：除 140 条 Skip 外还有 **42 条活用例**只测被删功能（见 `tests-diff.md`「活用例随被删功能删除」：K0 / 全知比对 ×10、全知配置 / 视野开关 ×7、`图形版配对来源Tests` 整文件、`他场结算提示条队列Tests` 整文件 5 条、`他战场玩家数值不进HUD视图模型Tests` 剩下的 1 条、配对种子 / `--links` 参数校验、`非链接档不可增长`、Perf 全知耗时等），B1 / B4 的删除范围必然带走它们。
- **N5 日志 / 报告哈希重钉（格式变化，不是走法变化）**：快照 JSON 的 `PlayerEntry` 去掉 `WaitingRounds`、结算条目去掉 `Deadline`、首部去掉 K / 视野，报告删等待者 / 倒计时段——凡是哈希整段日志文本 / 报告文本的黄金值都变：`候选格上限Tests.LinkGoldenTurnHash`（8A03DA5E… → C898F792…）、`停手阈值Tests.StrictImprovementTurnHash`（D0EB62C1… → 8841BD6F…）、`平衡分析方向Tests` Sample / RankedSample（65989D85… / A2EA5792… → 8F6ED482… / 5CBE36F0…）、`专家前瞻的记录Tests` 一层 / 非专家（D8CE9E7E… / 旧值见文件 → 97B7E1EB… / 388B9A9E…）。走法不变的证据：5 份决策序列基线逐字节不变（只比决策行）、`专家难度的一层前瞻Tests` G1 黄金值（哈希决策行与前瞻记录，不含快照 JSON）未动即过、`专家前瞻的记录Tests` 的小回合 84 / 前瞻记录 21 两条断言未动即过。
- **N6 `LookaheadFixtures.LinkConfig` 原写死 `SeatVision = false`（全知）**：开关删除后局面集改为座位视野；G1 黄金值三条逐项相同即过——唯一战场上全知与座位视野同走法在专家前瞻上同样成立。
- **N7 §14.4 / §11 / §1 不在 PRD 章节清单**：与开放地图直接矛盾（"只画本机战场""随机配对、棋盘链接开战""行动顺序跨战场"），做了最小同步并记入变更记录。
- **N8 art README 里失效的截图命令**（未改 `art/`，属 2.6 之后）：`--shot-show=expand` / `field` 随拼接演出与战场横幅删除，`art/board-link-mode/README.md` 第 10 / 11 / 15 行（`f-splice-mid` / `f-splice-mid-mover` / `f-field-lost`）与 `art/link-map-terrain/README.md` 第 15 行（`splice-mid`）的命令已不可用（严格解析报错退出 1）。
- **N9 `Replayer.Replay` 直调守门**：原 `可复现回放_链接模式Tests.回放接口拒绝非链接日志`（直调 `Replayer.Replay(Mode = null / Legacy)`）随文件删除后，`Replayer` 入口的模式检查只剩 analyze 命令级的 `链接模式平衡分析Tests.旧日志被拒绝` 在守——已在 `可复现回放Tests` 补回同名 Theory 2 行（载体换成本类的真跑局日志）。

## 逐条证据

### 2.1 Core 删除（B1 / B2 / B4）

影响面先用 codegraph（`codegraph_explore` × 3：`Pairing.cs LinkStage MatchFlow.Link/Countdown`、`Persistence.Link LinkLayout ApplySplice RelicLedger.Translate ValidatePlateGaps LinkMapGenerator.Link/Sequences`、`SeatVision VisionFor RivalsInBattlefield IsWaitingForFinal`）圈出，再删：
- 删文件：`Match/Pairing.cs`、`Match/LinkStage.cs`、`Match/MatchFlow.Countdown.cs`、`Board/Maps/LinkLayout.cs`。
- `MatchFlow.Link.cs` → **改名 `MatchFlow.Settlement.cs`**：删 `_pairings` / `_links` / `PairingSource` / `LinkStage` / `PendingPairing` / `Pairings` / `Links` / `FirstRoundCandidates` / `ApplyPairing` ×2 / `AdvanceLinkPhase` / `FinalReady` / `EnterAwaiting` / `ResolvePairing` / `Validate` / `RequireCandidate` / `ExecuteLink`；保留 `LockedFlagCounts`（遥测：同区率与各棋盘被选次数的数据源）、`SettledBattlefields` / `Settlements` / `PassedThisRound` / `IsBattlefieldSettled` / `BattlefieldOf` / `ActivePlayersIn` / `OccupiedBattlefields` / `SettleAtRoundEnd`（去掉等待大回合计数与 `completed` 参数）/ `JudgeAtRoundEnd`（去掉条件 c 到期）/ `HasPlayableEmptyCell` / `OccupancyCounts` / `SettleByAttrition` / `LastBattlefieldSettled` / `SettleBattlefield` / `Judge` / `ExecuteSettlement`（去掉 `Deadline`）/ `BattlefieldVerdict` / `Debug*` / `SettlementChain`。
- `MatchFlow.Persistence.Link.cs` → **改名 `MatchFlow.Persistence.Settlement.cs`**：`LinkSaveData` 只剩 `PassedThisRound` / `LockedFlagCounts` / `Settlements`（无 `Deadline`）/ `SettledBattlefields` / `Players`（无 `WaitingRounds`）；段名 `Link` 与 `Mode = "Link"` 常量不变（B2 不加存档格式版本号）；恢复顺序改为 `LoadState → Relics.RestoreState` 直通。**B2 拒绝**：`RejectRetiredLinkFields` 在 `Parse` 最前（先于地图摘要比对）递归扫 `Link` 段，命中 `RetiredLinkFields`（FirstPairingStepDone / FinalLinked / PendingPairing / Pairings / Links / FinalCountdownRounds / CountdownStart / Deadlines / Countdowns / WaitingRounds / Deadline）即抛 `FormatException`，文案以 `MatchFlow.RetiredLinkFieldMessage` = "拼接规则已移除：该存档属于改动前的链接模式" 开头并点名字段。
- `MatchFlow.cs`：删 `RequireValidFinalCountdown` 调用、`WaitingRounds`（`StateOf` / `PlayerRecord`）、`EndMajorRound` 末尾的 `AdvanceLinkPhase` / `UpdateFinalCountdown`；`MatchFlow.Persistence.cs` 删 K 回填 / 校验 / `FinalCountdownBackfilled`。
- `MatchPhases.cs`：删 `MatchPhase.AwaitingPairing` / `AwaitingFinalLink`、`FlowEventKind.PairingApplied` / `BattlefieldsLinked` / `FinalCountdown`；`BattlefieldSettlement.cs`：删 `SettlementReason.Expired`、`BattlefieldSettlementRecord.Deadline`；`PlayerFlowState.WaitingRounds`；`MatchOptions.FinalCountdownRounds` / `DefaultFinalCountdownRounds`；`MatchRunner.RunToEnd` 的待配对分支；`MatchDebugAccess.Link`；`SeatPublicView.Links` / `SeatLinkView`。
- `LinkMapGenerator.cs`：删 `LinkSide` / `BattlefieldShift` / `LinkResult` / `LinkSequenceKind` / `LinkStep` / `LinkSequence` / `Sequences` / `Link` / `PlateShifts` / `MoveByPlate` / `SideName` / `Extent` / `ComputeSequences` / `Combinations` / `Gap`，`Assemble` 固定 `MapVersion = 0`（类注释改写）。`InitialLayout` 是唯一的布局几何实现。
- `GameBoard.cs`：删 `ApplySplice` / `SpliceTargets` / `MoveEdit` / `LinkedMap`（构造与 `Clone` 收窄）；`RelicLedger.Translate` / `RelicState.MovedTo` 删；`MapValidator.ValidatePlateGaps` 与 `PlateRules.MinGap` 删（校验器链接行只剩分量 = 1 / 接缝气边 / 不重叠 / 可落子格都属棋盘）；`MapData.MapVersion` 字段保留（恒 0，注释改写）。
- AI：`AiSearchConfig.SeatVision` 删；`HeuristicAi.VisionFor` 恒给按座位投影 + 预演投影（`config` 参数保留签名、不再读）。
- 注释同步：`SeatPublicView` / `RelicLedger` / `MatchFlow.Preview` / `BatchPreview` / `MapValidator` / `TerrainEditRules` / `MatchDebugView` / `InitialLayout` / `MapProfile` / `MapFile` 里"拼接后 / 随链接平移 / 链接追加"的口径改掉。
- 验证：`dotnet build src/Siege.Core -c Release` 0 警告 0 错误；Grep 见「Grep 清零」。

### 2.2 Sim 删除（B2 / B5 / B6）

- 删文件：`Running/PairingSources.cs`、`Analysis/FinalCountdownAnalyzer.cs`、`Logging/LinkLogLayout.cs`。
- `RunConfig`：删 `PairingSeed` / `FinalCountdownRounds` / `SeatVision`（含校验、`ResolvedFor` 落成、`RejectSearchVision`）；三者加进 `RetiredKeys`（文案"拼接规则已移除（open-map 裁决 B1…）"）——`run --config` 读到即报错退出。`CommandLine.RetiredOptions` 加 `pairing-seed` / `final-countdown` / `links`（严格命令行报"选项 --x 已删除：拼接规则已移除…"）。
- `MatchLog`：删 `FinalCountdownRounds` / `RecordedPairings` / `LogEvent.Pairing` / `Link` / `Countdown` / `PairingLogEntry` / `LinkLogEntry` / `BattlefieldShiftEntry` / `FinalCountdownLogEntry` / `PlayerEntry.WaitingRounds` / `SettlementLogEntry.Deadline` / `LogEventType.PairingApplied` / `BattlefieldsLinked` / `FinalCountdown`。**B2 拒绝**：`Parse` 对每一行递归扫 `RetiredLinkFields`（PairingSeed / FinalCountdownRounds / SeatVision / Pairing / Link / Countdown / WaitingRounds / Deadline）与事件 `Type ∈ RetiredLinkEventTypes`，命中即 `FormatException`，文案 `MatchLog.RetiredLinkMessage` = "拼接规则已移除：该日志属于改动前的链接模式"（先于首部摘要比对；`replay` / `analyze` 共用这一处）。
- `MatchSession`（Sim）：删 `SeatVision` 属性与 K 一致性校验、配对来源参数、`_pairingCursor` / `_linkCursor` / `_countdownCursor`、`RecordTurn` 的链接平移（逐格比对同一坐标）、`LifeEntry` 的 `moved` / `back` 重载、配对 / 链接 / 倒计时三个事件分支、快照 `WaitingRounds`、结算 `Deadline`。`LogReplay` / `Replayer` / `DefeatReplay` 删 `RecordedPairingSource`。
- `Program.cs`：用法文本删 `--pairing-seed` / `--final-countdown` / `map --links` / 全知说明；`play` 删 `finalCountdown`；`map` 删 `--links` 与 `SimulateLinks`，输出加 **`连通分量 {Battlefields.Of(map).All.Length}（…应为 1）`**（实算，B5）；`run` 删两项配置。
- `PlayCommand`：删 `pairingSource` / `finalCountdown` 参数、配对 / 链接播报、倒计时说明、`MovedSince` 与 `Summarize` 的 `moved`，开场说明改为开放地图流程；`BoardRenderer` 删"其他战场（势力不可见）"分支与 `SameBattlefield`。
- 分析：`LinkModeAnalyzer` / `LinkModeSection` 重写——只剩同区率、开局布局、**各出生棋盘被选次数 `BoardPicks`**、对局大回合数（均值 / 中位 / 最小 / 最大）、终局原因、出局原因、过早 Pass 落败、耗时；`PassReviewAnalyzer.Waiters` 与 `FinalistGroup` / `WaiterOrigin` / `FinalistEntry` / `WaiterSection` 删；`BalanceAnalyzer` 删 `Waiters` / `FinalCountdown`，信物坐标不再按版本换算（`RelicTypes` 直接用首部）；`ReportWriter` 删 `AppendWaiters` / `AppendFinalCountdown`，链接模式段重写，`AppendThemePerformance` 改**整局胜率**（B6）；`ThemeAnalyzer` 删 `FirstField*` / `NoFirstField` / `FirstField` / `EliminatedBeforeFinal` / `FinalSeq`，改造与公共信物按首部棋盘清单归棋盘（本地 `ZoneAt`）。
- 验证：`dotnet build src/Siege.Sim -c Release` 0 警告；测试 `各入口支持生成图_链接模式Tests.map子命令打印链接初始图`（含"连通分量 1"）、`配对与链接相关选项已删除` ×4、`批量跑局Tests.配置文件里的已删除配置项被拒绝` 新增 3 行、`可复现回放Tests.旧拼接日志被拒` ×7（首部 K / 配对种子 / 视野、事件配对 / 链接 / 倒计时、快照等待大回合数；`MatchLog.Parse` + `replay` + `analyze` 三处都拒、反面未注入照常回放一致）、`AI改造与主题诊断Tests.主题表现可查` 恢复（A / C 两局整局胜 2 / 有名次 8、T 截断只计人次、报告含"整局胜"不含"首轮战场"）、`随机子流隔离_链接模式Tests.配对来源已随拼接规则删除`（Sim 源码无 `PairingSource` / `pairing-seed`，`PairingSeed` 只在两张拒绝表）。

### 2.3 Presentation / Godot 删除

- 删文件：`Presentation/Pairing/RandomPairingSource.cs`、`Show/FinalCountdownNotices.cs`、`Show/NoticeQueue.cs`（两个生产者——他场结算提示条与倒计时提示——都删了，队列无人入队，连同 Godot `_strips` / `AdvanceStrips` / `Hud.SetEventStrip` / `_eventStrip` 节点一起删）。
- `LinkShowBeats.cs`：删 `LinkShowBeats` 静态类（见 N2）；`LinkDemoAudit` 删 `Pairing` / `Linked` / `Settled` 三个死方法；`Worlds.ViewerWorld` 删 `IsConcealed` / `ConcealedStatusText`，`Layer` / `HandPanel` 不再遮蔽；`LayerContents` 删 `PlayerPowerRowView.Concealed` / `ConcealedText` / `Conceal`、`OrderRowView.Concealed` / `ConcealedExplanation` / `Conceal`；`HandInfoPanel.OpponentHandAreaView.Concealed` / `Conceal`；`SettlementBeats.Generate` 删 `moved` 参数与 `GenerateMoved`；`BoardCamera.FollowSlide` 删；`MapPreviewInfo` / `CameraInput` / `DefaultBoardView` 文案与注释改为开放地图。
- Godot：`GameRoot` 删配对来源字段与 `ScriptedPairingSource` / `RandomPairingSource` 接线、配对 / 链接 / 结算三段自检喂数、`_pickCheckAfterLink` / `_pickCheckPending` 第二轮拾取、`ApplySlideCamera` / `_slideStart` / `ApplyCameraPull` / `_pullStart`、`[field]` 横幅打印、`--shot-show=expand|field`（合法值表与报错文案同步）；`MatchSession` 删 `WithLinkBeats` / `CountdownNotice` / `Layout` / `MovedSince` / `_matesBefore` / `TakeNotices` / `LastSettlementShow` / `MyBattlefield` / `pairing` 参数，三条结算路径直接 `SettlementBeats.Generate`；`BoardView` 删 `ApplySlide` / `MirrorOf` / `ReturnFromMirror` / `CellOf` / `_slideRoot` / `_slideMirror` / `_slideCells` / `_tileNodes` / `RegisterTiles` / `_plateSource` / `_platesMerged` / `_plateRoot` / `SlideCellMeta`、`AddBoardPlates` 的 `merge` 参数、`ClearLayer` 镜像清理；`Hud` 删遮蔽行分支与 `_eventStrip`，插旗说明与新局界面文案改为开放地图。
- 验证：`dotnet build src/godot/Siege.Godot.csproj`（Debug）0 警告 0 错误；四条 headless 自检退出码见「Godot 自检」。**截图基线帧不变的证据**：四条自检的 `[flow]` 帧号与段 A 记录逐个相同——插旗锁定第 0 帧（218 / 218）、构筑期第 2 / 17 / 30 帧（42 格 ×3）、全图解禁第 43 帧（214 = 214 − 0）、`--rounds=0 --seed=5` 终局第 289 帧 / 第 26 大回合 `LastBattlefieldSettled`（段 A：289 帧、第 26 大回合）；`--map-select` 新局界面五步 + 锁定 258 / 258、构筑期 43 / 48、全图解禁 250 = 250 − 0 与段 A 相同（段 A 记 238 / 238、47 / 48、225 = 230 − 5 是另一随机种子）。自检行数 8 → 7：段 A 的终局局多一条「战场结算」行（`Settled` 喂数已删）。非 headless 重截 `art/open-map/*.png` 本次未做。

### 2.4 测试（B3 / B4）

- 删前清单 `tests-before.txt`（`dotnet test --list-tests`，2174 个唯一名）；删后 `tests-after.txt`（2018）；逐名对账 `tests-diff.md`：删除 204 条 = Skip 清单 139（`skip-list.txt` 140 条中 `AI改造与主题诊断Tests.主题表现可查` 按 B6 恢复、不删）+ 搬迁旧名 21 + 活用例随被删功能删除 42 + Theory 改 Fact 2（`构筑期候选不越棋盘_第4大回合候选可全图(seatVision)` 两行并成一条）；新增 48 条。
- 整文件删除 20 个（见 `deleted-tests.txt`），按方法删 52 个（跨 23 个文件）。
- 搬迁：`阶段推进Tests` 3 条 + `链接模式端到端Tests.从插旗跑到终局_开放地图`（Theory 4 行，含 `EarlyPlacer`）→ `Battlefield/开放地图阶段推进Tests.cs`；`链接模式存档Tests` 6 条 → `MatchSetup/对局存档Tests.cs`（`AssertSameState` 去掉 `LinkStage` / `Pairings` / `Links` / `PendingPairing` / `WaitingRounds` 比对，新增 B2 守门 `含拼接字段的旧存档被拒` Theory 9 行 + 嵌套 `WaitingRounds`）；`战场落败复盘与等待者评估Tests` 6 条 → `MatchTelemetry/战场落败复盘Tests.cs`；`可复现回放_链接模式Tests` 的两条活用例：`回放接口拒绝非链接日志` 以同名 Theory（2 行，载体换成本类真跑局）并入 `可复现回放Tests`；`replay命令拒绝旧日志`（命令级、非 `link:` 地图标识）随文件删除——同一拒绝由 `链接模式平衡分析Tests.旧日志被拒绝`（analyze 命令级）与新增的 `可复现回放Tests.旧拼接日志被拒`（7 行：`MatchLog.Parse` + `replay` + `analyze` 三处）覆盖。**偏离 B3 字面**："回放两条并入"实际是"1 条并入 + 1 条删除 + 新增 7 行"，请质检核对。
- 基线：删 `decisions-link1-{seed1,seed2,seed3,easy-seed1,hard-seed1}-{omniscient,k0}.txt` 10 份与 `全知与K0副本与缺省逐字节相同` / `K为0实跑与缺省基线逐字节相同` / `全知配置下与改前相同` 三条比对用例；5 份缺省基线 `git diff --stat -- tests/Siege.Core.Tests/AiDecision/Fixtures` 只有 10 个 D、无 M，`决策序列基线Tests` 2 条 + `决策序列基线_链接模式Tests` 1 条 + 其他难度 2 条全过。
- 夹具：`LinkModeFixtures` 删 `CountdownOptions` / `Started` 的 `source` 参数 / `Track` / `Pairs`，`Options` = `Immediate`；`LinkMapFixtures` 删 `IsolatedBirths` / `Isolated` / `Linked01` / `Build(mapVersion)` / `Gap`；`SimFixtures.PinPreCalibration` / `LookaheadFixtures.LinkConfig` 删 K 与视野，`ProbePositions` 去掉视野重载；`LinkSimFixtures.PlayScripted` 去掉配对来源，`TwoLinkLog` → `Round4Log`。
- 新增守门：`正式对战AI的信息边界_链接模式Tests.全知开关已删除`（源码扫描：Core/Ai 只剩 HeuristicAi.cs 一条"已删除"注释提到 `SeatVision`，Sim / Presentation / godot 不出现）、`全图可读`（原「全知例外集中可关」改写）、`棋盘台面的可视表现Tests` 改为断言 BoardView 无 `ApplySlide`。
- 全量（单进程，`--blame-hang 10m` 未触发）：第一次 2023 / 1988 / 6 / **29 失败**（全是文案与哈希：拒绝文案缺"拼接规则已移除"字面、`首轮战场` 文案、`"## 链接模式（board-link-mode）"` 标题、N5 的 6 个哈希、`FinalCountdownRounds` 首部期望、`ApplySlide` 源码扫描、终端开场文案、`PairingSeed` 字面扫描）→ 修正后第二次 2023 / 2017 / 6 / 0；再经 advisor 复核收窄扫描（N3）、补 `Replayer` 直调守门（N9）、`CommandLine.RetiredOptions` 后**最终：总 2025 / 通过 2019 / 跳过 6 / 失败 0，RC = 0，4 m 15 s**；跳过 6 条全是既有 Perf / Slow（段 A 的 7 条里"全知专家耗时"一条随全知删除）。删后清单 `tests-after.txt` 2020 个唯一名（Theory 同名行折叠），`tests-diff.md`：删 204 / 增 50。
- 变异（本段只删不增规则，守门变异按 testing.md 对**新增**守门做）：
  - M-B2a `MatchFlow.RejectRetiredLinkFields` 改为不扫描（`return;`）→ `对局存档Tests.含拼接字段的旧存档被拒` 9 行红（报"地图不一致"而非"拼接规则已移除"）。
  - M-B2b `MatchLog.Parse` 去掉 `FindRetired` 调用 → `可复现回放Tests.旧拼接日志被拒` 7 行红。
  - M-B5 `Program.ExportMap` 把连通分量行写死 `1` → 无用例可分（唯一战场上实算也是 1；本行只是 Scenario 的输出要求）。记为不可变异验证。

### 2.5 文档（B7–B9）

- 设计文档 v1.27 → **v1.28**：文首版本 / 适用范围 / §1 一句话；§3.3 改为"建局紧贴连通 + 3+2 逐块锚定 + 换锚规则 R-1"，删"链接 = 拼接"整段（改为"拼接（已删除）"一段）；§4 阶段只剩四段、§4.1 锁定后全展开、§4.2 两段范围 + 改造范围 + O7，删 §4.3；§11 行动顺序"跨战场统一"→"全员统一"；§12.2 删待配对阶段不可弃赛、§12.3 唯一战场三条、§12.4 只剩两类终局、§12.5 例子改为唯一战场；§13 折成"对全部玩家公开 / 全局公开 / 插旗阶段与数据层边界"，AI 座位视野 = 全图、全知开关删除；§14.4 按整图渲染、删拼接演出 / 战场横幅 / 他战场遮蔽；§15.1 / §15.2 名册 = 全部在局玩家、C15 口径；§17 开局与结算记录 + 旧拼接日志拒绝，重点分析第 12 项改写；§19 第 1 / 11 / 14 项同步，新登记 **16 R-3 AI 循环提子不收尾**、**17 3+2 布局公平**；§21.3"决赛胜者"→"胜者"；变更记录追加一行（O1–O8、B1–B10、删除范围、测试增减）。历史变更记录旧行未改。
- `HANDOFF.md` 整体改写：一句话 / 怎么玩 / 命令行选项（删 `--pairing-seed` / `--final-countdown` / `--links` / 全知，`--shot-show` 去 expand / field）/ 规则速览 v1.28 / 自检四步（含 Godot 读 Debug 程序集的提醒）/ 测试现状（基线只剩 5 份）/ 旧地图与旧规则（已删除）/ 后续 change（R-3、对局长度、布局公平、节拍类型残留、基线首部文案、其余沿用）/ 规范要点同步。Grep `配对|拼接|决赛|倒计时|等待者` 在 HANDOFF 与 `.trellis/spec/` 只剩"已删除 / 历史"引用。
- `.trellis/spec/core/boundaries.md`：第 27 行（拼接调用方）、第 48 行（初始布局 / `InitialLayout` 唯一几何、拼接已删）、第 49 行决赛倒计时改为**已删除**条目、第 50 行"缺省值 / 回填值"原则保留、例子换成带入带出并补"已删除配置项读到即拒绝"、第 51 行 AI 视野改为"开关已删除、`VisionFor` 恒按座位投影"。段 A notes C1 提到的 `BoardPlate` / `BatchContext` 注释：`BoardPlate` 注释未改（仍写"不参与结算"——与 boundaries 第 48 行同口径，构筑期范围读清单一事在 48 行已写明），未另动。

## Grep 清零

- `src/`（`Pairing|PairingKind|LinkRecord|LinkStage|FinalCountdown|Countdown|Waiter|WaitingRounds|ApplySplice|SeatVision|pairing-seed|--links`）：只剩 N3 列出的字面量拒绝表（3 处）与 2 条"已删除"注释；`src/godot/scripts/` 为零。
- `tests/`：同 token 只出现在新增的拒绝守门用例（字段名字符串）与"已删除"注释里。
- `Battlefield` / `LastBattlefieldSettled` / 按座位投影 `PublishSeatProjection` / `SeatProjection` 全部保留（B1）。

## Godot 自检（headless、顺序单跑，`..._console.exe`，Debug 程序集）

| 命令 | 退出码 | 关键行 |
|---|---|---|
| `--auto-demo` | 0 | 锁定 218 / 218（5 块全展开）→ 构筑期 42 格 ×3（棋盘之外 0）→ 第 43 帧全图解禁 214 = 214 − 0；自检 5 条 0 失败；开局对准自证通过 |
| `--auto-demo --pick-check --seed=5` | 0 | 锁定后全图一轮拾取 214 / 214（最近 / 最远 / 五个位姿全部往返一致）即退出 |
| `--map-select --auto-demo` | 0 | 新局界面五步（非法种子 / 12345 / 换一张 / 回到进入时 / 开始：建局后节点 174 = 169 + 主题名 5）→ 锁定 258 / 258 → 构筑期 43 / 48 ×3 → 解禁 250 = 250 − 0；5 条 0 失败 |
| `--auto-demo --rounds=0 --seed=5` | 0 | 第 289 帧、第 26 大回合 `LastBattlefieldSettled` 终局、名次 4 人；完整性四步齐全；自检 7 条 0 失败 |

日志：scratchpad `godot_*.txt`（未入库）。

## 变异实跑（`cp` 备份 → 改 → 只跑目标类 → `cp` 还原 → `cmp` 逐字节校验 → `touch`）

- **M-B2a** `MatchFlow.RejectRetiredLinkFields` 开头加 `if (json.Length >= 0) { return; }`（第一次用裸 `return;` 触发 CS0162 警告即错误、没编过去，换成可编译变体重做）→ `对局存档Tests.含拼接字段的旧存档被拒` **9 行红**、其余 6 条绿；还原 `cmp` 一致。
- **M-B2b** `MatchLog.Parse` 的 `FindRetired` 调用改为 `false && …` → `可复现回放Tests.旧拼接日志被拒` **7 行红**；还原 `cmp` 一致。
- 真旧日志实证（B2）：段 A 冒烟 `sim-out/open-map/smoke/link1/match-0000000000000001.jsonl`（首部含 `FinalCountdownRounds` / `SeatVision`）—— `replay --file` 与 `analyze --dir` 都报 `拼接规则已移除：该日志属于改动前的链接模式（header.Config.FinalCountdownRounds：…）`、退出码 1。
- `map --map link:1` 实跑输出 `连通分量 1（5 块出生棋盘紧贴成一张连通地图，应为 1）` + `地图校验通过。`（B5）。

## 全量复跑（最终）

- `dotnet build siege.sln -c Release`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`（单进程）：**总 2025 / 通过 2019 / 跳过 6 / 失败 0，RC = 0，4 m 15 s**（该次跑在 §4.2 的 O6 文案修正与最后一次行尾归一之前，二者不触碰任何 .cs 语义）。跳过：`活形分析性能基线Tests.中盘…三倍`、`活形保护性质Tests.…种子1至200`、`单子禁手的标示Tests.与完整预演一致_棋盘图中盘` / `单次查询耗时`、`活形分析的决策内缓存Tests.缓存开关不改变决策序列_链接图种子1至20`、`专家前瞻耗时计时Tests.按座位视野下…四倍`（全是 Perf / Slow）。
- 5 份缺省决策序列基线 `git diff --quiet HEAD -- …decisions-link1-{seed1,seed2,seed3,easy-seed1,hard-seed1}.txt` 全部一致（逐字节不变）；`AiDecision/Fixtures` 只有 10 个 `D`、无 `M`。
- 行尾：本次改动的 `.cs` / `.md` 已归一为 CRLF（与工作区既有文件一致；索引侧 autocrlf 归一为 LF）。
