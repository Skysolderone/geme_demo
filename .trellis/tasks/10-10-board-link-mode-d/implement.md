# 10-10-board-link-mode-d 段 D-a 实施报告（收口与数据层：tasks 4.0 / 4.0b / 4.1）

对应 `openspec/changes/board-link-mode/tasks.md` 4.0、4.0b、4.1（已勾上）。D-b / D-c / D-d 未动。未 `git commit`。

## 1. 完成情况与测试类（`tests/Siege.Core.Tests/`）

| 项 | 状态 | 测试类 |
|---|---|---|
| D7 收口：`MapCatalog.Resolve` 只认 `link[:<种子>]`、`DefaultId = link:1`、旧标识只经 `MapCatalog.Legacy`（新增 `Legacy.DefaultId` = v5）、两处"旧标识转交"路径改走 Legacy、旧存档拒绝 | 完成 | `MapDefinition/旧地图降级为测试夹具Tests`（夹具仍可加载、玩家入口不可达〔含 Godot 源码扫描〕）；`MapGeneration/既有生成器降级为测试夹具Tests`（夹具生成不变、玩家入口拒绝旧标识 ×6）；`MatchSetup/链接模式存档Tests.旧存档被拒绝`；`LinkMapGeneration/链接档地图标识与确定性Tests.目录解析link标识`（改为断言拒绝） |
| P7 整理（4.0b） | 完成，零行为改动 | 不新增测试；证据见 §5 |
| S10 先手预测（4.0） | 完成 | `InitiativeOrder/先手顺序预测_链接模式Tests`（质检局面_P0P1已Pass_预测为P2P0P3、预测与大回合结束后的实际顺序一致、未行动者按未Pass算、填满判定同样进预测、此刻结束即终局时没有预测、旧模式预测不变）；`Battlefield/链接后的公开补充载荷Tests`（修复实测缺陷，见 §7 D10） |
| 新局界面与图形版建局切 Link（4.1） | 完成（占位样式待 D-b） | `MapSelection/选图视图模型Tests`（重写：缺省进入新局界面、换一张、非法种子 ×12、输入种子复现、按标识预填只认 link:、生成失败回滚、确认后开局、预览说明、FriendlySeed）；`MapSelection/选图界面守门Tests`（改为"无清单、只拼 link:"）；`Pairing/图形版配对来源Tests`（首轮配对合法、真随机、决赛唯一、无人值守固定脚本） |

## 2. 新增 / 修改文件

新增：
- `src/Siege.Core/Board/GameBoard.MapTables.cs`（P7：`MapTables` 嵌套类原样搬出，`GameBoard` 改 partial）
- `src/Siege.Presentation/Pairing/RandomPairingSource.cs`（D3：图形版有人值守真随机来源，`RandomNumberGenerator.GetInt32` + Core `PairingDraw`；Presentation 不引用 Sim）
- 测试 5 个：上表所列 4 个新类 + `Battlefield/链接后的公开补充载荷Tests.cs`

修改（Core）：
- `Board/Maps/MapCatalog.cs`：`Resolve` 只认 `link:`，其余抛 `FormatException`（文案含"只接受 link:<地图种子>"与"测试夹具"）；`DefaultId = "link:1"`；`Legacy.DefaultId = FourPlayerBaseMap.Id`，`Legacy.Resolve(null)` → v5。
- `Board/GameBoard.cs`：partial；`CoordAt(width, index)` 成为格下标→坐标的唯一算式，`internal static CoordOfIndex(int width, int index)`；删死 `using System.Runtime.InteropServices`。
- `Scoring/CoverageMap.cs`：三处 `new Coord(i % w, i / w)`（`Compute` 两处、`ExclusiveCellsOf` 一处）改走 `GameBoard.CoordOfIndex`。
- `Match/MatchFlow.Link.cs`：战场结算拆为 `JudgeAtRoundEnd`（判定，无副作用）→ `Judge`（唯一比较链）→ `ExecuteSettlement`（执行）；`SettleAtRoundEnd` 先全判再逐执行；`SettleBattlefield` 退化为 Judge + Execute 的薄包装（减员结算 / 测试接缝用）；新增私有 `BattlefieldVerdict` 记录。
- `Match/MatchFlow.Preview.cs`：`ForecastInitiative` 在 Link 下复用 `JudgeAtRoundEnd(power, _passedThisRound)`，剔除落败者、按剔除后的名册重算势力名次；此刻结束会"最后战场结算"终局时返回 `null`；账本副本改经 `Relics.Copy()`（两处）。
- `Match/MatchFlow.Persistence.cs`：`Restore` 拒绝非 `link:` 存档（"该存档属于已移除的地图模式…"）；新增 `internal RestoreLegacy`（校验与重建相同，不拒旧地图）供 Legacy 夹具。
- `Match/Pairing.cs`：`PairingSourceNames`（random / pairing-seed）与 `PairingDraw`（洗牌两两相配、奇数末位等待、决赛取唯一结果）从 Sim 移入 Core。
- `Relics/RelicLedger.cs`：新增 `Copy()`（生成记录 + `LinkedPlacements` + 揭示记录，与链接模式恢复同序）。
- `Board/Maps/FourPlayerBaseMap.cs`：注释。

