# 09-16-terrain-model — 执行计划

> **执行进度以本文件为准。** `openspec/changes/terrain-model/tasks.md` 是规格侧快照。
> 实现前先读 `prd.md`（含分段计划）→ `openspec/changes/terrain-model/design.md`（D-A～D-H + 裁决记录 1–31，只做标「本轮」的）→ 七份 specs。
> 每段结束在文末「段记录」追加：改了什么、既有测试改写逐条、变异验证逐条、待决。

## 1. 地形数据模型

- [x] 1.1 `MapData` 新增每格高度（0/1/2）、地表（草地 / 土路 / 林地 / 深水）、预置桥集合、栅栏边集合（无序格对）；`Obstacles` 保留。提供 `HeightAt` / `SurfaceAt` / `HasBridge` / `HasFence(a,b)` / `IsPlayable(c)`（非障碍且非未架桥深水）查询。验证：单元测试——桥标在草地、栅栏标在不相邻格对时构造被拒并指出坐标。
- [x] 1.2 `MapFile` JSON 读写新字段，旧 v2 文件缺字段时按"全 h=0、全草地、无桥无栅"读入（只为人工对照，仍会被新区间校验拒绝）。验证：v3 导出后再读入逐字段相等。
- [x] 1.3 `Coord` / 记法：所有写死 11 列或 `A–L` 的地方改为按宽度取字母。算例：13 列字母为 `A B C D E F G H J K L M N`，`A1` 仍是左下角。验证：单元测试 + 全仓 grep 无剩余写死。

## 2. 两套导出关系

- [x] 2.1 `Adjacency` 新增气边导出：`LibertyNeighbors(map, c)` = 几何邻居中满足「两格可落子 ∧ |Δh| ≤ 1 ∧ 无栅栏」者。算例（`terrain` 规格）：h=0 的 `F6` 上方 h=2 的 `F7` 不计气（气数 3）；h=1 `F6` 与 h=2 `F7` 成串；`F6`–`G6` 有栅栏则不成串且 `G6` 非气；`G6` 深水时气数 3，架桥后 4。验证：单元测试逐条 + 对称性属性测试（任取 a、b 判定一致）。
- [x] 2.2 `Adjacency` 新增覆盖关系导出：`CoverageTargets(map, s)`，按 `terrain`「覆盖关系」三步规则。算例：h=2 `F7` 覆盖 h=0 `F6`，反向不覆盖；`G6` 林地不接收覆盖但林地上的棋子向 `H6` 覆盖；栅栏不挡覆盖；`F6`→深水 `G6`→同高 `H6` 覆盖 `H6` 不记 `G6`；`G6`、`H6` 均深水则 `J6` 不覆盖。验证：单元测试逐条。
- [x] 2.3 `GameBoard`：落子合法性加"未架桥深水不可落子"；棋串构成与气改走 2.1。算例：h=0 棋串三面敌子、第四面 h=2 空格 → 无气。验证：`board-topology` 全部场景测试。
- [x] 2.4 `CoverageMap` 改走 2.2；唯一覆盖查询与空格归属仍共用同一份数据。算例：林地信物格与敌子几何相邻时无人覆盖、未发现，占据后才发现并控制。信物揭示触发补"首次被占据"（`relic-control` 增量）。验证：`coverage-territory` 与 `relic-control` 场景测试。
- [x] 2.5 全仓扫掉绕过导出关系的地方：所有直接判断 `Terrain.Obstacle` 后手写邻居过滤的调用（`BatchRehearsal`、`GroupSafety`、`BatchEvaluator`、`MapValidator` 等）改为走 2.1 / 2.2。验证：新增守门测试——反射扫 `Siege.Core` 中对 `Adjacency.Neighbors` 的直接调用只允许出现在 `Adjacency` 自身、`MapValidator` 的几何校验与表现层几何；做变异验证（在别处偷调 `Neighbors` 应被守门抓住）。

## 3. 地图校验

- [x] 3.1 `MapValidator`：4 人可落子格区间 80–95 → 95–110（可落子按 1.1 的 `IsPlayable`）；删除"障碍占外接 25%–35%"；距离均衡与必死口袋改沿气边求最短路 / 连通区。算例：85 格图被拒并报越界；两区几何等距但一条跨崖时按各自气边最短路算。验证：`map-definition` 场景测试。
- [x] 3.2 新增校验：桥必须在深水格上；栅栏必须在几何相邻格之间；每个出生区至少一条气边通路到中央入口。算例：全边缘为崖壁 / 深水的出生区被拒并指出编号。验证：单元测试。
- [x] 3.3 对称校验器 D2 → C4：绕中心 90° 旋转后高度、地表、障碍、桥、栅栏、信物格逐格一致，出生区编号轮换。验证：单元测试（构造一张只满足 D2 不满足 C4 的小图应被判不对称）。
- [x] 3.4 连珠线沿气边（裁决 A-6）：`PieceEffects.Step` 改走 `GameBoard.LibertyNeighbors`；守门允许名单里 `GameBoard.Neighbors → PieceEffects` 随之移除。算例（`piece-effects` 增量）：`C6 D6 E6` 连珠，`E6` h=2 或 `D6`–`E6` 有栅栏 → 位置加值 2。验证：`piece-effects` 两个新 Scenario 测试 + 既有四个 Scenario 仍绿；变异：改回几何邻居应红。

## 4. v3 基准地图

- [x] 4.1 `FourPlayerBaseMap` 重写为 v3 生成器：13×13，种子格按 C4 轨道展开；四个出生区各 12–14 格全部 h=2，一侧经 2–3 格 h=1 缓坡下到 h=0 中央，其余边缘为崖壁；中央含一格宽深水与至少一座预置桥、至少一段栅栏、至少一片林地；信物格 14（出生区 8、公共 6）；标注中央入口与咽喉。验证：`MapValidator` 全项通过（含 3.1–3.3）；测试断言地形要素各至少一次、可落子格在 95–110、四区距离两两差 ≤ 1。
- [x] 4.2 出生区容量：前三大回合 9 枚部署不出现无合法落点。验证：既有容量测试在 v3 上仍绿。
- [x] 4.3 导出 `maps/siege-4p-base-v3.json`；`map` 子命令按高度 / 地表 / 桥 / 栅栏打印文本图。验证：文件存在、再读入相等、人工看图。
- [x] 4.4 地图 Id 与全部引用改为 v3；v2 JSON 保留为历史存档并在注释里说明已不能加载。验证：全量测试通过。

## 5. 表现层视图模型（Siege.Presentation）

- [x] 5.1 盘面层视图模型带出每格高度、地表、桥与栅栏边，供 Godot 渲染；归属读法与棋串读法各按 2.1 / 2.2 取集合。验证：单元测试——平地区域两读法集合相同；有崖壁 / 栅栏 / 一格深水 / 林地时差集中的每一格都能给出地形原因。
- [x] 5.2 改写 `merge-board-layer` 留下的"两读法恒等"测试为 D-G 口径。验证：新测试绿，旧断言删除并列入提交信息。

## 6. Godot 地形渲染与交互

- [x] 6.1 `BoardGeometry.Center` 带高度（每层抬升固定层高）；`TryFromWorld` / `TryPick` 支持分层拾取（射线与三层平面求交，取最近命中的可落子格）。验证：Godot 端 headless 测试脚本——对 v3 每个可落子格从相机投影再拾取回同一格。
- [x] 6.2 地砖按高度堆叠并画崖壁侧面（Δh=2 与 Δh=1 侧面可区分）；深水、桥、林地各有可辨地表；栅栏沿格边立起不占落点。验证：`--screenshot` 出图人工对照 `art/style-exploration/` 基准与用户参考图；检查清单写入 `art/terrain-v3/README.md`。
- [x] 6.3 坐标标注适配 13 列，锚点放在棋盘外圈 h=0 平面，边缘格高度不同仍可读。验证：截图人工检查 + `A B C D E F G H J K L M N` 无 `I`。
- [x] 6.4 `--auto-demo` 与 `--seed` 在 v3 上跑通；Godot 层不自己算邻接 / 地形过滤。验证：源码级扫描（`src/godot/` 不在 sln，补 grep 守门）确认只消费视图模型与 `BoardGeometry`。

## 7. 既有测试与文档

- [x] 7.1 改写写死 11×11 / 85 / 36 障碍 / D2 对称 / `A–L` 的测试；几何算例改坐标重构，不改期望值。逐条列入提交信息并说明新坐标等价。
- [x] 7.2 设计文档 §3.1–3.3 改写为格属性与 v3 描述、§7.1 覆盖改走覆盖关系、§7.3 补"占据信物格同样揭示"、§14.2 两读法差集说明、§20 地形从装饰升级为规则元素；把 `terrain` 规格里的气边 / 覆盖算例（`F6`/`F7` 崖壁、`F6`–`G6` 栅栏、`F6`→`G6`→`H6` 隔岸）带进 §3 作为标准算例，使本 change 的规则类任务算例在归档后来自设计文档；变更记录加一行（v1.1 → v1.2）。验证：人工检查。
- [x] 7.3 `.trellis/spec/core/boundaries.md` 补"几何四邻 / 气边 / 覆盖关系三个唯一实现点"；`coordinates.md` 补列字母随宽度。验证：人工检查。
- [x] 7.4 `relic-generation` 在 v3 基准图上补一条宽口径统计断言（升级率 15–21%，裁决 B-7）。验证：单元测试。
- [x] 7.5 崖壁阈值具名常量（裁决 C-8）：Core 出 `TerrainData.CliffDrop = 2`，`Adjacency.LibertyNeighbors / CoverageTargets` 与 `Presentation.LayerContents.ReasonFor` 共用，全仓不再有第二份字面量 2 / 1 表达同一阈值；`boundaries.md` 单一实现表补"几何四邻 / 气边 / 覆盖关系 / 崖壁阈值"与 `CoverageMap.SourcesOf` 只读查询。验证：grep 守门 + 既有测试仍绿。

## 8. 变异验证与回归

- [x] 8.1 变异验证并记录：气边忘判栅栏；覆盖关系写成对称；隔岸覆盖穿两格水仍通过；桥格仍被当深水拒落子；C4 校验器只查 180°；距离校验仍走几何路径而非气边。每条变异必须被至少一个测试抓住。
- [x] 8.2 200 局回归（Standard，种子 1–200）：首次跨出生区冲突大回合、首次提子时占用率、整局无提子局数、平均结束大回合、终局原因分布、第 3 大回合领先者胜率、Pass 率、每步 AI 耗时。**只记录不调参**；报告口径注明两条：①"13×13 + 领地计分，对局变长为预期"；②"覆盖已不对称，四家高台无争议地覆盖脚下低地一圈，领地分与军势的比例与 v2 不可直接对照"。若解禁当回合（第 4 大回合）即发生提子的局占比 > 50%、首次提子中位 ≤ 4、或整局无提子局 > 25%，立即报告，不自行改规则。

## 段记录


### 段 A（组 1 + 组 2 + relic-control「占据即揭示」）

**结果**：`dotnet build` 退出码 0、0 警告；`dotnet test` 退出码 0，725/725 通过（基线 680 + 新增 45）。v2 `FourPlayerBaseMap` 在缺省地形（全 h=0、草地、无桥无栅）下加载，`PlayableCount` 仍为 85，`MapDefinition` 全部既有测试原样通过。

**改动文件与公开接口**

- `src/Siege.Core/Board/TerrainData.cs`（新）：`enum Surface { Grass, Road, Forest, DeepWater }`；`readonly record struct FenceEdge(Coord a, Coord b)`（构造时按字典序归一化，`A < B`，同格抛出）；`sealed class TerrainData(ImmutableDictionary<Coord,int> heights, ImmutableDictionary<Coord,Surface> surfaces, ImmutableHashSet<Coord> bridges, ImmutableHashSet<FenceEdge> fences)`，静态 `Flat`、常量 `MaxHeight = 2`，查询 `HeightAt / SurfaceAt / HasBridge / IsUnbridgedDeepWater / HasFence(a,b)`。构造期校验：高度 0–2、桥必须在深水格、栅栏两端几何相邻，违者抛 `ArgumentException` 并指出坐标。
- `Board/Adjacency.cs`：保留 `Neighbors(int width, int height, Coord c)`；新增 `static bool AreAdjacent(Coord a, Coord b)`（几何相邻，供数据校验）、`static ImmutableArray<Coord> LibertyNeighbors(MapData map, Coord c)`（气边，对称，沿 Neighbors 顺序）、`static ImmutableArray<Coord> CoverageTargets(MapData map, Coord s)`（覆盖关系三步，结果字典序）。两个导出对不可落子的 `c`/`s` 返回空。
- `Board/MapData.cs`：新增 `TerrainData TerrainData { get; init; } = TerrainData.Flat`；`int HeightAt(Coord)`、`Surface SurfaceAt(Coord)`、`bool HasBridge(Coord)`、`bool HasFence(Coord, Coord)`、`bool IsPlayable(Coord)`（盘内 ∧ 非障碍 ∧ 非未架桥深水）。`TerrainAt` 改为 `IsPlayable ? Playable : Obstacle`；`PlayableCount` 改按 `IsPlayable`。
- `Board/MapFile.cs`：DTO 新增 `Heights`（行字符串自上而下，字符 0/1/2）、`Surfaces`（行字符串，G/R/F/W）、`Bridges`（坐标列表）、`Fences`（`F6-G6`）。缺省省略即平地；显式 `null` 按既有口径抛 `FormatException`；行数 / 行长 / 字符非法 / 栅栏格式非法均抛指名字段的 `FormatException`；`TerrainData` 构造期的 `ArgumentException` 在此包装为 `FormatException`。写出时四个字段总是输出。
- `Board/GameBoard.cs`：新增 `ImmutableArray<Coord> LibertyNeighbors(Coord)`、`ImmutableArray<Coord> CoverageTargets(Coord)`（委托 `Adjacency`）；`GroupAt` / `LibertiesOf` 改走气边；`Place` 改判 `!Map.IsPlayable`，深水报"未架桥的深水格"、岩石文案不变。`Neighbors(Coord)` 保留几何语义（`PieceEffects.Step` 找连珠方向仍用它，piece-effects 规格不在本 change）。
- `Scoring/CoverageMap.cs`：`Compute` 改走 `board.CoverageTargets`，删除手写 `Terrain.Obstacle` 过滤；`Resolve` 不变（未架桥深水经 `Cell.Terrain == Obstacle` 归入 `OwnershipKind.Obstacle`）。
- `Ai/GroupSafety.cs`：眼位判定改遍历 `LibertyNeighbors(liberty)`，`ownWall` 只判己子（无气边的邻格天然是墙）。平地上与旧行为逐格等价。
- `Board/MapValidator.cs`：`FloodFill` / `MultiSourceDistances` 两处改走 `Adjacency.LibertyNeighbors`（各 2 行），平地等价；这是段 B 3.1 "距离与口袋沿气边"的前置，段 B 只需补测试用例与新校验。MapValidator 现已无 `Adjacency.Neighbors` 直接调用。
- `Board/Primitives.cs`：仅文档——`Terrain.Obstacle` 注明含未架桥深水。
- 测试：新建 `tests/Siege.Core.Tests/Terrain/`（`格属性Tests` 7、`边属性Tests` 3、`气边Tests` 7、`覆盖关系Tests` 13）；既有类追加 MODIFIED 场景：`四邻接Tests.几何相邻但无气边`、`四邻接Tests.几何邻居枚举只在允许名单内直接调用`（守门）、`棋串构成Tests.崖壁两侧不成串`、`气的计算Tests.靠崖壁的棋串`、`坐标记法Tests.列字母跳过I`（改 Theory，加 13 列 `A–N`，11 列期望保留）、`棋子向四邻接相邻格提供覆盖Tests.覆盖判定与气边判定分离`、`唯一覆盖查询Tests.林地信物只能占据`、`信物内容在首次被覆盖时永久公开Tests.占据即揭示`、`地图文件健壮性Tests.{地形字段往返逐字段相等, 缺地形字段的旧文件按平地读入, 畸形地形字段被指名报出×5}`；`UI层不含规则计算Tests.ForbiddenMembers` 加入 `GameBoard.LibertyNeighbors / CoverageTargets`；`Godot层不含规则计算Tests` 的源码 token 表加入 `.Neighbors(` / `.LibertyNeighbors(` / `.CoverageTargets(`（`src/godot` 当前无任何调用，仍绿）。夹具：`TestMaps.Blank(TerrainData, size, obstacles)` 重载、`TestMaps.Terrain(heights, surfaces, bridges, fences)` 构造器、`TestMaps.Synthetic(..., terrain)`、`RelicFixtures.Scene(TerrainData, relics)` 重载。

