# 09-22-life-shape 实现记录

## 段 A（tasks 0.1–0.2、1.1–1.5）

### 改了什么

| 文件 | 性质 |
|---|---|
| `src/Siege.Core/Board/LifeShape.cs` | 新增。`LifeState`（Dead 0 / Undetermined 1 / Alive 2）、`EyeSpace`、`GroupLife`、`LifeShapeReport`（`Analyze(GameBoard)` + 查询） |
| `tests/Siege.Core.Tests/LifeShapeFixtures.cs` | 新增。文本盘面夹具 `Grid(rows, heights, fences, bridges)`：首行是最高行号；`.` 空、`#` 岩石、`~` 未架桥深水、`T` 空林地、`0`–`3` 玩家子 |
| `tests/Siege.Core.Tests/LifeShape/空区与封闭眼空间Tests.cs` | 新增 |
| `tests/Siege.Core.Tests/LifeShape/活形三态Tests.cs` | 新增 |
| `tests/Siege.Core.Tests/LifeShape/禁入格Tests.cs` | 新增 |
| `tests/Siege.Core.Tests/LifeShape/活形查询Tests.cs` | 新增 |
| `tests/Siege.Core.Tests/LifeShape/活形分析性能基线Tests.cs` | 新增。1.5 计时（`SIEGE_PERF=1` 门控，`Category=Perf`） |

既有测试改写 / 删除：无。未改预演、Match、AI、表现层。

**查询接口（段 B / C / D 与 `ai-eye` 共用，唯一实现）**

- `LifeShapeReport.Analyze(GameBoard)`：接受任何盘面，包括 `Clone()` 出的预演副本。不接名册，所有者直接从棋子读，所以弃赛 / 出局者的活形照常产生禁入。不缓存，不入存档。
- `Groups`：顺序与 `AllGroups()` 相同，每项含 `Group`、`Life`、`EyeValueSum`、`EyeSpaces`。`EyeSpaces` 是全部封闭空区，按首格坐标排序，每项含 `Owner`、`Cells`、`EyeValue`、`GroupIndices`。
- 按坐标查：`GroupLifeAt(stone)`（给 D4 用：按棋子在副本上所在的棋串重查；空格返回 null，调用方把 null 当作失活）、`EyeSpaceAt(cell)`，以及 `GroupsOf(space)`、`IsProtected(space)`（给「活棋禁入」定位所属活形棋串用）。
- 禁入：`ForbiddenCellsFor(player)` / `IsForbiddenFor(player, cell)`；按所有者分组用 `ProtectedCellsOf(owner)`。盘上没有棋子的玩家同样能查。
- `EyeSpaceMax = 12`：全仓只有这一个字面量。

**算法**：先调一次 `AllGroups()`，建"格 → 棋串下标"数组。起点只取棋串的气，沿 `board.LibertyNeighbors` 做 flood，同一遍收集贴边的棋串。封闭条件：贴边的棋串属于同一玩家、至少贴到一条、格数 ≤ 12。方四判定：4 格里每格恰好有 2 个区内气边邻格（即 4-环）。眼值相加后得出三态。内部只用按 `y*宽+x` 编址的数组和排过序的列表。
早停（不改语义，为 1.5 加的）：格数超过 12，或贴到第二名玩家，就放弃这个空区，已走过的格保留本次 flood 的标记。之后的 flood 一旦碰到别的 flood 留下的标记，就能断定是同一个不可能封闭的空区，也立即放弃。这样每格最多走一次。这条分支由新增的实现守门覆盖，见变异 M-A7。

### 0.1 / 0.2 核对

- 0.1：`openspec list` 只列出 `life-shape`、`ai-eye`，`restore-go-core-rules` 已在 `archive/2026-09-22-restore-go-core-rules`。主规范 `information-visibility`「始终公开的信息」第 3 条是"每个空格的归属（独占及独占者 / 争议 / 中立）"。✔
- 0.2：用脚本把 5 个 MODIFIED 增量（batch-deployment、batch-preview、capture-resolution、information-visibility、turn-sequence）的 Requirement 正文与 `openspec/specs/` 逐行 diff（scratchpad `cmp_modified.py`）。结果：**全部差异都属于活形改动，没有越界差异，主规范原文没有被意外删改。**
  - batch-deployment：原因清单加「活棋禁入」「破坏活形」，定位信息加两句，加 2 个 Scenario（删 2 行，加 10 行）。
  - batch-preview：原因清单同步，加 2 个 Scenario（删 1 行，加 9 行）。
  - capture-resolution：第 1 步加禁入判定，并注明"基于正式盘面"；插入新第 6 步「破坏活形」，原第 6、7 步顺延为 7、8；既有 3 个 Scenario 里"第 6 步"改成"第 7 步"，「自杀手」Scenario 加了"不属于任何活形眼空间"的前提；加所有者豁免段和 8 个 Scenario（删 7 行，加 42 行）。
  - information-visibility：加第 6 条和 1 个 Scenario（删 0 行，加 5 行）。
  - turn-sequence：契约加"扣除禁入格"，加 2 个 Scenario（删 3 行，加 11 行）。
  - `openspec validate life-shape --strict`：valid。

### 新测试清单（默认运行 30 条：26 条对应 Scenario，4 条守门或行为钉子；另有 1 个计时 Theory，门控，默认 Skipped）

- 空区与封闭眼空间Tests：单格眼、贴到敌子不封闭、棋盘边缘与障碍算墙、崖壁与栅栏算墙、未架桥深水算墙、超过上限不算眼空间（含"再落一子变 12 格就成眼"的对照，钉住 ≤12）、林地可以是眼空间、多条棋串共享眼空间（这 8 条对应 Scenario）；没有任何邻子的空区不封闭（spec 封闭条件第 2 条；在当前实现里由"起点只取气"构造保证，作为行为钉子保留）；超过上限的空区从多个起点出发也不算眼空间（早停的实现守门）；活形分析只经气边邻格查询取邻接（1.2 源码扫描守门）。
- 活形三态Tests（12 条 Scenario）：两个单格眼为活、一个单格眼为未定、直二不计、方四为死形、直四为活、被栅栏隔开的2x2不是方四、三格为未定、单格眼加三格眼空间为活、五格为未定、六格为活、状态随盘面重算、活形不入存档（存档 JSON 不含 life / eye / forbid / alive / 活形 / 禁入，`RestoreUnvalidated` 后逐项相同）。
  - 方法名偏差：Scenario「被栅栏隔开的 2×2 不是方四」里的 `×` 和空格不能用在 C# 标识符里，方法名写成 `被栅栏隔开的2x2不是方四`。
- 禁入格Tests（4 条 Scenario）：他人活形的眼空间禁入、未定棋串不产生禁入、弃赛者的活形仍禁入（`MatchFlow.Resign(P3)` 后查 `match.Board`）、活形解除后禁入同步解除。
- 活形查询Tests：预演副本可查询、查询确定（多玩家 + 崖 + 栅 + 深水同盘，期望禁入集合是手算的，写在注释里）；活形分析不直接遍历无序集合（1.4 确定性守门，源码扫描 `\w*HashSet\w*` / `\w*Dictionary\w*` / `\w*Lookup\w*`，正则不以词边界开头）。
- 活形分析性能基线Tests：见 1.5。

**先红**：只写 API 骨架（全部 `NotImplementedException`）时跑 LifeShape 过滤，29 条里 28 红。唯一通过的是确定性扫描守门，因为骨架里本来就没有 HashSet。
**第一次实现后 4 红，逐条处理，都没有改期望值：**
1. `没有任何邻子的空区不封闭`：夹具画错，外部 11 格空区只贴 B 的一枚子，按规则确实对 B 封闭。改法是盘面加一枚 A 子。
2. `未定棋串不产生禁入`、3. `预演副本可查询`：夹具少画 2 行，眼落在了第 3 行而不是第 5 行。改法是补足 7 行。
4. `活形分析只经气边邻格查询取邻接`：扫描正则 `[+-]\s*1\s*\)\s*[,;]` 太宽，误中了 `Array.Fill(x, -1);`。改成方向元组形状 `\(\s*-?[01]\s*,\s*-?[01]\s*\)`；几何偏移仍由 `\.[XY]\s*[+-]\s*1` 和 `new Coord(` 两条挡住。

### 变异逐条

