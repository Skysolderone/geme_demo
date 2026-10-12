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


---

# 段 D-b 实施报告（插旗占位与按战场渲染：tasks 4.2 / 4.3）

对应 `tasks.md` 4.2、4.3（已勾上）。D-c / D-d 未动。未 `git commit`。基线 `fbade6f`（D-a 已提交，裁决 D9–D19 全按建议）。

## 0. 续做核对（主会话以为被中断；实为报告追加那一步的 heredoc 失败）

`git status`：16 个已改文件 + `art/board-link-mode/` + 2 个新测试类 + tasks.md，与主会话列的清单一致，全部是本段改动。`dotnet build siege.sln -c Release` 0 警告 0 错误。派发五点逐项：

| 派发项 | 状态 |
|---|---|
| 1 按座位的视图模型（Presentation） | 已完成（含守门测试，变异 M-E1 / M-E2 实跑） |
| 2 插旗界面（4.2）：占位可点选、选中即锁定、只展开自己那块、HUD 同战场玩家、`_zoneOwners` 首位缺陷、空格键未选区到占位外接矩形中心 | 已完成 |
| 3 按战场渲染（4.3）：只搭本战场 + 占位轮廓、场景格不画、链接后重建、信息层只在本战场、灰度可辨 | 已完成（灰度数值见 §4） |
| 4 新局界面背景只画占位（D18）+ `MapPreviewInfo` 文案 | 已完成 |
| 5 三条自检 + 终局跑 + 截图四张彩色 / 灰度 | 已完成（截图 6 张彩色 + 6 张灰度） |

未开始：无。进行中：无。

## 1. 完成情况与测试类

| 项 | 状态 | 测试类（`tests/Siege.Core.Tests/`） |
|---|---|---|
| 按座位的视图模型（Presentation，零 Godot） | 完成：复用 `DefaultBoardView` / `ViewerWorld`，以"过滤后的格集合"实现，不复制视图模型逻辑 | `InformationVisibility/视图模型按座位过滤Tests`（插旗阶段只有五个占位_没有任何格、构筑期只见自己的棋盘_其余四块是占位、序列化不含其他出生棋盘的任何坐标、同区两人互见_他人不见、链接后合并战场的格一起可见、信息层只含本战场的格、旧模式全图可见无占位_与全知棋盘逐项相同、别人的座位视图不得进入观察者世界） |
| 4.2 插旗界面 | 完成（点占位即锁定、只展开自己那块、他人旗帜不显示、HUD 显示同战场玩家、`_zoneOwners` 首位缺陷已修、空格键未选区时到全部占位外接矩形中心） | 上表 + Godot 自检；`MapSelection/选图视图模型Tests.预览说明读默认棋盘视图模型`（D18 文案） |
| 4.3 按战场渲染 | 完成（只搭本战场格 + 5 个占位轮廓；其余场景格不绘制；链接后沿地形指纹重搭、沿用台面与地块部件；信息层 / 禁手 / 落点 / 落成反馈只在本战场格） | `VisualStyleBaseline/占位轮廓灰度Tests`（占位边框与每种已展开台面的亮度差不小于阈值、台面与占位颜色只在调色板定义_Godot经转换取用） |
| D18 新局界面背景 | 完成：预览会话按座位过滤后只有 5 个占位，渲染层只画边框与编号；`MapPreviewInfo` 文案改为画布 + 5 块出生棋盘（边长范围），不提信物与可落子格 | `选图视图模型Tests.预览说明读默认棋盘视图模型`；`--map-select --auto-demo` 自检步骤改按占位核对 |
| 实测缺陷修复（D10 第二处） | `BatchPreviewBuilder`（本人部署的富预演）同样在账本副本上跑"将揭示"，副本不含链接信物 → 自检种子 3 / 5 第 46 帧抛"揭示记录指向非信物格"（D11 守门把它变成退出码 1 才暴露） | `Battlefield/链接后的公开补充载荷Tests.链接后揭示公共棋盘信物_本人部署预演照常` |

## 2. 新增 / 修改文件

新增：`tests/.../InformationVisibility/视图模型按座位过滤Tests.cs`、`tests/.../VisualStyleBaseline/占位轮廓灰度Tests.cs`；`art/board-link-mode/`（截图 12 张，见 §4）。

Presentation：
- `Visibility/DefaultBoardView.cs`：新增 `BoardPlaceholderView(Zone, Origin, Width, Height, Expanded, Own)`（`Cells()` 枚举外接矩形）；`DefaultBoardView.Placeholders`（init，缺省空）；`ForSeat(SeatPublicView seat, int? ownZone)`——`Mode != Link` 原样返回，否则 `Cells` / `Fences` / `Edits` / `Boards` 按 `seat.Cells` 的坐标集过滤，`Placeholders` 由 `seat.Placeholders` 映射（Expanded = 原点格可见，Own = 编号 == ownZone）；`CellOrNull`（`CellAt` 改为经它，未展开格抛出）。
- `Visibility/Worlds.cs`：`ViewerWorld.Build(..., SeatPublicView? seat = null)`（属于他人即抛）；`Seat` / `OwnZone`（取公开快照里本座位自己的 `BirthZone`）/ `SameBattlefieldPlayers`；`Board()` 经 `ForSeat`；`Layer()` 在 Link 下过滤 Territory 格、Liberty / Power 棋串（全部棋子可见才留）、Power 独占格、Relic 格。
- `Camera/CameraInput.cs`：`HoverReadout.Of` 对未展开格给空读数（不再 `CellAt` 抛出）。
- `MapSelect/MapPreviewInfo.cs`：有占位时文案"73×73 画布，5 块出生棋盘（边长 5–7）；各自盲选一块，三个大回合后随机配对链接"；无占位的旧文案不变。
- `Style/VisualBaseline.cs`：新增 `BoardPlatePalette`（`PublicFrame` 232/228/214、`BirthHint` 232/198/96、`PlaceholderFrame` 40/44/52、`PlaceholderLabel` 200/204/212、`MinPlaceholderLumaGap = 40`、`ExpandedFrames`）。

Core：`Preview/BatchPreview.cs` 的账本副本改经 `RelicLedger.Copy()`（Core 唯一改动，不改规则）。

