# 地形全可落与地形加成（terrain-bonus）

## Goal

用户裁决（2026-11-08，8 项全按推荐默认）："所有格子都可以下；地形会影响棋子的效果，也会有加成。"
分两段：**段 A（本任务）= 规则内核 + 加成常量 + 面板文案，在现有地图上跑**；段 B（另起任务）= 生成器把岩石改成可落地表、虚空区分、校验预算、美术。

## Requirements

- R1 未架桥深水 SHALL 可落子（空的深水格沿用浅滩语义：不是气、不属于空区；不是覆盖目标，覆盖照旧穿过一格到对岸）。
- R2 新地表 **岩石**（`Surface.Rock`，字码 `K`）：可落子；位于其上的棋子不产生覆盖（同沼泽源）。现有地图不出现岩石（岩石仍在 `Obstacles` 里，段 B 才由生成器改写）。`Obstacles` 从此只表示虚空 / 不可通行。
- R3 现有限制型地形效果全部保留，在其上叠加**地形军势修正**：按地表与高度各一张常量表（`Uncalibrated`），逐枚计入基础军势：`有效基础军势 = max(0, 类型基础军势 + 地表修正 + 高度修正)`。
- R4 桥格 = 深水上的陆地，不吃深水修正；匠人搭桥目标与合法性不变，效果变为"去掉深水惩罚、空桥格恢复为气 / 可成空区"。
- R5 旗心、信物遗址 MUST NOT 落在未架桥深水上（规则文案点明）。
- R6 征募 / 手牌面板的棋子效果文案与新规则一致（连珠"隔深水不成线"删除）；新增地形修正一览文案入口（表现层只经 Labels 取常量）。
- R7 AI：不改评价逻辑；按约定只记录影响（`EvaluationWeights` 校准标注追加"terrain-bonus 后未校准"）。

## Acceptance Criteria

- [x] `dotnet build` 零警告；快测（`Category!=Slow&Category!=Perf`）全绿。
- [x] 每条新增 / 改动的 Scenario 有同名测试；新守门测试做过变异验证并记录。
- [x] 一次 `--rounds=6` 的 Sim 跑局能看到棋子落在深水格、势力拆解里出现地形修正。
- [x] 规格（terrain / board-topology / life-shape / coverage-territory / piece-effects / power-score / terrain-edit / match-setup / relic-spawn）与实现一致。

## Notes

- 不动 `src/godot/parts/terrain_plateau_temp/`（未提交的地形美术）。
- 段 A 唯一一处越过 A/B 边界：深水可落后 `MapValidator` 可落子预算与 `FlagRules` 合规下限需重钉，在 design.md 裁决记录里注明。

## 进度（2026-11-08）

- 已完成：8 项裁决确认（按推荐默认）；prd.md / design.md 落稿；worktree `../geme_demo-terrain-bonus`（分支 terrain-bonus）已建，任务已 start。
- 未开始：全部代码与规格改动（design.md I1–I9）。下个会话从 I1（`TerrainEffects` 常量）+ 规格 Scenario + 同名测试开始，先在 worktree 跑一遍快测基线。
- 2026-11-08 第二次会话：worktree 快测基线 **2093 通过 / 0 失败 / 1 跳过，14 m 16 s**。
- 分支点已定位（只改这些处即可对齐浅滩 / 沼泽语义）：`MapData.IsPlayable`（去深水条件）、`GameBoard.MapTables.Flags`（LibertyWhenEmpty 另排除未架桥深水，空区 / 气都只经它）、`Adjacency.CoverageTargets` 121 行（岩石源同沼泽）与 `Receives`（排除未架桥深水）、`GameBoard.Place` 677 行文案、`MapFile` 363/380 字码表加 `K`、`Labels` 35 行地表说明、`Program.cs` 416 行终端字码、`MapValidator` 410/455/908 行地表统计。

## 收尾（2026-11-22）

- 合入当时的 main 后快测 98 红，逐类处理后 **2112 通过 / 0 失败 / 1 跳过**；构建 0 警告 0 错误；Godot 点击自检与 `--rounds=6 --map=link:9:4:l` 均退出 0。
- 地图逐格不变：main 与本分支导出 link:1:2/3/4、link:9:4:l 逐字节相同；地图 / 校验 / 插旗类测试改走陆地口径（IsLand / LandCount / LandLibertyNeighbors）。
- 用户裁决（2026-11-22）：**同一批次里要架桥的深水格允许当批落子**（含匠人落点继续向外架桥）；terrain-edit「批次内不链式」与 Scenario 已改。
- 规格随 I10 收口：terrain「架桥使空深水格成为气」（架桥不再改变气边）、覆盖接收条件补"不是未架桥深水"；capture-resolution 删去已不可构造的「搭桥漏眼被拒」。
- 新增 `GroupPower.TerrainBonus` / 日志 `GroupEntry.TerrainBonus`（基础军势中的地形部分，只拆账不另加），Sim 6 大回合局可见非零地形修正（link:9:4:l 种子 9）；link:1:4 种子 1–4 的 6 大回合局里种子 2 / 4 都有落子在未架桥深水上（种子 2 第 3 小回合 P0 匠人落 AT42，此前无搭桥）。
- 变异验证：岩石修正丢失 → 红 1；桥格仍吃深水修正 → 红 1；岩石产生覆盖 → 红 1；深水不可落 → 红 12；TerrainBonus 写 0 → 红 1。全部还原。
- 删去 / 改写的守门：「搭桥漏眼被拒」「提子后恢复活形的批次合法」「与完整预演一致_暂放搭桥造出他人的新眼」已不可构造而删（步骤 5→6 顺序仍由「围死活形被拒」守；M-P4 预筛条款现无守门局面，已在测试注释说明）；深水当墙的夹具改用 h=2 崖壁。
- AI 观察（只记录，按"AI 放最后"）：地形修正进军势后每批收益普遍超过缺省停手阈值 20，种子 31–46 跑到 72 小回合两种阈值走法全同；停手阈值黄金测试的反面改为"阈值 0 对比极大阈值"。