脚本放在 scratchpad（`mutate_a.py` / `mutate_a2.py`），按 testing.md 执行：二进制读写，先探测行尾再归一锚点，`assert count(anchor)==1`，备份名带时间戳，在 `finally` 里还原，还原后与变异前的原始字节逐字节比对，`os.utime` 刷新 mtime，设 `DOTNET_CLI_UI_LANGUAGE=en`，解析 `Failed!/Passed!` 统计行，以退出码为准，不用 `if (false)`。过滤范围是 `LifeShape` + `四邻接Tests`。

| 编号 | 变异 | 红数 | 红的测试 | 还原 |
|---|---|---:|---|---|
| M-A1 | 崖上敌子算邻格：flood 在气边之外再按 `board.Neighbors` 收集贴边棋子 | 3 | 崖壁与栅栏算墙；活形分析只经气边邻格查询取邻接；`四邻接Tests.几何邻居枚举只在允许名单内直接调用`（IL 守门，说明新类型不在白名单里） | 逐字节一致 ✔ |
| M-A2 | 几何 2×2 判方四：4 格外接框为 2×2 即判方四 | 1 | 被栅栏隔开的2x2不是方四 | ✔ |
| M-A3 | 眼值不相加：`Sum` 改 `Max` | 9 | 两个单格眼为活、单格眼加三格眼空间为活、活形不入存档、状态随盘面重算、查询确定、预演副本可查询、他人活形的眼空间禁入、弃赛者的活形仍禁入、活形解除后禁入同步解除 | ✔ |
| M-A4 | 确定性守门自证：注入 `new HashSet<Coord>()` | 1 | 活形分析不直接遍历无序集合 | ✔ |
| M-A5 | 邻接守门自证：注入 `board.Neighbors(...)` | 2 | 活形分析只经气边邻格查询取邻接；几何邻居枚举只在允许名单内直接调用 | ✔ |
| M-A6 | 邻接守门自证：注入 `start.X + 1` | 1 | 活形分析只经气边邻格查询取邻接 | ✔ |
| M-A7 | 早停：碰到别的 flood 的标记时不放弃 | 18 | 超过上限的空区从多个起点出发也不算眼空间，另有 17 条 Scenario（截断后的剩余部分被误判为眼） | ✔ |
| M-A8 | 上限 `>` 改 `>=` | 1 | 超过上限不算眼空间（12 格对照） | ✔ |

每轮还原后复跑都是全绿（40/40、41/41），失败数没有残留，说明跑的不是变异后的旧二进制。
"贴到第二名玩家即放弃"这条早停是等价变异：去掉后 flood 会走完整个空区，再由 `IsClosed` 判出不封闭，结果相同，所以没单独做变异。

### 1.5 性能

中盘定义：Easy 4 人真实对局，种子 7，跑满 40 个小回合（10 个大回合）。预热 5 次后交错计时 31 次，取中位数。运行方式：`SIEGE_PERF=1 dotnet test tests/Siege.Core.Tests -c Release --filter "Category=Perf"`。

| 地图 | 棋子 / 棋串 / 眼空间 / 活形棋串 | `AllGroups` | `AllGroups`+全部 `LibertiesOf` | `Analyze` | 对 `AllGroups` 的比 | 对含气分母的比 |
|---|---|---:|---:|---:|---:|---:|
| siege-4p-base-v5 | 52 / 15 / 21 / 7 | 36.3 µs | 80.6 µs | 110.3 µs | **3.04** | 1.37 |
| siege-frontier-v2 | 65 / 42 / 3 / 0 | 108.5 µs | 270.0 µs | 407.3 µs | **3.75** | 1.51 |

多轮测得的比值：v5 在 3.00–3.14 之间，frontier 在 3.50–3.75 之间。`Analyze` 减去 `AllGroups` 的差：v5 约 75–85 µs，frontier 约 290–360 µs。
**tasks 1.5 的验证条件（≤ 3 倍 AllGroups）没有达到**，原因是结构性的。`Analyze` 本身就包含一次 `AllGroups`，所以比值下限是 1；除此之外它还必须访问棋子周边的空格，而 `AllGroups` 只访问棋子。边疆图有 750 格、65 子、42 条棋串，大多是单子，空格上的 `LibertyNeighbors` 调用次数天然是棋子数的几倍。
已经试过、都不改语义的优化：从气出发、超过 12 格早停、贴到第二名玩家早停、超限立即 break。这些把 frontier 从 4.55 降到约 3.6，再往下已在噪声带内。按裁决 R4，没有加缓存，也没有改阈值。计时测试保留 3 倍断言，但默认套件里是 Skipped；门控打开运行时，它目前会红。

### 段末自验

- `dotnet build siege.sln`：0 警告，0 错误。
- `dotnet test siege.sln -c Release`：通过 1227，跳过 1（性能门控），失败 0。
- `openspec validate life-shape --strict`：valid。

### 待决

1. **1.5 性能阈值**：v5 约 3.0，frontier 约 3.5–3.75，超过 3 倍的宽松上界。两个候选裁决：
   - (a) 按地图分档接受：边疆图上界 ≤ 4；
   - (b) 把分母改成"`AllGroups` + 全部 `LibertiesOf`"，也就是含气的全盘棋串计算，这样两图分别是 1.37 和 1.51。
   
   这项影响 AI 每回合的预演成本。段 B 接入预演后，应按 R4 用 AI 单回合耗时（与 ① 比，是否慢 2 倍以上）复核。
2. v5 中盘（40 小回合）已有 7 条活形棋串、21 块眼空间。活形在 v5 上形成得早，印证了 design 里的风险"活形过易形成"。这只是一个观察，留给段 D 的基线数据判断。
3. 性能测试的门控用的是环境变量加 `[Trait("Category","Perf")]`。R3 说的 2.3 种子 1–200 慢测试"以 Category 单独运行"，段 B 可以沿用同一个 `PerfTheory` / Trait 形状，也可以另定约定，由主会话决定。

## 段 B（tasks 2.1–2.6，外加段 A 收尾 R6 / R7）

### 改了什么

| 文件 | 性质 |
|---|---|
| `src/Siege.Core/Batch/BatchFailure.cs` | 新增 `BatchFailureKind.LifeForbidden`（活棋禁入）、`BreaksLife`（破坏活形）。`BatchFailure` 加三个 init 属性：`LifeGroup`（活形棋串坐标，批次开始前、坐标序）、`LifeOwner`、`Triggers`（破坏活形时为本批全部暂放）。活棋禁入的 `Coords` 是违规落点单格；破坏活形的 `Coords` 是受影响棋串批次开始前的全部坐标 |
| `src/Siege.Core/Batch/BatchRehearsal.cs` | 七步改八步。`Rehearse` 开头对正式盘面做一次 `LifeShapeReport.Analyze`，第 1 步与第 6 步共用，不跨调用复用。第 1 步在"地形可落子"之后、"合法落子范围"之前查 `IsForbiddenFor`，所有者不受限。第 5 步提子之后插入第 6 步 `BrokenLife`：只复查批次开始前已活、且所有者不是行动方的棋串，按原棋子在副本上所在棋串重查；棋子不在副本上（`null`）即失活。没有这样的棋串时不分析副本。原第 6、7 步顺延为 7、8。public `ValidateShape`（`StagedBatch.Stage` 用）签名不变，内部自行分析一次 |
| `src/Siege.Core/Match/MatchFlow.cs` | `LegalRangeFor` 在出生区 / 全图两种范围上都扣除 `ForbiddenCellsFor(player)`，全仓只有这一处扣除；`Publish` 在同一份 `Board.Clone()` 上做活形分析 |
| `src/Siege.Core/Match/MatchPublicView.cs` | 末尾新增位置参数 `LifeShapeReport LifeShape`，用来公开活形状态、眼空间和各玩家禁入格。全仓只有 `Publish` 构造它 |
| `src/Siege.Presentation/Preview/PreviewPresentation.cs` | `TitleOf` 加两行标题"活棋禁入""破坏活形"。这是段 C 3.1 的提前最小落地：`TitleOf` 是穷举 switch，不加就会抛异常 |
| AI（`HeuristicTurnController` 等） | **未改**。候选格来自 `context.LegalRange`，已经扣除禁入格；两类新原因走既有的 `OnRejected`（撤掉最后一枚再试） |
| `tests/Siege.Core.Tests/LifeShape/活形分析性能基线Tests.cs` | R6：分母改成 `AllGroups` + 全部 `LibertiesOf`，上界仍为 3 |
| `.trellis/spec/core/testing.md` | R7：新增一节「慢测试与计时测试：默认跳过，环境变量 + `Category` 才运行」（`[PerfTheory]` / `SIEGE_PERF=1` / `Category=Perf`；`[SlowFact]` / `SIEGE_SLOW=1` / `Category=Slow`；慢测试必须在默认套件里留一份缩小版） |

