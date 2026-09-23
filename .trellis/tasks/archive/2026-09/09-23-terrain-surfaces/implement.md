# 09-23-terrain-surfaces 实现记录

分支 `feat/terrain-surfaces`（worktree `.claude/worktrees/terrain-surfaces`，起点 `6bb38fb`）。裁决 7：ai-eye 在途时只做段 0 与段 5；段 1–4 等 ai-eye 归档。

## 0.1 基线（改动前实测）

| 对象 | sha256 |
|---|---|
| `Siege.Sim map --map gen:12345 --out` | `a29109204f818e40dbfd4268ef551b8b9f0be8a66c760482dd794a2764b96d4c` |
| `Siege.Sim map --map gen:987654321:p8 --out` | `e1f6933f0fd55b31789287acae68e0b80af64376c655585595974760e5713eb7` |
| `maps/siege-4p-base-v5.json` | `2c4a9e9eeb7b3b5e8fc598f89f1304d1f18d682c6c8333b7aa973e84635dfdf3` |
| `maps/siege-frontier-v2.json` | `c8fa9edaf39988da0439842acd93385a450eda238a1193280cf8de4bf0ba547b` |

段 0 完成后与段 5 完成后各重导一次，两张生成图 `cmp` 逐字节相同；`maps/` 未改动。另有「生成确定性」的黄金摘要测试全绿。

## 段 0（tasks 0.2–0.7）

| 文件 | 性质 |
|---|---|
| `Siege.Core/Board/TerrainData.cs` | `Surface` 加 `Desert / Marsh / Crag / Shallows`（岩台用 `Crag`，避开 h=1"平台"）；`SurfaceNames.DisplayName` 为显示名唯一映射 |
| `Siege.Core/Board/MapFile.cs` | 地表码 `D / M / P / S`；错误信息的合法码清单由映射推出 |
| `Siege.Core/Board/MapValidator.cs` | 第 9 条 `BIRTH_ZONE_SPECIAL_SURFACE` |
| `Siege.Sim/Program.cs` | 文本图 `SurfaceMark`（8 值全覆盖）、图例、概览计数（新地表为 0 时不打印，v5 / frontier-v2 输出不变） |
| `godot/scripts/Visuals.cs`、`BoardView.cs` | 四种地砖色（纯色；装饰在段 1–4） |
| `2026-09-10-…-v1.md` §3.1 | 四条新地表算例（0.7） |

新测试：`Terrain/格属性Tests`（新地表可落子 / 不改变气边 / 与高度独立）、`Terrain/新地表判断唯一Tests`（源码扫描守门，允许清单 6 个文件）、`MapDefinition/地图文件健壮性Tests`（码往返、非法码列八个码）、`MapDefinition/地图静态校验规则Tests`（出生区内新地表 ×5、公共区不受限）。

人工：`test-surfaces.json`（v5 中岛改出四种地表，scratchpad）在终端 `map` 与 Godot 下均能开局；Godot 截图四种地砖可辨（负责人待确认）。

## 段 5（tasks 5.1–5.3）

| 文件 | 性质 |
|---|---|
| `MapGenParameters.cs` | `NewSurfaces` 开关（缺省关）、`RandomPick`（入口随机取种子用，开）；标识 `gen:<种子>[:p<N>][:s1]` 解析 / 规范化 |
| `MapRandom.cs` | `ForSurfaces(mapSeed)`：独立子流，与全部尝试子流不同 |
| `FrontierSurfaces.cs` | 新增。投放：浅滩 → 岩台 → 荒漠 → 沼泽；浅滩挨主河（含桥格）；岩台 h ≤ 1；同种块不相邻；目标 [max(下限+1, 8), 上限] |
| `FrontierMapGenerator.cs` | 布局与校验通过后投放、构造地形、重跑静态校验（不过即抛，不换尝试） |
| `MapSelectModel.cs` | 随机图缺省开；按标识预选照原样；换一张一律开 |
| `Siege.Sim/Program.cs`、`godot/scripts/GameRoot.cs` | 裸 `gen` 用 `RandomPick` |