Godot：
- `Visuals.cs`：`PublicBoardFrame` / `BirthHint` 改为 `ToColor(BoardPlatePalette.…)`，新增 `PlaceholderFrame` / `PlaceholderLabel`（源码扫描守住不再手写字面量）。
- `BoardView.cs`：新增 `_visible`（本次搭建的格集）；`Build` 按画布四角（对称取负，不另写换算，`坐标映射在Godot侧唯一` 守门不受影响）算相机 bounds——视图过滤后插旗阶段无格、链接后格集变化，bounds 都不变，否则 Rig 重建、开局对准自检红；`AddPlaceholders`（未展开占位：四条边框 + 中央编号 `Label3D`，占位格登记 h=0 供拾取、不进 `_visible`）；九个逐格图元（`AddTint / AddDot / AddPillar / AddBadge / AddEyeBadge / AddCross / AddRing / AddCircle / AddCornerBadge`）与 `AddEditFence` 开头按 `_visible` 守门。链接后重建不另写：`Refresh` 的 `TerrainKeyOf(board)` 随格集变化 → 既有 `Build` 路径；合批策略未动（`[batch]` 读数见 §3）。
- `MatchSession.cs`：`BuildWorld` 多取一次 `Match.PublishFor(Me)` 传给 `ViewerWorld.Build`；`ChooseZone` 先 `_zoneOwners[zone] = Me` 再登记其余（修同区首位）；锁定 Notice 带"同棋盘：…/ 这块棋盘只有你"。
- `GameRoot.cs`：`HomeCells()` 改读 `World.OwnZone`（本机自己的锁定结果），未锁定时返回全部占位格（空格键 → 占位外接矩形中心）；新增 `[link]` 感知日志（`_seenLinks`，见 §5）。
- `GameRoot.MapSelect.cs`：自检第 1 / 2 步改核对"5 个占位 + 0 格"（原核对 `Boards.Length == 5`，过滤后预览没有台面）。
- `Hud.cs`：插旗文案改为占位 / 盲选口径；回合横幅副标题附"同战场：…"（读 `ViewerWorld.SameBattlefieldPlayers`，不读他人旗帜）。

测试改动：`PresentationFixtures.World` 传 `match.PublishFor(viewer)`（与图形版会话同口径）；`选图视图模型Tests.预览说明读默认棋盘视图模型` 按 D18 文案改写、`ViewOf` 按地图取模式；`链接后的公开补充载荷Tests` 加一条。既有断言只改了这一处 D18 文案（D-a 新写的测试）；像素 / 节点计数自证改在 Godot 自检代码里（`GameRoot.MapSelect.cs`），不在测试断言。

## 3. 构建、全量、自检、终局跑

- `dotnet build siege.sln -c Release`：0 警告 0 错误（`src/godot` Debug 构建同样成功）。
- `dotnet test tests/Siege.Core.Tests -c Release`（全量，最终源码）：总计 2344，通过 2333，跳过 11，失败 0，1 m 33 s。
- Godot（最终 Debug 构建）：
  - `-- --auto-demo`：退出码 0，无 ERROR；`[batch] 障碍装饰合批 0 格…场景面砖 0 格、MultiMesh 0 个`（场景格不再绘制），`地砖与台面归并：面砖材质 2 份、衬底 2 份、描边 / 边框 2 份、盒网格 10 份`；`[link] 第 19 帧、第 4 大回合 链接 #1：战场 出生区 3 + 出生区 5；本机可见格 25，已展开占位 1 / 5`；开局对准自检 25/25、位姿变化 0；23 帧。
  - `-- --auto-demo --pick-check`：退出码 0。**Link 口径变化**：逐格居中层"可落子格 25"（只验本战场可见格；D-a 时全图 179）、25/25 两种缩放；多位姿层"中心 / 最近 / 最远"注视 (0,0) 均 0 格 → 按 D12 口径记"地图中心是场景格，无可验"；左下角位姿 25/25；全局预览 23/23；`可落子格 25，至少在一个位姿下验到 25；通过`。`拾取自检的例外没有写宽` 钉的两行字节未改。
  - `-- --map-select --auto-demo --rounds=2`：退出码 0，五步通过（文案已是占位口径：`73×73 画布，5 块出生棋盘（边长 5–7）…`；开始那步棋盘节点 438 / 438——D-a 时 1147，预览不再搭全图）；随后 2 大回合 + 开局对准 42/42 通过。
  - 种子 3 / 5 `--auto-demo`：首跑退出码 1（D11 守门抓到 `BatchPreviewBuilder` 的账本副本缺陷，第 46 帧），修复后退出码 0。
  - `-- --auto-demo --rounds=0 --seed=5`：退出码 0，无 ERROR；`[link] 第 39 帧 链接 #1：出生区 1 + 出生区 2；本机可见格 312，已展开占位 2 / 5`、`[link] 第 1052 帧、第 88 大回合 链接 #2：出生区 1 + 出生区 5；本机可见格 669，已展开占位 3 / 5`；`终局：第 156 大回合，最后一个战场已结算`；1191 帧，331 s（两次链接后重建均正常）。
  - `[perf]` 行里没有绘制调用数（只有 `--perf-sample` 窗口模式路径打印 `RenderTotalDrawCallsInFrame`），本段未跑 `--perf-sample`；合批策略零改动，场景面砖 / 障碍 MultiMesh 数从 D-a 的 5150 格 / 58 个降到 0（场景格不画）。

## 4. 截图（`art/board-link-mode/`，1600×900，非 headless，彩色 + `-gray` 灰度各一，共 12 张）

| 文件 | 命令要点 | 内容 |
|---|---|---|
| `flags-placeholders.png` | `--screenshot=…:2 --overview`（无 `--auto-demo`，停在插旗） | 插旗阶段：5 个占位轮廓 + 编号，无任何格 |
| `locked-own-board.png` | `--auto-demo --seed=5 --screenshot=…:3 --overview` | 锁定后：自己那块（出生区 1）展开为完整棋盘，其余 4 块占位 |
| `linked-battlefield.png` | `--auto-demo --seed=5 --screenshot=…:41 --overview` | 首轮链接后（第 39 帧链接 1+2）：合并战场（两块出生棋盘 + 公共棋盘 + 通道）+ 3 块占位 |
| `linked-battlefield-closeup.png` | 同上无 `--overview` | 合并战场近景：出生棋盘与新公共棋盘同一套台面 / 地砖 / 格线 |
| `placeholder-closeup.png` | `--auto-demo --seed=5 --screenshot=…:41 --shot-cell=AX26` | 占位（出生区 3）近景：只有边框与编号 |
| `new-game.png` | `--map-select --screenshot=…:2` | 新局界面：背景 5 个占位，面板显示 `link:<种子>` |

