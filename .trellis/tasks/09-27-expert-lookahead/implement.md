# 09-27-expert-lookahead 实施记录

## 段 A——Core 前瞻、公平信息纯函数抽取、守门、确定性与性能（tasks 1.1–1.11）

### 结论先行

- 第 1 组 1.1–1.11 全部完成。默认套件全绿：1732 通过、6 跳过（本段新增的跳过只剩门控计时测试）。三档旧难度逐步不变，既有黄金哈希一字未改；W = 1 时专家与高难逐步相同；信息边界守门全绿。
- 段 A 初版发现 D2 与 Safety 维冲突：危险棋串被提走后，危险扣分随之消失，专家因此弃串。负责人已裁决采用修法 ①，裁决写入 design.md 最后一条。实现与验证见下文「段 A 续：裁决后的修正」。下面「待决」一节保留的是裁决前的原始记录。
- 耗时：修正后的门控计时比值为 **1.360**（裁决前 1.447），上限 4；预演次数代理为 **1.198**。

### 改动文件

| 文件 | 内容 |
|---|---|
| `src/Siege.Core/Ai/AiDifficulty.cs` | `AiDifficulty` 末尾追加 `Expert`。`AiSearchConfig` 在末尾追加位置参数 `LookaheadWidth`（缺省 0，`[property: JsonIgnore(WhenWritingDefault)]`：值为 0 时不写出，缺字段时按 0 读入）。新增 `DefaultLookaheadWidth = 4`；新增 `Expert` 预设，等于 `Hard with { LookaheadWidth = 4 }`。`ForDifficulty` 覆盖 `Expert`（`ForMap` 经它覆盖）。`Validated()` 拒绝负数 |
| `src/Siege.Core/Ai/ExpertLookahead.cs` | 新增。每次决策新建一个前瞻组件，带实例字段、不持有随机流：`Choose`（第 2–7 步）、`LookaheadSet`（去掉空批次，按 `CandidateSelection.Compare` 排序后取前 W 个）、`SelectIndex`（只有严格更大才替换，同分保留靠前者）、`Project`（D8 投影公开视图）、`NextOpponent`（本大回合顺延；末位时跨大回合，经 `InitiativeOrder.Generate` 预测顺序，种子兜底用玩家编号）、`SimulatedContext`（D 枚普通子、公开部署上限、合法落子范围、工坊标记）。模拟对手取标准预设，改为 M = 1、K 与阈值随本局，经 `HeuristicTurnController.ForSimulation` 创建。预演用 `BatchRehearsal.Rehearse`，同形历史只含局部两条：决策起点盘面与 B1 |
| `src/Siege.Core/Ai/LookaheadRecord.cs` | 新增。`LookaheadStatus`（Applied / NotApplied / Pass）、`LookaheadEntry`（批次键、前瞻前后分数、下一名对手、行动大回合、模拟部署上限、回应批次键）、`LookaheadRecord`（状态、前瞻集、被选下标、模拟对手预演次数，另有 `ChangedChoice` 与 `ToText()`）。分数全用 `BigInteger`。供段 B 遥测直接使用 |
| `src/Siege.Core/Ai/HeuristicTurnController.cs` | `_perturbation` 改为可空，构造改为一个私有本体。公开构造与 internal 构造对 null 仍然抛异常；只有新增的 `internal static ForSimulation`（要求 M = 1、W = 0）允许 null，此时 `Perturb` 遇到 null 流即抛。`Deploy`：去重之后、复摆之前，前瞻宽度大于 0 时走 `ExpertLookahead.Choose`，否则仍走 `CandidateSelection.Best`。新增 `LastLookahead`：宽度为 0 时恒为 null；早退 Pass 时记 `LookaheadRecord.Passed` |
| `src/Siege.Core/Match/PublicRules.cs` | 新增纯函数：`BuildProtectionRounds`、`LegalRange`（保护期取出生区，否则取当前地形下的全图可落子格，再扣除禁入格）、`IsEliminated` / `Eliminated`（出局判据） |
| `src/Siege.Core/Relics/PublicRelicEffects.cs` | 新增纯函数：`Deploy`（公开部署上限 = 分阶段基础值 + 受控且内容已知的军令，另给工坊标记）、`InitiativeBonuses`（公开先锋修正）。对外重载只读 `RelicPublicState`，按揭示标记为准；账本经 internal 的 `*FromKnown` 重载传入自己的内容 |
| `src/Siege.Core/Match/MatchFlow.cs` | `BuildProtectionRounds` 改为转发 `PublicRules.BuildProtectionRounds`。`LegalRangeFor` 委托 `PublicRules.LegalRange`。`CheckEliminations` 的判据委托 `PublicRules.Eliminated` |
| `src/Siege.Core/Relics/RelicLedger.cs` | `BuildSnapshot` 的部署上限与工坊委托 `PublicRelicEffects.DeployFromKnown`。两个 `ReadInitiativeBonuses` 重载委托 `InitiativeBonusesFromKnown`，删除 `SumVanguard` |
| `tests/.../LookaheadFixtures.cs` | 新增夹具：写死权重（开工时的缺省值）与阈值 80；单次决策；v5 局面探针 `ProbePositions` 与影子决策 `Shadow`（另起批次、每次从种子重新派生 `ai-<玩家>`、计数预演）；`ProbeController`、`LookaheadRecorder`；合成候选 |
| `tests/.../AiDecision/难度分级Tests.cs` | 追加 3 条：「难度名称与次序」「三档旧难度逐步不变」（Theory × 3，黄金值取自 HEAD 03d45f6）「专家难度不越权」 |
| `tests/.../AiDecision/专家难度的一层前瞻Tests.cs` | 新增，10 条（其中「前瞻改变选择」跳过，待裁；「不前瞻时直接取前瞻集第一个」在收尾时补上，覆盖第 3 步的 NotApplied 记录） |
| `tests/.../AiDecision/前瞻中的下一名对手Tests.cs` | 新增，6 条 |
| `tests/.../AiDecision/前瞻中的停手口径Tests.cs` | 新增，5 条（其中「被提走的损失压低前瞻后分数_待裁」跳过） |
| `tests/.../AiDecision/前瞻模拟的公平信息Tests.cs` | 新增，9 条（含投影视图三种情形 + 投影不揭示） |
| `tests/.../AiDecision/专家前瞻的确定性与耗时Tests.cs` | 新增，4 条（含预演次数代理） |
| `tests/.../AiDecision/专家前瞻耗时计时Tests.cs` | 新增，计时测试 1 条（`[PerfTheory]` + `Category=Perf`，不并行） |
| `tests/.../AiDecision/公开规则纯函数Tests.cs` | 新增，5 条（v5 真实对局等价测试是 Theory × 3） |
| `tests/.../TurnSequence/合法落子范围的对外契约Tests.cs` | **既有测试改写**：`禁入扣除只在LegalRangeFor…` 的期望值由 `MatchFlow.cs:LegalRangeFor` 改为 `PublicRules.cs:LegalRange`。理由：D7 把扣除搬进了唯一的纯函数，"全仓只有一处"这一点不变 |

