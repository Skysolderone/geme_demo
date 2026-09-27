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

## 段 B——入口、遥测、设计文档、冒烟与回归（tasks 2.1–2.7）

### 结论先行

- 第 2 组 2.1–2.7 全部完成。
  - 默认套件 1754 通过、0 失败、6 跳过（段 A 末为 1732 / 0 / 6，本段新增 22 条）。
  - 两处 `dotnet build` 均为 0 警告 0 错误；`openspec validate expert-lookahead --strict` 通过。
- 三档旧难度逐步不变：
  - 「三档旧难度逐步不变」的三档黄金值、`候选格上限Tests.V4GoldenTurnHash` 等既有黄金哈希一字未改。
  - 4 名标准在 v5 种子 1 上的整局确定性文本，SHA-256 仍为改动前钉下的 `29353FC9…07736A`。
- 20 局冒烟（1 专家 + 3 标准）只报告，未调任何数值：
  - 专家胜 5 / 20 = 25%，与均等基线持平；平均名次 2.25。
  - 前瞻改变了选择 2 / 128 次（1.6%）。
- 真实 `%APPDATA%\Siege` 在段末仍不存在。

### 改动文件

| 文件 | 内容 |
|---|---|
| `src/Siege.Core/Ai/AiDifficultyNames.cs` | 新增，难度名称的唯一解析：`TryParse` / `Parse` / `Usage`。只认四个成员名，不区分大小写，不去空白；拒绝数字、未知名称和空值。`Parse` 抛 `ArgumentException`，消息列出 `Easy\|Standard\|Hard\|Expert`。放在 Core 是因为 Godot 不引用 Sim |
| `src/Siege.Sim/Program.cs` | `play` / `run` 的 `Enum.Parse` 换成 `AiDifficultyNames.Parse`；两处用法说明改为 `--difficulty <{Usage}>`。`Execute` 增加 `playInput` / `playOutput` 测试接缝，原因是 `play` 开头设置 `Console.InputEncoding` 时，运行时会丢弃 `Console.SetIn` 换上的输入 |
| `src/Siege.Sim/Config/RunConfig.cs` | ① `PlayerAiConfig.Difficulty` 挂属性级转换器 `AiDifficultyNameConverter`：读入只认字符串并经共用解析，数字 token、`"3"`、`""`、`null` 一律抛 `ArgumentException`；写出仍为枚举名，首部逐字节不变。② `ResolvedFor` 末尾新增一步（原待决 4）：未显式给 `Search`、而难度预设带前瞻的玩家（即专家），落成 `ForMap(难度, 可落子格, 已落成的 K) with { PassThreshold = 已落成的阈值 }` 写进 config.json 与首部。落成值与会话建 AI 时按难度取的值相同 |
| `src/Siege.Sim/Logging/MatchLog.cs` | ① `TurnSnapshot.Lookahead`（`LookaheadLogEntry?`），带属性级 `WhenWritingNull`。② 新增 `LookaheadLogEntry`：状态、前瞻集、被选下标、是否改变选择、模拟对手预演次数。③ 新增 `LookaheadCandidateEntry`：批次键、前瞻前 / 后分数（`BigInteger`）、下一名对手、行动大回合、模拟部署上限、回应批次键。④ `MatchLog.LookaheadTurns`；`LookaheadWidthOf(player)`，缺 `Search` 或缺该字段时按 0 |
| `src/Siege.Sim/Running/LoggingController.cs` | 新增 `TurnTrace.Lookahead`，每小回合 `Reset` 时清空。装饰器在内层 `Deploy` 之后取 `Heuristic?.LastLookahead`，所以上一小回合的记录不会写进本小回合 |
| `src/Siege.Sim/Running/MatchSession.cs` | 小回合快照写入 `Lookahead = LookaheadLogEntry.From(trace)` |
| `src/Siege.Sim/Running/Replayer.cs` | 只补注释：前瞻宽度随首部 `Players[].Search` 重建，前瞻记录随确定性文本逐行核对，无需新代码 |
| `src/Siege.Sim/Play/PlayCommand.cs` | 前瞻宽度 > 0 时，开局行追加"（前瞻宽度 4）"；三档旧难度的这一行逐字不变。新增测试接缝 `onAi`：每名 AI 控制者建好后回调一次 |
| `src/Siege.Presentation/MapSelect/MapSelectModel.cs` | 难度选择：构造参数 `difficulty`（预选，缺省标准）、`DifficultyOptions`（四档，按枚举次序）、`Difficulty`、`SelectDifficulty`（确认开局后拒绝；未定义值抛异常） |
| `src/Siege.Presentation/Text/Labels.cs` | 新增 `Difficulty`：简单 / 标准 / 高难 / 专家 |
| `src/godot/scripts/GameRoot.cs` | ① `--difficulty=` 在结算之前经 `args.Value<AiDifficulty>(…, AiDifficultyNames.TryParse)` 读取。② 无人值守（自动演示 / 拾取自检 / 截图）时同时给出 `--difficulty=`，抛 `FormatException` 退出。③ `_difficulty = difficulty ?? Standard`，直接建局的路径用 `_difficulty`。④ 日志：启动行打"AI 难度 专家（Expert，前瞻宽度 4）"；AI 小回合有前瞻记录时打一行 `[ai]`；选图截图的自证行加上"AI 难度 X" |
| `src/godot/scripts/GameRoot.MapSelect.cs` | 改为 `new MapSelectModel(NewMapSeed(), _difficulty)`；接上 `MapDifficultyPicked`；「开始」时取 `_difficulty = _select.Difficulty` 建局。预览会话仍写死标准：此时未插旗，AI 不行动 |
| `src/godot/scripts/GameRoot.Carry.cs` | 补给确认后的两处建局改用 `_difficulty` |
| `src/godot/scripts/Hud.MapSelect.cs` | 选图面板加"AI 难度"一行：四个 `Ui.Action` 按钮，选中标记（●/○）与地图清单同样式，另加一行命令行提示。只用现有控件，面板宽度仍为 360 |
| `src/godot/scripts/MatchSession.cs` | 新增 `LastAiLookahead`：AI 小回合结束后读该控制者的 `LastLookahead`，转成一行文字；前瞻宽度为 0 的三档为 null |
| `2026-09-10-siege-core-gameplay-design-v1.md` | v1.15 → v1.16，见下文 2.5 |
| 测试 | 新增 4 个测试类共 22 条：`SimulationHarness/各入口的难度选项Tests.cs`（8 个方法、11 条）、`SimulationHarness/终端专家对局Tests.cs`（1）、`MatchTelemetry/专家前瞻的记录Tests.cs`（6）、`MapSelection/选图界面难度选择Tests.cs`（4）。另改写 1 条既有测试，见下文 |

