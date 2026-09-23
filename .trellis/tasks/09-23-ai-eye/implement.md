# 09-23-ai-eye 实施记录

## 段 A（tasks 0.1、1.1–1.5）——九维结构、眼位、威胁、活形中性

### 0.1 前置核对（按 R4）

- `openspec/changes/archive/` 下有 `2026-09-22-restore-go-core-rules`、`2026-09-22-life-shape`、`2026-09-21-frontier-map`。开工时 `openspec list` 只有 `ai-eye`；段 A 进行中出现了 `terrain-surfaces`（0/36，非本任务创建，未触碰）。
- 基线数据：`sim-out/restore-smoke20/`（20 局 + config / report / summary）、`sim-out/life-shape/baseline200/`（200 局 + config / report / summary），均为未校准口径。
- 改动前的二进制在 `baseline200/config.json`（种子 1–20）上重跑，小回合快照与 `baseline200` 前 20 局逐条相同（1252 个小回合），即 HEAD 可复现该基线。
- `openspec validate ai-eye --strict` 通过。

### 改了什么

| 文件 | 改动 |
|---|---|
| `src/Siege.Core/Ai/EvaluationWeights.cs` | `EvaluationDimension` 末尾追加 `Eye`、`Threat`（前七维下标不变）；`EvaluationWeights` 增加两个位置参数 `Eye`、`Threat`，`Default` 取 0，依据段写明"未校准、段 D 扫档"；`Of` 分派两维。旧配置 / 旧首部缺这两个键时按 0 读入 |
| `src/Siege.Core/Ai/EvaluationBreakdown.cs` | `DimensionCount = 9`；新增 `Version = 2`（AI 评价版本：1 = 七维，2 = ai-eye 九维）；`ToString` 输出九项（`… 供给N 眼位N 威胁N = 总分`） |
| `src/Siege.Core/Ai/BatchEvaluator.cs` | 非简单难度时，前后盘面各做一次 `LifeShapeReport.Analyze`，由安全、眼位、威胁三维共用（每个盘面查一次，不跨盘面缓存）；新增 `EyeOf`（己方眼空间眼值之和，每块眼空间只计一次 + 己方活形棋串数 × `AliveGroupEyeBonus = 3`）、`ThreatOf`（参赛敌方、非活形、气数 ≤ 3 的棋串棋子总数）；两者都取前后差；`SafetyOf` 改为遍历活形查询里的己方棋串 |
| `src/Siege.Core/Ai/GroupSafety.cs` | 删除自带的眼点循环与 `EyePoints`；参数改为 `(Liberties, EyeValueSum, DispersedLiberties, Size, IsAlive)`，`Analyze(GameBoard, GroupLife)`；`TwoEyePotential = min(2, EyeValueSum)`；已确定活形时 `Score` 恒为 `AliveScore = 4 + 2 × 6 + 2 = 18`（公式上界）；分散气循环保留（只看气与气是否相邻，不看占用者） |
| `src/Siege.Sim/Logging/MatchLog.cs` | `LogHeader.AiEvaluationVersion`（`int?`，旧日志为 null，不回填） |
| `src/Siege.Sim/Running/MatchSession.cs` | 首部写入 `AiEvaluationVersion = EvaluationBreakdown.Version`；落子事件的分解值随 `DimensionCount` 自动带上 `Eye` / `Threat` 两项 |
| `.trellis/spec/core/boundaries.md` | "活形 / 禁入"一行里 `GroupSafety.EyePoints 暂与之并存，去留归 ai-eye` 已过时，改写为已删除并注明守门 |
| `openspec/changes/ai-eye/tasks.md` | 勾选 0.1、1.1–1.5 |

1.5 的结论：`EnemyLoss` 现在只有"参赛敌方势力的实际下降 + 提子数 × 2"两项，**没有**潜在提子价值项，所以不需要改实现；用「不攻打活棋」里的一条断言钉住"攻活棋时敌损只等于已实现的下降"，并用变异 M-A5 证明它能红。

