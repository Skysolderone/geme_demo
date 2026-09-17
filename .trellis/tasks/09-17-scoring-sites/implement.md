# 09-17-scoring-sites 实施记录

每段追加：改了什么 / 既有测试改写逐条 / 变异验证逐条（编号、变异、红掉的测试、还原确认）/ 待决。

## 段 A1：据点数据与布点（tasks 1.1–1.4）

### 改了什么

- `src/Siege.Core/Board/SiteTier.cs`（新）：`Tent=1 / Campfire=2 / Stele=3`（营帐 / 篝火 / 石碑）。从 1 起编，`default(SiteTier)` 即"未标注"，让"档位必填"可校验。
- `MapData.Sites`：`ImmutableDictionary<Coord, SiteTier>`，非 required，缺省为空（旧图与合成测试图不必给）。
- `MapFile`：JSON 读写 `Sites`（格 → 档位名，按坐标序写出）；缺字段读为空；某格档位写成 `null` 时抛 `FormatException` 并指出坐标。
- `MapValidator`：
  - 规则 8 `ValidateSites`：`SITE_TIER_MISSING`、`SITE_ON_NON_PLAYABLE`、`SITE_ON_RELIC_CELL`（均带坐标）、`SITE_COUNT_OUT_OF_RANGE`（2 人 6–8 / 3 人 9–11 / 4 人 10–14，报文含数量、方向与区间）。
  - 规则 1：距离计算抽成公开的 `DistanceTable(map)`（返回 `BirthZoneDistance` 列表，由校验器与 `map` 子命令共用，只此一份实现）。目标增加"最近篝火""最近石碑"。目标集合为空时（无据点的图）该项跳过，所以不会因此报"不可达"。
- `MapSymmetry`：逐格比对据点的有无与档位，报"据点不一致"。
- `FourPlayerBaseMap`：地形不动，加 `TentSeeds / CampfireSeeds / SteleSeeds` 三条 C4 轨道，Id 改为 `siege-4p-base-v4`；类注释写明布点理由和"v3 已被取代"。
- `RunConfig.MapId` 默认改为 v4。`MapCatalog` 按 `builtin.Id` 自动跟随；再请求 `siege-4p-base-v3` 会去读 JSON 文件，然后因规则 8 被拒（缺据点），与 v2 的处理方式相同。
- `Siege.Sim`：`map` 子命令文本图用 `T` 营帐 / `C` 篝火 / `S` 石碑标出据点，另打印据点清单和各出生区距离表，图例同步补上；`Play/BoardRenderer` 的空据点格显示同样的字母（可落子时前缀 `+`），图例加一行。字母映射只在 `BoardRenderer.SiteLetter` 一处。
- `maps/siege-4p-base-v4.json`：由 `map` 子命令导出（新文件）。`maps/siege-4p-base-v3.json`：仿照 v2 加了顶层 `_comment`，说明本文件是历史存档、已被 v4 取代、通不过规则 8。其余内容未动。

### 布点（负责人审）

- **营帐** `B3 / L2 / M11 / C12`：出生区腹地，四个邻格都是本区高台格。`D2 D3` 能被 h=1 缓坡 `E2 E3` 覆盖，所以没选。测试"营帐只有本区高台能覆盖"用真实 `CoverageTargets` 钉住：能覆盖营帐的格全在本区内。
- **篝火** `J2 / M9 / E12 / B5`：测试从地图推导，"河外低地（不经桥沿气边可达的 h=0 格）且与另一出生区 h=2 格几何相邻"的候选集合恰好就是这条轨道，与 design D-C 一致。
- **石碑** `H5 / J8 / F9 / E6`：**这是实现方的选择，请负责人确认。** 岛上非林地、非信物的格共 3 条 C4 轨道：
  - 内圈 `G6 F7 H7 G8`：4 格都是岛心 `G7` 的邻居，`G7` 上一枚棋子就能同时覆盖全部 4 块石碑（4×45），价值过度集中。**否决。**
  - `F5 J6 H9 E8`：`F5` 隔一格深水 `F4`，能被主人河外低地 `F3` 覆盖，又紧挨本家桥头，实质上等于白送给主人。**否决。**
  - **选定** `H5 J8 F9 E6`：石碑与本家桥头信物（如 `G5`）之间隔着栅栏，本家能从桥头覆盖（栅栏不挡覆盖）但走不进去；只有邻家从桥头 `J7` 经 `J6` 和林地角 `J5` 才能走到占据。结果是"一家隔栏覆盖、一家绕林占据"的交叉争夺，逻辑与篝火相同。代价是最近石碑距离 8，比另两条轨道的 6 远；另外占据 `H5` 的棋子只有 1 口气（`J5`）。

### 各出生区沿气边最短距离（`map` 子命令输出；`基准地图对称性Tests` 用独立 BFS 复算并钉死 6 / 8）

| 目标 | 出生区 1 | 出生区 2 | 出生区 3 | 出生区 4 |
|---|---:|---:|---:|---:|
| 最近公共信物 | 5 | 5 | 5 | 5 |
| 中央入口 | 7 | 7 | 7 | 7 |
| 最近咽喉 | 4 | 4 | 4 | 4 |
| 最近篝火 | 6 | 6 | 6 | 6 |
| 最近石碑 | 8 | 8 | 8 | 8 |

### 文本图（`dotnet run --project src/Siege.Sim -c Release -- map`，原样）

```
地图 siege-4p-base-v4  13×13
可落子格 105   岩石 36   深水 32（其中桥 4）   栅栏 4   林地 4   土路 16
可落子格按高度 h0/h1/h2 = 45/8/52
出生区 4 个，各 13/13/13/13 个可落子格，高度 2/2/2/2
信物格 13（出生区 8，公共区 5）
据点 12（营帐 4，篝火 4，石碑 4）
  营帐（T） L2 B3 M11 C12
  篝火（C） J2 B5 M9 E12
  石碑（S） H5 E6 J8 F9
各出生区沿气边最短距离（出生区 1/2/3/4）：
  最近公共信物  5/5/5/5
  中央入口  7/7/7/7
  最近咽喉  4/4/4/4
  最近篝火  6/6/6/6
  最近石碑  8/8/8/8
咽喉 G4 D7 K7 G10   中央入口 G7   桥 G4 D7 K7 G10
栅栏 G5-H5 E6-E7 J7-J8 F9-G9

地图校验通过。

 13 ## 24 24 24 ## ## ## ## ## 23 23 23 ## 
 12 24 2r 2T 24 0C 0  0  ## 1. 23 23 2r 23 
 11 24 24 2r ~~ ~~ ## 0. 0. 1. 23 2r 2T 23 
 10 24 24 24 ~~ ~~ ~~ 0= ~~ ~~ ~~ ~~ 23 23 
  9 ## 1. 1. ~~ 0F 0S|0o 0  0F ~~ ~~ 0C ## 
  8 ## ## 0. ~~ 0  ## 0  ## 0S ~~ ## 0  ## 
                            --             
  7 ## 0  0. 0= 0o 0  0R 0  0o 0= 0. 0  ## 
                --                         
  6 ## 0  ## ~~ 0S ## 0  ## 0  ~~ 0. ## ## 
  5 ## 0C ~~ ~~ 0F 0  0o|0S 0F ~~ 1. 1. ## 
  4 21 21 ~~ ~~ ~~ ~~ 0= ~~ ~~ ~~ 22 22 22 
  3 21 2T 2r 21 1. 0. 0. ## ~~ ~~ 2r 22 22 
  2 21 2r 21 21 1. ## 0  0  0C 22 2T 2r 22 
  1 ## 21 21 21 ## ## ## ## ## 22 22 22 ## 
    A  B  C  D  E  F  G  H  J  K  L  M  N  

每格两位：首位是高度 0/1/2，次位是标记。## 岩石  ~~ 深水  = 桥  1-4 出生区  r 出生区信物  o 公共信物  R 公共高档信物  T 营帐  C 篝火  S 石碑
@ 中央入口  ^ 咽喉（与信物、据点或桥同格时显示信物 / 据点 / 桥的标记；入口 G7 是高档信物 R、四座桥即咽喉 =；据点不与信物重合）  F 林地  . 土路   格间 | 与行间 -- 为栅栏

```

### 新增测试（729 → 753，+24）

- `人数适配预算Tests`：据点数越界（16 / 15 / 9）、据点数恰在区间端点时通过（10 / 14）、两人三人据点数区间（4 组）。
- `地图静态校验规则Tests`：据点与信物重合（G7）、据点在不可落子格（C4 深水）、据点必须标注档位、到据点的距离失衡（石碑 / 篝火，6 对 9）。
- `地图文件往返Tests`：往返读写无信息丢失补上 Sites；缺据点字段的旧文件读入为无据点（用真实 v3 文件，只报 `SITE_COUNT_OUT_OF_RANGE` 一条）；据点缺档位的文件被指名报出。
- `四人基准地图Tests`：据点布点、营帐只有本区高台能覆盖、篝火可被邻家居高覆盖、保护期内篝火归邻家（`CoverageMap` 唯一覆盖 = P1，`OwnershipOf(J2)` = Exclusive(P1)，并断言 `Sites[J2]` = 篝火，使本测试绑定据点数据；不涉及分值）、v4 地形与 v3 文件逐格相同（v3 文件补上 v4 的 Id 与据点后序列化逐字节相等）。
- `基准地图对称性Tests`：只改一个据点档位的图被判不对称、据点在 C4 旋转下不变、四个出生区到最近篝火与石碑的距离精确相等；"每项属性都参与旋转比对"补了"据点"一项。

### 既有测试改写

| 文件 | 改了什么 | 理由 |
|---|---|---|
| `MapDefinition/四人基准地图Tests.cs` `外接尺寸与可落子格` | Id 期望 v3 → v4 | 测地图身份（S-9） |
| `MatchTelemetry/对局日志的记录内容Tests.cs` `日志覆盖七类记录` | `MapId` 期望 v3 → v4 | 测默认地图（RunConfig 默认切到 v4） |
| `RelicGeneration/公共争夺区信物权重与高阶升级Tests.cs` | 方法名 `v3基准图上…` → `基准图上公共信物升级率落在宽口径`；Id 断言改 v4；补注释 | 测的是信物分布，v4 信物格与 v3 相同、据点不参与信物生成；数值期望不变 |
| `TacticalLayers/盘面层视图模型带地形Tests.cs` | 只改类注释（说明现为 v4、地形与 v3 相同） | 只测地形，断言不变 |
| `MapDefinition/地图文件健壮性Tests.cs` `磁盘上的基准地图文件与代码一致` | 只改注释（文件名随 Id 变为 v4.json；v3 列为对照存档） | 文件路径由 `Create().Id` 推出，行为自动跟随 |

v3 的处理：代码里不再有 v3 生成器（`FourPlayerBaseMap.Create()` 返回 v4）。v3 只剩 `maps/siege-4p-base-v3.json` 历史文件，默认不加载，因缺据点通不过规则 8。"地形测试"直接用 v4（地形逐格相同，由"v4 地形与 v3 文件逐格相同"钉住）。规则 8 没有为任何图放宽。

### 变异验证（`cp` 备份 → 变异 → 全量 `dotnet test` → 还原 → `cmp` 逐字节一致）

| 编号 | 变异 | 红掉的测试 | 还原 |
|---|---|---|---|
| M-A1 | `MapSymmetry` 比对漏掉据点（据点不一致的 `defects.Add` 改为丢弃） | 红 2：`基准地图对称性Tests.每项属性都参与旋转比对`、`只改一个据点档位的图被判不对称` | cmp 一致 |
| M-A2 | `MapSymmetry` 只比位置不比档位（去掉 `siteP && tierP != tierQ`） | 红 1：`只改一个据点档位的图被判不对称` | cmp 一致 |
| M-A3 | 规则 8 不查与信物重合（条件恒假） | 红 1：`地图静态校验规则Tests.据点与信物重合` | cmp 一致 |
| M-A4 | 4 人据点上界 14 → 15 | 红 3：`人数适配预算Tests.据点数越界` 的 15 / 16 / 9 三组（15 是行为红，16 / 9 是报文里的 `10–14` 红） | cmp 一致 |
| M-A5 | 距离均衡漏掉"最近石碑"（目标集合置空） | 红 1：`地图静态校验规则Tests.到据点的距离失衡(Stele)` | cmp 一致 |
| M-A6 | `MapFile` 读入时丢弃 Sites | 红 8：`地图文件往返Tests` 的 往返读写无信息丢失 / 往返后仍然通过校验 / 序列化是确定性的 / 据点缺档位的文件被指名报出，`地图文件健壮性Tests` 的 磁盘上的基准地图文件与代码一致 / 含未知字段 / 全小写键名 / 全小写枚举值 | cmp 一致 |
| M-A7 | 规则 8 不查档位必填（条件恒假） | 红 1：`地图静态校验规则Tests.据点必须标注档位` | cmp 一致 |
| M-A8 | 篝火种子 `J2` → `H2`（仍是 C4、仍在河外低地，但不贴邻家崖边） | 红 6：`四人基准地图Tests.据点布点`、`篝火可被邻家居高覆盖`、`地图文件健壮性Tests.磁盘上的基准地图文件与代码一致`、`地图文件往返Tests.据点缺档位的文件被指名报出`、`基准地图对称性Tests.只改一个据点档位的图被判不对称`、`四个出生区到最近篝火与石碑的距离精确相等` | cmp 一致 |

全部还原后重跑全量：753 条全过，退出码 0。`dotnet build` 0 警告 0 错误。

### 待决 / 偏离

1. **石碑轨道**选 `H5` 轨道，理由见上，需负责人拍板。**决策相关的后果**：每家从自家桥头（距离 5）隔栏覆盖一块石碑，比走进去占据一块（距离 8）更快，所以这条轨道实际上像"第二圈篝火"——石碑默认归站在相邻桥头的那家，只有邻家沿 `J7→J6→J5` 走进来才会形成争议。这是否符合 D-C 说的"唯一的正面战场"，请与两条被否决的轨道对照后定夺。若改选别的轨道，只需改 `SteleSeeds` 一处，再同步 `基准地图对称性Tests` 里钉死的距离 8，并重新导出 v4 JSON。
2. 营帐选 `B3` 轨道，`C2` 轨道同样满足约束，属于实现方的选择。
3. `MapValidator.DistanceTable` 与 `BirthZoneDistance` 是新增的公开 API，供 `map` 子命令打印距离表（避免在 Sim 里再写一份 BFS）。
4. `BoardRenderer`（对局盘面）只显示据点档位字母，不显示控制状态；控制状态属于段 A2 / B。

## 段 A1 检查

### 结论（逐项）

1. 场景测试齐全且钉住行为：「据点布点」按规格定义从地图推导河外低地 / 岛并钉死篝火轨道；「篝火可被邻家居高覆盖」用真实 `Adjacency.CoverageTargets`；「保护期内篝火归邻家」已存在（`CoverageMap` 唯一覆盖，不涉分值），本次加强（见修复 3）。
2. 规则 8：2 / 3 / 4 人区间（含端点 10 / 14 通过、9 / 15 / 16 拒绝）、未架桥深水 `C4`、与信物重合 `G7`、档位必填均有测试，报错带坐标；规则 1 最近篝火 / 最近石碑（6 对 9）报文含目标名与出生区编号。
3. `MapFile` 缺 `Sites` 读为空（真实 v3 文件）；往返逐字段含 Sites；v4 与 v3 地形原测试只比 `ToJson` 文本——属 testing.md「逐字节相等抓不到漏字段」形状，已补逐格断言（修复 2）；v3 默认不加载原只测 `Validate` 失败，已补（修复 4）。
4. 既有测试改写只动 Id、方法名与注释，数值期望未改，与 implement.md 表一致。
5. `MapValidator.DistanceTable` 只是把原 `ValidateDistanceBalance` 循环抽出，BFS 仍只有 `MultiSourceDistances` 一份（走 `LibertyNeighbors`）；Sim 只消费表。无第二处遍历。
6. 越段：无据点控制 / 分值 / 高地加值 / 势力公式代码；`BoardRenderer` 只显示档位字母。
7. 出生区编号 0 基 / 1 基：**未统一**。校验器 8 条报文 + `MapSymmetry` 用 0 基，既有测试十余处断言 `出生区 0 = 4`、`出生区 0 有 11 个可落子格` 等，遥测分析报告（`平衡分析方向Tests`）也是 0 基；只有文本图 / 棋盘是 1 基。改哪一侧都要改多处既有断言并在另一侧制造新不一致，不属"很小"，留给负责人定口径。