未改动：`Siege.Sim`、`Siege.Presentation`、`src/godot`。本段没有补占位——Sim 直接按 `ForDifficulty(Expert)` 取到专家配置，JSON 按名称读写枚举。终端、Godot、CLI 严格解析、遥测与设计文档都留给段 B。

### 先红后绿

- 先写骨架，保证测试能在运行时变红，而不是编译失败：`Expert` 枚举成员与 `LookaheadWidth` 参数就位，但骨架里 `Expert` 预设等于 `Hard`，不写 `JsonIgnore`，`Validated` 也不检查；`PublicRules`、`PublicRelicEffects`、`ExpertLookahead` 的方法体一律 `throw new NotImplementedException()`；`LastLookahead` 恒为 null。
- 当时新增用例 45 条，其中红 38、绿 6、跳过 1（计时测试）。绿的 6 条都是守门型，改动前就该绿：「三档旧难度逐步不变」× 3、「专家难度不越权」「守门覆盖前瞻代码」（骨架里的新文件没有违禁读取），以及「前瞻宽度为 1 时与高难逐步相同」（骨架专家即高难）。它们的效力由下面的变异证明。另外，「难度名称与次序」在骨架上红，原因是专家预设断言不成立。
- 红阶段顺带修了测试自身的 3 处错：W = 1 比对误含 `TotalMs`，改为比对首部之外的确定性文本；两处信物强度 3 / 4 超出合法档位（只有 +1 / +2），改为 2。
- 转绿之后又调了夹具。以下 3 条只改夹具、断言不变，并对照段 A 实测：
  - 「前瞻集不足 W 个」：原局面的两个候选自身分都为负，被贪心拒掉，改为"两块各差一眼 + 阈值 700"。
  - 「专家是本大回合末位」：前提由"P2 > P0 > P1"放宽为"P2 最高"，这已足以让预测顺序区别于回绕。
  - 「未揭示信物不被读取」：专家部署上限改为 1，否则 4 子批次让夹具对泄漏不敏感，见 M-A4 的注记。