### 测试

新增（测试类 = Requirement，方法 = Scenario）：

- `启发式评价维度Tests`：`做出第二个眼获得眼位正贡献`（= 4，同一批次简单难度为 0）、`自填眼位不得分`（未定 −1、活形 −4）、`点直三中间眼位增量为眼值1加活形3`（= 4）、`已活时点直三中间眼位增量只有眼值1`（= 1）、`共享眼空间只计一次`（一块单格眼同时是两条己方棋串的眼空间，原始值 = 1，不是 2）、`叫吃获得威胁正贡献`（= +5）、`AI不自带眼判定只经活形查询`（源码扫描守门）、`九维权重经配置往返且旧配置缺两维按0读入`（往返用非回填值 5 / 7）、`日志首部写入AI评价版本且分解值为九项`。
- 新类 `已确定活形的评价中性Tests`：`活棋补气不加分`、`做活在安全维度上是净收益`、`不攻打活棋`（含 1.5 的敌损断言）。

先红：实现前跑 `AiDecision`，7 条红（眼位 4 条、威胁 1 条、`活棋补气不加分`（实际 25）、源码扫描守门）；`做活在安全维度上是净收益` 与 `不攻打活棋` 在旧实现下本来就成立（旧公式做活也加分；旧实现不算威胁，恒 0），它们靠变异 M-A2 / M-A5 证明有效。`共享眼空间只计一次` 是实现之后补的：自查时发现，全部眼位算例都只有一条己方棋串，"按棋串求和"与"按眼空间求和"在这些算例上结果相同，属于等价变异。补上这条测试后，由 M-A7 证明它能红。

既有测试的改写 / 删除（逐条）：

1. `启发式评价维度Tests.评价覆盖七个维度` → 改名 `评价覆盖九个维度`（规格 Scenario 改名）；加断言：维度数 9、枚举值 9 个、分解文本含"眼位""威胁"。其余断言不变。
2. `启发式评价维度Tests.两眼潜力近似区分活形与死形` → 改写为 `两眼潜力取自活形查询`：原测试钉的是被删掉的眼点循环（`EyePoints`，以及旧变异 M-A10"放宽单点气判定"），已无对象。新测试用同样两个盘面：活形取常数 18，`EyeValueSum` 2；直三的眼值按活形查询是 1（旧循环给 0，这正是两套判定的分歧），得分 `4 + 1 × 6 + 0 = 10`。旧 check 变异 M-C2（`× 6` 改 `× 0`）在新测试上重跑仍红。
3. `启发式评价维度Tests.权重可配置`：两处 `new EvaluationWeights(…)` 补 `Eye: 0, Threat: 0`（新增的位置参数），期望不变。
4. `默认评价权重的校准Tests.默认权重被改动`：加 `Eye 0`、`Threat 0` 两行 InlineData，按属性读的 switch 补两维；`默认权重的校准依据随值一起更新` 的逐维清单补 `Eye`、`Threat`（源码里须有 `Eye = 0`、`Threat = 0`）。
5. `难度分级Tests.简单难度只看即时收益`：简单难度必须为 0 的维度清单补 `Eye`、`Threat`（注释"五维"改"七维"）。
6. `SimFixtures.RankedMatch`、`地形改造日志与分析Tests.地形可离线重建` 的写死权重补 `Eye: 0, Threat: 0`。
7. `地形改造日志与分析Tests.地形可离线重建`：**重挑种子 17–19 → 3–5**（断言与期望未改，只换样本）。1.4 之后走法变了，17–19 的致提子改造变为 0 / 0 / 0，"样本里确实有致提子的改造"这一下界响亮失败。用同一份写死权重（Safety 27、Eye / Threat 0）的 CLI 探针重扫种子 1–24；探针配置先用改动前的二进制复现了 17–19 的"改造 2 / 2 / 3、致提子 0 / 1 / 1"。新结果里只有种子 3、4 各有 1 次致提子；取 3–5，改造 5 / 6 / 1、致提子 1 / 1 / 0。
8. `候选格上限Tests.缺省不限制时标准图整局与改动前逐步相同`：黄金哈希 `CDEB4C13…70084563` → `14E1B0D2…104CBA72`，**走法确实变了**。1.1 完成时该测试仍以原值通过（1.1 之后那次全量测试包含它）。1.4 之后改动前、后的二进制各跑种子 31、24 个小回合，第 1 个小回合（P3）就分叉：旧落点是三枚要塞 B10 / B11 / C12，安全 12；新落点把 B11 换成普通子，安全 36（B10-B11 与 C12 两条棋串共享平台角上一块 8 格、眼值 2 的眼空间，都已确定活形，各取常数 18），眼位原始值 8 = 2 + 2 × 3（权重 0，不进总分）。新值连跑两次一致。另外，该断言改为 `Assert.True(golden == hash, "…现为 {hash}")`，失败时打印完整哈希（原 `Assert.Equal` 会截断）。

