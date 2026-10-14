# implement：link-map-terrain

前置条件：`retire-legacy-maps` 已提交并归档，工作区干净，然后 `opsx:propose link-map-terrain`，再 `task.py start`。
每段派一个 `trellis-implement`（写代码、测试、变异记录，不 commit、不改 openspec/）。派之前把本段待决项拟成编号裁决一次性确认。主会话前台复核后提交。同一时间只跑一个任务。

## 段 A：Core 生成与校验（R1–R5、R7）

- [ ] A1 先红：`BoardTheme`、`BoardPlate.Theme`、`MapFile` 往返（链接档缺 `Theme` 字段要报错）
- [ ] A2 先红后绿：`Battlefields` 的棋盘归属不再取左下角格（AC3）
- [ ] A3 `BirthZones` 改为核心区；用 `codegraph_callers` 全量复核使用者，凡是要整块棋盘的改读 `Boards`（含 `SeatPublicView.cs:146`）
- [ ] A4 生成器：宽高 6–8、主题排列、五个主题的局部生成、岩石、信物（design §2）
- [ ] A5 校验器链接行按 design §3 改写，每条新失败码配一个拒绝算例（AC4）
- [ ] A6 种子 1–20：结构断言（AC1、AC2）+ 全部链接序列穷举 + 区间实测回写
- [ ] A7 旧摘要日志被拒的测试（design §6）；`link:1` 决策序列基线与受影响的黄金值重做，在提交说明里逐条列出

## 段 B：公开视图、Sim 与冒烟（R6、R8、AC5、AC6）

- [ ] B1 `BoardPlaceholder.Theme` 与 `information-visibility` 的 Scenario 测试
- [ ] B2 终端版占位显示主题；`map` 子命令打印主题与地形；`analyze` 按主题统计（棋盘胜率 / 名次、匠人改造次数）
- [ ] B3 冒烟：`run --map link:1 --seed 1 --count 10 --difficulty Standard`，再换 `--map-per-match` 跑 10 局，共 ≤ 20 局；和 F.5 基线对比，主题偏差明显时调 design §2 的初值并回写
- [ ] B4 AI 与地形相关的权重标"未校准"（不调权重）

## 段 C：图形版（AC5、AC7）

- [ ] C1 占位显示主题名；核心区和外圈的视觉区分（如核心区的嵌入亮条改成按核心区画）
- [ ] C2 headless 自检四条；非 headless 截图（五个主题各一张 + 拼接后一张），放进 `art/link-map-terrain/`
- [ ] C3 负责人试玩（可玩优先：段 A 一完成就先给终端版的试玩入口）

## 段 D：收尾

- [ ] D1 设计文档 §3.3 改写，升到 v1.21；HANDOFF 同步
- [ ] D2 `openspec validate link-map-terrain --strict` → 归档 → `task.py archive` → journal

## 验证命令（顺序单跑，看真实退出码）

```powershell
dotnet build siege.sln -c Release
dotnet test -c Release
dotnet run --project src/Siege.Sim -c Release -- map --map link:1 --links 1-4E,2-5E,1-2N
$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
& $G --headless --path src/godot --build-solutions --quit
& $G --headless --path src/godot -- --auto-demo
& $G --headless --path src/godot -- --auto-demo --pick-check --seed=5
& $G --headless --path src/godot -- --map-select --auto-demo
& $G --headless --path src/godot -- --auto-demo --rounds=0 --seed=5
```

## 风险点

- `BirthZones` 语义变化牵连面最大（A3）：漏改一处，表现就是占位尺寸错、取景错、出生区信物报错。所以 A3 要独立成一步，单独复核。
- 6×6 棋盘的中间带只有 7 格，主题可能放不下：生成器必须按面积缩放，并且穷举证明从不抛异常。
- `--pick-check` 的拾取数和取景依赖棋盘尺寸，6–8 的边长会改变 30/30、55/55 这类计数，要按实测回写，并在提交说明里写清楚。

## 第 1 组 1.2：`MapData.BirthZones` 使用者清单（2026-10-14）

