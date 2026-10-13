# retire-legacy-maps 实施报告（trellis-implement）

> 2026-10-13。未提交、未归档（5.4 留给主会话）。逐类迁移清单与各组对账见同目录 `migration.md`。

## 1. tasks.md 勾选

1.1–1.8、2.1–2.4、3.1–3.7、4.1–4.3、5.1–5.3 全部勾选（25 项）；5.4（validate + 归档）未做，留给主会话。

## 2. 迁移清单摘要（migration.md）

- 通过数：开工前 2385 / 跳过 10 → 结束 **1971 / 跳过 6**（共 1977，失败 0）。净 −414 = 新增 +26 − 删除 440；跳过 −4（Perf / Slow 各随旧图删除或合并）。
- 各组：G1 2385 → 2389（−4 删 +2 新基线 +5 校验规则 +1 棋盘清单；守门自 边疆档静态校验 迁入新类，类间 −1 / +1 对冲）；G2 → 1967（−430 +8）；G3 → 1965（−2）；G4 → 1971（−3 +9）。每组减少项都落在清单"删"里，逐类由 trx 对比对上。
- 整类删除 27 个测试类（MapDefinition 9、MapGeneration 11、SimulationHarness 5、AiDecision 2）+ 4 个旧夹具 + 2 份旧基线；部分删除 17 个类（含增删对冲的决策序列基线、地图规格档、孪生类与 Slow 合并的活形缓存）；其余约 70 个类"迁 link / 迁手写 / 改"（条数不变，断言不改，只换载体，均带 `retire-legacy-maps：` 注释）；`MatchFixtures.cs` 一行未改（"不动"）。
- 清单 98 行（一行可含多类）全部勾销。

## 3. 删除与保留

**删除的源文件**：`src/Siege.Core/Board/Maps/` 18 个（四张内置图、FrontierMapGenerator、FrontierMapLayout ×8、FrontierSurfaces、MapGenParameters、BoardMapGenerator、BoardMapLayout、BoardMapParameters）、`Board/MapSymmetry.cs`、`maps/` 7 个 JSON、csproj 两项 EmbeddedResource。

**删除的类型 / 成员**：`MapCatalog.Legacy / BuiltinIds / BuiltinMaps / BuiltinMapInfo`、`GeneratedMapId`、`BoardMapId`（随文件）；`MatchOptions.FlagRisk / DefaultFlagRisk`、`PrototypeZoneAssignment.Assign / RequireValidFlagRisk`、`GameSeed.ZonePick / FlagRisk`、`RunConfig.FlagRisk`（RetiredKeys 报错）；`EvaluationWeights.MapOverrides / ForMapId ×2 / MapOverrideCalibrationOf / TwoPlayerOverrideCalibrationStatus`；`BatchRunner / MatchSession / Replayer` 的 `resolveMap`、`MatchFlow.RestoreLegacy`；`MapRandom.ForAttempt / ForSurfaces / ForBoardAttempt`；`MapProfile.Standard / Frontier / Board`；校验器 `DistanceHandling`、`PlateConnectivity`、`ValidateCorridors`、`ValidatePlateConnectivity`、`ValidateBirthZoneConnectivity`、`ValidateDistanceBalance`、CHOKE_NOT_ANNOTATED / BOARDS_NOT_ALLOWED 分支；Program 的内置图权威文件导出与 `RequireNotAuthoritativeMapFile`；图形版 `Visuals.CorridorPath` 与通道地砖分支；Sim / 终端 / 图形版的建局模式分叉。

**保留项（L10 复用 / L6）**：
- `MapRandom.ForLinkLayout`、`LinkLayout`、`LinkMapId`（新增 `FriendlySeed`，自 GeneratedMapId 原样迁入）、`MapGenerationException`（自 FrontierMapGenerator.cs 抽出，LinkMapGenerator 用）。
- `MapValidator.DistanceTable / MultiSourceDistances`（`Siege.Sim map` 打印距离表）、`MapFindingSeverity.Report` / `Reports`（公开 API）、`BoardPlateKind.Public`、`MapData` 的咽喉 / 中央入口 / 容差字段（链接图文件里写着）。
- `Visuals.PublicBoardFrame` / `BoardPlatePalette.PublicFrame`（链接图连体棋盘外轮廓复用）。
- L6 = C：`MatchOptions.Mode`（init 降为 internal）与 `CreateUnvalidated / RestoreUnvalidated` 的按模式分支；Sim 日志字段与 `BoardRenderer` 读 `Mode` 的分支（L17）。

