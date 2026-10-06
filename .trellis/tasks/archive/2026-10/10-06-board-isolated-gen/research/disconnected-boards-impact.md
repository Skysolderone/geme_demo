# Research: 棋盘互不连通（去通道）对全仓的影响面

- **Query**: 取消棋盘间通道、棋盘只隔不可落子场景格（≥2 格）后，哪些代码 / 规格隐含"全图连通"；第一个 change（生成器）必须一并处理什么
- **Scope**: internal（工作树 `feat/board-terrain`）
- **Date**: 2026-10-06

图例：**[错]** = 会出错（异常 / 拒绝加载 / 生成耗尽 / 错误结果）；**[义]** = 只是语义变化（不出错，指标或行为含义变了）；**[无]** = 查过，无影响。

---

## 1. 地图静态校验（`src/Siege.Core/Board/MapValidator.cs`）

调用链：`Validate` 39 行起 → `ValidateExtent` / `ValidatePlateList`（199，失败即短路）→ 其余规则 206–215。距离 / 连通一律经 `MultiSourceDistances`（1131–1161）与 `FloodFill`（1106–1129），二者都沿 `Adjacency.LibertyNeighbors`；场景格是障碍，`IsPlayable` 为假（`Adjacency.cs:59-71`），所以 BFS 天然止于本棋盘边界。**不可达在本文件里一律表示为"字典里没有这个键"→ `int?` 为 `null`**，没有 `int.MaxValue`、没有除法，所以不会溢出或除零；后果全部是"拒绝项"。

| # | 规则 | 位置 | 不连通后 | 类别 |
|---|---|---|---|---|
| 1.1 | 全部棋盘经通道连成一片 | `ValidatePlateConnectivity` 620–660，报 `BOARD_ISOLATED` 654–657；由 `ValidatePlateList` 304 调用 | 以清单里最小下标的棋盘为根做 BFS，除根外**每一块**棋盘都报一条 `BOARD_ISOLATED` → 拒绝加载 | **[错]** |
| 1.2 | 出生区到中央入口有通路（规则第 7 条） | `ValidateBirthZoneConnectivity` 981–996，`BIRTH_ZONE_ISOLATED` 991 | 出生棋盘上不可能有中央入口（入口在最大公共棋盘中心，`BoardMapLayout.cs:758`）→ 每个出生区一条 → 拒绝 | **[错]** |
| 1.3 | 三类距离目标不可达（规则第 1 条后半） | `DistanceTable` 1004–1045（`null` = 不可达，1029–1038）；`ValidateDistanceBalance` 1053–1104，`LANDMARK_UNREACHABLE` 1070–1076 | "最近公共信物"：公共信物只在公共棋盘（`BoardMapLayout.cs:761-773`）→ 每区 `null` → 拒绝；"中央入口"：同上 → 拒绝；"最近咽喉"：见 1.4 | **[错]** |
| 1.4 | 必须显式标注咽喉 | `ValidateLandmarks` 907–913，`CHOKE_NOT_ANNOTATED` | 生成器把"通道格"标为咽喉（`BoardMapGenerator.cs:269-272`）。去掉通道后 `ChokePoints` 为空 → 拒绝。若改为在公共棋盘上另标咽喉，则 1.3 的"最近咽喉"变成不可达 → 仍拒绝；只有咽喉落在每个出生棋盘上，或目标集合为空（1062–1065 跳过）才过 | **[错]** |
| 1.5 | 距离极差 | 1084–1102；棋盘档 `DistanceHandling.AlwaysReport`（127） | 只在三项都可达时才算极差；不可达时 1079–1082 `continue`，不会走到 `ds.Min()`，无异常 | [义]（报告项会消失） |
| 1.6 | 通道规则（直条、两端贴两块不同棋盘、宽 3–4、长 2–4、高度 0） | `ValidateCorridors` 480–617；`PlateRules.CorridorWidth/CorridorLength`（130、160–165） | 没有"不属于棋盘的可落子格"时外层循环 488–616 全部 `continue`，不报任何项。规则本身成为死代码 | [义] |
| 1.7 | 场景里有可落子格 | 同上 514–523 `SCENERY_CELL_PLAYABLE` | 仍然生效：任何棋盘外的可落子格都被拒（这正是新约束想要的） | [无] |
| 1.8 | 必死口袋（规则第 2 条） | `ValidatePockets` 948–975；阈值 `MapData.MinTwoEyeArea` = 8（`MapData.cs:67`） | 连通分量 = 整块棋盘；出生棋盘最小 5×5 = 25 ≥ 8 → 不报。注意口径：现在"口袋"就是整块出生棋盘 | [义] |
| 1.9 | 保护期容量（9 枚） | `ValidateBirthZones` 803–810 | 只数出生区内可落子格，与连通无关 | [无] |
| 1.10 | 棋盘两两至少隔 2 格 | `ValidatePlateGaps` 404–425，`MinGap: 2`（130） | 与新裁决"至少隔 2 格"一致 | [无] |
| 1.11 | 棋盘边长 | `ValidatePlateCells` 333–341，`Side (5,15)` / `BirthSide (5,7)`（130） | 公共 7–15 落在 5–15 内，校验器不拦；但规格写的是 5–15，若要校验"公共 ≥7"须另加 | [义] |
| 1.12 | 规模预算只有 4 人 | 声明行 120–130：`[4] = new(250, 1000, 7, 20, (5, 5), (25, 49))` | 2 / 3 人图 → `ValidateBudgets` 740–747 报 `UNSUPPORTED_PLAYER_COUNT` 拒绝；`ValidatePlayableCount` 723–726 静默跳过 | **[错]**（支持 2/3 人的前置） |
| 1.13 | 信物格数 7–20 | 750–760 | 2 人：3 出生 + 一块 11×11 的 3 个 = 6 < 7 → 拒；4 人多块小公共棋盘：如 12 块公共 ×2 + 5 = 29 > 20 → 拒 | **[错]**（取决于参数） |
| 1.14 | 出生区数 | 763–788，`(5,5)` + `ZonesMustExceedPlayers: true`（125） | 人数 + 1 的规则对 4 人等价；2/3 人需各自的 `(3,3)` / `(4,4)` 行 | **[错]**（同 1.12） |
| 1.15 | 外接尺寸 20–50 | `ValidateExtent` 224–270，`ColumnRange/RowRange (20,50)`（128–129） | 2 人图（3 块出生 + 1 块公共）若排成一长条，短边 < 20 → `MAP_TOO_SHORT/NARROW` 拒绝；生成器 `Crop` 也同样作废（`BoardMapLayout.cs:707-711`） | [义]→可能**[错]**（2 人成功率） |