### 既有测试的改写 / 删除（逐条）

没有删除任何测试。改写 7 条：

1. `LifeShape/活形分析性能基线Tests`：R6 改分母，方法名 `中盘全盘活形分析不超过棋串计算的三倍` 改为 `中盘全盘活形分析不超过含气棋串计算的三倍`。
2. `TurnSequence/合法落子范围的对外契约Tests.范围随大回合切换`：规范改为"二者都不含该玩家的禁入格"。夹具加了 P2 在出生区 1 角上的两眼活形（眼 G1、J1）。期望值由 9 / 81 改为 7 / 79，并加了"P2 自己的范围含这两格"的断言。
3. `BatchDeployment/非法批次必须给出可定位的原因Tests.七类失败各有独立类别` 改名为 `九类失败各有独立类别`，按规范的原因清单加入两个新类别。
4. `BatchPreview/非法批次必须说明原因并高亮Tests.十类失败标题互不相同` 改名为 `十二类失败标题互不相同`，数量 10 改为 12（枚举新增两项）。
5. `AiDecision/候选格上限Tests` 的黄金哈希由 `49BCFA49…11DEA30C` 改为 `F1B2CAB6…4ACEB088`。改动前后的二进制各跑种子 31、24 个小回合，快照去掉耗时后前 15 个小回合逐条相同。第 16 个小回合（第 4 大回合、P0 第一手全图落子）起分叉：旧落点 M8、E9 在新规则下正是 P0 的禁入格（临时探针实测该时刻 P0 有 39 个禁入格）。新值连跑两次一致；M-K1 在新值下仍红（见变异表）。另外，M-B7（契约不扣除）下本测试仍绿，说明分叉来自预演拒绝，而不是契约扣除。
6. `MatchTelemetry/地形改造日志与分析Tests.地形可离线重建`：只换样本。新规则下种子 3–5 致提子为 0 / 0 / 0，样本口径下界响亮失败。用同一份写死权重重扫种子 1–24，致提子只剩种子 10、18、19、23 各 1 次；改取连续的 17–19（改造 2 / 2 / 3 次，致提子 0 / 1 / 1 次）。断言与期望未改。
7. `SimulationHarness/终端对局Tests.脚本输入能落子并走到输入耗尽`：脚本由 Pass 两次改为 Pass 一次。新旧二进制逐行比对，前 3 个大回合相同；第 4 大回合 AI 走法分叉，玩家2 在轮到人类之前提走了人类唯一的 B1，人类出局后对局自动跑到终局，永远走不到"输入耗尽"。改为在第 3 大回合提示处耗尽后，保护期内别家进不了人类的出生区，不再依赖 AI 走法。断言未改。（先试了换种子：43、44、45 都在第 4 大回合被提，所以放弃换种子。）

### 新测试清单（17 条：默认 16 条，另 1 条慢测试门控）

- `CaptureResolution/以整批最终状态判定合法性Tests` 新增 9 条。前 8 条对应 Scenario：同时填两眼被禁入拦下、未定棋串的眼可以进、立栅切开活形被拒、立栅隔开眼与棋子被拒、搭桥漏眼被拒、围死活形被拒、不影响活形的改造合法、所有者可以拆自己的眼。第 9 条 `提子后恢复活形的批次合法` 钉住第 5 步在第 6 步之前：A 在正式盘面上是活、提子前（手工 Clone + 放置 + 改造）是未定、提子后回到活，三个盘面都有断言。
- `BatchDeployment/非法批次必须给出可定位的原因Tests` 新增 2 条 Scenario：活棋禁入返回落点与棋串（走真实 `MatchFlow` 暂放，抓"禁入排在范围之后"）、破坏活形返回受影响棋串（`Coords` / `LifeGroup` / `LifeOwner` / `Triggers`）。
- `TurnSequence/合法落子范围的对外契约Tests` 新增 3 条：禁入格不在范围内、共享出生区内的禁入（这两条对应 Scenario），以及守门 `禁入只在契约一处扣除且与预演共用同一查询`。守门分两部分：一是源码扫描 Siege.Core + Siege.Sim，文件数下界 > 100，`ForbiddenCellsFor(` 只在 MatchFlow.cs、`IsForbiddenFor(` 只在 BatchRehearsal.cs；二是行为比对，全图范围下逐格单子预演，得到"活棋禁入"的格集合恰好等于契约扣掉的格集合。
- `InformationVisibility/始终公开的信息Tests.活形状态公开`：四名观察者读到活 / 未定 / 死三态、眼空间和四人禁入格，且与在 `view.Board` 上独立重算的结果逐项相同。既有的隐藏信息守门（`必须隐藏的信息Tests`、`正式对战AI的信息边界Tests`，含 `ReachableTypes` 闭包）保持绿。
- `CaptureResolution/活形保护性质Tests`（2.3 性质守门）。每个种子在 13×13 盘面上摆 4 个活形模板（两单格眼环、带深水墙的 6 格眼、直四、贴岩石的一字两眼），其余格随机撒子、深水和林地；然后跑 40 个随机批次，八成落点在他人活形两格以内，七成是带随机合法改造的匠人。每个确认成功的批次后，逐子核对批次开始前的非己方活形。样本口径下界：累计的受保护核对、BreaksLife 拒绝、LifeForbidden 拒绝都必须大于 0。
  - 默认 `[Fact]` 跑种子 1–20：批次 800，确认 472，受保护核对 935，拒绝 LifeForbidden 240 / BreaksLife 15 / Suicide 73，耗时 1.2 s。
  - `[SlowFact]` + `Category=Slow` 跑种子 1–200（`SIEGE_SLOW=1 dotnet test tests/Siege.Core.Tests -c Release --filter "Category=Slow"`）：批次 8000，确认 4793，受保护核对 9199，拒绝 LifeForbidden 2468 / BreaksLife 137 / Suicide 602，**无反例**，耗时 7.8 s。
- 新增特性 `SlowFactAttribute`（与 `PerfTheoryAttribute` 同形）。

**先红**：只加了枚举和工厂骨架、没有判定逻辑时，跑新测试 12 条红：5 条应拒的 Scenario（同时填两眼、立栅切开、立栅隔开、搭桥漏眼、围死），2 条定位 Scenario，4 条契约用例（含改写后的范围随大回合切换和守门），以及性质测试（有反例）。按设计本来就应该绿的 5 条是绿的：未定可进、不影响、所有者拆眼、提子后恢复，以及 2.5 已经接上的公开视图。

### 变异（逐条；脚本 scratchpad `mutate_b.py`，每条跑整个测试工程）

执行口径同段 A：二进制读写，先探测行尾，`assert count==1`，备份名带时间戳，在 `finally` 里还原；还原后与原始字节逐字节比对，用 `os.utime` 刷新 mtime；`DOTNET_CLI_UI_LANGUAGE=en`，解析统计行。全部 11 条 `restored=True`。跑完后 `git diff` 与变异前逐字节相同，复跑 0 失败、1243 通过。

| 编号 | 变异 | 红数 | 红的测试 |
|---|---|---:|---|
| M-B1 | 第 6 步挪到提子之前 | 2 | 提子后恢复活形的批次合法、围死活形被拒 |
| M-B2 | 第 1 步对所有者也施加禁入 | 3 | 所有者可以拆自己的眼、候选格上限黄金哈希、对局日志的记录内容.揭示时间可查 |
| M-B3 | 去掉第 6 步 | 6 | **活形保护性质（种子 1–20）**、立栅切开、立栅隔开、搭桥漏眼、围死、破坏活形返回受影响棋串 |
| M-B4 | 第 6 步不豁免所有者 | 3 | 所有者可以拆自己的眼、黄金哈希、地形可离线重建 |
| M-B5 | 第 6 步把"原棋子不在副本上"当作通过 | 2 | 围死活形被拒、活形保护性质 |
| M-B6 | 活棋禁入排在合法落子范围之后 | 1 | 活棋禁入返回落点与棋串 |
| M-B7 | 契约不扣除禁入格 | 4 | 范围随大回合切换、禁入格不在范围内、共享出生区内的禁入、禁入只在契约一处扣除… |
| M-B8 | 守门自证：AI 里自行 `ForbiddenCellsFor` | 1 | 禁入只在契约一处扣除且与预演共用同一查询 |
| M-B9 | 活棋禁入不带 `LifeGroup` | 1 | 活棋禁入返回落点与棋串 |
| M-B10 | 公开视图的活形取自空盘 | 1 | 活形状态公开 |
| M-K1 | 候选格预筛恒启用（在新黄金哈希上复核） | 38 | 含 缺省不限制时标准图整局与改动前逐步相同 |