测试数：改动前 1266 通过、2 跳过；段 A 后 **1280 通过**、2 跳过（+14 = 新增 12 个测试方法 + `默认权重被改动` 的 2 行 InlineData）。

### 变异

脚本：scratchpad `mutate.py`。做法：二进制读写；锚点按文件实际行尾归一，并断言命中 1 次；还原放在 finally；备份名带时间戳；还原后与原文逐字节比对，再 `os.utime`；解析 `Passed!/Failed!` 统计行；不用 `if (false)`。下表的红数是在 `--filter FullyQualifiedName~AiDecision` 下统计的（M-A6t 用 `~默认评价权重的校准`）。9 条全部 EXIT 1，还原后全部逐字节一致；最后一条变异之后重新构建并跑全量测试，0 红。红数同时写进了对应测试的注释。

| 编号 | 改了哪里 | 红 | 红的测试 |
|---|---|---|---|
| M-A1 | `Evaluate`：眼位改取结算后的绝对值（`EyeOf(life) - _eyeBefore` → `EyeOf(life) - 0L`） | 4 | 做出第二个眼获得眼位正贡献、**自填眼位不得分**、点直三中间眼位增量为眼值1加活形3、已活时点直三中间眼位增量只有眼值1 |
| M-A2 | `ThreatOf`：删掉 `\|\| group.Life == LifeState.Alive`（不排除活形敌串） | 1 | **不攻打活棋** |
| M-A3 | `GroupSafety.Score`：`if (IsAlive)` → `if (IsAlive && Size < 0)`（运行时恒假，活棋仍走公式） | 2 | **活棋补气不加分**、候选格上限（黄金哈希） |
| M-A4 | `GroupSafety` 分散气循环里插入 `_ = board[n].Occupant is { } o && o.Owner == life.Group.Owner;` | 1 | **AI不自带眼判定只经活形查询** |
| M-A4b | `BatchEvaluator.SafetyOf` 里插入一行 LINQ 眼判定（`LibertyNeighbors(c).All(n => board[n].Occupant … Owner == _me)`） | 1 | **AI不自带眼判定只经活形查询** |
| M-A5（1.5） | `Evaluate`：敌损加一项不排除活形的"潜在提子价值"（全部非己方棋串中气数 ≤ 3 者的棋子数，不排除活形） | 2 | **不攻打活棋**、评价覆盖九个维度 |
| M-C2（重跑） | `Score`：`TwoEyePotential * EyeWeight` → `* 0` | 2 | **两眼潜力取自活形查询**、候选格上限（黄金哈希） |
| M-A7 | `EyeOf`：改为对己方棋串的 `EyeValueSum` 求和（共享眼空间被重复计） | 1 | **共享眼空间只计一次** |
| M-A6t | 只改测试、不改实现：`默认权重被改动` 的 Supply / Eye 两行期望对调（2 ↔ 0） | 2 | 默认权重被改动（Supply、Eye 两行） |