校验器类注释 88–90 行、`ValidatePlateList` 注释 273–275 行、`ValidateCorridors` / `ValidatePlateConnectivity` 注释 476–479、619 行都写着"按清单规则逐块校验棋盘与通道""全部棋盘经通道沿气边连成一片"。

**`Siege.Sim map` 命令**（`src/Siege.Sim/Program.cs:198-203`）复用 `DistanceTable`，`null` 打印为 `-`，不会出错 **[义]**；但地图根本过不了校验，所以实际走不到。

**边疆档生成器**：`FrontierMapGenerator.cs:182` 用 `DistanceTable(map)[1]`，与棋盘档无关 **[无]**（随 ③ 删除）。

---

## 2. AI（`src/Siege.Core/Ai/`）

**结论：AI 里没有任何路径距离、BFS、最短路，也不读 `CentralEntrance` / `ChokePoints` / `Boards`。** 证据：在 `src/Siege.Core/Ai/*.cs` 中 grep `Queue<`、`Distance`、`Manhattan`、`Math.Abs`、`RelicCells`、`Boards`、`ChokePoints`、`CentralEntrance` 均无命中；唯一的 `long.MaxValue` 在 `HeuristicTurnController.cs:162`（征募 / 弃牌时"被替换者价值"的初值，与地图无关）。

九维逐项（`BatchEvaluator.cs`）：

| 维度 | 位置 | 依据 | 不连通影响 |
|---|---|---|---|
| PowerGain / EnemyLoss | 153–164 | `PowerCalculator.Compute` 前后差 | [无] |
| Relic | 168、279–298 | `CoverageMap.OwnershipOf(relic.Coord)`，只看信物格当前归属 | [无]（不看到信物的距离） |
| Safety | 173 / 184、`GroupSafety.cs:82-107` | 气数、分散气（`LibertyNeighbors`）、眼值 | [义]：棋盘边界由场景格构成，等价于围棋盘边，气数少一侧 |
| Growth | 169、427–447 | 倍增 / 连珠 / 协同 | [无] |
| Initiative | 170、253–257 | 势力名次 | [无] |
| Supply | 142、263–273 | 库存与部署上限 | [无] |
| Eye | 181、375–395 | `LifeShapeReport` | [义]：见 3.5 |
| Threat | 185、401–420 | 敌串气数 ≤ 3 | [无] |

**候选格**：`HeuristicTurnController.cs:352` 取 `context.LegalRange ∩ IsPlayableEmpty`，`CandidateCellLimit` 时经预筛（355、408–437）。保护期后合法范围 = 全图可落子格（见 3.2），因此 AI 会把**所有**棋盘的空格当候选，没有"离己方远近"的偏好或惩罚 **[义]**：在互不连通的地图上，AI 是否倾向在空公共棋盘上开局、是否会"撒子"，只由九维增量决定；需要校准（母任务已把 AI 校准放到 ③ 之后）。

