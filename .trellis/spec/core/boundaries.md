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
| 几何四邻邻居枚举 | `Adjacency.Neighbors(width, height, c)`；Core 内直接调用只允许 ① `Adjacency` 自身 ② `GameBoard.Neighbors` ③ `TerrainEditRules`（守门名单，测试 `四邻接Tests.几何邻居枚举只在允许名单内直接调用`）。第 ③ 条是 artisan-terrain-edit 裁决 T-2 的后果：改造目标口径定成几何四邻（深水没有气边，用气边会让搭桥不可能），而 `TerrainEditRules` 只拿得到 `MapData`、走不了 `GameBoard.Neighbors`。名单只放这一个类型，任何第二处"自己遍历四邻判改造目标"仍会红 |
| 气边（连接 / 棋串 / 气 / 围杀 / 连珠成线 / 校验器距离与口袋） | `Adjacency.LibertyNeighbors(MapData, Coord)`；经 `GameBoard.LibertyNeighbors` 到达 |
| 覆盖关系（覆盖 / 空格归属 / 唯一覆盖 / 信物发现） | `Adjacency.CoverageTargets(MapData, Coord)`；经 `GameBoard.CoverageTargets` 到达。可不对称 |
| 崖壁阈值 | `TerrainData.CliffDrop`（= 2）；气边 `abs(Δh) < CliffDrop`、覆盖 `h_t − h_s < CliffDrop`、表现层差集原因 `≥ CliffDrop` 三处共用，禁止第二份字面量 |
| 围棋记法 ↔ 内部索引映射 | `Siege.Core` 坐标类型 |
| 覆盖数据（谁覆盖了哪格、来源棋子、是否几何相邻） | `CoverageMap.Compute` 一次算出；`SourcesOf(c)` 只读查询（信物控制、盘面层差集原因都消费它，不自行遍历） |
| 据点控制（占据 / 唯一覆盖 / 争议 / 无人及控制者） | `SiteControl.Compute(board, coverage)`（`Siege.Core.Scoring`）；只读 `CoverageMap.OwnershipOf`，实现内不得出现 `Neighbors(` / `CoverageTargets(` / `HeightAt(`（守门 `据点控制判定Tests.据点控制实现只读覆盖表`）。插旗阶段尚无势力快照时，据点状态也由 Core 的 `MatchFlow.Publish` 给出（`MatchPublicView.SiteStates` 唯一构造点）；表现层与 `src/godot/` 只读 `SiteStates` / `SiteView`，不得自推"无人"或任何控制状态 |
| 高地压制加值 | `PieceEffects.HighGroundBonus(board, group)`；覆盖目标只经 `GameBoard.CoverageTargets` 取得，不另写邻接或崖壁判断；"严格更低"用 `Map.HeightAt` 比较目标格与自身格；`PowerCalculator` 是唯一消费者，表现层只读 `GroupPower.HighGroundBonus` |
| 地图规格档（标准 / 边疆）的分流 | `MapValidator` 里"规则 → 处理方式"的一张声明表（frontier-map D2）。校验器别处不得出现对规格档的分支，下游（对局、AI、Sim、表现层、`src/godot/`）不得读 `Profile`——要按图的大小分流就读可落子格数（如 `AiSearchConfig.DefaultCellLimitFor`）。守门在 `地图规格档Tests` / `边疆档静态校验Tests`，属性模式、强转比较等绕法已做过变异 |
| "标识 → 地图"解析 | `Siege.Core.Board.Maps.MapCatalog`；批量、终端、图形三个入口共用，未知标识响亮失败并列出可用标识，缺省恒为 `siege-4p-base-v4`。加内置图只在 `Builtins` 表加一行 |
| 原型插旗路径的 AI 选区 | `PrototypeZoneAssignment`（见 `determinism.md` 的 `zone-pick`）；三个入口不得各写一份循环（图形版不在 sln 里，靠源码扫描守门） |
| AI 候选格上限的缺省值 | `AiSearchConfig.ForMap` / `DefaultCellLimitFor`（可落子格 > 150 取 24，否则 0）；显式配置含 0 优先；实际生效值进 `config.json` 与日志首部，`Replayer` 按首部重建、缺项按不限制 |
| 相机位姿 | 纯计算在 `Siege.Presentation.Camera`（状态只有注视点与距离，俯角恒 60°、朝向恒定——缩放若带俯角变化，拾取会在某个缩放档静默出错）；`src/godot/` 只采输入，写相机节点的唯一位置是 `BoardView.ApplyCameraPose` |
| 图形版命令行 | `LaunchArgs`：只校验 `--` 之后的用户参数，合法选项集合唯一来自读取动作，未知 / 带错值一律退出码 1 并列出合法选项；全仓没有第二个读原始命令行的地方 |
| 出生区编号的对人显示（1–8） | `BirthZoneLabel.Of` / `Number`；校验器、对称检查、平衡分析报告、`FlagsLocked` 事件文本、`map` 文本图、`play` 棋盘与插旗提示都经它换算，不得手写 `+ 1`；内部索引与日志数据字段保持 0 起（`play` 读入玩家输入的 `z - 1` 是输入解析，不在此列） |
| 结算顺序（设计文档 §6.3 **七步**） | `Siege.Core` 批次结算驱动器（`SettlementDriver`）。第 3 步"同时应用本批全部改造"先于第 4 步提子，顺序不得改（设计文档 §3.4 / §6.3） |
| 地形写入口（加桥 / 加栅 / 烧林） | `TerrainWriter.Apply` / `ApplyAll`（`Siege.Core.Board`）——唯一构造"改造后 `TerrainData`"的地方，只做加法、没有逆向入口，不碰高度 / 障碍 / 信物 / 据点。盘面侧的唯一写入路径是 `GameBoard.ApplyTerrainEdits`（内部只经 `TerrainWriter`），调用点只有 `BatchRehearsal`（预演第 4 步）、`SettlementDriver.Confirm`（正式第 3 步）与 `AttributeEdits` 探针、`GameBoard.Fill`（存档回放）。守门 `地形写入口Tests.地形写入口之外不得构造改造后的地形`（IL 扫 `new TerrainData(`，白名单 = 写入口 + `TerrainData.Flat` + `MapFile` + 基准图，另配"扫描器确实命中写入口"的反面断言），变异 M-B1 已证红 |
| 改造合法性（动作集、目标枚举与拒绝理由） | `TerrainEditRules`（`Siege.Core.Board`）——`LegalTargets` 枚举、`IsLegal` / `Reject` 判定同类同源，守门 `改造合法性Tests.拒绝理由与合法目标集合一致` 穷举全盘 144 条几何边比对。全仓调用点**只有两处**：`BatchRehearsal.ValidateShape`（`Reject`）与 `HeuristicTurnController.EditOptions`（`LegalTargets`）；表现层的可改造目标经 `BatchPreview.EditOptions` 投影，**MUST NOT** 自判。"批内唯一 / 不链式"不在本类——那是批次层（`BatchRehearsal` 的 `DuplicateEditInBatch`）的事，复制一份就是第二实现。变异 M-T2（只放宽 `Reject` 一侧）已证"改其一即红" |
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

