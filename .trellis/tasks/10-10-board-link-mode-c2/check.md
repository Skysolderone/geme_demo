# 10-10-board-link-mode-c2 段 C2 质检报告（3b.1–3b.4）

检查对象：未提交改动（`src/Siege.Core/**` 四个文件、新测试类、`boundaries.md`、`tasks.md`、任务工件）。规格权威：proposal 裁决 C18 / C19 / P1–P4；先例 ai-turn-speed D2 / D3。P5 / P6 未动。

**结论：建议提交。无阻塞项。** 新增需裁决事项 P7（非阻塞，见 §4）。

## 1. 逐项检查结论（按严重度排序）

### 1.1 决策不变的证据 —— 通过

- 三条决策序列基线（`决策序列基线_链接模式Tests` ×2、`决策序列基线Tests` v5 / board:1）与旧模式黄金值 / 日志哈希测试：`git diff` 显示测试与夹具一条未改，终验全绿。
- **对照局复现（单列）**：`run --out <scratchpad> --map link:1 --count 1 --seed 1 --difficulty Standard --turn-limit 120`，配对缺省种子派生，输出到 scratchpad 不入库。用 `before/normalize.py` 规范化：
  - SHA-256 `2ae7b00f42f172cb6888674c0db4f3587e5af1e6328808a2c17425c61289c234`、411 行、2,440,556 字节——三项与 `before/match-seed1-turn120.sha256.txt` 相同；
  - 规范化文本与 `before/…normalized.jsonl.gz` 解压后逐字节相同（2,440,556 = 2,440,556）；
  - `before/` 的 gz 自身解压后哈希也等于哈希文件，三个文件自洽。
  - 本局在 Core 自修（见 §2）之后的构建上跑出；此后 Core 源码只经变异并逐字节还原（`cmp` 一致），终验构建与之同源，无需再跑。
- `normalize.py` 可复现：只按"键名以 `Ms` 结尾"递归剔除，`separators=(",", ":")`、`ensure_ascii=False`，无机器相关量。

### 1.2 九项优化逐项审 —— 通过（1 处证据缺口已补）