### 性能（R4 / R6）

- AI 单回合耗时：`Siege.Sim run --seed 1 --count 10 --serial`，v5、4 名 Standard AI，用小回合快照的 `ElapsedMs`。改动前的二进制复制一份，与改动后交错跑：

| 运行 | 小回合数 | 平均 | 中位 | P90 |
|---|---:|---:|---:|---:|
| 改动前 ① | 1041 | 139.9 ms | 114 ms | 282 ms |
| 改动后 | 682 | 218.3 ms | 174 ms | 386 ms |
| 改动前 ②（改动后之后再跑） | 1041 | 168.8 ms | 132 ms | 354 ms |

  中位数的比值为 1.32–1.53，平均数的比值为 1.29–1.56，**没有超过 2 倍**，未加缓存。注意两边对局不同：新规则下 10 局只有 682 个小回合，旧规则是 1041 个。
- 边疆图（`run --map siege-frontier-v2 --seed 1 --count 3 --turn-limit 120 --serial`，缺省候选格上限生效，每个候选格都要走 `Stage` + 预演；三局都截断在 120 个小回合，两边都是 360 个小回合），同样交错跑：

| 运行 | 平均 | 中位 | P90 |
|---|---:|---:|---:|
| 改动前 ① | 1060.9 ms | 941 ms | 1411 ms |
| 改动后 | 1457.9 ms | 1356 ms | 1884 ms |
| 改动前 ② | 1152.4 ms | 1037 ms | 1604 ms |

  中位数的比值为 1.31–1.44，平均数的比值为 1.27–1.37，**没有超过 2 倍**。
- 1.5 基线（R6 口径，`SIEGE_PERF=1 --filter Category=Perf`，两条都绿）：v5 的 `Analyze` 为 107.3 µs，含气分母为 97.3 µs，比值 **1.10**（只对 `AllGroups` 为 2.48）；frontier-v2 为 381.9 µs 对 285.3 µs，比值 **1.34**（只对 `AllGroups` 为 3.31）。

### 2.6 50 局（`run --seed 1 --count 50 --retention Full`，v5，4 名 Standard AI，并行 28）

- 50 局全部完成：`Failed 0`、`Capped 0`、`Truncated 0`，`FailedFiles` 为空，终局原因都是整轮 Pass（AllPassed；改动前种子 1–10 也全是）。共 2826 个小回合、796 个大回合，墙钟 153 s。
- AI 预演中被拒（`Rehearsal` 事件）：**活棋禁入 0**，破坏活形 4190，自杀手 64610。
- 确认被拒（`Rejected` 事件）：**活棋禁入 0，破坏活形 0**。
- 破坏活形只出现在 AI 的候选预演里。AI 的贪心已经把这些候选撤回，没有任何一次走到确认。

### 段末自验

- `dotnet build siege.sln`：0 警告、0 错误。`dotnet build src/godot/Siege.Godot.csproj`：0 警告、0 错误。
- `dotnet test -c Release`：通过 1243，跳过 2（Perf 门控 1 个 Theory、Slow 门控 1 条），失败 0。
- `openspec validate life-shape --strict`：valid。

### 待决

1. **活形在 v5 上形成得早而且多（段 A 待决 2 的加强版）**。种子 31 第 16 个小回合（第 4 大回合）时盘上已有 18 条活形棋串，P0 有 39 个禁入格。其中 13 条是单子。机制：v5 的岩石、深水切出很多小空区，只贴一枚子就封闭；一枚子贴两个这样的 1 / 3 / 5 格空区（各眼值 1），或贴一个 4 格非方四 / 6–12 格的空区（眼值 2），就已确定活形。结果是：
   - 同样种子 1–10，小回合总数由 1041 降到 682（−34%）；
   - 带写死权重时，"致提子的改造"原来种子 3–5 三局共 4 次，现在种子 1–24 二十四局共 4 次。

   这是规则表的直接后果（D1 无气边即墙 + D2 眼值相加）。本段没有改数值。是否要为"贴地形的小空区"另设约束，留给段 D 的 200 局基线数据和负责人裁决。
2. 破坏活形在 AI 预演中出现 4190 次（每局约 84 次），是一笔可观的预演浪费。AI 评价与候选过滤属于 `ai-eye` 范围，本段没有动。
3. `PreviewPresentation.TitleOf` 的两行标题是段 C 3.1 的提前最小落地，高亮与详情文案仍归段 C。
4. 活棋禁入的 `Coords` 只含落点；所属活形棋串放在 `LifeGroup`，所有者放在 `LifeOwner`。破坏活形的 `Triggers` 是本批全部暂放，因为结果导向检查不归因到单枚。段 C 做高亮时按这个形状取数；如果要换形状，请在段 C 开工前裁决。
5. 守门 `禁入只在契约一处扣除且与预演共用同一查询` 的源码扫描范围是 Siege.Core + Siege.Sim，断言 `ForbiddenCellsFor(` 只在 MatchFlow.cs 出现。段 D 4.2 要算"终局禁入格占比"，`BalanceAnalyzer` 读 `view.LifeShape.ForbiddenCellsFor(p)` 属于读取、不是扣除，但这个守门会翻红。段 D 开工时需要二选一：把守门收窄成"从范围里扣除（`.Except(…ForbiddenCellsFor`）只有一处"，或者给分析器加白名单。Siege.Presentation 不在扫描范围，段 C 读公开视图不受影响。
6. 段末发现工作树里有**不属于本段**的改动：`src/godot/scripts/GameRoot.cs`（+12 行），以及未跟踪的 `src/godot/parts/`、`src/godot/scripts/PartExport.cs`。它们在 18:15 之后出现，本段没有触碰；上面的 Godot 构建 0 警告是包含它们一起构建的结果。提交时请主会话把它们和本段分开。

## 段 C（tasks 3.1–3.4）

### 改了什么