**专家前瞻**：`ExpertLookahead.cs:302-326`（`NextMoveContext`）、`533-547`（`SimulatedContext`）都经 `PublicRules.LegalRange`，没有距离计算 **[无]**。

**信物估值**：`RelicEstimate.cs` 按分区先验（出生 / 公共 / 高档），不看位置 **[无]**。

---

## 3. 对局流程

| # | 项 | 位置 | 结论 |
|---|---|---|---|
| 3.1 | 插旗 | `FlagPlanting.cs`（`Plant` 135–146、`LockAll` 159–176、`LeastPopulatedZone` 197–213） | 只按出生区编号与旗数，**[无]** |
| 3.2 | 保护期 / 合法落子范围 | `PublicRules.LegalRange` `PublicRules.cs:27-38`：第 1–3 大回合 = `BirthZones[zone]`，此后 = 全图 `Terrain.Playable` | 不看连通。保护期后任意棋盘都可直接落子（与裁决"保护期之后可在任一棋盘落子"一致）**[无]**；语义上：玩家不再需要"走出去"，出生棋盘在第 4 大回合起即可被他人直接空降 **[义]** |
| 3.3 | 原型选区 | `PrototypeZoneAssignment.cs:50-63`：`zoneCount > map.MaxPlayers` 时随机抽空闲区，否则顺排 | 人数 + 1 个出生区且 `MaxPlayers` = 人数 → 走随机分支；**目前 `MaxPlayers` 被硬编码为 4**（`BoardMapGenerator.cs:298`），2/3 人图若不改，2/3 人局仍按 4 人上限判定 **[错]**（支持 2/3 人时必须改） |
| 3.4 | 人数上限 | `MatchFlow.cs:108-111` 玩家数 > `MaxPlayers` 抛 `ArgumentException` | 2/3 人图 `MaxPlayers` 正确设置后自然限制；图形版用 `Math.Min(4, map.MaxPlayers)`（`GameRoot.cs:324`、`GameRoot.MapSelect.cs:63/294`）**[无]** |
| 3.5 | 活形 / 眼 | `LifeShape.cs`：`EyeSpaceMax = 12`（82），封闭判定 307–313（贴边棋串同属一人、至少贴一串、格数 ≤ 12），早停 149、197 | 场景格不可落子，等同棋盘边。原先出生棋盘的空区可经通道"漏"出去（不封闭），现在边界全封 → 5–7 边长的出生棋盘角落更容易形成封闭眼空间 / 已确定活形 **[义]**（HANDOFF.md:115 记录过 2 人图"贴墙做眼"导致无提子，此处可能被放大） |
| 3.6 | 提子 / 气 / 棋串 | 全部经 `Adjacency.LibertyNeighbors`（`Adjacency.cs:59-71`） | 局部规则，**[无]**；棋串不可能跨棋盘（裁决本意） |
| 3.7 | 覆盖 / 领地 | `CoverageMap`（覆盖目标唯一实现 `Adjacency.CoverageTargets`；唯一的"隔格"情形是一格宽深水） | 棋盘档无深水（`MapValidator.cs:456-463`），棋盘间隔 ≥2 → 覆盖不跨棋盘 **[无]** |
| 3.8 | 匠人改造 | `TerrainEditRules.LegalTargets` 44–79：搭桥（深水）、烧林（林地）、立栅 | 棋盘档无深水 / 林地；立栅只能切断不能连通 → 对局中不可能把两块棋盘连起来 **[无]** |
| 3.9 | 终局"棋盘填满" | `MatchFlow.cs:586-610` → `GameBoard.HasPlayableEmptyCell`（`GameBoard.cs:322`，全图任一可落子空格） | 全局判定，不看连通 **[无]**；语义上：一块没人去的空公共棋盘就会阻止"棋盘填满"，终局更可能走"整轮 Pass" **[义]** |
| 3.10 | 出局 | `PublicRules.IsEliminated` 41、`Eliminated` 47–53：曾建立正势力且总势力为 0 | **[无]** |
| 3.11 | 通道 / 咽喉标记在流程里的使用 | grep `ChokePoints` / `CentralEntrance` 于 `src/Siege.Core` 非地图目录：只有 `MapFile.cs`（读写 49–50、106–107）、`MapSymmetry.cs:140-151`（只由标准档基准图测试调用）、各生成器 | 对局结算、信物、AI 都**不读**咽喉与中央入口 **[无]**。`relic-generation/spec.md:86` 提到"交通咽喉承担更高预算"，但代码里信物档位只来自地图的 `RelicCellSpec`（`RelicGenerator.cs`），不读 `ChokePoints` |

---

## 4. 呈现与 Godot