### 修复

1. `MapFile.cs` 反序列化 `ChokePoints =Parse(` 补空格（格式）。
2. `四人基准地图Tests.v4地形与v3文件逐格相同`：补第一条腿——169 格逐格比 `HeightAt / SurfaceAt / TerrainAt / HasBridge / BirthZoneOf`，再比 Obstacles、栅栏、信物格、咽喉、中央入口、v3 无据点；原 JSON 逐字节比对保留为第二条腿。
3. `四人基准地图Tests.保护期内篝火归邻家`：补 `CovererCount = 1`、`SourcesOf(J2)` 恰为 `K2`（几何相邻）、主人棋子 `D2 D3` 属出生区 0 且 h=2；对照组去掉 `K2` 后 `J2` 覆盖数为 0。
4. `地图文件往返Tests.缺据点字段的旧文件读入为无据点`：补 `GameBoard.Load(v3)` 抛 `MapValidationException`，`RunConfig().MapId` 与 `FourPlayerBaseMap.Create().Id` 均为 v4。
5. `基准地图对称性Tests.四个出生区到最近篝火与石碑的距离精确相等`：补 `MapValidator.DistanceTable` 五项名称顺序与各区距离逐项等于测试内独立 BFS 的最近值（堵住 M-C2 暴露的缺口）。

测试数不变（753，均为在既有方法内加断言）。

### 变异验证（`cp` 备份 → `sed` → 全量 `dotnet test -c Release` → `cp` 还原 → `cmp` 一致）

| 编号 | 变异 | 结果 | 还原 |
|---|---|---|---|
| M-C1 | `MapFile.ToJson` 写出 `map.Sites.Clear()`（写路径漏据点；M-A6 是读路径） | 红 4：`地图文件往返Tests` 往返读写无信息丢失 / 往返后仍然通过校验 / 据点缺档位的文件被指名报出，`地图文件健壮性Tests.全小写键名的地图能正确加载`。注：`磁盘上的基准地图文件与代码一致` 未红——它两边都经同一 `ToJson`/对象比对，对写路径漏字段不设防（据点漂移由 M-A8 证明能抓），A2 加序列化字段时注意 | cmp 一致 |
| M-C2 | `DistanceTable` 取最近改最远（`d < best` → `d > best`） | 修复 5 之前：**全绿 753（缺口）**——C4 下最远距离四区也相等，校验器与既有测试都不红。修复 5 之后：红 1 `基准地图对称性Tests.四个出生区到最近篝火与石碑的距离精确相等` | 两次均 cmp 一致 |

### 验证

- `dotnet build`：0 警告 0 错误，EXIT=0。
- `set -o pipefail; dotnet test -c Release`：753 通过 0 失败，EXIT=0。

### 未修 / 待决

- 出生区编号 0 基 / 1 基统一（见结论 7）。
- 石碑轨道 `H5/E6/J8/F9` 待负责人裁决，本次未动布点。

## 段 A2：计分、跑局与遥测（tasks 2.1–2.7、3.1–3.3）

### 改了什么

**Core（规则）**

- `Scoring/SiteValues.cs`（新）：`SiteValues(Tent, Campfire, Stele)`，`Standard = 5/15/45`，`Of(SiteTier)`；唯一校验 `Validated()`（正整数、营帐 ≤ 篝火 ≤ 石碑，报文点名档位）。对局建局、存档恢复、`PowerCalculator` 正式入口、`RunConfig.Validated` 都调这一份。
- `Scoring/SiteControl.cs`（新）：据点控制唯一实现。`SiteControlKind { Unclaimed, Contested, UniqueCoverage, Occupied }`、`SiteState(Coord, Tier, Kind, Controller)`、`SiteHolding(Coord, Tier, Value, Kind)`。`SiteControl.Compute(board, coverage)` 只读 `CoverageMap.OwnershipOf`（占据优先已在覆盖表里，与信物控制同一份数据），做 `OwnershipKind → SiteControlKind` 映射；不出现邻接 / 覆盖目标 / 高度调用（守门测试 + 变异）。
- `Scoring/PieceEffects.cs`：新增 `HighGroundBonus(board, group)`，唯一实现：每枚棋子遍历 `board.CoverageTargets(s)`，有"非本方棋子且高度严格低于 s"即 +1 并 `break`。
- `Scoring/PowerSnapshot.cs`：`GroupPower` 加 `HighGroundBonus`，`PositionBonus = 连珠 + 协同 + 高地`；`PlayerPower` **删除 `TerritoryScore`**，加 `Sites`（控制中的据点清单）与 `SiteScore`；`ExclusiveCells` 保留（只展示）；`PowerSnapshot` 加 `SiteStates`（全部据点，含争议 / 无人）与 `SiteValues`。
- `Scoring/PowerCalculator.cs`：`总势力 = 据点分 + 棋串军势`；`Evaluate` 把高地加值并入位置加值（不进倍率）。正式入口改为 `Compute(board, roster, siteValues)`（**分值无缺省**，调用方必须显式传）；无名册的测试便利重载 `Compute(board)` 用 `SiteValues.Standard`。弃赛 / 出局者遗留棋子控制的据点照常计入其盘面势力（R-4），名次过滤仍只在 `Rank`。
- `Scoring/PowerScoreboard.Recalculate(board, roster, siteValues, majorRound)`。
- `Match/FlagPlanting.cs` 的 `MatchOptions`：加 `SiteValues`（默认 `Standard`）。`MatchFlow`：建局与恢复都校验；公开属性 `SiteValues`、`SiteValuesBackfilled`；三处 `Scoreboard.Recalculate` 与顺序预测、富预演都传对局分值；`Finish` 的并列链输入改取 `detail.Sites.Length`。`MatchPublicView` 加 `SiteValues`（插旗阶段即公开）。
- `Match/FinalStandings.cs`：`StandingInput.ExclusiveCells` → `ControlledSites`，比较链 势力 → 信物数 → 据点数 → 棋子数。
- `Match/MatchFlow.Persistence.cs`：存档写 `SiteValues`（`SiteValuesSaveData`）；`StandingSaveData.ExclusiveCells` → `ControlledSites`。旧存档：缺 `SiteValues` 回填标准局并置 `SiteValuesBackfilled`；旧字段 `ExclusiveCells` 读入时被忽略，`ControlledSites` 缺省 0（R-7）。势力快照不入存档（恢复时重算），无需序列化 `SiteStates`。
- `Preview/BatchPreview.cs`：`BatchPreviewBuilder.Build` 加 `siteValues` 参数；`Ai/BatchEvaluator.cs` 从 `view.SiteValues` 取分值。**AI 权重未改**；AI 没有直接读 `TerritoryScore` 的地方（只经 `PowerCalculator` 的势力增量）。
- `Board/SiteAttribution.cs`（新，**只供遥测**）：`HomeZones(map)` 推每个据点的"主人出生区"。区域 `R(z)` = 从出生区 z 沿气边、不经桥格可达的格。营帐 → 所在出生区；篝火 → 唯一包含它的 `R(z)`；石碑 → 与之几何相邻（`Adjacency.AreAdjacent`）的公共信物格 → 与该信物有气边的桥格 → 桥格另一侧落在哪个 `R(z)`。任一步不唯一返回 `null`（不猜）。v4 推得：篝火 J2/M9/E12/B5 → 0/1/2/3，石碑 H5/J8/F9/E6 → 0/1/2/3，营帐 B3/L2/M11/C12 → 0/1/2/3（0 起；对玩家显示 +1，S-14 段 D 统一）。

**Sim（跑局与遥测）**

- `Config/RunConfig.cs`：加 `SiteValues`（默认标准局），`Validated` 调 Core 校验；`ParseSiteValues("3/8/24")`；`Effective()`（未配置权重的玩家填 `EvaluationWeights.Default`）。
- `Running/BatchRunner.cs`：`config.json` 写 `config.Effective().ToJson()` → 四名玩家完整权重与据点分值如实写出。
- `Program.cs`：`run` 新增 `--site-values 营帐/篝火/石碑`；启动行打印据点分值；用法说明注明"权重只能经 `--config` 指定，同时给 `--difficulty` / `--players` 会重建玩家列表、丢弃配置文件里的权重"（既有行为，未改，扫档时注意）。
- `Running/MatchSession.cs`：建局把 `RunConfig.SiteValues` 传入对局；配置与对局分值不一致即抛（同 MaxMajorRounds）；首部写 `SiteValues`（取自对局）与 `Sites`（坐标、档位、主人出生区）；每小回合快照写 `Sites`（状态 + 控制者）；比较前后两份公开快照的 `SiteStates` 记 `SiteControlChanged` 事件（Detail `坐标 档位: 旧状态[:控制者] -> 新状态[:控制者]`，Coords = 据点坐标，Turn / MajorRound 为小回合与大回合）；`PlayerEntry.Territory` → `SiteScore`；`GroupEntry` 加 `HighGroundBonus`；`StandingEntry.ExclusiveCells` → `ControlledSites`。
- `Logging/MatchLog.cs`：上述字段均为可空（旧日志读入为 `null`，不回填）。
- `Analysis/BalanceAnalyzer.cs` + `ReportWriter.cs`：新增 `SiteSection`（§17 第 10 项），口径见类型注释：纳入 = 首部有 `Sites` 且每条快照有 `Sites`，否则整局计入 `Skipped`；三档 被控制 / 争议占比（分母 = 小回合快照 × 该档据点数）、首次被控制平均大回合（另计从未被控制个数）、控制过该档据点的玩家胜率；篝火主人 / 石碑桥头那家 控制占比（占全部与占被控制两种分母）与主人首次控制平均大回合（推不出主人的据点单独计数，不进分母）；终局据点分占参赛玩家总势力（逐局比值取平均）；终局高地加值占位置加值（全批合并）。4b 各棋子势力占比注明"不含据点分与高地加值"（高地加值不属于任何棋子类型）。

**Presentation / Godot（只为编译通过的最小改动，界面语义留段 B）**

- `Siege.Presentation/Layers/LayerContents.cs`：`PlayerPowerRowView.TerritoryScore(int)` → `SiteScore(long)`。势力层的 `TerritoryCells`（独占格列表）未动。
- `src/godot/scripts/Hud.cs`：两处"领地 {TerritoryScore}"文案 → "据点 {SiteScore}"；终局名次行"独占 {ExclusiveCells}" → "据点 {ControlledSites}"（顺序改为 信物 在前，与并列链一致）。
- 未改（留段 B）：`PreviewPresentation.GroupPowerView` 的公式文案仍只写"连珠 / 协同"两项，高地加值非 0 时括号内两项之和小于位置加值；势力层据点项、公开视图据点状态、地标 / 旗帜。

### 验证

- `dotnet build`（整个 sln）：0 警告 0 错误，EXIT=0。
- Godot `--headless --path src/godot --build-solutions --quit`：EXIT=0（`Siege.Godot.dll` 重新生成）。
- `set -o pipefail; dotnet test -c Release`：**797 通过 0 失败，EXIT=0**（基线 753；删除 `领地分Tests` 3 条，新增 47 条）。
- 3.1：
  - `dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/a2-smoke --seed 1 --count 1 --difficulty Standard`：EXIT=0；`config.json` 四名玩家均写出完整 7 维权重（Safety 5）与 `SiteValues` 5/15/45。
  - 配置文件方式（3/8/24 + 全员 Safety 7）：`run --out sim-out/a2-sweepcheck --config <scratchpad>/a2-sweep-config.json --seed 1 --count 1`（配置文件含 `Players[4].Weights`（Safety 7）与 `"SiteValues": {"Tent":3,"Campfire":8,"Stele":24}`，**不传 `--difficulty`**）：EXIT=0；启动行"据点分值 3/8/24"；`config.json` 中 `SiteValues` = 3/8/24、四名玩家 `Weights.Safety` = 7 且 7 维齐全；日志首部 `SiteValues` = 3/8/24、`Sites` 12 个且主人出生区与上文一致。
  - 命令行方式：`run --out sim-out/a2-cli --site-values 3/8/24 --seed 1 --count 1`：EXIT=0，`config.json` 的 `SiteValues` = 3/8/24。
- 3.3：`run --out sim-out/a2-20 --seed 1 --count 20 --difficulty Standard --gzip` EXIT=0，`analyze --dir sim-out/a2-20 --out sim-out/a2-20/report.txt` EXIT=0。sim-out 不提交。

### 20 局报告第 10 项（`sim-out/a2-20/report.txt`，标准局 5/15/45、Standard AI、默认权重 Safety 5、种子 1–20，原样）

```
### 10. 据点（scoring-sites：分母为纳入局的「小回合快照 × 该档据点数」；缺据点字段的旧日志整局排除）
- 纳入 20 局，排除无据点字段的旧日志 0 局
- 营帐（80 个·局）：被控制 96.9%（3846/3968），争议 0.0%（0/3968），首次被控制平均第 1 大回合（从未被控制 0 个），控制过它的玩家胜率 25.0% (20/80，95% 区间 16.8%–35.5%)
- 篝火（80 个·局）：被控制 80.8%（3207/3968），争议 16.2%（641/3968），首次被控制平均第 1 大回合（从未被控制 0 个），控制过它的玩家胜率 25.0% (20/80，95% 区间 16.8%–35.5%)
- 石碑（80 个·局）：被控制 52.9%（2098/3968），争议 22.9%（909/3968），首次被控制平均第 4 大回合（从未被控制 0 个），控制过它的玩家胜率 25.6% (20/78，95% 区间 17.3%–36.3%)
- 篝火由所在低地主人控制：占全部据点小回合 3.6%（144/3968），占被控制小回合 4.5%（144/3207）；主人首次控制平均第 5.53 大回合（主人从未控制 42 个）；推不出主人 0 个
- 石碑由相邻桥头那家控制：占全部据点小回合 12.1%（481/3968），占被控制小回合 22.9%（481/2098）；主人首次控制平均第 4.68 大回合（主人从未控制 8 个）；推不出主人 0 个
- 终局据点分占参赛玩家总势力：平均 30.2%（样本 20 局）
- 终局高地加值占全部位置加值：0.5%（11/2117）
```

同一报告里与本轮判据相关的旁证（20 局样本，只作机制验证，不作结论；扫档在段 C）：终局原因 MajorRoundLimit×11 / PowerDominance×9（不收敛率 55%）；第 3 大回合领先者胜率（口径 A）95%（19/20）；首次冲突全部在第 4 大回合；整局无冲突 0 局。

人工看报告的观察：
- 营帐 96.9% 被控制、0 争议，与 design D-C"事实上只有主人能控制"一致；"控制过它的玩家胜率"对营帐与篝火恰为 25%（每家都控制过），该列对这两档没有区分度。
- 篝火主人控制占比 3.6%、主人从未控制 42/80——与 S-8"开局归邻家"预期方向一致，但幅度很大（主人首次控制平均第 5.5 大回合，超过一半的篝火主人整局没拿回）。Risks 里的"主人控制占比过低"报警值得在段 C 扫档时重点看。
- 石碑由桥头那家控制的小回合占 12.1%（占被控制的 22.9%），低于 S-13 担心的"固定归桥头那家"。
- 20 局领先者胜率 95%、不收敛 55%，远离 §16 目标；本段不调参（权重与分值归段 C），如实记录。

### 新增测试（753 → 797）