- 另有 2 条连夹具和断言一起改写，原因同待决 1：「前瞻不引入 Pass」换成"10 子块被紧气"的局面；「回应后不重判活形硬约束」改为测试内独立复算。见下文待决 2。
- 最终新增 47 个用例：44 条通过、3 条跳过（「前瞻改变选择」与「被提走的损失压低前瞻后分数_待裁」待裁，计时测试门控）。

### 逐步不变的证明

- **三档旧难度**：「三档旧难度逐步不变」在 HEAD 03d45f6（改动前）的代码上跑出黄金值后钉死。局面为 v5、种子 1、4 名同难度 AI 整局，写死权重 10 / 8 / 6 / 35 / 4 / 20 / 2 / 200 / 25、阈值 80、冒险概率 0、内容集 v2。每档钉住：小回合数、确定性文本行数、确定性文本（含首部）的 SHA-256、四名 AI 决策日志的 SHA-256。

  | 难度 | 小回合 | 行数 | 日志 SHA-256（前 16 位） | 决策 SHA-256（前 16 位） |
  |---|---|---|---|---|
  | Easy | 72 | 2364 | 277336FE7188875F | 987D1695B6134D25 |
  | Standard | 28 | 416 | 29353FC976C82867 | 6E6FEFBE0739612B |
  | Hard | 26 | 494 | 135B8E7AB5DB8CAC | 3F9D8A31F38EFD69 |

  改动后三档全部仍绿。既有黄金哈希（`候选格上限Tests.V4GoldenTurnHash` 等）、期望值与存档夹具一字未改，全量套件全绿。
- **W = 1 与高难**：「前瞻宽度为 1 时与高难逐步相同」跑 v5 种子 1 整局，玩家 1 分别用 `Expert with { LookaheadWidth = 1 }` 和 `Hard`，其余三名标准。玩家 1 的决策日志逐条相同，小回合快照相同，首部以外的确定性文本（事件、终局）逐行相同（> 100 行）。M-A9 证明它会红。
- **纯函数抽取行为零变化**：`公开规则纯函数Tests` 把改动前的实现原样抄进测试，作为"旧路径"，含 `LegalRangeFor`、`BuildSnapshot` 的军令 / 工坊部分、`SumVanguard`。在 v5 种子 1–3 的 4 名标准 AI 整局中，每个小回合比对三条路径：旧路径、对局下发的批次上下文（委托后）、读公开快照的纯函数。9×9 夹具另覆盖保护期 / 全图、活棋禁入、已揭示军令 / 工坊 / 先锋与弃赛者。

### 耗时（1.10）

- 计时（`SIEGE_PERF=1 dotnet test -c Release --filter "Category=Perf&FullyQualifiedName~专家"`，本机实跑 1 次）：33 个局面，丢弃预热 3 个，每组 60 个样本，按 ABBA 顺序测。
  - 高难：中位数 231.3 ms，p90 494.9 ms
  - 专家：中位数 334.6 ms，p90 532.7 ms
  - **中位数比值 1.447**，上限 4，未超限，没有动 W。
- 确定性代理（默认套件）：33 个局面，第 1–3 大回合 12 个、第 4 大回合以后 21 个。预演次数高难 14431、专家 17289（含模拟对手），**比值 1.198**。
- 局面集有一处偏差，见待决 3：v5 种子 1–3 上"玩家 1"只有 7 + 7 + 9 = 23 次部署，达不到 30。改为取四个座位的全部 92 次部署，每 3 个取 1，得 33 个。
- 默认套件耗时由改动前的 1 分 27 秒涨到 1 分 44–50 秒。主要来自新增的整局测试：v5 等价测试 3 局、W = 1 两局、专家整局 3 局，以及两次局面探针。若段 B 时长再涨，可以把 `公开规则纯函数Tests` 的 v5 等价 Theory 与 `ProbePositions` 的种子 1–3 标准局合并为一个 `Lazy` 共享样本，约省 10 秒（本段未做）。