查法：codegraph（`codegraph_callers BirthZones` / `BirthZoneOf`、`codegraph_search`）对属性引用只给出 1 条，按 `MapData.BirthZones`、`BirthZoneOf`、`Cell.BirthZone` 用文本检索补齐（Core / Sim / Presentation / src/godot，不含测试）。
分类："出生区" = 规则或统计说的就是出生区，D1 后自动作用到核心区，不改；"整块棋盘" = 实际要的是整块出生棋盘，改读棋盘清单（`MapData.BirthBoards()` / `BirthBoard(z)` / `BirthBoardAt(c)`，1.4 新增）。

### 直接读 `MapData.BirthZones`

| # | 位置 | 用途 | 分类 | 处理 |
|---|---|---|---|---|
| 1 | Core `Board/MapData.cs` `BirthZoneOf` | 格 → 出生区的唯一查询 | 出生区 | 不改 |
| 2 | Core `Board/MapFile.cs` `ToJson` / `FromJson` | 文件往返 | 出生区 | 不改 |
| 3 | Core `Board/Maps/LinkMapGenerator.cs` `Assemble` | 定义方：出生区 = 整块棋盘 | 出生区（定义） | 2.1 改为核心区 |
| 4 | Core `Board/Battlefields.cs` `Compute` | 出生区按"任一格落在分量内"归属战场 | 出生区 | 不改（棋盘归属已在 1.3 改读 `Boards` 的任一可落子格） |
| 5 | Core `Board/GameBoard.MapTables.cs` `Derive` | 换图判据（引用比较） | 出生区 | 不改 |
| 6 | Core `Board/MapValidator.cs` `ValidateBirthPlates` | "出生区 = 出生棋盘"对照 | 整块棋盘（校验规则） | 3.1 改为"出生区 = 核心区"，本组不动 |
| 7 | Core `MapValidator.cs` `ValidateBirthZoneCount` | 出生区数 | 出生区 | 不改 |
| 8 | Core `MapValidator.cs` `ValidateBirthZones` | 出生区越界 / 重叠 / 可落子 | 出生区 | 不改 |
| 9 | Core `MapValidator.cs` `DistanceTable` | 出生区距离表（`map` 打印） | 出生区 | 不改 |
| 10 | Core `Match/FlagPlanting.cs` `ZoneCount` | 数量 | 出生区（数量） | 不改 |
| 11 | Core `Match/PrototypeZoneAssignment.cs` `AssignBlind` | 数量 / 编号范围 | 出生区（数量） | 不改 |
| 12 | Core `Match/PublicRules.cs` `LegalRange` | 仅旧模式（Legacy）构筑期落子范围；链接模式走战场 | 出生区 | 不改 |
| 13 | Core `Match/SeatPublicView.cs` `PublishFor` 插旗阶段锁定后的可见格 | 锁定后公开这一块棋盘的布局 | **整块棋盘** | 已改 `BirthBoard(z).Cells()` |
| 14 | Core `Match/SeatPublicView.cs` `PublishFor` 占位 | 占位位置与宽高 | **整块棋盘** | 已改：链接模式取 `BirthBoards()`（含主题），旧模式保留出生区外接矩形 |
| 15 | Sim `Running/MatchSession.cs` 首部 `ZoneCount` | 数量 | 出生区（数量） | 不改 |
| 16 | Sim `Running/MatchSession.cs` 首部 `ZoneSides` / `SideOf` | 出生棋盘边长 | **整块棋盘** | 已改 `BirthBoards()` 的 max(宽, 高) |
| 17 | Sim `Running/MatchSession.cs` 末行 `LockedFlagCounts` | 下标范围 | 出生区（数量） | 不改 |
| 18 | Sim `Play/BoardRenderer.cs` `Viewport` | 插旗取景范围 | **整块棋盘** | 已改 `BirthBoards()` 的格 |
| 19 | Sim `Play/PlayCommand.cs`（3 处） | 数量 / 输入编号范围 | 出生区（数量） | 不改 |
| 20 | Sim `Program.cs` `map` 打印"出生区 N 个，各 X 个可落子格，高度…" | 统计出生区本身 | 出生区 | 不改（2.x 后显示核心区 9 格、高度 0，属预期；主题与地形打印在 5.1） |
| 21 | Sim `Program.cs` 距离表表头、图例编号 | 编号范围 | 出生区（数量） | 不改 |
| 22 | Sim `Program.cs` `--links` 解析 | 编号范围 | 出生区（数量） | 不改 |
| 23 | Godot `scripts/GameRoot.cs` 构筑期审计 `BuildingRange` | 比对"合法范围全在自己的出生棋盘内" | **整块棋盘** | 已改 `BirthBoard(z).Cells()` |