- `SiteControlSpec/据点控制判定Tests`（9）：占据即控制、唯一覆盖即控制、多人覆盖即争议、无人覆盖即无人、居高临下制造争议（真实 `CoverageTargets` 断言 + 占据后独得）、仰视无法争夺、林地据点只能占据、全部据点按坐标序给出、据点控制实现只读覆盖表（源码扫描守门：样本下界、反面命中 `CoverageMap.cs`、正向命中 `OwnershipOf(`）。
- `SiteControlSpec/据点档位与分值Tests`（8）：分值取自对局配置（10/30/90 端到端 MatchFlow + 预演与实际结算一致）、分值配置非法被拒（5 组：5/0/45、20/15/45、0/15/45、5/50/45、5/15/-1，直接校验 / 建局 / RunConfig 三处都点名档位）、标准局分值初值、据点不带额外效果（控制 4 石碑与无据点对照：展示 / 选取 / 部署上限 / 类型槽 / 先手修正全同）。
- `SiteControlSpec/据点分计入势力Tests`（4）：失去控制立即掉分、占据者被围杀（同一次结算 A −15、B +15）、弃赛者封锁据点、弃赛者与参赛者同时覆盖空据点为争议。
- `PieceEffects/高地压制加值Tests`（8）：规格 7 个场景 + 按棋串求和且弃赛者遗留棋子同样压制。
- `PowerScore/总势力Tests`：据点与棋串相加（77）、独占空格不计分（27）、孤立棋子的势力（1）。
- `PowerScore/势力明细Tests`：位置加值三来源可溯源（6 + 4 + 4 = 14）、据点分可溯源（65，占据 / 唯一覆盖标明）。
- `PowerScore/棋串军势公式Tests`：高地加值不被倍率放大（`⌊4×2.25⌋+3 = 12`）。
- `CoverageTerritory/空格归属三态Tests`：棋子格不在独占集合中（由删除的 `领地分Tests` 迁入）。
- `EliminationEndgame/终局名次与并列判定Tests`：信物相同比据点数、终局输入取控制中的据点数量（MatchFlow 接线）。
- `MatchFlowRegression/对局持久化Tests`：据点分值与终局据点数随存档往返且旧存档回填（非回填值 10/30/90、据点数 2；两条腿：逐字段 + 逐字节；旧格式剥字段后回填 5/15/45 + Backfilled、据点数 0）。
- `MatchTelemetry/据点遥测Tests`（9）：据点控制变化可查（真实盘面 → MatchSession → 文件往返；同局另放 P0 占据的营帐 E4，以非零 / 非 null 值钉住快照据点控制者、每名玩家据点分（5/0/0/0）、终局名次据点数（≥1，与活对象一致）、`HighGroundBonus` 写入、首部分值与据点表、活对象与文件逐条一致）、跑局配置与对局据点分值不一致即拒绝、扫档配置可追溯（3/8/24 + 全员 Safety 7，读 `config.json` 原文）、命令行据点分值（2 组）、命令行据点分值非法被拒（2 组）、据点主人按地图推导（v4 12 个据点 + 合成图推不出为 null）、据点分析分档输出（手算样本 + 一局被排除的旧日志；含「据点分占比口径」600 / 180 → 30% 与报告文本）。

### 既有测试改写（2.7；旧期望 → 新期望 → 依据）

| 文件 / 测试 | 旧期望 → 新期望 | 依据 |
|---|---|---|
| `CoverageTerritory/领地分Tests.cs`（整文件删除，3 条） | 孤立棋子势力 5、棋子格不重复计分 7+6、争议与中立领地各 3 | coverage-territory **REMOVED** 领地分。内容迁移：孤立棋子 → `总势力Tests.孤立棋子的势力`（5 → 1）；棋子格不重复 → `空格归属三态Tests.棋子格不在独占集合中`（13 → 6）；争议双方各 3 格 → `空格归属三态Tests.争议` 已有同形断言 |
| `CoverageTerritory/空格归属三态Tests` 独占 | 只断言归属 → 另加 `PowerCalculator` 总势力 1 | coverage-territory「独占」：不向 A 计入任何分数 |
| 同文件 争议 / 覆盖数量不影响独占 / 障碍不属于任何玩家 | `TerritoryScore` 3/3/9/3 → `ExclusiveCells.Length` 同值 | 字段删除；独占集合保留作展示 |
| `CoverageTerritory/弃赛玩家的遗留棋子仍产生覆盖Tests.弃赛者遗留棋子制造争议` | 空格 D5；A 领地 3、D 领地 3、D 势力 4 → D5 标为营帐：据点争议、A 据点分 0 势力 1、独占格仍各 3、D 势力 1 | coverage-territory「弃赛者遗留棋子制造争议」（改为据点格）+ 独占不计分 |
| `PieceEffects/倍增子的棋串倍率Tests.倍率不作用于领地` → 改名 `倍率不作用于据点分` | 10 独占 + 20 = 30 → 石碑 45 + 20 = 65 | piece-effects「倍率不作用于据点分」 |
| `PowerScore/总势力Tests.领地与棋串相加` → 改名 `据点与棋串相加` | 12 + 20 + 7 = 39 → 5 + 45 + 20 + 7 = 77 | power-score「据点与棋串相加」；原盘面保留为新测试「独占空格不计分」（27） |
| `PowerScore/总势力Tests.势力不累计` | 80 → 12 | 64 → 8（盘面不变，去掉独占格 16 与 4） | power-score「势力不累计」语义不变，数值按新公式 |
| `PowerScore/势力明细Tests.明细可复算总势力` | 复算起点 `TerritoryScore` → 测试内字面分值表复算据点分；盘面补营帐 A2 / 石碑 H2（争议）/ 篝火 J5；`PositionBonus` 三来源 | power-score「明细可复算总势力」 |
| `PowerScore/势力明细Tests.明细字段完整` | 领地 14、总势力 34 → 据点分 0、据点清单空、独占 14、总势力 20；加值三来源 (0,0,0) | power-score「势力明细」字段 |
| `PowerScore/棋串军势公式Tests.逐棋串取整` | `32 + TerritoryScore` → 32 | 独占不计分 |
| `PowerScore/势力名次Tests.排除非参赛玩家` | P3 38、名次 (1,37)(2,5) → 24、(1,25)(2,1) | 数值按新公式，名次结构不变 |
| `PowerScore/势力名次Tests.并列如实输出` | 37/37/5 → 25/25/1 | 同上 |
| `PowerScore/实时重算与公开排名Tests.每次结算后更新` | P0 13、P3 5 → 4、1 | 同上 |
| 同文件 `Pass也触发更新` | 5 / 5 → 1 / 1 | 同上 |
| 同文件 `弃赛者势力可见但不参与` | D 45、A 30 → 31、18（D 仍高于 A） | 同上 |
| `EliminationEndgame/主动弃赛Tests.保护期内允许弃赛` | 弃赛者势力 5 → 1 | 同上 |
| 同文件 `遗留棋子继续生效` | P0 势力 3+1 → 独占 3 格（断言 `ExclusiveCells.Length`）+ 势力 1；据点格那条腿由新测试覆盖 | elimination-endgame「遗留棋子继续生效」 |
| `EliminationEndgame/势力碾压Tests` 夹具 `Crushing` | (31,7,7,7) → (31,3,2,2) | 数值按新公式，碾压不等式不变 |
| 同文件 `开局不触发` | 首手势力 5 → 1 | 同上 |
| 同文件 `差一点不成为候选` | **改坐标重构**：P3 加 3 枚堡垒 → 7 枚（原靠领地凑过 31） | 几何算例改坐标，期望（不成为候选）不变 |
| 同文件 `取消后可再次成为候选` | **改坐标重构**：P2 增援 3 枚堡垒 → 7 枚 | 同上 |
| 同文件 `碾压只统计参赛玩家` | **改坐标重构**：P2 堡垒 7 → 8 枚 | 同上 |
| 同文件 `被拉下来则取消候选` | 提子后 (0,12,7,7) → (0,4,3,2)；**改坐标重构**：P2 补一子 C8（否则 P1 4 ≥ 2+2 顶上来成候选，与本 Scenario 无关） | 同上 |
| `Recruitment/落后者征募补偿Tests` 阶梯局面（10 处断言） | 9/7/5/3（并列 9/7/5/5、二人局 9/3、反超 24 vs 9）→ 4/3/2/1（4/3/2/2、4/1、10 vs 4） | 数值按新公式，名次与补偿期望全部不变 |
| `AiDecision/启发式评价维度Tests.评价覆盖七个维度` | P1 势力下降 2 → 1 | 独占不计分（敌损 = 势力下降 + 提子数 × 2） |
| 同文件 `先手位评价生效` | P1 14 / P0 10、提后 13 / 12 → **改坐标重构**：P1 补一子 J1，5 / 3、提后 4 = 4（并列第 1，先手值仍 +1）；安静落点 D4 仍名次不变 | 不补子则安静落点使 P0 与 P1 并列第 1、先手维不为 0，与本 Scenario 无关 |
| `TacticalLayers/五种战术信息层Tests.顺序层可解释下一轮排序` | 行势力 9/3/7/5 → 4/1/3/2 | 数值按新公式，名次 / 先手值 / 预测顺序不变 |
| `BatchPreview/批次预演必须显示的信息Tests.显示势力与排名变化` | 45→62（+17）/ P1 53 / P2 67 / P3 20 → 37→51（+14）/ 40 / 58 / 13 | 数值按新公式，名次变化不变 |
| `InformationVisibility/始终公开的信息Tests.势力明细公开` | 行视图 `TerritoryScore` → `SiteScore`；`TerritoryScore > 0` → `ExclusiveCells.Length > 1`（保住 M-V2 Take(1) 变异可红） | Presentation 最小改动；势力层据点语义留段 B |
| `SimulationHarness/批量跑局Tests.批量执行并汇总` | `config.ToJson()` 原样 → `config.Effective().ToJson()`，另断言权重为默认表、分值为标准局 | simulation-harness「扫档配置可追溯」 |
| 纯签名适配（期望不变） | `PowerCalculator.Compute(board, roster)` / `PowerScoreboard.Recalculate` / `BatchPreviewBuilder.Build` 加 `SiteValues.Standard` 参数：`启发式评价维度`、`批次预演必须显示的信息`、`非法批次必须说明原因并高亮`、`五种原型棋子的基础军势`、`效果随盘面实时重算`、`势力名次`、`势力明细`、`实时重算与公开排名`、`总势力`、`弃赛玩家的遗留棋子仍产生覆盖`、`PresentationFixtures`、`ScoringFixtures` | 分值无缺省的正式入口 |
| `EliminationEndgame/终局名次与并列判定Tests` 助手 | 参数名 `exclusive` → `sites`，`逐级比较到棋子数` 注释改"据点数"，数值不变 | elimination-endgame 并列链改名 |

### 变异验证（`scratchpad/mut_a2.py`：带时间戳备份 → 变异 → 全量 `dotnet test -c Release`（`DOTNET_CLI_UI_LANGUAGE=en`，以退出码判红）→ `finally` 写回原字节 → 与改前原文逐字节比对）

基线 797 全绿。每条 RC 均为 1、无编译错误；31 条全部"restored True"（写回内容与改前原文、备份文件三者逐字节一致）。全部还原后：`git diff` 与变异前快照 `cmp` 一致、`git status` 一致，再跑全量 797 通过、EXIT=0。

| 编号 | 变异 | 红 | 代表性红测试 |
|---|---|---:|---|
| M-S3（tasks 2.1） | `SiteControl.Compute` 注入 `_ = board.CoverageTargets(coord);` | 1 | `据点控制判定Tests.据点控制实现只读覆盖表` |
| M-S3b | 同处注入 `_ = board.Map.HeightAt(coord);` | 1 | 同上 |
| M-S1 | 占据映射成争议 | 9 | `占据即控制`、`居高临下制造争议`、`林地据点只能占据`、`占据者被围杀`、`弃赛者封锁据点`、`据点分可溯源`、`终局输入取控制中的据点数量`、存档往返、`大回合上限跑局Tests.上限写入对局配置并以规则原因终局` |
| M-S2 | 争议映射成无人 | 7 | `多人覆盖即争议`、`失去控制立即掉分`、`弃赛者遗留棋子制造争议`、`据点控制变化可查`、`明细可复算总势力` 等 |
| M-S8 | 占据的控制方式记成唯一覆盖 | 6 | `据点分可溯源`、`占据即控制`、`占据者被围杀` 等 |
| M-H1（tasks 2.3） | 高地加值去掉"严格低于"（`<` → `<=`） | 17 | `高地压制加值Tests.同高不加`，以及平地同高相邻的既有算例（碾压、先手、堡垒子不免死等） |
| M-H2（tasks 2.3） | 去掉"每枚至多 1"（删 `break`） | 1 | `高地压制加值Tests.多个低处敌子只加1` |
| M-H3（tasks 2.3） | `CoverageTargets(stone)` 改 `Neighbors(stone)` | 3 | `林地里的敌子压制不到`、`隔河压制`、`四邻接Tests.几何邻居枚举只在允许名单内直接调用` |
| M-S12 | 高地加值并入基础军势（被倍率放大） | 1 | `棋串军势公式Tests.高地加值不被倍率放大` |
| M-S5 | 总势力加回独占空格数 | 47 | `独占`、`独占空格不计分`、`孤立棋子的势力`、`棋子格不在独占集合中` 及全部按新公式改写的算例 |
| M-S20 | 总势力漏据点分 | 8 | `据点与棋串相加`、`倍率不作用于据点分`、`明细可复算总势力`、`失去控制立即掉分`、`占据者被围杀` 等 |
| M-S11 | 只给参赛玩家计据点分（弃赛者被过滤） | 1 | `据点分计入势力Tests.弃赛者封锁据点` |
| M-S9 | `Validated` 去掉"营帐 ≤ 篝火" | 1 | `分值配置非法被拒(20,15,45,"营帐")` |
| M-S4 | `MatchFlow.OnRecalculatePower` 改传 `SiteValues.Standard` | 1 | `据点档位与分值Tests.分值取自对局配置` |
| M-S4b | `BatchPreviewBuilder` 的 after 改用 `SiteValues.Standard` | 1 | 同上（预演 16 ≠ 实际 31） |
| M-S14 | `MatchFlow.Finish` 改回 `detail.ExclusiveCells.Length` | 2 | `终局输入取控制中的据点数量`、存档往返 |
| M-S13 | 并列链删除据点级（`c = 0`） | 1 | `终局名次与并列判定Tests.信物相同比据点数` |
| M-S15 | 存档不写 `SiteValues` | 1 | `对局持久化Tests.据点分值与终局据点数随存档往返且旧存档回填` |
| M-S16 | 读档 `ControlledSites` 恒 0 | 2 | 同上、`百局端到端Tests.连续一百局四人对局无死锁无非法状态` |
| M-T1 | 据点变化记成 `ControlChanged` 类型 | 1 | `据点遥测Tests.据点控制变化可查` |
| M-T2 | 日志棋串不写 `HighGroundBonus` | 1 | 同上 |
| M-T3 | 去掉跑局配置与对局分值一致性检查 | 1 | `跑局配置与对局据点分值不一致即拒绝` |
| M-T5 | `MatchSession.Create` 不把分值传入对局 | 1 | `扫档配置可追溯` |
| M-T4 | `config.json` 改回 `config.ToJson()` | 1 | `批量跑局Tests.批量执行并汇总` |
| M-T6 | `SiteAttribution` 区域推导不排除桥格 | 1 | `据点主人按地图推导` |
| M-T7 | 分析把缺据点字段的旧日志当"无据点"纳入 | 1 | `据点分析分档输出`（纳入 2 局） |
| M-T8 | 终局据点分占比计入弃赛者 | 1 | 同上 |
| M-T9 | 主人口径把推不出主人的据点计入分母 | 1 | 同上 |
| M-T10 | 日志玩家据点分恒写 0 | 1 | `据点遥测Tests.据点控制变化可查` |
| M-T11 | 快照据点控制者恒写 null | 1 | 同上 |
| M-T12 | 终局名次据点数恒写 0 | 1 | 同上 |