截图路径须给绝对路径（相对路径按 Godot 工程目录解析，首跑 5 张全部 `Can't save PNG`）。截图未读入会话，内容按控制台自证行与像素统计判定。

灰度亮度差（Rec.601 整数亮度）：调色板 占位边框 43 vs 公共台面边框 227（差 184）、出生区提示色 196（153）、红 98（55）、蓝 98（55）、金 169（126）、紫 100（57）→ 全部 ≥ 阈值 40（`占位轮廓灰度Tests`）。截图实测（PIL，±3 色差掩膜、跳过左侧 HUD 区）：占位近景里边框像素渲染为 (44, 49, 59)、灰度 48（中心 400×300 窗 1662 px），编号 (213, 216, 220) 灰度 215，占位所坐的底座 (135, 122, 110) 灰度 124；合并战场近景里公共台面边框像素 539 px 灰度均值 226、红方边框 98。渲染值与调色板差 ≤ 5（环境调整），差距结论不变（占位 48 vs 公共 226 / 红 98 / 金 169）。

## 5. D-c 接线点

- **相机范围数据**：`ViewerWorld.Board()` 的 `Cells`（本战场格）与 `Placeholders`（5 块外接矩形，`Expanded` / `Own`）；可行矩形按"当前战场外接矩形"取 `Cells` 的坐标包围盒、插旗未锁定时取全部占位的包围盒（`BoardPlaceholderView.Cells()`），再经 `_board.PlaneCenterOf` 换到平面坐标。本段 `BoardView.Build` 仍按整张画布建 `BoardCamera(bounds)`（画布四角、对称取负）；D-c 改成战场范围时注意：bounds 变化会重建 `Rig`、复位位姿，`_poseChangesAfterOpening` 计数要在重建处豁免或改为保位姿的重定界。`BuildCoordinateLabels` 仍沿整张画布边缘放 146 个标注（离战场很远），D-c 一并改为沿当前战场。
- **链接事件感知**：`GameRoot._Process` 的 `_dirty` 分支里新增的 `[link]` 循环（`_seenLinks` 与 `_session.Match.Links.Count` 比较）就是 Godot 第一次看到新链接的位置——AI 小回合 `RunAiTurn → Rebuild` 之后的同一帧（实测种子 5：第 39 帧、第 4 大回合）；紧随其后的 `RefreshViews → BoardView.Refresh` 经 `TerrainKeyOf` 变化重搭本战场。"棋盘展开"节拍挂在该循环内（拿 `LinkRecord.PublicBoard / Corridors / X / Y`），拉远到新战场范围用上面的包围盒；无人值守零时长沿用 `settlement-show` 时间线。
- **战场结算 / 落败横幅**：`_session.Match.Settlements`（`BattlefieldSettlementRecord`）与 `SeatPublicView.Settlements`；`Names.End` 已有 `LastBattlefieldSettled`。
- `GameRoot.HomeCells()` 现已按本机自己的 `OwnZone` 取格，开局对准 / 空格回家可直接复用。

## 6. 偏离与需裁决（编号续 D20）

| # | 事项 | 处理 | 建议 |
|---|---|---|---|
| D20 | 势力层 / 顺序层按玩家列出的行、HUD 排名栏与顺序预测仍显示他战场玩家的势力数值（information-visibility「他人势力不可见」的既有泄漏，来自全知 `MatchPublicView`）；本段只过滤了逐格 / 逐棋串内容 | 未改，不在 4.2 / 4.3 | 另起小段或并入 D-d：HUD 排名 / 顺序行读 `SeatPublicView.Players.SameBattlefield` 隐去数值 |
| D21 | 规格「占位灰度可辨」引用的"既有棋盘台面条款阈值"在既有规格、裁决记录与测试里都没有数值 | 本 change 取 `BoardPlatePalette.MinPlaceholderLumaGap = 40`（与 scoring-sites 据点三档"两两差 ≥ 40"同量级），占位色 (40, 44, 52)，与六种已展开台面边框色差 55–184 | 接受，或给出规格数值后回改常量 |
| D22 | 相机可行矩形本段仍取整张画布（规格「边界夹取」要求当前战场范围，属 tasks 4.4 / D-c）；全局预览也仍是整张画布 | 未改，接线点见 §5 | D-c 处理 |
| D23 | 账本副本缺链接信物在第二处（`BatchPreviewBuilder`，Core）复现，与 D10 同根因；D11 的无人值守守门第一次抓到真问题 | 改经 `RelicLedger.Copy()`，补测试（不改规则） | 接受 |
| D24 | `--pick-check` 口径随"只画本战场"变为只验本战场可见格（25 格），多位姿层注视中心的三档全部"无可验"，覆盖面靠逐格居中层与左下角 / 全局预览位姿 | 按 Link 口径接受；D-c 改为战场范围位姿后自然恢复 | 接受 |
| D25 | 占位格登记为 h=0 可拾取（插旗点占位即锁定）；对局中点到占位会走 `ToggleStage(coord)`（Core 拒绝：不在合法范围），无副作用 | 未改 | 接受 |

遗留与风险：
- `MatchSession._zoneOwners` 仍是 zone → 单一玩家，只给旧模式出生区染色用；链接模式下同区他人的出生区染色取首位登记者（本机已先登记），D-c / D-d 若要按玩家染色需改按座位视图。
- 新局界面与插旗阶段的坐标标注（146 个 `Label3D`）仍沿画布边缘，远离占位；视觉上空旷，D-c 相机 / 标注一并处理。
- 全局跑到终局 331 s（种子 5，156 大回合、两次链接后重建），比 D-a 的 142 s 长，差异来自对局长度，不是渲染。
- 变异验证记录（全部实跑，红数写在各测试类注释）：M-E1 `ForSeat` 不过滤 Cells → 红 5；M-E2 `Layer` 对链接模式恒不过滤 → 红 1（第一次尝试的变异不编译，已换成可编译写法重跑）；M-E3 占位色改 (110,112,120) → 红 1；M-E4 `BatchPreviewBuilder` 改回 `RelicLedger.Restore(Generation, ExportState())` → 红 1。均 cp 备份 → 只建测试工程 → filter → cp 还原 → cmp 一致 → touch；还原后相关类重建全绿（13/13、76/76），无 `.mutbak` 残留。


