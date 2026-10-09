# 10-09-board-link-mode-a 段 A 实施报告（Core 数据层）

对应 `openspec/changes/board-link-mode/tasks.md` 1.1–1.7（已全部勾上）。全部测试见 `tests/Siege.Core.Tests/LinkMapGeneration/`、`tests/Siege.Core.Tests/Battlefield/` 与下表。

## 段 B 接线契约（硬前提，三条并列）

1. `MatchFlow` 对 **`board.LinkedMap`**（开局 + 链接、不含改造）而不是 `board.Map` 调 `LinkMapGenerator.Link(...)`，再 `board.ApplyLink(result.Map)`——静态校验要求棋盘内无栅栏，匠人的栅栏是对局状态，`ApplyLink` 会把已有改造重新落在新图上。
2. 配对事件逐对链接的次序固定为"含最小出生棋盘编号的一对先"（R2）；`LinkMapGenerator.Sequences` 只按此次序核对过。
3. 存档恢复按事件顺序先重放链接、再重放改造；`BaseMap` 仍是开局图（日志首部摘要不变）。

## 0. 需要负责人裁决的事项（按优先级）

| # | 事项 | 现状 | 建议 |
|---|------|------|------|
| R1 | **画布 73×73，不是规格写的"列、行各不超过 50"** | 规格要求任意两块出生棋盘 Chebyshev 间隔 ≥ 15（11×11 公共棋盘 + 两条最短通道）。50 列里一轴最多排 3 个带（5+15+5+15+5 = 45），5 块棋盘只能落在 3×3 槽位；穷举 3×3 的全部 5 槽图案，没有一个能让 10 对棋盘都"用一块公共棋盘 + 两条直通道"接上（Python 穷举 + 几何模拟，随机摆放 50×50 在 0/50 通过）。4×4 槽位需要 3×22 + 7 = 73 列。 | 接受 73（本实现），或改"任意两块都放得下 11×11"为"相邻槽位放得下"（退回 50 需重做生成器）。规格文本 `link-map-generation`「初始布局」尚未改，待裁后同步。若裁回 50，`MapValidator` 链接行的 `ColumnRange / RowRange` 上限（现 80）要同步收到 50，否则校验器守不住（`初始布局Tests.画布固定…` 与 `CanvasSide` 推导式会红，校验器不会）。 |
| R2 | **首轮两次链接的执行次序固定为"含最小出生棋盘编号的一对先"** | 两种次序都要求走得通的话，536 个逐对可接的图案没有一个能过全部序列；固定次序后 32 个过。段 B 的 `ApplyPairing` MUST 按此次序逐对链接。 | 接受为设计约束（写进 battlefield「配对事件」）。 |
| R3 | **通道长度不设上限**（链接档声明行 `CorridorLength (2, 80)`，棋盘档是 2–4） | 非相邻槽位之间的链接通道必然很长：20 个种子全部序列的通道长度中位 8、p90 31、最长 54。3–4 宽 × 几十长的"长巷"是可玩性风险。 | v1 接受并在段 C 冒烟里观察；后续 change 若要缩短，需要改链接几何模型（例如允许拐弯通道或二跳）。 |
| R4 | **可落子格上限 1600**（裁决 A3 原定 1000） | 三块公共棋盘 + 六条长通道会超过 1000（实测 1012 即被校验拒绝）。 | 接受 1600。 |
| R5 | **A5"场景格不可改造"只对搭桥 / 烧林成立为"拒绝"，立栅是"合法但无效果"** | A5 要求"链接档地图上目标格不属于任何棋盘或通道即拒绝"，但 `TerrainEditRules` 既不在规格档读取白名单（`地图规格档Tests.规格档只被地图数据文件格式与校验器引用`），也不许读 `Boards`。曾按"任一端是障碍格的栅栏边即非法"实现（档位无关），v5 / board:1 上 AI 的合法改造候选随之变化，14 条钉死的决策序列基线红（带入带出 ×2、对局内容集、批量跑局、难度分级 ×3、候选格上限、专家前瞻记录 ×3、专家一层前瞻 ×2、停手阈值），已撤回。现状：障碍格上搭桥 / 烧林被拒绝（必须是深水 / 林地），场景格边上的栅栏合法但切不到任何气边，三种动作都不能把不可落子格变成可落子格，"除链接外无合并途径"的不变量由测试守住（`战场的定义与规则作用域Tests.场景格不可改造`）。**规格 Scenario「场景格不可改造」对立栅的字面要求（"系统拒绝该改造"）未满足，测试按实现口径写——测试全绿不等于该 Scenario 字面满足。** | 接受现状；或允许 `TerrainEditRules` 读规格档（需改两条守门测试与 boundaries.md 的单一实现表）；或接受重钉 14 条基线。 |
| R6 | **32 个槽位图案白名单** | 生成只从白名单选图案（主对角线 / 反对角线三块 + 第 0 行一块 + 第 0 / 3 列一块这一族及其镜像），边长 5–7 与槽内偏移随种子变化。地图外观变化不大。 | v1 接受；要更多样需放宽几何模型（见 R3）。 |

