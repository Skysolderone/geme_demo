# retire-legacy-rules 测试迁移清单

> 状态：规划稿，待负责人过目（R4 第一步）。实施第 1 组开始时用脚本按方法名复核本表并回填实际条数；本表分类为人工判读 + 脚本信号，标"待定"的按缺省执行、偏离即上报。

## 0. 口径

- **分类**（R1 三类之外另设四个记账类，避免硬塞）：
  - **(a) 规则层**：只测棋盘 / 纯规则的方法，改为直接在 `GameBoard` 与规则层入口上测（`BatchRehearsal.Rehearse`、`PowerCalculator.Compute`、`LifeShapeReport.Analyze`、`CoverageMap.Compute`、`RelicLedger.SnapshotFor`、`FinalStandings`、`InitiativeOrder`、`PublicRules`），沿用 9×9 地图数据与原坐标，**期望值不变**。
  - **(b) 链接夹具**：测对局流程、链接模式下同样成立的方法，改用链接模式载体，**断言含义不变**。缺省载体见 R6（G4 更正为第 1 组实际）：平坦 8×8 棋盘 0 的手写未校验链接档图 `link:90008` 上**四人同区** `LinkModeFixtures.PlainStarted()`（K = 0）——单一战场、无配对无链接、整轮 Pass 结算即终局，是旧 9×9"第 4 大回合起全图"阶段最近的等价物；需要新地表 / 特殊地形时在同图棋盘 0 上写地形（`PlainMap(terrain)`），特殊形状用临时 `link:900xx` 手写图（见第 1 节首注）。
  - **(c) 删除**：只测旧规则的方法（出生区保护期、第 4 大回合全图解禁、整轮 Pass / 填满即终局、全局连续 Pass 计数、Legacy 名次组序、看得见旗数 / 锁定前可改、Legacy 存档 / 日志字段、"旧模式不变 / 全图展开"对照）。
  - **(j) 合并**：迁过去后与既有链接测试逐条同义（同一 Scenario、同一载体），删本条、保留目标条；名字消失但覆盖不丢，质检与 (c) 一并逐名比对（R11）。
  - **(d) 地图数据**：只用 9×9 `MapData`（不建局）的方法，数据迁入 `TestMaps` 后零断言变化。
  - **(e) 常量**：只引 `P0..P3` / `All` / `Seed`，改引新位置。
  - **(m) 机械**：已在链接载体，只删 `Mode = MatchMode.Link` 写法、`ComputeLink` 更名等；带"删反面段"标注的方法保留、删掉其中的 Legacy 对照段。
- **条数**：1 个 `[Fact]` 或 1 个 `[Theory]` 记 1 条方法；Theory 的 `InlineData` 行数另列（"行"），迁移不改行数。Δ = 本文件方法数变化（(c) 与 (j) 减少；"迁入 / 迁出"在文件间搬动，不计入总变化）。
- **方法数取自脚本**（按 `[Fact]` / `[Theory]` 切方法体、匹配 `MatchFixtures.Started|Create|Map|Relics`、`Round5(`、`StartedWithZonedRelics`、`Create/RestoreUnvalidated`、`MatchMode.Legacy`、文件内包了这些的私有助手），个别文件 ±1，实施时复核。
- **范围**：派发口径"109 个使用 `MatchFixtures` 的文件"实测为 101 个调用建局 / 地图助手 + 8 个只引常量；另有 **16 个文件经 `AiFixtures.Round5` 或 `PresentationFixtures.StartedWithZonedRelics` 间接走 Legacy**（不在 109 内）、**8 个文件只因 `MatchMode` 写法或旧模式断言受影响**、**4 个带出测试经 `CarryFixtures.Result` 走 Legacy 名次算法**。`BoardTopology/盘面序列化Tests` 只用 `GameBoard.RestoreUnvalidated`（盘面恢复，与对局模式无关），排除。

## 1. 逐文件清单
> **G4 更正（2026-10-15，第 2 组）**：本节"迁移去向"栏原写的缺省载体 `StartedFlat([0,0,0,0])` / FlatMap（`link:90003`）与地形类的`LinkMapFixtures.Build` 为规划口径；第 1 组实际（逐文件扫描见 `g2/carriers.txt`）：
> - **主载体** `LinkModeFixtures.PlainStarted` / `PlainCreate` / `PlainMap`：手写、不经校验的平坦链接档图 `link:90008`——棋盘 0 为 8×8（左下角 (2, 2)），其余四块 6×6，四人同锁棋盘 0、K = 0；原 9×9 局面平移 (+2, +2) 等落在棋盘 0 上（FlatMap 的 6×6 棋盘容不下 9×9 局面）。地形类用例把 `TerrainData` 写在棋盘 0 上（`PlainMap(terrain)`）。`AiFixtures.Round5`、`PresentationFixtures.StartedWithZonedRelics` 同建在其上。
> - **临时标识**（R6 = A 的辅载体，均经 `CreateUnvalidated`）：`link:90005` `候选格上限Tests.小图不限制` 的 5 块 5×5 小图；`link:90006` `出生区编号显示Tests` 六块 5×5 出生棋盘；`link:90012` `终端对局Tests` / `对局日志的记录内容Tests` 的 12×12 整图一块出生棋盘、四人同锁；`link:90013` `盘面层视图模型带地形Tests` 的 13×13 地形夹具；`link:90032` `两位数行号贯通Tests` 的竖长图；`link:90046` `棋盘清单Tests.默认棋盘视图模型透传棋盘清单` 的 `LinkMapFixtures.Initial()` 换标识。
> - FlatMap（`link:90003`）/ `StartedFlat` 是 link-map-terrain 的既有夹具，迁移测试未用它，仍由既有链接测试使用（如 `链接模式下的决胜一手Tests`），不是死代码。
> - 本次改写：通用短语替换 73 处（同区四人缺省载体）+ 7 处（地形类），逐行特例 13 处；§0 (b) 定义同步改。§3 / §4 / §6 未动。


### AiDecision

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `AI决策的可复现性Tests.cs` | MF | ai-decision「AI 决策的可复现性」：同局面同决策、并列确定性打破 | 2 / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） + `AtRound(5)` | 0 |
| `AI对地表的感知Tests.cs` | MF | terrain-surfaces × ai-decision「AI 对地表的感知」：荒漠 / 沼泽 / 岩台 / 浅滩进入评价 | 5 / 5 | b×5 | `LinkModeFixtures.PlainMap(terrain)` / `PlainStarted(terrain)`（`link:90008`，地形写在棋盘 0 上，经 `CreateUnvalidated`）（新地表只能放在未校验图上：校验器禁止出生棋盘内新地表）；E5 孤子改为棋盘 0 中间带的同形局面 | 0 |
| `AI枚举改造目标Tests.cs` | MF | ai-decision 改造目标枚举：能一手立栅提子时选该手 | 5 / 4 | b×4 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：助手 `Match()` / `Boxed()` 跨行调用 `MatchFixtures.Started`，另 3 条同迁** | 0 |
| `专家难度的一层前瞻Tests.cs` | MF | ai-decision「专家难度的一层前瞻」：前瞻改变选择、两项之和、旧日志回放、宽度 1 等价高难、缺省预设等价一层 | 16（Theory 2，6 行） / 9 | b×9 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） + `AtRound(5)`；被测 AI 须为本战场势力最高（避开落后者阈值 0 / 决胜一手两道链接门）<br>**待定 / 注**：数值类断言（前瞻后分数）在新几何上可能变化 → 按 R8 记入"期望重钉表"<br>**1.1 复核：9 条（含经 `前瞻中的停手口径Tests.DoomedPosition` 的 2 条）** | 0 |
| `候选格上限Tests.cs` | MF | ai-decision「候选格上限」：小图不限制、前 K 格预筛、匠人回退、K≥合法格等价、小 K 找妙手、三入口同一阈值 | 15（Theory 2，5 行） / 10 | a×1 b×8 d×1 | 阈值解析（`大图缺省取上限小图为零显式配置优先`）→ a：对 9×9 地图数据与 `link:1` 直接调阈值函数；其余 → 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；`小图不限制` → 手写小链接档图（`LinkMapFixtures.Build("link:90005", …)`，5 块 5×5，可落子 125 ≤ 150，`CreateUnvalidated`）<br>**待定 / 注**：`小图不限制`：链接图可落子格恒 > 150，"小图"只能是未校验的手写图（规格同步改措辞，见 specs/ai-decision）；脚本口径把 `剪枝参数的文本往返与旧文本兼容` 计入，实施时核对，可能实为不涉及<br>**1.1 复核：`批次配置记录写入实际生效的上限` 只读 9×9 地图数据 → d；`剪枝参数的文本往返与旧文本兼容` 不涉及** | 0 |
| `停手阈值Tests.cs` | IND | ai-decision「停手阈值」：零收益 / 低于 / 恰等于 / 高于阈值、无子豁免、同批次不中途切换 | 15 / 8 | b×8 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；被测 AI 须为本战场势力最高或独处（否则落后者阈值取 0，断言含义改变）<br>**待定 / 注**：若某局面无法让被测 AI 居首而不改断言 → 单列上报，不改期望<br>**1.1 复核：8 条** | 0 |
| `公开规则纯函数Tests.cs` | MF | expert-lookahead D7 纯函数等价：部署上限、出局判据、先锋修正、合法范围（新旧路径逐项相等） | 5（Theory 1，3 行） / 4 | b×4 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；`真实对局…`（Theory 3 行）改在 `link:1` 探针上跑<br>**删反面段**：`真实对局每个小回合新旧两条路径逐项相等`、`夹具局面新旧两条路径逐项相等`<br>**待定 / 注**：两条等价测试里"合法范围旧路径"（`OldLegalRange`，抄录的保护期 / 全图规则）只在 Legacy 成立 → 删该段，改为 批次上下文 = `LegalRangeFor` = `PublicRules.LegalRange` 三方相等；`RelicLedger.Restore` 作对照处改用 `Copy()`<br>**1.1 复核：`公开部署上限算例` 不建局、不涉及** | 0 |
| `前瞻中的下一名对手Tests.cs` | MF | ai-decision「前瞻中的下一名对手」：跳过弃赛 / 出局者、候选使对手出局、末位跨大回合、只剩自己退化 | 8 / 6 | b×6 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；跨战场名册用例用 `LinkModeFixtures.Started([0, 0, 3, 4])` / `Started([0, 1, 3, 4])`（`link:1`）；`专家是本大回合末位` 断言改为"对手所在战场的可落子格"、`只剩自己时退化` 改 4 人局（规格同步 MODIFIED） | 0 |
| `前瞻中的停手口径Tests.cs` | IND | ai-decision「前瞻中的停手口径」 | 6 / 5 | b×5 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：`两层单点受活形硬约束` 经 `活形硬约束Tests.TwoEyes` 同迁** | 0 |
| `前瞻模拟的公平信息Tests.cs` | MF | ai-decision「前瞻模拟的公平信息」：对手手牌不同结果不变、模拟部署上限、保护期只在出生区回应、未揭示信物、投影一致 | 10 / 9 | b×9 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；`保护期内只在出生区回应` 改为"构筑期内只在其战场内回应"（对手独占另一块棋盘，规格同步 MODIFIED；方法改名记入改名表）<br>**1.1 复核：`守门覆盖前瞻代码` 为源码扫描、不涉及** | 0 |
| `前瞻的近似两层加分Tests.cs` | MF | ai-decision「前瞻的近似两层加分」：按下一大回合口径枚举、专家出局时加分为 0 | 9 / 7 | b×7 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；`按下一大回合的口径枚举` 断言改为"专家所在战场的可落子格"（规格同步 MODIFIED）<br>**1.1 复核：经 `Contact` 与 `前瞻中的下一名对手Tests.LastOpponentPosition` 的 5 条同迁** | 0 |
| `前瞻集的多样候选Tests.cs` | IND | ai-decision「前瞻集的多样候选」：补充候选同受停手阈值 | 7 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `启发式评价维度Tests.cs` | IND | ai-decision「启发式评价维度」：九维覆盖、权重、先手位、眼位 / 活形 / 威胁增量 | 14 / 9 | b×9 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）（眼位算例平移到棋盘 0 核心区外圈，局部形状不变）<br>**待定 / 注**：评分数值随全局量（空区、信物位置）可能变化 → R8 | 0 |
| `对隐藏信息的概率估计Tests.cs` | MF | ai-decision「对隐藏信息的概率估计」：估计不得使用真实值 | 2（Theory 1，16 行） / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `已确定活形的评价中性Tests.cs` | IND | ai-decision「已确定活形的评价中性」 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `新棋子与新信物的AI适配Tests.cs` | MF | ai-decision「新棋子与新信物的 AI 适配」 | 8 / 6 | b×6 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）（信物格按用例重设，同 `PresentationFixtures` 的分区信物写法）<br>**1.1 复核：6 条** | 0 |
| `正式对战AI的信息边界Tests.cs` | MF | ai-decision「正式对战 AI 的信息边界」：未知信物 / 敌方手牌数 / 未确认批次不可读 | 4 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `正式对战AI的信息边界_链接模式Tests.cs` | MF | ai-decision 信息边界（链接）：全知例外集中可关等 | 9（Theory 2，7 行） / 1 | m×1 | 已在链接载体<br>**删反面段**：`全知例外集中可关`<br>**待定 / 注**：删"旧模式：投影即全知视图"反面段<br>**1.1 复核：唯一受影响的就是删反面段的这条** | 0 |
| `活形分析的决策内缓存Tests.cs` | IND | ai-decision「活形分析的决策内缓存」 | 5 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：方法总数 5（含链接图种子 1–20 一条），经 `MatchFixtures.Started` 的 3 条** | 0 |
| `活形硬约束Tests.cs` | IND | ai-decision「活形硬约束」 | 6 / 6 | b×6 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `链接模式下的决胜一手Tests.cs` | MM | ai-decision「链接模式下的决胜一手」 | 6 / 1 | c×1 m×5 | 其余已在链接载体（去 `Mode = MatchMode.Link` 写法）<br>**删**：`旧模式不变` | -1 |
| `链接模式下落后者的停手阈值Tests.cs` | MM | ai-decision「链接模式下落后者的停手阈值」 | 5（Theory 2，4 行） / 1 | c×1 m×4 | 同上<br>**删**：`旧模式不变` | -1 |
| `难度分级Tests.cs` | MF | ai-decision「难度分级」：简单也会做活、高难 / 专家不越权 | 6 / 5 | b×5 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：经 `启发式评价维度Tests.CaptureRelicPosition` / `活形硬约束Tests.TwoEyes` 的 2 条同迁** | 0 |
| `高部署上限下的候选剪枝Tests.cs` | IND | ai-decision「高部署上限下的候选剪枝」 | 4（Theory 1，3 行） / 4 | b×4 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：经 `启发式评价维度Tests.CaptureRelicPosition` 的 1 条同迁** | 0 |