| # | 优化 | 结论 |
|---|---|---|
| 9 | `MapTables.Derive`，`EditReach = 2` | **成立**。由 `Adjacency` 源码闭合论证：`LibertyNeighbors(c)` 只看 c、四邻与其间栅栏（距离 ≤ 1）；`CoverageTargets(s)` 只看四邻 t 与直线隔一格的 u（隔水 / 岩台远格都是 `2m − s`，距离 2）；`Flags` 只看本格。`TerrainWriter.Apply` 三类改造分别只 `Bridges.Add(目标格)` / `Fences.Add(目标边)` / `Surfaces.SetItem(目标格)`，不碰高度、障碍、出生区、信物；`ApplyAll` 用 `map with { TerrainData = … }`，`Obstacles` / `BirthZones` / `RelicCells` 引用原样保留，`Derive` 的同实例前置条件在唯一调用方 `ApplyTerrainEdits` 上结构性成立。切比雪夫 5×5 是必要集合的超集。派生构造函数不抄 `_playable` / `_ordinal`（留 default 由 `EnsurePlayable` 重算），正确——搭桥会改可落子集合。守门样本：随机小图 12 张穷举 + 链接图 / v5 / board:1 抽样，桥 ≥ 20、栅 ≥ 100、烧林 ≥ 20，旧表"全部算过 / 一格未算"两态、同批两改造、派生的派生都有；下界断言要求距离 2 覆盖变化 ≥ 10 例、距离 1 气边变化 ≥ 10 例。MT-1 自己复现为红。 |
| 7 | 空集合 `RemoveStones` 不推进版本号 | **成立**。全部调用方 3 处：`BatchRehearsal.cs:178`、`SettlementDriver.cs:107`、`MatchFlow.SeatProjection.cs:80`，都只是"移除提子 / 不可见棋子集合"，没有把"调用即失效"当信号用；`_version` 为 private，无外部读取。 |
| 1 / 2 | `Serialize` 记忆 + `string.Create`；`SuperkoKey` 租用缓冲 | **成立**。`序列化按版本号记忆且与逐格写法相同` 用改动前 `StringBuilder` 写法做参考实现，覆盖 15 号玩家、十种类型、改造段，每种写入原语后重算、副本从零起；`RefSuperkoKey` 同样逐字符比对。`SuperkoKey` 调用方只有 `BoardHistory` 两处；投影仍只取占用者位 + 改造段（不含类型），`盘面同形禁则Tests.同形比对键投影只有一处实现` 未改。 |
| 3 | `Fingerprint` 一次成串 | **原缺"与旧写法逐字符相同"的直接对照**（`private static`，只有基线兜底）。已补：改 `internal`，新增 `活形缓存指纹与逐格写法相同`（40 个随机盘面逐字符比对 + 语义钉住：同格换类型指纹相同、换占用者 / 加改造不同），变异 MT-10 红 1 / 4。 |
| 4 | `CoverageMap` 紧凑存放 | **成立**。来源按格下标升序两遍遍历（= 旧 `AllCoords` 序）；覆盖者数由每格来源的不同所有者数出，首个来源所有者即"第一名覆盖者"，与旧 `first + others 集合` 逐项等价；不可落子格查询答 `None` / `Obstacle` / 空来源，与旧 `Resolve` 第一步相同；`ExclusiveCellsOf` 只扫可落子格，序号升序即坐标序。`盘面派生量优化对照Tests` 参考实现守住，MT-5 / 7 / 8 已记录。 |
| 8 | `LifeShape` 按可落子序号编址 | **成立**。起点循环按序号升序 = 旧 `AllCoords` 中可落子子序列，`isLiberty` 只在可落子格为真，顺序不变；`_groupAt` / `_eyeSpaceAt` 只经 `IndexChecked` 两处进入（已核对全部使用点），不可落子格返回 −1 → `null`，与旧表的 −1 语义相同。MT-9 自己复现：41 条红 1（`棋盘边缘与障碍算墙`）。 |
| 5 / 6 | `Occupants` / `PlayableIndices` / `CoordOfIndex` 读取口；`LibertiesOf` 排序去重 | **成立**。读取口都是 internal、与既有逐格查询同一份派生表；`LibertiesOf` 排序 + 去相邻重复 ≡ 旧 `seen[]` 去重后排序，MT-6 已记录。 |
| — | 两处新增响亮失败 | **不可达**：`Place` 要求 `Map.IsPlayable`（`Fill` 也经 `Place`）；三类改造与链接只增不减可落子格；`Adjacency.LibertyNeighbors` / `Receives` 的两端 / 接收条件都含可落子。合法流程全路径证据：三条基线 + 对照局 + 全量 2318 条无一抛出。 |

### 1.3 硬边界 —— 通过

四个 Core 文件 grep：无 `double` / `float` / `Random` / `Godot`；无随机消费改动；坐标只经 `Coord`（`CoverageMap` 内 `new Coord(i % w, i / w)` 两处是格下标 ↔ 坐标的换算，与 LifeShape 改为 `board.CoordOfIndex` 口径相同，见 P7）；没有新增邻接遍历 / 空格归属 / 活形判定——覆盖仍只经 `Adjacency.CoverageTargets`，活形仍只在 `LifeShapeReport`，同形键仍只在 `GameBoard.SuperkoKey`；`BatchEvaluator` diff 不含任何权重 / 阈值 / 候选上限行。

### 1.4 缓存失效守门 —— 通过

- 序列化：每种写入原语（Place / Place 15 号 / ApplyTerrainEdits 两类 / Clear / RemoveStones）后断言不再返回旧实例且与从零相同；副本互不可见。
- `PlayableIndices` / 派生表：`AssertSameAsFresh` 比 `PlayableIndices` / `PlayableOrdinals` 与新建盘面相同；派生表不抄旧 `_playable`。
- 空集合移除：`空集合移除不令记忆失效` 正反两面。
- 变异抽检（按 testing.md：`cp` 不带 `-p` 备份 → 锚点恰命中 1 次 → 只建测试工程 → `--filter` → `cp` 还原 → `cmp` → `touch` → `git diff` 与变异前相同）：
  - MT-1 `EditReach` 2 → 1：`盘面派生量提速对照Tests` 4 条红 1（`改造派生表与从零建表逐项相同`）。
  - MT-9 `IndexChecked` 返回格下标：LifeShape 41 条红 1（`棋盘边缘与障碍算墙`），与实施报告"40 条里红 1"相符。
  - MT-10（新）`Fingerprint` 丢改造段：4 条红 1（`活形缓存指纹与逐格写法相同`）。
  - 还原后 `src` / `tests` 无 `.bak` 残留；终验失败数 0 ≠ 最后一条变异的红数 1，排除陈旧 DLL。