**既有测试改写**：无。680 条基线测试在 v2 缺省地形下全部原样通过，无一需要换坐标。唯一触碰的既有方法是 `坐标记法Tests.列字母跳过I`——由 `[Fact]` 改为 `[Theory]`，原 11 列断言 `ABCDEFGHJKL` 原样保留为第一组数据，新增 13 列 `ABCDEFGHJKLMN`（规格 MODIFIED 算例）。

**变异验证**（备份 `cp` → 变异 → 全量 build+test → 还原 → `filecmp` 逐字节一致，7 条全部 True）

| 编号 | 变异（文件 / 改动） | 红 | 红掉的测试 |
|---|---|---|---|
| M-A1 | `Adjacency.LibertyNeighbors` 去掉 `&& !map.HasFence(c, n)`（气边忘判栅栏） | 2 | `气边Tests.栅栏切断气与连接`、`覆盖关系Tests.栅栏不挡覆盖` |
| M-A2 | `Adjacency.Receives` 把 `h_t − h_s ≤ 1` 写成 `\|Δh\| ≤ 1`（覆盖对称化） | 3 | `覆盖关系Tests.居高临下`、`覆盖关系Tests.覆盖不等于气`、`棋子向四邻接相邻格提供覆盖Tests.覆盖判定与气边判定分离` |
| M-A3 | `Adjacency.CoverageTargets` 对岸 u 仍是深水时再沿同方向走一格（穿两格水） | 1 | `覆盖关系Tests.宽河不可隔岸` |
| M-A4 | `MapData.IsPlayable` 忽略桥：`SurfaceAt(c) != DeepWater`（桥格仍拒落子） | 4 | `格属性Tests.桥格可落子`、`气边Tests.桥恢复气`、`气边Tests.不可落子格没有气边`、`覆盖关系Tests.隔岸的桥格可被覆盖` |
| M-A5 | `CoverageMap.Compute` 加一行 `_ = Adjacency.Neighbors(board.Width, board.Height, c);`（别处偷调几何邻居） | 1 | `四邻接Tests.几何邻居枚举只在允许名单内直接调用` |
| M-A6 | `Adjacency.Receives` 去掉 `SurfaceAt(target) != Forest`（覆盖忘判林地） | 4 | `覆盖关系Tests.林地不接收覆盖`、`覆盖关系Tests.隔岸不能跨崖`、`唯一覆盖查询Tests.林地信物只能占据`、`信物内容在首次被覆盖时永久公开Tests.占据即揭示` |
| M-A7 | `Adjacency.CoverageTargets` 遇深水直接不覆盖（取消隔岸一跳） | 2 | `覆盖关系Tests.隔岸覆盖`、`覆盖关系Tests.隔岸的桥格可被覆盖` |

守门测试自带反面断言：扫描结果必须命中 `GameBoard.Neighbors → Adjacency.Neighbors` 与 `Adjacency.LibertyNeighbors / CoverageTargets → Adjacency.Neighbors`，防止空集恒绿。8.1 中属于本段的四条（气边忘判栅栏 / 覆盖写成对称 / 隔岸穿两格水 / 桥格仍拒落子）即 M-A1–M-A4，段 D 直接引用。

**常规决定（自定，附理由）**

1. `Terrain` 枚举保持两值，未架桥深水由 `TerrainAt` / `Cell.Terrain` 报为 `Obstacle`。`IsPlayableEmpty`、`MatchFlow._playableCells`、`BatchRehearsal:57`、`CoverageMap.Resolve`、`RelicLedger:72/223`、`BatchEvaluator:157`、Sim `BoardRenderer`、godot `BoardView` 全部零改动即满足"深水不可落子 / 不可控制 / 不计分"；加第三个枚举值则每处都要复核，而 Presentation / godot 本段不可写。要区分岩石与水读 `SurfaceAt`。
2. 地形装进独立的、构造函数里做校验的 `TerrainData` 值对象挂在 `MapData` 上：`MapData` 是 `record` + `required init`，init 赋值在构造函数之后，无法在 record 构造期抛出。属性名取 `TerrainData`（与类型同名）而不是 `Terrain`——后者会在类内遮蔽枚举 `Terrain`，`Terrain.Playable` 无法编译。
3. 构造期违规抛 `ArgumentException`（含坐标）；`MapFile.FromJson` 包装成指名字段的 `FormatException`，与既有"坏文件必须说得出问题在哪"的口径一致。
4. JSON 高度 / 地表用行字符串、自上而下（第一行是最高行号，与看图方向一致），桥用坐标列表，栅栏 `F6-G6`。四个字段总是写出：`磁盘上的基准地图文件与代码一致` 两侧都经 `ToJson`，不受影响；`缺地形字段的旧文件按平地读入` 钉住 v2 文件缺字段按平地读。
5. 2.5 改后复核：`src/Siege.Core` 内 `Terrain.Obstacle` 只剩 4 处，均非邻居过滤——`MapData.cs:86` `TerrainAt` 定义本身、`MapData.cs:83` 文档、`CoverageMap.cs:145` `Resolve` 的归属分支（不可落子格 → `OwnershipKind.Obstacle`）、`BatchRehearsal.cs:57` 落点合法性（单格判断）。`GroupSafety` 与 `CoverageMap.Compute` 的手写过滤已删。`RelicLedger` / `BatchEvaluator` 只判 `OwnershipKind.Obstacle`，在决定 1 下语义已正确。
6. `PieceEffects.Step` 继续用几何 `Neighbors` 找连珠方向：连珠是否被崖壁 / 深水切断属 piece-effects 规格，本 change 未定义。
7. 「占据即揭示」在 `RelicLedger.Reveal` 里本就成立（`kind != Neutral` 含 `Occupied`，既有测试 `直接占据也揭示` 守着），本段只补林地场景 `占据即揭示`，未改实现。
8. `Coord` 1.3 的勾选范围：`Siege.Core` 内除 `Coord.ColumnLetters` 外无第二份字母表、无写死 11 列（`FourPlayerBaseMap.Size = 11` 是 v2 地图自身，段 B 重写）；Sim 已按宽度索引 `ColumnLetters`。**Presentation / godot 的列标注写死未扫**，留给段 C 6.3。未新增按宽度切片的 helper（`映射实现只有一处` 会扫 `"ABCDEFGH"` 字面量，且无消费者）。1.2 的"v3 导出后再读入逐字段相等"目前只在含全部地形要素的合成图上验过，段 B 4.3 用真 v3 再跑一次。
9. 变异脚本用 `cp` 备份 + `filecmp` 逐字节校验还原，不用 `git checkout --`（工作树里有未提交改动）。

**待决（给主会话）**

- **A-1（段 B 3.2 边界）**："桥必须在深水格 / 栅栏必须在相邻格间"现已在 `TerrainData` 构造期强制（`ArgumentException`），MapValidator 到不了这两种坏数据。段 B 3.2 是否只补"桥 / 栅栏端点须在盘内"与"出生区气边连通"两项，还是要求同一违规也出 `MapValidationFailure` 编码（需把 `TerrainData` 校验改为可延迟）？建议前者。
- **A-2（表现层过渡）**：段 C 之前 godot `BoardView` 与 Sim `BoardRenderer` 用 `Terrain.Obstacle` 判色，深水会画成岩石；Sim `map` 子命令打印 `#`。段 B 4.3 的文本图应改读 `SurfaceAt` / `HeightAt`。
- **A-3（守门允许名单）**：`MapValidator` 现无 `Adjacency.Neighbors` 直接调用，仍按派发留在允许名单（段 B 的栅栏相邻 / C4 校验会用几何邻居）。段 B 结束后若仍无调用，建议收紧名单。
- **A-4（JSON 形制）**：行字符串自上而下是本段自定；v3 导出前若设计师偏好稀疏字典（`"F6": 2`），改 `MapFile` 两个私有方法即可，`MapData` 不动。
- **A-5（AI 语义提示，不调参）**：`GroupSafety` 眼位判定把崖壁 / 栅栏 / 深水一侧视为墙——v2 上行为不变，v3 上"靠崖的单眼"会被算作眼。属预期（无气边就是封堵），记录供 8.2 基线归因。

#### check 修订（trellis-check，2026-09-16）

**核对结论**：`terrain` 19 个 Scenario、`board-topology` MODIFIED 4 个、`coverage-territory` 2 个、`relic-control` 1 个均有同名测试；覆盖关系三步与气边三条件的实现逐字对得上规格（u 的条件是"非未架桥深水"、栅栏不进覆盖、`h_t − h_s ≤ 1` 单向、只穿一格水）。`src/Siege.Core` 内无手写邻居偏移；`Terrain.Obstacle` 只剩单格判断；`Board` / `Scoring` 无浮点、无缓存、无 `Godot.*`；`Siege.Sim` / `Siege.Presentation` 编译通过、零改动。v2 缺省地形 `PlayableCount` = 85。

**修订**

1. `tests/Siege.Core.Tests/BoardTopology/四邻接Tests.cs`「几何邻居枚举只在允许名单内直接调用」——原只扫 `Adjacency.Neighbors` 的直接调用，而 `GameBoard.Neighbors` 是 Core 内公开的几何入口，经它绕回"几何邻居 + 手写地形过滤"守门抓不到（见 N-4，0 红）。加第二段扫描：`GameBoard.Neighbors` 的 Core 内调用者只允许 `PieceEffects`（`Step` 找连珠方向，实现方决定 6），并加反面断言命中 `PieceEffects`。补后 N-4 红 1。
2. `src/Siege.Core/Board/TerrainData.cs` 构造器——显式写成缺省值的项（h=0、`Grass`）归一化掉。原因：`MapFile` 只写非缺省项，`TerrainData.Heights / Surfaces` 对含显式缺省项的数据"写出再读入"不相等，段 B 4.3 的"再读入相等"会假红（v3 生成器若逐格填高度必踩）。`HeightAt` / `SurfaceAt` 语义不变。
3. `tests/Siege.Core.Tests/MapDefinition/地图文件健壮性Tests.cs`「地形字段往返逐字段相等」——样本加入 `A1` 显式 h=0 与 `A2` 显式草地，并断言归一化后 `Heights.Count == 3`、`Surfaces.Count == 4`。

**变异验证（check 新增；`cp` 带时间戳备份 → 变异 → 全量 build+test → `finally` 还原 → 与原文逐字比对，5 条全部 True）**

| 编号 | 变异（文件 / 改动） | 红 | 红掉的测试 |
|---|---|---|---|
| N-1 | `Adjacency.Receives` 去掉 `h_t − h_s ≤ 1`（覆盖不看高度） | 2 | `覆盖关系Tests.仰视不覆盖`、`覆盖关系Tests.隔岸不能跨崖`（后者证明隔岸一跳的 u 也受高度约束） |
| N-2 | `Adjacency.LibertyNeighbors` 把 `\|Δh\| ≤ 1` 写成 `≤ 2`（崖壁不断气） | 6 | `四邻接Tests.几何相邻但无气边`、`棋串构成Tests.崖壁两侧不成串`、`气的计算Tests.靠崖壁的棋串`、`气边Tests.崖壁切断气`、`覆盖关系Tests.覆盖不等于气`、`棋子向四邻接相邻格提供覆盖Tests.覆盖判定与气边判定分离` |
| N-3 | `Adjacency.CoverageTargets` 循环开头加 `if (map.HasFence(s, t)) continue;`（栅栏挡覆盖） | 1 | `覆盖关系Tests.栅栏不挡覆盖` |
| N-4 | `GroupSafety` 改回 `board.Neighbors(liberty)` + 手写 `cell.Terrain == Terrain.Obstacle` 判墙（经 `GameBoard.Neighbors` 绕过气边） | 修订前 0 → 修订后 1 | `四邻接Tests.几何邻居枚举只在允许名单内直接调用` |
| N-5 | `TerrainData` 构造器去掉高度缺省项归一化（`Heights = heights;`） | 1 | `地图文件健壮性Tests.地形字段往返逐字段相等`（`Surfaces` 归一化分支由同一测试的 `Surfaces.Count == 4` 断言覆盖：去掉即 5 → 红） |