### BatchDeployment

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `批次数量与库存约束Tests.cs` | MF | batch-deployment：分阶段基础部署上限（含插旗阶段） | 9（Theory 2，9 行） / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）（`基础值随阶段提高` Theory 3 行照旧） | 0 |
| `非法批次必须给出可定位的原因Tests.cs` | MF | batch-deployment：活棋禁入返回落点与棋串 | 12 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

### BatchPreview

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `批次预演必须显示的信息Tests.cs` | IND | batch-preview：部署额度、Pass 前警示撤销新征募 | 6 / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `未知信物在预演中只显示将揭示Tests.cs` | IND | batch-preview：未知信物只显示将揭示 | 1 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `计分信物与新棋子的预演Tests.cs` | MF | batch-preview：已揭示 / 将揭示连营、新棋子位置加值 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）（连营信物放棋盘 0 公共区档格） | 0 |
| `非法批次必须说明原因并高亮Tests.cs` | MF | batch-preview：活棋禁入说明 | 7 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `预演一致性回归Tests.cs` | MF | implement 6.2 强制回归：千例随机局面预演 = 正式结算 | 1 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；随机局面生成器改在棋盘 0 内取格 | 0 |
| `预演不改变游戏状态Tests.cs` | IND | batch-preview：预演零副作用 | 1 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

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
| `弃赛结算Tests.cs` | CF | carry-in-out「弃赛结算」（含保护期内弃赛不带出） | 6 / 2 | a×2 | 同上；输入为完赛 + 弃赛，两种组序相同，期望不变<br>**1.1 复核：只有 `弃赛时第1名`、`保护期内弃赛不带出补给点` 经 `CarryFixtures.Result`；其余 4 条只测纯函数、不涉及** | 0 |
| `战场落败出局结算Tests.cs` | MF | carry-in-out 战场落败 | 1 / — | e×1 | 只引常量 P0..P3 | 0 |
| `换型候选类型Tests.cs` | MF | carry-in-out「换型候选类型」：建局拒绝非候选 / 不合形带入 | 5（Theory 1，3 行） / 2 | b×2 | `LinkModeFixtures.Create(options)`（建局校验与模式无关） | 0 |
| `补给种类与开局效果Tests.cs` | MF | carry-in-out「补给种类与开局效果」 | 5 / 5 | b×5 | `LinkModeFixtures.Create` + 依次插旗（开局手牌与模式无关） | 0 |
| `CarryFixtures.cs` | FIX | 带出夹具：`Result` | — | — | 改调唯一名次实现、原因 `LastBattlefieldSettled` | 0 |

### EliminationEndgame

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `三类终局条件Tests.cs` | MF | elimination-endgame 旧「三类终局条件」：整轮 Pass / 填满即终局、全局连续 Pass 计数、旧优先级链 | 8 / 8 | c×4 j×2 b×2 | 整文件删除；`势力悬殊不提前结束` 迁入 `两类终局条件Tests`（同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0），一方领先数百仍不终局）；`Pass计数跨回合重置` 迁入 `两类终局条件Tests`，只断言 `PassStreak` 的增减与清零、对局继续（计数器仍写进公开快照 / 存档 / 日志 / 终端，D4 保留，须有人守）<br>**删**：`整轮Pass`、`棋盘填满`、`棋盘填满优先于整轮Pass`、`只剩一名参赛玩家优先于棋盘填满`<br>**合并**：`唯一参赛者获胜` → `两类终局条件Tests.唯一参赛者获胜`、`轮数再多也不结束` → `两类终局条件Tests.轮数再多也不结束`<br>**待定 / 注**：链接口径的优先级"只剩一名 > 最后战场结算"目前无直接用例 → 质检补测建议 Q1 | -6 |
| `两类终局条件Tests.cs` | MF | elimination-endgame「两类终局条件」 | 6 / 1 | c×1 m×5 | 其余已在链接载体；迁入 2 条（势力悬殊不提前结束、Pass计数跨回合重置）<br>**删**：`旧模式终局原因不含最后战场结算` | -1（+2 迁入） |
| `主动弃赛Tests.cs` | MF | elimination-endgame「主动弃赛」：停止行动、保护期内允许、遗留棋子、快照、弃赛名次与存档往返 | 9 / 9 | b×9 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；`弃赛名次不影响最终名次` 终局改为"三人整轮 Pass → 战场结算 → 最后战场结算"，D 仍为第 4（链接口径弃赛组本就最后） | 0 |
| `出局判定Tests.cs` | MF | elimination-endgame「出局判定」：零势力不出局、归零即出局、手牌有子也出局、构筑期不豁免、同时归零 | 6 / 6 | b×6 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `终局名次与并列判定Tests.cs` | MF | elimination-endgame「终局名次与并列判定」：比较链各级、弃赛 / 出局组、旧流程组序 | 11 / 11 | a×8 c×2 b×1 | 纯函数 8 条改调唯一名次实现（原 `ComputeLink` 更名 `Compute`，D6）；`终局输入取独占空格数` → 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**删**：`整轮Pass时按同一规则排名`、`旧模式流程中弃赛者排在势力归零出局者之前`<br>**待定 / 注**：`出局者倒序` 的输入同时含弃赛者，组间次序在链接口径下相反（完赛 > 出局 > 弃赛）：缺省去掉输入里的弃赛者、只断言出局者倒序（组间次序由 `链接模式终局名次Tests` 守）——期望数组因此改写，记入 R8 表 | -2 |
| `链接模式终局名次Tests.cs` | RN | elimination-endgame 链接名次 | 8 / — | m×8 | `ComputeLink` 更名 `Compute` 的机械修改 | 0 |

### HandInfoPanel

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `全玩家手牌对比面板Tests.cs` | IND | hand-info-panel 对比面板 | 1 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `公开结构参数与信物来源Tests.cs` | MF | hand-info-panel「公开结构参数与信物来源」 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `弃赛玩家的面板表现Tests.cs` | IND | hand-info-panel 弃赛玩家面板 | 1 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `征募面板可收起Tests.cs` | MF | hand-info-panel「征募面板可收起」：新征募阶段默认展开、阶段标识只认本机 | 12 / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `敌方区域的显示内容Tests.cs` | IND | hand-info-panel 敌方区域 | 2 / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `新棋子类型与驿站来源的显示Tests.cs` | MF | hand-info-panel 新棋子 / 驿站来源 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `自己区域的显示内容Tests.cs` | MF | hand-info-panel「自己区域的显示内容」 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

### InformationVisibility

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `他战场玩家数值不进HUD视图模型Tests.cs` | MF | information-visibility 他战场遮蔽（HUD） | 5 / 1 | m×1 | 已在链接载体<br>**删反面段**：`旧模式与同战场不遮蔽`<br>**待定 / 注**：删旧模式半段、保留同战场半段；方法改名 `同战场不遮蔽`（记入改名表）<br>**1.1 复核：唯一受影响的就是删反面段的这条** | 0 |
| `始终公开的信息Tests.cs` | MF | information-visibility「始终公开的信息」 | 6 / 6 | b×6 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `必须隐藏的信息Tests.cs` | MF | information-visibility「必须隐藏的信息」 | 4 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：`数据层边界` 为反射闭包、不涉及** | 0 |
| `按座位视图Tests.cs` | MF | information-visibility 按座位视图 | 11 / 1 | c×1 | —<br>**删**：`旧模式全图展开` | -1 |
| `视图模型按座位过滤Tests.cs` | MF | information-visibility 表现层镜像 | 10 / 2 | c×1 m×1 | `棋盘内有岩石时本座位棋盘仍可见` 已是手写链接档图 + `LinkModeFixtures.Options`（只去 `Mode`）<br>**删**：`旧模式全图可见无占位_与全知棋盘逐项相同`<br>**1.1 复核：m 那条用 `MatchFixtures.Relics` 造手工信物记录（改引新位置）** | -1 |

### InitiativeOrder

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `先手值公式Tests.cs` | MF | initiative-order「先手值公式」：四人局基础值、参赛人数变化 | 3 / 2 | b×2 e×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；`先锋推动低名次玩家前移` 是纯公式，只引常量 | 0 |
| `先手顺序预测_链接模式Tests.cs` | MF | initiative-order 链接预测 | 8 / 1 | c×1 | —<br>**删**：`旧模式预测不变` | -1 |
| `同值判定链Tests.cs` | MF | initiative-order「同值判定链」：首回合种子兜底 | 4 / 1 | b×1 | `LinkModeFixtures.Started([0,1,2,3])`（种子兜底与模式无关） | 0 |
| `基础排序Tests.cs` | MF | initiative-order「基础排序」 | 2 / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `排名的作用范围Tests.cs` | MF | initiative-order「排名的作用范围」：排名不累计、落后不获补偿 | 2 / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