### 先红后绿

- **2.1**：
  - 先落骨架：`AiDifficultyNames` 的方法体抛 `NotImplementedException`；`Execute` 加接缝；日志类型只定义、不写入。
  - 再写 `各入口的难度选项Tests`。初跑 11 条，红 9、绿 2。
  - 绿的两条是守门型，改动前就该绿：「缺省仍为标准」「真实档案目录不被触碰」。
- **2.3**：
  - 写 `专家前瞻的记录Tests` 时，写入端尚未实现。初跑红 4、绿 2。
  - 绿的两条同样是守门型：「非专家没有前瞻记录」「旧日志照常解析」。
  - 此时 2.1 的「逐玩家难度」也仍是红的。实现写入端后全部转绿。
- **2.4**：
  - `MapSelectModel` / `Labels` 骨架抛异常，`选图界面难度选择Tests` 4 条全红；实现后转绿。
  - 其中「图形版难度参数…」一度因测试自身的定位写法而红：被测的读取调用跨了行。改为容许空白的正则后转绿，断言内容不变。
- **2.2 没有独立的先红**：
  - 终端开局行与 `onAi` 接缝是随 2.1「名称不区分大小写」一起实现的，那一条先红过。
  - `终端专家对局Tests` 写在实现之后，改由 M-B2 证明它会红。