| 项 | 位置 | 结论 |
|---|---|---|
| 通道染土路色 | `src/godot/scripts/BoardView.cs:190-193`（"可落子且不属于任何棋盘即通道"）、280–283（`Visuals.CorridorPath`）、`Visuals.cs:105-106` | 没有通道格时这段永不触发 **[义]**（死代码）；规格 `visual-style-baseline/spec.md:183` "通道 SHALL 呈现为窄路" 无对象可验 |
| 棋盘台面 | `BoardView.cs:382`、1036–1080（`AddBoardPlates`） | 只读清单 **[无]** |
| 镜头回家 / 开局对准 | `GameRoot.cs:1643-1690`（`FocusHome`/`HomeCells`/`HomePlatformFramed`）、`Siege.Presentation/Camera/CameraInput.cs:36-42`（`Platform` 取出生区格子外接矩形） | 只用出生区格子坐标，**[无]** |
| 镜头跟随 | `Siege.Presentation/Camera/CameraFollow.cs` | 纯状态机，目标点来自落点坐标，**[无]** |
| 截图取景 | `GameRoot.cs:677-693`（`--shot-board` 读清单下标） | **[无]** |
| 选图界面棋盘数 | `MapSelectModel.cs:71、98、132、135、254-263、291` 绑定 `BoardMapParameters.MinBoards/MaxBoards/DefaultBoards` 与 `new BoardMapParameters { BoardCount }`；`MapPreviewInfo.cs:20-26` 显示"出生 n、公共 m" | 参数形状（人数、公共块数区间）一改即需同步；不涉及连通 **[义]**/编译级 |
| 图形版自检 | `GameRoot.MapSelect.cs:208-227` 断言 `Boards.Length == model.BoardCount`、标识以 `:n<N>` 结尾当且仅当非缺省 | 标识格式变了要跟着改 |
| 裸 `board` 取种子 | `GameRoot.cs:309-311`、`Siege.Sim/Program.cs:143-146` 用 `BoardMapParameters.Default` | 参数形状变化需同步 |

`src/Siege.Presentation` 与 `src/godot/scripts` 中没有任何 BFS / `Queue<` / `LibertyNeighbors` 调用（grep 无命中），不存在依赖连通的呈现逻辑。

---

## 5. Sim 分析（`src/Siege.Sim/Analysis`）

- `BalanceAnalyzer.cs`、`ReportWriter.cs`、`Statistics.cs` **全部基于日志**，没有任何几何 / 距离指标（grep `Queue<`、`Distance`、`Neighbors`、`Choke`、`CentralEntrance` 无命中）。
- **"被封在出生棋盘内""通道被封死"不是代码里的指标**：只出现在归档任务的人工冒烟口径里（`openspec/changes/archive/2026-09-30-board-map/tasks.md:26、39`，`design.md:43、85`）与规格的裁决说明（`openspec/specs/map-generation/spec.md:178`）。去通道后这两个口径失去意义 **[义]**。
- `PlatformSideSection`（`BalanceAnalyzer.cs:170-178`）：边长 5–9 各一行；数据来自 `MatchSession.cs:766` 的 `ZoneSides`（`SideOf` 814–818 = 出生区外接矩形较长边），出生棋盘 5–7 落在范围内 **[无]**。
- `BirthZoneSection`（153、1455–1470）：基线按 1/参赛人数，区数取日志首部 `ZoneCount`（`MatchSession.cs:763`）**[无]**。
- `MapScaleSection`（56–66）：只统计可落子格与信物格数 **[无]**。
- `Program.cs:382` 用 `MapCatalog.Resolve(...).MaxPlayers` 决定人数上限，2/3 人图依赖 `MaxPlayers` 正确。

---

## 6. 生成器本身

### 6.1 现行摆法对通道的依赖（`src/Siege.Core/Board/Maps/BoardMapLayout.cs`）