### LifeShape

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `活形三态Tests.cs` | MF | life-shape：活形不入存档 | 16 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）（存档往返） | 0 |
| `禁入格Tests.cs` | MF | life-shape「禁入格」：他人眼空间禁入、弃赛者活形仍禁入、解除同步 | 4 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：只有 `弃赛者的活形仍禁入` 建局；另 3 条为 GameBoard 级、不涉及** | 0 |

### LinkMapGeneration

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `物化状态校验Tests.cs` | MF | map-definition 规格档：非链接档不可增长 | 10 / 1 | d×1 | 9×9 地图数据迁入 `TestMaps` | 0 |
| `链接时的棋盘拼接Tests.cs` | MF | link-map-generation 拼接 | 7 / — | e×7 | 只引常量（脚本把注释里的"全部"计成标记，非 Legacy） | 0 |

### MapDefinition

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `两位数行号贯通Tests.cs` | MF | frontier-map 1.6：两位数行号在存档 / 终端 / 日志贯通（25×32 合成图、2 人） | 3 / 2 | b×2 | 手写链接档高画布图（原竖长图换标识 `link:90032`，出生棋盘放到第 25–32 行，4 人，`CreateUnvalidated`）；断言里的坐标随图重写，"行号 ≥ 10 贯通"含义不变<br>**待定 / 注**：终端渲染与日志文本断言须按新图重导 → R8 | 0 |
| `出生区归属Tests.cs` | MF | map-definition「出生区归属与共享」：出生区标记不自带限制 | 4 / 1 | d×1 | 9×9 地图数据（GameBoard 级） | 0 |
| `棋盘清单Tests.cs` | MF | map-definition「棋盘清单」：空清单不写出、清单不参与结算、视图透传 | 9（Theory 1，3 行） / 3 | d×1 a×1 m×1 | `空清单不写出` → d；`清单不参与结算` → a（在 9×9 GameBoard 上以 `BatchRehearsal` / 结算层逐步比对有无清单）；其余已在链接载体<br>**删反面段**：`默认棋盘视图模型透传棋盘清单`（助手 `Started` 改为链接 4 人局，`LinkMapFixtures.Initial()` 换标识 `link:90046`）<br>**待定 / 注**：删"旧夹具清单为空"反面段（对局不再能建在无清单图上）；`清单不参与结算` 没有 (b) 兜底（链接局必须有清单），见 P7<br>**1.1 复核：受影响 3 条 = d `空清单不写出` + a `清单不参与结算` + m（删反面段）`默认棋盘视图模型透传棋盘清单`** | 0 |

### MapSelection

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `选图视图模型Tests.cs` | MF | map-selection 视图模型 | 11（Theory 1，12 行） / 3 | m×3 | `CreateUnvalidated` 的模式三元式改为恒链接<br>**删反面段**：`预览说明读默认棋盘视图模型`<br>**待定 / 注**：9×9 样本行（"9×9，4 个出生区，可落子 81 格"）需在 9×9 上建局，缺省删该样本行（选图只到达链接图）<br>**1.1 复核：3 条（`ViewOf` 的模式三元式 2 条 + `MatchFixtures.Relics` 1 条）** | 0 |

### MatchFlowRegression

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `对局持久化Tests.cs` | MF | implement 1.1 / 8.2：小回合边界存档恢复、弃赛快照、终局结果、地图不符、匠人权重、旧字段被拒 | 9（Theory 1，5 行） / 9 | b×9 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） + 恢复仍走测试口 `RestoreUnvalidated` 的链接路径（`link:90008` 不经校验，不能换公开 `MatchFlow.Restore`） | 0 |
| `流程回归Tests.cs` | MF | implement 8.3：出局 / 弃赛改人数后的整轮 Pass 重比、先锋即时生效、替换决策来源 | 5 / 4 | c×2 b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**删**：`出局改变参赛人数后Pass计数与新人数重比`、`弃赛改变参赛人数后立即重比Pass计数`<br>**待定 / 注**：链接口径"出局 / 弃赛后按战场剩余在局者判整轮 Pass"若无用例 → 质检补测建议 Q2 | -2 |
| `百局端到端Tests.cs` | MM | 百局端到端 | 1 / — | m×1 | 去 `Mode` 写法 | 0 |
| `输出契约Tests.cs` | MF | implement 8.1 输出契约 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

### MatchSetup

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `决赛倒计时配置Tests.cs` | MF | match-setup「决赛倒计时配置」 | 4 / — | m×4 | 按 R7 缺省（窄解）不改断言 | 0 |
| `匿名同时插旗Tests.cs` | MF | match-setup 旧插旗：旗数可见、锁定前可更换、多人同区（看得见旗数） | 4 / 4 | c×3 b×1 | 整文件删除；`时限可配置` 迁入 `盲选插旗Tests`（`LinkModeFixtures.Create(options)`）<br>**删**：`身份匿名`、`锁定前可更换`、`多人同区`<br>**待定 / 注**：三条场景已由 `盲选插旗Tests` 的 旗帜互不可见 / 选中即锁定 / 多人同区 在链接口径覆盖 | -3 |
| `原型插旗替代路径Tests.cs` | MF | match-setup「原型插旗替代路径」：依次插旗 = 同时插旗、锁定结果可持久化 | 3 / 2 | b×2 | `LinkModeFixtures.Create` + `MatchFlow.Restore` | 0 |
| `对局内容集Tests.cs` | MM | match-setup 内容集 | 6 / — | m×6 | 去 `Mode` 写法 | 0 |
| `对局模式Tests.cs` | MF | board-link-mode B1：模式缺省 / 公开建局恒链接 / 链接图须链接模式 / 恰 4 人 | 5 / 4 | c×3 b×1 e×1 | 改名 `建局约束Tests`：保留"非链接图建局报错""建局须 4 人"两条<br>**删**：`缺省为旧模式`、`公开建局恒为链接模式`、`链接图以链接模式建局`<br>**1.1 复核：`链接模式只有四人局` 已在链接图、只引常量 → e** | -3 |
| `对局配置公开完整地图标识Tests.cs` | MM | match-setup 地图标识 | 6（Theory 2，4 行） / — | m×6 | 去 `Mode` 写法 | 0 |
| `带入带出配置Tests.cs` | MF | carry-in-out × match-setup「带入带出配置」：旧存档按无带入、往返、不可改、关闭有带入即拒 | 8 / 5 | b×5 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；旧存档样本改为"删掉带入字段的链接存档" | 0 |
| `开局信息公开范围Tests.cs` | MF | match-setup「开局信息公开范围」 | 6 / 4 | b×1 m×4 | 已是手写链接档图 + `LinkModeFixtures.Options`，只去 `Mode`<br>**1.1 复核：`插旗阶段的可见信息` 经 `MatchFixtures.Create` 在 9×9 上建局（原表漏计）→ b：`LinkModeFixtures.PlainCreate`（`link:90008`）+ 手工信物（文件中既有的 `link:90001` 为 link-map-terrain 夹具，非本 change 引入）；其余 3 条用 `MatchFixtures.Relics`、1 条只引常量 → m** | 0 |
| `盲选插旗Tests.cs` | MF | match-setup 盲选 | 7 / 2 | m×2 | 已在链接载体；迁入 1 条（时限可配置）<br>**删反面段**：`旗帜互不可见`、`选中即锁定`<br>**待定 / 注**：删"反面：旧模式公开视图给旗数 / 旧模式锁定前可更换"两段<br>**1.1 复核：受影响的就是删反面段的 2 条** | 0（+1 迁入） |
| `链接模式存档Tests.cs` | MF | match-setup / battlefield 存档：旧模式存档、旧存档被拒、旧模式终局存档 | 12 / 3 | c×2 b×1 | `旧存档被拒绝` 改为把链接存档的地图标识改成非 `link:` 后交公开 `Restore`<br>**删**：`旧模式存档不写新字段且按旧模式读入`、`旧模式终局存档不写出局原因` | -2 |
| `首回合顺序随机Tests.cs` | MF | match-setup「首回合顺序随机」 | 2 / 2 | b×2 | `LinkModeFixtures.Started([0,1,2,3])` | 0 |

### MatchTelemetry

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `AI改造与主题诊断Tests.cs` | MF | match-telemetry「AI 改造与主题诊断」：立栅 / 搭桥 / 烧林目标、预筛排除 | 8 / 4 | b×4 | `LinkModeFixtures.PlainMap(terrain)` / `PlainStarted(terrain)`（`link:90008`，地形写在棋盘 0 上，经 `CreateUnvalidated`）（深水 / 林地放棋盘 0 中间带） | 0 |
| `对局日志的记录内容Tests.cs` | MF | match-telemetry「对局日志的记录内容」：八类记录、领地分、大数、非法批次、新来源、驿站 / 工坊、带出 | 11 / 7 | b×7 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；`状态栏大数势力缩写` 同类用例用 12×12 整图一块出生棋盘 `link:90012`（同 `终端对局Tests`）；日志首部从此恒带链接字段，断言只比所测字段 | 0 |
| `对局日志的记录内容_链接模式Tests.cs` | MF | match-telemetry 链接日志 | 8 / 1 | c×1 | —<br>**删**：`旧模式日志不写链接模式字段` | -1 |
| `活形记录与统计Tests.cs` | MF | life-shape × match-telemetry「活形记录与统计」 | 8 / 5 | b×5 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：5 条** | 0 |
| `专家前瞻的记录Tests.cs` | EXT | 读 v5 时代 Legacy 日志夹具（结果原因 "AllPassed"） | 10 / — | — | 不迁：只经 `MatchLog.Parse` 读、不回放（已核）；读端按字符串读原因，不受枚举成员删除影响（R9）。测试里经 `Replayer.Replay` 回放的日志全部产自 `link:1`，`Replayer` 对非链接日志的 `null` 分支早已走不到（旧地图标识被 `MapCatalog.Resolve` 拒绝） | 0 |

### Recruitment

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `初始配置与基础棋池Tests.cs` | MF | recruitment：匠人权重可配置 | 8 / 1 | b×1 | `LinkModeFixtures.Create(options)` | 0 |
| `私人征募面板Tests.cs` | MF | recruitment「私人征募面板」：最后一名没有补偿、驿站生效 | 7 / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

### RelicEffects

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `同类信物叠加且无统一硬上限Tests.cs` | MF | relic-effects：来源可拆分 | 4 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `工坊扩大匠人的格改造范围Tests.cs` | MF | relic-effects：控制工坊即标记生效 | 3 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `效果快照在小回合开始时生成Tests.cs` | MF | relic-effects「效果快照」：分阶段基础值读一次、名次不影响快照 | 10 / 2 | b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `计分信物连营与犄角Tests.cs` | MF | relic-effects：失去控制立即失效 | 8 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `驿站的展示数加成Tests.cs` | MF | relic-effects：驿站与探勘相加 | 6 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

### RelicGeneration

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `区域强度预算Tests.cs` | MF | relic-generation「区域强度预算」：兜底、同区降概率、高风险区预算 | 6（Theory 1，2 行） / 2 | d×2 | 9×9 地图数据（信物生成层，不建局）<br>**1.1 复核：经 `HighTierMap` 的 2 条** | 0 |

### SettlementShow

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `演出期间对局不推进Tests.cs` | MF | settlement-show：演出不影响对局结果 | 1 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `结算节拍序列的生成Tests.cs` | MF | settlement-show：相同输入得到相同序列 | 7 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