N-1 中 `覆盖不等于气` / `覆盖判定与气边判定分离` 不红是几何原因：两例棋子都在 h=2 最高处，去掉高度条件不增加任何覆盖目标；`仰视不覆盖` 与 `隔岸不能跨崖` 才是钉高度条件的测试。

**对实现方待决的意见**：A-1 同意"前者"（`MapFile` 已把构造期 `ArgumentException` 包成带坐标的 `FormatException`，规格两条"加载被拒并指出坐标"已满足）；A-2 / A-4 / A-5 同意照记；A-3 同意，且允许名单现在有两份（`Adjacency.Neighbors` → `Adjacency` / `MapValidator` / `GameBoard.Neighbors`；`GameBoard.Neighbors` → `PieceEffects`），段 B 结束后一并复核收紧。

**新增待决（给主会话）**

- **A-6（设计级，不改）**：`PieceEffects.Step` 走几何邻居，连珠可跨崖壁 / 未架桥深水 / 栅栏成线。piece-effects 规格不在本 change，但 v3 上"隔河连珠"是否符合设计意图需裁决；若要求连珠沿气边，改一行 + 补 piece-effects 增量规格。
- **A-7（命名，不改）**：新测试目录 `tests/Siege.Core.Tests/Terrain/` 的命名空间是 `Siege.Core.Tests.TerrainSpec`，与目录不同名——为避开 `Siege.Core.Board.Terrain` 枚举遮蔽（`格属性Tests` 里已需要全限定 `Siege.Core.Board.Terrain.Obstacle`）。可接受，记一笔。
- **A-8（段 D 文档）**：`.trellis/spec/core/coordinates.md` 仍写"列字母 `A`–`L` 共 11 列"，段 D 7.3 改；`Godot层不含规则计算Tests` 的 token `.Neighbors(` 在段 C 若 Godot 侧出现同名 API 调用会误报，届时按需收窄为 `board.Neighbors(` 形状。

#### 主会话核实（段 A）

- `dotnet build siege.sln` EXIT=0、零警告；`dotnet test siege.sln` EXIT=0，725/725（check 修订后）。
- 自做变异 M-主-1：`Adjacency.CoverageTargets` 隔岸分支把 `Receives(map, u, h)` 换成 `map.IsPlayable(u)`（对岸忽略高度与林地）→ 只红 `隔岸不能跨崖` 1 条，**对岸林地条件无测试钉住**。补测试 `覆盖关系Tests.隔岸对岸为林地不覆盖`，复跑同一变异红 2（新测试 + 跨崖），还原逐字节一致。段 A 测试数 725 → 726。
- 裁决：A-1 取①（构造期抛出即满足规格，段 B 校验器不重复）；A-2 段 B 4.3 文本图改读 `SurfaceAt/HeightAt`，段 C 修 Godot；A-3 段 B 后收紧允许名单；A-4 接受 JSON 形制；A-5 只记录供 8.2 归因；A-7 接受；A-8 段 C/D 处理。check 修订 2（`TerrainData` 归一化缺省项）接受。
- **A-6 待用户裁**：连珠（`PieceEffects.Step`）走几何邻居还是气边。

### 段 B（组 3 + 组 4 + 7.1）

**结果**：`dotnet build siege.sln` 退出码 0、0 警告；`dotnet test siege.sln` 退出码 0，723/723 通过（段 A 末 726：删障碍占比 6 条、D2 对称 Theory 收成 C4 Fact 净减 4、新增连珠 2 + 基准图 2 + 校验规则 3）。`siege-4p-base-v3` 是唯一可加载的 4 人图：v2 JSON 留在 `maps/` 作历史存档，`可落子格越界` 测试用它证明 85 格被 `PLAYABLE_COUNT_OUT_OF_RANGE` 拒绝。

**v3 文本图**（`dotnet run --project src/Siege.Sim -- map` 输出；每格两位 = 高度 + 标记）

```
 13 ## 24 24 24 ## ## ## ## ## 23 23 23 ##
 12 24 2r 24 24 0  0  0  ## 1. 23 23 2r 23
 11 24 24 2r ~~ ~~ ## 0. 0. 1. 23 2r 23 23
 10 24 24 24 ~~ ~~ ~~ 0= ~~ ~~ ~~ ~~ 23 23
  9 ## 1. 1. ~~ 0F 0 |0o 0  0F ~~ ~~ 0  ##
  8 ## ## 0. ~~ 0  ## 0  ## 0  ~~ ## 0  ##
                            --
  7 ## 0  0. 0= 0o 0  0R 0  0o 0= 0. 0  ##
                --
  6 ## 0  ## ~~ 0  ## 0  ## 0  ~~ 0. ## ##
  5 ## 0  ~~ ~~ 0F 0  0o|0  0F ~~ 1. 1. ##
  4 21 21 ~~ ~~ ~~ ~~ 0= ~~ ~~ ~~ 22 22 22
  3 21 21 2r 21 1. 0. 0. ## ~~ ~~ 2r 22 22
  2 21 2r 21 21 1. ## 0  0  0  22 22 2r 22
  1 ## 21 21 21 ## ## ## ## ## 22 22 22 ##
    A  B  C  D  E  F  G  H  J  K  L  M  N
## 岩石  ~~ 深水  = 桥  1-4 出生区  r 出生区信物  o 公共信物  R 公共高档信物（兼中央入口 G7）  F 林地  . 土路  | 与 -- 栅栏
```

**统计**：外接 13×13 = 169；可落子 105（h0/h1/h2 = 45/8/52）；岩石 36（9 轨道）；深水 32（其中桥 4：G4 D7 K7 G10，未架桥 28）；栅栏 4（E6-E7 G5-H5 J7-J8 F9-G9）；林地 4（岛四角 E5 J5 J9 E9）；土路 16。出生区 4 × 13 格，全部 h=2，0 左下 → 1 右下 → 2 右上 → 3 左上（旋转序）；每区经 2 格 h=1 缓坡（E2 E3 轨道）下到 h=0 落脚点，其余边缘为崖壁 / 岩石 / 深水。信物 13 = 出生区 8（B2 C3 轨道）+ 公共标准档 4（桥头 G5 E7 J7 G9）+ 公共高档 1（岛心 G7 = 中央入口）。咽喉 = 四座桥。四区沿气边最短距离：最近公共信物 5 / 中央入口 7 / 最近咽喉 4，四区全等（极差 0）。全盘可落子格沿气边连通成一块，无口袋、无豁免、容差 1 未放宽。C4 校验（`MapSymmetry.RotationDefects`）空。

**设计方法**：先用脚本复刻校验器全部规则（可落子预算、气边 BFS 距离、气边连通区口袋、全盘连通、一格宽水的覆盖定义、C4 逐格比）迭代布局，定稿后把种子表移植进 C# 生成器（种子 + C4 轨道展开，`Rotate(x, y) = (12 − y, x)`）。第一版可落子 109 贴着上限，去掉每条走廊落脚点一格（F2 轨道）留出余量。

**改动文件与公开接口**

- `src/Siege.Core/Board/Maps/FourPlayerBaseMap.cs`：重写为 v3（Id `siege-4p-base-v3`、`Size = 13`、C4 轨道展开，栅栏边轨道两端一起旋转）。
- `src/Siege.Core/Board/MapSymmetry.cs`（新）：`static Coord Rotate90(MapData, Coord)`；`static ImmutableArray<string> RotationDefects(MapData)`（逐格比障碍 / 高度 / 地表 / 桥 / 信物格 / 出生区编号轮换 `R(zone_i) == zone_{(i+1) mod n}`，再比栅栏集合、咽喉集合、中央入口不动；非正方形直接判不对称）；`static bool IsC4Symmetric(MapData)`。**不接入 `MapValidator`**（3 人图 MUST NOT 被强制方形对称），由 `四人基准地图Tests.旋转对称` 调用。
- `src/Siege.Core/Board/MapValidator.cs`：4 人可落子区间 80–95 → 95–110；删除 `ValidateObstacleRatio`、`MinObstaclePercent` / `MaxObstaclePercent`、`FormatPercent`（失败码 `OBSTACLE_RATIO_TOO_LOW/HIGH` 随之消失）；新增 `ValidateTerrainBounds` → `TERRAIN_OUT_OF_BOUNDS`（高度 / 地表 / 桥 / 栅栏端点越界，列出坐标）与 `ValidateBirthZoneConnectivity` → `BIRTH_ZONE_ISOLATED`（沿气边到不了中央入口，消息带区号、坐标为该区可落子格）；内部 `TerrainAt == Playable` 一律改 `IsPlayable`。距离 / 口袋沿气边在段 A 已改，本段补测试。
- `src/Siege.Core/Scoring/PieceEffects.cs`：`Step` 改遍历 `board.LibertyNeighbors(c)`（裁决 A-6）。
- `src/Siege.Sim/Program.cs`：`map` 子命令重写文本图（高度 + 标记双字符、格间 `|` / 行间 `--` 画栅栏、头部统计岩石 / 深水 / 桥 / 栅栏 / 林地 / 土路 / 高度分布）；新增私有 `Glyph`。`src/Siege.Sim/Play/BoardRenderer.cs`：不可落子格按 `SurfaceAt` 区分深水 ` ~ ` 与岩石 ` # `（裁决 A-2，最小改动）。`src/Siege.Sim/Config/RunConfig.cs`：`MapId` 默认 `siege-4p-base-v3`。
- `maps/siege-4p-base-v3.json`（新，`map` 子命令导出，`磁盘上的基准地图文件与代码一致` 钉住）；`maps/siege-4p-base-v2.json` 顶部加 `_comment` 说明已不能加载（不含 `Heights` 字段，`缺地形字段的旧文件按平地读入` 仍绿）。
- 守门 `四邻接Tests.几何邻居枚举只在允许名单内直接调用`：① `Adjacency.Neighbors` 名单删 `MapValidator`（裁决 A-3）；② `GameBoard.Neighbors` 在 Core 内不再允许任何调用者（`PieceEffects` 移除），`boardCallers` 直接进违规清单，原正面断言删除（① 的三条正面断言兜住"扫描器坏了"）。

**既有测试改写逐条**（换坐标不改期望值；写死尺寸 / 格数 / 对称的按 v3 事实改写）

| 测试 | 改法 |
|---|---|
| `四人基准地图Tests.外接尺寸与可落子格` | 11×11 / 80–95 → 13×13 / 95–110，加 Id 断言 |
| `四人基准地图Tests.可落子格规模` | 80–95 → 95–110；删"障碍占比 25%–35%"断言（规则取消）；加"每区全部 h=2"与"区外气边邻格恰为 2–3 格 h=1 缓坡" |
| `四人基准地图Tests.信物格分布` | 公共区 6 → 5（C4 轨道算术，见待决 B-1），加"中央入口是高档信物" |
| `四人基准地图Tests` 新增 | `旋转对称`、`地形要素齐全`（一格宽水按 E4 覆盖定义断言） |
| `基准地图对称性Tests` | D2 三变换 Theory（障碍 / 信物 / 咽喉 / 出生区 ×3）→ C4 Fact：`障碍集合在C4旋转下不变`、`地形在C4旋转下逐格不变`（新）、`信物格与分区在C4旋转下不变`、`咽喉与中央入口在C4旋转下不变`、`出生区在C4旋转下编号轮换`（收紧为 `R(zone_i) == zone_{i+1}`）；`四个出生区到三类地标的距离精确相等` 期望 [2, 6, 3] → [5, 7, 4]，测试内 BFS 改沿气边（自带 |Δh| ≤ 1 / 深水 / 栅栏规则）；`全盘可落子格连通且无小口袋` 85 / 36 → 105 / 36 / 未架桥 28；`校验输出跨次运行确定` C6 → G4；新增 `只满足D2的图被判不对称`、`出生区编号不轮换的图被判不对称`、`每项属性都参与旋转比对` |
| `地图静态校验规则Tests.障碍占比越界` / `障碍占比区间两端都是闭的`（×4） / `障碍占比过高` | **删除**（规则在 D-F 取消） |
| `地图静态校验规则Tests.可落子格越界` | v1 障碍 109 + 挖 75 → v2 JSON 85（下界，同时证明 v2 不能加载）+ v3 去岩石 141（上界）；文案 80–95 → 95–110 |
| `地图静态校验规则Tests.出生区可落子格超出区间` | 挖 2 格 → 11 不变；上界 14 由"去区内障碍"改为"把缓坡 E2 划进出生区 0" |
| `地图静态校验规则Tests.地标不可达被报出` | 墙 x=5 / y=5 从 11 格改 13 格（F 列 + 第 6 行），加 `BIRTH_ZONE_ISOLATED` |
| `地图静态校验规则Tests.出生区距离失衡` | 咽喉 C6 → G4，断言"出生区 0 = 4" |
| `地图静态校验规则Tests.必死口袋` / `必死口袋可显式豁免` / `豁免必须写明理由` | 墙 A1 C2 B3 A4 → B1 C2 D3，口袋 7 格 [B1 C1 D1 A2 B2 D2 A3] → [A2 B2 A3 B3 C3 A4 B4]；豁免格 B1 / A1 → A2 |
| `地图静态校验规则Tests.面积恰好等于两眼最小格数的空区不算口袋` | 墙 C2 B3 A4（+A1）→ B1 C1 D1 D2 D3（+A2），8 格 → 7 格的期望不变 |
| `地图静态校验规则Tests.信物格必须位于可落子格` | D4 → B2 |
| `地图静态校验规则Tests` 新增 | `距离沿气边计算`（G 列 h=2 墙：出生区 0 = 4、出生区 1 = 12；降为 h=1 即通过）、`出生区被孤立`（崖壁 / 深水两种封法 + 缓坡 / 桥两种解法）、`地形数据越界被报出` |
| `人数适配预算Tests.格数超出预算` | v2 裁成 12×12 → 合成 12×12 + 14 障碍（130 不变；裁 v3 会触发 `TERRAIN_OUT_OF_BOUNDS`）；80–95 → 95–110 |
| `出生区归属Tests.共享出生区` | 区形 15 → 13（v3 区内无障碍），可落子 13 不变 |
| `地图文件往返Tests.文件里用围棋记法` | "F6" / "A6" → "G7" / "B2" / "G4" |
| `棋盘格子状态模型Tests.信物格可正常落子` / `越界坐标的地形就是障碍` | D4 → B2；(11,0) / (0,11) → (13,0) / (0,13)（13×13 上原坐标变成盘内格）；F6 → G7 |
| `盘面序列化Tests.非盘面信息不影响序列化` | 空盘 11 → 13，D4 → B2 |
| `盘面副本与批量写入Tests.副本不重跑校验` | F6（v3 岩石）→ G7 |
| `对局日志的记录内容Tests.日志覆盖七类记录` | MapId v2 → v3 |
| `冲突占用率口径Tests.真实跑局把可落子格写进日志首部` | 85 → 105 |
| `终端对局Tests.脚本输入能落子并走到终局` | 落子 A1（v3 角石）→ B1 |
| `公共争夺区信物权重与高阶升级Tests` | `Map` 由基准图改为合成图（出生区 4×2 + 公共 4 Standard + 2 High，即 v2 分布）；60000 / 40000 / 20000 与 20% 均值期望不变——v3 公共区 4+1 的均值是 18%，不该改期望值去凑 |
| `连珠子的位置加值Tests` 新增 | `崖壁截断连珠线`、`栅栏截断连珠线` |
| `地图文件健壮性Tests` | 仅注释（v3 文件；v2 为存档） |