### 变异验证（先改一行、再构建、再跑、最后还原）

脚本做法：二进制读写，锚点按文件行尾归一，并断言命中恰为 1 次；还原放在 finally 里，逐字节校验后执行 `os.utime`；备份文件名带时间戳；统计以退出码和 `Failed:` 行为准。每条变异跑的都是同一个过滤集，覆盖专家 / 前瞻 / 公开规则纯函数 / 难度分级 / TurnSequence / RelicEffects / InitiativeOrder / EliminationEndgame / 正式对战AI的信息边界，共 160 条（3 条跳过）。全部变异跑完后，工作树与变异前一致，`git diff --stat` 与跑前相同，全量套件重跑为绿。

| 编号 | 变异 | 红 | 红的用例 |
|---|---|---|---|
| M-A2a | `Expert` 插在 `Hard` 之前 | 1 | 难度名称与次序 |
| M-A2b | 前瞻宽度为 0 时照样写出（去掉 `WhenWritingDefault`） | 1 | 前瞻宽度进入记录 |
| M-A2c | `Validated()` 放过负数 | 1 | 前瞻宽度拒绝负数 |
| M-P1 | 部署上限计入争议军令 | 4 | 公开部署上限算例、模拟部署上限按公开信息推得、v5 等价测试、既有「争议与失控信物不提供效果」 |
| M-P2 | 先锋修正计入已弃赛者（账本传整张名册） | 3 | 公开先锋修正不计已弃赛者、夹具局面等价测试、既有「主动弃赛Tests.弃赛后停止行动」 |
| M-A4 | 投影时按真实内容揭示新覆盖的信物（经 `HeuristicAi.Create` 临时开的 `[ThreadStatic]` 通道） | 2 | 未揭示信物不被读取、投影不揭示新覆盖的信物 |
| M-A5a | 名册不去掉已弃赛者 | 2 | 跳过已弃赛者、只剩自己时退化 |
| M-A5b | 按决策起点、而不是按候选判定出局 | 2 | 候选使下一名对手出局、只剩自己时退化 |
| M-A5c | 末位时按本大回合顺序回绕 | 2 | 专家是本大回合末位、专家在预测顺序中居首 |
| M-A5d | 专家居首时返回专家本人 | 1 | 专家在预测顺序中居首 |
| M-A6a | 模拟库存改读对手真实手牌（经测试接缝注入，`[ThreadStatic]` 通道） | 3 | 对手真实手牌不同结果不变、模拟部署上限按公开信息推得、专家是本大回合末位 |
| M-A6b | 部署上限不计军令 | 2 | 模拟部署上限按公开信息推得、保护期内只在出生区回应 |
| M-A6c | 模拟对手用专家自己的 `ai-<玩家>` 做完整的标准 M = 8（`[ThreadStatic]` 通道） | 1 | 不消费新随机 |
| M-A6d | 末位应答者仍按第 3 大回合的部署上限 / 范围 | 2 | 专家是本大回合末位、专家在预测顺序中居首 |
| M-A7a' | （替代）回应提走专家棋子时，候选作废 | 1 | 回应后不重判活形硬约束 |
| M-A7b | 同分取后者（`>` 改为 `>=`） | 2 | 同分取原次序、模拟回应全为 Pass |
| M-A7c | 空批次进入前瞻集 | 2 | 空批次不进前瞻集、高难会 Pass 时专家也 Pass |
| M-A7d | 前瞻后分数再过一次停手阈值 | 1 | 前瞻不引入 Pass |
| M-A7e | 前瞻后分数的"前"改用 B1 | 2 | 模拟回应全为 Pass、回应后不重判活形硬约束 |
| M-A9 | 前瞻分支里消费一次 `ai-<玩家>` | 2 | 不消费新随机、前瞻宽度为 1 时与高难逐步相同 |
| M-G1 | 前瞻代码加一个调用 `TrueContents(` 的私有方法 | 2 | 守门覆盖前瞻代码、既有「AI源码不读取真实信物内容」 |
| M-G2 | `ExpertLookahead` 加 `HandLedger` 类型的成员（单列为闭包根） | 2 | 守门覆盖前瞻代码、专家难度不越权 |
| M-G3 | （替代）纯函数文件读 `RelicPlacement.Content` | 1 | 守门覆盖前瞻代码 |

