# 10-09-board-link-mode-a 段 A 质量检查报告（trellis-check，2026-10-09）

对象：OpenSpec change `board-link-mode` 段 A（tasks 1.1–1.7）未提交改动。R1–R6 负责人已接受，不作为违规报告。

## 1. 逐项结论（按严重度）

### 中（已自修）

| # | 结论 | 位置 | 处理 |
|---|------|------|------|
| M1 | **退化直通道路径零覆盖**：种子 1–20 × 全部序列退化 0 次（implement.md §4），`TryCorridor → grown → MapValidator.Validate` 这条路从未被任何测试走过；若校验器隐含拒绝"直接连两块棋盘、没有公共棋盘的通道"，生产上退化链接会直接抛异常。 | `src/Siege.Core/Board/Maps/LinkMapGenerator.cs` `Link` 退化分支 | 新增 `链接时的公共棋盘生成Tests.退化为直通道`（手写夹具：0 号 (0,0)、1 号 (0,9) 同列相隔 4，2 号 (7,2) 挡死横向通道）。**通过**：退化链接过校验、版本号 +1、棋盘清单不变、战场 5→4、新增格恰为那条 3–4 宽 × 4 长通道。不是阻塞项。 |
| M2 | **空断言测试**：`战场的定义与规则作用域Tests.旧地图的孤立可落子区不属于任何战场编号` 后半段包在 `if (isolated.IsPlayable(A1) && …)` 里，变异 `Battlefields.HasBirthBoard` 恒真时该测试不红——分支从未执行。 | `tests/Siege.Core.Tests/Battlefield/战场的定义与规则作用域Tests.cs:160–168` | 改为确定性构造（扫第一个不在出生区、四邻也不在出生区且非信物格的可落子格，把四邻改成障碍），断言 Id = −1、`HasBirthBoard` 假、排在最后、`At(邻格)` 为 null；删掉只为它存在的 `Ext.Let`。 |

### 低（已自修）

| # | 结论 | 位置 | 处理 |
|---|------|------|------|
| L1 | 类注释与实现 / 已裁决规格矛盾两处：「校验闭环」写"首轮两次链接的两种先后次序都算"（实际只按规范次序，R2）；「为什么不是 50×50」末尾仍写"对规格的偏离，见任务报告"（规格已改 73、R1 已接受）。 | `LinkMapGenerator.cs` 类 remarks | 改为引用裁决 R2 / R1 / R6。 |
| L2 | Scenario「同链接序列同最终图」没有同名测试方法（只是 `链接事件Tests.链接可重放` 里的一条断言）。 | `链接档地图标识与确定性Tests` | 新增 `[Fact] 同链接序列同最终图`（link:12345 上 (0,3)(1,4)(0,1) 两次逐字节相同 + 换序列不同的样本口径断言）。 |
| L3 | `Link` 的 `ArgumentException("两个战场都必须至少含一块棋盘。")` 不指名哪个战场。 | `LinkMapGenerator.cs` `Link` | 消息带两个战场标识与各自棋盘数。 |
| L4 | `ApplyLink` 与 `ApplyTerrainEdits` 各复制一份"换 Map → `new MapTables` → `_version++`"。 | `GameBoard.cs` | 抽成私有 `ReplaceMap(MapData)`，两处共用；语句与顺序不变（`_edits.AddRange` 留在原处）。 |
| L5 | `初始布局Tests.画布固定且不超过校验器上限` 注释"规格 50 vs 实现 73 的裁决一旦落地"已过期。 | `初始布局Tests.cs:77` | 改为 A2 / R1 口径。 |
| L6 | design D2 第二条仍写几何由 `(地图种子, 链接序号, X, Y)` 派生；规格文本、`MapRandom.ForLink` 注释与实现都是 `(地图种子, X, Y)` + 当前物化状态。 | `openspec/changes/board-link-mode/design.md` D2 | 改为与规格 / 实现一致（不属于 R1–R6）。 |

### 备注（不修、供主会话知悉）

