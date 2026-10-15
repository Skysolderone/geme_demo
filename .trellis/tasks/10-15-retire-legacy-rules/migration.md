# retire-legacy-rules 测试迁移清单

> 状态：规划稿，待负责人过目（R4 第一步）。实施第 1 组开始时用脚本按方法名复核本表并回填实际条数；本表分类为人工判读 + 脚本信号，标"待定"的按缺省执行、偏离即上报。

## 0. 口径

- **分类**（R1 三类之外另设四个记账类，避免硬塞）：
  - **(a) 规则层**：只测棋盘 / 纯规则的方法，改为直接在 `GameBoard` 与规则层入口上测（`BatchRehearsal.Rehearse`、`PowerCalculator.Compute`、`LifeShapeReport.Analyze`、`CoverageMap.Compute`、`RelicLedger.SnapshotFor`、`FinalStandings`、`InitiativeOrder`、`PublicRules`），沿用 9×9 地图数据与原坐标，**期望值不变**。
  - **(b) 链接夹具**：测对局流程、链接模式下同样成立的方法，改用链接模式载体，**断言含义不变**。缺省载体见 R6：FlatMap（`link:90003`，K = 0）上**四人同区** `StartedFlat([0,0,0,0])`——单一战场、无配对无链接、整轮 Pass 结算即终局，是旧 9×9"第 4 大回合起全图"阶段最近的等价物；需要新地表 / 特殊地形 / 特殊形状时用手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`）。
  - **(c) 删除**：只测旧规则的方法（出生区保护期、第 4 大回合全图解禁、整轮 Pass / 填满即终局、全局连续 Pass 计数、Legacy 名次组序、看得见旗数 / 锁定前可改、Legacy 存档 / 日志字段、"旧模式不变 / 全图展开"对照）。
  - **(j) 合并**：迁过去后与既有链接测试逐条同义（同一 Scenario、同一载体），删本条、保留目标条；名字消失但覆盖不丢，质检与 (c) 一并逐名比对（R11）。
  - **(d) 地图数据**：只用 9×9 `MapData`（不建局）的方法，数据迁入 `TestMaps` 后零断言变化。
  - **(e) 常量**：只引 `P0..P3` / `All` / `Seed`，改引新位置。
  - **(m) 机械**：已在链接载体，只删 `Mode = MatchMode.Link` 写法、`ComputeLink` 更名等；带"删反面段"标注的方法保留、删掉其中的 Legacy 对照段。
- **条数**：1 个 `[Fact]` 或 1 个 `[Theory]` 记 1 条方法；Theory 的 `InlineData` 行数另列（"行"），迁移不改行数。Δ = 本文件方法数变化（(c) 与 (j) 减少；"迁入 / 迁出"在文件间搬动，不计入总变化）。
- **方法数取自脚本**（按 `[Fact]` / `[Theory]` 切方法体、匹配 `MatchFixtures.Started|Create|Map|Relics`、`Round5(`、`StartedWithZonedRelics`、`Create/RestoreUnvalidated`、`MatchMode.Legacy`、文件内包了这些的私有助手），个别文件 ±1，实施时复核。
- **范围**：派发口径"109 个使用 `MatchFixtures` 的文件"实测为 101 个调用建局 / 地图助手 + 8 个只引常量；另有 **16 个文件经 `AiFixtures.Round5` 或 `PresentationFixtures.StartedWithZonedRelics` 间接走 Legacy**（不在 109 内）、**8 个文件只因 `MatchMode` 写法或旧模式断言受影响**、**4 个带出测试经 `CarryFixtures.Result` 走 Legacy 名次算法**。`BoardTopology/盘面序列化Tests` 只用 `GameBoard.RestoreUnvalidated`（盘面恢复，与对局模式无关），排除。

## 1. 逐文件清单

### AiDecision

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `AI决策的可复现性Tests.cs` | MF | ai-decision「AI 决策的可复现性」：同局面同决策、并列确定性打破 | 2 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） + `AtRound(5)` | 0 |
| `AI对地表的感知Tests.cs` | MF | terrain-surfaces × ai-decision「AI 对地表的感知」：荒漠 / 沼泽 / 岩台 / 浅滩进入评价 | 5 / 5 | b×5 | 手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）（新地表只能放在未校验图上：校验器禁止出生棋盘内新地表）；E5 孤子改为棋盘 0 中间带的同形局面 | 0 |
| `AI枚举改造目标Tests.cs` | MF | ai-decision 改造目标枚举：能一手立栅提子时选该手 | 5 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `专家难度的一层前瞻Tests.cs` | MF | ai-decision「专家难度的一层前瞻」：前瞻改变选择、两项之和、旧日志回放、宽度 1 等价高难、缺省预设等价一层 | 16（Theory 2，6 行） / 10 | b×10 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） + `AtRound(5)`；被测 AI 须为本战场势力最高（避开落后者阈值 0 / 决胜一手两道链接门）<br>**待定 / 注**：数值类断言（前瞻后分数）在新几何上可能变化 → 按 R8 记入"期望重钉表" | 0 |
| `候选格上限Tests.cs` | MF | ai-decision「候选格上限」：小图不限制、前 K 格预筛、匠人回退、K≥合法格等价、小 K 找妙手、三入口同一阈值 | 15（Theory 2，5 行） / 12 | a×1 b×11 | 阈值解析（`大图缺省取上限小图为零显式配置优先`）→ a：对 9×9 地图数据与 `link:1` 直接调阈值函数；其余 → 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；`小图不限制` → 手写小链接档图（5 块 5×5，可落子 125 ≤ 150，`CreateUnvalidated`）<br>**待定 / 注**：`小图不限制`：链接图可落子格恒 > 150，"小图"只能是未校验的手写图（规格同步改措辞，见 specs/ai-decision）；脚本口径把 `剪枝参数的文本往返与旧文本兼容` 计入，实施时核对，可能实为不涉及 | 0 |
| `停手阈值Tests.cs` | IND | ai-decision「停手阈值」：零收益 / 低于 / 恰等于 / 高于阈值、无子豁免、同批次不中途切换 | 15 / 9 | b×9 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；被测 AI 须为本战场势力最高或独处（否则落后者阈值取 0，断言含义改变）<br>**待定 / 注**：若某局面无法让被测 AI 居首而不改断言 → 单列上报，不改期望 | 0 |
| `公开规则纯函数Tests.cs` | MF | expert-lookahead D7 纯函数等价：部署上限、出局判据、先锋修正、合法范围（新旧路径逐项相等） | 5（Theory 1，3 行） / 5 | b×5 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；`真实对局…`（Theory 3 行）改在 `link:1` 探针上跑<br>**删反面段**：`真实对局每个小回合新旧两条路径逐项相等`、`夹具局面新旧两条路径逐项相等`<br>**待定 / 注**：两条等价测试里"合法范围旧路径"（`OldLegalRange`，抄录的保护期 / 全图规则）只在 Legacy 成立 → 删该段，改为 批次上下文 = `LegalRangeFor` = `PublicRules.LegalRange` 三方相等；`RelicLedger.Restore` 作对照处改用 `Copy()` | 0 |
| `前瞻中的下一名对手Tests.cs` | MF | ai-decision「前瞻中的下一名对手」：跳过弃赛 / 出局者、候选使对手出局、末位跨大回合、只剩自己退化 | 8 / 6 | b×6 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；`专家是本大回合末位` 断言改为"对手所在战场的可落子格"、`只剩自己时退化` 改 4 人局（规格同步 MODIFIED） | 0 |
| `前瞻中的停手口径Tests.cs` | IND | ai-decision「前瞻中的停手口径」 | 6 / 4 | b×4 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `前瞻模拟的公平信息Tests.cs` | MF | ai-decision「前瞻模拟的公平信息」：对手手牌不同结果不变、模拟部署上限、保护期只在出生区回应、未揭示信物、投影一致 | 10 / 10 | b×10 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；`保护期内只在出生区回应` 改为"构筑期内只在其战场内回应"（对手独占另一块棋盘，规格同步 MODIFIED；方法改名记入改名表） | 0 |
| `前瞻的近似两层加分Tests.cs` | MF | ai-decision「前瞻的近似两层加分」：按下一大回合口径枚举、专家出局时加分为 0 | 9 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；`按下一大回合的口径枚举` 断言改为"专家所在战场的可落子格"（规格同步 MODIFIED） | 0 |
| `前瞻集的多样候选Tests.cs` | IND | ai-decision「前瞻集的多样候选」：补充候选同受停手阈值 | 7 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `启发式评价维度Tests.cs` | IND | ai-decision「启发式评价维度」：九维覆盖、权重、先手位、眼位 / 活形 / 威胁增量 | 14 / 9 | b×9 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）（眼位算例平移到棋盘 0 核心区外圈，局部形状不变）<br>**待定 / 注**：评分数值随全局量（空区、信物位置）可能变化 → R8 | 0 |
| `对隐藏信息的概率估计Tests.cs` | MF | ai-decision「对隐藏信息的概率估计」：估计不得使用真实值 | 2（Theory 1，16 行） / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `已确定活形的评价中性Tests.cs` | IND | ai-decision「已确定活形的评价中性」 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `新棋子与新信物的AI适配Tests.cs` | MF | ai-decision「新棋子与新信物的 AI 适配」 | 8 / 7 | b×7 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）（信物格按用例重设，同 `PresentationFixtures` 的分区信物写法） | 0 |
| `正式对战AI的信息边界Tests.cs` | MF | ai-decision「正式对战 AI 的信息边界」：未知信物 / 敌方手牌数 / 未确认批次不可读 | 4 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `正式对战AI的信息边界_链接模式Tests.cs` | MF | ai-decision 信息边界（链接）：全知例外集中可关等 | 9（Theory 2，7 行） / — | m×1 | 已在链接载体<br>**删反面段**：`全知例外集中可关`<br>**待定 / 注**：删"旧模式：投影即全知视图"反面段 | 0 |
| `活形分析的决策内缓存Tests.cs` | IND | ai-decision「活形分析的决策内缓存」 | 4 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `活形硬约束Tests.cs` | IND | ai-decision「活形硬约束」 | 6 / 6 | b×6 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `链接模式下的决胜一手Tests.cs` | MM | ai-decision「链接模式下的决胜一手」 | 6 / 1 | c×1 m×5 | 其余已在链接载体（去 `Mode = MatchMode.Link` 写法）<br>**删**：`旧模式不变` | -1 |
| `链接模式下落后者的停手阈值Tests.cs` | MM | ai-decision「链接模式下落后者的停手阈值」 | 5（Theory 2，4 行） / 1 | c×1 m×4 | 同上<br>**删**：`旧模式不变` | -1 |
| `难度分级Tests.cs` | MF | ai-decision「难度分级」：简单也会做活、高难 / 专家不越权 | 6 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `高部署上限下的候选剪枝Tests.cs` | IND | ai-decision「高部署上限下的候选剪枝」 | 4（Theory 1，3 行） / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### BatchDeployment

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `批次数量与库存约束Tests.cs` | MF | batch-deployment：分阶段基础部署上限（含插旗阶段） | 9（Theory 2，9 行） / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）（`基础值随阶段提高` Theory 3 行照旧） | 0 |
| `非法批次必须给出可定位的原因Tests.cs` | MF | batch-deployment：活棋禁入返回落点与棋串 | 12 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### BatchPreview

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `批次预演必须显示的信息Tests.cs` | IND | batch-preview：部署额度、Pass 前警示撤销新征募 | 6 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `未知信物在预演中只显示将揭示Tests.cs` | IND | batch-preview：未知信物只显示将揭示 | 1 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `计分信物与新棋子的预演Tests.cs` | MF | batch-preview：已揭示 / 将揭示连营、新棋子位置加值 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）（连营信物放棋盘 0 公共区档格） | 0 |
| `非法批次必须说明原因并高亮Tests.cs` | MF | batch-preview：活棋禁入说明 | 7 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `预演一致性回归Tests.cs` | MF | implement 6.2 强制回归：千例随机局面预演 = 正式结算 | 1 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；随机局面生成器改在棋盘 0 内取格 | 0 |
| `预演不改变游戏状态Tests.cs` | IND | batch-preview：预演零副作用 | 1 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### Battlefield

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `战场的定义与规则作用域Tests.cs` | MF | battlefield「战场的定义」：无棋盘清单地图的孤立可落子区不属于任何战场编号 | 6 / 1 | d×1 | 9×9 地图数据迁入 `TestMaps`（GameBoard 级，不建局），断言不变 | 0 |
| `链接后的公开补充载荷Tests.cs` | MF | RelicLedger.Copy 在拼接后照常 | 3 / 1 | c×1 | —<br>**删**：`旧模式副本与按生成记录恢复逐项相同`<br>**待定 / 注**：`RelicLedger.Restore` 只剩 Legacy 恢复一处生产调用，随之删除（D7） | -1 |
| `阶段推进Tests.cs` | MF | battlefield「阶段推进」 | 11 / 1 | c×1 | —<br>**删**：`旧模式没有阶段转换` | -1 |

### CarryInOut

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `AI带入Tests.cs` | MM | carry-in-out「AI 带入」 | 7 / — | m×7 | 去 `Mode = MatchMode.Link` 写法 | 0 |
| `出局结算Tests.cs` | CF | carry-in-out「出局结算」（经 `CarryFixtures.Result`） | 3 / 1 | a×1 | `CarryFixtures.Result` 改调唯一名次实现、终局原因改 `LastBattlefieldSettled`；输入只有完赛 + 出局两组，两种组序相同，期望不变 | 0 |
| `名次补给点表Tests.cs` | CF | carry-in-out「名次补给点表」 | 5 / 2 | a×2 | 同上；输入全为完赛者，期望不变 | 0 |
| `完赛结算Tests.cs` | CF | carry-in-out「完赛结算」 | 2 / 2 | a×2 | 同上；`只剩一名参赛玩家` 输入含出局 + 弃赛（两种组序不同），但断言只读第 1 名与弃赛快照点数，已核期望不变 | 0 |
| `弃赛结算Tests.cs` | CF | carry-in-out「弃赛结算」（含保护期内弃赛不带出） | 6 / 5 | a×5 | 同上；输入为完赛 + 弃赛，两种组序相同，期望不变 | 0 |
| `战场落败出局结算Tests.cs` | MF | carry-in-out 战场落败 | 1 / — | e×1 | 只引常量 P0..P3 | 0 |
| `换型候选类型Tests.cs` | MF | carry-in-out「换型候选类型」：建局拒绝非候选 / 不合形带入 | 5（Theory 1，3 行） / 2 | b×2 | `LinkModeFixtures.Create(options)`（建局校验与模式无关） | 0 |
| `补给种类与开局效果Tests.cs` | MF | carry-in-out「补给种类与开局效果」 | 5 / 5 | b×5 | `LinkModeFixtures.Create` + 依次插旗（开局手牌与模式无关） | 0 |
| `CarryFixtures.cs` | FIX | 带出夹具：`Result` | — | — | 改调唯一名次实现、原因 `LastBattlefieldSettled` | 0 |

### EliminationEndgame

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `三类终局条件Tests.cs` | MF | elimination-endgame 旧「三类终局条件」：整轮 Pass / 填满即终局、全局连续 Pass 计数、旧优先级链 | 8 / 8 | c×4 j×2 b×2 | 整文件删除；`势力悬殊不提前结束` 迁入 `两类终局条件Tests`（同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0），一方领先数百仍不终局）；`Pass计数跨回合重置` 迁入 `两类终局条件Tests`，只断言 `PassStreak` 的增减与清零、对局继续（计数器仍写进公开快照 / 存档 / 日志 / 终端，D4 保留，须有人守）<br>**删**：`整轮Pass`、`棋盘填满`、`棋盘填满优先于整轮Pass`、`只剩一名参赛玩家优先于棋盘填满`<br>**合并**：`唯一参赛者获胜` → `两类终局条件Tests.唯一参赛者获胜`、`轮数再多也不结束` → `两类终局条件Tests.轮数再多也不结束`<br>**待定 / 注**：链接口径的优先级"只剩一名 > 最后战场结算"目前无直接用例 → 质检补测建议 Q1 | -6 |
| `两类终局条件Tests.cs` | MF | elimination-endgame「两类终局条件」 | 6 / 1 | c×1 m×5 | 其余已在链接载体；迁入 2 条（势力悬殊不提前结束、Pass计数跨回合重置）<br>**删**：`旧模式终局原因不含最后战场结算` | -1（+2 迁入） |
| `主动弃赛Tests.cs` | MF | elimination-endgame「主动弃赛」：停止行动、保护期内允许、遗留棋子、快照、弃赛名次与存档往返 | 9 / 9 | b×9 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；`弃赛名次不影响最终名次` 终局改为"三人整轮 Pass → 战场结算 → 最后战场结算"，D 仍为第 4（链接口径弃赛组本就最后） | 0 |
| `出局判定Tests.cs` | MF | elimination-endgame「出局判定」：零势力不出局、归零即出局、手牌有子也出局、构筑期不豁免、同时归零 | 6 / 6 | b×6 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `终局名次与并列判定Tests.cs` | MF | elimination-endgame「终局名次与并列判定」：比较链各级、弃赛 / 出局组、旧流程组序 | 11 / 11 | a×8 c×2 b×1 | 纯函数 8 条改调唯一名次实现（原 `ComputeLink` 更名 `Compute`，D6）；`终局输入取独占空格数` → 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）<br>**删**：`整轮Pass时按同一规则排名`、`旧模式流程中弃赛者排在势力归零出局者之前`<br>**待定 / 注**：`出局者倒序` 的输入同时含弃赛者，组间次序在链接口径下相反（完赛 > 出局 > 弃赛）：缺省去掉输入里的弃赛者、只断言出局者倒序（组间次序由 `链接模式终局名次Tests` 守）——期望数组因此改写，记入 R8 表 | -2 |
| `链接模式终局名次Tests.cs` | RN | elimination-endgame 链接名次 | 8 / — | m×8 | `ComputeLink` 更名 `Compute` 的机械修改 | 0 |

### HandInfoPanel

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `全玩家手牌对比面板Tests.cs` | IND | hand-info-panel 对比面板 | 1 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `公开结构参数与信物来源Tests.cs` | MF | hand-info-panel「公开结构参数与信物来源」 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `弃赛玩家的面板表现Tests.cs` | IND | hand-info-panel 弃赛玩家面板 | 1 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `征募面板可收起Tests.cs` | MF | hand-info-panel「征募面板可收起」：新征募阶段默认展开、阶段标识只认本机 | 12 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `敌方区域的显示内容Tests.cs` | IND | hand-info-panel 敌方区域 | 2 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `新棋子类型与驿站来源的显示Tests.cs` | MF | hand-info-panel 新棋子 / 驿站来源 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `自己区域的显示内容Tests.cs` | MF | hand-info-panel「自己区域的显示内容」 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### InformationVisibility

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `他战场玩家数值不进HUD视图模型Tests.cs` | MF | information-visibility 他战场遮蔽（HUD） | 5 / — | m×1 | 已在链接载体<br>**删反面段**：`旧模式与同战场不遮蔽`<br>**待定 / 注**：删旧模式半段、保留同战场半段；方法改名 `同战场不遮蔽`（记入改名表） | 0 |
| `始终公开的信息Tests.cs` | MF | information-visibility「始终公开的信息」 | 6 / 6 | b×6 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `必须隐藏的信息Tests.cs` | MF | information-visibility「必须隐藏的信息」 | 4 / 4 | b×4 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `按座位视图Tests.cs` | MF | information-visibility 按座位视图 | 11 / 1 | c×1 | —<br>**删**：`旧模式全图展开` | -1 |
| `视图模型按座位过滤Tests.cs` | MF | information-visibility 表现层镜像 | 10 / 1 | c×1 m×1 | `棋盘内有岩石时本座位棋盘仍可见` 已是手写链接档图 + `LinkModeFixtures.Options`（只去 `Mode`）<br>**删**：`旧模式全图可见无占位_与全知棋盘逐项相同` | -1 |

### InitiativeOrder

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `先手值公式Tests.cs` | MF | initiative-order「先手值公式」：四人局基础值、参赛人数变化 | 3 / 2 | b×2 e×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；`先锋推动低名次玩家前移` 是纯公式，只引常量 | 0 |
| `先手顺序预测_链接模式Tests.cs` | MF | initiative-order 链接预测 | 8 / 1 | c×1 | —<br>**删**：`旧模式预测不变` | -1 |
| `同值判定链Tests.cs` | MF | initiative-order「同值判定链」：首回合种子兜底 | 4 / 1 | b×1 | `LinkModeFixtures.Started([0,1,2,3])`（种子兜底与模式无关） | 0 |
| `基础排序Tests.cs` | MF | initiative-order「基础排序」 | 2 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `排名的作用范围Tests.cs` | MF | initiative-order「排名的作用范围」：排名不累计、落后不获补偿 | 2 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### LifeShape

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `活形三态Tests.cs` | MF | life-shape：活形不入存档 | 16 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）（存档往返） | 0 |
| `禁入格Tests.cs` | MF | life-shape「禁入格」：他人眼空间禁入、弃赛者活形仍禁入、解除同步 | 4 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### LinkMapGeneration

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `物化状态校验Tests.cs` | MF | map-definition 规格档：非链接档不可增长 | 10 / 1 | d×1 | 9×9 地图数据迁入 `TestMaps` | 0 |
| `链接时的棋盘拼接Tests.cs` | MF | link-map-generation 拼接 | 7 / — | e×7 | 只引常量（脚本把注释里的"全部"计成标记，非 Legacy） | 0 |

### MapDefinition

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `两位数行号贯通Tests.cs` | MF | frontier-map 1.6：两位数行号在存档 / 终端 / 日志贯通（25×32 合成图、2 人） | 3 / 2 | b×2 | 手写链接档高画布图（出生棋盘放到第 25–32 行，4 人，`CreateUnvalidated`）；断言里的坐标随图重写，"行号 ≥ 10 贯通"含义不变<br>**待定 / 注**：终端渲染与日志文本断言须按新图重导 → R8 | 0 |
| `出生区归属Tests.cs` | MF | map-definition「出生区归属与共享」：出生区标记不自带限制 | 4 / 1 | d×1 | 9×9 地图数据（GameBoard 级） | 0 |
| `棋盘清单Tests.cs` | MF | map-definition「棋盘清单」：空清单不写出、清单不参与结算、视图透传 | 9（Theory 1，3 行） / 2 | d×1 a×1 m×2 | `空清单不写出` → d；`清单不参与结算` → a（在 9×9 GameBoard 上以 `BatchRehearsal` / 结算层逐步比对有无清单）；其余已在链接载体<br>**删反面段**：`默认棋盘视图模型透传棋盘清单`<br>**待定 / 注**：删"旧夹具清单为空"反面段（对局不再能建在无清单图上）；`清单不参与结算` 没有 (b) 兜底（链接局必须有清单），见 P7 | 0 |

### MapSelection

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `选图视图模型Tests.cs` | MF | map-selection 视图模型 | 11（Theory 1，12 行） / — | m×3 | `CreateUnvalidated` 的模式三元式改为恒链接<br>**删反面段**：`预览说明读默认棋盘视图模型`<br>**待定 / 注**：9×9 样本行（"9×9，4 个出生区，可落子 81 格"）需在 9×9 上建局，缺省删该样本行（选图只到达链接图） | 0 |

### MatchFlowRegression

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `对局持久化Tests.cs` | MF | implement 1.1 / 8.2：小回合边界存档恢复、弃赛快照、终局结果、地图不符、匠人权重、旧字段被拒 | 9（Theory 1，5 行） / 9 | b×9 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） + `RestoreUnvalidated` 换成 `MatchFlow.Restore`（FlatMap 过校验） | 0 |
| `流程回归Tests.cs` | MF | implement 8.3：出局 / 弃赛改人数后的整轮 Pass 重比、先锋即时生效、替换决策来源 | 5 / 4 | c×2 b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）<br>**删**：`出局改变参赛人数后Pass计数与新人数重比`、`弃赛改变参赛人数后立即重比Pass计数`<br>**待定 / 注**：链接口径"出局 / 弃赛后按战场剩余在局者判整轮 Pass"若无用例 → 质检补测建议 Q2 | -2 |
| `百局端到端Tests.cs` | MM | 百局端到端 | 1 / — | m×1 | 去 `Mode` 写法 | 0 |
| `输出契约Tests.cs` | MF | implement 8.1 输出契约 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### MatchSetup

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `决赛倒计时配置Tests.cs` | MF | match-setup「决赛倒计时配置」 | 4 / — | m×4 | 按 R7 缺省（窄解）不改断言 | 0 |
| `匿名同时插旗Tests.cs` | MF | match-setup 旧插旗：旗数可见、锁定前可更换、多人同区（看得见旗数） | 4 / 4 | c×3 b×1 | 整文件删除；`时限可配置` 迁入 `盲选插旗Tests`（`LinkModeFixtures.Create(options)`）<br>**删**：`身份匿名`、`锁定前可更换`、`多人同区`<br>**待定 / 注**：三条场景已由 `盲选插旗Tests` 的 旗帜互不可见 / 选中即锁定 / 多人同区 在链接口径覆盖 | -3 |
| `原型插旗替代路径Tests.cs` | MF | match-setup「原型插旗替代路径」：依次插旗 = 同时插旗、锁定结果可持久化 | 3 / 2 | b×2 | `LinkModeFixtures.Create` + `MatchFlow.Restore` | 0 |
| `对局内容集Tests.cs` | MM | match-setup 内容集 | 6 / — | m×6 | 去 `Mode` 写法 | 0 |
| `对局模式Tests.cs` | MF | board-link-mode B1：模式缺省 / 公开建局恒链接 / 链接图须链接模式 / 恰 4 人 | 5 / 5 | c×3 b×2 | 改名 `建局约束Tests`：保留"非链接图建局报错""建局须 4 人"两条<br>**删**：`缺省为旧模式`、`公开建局恒为链接模式`、`链接图以链接模式建局` | -3 |
| `对局配置公开完整地图标识Tests.cs` | MM | match-setup 地图标识 | 6（Theory 2，4 行） / — | m×6 | 去 `Mode` 写法 | 0 |
| `带入带出配置Tests.cs` | MF | carry-in-out × match-setup「带入带出配置」：旧存档按无带入、往返、不可改、关闭有带入即拒 | 8 / 5 | b×5 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；旧存档样本改为"删掉带入字段的链接存档" | 0 |
| `开局信息公开范围Tests.cs` | MF | match-setup「开局信息公开范围」 | 6 / — | m×5 | 已是手写链接档图 + `LinkModeFixtures.Options`，只去 `Mode` | 0 |
| `盲选插旗Tests.cs` | MF | match-setup 盲选 | 7 / — | m×2 | 已在链接载体；迁入 1 条（时限可配置）<br>**删反面段**：`旗帜互不可见`、`选中即锁定`<br>**待定 / 注**：删"反面：旧模式公开视图给旗数 / 旧模式锁定前可更换"两段 | 0（+1 迁入） |
| `链接模式存档Tests.cs` | MF | match-setup / battlefield 存档：旧模式存档、旧存档被拒、旧模式终局存档 | 12 / 3 | c×2 b×1 | `旧存档被拒绝` 改为把链接存档的地图标识改成非 `link:` 后交公开 `Restore`<br>**删**：`旧模式存档不写新字段且按旧模式读入`、`旧模式终局存档不写出局原因` | -2 |
| `首回合顺序随机Tests.cs` | MF | match-setup「首回合顺序随机」 | 2 / 2 | b×2 | `LinkModeFixtures.Started([0,1,2,3])` | 0 |

### MatchTelemetry

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `AI改造与主题诊断Tests.cs` | MF | match-telemetry「AI 改造与主题诊断」：立栅 / 搭桥 / 烧林目标、预筛排除 | 8 / 4 | b×4 | 手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）（深水 / 林地放棋盘 0 中间带） | 0 |
| `对局日志的记录内容Tests.cs` | MF | match-telemetry「对局日志的记录内容」：八类记录、领地分、大数、非法批次、新来源、驿站 / 工坊、带出 | 11 / 7 | b×7 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；日志首部从此恒带链接字段，断言只比所测字段 | 0 |
| `对局日志的记录内容_链接模式Tests.cs` | MF | match-telemetry 链接日志 | 8 / 1 | c×1 | —<br>**删**：`旧模式日志不写链接模式字段` | -1 |
| `活形记录与统计Tests.cs` | MF | life-shape × match-telemetry「活形记录与统计」 | 8 / 6 | b×6 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `专家前瞻的记录Tests.cs` | EXT | 读 v5 时代 Legacy 日志夹具（结果原因 "AllPassed"） | 10 / — | — | 不迁：只经 `MatchLog.Parse` 读、不回放（已核）；读端按字符串读原因，不受枚举成员删除影响（R9）。测试里经 `Replayer.Replay` 回放的日志全部产自 `link:1`，`Replayer` 对非链接日志的 `null` 分支早已走不到（旧地图标识被 `MapCatalog.Resolve` 拒绝） | 0 |

### Recruitment

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `初始配置与基础棋池Tests.cs` | MF | recruitment：匠人权重可配置 | 8 / 1 | b×1 | `LinkModeFixtures.Create(options)` | 0 |
| `私人征募面板Tests.cs` | MF | recruitment「私人征募面板」：最后一名没有补偿、驿站生效 | 7 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### RelicEffects

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `同类信物叠加且无统一硬上限Tests.cs` | MF | relic-effects：来源可拆分 | 4 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `工坊扩大匠人的格改造范围Tests.cs` | MF | relic-effects：控制工坊即标记生效 | 3 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `效果快照在小回合开始时生成Tests.cs` | MF | relic-effects「效果快照」：分阶段基础值读一次、名次不影响快照 | 10 / 2 | b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `计分信物连营与犄角Tests.cs` | MF | relic-effects：失去控制立即失效 | 8 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `驿站的展示数加成Tests.cs` | MF | relic-effects：驿站与探勘相加 | 6 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### RelicGeneration

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `区域强度预算Tests.cs` | MF | relic-generation「区域强度预算」：兜底、同区降概率、高风险区预算 | 6（Theory 1，2 行） / 3 | d×3 | 9×9 地图数据（信物生成层，不建局） | 0 |

### SettlementShow

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `演出期间对局不推进Tests.cs` | MF | settlement-show：演出不影响对局结果 | 1 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `结算节拍序列的生成Tests.cs` | MF | settlement-show：相同输入得到相同序列 | 7 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### SimulationHarness

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `人工接管Tests.cs` | MF | simulation-harness「人工接管」 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） + `MatchFlow.Restore` | 0 |
| `出生区编号显示Tests.cs` | MF | simulation-harness：六区图插旗快照、区号中性色、八区编号 | 4（Theory 1，3 行） / 1 | b×1 | 手写 6 块棋盘链接档图（`CreateUnvalidated`）<br>**待定 / 注**：插旗阶段终端快照按链接占位重导 → R8 | 0 |
| `原型调试AITests.cs` | MF | simulation-harness「原型调试 AI」 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `可复现回放Tests.cs` | MF | simulation-harness 回放 | 4 / — | e×4 | 只引常量 | 0 |
| `各入口按地图标识选图_链接模式Tests.cs` | MM | simulation-harness 选图 | 8（Theory 2，8 行） / — | m×2 | `Match.Mode` / `Header.Mode` 断言改为"首部 Mode 恒为 Link"（R10） | 0 |
| `旧生成器摘要被拒Tests.cs` | MM | simulation-harness 旧摘要被拒 | 2 / — | m×1 | 去 `Mode` 写法 | 0 |
| `终端对局Tests.cs` | MF | 终端：状态栏大数势力缩写不撑破一行 | 4 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `终端新棋子与新信物Tests.cs` | MF | 终端：新棋子 / 信物渲染快照、v1 图例、驿站来源、工坊目标 | 5 / 4 | b×4 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）<br>**待定 / 注**：渲染快照随链接视口（只画本战场外接矩形）重导 → R8 | 0 |
| `终端活形与禁入标示Tests.cs` | MF | life-shape 3.3 终端禁入 / 已活标示 | 6 / 5 | b×5 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）<br>**待定 / 注**：同上，文本盘面快照重导 → R8 | 0 |

### TacticalLayers

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `五种战术信息层Tests.cs` | IND | tactical-layers「五种战术信息层」 | 6 / 6 | b×6 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）（`SetPassStreak(0)` 写法保留：计数器仍在，D6） | 0 |
| `信息层的可用时机与无副作用Tests.cs` | MF | tactical-layers「信息层的可用时机与无副作用」 | 4 / 4 | b×4 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `势力层领地与高地Tests.cs` | MF | tactical-layers 势力层领地分与高地拆分 | 7（Theory 1，11 行） / 6 | b×6 | 手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）（荒漠 / 高地格） | 0 |
| `单子禁手的标示Tests.呈现.cs` | MF | tactical-layers「单子禁手的标示」呈现 | 11 / 4 | b×4 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `单子禁手的标示Tests.对照.cs` | MF | tactical-layers 禁手与完整预演一致（`link:1` 探针） | 5（Theory 1，3 行） / — | m×5 | 去 `Mode` 写法 | 0 |
| `单子禁手的标示Tests.查询.cs` | MF | tactical-layers 禁手查询 | 9 / 7 | b×7 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）（`Surrounded` 局面平移到棋盘 0） | 0 |
| `改造在默认棋盘与棋串读法里的呈现Tests.cs` | MF | tactical-layers 改造呈现 | 4 / 4 | b×4 | 手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）（栅栏 / 工坊目标） | 0 |
| `新地表的规则标示Tests.cs` | MF | terrain-surfaces × tactical-layers「新地表的规则标示」 | 8 / 8 | b×8 | 手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）（沼泽 / 岩台 / 浅滩） | 0 |
| `活形与禁入格的标示Tests.cs` | MF | tactical-layers「活形与禁入格的标示」 | 6 / 5 | b×5 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）；`禁入格区别于地形不可落子与范围外` 的"范围外"改为本战场之外的格 | 0 |
| `盘面层的读法切换Tests.cs` | MF | tactical-layers 读法切换与差集 | 9 / 3 | b×3 | 手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）（地形差集） | 0 |
| `盘面层视图模型带地形Tests.cs` | MF | terrain-model 5.1：视图模型带出高度 / 地表 / 桥 / 栅栏（13×13 地形夹具） | 2 / 2 | b×2 | 手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）：把 13×13 夹具的地形逐格搬进棋盘 0（坐标平移）<br>**待定 / 注**：字段值不变、坐标随平移重写 → R8 | 0 |
| `默认棋盘上的未发现信物Tests.cs` | IND | tactical-layers 未发现信物（经 `StartedWithZonedRelics`） | 2 / 2 | b×2 | `PresentationFixtures.StartedWithZonedRelics` 改为 FlatMap 上按分区写信物 | 0 |

### TerrainEditing

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `同形与存档纳入设施Tests.cs` | MF | terrain-edit：对局存档往返保留改造与匠人权重 | 5 / 1 | b×1 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） + `MatchFlow.Restore`（`盘面序列化往返保留改造` 是 GameBoard 级，不涉及） | 0 |
| `地形派生数据不缓存Tests.cs` | MF | artisan-terrain-edit 2.6：合法范围含新架的桥、Map 随改造更新、表现层地形视图 | 7 / 3 | b×3 | 手写链接档图（`LinkMapFixtures.Build` + `CreateUnvalidated`，棋盘 0 带所需地形）（棋盘 0 中间带深水）；`合法落子范围在保护期后含本局新架的桥` 改名"合法落子范围含本局新架的桥"（构筑期同样成立） | 0 |
| `改造不可逆与公开Tests.cs` | MF | terrain-edit「改造不可逆」「改造公开」 | 4 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |

### TurnSequence

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `合法落子范围的对外契约Tests.cs` | MF | turn-sequence「合法落子范围的对外契约」 | 4 / 4 | c×1 j×1 b×2 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0）<br>**删**：`范围随大回合切换`<br>**合并**：`禁入格不在范围内` → `战场内合法落子范围Tests.禁入格不在范围内`<br>**待定 / 注**：`范围随大回合切换` 断言"保护期 = 出生区、第 4 大回合 = 全图"，链接口径由 `战场内合法落子范围Tests.范围随链接扩大` 覆盖 | -2 |
| `大回合的定义与推进Tests.cs` | MF | turn-sequence「大回合的定义与推进」 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `小回合的五个阶段Tests.cs` | MF | turn-sequence「小回合的五个阶段」 | 3 / 3 | b×3 | 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） | 0 |
| `战场内合法落子范围Tests.cs` | MF | turn-sequence 链接范围 | 9 / 1 | c×1 | 迁入 1 条（构筑期内规则照常）<br>**删**：`旧模式第4大回合仍全图解禁` | -1（+1 迁入） |
| `构筑保护期的落子限制Tests.cs` | MF | turn-sequence 旧「构筑保护期的落子限制」（出生区 3×3 限制） | 2 / 2 | j×1 b×1 | 整文件删除；`保护期内规则照常` 迁入 `战场内合法落子范围Tests` 改名"构筑期内规则照常"（两人共享棋盘 0，第 3 大回合围杀）<br>**合并**：`保护期内越区落子被拒` → `战场内合法落子范围Tests.构筑期内越区落子被拒`<br>**待定 / 注**：turn-sequence「构筑保护期的落子限制」正文"此时战场就是其锁定的出生棋盘，因此等价于出生区内"在 link-map-terrain（出生区缩为核心区）之后已不准确，不属本 change 的 Legacy 条款、不做增量；建议后续 change 顺手改为"等价于出生棋盘内"，质检对照时知悉 | -1 |
| `第4大回合的全图解禁Tests.cs` | MF | turn-sequence 旧「第 4 大回合的全图解禁」 | 2 / 2 | c×1 j×1 | 整文件删除<br>**删**：`解禁后全图可落子`<br>**合并**：`空盘面玩家重新进入` → `战场内合法落子范围Tests.空盘面玩家重新进入` | -2 |

### VisualStyleBaseline

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `棋盘台面的可视表现Tests.cs` | MF | visual-style-baseline：棋盘清单为空时一律是场景装饰 | 3 / 1 | a×1 | 由 9×9 GameBoard 直接组装公开世界后取 `DefaultBoardView`（不经对局）<br>**待定 / 注**：若 `DefaultBoardView` 无法脱离对局组装 → 删除（c），规格「空清单不画台面」改由视图模型单测守；实施时先试 a | 0 |

### 夹具

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `MatchFixtures.cs` | FIX | 9×9 手写流程夹具本体 | — | — | 删除：`Map` / `Relics` / `Seed` / `P0..P3` / `All` 迁入 `TestMaps`（规则层与地图数据层继续用 9×9 数据）；`AtRound` / `Stones` / `Run…` 扩展迁入 `LinkModeFixtures`；`TwoPlayer` 无调用者，直接删 | 0 |
| `AiFixtures.cs` | FIX | AI 夹具：`Round5` | — | — | `Round5` 改为 同区四人（`StartedFlat([0,0,0,0])`，FlatMap，K=0） + `AtRound(5)`；常量改引新位置 | 0 |
| `PresentationFixtures.cs` | FIX | 表现层夹具：`StartedWithZonedRelics` | — | — | 改为 FlatMap 上按分区写信物的链接局 | 0 |
| `LinkModeFixtures.cs` | FIX | 链接夹具 | — | — | `Options` 去 `Mode`；接收 `AtRound` / `Stones`；常量改引 `TestMaps` | 0 |
| `LookaheadFixtures.cs` | FIX | 前瞻夹具 | — | — | 常量改引；`LinkConfig` 不变 | 0 |

## 2. 汇总

| 类 | 涉及文件数 | 方法数 | 说明 |
|---|---|---|---|
| (a) 规则层 | 8 | 21 | 期望值不变 |
| (b) 链接夹具 | 96 | 317 | 断言含义不变；字面坐标 / 文本快照 / AI 数值的重导按 R8 记表 |
| (c) 删除 | 18 | 28 | 逐名列于下节 |
| (j) 合并 | 4 | 5 | 目标条已存在，覆盖不丢 |
| (d) 地图数据 | 5 | 7 | 零断言变化 |
| (e) 常量 | 4 | 13 | 零断言变化 |
| (m) 机械 | 19 | 69 | 零断言变化（删反面段者除外） |

- 计入汇总的测试文件 **134** 个（另 6 个夹具文件、1 个只读旧日志的文件单列）；按"主分类"（方法数最多的一类）计：(a) 规则层 6、(b) 链接夹具 89、(c) 删除 11、(j) 合并 2、(d) 地图数据 4、(e) 常量 3、(m) 机械 19。
- R1 三类：**(a) 21 条 / 8 个文件**；**(b) 317 条 / 96 个文件**；**(c) 28 条 / 18 个文件**（另 (j) 合并 5 条）。
- 方法总数净减少 **33** 条（(c) 28 + (j) 5）；整文件删除 4 个：`三类终局条件Tests`、`匿名同时插旗Tests`、`构筑保护期的落子限制Tests`、`第4大回合的全图解禁Tests`（其中 4 条迁出到链接测试文件）；`对局模式Tests` 改名 `建局约束Tests`。
- 另有 L18 两条校验器测试随规则删除：`MapDefinition/地图静态校验规则Tests` 的 `中央入口与咽喉必须位于可落子格`、`放宽容差必须写明理由`（(c) 计入 L18，不在上表）；`尺寸非正与越界障碍被报出` 删 `TOLERANCE_NEGATIVE` 一段（反面段）。

## 3. (c) 删除与 (j) 合并逐名表（质检逐名比对用）

| # | 文件 | 方法 | 类 | 理由 / 合并目标 |
|---|---|---|---|---|
| 1 | `AiDecision/链接模式下的决胜一手Tests.cs` | `旧模式不变` | c | 只测旧规则（ai-decision「链接模式下的决胜一手」） |
| 2 | `AiDecision/链接模式下落后者的停手阈值Tests.cs` | `旧模式不变` | c | 只测旧规则（ai-decision「链接模式下落后者的停手阈值」） |
| 3 | `Battlefield/链接后的公开补充载荷Tests.cs` | `旧模式副本与按生成记录恢复逐项相同` | c | 只测旧规则（RelicLedger.Copy 在拼接后照常） |
| 4 | `Battlefield/阶段推进Tests.cs` | `旧模式没有阶段转换` | c | 只测旧规则（battlefield「阶段推进」） |
| 5 | `EliminationEndgame/三类终局条件Tests.cs` | `整轮Pass` | c | 只测旧规则（elimination-endgame 旧「三类终局条件」：整轮 Pass / 填满即终局、全局连续 Pass 计数、旧优先级链） |
| 6 | `EliminationEndgame/三类终局条件Tests.cs` | `棋盘填满` | c | 只测旧规则（elimination-endgame 旧「三类终局条件」：整轮 Pass / 填满即终局、全局连续 Pass 计数、旧优先级链） |
| 7 | `EliminationEndgame/三类终局条件Tests.cs` | `棋盘填满优先于整轮Pass` | c | 只测旧规则（elimination-endgame 旧「三类终局条件」：整轮 Pass / 填满即终局、全局连续 Pass 计数、旧优先级链） |
| 8 | `EliminationEndgame/三类终局条件Tests.cs` | `只剩一名参赛玩家优先于棋盘填满` | c | 只测旧规则（elimination-endgame 旧「三类终局条件」：整轮 Pass / 填满即终局、全局连续 Pass 计数、旧优先级链） |
| 9 | `EliminationEndgame/三类终局条件Tests.cs` | `唯一参赛者获胜` | j | 合并入 `两类终局条件Tests.唯一参赛者获胜` |
| 10 | `EliminationEndgame/三类终局条件Tests.cs` | `轮数再多也不结束` | j | 合并入 `两类终局条件Tests.轮数再多也不结束` |
| 11 | `EliminationEndgame/两类终局条件Tests.cs` | `旧模式终局原因不含最后战场结算` | c | 只测旧规则（elimination-endgame「两类终局条件」） |
| 12 | `EliminationEndgame/终局名次与并列判定Tests.cs` | `整轮Pass时按同一规则排名` | c | 只测旧规则（elimination-endgame「终局名次与并列判定」：比较链各级、弃赛 / 出局组、旧流程组序） |
| 13 | `EliminationEndgame/终局名次与并列判定Tests.cs` | `旧模式流程中弃赛者排在势力归零出局者之前` | c | 只测旧规则（elimination-endgame「终局名次与并列判定」：比较链各级、弃赛 / 出局组、旧流程组序） |
| 14 | `InformationVisibility/按座位视图Tests.cs` | `旧模式全图展开` | c | 只测旧规则（information-visibility 按座位视图） |
| 15 | `InformationVisibility/视图模型按座位过滤Tests.cs` | `旧模式全图可见无占位_与全知棋盘逐项相同` | c | 只测旧规则（information-visibility 表现层镜像） |
| 16 | `InitiativeOrder/先手顺序预测_链接模式Tests.cs` | `旧模式预测不变` | c | 只测旧规则（initiative-order 链接预测） |
| 17 | `MatchFlowRegression/流程回归Tests.cs` | `出局改变参赛人数后Pass计数与新人数重比` | c | 只测旧规则（implement 8.3：出局 / 弃赛改人数后的整轮 Pass 重比、先锋即时生效、替换决策来源） |
| 18 | `MatchFlowRegression/流程回归Tests.cs` | `弃赛改变参赛人数后立即重比Pass计数` | c | 只测旧规则（implement 8.3：出局 / 弃赛改人数后的整轮 Pass 重比、先锋即时生效、替换决策来源） |
| 19 | `MatchSetup/匿名同时插旗Tests.cs` | `身份匿名` | c | 只测旧规则（match-setup 旧插旗：旗数可见、锁定前可更换、多人同区（看得见旗数）） |
| 20 | `MatchSetup/匿名同时插旗Tests.cs` | `锁定前可更换` | c | 只测旧规则（match-setup 旧插旗：旗数可见、锁定前可更换、多人同区（看得见旗数）） |
| 21 | `MatchSetup/匿名同时插旗Tests.cs` | `多人同区` | c | 只测旧规则（match-setup 旧插旗：旗数可见、锁定前可更换、多人同区（看得见旗数）） |
| 22 | `MatchSetup/对局模式Tests.cs` | `缺省为旧模式` | c | 只测旧规则（board-link-mode B1：模式缺省 / 公开建局恒链接 / 链接图须链接模式 / 恰 4 人） |
| 23 | `MatchSetup/对局模式Tests.cs` | `公开建局恒为链接模式` | c | 只测旧规则（board-link-mode B1：模式缺省 / 公开建局恒链接 / 链接图须链接模式 / 恰 4 人） |
| 24 | `MatchSetup/对局模式Tests.cs` | `链接图以链接模式建局` | c | 只测旧规则（board-link-mode B1：模式缺省 / 公开建局恒链接 / 链接图须链接模式 / 恰 4 人） |
| 25 | `MatchSetup/链接模式存档Tests.cs` | `旧模式存档不写新字段且按旧模式读入` | c | 只测旧规则（match-setup / battlefield 存档：旧模式存档、旧存档被拒、旧模式终局存档） |
| 26 | `MatchSetup/链接模式存档Tests.cs` | `旧模式终局存档不写出局原因` | c | 只测旧规则（match-setup / battlefield 存档：旧模式存档、旧存档被拒、旧模式终局存档） |
| 27 | `MatchTelemetry/对局日志的记录内容_链接模式Tests.cs` | `旧模式日志不写链接模式字段` | c | 只测旧规则（match-telemetry 链接日志） |
| 28 | `TurnSequence/合法落子范围的对外契约Tests.cs` | `范围随大回合切换` | c | 只测旧规则（turn-sequence「合法落子范围的对外契约」） |
| 29 | `TurnSequence/合法落子范围的对外契约Tests.cs` | `禁入格不在范围内` | j | 合并入 `战场内合法落子范围Tests.禁入格不在范围内` |
| 30 | `TurnSequence/战场内合法落子范围Tests.cs` | `旧模式第4大回合仍全图解禁` | c | 只测旧规则（turn-sequence 链接范围） |
| 31 | `TurnSequence/构筑保护期的落子限制Tests.cs` | `保护期内越区落子被拒` | j | 合并入 `战场内合法落子范围Tests.构筑期内越区落子被拒` |
| 32 | `TurnSequence/第4大回合的全图解禁Tests.cs` | `解禁后全图可落子` | c | 只测旧规则（turn-sequence 旧「第 4 大回合的全图解禁」） |
| 33 | `TurnSequence/第4大回合的全图解禁Tests.cs` | `空盘面玩家重新进入` | j | 合并入 `战场内合法落子范围Tests.空盘面玩家重新进入` |
| 34 | `MapDefinition/地图静态校验规则Tests.cs` | `中央入口与咽喉必须位于可落子格` | c（L18） | 规则删除（R3） |
| 35 | `MapDefinition/地图静态校验规则Tests.cs` | `放宽容差必须写明理由` | c（L18） | 规则删除（R3） |

合计 35 条（含 L18 两条）。

## 4. 改名与迁出表

| 原位置 | 新位置 / 新名 | 原因 |
|---|---|---|
| `三类终局条件Tests.势力悬殊不提前结束` | `两类终局条件Tests.势力悬殊不提前结束` | 规则在链接口径仍成立，原文件整删 |
| `三类终局条件Tests.Pass计数跨回合重置` | `两类终局条件Tests.Pass计数跨回合重置` | 只断言计数器增减与清零（计数器保留，D4） |
| `匿名同时插旗Tests.时限可配置` | `盲选插旗Tests.时限可配置` | 同上 |
| `构筑保护期的落子限制Tests.保护期内规则照常` | `战场内合法落子范围Tests.构筑期内规则照常` | 同上；对应 turn-sequence Scenario「构筑期内规则照常」 |
| `前瞻模拟的公平信息Tests.保护期内只在出生区回应` | `…构筑期内只在本战场回应` | 规格 Scenario 同步改名（specs/ai-decision） |
| `他战场玩家数值不进HUD视图模型Tests.旧模式与同战场不遮蔽` | `…同战场不遮蔽` | 删旧模式半段 |
| `地形派生数据不缓存Tests.合法落子范围在保护期后含本局新架的桥` | `…合法落子范围含本局新架的桥` | 链接口径构筑期同样成立 |
| `MatchSetup/对局模式Tests` | `MatchSetup/建局约束Tests` | 模式枚举删除 |

## 5. 待定项汇总（缺省建议）

| # | 事项 | 缺省 |
|---|---|---|
| P1 | `候选格上限Tests.小图不限制`：链接图可落子格恒 > 150 | 手写小链接档图（5 块 5×5，`CreateUnvalidated`）；规格 Scenario 措辞同步 |
| P2 | AI 测试在同区四人载体上会碰到两道链接门（落后者阈值 0、决胜一手） | 局面构造让被测 AI 居本战场势力首位；做不到的单列上报，不改期望 |
| P3 | `终局名次与并列判定Tests.出局者倒序` 输入含弃赛者，组间次序两口径相反 | 去掉输入里的弃赛者，只断言出局者倒序；期望数组改写记入 R8 表 |
| P4 | `棋盘台面的可视表现Tests.棋盘清单为空时一律是场景装饰` 需在无清单图上取视图 | 先试由 9×9 GameBoard 直接组装视图（a）；不行则删并上报 |
| P5 | `选图视图模型Tests.预览说明读默认棋盘视图模型` 的 9×9 样本行 | 删该样本行（选图只到达链接图） |
| P6 | 文本快照 / 渲染快照 / 坐标字面量（终端 3 个文件、两位数行号、出生区编号显示、盘面层视图模型带地形） | 按 R8 重导并记表 |
| P7 | `棋盘清单Tests.清单不参与结算`：链接局必须有棋盘清单，(a) 做不出时没有 (b) 可降 | 先做 (a)；不成立则只能删除并 MODIFIED `map-definition`「棋盘清单」Scenario「清单不参与结算」——二选一，届时报负责人 |
| Q1 | 链接口径"只剩一名 > 最后战场结算"优先级无直接用例 | 质检补测 1 条 |
| Q2 | 链接口径"出局 / 弃赛后按战场剩余在局者判整轮 Pass"若无用例 | 质检先查 `战场结算Tests`，缺则补 1 条 |

## 6. 期望重钉表（R8，实施时回填）

| 文件.方法 | 旧期望 | 新期望 | 原因（几何平移 / 全局量 / 视口） | 关系断言是否不变 |
|---|---|---|---|---|
| | | | | |