### SimulationHarness

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `人工接管Tests.cs` | MF | simulation-harness「人工接管」 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） + `MatchFlow.Restore` | 0 |
| `出生区编号显示Tests.cs` | MF | simulation-harness：六区图插旗快照、区号中性色、八区编号 | 4（Theory 1，3 行） / 1 | b×1 | 手写 6 块棋盘链接档图（标识 `link:90006`，六个平台登记为六块 5×5 出生棋盘，`CreateUnvalidated`）<br>**待定 / 注**：插旗阶段终端快照按链接占位重导 → R8 | 0 |
| `原型调试AITests.cs` | MF | simulation-harness「原型调试 AI」 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `可复现回放Tests.cs` | MF | simulation-harness 回放 | 4 / — | e×4 | 只引常量 | 0 |
| `各入口按地图标识选图_链接模式Tests.cs` | MM | simulation-harness 选图 | 8（Theory 2，8 行） / — | m×2 | `Match.Mode` / `Header.Mode` 断言改为"首部 Mode 恒为 Link"（R10） | 0 |
| `旧生成器摘要被拒Tests.cs` | MM | simulation-harness 旧摘要被拒 | 2 / — | m×1 | 去 `Mode` 写法 | 0 |
| `终端对局Tests.cs` | MF | 终端：状态栏大数势力缩写不撑破一行 | 4 / 1 | b×1 | 12×12 全平地手写链接档图（标识 `link:90012`，整张图登记为一块 12×12 出生棋盘、四人同锁，经 `CreateUnvalidated`；不用 `PlainStarted`） | 0 |
| `终端新棋子与新信物Tests.cs` | MF | 终端：新棋子 / 信物渲染快照、v1 图例、驿站来源、工坊目标 | 5 / 4 | b×4 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**待定 / 注**：渲染快照随链接视口（只画本战场外接矩形）重导 → R8 | 0 |
| `终端活形与禁入标示Tests.cs` | MF | life-shape 3.3 终端禁入 / 已活标示 | 6 / 5 | b×5 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**待定 / 注**：同上，文本盘面快照重导 → R8 | 0 |

### TacticalLayers

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `五种战术信息层Tests.cs` | IND | tactical-layers「五种战术信息层」 | 6 / 6 | b×6 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）（`SetPassStreak(0)` 写法保留：计数器仍在，D6） | 0 |
| `信息层的可用时机与无副作用Tests.cs` | MF | tactical-layers「信息层的可用时机与无副作用」 | 4 / 4 | b×4 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `势力层领地与高地Tests.cs` | MF | tactical-layers 势力层领地分与高地拆分 | 7（Theory 1，11 行） / 6 | b×6 | `LinkModeFixtures.PlainMap(terrain)` / `PlainStarted(terrain)`（`link:90008`，地形写在棋盘 0 上，经 `CreateUnvalidated`）（荒漠 / 高地格） | 0 |
| `单子禁手的标示Tests.呈现.cs` | MF | tactical-layers「单子禁手的标示」呈现 | 11 / 10 | b×10 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**1.1 复核：经 `Surrounded`（同一 partial 类定义在 .查询.cs）的 6 条原脚本漏计** | 0 |
| `单子禁手的标示Tests.对照.cs` | MF | tactical-layers 禁手与完整预演一致（`link:1` 探针） | 7（Theory 2，4 行） / — | m×5 | 去 `Mode` 写法<br>**1.1 复核：总数 7（含 Slow / Perf 各 1）；`Mode` 写法在助手 `Compare` 里** | 0 |
| `单子禁手的标示Tests.查询.cs` | MF | tactical-layers 禁手查询 | 9 / 7 | b×7 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）（`Surrounded` 局面平移到棋盘 0） | 0 |
| `改造在默认棋盘与棋串读法里的呈现Tests.cs` | MF | tactical-layers 改造呈现 | 4 / 4 | b×4 | `LinkModeFixtures.PlainMap(terrain)` / `PlainStarted(terrain)`（`link:90008`，地形写在棋盘 0 上，经 `CreateUnvalidated`）（栅栏 / 工坊目标） | 0 |
| `新地表的规则标示Tests.cs` | MF | terrain-surfaces × tactical-layers「新地表的规则标示」 | 8 / 8 | b×8 | `LinkModeFixtures.PlainMap(terrain)` / `PlainStarted(terrain)`（`link:90008`，地形写在棋盘 0 上，经 `CreateUnvalidated`）（沼泽 / 岩台 / 浅滩） | 0 |
| `活形与禁入格的标示Tests.cs` | MF | tactical-layers「活形与禁入格的标示」 | 6 / 5 | b×5 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）；`禁入格区别于地形不可落子与范围外` 的"范围外"改为本战场之外的格 | 0 |
| `盘面层的读法切换Tests.cs` | MF | tactical-layers 读法切换与差集 | 9 / 3 | b×3 | `LinkModeFixtures.PlainMap(terrain)` / `PlainStarted(terrain)`（`link:90008`，地形写在棋盘 0 上，经 `CreateUnvalidated`）（地形差集） | 0 |
| `盘面层视图模型带地形Tests.cs` | MF | terrain-model 5.1：视图模型带出高度 / 地表 / 桥 / 栅栏（13×13 地形夹具） | 2 / 2 | b×2 | 13×13 手写地形夹具（`TestMaps.Synthetic(13)`）原样保留，只把标识改为 `link:90013`、以链接选项经 `CreateUnvalidated` 建局并只取插旗阶段的公开世界（不经 `LinkMapFixtures.Build`、坐标未平移，地形与断言不变）<br>**待定 / 注**：字段值不变、坐标随平移重写 → R8 | 0 |
| `默认棋盘上的未发现信物Tests.cs` | IND | tactical-layers 未发现信物（经 `StartedWithZonedRelics`） | 2 / 2 | b×2 | `PresentationFixtures.StartedWithZonedRelics` 改为 `LinkModeFixtures.PlainMap()`（`link:90008`）上按分区写信物 | 0 |

### TerrainEditing

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `同形与存档纳入设施Tests.cs` | MF | terrain-edit：对局存档往返保留改造与匠人权重 | 5 / 1 | b×1 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） + `MatchFlow.Restore`（`盘面序列化往返保留改造` 是 GameBoard 级，不涉及） | 0 |
| `地形派生数据不缓存Tests.cs` | MF | artisan-terrain-edit 2.6：合法范围含新架的桥、Map 随改造更新、表现层地形视图 | 7 / 3 | b×3 | `LinkModeFixtures.PlainMap(terrain)` / `PlainStarted(terrain)`（`link:90008`，地形写在棋盘 0 上，经 `CreateUnvalidated`）（棋盘 0 中间带深水）；`合法落子范围在保护期后含本局新架的桥` 改名"合法落子范围含本局新架的桥"（构筑期同样成立） | 0 |
| `改造不可逆与公开Tests.cs` | MF | terrain-edit「改造不可逆」「改造公开」 | 4 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |

### TurnSequence

| 文件 | 来源 | 测的规则 | 方法（总 / Legacy 载体） | 分类 | 迁移去向 / 删除理由 | Δ |
|---|---|---|---|---|---|---|
| `合法落子范围的对外契约Tests.cs` | MF | turn-sequence「合法落子范围的对外契约」 | 4 / 4 | c×1 j×1 b×2 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0）<br>**删**：`范围随大回合切换`<br>**合并**：`禁入格不在范围内` → `战场内合法落子范围Tests.禁入格不在范围内`<br>**待定 / 注**：`范围随大回合切换` 断言"保护期 = 出生区、第 4 大回合 = 全图"，链接口径由 `战场内合法落子范围Tests.范围随链接扩大` 覆盖 | -2 |
| `大回合的定义与推进Tests.cs` | MF | turn-sequence「大回合的定义与推进」 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
| `小回合的五个阶段Tests.cs` | MF | turn-sequence「小回合的五个阶段」 | 3 / 3 | b×3 | 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） | 0 |
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
| `MatchFixtures.cs` | FIX | 9×9 手写流程夹具本体 | — | — | 删除：`Map` / `Relics` / `Seed` / `P0..P3` / `All` 迁入 `TestMaps`（规则层与地图数据层继续用 9×9 数据）；`AtRound` / `Stones` / `Run…` 扩展迁入 `LinkModeFixtures`；`TwoPlayer` 无调用者，直接删<br>**第 2 组 2.2 已删**：`RandomTurnController` 原样迁出为独立文件 `RandomTurnController.cs`；`tests/` 内 `MatchFixtures` 引用归零（5 个文件的注释措辞改为"原 9×9 流程夹具"） | 0 |
| `AiFixtures.cs` | FIX | AI 夹具：`Round5` | — | — | `Round5` 改为 同区四人（`LinkModeFixtures.PlainStarted()`：手写未校验平坦 8×8 棋盘 0 链接图 `link:90008`，四人同锁棋盘 0，K=0） + `AtRound(5)`；常量改引新位置 | 0 |
| `PresentationFixtures.cs` | FIX | 表现层夹具：`StartedWithZonedRelics` | — | — | 改为 `LinkModeFixtures.PlainMap()`（`link:90008`）上按分区写信物的链接局（`CreateUnvalidated` + `RequireLink`） | 0 |
| `LinkModeFixtures.cs` | FIX | 链接夹具 | — | — | `Options` 去 `Mode`；接收 `AtRound` / `Stones`；常量改引 `TestMaps` | 0 |
| `LookaheadFixtures.cs` | FIX | 前瞻夹具 | — | — | 常量改引；`LinkConfig` 不变 | 0 |

## 2. 汇总

| 类 | 涉及文件数 | 方法数 | 说明 |
|---|---|---|---|
| (a) 规则层 | 8 | 18（原 21） | 期望值不变 |
| (b) 链接夹具 | 97（原 96） | 324（原 317） | 断言含义不变；字面坐标 / 文本快照 / AI 数值的重导按 R8 记表 |
| (c) 删除 | 18 | 28 | 逐名列于下节 |
| (j) 合并 | 4 | 5 | 目标条已存在，覆盖不丢 |
| (d) 地图数据 | 6（原 5） | 7 | 零断言变化 |
| (e) 常量 | 5（原 4） | 14（原 13） | 零断言变化 |
| (m) 机械 | 19 | 67（原 69） | 零断言变化（删反面段者除外） |