- implement.md §0 R1 行与 §6 末条写"规格文本待裁决后同步，本段未改"——规格与 design 现已由主会话同步为 73×73，这两句过期；§3 的测试计数也应更新为 2139 / 2129 / 10。
- 「对局种子不影响地图」目前是源码扫描 + 反面断言；行为版（两个对局种子同地图）、「增长只经链接」「加载地图不引入随机」「信物子流独立」都要等段 B 的 `MatchFlow` 接线。
- 「生成图同样经过校验」对链接档没有同名测试：`Generate` 每次尝试都先过 `MapValidator`，`初始布局Tests` 与 `物化状态校验Tests` 间接覆盖；同名测试在 `生成图布局规则Tests` 里只覆盖 `gen:`。
- `夹具仍可加载_夹具生成不变` 一个方法覆盖两个 Requirement 的两个 Scenario，可接受。
- `MapValidator` 旧三档声明行**追加**了 `Connectivity / RequireLandmarks / BirthZonesMustReachEntrance` 三个字段，取值全部复现旧语义；阈值字面量未动。`边疆档静态校验Tests` 已把三个新字段纳入"每字段只读一次"。严格说是"加字段不改行为"而非"未改"。
- `Battlefields` 按 **MapData 实例**记忆化（`ConditionalWeakTable`）而非 design D1 / tasks 1.2 写的"版本号"：这是必要的——`ApplyTerrainEdits` 不动 `MapVersion`，按版本号会在改造后给出陈旧分量。失效条件正确。
- `GameBoard.LinkMismatch` 不比对 `ChokePoints` / `CentralEntrance` / `MaxPlayers`（已覆盖画布、标识、版本号、地形、出生区、棋盘、信物、可落子格），低优先级。
- 链接行 `CorridorLength (2, 80)` 的 80 ≥ 画布 73，等价于"不限"。
- `TryVerify` 用 `Environment.NewLine` 只是换行常量，不读环境；守门正则 `Environment\.TickCount` 不误报。
- `LinkMapGenerator.Cache` 进程级无上限，implement.md 已记为段 D 风险。
- `MapCatalog.DefaultId` 仍为 `siege-4p-base-v5`，`Resolve(null)` 经 `Legacy` 得到 v5；boundaries.md 单一实现表"缺省恒为 v5"在段 C 收口前仍成立。
- `Battlefields.Of` 的 `ConditionalWeakTable.GetValue` 是去重不是互斥：并发首算可能各算一次、只保留一份；`Compute` 是纯函数，结果不受影响。
- `退化为直通道` 断言 `ById(0).BirthZones == [0, 1]`，同时钉住"战场 Id = 最小出生区编号"的定义。
- 改动范围内还含实施方动过的 `.trellis/workspace/rubioc/journal-1.md`。

## 2. 检查项结论

1. **规格符合 — 通过**（含上表修正）。核对数值：`CanvasSide = 3×22 + 7 = 73` ✓；`SlotPatterns` 32 项 ✓；链接行 `CorridorLength (2, 80)`、可落子 `(125, 1600)`、`PlateConnectivity.PerComponent` → `ValidateComponentsHaveBirthBoards`（新失败码 `BATTLEFIELD_WITHOUT_BIRTH_BOARD`）✓；`ComputeSequences` 四人四区首对恒含最小编号（R2）✓；R5：`TerrainEditRules` 只加注释、`场景格不可改造` 测试按"搭桥 / 烧林拒绝、立栅合法无效果"断言 ✓。每个段 A 范围内的 Scenario 都有同名或注释点名的测试（新增两条后无缺口）。
2. **硬边界 — 通过**。零 Godot（构建期 `AssertNoGodotDependency` 过、源码无 `Godot` token）；随机只经 `MapRandom.ForLinkAttempt` / `ForLink`，三个新域常量 `0x17E3…`、`0x8C4F…`、`0x4A9D…` 与既有 `AttemptDomain 0xA5C3…`、`SurfacesDomain 0x3F71…`、`BoardDomain 0x6B0A…`、`BoardAttemptDomain 0xD2F4…` 互不相同；新文件无 `double / float / Random / DateTime / Stopwatch`（`对局种子不影响地图` 守门 + 本次 grep）；坐标只经 `Coord`（生成器内部 `Rect` 只做矩形算术，输出一律 `new Coord`）；连通分量只在 `Battlefields.Compute` 一处、经 `Adjacency.Neighbors`，已进 `四邻接Tests` 允许名单 ⑥ 且有反面断言；`LinkMapGenerator` 用二维前缀和，没有第二处邻接遍历；`MapProfile` 读取点只有 `MapValidator` 声明表与 `Maps/` 目录（`地图规格档Tests` 白名单允许 `Siege.Core/Board/Maps/`），`GameBoard` / `TerrainEditRules` 不读。
3. **既有行为不变 — 通过**。`生成确定性Tests`、`棋盘档生成图标识Tests`、`各入口支持生成图Tests`、`夹具仍可加载_夹具生成不变`（v5 / gen:12345 / board:12345 逐字节）全绿；`MapValidator` 旧三档阈值未动（见备注）；`Clone` 带 `LinkedMap`、共享 `_tables` 不变；`ApplyTerrainEdits` 重构后语句等价。
4. **代码质量 — 通过**（L3 / L4 已修）。`TryVerify` 按步骤前缀记忆几何，55 条序列只算 70 次链接，无重复计算；`TryPlacePublic` 每次链接枚举 9 种边长 × 全部位置 × 通道选项，实测单图 0.3–1 s（含 70 次核对），可接受；`MapValidationException` 带全部失败项、`MapGenerationException` 指名两个战场。
5. **测试组织 — 通过**。Requirement → 测试类、Scenario → 方法；变异复现一条：`Battlefields.HasBirthBoard` 由 `Id >= 0` 改 `Id >= -1` → 只跑 Battlefield + LinkMapGeneration 两组 87 条，**红 1**（`无出生棋盘的分量即校验失败`）；`cp` 备份、还原后 `cmp` 逐字节一致、`touch` 刷新时间戳；同时暴露 M2 的空断言。
   第二轮（自修后，针对新增 / 改写的守门测试）：`LinkMapGenerator.Link` 退化分支的 `degraded = true` 改 `false` + `HasBirthBoard` 再次恒真，同一进程跑两组 89 条 → **红 3**：`退化为直通道`、`旧地图的孤立可落子区不属于任何战场编号`（M2 修复生效，此前不红）、`无出生棋盘的分量即校验失败`；还原 `cmp` 一致、`touch` 后两组全绿。`同链接序列同最终图` 未单独变异，靠"换序列即不同"的样本口径断言保证不恒真。