既有守门改动（均附理由注释）：`四邻接Tests` 允许 `FrontierSurfaces` 直接调几何四邻（块按几何四邻定义）；`地图随机源Tests` 把 `FrontierSurfaces.cs` 记为生成器文件。既有期望改动：`选图视图模型Tests` 14 处、`各入口支持生成图Tests` 9 处随机取种子的标识带 `:s1`。

实测修掉的两个缺陷（扩样本后才暴露）：
1. 先投荒漠 / 沼泽会占掉河岸，gen:18:p7 / gen:10:p8 / gen:45:p5 浅滩无处可放 → 改为约束最紧的先投、主河含桥格。
2. 目标 < 8 时第 0 轮余量不足，gen:213:p5 的沼泽整块静默落空 → 目标下界取 8，第 0 轮余量不足改为抛错。
临时扫描种子 1–400 × 平台数 5–8（1600 张）全部满足投放规模；默认套件保留 1–50 × 5–8。

Godot `--map-select --auto-demo --map=gen:12345`：自检 9 步全过，「换一张」→ `gen:861896450:s1`，最后以 `gen:12345` 建局，退出码 0。

## 段 1——荒漠（tasks 1.1–1.3、1.5；1.4 AI 感知按裁决 8 等 ai-eye 归档）

| 文件 | 性质 |
|---|---|
| `Siege.Core/Scoring/PowerCalculator.cs` | `ScoresTerritory(map, cell)`：独占计分谓词（唯一落点，荒漠 → false）；`scored` 过滤只在"归属 → 领地分"一步 |
| `Siege.Core/Scoring/PowerSnapshot.cs` | `PlayerPower` 加 `ScoredCells`；`TerritoryScore` 改按它计。`ExclusiveCells` 仍是全部独占格（含荒漠），终局并列链"独占空格数"不变 |
| `Siege.Presentation/Layers/LayerContents.cs` | `TerritoryCellView.Scored`（势力层取自 `ScoredCells`；归属读法恒 true） |
| `godot/scripts/BoardView.cs` | 势力层不计分独占格淡着色（0.18 vs 0.5）；荒漠格加装饰 |
| `godot/scripts/LowPoly.cs`、`Visuals.cs`、`PartExport.cs` | `LowPoly.Desert`：两道沙纹 + 带臂仙人掌 + 矮仙人掌 + 兽骨，高 ≤ 0.25、在底座半径外；导出 `desert_0..5` |

测试：`空格归属三态Tests`（荒漠独占不计分、荒漠上的信物照常被控制）、`总势力Tests`（荒漠中的孤立棋子 = 1、荒漠与草地混合 = 3、荒漠上的棋子照常计军势 = 5）、`势力层领地与高地Tests`（荒漠独占不进领地分；原「势力层显示领地分」的逐玩家核对改读 `ScoredCells`）。守门允许清单加 `PowerCalculator.cs`。
重导部件时既有 21 个 `.tscn` 只有随机 `unique_id` 与自动节点名变化，已还原为已提交版本，只新增荒漠 6 个部件与 3 份材质。
全量：1335 通过 / 2 跳过；Godot 构建 0 警告。

## 段 2——沼泽（tasks 2.1–2.3、2.5；2.4 AI 感知等 ai-eye 归档）

| 文件 | 性质 |
|---|---|
| `Siege.Core/Board/Adjacency.cs` | `CoverageTargets`：源格是沼泽 → 空（沼泽源，唯一落点）；归属、信物发现、高地压制经它自然继承，`PieceEffects` 未改 |
| `Siege.Presentation/Layers/LayerContents.cs` | `TerrainReason.Marsh`；差集"是气但无人覆盖"除林地外认沼泽源（以该格为气的棋串里有沼泽上的棋子，只查气快照与地表，不算邻接）——**修改前沼泽会让 ReadingDiff 抛异常**；两种读法加 `SurfaceLegend`（只列地图上出现的新地表） |
| `Siege.Presentation/Text/Labels.cs` | `TerrainReason.Marsh` 文案；`SurfaceRule`：四种新地表的一句规则说明 |
| `godot/scripts/Hud.cs` | 盘面层面板列出图例 |
| `godot/scripts/LowPoly.cs`、`Visuals.cs`、`BoardView.cs`、`PartExport.cs` | `LowPoly.Marsh`：贴地积水斑块 + 两丛芦苇（不用锥形树冠）；导出 `marsh_0..5` |