tasks 要求的 4 条（M-S3、M-H1、M-H2、M-H3）均红；其余 27 条为自做（M-T10～12 为复核后补：原测试只用零值 / null 断言这三个写入字段，补营帐 E4 后三条均红）。

### 偏离 / 待决

1. **Presentation / Godot 最小改动**（只为编译）：行视图 `TerritoryScore` → `SiteScore`、Hud 三处文案；`GroupPowerView.FormulaText` 未拆出高地（高地加值非 0 时括号内"连珠 / 协同"两项之和小于位置加值），势力层据点项与公开视图据点状态均留段 B。
2. **`PowerCalculator.Compute(board, roster)` 两参重载已删除**，正式入口必须传 `SiteValues`（防止静默用默认分值）；只保留无名册的测试便利重载 `Compute(board)` = 标准局分值。`ConsoleController`（play 命令的预演）原来走无名册重载，已改为带名册与对局分值。
3. **config.json 写"实际生效配置"**（`Effective()`：未配置权重填默认表）。日志首部的 `Config` 仍是原样配置（`Weights` 可为 null），首部另有取自对局本身的 `SiteValues`。
4. **扫档注意（既有行为，未改）**：`run` 命令只要给了 `--difficulty` 或 `--players` 就重建整个玩家列表，配置文件里的 `Weights` 会被丢弃。Safety 扫档必须用配置文件指定 `Players[].Difficulty` 与 `Weights`，且不传这两个参数；每批核对 `config.json`。已写进用法说明。
5. **石碑"相邻桥头那家"推导规则**（选定一种并记录）：石碑几何相邻的公共信物格（不属于任何出生区）→ 与之有气边的桥格 → 桥另一侧的格所在的"不经桥可达区域"的出生区。v4 上四块石碑都能唯一推出（H5/J8/F9/E6 → 0/1/2/3），恰与同编号篝火（J2/M9/E12/B5）的主人相同。任一步不唯一时为 `null`，分析里单列"推不出主人"计数。
6. **SiteAttribution 放在 Core**（只供遥测，不参与规则）：分析端只读日志不重建地图（design D6），所以由跑局层建局时算好写进首部。它用 `Adjacency.LibertyNeighbors` 做 BFS、`Adjacency.AreAdjacent` 判几何相邻，没有新增邻接实现；`boundaries.md` 单一实现表是否补一行留段 D。
7. **各棋子势力占比（4b）不含高地加值**：高地加值按棋子而非棋子类型产生，未归因到任何类型；报告文案已注明。
8. **出生区编号**：日志 `HomeZone` 与分析内部仍 0 起（S-14 留段 D）。
9. **20 局旁证**（非结论）：领先者胜率 95%、不收敛 55%、篝火主人控制占比 3.6%（42/80 个篝火主人整局未夺回）。按 Risks 的报警项在段 C 扫档重点看。

## 段 A2 检查（trellis-check）

### 结论

1. **规格场景与 tasks 算例**：site-control / power-score / piece-effects / coverage-territory / elimination-endgame / match-telemetry / simulation-harness 的 A2 场景均有同名或对应测试，断言钉住数值。算例逐条找到：77（`总势力Tests.据点与棋串相加`）、27（`独占空格不计分`）、1（`孤立棋子的势力`）、`⌊4×2.25⌋+3=12`（`棋串军势公式Tests.高地加值不被倍率放大`）、10/30/90 下篝火 30（`据点档位与分值Tests.分值取自对局配置`，另钉预演）、5/0/45 与 20/15/45 被拒并点名档位（`分值配置非法被拒`，三处入口）、并列链 3 vs 1（`信物相同比据点数`，棋子数反向以排除误比）、弃赛 D 占营帐含 5（`据点分计入势力Tests.弃赛者封锁据点`，D 5/6、不占名次）。未覆盖：site-control「据点公开 / 开局即可见」无测试——属 tasks 4.1（公开视图据点状态），留段 B。
2. **既有测试改写**：清单逐条核对，数值变化均可由"去掉独占空格数"直接推出（如 45→37 = 去 8 格、势力不累计 80→64 / 12→8）；4 处"改坐标重构"（碾压 3 处、先手位 1 处）期望不变，补子理由成立。删除的 `领地分Tests` 三条非计分断言均已迁移（独占集合 F5/E6/G6/F7 → `孤立棋子的势力`；棋子格不在独占集合 → `空格归属三态Tests.棋子格不在独占集合中`；争议双方各 3 → `空格归属三态Tests.争议` 已有同形断言）。未发现为凑绿改无关期望。
3. **单一实现**：`SiteControl` 只读 `CoverageMap.OwnershipOf`（守门 + M-S3/M-S3b）；高地加值只在 `PieceEffects.HighGroundBonus` 经 `board.CoverageTargets`；生产代码 `PowerCalculator.Compute` 调用点 7 处全部带对局分值。`SiteAttribution` 只被 `Sim/MatchSession` 首部引用，规则代码未引用；其 BFS 用 `Adjacency.LibertyNeighbors`、几何相邻用 `Adjacency.AreAdjacent`，没有新邻接 / 覆盖实现。与 `MapValidator` 私有 `MultiSourceDistances` / `FloodFill` 语义不同（需排除桥格、且校验器 helper 为 private），不算第二处距离实现；`boundaries.md` 是否补"据点主人（遥测）"一行留段 D。
4. **20 局剧变不是计分 bug，是规则 / 参数的真实后果**（数字见下节）。
5. **`--difficulty` / `--players`**：用法行已写明"权重只能经 `--config` 指定，同时给二者会重建玩家列表、丢弃配置文件里的权重"；重建后 `Weights = null`，AI 侧 `weights ?? EvaluationWeights.Default`（与难度无关），`config.json` 写 `Effective()` 填同一 Default，如实。`sim-out/a2-sweepcheck/config.json` 实查 3/8/24 + 四家 Safety 7。
6. **越段**：Presentation 仅 `PlayerPowerRowView.TerritoryScore → SiteScore`、Godot `Hud` 三处文案；`MatchPublicView` 加 `SiteValues` 字段（Core 公开数据，非界面语义）。未实现第 4 组。本次检查未改 Presentation / Godot，未跑 Godot 构建。

### 第 4 项：20 局（`sim-out/a2-20/`）计分核对

脚本 `scratchpad/verify_a2.py` 流式读 20 份 gzip 日志，逐小回合 × 每名玩家核对：
- 据点分 = 快照 `Sites` 中 Holder=该玩家的据点按 5/15/45 求和 = 日志 `SiteScore`；
- 每串军势 = `⌊Base × 1.5^min(n,3)⌋ + 连珠 + 协同 + 高地` = 日志 `Power`；
- 总势力 = 据点分 + Σ军势 = 日志 `Total`；
- `Occupied` 据点格上确有该持有者棋子、非 `Occupied` 据点格上无棋子；`UniqueCoverage` 持有者在直线 ≤2 格内有棋子、`Contested` 附近至少两家（粗查）；
- 终局 `Standings.Power` = 最后快照 `Total`，`ControlledSites` = 最后快照持有据点数。

结果：992 小回合、0 差异；20 局无弃赛 / 出局者（弃赛误计无从发生）；据点分每名玩家只按控制者累加一次，无重复计入。碾压判定 `MatchFlow.cs:640` 读 `power.Of(p).Total`，即新总势力；抽查 `0x11`（第 7 大回合碾压）：P1 据点 200（J2 篝火唯一覆盖 15、L2 营帐 5、四块石碑 180）+ 军势 146 = 346 ≥ 其余 132+78+104 = 314，手算与日志一致。

机制证据：
- **第 3 大回合四家据点分全部恒为 20**（自家营帐 5 + 邻家篝火 15，C4 对称抵消），第 3 大回合领先完全由军势决定；领先者第 4 大回合先手行动，且**20/20 局第 4 大回合首个控制石碑的就是第 3 大回合领先者**，19/20 最终获胜。即"军势领先 → 先手 → 先上岛拿 45 分 → 滚雪球 / 碾压"，对应 design Risks 第 1 条与 §16 首判据，归段 C 扫档。
- **整轮 Pass 0 局**：v3 的 176 局 AllPassed 来自"填自家独占格净收益 0（军势 +1、领地 −1）"；现在独占格不计分，几乎任意安全落子势力增量 ≥ +1。20 局共 15 次 Pass（1.5%，v3 15.7%），全部在第 8 大回合及之后、手牌非空、无非法批次，为个别小回合评价无正收益手，不构成整轮 Pass。失去 AllPassed 出口后终局只剩上限（11）与碾压（9）。

### 问题清单

| # | 问题 | 处理 |
|---|---|---|
| C-1 | 遥测变化判定 `wasKind != now.Kind \|\| wasHolder != now.Controller` 的 `\|\|` 改 `&&` 全绿（既有用例的变化都是两项同时变；testing.md「`A \|\| B` 要改 `&&` 做变异」） | 已修：新增 `据点遥测Tests.控制者不变而控制方式变化也记事件`（UniqueCoverage:P1 → Occupied:P1） |
| C-2 | AI 评价器 `BatchEvaluator` 改用 `SiteValues.Standard` 全绿（`分值取自对局配置` 只钉了结算与预演，没钉 AI 的势力增量维度；扫档非标准分值时 AI 会静默按 5/15/45 决策） | 已修：新增 `据点档位与分值Tests.AI评价使用对局据点分值`（10/30/90 下 PowerGain = 31） |
| C-3 | site-control「据点公开 / 开局即可见」无测试 | 未修：属 tasks 4.1 公开视图据点状态，段 B |
| C-4 | `PreviewPresentation.GroupPowerView.FormulaText` 高地加值非 0 时括号内两项之和 ≠ 位置加值（实现方已自报） | 未修：段 B |
| C-5 | `boundaries.md` 单一实现表未补据点控制 / 高地加值 / 据点主人（遥测） | 未修：tasks 6.2，段 D |

### 变异（`scratchpad/mut_check.py`：备份 → 替换（断言原串唯一）→ 全量 `dotnet test -c Release` → `finally` 写回 → 与原字节、备份三方逐字节比对；全部完成后 `git diff` / `git status --short` 与变异前快照 `cmp` 一致）

| 编号 | 变异 | 结果 | 还原 |
|---|---|---|---|
| N-1 | `MatchSession` 据点变化判定 `\|\|` → `&&` | 补测试前：**全绿 797（缺口 C-1）**；补后红 1 `据点遥测Tests.控制者不变而控制方式变化也记事件` | restored True |
| N-2 | `BatchEvaluator` 构造 `_siteValues = view.SiteValues` → `SiteValues.Standard` | 补测试前：**全绿 797（缺口 C-2）**；补后红 1 `据点档位与分值Tests.AI评价使用对局据点分值` | restored True |
| N-3 | `PieceEffects.HighGroundBonus` 去掉 `enemy.Owner != group.Owner`（己方低处棋子也算） | 红 2：`高地压制加值Tests.己方棋子不算`、`连珠子的位置加值Tests.崖壁截断连珠线` | restored True |

### 验证

- `dotnet build`：0 警告 0 错误，EXIT=0。
- `dotnet test -c Release`（输出落文件后取 `$?`）：**799 通过 0 失败，EXIT=0**（797 + 本次新增 2）。
- Godot：本次未改 `src/godot/` 与 Presentation，未重跑。

## 段 B（界面，tasks 4.1–4.3）

### 改动文件

- Presentation：
  - 新增 `src/Siege.Presentation/Visibility/SiteView.cs`：`SiteView`（坐标、档位、档位名、分值、控制状态、控制者、争议覆盖方、状态文案）与 `SiteViews.From(PublicWorld)`。状态一律取 `PowerSnapshot.SiteStates`；争议覆盖方取 `Coverage.SourcesOf` 的来源棋子所有者（读数）；分值取公开快照的 `SiteValues`。插旗阶段 `Power` 为 null 时按地图列出据点、状态记无人（空盘事实，注释写明）。
  - `Visibility/DefaultBoardView.cs`：加 `Sites` 集合（与 `Fences` 平行，不改 `BoardCellView`）。
  - `Layers/LayerContents.cs`：删除 `TerritoryContributionView`；`PowerLayerContent.TerritoryCells` → `Sites`（`ImmutableArray<SiteView>`）；插旗阶段势力层也给出据点。
  - `Preview/PreviewPresentation.cs`：`GroupPowerView` 加 `HighGroundBonus`；`FormulaText` 为 `位置加值 N（连珠 a / 协同 b / 高地 c）`（位置加值 0 时文案不变）。修掉 C-4。
  - `Text/Labels.cs`：`SiteTier`、`SiteStatus`。
- Godot（`src/godot/scripts/`）：
  - `LowPoly.cs`：`Tent` / `Campfire` / `Stele` 三种程序化低多边形地标、`SiteFlag`（主色旗面 + 两面徽记）、`ContestedFlags`（交叉杆 + 无徽记金色三角旗）、私有 `Prism`（程序化直棱柱）。
  - `Visuals.cs`：帆布 / 门洞 / 火焰两色 / 石碑石面 / 刻痕 6 个颜色。
  - `BoardView.cs`：`_sites` 节点，`Refresh` 里 `DrawSites`（在棋子之前）；有正式棋子 / 本人暂放 / 打开信息层时地标 ×0.375 退到远侧格角 (−0.35, −0.35)（营帐缩后最近角距格心 0.363 > 棋子底座半径 0.36；首版 ×0.42 / (−0.30, −0.30) 算得营帐最近 0.276 会压底座，已改）、旗帜 ×0.62 插在棋子身后；势力层 `DrawPower` 改为据点格着色 + 争议虚框，不再给空格着领地色。
  - `Hud.cs`：势力层面板加说明行与 12 条据点项；归属读法图例"实色 = 独占（计入领地分）"改"（只作判读，不计分）"。
  - `GameRoot.cs`：`Capture` 打印图片尺寸、帧号、大回合、信息层与当刻全部据点状态（供截图清单对照）。
- 测试：
  - 新增 `SiteControlSpec/据点公开Tests.cs`（`开局即可见`，v4 真图，插旗阶段与锁定后各查一次，四名观察者 × 默认棋盘 / 势力层；修掉 C-3）。
  - 新增 `InformationVisibility/据点控制公开Tests.cs`（`据点控制公开`：占据 / 唯一覆盖 / 争议 / 无人四态 + 控制者 + 覆盖方 + 文案，与 Core 快照逐项一致；`据点分值取自对局配置`：10/30/90）。
  - 新增 `TacticalLayers/势力层据点与高地Tests.cs`（`势力层显示据点控制`：石碑 45 争议、覆盖方 P0/P1；`势力层视图模型不含领地贡献字段`：反射守门；`势力层显示高地加值`：h=2 两子各压一子 → 高地 2，文案含"高地 2"）。
  - 守门：`UI层不含规则计算Tests` 的 `ForbiddenTypes` 加 `SiteControl`；`Godot层不含规则计算Tests` token 表加 `"SiteControl.Compute("`、`"PieceEffects"`、`".HighGroundBonus("`（不加裸 `SiteControl`，避免误伤 `SiteControlKind`；`GroupPowerView.HighGroundBonus` 属性读不带括号，合法）。
- 美术：`art/sites-v4/`（README + 4 张图）。

### 既有测试改写（旧 → 新 → 依据）

| 测试 | 旧 | 新 | 依据 |
|---|---|---|---|
| `始终公开的信息Tests.势力明细公开` | 断言势力层 `TerritoryCells` 等于 `ExclusiveCells`，且独占格 > 1（M-V2 变异对象） | 删除这两条，保留棋串与行视图断言；注释说明 M-V2 对象已删除，据点清单公开改由 `据点控制公开Tests` 覆盖 | 4.1：势力层不再含领地贡献 |
| `信息层的可用时机与无副作用Tests.他人行动时可查看` | 每名玩家 `ExclusiveCells` = 势力层 `TerritoryCells` 中该玩家的格 | 每名玩家 `PlayerPower.Sites` 坐标 = 势力层 `Sites` 中控制者为该玩家的坐标 | 同上 |
| `棋串军势公式Tests`（第 53 行） | `位置加值 12（连珠 12 / 协同 0）` | `位置加值 12（连珠 12 / 协同 0 / 高地 0）` | 4.1：棋串分数拆出高地 |