修改（Sim）：
- `Running/MatchSession.cs`、`Running/BatchRunner.cs`、`Running/Replayer.cs`：新增可选参数 `Func<string, MapData>? resolveMap`（缺省 `MapCatalog.Resolve`），Legacy 夹具测试显式传 `MapCatalog.Legacy.Resolve`；源码仍保留 `MapCatalog.Resolve(` 字面量（"三个入口共用目录"守门）。
- `Running/PairingSources.cs`：删本地 `PairingSourceNames` / `PairingDraw`，改用 Core 的；`RandomNumberGenerator` 仍恰在 `RandomPairingSource` 一处。
- `Config/RunConfig.cs`：`MapId` 内部缺省 → `MapCatalog.Legacy.DefaultId`（v5，C10）。
- `Play/PlayCommand.cs`：地图行"缺省不多打一行"的比较对象改为 `Legacy.DefaultId`（Legacy 转录逐字不变；`link:1` 照常打印完整标识）。
- `Program.cs`：`map` 子命令缺省 `requested` 改读 `Legacy.DefaultId`（C10）；注释更新。

修改（Presentation / Godot）：
- `Siege.Presentation/MapSelect/MapSelectModel.cs`：重写为只有 `link:` 一种——`MapSeed / CurrentId / SeedText / Notice / IsConfirmed / Difficulty`、`SubmitSeed`（口径走 `LinkMapId.Parse`）、`Reroll`、`TrySelectId`（只认完整 `link:`）、`Accept / RollBack / Confirm`；删 `MapOption` / `MapOptionKind` / 清单 / 平台数 / 棋盘数。`MapPreviewInfo` 未改。
- `godot/scripts/GameRoot.MapSelect.cs`：新局界面（预填只认 `link:`，错误文案更新）；自检步骤改为"非法种子 abc → 种子 12345 → 换一张 → 回到进入时种子 → 开始"（节点不泄漏核对保留）；预览 / 开始建局都挂 `_pairing`。
- `godot/scripts/Hud.MapSelect.cs`：面板重做——标题"开始新局"、一句模式说明、种子输入 + 采用 + 换一张、完整标识、说明、提示、AI 难度、开始；删清单与平台数 / 棋盘数行。
- `godot/scripts/GameRoot.cs`：`--map=` 只接受 `link:`（旧标识由 `MapCatalog.Resolve` 报错 → 退出码 1）；裸 `link` 取种子（`GeneratedMapId.FriendlySeed(Stopwatch)` 同一折叠函数），删裸 gen / 裸 board；无人值守未给 `--map=` 取 `link:1`；新增字段 `_pairing`（`Unattended ? new ScriptedPairingSource() : new RandomPairingSource()`）并在建局处传入；无人值守下 `_Process` 推进对局抛异常 → 打印并退出码 1（D11）；`--pick-check` 中心位姿在"地图中心不是可落子格"时记无可验（D12）；截图自证行改读 `MapSeed`。
- `godot/scripts/GameRoot.Carry.cs`：两处建局传 `pairing: _pairing`。
- `godot/scripts/MatchSession.cs`：`Create(..., IPairingSource? pairing = null)`：`link:` 地图以 `Mode = Link` 建局并挂来源（缺来源即抛）；旧地图仍 Legacy。`ChooseZone` 注释更新（盲选、`ZoneOwners` 同区只记首位）。
- `godot/scripts/Names.cs`：`End` 增加 `LastBattlefieldSettled => "最后一个战场已结算"`。