| 文件 | 性质 |
|---|---|
| `src/Siege.Presentation/Preview/PreviewPresentation.cs` | `HighlightKind` 加 `LifeGroup`。`FailurePresentation` 末尾加位置参数 `EdgeHighlights`、`LifeOwner`。`From` 拆成三支：`Plain`（原逻辑）、`LifeForbidden`（落点为 FailureFocus，`LifeGroup` 整串为 LifeGroup）、`BreaksLife`（受影响棋串为 LifeGroup；整批落点与格目标改造为 FailureFocus；立栅目标走边高亮 FailureFocus）。`PreviewPresentation.Build` 把失败的边高亮并入 `EdgeHighlights`。定位形状沿用 R9 |
| `src/Siege.Presentation/Layers/LayerContents.cs` | `LibertyGroupView` 末尾加 `LifeState Life`，取自 `world.View.LifeShape.GroupLifeAt`，取不到时抛出（两份快照不同源）。新增 `Mark`：已活优先于危险；新增 `MarkText`。新增枚举 `GroupMark`（Normal / Danger / Alive） |
| `src/Siege.Presentation/Visibility/DefaultBoardView.cs` | `BoardCellView` 末尾加 `PlayerId? LifeForbiddenBy`：当前行动玩家的禁入格，值为所有者。集合来自 `View.LifeShape.ForbiddenCellsFor(current)`，所有者来自 `EyeSpaceAt(c).Owner`。新增 `Block` 与枚举 `PlacementBlock`（None / Terrain / LifeForbidden）。范围外不在此枚举里，因为范围来自契约，默认棋盘不推断 |
| `src/Siege.Presentation/Style/VisualBaseline.cs` | `HighlightStyle.LifeOutline` 及其 `StyleOf` / `LayerOf` 映射。`GroupMarkShape` 与 `GroupMarks.ShapeOf`：虚线环 / 实线环 / 实线环加悬浮眼徽记，形状是第一通道。`PlacementMarkStyle` 与 `PlacementBlocks`（`StyleOf`、`OutOfRangeStyle`、`ReasonText`） |
| `src/Siege.Presentation/Text/Labels.cs` | `GroupMark(mark)`：已活 / 危险 / 空 |
| `src/Siege.Presentation/Camera/CameraInput.cs` | `HoverReadout.Of(Coord?, DefaultBoardView)` 重载：指向禁入格时输出 "E5 · 活棋禁入（红方(P0)）"。原来的单参 `Of` 保留 |
| `src/Siege.Sim/Play/BoardRenderer.cs` | 已活棋串的子在类型字母后加 `@`（如 `1B@`）；禁入格记作 `xN`（N 为所有者的玩家号），优先级排在信物 / 区号 / `+` 之前；图例加一行。禁入格按"他人各自的 `ProtectedCellsOf`"取，原因见待决 1 |
| `src/godot/scripts/BoardView.cs` | `DrawLifeSeals`：默认棋盘上当前行动玩家的禁入格画成压暗底 + 所有者阵营色方框 + 叉。`DrawLiberties` 按 `GroupMarks.ShapeOf` 选图元，已活为已活色实线环 + 悬浮 `TorusMesh` 眼徽记（`AddEyeBadge`）。`DrawPreview` 加 `LifeOutline` 一支和失败边高亮（警示色亮栏）。`AddCross` 加可选 parent 参数 |
| `src/godot/scripts/Visuals.cs` | 新增 `Alive`、`ForbiddenShade` 两色 |
| `src/godot/scripts/GameRoot.cs` | 只加本段自己的 hunk，**别人的 `--export-parts` 两段（现第 100–102、104–112 行）逐字未动**。本段 hunk 按当前行号：第 53 行字段 `_shotGroups`；第 82–84 行解析 `--shot-groups`；第 428–436 行 `BeginCapture` 按 `--shot-groups` 打开盘面层棋串读法；第 439–445 行截图取景自证打印 `[life-shape]`；第 453 行 `RefreshViews` 刷新悬停读数；第 855 行 `UpdateHover` 改用带棋盘的 `HoverReadout.Of`（**唯一一处改动的既有行**）。与段 C 开工前的快照做 difflib 比对：删 1 行、加 22 行。`PartExport.cs` 与 `src/godot/parts/` 的 SHA-256 全部未变 |

活形与禁入在表现层只读公开视图：Presentation 读 `View.LifeShape`，不调 `Analyze`；Godot 一处都不碰 `LifeShapeReport`，只读 Presentation 视图模型。

### 既有测试改写（逐条）

没有删除，也没有改期望值。

1. `BatchPreview/Godot层不含规则计算Tests.Godot层不调用规则计算入口`：禁用 token 表追加 `"LifeShapeReport"`、`".LifeShape."`（3.4 守门，变异 M-C2 已证红）。
2. `BatchPreview/非法批次必须说明原因并高亮Tests`：只加 `using`（`Siege.Core.Match`、`Siege.Presentation.Text`）和新方法，既有 4 个方法未动。`十二类失败标题互不相同` 不需要改。

### 新测试（14 条，全部默认运行）

- `BatchPreview/非法批次必须说明原因并高亮Tests` 加 3 条：
  - 活棋禁入说明、破坏活形说明：这两条对应 Scenario。破坏活形走预演与确认两条路径，钉住边高亮。
  - 破坏活形的格目标改造高亮在格上：搭桥目标走格高亮。
- `TacticalLayers/活形与禁入格的标示Tests`（新类）加 6 条：
  - 对应 3 个 Scenario：已活棋串可辨、禁入格在默认棋盘上可见、所有者看到的是可落子；
  - 已活棋串即使气少也标已活：钉标记优先级；
  - 禁入格区别于地形不可落子与范围外：保护期内眼格同时在范围外时仍标禁入，与预演第 1 步同序；另验岩石和别家出生区；
  - 守门 `表现层不调用活形分析只读公开视图`：IL 扫描确认没有 `LifeShapeReport.Analyze`，并反面断言扫描器确实看得见 `MatchPublicView.get_LifeShape`、`GroupLifeAt`、`ForbiddenCellsFor`。
- `SimulationHarness/终端活形与禁入标示Tests`（新类）加 5 条：
  - 文本盘面标出禁入格与已活棋串：逐格断言，并与测试侧独立取的 `ForbiddenCellsFor(P1)` 逐项相等；
  - 所有者看自己的眼不标禁入；
  - 未定棋串不标已活；
  - 图例含禁入与已活符号；
  - 脚本对局里出现禁入与已活标示：真实 `PlayCommand.Run`，种子 31，Standard 难度。样本口径下界要求棋盘行至少 20 行，且出现 `@` 与 `x`。

**先红**：先加 API 骨架，返回值全部是默认值（`Life = Dead`、`LifeForbiddenBy = null`、边高亮为空、`HoverReadout` 不带原因、终端不改）。这时跑相关 7 个类共 28 条，12 红。按设计本来就绿的 4 条：所有者看到的是可落子、未定棋串不标已活（骨架默认值恰好正确），以及 Godot 守门、UI 守门（此时源码里还没有违规）。实现后全绿，没有改过期望值。

### 变异（脚本 scratchpad `mutate_c.py`，每条跑整个测试工程）

口径同段 A / B：二进制读写，先探测行尾，断言锚点命中恰为 1 次，备份名带时间戳，在 `finally` 里还原，还原后逐字节比对，用 `os.utime` 刷新 mtime；设 `DOTNET_CLI_UI_LANGUAGE=en`，解析统计行；条件变异只用运行时恒假的条件。14 条全部 `restored=True`。跑完后 `git diff` 与 `git status` 和变异前逐字节相同（cmp），复跑 1257 通过 / 2 跳过 / 0 失败。

| 编号 | 变异 | 红数 | 红的测试 |
|---|---|---:|---|
| M-C1 | Presentation 棋串读法改为自调 `LifeShapeReport.Analyze(world.View.Board)` | 1 | 表现层不调用活形分析只读公开视图 |
| M-C2 | Godot `DrawLifeSeals` 注入 `LifeShapeReport.Analyze` | 1 | Godot层不调用规则计算入口 |
| M-C3 | 默认棋盘禁入改取全部受保护眼空间，对所有者也标 | 2 | 所有者看到的是可落子、表现层不调用活形分析只读公开视图（反面断言失去 `ForbiddenCellsFor`） |
| M-C4 | 棋串读法的活形恒为 Dead | 2 | 已活棋串可辨、已活棋串即使气少也标已活 |
| M-C5 | 标记优先级改为危险先于已活 | 1 | 已活棋串即使气少也标已活 |
| M-C6 | 活棋禁入不高亮所属活形棋串 | 1 | 活棋禁入说明 |
| M-C7 | 破坏活形丢掉立栅边高亮 | 1 | 破坏活形说明 |
| M-C8 | 预演呈现不并入失败的边高亮 | 1 | 破坏活形说明（预演路径） |
| M-C9 | 破坏活形不高亮格目标改造 | 1 | 破坏活形的格目标改造高亮在格上 |
| M-C10 | `HoverReadout` 不给所有者 | 1 | 禁入格在默认棋盘上可见 |
| M-C11 | 已活与危险同形（`ShapeOf(Alive)=SolidRing`） | 1 | 已活棋串可辨 |
| M-C12 | `Block` 不看 `LifeForbiddenBy` | 2 | 禁入格在默认棋盘上可见、禁入格区别于地形不可落子与范围外 |
| M-C13 | 终端对自己的眼也标禁入 | 1 | 所有者看自己的眼不标禁入 |
| M-C14 | 终端不标已活 | 3 | 文本盘面标出禁入格与已活棋串、所有者看自己的眼不标禁入、脚本对局里出现禁入与已活标示 |

### 3.3 人工看一局

`sim-out/life-shape/terminal-sample.txt`（58 行）。

- 命令：`Siege.Sim play --seed 31 --players 4 --seat 1 --difficulty Standard`（v5 图），输入为"选 1 号区，之后每个小回合 Pass"。
- 完整输出 617 行、18 个盘面，其中 17 个同时出现 `@` 和 `x`。样本摘了首个（第 1 大回合）和末个（第 9 大回合）盘面，都带状态栏与图例。
- 第 1 大回合时，玩家4 的三枚单子堡垒已经各自成活（R8 现象）。

### 3.4 Godot 自检（最终 Debug 构建后重跑）

`$G = D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`