## 1. 任务勾选与测试

| task | 状态 | 测试类（`tests/Siege.Core.Tests/`） |
|------|------|------|
| 1.1 `MapProfile.Link`、`MapData.MapVersion`、`MapFile` 往返、非链接档拒绝增长 | 完成 | `LinkMapGeneration/物化状态校验Tests`（规格档读写往返、非链接档不可增长）、`LinkMapGeneration/链接档地图标识与确定性Tests.同种子同初始图` |
| 1.2 战场导出 `Battlefields`（几何四邻连通分量、标识 = 最小出生棋盘编号、按实例记忆化） | 完成 | `Battlefield/战场的定义与规则作用域Tests`（隔绝棋盘各成战场、战场导出按地图实例记忆化、规则不跨战场、旧地图的孤立可落子区不属于任何战场编号）、`BoardTopology/四邻接Tests`（允许名单 ⑥ + 反面断言） |
| 1.3 场景格不可改造 | 完成（见 R5） | `Battlefield/战场的定义与规则作用域Tests.场景格不可改造` |
| 1.4 `LinkMapId` + `LinkMapGenerator.Generate` | 完成（见 R1、R6） | `LinkMapGeneration/初始布局Tests`（初始图只有出生棋盘 ×20、出生棋盘间距 ×20、画布固定且不超过校验器上限 ×20、槽位图案每一对棋盘都能接上）、`链接档地图标识与确定性Tests`（标识解析与规范化、同种子同初始图、对局种子不影响地图） |
| 1.5 `LinkMapGenerator.Link`（公共棋盘 + 双通道、备选棋盘对、退化直通道、信物格位置与档位） | 完成（见 R2、R3） | `LinkMapGeneration/链接时的公共棋盘生成Tests`（首轮链接生成公共棋盘、决赛链接、全部链接序列都能生成、链接序列清单覆盖三类）、`Battlefield/链接事件Tests`（链接合并战场、链接不动已有格、链接可重放） |
| 1.6 物化状态校验 + 链接后重校验 + `GameBoard.ApplyLink` | 完成（见 R4） | `LinkMapGeneration/物化状态校验Tests`（链接后校验、校验失败中止、无出生棋盘的分量即校验失败、可落子格都属于棋盘或通道_场景格不可落子、规格档不改规则）、`Battlefield/链接事件Tests.ApplyLink拒绝改写已有格的地图`、`MapDefinition/边疆档静态校验Tests.校验器对规格档的分支只在声明表里`（更新到四档） |
| 1.7 `MapCatalog.Legacy`、`Resolve` 识别 `link:` | 完成 | `链接档地图标识与确定性Tests`（目录解析link标识、夹具仍可加载_夹具生成不变）；既有 25 个测试文件的 `MapCatalog.Resolve(` 全部切到 `MapCatalog.Legacy.Resolve(`（扫源码字面量的三处除外） |

未做（按任务书属段 B）：信物**内容**子流、链接事件的日志 / 存档、`MatchFlow` 接线。

## 2. 新增 / 修改文件