### 地形不再是对局内不变量（artisan-terrain-edit）

`terrain-model` 与 `scoring-sites` 两轮里地形是只读的：建局时读一次 `MapData`，此后一局里没有任何一手能改变它。第三轮加入匠人与三种改造（搭桥 / 立栅 / 烧林）后**这条前提作废**：

- `GameBoard.Map` 是**可变**的（`{ get; private set; }`），`GameBoard.BaseMap` 才是开局那一份；`MatchFlow.Map` 是 `=> Board.Map` 的转发，不得再拷成字段。
- 一次改造之后，气边、覆盖、棋串、气、据点控制与信物控制**全部**按新地形重算；预置设施与对局中造出来的设施在规则上完全等价（`地形写入口Tests.对局中架的桥与预置桥完全等价` 逐格比可落子性 / 气边 / 覆盖）。
- 凡是"建局时算好、此后不再更新"的地形派生量，现在都是缺陷。本轮实锤两处：`MatchFlow._playableCells`（可落子格集合，已删，`LegalRangeFor` 改现算，变异 M-B15）与 `MatchFlow.Map`（建局时的 `MapData` 拷贝，已改成转发，变异 M-B13）。写任何持有 `MapData` / 坐标派生表的字段之前，先问"改造之后它还对吗"。
- 例外（有意保留、不是遗漏）：`MapValidator.DistanceTable` 与 `MatchFlow.Flags` 只看**开局地图**——前者是"地图设计"的守门而不是对局态，后者只用于插旗（出生区与高度都不可改造）；`Siege.Sim` 日志首部的 `PlayableCells` 取 `Board.BaseMap.PlayableCount`，因为首部是一局一条、终局时才写出，写终局值等于把"未来的分母"塞进占用率指标。这三处的口径都要在引用它们的数据里注明。
- `src/godot/` 的渲染态缓存**需要**失效机制：`BoardView.Build`（地砖 / 水面 / 桥 / 栅栏 / `_levels`）全仓只有 **3 处**调用——`GameRoot._Ready` 与两处"选出生区"（自动演示一处、手动一处），`Refresh` 路径上一处都没有。段 B 的 2.6 排查第 12 条曾写成"`GameRoot` 每次刷新都重跑 `Build`"，**这是错的**（段 C 核实并更正；段 C 实现记录里把三处写成"`_Ready`、选出生区、重开局"，也一并更正为"`_Ready` + 两处选出生区"）。现行做法：`Build` 时记一份地形指纹（每格可落子 / 高度 / 地表 / 有无桥 + 全部栅栏边，只读默认棋盘视图模型），`Refresh` 开头指纹不符即整体重搭。不补这条，AI 架的桥 / 立的栅 / 烧的林一处都不会显示，且新桥格不进 `_levels` → 拾取拿不到它；`--pick-check` 在第 2 帧就跑完，抓不到这个回归。

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

**地形派生量同理**（artisan-terrain-edit）：气边、覆盖、可落子格集合也是派生量，改造之后一律重算。本轮实锤的两处反例都是"建局时算好的地形派生量"——`MatchFlow._playableCells`（变异 M-B15）与 `MatchFlow.Map` 的字段拷贝（变异 M-B13），在地形只读的两轮里它们是对的，加入改造后立刻成了缺陷。这类"曾经正确的缓存"不会有编译错误，只会给出陈旧答案：给每一处缓存配一条"改造后结果变化"的测试，或在实现记录里写明它不缓存/只看开局地图（本轮的 14 项排查清单见 `.trellis/tasks/09-18-artisan-terrain-edit/implement.md` 段 B 的 2.6 表）。

## 显式输入的名册：未知玩家必须响亮失败

计分层曾把"盘面上有棋子但名册未列"的玩家静默视为 Active。check 一改成抛 `SiegeRuleException`，立刻暴露了一处测试里名册漏人的真实接线错误——静默默认值掩盖的正是这类 bug。规则：凡是把流程层状态（名册、玩家状态、快照）做成显式参数的层，对"参数里没有、盘面上却有"的情况一律抛出；测试便利用显式的无参重载，不用默认值兜底。