- **种子选择**：用探针选定，条件为 v5、写死权重、种子 1–5，整局和 16 小回合各跑一轮。探针文件已删除。
  - 选种子 3 的整局：已前瞻 6 次、Pass 4 次、"前瞻改变选择" 1 次。
  - 选种子 2 的 16 小回合（1 名隐式专家 + 1 名显式 W = 2 专家）：两名专家都有已前瞻。
- **整段复审**：
  - 全量首跑红 7 条，其中 6 条出自同一原因：`SimFixtures.TurnTexts` 用缺省序列化选项，会把 `"Lookahead":null` 写进每条快照，导致 6 个快照黄金哈希变化。
  - 修法：给该属性加属性级 `WhenWritingNull`，与 `RelaySources` 同一先例。加上后这 6 条全绿，黄金值一字未改。
  - 这等于一次自然变异，证明这 6 条既有守门能挡住"快照多写一项"。
  - 第 7 条是下面这条既有测试。

### 既有测试改写（1 条）

- 测试：`AiDecision/专家难度的一层前瞻Tests.前瞻宽度为1时与高难逐步相同`（段 A）。
- 原断言：W = 1 专家与高难的小回合快照，以及首部以外的确定性文本，逐项相同。
- 变红原因：2.3 之后，前瞻宽度 > 0 的控制者每次部署都留一条记录（W = 1 时只会记 NotApplied / Pass），快照因此多了 `Lookahead` 一项。
- 改法：
  - 先断言玩家 1 的每个小回合都有记录，状态只能是 NotApplied 或 Pass；其余玩家都没有记录。
  - 再去掉前瞻记录，与高难逐项比较。
  - 决策序列相同的断言不动。
- 规格 Scenario 的 THEN 只要求"每一步决策逐步相同"，所以这是按新遥测收紧后的等价改写。

### 变异验证

脚本沿用段 A 的纪律：
- 二进制读写，逐文件探测行尾，锚点命中恰为 1 次。
- 还原放在 `finally`，还原后逐字节校验并执行 `os.utime`；备份文件名带时间戳。
- 设 `DOTNET_CLI_UI_LANGUAGE=en`，从 `Passed!` / `Failed!` 统计行取数。

过滤集共 104 条，覆盖：各入口的难度选项、专家前瞻的记录、终端专家对局、MapSelection、可复现回放、批量跑局Tests、难度分级、专家难度的一层前瞻。基线全绿；13 条变异跑完后收尾重跑仍全绿；`git diff --stat` 与跑前逐字节相同。

| 编号 | 变异 | 红 | 红的用例 |
|---|---|---|---|
| M-B1a | `play` / `run` 恢复 `Enum.Parse(…, ignoreCase: true)` | 5 | 数字难度被拒绝 × 4、未知难度被拒绝（空值） |
| M-B1b | 配置文件绕过共用解析：转换器改为 `Enum.Parse`，并接受数字 token | 1 | 未知难度被拒绝（配置文件分支） |
| M-B2 | 终端建 AI 时，专家用高难的搜索配置 | 2 | 专家难度开局_AI按专家决策、名称不区分大小写 |
| M-B3a | 非专家也写前瞻记录：控制者没有记录时写一条 Pass | 10 | 非专家没有前瞻记录、三档旧难度逐步不变 × 3、旧日志照常解析、缺省仍为标准、逐玩家难度、前瞻前后分数可查、前瞻宽度为1时与高难逐步相同、既有「批量跑局Tests.缺省关闭带入」 |
| M-B3b | 前瞻记录写入墙钟：预演次数加上时间戳余数 | 2 | 前瞻前后分数可查、回放核对前瞻记录 |
| M-B3c | 回放不读首部的前瞻宽度（归 0） | 1 | 回放核对前瞻记录 |
| M-B3c' | 回放不读首部的前瞻宽度（回落到难度预设 4） | 1 | 回放核对前瞻记录（靠样本里显式 W = 2 的那名专家） |
| M-B3d | `ResolvedFor` 不落成专家的搜索配置 | 3 | 批量跑局选专家、逐玩家难度、回放核对前瞻记录 |
| M-B4a | 选图「开始」建局时仍写死标准 | 1 | 建局用所选难度_只有预览固定标准 |
| M-B4b | 去掉无人值守时对 `--difficulty=` 的拒绝 | 1 | 无人值守固定标准难度 |
| M-B4c | 图形版改用 `Enum.TryParse` | 1 | 图形版难度参数经共用解析且在结算之前读取 |
| M-B4d | `SelectDifficulty` 不检查是否已确认开局 | 1 | 难度选择缺省标准_四档按次序_可预选可切换 |
| （自然） | `TurnSnapshot.Lookahead` 不加属性级 `WhenWritingNull` | 6 | 既有快照黄金哈希 6 条（见上） |