新增：
- `src/Siege.Core/Board/Battlefields.cs` — 战场导出（几何四邻连通分量，`ConditionalWeakTable` 按 `MapData` 实例记忆化；`Battlefield(Id, Cells, BirthZones, Boards)`）。
- `src/Siege.Core/Board/Maps/LinkMapId.cs` — `link:<种子>` 的解析 / 格式化 / 裸 `link` 识别（照 `BoardMapId` 写法）。
- `src/Siege.Core/Board/Maps/LinkMapGenerator.cs` — 初始布局生成（4×4 槽位、32 图案白名单、边长 5–7、每块 1 信物格、校验闭环含"全部链接序列核对"）、`Link(current, X, Y[, seed])`（公共棋盘 9–11 + 两条 3–4 宽直通道，按（总通道长, 偏好边长差, 偏好宽度差, 行, 列）取最小；最近棋盘对放不下按距离次序试其余对；全失败退化直通道并标 `Degraded`；信物格 2–3 个、首个高档；新图过校验才返回）、`Sequences`（三类序列清单）、`LinkResult` / `GeneratedLinkMap` / `LinkVerification`。
- `tests/Siege.Core.Tests/LinkMapFixtures.cs` — 手写 45×25 链接档图（5 块 5×5 出生棋盘）与"链接 0–1 之后"状态。
- `tests/Siege.Core.Tests/Battlefield/战场的定义与规则作用域Tests.cs`、`链接事件Tests.cs`；`tests/Siege.Core.Tests/LinkMapGeneration/初始布局Tests.cs`、`链接时的公共棋盘生成Tests.cs`、`链接档地图标识与确定性Tests.cs`、`物化状态校验Tests.cs`、`槽位图案诊断Tests.cs`（`[SlowFact]`，约 3 分钟，重算白名单用）。

修改：
- `src/Siege.Core/Board/MapProfile.cs` — 新增 `Link = 3`。
- `src/Siege.Core/Board/MapData.cs` — 新增 `MapVersion`（初始 0，每次链接 +1；`Boards` 即物化棋盘集合）。
- `src/Siege.Core/Board/MapFile.cs` — `MapVersion` 读写（0 省略，负数拒绝）；规格档报错文案含 Link。
- `src/Siege.Core/Board/MapValidator.cs` — 声明表加链接行（可落子 125–1600、信物 5–14、出生区 (5,5)/(25,49)、画布 20–80、通道宽 3–4 长 2–80）；`ProfileRules` 新增 `Connectivity` / `RequireLandmarks` / `BirthZonesMustReachEntrance`；`DistanceHandling.Skip`；`PlateConnectivity.PerComponent` → `ValidateComponentsHaveBirthBoards`（新失败码 `BATTLEFIELD_WITHOUT_BIRTH_BOARD`）。旧三档行为不变。
- `src/Siege.Core/Board/GameBoard.cs` — 新增 `Battlefields`、`LinkedMap`（开局 + 链接、不含改造）、`ApplyLink(grown)`（核对"只增不改"后换图、把已有改造重新落在新图上、重建 `MapTables`）；`Clone` 带 `LinkedMap`。
- `src/Siege.Core/Board/Maps/MapRandom.cs` — `ForLinkAttempt(seed, attempt)`、`ForLink(seed, X, Y)`（新域常量，不改任何 `gen:` / `board:` 图）。
- `src/Siege.Core/Board/Maps/MapCatalog.cs` — `Resolve` 识别 `link:`（裸 `link` 抛 FormatException），其余转交新增的嵌套类 `Legacy.Resolve`（原解析整段搬入；找不到时的提示加入 `link:` 写法）。
- `src/Siege.Core/Board/TerrainEditRules.cs` — 只加类注释说明 A5 的口径（规则代码未变，见 R5）。
- `tests/Siege.Core.Tests/BoardTopology/四邻接Tests.cs` — 几何邻居枚举允许名单加 ⑥ `Battlefields` + 反面断言。
- `tests/Siege.Core.Tests/MapDefinition/边疆档静态校验Tests.cs` — 声明表长度上限 3500、四档字面量、新字段各读一次、`PlateConnectivity` 字面量计数。
- `tests/Siege.Core.Tests/MapGeneration/地图随机源Tests.cs` — 生成器文件名单加 `LinkMapGenerator.cs` / `LinkMapId.cs`。
- `tests/Siege.Core.Tests/SimulationHarness/各入口支持生成图Tests.cs` — 裸标识识别点名单加 `LinkMapId.cs`。
- 25 个测试文件：`MapCatalog.Resolve(` → `MapCatalog.Legacy.Resolve(`。
- `openspec/changes/board-link-mode/tasks.md` — 1.1–1.7 勾上。

## 3. 测试结果

- `dotnet build siege.sln -c Release`：零警告（`TreatWarningsAsErrors` 已开）。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2137 条，通过 2127，跳过 10，失败 0**（跳过的是既有的 Perf / Slow 测试 + 新增的 `槽位图案诊断Tests`）。全量约 2 分钟。
- 新增测试 87 条（含 20 种子 × 3 的 Theory 展开），`gen:` / `board:` 逐字节不变的既有测试（`生成确定性Tests`、`棋盘档生成图标识Tests`、`各入口支持生成图Tests`）照常通过。