注记：
- M-A4 / M-A6a / M-A6c 第一轮用的是普通静态字段做通道，xunit 并行时会被别的测试覆盖，红数不稳定。改为 `[ThreadStatic]` 后重跑，表中是重跑的数。
- M-A4 第一轮（普通静态字段）是红的，但那是并行竞态碰巧造成的；改用 `[ThreadStatic]` 重跑时，「未揭示信物不被读取」没红：专家部署上限为 4 时，候选是 4 子批次，模拟回应对 E5 的价值不敏感。把专家部署上限改为 1 后，该条红，这是上面提到的夹具加强。兵站的价值 5 恰好等于分区期望 5，所以变异只会让军令那一局的回应改变。
- tasks 1.7 的"改用 `TryEvaluate`"在本实现里是**等价变异**，无法变红。原因：回应不可能让专家的已确定活形失活（预演第 6 步拒绝）；合成结果带的是候选自身的提子；候选本身已在生成时通过硬约束。于是 `TryEvaluate(B2)` 在可达局面上恒为真。因此改用 M-A7a'，把"在 B2 上重判、淘汰候选"写成作废。
- tasks 1.8 的"纯函数文件里读 `RelicState.Content`"无法编译：`RelicState` 是 `RelicLedger` 的 private 嵌套类，别的文件看不到。因此改用 M-G3，读公开的真实内容载体 `RelicPlacement`。纯函数文件的扫描同时禁止 `ForbiddenForOfficialAi` 的全部类型名、`RelicState` 与 `TrueContents(`；反面命中取自 `RelicLedger.cs`。

### 既有测试改写

- `TurnSequence/合法落子范围的对外契约Tests.禁入扣除只在LegalRangeFor且与预演共用同一查询`：期望值由 `["MatchFlow.cs:LegalRangeFor"]` 改为 `["PublicRules.cs:LegalRange"]`。理由：D7 把合法落子范围连同禁入扣除抽成唯一纯函数，"全仓只有一处扣除"这一点不变。`.trellis/spec/core/boundaries.md` 中"从格集合里扣除禁入格只在 `MatchFlow.LegalRangeFor` 一处"一句随之过期，见待决 5。
- 其余既有测试一律未改，`难度分级Tests` 只追加了新用例。
- 变异通道已全部还原：三条注入变异借 `HeuristicAi.Create` 临时开的 `[ThreadStatic]` 通道，只在变异期间存在。`HeuristicAi.cs` 与 HEAD 逐字节相同，生产代码里没有任何 `Leak*` 成员。

### 待决

1. **D2 与 Safety 维冲突（阻塞 1.7，须在段 B 冒烟前裁决）**。
   - 实测数字：`AtariPosition` 上，高难选 G5 救四子串，自身分 1111；专家的前瞻集是 [G5, J8, G8]。J8 之后 P1 在 G5 提走四子串，前瞻后分数由 353 升到 1736，高于 G5 的 1111 → 991，于是专家选 J8，放弃救串。
   - 九维分解（原始值）：J8 前瞻前为 [4, 6, 0, 9, 0, 0, 0, 0, −2]，前瞻后为 [−1, −13, 0, 55, 0, 0, 0, 0, −3]。Safety 维 9 → 55，+46 × 35 = +1610，正是四子串 1 口气时的 danger（4 × 12 = 48）随串被提走而消失；势力 −5 × 10、敌损 −19 × 8 远抵不上。
   - `DoomedPosition` 同理：7 子串被提走后，前瞻后分数由 258 升到 2930。
   - 已排除传参错误：记录里 G5 与 J8 的差值与 danger 消失的量对得上，"前"确实是决策起点快照，合成结果带的是候选自身的提子。
   - 结论：在当前评价器下，"高难弃串、专家救串"不可达（A 的前瞻后分数恒比自身分高约 1400）。专家会系统性地放弃救已在危险中的棋串，**段 B 的 20 局冒烟大概率会把"专家弱于高难"跑出来**。
   - 可选修法（都只动 `ExpertLookahead.Simulate` 里那一次 `Evaluate` 的口径，其余交付物不作废）：
     - ① 前瞻后分数里，对"决策起点在盘、B2 上已被提走"的己方棋子，按 ≤ 1 口气的 danger 计入 Safety，把预期损失转成已实现损失。需改 D2 口径，只作用于前瞻，不改权重，不触及 Non-goal。
     - ② 前瞻后分数不计 Safety 维，只用其余八维，另加实际势力损失。需改 D2，前后两个分数的量纲不再一致。
     - ③ 维持 D2 原样，接受专家在叫吃局面上弱于高难，并写进设计文档。
   - 本段没有实施任何修法。