| 命令（`$G … --path src/godot …`） | 退出码 | 要点 |
|---|---:|---|
| `--headless --quit-after 20000 -- --auto-demo` | 0 | 开局对准自检通过；日志里没有 exception / error |
| `--headless --quit-after 20000 -- --auto-demo --pick-check` | 0 | 105/105，通过 |
| `--headless --quit-after 40000 -- --auto-demo --map=siege-frontier-v2` | 0 | 开局对准 81/81 |
| `--headless --quit-after 40000 -- --auto-demo --pick-check --map=siege-frontier-v2` | 0 | 411/411，通过 |
| `-- --auto-demo --rounds=8 --screenshot=…/v5-default.png:95` | 0 | 取景：第 9 大回合，当前行动 P1，禁入格 35，已活棋串 32，棋串读法关 |
| `-- --auto-demo --rounds=8 --shot-groups --screenshot=…/v5-groups.png:95` | 0 | 同上，棋串读法开 |
| `-- --auto-demo --map=siege-frontier-v2 --rounds=8 --screenshot=…/frontier-default.png:95` | 0 | 第 8 大回合，当前行动 P0，禁入格 34，已活棋串 29 |
| `-- --auto-demo --map=siege-frontier-v2 --rounds=8 --shot-groups --screenshot=…/frontier-groups.png:95` | 0 | 棋串读法开 |
| 同上再加 `--overview`，存为 `frontier-groups-overview.png` | 0 | 全局预览 |

截图共 5 张，都在 `sim-out/life-shape/`，1600×900，没有读进上下文，供负责人过目。截图时手牌面板和中央面板都是关的。

### 段末自验

- `dotnet build siege.sln`：0 警告、0 错误。
- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告、0 错误。
- `dotnet test siege.sln -c Release`：通过 1257，跳过 2，失败 0。
- Godot 自检：见上表，全部退出码 0。
- `openspec validate life-shape --strict`：valid。

### 待决

1. **终端的禁入格取法**。段 B 的守门 `禁入只在契约一处扣除且与预演共用同一查询` 扫描 Siege.Core + Siege.Sim，要求 `ForbiddenCellsFor(` 只出现在 MatchFlow.cs、`IsForbiddenFor(` 只出现在 BatchRehearsal.cs。终端在 Siege.Sim 里，调哪一个都会让它翻红，所以改用"除我之外每名玩家的 `ProtectedCellsOf(p)`"。这等于把"他人受保护的眼格 = 我的禁入格"复述了一遍，一致性由测试 `文本盘面标出禁入格与已活棋串` 钉住（测试侧取 `ForbiddenCellsFor(P1)` 逐项比对）。建议段 D 按段 B 待决 5 的方案收窄守门（只管"从范围里扣除"），届时终端改调 `ForbiddenCellsFor(me)`。Presentation 不在那条守门的扫描范围内，直接读 `ForbiddenCellsFor`。
2. **默认棋盘的禁入标示对象**是"当前行动玩家"（规格原文）。Godot 在 AI 行动时显示的是那名 AI 的禁入格。另外，Godot 本来就没有合法落点标记（既有状况，不是本段引入的），所以"范围外"在画面上就是"无标记"。数据层三类互斥已有测试钉住。如果希望改成始终按本机玩家显示，需要裁决。
3. **悬停提示进不了截图**：无人值守模式下 `UpdateHover` 不执行，这是既有设计。悬停只由 `HoverReadout` 单测与变异 M-C10 覆盖。
4. **截图的像素验收没能做成判据**。打开信息层后场景降饱和到 45%，已活色和草地在颜色窗口上分不开，数像素的结果不能作为依据。取景由 `[life-shape]` 打印的视图模型读数自证（禁入格数、已活棋串数、棋串读法开关）。已活标记的外观要请负责人看 `v5-groups.png` / `frontier-groups.png`。
5. **标记优先级**：已活优先于危险（两眼活形往往只有两口气）。这是本段的实现选择，已由测试与变异 M-C5 钉住，请确认。
6. **R8 数据点**：Godot 自动演示第 9 大回合（v5，种子 20260915）有 32 条已活棋串，当前行动玩家有 35 个禁入格；边疆图第 8 大回合是 29 条 / 34 个。终端样本里，第 1 大回合就有单子堡垒成活。
7. 新增了一个 Godot 命令行选项 `--shot-groups`，仅用于截图时打开棋串读法。它走 `LaunchArgs` 的合法选项集合，未知选项照旧退出码 1。
8. **终端里禁入格会盖住信物标记**。if 链里禁入格排在信物之前，眼空间格如果恰好是信物格，`xN` 会盖掉 `?` / `p` 等标记，公开的信物状态在文本盘面上就看不到了（已揭示信物仍列在状态栏的"已揭示信物"行）。3 字符的格宽放不下两个标记，这是有意的取舍。如果要改为信物优先，需要裁决。

## 段 D（tasks 4.1–4.6，外加段 C 收尾 R12 / R13）

### 改了什么

| 文件 | 性质 |
|---|---|
| `src/Siege.Core/Batch/StagedBatch.cs` | 新增 `Refusals`（`StageRefusal(Tried, Failure)` 列表）：暂放 / 换位 / 替换被拒时追加，`Clear` 不清。活棋禁入在暂放环节（预演第 1 步）就被拦下，落进禁入格的那一枚永远进不了 `Placements`、也到不了 `Rehearsal` / `Rejected`，所以日志要记"活棋禁入的尝试"只能读这份 Core 留痕（先例：`Match.TerrainEdits`）。**Core 唯一改动** |
| `src/Siege.Sim/Running/LoggingController.cs` | `TurnTrace.Batch`：`Deploy` 时记下本小回合的暂放批次 |
| `src/Siege.Sim/Logging/MatchLog.cs` | `TurnSnapshot.Life`（`LifeTurnEntry?`，旧日志为 `null`）：`Changes`（`LifeChangeEntry`：确立 / 失去、所有者、行动者、代表坐标 `At` = 坐标序首格、棋串坐标、眼空间与眼值 `A1,B1=1`、失去原因 `OwnerFill` / `OwnerEdit` / `OwnerOther` / `NonOwner`）；五个拒绝计数 `ForbiddenStaged` / `ForbiddenRehearsed` / `ForbiddenRejected` / `BreaksRehearsed` / `BreaksRejected`；结算后逐玩家 `LifePlayerEntry`（活形棋串数、其中单子数、受保护眼空间块数 / 格数、贴地形小空区块数）；`ProtectedCells`、`PlayableCells`。新事件类型 `LifeRefused`（非细粒度，暂放 / 确认环节被拒时写：行动玩家、类别、坐标、`Values.Owner`、`Detail` 以 `stage` / `confirm` 开头） |
| `src/Siege.Sim/Running/MatchSession.cs` | `RecordTurn` 写 `Life` 与 `LifeRefused`；新增 `LifeEntry`（internal static）。确立 / 失去比对 `RecordTurn` 已有的前后两份 `MatchPublicView.LifeShape`，**Sim 不调 `Analyze`**，按**棋子归属**判：结算后一条活串里没有任何一枚子在结算前属于同主的活串 → 确立；结算前一条活串里有任一枚子在结算后不在同主的活串里 → 失去。失去原因：行动者 ≠ 所有者 → `NonOwner`（规则缺陷）；所有者落点落进原眼空间 → `OwnerFill`；所有者带了改造 → `OwnerEdit`；否则 `OwnerOther`。R8 的"贴地形"判定 `IsTerrainSmall`：≤ 3 格且至少一格 `board.Neighbors(c).Length > board.LibertyNeighbors(c).Length`（盘内几何方向上缺气边；棋盘外沿不在几何邻居里，不算）。这是遥测分类，不是规则判断；`GameBoard.Neighbors` 的 IL 守门只扫 Core 程序集，boundaries.md 的"只留给表现层几何"这里按"分析层分类"同类处理 |
| `src/Siege.Sim/Analysis/BalanceAnalyzer.cs` | 新 `LifeShapeSection` / `RoundStat` / `LifeDefect`，`BalanceReport` 末尾加 `LifeShape`。任一快照 `Life == null` 即整局排除并计数；胜率与"按名次分组"只取有名次的局（`rankable`） |
| `src/Siege.Sim/Analysis/ReportWriter.cs` | 新段「## 活形（life-shape）」+「### R8：活形过易的两项单列」；§16-5 加一行"终局局的结束大回合：中位 X，最长 Y"（从既有直方图算，供与 ① 并列） |
| `src/Siege.Sim/Play/BoardRenderer.cs` | R12：禁入格改为直接取 `life.ForbiddenCellsFor(me)`，所有者取 `EyeSpaceAt(c).Owner`。R13：if 链里信物排到禁入格之前；被信物盖住的禁入格在图例下方单列一行"信物格同为禁入：E5(x1)"（无重叠不出这一行） |
| `2026-09-10-siege-core-gameplay-design-v1.md` | 4.3：v1.5 → v1.6；§6.1 七步 → 八步（第 1 步加活棋禁入、提子后新增第 6 步「破坏活形」）；新增 §6.4 活形判定与活棋禁入（空区、无气边即墙、`EYE_SPACE_MAX` = 12、眼值表与相加、三态、方四按气边图、全量重算不入存档、公开）；§12.2 弃赛者遗留活棋指向 §6.4；§13.1 公开活形状态 / 眼空间 / 禁入格；§16 在 20 局冒烟表后追加 200 局基线表（清掉 ① 留下的"完整 200 局由 life-shape 4.4 给出"欠条）；文末变更记录加一行。§6.3 正式结算仍是七步，boundaries.md 那行"七步"未改 |
| `.trellis/spec/core/boundaries.md` | 4.5：单一实现清单加一行「活形 / 禁入」（唯一实现 `LifeShapeReport`；预演、契约、公开视图、表现、终端、AI、遥测共用；扣除只在 `LegalRangeFor`；守门与变异编号） |
| `openspec/changes/life-shape/tasks.md` | 23 项全部 `[x]`（段 A–C 的 0.1–3.4 已由前三段完成并记录，本段一并勾选） |