| 结构 | 位置 | 去通道后 |
|---|---|---|
| 摆法总纲："每块新棋盘贴在已摆公共棋盘外侧，间隔 2–4、投影重叠 ≥ 通道最小宽度，随即开一条直通道" | 类注释 8–15；`PlaceBoards` 298–352 | 生长式仍可用来保证紧凑，但"开通道"一步与"重叠 ≥3"的约束失去理由 |
| 通道常量 | `MinCorridorLength/MaxCorridorLength` 40–41、`MinCorridorWidth/MaxCorridorWidth` 44–45 | `Placements` 用 `MinCorridorLength..MaxCorridorLength` 作间隔（477）、`MinCorridorWidth` 作投影重叠下限（483）——须改为"间隔 ≥ 2"的独立常量，否则仍把棋盘之间的间隔限死在 2–4 |
| 每盘通道数 | `BirthMinLinks/BirthMaxLinks/PublicMaxLinks` 55–57、`MaxLinks` 137、`_degree` 97 | 全删 |
| 通道余量 | `CorridorReserve = 12` 69；`TrySampleSizes` 168–169 `upper = maxPlayable − links × 12` | **[错]** 若保留：上界被白白压低（4 人 n=7 时 11 条 × 12 = 132 格），目标带上沿约 668 而不是 800 |
| `Attach` 要求至少有一条可开的通道 | 357–379（364–369：`corridors.Count == 0` 即弃该位置） | 须改为只看 `Fits` |
| 出生棋盘双出路 | `AttachBirth` 384–424、`Partners` 513–567、`Linked` 569–580、修补环 331–348、`DistinctNeighbors` 279–294 | 全删 |
| 通道落格 | `Connect` 446–459、`Corridor = -1` 91、`Corridors` 612–643、`CorridorFits` 649–675、`Fits` 601 的"离通道 1 格"判断 | 全删；`Grid` 只剩 0 / 棋盘号 |
| 裁切 | `Crop` 683–741（729–733 平移 `Links`） | 去掉通道平移；外接范围只由棋盘决定 |
| 可落子数 | `PlayableCount` 261–276（棋盘 + 通道） | 变为 Σ 棋盘面积 |
| 出生棋盘数 | `TrySampleSizes` 146–166、227 读 `BoardMapParameters.BirthBoards`（=5，`BoardMapParameters.cs:22`） | 须改为人数 + 1 |
| 第一块公共棋盘边长 | 153–154：下标 == BirthBoards 的那块 floor = ceiling = `AnchorSide` | 见 6.2 |

`BoardMapGenerator.cs` 里与通道 / 4 人绑定的点：

| 位置 | 内容 | 去通道 / 2–3 人后 |
|---|---|---|
| 13 | `GeneratedBoardMap` 带 `Links`（公开记录字段） | 删字段；测试 `BoardGenFixtures.cs:29` 经 `GenerateDetailed` 取结果 |
| 35–38 | `TargetMinPlayable = 300`、`TargetMaxPlayable = 800` | 须按人数取 300–800 / 225–600 / 150–400 |
| 69–73 | 测试入口要求 `fixedBirths.Length == 5` | 须改为人数 + 1 |
| 141 | `layout.Links.Select(...)` | 删 |
| 149–245 `CheckLayout` | 180–184 每盘通道数（出生 ≥2）；186–203 出生棋盘 ≥2 个邻盘、直通公共棋盘；171–172 公共边长取 `PublicMinSide/PublicMaxSide`（9/11）；178、232–235 锚盘 11×11；205 信物数 `SmallPublicSide` | **[错]**：不改则每次尝试都在 180–184 作废 → 64 次后抛 `MapGenerationException`（87–88） |
| 248–307 `ToMapData` | 253 `zones` 长度 = `BirthBoards`；269–272 通道格 → `ChokePoints`；298 `MaxPlayers = 4` | **[错]**：咽喉为空 → `CHOKE_NOT_ANNOTATED`（见 1.4）；`MaxPlayers` 必须 = 人数 |

`BoardMapParameters.cs`：`MinBoards = 7`（13，理由是"唯一公共棋盘要接 5 条通道"——通道没了理由也没了）、`MaxBoards = 10`、`DefaultBoards = 7`、`BirthBoards = 5`、`PublicBoards = BoardCount − 5`；没有人数字段。`BoardMapId`（53–126）格式 `board:<种子>[:n<棋盘数>]`，没有人数段；`FormatHelp` 58–60 写死"7–10、出生固定 5"。`MapCatalog.cs:84` 的报错文案引用 `MinBoards/MaxBoards`。

### 6.2 尺寸与数量的算术（出生 5–7、公共 7–15）

出生棋盘面积区间：4 人 5 块 125–245；3 人 4 块 100–196；2 人 3 块 75–147。

若保留"第一块公共棋盘恰为 11×11（121 格）"，其余公共棋盘取最小 7×7（49 格）来求块数上限、取最大 15×15（225 格）求下限：

| 人数 | 目标带 | 只有锚盘时的总量 | 公共棋盘块数可行区间 | 说明 |
|---:|---|---|---|---|
| 4 | 300–800 | 246–366 | 1–12（125 + 121 + 11×49 = 785 ≤ 800；12 块其余为 7×7 时 834 > 800） | 下界 300 需出生棋盘抽样上调到 ≥ 179 格才只靠一块锚盘；2 块起无压力 |
| 3 | 225–600 | 221–317 | 1–8（100 + 121 + 7×49 = 564；8 块其余为 7×7 时 613 > 600） | |
| 2 | 150–400 | 196–268 | 1–5（75 + 121 + 4×49 = 392） | 有锚盘时总量**至少 196**，目标带 150–195 这一段永远取不到 [义] |