### 验证

- `dotnet build`：0 警告 0 错误，EXIT=0。
- `set -o pipefail; dotnet test -c Release`（输出落文件后取 `$?`）：**805 通过 0 失败，EXIT=0**（799 + 新增 6）。
- Godot（最后一次改 Godot 源码之后跑）：
  - `--headless --path src/godot --build-solutions --quit`：EXIT=0，输出无 `error CS` / `warning CS`。
  - `--headless --path src/godot --quit-after 3000 -- --auto-demo`：EXIT=0，`[auto-demo] 终局：第 4 大回合，达到大回合上限；…`。
  - `--headless --path src/godot --quit-after 3000 -- --auto-demo --pick-check`：EXIT=0，`[pick-check] 可落子格 105，往返一致 105，失败 0`。注意：拾取是数学投影，地标无碰撞体，pick-check 对地标遮挡零敏感，不作地标验证。
- 截图（非 headless，种子 12345，均 1600×900，EXIT=0）：`art/sites-v4/sites-v4-opening.png`（第 14 帧，插旗阶段，12 据点全无人）、`sites-v4-midgame.png`（auto-demo 第 44 帧，第 4 大回合、信息层关：6 个被控制（3 占据 + 3 唯一覆盖）、4 块石碑争议、2 个无人）、`sites-v4-midgame-gray.png`（中局灰度版）、`sites-v4-endgame.png`（第 90 帧终局结算）。帧号用临时逐帧打印选出（中局 42–46 帧同时有控制与争议且无信息层），临时打印已删除（`GameRoot.cs` 从备份拷回，`grep TEMPSITES` = 0）。截图未读入会话，README 清单第 1–8 项待人工确认。

### 变异验证（`scratchpad/mut_b.py`：带时间戳备份 → 替换（断言原串唯一）→ 全量 `dotnet test -c Release`（`DOTNET_CLI_UI_LANGUAGE=en`）→ `finally` 写回原字节 → 与原字节、备份逐字节比对；全部完成后 `git status --short` / `git diff` 与变异前快照 `cmp` 一致）

| 编号 | 变异 | 结果 | 还原 |
|---|---|---|---|
| M-B7 | **（4.3 要求）** `BoardView.DrawSites` 加 `_ = Siege.Core.Scoring.SiteControl.Compute(null!, null!);` | 红 1：`Godot层不含规则计算Tests.Godot层不调用规则计算入口` | True |
| M-B8 | 同处改加 `PieceEffects.HighGroundBonus(null!, null!)` | 红 1：同上 | True |
| M-B1 | `SiteViews.From` 快照为 null 时 `.Take(0)`（插旗阶段不列据点） | 红 1：`据点公开Tests.开局即可见` | True |
| M-B2 | `SiteViews.Build` 控制者恒写 null | 红 1：`据点控制公开Tests.据点控制公开` | True |
| M-B3 | `SiteViews.Build` 分值改 `SiteValues.Standard.Of` | 红 1：`据点控制公开Tests.据点分值取自对局配置` | True |
| M-B4 | 争议覆盖方恒为空 | 红 2：`据点控制公开`、`势力层显示据点控制` | True |
| M-B5 | `PowerLayerContent` 加回 `ImmutableArray<Coord> ExclusiveCells = default` | 红 1：`势力层视图模型不含领地贡献字段` | True |
| M-B6 | `GroupPowerView.FormulaText` 去掉"/ 高地 N" | 红 2：`棋串军势公式Tests`（第 53 行那条）、`势力层显示高地加值` | True |
| M-B9 | `SiteViews.Build` 自调 `SiteControl.Compute(board, power.Coverage)` | 红 1：`UI层不含规则计算Tests.表现层不调用规则计算入口` | True |

### 偏离 / 待决

1. **未改 Core**：没有给 `MatchPublicView` 加字段。公开快照已含 `SiteValues` 与 `Power.SiteStates`（插旗锁定即重算，`MatchFlow.cs:317`）；只有插旗阶段 `Power` 为 null，由 `SiteViews.From` 按地图列出、状态记无人。如负责人认为这算表现层"推状态"，可改为 Core `Publish` 在快照为 null 时调 `SiteControl.Compute` 给出。
2. **争议覆盖方**由 Presentation 读 `Coverage.SourcesOf` 的来源棋子所有者得到（Core 的 `CellCoverage` 只有数量与唯一覆盖者，没有覆盖方清单）。只读、不算覆盖；`UI层不含规则计算` IL 守门仍绿。
3. **打开任一信息层时地标也退到格角**（spec 只要求占据时退让）：为了不挡气点与着色（visual-style-baseline"不遮挡合法落点与气的判读"）。本人暂放棋子的格同样退让。
4. **中局截图取第 4 大回合**：`--auto-demo` 固定 4 个大回合，前 3 个大回合只有营帐 / 篝火被控制、无争议；第 4 大回合才出现争议。终局图状态与中局相同，结算面板盖住岛心。
5. **视觉未经本人目检**：截图未读入会话（用户约束），地标尺寸 / 徽记在远半盘的可读性、退让后旗帜是否被棋子遮挡等以 `art/sites-v4/README.md` 人工清单为准；不合格需要调 `LowPoly` 尺寸常量与 `BoardView.SiteAside*`。
6. `Hud` 归属读法图例的"计入领地分"是段 A2 起就错的文案，顺手改掉。

## 段 B 检查（trellis-check）

### 结论（逐项）

1. 场景测试：势力层显示据点控制、势力层显示高地加值、据点控制公开（四态 + 分值取配置）、开局即可见均有测试且断言钉住。盲区 1 处：`Labels.SiteTier` 营帐 / 篝火文案无测试（互换全绿，见 M-BC2-对照），已在 `开局即可见` 补 `TierText` 断言。另：`.Distinct()` 去掉仍全绿（测试里每方只有一枚覆盖子），不阻塞，未修。
2. **偏离 1 判为表现层自推状态**（在表现层写出 `SiteControlKind.Unclaimed`），且前提未被 Core 封死：`MatchFlow.Restore` 对插旗阶段存档跳过 `RecalculateDerived`、不校验盘面无棋子。**已改为 Core 提供**：`MatchPublicView` 加 `ImmutableArray<SiteState> SiteStates`（唯一构造点 `MatchFlow.Publish`），值为 `Scoreboard.Latest?.SiteStates ?? SiteControl.Compute(Board, CoverageMap.Compute(Board))`；`SiteViews.From` 删掉 null 分支，统一读 `View.SiteStates`，覆盖方在无势力快照时为空。`开局即可见` 插旗阶段加 Core 快照 12 据点全无人断言、锁定后加 `View.SiteStates == Power.SiteStates`。**段 B 变异表的 M-B1（`.Take(0)`）变异对象已不存在，由 M-BC1 替代。** 本项使段 B diff 含 Core 两个文件。
3. 偏离 2：`CoverageMap.SourcesOf` 是只读访问器，取来源棋子主人是投影；IL 守门禁 `CoverageMap.Compute` 与 `SiteControl` 整型，通过。
4. Godot：`LowPoly` / `BoardView` 无 `CollisionShape` / `StaticBody` / `Area3D`，地标是纯 `MeshInstance3D`；退让只读 `CellAt().Occupant`、预演 `StagedPieces`、`content is not null`，不读地形、不算控制。token 表 `"SiteControl.Compute("` 收紧为 `"SiteControl."`（`SiteControlKind.` 不含该子串）。`领地分` / `TerritoryScore` 全仓：src 仅 `Siege.Sim/Logging/MatchLog.cs:377` 旧日志兼容注释；tests 全是 2.7 改写说明；其余在归档 change / 归档任务 / 设计文档 v1 / `openspec/specs/` 主规格（归档 scoring-sites 时同步，不在段 B 范围）。`Territory` 在 src 只剩盘面层归属读法（`TerritoryLayerContent` / `DrawTerritory` / `RenderLayer.TerritoryTint`），合法。
5. `art/sites-v4/README.md`：规格 3 条 MUST 与 4 个 Scenario 均对到清单 1–9 项，数值与代码一致。补"已知限制"两条：规格实例（唯一覆盖的石碑、争议的篝火）截图无一一对应；"可被拾取"由无碰撞体保证。
6. 既有测试改写 3 处均只因领地退役 / 高地拆分。注意 `信息层的可用时机与无副作用Tests.他人行动时可查看` 的新断言在无据点的合成图上两边都是空集（恒真），据点一致性由 `据点控制公开Tests` 覆盖，未修。

### 修复

- `src/Siege.Core/Match/MatchPublicView.cs`、`MatchFlow.cs`（`Publish`）：加 `SiteStates`。
- `src/Siege.Presentation/Visibility/SiteView.cs`：删插旗阶段自记无人分支。
- `tests/.../SiteControlSpec/据点公开Tests.cs`：Core 快照断言 + `TierText` 断言。
- `tests/.../BatchPreview/Godot层不含规则计算Tests.cs`：token 收紧。
- `art/sites-v4/README.md`：已知限制两条。

### 变异（`scratchpad/mut_bc.py`：带时间戳备份 → 替换（断言原串唯一）→ 全量 `dotnet test -c Release` → `finally` 写回 → 原字节 / 备份三方比对；完成后 `git status --short` / `git diff` 与变异前快照 `cmp` 一致）

| 编号 | 变异 | 结果 | 还原 |
|---|---|---|---|
| M-BC1 | `Publish` 中 `power?.SiteStates ?? SiteControl.Compute(...)` → `?? []` | 红 1：`据点公开Tests.开局即可见` | True |
| M-BC2 | `Labels.SiteTier` 营帐 / 篝火文案互换 | 红 1：`据点公开Tests.开局即可见` | True |
| M-BC2-对照 | 同 M-BC2，且把新加的 `TierText` 断言换回按枚举自比 | 绿 805（证明补断言前是盲区） | True |

### 验证

- `dotnet build`：0 警告 0 错误，EXIT=0。
- `set -o pipefail; dotnet test -c Release`：805 通过 0 失败，EXIT=0（补的是断言，不增测试数）。
- Godot：`--build-solutions` EXIT=0（无 `error CS` / `warning CS`）；`--auto-demo` EXIT=0（第 4 大回合终局）；`--auto-demo --pick-check` EXIT=0（105 / 105，失败 0）。

### 未修 / 待决

- `.Distinct()` 无测试钉住（见第 1 项）；`他人行动时可查看` 据点断言恒真（见第 6 项）。
- 截图第 1–8 项仍待人工目检（本次未读图）。

## 段 C 扫档（主会话执行）

全部批次种子 1–200、4 人 Standard、大回合上限 15、v4；每批跑完核对 config.json 实际生效值一致；run / analyze 退出码均为 0。

### 第 1 步：据点分量三档（Safety 5）

| 指标 | sites-scale-1-5 | sites-scale-1-3 | sites-scale-1-2 |
|---|---|---|---|
| 领先者胜率A | 90.0% | 95.5% | 92.0% |
| 第4回合先手平均名次 | 1.12 | 1.03 | 1.07 |
| 整局无提子局 | 0 | 0 | 0 |
| 据点分占比 | 17.1% | 30.4% | 51.8% |
| 首次冲突 | 4 | 4 | 4 |
| 终局局平均结束 | ? | 9.82 | 8.43 |
| 终局原因 | AllPassed×49，MajorRoundLimit×94，PowerDominance×57 | AllPassed×15，MajorRoundLimit×99，PowerDominance×86 | AllPassed×4，MajorRoundLimit×72，PowerDominance×124 |
| 不收敛率 | 47.0% | 49.5% | 36.0% |
| 碾压成立回合 | 10.68 | 9.28 | 8.24 |
| 篝火控制/争议 | 67.0%/28.5% | 74.1%/22.9% | 78.3%/18.3% |
| 石碑控制/争议 | 57.5%/19.4% | 56.3%/19.3% | 51.6%/20.4% |
| 篝火主人占被控制 | 7.6% | 5.1% | 6.2% |
| 石碑桥头家占被控制 | 25.6% | 24.9% | 26.0% |
| 高地加值占比 | 0.7% | 0.8% | 1.1% |
| Pass率 | 6.3% | 2.4% | 0.7% |
| 出生区胜率 | 23.5/27.0/27.5/22.0 | 23.5/24.5/27.0/25.0 | 23.5/24.5/32.0/20.0 |

诊断（流式读日志）：1/3 档终局赢家平均控制石碑 2.96 块、其余三家 0.08；赢家 = 第 4 大回合先手 196/200。1/5 档对应 3.0 / 0.13、183/200。

### 第 2 步：Safety 九档 + 22/25/27 加密档（分值 5/15/45）

| 指标 | sites-safety3 | sites-safety5 | sites-safety7 | sites-safety8 | sites-safety10 | sites-safety20 | sites-safety22 | sites-safety25 | sites-safety27 | sites-safety30 | sites-safety40 | sites-safety60 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 领先者胜率A | 80.5% | 95.5% | 95.5% | 96.0% | 95.0% | 95.5% | 76.0% | 31.0% | 25.5% | 17.0% | 13.5% | 15.5% |
| 第4回合先手平均名次 | 1.37 | 1.03 | 1.02 | 1.02 | 1.03 | 1.03 | 1.39 | 2.17 | 2.19 | 2.26 | 2.61 | 2.8 |
| 整局无提子局 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 116 | 117 |
| 据点分占比 | 28.7% | 30.4% | 32.4% | 31.5% | 32.1% | 34.3% | 22.6% | 19.1% | 22.1% | 23.3% | 34.2% | 38.6% |
| 首次冲突 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | 4 | ? | ? |
| 终局局平均结束 | 9.61 | 9.82 | 9.76 | 9.28 | 9.33 | 9.86 | 9.95 | 8.18 | 7.78 | 7.65 | 7.69 | 7.51 |
| 终局原因 | AllPassed×1，MajorRoundLimit×129，PowerDominance×70 | AllPassed×15，MajorRoundLimit×99，PowerDominance×86 | AllPassed×20，MajorRoundLimit×76，PowerDominance×104 | AllPassed×20，MajorRoundLimit×70，PowerDominance×110 | AllPassed×23，MajorRoundLimit×57，PowerDominance×120 | AllPassed×7，MajorRoundLimit×59，PowerDominance×134 | AllPassed×92，MajorRoundLimit×54，PowerDominance×54 | AllPassed×169，MajorRoundLimit×30，PowerDominance×1 | AllPassed×182，MajorRoundLimit×13，PowerDominance×5 | AllPassed×183，MajorRoundLimit×16，PowerDominance×1 | AllPassed×173，MajorRoundLimit×24，PowerDominance×3 | AllPassed×149，MajorRoundLimit×50，PowerDominance×1 |
| 不收敛率 | 64.5% | 49.5% | 38.0% | 35.0% | 28.5% | 29.5% | 27.0% | 15.0% | 6.5% | 8.0% | 12.0% | 25.0% |
| 碾压成立回合 | 9.54 | 9.28 | 9.19 | 8.71 | 8.91 | 9.71 | 10.44 | 9 | 10 | 8 | 9 | 7 |
| 篝火控制/争议 | 78.0%/19.1% | 74.1%/22.9% | 73.2%/23.6% | 67.4%/29.3% | 64.2%/32.3% | 33.4%/56.8% | 39.7%/45.3% | 43.2%/29.5% | 43.8%/19.8% | 40.6%/17.1% | 49.3%/9.9% | 56.3%/4.4% |
| 石碑控制/争议 | 57.2%/19.7% | 56.3%/19.3% | 54.2%/20.0% | 51.7%/21.4% | 51.6%/20.6% | 45.2%/28.2% | 18.2%/54.7% | 8.1%/57.5% | 8.7%/52.4% | 7.5%/53.5% | 16.4%/46.7% | 19.3%/47.2% |
| 篝火主人占被控制 | 8.2% | 5.1% | 5.7% | 5.5% | 4.6% | 1.1% | 4.5% | 10.3% | 10.6% | 17.1% | 16.7% | 22.6% |
| 石碑桥头家占被控制 | 25.3% | 24.9% | 24.6% | 25.6% | 24.3% | 23.7% | 28.6% | 22.6% | 25.5% | 24.8% | 16.4% | 21.9% |
| 高地加值占比 | 0.9% | 0.8% | 0.9% | 0.9% | 0.9% | 0.4% | 0.6% | 0.7% | 0.6% | 0.7% | 0.6% | 1.0% |
| Pass率 | 1.0% | 2.4% | 2.8% | 3.4% | 4.1% | 6.4% | 18.1% | 24.8% | 22.1% | 22.6% | 25.3% | 25.7% |
| 出生区胜率 | 22.5/23.0/31.0/23.5 | 23.5/24.5/27.0/25.0 | 23.0/27.0/28.0/22.0 | 27.5/21.0/30.0/21.5 | 23.5/29.0/25.5/22.0 | 28.5/29.0/21.5/21.0 | 24.5/29.0/27.5/19.0 | 23.0/23.5/27.0/26.5 | 20.0/24.5/28.5/27.0 | 17.5/21.0/30.0/31.5 | 32.0/20.5/22.5/25.0 | 27.5/21.5/27.5/23.5 |