**R10**：`GameRoot.cs` 的 `--export-parts` 两段、`PartExport.cs`、`src/godot/parts/` 未触碰；本段没有改任何 `src/godot/` 文件。Godot 构建 0 警告是含这些改动一起构建的结果。

### 既有测试的改写 / 删除（逐条）

没有删除。改写 4 处，均未改期望值去凑绿：

1. `TurnSequence/合法落子范围的对外契约Tests.禁入只在契约一处扣除且与预演共用同一查询` → 改名 `禁入扣除只在LegalRangeFor且与预演共用同一查询`（R12 收窄）。源码腿由"`ForbiddenCellsFor(` 只在 MatchFlow.cs、`IsForbiddenFor(` 只在 BatchRehearsal.cs"改为"扣除形态只在 `MatchFlow.cs:LegalRangeFor`"：注释剥离后匹配 `Except\w*(…Forbidden…)`、`Where(…!…IsForbiddenFor)`、`Remove\w*(…Forbidden…)` 三种形态，并报出所在方法名；扫描口径扩到 Core / Sim / Presentation / `src/godot`（136 个文件，下界 > 120，另断言口径含 Godot 的 `BoardView.cs`）。"读取放开"加反面断言：`BoardRenderer.cs`、`DefaultBoardView.cs` 确实在读 `ForbiddenCellsFor`。原"`IsForbiddenFor` 只在 `BatchRehearsal.cs`"的断言改为反面断言（`BatchRehearsal.cs` 在读取者之中），读取不再锁死。行为腿（逐格单子预演 = 契约扣除）未动。先红：终端改调 `ForbiddenCellsFor` 后旧守门红 1，收窄后绿。
2. `AiDecision/候选格上限Tests` 黄金哈希 `F1B2CAB6…4ACEB088` → `CDEB4C13…70084563`。**走法一步没变**：临时探针（已删）在同一局 24 条快照上逐条用 JsonNode 删掉 `Life` 键再序列化，哈希恰为旧值 `F1B2CAB6…`；24 条快照的活形字段全部非空。M-K1 在新值下重跑仍红（40 条）。
3. `SimFixtures.Turn` 加参数 `life` / `legacyNoLife`（缺省写空的活形记录；`legacyNoLife: true` 造旧日志 `null`）。
4. `SimulationHarness/终端活形与禁入标示Tests`：只新增方法，既有 5 条未动、全绿（夹具里的信物不在眼上）。

### 新测试（10 条，全部默认运行）

- `MatchTelemetry/活形记录与统计Tests`（新类 = Requirement 名）：
  - 5 条 Scenario：`活形确立可查`（第 3 大回合 P0 落 B1 成两眼：大回合 3、小回合 1、所有者、`At = B1`、6 枚子、`A1=1`、`C1=1`；并验逐玩家状态、平地角上的眼不算贴地形）、`自拆可查`（P0 填 A1 → `Lost` / `OwnerFill`，棋串与眼空间取结算前）、`拒绝尝试可查`（P1 暂放进 P0 的眼 A1：`ForbiddenStaged = 1`；只存快照的保留策略下 `LifeRefused` 事件仍在，类别 / 坐标 / 所有者 / `stage`）、`活形分析输出`（4 局手算样本 + 1 局旧日志：排除计数、首次确立均值 11/3、按名次 4 / 3.5、终局均值、禁入占比 0.03、胜率 2/3 与 1/9、拒绝计数、失去原因、R8 两项 1/3、1/4、1/4，以及报告行原文）、`他人致失活视为缺陷`（给出种子、小回合、大回合、行动者、所有者；反面：正常样本不出明细）。
  - 3 条行为钉子：`活形棋串加子不重复记确立`（按棋子归属，不按棋串相等）、`所有者的改造导致失去记为所有者的改造`（B2 深水为墙的两眼活串，P0 在 B3 落匠人搭桥 → B1、B2 成 2 格空区 → `OwnerEdit`）、`真实跑局的活形字段自洽`（Standard、种子 1、40 小回合、完整事件流：每条快照有 `Life`；`BreaksRehearsed` 之和 = 细粒度 `Rehearsal` 事件里 BreaksLife 的条数且 > 0；终局逐玩家四项 = 测试侧在活对局上独立数出的值；贴地形小空区块数 = 测试侧用坐标算术独立算的值且 > 0）。
- `SimulationHarness/终端活形与禁入标示Tests.信物格同为禁入时信物标记优先`（R13）：E5 放未揭示信物 → `" ? "`，G5 仍为 `"x1 "`；盘面 x 集合 = `ForbiddenCellsFor(P1)` − 信物格；说明行含 `E5(x1)`；反面：无重叠时没有该行。

**先红**：活形遥测的测试写在实现之前，但新类型尚不存在，第一次只能编译失败，不算有效的红；有效的红由下面的变异逐条给出（每条 Scenario 至少一条变异使其红）。R13 同理：实现与测试同批写入，由 M-D4 / M-D5 证红。R12 的红见上面改写第 1 条。

### 变异（脚本 scratchpad `mutate_d.py`，每条跑整个测试工程）

口径同段 A–C：二进制读写，先探测行尾，锚点命中恰 1 次，备份名带变异编号与时间戳，`finally` 里还原，还原后与变异前原始字节逐字节比对，`os.utime` 刷新 mtime；`DOTNET_CLI_UI_LANGUAGE=en`，解析统计行；条件变异只用运行时恒假。17 条全部 `restored=True`；跑完后全部改动文件与未跟踪文件的 SHA-1 与变异前逐一相同，复跑 1266 通过 / 2 跳过 / 0 失败（退出码 0）。

