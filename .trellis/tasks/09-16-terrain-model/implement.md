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

- [ ] 3.1 `MapValidator`：4 人可落子格区间 80–95 → 95–110（可落子按 1.1 的 `IsPlayable`）；删除"障碍占外接 25%–35%"；距离均衡与必死口袋改沿气边求最短路 / 连通区。算例：85 格图被拒并报越界；两区几何等距但一条跨崖时按各自气边最短路算。验证：`map-definition` 场景测试。
- [ ] 3.2 新增校验：桥必须在深水格上；栅栏必须在几何相邻格之间；每个出生区至少一条气边通路到中央入口。算例：全边缘为崖壁 / 深水的出生区被拒并指出编号。验证：单元测试。
- [ ] 3.3 对称校验器 D2 → C4：绕中心 90° 旋转后高度、地表、障碍、桥、栅栏、信物格逐格一致，出生区编号轮换。验证：单元测试（构造一张只满足 D2 不满足 C4 的小图应被判不对称）。

## 4. v3 基准地图

- [ ] 4.1 `FourPlayerBaseMap` 重写为 v3 生成器：13×13，种子格按 C4 轨道展开；四个出生区各 12–14 格全部 h=2，一侧经 2–3 格 h=1 缓坡下到 h=0 中央，其余边缘为崖壁；中央含一格宽深水与至少一座预置桥、至少一段栅栏、至少一片林地；信物格 14（出生区 8、公共 6）；标注中央入口与咽喉。验证：`MapValidator` 全项通过（含 3.1–3.3）；测试断言地形要素各至少一次、可落子格在 95–110、四区距离两两差 ≤ 1。
- [ ] 4.2 出生区容量：前三大回合 9 枚部署不出现无合法落点。验证：既有容量测试在 v3 上仍绿。
- [ ] 4.3 导出 `maps/siege-4p-base-v3.json`；`map` 子命令按高度 / 地表 / 桥 / 栅栏打印文本图。验证：文件存在、再读入相等、人工看图。
- [ ] 4.4 地图 Id 与全部引用改为 v3；v2 JSON 保留为历史存档并在注释里说明已不能加载。验证：全量测试通过。

## 5. 表现层视图模型（Siege.Presentation）

- [ ] 5.1 盘面层视图模型带出每格高度、地表、桥与栅栏边，供 Godot 渲染；归属读法与棋串读法各按 2.1 / 2.2 取集合。验证：单元测试——平地区域两读法集合相同；有崖壁 / 栅栏 / 一格深水 / 林地时差集中的每一格都能给出地形原因。
- [ ] 5.2 改写 `merge-board-layer` 留下的"两读法恒等"测试为 D-G 口径。验证：新测试绿，旧断言删除并列入提交信息。

## 6. Godot 地形渲染与交互

- [ ] 6.1 `BoardGeometry.Center` 带高度（每层抬升固定层高）；`TryFromWorld` / `TryPick` 支持分层拾取（射线与三层平面求交，取最近命中的可落子格）。验证：Godot 端 headless 测试脚本——对 v3 每个可落子格从相机投影再拾取回同一格。
- [ ] 6.2 地砖按高度堆叠并画崖壁侧面（Δh=2 与 Δh=1 侧面可区分）；深水、桥、林地各有可辨地表；栅栏沿格边立起不占落点。验证：`--screenshot` 出图人工对照 `art/style-exploration/` 基准与用户参考图；检查清单写入 `art/terrain-v3/README.md`。
- [ ] 6.3 坐标标注适配 13 列，锚点放在棋盘外圈 h=0 平面，边缘格高度不同仍可读。验证：截图人工检查 + `A B C D E F G H J K L M N` 无 `I`。
- [ ] 6.4 `--auto-demo` 与 `--seed` 在 v3 上跑通；Godot 层不自己算邻接 / 地形过滤。验证：源码级扫描（`src/godot/` 不在 sln，补 grep 守门）确认只消费视图模型与 `BoardGeometry`。

## 7. 既有测试与文档

- [ ] 7.1 改写写死 11×11 / 85 / 36 障碍 / D2 对称 / `A–L` 的测试；几何算例改坐标重构，不改期望值。逐条列入提交信息并说明新坐标等价。
- [ ] 7.2 设计文档 §3.1–3.3 改写为格属性与 v3 描述、§7.1 覆盖改走覆盖关系、§7.3 补"占据信物格同样揭示"、§14.2 两读法差集说明、§20 地形从装饰升级为规则元素；把 `terrain` 规格里的气边 / 覆盖算例（`F6`/`F7` 崖壁、`F6`–`G6` 栅栏、`F6`→`G6`→`H6` 隔岸）带进 §3 作为标准算例，使本 change 的规则类任务算例在归档后来自设计文档；变更记录加一行（v1.1 → v1.2）。验证：人工检查。
- [ ] 7.3 `.trellis/spec/core/boundaries.md` 补"几何四邻 / 气边 / 覆盖关系三个唯一实现点"；`coordinates.md` 补列字母随宽度。验证：人工检查。

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
