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

- [ ] 5.1 盘面层视图模型带出每格高度、地表、桥与栅栏边，供 Godot 渲染；归属读法与棋串读法各按 2.1 / 2.2 取集合。验证：单元测试——平地区域两读法集合相同；有崖壁 / 栅栏 / 一格深水 / 林地时差集中的每一格都能给出地形原因。
- [ ] 5.2 改写 `merge-board-layer` 留下的"两读法恒等"测试为 D-G 口径。验证：新测试绿，旧断言删除并列入提交信息。

## 6. Godot 地形渲染与交互

- [ ] 6.1 `BoardGeometry.Center` 带高度（每层抬升固定层高）；`TryFromWorld` / `TryPick` 支持分层拾取（射线与三层平面求交，取最近命中的可落子格）。验证：Godot 端 headless 测试脚本——对 v3 每个可落子格从相机投影再拾取回同一格。
- [ ] 6.2 地砖按高度堆叠并画崖壁侧面（Δh=2 与 Δh=1 侧面可区分）；深水、桥、林地各有可辨地表；栅栏沿格边立起不占落点。验证：`--screenshot` 出图人工对照 `art/style-exploration/` 基准与用户参考图；检查清单写入 `art/terrain-v3/README.md`。
- [ ] 6.3 坐标标注适配 13 列，锚点放在棋盘外圈 h=0 平面，边缘格高度不同仍可读。验证：截图人工检查 + `A B C D E F G H J K L M N` 无 `I`。
- [ ] 6.4 `--auto-demo` 与 `--seed` 在 v3 上跑通；Godot 层不自己算邻接 / 地形过滤。验证：源码级扫描（`src/godot/` 不在 sln，补 grep 守门）确认只消费视图模型与 `BoardGeometry`。

## 7. 既有测试与文档

- [x] 7.1 改写写死 11×11 / 85 / 36 障碍 / D2 对称 / `A–L` 的测试；几何算例改坐标重构，不改期望值。逐条列入提交信息并说明新坐标等价。
- [ ] 7.2 设计文档 §3.1–3.3 改写为格属性与 v3 描述、§7.1 覆盖改走覆盖关系、§7.3 补"占据信物格同样揭示"、§14.2 两读法差集说明、§20 地形从装饰升级为规则元素；把 `terrain` 规格里的气边 / 覆盖算例（`F6`/`F7` 崖壁、`F6`–`G6` 栅栏、`F6`→`G6`→`H6` 隔岸）带进 §3 作为标准算例，使本 change 的规则类任务算例在归档后来自设计文档；变更记录加一行（v1.1 → v1.2）。验证：人工检查。
- [ ] 7.3 `.trellis/spec/core/boundaries.md` 补"几何四邻 / 气边 / 覆盖关系三个唯一实现点"；`coordinates.md` 补列字母随宽度。验证：人工检查。
- [ ] 7.4 `relic-generation` 在 v3 基准图上补一条宽口径统计断言（升级率 15–21%，裁决 B-7）。验证：单元测试。

## 8. 变异验证与回归

- [ ] 8.1 变异验证并记录：气边忘判栅栏；覆盖关系写成对称；隔岸覆盖穿两格水仍通过；桥格仍被当深水拒落子；C4 校验器只查 180°；距离校验仍走几何路径而非气边。每条变异必须被至少一个测试抓住。
- [ ] 8.2 200 局回归（Standard，种子 1–200）：首次跨出生区冲突大回合、首次提子时占用率、整局无提子局数、平均结束大回合、终局原因分布、第 3 大回合领先者胜率、Pass 率、每步 AI 耗时。**只记录不调参**；报告口径注明两条：①"13×13 + 领地计分，对局变长为预期"；②"覆盖已不对称，四家高台无争议地覆盖脚下低地一圈，领地分与军势的比例与 v2 不可直接对照"。若解禁当回合（第 4 大回合）即发生提子的局占比 > 50%、首次提子中位 ≤ 4、或整局无提子局 > 25%，立即报告，不自行改规则。

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