测试：`覆盖关系Tests`（沼泽上的棋子不覆盖 / 沼泽格本身接收覆盖）、`棋子向四邻接相邻格提供覆盖Tests`（沼泽上的棋子不提供覆盖）、`高地压制加值Tests`（沼泽上的棋子没有压制）、新文件 `TacticalLayers/新地表的规则标示Tests`（图例给出规则描述、沼泽上的棋子不点亮归属、差集可由沼泽源解释）。守门允许清单加 `Adjacency.cs`、`LayerContents.cs`、`Labels.cs`。
全量：1342 通过 / 2 跳过；Godot 构建 0 警告；截图沼泽（水洼 + 芦苇）与林地可辨。

## 段 3——岩台（tasks 3.1–3.3、3.5；3.4 AI 感知与 3.6 的 200 局对照等 ai-eye 归档）

| 文件 | 性质 |
|---|---|
| `Siege.Core/Board/Adjacency.cs` | `CoverageTargets` 第 3 步：源格是岩台时沿四方向越过中间格 m 覆盖 u；m 是障碍 / 盘外 / 高出崖壁阈值时阻挡，深水、林地、有子格、栅栏不阻挡；与隔水覆盖同格时去重。崖壁阈值只经 `TerrainData.CliffDrop` |
| `Siege.Presentation/Layers/LayerContents.cs`、`Text/Labels.cs` | `TerrainReason.Crag`：来源不相邻且站在岩台上 → "岩台"，否则仍是"隔岸"（岩台隔一格宽深水的同格按岩台解释）。**修改前远格会被错标成"隔岸"** |
| `godot/scripts/LowPoly.cs`、`Visuals.cs`、`BoardView.cs`、`PartExport.cs` | `LowPoly.Crag`：内缘 0.03 高石沿 + 贴地裂纹 + 两颗碎石；导出 `crag_0..3` |

测试：`覆盖关系Tests` 七条岩台 Scenario、`棋子向四邻接相邻格提供覆盖Tests`（岩台上的棋子覆盖更远，含 `SourcesOf` 远格来源不相邻）、`高地压制加值Tests`（岩台远格压制，含草地对照）、`新地表的规则标示Tests`（岩台远格的归属、差集可由岩台远格解释、差集可由新地表解释〔岩台 + 沼泽，段 4 补浅滩〕）。
高地压制、空格归属、信物发现都未改代码，经覆盖关系自然继承。
全量：1354 通过 / 2 跳过；Godot 构建 0 警告；截图岩台可辨、不像高一层。
说明：新地表的装饰占用 `BoardView.Build` 的变体计数器，含新地表的地图上其后障碍 / 林地装饰的变体会顺移（纯外观、确定性不变；v5 / frontier-v2 等无新地表的图不受影响）。

## 段 4——浅滩（tasks 4.1–4.4、4.6；4.5 AI 感知与 4.7 的 200 局对照等 ai-eye 归档）

| 文件 | 性质 |
|---|---|
| `Siege.Core/Board/GameBoard.cs` | `GivesLiberty(c)`："空格能否作为气"谓词的唯一落点（为空、可落子、不是浅滩）；`LibertiesOf` 改用它；新增 `EmptyShallowsBeside(group)` 给表现层 |
| `Siege.Core/Board/LifeShape.cs` | 空区起点只取 `GivesLiberty` 的格；flood 碰到空浅滩即判不封闭（不是墙）。不直接比较地表 |
| `Siege.Core/Preview/LibertySnapshot.cs` | `GroupLiberties.ShallowsBeside`（init 属性，取自 `EmptyShallowsBeside`） |
| `Siege.Presentation/Layers/LayerContents.cs`、`Text/Labels.cs` | `LibertyGroupView.ShallowsBeside`；差集 `TerrainReason.Shallows`（被覆盖却不是气的空浅滩，原因在格本身）。**修改前被覆盖的空浅滩会让 ReasonFor 抛异常** |
| `godot/scripts/BoardView.cs`、`LowPoly.cs`、`Visuals.cs`、`PartExport.cs` | 棋串读法把贴着的空浅滩画成更小的暗灰点；`LowPoly.Shallows`：两道近白水纹 + 贴地鹅卵石；导出 `shallows_0..5` |