## 4. 护栏

- 三条链接图决策基线（`link:1` 种子 1 / 2 新钉、种子 3 沿用）每组结束全过，种子 3 文件 git 干净。
- 专家 G1 黄金值改钉 `link:1` 种子 1–3（18/3069/9、22/7502/11、24/6510/12）；Perf 比值 专家 / 高难 = 1.576（≤ 4）。
- `link:1`–`link:20` 初始图、`--links 1-2,3-4` 后的图、首部地图摘要与 config：G1 / G2 / G3 / G4 四份快照逐字节相同；`link:1` 种子 1 / 2 截断 24 小回合完整日志 G2 / G3 / G4 除计时字段外逐字节相同。
- L12：区域强度预算未收敛比例（V2）`link:i × 种子 i` 15 / 100、`link:1–100 × 种子 1` 0 / 100、`link:1 × 种子 1–100` 13 / 100（V1：24 / 16 / 29 %）。

## 5. 构建 / 测试 / Godot / 变异

- `dotnet build siege.sln -c Release` 0 警告 0 错误；Godot 工程 0 / 0；`dotnet test` 1971 通过 / 6 跳过 / 0 失败。
- Godot（console，headless，顺序单跑）：`--auto-demo` 0（6 条通过）；`--auto-demo --pick-check --seed=5` 0（30/30、55/55）；`--map-select --auto-demo` 0（五步全过）；`--auto-demo --rounds=0 --seed=5` 0（13 条、七步齐全）；无 ERROR。seed 5 两条的 [flow] / [camera] 行与 G1 逐行相同。画面：构筑期帧逐像素相同，决赛链接帧 4 像素抗锯齿差，绘制调用 / 对象 / 图元全同。
- 变异（均实跑后还原）：G2 7 处（生成器只经目录 ×2、目录无内置成员、选图种子折叠、四邻接、新地表、地形写入口）、G3 4 处（公开建局恒链接、公开恢复模式把关、FlagRisk 退役键、选区唯一实现）、G4 4 处（规格档字面量 / 字段二读、已移除规格档、校验器四邻接）——全部按预期红。

## 6. 偏离与待裁决

- **L15** 终端链接局链接后 `PlayCommand.Summarize` 越界崩溃（既有缺陷，未修；建议另开 change）。
- **L16** 部分 G3 删除项（地图覆盖 / 冒险概率记录 / 地图子命令 / 原型插旗 24 条）与 3.4 的 map 子命令、RunConfig 缺省、裸 gen/board 分支提前到 G2（载体随 G2 删除无法编译）。
- **L17** Sim 读 `Match.Mode` 的日志字段与 `BoardRenderer` 视口分支保留（手写 9×9 夹具仍经 ForMatch / BoardRenderer 走 Sim）；建局分叉已全删。待裁决是否并入 `retire-legacy-rules`。
- 区域强度预算的同区同类型 / 高风险区交叉检查改用同构 9×9 手写图（link:1 上效应不足原口径 1/3）。
- 专家前瞻的记录「本change之前的专家日志照常解析」：旧日志首部的 `"FlagRisk":15,` 读入忽略、写回消失，断言改为去掉这一项后逐字节相同。

**遗留与风险**：`.trellis/spec/core/` 仍有旧地图陈述（`boundaries.md` 26 / 48 / 69、`determinism.md` 19、`testing.md` 83），第 5 组范围外未改，建议归档时处理；tasks.md 的"每组单独提交"子 agent 无法执行，当前是一个工作区整体改动，`MatchFixtures.cs` 与 `decisions-link1-seed3.txt` 基线 git 干净；`snapshots/`（约 4 MB，含完整日志）与 `shots/` 是验证产物，是否入库由主会话决定；`TacticalLayers` Slow 用例名「与完整预演一致_棋盘图中盘」载体已是 link:2，名称未改；`MatchOptions.Mode` 仍对 Sim 可写（Core 友元），公开入口已无从选旧模式。

## 4b（质检后补充，负责人 2026-10-13 裁决 L15 / L19 / L20 / L21）