---

# 段 D-c 实施报告

> 派单：先修 D20（他战场玩家势力数值泄漏）→ tasks 4.4 相机与 D26 底座 → tasks 4.5 展开演出与结算横幅 → 自检与截图。未 commit；单 dotnet / Godot 顺序执行；图形版只引用 Core / Presentation；Godot 层不含规则计算；Presentation 零 Godot；规则 / AI / 子流未动。

## 1. 完成情况与测试类（`tests/Siege.Core.Tests/`，新增 20 条，全量 2364 条：通过 2353、跳过 11）

| 项 | 状态 | 测试类 |
|---|---|---|
| D20 他战场玩家只剩名字 + 在局 / 出局 / 弃赛 + 所在战场是否已结算 | 完成 | `InformationVisibility/他战场玩家数值不进HUD视图模型Tests`（5 条：势力行 / 顺序行 / 手牌对比面板 / 已结算状态文案 / 旧模式与同战场不遮蔽） |
| 4.4 相机：可行矩形 = 当前战场 ∪ 5 个占位包围盒，全局预览与"一屏看全"按当前战场，链接后保位姿重定界 | 完成 | `ViewportCamera/战场范围的相机Tests`（5 条） |
| 4.4 底座 / 坐标标注只盖画幅（D26） | 完成 | Godot 自证（`[camera]` 行、`--pick-check`）；旧地图逐值不变（`BoardGeometry` 新重载对边缘行列 0 / 末行末列退化为旧算式） |
| D12 / D24 "无可验"例外回收 | 完成 | `ViewportCamera/生成图上的拾取自检口径Tests`（守门恢复为精确条件，D-c 注释） |
| 4.5 棋盘展开节拍（升起 → 拉远 → 对方显现 → 横幅，约 2.5 s，本机不参与则空，无人值守零时长，提速照旧） | 完成 | `SettlementShow/棋盘展开节拍Tests`（5 条） |
| 4.5 战场胜出 / 落败横幅、他场结算提示条、决赛不重复、落败横幅排在出局横幅之前 | 完成 | `SettlementShow/战场结算横幅Tests`（5 条） |

变异验证（全部实跑，`cp` 备份 → 只建测试工程 → `--filter` → `cp` 还原 → `cmp` 一致 → `touch`；还原后相关类 193/193 全绿、无 `.mutbak` 残留；红数已回写各测试类 `<remarks>`）：
M-F1 `ViewerWorld.IsConcealed` 恒假 → 红 4；M-F2 `BoardCamera.Rebound` 复位到中心 / 最远 → 红 1（第一次写法改只读属性不编译，换成改字段重跑）；M-F3 `OverviewAvailable` 改回 `!FitsOneScreen` → 红 1；M-F4 `Expansion` 不区分对方出生棋盘与公共棋盘 → 红 3；M-F5 `Expansion` 对无新增格也出横幅 → 红 1；M-F6 `Settlements` 终局也出横幅 / 提示条 → 红 1；M-F7 他场结算出横幅而不出提示条 → 红 2。

## 2. 新增 / 修改文件

Presentation（零 Godot）：
- `src/Siege.Presentation/Show/LinkShowBeats.cs`（新）：`CoordRect`、`BoardRiseBeat`（700 ms）/ `CameraPullBeat`（500 ms）/ `OpponentRevealBeat`（600 ms）三种新节拍；`LinkShowBeats.Expansion(before, after, newOpponents)`（新增格 = 对方刚展开的出生棋盘 → 显现，其余 → 升起；相机目标 = 合并后战场包围盒；横幅"棋盘展开：与 X 交战"；总 2600 ms）；`Settlements(before, after, me, matchEnded)` → 横幅"战场胜出" / "战场落败（第 N 名）"或提示条"战场 出生区 N 已结算，胜者 X"；终局时两者都不出。`Splice(beats, settled, expansion)`：既有结算节拍末尾的出局 / 终局横幅与战场横幅并成一条、战场横幅在前（规格"先落败横幅再出局视图"），展开节拍永远最后。
- `src/Siege.Presentation/Show/ShowTimeline.cs`：`ShowMask` 增 `Rising`（格 → 千分进度）与 `CameraPull`（目标矩形 + 千分进度）；`Mask()` 对未到的升起格给 0、未到的显现格列入 `Hidden`，当前节拍按进度给值。
- `src/Siege.Presentation/Camera/BoardCamera.cs`：`Bounds`（可行矩形）与 `Focus`（当前战场）分离；`Rebound(bounds, focus)` 保位姿只重夹；`FullViewDistance` / `OverviewAvailable` / `FitsOneScreen` 按 `Focus`，`BoundsViewDistance` / `Farthest` 按 `Bounds`；不给 `focus` 时两者相同、旧地图逐位不变。
- `src/Siege.Presentation/Visibility/Worlds.cs`：`IsConcealed(player)`（Link 且非本机且 `SeatPublicView.Players.SameBattlefield == false`）、`ConcealedStatusText`；`Layer()` 链接模式下对势力行 / 顺序行 `Conceal`，`HandPanel()` 对手区 `Conceal`。
- `src/Siege.Presentation/Layers/LayerContents.cs`：`PlayerPowerRowView.Concealed / ConcealedText / Conceal(statusText)`、`OrderRowView.Concealed / ConcealedExplanation / Conceal()`。
- `src/Siege.Presentation/Hand/HandInfoPanel.cs`：`OpponentHandAreaView.Concealed / Conceal(statusText)`。