变异验证（实跑，只跑 LinkMapGeneration + Battlefield 两组，变异前 `cp` 备份、还原后 `cmp` 逐字节校验 + `touch`）：
- M-A9 `MapValidator.ValidateComponentsHaveBirthBoards` 不加失败项（条件改恒假）→ `物化状态校验Tests.无出生棋盘的分量即校验失败` 红。
- M-A1 `Battlefields.Compute` 的 Id 取 `Max()` → `链接事件Tests.链接合并战场`、`战场的定义与规则作用域Tests.隔绝棋盘各成战场` 红（后者因生成期核对按最小编号找战场而整体失败，连带 30 条红）。
- 两条一并做，共红 30 条，还原后 87 条全绿。

## 4. 退化链接统计（`链接时的公共棋盘生成Tests.全部链接序列都能生成`，种子 1–20）

| 序列类别 | 链接次数 | 退化（无公共棋盘） | 备选棋盘对（不是最近两块） |
|---|---|---|---|
| 四人四区（15 配对 × 规范次序 × 3 步） | 900 | 0 | 4 |
| 2+1+1（10 对 × 3 个同区棋盘 × 2 步） | 1200 | 0 | 0 |
| 3+1（10 对 × 1 步） | 200 | 0 | 0 |

去重后每个种子恰 70 次不同的链接几何；通道长度中位 8、p90 31、最长 54（见 R3）。生成期对每张图跑同一份序列清单，不通过的尝试直接作废，因此"每步通过校验"由构造保证；20 个种子都在缺省 64 次尝试内生成。

## 5. 偏离裁决 / 规格之处

1. 画布 73×73 而非 ≤ 50（R1）。
2. 首轮两次链接固定规范次序（R2）；`Sequences` 只含该次序。
3. 链接档通道长度不限（R3）；A3 的可落子上限 1000 → 1600（R4）。
4. A5 的立栅部分未按"链接档拒绝"实现（R5），改为档位无关的"无效果但合法"，由测试守住不变量。
5. 生成只从 32 个图案白名单里选（R6）；"校验闭环"比棋盘档多一关：全部链接序列核对。
6. `MapCatalog.Resolve` 段 A 仍转交 `Legacy`（按 A4）；`DefaultId` 仍为 v5（Q4 的 `link:1` 缺省属段 C）。
7. 测试中战场 / 出生棋盘编号用 0 起的内部索引（规格 Scenario 写的"战场 1 与 3"对应这里的 0 与 2）。

## 6. 遗留与风险

- 长通道（p90 31、最长 54）的可玩性未知；AI 候选格上限按可落子格数分流（链接后 ≤ 1600 格）需在段 C 冒烟里看耗时。
- `MapCatalog.Resolve("link:N")` 首次生成约 0.3–1 s（含 70 次链接核对），之后按种子缓存；Godot 新局界面"换一张"的手感要在段 D 确认。缓存是进程级静态且无上限（每张图的障碍集约 5000 格），长时间反复"换一张"会累积，段 D 若有感再加上限。
- `GameBoard.LinkedMap` 是段 B 接线点：`MatchFlow` 应对 `board.LinkedMap` 调 `LinkMapGenerator.Link`，再 `board.ApplyLink(result.Map)`；存档恢复按事件顺序重放链接后再重放改造。
- `Battlefield.Id` 对无出生棋盘的分量为 −1（只可能出现在旧地图的孤立可落子区）；链接档上校验器拒绝这种分量。
- 白名单由 `槽位图案诊断Tests`（边长 7、零偏移、种子 1）得出；改链接几何或序列清单后须重跑并同步。
- 规格文本（`link-map-generation`「初始布局」的 50、design D2 的"列、行各 50 以内"）待 R1 裁决后同步，本段未改。

## 主会话复核更新（2026-10-09）

- R1–R6 负责人全部接受；规格文本已同步（画布 73×73、32 种槽位白名单、通道长度不限、可落子 125–1600、链接规范次序、场景格立栅合法但无效果），`openspec validate --strict` 通过。上文 §0 R1 行与 §6 "规格文本未改" 已过期。
- 质检（check.md）自修后：新增 `退化为直通道`、`同链接序列同最终图` 两条测试；`GameBoard.ReplaceMap` 抽共用；`旧地图的孤立可落子区…` 去条件分支。
- 主会话独立复核：`dotnet build siege.sln -c Release` 零错误；`dotnet test tests/Siege.Core.Tests -c Release` **2139 条，通过 2129，跳过 10，失败 0**。