### 1.5 代码质量 —— 小问题已修

- `GameBoard.cs` 1043 行，`MapTables` 嵌套类约 190 行。抽成 `GameBoard.MapTables.cs` partial 文件（项目已有 `MatchFlow.SeatProjection.cs` 先例）是合理归拢，但属结构性搬移、会把待审 diff 再放大一倍，本次不做，列为 P7。
- 注释引用：`GameBoard.cs:848` 把派生表守门测试写成 `盘面派生量优化对照Tests`，实际在 `盘面派生量提速对照Tests`——已改，并把"`TerrainWriter` 每类改造只改目标格 / 目标边"这一耦合点补进 remarks。
- `boundaries.md` 新段原文"三处均由 `盘面派生量提速对照Tests` 守住（MT-1–MT-4）"不准确：第三处（覆盖表 / 活形只存可落子格）由既有 `盘面派生量优化对照Tests` 与 `空区与封闭眼空间Tests` 守（MT-5–MT-9）；且漏了 implement.md §7.1 的第二个耦合点。已改为三条分列、两条局部性前提都写明。
- 死 `using System.Text;`（`GameBoard.cs`、`BatchEvaluator.cs`，`StringBuilder` 已全部替掉；`TreatWarningsAsErrors` 下 IDE0005 不报）——已删。
- 测试 `Check(string before, string what)` 的 `_ = what;` 占位——改为把 `what` 写进断言消息。

### 1.6 改动范围 —— 通过

只有 `src/Siege.Core/**`（4 文件）、`tests/**`（1 新文件）、`openspec/changes/board-link-mode/tasks.md`、`.trellis/**`（spec/boundaries.md、任务 prd / implement / before/、`workspace/rubioc/journal-1.md` 加 1 行——派发清单未列但在允许目录内）。`src/Siege.Sim`、`src/Siege.Presentation`、`src/godot` 零改动。`before/` 三个文件合计 52,818 字节。

## 2. 自修清单

| 文件 | 改动 |
|---|---|
| `src/Siege.Core/Ai/BatchEvaluator.cs` | 删死 `using System.Text`；`Fingerprint` 改 `internal`（注释注明只供本类与守门测试） |
| `src/Siege.Core/Board/GameBoard.cs` | 删死 `using System.Text`；`MapTables` remarks 的守门测试类名改正，补 `TerrainWriter` 只改目标格 / 目标边的耦合说明 |
| `tests/Siege.Core.Tests/BoardTopology/盘面派生量提速对照Tests.cs` | 新增 `活形缓存指纹与逐格写法相同`（含 `RefFingerprint` 参考实现）；`Check` 的 `what` 进断言消息；类备注补 MT-10 与 check 复现记录 |
| `.trellis/spec/core/boundaries.md` | 段 C2 段落改为三条分列，守门归属与两条局部性前提写准 |

自修触碰了 Core 文件（using / 注释 / 可见性，无逻辑改动）。按要求在自修之后重跑了三条决策序列基线（全绿）与对照局（哈希 / 行数 / 字节数相同）。

## 3. 构建与测试

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：总计 2318（= 2317 + 新增 1），通过 2307，跳过 11，失败 0，2 m 6 s。
- 变异复现：MT-1 / MT-9 / MT-10 均红，见 §1.4。
- 本次只跑了 1 局对照局，没有批量。

## 4. 是否建议提交

**建议提交。** 无阻塞项。

需裁决（非阻塞，编号续 P7）：

| # | 事项 | 建议 |
|---|---|---|
| P7 | `GameBoard.cs` 已 1043 行，`MapTables`（派生表 + `Derive` / `Invalidate` / `PlayableIndices`）约 190 行适合抽成 `GameBoard.MapTables.cs` partial 文件；同时 `CoverageMap.Compute` / `ExclusiveCellsOf` 里的 `new Coord(i % w, i / w)` 可统一走 `GameBoard.CoordOfIndex`（LifeShape 已如此）。两者都是零行为改动 | 提交本段后另起小 change 或并入段 D 前的整理；不在质检里做，避免放大待审 diff |

遗留知悉（implement.md §7 已列，无新增）：`Derive` 正确性依赖 `Adjacency` 最远距离 2 与 `TerrainWriter` 只改目标格 / 边两条局部性，已写进 `boundaries.md` 与 `MapTables` remarks。