| 编号 | 变异 | 红数 | 红的测试 |
|---|---|---:|---|
| M-D1 | 终端 `legal` 上 `.Except(view.LifeShape.ForbiddenCellsFor(me))`（第二处扣除） | 1 | 禁入扣除只在LegalRangeFor且与预演共用同一查询 |
| M-D2 | AI 候选格 `.Where(c => !LifeShapeReport.Analyze(…).IsForbiddenFor(…))`（第二处扣除） | 1 | 同上（黄金哈希不红：范围本已扣除禁入格） |
| M-D4 | 终端：信物分支加 `!forbidden.ContainsKey(c)`（禁入重新盖住信物） | 1 | 信物格同为禁入时信物标记优先 |
| M-D5 | 终端：不输出"信物格同为禁入"行 | 1 | 信物格同为禁入时信物标记优先 |
| M-D6 | 不写确立事件 | 2 | 活形确立可查、候选格上限黄金哈希 |
| M-D6b | 确立判定 `Any` → `All`（按棋串整体而非棋子归属） | 2 | 活形棋串加子不重复记确立、黄金哈希 |
| M-D7 | 失去原因：有改造也记 `OwnerFill` | 1 | 所有者的改造导致失去记为所有者的改造 |
| M-D8 | `ForbiddenStaged` 恒 0 | 1 | 拒绝尝试可查 |
| M-D9 | `StagedBatch` 不追加 `Refusals` | 1 | 拒绝尝试可查 |
| M-D10 | `BreaksRehearsed` 恒 0 | 2 | 真实跑局的活形字段自洽、黄金哈希 |
| M-D12 | 暂放被拒不写 `LifeRefused` 事件 | 1 | 拒绝尝试可查 |
| M-D13 | 贴地形判定只看格数（去掉地形墙条件） | 3 | 活形确立可查（角上眼被误计）、真实跑局的活形字段自洽、黄金哈希 |
| M-D13b | `TerrainSmallEyeSpaces` 恒 0 | 2 | 真实跑局的活形字段自洽、黄金哈希 |
| M-D14 | 旧日志不排除（`Life` 缺失回填空记录） | 1 | 活形分析输出 |
| M-D15 | 他人致失活不进缺陷单 | 1 | 他人致失活视为缺陷 |
| M-D16 | 胜率不排除截断局 | 1 | 活形分析输出 |
| M-K1 | 候选格预筛恒启用（在新黄金哈希上复核） | 40 | 含 缺省不限制时标准图整局与改动前逐步相同 |

旧 M-B8（AI 里纯读 `ForbiddenCellsFor`）在收窄后的守门下按设计应为绿（读取放开），未单独重跑；"读取放开"由守门里 `BoardRenderer.cs` / `DefaultBoardView.cs` 的反面断言在常态下钉住。

### 旧日志回放 / 排除

- 既有回放测试（`可复现回放Tests`、`批量跑局Tests`、`地形改造日志与分析Tests` 等）全绿。
- 真实旧日志：新分析器读 `sim-out/restore-smoke20/`（① 的 20 局，无 `Life` 字段）→ 报告「纳入 0 局，排除缺活形字段的旧日志 20 局」，不崩；同一份报告的新行"中位 15，最长 69"与 ① 的记录一致（新行的自证）。

### 4.4 200 局基线

命令（与 ① 逐项同口径；`config.json` 核对：v5、4 名 Standard、七维权重相同、匠人权重 10、截断 600、`SnapshotsOnly`）：
`Siege.Sim.exe run --out sim-out/life-shape/baseline200 --seed 1 --count 200 --map siege-4p-base-v5 --players 4 --difficulty Standard`，随后 `analyze --dir sim-out/life-shape/baseline200`。
产物：`sim-out/life-shape/baseline200/`（`config.json`、200 份日志、`summary.json`、`report.txt`）与控制台 `sim-out/life-shape/baseline200.run.log`。失败 0，墙钟 174.8 s（28 并行）。因单次命令上限 10 分钟，以后台方式单独运行，期间没有并行任何其它 dotnet 进程，只做了文档编辑。**AI 未校准口径**（七维权重待 `ai-eye`）。

| 指标 | ① restore-go-core-rules 20 局冒烟 | life-shape 200 局基线 |
|---|---|---|
| 截断率（turn_limit） | 0 / 20（0%） | **0 / 200（0%，95% 上界 1.9%）** |
| 终局原因分布 | 整轮 Pass 20（100%） | 整轮 Pass **200（100%）**；只剩一名 0；棋盘填满 0 |
| 平均结束大回合 | 18.7 | **13.15** |
| 中位结束大回合 | 15 | **10** |
| 最长结束大回合 | 69 | **83** |
| 平均小回合 / 局 | 70.75 | 48.07 |
| 首次跨出生区冲突 | 第 6.13 大回合；整局无提子 5 / 20 | 第 8.52 大回合；**整局无提子 82 / 200（41%）** |
| 每批次提子 / Pass 率 | 0.82 / 29.0% | 0.65 / 33.5% |
| 第 3 大回合领先者胜率 | 35.0%（7/20） | 46.0%（92/200，区间 39.2%–52.9%） |
| 首次活形确立（全局） | —（无此机制） | 第 **1.02** 大回合（200 局全部有确立） |
| 首次活形确立（按终局名次） | — | 第 1 名 1.33 / 第 2 名 1.34 / 第 3 名 1.40 / 第 4 名 1.50 |
| 终局每名玩家：活形棋串 / 受保护眼格 | — | 6.12 条 / 9.29 格 |
| 终局禁入格占可落子格 | — | **34.9%** |
| 有活形玩家胜率 / 无活形玩家胜率 | — | 26.9%（200/743）/ 0.0%（0/57） |
| 活棋禁入（暂放 / 预演 / 确认） | — | 0 / 0 / 0 |
| 破坏活形（预演 / 确认） | — | 12046 / 0 |
| 活形失去按原因 | — | OwnerFill 212 |
| 他人致失活（规则缺陷） | — | **0** |
| **R8 单子活形棋串** | — | 终局 **3780 / 4893（77.3%）**；确立事件 5231 / 6471（80.8%） |
| **R8 贴地形小空区形成的眼空间** | — | 终局 **6001 / 7055（85.1%）** |

### 段末自验（4.6）

- `dotnet build siege.sln --no-incremental`：0 警告、0 错误。
- `dotnet build src/godot/Siege.Godot.csproj --no-incremental`（Debug）：0 警告、0 错误（含 R10 的他人改动）。
- `dotnet test -c Release`：通过 1266，跳过 2（Perf、Slow 门控），失败 0，退出码 0。
- `SIEGE_SLOW=1 dotnet test tests/Siege.Core.Tests -c Release --filter "Category=Slow"`：1 通过（活形保护性质，种子 1–200），耗时 3 s。
- `openspec validate life-shape --strict`：valid。tasks.md 23 / 23 勾选。

### 待决

1. **R8 数据已经给出，需要负责人裁决是否另开 change**：终局活形棋串 77% 是单子，受保护眼空间 85% 是贴地形的 ≤ 3 格小空区；首次活形确立平均第 1.02 大回合（每局第 1 大回合就有人活）；终局 34.9% 的可落子格对至少一人禁入。连带现象：整局无提子 41%（① 为 25%）、首次冲突推迟到第 8.52 大回合。结论只在 AI 未校准口径下成立。
2. **"有活形玩家胜率"几乎是全员**：800 个玩家样本里 743 个终局有活形，无活形的 57 人胜率 0%。这个指标在当前 v5 上区分度很低，与第 1 条同源。
3. **R8"贴地形"的口径**：以"盘内几何方向上缺气边"定义地形墙（岩石 / 深水 / 崖壁 / 栅栏），棋盘外沿不算；分母是终局受保护眼空间块数。实现用 Sim 里的 `GameBoard.Neighbors`（遥测分类，不是规则判断）。如果要把棋盘外沿也算作地形，或者按格数而不是块数计，需要裁决。
4. **失去原因的判定是启发式**：落点落进原眼空间就记 `OwnerFill`，否则有改造就记 `OwnerEdit`。所有者同一批既填眼又改造时记 `OwnerFill`。基线里 212 次全是 `OwnerFill`，没有出现 `OwnerOther`。
5. **活棋禁入尝试在 AI 跑局中恒为 0**，这符合设计：AI 的候选来自已扣除禁入格的合法范围。暂放环节的禁入尝试只有人类 / 脚本控制者会产生，现在由 `StagedBatch.Refusals` 记录。破坏活形在 AI 预演里每局约 60 次（12046 / 200），是可观的预演浪费，归 `ai-eye`。
6. `LifeRefused` 事件只写暂放与确认两个环节；预演环节的同类失败只计数（快照），明细仍在细粒度 `Rehearsal` 事件里（完整模式才保留），这样避免每局约 60 行的日志膨胀。
7. **`LifeRefused` 的 `confirm` 环节没有专门测试**：`拒绝尝试可查` 走的是 `stage` 环节；"破坏活形在确认时被拒"（控制者不预演直接确认）的写入路径和 `stage` 共用同一个 `AddLifeRefused`，但没有样本钉住，基线里 `BreaksRejected` 也是 0。列为已知薄弱点。
8. 变异编号跳过了 M-D3、M-D11（草拟时合并或删去），并不是漏跑。
9. 收尾补丁：按 R12 原文，守门里原来的"`IsForbiddenFor` 只在 `BatchRehearsal.cs`"改为反面断言（读取放开）。改完后重跑该测试类，4 条全绿；复跑 M-D2，仍然只红 1 条（守门本身）。