提子 / 自杀 / 禁入格未改代码：`capture-resolution` 按"气"判定，禁入格由活形分析给出，都经上面两处自然继承。
测试：`气的计算Tests` 五条浅滩 Scenario + `贴着的空浅滩单独列出`；`以整批最终状态判定合法性Tests`（只挨空浅滩的落子是自杀手，含草地对照）；`空区与封闭眼空间Tests`（空浅滩不是眼、贴着空浅滩不封闭、浅滩被己方占据后可封闭、荒漠 / 沼泽 / 岩台可以是眼空间 ×3；夹具加 `s d m p` 字符与 `under` 参数）；`新地表的规则标示Tests`（空浅滩在棋串读法中标为不算气、差集可由空浅滩解释、差集可由新地表解释补浅滩）。守门允许清单加 `GameBoard.cs`。
全量：1369 通过 / 2 跳过；Godot 构建 0 警告；截图浅滩与深水可辨。灰度下浅滩与荒漠都偏亮，靠鹅卵石 / 仙人掌区分——留给 6.2 人工清单判定。
活形性能基线（`Category=Perf`，环境变量门控）本段未跑。

## 收尾（tasks 6.1–6.3）

- 6.1 设计文档：§3.1 地表清单、可落子格、气的定义（空浅滩不算气）、覆盖关系（沼泽源 / 岩台远格）、一句话记法、新增"新地表"一条；§10 领地分注明荒漠不计；
  §14.2 差集来源、棋串读法 / 势力层 / 图例；§20 新地表的形状提示；变更记录追加一行（版本号按与 ai-eye 的归档先后顺延）。四条算例已在 0.7 补入。
- 6.2 人工检查清单 `art/surfaces-v1/README.md`：演示图 `surfaces-demo.json`（v5 中岛改出新地表，另把误落在两格岩石上的地表码清回草地）、
  `surfaces-v1-demo.png` / `-gray.png` / `-closeup.png`（彩色 + 灰度并排）、`surfaces-v1-gen12345-s1.png`，8 个检查项待负责人确认。
- 6.3 验证：段 4 之后未再改 src / tests（工作区对 HEAD 的差异为空），全量测试取段 4 那次：1369 通过 / 2 跳过；`openspec validate terrain-surfaces --strict` 通过；
  Godot 构建 0 警告；`--export-parts` 导出 43 个部件（原 21 + 荒漠 6 + 沼泽 6 + 岩台 4 + 浅滩 6）、18 份共享材质。

仍未完成（裁决 8，等 ai-eye 归档）：1.4 / 2.4 / 3.4 / 4.5 AI 感知测试，3.6 / 4.7 两次 200 局对照；活形性能基线（Perf 门控）未跑。

## 合入 main 前（裁决 9）

- main（ai-eye 段 A–C、life-single-stone 提案）合进本分支（`f53fd38`），无文本冲突；合并后全量 1394 通过 / 4 跳过。
- AI 感知（tasks 1.4 / 2.4 / 3.4 / 4.5）：新文件 `AiDecision/AI对地表的感知Tests`——不去抢荒漠（草地 5 对荒漠 1）、沼泽落子无领地收益（= 1）、
  岩台远格计入收益（= 9）、空浅滩不算安全（`GroupSafety.Liberties` = 2）、剪枝使用唯一查询（预筛口径岩台 9 / 草地 5 = 1 + 覆盖目标数）。
  AI 代码未改：评价、预筛、安全维度都经唯一查询自然感知地表。
- 3.6 / 4.7 的 200 局对照按 ai-eye 裁决 R16 移交其段 D（在"新地表 + R8 新规则"上一次校准并出对照）。
- 全量：1399 通过 / 4 跳过。