### 2.2 终端（偏差）

- 终端 `play` 不写对局日志，所以 tasks 2.2 要求的"断言 AI 玩家的日志首部记有专家与前瞻宽度 4"写不成。改用两处核对：
  - 开局行"对手 3 名 Expert AI（前瞻宽度 4）"：宽度取自实际建 AI 时用的搜索配置。
  - `onAi` 接缝：3 名 AI 的 `Config` 等于 `Expert` 预设（阈值写死为 80）；每名至少决策 2 次，且都留有前瞻记录。
- 脚本与既有终端脚本同形：选 1 号区 → 第 1 大回合落 B1 → 第 2 大回合 Pass → 在第 3 大回合的提示处输入耗尽后退出。权重、阈值、冒险概率与内容集（v1）全部写死。
- 既有终端脚本测试未改动，保持绿。

### 2.4 Godot

构建为 Debug；`$G` = `Godot_v4.7.2-stable_mono_win64_console.exe`，参数带 `--path src/godot`。

| 命令 | 结果 |
|---|---|
| `$G --headless … --quit-after 3000 -- --auto-demo` | EXIT 0。日志为"AI 难度 标准（Standard，前瞻宽度 0）"，没有 `[ai]` 行；开局对准自检通过 |
| `… -- --auto-demo --pick-check` | EXIT 0；105 / 105 ×2，失败 0 |
| `… -- --map-select --auto-demo` | EXIT 0；选图自检 10 步全过，建局为标准难度 |
| `… -- --auto-demo --difficulty=Expert` | EXIT 1；报"--auto-demo / --pick-check / --screenshot 固定标准难度，不接受 --difficulty=。" |
| `… -- --difficulty=3 --map=siege-4p-base-v5 --no-carry`（另测 `=Master`） | EXIT 1；报"无效：应为难度名称（Easy\|Standard\|Hard\|Expert，不区分大小写）"，并列出合法选项表 |
| `… --quit-after 200 -- --difficulty=expert --map=siege-4p-base-v5 --no-carry --seed=7` | EXIT 0；日志为"AI 难度 专家（Expert，前瞻宽度 4）"。非无人值守时对局停在等人插旗，AI 不会行动 |
| 临时探针：只在文件里临时把拒绝条件改成运行时恒假，跑 `--auto-demo --difficulty=Expert` | EXIT 0；开局即专家。4 个大回合内共 12 条 `[ai] … 专家前瞻：Applied，前瞻集 3–4 个，选第 1 个 […]（前瞻前 → 后）`。跑完后逐字节还原、刷新 mtime、重新构建（0 警告），并确认 DLL 时间晚于源文件 |
| `$G --path src/godot -- --map-select --screenshot=E:/wws/geme_demo/art/expert-lookahead/map-select-difficulty.png:40`（非 headless） | EXIT 0；1600×900，第 40 帧。自证行为"面板 (14, 14) 360×633，对局面板 隐藏；AI 难度 标准"。截图只存盘、未读回，**交负责人过目** |

