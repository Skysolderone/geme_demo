# Research: 要删的产品代码与地形系统的去留（问题 1、2）

- **Query**: 删除 v5 / 2p / 3p / 边疆 / `gen:` 及 `maps/*.json` 后，哪些产品代码随之成为死代码、哪些仍有来源；地形规则与呈现在只有棋盘图时是否可达
- **Scope**: internal
- **Date**: 2026-10-06
- **基线**: 工作树 `feat/board-terrain` = main `7be3f3b`

---

## 1. 直接删除的产品代码（删掉即可，无其他产品调用方）

| 文件 | 行数 | 说明 / 被引用处 |
|---|---:|---|
| `src/Siege.Core/Board/Maps/FourPlayerBaseMap.cs` | 195 | `Id = "siege-4p-base-v5"`（:30）；仅 `MapCatalog.cs:34` 引用 |
| `src/Siege.Core/Board/Maps/TwoPlayerBaseMap.cs` | 40 | 读嵌入资源 `siege-2p-base-v1.json`；仅 `MapCatalog.cs:35` |
| `src/Siege.Core/Board/Maps/ThreePlayerBaseMap.cs` | 40 | 读嵌入资源 `siege-3p-base-v1.json`；仅 `MapCatalog.cs:36` |
| `src/Siege.Core/Board/Maps/FrontierMapV2.cs` | 227 | 仅 `MapCatalog.cs:37` |
| `src/Siege.Core/Board/Maps/FrontierMapGenerator.cs` | 353 | 仅 `MapCatalog.cs:80`。**注意**：`MapGenerationException`（:37）定义在此文件，`BoardMapGenerator.cs:89` 与 `src/godot/scripts/GameRoot.cs:335`、`GameRoot.MapSelect.cs:104`、`src/Siege.Sim/Program.cs:53` 在用，须先迁出 |
| `src/Siege.Core/Board/Maps/FrontierMapLayout*.cs`（8 个分部：主体、Checks、Corridors、Fill、Platforms、Ramps、Relics、River） | 1884 | 只被 `FrontierMapGenerator` 用 |
| `src/Siege.Core/Board/Maps/FrontierSurfaces.cs` | 186 | `:s1` 新地表投放，只被边疆生成器用 |
| `src/Siege.Core/Board/Maps/MapGenParameters.cs` | 177 | `MapGenParameters` + `GeneratedMapId`。**注意**：`GeneratedMapId.FriendlySeed`（:93–107）也被棋盘图入口用：`src/godot/scripts/GameRoot.cs:318`、`GameRoot.MapSelect.cs:39`、`src/Siege.Sim/Program.cs:23`，须迁到 `BoardMapId` 或别处 |
| `src/Siege.Core/Board/MapSymmetry.cs` | 161 | 产品代码零调用（`MapValidator.cs:94` 注释写明"只由标准档基准图的测试调用"） |
| `src/Siege.Core/Board/Maps/MapRandom.cs` 的 `ForAttempt`（:17）、`ForSurfaces`（:39） | 约 30 | 只供边疆生成器；`ForBoardAttempt`（:54）保留 |
| 合计 | 约 3290 行 | |

数据与资源：
- `maps/` 下 7 个 json：`siege-2p-base-v1`、`siege-3p-base-v1`、`siege-4p-base-v1/v2/v3/v5`、`siege-frontier-v2`（共约 25 KB）。
- `src/Siege.Core/Siege.Core.csproj:17–21`：两条 `EmbeddedResource`（2p / 3p json）连同注释删除。
- 附带发现：`maps/siege-4p-base-v1/v2/v3.json` 现在还在仓库里，而 `MapCatalog.Resolve` 的 `maps/<标识>.json` 文件回落（`MapCatalog.cs:94`）会让 `--map siege-4p-base-v3` 从仓库根目录运行时被悄悄复活——这与 `.trellis/spec/core/boundaries.md:62–68`"旧标识的 json 不得留在仓库里"相抵触（已存在的问题，删除时一并消失）。

## 2. 要改（不是整删）的产品代码