源码扫描守门的反面自证，写在测试里：扫描器必须认出两个样本——段 A 之前 `GroupSafety` 的眼点循环原文，以及一行 LINQ 写法；口径下界是扫描文件数 ≥ 10、命中邻格枚举 ≥ 1 处。

### 决策序列比对（种子 1–20，`baseline200/config.json`：v5、4 名 Standard AI、`--retention Full`）

投影规则：去掉首部与耗时字段；落子事件的分解值里去掉 `Eye` / `Threat` 两个键；候选文本里去掉"眼位N 威胁N"两项。比对脚本是 scratchpad 里的 `seqcmp.py`。

1. **1.1 完成后（九维结构、首部版本，新两维权重为 0、尚未计算）**：与改动前**逐步相同**，20 / 20 局一致。投影 33617 行，含 1252 个小回合快照，以及候选、预演、落子分解、征募等全部事件。旧配置只有七个权重键，读入后新两维为 0。黄金哈希在这一步没变（全量 1270 条全绿）。
2. **归因探针（段 A 完成后）**：两份二进制都把 Safety 权重设为 0、其余权重不变，跑种子 1–20，小回合快照 **20 / 20 局逐步相同**（9567 个小回合）。结论：眼位、威胁已真实计算但权重为 0，且把新 Safety 公式的影响排除之后，段 A 不改变任何决策；走法的变化全部来自 1.4 的 Safety 公式。
3. **段 A 完成后（默认权重）**：20 / 20 局分叉——17 局在第 1 个小回合、3 局在第 2 个小回合，与 D2 活形中性的预期一致。开局平台角上的棋串按活形查询已是已确定活形，拿安全常数 18。20 局全部以 AllPassed 结束；平均结束大回合 18.6 → 10.85，平均小回合 62.6 → 40.65；墙钟 77.9 s → 24.7 s（对局变短）。**这些数字属于未校准口径，只作参照，不作基线。**

### 段末自验

- `dotnet build siege.sln`：0 警告 0 错误
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误
- `dotnet test -c Release`：1280 通过、2 跳过、0 失败（RC 0，在最后一条变异之后跑）
- `openspec validate ai-eye --strict`：valid

### 待决

1. **威胁的"敌方"口径**：目前只计参赛（Active）敌方，与敌损"参赛敌方势力下降"同一口径；弃赛 / 出局者遗留的非活形棋串不计入威胁。design D1 原文只写"敌方"，请负责人确认。
2. **"直三点中间"两条算例的解读**：tasks 1.2 写的是"未成活形时增量 1；成活形时 +3"。同一棋串在点中间后必然眼值 ≥ 2、成为活形，所以"未成活形"的情形按"本手没有新增活形"来实现，即棋串本就已活（另有单格眼），点直三中间只得眼值增量 1。若原意是别的盘面（例如两个单格眼分属两条棋串），需另补算例。
3. **与 life-shape R8（活形过易）的耦合**：开局第 1 个小回合，平台角上的 3 子就已是已确定活形，于是活形中性把安全维直接推到常数上限（种子 31：安全 12 → 36），20 局平均结束大回合 18.6 → 10.85。这正是 R3 要求段 D 开工前先裁决 R8 的原因；段 A–C 不受影响。
4. **性能**：非简单难度下，每次评价都会多做一次 `LifeShapeReport.Analyze`（每个盘面一次，三维共用）。段 C 做缓存与耗时对比，可以先拿这组粗略参照：种子 1–20（Standard、v5、28 路并行，非受控测量），每个小回合的平均耗时由改动前的 567639 ms / 1252 ≈ 453 ms 变为段 A 后的 324769 ms / 813 ≈ 399 ms。
5. `openspec list` 里新出现 `terrain-surfaces`（0/36），不是本任务所建，也未触碰；0.1 的"只剩本 change"只对开工时刻成立。
