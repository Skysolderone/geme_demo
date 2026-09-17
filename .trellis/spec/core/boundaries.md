# 边界与依赖

## 零 Godot 依赖（硬约束）

`Siege.Core` 与 `Siege.Sim` MUST NOT 引用任何 `Godot.*` 命名空间或 `Godot.NET.Sdk`。

**为什么。** 首轮原型的唯一产出目标是跑数千局并产出 §16/§17 的实测结论。规则一旦写进 `Node` 派生类、依赖场景树或 `_process`，批量跑局就要起引擎实例，并行与确定性都会失守。

`Siege.Core/Siege.Core.csproj` 里有 `AssertNoGodotDependency` 目标做编译期守门。不要为了图方便绕过它——需要 Godot 类型时，说明边界画错了。

Godot 项目（`godot/`）单向引用 `Siege.Core`，反向引用不存在。

## 单一实现原则

下列语义在全项目**只允许存在一处实现**，第二处即缺陷：

| 语义 | 唯一归属 |
|---|---|
| 几何四邻邻居枚举 | `Adjacency.Neighbors(width, height, c)`；Core 内直接调用只允许 `Adjacency` 自身与 `GameBoard.Neighbors`（守门名单） |
| 气边（连接 / 棋串 / 气 / 围杀 / 连珠成线 / 校验器距离与口袋） | `Adjacency.LibertyNeighbors(MapData, Coord)`；经 `GameBoard.LibertyNeighbors` 到达 |
| 覆盖关系（覆盖 / 空格归属 / 唯一覆盖 / 信物发现） | `Adjacency.CoverageTargets(MapData, Coord)`；经 `GameBoard.CoverageTargets` 到达。可不对称 |
| 崖壁阈值 | `TerrainData.CliffDrop`（= 2）；气边 `abs(Δh) < CliffDrop`、覆盖 `h_t − h_s < CliffDrop`、表现层差集原因 `≥ CliffDrop` 三处共用，禁止第二份字面量 |
| 围棋记法 ↔ 内部索引映射 | `Siege.Core` 坐标类型 |
| 覆盖数据（谁覆盖了哪格、来源棋子、是否几何相邻） | `CoverageMap.Compute` 一次算出；`SourcesOf(c)` 只读查询（信物控制、盘面层差集原因都消费它，不自行遍历） |
| 据点控制（占据 / 唯一覆盖 / 争议 / 无人及控制者） | `SiteControl.Compute(board, coverage)`（`Siege.Core.Scoring`）；只读 `CoverageMap.OwnershipOf`，实现内不得出现 `Neighbors(` / `CoverageTargets(` / `HeightAt(`（守门 `据点控制判定Tests.据点控制实现只读覆盖表`）。插旗阶段尚无势力快照时，据点状态也由 Core 的 `MatchFlow.Publish` 给出（`MatchPublicView.SiteStates` 唯一构造点）；表现层与 `src/godot/` 只读 `SiteStates` / `SiteView`，不得自推"无人"或任何控制状态 |
| 高地压制加值 | `PieceEffects.HighGroundBonus(board, group)`；覆盖目标只经 `GameBoard.CoverageTargets` 取得，不另写邻接或崖壁判断；"严格更低"用 `Map.HeightAt` 比较目标格与自身格；`PowerCalculator` 是唯一消费者，表现层只读 `GroupPower.HighGroundBonus` |
| 出生区编号的对人显示（1–4） | `BirthZoneLabel.Of` / `Number`；校验器、对称检查、平衡分析报告、`FlagsLocked` 事件文本、`map` 文本图、`play` 棋盘与插旗提示都经它换算，不得手写 `+ 1`；内部索引与日志数据字段保持 0 起（`play` 读入玩家输入的 `z - 1` 是输入解析，不在此列） |
| 结算顺序（设计文档 §6.3 六步） | `Siege.Core` 批次结算驱动器 |
| 规则计算 | `Siege.Core`——表现层只消费预演结果，绝不自己算 |

据点主人推导（`SiteAttribution.HomeZones`）只供遥测首部，规则代码不得引用；它复用 `Adjacency.LibertyNeighbors` / `AreAdjacent`，不是第二份邻接实现。

写新代码前先搜一遍是否已有实现。重复实现的典型症状：领地层说独占、信物层判争议。

### 气与覆盖是两套边，不再恒等（terrain-model）

`terrain-model` 之前"覆盖 = 棋子的四邻空格 = 棋串的气"，两个集合恒等，`merge-board-layer` 就是靠这一点把两层合成一层。地形进入规则后：

- **气边**被 崖壁（Δh ≥ 2）、未架桥深水、栅栏 切断，对称
- **覆盖关系**被 林地（不接收）切断，跨崖只能居高临下，遇一格宽深水落到对岸，栅栏不挡；可不对称

任何地方写"被覆盖的空格就是气"或反过来，都是缺陷。表现层差集的四种原因（隔岸 / 栅栏 / 崖壁 / 林地）互斥，未命中必须响亮失败，不得静默归零。

**Wrong**：`foreach (var n in board.Neighbors(c)) if (map.TerrainAt(n) != Terrain.Obstacle) …`（手写地形过滤，段 A 前 `GroupSafety` 就是这么写的）
**Correct**：`foreach (var n in board.LibertyNeighbors(c)) …` 或 `board.CoverageTargets(c)`，按语义选一个。

### 地形属性在表现层只能用于渲染

`src/godot/` 与 `Siege.Presentation` 可以读 `HeightAt / SurfaceAt / HasBridge / HasFence` 决定画什么（层高、材质、栅栏朝向），**不得**用它们做任何规则判断（是否相邻、是否是气、谁覆盖）。规则结论一律从 `Siege.Presentation` 视图模型拿。守门：`Godot层不含规则计算Tests` 的 token 表含 `LibertyNeighbors(` / `CoverageTargets(`（变异 G1 已证实有效）。

## 视图分离

公开视图与私有视图是**两个不同的类型**，不是一个类型加权限标志。

- 公开视图：盘面、已揭示信物、势力明细、行动顺序、手牌**类型集合**
- 私有视图：手牌数量与两段账、征募面板、暂放批次

公开视图的结构里根本不存在"对手手牌数量"这个字段。这同时服务玩家信息对称性（§13）与 AI 公平性（§15.1）——靠"约定不去读"守不住。

调试 AI 的全量读取走显式标记为测试专用的旁路，非测试模式下不可达。

## 全量重算，不做增量

棋串、气、覆盖、领地、位置加值、倍率、势力**全部是派生量**，每次盘面变化后重算，不做增量维护、不缓存。

设计文档 §9.2 / §10.1 明确要求实时重算、不保留成长层数。一次提子可能同时改变覆盖、棋串分裂、连珠断线与倍率，增量维护的组合爆炸不可控。

11×11 约 121 格 × 4 人的全量重算开销可忽略。性能优化必须等到 `Siege.Sim` 有实测数据后再做，且不得改变语义。

## 显式输入的名册：未知玩家必须响亮失败

计分层曾把"盘面上有棋子但名册未列"的玩家静默视为 Active。check 一改成抛 `SiegeRuleException`，立刻暴露了一处测试里名册漏人的真实接线错误——静默默认值掩盖的正是这类 bug。规则：凡是把流程层状态（名册、玩家状态、快照）做成显式参数的层，对"参数里没有、盘面上却有"的情况一律抛出；测试便利用显式的无参重载，不用默认值兜底。