**变异验证**（`mutate.py`：`cp` 带时间戳备份 → 变异 → 全量 build+test → `finally` 还原 → 与原文逐字比对，6 条全部 True）

| 编号 | 变异（文件 / 改动） | 红 | 红掉的测试 |
|---|---|---|---|
| M-B1 | `MapSymmetry.Rotate90` 改成 180° 旋转（C4 校验器只查 180°） | 2 | `基准地图对称性Tests.只满足D2的图被判不对称`、`四人基准地图Tests.旋转对称`（180° 下出生区 0 的像是 2，编号轮换检查报出） |
| M-B2 | `MapValidator.MultiSourceDistances` 改走 `Adjacency.Neighbors` + `IsPlayable` 过滤（距离仍走几何路径） | 3 | `地图静态校验规则Tests.距离沿气边计算`、`出生区被孤立`、`四邻接Tests.几何邻居枚举只在允许名单内直接调用` |
| M-B3 | `PieceEffects.Step` 改回 `board.Neighbors(c)`（连珠改回几何邻居） | 3 | `连珠子的位置加值Tests.崖壁截断连珠线`、`栅栏截断连珠线`、`四邻接Tests.几何邻居枚举只在允许名单内直接调用` |
| M-B4 | `Budgets[4]` 写成 `new(110, 95, …)`（可落子区间写反） | 35 | `地图静态数据Tests.地图基准校验通过`、`磁盘上的基准地图文件与代码一致`、`可落子格越界`、`格数超出预算` 与全部经 `GameBoard.Load` 的跑局 / 遥测测试 |
| M-B5 | 去掉 `ValidateBirthZoneConnectivity` 调用 | 2 | `地图静态校验规则Tests.出生区被孤立`、`地标不可达被报出` |
| M-B6 | 去掉 `ValidateTerrainBounds` 调用 | 1 | `地图静态校验规则Tests.地形数据越界被报出` |

8.1 中属于本段的四条（C4 只查 180° / 距离走几何 / 连珠改回几何 / 区间写反）即 M-B1–M-B4，段 D 直接引用。

**常规决定（自定，附理由）**

1. 岛心 G7 同时是中央入口与高档信物：C4 唯一定点只有中心，公共信物要凑到 13–15 只能 4 + 1；规格"中央区承担更高预算"正好落在这一格。
2. `MapSymmetry` 独立成类、不进 `MapValidator`：规格明写 3 人图 MUST NOT 被强制方形对称；生成器不自检，由测试调用。
3. `BIRTH_ZONE_ISOLATED` 与 `LANDMARK_UNREACHABLE`（中央入口那列）对同一根因双报：它们是规格第 1 条与第 7 条两条规则，各报各的口径（后者带距离），不在距离校验里特判。
4. 3.2 按裁决 A-1 只补"地形坐标盘内"与"出生区气边连通"两项；桥 / 栅栏合法性仍在 `TerrainData` 构造期。
5. 咽喉 = 四座桥（`G4` 轨道）：过桥是进岛唯一路径，桥格只有 2 个气边邻格，天然是咽喉。
6. 尾巷 H2/J2 保留：J2 被邻家高台 K2 单向覆盖，是 E5"居高临下"在图上的唯一展示点；为此把 F2 轨道改岩石把可落子压回 105。
7. 出生区编号按旋转序 0 左下 → 1 右下 → 2 右上 → 3 左上（v2 是左下、右下、左上、右上）：`R(zone_i) == zone_{i+1}` 才能做成精确断言。
8. Sim `BoardRenderer`（`play` 视图）只按 A-2 区分水 / 岩石，不画高度 / 桥 / 栅栏：终端对局不在段 C 范围，也不是本段验收项。
9. 岛内四块岩石（F6 轨道）与四角林地保留：岩石给围杀提供墙，林地让岛角只能靠占据拿——两者都是 D-A 里"地形被规则读到"的展示。
10. 变异脚本用 `cp` 带时间戳备份 + `finally` 还原 + 逐字比对，未用 `git checkout --`。

**待决（给主会话）**

- **B-1（设计级）**：公共信物 5 枚而非"约 6"。C4 下轨道大小只有 4 / 1，8 + 6 = 14 不可能（只能 13 或 17）。已按 5 实现并改 `信物格分布` 断言；`relic-generation` 的 20% 均值测试改在合成 4:2 图上跑。若坚持 6，要么放弃严格 C4，要么信物总数 17（超 13–15）——需改规格。
- **B-2（设计级）**：中央入口与高档信物同格（G7）。校验器不禁止；若设计上要分开，中央入口只能是中心格，信物就得挪到轨道 4 → 公共信物 4 / 8，总数 12 / 16 越界，同样要改规格。
- **B-3（记录供 8.2 归因）**：每家高台都单向覆盖邻家（顺时针下一家）走廊尽头一格（J2 轨道）；桥格只有 2 气；岛 21 格 + 4 桥是四家唯一的接触面，四区各 20 格自留地（13 高台 + 2 缓坡 + 3 落脚点 + 2 尾巷；4×20 + 21 + 4 = 105）。领地计分下"自留地"几乎稳拿，胜负在岛上——这是 D-E 高台安全的直接后果，基线看首次提子与整局无提子比例。另注意：design Open Question 1 写"其余边缘直接落到 h=0（崖壁）"，v3 高台四周多数是岩石 / 深水，真正的 h=2→h=0 崖壁每区只有一处（B4→B5 轨道，即尾巷尽头），E5 单向覆盖在盘面上出现的频率由此决定。
- **B-4（段 C 留意）**：出生区编号方位与 v2 不同（见常规决定 7）；Godot / Presentation 若按编号推断方位或颜色，段 C 要复核。另：`src/godot/scripts/MatchSession.cs` 直接调 `FourPlayerBaseMap.Create()`，段 B 后图形版加载的就是 13×13 v3，而列标注 / 拾取仍按 11 列写死（6.1–6.3）——段 C 之前图形版处于"能跑、显示不对"状态。
- **B-5（段 C / 段 D）**：Sim `play` 视图未画高度 / 桥 / 栅栏（只区分水与岩石）；`.trellis/spec/core/coordinates.md` 仍是 11 列口径（A-8）。
- **B-6（守门现状）**：`Adjacency.Neighbors` 名单只剩 `Adjacency` 自身与 `GameBoard.Neighbors`；`GameBoard.Neighbors` 在 Core 内零调用者，只留给表现层几何（段 C 的 `Godot层不含规则计算Tests` token 表需按 A-8 复核）。

#### check 修订（trellis-check，2026-09-16）

**结果**：`dotnet build siege.sln` 退出码 0、0 警告；`dotnet test siege.sln` 退出码 0，723/723。`Siege.Presentation` / `src/godot/` 零改动；实现方落盘的 `maps/siege-4p-base-v3.json` 在本次改动前已由 `磁盘上的基准地图文件与代码一致` 证明与代码语义等价；check 期间重跑 `map` 导出两次 md5 一致（7c3dddd4…，证明导出确定性，未做与原文件的逐字节比对）；`play` 用 `1/1/B1 B/v/ok` 脚本能开局并走到 AI 回合。

**修订清单**

1. `tests/Siege.Core.Tests/MapDefinition/四人基准地图Tests.cs`（`地形要素齐全`）：规格 Requirement"中央区域为 h=0 低地并含深水"此前没有测试钉住——变异 N-4（生成器把 G7 抬到 h=1）只红 `磁盘上的基准地图文件与代码一致` 这条通用漂移守门，说不出违反了哪条约束。补三段断言：中央入口 = 盘心且 h=0；切比雪夫距离 ≤ 2 的岛内可落子格全 h=0；距离 ≤ 3 处存在未架桥深水。补后 N-4 红 2（本测试 + 磁盘一致）。
2. `maps/siege-4p-base-v2.json`：段 B 记录写"顶部加 `_comment`"，实际文件没加——工作区里它只是 LF→CRLF 的换行差异（`git diff HEAD --stat` 不含它）。现已补 `_comment`（不含 `"Heights"` 字面量，`缺地形字段的旧文件按平地读入` 仍绿；`含未知字段的地图能正确加载` 已证明 `MapFile` 宽容未知字段），文件写回 LF。
3. `src/Siege.Sim/Program.cs`：`map` 图例列了 `@ 中央入口 ^ 咽喉`，但 v3 图上这两个字符永远不出现（G7 画成 R、桥画成 =）。图例加一句说明，不改画法。

**新增变异**（同实现方口径：备份 → 变异 → 全量 build+test → `finally` 还原 → 逐字节比对，4 条全部 True；不与 M-B1～M-B6 重复。派发提示里的"出生区连通校验用几何邻居"与 M-B2 是同一处代码——`ValidateBirthZoneConnectivity` 复用 `MultiSourceDistances`——不再重跑）

| 编号 | 变异（文件 / 改动） | 红 | 红掉的测试 |
|---|---|---|---|
| N-1 | `MapSymmetry.RotationDefects` 删掉栅栏比对那段 foreach | 1 | `基准地图对称性Tests.每项属性都参与旋转比对`（`四人基准地图Tests.旋转对称` 仍绿：v3 自身对称） |
| N-2 | `FourPlayerBaseMap.ContestedStandardSeeds = []`（少展开一个轨道，信物 13 → 9） | 35 | `四人基准地图Tests.信物格分布`、`地图静态数据Tests.地图基准校验通过`（RELIC_COUNT）、`磁盘上的基准地图文件与代码一致`、`四个出生区到三类地标的距离精确相等`（最近公共信物变远）及全部经 `GameBoard.Load` 的跑局 / 遥测 / 回放测试 |
| N-3 | 缓坡轨道 `RampSeeds` 高度 1 → 2（高台没有下坡） | 40 | `四人基准地图Tests.可落子格规模`（区外气边邻格须为 2–3 格 h=1）、`地形要素齐全`（h=1 消失）、`地图基准校验通过`（BIRTH_ZONE_ISOLATED）、`全盘可落子格连通且无小口袋`、`必死口袋`×2 及全部跑局测试 |
| N-4 | 生成器 `heights[G7] = 1`（中央入口抬高） | 修前 1 / 修后 2 | 修前只有 `磁盘上的基准地图文件与代码一致`；补断言后加 `四人基准地图Tests.地形要素齐全` |

**观察（不改）**

- `MapSymmetry` 不接入 `MapValidator` 符合规格（"地图静态校验规则"7 条里没有 C4；3 人图 MUST NOT 被强制方形对称）；C4 由 `四人基准地图Tests.旋转对称`（`RotationDefects` 为空）+ `基准地图对称性Tests` 独立旋转算式两边钉住。
- 距离 / 口袋 / 出生区连通全部走 `Adjacency.LibertyNeighbors`（`MultiSourceDistances`、`FloodFill`），Core 内 `Adjacency.Neighbors` 的调用者只剩 `GameBoard.Neighbors` 委托；守门测试 ① 段三条反面断言仍在。
- `relic-generation` 规格的"约 20%"是升级判定的全局口径（`spec.md` L42/L46），不是基准图性质；`公共争夺区信物权重与高阶升级Tests` 换合成 4:2 图、期望值不变是正确处理。但从此没有统计测试在真实基准图上跑：v3 公共区 4 Standard + 1 High 的均值是 18%。
- `CentralEntrance` 在 `Siege.Core` / `Siege.Presentation` 内除 `MapData` / `MapFile` / `MapValidator` / `MapSymmetry` / 生成器外零消费者：它只是校验器的距离锚点，没有任何运行时规则读它。

#### 主会话核实（段 B）

- `dotnet build siege.sln` EXIT=0 零警告；`dotnet test siege.sln` EXIT=0，723/723（check 修订后）。
- 自做变异 M-主-2：`FourPlayerBaseMap.FenceSeeds` 多加一条 `("G5","G6")` → MapDefinition 红 6+（基准校验、往返、距离失衡、出生区归属……），还原逐字节一致。
- 裁决：B-1 公共信物取 5，规格文字改为"桥头 4 + 岛心 1"（design 裁决 33）；B-2 接受同格（裁决 34）；B-3 转 8.2 归因；B-4 / B-5 转段 C；B-6 守门现状接受；B-7 转段 D（裁决 35）。check 修订 3 条接受。

### 段 C（组 5 + 组 6）

