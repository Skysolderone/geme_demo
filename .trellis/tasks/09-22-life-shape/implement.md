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