每批次平均提子：3→1.75、5→1.54、10→1.39、20→1.15、22→0.58、25→0.34、27→0.29、30→0.29、40→0.21、60→0.28（v3 基线 0.40）。1/5 档终局局平均结束 11.66（表中 ? 为解析偏离行）；40 / 60 首次冲突为 ?：整局无提子 116 / 117 局。

### 拍板与落地（裁决 S-15）

负责人选 Safety 27、分值保持 5/15/45。`EvaluationWeights.Default.Safety` 5→27，校准注释与 `默认评价权重的校准Tests` 同步（两处期望 5→27、数据路径断言改 `sim-out/sites-safety`）。全量 805 通过 EXIT=0。变异 M-C-1：默认值改 25 → 红 3，还原 cmp 一致。

### 确认跑 200 局（`sim-out/sites-final/`，不带配置文件，`--difficulty Standard`）

| 指标 | sites-safety27 | sites-final |
|---|---|---|
| 领先者胜率A | 25.5% | 25.5% |
| 第4回合先手平均名次 | 2.19 | 2.19 |
| 整局无提子局 | 0 | 0 |
| 据点分占比 | 22.1% | 22.1% |
| 首次冲突 | 4 | 4 |
| 终局局平均结束 | 7.78 | 7.78 |
| 终局原因 | AllPassed×182，MajorRoundLimit×13，PowerDominance×5 | AllPassed×182，MajorRoundLimit×13，PowerDominance×5 |
| 不收敛率 | 6.5% | 6.5% |
| 碾压成立回合 | 10 | 10 |
| 篝火控制/争议 | 43.8%/19.8% | 43.8%/19.8% |
| 石碑控制/争议 | 8.7%/52.4% | 8.7%/52.4% |
| 篝火主人占被控制 | 10.6% | 10.6% |
| 石碑桥头家占被控制 | 25.5% | 25.5% |
| 高地加值占比 | 0.6% | 0.6% |
| Pass率 | 22.1% | 22.1% |
| 出生区胜率 | 20.0/24.5/28.5/27.0 | 20.0/24.5/28.5/27.0 |

全部指标与 Safety 27 档逐项相同（确定性复现），满足 ±3 个百分点 / ±0.3 回合。

## S-16 地标可读性（裁决记录 22，段 D 前单独完成）

### 改动文件
- `src/godot/scripts/BoardView.cs`：`DrawSites` 无棋子态按档位放大 + 向相机侧挪 0.05；新增 `AddSiteBand`（据点格底色框）、`SiteFullScale`、`SiteFullNudge`、`SiteBandCompactAlpha`；无棋子态控制方旗杆脚 (0.32, −0.24) → (0.43, −0.24)；无棋子态争议旗改插 (0, −0.30) 并缩到 0.9（新增 `SiteContestedFullScale`）。退让态（0.375 / 偏移 0.35 / 旗 0.62）不变。
- `src/godot/scripts/LowPoly.cs`：篝火石圈半径 0.24→0.23、火焰加大（外焰底半径 0.13→0.17、高 0.36→0.50、自发光 1.1→2.0；内焰 0.07→0.10、0.22→0.34、1.4→2.6）；石碑基座不再用 `Visuals.Rock`，改 `Visuals.SteleBase`。
- `src/godot/scripts/Visuals.cs`：`FlameOuter`/`FlameInner`/`SteleStone`/`SteleCarving` 改值；新增 `SteleBase`、`SiteBandTent`/`SiteBandCampfire`/`SiteBandStele`。
- `tests/Siege.Core.Tests/VisualStyleBaseline/据点地标可读性Tests.cs`（新增 3 条，源码文本扫描）：底色三档明度差、石碑石色与岩石明度差、Godot 脚本无碰撞体。
- `art/sites-v4/`：四张截图重拍覆盖；README 画法约定 / 核对项 / 已知限制 / 存档表同步。
- Core 规则、参数、守门 token 表均未改。Godot 新代码只读 `SiteView.Tier` / `Kind` / `Controller`。

### 放大倍率与包围盒（格心为原点，地砖半宽 0.45，格界 0.5；+Z 朝相机）
核算脚本在会话 scratchpad（`geom.py`）：按 `BoardView.Build` 的相机（俯角 60°、距离 14.6×16.4/12.7、FOV 54、1600×900）把地标各部件与邻格棋子（底座半径 0.36 高 0.07 + 身体半径 0.25 顶高 0.70）投影到屏幕，求凸包分离距离；邻格取同行与远侧 5 格（近侧邻格在地标前面，不会被地标挡），高度取 v4 地图。

| 档位 | 无棋子倍率 (X, Y, Z) | 平面包围盒 x / z（含 +0.05 挪动） | 顶高 |
|---|---|---|---|
| 营帐 | (1.5, 1.2, 1.2) | ±0.405 / −0.274..+0.374 | 0.378 |
| 篝火 | 1.5 | ±0.443 / −0.346..+0.446 | 0.825（火焰） |
| 石碑 | (1.5, 1.1, 1.5) | ±0.300 / −0.130..+0.230 | 0.880 |

全部 < 地砖半宽 0.45。控制方旗杆脚 x 0.43 + 杆半径 0.018 = 0.448 < 0.45，离营帐 0.405、篝火近旁石块（中心 (0.345, 0.05) 半径 0.098，距杆脚 0.30）都有余量，旗面向 −X 展开到 x 0.07。

争议旗（两臂外倾 ±24°、杆 0.85、旗面挂在 0.75、旗长 0.20）单侧最远伸出 = 0.75·sin24° + 0.20·cos24° = 0.488 × 缩放。段 B 插在 (0.32, −0.24)、缩放 1 时旗尖到 x 0.81，已越过格界 0.5 伸到 +X 邻格上方；本次先按控制方旗挪到 0.43 会到 0.92（advisor 复核发现），改为无棋子态插在格心正后方 (0, −0.30)、缩 0.9：x −0.439..+0.439。杆脚 z −0.30 在营帐后沿 −0.274 + 杆半径之外，距篝火后侧石块中心 (±0.1725, −0.249) 0.18 > 0.116。退让态争议旗 (−0.10, −0.38)×0.62 伸出 −0.402..+0.202，本来就在格内，未动。

为什么高度 / 进深没到 1.5：远边仰角只有约 46°，同比 1.5 时投影与远侧邻格棋子重叠——石碑 J8→J9 −8.5 px、E6→E7 −3.6 px，营帐 C12→C13 −6.5 px、M11→M12 −5.4 px（负数 = 重叠）。逐档收窄并挪 +0.05 后最小分离：C12 营帐 +0.4 px、J8 石碑 +1.1 px、M11 营帐 +1.4 px，其余 > 3 px，无重叠。段 B 原尺寸的最小分离是 J8 +2.2 px、C12 +2.4 px。放大前后正面投影面积：营帐 1.5×1.2 = 1.8 倍，篝火 2.25 倍，石碑 1.5×1.1 = 1.65 倍。

### 退让态核算
放大只乘在无棋子态（`SiteFullScale`），退让态仍是原始几何 × 0.375（相对放大态约 0.25），偏移 (−0.35, −0.35)。最近点到格心：营帐 0.363（帆布角 (0.27, 0.23)×0.375）、篝火 0.388（石圈半径改 0.23 后，段 B 为 0.384）、石碑 0.411，均 ≥ 棋子底座 0.36。火焰加大后退让态火焰顶高 0.206、底半径 0.064，在格角内。

### 颜色与灰度明度（0.299R + 0.587G + 0.114B）
| 项 | 值 | 明度 | 对照 |
|---|---|---|---|
| 营帐底色框 | 74,66,58 | 67 | 出生区地砖反照率 132–178；截图实测 141–188 |
| 篝火底色框 | 140,92,104 | 108 | 草地反照率 149；截图实测 158 |
| 石碑底色框 | 214,210,196 | 210 | 草地 149 / 实测 158 |
| 石碑石面 | 226,222,208（原 168,176,190） | 222（原 175） | 岩石 122,120,116 明度 120 |
| 石碑基座 | 190,184,168（原用岩石色） | 184 | |
| 石碑刻痕 | 120,112,100（原 236,232,214） | 113 | 与石面差 109 |
| 外焰 | 255,140,40（原 236,118,40），自发光 2.0 | 163（原 144） | |
| 内焰 | 255,236,140（原 255,214,96），自发光 2.6 | 231 | |

三档框两两差：41 / 102 / 143。截图实测（按上面的相机模型投影取像素，不读图）：开局与中局灰度图里框像素恰为 67 / 108 / 210（无光照材质），渲染后地砖灰度草地 158、林地 121、土路 171、出生区插旗前 188、锁定后 141–165。篝火框 108 与林地 112 / 桥 123 明度接近：靠色相（梅灰红，地表无此色相）与框形区分，v4 据点不在林地 / 桥上，已写入 README 已知限制。

底色框：内缘 0.38（底座 0.36 之外）、外缘 0.44，贴地 +0.004（低于势力层着色 +0.008、轮廓环 +0.03），`Visuals.Flat` 无光照不透明，纯 `MeshInstance3D` 无碰撞体。信息层打开时的取舍：不透明度降到 0.3、不完全隐藏——隐藏会让气层 / 信物层下看不出哪些格是据点（地标已缩到格角）；0.3 压在着色平面之下，势力层着色（0.6）与轮廓环完整盖住它，全场降饱和时它也跟着变暗，不与着色抢明度。有棋子但信息层关时框保持不透明（框在底座外一圈，提示棋子站在据点上）。

### 验证（`set -o pipefail`，真实 EXIT）
- `dotnet build`：0 错误，EXIT=0。
- `dotnet test -c Release`：808 通过 / 0 失败（805 + 新增 3），EXIT=0。
（以下为争议旗修正后的最终一轮）
- `--build-solutions --quit`：EXIT=0，日志无 error / warning。
- `--quit-after 3000 -- --auto-demo`：EXIT=0，终局第 4 大回合达到上限，蓝方 86 第 1。
- `--auto-demo --pick-check`：EXIT=0，可落子格 105、往返一致 105、失败 0。
- 截图（非 headless，按 README 命令）：四条 EXIT=0，均 1600×900：`art/sites-v4/sites-v4-opening.png`、`sites-v4-midgame.png`、`sites-v4-midgame-gray.png`（L 模式）、`sites-v4-endgame.png`。中局据点状态与段 B 版不同（S-15 Safety 27 后 AI 打法变了）：J2 蓝唯一覆盖；L2 蓝、M11 金、C12 紫占据；四块石碑争议（蓝、紫）；B3、B5、M9、E12 无人。README 第 4–6 项实例已按新状态改写。截图未读入会话，第 1–8 项仍待人工目检。重拍后按相机模型投影取框像素（不读图）：12 个据点框像素恰为 67 / 108 / 210；B5 框近侧一边读到 188（被前方 B4 高台挡住）、J8 框近侧一边读到 75 / 98（J7–J8 栅栏），属正常遮挡，已写入 README 已知限制。

### 变异（带时间戳备份 → 改 → 全量 `dotnet test -c Release`（EXIT 单独记录，不接管道）→ 拷回 → `cmp` 一致；三条还原后全量 808 通过 EXIT=0。最初用 7 条过滤集跑过一轮，结果相同，按 testing.md 与段 B 口径改为全量重跑）
- M-S16-1：`SiteBandCampfire` 改成营帐同色 74,66,58 → EXIT=1，808 中红 1（`据点底色三档灰度可辨且与所在地砖拉开明度`）；还原 cmp 一致。
- M-S16-2：`SteleStone` 还原段 B 的 168,176,190 → EXIT=1，红 1（`石碑石色与岩石明度拉开`）；还原 cmp 一致。
- M-S16-3：`AddSiteBand` 里加 `_sites.AddChild(new StaticBody3D());` → EXIT=1，红 1（`地标与底色不带碰撞体`）；还原 cmp 一致。
- 放大倍率被还原（`SiteFullScale` 改回 1）**没有自动化手段抓**：Godot 不进 siege.sln，`--pick-check` 与 `--auto-demo` 对地标尺寸不敏感；只能看图与看 README 画法约定。

### 偏离
1. "整体放大约 1.5 倍"：宽度三档都 1.5，篝火三轴 1.5；营帐高 / 进深 1.2、石碑高 1.1——同比 1.5 会在固定相机下压到远侧邻格棋子（数字见上），按"不得遮挡相邻格棋子"取舍。另加了无棋子态整体 +0.05 向相机挪动。
2. 篝火石圈半径 0.24→0.23（1.5 倍后 0.4575 越出地砖半宽）。这使退让态篝火最近点 0.384→0.388，仍满足。
3. 测试数 805→808：为了让变异有自动化可抓，新增 3 条源码扫描测试（任务未要求新增测试）。
4. 任务要求底色"与林地 / 桥不混淆"：篝火框灰度 108 与林地 112（渲染实测 121）/ 桥 123 明度接近，只靠色相（梅灰红）与框形区分。三档明度要两两差 ≥ 40 且各自离所在地砖（出生区 132–188、草地 149–158）≥ 30，中间档只能落在约 107–119，与林地 / 桥的明度带无法同时避开；v4 据点无一落在林地或桥上。
5. 争议旗无棋子态位置与缩放改了（段 B 就越出格界，本次一并修）：不在任务清单里，但属于"不得越出格子边界"。

## 段 D：文档、规范、出生区编号与变异汇总（tasks 6.1–6.4）

### 改动文件

- `2026-09-10-siege-core-gameplay-design-v1.md`：v1.2 → v1.3（章节清单见下）。
- `.trellis/spec/core/boundaries.md`：单一实现表加 3 行——据点控制（`SiteControl.Compute`，只读 `CoverageMap.OwnershipOf`；插旗阶段状态由 `MatchFlow.Publish` 给出 `MatchPublicView.SiteStates`，表现层不得自推）、高地压制加值（`PieceEffects.HighGroundBonus`，只走 `CoverageTargets`）、出生区对人显示编号（`BirthZoneLabel`）；表后补一句 `SiteAttribution.HomeZones` 只供遥测首部（A2 检查 C-5 遗留）。
- `.trellis/spec/core/testing.md`：加 4 节——扫档核对 `config.json` 实际生效值（`--difficulty` / `--players` 丢弃配置文件权重）；计分口径一变默认权重必须重扫、临界区加密档（Safety 5→27、22/25/27）；写出漏字段要用往返测试（M-C1）；0 / null 期望的遥测断言抓不到写入端遗漏（M-T10～12）。
- `src/Siege.Core/Board/BirthZoneLabel.cs`（新）：`Number(z) = z + 1`、`Of(z) = "出生区 {z+1}"`，对人显示的唯一换算点。
- `src/Siege.Core/Board/MapValidator.cs`：8 处报文改走 `BirthZoneLabel`（越界 / 容量不足 / 区间 / 重叠（两个编号）/ 信物分区不符 / 孤立 / 地标不可达 / 距离失衡明细）。
- `src/Siege.Core/Board/MapSymmetry.cs`：`Describe` 改走 `BirthZoneLabel.Of`。
- `src/Siege.Sim/Analysis/ReportWriter.cs`：第 5 节出生区胜率行改走 `BirthZoneLabel.Of`（`ZoneStat.Zone` 仍 0 起）。
- 测试：`地图静态校验规则Tests.cs`、`平衡分析方向Tests.cs`（断言改写，见 6.4 表）；`基准地图对称性Tests.cs`（在既有方法内补 2 条断言，测试数不变）。
- `HANDOFF.md`：顶部注、分支状态（808）、权威来源 v1.3、去围棋化表第②行、新增「第二轮现状」块（v4 参数、计分、段 C 关键数据、Safety 27、下一步）、v3 回归标作废、下一步表第二轮行。
- `openspec/changes/scoring-sites/tasks.md`：1.1–6.4 全部 24 项勾选。