Godot（只接线、不算规则）：
- `src/godot/scripts/BoardView.cs`：画幅 `(low, high)` = 当前战场 ∪ 占位格的包围盒；底座 / 浮岛按画幅定位与尺寸；坐标标注沿画幅边缘；`Rig` 首建 `new BoardCamera(bounds, focus: 战场包围盒)`，重搭时 `Rig.Rebound(bounds, focus)`；`_tileNodes / _tileDrop` 登记每格地砖节点，`ApplyRising(mask)` 按 `Rising` 进度把地砖从地面之下（`RiseDepth` 1.6）抬到位；`RefreshShow` 地形键变化先重搭再应用升起。
- `src/godot/scripts/BoardGeometry.cs`：`ColumnLabelAnchor / RowLabelAnchor` 增带边缘行 / 列的重载，旧重载委托之（列标 / 行号换算仍只在这里）。
- `src/godot/scripts/GameRoot.cs`：`_hud.OverviewAvailable` 随 `Rig`；`ApplyCameraPull(mask)` 按 `CameraPull` 进度从节拍起始位姿插值到目标矩形的回家位姿（经可行矩形夹取，演出结束留在该处不强制回家）；`--pick-check` 注视中心三档以"画幅中心最近的可落子格"为注视点、全局预览档以 `OverviewAvailable` 为条件、无可落子格时不抛异常；`--shot-show=expand|field`；`[strip]` / `[field]` 自证行；`BeginShow` 打印"展开 a+b 格、拉远"。
- `src/godot/scripts/Hud.cs`：`OverviewAvailable` 门控"全局 [M]"按钮；`_eventStrip` 提示条（不阻塞）；排名栏 / 势力层 / 手牌面板对 `Concealed` 行只印名字与状态。
- `src/godot/scripts/MatchSession.cs`：`EventStrip`、`_matesBefore`；本机 Confirm / Pass 与 `RunAiTurn` 前采 `boardBefore` / `settlementsBefore`，`Rebuild()` 后 `WithLinkBeats` 经 `LinkShowBeats.Splice` 接上 `Settlements(...)` 与 `Expansion(...)`。

测试：
- 新：`InformationVisibility/他战场玩家数值不进HUD视图模型Tests.cs`、`ViewportCamera/战场范围的相机Tests.cs`、`SettlementShow/棋盘展开节拍Tests.cs`、`SettlementShow/战场结算横幅Tests.cs`（均 CRLF）。
- 改（既有断言，逐条）：
  1. `ViewportCamera/生成图上的拾取自检口径Tests.cs` —— 回收 D12 / D24：守门恢复为 `ok &= failures.Count == 0 && (inView > 0 || !focusedOnCenter);`、`DoesNotContain("centerPlayable")`、`Contains("bool focusedOnCenter = name is \"中心\" or \"最近\" or \"最远\";")`（注释"board-link-mode D-c："）。
  2. `VisualStyleBaseline/双字母列标不重叠Tests.cs`「单字母列标不变」—— 行号锚点字面量由 `BoardGeometry.RowLabelAnchor(y, _width, _height, right: …)` 改为 `BoardGeometry.RowLabelAnchor(y, low.X / high.X, _width, _height, right: …)`（D26 沿画幅放标注；所守的"行号不走列标字号"不变；注释"board-link-mode D-c："）。

其它：`openspec/changes/board-link-mode/tasks.md` 勾选 4.4、4.5。`.trellis/workspace/rubioc/journal-1.md` 的未提交改动是主会话的，未动。

## 3. 构建、全量、自检、终局跑

- `dotnet build siege.sln -c Release`：0 警告 0 错误。`dotnet test tests/Siege.Core.Tests -c Release`：通过 2353 / 跳过 11 / 总计 2364。
- `dotnet build src/godot/Siege.Godot.csproj`（Debug）后：
  - `--auto-demo`（默认种子 20260915，4 大回合）退出 0；`[camera] 开局对准自检：出生平台 25 格，整格在画面内 25；位姿变化 0 次；通过`。
  - `--auto-demo --pick-check` 退出 0：左下角 25/25、最近 17/17、最远 25/25（注视点 = 画幅中心最近的可落子格，距离 28 = 可行矩形含占位时的上限）、三个角位姿无可验（非注视中心档，照旧不算失败）；`可落子格 25，至少在一个位姿下验到 25；通过`。link:1 开局单块 5×5 一屏看全 → 不再有"全局预览"档（见 D28）。单跑 `--pick-check`（不带 `--auto-demo`，插旗未锁定）现以"可落子格 0 … 失败"退出 1，不再抛 `Sequence contains no elements`。
  - `--map-select --auto-demo` 退出 0（选图自检 5 步通过、建局后开局对准 35/35 通过）。
- 终局跑 `--auto-demo --rounds=0 --seed=5` 退出 0：156 大回合、1191 帧、246 s（`[perf] 启动到首帧 1433 ms，启动到终局 244795 ms`），0 条 ERROR；第 39 帧链接 #1（1+2，本机参与：`展开 252+35 格、拉远`）、第 104 帧提示条"战场 出生区 5 已结算，胜者 紫方(P3)"、第 1052 帧链接 #2（1+5，`展开 322+35 格、拉远`）、第 1190 帧终局。无人值守全部零时长、对局不推进的守门照旧（`Drive` 在 `!_show.IsFinished` 时直接返回）。
- 种子扫描（`--rounds=30`，种子 1–8 顺序单跑）：种子 3 第 92 帧出"战场落败（第 2 名）"横幅（本机 P0 落败、对局未终局）；种子 7 第 51 帧"棋盘展开：与 蓝方(P1)、金方(P2) 交战"（两人同时成为同战场）。

## 4. 截图（`art/board-link-mode/`，1600×900，非 headless，绝对路径，彩色 + `-gray` 灰度各一，共 8 张；未读入会话，按自证行与 PIL 统计判定）

时间线：`d-c-expand-mid` / `d-c-strip-elsewhere` / `d-c-overview-field` 三张拍在 D29 的标注换算重载迁移之前（迁移只搬换算、坐标逐值不变，之后 `--auto-demo` / `--pick-check` / `--map-select` 自检已重跑）；`d-c-field-lost` 在 `Splice`（战场横幅排到出局横幅之前）之后重拍。

| 文件 | 命令（`--path src/godot --`） | 内容 | 灰度均值 / 暗(<40) / 亮(≥200) |
|---|---|---|---|
| `d-c-expand-mid.png` | `--auto-demo --seed=5 --shot-show=expand --screenshot=…:39` | 展开演出中间帧：升起节拍进行到一半（公共棋盘与通道地砖抬到半高，对方出生棋盘仍隐藏，相机未拉远）；自证 `[show] 第 40 帧 本机结算：节拍 7（… 展开 252+35 格、拉远），4350 ms`、`[field] … 棋盘展开：与 金方(P2) 交战` | 125.3 / 4.6% / 6.2% |
| `d-c-field-lost.png` | `--auto-demo --seed=3 --rounds=10 --shot-show=field --screenshot=…:90` | 战场落败横幅"战场落败（第 2 名）"（第 92 帧、第 8 大回合；节拍 2 = 势力 + 并成一条的横幅［战场落败 → 红方出局］，2500 ms，取横幅半程） | 122.0 / 4.3% / 5.3% |
| `d-c-strip-elsewhere.png` | `--auto-demo --seed=5 --rounds=12 --screenshot=…:110` | 他场结算提示条"战场 出生区 5 已结算，胜者 紫方(P3)"（第 104 帧出现，第 110 帧截） | 130.0 / 7.0% / 15.4% |
| `d-c-overview-field.png` | `--auto-demo --seed=5 --overview --screenshot=…:45` | 全局预览（当前战场 = 链接后的 1+2 合并战场）：`[camera] 第 43 帧（全局预览）注视点 (-0.5, 32)，距离 48.758`（注视合并战场中心，不是画布中心；其余占位不在预览范围内） | 142.8 / 11.2% / 33.0% |