### 间接（经 `MapData.BirthZoneOf` / `Cell.BirthZone`）

| # | 位置 | 用途 | 分类 | 处理 |
|---|---|---|---|---|
| 24 | Core `GameBoard.MapTables.cs` 建表 → `Cell.BirthZone` | 格的出生区标记 | 出生区 | 不改 |
| 25 | Core `GameBoard.cs` `SpliceTargets` | 拼接不改出生区归属 | 出生区 | 不改 |
| 26 | Core `MapValidator.cs` `ValidateRelicCells` | 信物分区与出生区一致 | 出生区 | 不改 |
| 27 | Core `MapValidator.cs` `ValidatePockets` | 必死口袋（出生区内） | 出生区 | 不改 |
| 28 | Core `Relics/RelicGenerator.cs` `BalanceBirthZones` | 出生区档信物均衡 | 出生区 | 不改 |
| 29 | Sim `Play/BoardRenderer.cs` 插旗占位格 | 占位格标编号 | **整块棋盘** | 已改 `BirthBoardAt(c)` |
| 30 | Sim `Play/BoardRenderer.cs` `zones && BirthZoneOf` | 标出生区编号（链接模式下 zones 为真时整幅走占位分支，只在旧模式可达） | 出生区 | 不改 |
| 31 | Sim `Program.cs` `Glyph` | 文本图标出生区编号 | 出生区 | 不改（2.x 后只标核心区；5.1 另加主题与图例） |
| 32 | Presentation `MapSelect/MapPreviewInfo.cs` | 旧模式统计区数（链接档走占位分支） | 出生区（数量） | 不改 |
| 33 | Presentation `Visibility/DefaultBoardView.cs` 格视图透传 `Cell.BirthZone` | 格的出生区标记 | 出生区 | 不改 |
| 34 | Godot `scripts/MatchSession.cs` `BirthZoneAt`（`GameRoot.OnBoardClicked` 插旗点选） | 点占位选棋盘 | **整块棋盘** | 已改 `BirthBoardAt(c)` |
| 35 | Godot `scripts/BoardView.cs` `zoneOf` / `IsZoneEdge` 出生区描边与归属色 | 出生区亮条 | 出生区 | 不改（6.1 按核心区画） |
| 36 | Godot `scripts/BoardView.cs` `AddBoardPlates` 边框色、`[plates]` 自证行 | 按 `plate.Origin` 查 `zoneOf` 得棋盘编号 | **整块棋盘**（棋盘 → 编号） | 已改为"棋盘内任一出生区格"的编号 |

合计 36 处：整块棋盘 9 处（#13 #14 #16 #18 #23 #29 #34 #36 已改；#6 是校验规则，留 3.1），其余 27 处为出生区（含 3 处只取数量的同类合并项）。

### 同名但不是 `MapData.BirthZones`

`Battlefield.BirthZones`（战场内的出生区编号表）：Core `Ai/HeuristicTurnController.cs`（2 处）、`MapValidator.cs` 战场数校验、`Match/MatchFlow.SeatProjection.cs`、`Match/PublicRules.cs` `BattlefieldOf`、Sim `Program.cs` `--links`、Sim `MatchSession.cs` 链接事件——按战场归属取编号，与出生区范围无关，不受 D1 影响。

### 附注：同属"左下角 / 整块可落子"假设、不经 `BirthZones` 的两处（第 2 组前需处理）

- Presentation `Visibility/DefaultBoardView.ForSeat`：原 `Boards.Where(b => b.Cells().All(visible.Contains))` 与占位 `Expanded = visible.Contains(origin)` 假设棋盘内每格可落子。第 1 组收尾（裁决④）已改为"棋盘内可落子格全部可见"与"外接矩形内有可见格"，占位同时透传主题（裁决③）。
- 测试侧仍有若干处把 `BirthZones[z]` 当整块棋盘用（如 `按座位视图Tests`、`链接模式下的决胜一手Tests.FarCorner`、`LinkModeFixtures.BirthCell`），在 2.1 出生区改为核心区时会红，届时按本清单改读棋盘清单。