## 变异验证（全部实跑、已还原）

| 编号 | 变异 | 结果 |
|---|---|---|
| M-S0a | `IsPlayable` 把浅滩当不可落子 | 红 2 |
| M-S0b | `SurfaceCode(Crag)` 写成 `'D'` | 红 2 |
| M-S0c | 第 9 条漏掉浅滩 | 红 2 |
| M-S0d | `GroupSafety.Analyze` 里写 `== Surface.Marsh` | 守门红 1 |
| M-S5a | `Format` 漏写 `:s1` | 红 5 |
| M-S5b | 投放候选放进林地 | 「投放不动布局」红 2 |
| M-S5c | 块上限 6 → 8 | 「投放规模」红 |
| M-S5d | 浅滩种子不要求挨河 | 「投放规模」红 |
| M-S5e | 「换一张」不改开关 | 红 1 |
| M-S5f（check） | 去掉"同种块不相邻" | 「投放规模」红 4；`:s1` 黄金值红 2（gen:12345:s1 恰好不变） |
| M-S1a | 把荒漠直接从 `ExclusiveCells` 剔掉（当作不独占） | 红 3 |
| M-S1b | `Total` 仍按 `ExclusiveCells.Length` | 红 3 |
| M-S1c | 势力层 `Scored` 恒 true | 「荒漠独占不进领地分」红 |
| M-S2a | `CoverageTargets` 去掉沼泽源 | 红 5 |
| M-S2b | 差集去掉沼泽源分支 | 红 2（抛出） |
| M-S3a | 去掉岩台远格一步 | 岩台测试红 9 / 11 |
| M-S3b | 去掉"m 不是障碍" | 「岩台远格被障碍阻挡」红 |
| M-S3c | 去掉中间格崖壁判断 | 「岩台远格被高崖阻挡」红 |
| M-S3d | 远格不去重 | 「岩台与隔水覆盖重合」红 |
| M-S3e | 不相邻来源恒为隔岸 | 「差集可由岩台远格解释」红 |
| M-S4a | `GivesLiberty` 不排除浅滩 | 浅滩相关测试红 9 / 18 |
| M-S4b | 空区起点与 flood 不排除空浅滩（编译安全写法 `n.X < 0`；第一次写成 `false` 触发 CS0162 编不过，结果作废重跑） | 红 2 |
| M-S4c | 把空浅滩当墙（`continue`） | 「贴着空浅滩不封闭」红 |
| M-S4d | 气快照不填 `ShallowsBeside` | 「空浅滩在棋串读法中标为不算气」红 |
| M-S6a | 领地分改回按 `ExclusiveCells` 计 | 「不去抢荒漠」红 |
| M-S5g（check） | `FrontierSurfaces` 里加一处 `new HashSet<Coord>(…)` | 「生成器源码不含散列次序遍历」红 1 |

check 阶段把 `FrontierSurfaces` 的工作态改为数组 / 列表并纳入生成器源码扫描守门；重构前后 8 张 `:s1` 图与 2 张无 `:s1` 图的 `map --out` 导出逐字节相同。新增 `:s1` 黄金摘要（`新地表投放Tests`）：gen:12345:s1、gen:45:p5:s1、gen:10:p8:s1。

说明：原计划的"投放改用布局子流"不是有效变异——那时布局已定，地图不变；"独立子流"由「新地表投放子流独立于全部尝试子流」钉住。

## 已知限制

- `:s1` 地图上的四种地表**目前没有任何规则效果**（段 1–4 才落地），按草地结算。
- 终端对局画面（`BoardRenderer`）不显示可落子格的地表（林地同样不显示，既有行为）；只有 `map` 子命令的文本图有新字符。
- 灰度可辨依赖段 1–4 的装饰；段 0 只有纯色地砖。
- 投放比例：负责人裁决由 8%–16% 调到 15%–25%（平台外可落子 70–130 格 → 约 11–32 格）。调整后临时扫描种子 1–400 × 平台数 5–8（1600 张）全部满足；`:s1` 三条黄金摘要随之重取。