## 5. D-d 接线点

- 自证行：`[link]`（新链接）、`[strip]`（提示条变化）、`[field]`（战场 / 展开横幅文案）、`[show]` 的"展开 a+b 格、拉远"，可直接做自检断言。
- `--shot-show=expand`（升起 / 显现节拍半程）、`--shot-show=field`（战场胜出 / 落败横幅）已可用；提示条截图用普通 `--screenshot=…:N` 在 `[strip]` 之后几帧取。
- 相机：`BoardCamera.Bounds / Focus / Rebound / OverviewAvailable / FitsOneScreen`；Godot 侧 `BoardView.Rig`、`GameRoot.ApplyCameraPull`。
- 演出：`LinkShowBeats.Expansion / Settlements`，`ShowMask.Rising / CameraPull`，`BoardView.ApplyRising`。

## 6. 偏离与需裁决（编号续 D27）

| # | 事项 | 处理 | 建议 |
|---|---|---|---|
| D27 | 决赛战场结算即终局时，本机若早已出局 / 弃赛（不在决赛战场在局名单里），按"他场"逻辑会出提示条"战场 X 已结算，胜者 Y"（种子 5 终局跑第 1190 帧实测，本机 P0 此前因势力归零出局） | `Settlements` 在 `matchEnded` 时横幅与提示条都不出，只留既有终局横幅与终局面板；「决赛不重复」补断言 | 接受 |
| D28 | HUD"全局 [M]"按钮与 `--pick-check` 的全局预览档都以 `OverviewAvailable`（当前战场一屏看不全）为条件；可推屏提示（`CameraHintVisible`）仍按可行矩形（含占位） | link:1 开局单块一屏看全 → 无全局预览档（D-b 时按整画布判有此档）；链接后战场变大自动出现 | 接受 |
| D29 | 坐标标注沿画幅放（D26）与既有守门「单字母列标不变」的行号锚点字面量冲突 | 锚点换算迁入 `BoardGeometry` 带边缘行 / 列的重载（换算仍唯一），守门字面量相应更新并注释 | 接受 |
| D30 | 相机可行矩形按 D26 含 5 个占位：插旗前后都可把相机推到占位处，"最远"档 = min(可行矩形一屏距离, 28)；回家锁定后仍是出生棋盘 + 边距（`CameraHome.Platform`），未锁定为占位包围盒中心 | 已实现 | 接受 |
| D31 | 本机被势力归零出局后观战，原战场随后结算：`Entries` 只含在局玩家 → 本机按"他场"收提示条而非横幅（规格只写了"本机战场结算"与"他场结算"两种） | 按"不在在局名单 = 他场"处理 | 接受，或要求对已出局观战者出"你曾在的战场已结算"横幅 |
| D32 | 他战场玩家的"所在战场已结算（胜出）"只按结算记录胜者判；出局 / 弃赛走 `Labels.Status`（"已出局 · 不再行动"等既有文案），没有另造"出局"短文案 | 已实现 | 接受 |
| D33 | 展开演出的拉远节拍在有人值守下经 `ApplyCamera("棋盘展开")` 改位姿；`_poseChangesAfterOpening` 只在无人值守终局自证，零时长下拉远不发生，自证照常 0 次 | 已实现 | 接受 |
| D34 | 本机战场落败那一回合既有 `SettlementBeats` 已出"红方出局"横幅，直接顺序拼接会先"出局"再"战场落败"，与规格"先落败横幅再出局视图"相反（种子 3 第 92 帧实测） | 加纯函数 `LinkShowBeats.Splice`：两条并成一条横幅节拍、战场横幅在前；补测试；重拍截图 | 接受 |

遗留与风险：
- Legacy 53 帧 Godot 基线仍不可在图形版复现（新局界面只认 `link:`，D-a 起）；旧模式只靠 Core / Presentation 测试与 `BoardCamera` 不给 `focus` 时逐位不变的测试守。
- `--pick-check` 只验开局那一帧（第 2 帧），链接后合并战场的拾取没有自检覆盖（testing.md 已有同类说明）；可在 D-d 加"链接后重跑拾取"。
- 他场结算提示条只保留最近一条（`EventStrip` 覆盖式），不排队、不自动消失。
- 展开演出里对方盘面"显现"用既有 `Appearing` 遮罩（渐显），对方已揭示信物随格一起显现，没有单独的信物节拍。

---

# 段 D-d 实施报告

> 派单：tasks 4.6 —— `--auto-demo` 全流程可断言自检、链接后重跑拾取、他场结算提示条队列、截图整理与 README、`--map-select --auto-demo` 核对、全套跑。未 commit；单 dotnet / Godot 顺序执行；图形版只引用 Core / Presentation；Godot 层不含规则计算（只调 Core 只读查询、喂数据给 Presentation 判定）；Presentation 零 Godot；规则 / AI / 子流未动；既有测试断言一条未改。

## 1. 完成情况与测试类（`tests/Siege.Core.Tests/`，新增 12 条，全量 2376 条：通过 2365、跳过 11）