"`--difficulty=Expert` 后 AI 按专家决策"在无人值守模式下无法自动实测：自检固定为标准，而非无人值守时要有人插旗，对局才会开始。现有证据只有三项：临时探针、开局行，以及 Sim 侧对同一个 `HeuristicAi.Create(…, Expert, config: ForMap(Expert))` 的测试。见待决 1。

### 2.5 设计文档 v1.15 → v1.16 人工核对清单

| 项 | 文档 | 规格 / 代码 | 一致 |
|---|---|---|---|
| 版本行 | v1.16，最近一次为 expert-lookahead | — | ✔ |
| §15.1 前瞻的公平信息 | "只用公开信息与自己的私有信息，对手按部署上限枚普通子建模；未揭示信物保持未揭示、按先验估值" | ai-decision「前瞻模拟的公平信息」第 1、5 项 | ✔ |
| 难度表专家列 | 九维 / ✔ / 24 / 32 / 前瞻宽度 4（其余三档为 —） | `AiSearchConfig.Expert = Hard with { LookaheadWidth = 4 }`；Hard 为 N 24 / M 32 | ✔ |
| 停手阈值共用 | "各档共用（专家与高难同一口径）" | 「难度分级」：对全部难度生效 | ✔ |
| 机制 | 去掉空批次后取前 W 个；B1 → 标准启发式回应 → B2；以决策起点为"前"、用同一评价器；同分取原次序；不重判活形；起点棋子被提走时每枚扣 12 | 「一层前瞻」第 2–7 步；`GroupSafety.AtariDangerPerStone = 12` | ✔ |
| W 为 0 / 1 | "0 与 1 都表示不前瞻" | 第 3 步与宽度定义 | ✔ |
| 下一名对手 | 跳过弃赛、出局、被本候选打到出局者；专家为末位时，跨大回合经先手值的唯一实现预测；种子兜底以玩家编号代替；只剩本人时不模拟 | 「前瞻中的下一名对手」第 1–4 项 | ✔ |
| 停手口径 | 阈值与活形硬约束只在前瞻之前生效；Pass 不进前瞻集；不因前瞻而 Pass | 「前瞻中的停手口径」 | ✔ |
| 对手模型 | D = 基础值 + B1 上受控的已揭示军令；落子范围与流程同一实现；用本人权重；K 与阈值随本局；单条无扰动贪心；同形历史 = 起点 + B1 | 「前瞻模拟的公平信息」第 1–4、6 项 | ✔ |
| 耗时 | ≤ 4 × 高难；实测 1.36，代理约 1.20 | 「确定性与耗时」；段 A 实测 1.360 / 1.198 | ✔ |
| 遥测 | 宽度写入首部，为 0 时不写；每次部署一条记录 | ai-decision 宽度段；match-telemetry | ✔ |
| §18.2 | "完整博弈树 AI"未改 | tasks 2.5 | ✔ |
| §19 第 4 项 | "首期已由 expert-lookahead（v1.16）实现……多层搜索仍不做" | design.md Migration 第 5 项 | ✔ |
| 变更记录 | 新增一行，含依据与冒烟数据；数值与下文 2.6 表相同 | 冒烟摘要 | ✔ |

注：难度表列头沿用文档原有的"困难"，正文写作"困难（高难）"；规格与代码用"高难"。本次没有改列头。

### 2.6 冒烟（20 局，只报告）

- **配置**：`siege-4p-base-v5`、种子 1–20、内容集 V2，`Players` = Expert、Standard × 3；其余取缺省（截断 600、缺省权重、阈值 80、K = 0）。命令行只给 `--out` / `--config` / `--serial`。
- **配置核对**：已打开落盘的 `config.json` 逐项核对：
  - 专家的 `Search` 为 24 / 32，阈值 80，`LookaheadWidth` 4。
  - 三名标准的 `Search` 为空。
  - 四名玩家的权重都是缺省九维。