2. **待决 1 的影响范围**：「前瞻改变选择」与「被提走的损失压低前瞻后分数_待裁」两条以 `Skip` 挂起，理由即待决 1。另外两条已改为非提子局面并已转绿：
   - 「前瞻不引入 Pass」：10 子块被紧气，从安全落入危险，全部前瞻后分数约为 −2683。
   - 「回应后不重判活形硬约束」：断言改为测试内独立复算前瞻后分数，并确认即时势力增量维下降。
   裁决后需按新口径重摆「前瞻改变选择」的夹具，再补它的变异。
3. **耗时局面集**：v5 种子 1–3 上"玩家 1"只有 23 次部署，不满足"不少于 30"。改取四个座位的全部部署，每 3 个取 1，得 33 个；若要严格只取玩家 1，需扩到种子 1–5 左右。
4. **「前瞻宽度进入记录」只做到 Core 层**：`AiSearchConfig` 与 `RunConfig` 的序列化已完成，三档预设与改动前逐字节相同，专家预设带 `"LookaheadWidth":4`，缺字段读 0。"未显式给 `Search` 的专家在日志首部记前瞻宽度 4"要改 `ResolvedFor` / 首部，属于段 B 2.3。
5. **规范文字过期**：`.trellis/spec/core/boundaries.md` 活形 / 禁入一行里"扣除只在 `MatchFlow.LegalRangeFor` 一处"应改为 `PublicRules.LegalRange`（MatchFlow 委托）。段 A 不改文档，留待主会话决定何时改。
6. **「空批次不进前瞻集」在真实决策里不可达**：只要有一个落点越过阈值，每个扰动次序的贪心都会把它加进去，所以候选集合要么全空、要么全非空。该 Scenario 因此改为直接对 `ExpertLookahead.LookaheadSet` 做合成候选测试；真实决策里空批次与 Pass 的口径由「高难会 Pass 时专家也 Pass」覆盖。
7. 「同分取原次序」同样用 `SelectIndex` 的单元测试，钉住"第 2、第 3 个同分取第 2 个"。端到端的同分出现在「前瞻集不足 W 个」：D1 与 F1 的前瞻后分数都是 900，取靠前的 D1。

### 段末自验