| 项 | 状态 | 测试 / 自证 |
|---|---|---|
| 1 `--auto-demo` 全流程自检：插旗锁定 → 构筑期 → 首轮配对 → 链接 → 战场结算 → 决赛链接 → 终局，每步 `[flow]` 行可断言、任一步不符退出码 1；`--rounds=0` 另要求七步齐全 | 完成 | `SimulationHarness/自动演示全流程自检Tests`（7 条）；Godot `[flow]` 行（缺省种子 `--rounds=0`：11 条自检全通过、七步齐全；种子 5：13 条） |
| 2 链接后重跑拾取：开局一轮通过后不退出，本机参与的链接把战场合并、重搭后的下一帧再跑一轮，两轮都过才退出 0 | 完成 | `--auto-demo --pick-check --seed=5`：开局 25/25，链接后 312 格逐格居中 312/312 ×2、各位姿含"全局预览"档（距离 86.68）312/312；缺省种子以 1 退出并提示换种子 |
| 3 提示条队列：多条依次、每条停留 3000 ms 自动消失；无人值守零时长 | 完成 | `SettlementShow/他场结算提示条队列Tests`（5 条）；`[strip]` 行照常打（与显示解耦） |
| 4 截图整理：补"决赛链接后合并战场全局预览"与"终局面板"（彩色 + 灰度），`art/board-link-mode/README.md`（28 行：12 对图的命令 / 帧 / 看什么 + 7 项人工检查清单） | 完成 | §4 |
| 5 `--map-select --auto-demo` 五步与规格四步核对 | 已通过，未动 | 第 3 步"回到进入时的种子"只为"开始"那步的节点计数比对服务，规格的四步（非法种子 / 合法种子 / 换一张 / 开始）全在；退出 0 |
| 6 全套跑 | 完成 | §3 |

变异验证（全部实跑，`cp` 备份 → 只建测试工程 → `--filter` → `cp` 还原 → `cmp` 一致 → `touch`；还原后两类 12/12 全绿、无 `.mutbak` 残留；红数已回写各测试类 `<remarks>`）：
M-G1 `NoticeQueue.Advance` 到期后不换下一条 → 红 3；M-G2 零时长也入队 → 红 1；M-G3 `LinkDemoAudit.Pairing` 不看来源 → 红 1；M-G4 `Linked` 本机参与时不比可见格数 → 红 1；M-G5 `Completeness` 恒通过 → 红 1。

## 2. 新增 / 修改文件

Presentation（零 Godot，不调任何 `MatchFlow` / `FlagPlanting` 成员——`UI层不含规则计算Tests` 的禁用表）：
- `src/Siege.Presentation/Diagnostics/LinkDemoAudit.cs`（新）：`AuditLine(Step, Detail, Ok)`；`LinkDemoAudit` 纯判定——`FlagsLocked`（占位 5 块、仅本机那块展开、可见格 = 本战场格数）、`BuildingRange`（第 1–3 大回合合法范围非空且 ⊆ 自己出生棋盘）、`Pairing`（来源 = `script`、各对出自候选且不重复、首轮奇数候选恰一名等待者、决赛无等待者且全部候选链成一个、首轮先于决赛）、`Linked`（本机参与：可见格 = 本战场格数且增长、已展开占位 ≥ 2；他人：可见格不变）、`Settled`（本机在名单且未终局 → 横幅"战场胜出" / "战场落败（第 N 名）"；不在 → 提示条"战场 X 已结算，胜者 Y"；终局 → 都没有）、`Ended`（原因 = `LastBattlefieldSettled`）、`Completeness`（七步齐全）；`Lines / AllOk / Seen`。
- `src/Siege.Presentation/Show/NoticeQueue.cs`（新）：`DwellMs = 3000`，`Enqueue`（零时长丢弃）、`Advance(ms)`（到期换下一条、多余时间顺延）、`Current / PendingCount / IsEmpty / ElapsedMs`。

Godot（只接线）：
- `src/godot/scripts/MatchSession.cs`：删 `EventStrip`；新增 `TakeNotices()`（本回合新提示条文案）、`LastSettlementShow`（最近一次结算接线的战场横幅与提示条）、只读查询 `MyBattlefield / MyBattlefieldCells`（`Battlefields.Of(Map).ById(...)`，与按座位过滤独立的口径）/ `MyLegalRange`（`Match.LegalRangeFor(Me)`）。
- `src/godot/scripts/Hud.cs`：`Refresh` 不再读 `EventStrip`；新增 `SetEventStrip(string?)`（逐帧由队列当前条驱动，到期消失不走脏刷新）。
- `src/godot/scripts/GameRoot.cs`：`LinkDemoAudit _audit`、`Flow(line)` 打 `[flow]` 行（失败另打错误）；锁定（`ChooseZone` 后）、构筑期（本机每大回合一次，≤ 3）、配对 / 链接 / 结算（脏刷新分支增量）、终局与完整性（`FinishAutoDemo`，`_rounds == 0` 才要求完整）；退出码 `HomePlatformFramed() && _audit.AllOk`。`NoticeQueue _strips`（无人值守且无待截图 → 零时长）+ `AdvanceStrips(delta)`（截图模式固定 16 ms / 帧）。`--pick-check`：Link 模式开局一轮通过后置 `_pickCheckAfterLink` 继续演示，`[link]` 循环里本机参与（`MyBattlefield == link.X`）则下一帧跑第二轮；演示结束仍没等到 → 错误、退出 1。`Unattended` 含 `_pickCheckAfterLink`。

文档 / 资产：
- `art/board-link-mode/README.md`（新，28 行）；`art/board-link-mode/d-d-final-link-overview(.png/-gray.png)`、`d-d-match-end(.png/-gray.png)`（新）；`d-c-strip-elsewhere(.png/-gray.png)` 用队列实现重拍（同一命令同一帧）。
- `HANDOFF.md`：自检命令清单加一行（链接全流程 `--auto-demo --rounds=0 [--seed=5]`；第二轮拾取 `--auto-demo --pick-check --seed=5`）。其余未动（整体改写属段 E）。
- `openspec/changes/board-link-mode/tasks.md` 勾选 4.6。

测试：新 `SimulationHarness/自动演示全流程自检Tests.cs`（7 条）、`SettlementShow/他场结算提示条队列Tests.cs`（5 条），均 CRLF。既有测试断言未改。

## 3. 构建、全量、自检、终局跑