6. **改动范围 — 通过**。`git diff --name-only` + 未跟踪文件全部落在 `src/Siege.Core/Board/**`、`tests/**`、`openspec/**`、`.trellis/**`。

## 3. 自修清单

1. `src/Siege.Core/Board/Maps/LinkMapGenerator.cs` — 类 remarks 两处过期文字改为引用 R1 / R2 / R6；`Link` 的"至少含一块棋盘"异常消息指名两个战场与棋盘数。
2. `src/Siege.Core/Board/GameBoard.cs` — 新增私有 `ReplaceMap(MapData)`，`ApplyTerrainEdits` 与 `ApplyLink` 共用。
3. `tests/Siege.Core.Tests/LinkMapGeneration/初始布局Tests.cs` — 过期注释。
4. `tests/Siege.Core.Tests/LinkMapGeneration/链接档地图标识与确定性Tests.cs` — 新增 `同链接序列同最终图`。
5. `tests/Siege.Core.Tests/LinkMapGeneration/链接时的公共棋盘生成Tests.cs` — 新增 `退化为直通道`。
6. `tests/Siege.Core.Tests/Battlefield/战场的定义与规则作用域Tests.cs` — `旧地图的孤立可落子区不属于任何战场编号` 去掉条件分支改确定性构造；删 `Ext.Let`。
7. `openspec/changes/board-link-mode/design.md` — D2 第二条几何派生参数改为 `(地图种子, X, Y)` + 当前物化状态。

未改：几何、校验阈值、白名单、随机域常量、任何规则代码；未重开 R1–R6。

## 4. 构建与测试

- `dotnet build siege.sln -c Release`：Build succeeded，**0 Warning(s)**，0 Error(s)。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2139 条，通过 2129，跳过 10，失败 0**（1 m 47 s）。跳过的是既有 Perf / Slow + `槽位图案诊断Tests`；比 implement.md 多 2 条即本次新增。
- 变异复现：红 1 → 还原 → 全绿（失败数 0 ≠ 变异时的 1，确认跑的不是变异二进制）。
- 第二轮变异（新增 / 改写的三条守门测试）：红 3 → 还原 → Battlefield + LinkMapGeneration 两组 89 条全绿（通过 88、跳过 1）。

## 5. 提交建议

**可以提交**。无阻塞项。建议主会话提交前顺手：implement.md §0 R1 行 / §6 末条的"规格文本未改"改为已同步，§3 测试计数更新为 2139 / 2129 / 10，并把本次两条新测试与变异记录补进 §1 / §3。