| 位置 | 现状 | 删除后要做的 |
|---|---|---|
| `src/Siege.Core/Board/Maps/MapCatalog.cs:32–38` | `Builtins` 前 4 行是旧图 | 只剩 `BuiltinBoards` 三行 |
| `MapCatalog.cs:72–81` | `gen:` 分支 | 删；**旧标识的明确报错放这里**（见 saves-docs.md） |
| `MapCatalog.cs:94–101` | 文件回落 + 报错文案里有 `gen:` 写法与 `MapGenParameters` 平台数 | 改文案；是否保留"地图文件路径 / `maps/<标识>.json` 回落"需裁决（见 legacy-removal.md Q5） |
| `MapCatalog.cs:4–12、55–60` 注释 | 提到 `gen:` / `GeneratedMapId` | 改注释 |
| `src/Siege.Sim/Program.cs:21–23、140–158` | `ClockMapSeed` 用 `GeneratedMapId.FriendlySeed`；`MaterializeMapRequest` 处理裸 `gen` | 去掉 `gen` 分支，`FriendlySeed` 换来源 |
| `src/Siege.Sim/Program.cs:162–260`（`map` 子命令） | 打印高度 / 深水 / 桥 / 栅栏 / 林地 / 土路 / 新地表计数、咽喉、距离表、文本图图例 | 全部是棋盘图上恒为 0 或"不可达"的项；可删可留（只是打印），需随 MapProfile 决定一起定 |
| `src/Siege.Sim/Config/RunConfig.cs:191–201、227–239` | **每局换图 `MapPerMatch` 只支持 `gen:`**（`GeneratedMapId.Parse/Format`） | 删 `gen:` 后此功能失去来源：要么删 `--map-per-match`，要么扩到 `board:`（需裁决） |
| `src/Siege.Sim/Play/PlayCommand.cs:95` | `GeneratedMapId.IsGenerated` 决定是否打印"随机生成图"提示 | 改为 `BoardMapId.IsBoardMap` 或删 |
| `src/godot/scripts/GameRoot.cs:304–313` | 裸 `--map=gen` 处理 | 删；`:318` 的 `FriendlySeed` 换来源 |
| `src/godot/scripts/GameRoot.MapSelect.cs:39、50` | `FriendlySeed`、注释 | 换来源、改注释 |
| `src/Siege.Core/Ai/EvaluationWeights.cs:113–135、157` | `MapOverrides` 只登记 `siege-2p-base-v1`（Eye 50）+ 校准状态字串 | 删后覆盖表为空；`ForMapId` 机制本身仍可用（需裁决是否保留空机制） |
| `src/Siege.Core/Ai/EvaluationWeights.cs:83、167–169`、`AiDifficulty.cs:80、90、106` | 校准口径文字写 `siege-4p-base-v5` | 文本是历史口径，可留作"在已删除的 v5 上校准"说明 |
| `src/Siege.Presentation/Camera/BoardCamera.cs:120` | 注释举例 v5 | 改注释 |
| `src/Siege.Presentation/MapSelect/MapSelectModel.cs:36、142` | 注释提标准档 / 边疆档 / `gen:` | 改注释 |

## 3. `MapProfile` 与校验器：哪些可删、哪些仍被棋盘档用到

`MapProfile`（`src/Siege.Core/Board/MapProfile.cs:8–21`）有 `Standard=0` / `Frontier=1` / `Board=2`。删旧图后**产品里只剩 `Board`**：
- `Frontier` 的产品来源只有 `FrontierMapV2.cs:203`、`FrontierMapGenerator.cs:344`、`MapGenParameters.cs:40–45`——全删。
- `Standard` 的来源：`MapData.cs:31` 的**缺省值**、`MapFile.cs:37`（不写出）与 `:121`（缺字段按标准档读）。产品里没有任何 Standard 图了，但**测试里大量合成地图靠缺省 Standard**（`MatchFixtures.Map` 等，都走 `LoadUnvalidated` / `CreateUnvalidated` 绕过校验，所以档位不影响它们）。

校验器声明表 `src/Siege.Core/Board/MapValidator.cs:96–139`，按"在棋盘档上是否还起作用"分：