- **数据**：`sim-out/expert-lookahead/smoke20/`，含 20 份日志、`config.json`、`input-config.json`、一次性脚本 `smoke_report.py` 与摘要 `smoke-summary.txt`。串行运行，批次墙钟 146.7 s。

| 指标 | 值 |
|---|---|
| 截断 / 终局原因 | 截断 0；终局 20 / 20 都是整轮 Pass（AllPassed） |
| 结束大回合 | 平均 8.95，中位 8，范围 7–22 |
| 专家胜率 | 5 / 20 = 25%，无并列（均等基线 25%）；各座位胜局 P0 5 / P1 6 / P2 5 / P3 4 |
| 专家平均名次 | 2.25；名次 1 / 2 / 3 / 4 分别 5 / 8 / 4 / 3 局 |
| 单小回合耗时（串行，取日志 `ElapsedMs`） | 专家 n = 171，中位 439 ms，p90 866 ms；标准 n = 515，中位 106 ms，p90 207 ms |
| 同上，只看非 Pass 小回合 | 专家中位 414 ms，p90 731 ms；标准中位 103 ms，p90 233 ms |
| 专家 / 标准中位数之比 | 约 4.1。注意这是与标准比，不是验收口径；验收口径是与高难比 ≤ 4 倍，段 A 同局面计时为 1.36 |
| 前瞻状态 | 共 171 次决策：已前瞻 128、不前瞻 2、Pass 41。已前瞻时前瞻集为 2 / 3 / 4 个的各 9 / 27 / 92 次 |
| 前瞻改变选择 | 占已前瞻 2 / 128（1.6%），占全部决策 1.2% |
| 被选候选"前瞻后 − 前"分数差（128 次） | < 0 共 77 次，= 0 共 28 次，> 0 共 23 次；最小 −501，P10 −145，P25 −116，中位 −8，P75 0，P90 16，最大 54 |

样本只有 20 局，胜率的置信区间约 ±20 个百分点，因此只报告、不下"专家更强 / 更弱"的结论。未跑对照组，未调任何数值。

### 段末自验

- `dotnet build siege.sln`：0 警告 0 错误。
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test -c Release`：1754 通过、0 失败、6 跳过，退出码 0。
- `openspec validate expert-lookahead --strict`：通过。
- `%APPDATA%\Siege`：不存在。本段所有 `play` 用例都带 `--no-carry`，Godot 的非无人值守启动也一律带 `--no-carry`。
- tasks.md 2.1–2.7 已勾选。

### 待决

1. **无人值守时给出 `--difficulty=`**：现按 D10"固定标准"的字面处理，同时给出即报错退出。代价是"`--difficulty=Expert` 后 AI 按专家决策"只能靠临时探针或人工试玩来验证。备选做法：显式给出 `--difficulty=` 时，自动演示改用该难度；不给时仍为标准。请负责人裁决。
2. **2.2 的偏差**：终端不写日志，改用开局行与 `onAi` 接缝核对，详见上文 2.2。
3. **专家决策耗时的体感**：冒烟中专家单小回合中位 0.44 s、p90 0.87 s、最大 1.14 s。Godot 的 AI 回合跑在主线程上，每个专家回合会多卡约半秒。本次未做人工试玩核实，design.md Risks 已有记录。
4. **"前瞻改变选择"只有 1.6%**：被选候选的前瞻后分数中位比前瞻前低 8，多数情况下前瞻只是确认了高难的选择。这只是报告项，不在本 change 内调整。
5. **已知不一致（理论缺口，不影响任何现有日志）**：难度为专家、但首部没有 `Search` 的玩家，`MatchLog.LookaheadWidthOf` 读 0，回放（`AttachConfigured` 按难度取 `ForMap(Expert)`）却按 4 重建。写入端 `ResolvedFor` 保证新日志里专家一定带 `Search`，所以这种首部不会产生；本段未改代码。