其他：`openspec/changes/board-link-mode/tasks.md` 4.0 / 4.0b / 4.1 勾上。

## 3. 构建、测试、Godot 自检

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`（最终全量，变异还原后重建）：总计 2333，通过 2322，跳过 11（既有），失败 0，1 m 44 s。中途三轮：D7 刚收口未改测试时 153 红（全部是 Legacy 夹具经 Sim 入口解析 v5 / 旧存档 / 缺省地图断言，见 §4）；改完 6 红 → 1 红（`生成图上的拾取自检口径Tests`，D12 的守门镜像）→ 0。
- Godot（最终 Debug 构建上三条都重跑过；`Godot_v4.7.2-stable_mono_win64_console.exe --headless --path src/godot -- …`）：
  - `--auto-demo`：退出码 0。`[siege] 地图 link:1，对局种子 20260915`；`[auto-demo] 演示停止：跑满 4 个大回合（对局停在第 5 大回合）`；`[camera] 开局对准自检：出生平台 25 格，整格在画面内 25；位姿变化 0 次；通过`；23 帧（首跑因 D10 的异常被引擎吞掉是 20 帧，修后 23 帧），无 ERROR。
  - `--auto-demo --pick-check`：退出码 0。逐格居中 179/179（最近、最远）；`位姿「最近」注视点 (0, 0)：画面内可落子格 0 …（地图中心是场景格，该位姿视野内没有可落子格，无可验）`；`可落子格 179，至少在一个位姿下验到 179；通过`。
  - `--map-select --auto-demo --rounds=2`：退出码 0。规格列的是四步（非法种子、合法种子、换一张、开始），自检多走一步"回到进入时的种子"——为了"开始"那一步按进入时同一张图核对棋盘节点数 / 游离节点数不泄漏（旧自检同样有"回到进入时的那一项"）。五步全部"通过"：`输入非法种子 abc`（标识不变、提示非空）、`输入种子 12345`（`link:12345`，5 块棋盘）、`换一张`、`回到进入时的种子`、`开始（棋盘节点 1187 / 进入时 1187，游离节点 0 / 进入时 0）`；随后演示跑满 2 个大回合，开局对准自检通过。
  - 附加：`--auto-demo --rounds=0` 跑到终局：退出码 0，`终局：第 93 大回合，最后一个战场已结算；第1名 蓝方(P1) 势力 115185624 …`，240 帧，142 s，无 ERROR（插旗 → 构筑 → 脚本配对 → 链接 → 战场结算 → 决赛 → 终局全流程走通）。
  - 附加：`--map=siege-4p-base-v5` / `--map=board:12345` / `--map=gen:12345` 各退出码 1，`[siege] 错误：地图标识 … 不受支持：玩家入口只接受 link:<地图种子>…`；`--map-select --map=siege-4p-base-v5 --auto-demo` 退出码 1（预填只认 link:）；`--map=link --auto-demo --rounds=1` 退出码 0 并打印随机取到的种子。

## 4. D7 收口直接导致的既有测试改动（逐条）

注释一律写"board-link-mode 裁决 D7：…"。

1. **Legacy 夹具经 Sim 入口解析地图**（旧标识可达 → 显式经 Legacy）：`MatchSession.Create` / `BatchRunner.Execute` / `BatchRunner.ExecuteToDirectory` / `Replayer.Replay` / `Replayer.ReplayFile` 共 165 处（41 个文件，脚本批量加 `resolveMap: MapCatalog.Legacy.Resolve`，行尾加说明注释）+ 手改 5 处多行 / 混合文件（`可复现回放_链接模式Tests` ×2、`对局日志的记录内容_链接模式Tests`、`链接模式平衡分析Tests`、`各入口按地图标识选图_链接模式Tests`、`启发式评价维度Tests` ×2）；`PlayCommand.Run` 未传地图的 9 处加 `map: MapCatalog.Legacy.Resolve(null)`（`本地玩家档案Tests`、`终端的带入选择弃赛与结算显示Tests` ×2、`终端对局Tests` ×2、`终端活形与禁入标示Tests`、`终端专家对局Tests`、`各入口按地图标识选图Tests` ×2）。断言一条未改。
2. **段 C 遗留的两处转交路径**：`各入口支持生成图Tests.以裸gen批量跑局…` 的 `RunLegacyToDirectory` 经 `BatchRunner.Execute(..., resolveMap: Legacy)` 逐局解析 `gen:101:s1`；`棋盘档生成图标识Tests.裸board不入记录` 的 `Replayer.Replay(..., resolveMap: Legacy)`。
3. **缺省地图 v5 → link:1**：`各入口按地图标识选图Tests.缺省地图不变`（`DefaultId == "link:1"`、`Legacy.DefaultId == v5`）与 `.参赛人数缺省取地图人数上限`（`RunLegacy(Legacy.DefaultId)`）；`两人基准地图Tests`（`Legacy.DefaultId == BuiltinIds[0]`）；`棋盘档生成图标识Tests`（`FourPlayerBaseMap.Id == Legacy.DefaultId`）；`Legacy.Resolve(MapCatalog.DefaultId)` → `Legacy.Resolve(Legacy.DefaultId)`（`AI带入Tests`、`对局内容集Tests` ×2、`带入带出配置Tests`、`初始配置与基础棋池Tests`）。
4. **旧标识转交断言**：`链接档地图标识与确定性Tests.目录解析link标识` 的"Resolve 与 Legacy 逐字节相同"循环改为"Resolve 抛 FormatException 且文案说明只接受 link:"，并保留 Legacy 反面。
5. **旧存档门**：`百局端到端Tests`、`对局内容集Tests`、`对局配置公开完整地图标识Tests` 的 `MatchFlow.Restore(` → `MatchFlow.RestoreLegacy(`（文件头一行说明；断言不变）。
6. **选图清单删除**：`选图视图模型Tests` 整体重写（旧 Scenario 全部针对清单 / gen / board）；`选图界面守门Tests.选图界面不自带地图清单`（"选项只在视图模型里构造、取自 BuiltinMaps、BoardMapId/GeneratedMapId.Format"→"任何选图源码不得出现 new MapOption( / MapCatalog.Builtin；模型含 LinkMapId.Format(、不手拼 "link:"、不再出现 BoardMapId / GeneratedMapId.Format|Parse"）；`.选图界面不自行生成地图` 禁用词加 `LinkMapGenerator` + 反面 `LinkMapGenerator.Generate(` 在目录里命中；`各入口按地图标识选图Tests.两人图可选… / 三人图可选…` 删去"从清单选中"部分，只保留夹具建局"缺省人数取地图上限"。
7. **图形版不再识别裸 gen / board**：`各入口支持生成图Tests.生成器只经目录调用…` 的 `bareBoardReaders` 去掉 `godot/scripts/GameRoot.cs`；GameRoot 两条 `GeneratedMapId/BoardMapId.Format(…Stopwatch…)` 断言改为一条 `LinkMapId.Format(…)` + `DoesNotMatch (GeneratedMapId|BoardMapId)\.Format\(`。
8. **P7 文件名白名单**（裁决 P7 直接导致）：`总势力Tests.领地计分直接取空格归属结果` 的 `CoverageTargets(` 读者名单加 `GameBoard.MapTables.cs`；`新地表判断唯一Tests` 的 Allowed 把 `GameBoard.cs` 换成 `GameBoard.MapTables.cs`（浅滩谓词随 `MapTables` 搬入，`GameBoard.cs` 已不再引用新地表）。
9. **D12 守门镜像**（需裁决，见 §7）：`ViewportCamera/生成图上的拾取自检口径Tests.拾取自检的例外没有写宽` 的那一行精确断言改为新条件 `(inView > 0 || !focusedOnCenter || !centerPlayable)`，并新增断言钉住 `centerPlayable` 的判据写法。

## 5. S10 的判定 / 执行拆分与 P7 零行为证据

- **S10**（`MatchFlow.Link.cs`）：`JudgeAtRoundEnd(PowerSnapshot power, IReadOnlySet<PlayerId> passed)` 对每个未结算、在局 ≥ 2 的战场判 (a) 全员在 `passed` 里 → `AllPassed`，(b) `!HasPlayableEmptyCell` → `BoardFull`，产出 `BattlefieldVerdict(Battlefield, Reason, Entries)`；`Judge(id, reason, power, relics, winner)` 是**唯一**比较链（势力 → 控制信物 → 独占空格 → 棋子，`SeedRank` 兜底，S6 并列名次）；`ExecuteSettlement(verdict, order)` 只做状态写入（已结算集合、记录、事件、落败者出局）。`SettleAtRoundEnd` = 重算势力 → `JudgeAtRoundEnd(power, _passedThisRound)` → 逐个 `ExecuteSettlement`（与原"逐战场判定即执行"等价：成员互不相交、判定只读大回合开始前定下的量）。减员结算与 Debug 接缝走 `SettleBattlefield` = Judge + Execute。
- `ForecastInitiative`（`MatchFlow.Preview.cs`）：Link 下 `power = PowerCalculator.Compute(...)` → `JudgeAtRoundEnd(power, _passedThisRound)`（未行动者不在 Pass 记录里 = 按未 Pass 算）→ 落败者在名册副本上标 `Eliminated` → 若剩余玩家只在一个战场且该战场已结算 / 在本次判决中 → `null`（此刻结束即终局，没有下一轮）→ 否则按剔除后的名册重算势力与先手修正（与 `EndMajorRound` 结算后的重算同口径），`active` = 剩余人数。Legacy 不进分支。质检局面实测：种子 9、`[0,0,3,4]`、P0、P1 已 Pass → `P2,P0,P3`，且与大回合结束后的真实顺序逐项相同。
- **P7 零行为**：`MapTables` 文本原样搬出（只加 partial 声明与文件注释）；坐标换算三处替换为同一算式 `index % width, index / width`。三条决策序列基线（`决策序列基线Tests` v5 / board:1、`决策序列基线_链接模式Tests` ×2）与 2300+ 条全量在三轮全量里均绿，测试资源一个字节未改（`git status` 不含 `AiDecision/Fixtures`）。

## 6. D-b 接线点

- **数据来源**：`MatchFlow.PublishFor(seat)` → `SeatPublicView`：`Placeholders`（`BoardPlaceholder(Zone, Origin 围棋记法, Width, Height)`，5 块出生棋盘的外接矩形，始终公开）、`Battlefield`（本座位所在战场标识，插旗阶段 `null`）、`Cells`（只含本战场的格，`SeatCellView.Ownership` 三态 / `Owner` / `Piece` / `RelicCell`）、`Fences` / `Relics` / `Groups` / `Players`（`SameBattlefield` 为假的玩家没有势力明细）/ `Settlements` / `Links`。插旗阶段读 `Flags.ViewFor(seat)` → `SeatFlagView(OwnZone, ZoneCount, IsLocked, SameBoard)`；**不得读 `FlagPublicView.FlagsAt`**（盲选下抛越界）。
- **Godot 现状（要改的入口）**：`MatchSession.BuildWorld()`（`src/godot/scripts/MatchSession.cs`）仍用全知 `Match.Publish()` + `ViewerWorld.Build(Me, view, supplement, hand, preview, forbidden)` → Presentation `PublicWorld.From(view, supplement)` / `DefaultBoardView.From(world)`（读 `board.Map.Boards`、`Cells`）；`BoardView.Build(DefaultBoardView, IReadOnlyDictionary<int, PlayerId> zoneOwners)`（`BoardView.cs:104`，全图 5150 格逐格搭建，`BirthZone` 着色在 147 / 242 行）与 `BoardView.Refresh(world, layers, …)`（`BoardView.cs:1005`）。D-b 要在 Presentation 加按座位的视图模型（输入 `SeatPublicView`），`BoardView` 只搭本战场格 + 5 个占位轮廓（D4：他人出生棋盘低饱和外接矩形，其余场景格不画）。
- **插旗**：`GameRoot.OnBoardClicked`（1748 行起）→ `MatchSession.ChooseZone(zone)` → `Match.PlantPrototype((Me, zone))`（Core 盲选已就位）；`MatchSession._zoneOwners`（zone → 首个玩家，`TryAdd`）与 `GameRoot.HomeCells()`（按 `ZoneOwners` 找自家）在同区时只剩首位——改读 `Flags.ViewFor(Me).OwnZone` / `SeatPublicView.Battlefield`。点占位即锁定（D2），当前点格子走 `BirthZoneAt(coord)`，改为点占位矩形。
- **新局界面背景**：`GameRoot.MapSelect.PreviewSession()` 用未插旗的会话 + 现有渲染画 5 块出生棋盘（含内部布局）——规格要求占位轮廓且 MUST NOT 显示内部布局，D-b 定稿占位样式后替换；`MapPreviewInfo.Of` 的文案（"棋盘 5 块（出生 5、公共 0）"）可一并改为"5 块出生棋盘"。
- **演出 / 终局**：`MatchFlow.Pairings / Links / Settlements` 供节拍；`EndReason.LastBattlefieldSettled` 文案已在 `Names.End`；`SeatCellView.Ownership` 供归属读法。
- **配对来源**：`GameRoot._pairing`（有人值守 `Siege.Presentation.Pairing.RandomPairingSource`，无人值守 `ScriptedPairingSource`），每次 `MatchSession.Create` 都传同一个实例。

## 7. 偏离与需裁决事项（编号续 D9）

| # | 事项 | 处理 | 建议 |
|---|---|---|---|
| D9 | 派发写"段 C 遗留的两处转交路径"，实测 Legacy 夹具经 Sim 入口解析地图的路径有 165 处（41 文件）+ `PlayCommand.Run` 9 处 + `MatchFlow.Restore` 3 个类：段 C 只改了走 CLI 的 11 条，其余 Core 测试一直靠 `MapCatalog.Resolve` 转交 v5（`RunConfig.MapId` 缺省） | 按派发给的两选项合并落实：批量 / 会话 / 回放加 `resolveMap` 解析器参数（缺省 `MapCatalog.Resolve`），测试显式传 `MapCatalog.Legacy.Resolve`；没有第二份清单；每处行尾注释 D7 | 接受 |
| D10 | Godot `--auto-demo` 首跑在链接后抛 `存档中的揭示记录指向非信物格 AW35`（被引擎吞掉、退出码仍 0）：`PublishSupplement` 的账本副本由 `RelicLedger.Restore(Generation, ExportState())` 构造，不含链接追加的公共棋盘信物——段 B 既有缺陷，Sim 不调公开补充载荷故段 C 未暴露 | Core 加 `RelicLedger.Copy()`（生成记录 + `LinkedPlacements` + 揭示记录），Preview 两处改用；补 `链接后的公开补充载荷Tests`；Legacy 逐项相同 | 接受（Core 改动超出"数据层"字面范围，但不改规则判定） |
| D11 | 无人值守下 `_Process` 抛异常被引擎记录后继续演示、退出码 0，违反 testing.md「自检中途抛异常 MUST 以非 0 退出」 | `GameRoot._Process` 在 `Unattended` 时接住推进对局的异常：打印、`Quit(1)`；有人值守照旧 | 接受 |
| D12 | `--pick-check` 多位姿层要求"注视地图中心的位姿画面内必须有格"；链接图画布中心是场景格，近景位姿（距离 8.047）整屏无可落子格 → 失败 | 按 Link 口径放宽：仅当地图中心格不可落子时，该位姿记"无可验"（`centerPlayable` 判据写死为"地图中心格在可落子格清单里"）；守门 `拾取自检的例外没有写宽` 的精确断言随之改并钉住判据。D-c（4.4）改成以当前战场为范围的位姿后可回收 | 接受；或由 D-c 一并处理、本段先保留失败 |
| D13 | 质检局面 `[0,0,3,4]` 在夹具缺省种子下 P0、P1 排在顺序末尾，两人 Pass 完大回合即结束、取不到预测；对 20 个种子扫描，种子 9 与 16 给出 `P2,P0,P3` | 测试钉种子 9（顺序 P0>P1>P3>P2，P3、P2 未行动）；另一条"预测与大回合结束后的实际顺序一致"用同一种子 | 接受 |
| D14 | 旧存档拒绝落在 Core `MatchFlow.Restore`（公开入口），Legacy 夹具走新增 `internal RestoreLegacy`（不是 `RestoreUnvalidated`——后者跳过地图校验，v5 黄金测试需要校验路径） | 已实现 | 接受 |
| D15 | `PairingSourceNames` / `PairingDraw` 从 Sim 移入 Core（Presentation 不得引用 Sim，又不得写第二份洗牌配对算法） | Sim 的 `SeededPairingSource` / `RandomPairingSource` 改用 Core 版，行为不变；Sim 真随机守门（恰一处 `RandomNumberGenerator`）仍绿 | 接受 |
| D16 | `MapCatalog.DefaultId` 改 `link:1` 后，`RunConfig.MapId` 内部缺省、`map` 子命令缺省、`PlayCommand` "缺省图不多打一行"三处若跟着变会动 Legacy 黄金值 / 转录 | 新增 `MapCatalog.Legacy.DefaultId`（v5）供这三处（C10 口径） | 接受 |
| D17 | `ForecastInitiative` 在"假设此刻结束即以最后战场结算终局"时返回 `null`（顺序层显示为无预测），而不是给出单人顺序 | 已实现并测试 | 接受；或改为仍给出剩余者顺序 |
| D18 | 新局界面背景仍是现有渲染的 5 块出生棋盘（含内部布局），规格 MUST NOT 显示布局 | 派发已说明"本小段先用现有渲染，占位样式由 D-b 定稿" | D-b 必做 |
| D19 | P7 裁决原文"全量测试不改"，但把 `MapTables` 抽成 `GameBoard.MapTables.cs` 后，两条按**文件名**扫源码的守门必然变：`总势力Tests.领地计分直接取空格归属结果` 的 `CoverageTargets(` 读者名单（多出 `GameBoard.MapTables.cs`）、`新地表判断唯一Tests` 的 Allowed（浅滩谓词随 `MapTables` 搬入新文件，`GameBoard.cs` 不再引用新地表） | 只改这两条白名单（各加一行 P7 注释），断言口径不变；三条决策序列基线与其余 2300+ 条未改 | 接受；否则 P7 无法抽文件 |

遗留与风险：
- `MatchSession._zoneOwners` / `GameRoot.HomeCells()` 同区时只记首位：自动演示 P0 为名单首位所以开局对准自检仍通过；有人值守若本机与 AI 同区、本机不是首位，回家 / 开局对准会取不到格——D-b 改按座位视图时一并修。
- 全局自动演示 93 大回合 142 s（headless），`--rounds` 缺省 4 不受影响。
- `HANDOFF.md`「现在就能玩」仍写旧地图标识与 `--flag-risk`，属段 E 5.2。
- `各入口按地图标识选图Tests.显式选缺省地图与不带选项逐字相同`：两次 `PlayCommand.Run` 都显式传 Legacy v5 后该断言趋于恒真；命令行缺省 `link:1` 由 `各入口按地图标识选图_链接模式Tests.缺省地图` 守住。
- 变异验证记录（全部实跑，红数已写回各测试类注释）：M-D1（预测不用判定）红 4；M-D2（判定把未行动者当已 Pass）红 6（预测 1 + 战场结算Tests 5）；M-D3（Restore 去门）红 1；M-D4（非法种子置 0）红 12；M-D5（Reroll 忽略注入值）红 2；M-D6（TrySelectId 解析不了时 Move(0) 报成功）红 1；M-D7（Resolve 转交 Legacy）红 8；M-D8（PairingDraw 首轮不配对）红 2；M-D9（Copy 不追加链接信物）红 1。均 `cp` 备份（不带 -p）→ 只建测试工程 → `--filter` → `cp` 还原 → `cmp` 一致 → `touch`；还原后相关类重建全绿（62/62、38/38），无 `.mutbak` 残留；最终全量在还原后的构建上跑出。
- `.trellis/workspace/rubioc/journal-1.md` 在本段开工前已是 M 状态（`git status` 首行），不是本段改动。