（块数上限按"其余全取 7×7"求得，是理论上限；实际抽样后由收敛环 171–225 再缩放。）

### 6.3 现行硬约束与新尺寸的缺口

- **`AnchorSide = 11`**（`BoardMapLayout.cs:52`；`CheckLayout` 178、232–235；规格 `map-generation/spec.md:176`）：当时是"公共 9–11 且至少一块 11×11"。公共放宽到 7–15 后，这条只是保留下来的约束；面积最大的公共棋盘可能是 12–15 的那块，中央入口随之移到那里（`PlaceRelics` 748–758）。保留与否是设计问题，代码上二者都能跑。
- **信物数按边长分档**：`SmallPublicSide = 10`（60），`want = 较小边 ≤ 10 ? 2 : 3`（764；`CheckLayout` 205）。按现有公式，**7–8 会落进"≤10 → 2 个"一档**，代码不会报缺口；规格（`map-generation/spec.md:180`）只写了"9–10 两个、11–15 三个"，**7–8 没有定义**，需要裁决。
- **7×7 放两个信物会偶发失败**：`PlaceRelic` 789–811 只在最外一圈以内取点（7×7 → 内部 5×5），`RelicSpacing = 3`（63，切比雪夫距离）。若第一个信物落在 7×7 的正中格，内部其余格到它的距离最多 2 → 第二个放不下 → `PlaceRelics` 767–770 作废本次尝试。8×8（内部 6×6）不存在这个死角。属于成功率问题 [义]，不是异常。
- **信物格总数预算**：校验器 4 人 7–20（`MapValidator.cs:124`）。多块小公共棋盘时容易超出 20（如 4 人 8 块公共 ×2 + 高档盘多 1 + 5 ≈ 22）；2 人单锚盘时 3 + 3 = 6 < 7。**[错]**，需要按人数重新给区间。
- **2 / 3 人出生棋盘的摆法**：现行代码只有"出生棋盘贴在公共棋盘外侧"一种（`Placements` 466–503 的宿主只取公共棋盘）。去通道后任何"贴着谁"都只是为了紧凑；2 人时 3 块出生 + 1–5 块公共，若公共只有 1 块，3 块出生都挤在锚盘四边，外接矩形可能 < 20 行 / 列 → `Crop` 707–711 作废（同时校验器 `ValidateExtent` 也会拒，见 1.15）。`MinMapSide = 20` 是 4 人时代定的。
- **对称 / 公平**：现行生成器不做出生棋盘之间的公平约束（距离只作报告项，`DistanceHandling.AlwaysReport`）。不连通后"出生棋盘到公共信物 / 中央入口的距离"这一报告口径不存在了，生成器没有别的出生公平量。

---

## 7. 规格里必须改或删的条目

### `openspec/specs/map-generation/spec.md`

| 位置 | 条目 | 处理 |
|---|---|---|
| 139–145 | Requirement「棋盘档生成参数」正文：棋盘数 7–10 / 缺省 7 / 出生固定 5 / 公共 = 棋盘数 − 5（至少 2，理由是通道数）/ 人数固定 4 / "取全部棋盘与通道的实际外接范围" | 改：人数 2/3/4、出生 = 人数 + 1、公共块数按人数给区间、外接范围只看棋盘 |
| 147–153 | Scenario「棋盘数越界」「棋盘数 6 不再合法」 | 改或删（范围换了） |
| 155–157 | Scenario「图面不留多余空白」："最靠外的棋盘或通道格" | 改措辞（只剩棋盘） |
| 159–161 | Scenario「缺省棋盘数」：7 块 = 5 + 2 | 改 |
| 163–169 | 「同种子同图」「边疆档生成图不变」 | 前者保留（加人数），后者随 ③ 删除 `gen:` 再处理 |
| 171–182 | Requirement「棋盘档布局规则」正文：出生 5 块；公共 9–11 且至少一块 11×11；目标带 300–800（棋盘 + 通道）；每块出生 2–4 条通道、通向两块不同棋盘、直通公共；公共 1–6 条通道；**通道格 MUST 标为咽喉**；信物 9–10 两个 / 11–15 三个 | 改：出生 = 人数 + 1；公共 7–15；锚盘条款去留；目标带按人数；通道三条删除；咽喉的来源重定；信物 7–8 档补齐 |
| 184–190 | Scenario「生成图通过校验」「规模落在目标带」：种子 1–20 × 棋盘数 7–10、300–800 | 改（参数与带按人数） |
| 192–194 | 「棋盘无障碍」：宽高不小于 5 | 可保留 |
| 196–198 | 「棋盘之外只有通道与场景」 | 改为"棋盘之外只有场景（障碍）" |
| 200–202 | 「出生棋盘直通公共棋盘」 | 删 |
| 204–206 | 「全部棋盘连通」 | 删（或反转为"不同棋盘的格子之间不存在四邻接"——母任务跨子任务验收第 2 条） |
| 208–210 | 「资源布点」：7 块、9×10 与 11×11、共 10 个 | 改（例子要换成新尺寸） |
| 216–240 | Requirement「棋盘档生成图标识」：`board:<种子>[:n<棋盘数>]`、"棋盘数范围 7–10" | 改：参数段要能表达人数（及公共块数）；非法标识报文范围 |