| 规则 / 字段 | 位置 | 棋盘档上 | 结论 |
|---|---|---|---|
| Standard / Frontier 两行预算（2/3/4 人 50–110 格、边疆 300–420） | :99–124 | 不用 | 随档删 |
| `DistanceHandling.RejectOnImbalance` | :108、:142–149、:988 | 棋盘档是 `AlwaysReport` | 枚举可删，距离只剩"报告" |
| `ColumnRange (0,25)`、`RowRange null`、`PlateList null` → `BOARDS_NOT_ALLOWED` | :109–111、:293–307 | 不用 | 随档删 |
| `Reachability(true,true,true)`：不可达拒绝、出生区到中央入口通路（`ValidateBirthZoneConnectivity` :857）、必须标咽喉（`ValidateLandmarks` :783） | :112、:124 | 棋盘档三项全豁免 | 三个开关与入口通路校验可删；`CENTRAL_ENTRANCE_NOT_PLAYABLE`（:769）仍用 |
| `DistanceTolerance` / `ToleranceRelaxReason` / `ValidateTolerance` | `MapData.cs:60–64`、:792–806 | 只在 RejectOnImbalance 下读容差 → 棋盘档上永不生效 | 可删（字段删除会改地图导出文本，见下） |
| 必死口袋 `ValidatePockets` + `MinTwoEyeArea` + `PocketExemptions(Reasons)` | :820–855、`MapData.cs:66–74` | 出生区 = 出生棋盘整块 5–7 矩形、无障碍无高差无栅栏（棋盘档第 1–4 条保证）→ 唯一连通块 ≥ 25 格 ≥ 8，**永不触发** | 可删（同上，改导出文本） |
| `ChokePoints` | `MapData.cs:55`；`DistanceTable` 的"最近咽喉"（:892） | 生成器恒写空（`BoardMapGenerator.cs:277`）→ 距离表这一行恒为"不可达" | 可删（改导出文本；22 个测试文件 43 处构造 MapData 时给了它，`required` 字段） |
| `CentralEntrance` | `BoardMapGenerator.cs:212、278` | 仍用（主战场高档信物位置、距离表"中央入口"） | 保留 |
| 出生区内不得有新地表 `BIRTH_ZONE_SPECIAL_SURFACE` | :697–709 | 被棋盘档 `BOARD_CELL_NOT_FLAT_GRASS`（:381–400）完全覆盖 | 可删 |
| `MapSymmetry` 旋转 / 镜像对称 | `MapSymmetry.cs` | 校验器不调用；棋盘图不对称 | 删 |
| 共用规则：结构、`TERRAIN_OUT_OF_BOUNDS`、可落子预算、信物格合法、保护期 9 枚容量、`BOARD_*` 全部 | — | 仍用 | 保留 |
| 规格档分流守门（`规格档分流守门Tests` 要求档位字面量只在声明表里） | :79–80、:189 | 只剩一档时表的意义变弱 | 需裁决：保留单行表（最小改动）还是拆掉表 |

**导出文本 / 摘要的连锁**：`MapFile.ToJson`（`src/Siege.Core/Board/MapFile.cs:27–63`）对棋盘图也写出 `Profile:"Board"`、`ChokePoints:[]`、`DistanceTolerance:1`、`MinTwoEyeArea:8`、`PocketExemptions:{}`、整版 `Heights` 全 `0`、`Surfaces`。任何字段删减都会改变三张内置棋盘图的 `MapFile.Digest`，触发 `内置棋盘图Tests` 的摘要黄金值，而 `boundaries.md:62–68` 规定"内置图内容一变标识必须递增"→ 要么 `siege-*-board-v1` 升 `-v2`，要么本 change **不动 MapData / MapFile 的字段**（只删档位与旧图），把字段清理留到以后。这是需要裁决的点（legacy-removal.md Q3）。

## 4. 地形元素在"只有棋盘图"时的来源核对（问题 1 后半 + 问题 2）

来源只有两条：① 地图开局值（`MapData.TerrainData`）；② 匠人改造（`TerrainEditRules`，三种动作：搭桥 / 立栅 / 烧林，`src/Siege.Core/Board/TerrainEdit.cs:4–11`）。

- ① 棋盘图开局值：`BoardMapGenerator.cs:265–280` 不设 `TerrainData` → `TerrainData.Flat`（全 h=0 草地、无桥无栅）；校验器另禁止棋盘内非 h0 草地（`MapValidator.cs:381`）、棋盘内/边界上栅栏（:404–415）、全图深水（:499–508）。场景格全是障碍。
- ② 改造能产生什么（`src/Siege.Core/Board/TerrainEditRules.cs`）：
  - 搭桥：目标须是未架桥深水（:52、:110）→ **棋盘图上永无目标**。
  - 烧林：目标须是林地（:57、:125）→ **永无目标**。
  - 立栅：边两端只要求在地图外接范围内、几何相邻、至少一端是匠人四邻（:129–147），**不要求可落子** → 棋盘内的边、以及棋盘格与相邻场景格之间的边都是合法目标。**栅栏仍有来源。**
  - 没有任何动作改高度、产生新地表、产生深水 / 林地 / 土路。

逐项结论：