**结果**：`dotnet build siege.sln` 退出码 0、0 警告；`dotnet test siege.sln` 退出码 0，726/726（段 B 末 723 + 新增 3：`平地上两种读法点亮同一批空格` 是改写不是新增，新增为 `差集可由地形解释`、`盘面层视图模型带地形Tests` ×2）。Godot `--headless --build-solutions` 退出码 0；`--headless -- --auto-demo` 退出码 0（种子 20260915 第 4 大回合达到上限，四家名次照常打印）；`-- --auto-demo --pick-check` 退出码 0，105 个可落子格投影 → 拾取往返一致 105。

**改动文件与公开接口**

- `src/Siege.Core/Scoring/CoverageMap.cs`（**Core 只读查询，段记录写明**）：新增 `readonly record struct CoverageSource(Coord Stone, bool Adjacent)` 与 `ImmutableArray<CoverageSource> CoverageMap.SourcesOf(Coord)`。`Compute` 遍历 `board.CoverageTargets(c)` 时顺手记录来源与 `Adjacency.AreAdjacent(c, target)` 一位，不另算覆盖关系、不改任何既有查询。理由：Presentation 被 IL 守门禁用 `Adjacency` 整类与 `GameBoard.CoverageTargets / LibertyNeighbors`，而差集原因里"崖壁 vs 隔岸"要知道来源与目标是否相邻——这一位只能由 Core 给。`UI层不含规则计算Tests` 未禁 `SourcesOf`（它是已算好的表的读取）。
- `src/Siege.Presentation/Visibility/DefaultBoardView.cs`：`BoardCellView` 新增 `int Height`、`Surface Surface`、`bool HasBridge`（位于 `Terrain` 之后）；`DefaultBoardView` 新增 `ImmutableArray<FenceEdge> Fences`（Coord 字典序）。`From` 读 `board.Map.HeightAt / SurfaceAt / HasBridge / TerrainData.Fences`。
- `src/Siege.Presentation/Layers/LayerContents.cs`：新增 `enum TerrainReason { Cliff, Fence, AcrossWater, Forest }`、`record ReadingDiffCell(Coord, ImmutableArray<TerrainReason> Reasons)`、`record BoardReadingDiff(CoveredNotLiberty, LibertyNotCovered)`（`Empty` / `IsEmpty`）；`TerritoryLayerContent(Cells, Diff)` 与 `LibertyLayerContent(Groups, Thresholds, Diff)` 各带差集；新增 `TacticalLayers.ReadingDiff(PublicWorld)`：被覆盖 = 覆盖表独占 / 争议格，气 = 气快照全部棋串的气；差集原因由 `SourcesOf` 的 `Adjacent` 位 + `MapData.HasFence / HeightAt / SurfaceAt` 查表：不相邻 → 隔岸；相邻有栅栏 → 栅栏；相邻且来源比目标高 ≥ 2 → 崖壁；是气无人覆盖且林地 → 林地；四条都不命中抛 `InvalidOperationException`（说明 Core 两套关系与规格不一致，不静默）。归属读法仍按覆盖表（2.2）、棋串读法仍按气快照（2.1）取集合，段 A 已接好，本段没有重取。
- `src/Siege.Presentation/Layers/TacticalLayerState.cs`：`BoardReading` 文档注释由"恒等"改为 D-G 口径。`src/Siege.Presentation/Text/Labels.cs`：新增 `Labels.TerrainReason`（崖壁 / 栅栏 / 隔岸 / 林地）。
- `src/godot/scripts/BoardGeometry.cs`：新增 `LayerHeight = 0.35f`、`MaxLevel = 2`、`TopYOf(level)`、`Center(coord, width, height, level)`（3 参重载 = h=0 平面，标注锚点专用）；`TryPick(camera, screen, width, height, Func<Coord,int?> levelOf, out coord)` 分层拾取；`LabelMargin` 0.85 → 1.2（近边与左右），新增 `FarLabelMargin = 1.7`（远边列标注专用，`ColumnLabelAnchor` 的 `far` 分支用它，`Z = edge.Z` 形状不变）。`TryFromWorld` 不变。`(width - 1)` 类换算仍恰 6 处、`RoundToInt` 恰 2 处，`坐标映射在Godot侧唯一` 的精确计数不用改。
- `src/godot/scripts/BoardView.cs`：`Build(DefaultBoardView, zoneOwners)`（原来吃 Core `MatchPublicView`，现在只吃 Presentation 视图模型）；新增 `LevelOf(Coord)`（可落子格的高度，否则 `null`）、`CenterOf(Coord)`（带高度格心）；`AddTileStack`（h=1 层土色带、h=2 层岩灰带 + 面砖）、`AddWater`（水面比同层地砖低 `WaterDrop = 0.10`，与底座齐平）、`AddFence`（缝中点、两格较高层）；林地加 `LowPoly.Trees`、桥加 `LowPoly.Bridge`；底座板顶面下沉到地砖面之下 0.10；一切叠加物（光标 / 信物标记 / 棋子 / 着色 / 气点 / 柱 / 环 / 叉）改走 `CenterOf`；相机改为固定俯角 60°、距离随（棋盘 + 标注外圈）跨度缩放。
- `src/godot/scripts/LowPoly.cs`：新增 `Trees(variant)`、`Fence(alongX)`、`Bridge()`。`src/godot/scripts/Visuals.cs`：新增 `TileRoad / TileForest / DeepWater / WaterRipple / BridgeDeck / Timber / SlopeSide / CliffSide / TreeCanopy`，`TilePlayable` 改偏草绿。
- `src/godot/scripts/GameRoot.cs`：`_board.Build(_session.World.Board(), …)` ×3；两处 `TryPick` 传 `_board.LevelOf`；新增 `-- --pick-check`（第 2 帧对视图模型里每个可落子格 `Camera.UnprojectPosition(CenterOf)` → `TryPick` 回同格，打印 `[pick-check]` 一行并以 0 / 1 退出）。
- `src/godot/scripts/Hud.cs`：盘面层面板文案改为"平地上两种读法点亮同一批空格；崖壁、栅栏、深水与林地会让两者不同"，并列出 `Diff`："被覆盖但不是气：D4（崖壁）…" / "是气但无人覆盖：B5（林地）"。两处布局挪动（13×13 + 60° 相机下棋盘在屏幕上占得更高更宽，四边标注贴到了 HUD 上）：插旗提示面板由顶部居中（y 100–206，正压远边列标注 y≈186）挪到左列 x 14–392 / y 92–198（信息层面板的位置，插旗时它隐藏）；底部通知条由 y 772–796（压近边标注 y≈794）挪到顶部顺序条之下 y 66–90。
- `art/terrain-v3/README.md`（新）：渲染约定表 + 18 项人工检查清单 + 截图 / 自检命令；`terrain-v3-opening.png`（种子 12345、不带 auto-demo、第 14 帧、插旗阶段：无棋子无信息层）、`terrain-v3-endgame.png`（种子 12345、auto-demo 第 90 帧终局）。
- 测试：`tests/Siege.Core.Tests/TacticalLayers/盘面层的读法切换Tests.cs`（改写 + 新增，见下）、`盘面层视图模型带地形Tests.cs`（新，真 v3 图：B2 h=2 草地出生区 0、E2 h=1、G7 h=0、D4 未架桥深水 = 障碍、G4 桥 = 可落子、A13 岩石地表草地、E5 林地、C8 土路、高度恰 {0,1,2}；栅栏四段按 Coord 序、端点归一化）。

**层高常量与拾取算法**：`LayerHeight = 0.35` 格宽——Δh=2 崖壁侧面 0.70、Δh=1 缓坡 0.35，配两条 / 一条色带。拾取：射线依次与 h=2 / h=1 / h=0 三层平面求交（相机在上方，越高的平面越先被击中 = 最近命中优先）；每层交点经 `TryFromWorld` 取格，若该格可落子且高度恰等于该层即命中，否则（盘外 / 高度不等）继续下一层。相机俯角从 45° 抬到 60° 的原因：`--pick-check` 首跑抓到 B5(h0) → B4——B5 紧贴 B4 高台正后方，45° 下 h=2 顶面向远处投 0.70 / tan 45° = 0.70 格遮挡 > 半格，B5 格心被崖壁挡住（几何事实，不是算法错）；格心可见的判据是 `(TopYOf(2) − TopYOf(0)) / tan θ < 0.5` 即 θ > 54.5°，取 60°（遮挡 0.40 格），崖壁侧面仍有 cos 60° = 0.5 的投影高度。**限定**：60° 只是相机对注视点（盘心附近）的俯角，射线仰角随行数变化——B5 / B4 在第 4–5 行（z ≈ +2），射线约 60°，所以过；远半盘（第 10–13 行）射线只有 45–50°，遮挡区 0.70 / tan 46° ≈ 0.68 格 > 半格。v3 恰好没有"h=0 可落子格紧贴 h=2 格正后方"落在远半盘的情形（远半盘高台身后是岩石 / 深水 / 盘外），`--pick-check` 105/105 不能推出远半盘也安全；换图或调相机时以 `--pick-check` 为准。远边列标注按远半盘的 46° 算遮挡：0.45 + 0.76 / tan 46° ≈ 1.2 格，所以远边单独取 `FarLabelMargin = 1.7`，近边与左右（遮挡只有侧向分量约 0.3 格）取 1.2。

**截图**：`art/terrain-v3/terrain-v3-opening.png`、`art/terrain-v3/terrain-v3-endgame.png`。实现方按派发要求未读图片；只做了纯 Python 像素抽样（不进上下文）：开局图水蓝 2.09% / 草林绿 3.75% / 暖色（土路 / 木 / 缓坡 / 出生区提示）11.0% / 灰（岩 / 崖壁）5.3%——第一版水蓝为 0%，查出水面顶 0.03 被底座顶面 0.04 埋住，改底座下沉后露出。另做了投影探针：用相同相机参数把四边标注锚点投到截图（Label3D 无光照，`CoordinateLabel` 色精确），锚点 ±30 px 框内数标注色像素——开局图四边每个字母 / 数字都有像素（远边 A–N 最少 74、近边最少 98、左右最少 78，近边 / 远边逐字：近 [155 192 116 173 147 117 162 167 99 153 98 231 189]、远 [101 124 77 116 82 74 114 113 81 94 74 150 157]）；第一版远边 13 个字母全为 0，查出是插旗提示面板盖住（不是地砖遮挡），挪面板后恢复。终局图左右第 5–9 行的数字被居中的结算面板盖住，是终局画面的既有布局，与地形无关。人工清单 18 项待主会话核。

**既有测试改写逐条**

| 测试 | 改法 |
|---|---|
| `盘面层的读法切换Tests.两种读法点亮同一批空格` | 改名 `平地上两种读法点亮同一批空格`（规格 MODIFIED Scenario 同名）。9×9 夹具全平地，原两条 `Assert.Empty(covered.Except(liberties))` / 反向断言**原样保留**——它们现在钉的是 D-G 的"平地区域恒等"；加 `ownership.Diff.IsEmpty` 与两读法 `Diff` 的 `Dump` 相等。"全盘恒等"的前提只删注释与 Hud 文案，没有测试断言被删除（旧断言在平地上仍是规格要求） |
| `盘面层的读法切换Tests` 新增 | `差集可由地形解释`：9×9 夹具 `with { TerrainData }` + `CreateUnvalidated`，D5 h=2 / D6 h=1 / E3 深水 / B5 林地 / F6–G6 栅栏，四家各一子；测试内独立算差集 = {D4 E4 C5 E5 G6} 与 {B5}，视图模型逐格原因 = 崖壁 ×3 / 隔岸 / 栅栏 / 林地，D6 缓坡两集合都有 |
| `盘面层视图模型带地形Tests` 新增 ×2 | 见上 |
| `默认棋盘上的未发现信物Tests.默认棋盘不显示分区` | 未改：B2 / E5 的 `Dump` 比较在 9×9 平地上新增三字段相等 |
| `坐标映射在Godot侧唯一` / `棋盘坐标标注Tests` / `Godot层不含规则计算Tests` | 未改，全绿：BoardGeometry 换算计数不变；`ColumnLabelAnchor` 仍在 `RowLabelAnchor` 之前；Godot 侧无 `.Neighbors(` 等 token（A-8 未触发，未收窄） |

**变异验证**（`mutate.py`：`cp` 带时间戳备份 → 变异 → 全量 build+test 或 Godot 重建 + `--pick-check` → `finally` 还原 → `filecmp` 逐字节比对，3 条全部 True）

| 编号 | 变异（文件 / 改动） | 红 | 红掉的测试 / 检查 |
|---|---|---|---|
| M-C1 | `TacticalLayers.ReasonFor` 去掉"来源不相邻 → 隔岸"分支 | 1 | `盘面层的读法切换Tests.差集可由地形解释`（E4 抛"既无栅栏也非崖壁"） |
| M-C2 | `CoverageMap.Compute` 记录来源时 `Adjacent` 写死 `true` | 1 | 同上（证明 Presentation 确实靠 Core 的相邻位，不是自己算） |
| M-C3 | `BoardGeometry.TryPick` 只取 h=0 平面（`for (int level = 0; …)`） | 60 | `--pick-check` 退出码 1：全部 52 个 h=2 与 8 个 h=1 可落子格"未命中"，105 → 45 |

**常规决定（自定，附理由）**