- `dotnet build siege.sln`：0 警告 0 错误。
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test -c Release`：1729 通过、0 失败、8 跳过（改动前为 1685 / 0 / 5）。
- `openspec validate expert-lookahead --strict`：通过。
- 计时测试：`SIEGE_PERF=1`，1 条通过，比值 1.447。
- tasks.md 第 1 组：1.1–1.6、1.8–1.11 已勾选；**1.7 未勾选**（待决 1）。

## 段 A 续：裁决后的修正（2026-09-27，修法 ①）

### 改动

| 文件 | 内容 |
|---|---|
| `openspec/changes/expert-lookahead/specs/ai-decision/spec.md` | 「专家难度的一层前瞻」第 6 步补一段：决策起点在盘、被模拟回应提走的专家棋子，每枚按 ≤ 1 口气的危险计入安全维；只作用于前瞻后分数，不改权重，前后同一量纲；c 本批落下又被提走的棋子不计。新增两个 Scenario：「被提走的己方棋子按危险计入」「被提走的损失压低前瞻后分数」 |
| `openspec/changes/expert-lookahead/design.md` | D2 补一条"修正"说明（裁决记录本身由负责人写入） |
| `src/Siege.Core/Ai/GroupSafety.cs` | 新增常数 `AtariDangerPerStone = 12`。`Score` 的 ≤ 1 口气分支改为引用它，行为不变 |
| `src/Siege.Core/Ai/ExpertLookahead.cs` | `Respond` 额外返回回应的提子。`Simulate` 先用同一评价器 `Evaluate`，再经 `WithLostStonesInDanger` 处理：数出回应提走的、决策起点就在盘上的专家棋子（`CountLostAtStart`），在安全维原始值上扣 n × 12，其余八维不变。只看即时收益的难度不扣 |
| `.trellis/spec/core/boundaries.md` | "扣除禁入格只在一处"的归属由 `MatchFlow.LegalRangeFor` 改为 `PublicRules.LegalRange`，注明 MatchFlow 与前瞻都委托它；守门期望值同步写明（原待决 5） |
| `tests/.../AiDecision/专家难度的一层前瞻Tests.cs` | 「前瞻改变选择」重摆夹具并取消 Skip，删除 `PendingSafetyRuling`。新增「被提走的己方棋子按危险计入」「被提走的损失压低前瞻后分数」（后者即原 `_待裁` 那条，移到对应的 Requirement 类并取消 Skip） |
| `tests/.../AiDecision/前瞻中的停手口径Tests.cs` | 抽出复算夹具 `Recompute`：在测试里独立走一遍 B1 → 投影 → 模拟上下文 → B2，再用同一评价器打分，并数出被提走的起点棋子。「回应后不重判活形硬约束」的期望改为"直接打分 − 35 × 12 × n"，这是按新口径改写断言 |

### 「前瞻改变选择」的新夹具

保留 4 子串 C5–F5 只剩 G5 一口气的叫吃局面。原来提 2 子的 A 自身分只有 353，低于救串的 B（1111），所以把 A 改为提走 P2 的 3 子串：P2 在 G9–J9，被 P0 的 F9 / H8 / J8 围住，只剩 G8 一口气。实测：

- 高难按自身分选 A = G8：1157 对 1111。
- 专家的前瞻集依次为 [G8, G5, H7]：
  - G8：1157 → 860。之后 P1 在 G5 提走四子串。
  - G5：1111 → 991。之后 P1 的回应不落在 G5。
  - 专家选 G5，`ChangedChoice` 为真。
- 去掉修正后，G8 的前瞻后分数为 860 + 35 × 12 × 4 = 2540，专家又会弃串（M-A10 已证红）。

### 先红后绿

- 规格先改，随后测试先写：两个新 Scenario 与改写后的「回应后不重判活形硬约束」，在未修正的代码上红 3，其余 12 条绿。「前瞻改变选择」的新夹具在修正实现之后才定：先按实测挑出高难选 A 的局面，再用 M-A10 证明它依赖这条修正。
- 实现后，这两个测试类共 16 条全部通过，没有跳过。

### 补充变异（过滤集同前，还原后逐字节校验并刷新 mtime）

| 编号 | 变异 | 红 | 红的用例 |
|---|---|---|---|
| M-A10 | 去掉修正（前瞻后分数直接取 `Evaluate` 总分） | 4 | 前瞻改变选择、被提走的己方棋子按危险计入、被提走的损失压低前瞻后分数、回应后不重判活形硬约束 |
| M-A7a'（重跑） | 回应提走专家棋子时，候选作废 | 2 | 被提走的己方棋子按危险计入、回应后不重判活形硬约束 |
| M-A7e（重跑） | 前瞻后分数的"前"改用 B1 | 3 | 模拟回应全为 Pass、被提走的己方棋子按危险计入、回应后不重判活形硬约束 |

其余变异不涉及这次修改的代码，锚点也未变，没有重跑，红数以段 A 初版的表为准。

### 耗时（修正后重跑一次）

- 门控计时：33 个局面，丢弃预热 3 个，每组 60 个样本。
  - 高难：中位数 330.5 ms，p90 740.2 ms
  - 专家：中位数 449.5 ms，p90 768.6 ms
  - **中位数比值 1.360**，上限 4。两组的绝对值都比初版高，同一次运行内交替计时，比值口径不受影响。
- 预演次数代理：1.198，不变（修正不增加预演）。
- 局面集维持现取法：四个座位的全部部署每 3 个取 1，得 33 个；原因是 v5 种子 1–3 上玩家 1 只有 23 次部署，不足 30。负责人已知悉并同意维持。

### 段末自验（修正后）

- `dotnet build siege.sln`：0 警告 0 错误。
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test -c Release`：1732 通过、0 失败、6 跳过（改动前 1685 / 0 / 5）；耗时 2 分 56 秒，与机器负载有关，此前两次为 1 分 44–50 秒。
- `openspec validate expert-lookahead --strict`：通过。
- tasks.md 第 1 组 1.1–1.11 全部勾选。
- 仍待段 B 处理的只剩原待决 4：未显式给 Search 的专家，日志首部要记前瞻宽度，属于 2.3。