### `openspec/specs/map-definition/spec.md`

| 位置 | 条目 | 处理 |
|---|---|---|
| 120 | 「地图静态校验规则」第 1 条：三类目标不可达两档都拒绝 | 棋盘档须改为不适用 / 另立口径（否则 1.3 拒绝） |
| 126 | 第 7 条：任一出生区必须有通路到中央入口 | 棋盘档须豁免（否则 1.2 拒绝） |
| 121 | 第 2 条 必死口袋 | 可保留（对棋盘档恒过） |
| 140–142、156–158 | Scenario「边疆档不可达仍拒绝」「出生区被孤立」 | 标准 / 边疆档仍适用；③ 删除旧地图后再评估 |
| 288 | 「棋盘清单」：清单"只用于静态校验与呈现" | 保留 |
| 300–302 | Scenario「清单不参与结算」 | 保留 |
| 304–324 | Requirement「棋盘档预算与校验」：第 1 条边长 5–15（公共下限是否收到 7）；**第 4 条通道定义与 3–4 宽、2–4 长**；**第 5 条全部棋盘经通道连成一片**；第 6 条"不属于棋盘与通道的格子必须是障碍"；第 7 条"通道格不得是信物格"；第 9 条规模表只 4 人（250–1000 / 5 / 25–49 / 7–20）；末段"两档共用规则（到中央入口可达、目标不可达即拒绝）对棋盘档同样生效" | 第 4、5 条删；第 6、7 条去掉"通道"；第 9 条补 2/3 人行；末段把"到中央入口可达""目标不可达即拒绝"从棋盘档剔除 |
| 334–340 | Scenario「通道过宽」「通道过窄」 | 删 |
| 342–344 | Scenario「棋盘被孤立」 | 删（或反转） |
| 346–348 | Scenario「场景里有可落子格」："既不属于任何通道也不是障碍" | 改措辞，保留 |
| 350–352 | Scenario「合法棋盘图」：5 出生 + 2 公共、"通道与规模全部合规"、"报告项中给出每个出生区到三类目标的距离" | 改（距离报告不再存在） |
| 182–204 | Requirement「地图规格档」及 Scenario「棋盘档读写往返」 | 不涉及通道，保留 |

### 其他规格

- `openspec/specs/visual-style-baseline/spec.md:183`："通道 SHALL 呈现为窄路，外观与棋盘台面不同"；185、285 "棋盘与通道之外的场景格" —— 措辞需去掉通道（可放到后续 change）。
- `openspec/specs/relic-generation/spec.md:86`："交通咽喉承担更高的强度预算" —— 只是描述，代码不读咽喉。

---

## 8. 测试（受影响的现有文件，供拆分参考）

- `tests/Siege.Core.Tests/MapGeneration/棋盘档布局规则Tests.cs`（14 个用例；50–53 Σ面积 + 通道格；118–153 通道形状与"咽喉 == 通道格"146；167 起出生棋盘双出路；244 用 `fixedBirths`）
- `tests/Siege.Core.Tests/MapGeneration/棋盘档生成参数Tests.cs`（13）、`棋盘档生成图标识Tests.cs`（6）、`地图随机源Tests.cs`（48 处引用 board 参数）
- `tests/Siege.Core.Tests/MapDefinition/棋盘档预算与校验Tests.cs`（18；含通道宽窄、孤立）
- `tests/Siege.Core.Tests/BoardGenFixtures.cs`（`Corridors` 45–90 从地图反推通道；文本图 `:` 表示通道 92）
- `tests/Siege.Core.Tests/BoardMapFixtures.cs`（29×13 的 2 人手工棋盘图，带两条 3×3 通道；被 `棋盘清单Tests`、`地图规格档Tests`、`棋盘档预算与校验Tests` 引用）
- `tests/Siege.Core.Tests/MapSelection/选图视图模型Tests.cs`、`SimulationHarness/各入口支持生成图Tests.cs`（棋盘数 / 标识）

---

## 9. 按风险排序的清单