1. Core 只加 `SourcesOf`（Adjacent 一位），不动 `Adjacency` / `GameBoard`：顾问复核后的最小方案；差集原因的其余判断全是 `MapData` 查表。
2. 差集挂在两个盘面层 content 上而不是第三种 content：两种读法的面板都要列同一份差集，且 D-G 说的是"同一层的两种读法"。
3. 拾取只返回可落子格（`levelOf` 为 `null` 的岩石 / 未架桥深水不命中）：派发口径"该格高度等于该层的可落子格"；插旗与落子都不需要点到不可落子格。
4. 桥用四根角柱不用两侧栏杆：视图模型不带水流方向，推方向就是 Godot 自算邻接。
5. 林地小树放地砖角、树冠半径 0.09：落在棋子底座（0.36）之外，不遮落点 / 气点 / 着色。
6. `--pick-check` 放第 2 帧而不是 `_Ready`：视口尺寸在 `_Ready` 时可能未定；headless 下 `UnprojectPosition` 可用（实测 105/105），不必开窗。
7. 开局截图不带 `--auto-demo`：自动演示每帧推进一步，其中 4/7 的帧按着某个信息层（场景降饱和），截到的开局图会发灰；插旗阶段的画面无棋子、无信息层，地形最清楚。
8. B-4 复核：Godot / Presentation 没有按出生区编号推方位或颜色——`MatchSession.ZoneOwners` 按玩家实际选择填表，颜色走 `FactionTable.For(PlayerId)`；auto-demo `ChooseZone(0)` 在 v2 / v3 都是左下（v3 编号 0 仍是左下，只是 1–3 改为旋转序）。
9. 变异脚本用 `cp` 备份 + `filecmp`，不用 `git checkout --`；M-C3 还原后重建 Godot 程序集（headless 运行不自动重建，首次复跑时踩过：改了源码没 `--build-solutions`，结果与改前一样）。

**待决（给主会话）**

- **C-1（人工核图）**：两张截图实现方未目视，只有像素抽样证据；请按 `art/terrain-v3/README.md` 18 项清单核，重点第 2（崖壁 / 缓坡色带）、10（远边字母不被高台挡）、14（B5 光标不跳到 B4）。
- **C-2（相机角度）**：45° → 60° 是为拾取准确性。判据 `(2·LayerHeight) / tan θ < 0.5` 里的 θ 是**该格的射线仰角**而非相机标称俯角（远半盘比标称低 10–15°）。若嫌崖壁侧面不够立体想回 50–55°，`LayerHeight` 须同步降（55° 标称时远半盘约 42°，需 ≤ 0.22），且每次调整都以 `--pick-check` 为准；v3 上目前远半盘没有紧贴崖壁的低地可落子格，才没触发。
- **C-3（Core 增量）**：`CoverageMap.SourcesOf` 是本段唯一 Core 改动，只读、不改既有语义；`.trellis/spec/core/boundaries.md` 段 D 补条时可把"覆盖来源（含相邻位）由覆盖表给出，表现层不算邻接"一并写进去。
- **C-4（Sim 不动）**：Sim `play` 视图仍只区分水 / 岩石（B-5 保留），本段范围是图形版。
- **C-5（拾取边界）**：`--pick-check` 只验格心往返；高台边缘的非格心点击按"最近命中"处理，未做逐像素验证。
- **C-6（5.2 口径，写提交信息用）**：派发写"旧断言删除并列入提交信息"，实际处理是**改名不删**：`两种读法点亮同一批空格` → `平地上两种读法点亮同一批空格`，两条 `Except` 断言原样保留（规格 MODIFIED Scenario 要求平地恒等），删掉的只是"全盘恒等"的注释与 Hud 文案；差集口径由新测试 `差集可由地形解释` 承担。
- **C-7（HUD 布局）**：插旗提示与通知条的挪动是为不压标注（6.3），属 Godot 层布局；若主会话认为通知条在顶部不合阅读习惯，可改回底部但要把近边标注一起下移（需加大相机距离），不要只挪一头。
- **A-8**：`Godot层不含规则计算Tests` 的 `.Neighbors(` token 本段未被触发，未收窄。

#### check 修订（段 C，trellis-check）

**验证**：`dotnet build siege.sln` 退出码 0（0 警告）；`dotnet test siege.sln` 退出码 0，726/726；Godot `--build-solutions` 0、`-- --auto-demo` 0（种子 20260915 第 4 大回合上限，名次照常）、`-- --auto-demo --pick-check` 0（105/105）。codegraph MCP 本会话不可用，理解代码只读 `git diff HEAD` 涉及文件与定点 grep。

**修订**

1. `src/godot/scripts/BoardGeometry.cs` `LayerHeight` 文档注释仍写"约 45° 的固定对局相机"，与相机改 60° 不符 → 改为 60° 并注明崖壁侧面投影 cos 60° = 0.5。
2. `tests/.../盘面层的读法切换Tests.差集可由地形解释`：`CoverageMap.SourcesOf` 此前只被间接覆盖（经 ReadingDiff），补 4 条直接断言——E4 来源 `(E2, Adjacent:false)`、C5 `(D5, true)`、G6 `(F6, true)`、B5 无来源；并把 M-C4 记进测试注释。
3. `src/godot/scripts/Hud.cs` `DiffText` 参数类型去掉多余的全限定 `System.Collections.Immutable.`（文件已 using）。
4. auto-demo 证据复核：`all = Enum.GetValues<TacticalLayer>()` 含 `Board`，`Hud.RefreshLayerPanel` 对活动层调 `world.Layer(active, reading)`，所以 headless auto-demo 确实在 v3 上算过 `ReadingDiff`。
5. 给 C-1 人工核图的提醒：`BoardView.AddTileStack` 的色带颜色按**层序号**取（第 1 层土色、第 2 层岩灰），不是按 Δh——h=1→h=2 的缓坡露出的是一条岩灰带，h=0→h=1 的缓坡是一条土色带。"崖壁 / 缓坡可分"靠**色带条数**（2 vs 1）成立，README 第 2 项核图时应数条数而不是认颜色，否则会把 h=1→h=2 的缓坡误读成崖壁。

**变异验证（check 阶段，`cp` 备份 → 变异 → 跑 → 还原 → `cmp` 逐字节一致，4 条全部一致）**

| 编号 | 变异 | 红 | 红掉的测试 / 检查 |
|---|---|---|---|
| G1（派发要求） | `BoardView.Build` 加一行 `_ = board.LibertyNeighbors(default);` | 1 | `Godot层不含规则计算Tests.Godot层不调用规则计算入口`（token `.LibertyNeighbors(`；守门表仍有效） |
| M-C4 | `TacticalLayers.ReadingDiff` 去掉"是气但无人覆盖 → 林地"分支（条件改 `false`） | 1 | `盘面层的读法切换Tests.差集可由地形解释`（B5 抛"不是林地"）。注：条件改 `true` 是等价变异——Core 不变量下"是气且无人覆盖"只能是林地，测试无法也不必区分 |
| M-C5 | `BoardGeometry.Center(coord,w,h,level)` 高度写成 `TopY`（忽略层数） | 26 | `--pick-check` 退出码 1：105 → 79，失败全在远半盘第 10–13 行的 h=2 格（`A11(h2) → A10` 之类，或"未命中"）；近半盘 h=2 格因 h=2 平面交点只偏 0.40 格仍落在同格而漏过——与"射线仰角随行数变化"的分析一致，说明 `--pick-check` 对格心高度错误只在远半盘敏感 |
| M-C6 | `BoardView.BuildCoordinateLabels` 列字母写死 `"ABCDEFGHJKLMN"[x]` | 2 | `坐标记法Tests.映射实现只有一处`、`棋盘坐标标注Tests.表现层不得自带跳过I的列字母表` |

**C-5 结论（读代码，未逐像素）**：`TryPick` 用真实鼠标射线依次交 h=2 / h=1 / h=0 平面并要求"命中格高度 == 该层"。对屏幕上显示的是**顶面**的像素，这与渲染遮挡精确一致（高台顶面挡住身后低地的区域 = 射线在高层平面的交点正好落进高台格），不会把高台边缘点到低处格。对显示的是**侧面**（崖壁 / 缓坡）的像素：高层交点落在前方低格（层不等），h=0 交点落在高格脚印内（层不等）→ 返回 false，是"死区"而非误拾。唯一例外是朝 ±X 的侧面靠远角处：射线穿过侧面后可能出高格远边落到身后的格并命中它——极窄的一条，不改。

**边界扫描**：`src/godot/` 无 `HeightAt / SurfaceAt / HasFence / Neighbors / AreAdjacent / CoverageTargets / TerrainData / Math.Abs(`；`AddFence` 的 `fence.A.X == fence.B.X`（栅栏朝向）与 `Math.Max(LevelOf(A), LevelOf(B))`（栅栏高度）是渲染用途，判定可接受。`Siege.Presentation` 无 `Godot.*`；`ReadingDiff` 只做 `MapData` 查表 + `SourcesOf` 相邻位。`LibertySnapshot.Compute` 走 `board.AllGroups()`、`CoverageMap` 不过滤玩家状态（D7）→ 弃赛者遗留棋子两侧都计入，`ReadingDiff` 的抛出路径不会因弃赛触发；auto-demo 在 v3 上按过盘面层且退出码 0。

**新增待决（给主会话）**

- **C-8（设计级，不改）**：`TacticalLayers.ReasonFor` 里的崖壁判据 `HeightAt(source) - HeightAt(target) >= 2` 与 Core `Adjacency` 的 `Math.Abs(Δh) <= 1` / `HeightAt(target) - sourceHeight <= 1` 是同一常量的两份字面量（Core 无具名常量）。Core 若改阈值，Presentation 会先抛 `InvalidOperationException`（响亮失败，不静默），但仍是第二份；段 D 补 `boundaries.md` 时可裁决：要么 Core 给 `TerrainData.CliffDrop = 2` 常量供两侧共用，要么把差集原因整体下沉到 Core（`CoverageSource` 直接带原因）。

#### 主会话核实（段 C）

- `dotnet build` EXIT=0 零警告；`dotnet test` EXIT=0，726/726；Godot `--build-solutions` EXIT=0；`--auto-demo --pick-check` EXIT=0，105/105。
- 截图人工核（开局 / 终局两张）：四角高台抬起且侧面可见、h=1 缓坡台阶、环河 + 四桥、岛上林地、栅栏立在格边、岩石灰块；13 列 `A–N` 四边正向可读、无 `I`；面板不压标注。通过。
- 自做变异 M-主-3：`LayerContents.ReasonFor` 栅栏分支短路为 false → `差集可由地形解释` 红，还原逐字节一致。
- 裁决：C-2 相机 60° 保留，调相机以 `--pick-check` 为门；C-3 / C-8 段 D 处理——Core 出具名常量 `TerrainData.CliffDrop = 2`，`Adjacency` 与 Presentation 共用，去掉第二份字面量；C-4 Sim 不动；C-5 结论接受不改；C-6 按"改名 + 原断言保留 + 加 Diff 为空"口径；C-7 无动作；C-9 README 第 2 项按色带条数核。check 修订 3 条接受。

### 8.1 变异验证汇总

口径统一：`cp` 备份 → 变异 → 全量 `dotnet build` + `dotnet test`（Godot 侧为重建 + `--pick-check`）→ 还原 → 逐字节比对（`filecmp` / `cmp`）。段 A 与段 B 的 check 变异都编号 N-x，此处加段前缀消歧。红数取各段记录当时的值（默认值未变，未重跑）。