### 6.1 设计文档改动章节

| 章节 | 改动 |
|---|---|
| 头部 | 版本 v1.3，最近一次为 scoring-sites |
| §1 | 核心循环"围杀与抢地"→"围杀与抢夺据点" |
| §2 | "覆盖"改为控制影响；新增"据点""据点分""高地加值"；"势力值"= 据点分 + 军势，空格归属不计分 |
| §3.1 | 首条"地图由…组成"与"由设计师固定"补据点 |
| §3.2 | 预算表加据点数列（6–8 / 9–11 / 10–14）与越界算例；距离均衡目标加最近篝火 / 石碑；静态校验加据点三条（带 `G7` 与深水算例）；"领地计分将在后续 change 退役"改为已退役 |
| §3.3 | 基准图 v4（地形同 v3，S-9 升号，v3 文件为历史）；据点 12 条目与三档坐标及理由（S-8 / S-13）；C4 比对含据点；距离 5 / 7 / 4 / 6 / 8；出生区编号 1 左下 → 4 左上，对人显示 1–4（S-14）；文本图换为 `map` 真实输出（只省"已导出"一行）；末段"计分不变、对局变长"改为旧口径数据 + 指向 §16 |
| §6.3 | 第 5 步加据点控制（与高地、总势力口径）；围杀收益去掉"领地"，改为信物与据点控制 |
| §7 | 标题改"覆盖、空格归属、信物与据点控制"；§7.2 去计分语义（只作控制判定与展示）并补"覆盖数量不影响" |
| §7.4（新） | 档位表、分值配置约束、控制四态、林地、实时重算、弃赛、无额外效果；算例表 14 行 |
| §9.2 | 倍增子行：不放大连珠 / 协同 / 高地位置加值与据点分 |
| §10.1 | 总势力公式、位置加值三项、据点分 / 空格不计分 / 明细结构；高地压制加值定义 + 7 行算例；标准算例表 11 行（孤立棋子 1 点、20、25、14、12、77、27、45、复算、65、14） |
| §12.3 | 并列链 信物数 → 据点数 → 棋子数，独占空格数不参与，60 / 2 / 3 vs 1 算例 |
| §13.1 | 公开据点位置 / 档位 / 分值、据点控制状态、势力明细拆分 |
| §14.2 | 势力层改看据点档位 / 分值 / 状态与高地拆分，不标空格分，不加新层（S-5） |
| §16 | Safety 口径 5 → 27（旧 5 作废）；v4 上 Safety 扫档数据与机理、分量不是杠杆；新增两条目标（据点分占比 25%–45%、整局无提子 ≤ 5%，S-10）；确认 200 局实测表，据点分占比 22.1% 标"未达标，负责人知情接受（S-15）" |
| §17 | 记录项加据点分值、据点控制变化、高地拆分、玩家据点分；批次 `config.json` 口径；重点分析改为 1–10 编号，补 8（碾压）、9（冲突占用率）与新增 10（据点） |
| §20 | "领地归属 / 领地"改"空格归属"；新增据点地标条（三档、底色框、石碑亮石色、篝火、旗帜主色 + 徽记、争议 / 无人、放大与退让、无碰撞体，S-16） |
| 变更记录 | 加 2026-09-17 scoring-sites（v1.2 → v1.3）一行 |

### grep 旧说法核对（变更记录从第 687 行起；正文 = 之前）

| 模式 | 正文 | 变更记录 |
|---|---:|---:|
| `领地分` | 0 | 0 |
| `领地` | 0 | 4（catch-up-recruit / merge-board-layer / terrain-model 历史行 + 本轮新行描述"空格领地退出计分"） |
| `独占空格提供` / `最多产生 5 点` | 0 / 0 | 0 / 0 |
| `5 点势力` | 0 | 1（本轮新行"孤立普通子最多 5 点势力作废"） |
| `独占空格数` | 1（§12.3"独占空格数不参与比较"，新规则） | 1 |
| `v3` / `siege-4p-base-v3` | 1 / 1（§3.3 首段：v4 地形同 v3、v3 文件为历史） | 2 / 1 |

### 规格场景 → 设计文档对照清单（人工逐条核对，数值一致）

| 规格 | 场景 | 设计文档 |
|---|---|---|
| site-control | 分值取自对局配置（10/30/90 → 30）、分值配置非法被拒（5/0/45、20/15/45）、据点不带额外效果 | §7.4 正文 + 算例表 |
| site-control | 占据即控制、唯一覆盖即控制、多人覆盖即争议、居高临下制造争议、仰视无法争夺 | §7.4 算例表 |
| site-control | 失去控制立即掉分（−45）、占据者被围杀（−15 / +15）、弃赛者封锁据点（D 含 5、不占名次） | §7.4 算例表 |
| site-control | 开局即可见（12 个、全无人） | §7.4 算例表、§13.1 |
| power-score | 设计文档标准算例（20）、含位置加值的计算（14 非 16）、位置加值不被倍率放大（25 非 40）、高地加值不被倍率放大（12 非 15） | §10.1 标准算例表 |
| power-score | 逐棋串取整、恰好第 3 枚、第 4 枚只加基础、封顶不影响其他效果、倍率显示表达封顶 | §10.1 正文（逐棋串取整、封顶 3、超出仍计基础）与 §9.2；这 5 条的具体数值（16.5、23、27、29、3.375 显示）设计文档未列，沿用 multiplier-rebalance 口径，本轮未新增 |
| power-score | 据点与棋串相加（77）、独占空格不计分（27）、孤立棋子的势力（1） | §10.1 标准算例表 |
| power-score | 势力不可消耗、势力不累计 | §10.1 列表 |
| power-score | 明细可复算总势力、据点分可溯源（65）、位置加值可溯源（14）、明细区分原始与生效倍率、明细可复算棋串军势 | §10.1 明细条 + 算例表 |
| piece-effects | 跨崖居高临下、缓坡压制、同高不加、林地里的敌子压制不到、隔河压制、多个低处敌子只加 1、己方棋子不算 | §10.1 高地算例表（坐标 `F7/F6/G6/H6` 与高度逐条一致） |
| piece-effects | 两枚倍增子、倍率指数封顶为 3 | §9.2、§10.1 正文 |
| piece-effects | 倍率不作用于据点分（45）、倍率不作用于位置加值（12） | §10.1 算例表（据点分行、25 那行） |
| coverage-territory | 独占（不计分）、争议、覆盖数量不影响独占、中立 | §7.2 |
| coverage-territory | 唯一覆盖者、多人覆盖、林地信物只能占据 | §7.3、§7.4 控制四态与林地条 |
| coverage-territory | 弃赛者遗留棋子制造争议 | §7.4 算例表 |
| elimination-endgame | 弃赛后停止行动、保护期内允许弃赛、遗留棋子可被围杀、弃赛快照可记录 | §12.2（未改） |
| elimination-endgame | 遗留棋子继续生效（据点争议） | §7.4 弃赛条与算例表 |
| elimination-endgame | 势力相同比信物数、逐级比较到棋子数、信物相同比据点数（60 / 2 / 3 vs 1）、完全相同则并列 | §12.3 |
| elimination-endgame | 弃赛者排在完赛者之后、多名弃赛者互比、出局者倒序、达上限时按同一规则、碾压获胜者为第 1 名 | §12.3（未改） |
| map-definition | 加载地图不引入随机、拒绝裁切适配 | §3.1 首条、§3.2 首句、§7.4 首段 |
| map-definition | 据点数越界（16 → 10–14） | §3.2 表与越界句 |
| map-definition | 出生区容量、信物格分布、可落子格规模、旋转对称（含据点）、地形要素齐全 | §3.3 |
| map-definition | 据点布点、篝火可被邻家居高覆盖 | §3.3 据点条 |
| map-definition | 保护期内篝火归邻家 | §3.3 篝火条、§7.4 算例表 |
| map-definition | 据点与信物重合（`G7`）、据点在不可落子格（深水）、到据点的距离失衡 | §3.2 校验条 |
| match-telemetry | 据点控制变化可查 | §17 记录项 |
| match-telemetry | 据点分析分档输出、据点分占比口径（600 / 180 → 30%） | §17 第 10 项、§16 目标条 |
| match-telemetry | 碾压胜统计、冲突时的盘面占用率 | §17 第 8、9 项（本轮补齐） |
| simulation-harness | 扫档配置可追溯 | §17 批次配置记录句、§16 实测口径 |
| tactical-layers | 势力层显示据点控制（45、争议、A / B 覆盖方）、势力层显示高地加值 | §14.2 势力层 |
| information-visibility | 势力明细公开、据点控制公开 | §13.1 |
| visual-style-baseline | 三档可辨、控制方可辨且不只靠颜色、争议与无人可区分、占据时棋子可见 | §20 据点地标条 |

### 6.4 出生区编号统一（S-14）

显示层统一走 `BirthZoneLabel`；内部索引、`MapData.BirthZones[z]`、日志 `HomeZone` / `Header.Zones`、`ZoneStat.Zone` 均保持 0 起。`map` 子命令文本图（`'1' + z`）、`BoardRenderer`（`z + 1`）与 `play` 插旗（`c.Item2 + 1`）原本就是 1 起，段 D 检查时改走 `BirthZoneLabel.Number`（输出字符不变，`map` 输出与改前 `cmp` 一致）；`play` 读入玩家输入的 `z - 1` 是输入解析，未改。人工核对：`map` 输出（本段抓取，与段 A1 文本图逐行相同）出生区标记 21–24 = 1 左下 → 4 左上；校验报文由下表断言钉住（如出生区距离失衡报"出生区 1 = 4"）。

改写的既有断言（10 条）：

| 文件:行 | 旧 | 新 |
|---|---|---|
| `地图静态校验规则Tests.cs:58`（距离沿气边计算） | `出生区 0 = 4` | `出生区 1 = 4` |
| 同上 `:59` | `出生区 1 = 12` | `出生区 2 = 12` |
| `:74`（出生区被孤立） | `出生区 0` | `出生区 1` |
| `:85`（同上，护城河变体） | `出生区 0` | `出生区 1` |
| `:123`（出生区可落子格超出区间） | `出生区 0 有 11 个可落子格` | `出生区 1 有 11 个可落子格` |
| `:184`（出生区容不下九枚部署） | `出生区 0 只有 8 个可落子格` | `出生区 1 只有 8 个可落子格` |
| `:274`（出生区距离失衡） | `出生区 0 = 4` | `出生区 1 = 4` |
| `:490`（到据点的距离失衡，2 组数据） | `出生区 0 = 6` | `出生区 1 = 6` |
| `:491`（同上） | `出生区 1 = 9` | `出生区 2 = 9` |
| `平衡分析方向Tests.cs:70`（出生区公平性） | `出生区 0：胜率 100.0% (40/40` | `出生区 1：胜率 100.0% (40/40` |
| `匿名同时插旗Tests.时限可配置`（段 D 检查补，非改写） | 无 | `FlagsLocked` Detail 以 `出生区 P0:3 P1:1 P2:4 P3:2；` 开头 |

补充断言（非改写）：`基准地图对称性Tests.出生区编号不轮换的图被判不对称` 加"报文含 `出生区 4`、不含 `出生区 0`"——`MapSymmetry` 报文原先没有任何测试钉编号（M-D2 在补之前会全绿）。测试注释里描述夹具的"出生区 0 = {A5}"等是内部索引说明，未改。

~~未改（待决）：`FlagsLocked` Detail 仍 0 起~~ → 段 D 检查已改：Detail 是人读文本，唯一解析日志 Detail 的 `BalanceAnalyzer` 只解析 `Recruit` 事件的 `candidates` / `picks`，无测试依赖旧文本，故改走 `BirthZoneLabel.Number` 并补断言（M-D3）。

### 6.3 变异验证汇总（段 A1–D 全部）

"还原"列：A1 / A1 检查 / C / S-16 / D 为 `cp` 备份 → 还原 → `cmp` 一致；A2 / A2 检查 / B / B 检查为脚本 `finally` 写回并与原字节、备份三方比对（记为 True）；主会话三条为主会话变异后还原（主会话记录，只有红数，未留测试名）。