| 排名 | 项 | 类别 | 归属 |
|---:|---|---|---|
| 1 | `CheckLayout` 的通道数 / 双出路 / 直通公共检查（`BoardMapGenerator.cs:180-203`）→ 每次尝试作废 → `MapGenerationException` | 错 | **① 必须** |
| 2 | `ValidatePlateConnectivity` → `BOARD_ISOLATED`（`MapValidator.cs:620-660`） | 错 | **① 必须** |
| 3 | `ValidateBirthZoneConnectivity` → `BIRTH_ZONE_ISOLATED`（981–996） | 错 | **① 必须**（棋盘档豁免需走规格档声明表，守门测试要求规格档分支只出现在 `Rules` 表 93–131 行，见 80 行注释） |
| 4 | `ValidateDistanceBalance` → `LANDMARK_UNREACHABLE`（1053–1104） | 错 | **① 必须**（同上，走声明表） |
| 5 | `ChokePoints` 为空 → `CHOKE_NOT_ANNOTATED`（907–913；来源 `BoardMapGenerator.cs:269-272`）；且咽喉与第 4 项联动 | 错 | **① 必须**（需裁决：棋盘档咽喉标什么，或棋盘档免标） |
| 6 | 棋盘档预算只有 4 人：`UNSUPPORTED_PLAYER_COUNT`、出生区数、信物数（`MapValidator.cs:120-130、738-788`） | 错 | **① 必须**（支持 2/3 人） |
| 7 | `MaxPlayers = 4` 硬编码（`BoardMapGenerator.cs:298`）→ 选区分支（`PrototypeZoneAssignment.cs:63`）与人数上限（`MatchFlow.cs:108`）跟着错 | 错 | **① 必须** |
| 8 | `TrySampleSizes` 的 `CorridorReserve` 扣减（`BoardMapLayout.cs:168-169`）、`BirthBoards = 5`、目标带常量（`BoardMapGenerator.cs:35-38`） | 错（规模错） | **① 必须** |
| 9 | 摆法里的通道 / 间隔 / 重叠约束（`Placements` 477–483、`Attach` 364–369、`AttachBirth`/`Partners`/修补环） | 错（摆不出或间隔被限死 2–4） | **① 必须** |
| 10 | 信物数分档 7–8 未定义；7×7 正中首信物致第二个放不下（`PlaceRelic` 789–811） | 义 / 成功率 | **① 必须裁决**（分档）；成功率 ① 内顺带 |
| 11 | 2 人图外接尺寸 < 20（`MinMapSide` 33、`ValidateExtent`） | 成功率 / 可能错 | **① 应处理** |
| 12 | 标识格式与参数形状（`BoardMapParameters` / `BoardMapId`、`MapCatalog.cs:84`、`MapSelectModel`、`GameRoot.cs:311`、`Program.cs:146`、`GameRoot.MapSelect.cs:208-227`） | 编译 / 接口 | **① 必须**（Core 侧与直接调用方；选图界面的多人数选择可放 ②） |
| 13 | 规格「棋盘档生成参数 / 布局规则 / 生成图标识 / 棋盘档预算与校验」及第 1、7 条共用规则对棋盘档的适用（第 7 节清单） | 规格 | **① 必须**（与代码同 change） |
| 14 | `ValidateCorridors` / `PlateRules.CorridorWidth/Length` 成为死代码 | 义 | ① 可顺手删，也可留 |
| 15 | `BoardView.cs` 通道染色死代码、`visual-style-baseline` "通道窄路" 措辞 | 义 | 后续（② 或 ③） |
| 16 | 活形在封闭小棋盘上更易成立（`LifeShape.cs` 封闭判定）、AI 候选覆盖所有棋盘无距离偏好、"棋盘填满"更难达成 | 义 | AI 校准 change（③ 之后） |
| 17 | 冒烟口径"被封在出生棋盘内 / 通道被封死"失效；`Siege.Sim map` 距离表全为 `-` | 义 | ②（冒烟口径重定）|
| 18 | `MapSymmetry`、`FrontierMapGenerator` 的 `DistanceTable` 用法、旧地图规格条目 | 无 / 旧图 | ③ |

## Caveats / Not Found

- 未在代码里找到"被封在出生棋盘内"的自动指标；它只存在于归档任务的人工冒烟口径中。
- AI 九维与专家前瞻中未找到任何路径距离 / BFS / 可达性计算，所以不存在"不可达返回 int.MaxValue / 溢出"的风险；本结论基于 `src/Siege.Core/Ai/*.cs` 全文 grep 与 `BatchEvaluator.cs`、`GroupSafety.cs` 的通读。
- 第 6.2 节的块数区间是按"其余公共棋盘全取 7×7 / 锚盘恒为 11×11"的算术上限，没有实际跑生成器验证摆放是否放得下（工作区 50×50、`MaxMapSide` 50）。
- 规格档守门测试（`MapValidator.cs:79-81` 注释提到的 `规格档分流守门Tests`）会约束棋盘档豁免的写法；本次未读该测试全文。