| 地形元素 | 规则代码（Core） | 棋盘图开局 | 改造 | 结论 |
|---|---|---|---|---|
| 高度 / 崖壁 | `Adjacency.cs:67–71`（气边 |Δh|<2）、`:97、:141、:159–162`（覆盖居高临下、岩台远格的崖壁阻挡）；`PieceEffects.cs:185–188` 高地压制；`TerrainData.CliffDrop` | 全 0 | 无 | **不可达但规则仍在** |
| 深水 / 隔水覆盖 | `MapData.cs:92`（不可落子）、`Adjacency.cs:107–124`（隔一格水覆盖）、`TerrainData.IsUnbridgedDeepWater` | 禁止 | 无 | **不可达** |
| 桥（预置 / 搭桥） | `TerrainData` 桥集合、`TerrainWriter` 搭桥、`TerrainEditRules` 搭桥分支 | 无（桥须在深水上） | 无目标 | **不可达**（搭桥动作整支不可达） |
| 林地 / 烧林 | `Adjacency.cs:162`（林地不收覆盖）、`TerrainEditRules` 烧林分支、`TerrainWriter` | 无 | 无目标 | **不可达**（烧林整支不可达） |
| 土路 | 规则上同草地，只有呈现 | 无 | 无 | 不可达（仅视觉） |
| 新地表 荒漠 | `PowerCalculator.cs:50`（不计领地分） | 无 | 无 | **不可达**；删 `gen:`（`FrontierSurfaces`）后**没有任何来源** |
| 新地表 沼泽 | `Adjacency.cs:92`（不产生覆盖） | 无 | 无 | 同上 |
| 新地表 岩台 | `Adjacency.cs:133–151`（远一格覆盖） | 无 | 无 | 同上 |
| 新地表 浅滩 | `GameBoard.cs:249、681`（空浅滩不算气 / 不属空区）、`LifeShape` 相关 | 无 | 无 | 同上 |
| 栅栏 | `Adjacency.cs:71`（挡气）、覆盖不受影响；`LifeShape`、`LayerContents.cs:311、404`（"栅栏挡气不挡覆盖"标示）、`DefaultBoardView.cs:191`、Godot `fence_x/z` | 禁止 | **立栅可产生** | **仍可达，必须保留** |
| 工坊信物（`RelicType.Workshop`） | `TerrainEditRules.cs:163–189`：只扩**格目标**（搭桥 / 烧林），边目标不扩 | — | — | 棋盘图上**效果恒为空**，但信物权重表仍会生成它（`RelicWeights.cs:32`）——玩法上是"死信物"，需裁决 |
| 匠人 | 唯一可用动作只剩立栅 | — | — | 仍可用 |

呈现层（同样"不可达但代码仍在"，除栅栏外）：
- Presentation：`Text/Labels.cs:18–19、33–37`（崖壁 / 栅栏理由、四种新地表说明）、`Layers/LayerContents.cs:90–98、356–411`（Cliff / Fence / AcrossWater / Crag / Shallows / Forest 理由）、`Visibility/DefaultBoardView.cs:184`（高度、地表、桥）、`Preview/PreviewPresentation.cs:71`（`BridgeCells`）。
- Godot `src/godot/scripts/BoardView.cs`：深水 / 桥（:241–263、`AddWater` :1116–1145 的 `water_bed`、`bank_lip`）、地表面砖（:268–277）、林木（:318 `trees`）、新地表部件（:326–352 `desert/marsh/crag/shallows`）、高度侧面（:695 `side_slope/side_cliff`）、栅栏（:379、:1155 `fence_x/z`，**仍可达**）。障碍装饰 `rock/pines/ruins`（:285）在棋盘图的场景格上**仍被使用**（场景格同时画 `scene_slab`）。
- 部件文件：`src/godot/parts/terrain/` 下 `desert_0–5`、`marsh_0–5`、`crag_0–3`、`shallows_0–5`、`trees_0–2`、`bridge`、`water_bed_0–1`、`bank_lip_0–2`、`side_slope_0–1`、`side_cliff_0–2` 在棋盘图上不可达；`fence_x/z`、`rock/pines/ruins`、`scene_slab`、`plate_frame`、`island_*`、`cloud`、`rim`、`zone_strip`、`tile_top`、`liner` 仍用。

**"不可达但规则仍在"清单（需负责人裁决删 / 留）**：高度与崖壁（含高地压制加值）、深水与隔水覆盖、预置桥与搭桥动作、林地与烧林动作、土路、四种新地表（荒漠 / 沼泽 / 岩台 / 浅滩）、工坊信物的效果。仍可达必须保留的只有：**栅栏（经立栅）**、匠人改造框架本身。

测试里这些规则**不依赖旧地图**：258 个测试方法（另 26 个同时依赖旧图）用 `TestMaps.Blank(TerrainData …)` / `MatchFixtures.Map(terrain)` / `LifeShapeFixtures` 在 `GameBoard.LoadUnvalidated` / `MatchFlow.CreateUnvalidated` 上直接构造带地形的合成盘面（`tests/Siege.Core.Tests/TestMaps.cs:13–45`），删旧图不影响它们。若决定"保留待用"，它们原样继续守门；若决定"删规则"，这些测试随规则删。

## Caveats / Not Found

- Godot 部件的"不可达"是按 `BoardView.cs` 分支静态推断的，没有在 Godot 里实跑核对。
- 地形测试数（258）来自正则静态扫描（`Surface.X`、`TestMaps.Terrain`、`heights:`/`fences:`、`TerrainEdit.` 等），可能有个位数偏差。