| 编号 | 段 | 变异 | 红 | 红掉的测试 / 命令 | 还原 |
|---|---|---|---|---|---|
| M-A1 | A | `Adjacency.LibertyNeighbors` 去掉 `HasFence`（气边忘判栅栏） | 2 | `气边Tests.栅栏切断气与连接`、`覆盖关系Tests.栅栏不挡覆盖` | 一致 |
| M-A2 | A | `Adjacency.Receives` `h_t − h_s ≤ 1` 写成 `\|Δh\| ≤ 1`（覆盖对称化） | 3 | `覆盖关系Tests.居高临下`、`覆盖不等于气`、`棋子向四邻接相邻格提供覆盖Tests.覆盖判定与气边判定分离` | 一致 |
| M-A3 | A | `CoverageTargets` 对岸仍是深水时再走一格（穿两格水） | 1 | `覆盖关系Tests.宽河不可隔岸` | 一致 |
| M-A4 | A | `MapData.IsPlayable` 忽略桥（桥格仍拒落子） | 4 | `格属性Tests.桥格可落子`、`气边Tests.桥恢复气`、`不可落子格没有气边`、`覆盖关系Tests.隔岸的桥格可被覆盖` | 一致 |
| M-A5 | A | `CoverageMap.Compute` 偷调 `Adjacency.Neighbors` | 1 | `四邻接Tests.几何邻居枚举只在允许名单内直接调用` | 一致 |
| M-A6 | A | `Receives` 去掉林地判断 | 4 | `覆盖关系Tests.林地不接收覆盖`、`隔岸不能跨崖`、`唯一覆盖查询Tests.林地信物只能占据`、`信物内容在首次被覆盖时永久公开Tests.占据即揭示` | 一致 |
| M-A7 | A | `CoverageTargets` 遇深水直接不覆盖（取消隔岸一跳） | 2 | `覆盖关系Tests.隔岸覆盖`、`隔岸的桥格可被覆盖` | 一致 |
| A/N-1 | A check | `Receives` 去掉 `h_t − h_s ≤ 1`（覆盖不看高度） | 2 | `覆盖关系Tests.仰视不覆盖`、`隔岸不能跨崖` | 一致 |
| A/N-2 | A check | `LibertyNeighbors` `\|Δh\| ≤ 1` 写成 `≤ 2`（崖壁不断气） | 6 | `四邻接Tests.几何相邻但无气边`、`棋串构成Tests.崖壁两侧不成串`、`气的计算Tests.靠崖壁的棋串`、`气边Tests.崖壁切断气`、`覆盖关系Tests.覆盖不等于气`、`覆盖判定与气边判定分离` | 一致 |
| A/N-3 | A check | `CoverageTargets` 加 `HasFence` 跳过（栅栏挡覆盖） | 1 | `覆盖关系Tests.栅栏不挡覆盖` | 一致 |
| A/N-4 | A check | `GroupSafety` 改回 `GameBoard.Neighbors` + 手写障碍判墙 | 修订前 0 → 修订后 1 | `四邻接Tests.几何邻居枚举只在允许名单内直接调用`（守门补第二段扫描后才红） | 一致 |
| A/N-5 | A check | `TerrainData` 构造器去掉缺省项归一化 | 1 | `地图文件健壮性Tests.地形字段往返逐字段相等` | 一致 |
| M-主-1 | A 主会话 | 隔岸分支 `Receives(map, u, h)` 换成 `map.IsPlayable(u)`（对岸忽略高度与林地） | 1 → 补测后 2 | `覆盖关系Tests.隔岸不能跨崖` + 新增 `隔岸对岸为林地不覆盖` | 一致 |
| M-B1 | B | `MapSymmetry.Rotate90` 改成 180° 旋转（C4 只查 180°） | 2 | `基准地图对称性Tests.只满足D2的图被判不对称`、`四人基准地图Tests.旋转对称` | 一致 |
| M-B2 | B | `MapValidator.MultiSourceDistances` 改走 `Adjacency.Neighbors` + `IsPlayable`（距离走几何路径） | 3 | `地图静态校验规则Tests.距离沿气边计算`、`出生区被孤立`、`四邻接Tests.几何邻居枚举只在允许名单内直接调用` | 一致 |
| M-B3 | B | `PieceEffects.Step` 改回 `board.Neighbors(c)`（连珠改回几何邻居） | 3 | `连珠子的位置加值Tests.崖壁截断连珠线`、`栅栏截断连珠线`、`几何邻居枚举只在允许名单内直接调用` | 一致 |
| M-B4 | B | `Budgets[4]` 区间写反 `(110, 95)` | 35 | `地图基准校验通过`、`磁盘上的基准地图文件与代码一致`、`可落子格越界`、`格数超出预算` 及全部经 `GameBoard.Load` 的跑局 / 遥测测试 | 一致 |
| M-B5 | B | 去掉 `ValidateBirthZoneConnectivity` 调用 | 2 | `出生区被孤立`、`地标不可达被报出` | 一致 |
| M-B6 | B | 去掉 `ValidateTerrainBounds` 调用 | 1 | `地形数据越界被报出` | 一致 |
| B/N-1 | B check | `RotationDefects` 删栅栏比对 | 1 | `基准地图对称性Tests.每项属性都参与旋转比对` | 一致 |
| B/N-2 | B check | `ContestedStandardSeeds = []`（信物 13 → 9） | 35 | `信物格分布`、`地图基准校验通过`、`磁盘一致`、`距离精确相等` 及全部跑局测试 | 一致 |
| B/N-3 | B check | `RampSeeds` 高度 1 → 2（高台没有下坡） | 40 | `可落子格规模`、`地形要素齐全`、`地图基准校验通过`（BIRTH_ZONE_ISOLATED）、连通 / 口袋及跑局测试 | 一致 |
| B/N-4 | B check | 生成器 `heights[G7] = 1`（中央入口抬高） | 修前 1 → 修后 2 | `磁盘一致` + 补断言后 `四人基准地图Tests.地形要素齐全` | 一致 |
| M-主-2 | B 主会话 | `FenceSeeds` 多加 `("G5","G6")` | 6+ | MapDefinition：基准校验、往返、距离失衡、出生区归属…… | 一致 |
| M-C1 | C | `TacticalLayers.ReasonFor` 去掉"不相邻 → 隔岸"分支 | 1 | `盘面层的读法切换Tests.差集可由地形解释` | 一致 |
| M-C2 | C | `CoverageMap.Compute` 来源 `Adjacent` 写死 `true` | 1 | 同上（证明表现层靠 Core 的相邻位） | 一致 |
| M-C3 | C | `BoardGeometry.TryPick` 只取 h=0 平面 | 60 | `--pick-check` 退出码 1（105 → 45） | 一致 |
| G1 | C check | `BoardView.Build` 加 `_ = board.LibertyNeighbors(default)` | 1 | `Godot层不含规则计算Tests.Godot层不调用规则计算入口` | 一致 |
| M-C4 | C check | `ReadingDiff` 去掉"是气但无人覆盖 → 林地"分支 | 1 | `差集可由地形解释`（B5 抛"不是林地"） | 一致 |
| M-C5 | C check | `BoardGeometry.Center(…, level)` 忽略层数 | 26 | `--pick-check` 退出码 1（105 → 79，全在远半盘 h=2 格） | 一致 |
| M-C6 | C check | `BuildCoordinateLabels` 列字母写死 `"ABCDEFGHJKLMN"` | 2 | `坐标记法Tests.映射实现只有一处`、`棋盘坐标标注Tests.表现层不得自带跳过I的列字母表` | 一致 |
| M-主-3 | C 主会话 | `ReasonFor` 栅栏分支短路为 false | 1 | `差集可由地形解释` | 一致 |
| M-D1 | D | `LayerContents.ReasonFor` 改回 `>= 2`（表现层第二份阈值字面量） | 1 | `崖壁阈值唯一Tests.高度差与裸数字比较只允许经CliffDrop` | 一致 |
| M-D2 | D | `Adjacency.LibertyNeighbors` 改回 `<= 1`（Core 第二份阈值字面量，数值等价） | 1 | 同上；其余 728 条全绿——数值等价的变异只有源码扫描抓得住，IL 里 `const` 已内联 | 一致 |
| M-D3 | D | `RelicGenerationOptions.StandardUpgradePermille` 150 → 300 | 4 | `公共争夺区信物权重与高阶升级Tests.v3基准图上公共信物升级率落在宽口径`（27% 越 21% 上界）、`高阶比例`、`高档升级率严格高于标准档`、`区域强度预算Tests.高风险区域算更高` | 一致 |

**8.1 原列六项对照**

| 8.1 原列 | 已覆盖于 | 备注 |
|---|---|---|
| 气边忘判栅栏 | M-A1 | 另 A/N-3 钉住反向（栅栏不得挡覆盖） |
| 覆盖关系写成对称 | M-A2 | 另 A/N-1 钉住"覆盖不看高度"、M-主-1 钉住隔岸分支的高度 / 林地条件 |
| 隔岸覆盖穿两格水仍通过 | M-A3 | 另 M-A7 钉住"隔岸一跳不能取消" |
| 桥格仍被当深水拒落子 | M-A4 | |
| C4 校验器只查 180° | M-B1 | 另 B/N-1 钉住栅栏参与旋转比对 |
| 距离校验仍走几何路径 | M-B2 | 出生区连通校验复用同一 `MultiSourceDistances`，同一变异同时覆盖 |
| （本段追加）崖壁阈值第二份字面量 | M-D1、M-D2 | 7.5 守门；反面断言把改动前三行原文写进测试证明判据命中 |
| （本段追加）v3 基准图统计口径 | M-D3 | 7.4；`Assert.Equal(50000, total)` 先钉 4+1 分布，宽口径不吞轨道缺失 |

### 段 D（7.2 + 7.4 + 7.5 + 8.1；7.3 与 8.2 由主会话执行）

**结果**：`dotnet build siege.sln` 退出码 0、0 警告；`dotnet test siege.sln` 退出码 0，729/729（段 C 末 726 + 新增 3：`崖壁阈值唯一Tests` ×2、`v3基准图上公共信物升级率落在宽口径`）。`openspec/`、`.trellis/spec/`、`src/godot/` 零改动。

**7.2 设计文档 `2026-09-10-siege-core-gameplay-design-v1.md`（v1.1 → v1.2）改动节清单**（定点替换，`git diff --stat` 74+ / 28−，§16 未动）

| 节 | 改动 |
|---|---|
| 版本头（第 3 行） | v1.1 → v1.2（隐含项：不改则与变更记录矛盾，cap-multiplier 当年同样改过这一行） |
| §2 术语 | 新增"格属性 / 边属性""气边""覆盖关系"三条；"气"改为沿气边；同节一致性顺带："棋串"由"上下左右相连"改"经气边相连"、"覆盖"由"对相邻地块"改"按覆盖关系" |
| §3.1 | 整节改写：四项格属性 + 栅栏边 + 可落子格定义 + 两套导出关系的规则文本 + 一句话记法；新增"标准算例"表 12 行，坐标 / 期望逐字取自 `terrain` 规格（`F6`/`F7`、`F6`–`G6`、`F6`→`G6`→`H6`、`J6`、气数 3 / 4、居高临下 / 仰视、林地、靠崖壁的棋串） |
| §3.2 | 表 4 人 80–95 → 95–110；区间说明补 terrain-model 放宽理由；对称改 C4 旋转、不再要求镜像；距离补"沿气边、容差 1"；"障碍占 25%–35%"段删除，改为密度由可落子格区间把控 + 口袋沿气边 + 出生区连通 + 桥 / 栅栏合法性 |
| §3.3 | 整节改写为 v3：13×13、可落子 105（45/8/52）、四家 13 格 h=2 高台 + 2 格缓坡、中央环河岛 21 格、四桥即咽喉、信物 13 = 8 + 4 + 1、C4、沿气边距离 5/7/4、极差 0；v1 / v2 写成历史存档（越区间且缺地形字段）；文本图从段 B 记录逐字复制；末段"墙"的论述扩到崖壁 / 深水 / 栅栏并注明对局变长为预期 |
| §6.1 第 1 条 | 可落子补"非障碍、非未架桥深水" |
| §7.1 第 3 条 | 覆盖改按覆盖关系（林地不接收、只能居高临下、隔一格水、栅栏不挡；不斜向、不穿障碍） |
| §7.3 第 2 条 | 揭示补"或首次被任意玩家的棋子占据"，注明林地信物只能靠占据 |
| §9.2 连珠子行 | "连续"改为相邻两枚之间存在气边，隔崖壁 / 未架桥深水 / 栅栏不成线 |
| §14.2 | 删"恒等"表述，改为 terrain-model 前恒等 → 现在平地恒等 + 差集可由地形解释（崖壁 / 隔岸 / 栅栏 → 被覆盖非气；林地 → 是气未覆盖），盘面层列出差集与原因 |
| §20 | 地形要素（三档高度层高 / 崖壁侧面、深水、桥、栅栏格边、林地）改为承载规则的可判读要素，纯装饰不遮挡判读；新增坐标标注一条（唯一映射推出、列数随宽度、跳过 `I`、外圈 h=0 平面） |
| 变更记录 | 追加 `2026-09-16 \| change terrain-model（v1.1 → v1.2）` 一行：改动节、依据（31 条裁决 + A-6 / B-1 / B-2 / C-8）、"计分不动，§16 不动，13×13 下对局变长为预期" |

未写：§16 数值目标（等 8.2）；据点 / 匠人（②③轮）只在变更记录里点名另开 change。

**7.5 常量落点**

- `src/Siege.Core/Board/TerrainData.cs`：`public const int CliffDrop = 2;`（紧邻 `MaxHeight`，注释写明裁决 D2 语义、两处消费点与"与 `MaxHeight` 同值只是三层下的巧合"）。命名取 `CliffDrop`（崖壁落差）：它是"差达到多少算崖壁"的阈值，不是最大可通行差（那是 `CliffDrop − 1`），用 `<` / `>=` 与它比较最不容易写错方向。
- `src/Siege.Core/Board/Adjacency.cs`：`LibertyNeighbors` `Math.Abs(Δh) <= 1` → `< TerrainData.CliffDrop`；`Receives` `h_t − sourceHeight <= 1` → `< TerrainData.CliffDrop`；两处文档注释同步。
- `src/Siege.Presentation/Layers/LayerContents.cs`：`ReasonFor` `>= 2` → `>= TerrainData.CliffDrop`。
- 全仓复核：`src/` 内含 `HeightAt` 的差值与裸数字比较为 0 处（守门测试扫描结果）。

**新测试**

- `tests/Siege.Core.Tests/Terrain/崖壁阈值唯一Tests.cs`：`崖壁阈值取自裁决D2`（`CliffDrop == 2`）；`高度差与裸数字比较只允许经CliffDrop`——源码扫描 `src/` 全树（含 `src/godot/scripts`，排 bin/obj；下界 90 文件 / 500 KB，实测 110 / 786 KB），判据两条正则（`HeightAt(…)` 在减号左 / 右侧、与 `<= >= < >` 后接数字），运算符不含 `==` / `!=`；反面断言：改动前三行原文逐条命中判据；正面断言：`Adjacency.cs` 含 `TerrainData.CliffDrop` ≥ 2 次、`LayerContents.cs` ≥ 1 次。只能做源码扫描的理由写在类注释：`const` 编译期内联，IL 里与裸 `2` 不可区分。
- `tests/Siege.Core.Tests/RelicGeneration/公共争夺区信物权重与高阶升级Tests.cs`：新增 `v3基准图上公共信物升级率落在宽口径`——`FourPlayerBaseMap.Create()` 上 10000 种子 × 5 公共格，先 `Assert.Equal(50000, total)` 钉 4 + 1 分布，再 `InRange(升级‰, 150, 210)`（均值 (4×150 + 300) / 5 = 180‰）。原两条合成图测试与期望值不动。

**变异记录**：见上表 M-D1～M-D3（三条还原逐字节一致；脚本 `mutate_d.py` 用 `cp` 备份 + `filecmp`）。M-D2 是本段守门存在的理由：改回 `<= 1` 数值完全等价，其余 728 条全绿。

**常规决定（自定，附理由）**

1. 7.4 的变异不取 "High 300 → 150"：那样均值恰为 15.0% 压在下界，抽样噪声下红绿不定，不能作证；取 "Standard 150 → 300"（27%）。
2. §2 顺带改"棋串""覆盖"两条定义：任务只点名"气"，但同节里"上下左右相连"与新 §3.1 直接矛盾，不改则术语表自相冲突。
3. §3.3 收入段 B 待决 B-3 的两条盘面观察（`J2` 单向覆盖展示点、四区各约 20 格自留地 / 接触面只在岛上）作为图的描述，因为它们是 v3 设计意图（E5 / D-E）在图上的落点；不含任何数值目标或调参建议。
4. 守门判据只抓 `HeightAt`，且要求 `HeightAt` 与比较符之间有减号、逐行匹配——`HeightAt(t) <= sourceHeight + 1`、`HeightAt(a) >= HeightAt(b) + 2` 这类加法形与跨行拆写不在判据内（任务点名的形状是"差值与裸数字比较"，记下盲区供下次改阈值写法时复核）。Godot 侧 `LevelOf` 是渲染层高（`Math.Max(LevelOf(A), LevelOf(B))` 定栅栏高度），不是规则阈值；若第三轮 Godot 侧出现 `LevelOf(a) − LevelOf(b) >= 2` 这类判定，应先视为越界（Godot 不得自判地形），再考虑把 `LevelOf` 加进判据。
5. 变异脚本用 `cp` 备份 + `filecmp` 还原比对，不用 `git checkout --`（工作树有未提交改动）。