- **1.1 复核回填（2026-10-15，第 1 组实施）**：脚本 `g1/scan_legacy.py`（按方法切分、同文件与跨文件助手传递闭包；输出 `g1/scan-before.txt`）与本表逐文件比对（`g1/compare_list.py` → `g1/compare-before.txt`，复核后出入 0）。27 个文件的方法数 / 分类按脚本改正（各行末尾"1.1 复核"注），主因是原脚本只做单文件扫描、漏了跨文件助手（`AiFixtures.Round5`、`活形硬约束Tests.TwoEyes`、`启发式评价维度Tests.CaptureRelicPosition`、`单子禁手的标示Tests.Surrounded` 等）与跨行的 `MatchFixtures
.Started`，以及把不建局的方法计入。(a) 21 → 18（`弃赛结算Tests` 只有 2 条经 `CarryFixtures.Result`），(b) 317 → 324，(e) 13 → 14，(m) 69 → 67；**(c) 28 条与 (j) 5 条逐名不变**（第 3 节 35 条全部命中），第 4 节迁出源方法全部存在。proposal.md / tasks.md 里的 "317 / 21" 为规划口径，未改。
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
| `EliminationEndgame/主动弃赛Tests.弃赛后停止行动` | 信物 / 落点 E5；B2 / H2 / B8 | G7；D4 / K4 / D10 | 几何平移 (+2, +2) | 是（数值期望不变） |
| `EliminationEndgame/主动弃赛Tests.保护期内允许弃赛` | H8；B2 / H2 / B8 | J9；C3 / J3 / C9 | 几何平移 (+1, +1)：H8 须不贴边（四邻独占 4），+2 会落到棋盘 0 的角 | 是（势力 5 不变） |
| `EliminationEndgame/主动弃赛Tests.遗留棋子继续生效` | E5 / E6 / E7 | G7 / G8 / G9 | 几何平移 (+2, +2) | 是（独占 3、势力 4 不变） |
| `EliminationEndgame/主动弃赛Tests.遗留棋子可被围杀` | A1 / B1 / A2 | C3 / D3 / C4 | 几何平移 (+2, +2) | 是 |
| `EliminationEndgame/主动弃赛Tests.弃赛快照可记录` | 信物 H8，P3 H8 / J9；控制信物 ["H8"] | J9，K10；["J9"] | 几何平移 (+1, +1)：J9 是 9×9 的角，对应棋盘 0 的角 K10 | 是（部署上限 5、名次 1 不变） |
| `EliminationEndgame/主动弃赛Tests.WithEliminatedC`（助手：弃赛时势力名次、弃赛名次随存档往返） | A9 / J1 / J9；A8 / B9 | C10 / K3 / K10；C9 / D10 | 四角分别贴棋盘 0 四角（西 / 南 +2，东 / 北 +1） | 是（名次 2 不变） |
| `EliminationEndgame/主动弃赛Tests.此前已弃赛者计入弃赛名次` | G1 / H1 / J1；J9 | H3 / J3 / K3；K10 | 同上（四角贴角） | 是（名次 1 / 2 不变） |
| `EliminationEndgame/主动弃赛Tests.弃赛名次不影响最终名次` | G9 / H9 / J9 / J8；A1；三人整轮 Pass → 终局 | H10 / J10 / K10 / K9；C3；三人整轮 Pass → 本战场结算 → 终局（最后战场结算） | 四角贴角；终局路径按链接规则（设计文档 §12.4 / §12.5） | 是（D 第 4、排在三名完赛者之后） |
| `AiDecision/停手阈值Tests.Marginal`（助手：低于 / 恰等于 / 高于阈值） | D5 / E4 / E6 / F5 / E5 | F7 / G6 / G8 / H7 / G7 | 几何平移 (+2, +2) | 是（总分 15 / 20 / 21 不变） |
| `AiDecision/停手阈值Tests.零收益不落子` | A2 / B2 / C1；B1；空区 A1–B1 | C4 / D4 / E3；D3；C3–D3 | 几何平移 (+2, +2) | 是 |
| `AiDecision/停手阈值Tests.无落点被保留则Pass` | P0 A1；P1 E5 / F6 / D7；P2 G3 | P0 C3 + D6 / E4 / K3；P1 G7 / H8 / F9；P2 J5 | 几何平移 (+2, +2)；清单 P2：P0 加三枚孤子使其居本战场首位（否则落后者阈值取 0），加前提断言 | 是（MaxValue 下 Pass、阈值 0 下落子） |
| `AiDecision/停手阈值Tests.无子时 / 有子后 / 同批次内不中途切换` | E5；A1；B2 / E5 | G7；C3；D4 / G7 | 几何平移 (+2, +2) | 是（每枚 30、整批 60 不变） |
| `EliminationEndgame/终局名次与并列判定Tests.出局者倒序`（(a)，清单 P3） | 输入含弃赛者 P3；名次 [1, 4, 3, 2]（P0..P3） | 输入去掉弃赛者；名次 [1, 3, 2]（P0..P2） | 组间次序两口径相反（链接：完赛 > 出局 > 弃赛，由「链接模式终局名次Tests.弃赛者最后」守） | 是（第 8 大回合出局者高于第 5 大回合出局者） |
| `EliminationEndgame/终局名次与并列判定Tests.终局输入取独占空格数` | B5 / C5 | D7 / E7 | 几何平移 (+2, +2) | 是（期望取自同一份势力明细，不写死数字） |
| `MapDefinition/棋盘清单Tests.清单不参与结算`（(a)，清单 P7） | 第一段经对局流程（指纹含流程 / 手牌 / 信物）；第二段两名 AI 真实跑局（日志快照与事件） | 第一段经结算层（SettlementDriver + PowerCalculator，指纹：确认、提子、盘面、同形历史、势力明细），局面与落点不变；第二段改为同一结算层上 60 步种子驱动随机批次（样本口径：至少一次提子） | 链接局必须有棋盘清单，"无清单"一侧不能再建局（P7 先做 (a)） | 是（有无清单逐步相同；第一段 3 步、提子与 A9 归属断言不变） |
| `TurnSequence/大回合的定义与推进Tests.大回合完成 / 无固定轮数` | B2 / H2 / B8 / H8 | D4 / K4 / D10 / K10 | 几何平移 (+2, +2) | 是 |
| `TurnSequence/大回合的定义与推进Tests.出局者不再获得小回合` | A1 / B1 / A2；A5；J5 | C3 / D3 / C4；C7；K7 | 列四角贴角（西 +2、东 +1）、行 +2 | 是 |
| `TurnSequence/小回合的五个阶段Tests.快照在回合内不变` | 信物 E5；暂放 E5 / A5 / B7 / C8 / D2 | G7；G7 / C7 / D9 / E10 / F4 | 几何平移 (+2, +2) | 是（上限 4 → 5 不变） |
| `TurnSequence/合法落子范围的对外契约Tests.共享出生区内的禁入` | 出生区 0（A1–C3，9 格）扣除眼 A1、C1 | 棋盘 0（64 格）扣除眼 C3、E3 | 链接口径构筑期的合法范围是所在战场（同锁的出生棋盘），不是 3×3 出生区 | 是（同战场他人眼空间不在 P1 范围内、P0 范围含之） |
| `TurnSequence/合法落子范围的对外契约Tests.禁入扣除只在LegalRangeFor且与预演共用同一查询`（行为段） | 全图逐格；P2 角形 A8–D8 / B9 / D9；扣除 ["E5","G5","A9","C9"] | 棋盘 0 逐格；P2 角形翻到左下角 C4–F4 / D3 / F3；扣除 ["C3","E3","G7","J7"] | 链接载体上其余棋盘本就不在合法范围（非禁入扣除），比较域收窄为本战场；环平移 (+2, +2)，P2 角形上下翻转后平移 | 是（扣除格 = 预演"活棋禁入"格） |
| `TurnSequence/战场内合法落子范围Tests.构筑期内规则照常`（迁自「构筑保护期的落子限制Tests.保护期内规则照常」） | 信物 B2；A1；落 B1 / A2 | D4；C3；落 D3 / C4 | 几何平移 (+2, +2) | 是 |
| `EliminationEndgame/两类终局条件Tests.势力悬殊不提前结束`（迁自「三类终局条件Tests」） | P0 占满 A、B 两列 18 子；P1 J1、P2 J9、P3 E5；落 D5 / H1 / H9 / F5 | P0 占满棋盘 0 的 C、D 两列 16 子；P1 K3、P2 K10、P3 G7；落 F7 / J3 / J10 / H7 | 8×8 棋盘放不下 9 行的列：按"西侧两整列 + 东侧两角 + 居中"重排，关系"P0 ≥ 其余之和"由前提断言钉住 | 是（跑完第 9 大回合进入第 10 大回合、无终局） |
| `EliminationEndgame/两类终局条件Tests.Pass计数跨回合重置`（迁自「三类终局条件Tests」） | 落 E5 | 落 G7 | 几何平移 (+2, +2) | 是（计数 2 → 0 → 1、对局继续） |
| `EliminationEndgame/出局判定Tests`（5 条：势力归零立即出局 / 手牌有子也出局 / 保护期内不豁免 / 从未落子者不因Pass出局 / 开局零势力不出局） | A1 / B1 / A2；E5 / H8 | C3 / D3 / C4；G7 / K10 | 几何平移 (+2, +2)；构筑期"共享出生区"= 同锁棋盘 0 | 是 |
| `EliminationEndgame/出局判定Tests.同时归零同时出局` | A1 / J9；B1 / H9；落 A2 / J8 | C3 / K10；D3 / J10；落 C4 / K9 | 四角贴角（西 / 南 +2，东 / 北 +1） | 是（同序号、共享名次、只剩一名终局） |
| `MatchSetup/原型插旗替代路径Tests.调试依次插旗 / 出生区锁定结果可持久化` | 9×9 夹具；恢复经 RestoreUnvalidated | link:1；恢复经公开 MatchFlow.Restore | 载体换为合法链接档图（清单 R6） | 是（存档逐字节相同、锁定结果往返） |
| `MatchSetup/盲选插旗Tests.时限可配置`（迁自「匿名同时插旗Tests」） | 9×9 夹具 | link:1（公开建局） | 载体换为合法链接档图 | 是（锁定事件文本 "出生区 P0:3 P1:1 P2:4 P3:2；" 不变） |
| `MatchSetup/对局模式Tests.链接模式不能配旧地图` | MatchFixtures.Create(链接选项) | CreateUnvalidated(TestMaps.NineByNine, 链接选项) | 9×9 地图数据迁到 TestMaps（同为"链接模式 + 非链接图"） | 是 |
| `MatchSetup/带入带出配置Tests`（5 条：旧存档按无带入读取 / 新存档往返 / 对局中不可改 / 关闭时有带入即拒绝 / 征召签与其他玩家的带入无关） | 9×9 夹具；P3 H8 | 平坦 8×8 链接载体；P3 K10 | 几何平移 (+2, +2) | 是 |
| `MatchSetup/链接模式存档Tests.旧存档被拒绝` | 旧存档 = 9×9 Legacy 局的存档；另断言内部口 RestoreUnvalidated 能以旧模式读回 | 旧存档 = 链接存档把地图标识改成 test-match-9x9；"内部口以旧模式读回"一段去掉 | 对局只能建在链接档图上；旧模式恢复路径在 R2 删除范围内 | 是（玩家入口拒绝并点名标识与 link:<地图种子>、链接存档照常恢复） |
| `MatchSetup/首回合顺序随机Tests`（2 条） | 9×9 夹具 | link:1，插旗结果不变 | 载体换为合法链接档图 | 是 |
| `MatchSetup/开局信息公开范围Tests.插旗阶段的可见信息` | 出生区 4 个；信物格 ["B2","E5"] | 出生区 5 个；["D4","G7"] | 链接档图五块出生棋盘；几何平移 (+2, +2) | 是（信物格公开、内容隐藏） |
| `MatchFlowRegression/对局持久化Tests.小回合边界存档恢复后状态完全一致` | 信物 E5 / C3；首轮 B2 / H2 / A9 / H8；B9；批次 E5 / C3 / A8；栅栏 G2–G3；续跑 F6 | G7 / E5；D4 / K4 / C10 / K9；D10；G7 / E5 / C9；J4–J5；H8 | 列 +2；第 1–7 行 +2、第 8–9 行 +1（左上角 A9 贴棋盘 0 的角） | 是（全部往返断言、续跑逐字节相同、上限 5） |
| `MatchFlowRegression/对局持久化Tests.弃赛快照的驿站来源与工坊标记…` | G4 / H4 / J4；驿站来源 (G4, 2) | H6 / J6 / K6；(H6, 2) | 列 +1（J 列贴东边）、行 +2 | 是 |
| `MatchFlowRegression/对局持久化Tests.已结束对局与终局结果可恢复` | P0 E5 / B1 / H9 / H1；P1 A1、P2 J9、P3 J1；批次 A2 / J8 / J2 | G7 / D3 / J10 / J3；C3、K10、K3；C4 / K9 / K4 | 三角各贴棋盘 0 的角（西南 +2、东北 +1，中线 E5 +2） | 是（名次 [1, 2, 2, 2]） |
| `MatchFlowRegression/对局持久化Tests`（终局独占空格数… / 曾建立正势力标记… / 存档地图不符即拒绝 等 6 条） | B5 / C5；E5；`MatchFixtures.Map() with { Id = another-map }` | D7 / E7；G7；`PlainMap() with { Id = another-map }` | 几何平移 (+2, +2)；恢复仍经 RestoreUnvalidated（手工信物）的链接路径 | 是 |
| `MatchFlowRegression/流程回归Tests.最后一手抢到的先锋本大回合结束即生效` | P0 B2 / E2 / H2、P1 E8 / H8、P2 B8、P3 落 E5（先锋） | P0 D4 / G4 / J5、P1 D8 / J8、P2 E6、P3 落 G7 | 8×8 内圈放不下原三行孤子：重排为两两曼哈顿距离 ≥ 3 的七格，加前提断言势力 [15, 10, 5, 0] | 是（修正 1、第 3 名、先手值 2、顺序 P0 > P3 > P1 > P2） |
| `MatchFlowRegression/流程回归Tests.任意阶段可替换决策来源`、`输出契约Tests`（3 条） | E5 | G7 | 几何平移 (+2, +2) | 是 |
| `InitiativeOrder/先手值公式Tests.四人局基础先手值 / 参赛人数变化影响公式`、`基础排序Tests`（2 条） | 9×9 坐标（B2 / E2 / H2 / E5 / E8 / H8 / J9 / A1 / B1 / A2 / J8 / H9 等） | 列 / 行均按 0–4 → +2、5–8 → +1 映射（四角贴棋盘 0 的角；如 E5 → G7、H8 → J9、J9 → K10） | 8×8 放不下 9 行 / 9 列：中线以西 / 南 +2，以东 / 北 +1 | 是（名次 / 先手值 / 顺序断言不变；严格递减由测试自身断言） |
| `InitiativeOrder/排名的作用范围Tests.排名不累计` | P0 十字 B5 / E5 / H5 / E2 / E8；三角各五手 | P0 中部 C3 / E4 / G5 / C6 / E7；三角五手贴棋盘 0 的角 | 8×8 上原十字会与角上棋串共享邻格（势力不再恒等）：P0 改放与三角距离 ≥ 3 的五格 | 是（五轮均第 1、势力恒等） |
| `InitiativeOrder/排名的作用范围Tests.落后不获补偿` | P0 A1–D1、P1 G1–J1、P2 A9 / B9、P3 J9（注释复算 9 / 7 / 5 / 3） | C3–F3、H3–K3、C10 / D10、K10（复算 8 / 6 / 5 / 3，另加断言钉住） | 四角贴角；底边少一格空位，P0 / P1 之间的 G3 为争议格 | 是（P3 第 4、快照参数 "5:5/3/5/4" … 不变） |
| `InitiativeOrder/同值判定链Tests.首回合种子兜底` | 9×9 四角出生区落 B2 / H2 / B8 / H8 | link:1 四人各锁一块出生棋盘、各落核心区中心 | 载体换为合法链接档图（清单写法），对称位置取核心区中心 | 是（势力全等、顺序 = 种子顺序） |
| `Recruitment/私人征募面板Tests.最后一名没有补偿`、`RelicEffects/效果快照在小回合开始时生成Tests.名次不影响快照` | P0 A1–D1、P1 G1–J1、P2 A9 / B9、P3 J9（复算 9 / 7 / 5 / 3） | C3–F3、H3–K3、C10 / D10、K10（复算 8 / 6 / 5 / 3） | 四角各贴棋盘 0 的角 | 是（名次 1 / 4、面板 / 快照参数不变） |
| `Recruitment/私人征募面板Tests.驿站生效`、`RelicEffects/同类信物叠加且无统一硬上限Tests.来源可拆分`、`驿站的展示数加成Tests.与探勘相加`（对局段） | G4 / H4 / J4；来源 ["H4","J4"] / (J4, H4) | H6 / J6 / K6；["J6","K6"] / (K6, J6) | 列 +1（J 列贴东边）、行 +2 | 是（展示数 8 / 7、上限 7 = 5 + 2） |
| `RelicEffects/工坊扩大匠人的格改造范围Tests.控制工坊即标记生效`（对局段） | 林地 H6 / F4、工坊 G4；匠人 F6 烧林 H6 | 林地 K8 / H6、工坊 J6；匠人 H8 烧林 K8 | 几何平移 (+2, +2)（账本段坐标不动） | 是 |
| `RelicEffects/计分信物连营与犄角Tests.失去控制立即失效` | 连营 E5、E4、连珠 D8–F8、B 落 E6 | G7、G6、F10–H10、G8 | 几何平移 (+2, +2) | 是（连珠加值 9 → 6） |
| `RelicEffects/效果快照在小回合开始时生成Tests.分阶段基础值只在生成快照时读取一次`（对局段）、`Recruitment/初始配置与基础棋池Tests.匠人权重可配置` | H8 | K10 | 几何平移 (+2, +2)（账本段坐标不动） | 是 |
| `LifeShape/禁入格Tests.弃赛者的活形仍禁入`、`LifeShape/活形三态Tests.活形不入存档` | 环 D4–H6、P1 B9；眼 ["E5","G5"] | 环 E5–J7、P1 D10；眼 ["F6","H6"] | 列：B → D、D–H 各 +1（环东侧仍留一列空地）；行 +1 | 是 |
| `BatchDeployment/非法批次必须给出可定位的原因Tests.活棋禁入返回落点与棋串`（共用常量 RingE5G5 → RingF7H7） | 环 D4–H6，暂放 E5 | 环 E6–J8，暂放 F7 | 列 +1、行 +2（环不贴棋盘 0 的边；常量另起 `RingF7H7`，原常量留给 (c) / (j)） | 是 |
| `BatchDeployment/批次数量与库存约束Tests.基础值随阶段提高`（Theory 3 行） | 暂放 A1 / C1 / B2 / A3 / C3 / B1 | C3 / E3 / D4 / C5 / E5 / D3 | 几何平移 (+2, +2) | 是（上限 3 / 4 / 5） |
| `BatchPreview/批次预演必须显示的信息Tests`（2 条）、`预演不改变游戏状态Tests.预演无副作用`、`未知信物在预演中只显示将揭示Tests.将揭示提示` | B2 / E5 / H8；信物 E5、P1 D5 / F5、暂放 E4 / E6 / C5；指纹替换 "E4:Fortress" | D4 / G7 / K10；G7、F7 / H7、G6 / G8 / E7；"G6:Fortress" | 几何平移 (+2, +2) | 是 |
| `BatchPreview/计分信物与新棋子的预演Tests`（3 条 + 助手 Match） | 连营 H5；线 C5–E5；暂放 H4 / G5；P1 G4 / G6 | J7；D7–F7；J6 / H7；H6 / H8 | 列 +1（H 列不贴边）、行 +2 | 是（连珠加值 9 / 6、差 3、哨兵加值 4） |
| `BatchPreview/非法批次必须说明原因并高亮Tests.活棋禁入说明` | 环 RingE5G5、焦点 E5 | RingF7H7、F7 | 列 +1、行 +2 | 是（14 处高亮） |
| `BatchPreview/预演一致性回归Tests.千例随机局面预演与结算一致` | 9×9 全图铺子；信物 C3 / G3 / E5 / C7 / G7；实测样本 281 / 35 / 85 / 634 | 只铺棋盘 0；E5 / J5 / G7 / E9 / J9；实测 324 / 46 / 74 / 602 | 随机局面改在棋盘 0 内取格（清单写法） | 是（下界 200 / 25 / 50 / 400 不变） |
| `CarryInOut/换型候选类型Tests`（2 条）、`补给种类与开局效果Tests`（5 条） | 9×9 夹具 | link:1（公开建局，开局手牌与模式无关） | 载体换为合法链接档图（清单写法） | 是 |
| `TerrainEditing/同形与存档纳入设施Tests.对局存档往返保留改造与匠人权重`、`地形派生数据不缓存Tests`（3 条，其一改名「合法落子范围含本局新架的桥」）、`改造不可逆与公开Tests.弃赛不撤销改造 / 改造结果人人可见且不显示改造者`（对局段） | 深水 / 栅栏 / 桥 E5、E5–E6、D4、F4 | G7、G7–G8、F6、H6 | 几何平移 (+2, +2)（规则层段坐标不动） | 是（范围只多一格） |
| `TerrainEditing/改造不可逆与公开Tests.默认棋盘上的新旧设施同形且不带改造者` | 深水 D4 / F4、林地 G4 / H4、栅栏 B1–B2 / C1–C2 | F6 / G6、H6 / J6、D3–D4 / E3–E4 | 列 0–3 +2、4–8 +1（H 列不贴边）；行 +2 | 是 |
| `HandInfoPanel/公开结构参数与信物来源Tests`（3 条） | 军令 H4 / J4，来源文案 ["H4 军令 +1","J4 军令 +1"]；梯队 A1–D1 / G1–J1 / A9 / B9 / J9；探勘 B4 | J6 / K6，["J6 军令 +1","K6 军令 +1"]；C3–F3 / H3–K3 / C10 / D10 / K10；D6 | 四角贴角映射（列 / 行 0–4 +2、5–8 +1） | 是（上限 6 = 4 + 2 等文案数值不变） |
| `HandInfoPanel/新棋子类型与驿站来源的显示Tests`（3 条 + 助手） | 驿站 / 探勘 / 军令 G4 / H4 / J4；文案 "H4 探勘 +1"、"G4 驿站 +2（…）"、"…驿站 G4 +2（…）" | H6 / J6 / K6；"J6 探勘 +1"、"H6 驿站 +2（…）"、"…驿站 H6 +2（…）" | 列 +1、行 +2 | 是（展示数 8 = 5 + 3） |
| `HandInfoPanel/征募面板可收起Tests`（2 条） | 落点 J1 / H1 / G1、A9 / A8 / A7、J9 / H9 / G9、A1 | K3 / J3 / H3、C10 / C9 / C8、K10 / J10 / H10、C3 | 四角贴角映射 | 是 |
| `HandInfoPanel` 其余（全玩家手牌对比面板 / 弃赛玩家的面板表现 / 敌方区域的显示内容 / 自己区域的显示内容，共 6 条） | E5 等 | G7 等 | 几何平移 (+2, +2) | 是 |
| `InformationVisibility/始终公开的信息Tests.领地分公开` | P0 C5–G5；独占 ["B5","C4",…,"H5"] 12 格 | P0 D7–H7；独占 ["C7","D6",…,"J7"] 12 格 | 列 +1、行 +2（一排五子两端各要一格空位） | 是（领地分 12） |
| `InformationVisibility/始终公开的信息Tests.活形状态公开` | 环 RingE5G5、P2 A8 / B9、P3 J1；眼 ["E5","G5"] | RingF7H7、C9 / D10、K3；["F7","H7"] | 列 A–B +2 / 其余 +1，行 1–7 +2 / 8–9 +1（三角贴角、环不贴边） | 是（三态与禁入不变） |
| `InformationVisibility/始终公开的信息Tests`（棋子类型 / 势力明细 / 气可推导 / 带入公开）、`必须隐藏的信息Tests`（3 条 + 助手） | E5；B2–G2；D4-D5-E5 及其 7 口气；信物 E5、B8 | G7；D4–J4；F6-F7-G7 及其 7 口气；G7、D9（必须隐藏：列 +2 行 +1） | 几何平移 | 是 |
| `SettlementShow/演出期间对局不推进Tests.演出不影响对局结果`（脚本与 Start 助手） | F6 / E6、B8、H2、C2 / B1；P1 B2、P0 A2 / B3 | G7 / F7、D9、J4、E4 / D3；D4、C4 / D5 | 列 A–C +2 / D–J +1，行 1–4 +2 / 5–9 +1 | 是（演出前后快照逐项相同、含一次提子） |
| `SettlementShow/结算节拍序列的生成Tests.相同输入得到相同序列`（对局段） | F6 / E6 | H8 / G8 | 几何平移 (+2, +2) | 是 |
| `AiDecision/活形硬约束Tests`（TwoEyes 与 5 条） | 眼 A1 / C1、直三 A1–C1 等左下角局面 | C3 / E3、C3–E3 | 几何平移 (+2, +2) | 是（排行 = 空格去掉单格眼等） |
| `AiDecision/活形硬约束Tests.为提子而填眼放行` | 左下角形 + P0 H9、P1 J9；批次 A1 + J8 | 同形 + J10、K10；C3 + K9 | 四角贴角 | 是 |
| `AiDecision/活形硬约束Tests.用改造拆自己的活形也被淘汰` | 预置栅栏 A1–A2 / J1–J2，一排 B1…H1（7 子），匠人 D2 立栅 D1–E1 | 栅栏 C3–C4 / K3–K4，一排 D3…J3（6 子），匠人 F4 立栅 F3–G3 | 8 列放不下 9 格宽的一排：同形缩一子 | 是（两端各剩一眼、两条未定棋串、该改造被淘汰） |
| `AiDecision/前瞻中的停手口径Tests.DoomedPosition`（及 回应后不重判活形硬约束、专家难度 2 条） | 7 子串 B5–H5、唯一气 J5；P1 两整行 + A5（19 子）；回应含 J5 | 7 子串 D7–K7、唯一气 C7；P1 两整行（16 子）；回应含 C7 | 8 列放不下"1 + 7 + 1"：七子串贴东边（盘边即封堵） | 是（被提 7 子、危险扣分算式不变） |
| `AiDecision/前瞻中的停手口径Tests.SqueezedPosition`（前瞻不引入Pass） | A1–E2 十子块，气 B3 / D3 / F1 / F2 | C3–G4，气 D5 / F5 / H3 / H4 | 几何平移 (+2, +2) | 是（3 个候选前瞻后分数全负、仍落子） |
| `AiDecision/前瞻中的停手口径Tests`（高难会Pass… / 两层单点不受停手阈值 / 两层单点受活形硬约束） | E5 / B8；D5 / E5 / F5 / F6 / B8 / H2；TwoEyes + J4 / J6 / J5 / B8 / H8 | 映射后坐标（自动逐轴 / 列四角贴角、行 +2） | 几何映射 | 是 |
| `AiDecision/专家难度的一层前瞻Tests.AtariPosition`（及 4 条） | 候选键 "G8:Basic"（A）/ "G5:Basic"（B）；局面 B4–J9 | "H9:Basic" / "H6:Basic"；局面 C5–K10 | 平移 (+1, +1)（右上角仍贴盘角） | 是（高难选 A、专家选 B、A 的回应提四子串） |
| `AiDecision/专家难度的一层前瞻Tests.TwoCandidatePosition`（前瞻集不足W个 / 不前瞻时直接取前瞻集第一个） | 两块并排于底边 B1–D2、F2–J2（P2 E3 挡子）；候选 ["D1:Basic","F1:Basic"] | 左下 D3–F4 + 右上同形转 180° J10–G9（无挡子）；["F3:Basic","G10:Basic"] | 8 列放不下"4 + 1 + 4"：右块改放右上角 | 是（候选恰 2 个、各做模拟；OneCandidate 只剩 {F3}） |
| `AiDecision/专家难度的一层前瞻Tests.模拟回应全为Pass`（NoResponsePosition） | P1 的 3×3 出生区被自己 9 子填满（旧模式保护期合法范围 = 出生区 → 空） | P0 占满棋盘 0、只留三块直三眼空间（C3–E3 / C10–E10 / K5–K7），P1 的合法范围里没有空格（全是 P0 活形的禁入格）；另设部署上限 1 | 链接模式合法范围是整个战场，原构造在链接规则下不成立：改用"全部空格都是他人活形眼空间"使对手无处可落 | 是（每个回应都是 Pass、前瞻后 = 自身总分、与高难同选） |
| `AiDecision/前瞻中的下一名对手Tests`（跳过已弃赛者 / 跳过已出局者 / 候选使下一名对手出局 / 只剩自己时退化） | E5 / B8 / H2 / H8；J9 / J8 / H9；B2 / A2 / C2 / B1 / B3 等 | 映射后坐标（如 J8 → K9、B3 → D5） | 几何映射 | 是 |
| `AiDecision/前瞻中的下一名对手Tests.LastSeatPosition / 专家是本大回合末位` | P2 两行（A6–J6 九子 + 五子）；期望合法范围 = 全图可落子格扣除禁入格 | P2 C7–K7（八子）+ 五子；期望 = 本战场（棋盘 0）可落子格扣除禁入格 | 8 列放不下九子行（并一子）；规格 Scenario 改为"对手所在战场" | 是（下一名对手 (P2, 4)、上限 4） |
| `AiDecision/前瞻中的下一名对手Tests.专家在预测顺序中居首` | P0 底边 A1–J1 九子 + A3 / C3 / E3，候选 G3 | C3–K3 八子 + C5 / E5 / G5，候选 J5 | 8 列放不下九子行（并一子） | 是（P0 > P3 > P1、下一名对手 (P3, 6)） |
| `AiDecision/前瞻的近似两层加分Tests.按下一大回合的口径枚举` | 期望 = 全图可落子格扣除禁入格；"出生区之外" E5 | 期望 = 本战场（棋盘 0）可落子格扣除禁入格；K10（棋盘 0 核心区之外） | 规格 Scenario 改为"专家所在战场" | 是（上限 4 + 2 = 6） |
| `AiDecision/前瞻的近似两层加分Tests`（无对手的候选在B1上计加分 / 专家出局时加分为0） | 候选键 "B3:Basic"；E5 等 | "D5:Basic"；G7 等 | 几何映射 | 是 |
| `AiDecision/前瞻模拟的公平信息Tests`（Contact / Command / HiddenRelic / 投影 3 条 / 投影不揭示） | 9×9 坐标 | 映射后坐标（Contact 列行 0–1 +2 其余 +1；HiddenRelic (+1, +1) 等） | 几何映射 | 是 |
| `AiDecision/前瞻模拟的公平信息Tests.构筑期内只在本战场回应`（原「保护期内只在出生区回应」） | 军令 H2 由 P1 的 H3 覆盖；回应落点 ⊆ P1 出生区 G1–J3 | 军令 K3（盘角）由 P1 的 J3 / K4 封住；回应落点 ⊆ P1 所在战场（棋盘 0） | 规格 Scenario 改名改写；链接构筑期专家能落到军令旁使其争议，故军令移入盘角 | 是（上限 3 + 1 = 4、回应在战场内） |
| `AiDecision/前瞻模拟的公平信息Tests.ProjectedFields`（投影 3 条共用） | 比较 PlayerFlowState 全部字段（旧载体上出局原因 / 落败势力 / 等待大回合恒空） | 剔除这三项链接新增字段再比较 | 投影（D8）不填链接新增的出局原因，链接局真实快照会记 PowerZero——见回报 | 是（D8 字段逐项相同） |
| `AiDecision/启发式评价维度Tests.CaptureRelicPosition`（及评价覆盖九个维度 / 权重可配置 / 先手位评价生效；外部用户：难度分级、候选格上限、高部署上限） | 局面 D5 / E4 / E6、E5 / H8 / H6 / A9，信物 F5；对照远点 A1；不改名次的 D4 | E6 / F5 / F7、F6 / J9 / J7 / C3，信物 G6；对照远点 K3；E5 | 平移 (+1, +1)；左上角孤子 A9 落在棋盘 0 外，改放另一个盘角 C3（同为角上孤子），远点 A1 → K3 | 是（势力 14 / 10 → 12 / 13、名次 2 → 1、供给 −2 等逐项不变） |
| `AiDecision/启发式评价维度Tests`（眼位 5 条 + 叫吃获得威胁正贡献） | 左下角眼形 A1–D2；威胁 A4–E6、气 ["E4","F5"] | C3–F4；C6–G8、["G6","H7"] | 几何平移 (+2, +2) | 是（眼位 4 / −1 / −4 / 1、威胁 5） |
| `AiDecision/候选格上限Tests.小图不限制`（P1）与黄金值 `SmallMapGoldenHash` | 9×9 手写夹具（81 格，Legacy，四角出生区）；DEB4ED37…87E4 | 手写小链接档图（五块 5×5，125 格，链接模式，四人同锁棋盘 0）；26AF3B28…BD41 | 链接档图可落子格恒 > 150，"小图"只能是未校验手写图；黄金值以当前实现在新载体上重钉 | 是（缺省 K = 0、与显式 0 逐步相同、确有落子） |
| `AiDecision/候选格上限Tests`（OpenPosition 5 条 / ArtisanOnlyCellPosition） | E5 / F6 / D7 / G3；A2 深水、B1 / B2，候选 A1，栅栏 B1–C1 | G7 / H8 / F9 / J5；C4、D3 / D4，C3，D3–E3 | 几何平移 (+2, +2) | 是（合法空格 ≥ 60、前 K 取法等） |
| `AiDecision/候选格上限Tests.小K下仍找到妙手`、`高部署上限下的候选剪枝Tests.必须找到的妙手`（Theory 各 3 行） | 行数据 ("a","F5") / ("b","E5") / ("c","F5")；局面 a / b 原样 | ("a","G6") / ("b","F6") / ("c","G6")；局面 a / b 另给 P0 补一块 2×2（J3–K4 / J9–K10） | 平移 (+1, +1)；清单 P2：a / b 中 P0 原本落后，补子使其居首（AsLeader 断言） | 是（妙手进前 K、真的提子） |
| `AiDecision/高部署上限下的候选剪枝Tests`（不穷举排列 / 剪枝保留高价值落点 / 种子扰动由种子驱动） | E5 / F6 / D7 / G3；F5；E5 / F6 / C4 | G7 / H8 / F9 / J5；G6（+ P0 补 J3–K4）；G7 / H8 / E6（+ P0 补 C3、K3） | 几何平移；清单 P2 补子 | 是（候选 ≤ M、落满 8 枚、同种子同候选） |
| `AiDecision/难度分级Tests`（4 条） | F5；左下角眼形 / TwoEyes；空盘 | G6；C3–F4；PlainStarted | 平移 | 是 |
| `AiDecision/对隐藏信息的概率估计Tests.估计不得使用真实值` 与 `RunUntilReveal`（难度分级共用） | 信物 B2（+ E5）；候选 A2 | D4（+ G7）；C4；另把首轮顺序摆成 P0 先行 | 几何平移 (+2, +2)；链接构筑期四人同战场，先行的对手可能先揭示 D4（旧模式保护期内只有 P0 够得着） | 是（揭示前两局决策逐项相同、P0 至少决策一次） |
| `AiDecision/新棋子与新信物的AI适配Tests`（6 条） | E5 / G5 / F5 / A1；H8 / C3–C5；E7 / E5；H5 / C5 / D5 / H4；E5 / E4 | 映射后坐标（各条自动逐轴，保住盘边身份） | 几何映射 | 是 |
| `AiDecision/正式对战AI的信息边界Tests`（3 条）、`已确定活形的评价中性Tests`（3 条）、`AI决策的可复现性Tests`（2 条）、`前瞻集的多样候选Tests.补充候选同样受停手阈值`、`活形分析的决策内缓存Tests`（OpenPosition / RichPosition） | E5 / B2；角上眼形与气位；H9…（右上角）；信物 E5 / B7；候选键 "D1:Basic" | G7 / D4；C3…；J10…（平移 (+1, +1)）；G7 / D9；"F3:Basic" | 几何平移 | 是 |
| `AiDecision/活形分析的决策内缓存Tests.地形改造不同的盘面不共用分析` | 栅栏 A1–A2 / J1–J2，一排 B1…H1，匠人 D2 立栅 D1–E1 | 栅栏 C3–C4 / K3–K4，一排 D3…J3，匠人 F4 立栅 F3–G3 | 与「活形硬约束」同一缩形 | 是 |
| `AiDecision/AI对地表的感知Tests`（5 条）、`AI枚举改造目标Tests`（4 条） | 地表 / 深水 / 林地 / 栅栏在 9×9 中央 E5 一带 | 平坦 8×8 链接载体棋盘 0 的 G7 一带（未校验图承载新地表） | 几何平移 (+2, +2) | 是（势力增量 5 / 1 / 1 / 9、气 2、改造候选等） |
| `AiDecision/公开规则纯函数Tests.真实对局每个小回合新旧两条路径逐项相等`（Theory 3 行） | 9×9 夹具 4 名标准 AI 整局（手放军令 / 工坊 / 先锋）；① 比对含旧路径 OldLegalRange | link:1 探针（内置配对脚本、生成信物，样本口径断言含三类信物）整局；① 改为三方相等；旧实现读内容改按账本当前坐标（ContentAt） | 清单"删反面段"：旧合法范围路径只在旧模式成立；链接拼接后信物随格平移 | 是（② ③ ④ 不变、比对 ≥ 20 个小回合） |
| `AiDecision/公开规则纯函数Tests`（先锋修正 / 出局判据 / 夹具局面） | RelicLedger.Restore 作副本；9×9 坐标；① 含旧路径 | Copy()；映射后坐标；① 三方相等 | R12 / 清单写法 | 是 |
| `TacticalLayers/五种战术信息层Tests`（6 条） | 9×9 坐标；领地层格数 81；顺序层 P0–P3 全员 Pass 结束大回合 | 逐用例映射坐标（保住盘边 / 盘角）；64；P0 在 G7 落一子、P1–P3 Pass，预测在落子后读取 | 链接规则下本战场全员整轮 Pass 触发战场结算（唯一战场即终局，不再生成下一轮顺序） | 是（9 / 7 / 5 / 3、预测顺序 P0 > P3 > P1 > P2、预测 = 实际） |
| `TacticalLayers/信息层的可用时机与无副作用Tests`（4 条） | 9×9 坐标；插旗阶段默认棋盘 81 格 | 平移 (+2, +2)；插旗阶段默认棋盘为空（`Assert.Empty`） | 链接模式插旗阶段座位视野里还没有格（只有棋盘占位） | 是（领地 / 势力 / 顺序层退化为空、补充载荷可发布） |
| `TacticalLayers/单子禁手的标示Tests.呈现` / `.查询`（17 条，共用 `Surrounded`） | 围点 E5（`AroundE5` = E4 / E6 / D5 / F5）；候选数 77 / 74 / 73；劫点 C4 / J1；四类标记 A5 / B5 / C5 + 环 E5 / G5；破坏活形 岩石 B1 / C1 / D1 / F1、一排 A2–E2 | 围点 F6（`AroundF6` = F5 / F7 / E6 / G6，A 列 → C、其余列 +1、行 +1）；60 / 57 / 56；D6 / K3；C4 / C7 / D7 + `RingF7H7`；岩石 D3 / E3 / F3 / H3、一排 C4–G4（平坦图加岩石） | 棋盘 0 为 64 格（候选 = 64 − 棋子 − 暂放）；几何映射保住劫形与盘边 | 是（禁手种类、原因文案、所有者、同形序号 2 / 7） |
| `TacticalLayers/势力层领地与高地Tests`（6 条） | C5–G5 一排、J9 / G9 争议 H9；荒漠 B5 / C4 / C6；高地 D5 / E5 等 | 平移 (+1, +1)：D6–H6、K10 / H10 争议 J10；其余平移 (+2, +2)；高地改 `PlainStarted(地形)` | 几何映射（J 列保住） | 是（独占 12、领地 5 + 3、高地 2、铁链 4 / 哨兵 2、旗手 3 / 界碑 3、热区 3 / 7.59375） |
| `TacticalLayers/改造在默认棋盘与棋串读法里的呈现Tests`（4 条） | 深水 E5 / 林地 G5 / 匠人 F5；栅栏 D5–E5；工坊 G4、深水 H6、匠人 F6 | G7 / J7 / H7；F7–G7；J6、K8、H8 | 几何平移 (+2, +2) | 是（候选边 16、气 5 口、差集原因栅栏） |
| `TacticalLayers/新地表的规则标示Tests`（8 条）、`盘面层的读法切换Tests`（3 条，共用 `TerrainMatch`） | 9×9 夹具中段地形（沼泽 / 岩台 / 浅滩 / 崖壁 / 栅栏 / 深水 / 林地） | 平坦 8×8 链接载体棋盘 0（`PlainStarted(地形)`），平移 (+2, +2) | 几何平移 | 是（差集格与原因逐格不变） |
| `TacticalLayers/活形与禁入格的标示Tests`（5 条） | 环 `RingE5G5`；孤子 A1 / A2；角上两眼 A1–E4；范围外 A9（保护期里 P2 的出生区）、岩石 B6 | `RingF7H7`；C3 / C4（A / B 列 +2、其余 +1、行 +2）；C3–G6；范围外 = 棋盘 1 左下角（不带视野过滤的公开棋盘上取）、岩石 C8 | 链接模式合法范围恒为整个战场、座位视野看不到战场之外的格：原"同战场内的范围外格"不存在 | 是（三类落不下互斥、样式两两不同） |
| `TacticalLayers/盘面层视图模型带地形Tests`（2 条） | 13×13 地形夹具（旧模式建局，标识 test-terrain-view-13） | 同一夹具改以链接模式建局（标识 link:90013），只取插旗阶段公开世界 | 链接模式只配 `link:` 标识 | 是（地形字段与断言一字不改） |
| `TacticalLayers/默认棋盘上的未发现信物Tests`（2 条，`PresentationFixtures.StartedWithZonedRelics`） | 9×9 夹具上 B2（出生区档）/ E5（公共高档） | 平坦载体上 D4 / G7（四人同锁棋盘 0） | 几何平移 (+2, +2) | 是 |
| `AiDecision/链接模式下的决胜一手Tests.旧模式不变`、`链接模式下落后者的停手阈值Tests.旧模式不变`（(c)） | `AiFixtures.Round5Legacy()` | 直接调 `MatchFixtures.Started().AtRound(5, …)`（第 2 组随 (c) 删除） | tasks 1.2 写法；`Round5Legacy` 删除 | — |
| `SimulationHarness/人工接管Tests`（3 条）、`原型调试AITests`（3 条） | 人工落点 H2 / G2 / H3（P1 出生区）；信物 E5 | K4 / J4 / K5；G7（四人同锁棋盘 0） | 几何平移 (+2, +2) | 是 |
| `SimulationHarness/出生区编号显示Tests.六区图的插旗棋盘快照` | 旧模式 20×20 六平台图（test-six-zones），插旗快照 20 行、台外 "."、每区 24 格（扣台内信物） | 同图改以链接模式建局（link:90006，六平台登记为六块出生棋盘）：只画出生棋盘外接矩形 18 行、台外空白、占位整块画区号 25 格；两条期望行按渲染重导 | 链接终端插旗阶段画出生棋盘占位（BoardRenderer.Viewport） | 是（六个区号都出现、无 7 以上区号） |
| `SimulationHarness/终端对局Tests.状态栏大数势力缩写不撑破一行` | 9×9 夹具放大为 12×12、旧模式、各锁一角 | 同尺寸改以链接模式建局（link:90012，整图一块 12×12 出生棋盘、四人同锁） | 链接模式只配 `link:` 标识、战场取自棋盘清单 | 是（领地 12、缩写、≤ 100 列） |
| `SimulationHarness/终端新棋子与新信物Tests`（4 条） | C5 / E5 / H4 / 驿站 H5；B8；深水 E7、"E5 A B:E7"；`CellText(行 5, 列 2 / 4 / 7)` | E7 / G7 / K6 / K7；D10；G9、"G7 A B:G9"；`CellText(行 7, 列 2 / 4 / 7)`；文案 "驿站 K7"、"搭桥 G9（隔一格）"、"立栅 G7-G8" | 平移 (+2, +2)；链接终端只画本战场外接矩形，列号按视口左缘计（平移后列号不变、行号 +2） | 是（字母 / 图例 / 来源拆分不变） |
| `SimulationHarness/终端活形与禁入标示Tests`（5 条） | `RingE5G5`；`CellText(5, 4 / 6)`、`(4, 3)`、`(6, 7)`、`(5, 0)`；未定 E5 周围 D5 / F5 / E4 / E6；信物 E5、"E5(x1)"；x 集合按全图坐标取 | `RingF7H7`；`(7, 3 / 5)`、`(6, 2)`、`(8, 6)`、`(7, 0)`；G7 周围 F7 / H7 / G6 / G8；F7、"F7(x1)"；x 集合按棋盘 0 视口取（列 = x − 视口左缘） | 链接终端视口 = 本战场外接矩形 | 是（x 集合 = 禁入格集合（− 信物格）） |
| `MatchTelemetry/AI改造与主题诊断Tests`（4 条） | 匠人 E5 / E4、深水 D4 / D5、林地 F4；预筛 {E4, H7}、P1 孤子 H8 被 G8 / J8 / H9 围 | G7 / G6、F6 / F7、H6；{F5, J8}、J9 被 H9 / K9 / J10 围 | 平移 (+2, +2)；预筛一条 (+1, +1)（J 列保住） | 是（目标数 16 / 1 / 1、预筛外打分、与正式排序同口径） |
| `MatchTelemetry/对局日志的记录内容Tests`（7 条） | 领地 C5–H5、P1 D6 / H4；自杀手 A1（P1 A2 / B1）；哨兵 F5（E5 / G5）；驿站 H5 / 工坊 H3 / 先锋 B8、搭桥 E7 / D5，期望 "H5=2"、"B:D5/False"、"B:E7/True"；带出 A9 / J1 / J9 / A8 / B9；12×12 旧模式大数图 | D6–J6、E7 / J5（+1, +1）；C3（C4 / D3）；H7（G7 / J7）；K7 / K5 / D10、G9 / F7，"K7=2"、"B:F7/False"、"B:G9/True"；C10 / K3 / K10 / C9 / D10（四角映射）；12×12 链接图（link:90012，整图一块出生棋盘） | 几何映射；链接模式只配 `link:` 标识 | 是（领地 14 → 9、Suicide、哨兵 4、展示数 7、带出结算行逐字不变、大数逐位一致） |
| `MatchTelemetry/活形记录与统计Tests`（5 条） | 角上活形 `CornerA1C1`（眼 A1 / C1）、眼值串 "A1=1" / "C1=1"、"B1=1" / "D1=1"；`TerrainSmallEyeSpaces` 0；`PlayableCells` 81 | `CornerC3E3`（眼 C3 / E3）、"C3=1" / "E3=1"、"D3=1" / "F3=1"；**2**；208 | 平移 (+2, +2)；链接图棋盘 0 之外是画布内障碍格，角眼被计为"贴地形小空区"（原口径"棋盘外沿不算地形墙"在链接图上不成立——见回报）；可落子格为五块棋盘 64 + 4 × 36 | 是（确立 / 失去 / 拒绝记录逐项不变） |
| `MapDefinition/两位数行号贯通Tests`（2 条） | 25×32 竖长图、旧模式 2 人局、各锁一区；跑局截断 2 个小回合（2 人） | 同图改链接形态（link:90032、人数上限 4、棋盘清单外一律障碍）：存档 / 渲染一条整图登记为一块 25×32 出生棋盘、四人同锁；日志一条两区各一块 5×5 出生棋盘、P0 / P2 锁 0 号、P1 / P3 锁 1 号，截断 4 个小回合 | 链接模式只有 4 人局、只配 `link:` 标识；链接终端只画本战场外接矩形（整图一块才画得出 32 行）；合法范围恒为本战场（P0 落点仍只在 25–29 行） | 是（断言一字不改） |
| `MapSelection/选图视图模型Tests.预览说明读默认棋盘视图模型`（(m)，1.7 一并做） | 末行旧地图文案 "9×9，4 个出生区，可落子 81 格"；助手 `ViewOf` 按标识分旧 / 链接模式 | 末行删除（P5）；`ViewOf` 恒链接模式 | 清单"删反面段" | — |

> 第 1 组回填（retire-legacy-rules 1.3–1.7）：共 117 行（一行可覆盖同一文件里同一映射的多条用例）。坐标映射记法：平移 (+dx, +dy) 指原 9×9 坐标整体平移到平坦 8×8 链接载体（`link:90008`）棋盘 0；"逐用例映射 / 四角映射"指按 `g1/migrate.py --map=auto|split|o:` 逐轴保住盘边身份。凡"关系断言是否不变"写"是"的行，断言结构与比较关系不变、只换载体与字面值。