- `dotnet build siege.sln -c Release`：0 警告 0 错误。`dotnet test tests/Siege.Core.Tests -c Release`：通过 2365 / 跳过 11 / 总计 2376。
- `dotnet build src/godot/Siege.Godot.csproj`（Debug）后（全部 headless、顺序单跑、0 条 ERROR）：
  - `--auto-demo`（缺省种子、4 大回合）退出 0：`[flow]` 5 条通过（插旗锁定 25/25、构筑期 25 格全在棋盘内、第 12 帧他场结算提示条、首轮配对 (3,5) 来源 script、他人链接可见格 25 → 25），完整性不要求；开局对准自检通过。
  - `--auto-demo --pick-check --seed=5` 退出 0：开局一轮 25/25 → 第 39 帧链接 1+2 → 第 40 帧第二轮：逐格居中 312/312 ×2，位姿 中心 45、左下 28、右下 39、最近 23、最远 75、全局预览 312（距离 86.68，D28 后这一档在合并战场一屏看不全时自然出现），`可落子格 312，至少在一个位姿下验到 312；通过`。缺省种子：开局一轮通过后演示到第 4 大回合停止，`错误：--pick-check 直到演示结束都没有等到本机参与的链接……换一个 --seed=（如 --seed=5）`，退出 1。
  - `--map-select --auto-demo` 退出 0：五步通过（本次随机种子 `link:852646294`，棋盘节点 428 / 428、游离 0 / 0），建局后自动演示 `[flow]` 6 条通过、开局对准 30/30 通过。
  - `--auto-demo --rounds=0`（缺省种子）退出 0：93 大回合、240 帧、约 100–103 s（两次实跑）；`[flow]` 11 条全通过、七步齐全（锁定 → 构筑 → 第 12 帧他场结算［本机第 1 大回合被减员（势力归零）出局，此后全程观战］→ 第 19 帧首轮配对 (3,5) 与他人链接 → 第 139 帧决赛链接 (1,3)、本机参与链接 25 → 564 格 = 本战场、他场结算提示条 → 第 239 帧决赛结算（终局：不出横幅与提示条）→ 终局 `LastBattlefieldSettled`）。
  - `--auto-demo --rounds=0 --seed=5` 退出 0：156 大回合、1191 帧、246–361 s（两次实跑）；`[flow]` 13 条全通过（第 39 帧首轮配对 (1,2)(4,5)、本机参与链接 25 → 312；第 104 帧他场提示条；第 1052 帧本机战场结算"战场落败（第 2 名）"横幅、决赛链接 (1,5)、本机参与链接 312 → 669；第 1190 帧决赛结算不出横幅；终局）。

## 4. 截图（`art/board-link-mode/`，共 24 张 = 12 对；索引与人工清单见 `README.md`）

| 文件 | 命令（`--path src/godot --`） | 内容 | 灰度均值 / 暗(<40) / 亮(≥200) |
|---|---|---|---|
| `d-d-final-link-overview.png` | `--auto-demo --rounds=0 --seed=5 --overview --screenshot=…:1060` | 决赛链接（第 1052 帧 1+5）后的合并战场全局预览：`[camera] 第 1058 帧（全局预览）注视点 (-0.5, 0.5)，距离 86.68`（三块出生棋盘 + 两块公共棋盘的中心），约 195 s | 157.3 / 4.1% / 36.5% |
| `d-d-match-end.png` | `--auto-demo --rounds=0 --seed=5 --screenshot=…:1200` | 终局面板（第 1190 帧"最后一个战场已结算"，四人名次），`FinishAutoDemo` 有待截图时停在结算画面；约 270 s | 131.3 / 4.5% / 18.9% |
| `d-c-strip-elsewhere.png`（重拍） | `--auto-demo --seed=5 --rounds=12 --screenshot=…:110` | 第 104 帧入队、第 110 帧截（截图模式每帧 16 ms，队列里仍在显示）；像素统计与 D-c 版相同 | 130.0 / 7.0% / 15.4% |

## 5. 偏离与需裁决（编号续 D35）

| # | 事项 | 处理 | 建议 |
|---|---|---|---|
| D35 | 全流程"七步齐全"只在 `--rounds=0` 要求；缺省 `--auto-demo`（4 大回合）不要求完整性、保持快速退出，但已见各步任一失败同样退出 1（退出码 = 开局对准自证 `&&` 全部 `[flow]` 通过，对缺省与全流程一样生效） | 已实现 | 接受 |
| D36 | 链接后第二轮拾取要本机参与的链接；缺省种子（20260915）的本机在 2 人棋盘上、不进首轮配对，`--auto-demo --pick-check` 不带种子现以 1 退出并提示换种子；文档命令改为 `--auto-demo --pick-check --seed=5`（HANDOFF 只加一行，第 158 行旧命令未动） | 已实现 | 接受；或段 E 改写 HANDOFF 时把第 158 行并掉 |
| D37 | 提示条零时长只在"无人值守且没有待截图"时生效；截图模式下队列每帧固定推进 16 ms（提示条截图帧可复现），正常时长按帧间隔 | 已实现 | 接受 |
| D38 | 全流程判定放 Presentation（`LinkDemoAudit`，纯函数、可测），Godot 只喂数据；`MatchSession` 新增三个只读查询（`MyLegalRange` 调 `Match.LegalRangeFor`，`MyBattlefieldCells` 调 `Battlefields.Of(Map).ById`），不自算规则 | 已实现 | 接受 |
| D39 | 战场结算核对口径："本机在名单" = 在该结算记录的 `Entries`（在局者）；本机已出局后原战场结算按他场提示条核对（D31 延续）；终局那条要求横幅与提示条都没有 | 已实现 | 接受 |
| D40 | 缺省种子全流程实测：本机第 1 大回合即被减员（势力归零）出局，此后全程观战，本机战场横幅一次都没出现；七步仍齐全（"战场结算"以他场提示条计） | 未改缺省种子 | 接受；若要缺省种子演示到本机横幅，需另选缺省种子（种子 5 有"战场落败"横幅但 156 大回合 / 约 4 分钟） |
| D41 | `--map-select --auto-demo` 五步比规格多出第 3 步"回到进入时的种子"（为"开始"那步的节点 / 游离节点计数比对服务） | 未动 | 接受 |
| D42 | `--pick-check` 第二轮沿用 `RunPickCheck` 原函数（`ok &=` 守门行与 `focusedOnCenter` 一字未动），四角位姿在合并战场上两角"无可验"照旧不算失败 | 已实现 | 接受 |

遗留与风险：
- 全流程自检只覆盖无人值守路径的七步；有人值守路径（真随机配对、演出正常时长）仍靠人工试玩（README 清单）。
- 第二轮拾取只在本机参与的**第一次**链接后跑一次；决赛链接后的更大战场（种子 5：669 格）没有拾取自检。
- 提示条队列不去重：同一帧多条结算各入一条（依次 3 s）。
- `d-d-*` 两张截图各需约 3–5 分钟非 headless 跑到第 1052 / 1190 帧，复现成本高于其它图。