**待决（给主会话）**

- **D-1（7.3，主会话写）**：`.trellis/spec/core/boundaries.md` 单一实现表建议补四行——几何四邻（`Adjacency.Neighbors`）、气边（`Adjacency.LibertyNeighbors`）、覆盖关系（`Adjacency.CoverageTargets`）、崖壁阈值（`TerrainData.CliffDrop`，Core 与 Presentation 共用，守门 `崖壁阈值唯一Tests`）；另补"覆盖来源（含相邻位）由 `CoverageMap.SourcesOf` 只读给出，表现层不算邻接"（C-3）。`coordinates.md` 补列字母随宽度（A-8 / B-5）。
- **D-2（设计文档口径）**：§3.3 是否保留 B-3 的两条盘面观察（常规决定 3）；若认为属基线归因不该进设计文档，删两行即可，不影响其他节。
- **D-3（8.2 提示）**：`FourPlayerBaseMap.Create()` 上 10000 种子的升级率实测落在 [15%, 21%] 内（测试绿），8.2 报告若引用"公共信物高阶比例"，口径是 4 + 1 分布下的 18%，不是 20%。

#### check 修订（trellis-check，2026-09-16）

**结果**：`dotnet build siege.sln` 退出码 0、0 警告；`dotnet test siege.sln` 退出码 0，729/729。`openspec/`、`.trellis/spec/`、`src/godot/` 零改动。

**设计文档逐节核对**（v1.2；`git diff HEAD` 11 个 hunk，无一落在 §16 旧行 437–450；§4–§5、§8、§10–§13、§15–§19 零改动；全文无"据点 / 匠人 / 相对高度"等 ②③ 轮内容，只在变更记录里点名另开 change）

| 节 | 结论 |
|---|---|
| 版本头 / §2 | 一致：三条新术语与 `terrain` 规格的气边 / 覆盖关系条件逐项相同；"棋串""气""覆盖"改沿新关系 |
| §3.1 | 一致：可落子格定义、气边三条件、覆盖三步规则与 `terrain` 规格逐字对应；12 行算例的坐标 / 高度 / 期望逐条对上（其中"靠崖壁的棋串"来自 `board-topology`，表头已补注） |
| §3.2 | 一致：95–110、C4、沿气边距离容差 1、口袋 < 8、出生区连通、桥 / 栅栏合法性、障碍占比取消，与 `map-definition` 规格相同 |
| §3.3 | 数字全部用 `maps/siege-4p-base-v3.json` 独立重算核过：可落子 105（45/8/52）、岩石 36、深水 32 = 4 桥（`G4 D7 K7 G10` = 咽喉）+ 28、栅栏 4、林地 4（`E5 E9 J5 J9`）、土路 16、岛 21 格全 h=0、桥格气边邻格各 2、四区各 13 格全 h=2 + 2 格 h=1 缓坡、信物 13 = 8 + 4 Standard + 1 High（`G7` = 中央入口）、沿气边距离 5 / 7 / 4 四区全等、全盘连通；文本图与段 B 记录逐字相同。**一处不一致已修**（见修订 1） |
| §6.1 / §7.1 / §7.3 / §9.2 / §14.2 | 各只改一句，内容分别与 `coverage-territory`、`relic-control`、`piece-effects`、`tactical-layers` 规格一致，同节其余规则未动 |
| §16 | 一字未动 |
| §20 | 两条改动与 `visual-style-baseline` 规格一致（可判读要素 + 坐标标注），其余五条未动 |
| 变更记录 | 行格式与既有行一致（日期 \| change `x`（版本）\| 内容，含依据与"计分不动 / 对局变长为预期"）；"31 条裁决"指裁决记录编号 1–31（含默认项），与 design.md Context 的"27 条编号裁决"口径不同但不矛盾，未改 |

**修订清单**

1. `2026-09-10-siege-core-gameplay-design-v1.md` §3.3 首段："两者都不在 95–110 区间"是事实错误——v1 的 109 格恰在新区间内。用临时探针（已删）跑 `MapValidator.Validate`：v1 被拒的实际原因是 `BIRTH_ZONE_SIZE_OUT_OF_RANGE`（四区各 15 格越 12–14），v2 是 `PLAYABLE_COUNT_OUT_OF_RANGE`（85）。改为分别陈述两个原因，"不含地形字段"保留。
2. 同文件 §3.3：**D-2 结案——删**"每家高台单向覆盖顺时针下一家走廊尽头的一格（`J2` 轨道）……四家的接触面只在岛上"整条 bullet。它是段 B 待决 B-3 的实现期观察（含"约 20 格自留地"的估算与"基线看……"的归因提示），不是规则也不是地图事实；设计文档只写规则与地图事实，观察归本文件段 B / 8.2。变更记录行未提及此两条，无连带。实现方"常规决定 3"作为历史记录不改写。
3. 同文件 §3.1 算例表头："与 `terrain` 规格及测试一致" → "与 `terrain` / `board-topology` 规格及测试一致"（"靠崖壁的棋串"算例出自 `board-topology`）。
4. 同文件 §3.3："设置 4 个出生区，每区 12–14 格；v2 每区 13 格"改写成"四个出生区各 13 格"时把每区 12–14 格的预算区间丢了（§3.2 表只有出生区数量列，全文再无此区间，而修订 1 的 v1 被拒原因正引用它）。补回为"四个出生区各 13 格（预算每区 12–14 格）"。顺带核过 `MapSymmetry` 高度比对确是 `HeightAt(p) != HeightAt(q)`（第 42 行），非阈值比较。

**代码核对**

- `TerrainData.CliffDrop` 三处共用成立：全仓 `src/`（含 `Siege.Sim`、`src/godot/scripts`）里含 `HeightAt` 的差值比较只有 `Adjacency.cs:71`、`Adjacency.cs:137`、`LayerContents.cs:287` 三处，全部经 `TerrainData.CliffDrop`；`MapValidator` / `MapSymmetry` / `FourPlayerBaseMap` 无高度差阈值比较（校验器走 `LibertyNeighbors`，对称比对是 `!=`）；`Siege.Sim` 只有 `HeightAt(c) == h` 的分组统计；Godot 侧 `LevelOf` / `MaxLevel` 只用于渲染层高与逐层拾取，无阈值判定。`MaxHeight` 的消费点（`MapFile`、`TerrainData` 构造校验、Sim 统计）语义都是"档位上限"，与 `CliffDrop` 不混用。
- `崖壁阈值唯一Tests`：样本下界（≥ 90 文件 / ≥ 500 KB）+ 反面断言（三行原文命中判据）+ 正面断言（两处消费点含常量）齐全；变异 b 证明下界是唯一拦住"扫描目录指空"的东西。
- `v3基准图上公共信物升级率落在宽口径`：trx 实测 2.42 s（< 3 s，不报）；口径 4 Standard + 1 High → 180‰，区间 [150, 210] 自洽。`崖壁阈值唯一Tests` 两条 0.12 s。

**新增变异**（`cp` 备份 → 变异 → 全量 build + test → 还原 → `cmp` 逐字节一致；不与 M-D1～M-D3 重复）

| 编号 | 变异 | 红 | 红掉的测试 | 还原 |
|---|---|---|---|---|
| D/N-a | `Adjacency.LibertyNeighbors` `< TerrainData.CliffDrop` → `<=`（崖壁不断气） | 13 | `四邻接Tests.几何相邻但无气边`、`气边Tests.崖壁切断气`、`棋串构成Tests.崖壁两侧不成串`、`气的计算Tests.靠崖壁的棋串`、`覆盖关系Tests.覆盖不等于气`、`覆盖判定与气边判定分离`、`差集可由地形解释`、`崖壁截断连珠线`、`距离沿气边计算`、`出生区被孤立`、`必死口袋`、`面积恰好等于两眼最小格数的空区不算口袋`、`四人基准地图Tests.可落子格规模`。守门 `崖壁阈值唯一Tests` **不红**（比较符后是标识符不是数字）——守门只管"第二份字面量"，行为归行为测试，两者各管一头 | 一致 |
| D/N-b① | `崖壁阈值唯一Tests` 下界 90 / 500_000 都改 0，扫描目录指向只含一个空 `.cs` 的临时目录 | 0（**假绿**） | 无——正面 / 反面断言按绝对路径读文件不受扫描目录影响，`offenders` 为空即过 | — |
| D/N-b② | 同上只还原两条下界，目录仍指空目录 | 1 | `高度差与裸数字比较只允许经CliffDrop`（"只扫到 1 个源文件"）——证明下界是守门不空转的唯一依赖 | 一致 |
| D/N-c | `Adjacency.Receives` `< TerrainData.CliffDrop` → `<=`（仰视也覆盖） | 2 | `覆盖关系Tests.仰视不覆盖`、`隔岸不能跨崖` | 一致 |

**对待决的意见**

- D-1：附议；建议的四行与本次 grep 到的三处消费点 + `CoverageMap.SourcesOf` 吻合。
- D-2：已按"删"处理（修订 2），结案。
- D-3：附议，8.2 引用口径为 18%（4 + 1 分布），测试区间 [15%, 21%] 与之自洽。

**新增待决**

- **D-4（口径，不阻塞）**：变更记录写"31 条裁决"，design.md Context 写"27 条编号裁决"（D/E/F 27 条 + 默认 1/2/4/5 四条 = 编号 1–31）。若主会话希望两处一致，任选其一改字即可。
- **D-5（守门盲区，记录）**：实现方常规决定 4 已记"加法形 / 跨行拆写不在判据内"；本次另注意判据对 `=>` 后接数字的行不误报只是碰巧（`Program.cs:99` 无减号），下次改判据时把 lambda 箭头排除掉更稳。

#### 主会话核实（段 D）

- `dotnet build` EXIT=0 零警告；`dotnet test` EXIT=0，729/729（check 修订后）。
- 自做变异 M-主-4：`TerrainData.CliffDrop = 2 → 3` → Terrain + TacticalLayers 红 6，还原逐字节一致（证明三处确实共用常量：改一处三处一起变）。
- 7.3 由主会话完成并提交（`1bc2b6a`）：`boundaries.md` 单一实现表五行 + 两节、`coordinates.md` 列字母随宽度 + 两套导出关系 + 高度进 3D、`testing.md` pick-check 口径。D-1 结案。
- 裁决：D-2 按 check 删除结案；D-3 8.2 口径 18%；D-4 不统一——"27 条"指用户逐条裁决的编号项，"31 条"含实施期追加，两处各自成立；D-5 记录，下次改判据时排除 `=>`。check 修订 4 条接受。

### 8.2 200 局回归（v3，Standard，种子 1–200，`sim-out/terrain-v3/`，主会话执行）

口径：13×13 + 领地计分未退役 + 覆盖不对称（四家高台无争议覆盖脚下低地一圈），与 v2 基线（`sim-out/baseline/`，Safety=5）**不可直接对照**，下表只作方向参考。只记录不调参。

| 指标 | v3（200 局） | v2 基线（2000 局） | 备注 |
|---|---|---|---|
| 首次跨出生区冲突 | 第 5.97 大回合（分布 4×38 5×14 6×54 7×48 8×17 9×1） | 4.75 | 变晚 1.2；四家高台自留地 20 格，接触面只在岛上（B-3） |
| 首次冲突时占用率 | 61.0% | 61.9% | 与 denser-map 结论一致：仍是密度驱动 |
| 整局无提子局 | 28 / 200（14.0%） | 1.2% | 8.2 报警线 25% 未触发，但从 1% 跳到 14%——第二轮据点计分后复看 |
| 解禁当回合（第 4）即提子 | 38 / 172 有提子局（22%） | — | 报警线 50% 未触发 |
| 首次提子中位 | 6 | — | 报警线 ≤4 未触发 |
| 终局局平均结束大回合 | 10.57（7×8 8×20 9×38 10×29 11×27 12×22 13×18 14×10 15×8） | 10.47 | 预期"变长"只多 0.1——105 格并未让铺满更慢，因为高台自留地 20 格很快填满、岛 21 格是唯一战场 |
| 不收敛率（达上限 15） | 10.0%（20/200） | 6.4% | 记录 |
| 终局原因 | 整轮 Pass 176 / 达上限 20 / 碾压 4 | 92.8% / 6.4% / 0.9% | 碾压 2.0%，比例微升 |
| 第 3 大回合领先者胜率 | **17.0%**（95% 区间 12.4–22.8） | 50.8% | **显著低于 25% 随机基线**（HANDOFF 已定：显著低于 25% 同样算偏离）。第 3 大回合势力由高台领地主导，与最终胜负脱钩——据点计分（②）会直接改变这条，本轮不动 |
| 出生区胜率 | 28.0 / 25.0 / 25.0 / 22.0%，区间均含 25% | 无显著差异 | C4 对称成立 |
| 自杀手尝试率 / Pass 率 | 0.0% / 15.7% | 0.0% / 14.6% | 达标 |
| 部署上限中位 | 3 / 4 / 5 | 3 / 4 / 5 | 达标 |
| 势力 开局 / 中期 | 22.7 / 93.4 | 21.3 / 93.5 | 达标 |
| 倍增子势力占比 | 54.7%（每颗 18.0） | — | multiplier-rebalance 后口径约 51%，相近 |
| AI 耗时 | 1341 ms / 大回合，14.8 s / 局，200 局墙钟 122 s（28 核） | — | 13×13 后明显变慢，第二轮基线要看 2000 局的墙钟 |
| 信物 | 全部 2600 枚均被揭示；Vanguard 控制者胜率 38.2% 偏高（样本 228） | — | 记录 |

**结论**：三条报警线均未触发，本轮不改规则不调参。值得带进第二轮的三个观察：① 整局无提子局 1% → 14%，四家高台自留地让"不下山也能活到终局"成立；② 第 3 大回合领先者胜率 17%，高台领地在早期撑势力但不决定胜负；③ 对局长度几乎不变——"13×13 会拉长对局"的预期没有成立，主控变量是岛的 21 格而不是总格数。