- **4b.1 L15 根因与修法**：`PlayCommand.Summarize` 遍历拼接**后**画布的全部坐标，拿去索引拼接**前**的盘面。画布变大时直接越界（`ArgumentOutOfRangeException: 坐标超出棋盘范围（43×11）`）；画布没变大时，平移过的棋子会被误报成"落子"和"提走"。修法：在 `RunTurn` 之前记下 `linksBefore = match.Links.Count`；本小回合发生了拼接时，`MovedSince` 用 Core 的唯一换算 `LinkLayout(match.Board.BaseMap.Boards, match.Links.Select(l => l.ShiftStep)).Translate(c, linksBefore, to)` 把拼接前的格平移过去，`Summarize` 改为遍历拼接前的可落子格、与平移后的坐标比对。口径与 Sim `MatchSession.RecordTurn`、图形版 `MatchSession.MovedSince` 相同，没有另写换算。新测试 `终端对局Tests.终端链接局跨过拼接后小回合播报不崩溃且不误报`（link:1、种子 2、本机 3 号棋盘落 1 子后一直 Pass，3 号战场平移贴到 2 号东侧，拼接发生在本机这一手的末尾）断言：这一手照实报 Pass、没有"提走"、子还在（棋串 1）；拼接后对局继续逐手播报；退出码 0。
- **4b.2 L19**：删掉 `MapValidator.ValidateBudgets` 里"区间恰等于人数"时报 `BIRTH_ZONE_COUNT_MISMATCH` 的分支。链接档是 5 区对 4 人，走不到这个分支；出生区数越界仍报 `BIRTH_ZONE_COUNT_OUT_OF_RANGE`，行为不变。
- **4b.3 L20**：删掉 Sim `Program.DefaultPlayerCount`、`DeclaresPlayers` 和 `run` 里调用它们的分支（链接图恒为 4 人，与缺省的 4 名玩家相同）。`PlayCommand` 不给人数即取 4（常量 `LinkPlayers`）；给非 4 的值在入口按 S1 报"链接模式只有 4 人局，实际 N 人。"（原报文"人数须在 2..上限"）。`run --players` 非 4 由建局时的 S1 报错。`PrintUsage` 两处改为"--players <人数，链接模式恒为 4>"。新测试 `各入口按地图标识选图_链接模式Tests.参赛人数不是4即按S1报错` ×2（3 / 5；play 退出 1，run 非 0）。
- **4b.4 L21**：`LinkDecisionSequence.Export` 新增带难度的重载；标准难度不改玩家配置，输出与原重载逐字节相同，三条既有基线照过。新钉 `AiDecision/Fixtures/decisions-link1-easy-seed1.txt`（43 个小回合，截到第 12 大回合）和 `decisions-link1-hard-seed1.txt`（34 个小回合，第 10 大回合 LastBattlefieldSettled），测试 `决策序列基线Tests.链接图其他难度决策序列与基线相同` ×2；口径同既有链接图基线（`link:1`、脚本缺省配对、截到第 12 大回合）。
- **改动文件**：`src/Siege.Sim/Play/PlayCommand.cs`、`src/Siege.Sim/Program.cs`、`src/Siege.Core/Board/MapValidator.cs`、`tests/Siege.Core.Tests/SimulationHarness/终端对局Tests.cs`、`tests/Siege.Core.Tests/SimulationHarness/各入口按地图标识选图_链接模式Tests.cs`、`tests/Siege.Core.Tests/AiDecision/决策序列基线_链接模式Tests.cs`、`tests/Siege.Core.Tests/AiDecision/决策序列基线Tests.cs`、两份新基线文件、`openspec/changes/retire-legacy-maps/tasks.md`（勾 4b.1–4b.4）。
- **测试数**：1975 → **1980** 通过 / 跳过 6 / 失败 0（+1 L15、+2 S1、+2 新基线）。Release 构建与 Godot 工程均 0 警告 0 错误；`openspec validate retire-legacy-maps --strict` 通过。
- **变异**（均实跑后还原）：
  - MUT-4b1：`Summarize` 不传平移映射（恢复旧行为）→ 新终端测试红，报 `ArgumentOutOfRangeException: 坐标超出棋盘范围（43×11）`。
  - MUT-4b4：`AiSearchConfig.Easy` 候选点数 6 → 5、`Hard` 24 → 23 → 两条新基线各红，三条标准难度基线仍绿。