| 段 | 编号 | 变异 | 红掉的测试 / 命令 | 还原 |
|---|---|---|---|---|
| A1 | M-A1 | `MapSymmetry` 比对漏掉据点 | 红 2：`基准地图对称性Tests.每项属性都参与旋转比对`、`只改一个据点档位的图被判不对称` | cmp 一致 |
| A1 | M-A2 | `MapSymmetry` 只比位置不比档位 | 红 1：`只改一个据点档位的图被判不对称` | cmp 一致 |
| A1 | M-A3 | 规则 8 不查与信物重合 | 红 1：`地图静态校验规则Tests.据点与信物重合` | cmp 一致 |
| A1 | M-A4 | 4 人据点上界 14 → 15 | 红 3：`人数适配预算Tests.据点数越界`（15 / 16 / 9） | cmp 一致 |
| A1 | M-A5 | 距离均衡漏掉"最近石碑" | 红 1：`地图静态校验规则Tests.到据点的距离失衡(Stele)` | cmp 一致 |
| A1 | M-A6 | `MapFile` 读入丢弃 Sites | 红 8：`地图文件往返Tests` 4 条、`地图文件健壮性Tests` 4 条 | cmp 一致 |
| A1 | M-A7 | 规则 8 不查档位必填 | 红 1：`地图静态校验规则Tests.据点必须标注档位` | cmp 一致 |
| A1 | M-A8 | 篝火种子 `J2` → `H2` | 红 6：`四人基准地图Tests.据点布点` / `篝火可被邻家居高覆盖`、`磁盘上的基准地图文件与代码一致`、`据点缺档位的文件被指名报出`、`只改一个据点档位的图被判不对称`、`四个出生区到最近篝火与石碑的距离精确相等` | cmp 一致 |
| A1 | 主-A1（主会话） | 去掉"据点须在可落子格"检查 | 红 2 | 已还原（主会话） |
| A1 检查 | M-C1 | `MapFile.ToJson` 写出前清空 Sites | 红 4：`地图文件往返Tests` 3 条、`地图文件健壮性Tests.全小写键名的地图能正确加载`（磁盘一致性测试未红，见 testing.md 新节） | cmp 一致 |
| A1 检查 | M-C2 | `DistanceTable` 取最近改最远 | 补测试前全绿 753（缺口）；补后红 1：`四个出生区到最近篝火与石碑的距离精确相等` | cmp 一致 |
| A2 | M-S3 | `SiteControl.Compute` 注入 `CoverageTargets(` | 红 1：`据点控制判定Tests.据点控制实现只读覆盖表` | True |
| A2 | M-S3b | 同处注入 `HeightAt(` | 红 1：同上 | True |
| A2 | M-S1 | 占据映射成争议 | 红 9：`占据即控制`、`居高临下制造争议`、`林地据点只能占据`、`占据者被围杀` 等 | True |
| A2 | M-S2 | 争议映射成无人 | 红 7：`多人覆盖即争议`、`失去控制立即掉分`、`弃赛者遗留棋子制造争议` 等 | True |
| A2 | M-S8 | 占据的控制方式记成唯一覆盖 | 红 6：`据点分可溯源`、`占据即控制`、`占据者被围杀` 等 | True |
| A2 | M-H1 | 高地加值 `<` → `<=` | 红 17：`高地压制加值Tests.同高不加` 及平地同高相邻既有算例 | True |
| A2 | M-H2 | 删"每枚至多 1"的 `break` | 红 1：`多个低处敌子只加1` | True |
| A2 | M-H3 | `CoverageTargets` 改 `Neighbors` | 红 3：`林地里的敌子压制不到`、`隔河压制`、`几何邻居枚举只在允许名单内直接调用` | True |
| A2 | M-S12 | 高地加值并入基础军势 | 红 1：`棋串军势公式Tests.高地加值不被倍率放大` | True |
| A2 | M-S5 | 总势力加回独占空格数 | 红 47：`独占空格不计分`、`孤立棋子的势力` 等 | True |
| A2 | M-S20 | 总势力漏据点分 | 红 8：`据点与棋串相加`、`倍率不作用于据点分`、`明细可复算总势力` 等 | True |
| A2 | M-S11 | 只给参赛玩家计据点分 | 红 1：`据点分计入势力Tests.弃赛者封锁据点` | True |
| A2 | M-S9 | `Validated` 去掉"营帐 ≤ 篝火" | 红 1：`分值配置非法被拒(20,15,45,"营帐")` | True |
| A2 | M-S4 | `MatchFlow.OnRecalculatePower` 传 `Standard` | 红 1：`据点档位与分值Tests.分值取自对局配置` | True |
| A2 | M-S4b | `BatchPreviewBuilder` after 用 `Standard` | 红 1：同上 | True |
| A2 | M-S14 | `Finish` 改回 `ExclusiveCells.Length` | 红 2：`终局输入取控制中的据点数量`、存档往返 | True |
| A2 | M-S13 | 并列链删据点级 | 红 1：`信物相同比据点数` | True |
| A2 | M-S15 | 存档不写 `SiteValues` | 红 1：`据点分值与终局据点数随存档往返且旧存档回填` | True |
| A2 | M-S16 | 读档 `ControlledSites` 恒 0 | 红 2：同上、`百局端到端Tests.连续一百局四人对局无死锁无非法状态` | True |
| A2 | M-T1 | 据点变化记成 `ControlChanged` | 红 1：`据点遥测Tests.据点控制变化可查` | True |
| A2 | M-T2 | 日志棋串不写 `HighGroundBonus` | 红 1：同上 | True |
| A2 | M-T3 | 去掉跑局配置与对局分值一致性检查 | 红 1：`跑局配置与对局据点分值不一致即拒绝` | True |
| A2 | M-T5 | `MatchSession.Create` 不传分值 | 红 1：`扫档配置可追溯` | True |
| A2 | M-T4 | `config.json` 改回 `config.ToJson()` | 红 1：`批量跑局Tests.批量执行并汇总` | True |
| A2 | M-T6 | `SiteAttribution` 不排除桥格 | 红 1：`据点主人按地图推导` | True |
| A2 | M-T7 | 分析纳入缺据点字段旧日志 | 红 1：`据点分析分档输出` | True |
| A2 | M-T8 | 据点分占比计入弃赛者 | 红 1：同上 | True |
| A2 | M-T9 | 主人口径把推不出主人的计入分母 | 红 1：同上 | True |
| A2 | M-T10 | 日志玩家据点分恒 0 | 红 1：`据点控制变化可查`（补营帐 E4 后） | True |
| A2 | M-T11 | 快照据点控制者恒 null | 红 1：同上 | True |
| A2 | M-T12 | 终局名次据点数恒 0 | 红 1：同上 | True |
| A2 | 主-A2（主会话） | 唯一覆盖控制者置 null | 红 12 | 已还原（主会话） |
| A2 检查 | N-1 | 据点变化判定 `\|\|` → `&&` | 补测试前全绿 797（缺口 C-1）；补后红 1：`控制者不变而控制方式变化也记事件` | True |
| A2 检查 | N-2 | `BatchEvaluator` 分值改 `Standard` | 补测试前全绿 797（缺口 C-2）；补后红 1：`AI评价使用对局据点分值` | True |
| A2 检查 | N-3 | 高地加值去掉 `enemy.Owner != group.Owner` | 红 2：`己方棋子不算`、`连珠子的位置加值Tests.崖壁截断连珠线` | True |
| B | M-B1 | `SiteViews.From` 快照 null 时 `.Take(0)` | 红 1：`据点公开Tests.开局即可见`。**变异对象已在 B 检查中删除，由 M-BC1 替代** | True |
| B | M-B2 | 控制者恒 null | 红 1：`据点控制公开` | True |
| B | M-B3 | 分值改 `Standard.Of` | 红 1：`据点分值取自对局配置` | True |
| B | M-B4 | 争议覆盖方恒空 | 红 2：`据点控制公开`、`势力层显示据点控制` | True |
| B | M-B5 | `PowerLayerContent` 加回 `ExclusiveCells` | 红 1：`势力层视图模型不含领地贡献字段` | True |
| B | M-B6 | `FormulaText` 去掉"/ 高地 N" | 红 2：`棋串军势公式Tests`（第 53 行）、`势力层显示高地加值` | True |
| B | M-B7 | `BoardView.DrawSites` 调 `SiteControl.Compute` | 红 1：`Godot层不含规则计算Tests.Godot层不调用规则计算入口` | True |
| B | M-B8 | 同处调 `PieceEffects.HighGroundBonus` | 红 1：同上 | True |
| B | M-B9 | `SiteViews.Build` 自调 `SiteControl.Compute` | 红 1：`UI层不含规则计算Tests.表现层不调用规则计算入口` | True |
| B 检查 | M-BC1 | `Publish` 中 `?? SiteControl.Compute(...)` → `?? []` | 红 1：`据点公开Tests.开局即可见` | True |
| B 检查 | M-BC2 | `Labels.SiteTier` 营帐 / 篝火文案互换（对照：同时把断言换回自比 → 绿 805，证明补断言前是盲区） | 红 1：`开局即可见` | True |
| B 检查 | 主-B（主会话） | 争议状态文案改成"无人" | 红 2 | 已还原（主会话） |
| C | M-C-1 | `EvaluationWeights.Default.Safety` 默认值改 25 | 红 3（段 C 记录未列测试名） | cmp 一致 |
| S-16 | M-S16-1 | `SiteBandCampfire` 改营帐同色 | 红 1：`据点底色三档灰度可辨且与所在地砖拉开明度`（EXIT=1） | cmp 一致 |
| S-16 | M-S16-2 | `SteleStone` 还原段 B 色 | 红 1：`石碑石色与岩石明度拉开`（EXIT=1） | cmp 一致 |
| S-16 | M-S16-3 | `AddSiteBand` 加 `StaticBody3D` | 红 1：`地标与底色不带碰撞体`（EXIT=1） | cmp 一致 |
| D | M-D1 | `BirthZoneLabel.Number` 改回 `zoneIndex`（0 起） | 红 8（EXIT=1，808 中 800 过）：`地图静态校验规则Tests` 的 `距离沿气边计算`、`出生区被孤立`、`出生区可落子格超出区间`、`出生区容不下九枚部署`、`出生区距离失衡`、`到据点的距离失衡`（Campfire、Stele），`平衡分析方向Tests.出生区公平性` | cmp 一致 |
| D | M-D2 | `MapSymmetry.Describe` 改回 `$"出生区 {z}"`（不经 `BirthZoneLabel`） | 红 1（EXIT=1）：`基准地图对称性Tests.出生区编号不轮换的图被判不对称`（补断言后；补之前该处无任何编号断言） | cmp 一致 |
| D 检查 | M-D3 | `MatchFlow` 锁定事件 `BirthZoneLabel.Number(kv.Value)` 改回 `kv.Value` | 红 1（EXIT=1，808 中 807 过）：`匿名同时插旗Tests.时限可配置`（补断言后） | cmp 一致 |

合计：A1 9（8 + 主会话 1）、A1 检查 2、A2 32（31 + 主会话 1）、A2 检查 3、B 9、B 检查 3（2 + 主会话 1）、C 1、S-16 3、D 2、D 检查 1，共 65。无自动化手段可抓、只靠人工的已知项：S-16 放大倍率改回 1（Godot 不进 sln）。

### 验证（`set -o pipefail`，EXIT 单独取）

- `dotnet build`：0 警告 0 错误，EXIT=0。
- `dotnet test -c Release`（`DOTNET_CLI_UI_LANGUAGE=en`，输出落文件后取 `$?`）：**808 通过 0 失败，EXIT=0**（测试数不变）。M-D1、M-D2 均已还原（cmp 一致）后跑的最终全量。
- `openspec validate scoring-sites`：`Change 'scoring-sites' is valid`，EXIT=0。
- `dotnet run --project src/Siege.Sim -c Release -- map`：EXIT=0；输出与段 A1 文本图逐行相同，已原样写入设计文档 §3.3。该命令顺带重写 `maps/siege-4p-base-v4.json`，`git status` 无变化。

### 偏离 / 待决

1. **设计文档超出 tasks 6.1 列出的章节**：§1（抢地）、§3.1（首条补据点）、§3.2（据点数列、据点校验、距离目标、"将在后续退役"）、§9.2（倍增子行补高地与据点分），以及 §17 补第 8、9 项——均为清掉旧说法或让规格场景（据点数越界、到据点的距离失衡、碾压与占用率）在设计文档有落点所必需。§17 第 8、9 项是 dominance-victory / denser-map 已实现、§16 已引用"§17 第 9 条"但本节漏写，已在变更记录注明。
2. **power-score 5 条倍率封顶场景**（逐棋串取整 16.5、第 3 / 4 枚 23 / 27、封顶不影响其他效果 29、倍率显示 3.375）的数值算例未写进设计文档，只有规则正文；本轮未改这些规则，未新增。
3. ~~`FlagsLocked` 事件 Detail 的出生区编号仍 0 起~~（段 D 检查已改，见 6.4 与 M-D3）。
4. **§16 实测表只列确认跑的 200 局一组**；扫档全表仍以本文件「段 C」为准，设计文档只摘领先者胜率与不收敛率两列。
5. **§14.1 "基础军势 + 位置加值 × 倍率"预览文案**是 multiplier-rebalance 之前的写法，与本轮无关，未改。
6. 主会话三条变异（主-A1 / 主-A2 / 主-B）只有红数，没有留存测试名。

## 段 D 检查（trellis-check）

### 结论（逐项）

1. **算例 / 数据一致**：§7.4、§10.1、§12.3 与 `site-control` / `power-score` / `piece-effects` / `elimination-endgame` / `coverage-territory` 场景逐条对过（77、27、1、`⌊4×2.25⌋+3=12`、高地 7 场景坐标与高度、60 / 2 / 3 vs 1、弃赛 D 含 5、控制四态），一致。§16 与 `sim-out/sites-final/report.txt` 逐项一致（25.5%、6.5%、AllPassed 182 / 上限 13 / 碾压 5、整局无冲突 0、7.78、首次冲突 4、22.1%、0.29、Pass 22.1%、石碑争议 52.4%、篝火主人 10.6%、高地 0.6%），旧计分对照 0.40 / 10.57 / 14%（28/200）/ 17% 与 `sim-out/terrain-v3/report.txt` 一致；22.1% 标未达标。§3.3 文本图与现跑 `map` 输出 39 行逐行相同（python 比对 0 差异）。
2. **旧说法**：正文（第 687 行变更记录之前）`领地` / `领地分` / `5 点` / `抢地` / `出生区 0` 均 0；`v3` 只在 §3.3 首段作历史；`Safety` 5 只以"已作废"出现。
3. **boundaries / testing**：引用的 `SiteControl.Compute(GameBoard, CoverageMap)`、`CoverageMap.OwnershipOf`、`据点控制判定Tests.据点控制实现只读覆盖表`、`MatchFlow.Publish`、`MatchPublicView.SiteStates`、`SiteView`、`PieceEffects.HighGroundBonus(GameBoard, Group)`、`GameBoard.CoverageTargets`、`GroupPower.HighGroundBonus`、`SiteAttribution.HomeZones`、`Adjacency.LibertyNeighbors` / `AreAdjacent`、`BirthZoneLabel` 均存在；testing.md 所述 `--difficulty` / `--players` 重建玩家列表（`Program.cs:203–207`）与 Safety 5 档 95.5% / 49.5% 属实。
4. **出生区编号**：见下"修复"1–2。日志数据字段（`HomeZone`、`Header.Zones`、`ZoneStat.Zone`）仍 0 起；`sites-final/report.txt` 里的"出生区 0–3"是改前产物，不重跑。Godot 与 Presentation 无带编号的出生区文本。
5. **HANDOFF / tasks**：未推送（`main` ahead 7）、808、v4、Safety 27、关键数据与报告一致；tasks 24 项勾选与各段记录一致。
6. **变异汇总**：原表 64 条与各段记录（A1 9 / A1 检查 2 / A2 32 / A2 检查 3 / B 9 / B 检查 3 / C 1 / S-16 3 / D 2）逐编号吻合（正文另见的 `M-V2` 是前序 change 的变异，不属本轮）；本次加 M-D3，共 65。

### 修复

1. `src/Siege.Core/Match/MatchFlow.cs` `FlagsLocked` Detail 改走 `BirthZoneLabel.Number`；`tests/.../MatchSetup/匿名同时插旗Tests.cs` `时限可配置` 补断言（测试数不变）。
2. `BirthZoneLabel` 不是唯一换算入口：`src/Siege.Sim/Program.cs`（`map` 文本图 `'1' + z`）、`src/Siege.Sim/Play/BoardRenderer.cs`（`z + 1`）、`src/Siege.Sim/Play/PlayCommand.cs`（`c.Item2 + 1`）改走 `BirthZoneLabel.Number`，输出不变（`map` 改前后输出 `cmp` 一致）；`BirthZoneLabel` 注释与 boundaries.md 出生区行补全消费者清单并写明"不得手写 `+ 1`"。
3. `boundaries.md` 高地行原写"不另写邻接或高度比较"，而实现本就用 `Map.HeightAt` 比高度（且据点控制行明文禁 `HeightAt(`，易被误读成同类禁令）→ 改为"覆盖目标只经 `CoverageTargets`，不另写邻接或崖壁判断；严格更低用 `Map.HeightAt` 比较"。
4. 设计文档 §3.2 距离均衡条补失衡报文算例（出生区 1 到最近石碑 6、出生区 2 为 9 → 被拒），规格「到据点的距离失衡」有了数值落点。
5. 设计文档 §16 "首次提子稳定发生在占用率约 62%"是 v1 / v2 数值，与当前 38.6% 矛盾 → 补一句注明非常数（13×13 旧计分 61.0%、据点计分 + Safety 27 为 38.6%）。

### 未修 / 说明

- §16 Safety 扫档只列 10 档（缺 7、8），与 design.md S-15 原文一致，未改。
- `map` 文本图与 `play` 棋盘的 `BirthZoneLabel.Number` 改动无自动化断言（`map` 输出靠人工 `cmp`），与 S-16 放大倍率同属人工项。
- 未动 `src/godot/`，未跑 Godot 构建。

### 验证（`set -o pipefail`，EXIT 单独取）

- `dotnet build`：0 警告 0 错误，EXIT=0。
- 变异 M-D3：红 1（EXIT=1，807 / 808），`cp` 还原后 `cmp` 一致。
- `dotnet test -c Release`（`DOTNET_CLI_UI_LANGUAGE=en`）：**808 通过 0 失败，EXIT=0**。
- `openspec validate scoring-sites`：valid，EXIT=0。
- `dotnet run --project src/Siege.Sim -c Release -- map`：EXIT=0，与改前输出 `cmp` 一致，`maps/` 无变化。
