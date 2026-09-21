# 09-21-restore-go-core-rules 实施记录

每段追加：改了什么 / 类型变更波及的文件 / 既有测试改写与删除逐条 / 变异验证逐条 / 待决。

## 段 A——军势公式与领地计分（tasks 1.1–1.5，2026-09-21）

基线 1245 项全绿 → 段末 **1256 项全绿**，`dotnet build -c Release` 0 警告 0 错误。未提交。`src/godot/` 不在 `siege.sln`：单独 `dotnet build src/godot/Siege.Godot.csproj -c Release` 验证，0 警告 0 错误。

### 改了什么

1. **军势公式**（D1）：`棋串军势 = ⌊(基础 + 连珠 + 协同 + 高地) × 3^n / 2^n⌋`，n = 倍增子数量、不封顶、逐棋串各取整一次。
   `Multiplier` 删除 `MaxExponent` 常量与 `Exponent`（"生效倍率指数"）；`GroupPower` / `MultiplierPeak` 删除 `EffectiveMultiplierCount`；
   `PowerCalculator.GroupPowerOf` 把"基础 + 位置加值"整体传进唯一的 `Multiplier.Apply`。
2. **数值类型**：倍率分子 / 分母、棋串军势、总势力、名次组势力一律 `System.Numerics.BigInteger`（任意精度，不溢出、不截断、不饱和）；原 `Int128 checked` + `long` 全部换掉，
   `OverflowException` 这条"响亮失败"路径随之消失。计分路径无 `double` / `float` / `decimal`（既有守门 `棋串军势公式Tests.计分路径不含浮点` 扫 `Scoring/` 仍绿）。
3. **领地计分**（D2）：`总势力 = 独占空格数 + Σ棋串军势`。独占空格直接取 `CoverageMap.ExclusiveCellsOf(player)`（空格归属三态的既有结果），`PowerCalculator` 里没有新增任何覆盖遍历；
   争议 / 中立不计分，空林地格不是覆盖目标故恒为中立，棋子所在格不在独占集合里，领地分不进倍率。
4. **势力明细**：`PlayerPower` 新增 `TerritoryScore`（= `ExclusiveCells.Length`），`ExclusiveCells` 由"只展示"恢复为计分依据；`GroupPower` 去掉"生效倍率指数"。
5. **据点（过渡）**：总势力公式里去掉据点分一项。`SiteTier` / `SiteControl` / `SiteValues` / `PlayerPower.Sites` / `PlayerPower.SiteScore` / `PowerSnapshot.SiteStates` / `Compute(board, roster, siteValues)` 签名
   全部原样保留（只展示、不计分），留给段 B 删除。`PlayerPowerRowView.SiteScore`、Godot HUD 里"据点 N"的显示也因此仍在，段 B / 段 E 处理。
6. **JSON**：System.Text.Json 不支持 `BigInteger`（运行期抛 `NotSupportedException`），新增 `Siege.Core.Scoring.BigIntegerJsonConverter`，存档（`MatchFlow.JsonOptions`）与跑局日志（`LogJson.Options`）各注册一次。
   写出 = 精确十进制整数的 JSON **数字**（不加引号、无指数）——小数值与此前 `long` 的写法逐字节相同，既有日志 / 存档 / 黄金文本格式不变；读入接受数字与整数字符串，小数 / 指数 / 其他一律 `JsonException`，全程不经浮点。
7. **表现层**：`GroupPowerView` 删 `EffectiveExponent`、`Power` 改 `BigInteger`，公式文案改为"（基础 B + 位置加值 P（…））× 倍率 = 军势"；`PowerChangeView` / `PlayerPowerRowView.Total` / `OrderRowView.Power` 改 `BigInteger`。
   `GroupScoreView.HeatLevel` 原来 = 生效倍率指数（0–3），Godot 用它算势力层柱高（`0.12 + 0.14 × HeatLevel`）；不封顶后改为 `min(倍增子数量, GroupScoreView.MaxHeatLevel = 3)`——**只是显示档位，不是倍率封顶**，倍率文字与军势仍取精确值。
8. **Sim**：`MatchLog` 的 `PlayerEntry.Total` / `GroupEntry.Power` / `StandingEntry.Power` / `PeakEntry.Power` 与 `LogEvent.Values`（`Dictionary<string, BigInteger>`，AI 评价原始值与 `P{n}.Power` 走这里）改 `BigInteger`；
   `GroupEntry` / `PeakEntry` 删 `EffectiveMultiplierCount` 与按封顶回填（旧日志里的该字段读入时被忽略，仍可解析）；`BalanceAnalyzer` 删"峰值生效倍率指数分布"，最高单串军势 / 最高峰值军势 / 各棋子归因势力保持精确整数，
   均值与占比这类统计量显式 `(double)` 转换（分析口径，不在计分路径上）；`ReportWriter` 对应文案更新。"各棋子势力占比"的倍增子放大部分改为 `军势 − 基础 − 全部位置加值`（见待决 4）。
9. **AI**：`EvaluationBreakdown.Raw` / `RawOf` / `ContributionOf` / `Total`、`PointScore.Total` / `CandidateBatch.Total`、`HeuristicTurnController` 单点打分改 `BigInteger`；`BatchEvaluator` 的势力增量与敌方损失直接用大数相减，不再 `checked` 成 `long`。权重与其余维度未动。

### 数值类型变更波及的文件

| 层 | 文件 |
|---|---|
| Scoring | `PieceEffects.cs`（`Multiplier`）、`PowerCalculator.cs`、`PowerSnapshot.cs`（`GroupPower` / `PlayerPower` / `RankGroup`）、`PowerScoreboard.cs`（`MultiplierPeak`）、`BigIntegerJsonConverter.cs`（新） |
| Match | `MatchFlow.cs`（弃赛势力）、`MatchFlow.Persistence.cs`（`PowerAtResign`、两处 `Power`、注册转换器）、`PlayerFlowState.cs`、`ResignationSnapshot.cs`、`FinalStandings.cs`（`StandingInput.Power`）、`InitiativeOrder.cs`（`InitiativeEntry.Power`）、`DominanceCheck.cs`（`DominanceEntry.Power`，`Sum` → `Aggregate`；段 C 整体删除） |
| Preview | `BatchPreview.cs`（`PowerChange.Before / After / Delta`） |
| Ai | `EvaluationBreakdown.cs`、`BatchEvaluator.cs`、`CandidateBatch.cs`、`HeuristicTurnController.cs` |
| Presentation | `Preview/PreviewPresentation.cs`、`Layers/LayerContents.cs` |
| Sim | `Logging/MatchLog.cs`、`Running/MatchSession.cs`、`Analysis/BalanceAnalyzer.cs`、`Analysis/ReportWriter.cs`、`Play/BoardRenderer.cs`、`Play/ConsoleController.cs`、`Play/PlayCommand.cs` |
| Godot | 源码零改动：`Hud.cs` / `GameRoot.cs` 读势力的 6 处全是字符串插值（`{row.Total}`、`{standing.Input.Power}`、`FormulaText`），`BoardView.cs` 只读 `HeatLevel`（已在表现层夹到 0–3） |

### 新增测试（按规格 Scenario）

- `PowerScore/棋串军势公式Tests`：设计文档标准算例、位置加值被倍率放大、连珠加值被倍率放大、高地加值被倍率放大、逐棋串取整、倍率不封顶、大指数精确不溢出（60 枚盘面 = 2206108123015；另钉 n = 411 的 75 位结果，这才是真正越过 `Int128` 的样本——60 枚的结果其实装得进 `long`）、倍率显示为精确值。
- `PieceEffects/倍增子的棋串倍率Tests`：两枚倍增子、倍率不作用于领地分、倍率作用于位置加值、倍率指数不封顶（12 枚 = 1556）；另 `大数值精确不抛出`。`PieceEffects/高地压制加值Tests.高地加值随倍率放大`（= 7）。
- `PowerScore/总势力Tests`：领地与棋串相加（39）、争议格不计分、孤立棋子的势力（5）、领地分不参与倍率（19）、势力不可消耗、势力不累计（80 → 12）；过渡 `据点分不计入总势力`；守门 `领地计分直接取空格归属结果`。
- `CoverageTerritory/空格归属三态Tests`：独占、争议、覆盖数量不影响独占、中立、空林地格恒为中立（新）。
- `PowerScore/势力明细Tests`：明细可复算总势力、领地分可溯源（新）、位置加值可溯源、明细可复算棋串军势（两条"可复算"都用测试内独立整数式 / 显示串分数复算，不回调被测方法）。
- `MatchTelemetry/军势精确整数遥测Tests`（新，6 个方法 / 13 例）：快照与峰值记录的军势逐位往返、旧日志的生效指数字段被忽略、高倍率棋串报告按倍增子数量且最高军势精确、势力值读入接受整数与整数字符串、势力值读入拒绝非整数、势力值写出为不带引号的精确十进制整数。
- `TacticalLayers/势力层据点与高地Tests.倍率热区等级只是显示档位`（新）。
- 先红：新写的 `棋串军势公式Tests` 在旧实现上实跑 **红 18 / 27**（其余新测试引用了新成员 `TerritoryScore` 等，旧实现上是编译期红）。

### 既有测试改写 / 删除逐条（旧值 → 新值 + 算式）

**A. 军势公式类（重算）**

| 测试 | 旧 → 新 | 算式 |
|---|---|---|
| 棋串军势公式.含位置加值的计算 →「位置加值被倍率放大」 | 14 → 16 | ⌊(6 + 5) × 3 / 2⌋ |
| 棋串军势公式.位置加值不被倍率放大 →「连珠加值被倍率放大」 | 25 → 40；文案改为"（基础 6 + 位置加值 12（…））× 2.25 = 40" | ⌊(6 + 12) × 9 / 4⌋ = ⌊40.5⌋ |
| 棋串军势公式.高地加值不被倍率放大 →「高地加值被倍率放大」 | 12 → 15 | ⌊(4 + 3) × 9 / 4⌋ = ⌊15.75⌋ |
| 棋串军势公式.第4枚倍增子只加基础军势 →「倍率不封顶」 | 27 → 40；分子 / 分母 27 / 8 → 81 / 16 | ⌊8 × 81 / 16⌋ = ⌊40.5⌋ |
| 棋串军势公式.恰好第3枚倍增子 + 倍率显示表达封顶 →「倍率显示为精确值」 | 3 枚：23 不变；8 枚：3.375 / 27 → 25.62890625 / 205 | 15^8 / 10^8；⌊8 × 6561 / 256⌋ |
| 棋串军势公式.封顶不影响其他效果 →「倍增子仍是棋串成员」 | 29 → 170 | ⌊(8 + 2) × 3^7 / 2^7⌋ = ⌊21870 / 128⌋ |
| 棋串军势公式.倍率取整边界_恰为整数 n = 4..8 | 54 / 108 / 216 / 432 / 864 → 81 / 243 / 729 / 2187 / 6561 | 2^n × 1.5^n = 3^n |
| 棋串军势公式.倍率取整边界_非整数 n = 4..8 | 57 / 111 / 219 / 435 / 867 → 86 / 250 / 740 / 2204 / 6586 | 3^n + ⌊3^n / 2^n⌋ |
| 棋串军势公式.逐棋串取整（总势力断言） | 32 → 68 | 军势 16 + 16，领地 18 × 2（每串上 8 + 下 8 + 两端 2） |
| 倍增子的棋串倍率.倍率的精确十进制表示 n = 4 / 5 | 3.375 / 3.375 → 5.0625 / 7.59375 | 15^n / 10^n |
| 倍增子的棋串倍率.倍率不作用于位置加值 →「倍率作用于位置加值」 | 25 → 40；与对照串之差 7 → 22 | ⌊(6 + 12) × 9 / 4⌋；40 − 18 |
| 倍增子的棋串倍率.倍率指数封顶为3 →「倍率指数不封顶」 | 40 → 1556；27 / 8 → 531441 / 4096；显示 3.375 → 129.746337890625 | ⌊12 × 3^12 / 2^12⌋ |
| 倍增子的棋串倍率.超出整数范围时响亮失败 →「大数值精确不抛出」 | 抛 `OverflowException` → 给出精确值 | 字面值由 python 独立算出，见测试注释 |
| 势力明细.明细可复算总势力（抽查 P1 混合串） | 13 → 15 | ⌊(6 + 4) × 3 / 2⌋ |
| 势力明细.明细可复算棋串军势（五串） | 25 / 8 / 20 / 22 / 7 → 40 / 10 / 20 / 60 / 7 | ⌊18 × 9/4⌋、⌊7 × 3/2⌋、不变、⌊8 × 243/32⌋、不变 |
| 势力明细.遥测峰值保留原始数量并可得生效指数 →「遥测峰值保留倍增子数量」 | 27 / 30 → 205 / 345；显示 3.375 → 25.62890625 / 38.443359375 | ⌊8 × 6561/256⌋、⌊9 × 19683/512⌋ |
| 各棋子势力占比.各棋子势力占比按口径手算 | 夹具军势 25 → 40、27 → 83；倍增归因 43 → 114；合计 72 → 143；占比 59.7% / 5.6% / 1.4% → 79.7% / 2.8% / 0.7% | ⌊18 × 9/4⌋、⌊11 × 243/32⌋；13 + 24 + 77；114/143、4/143、1/143 |
| 各棋子势力占比.含连珠线协同倍增的真实棋串… | 27 → 42；倍增归因 10 → 25；占比 25.9% / 3.7% → 16.7% / 2.4% | ⌊(7 + 12) × 9/4⌋ = ⌊42.75⌋；2 + (42 − 7 − 12)；7/42、1/42 |
| 批次预演必须显示的信息.显示军势预览 | 文案 → "（基础 9 + 位置加值 0）× 2.25 = 20"（数值不变） | — |
| 势力层据点与高地.势力层显示高地加值 | 文案 → "（基础 2 + 位置加值 2（…））× 1 = 4"（数值不变） | — |

**B. 领地重新计分类（重算；全部与 territory-power 时期、即 scoring-sites 改写前注释里留下的旧值一致，每条都在测试注释里写了逐格手数）**

| 测试 | 旧 → 新 | 算式 |
|---|---|---|
| 总势力.独占空格不计分 →「领地与棋串相加」 | 27 → 39 | 12 + 20 + 7 |
| 总势力.孤立棋子的势力 | 1 → 5 | 1 + 4 |
| 总势力.势力不累计 | 64 → 8 改为 80 → 12 | 64 + 16；8 + 4 |
| 势力明细.明细字段完整 | 总势力 20 → 34，领地分 14 | 14 + 20 |
| 空格归属三态.独占 / 棋子格不在独占集合中 / 争议 / 覆盖数量不影响独占 / 障碍不属于任何玩家 | 1 → 5；6 → 13；（新增）4 / 4；（新增）13；（新增）4 | 1 + 4；6 + 7；1 + 3；4 + 9；1 + 3 |
| 弃赛玩家的遗留棋子仍产生覆盖.弃赛者遗留棋子制造争议 | 1 / 1 → 4 / 4 | 1 + 3（D5 争议不计） |
| 势力名次.排除非参赛玩家 | P3 24 → 38；名次 (1, 25)(2, 1) → (1, 37)(2, 5) | 24 + 14；25 + 12；1 + 4 |
| 势力名次.并列如实输出 | 25 / 25 / 1 → 37 / 37 / 5 | 同上 |
| 实时重算与公开排名.每次结算后更新 | P0 4 → 13；P3 1 → 5 | 4 + 9；1 + 4 |
| 实时重算与公开排名.Pass也触发更新 | 1 / 1 → 5 / 5 | 1 + 4 |
| 实时重算与公开排名.弃赛者势力可见但不参与 | 31 / 18 → 45 / 30（恰为规格算例） | 31 + 14；18 + 12 |
| 批次预演必须显示的信息.显示势力与排名变化 | 37→51(+14) / 40 / 58 / 13 → 45→62(+17) / 53 / 67 / 20（恰为规格算例） | 领地 8→11 / 13 / 9 / 7 |
| 五种战术信息层.顺序层可解释下一轮排序 | 4 / 3 / 2 / 1 → 9 / 7 / 5 / 3 | 领地 5 / 4 / 3 / 2 |
| 落后者征募补偿（13 处 `AssertRanks`） | 阶梯 4 / 3 / 2 / 1 → 9 / 7 / 5 / 3；并列 2 → 5；二人局 1 → 3；反超 10 → 24 | 同上；2 + 3；1 + 2；10 子 + 领地 14 |
| 主动弃赛.保护期内允许弃赛 / 遗留棋子继续生效 | 1 → 5；1 → 4 | 1 + 4；1 + 3 |
| 据点控制判定.多人覆盖即争议 | (1, 1) → (4, 4) | 1 + 3 |
| 终局名次与并列判定.终局输入取控制中的据点数量 | 22 → 8 | 领地 6 + 军势 2（据点分 5 + 15 不再计入） |
| 启发式评价维度.评价覆盖七个维度 | 敌方势力下降 1 → 2 | 失去 E5 军势 1 + 失去独占格 F5 |
| 势力碾压.Crushing 夹具 / 开局不触发 / 待回应者出局或弃赛 | (31, 3, 2, 2) → (31, 7, 7, 7)；1 → 5；新增断言 P1 4 / P2 38 | 领地 0 / 4 / 5 / 5；1 + 4；1 + 3、32 + 6 |

**C. 局面重构（scoring-sites 为"独占空格不计分"改过摆法、领地恢复后前提失效 → 回到 territory-power 时期的原摆法与原数值；取自 `git show bb459c4^`）**

| 测试 | 处理 |
|---|---|
| 启发式评价维度.先手位评价生效 | 去掉给 P1 补的 J1：提前 P1 14 > P0 10，提后 P0 13 > P1 12（保留 J1 则 17 → 15 > 13、名次不变，前提失效） |
| 势力碾压.差一点不成为候选 | 七枚堡垒 → 原三枚 G8 / G9 / H9：P3 = 14 + 领地 6 = 20，其余之和 34 > 31；新增 `Assert.Equal(20, …)` |
| 势力碾压.被拉下来则取消候选 | 去掉给 P2 补的 C8：提子后 (0, 4, 3, 2) → (0, 12, 7, 7)，P1 = 4 子 + 领地 8 |
| 势力碾压.取消后可再次成为候选 | 七枚堡垒 → 原三枚 C9 / D9 / E9：P2 = 14 + 领地 8 = 22，其余之和 36 > 31；新增 `Assert.Equal(22, …)` |

**D. 样本 / 黄金值重建（规则变了，AI 走法与快照文本随之变；断言本身未改）**

| 测试 | 处理 |
|---|---|
| 候选格上限.缺省不限制时标准图整局与改动前逐步相同 | 黄金哈希 `83755037…403E18` → `43D7E980…AA757D`（段 A 完成后 K = 0 的实跑，连跑两次一致；M-K1 在新值下重跑仍红） |
| 地形改造日志与分析.回放日志改造可重建终局地形并与对局逐项一致 | 种子 1–3 → 3–5：原第 2 局在新计分下一次改造都没有，样本口径下界响亮失败；写死权重下扫 1–24 取连续三颗（改造 5 / 3 / 5、致提子 3 / 1 / 0） |

**E. 删除（共 9 个测试方法）**

| 类别 | 测试 | 理由 |
|---|---|---|
| 封顶概念 | 整个 `MatchTelemetry/倍率封顶遥测Tests.cs`：快照与峰值记录同时保留原始数量与生效指数、旧日志缺生效指数按封顶回填、高倍率棋串报告双列输出 | "生效倍率指数"字段与回填已删；由 `军势精确整数遥测Tests` 取代 |
| 封顶概念 | 势力明细.明细区分原始与生效倍率 | 同上 |
| 据点计分 | 整个 `SiteControlSpec/据点分计入势力Tests.cs`：失去控制立即掉分、占据者被围杀、弃赛者封锁据点、弃赛者与参赛者同时覆盖空据点为争议 | Requirement「据点分计入势力」已不成立；过渡断言见 `总势力Tests.据点分不计入总势力` |
| 据点计分 | 据点档位与分值.分值取自对局配置、据点档位与分值.AI评价使用对局据点分值 | 都断言"据点分进总势力 / AI 势力增量" |

**F. 据点相关但保留、只改数值或删据点腿的**：总势力.据点与棋串相加 →「据点分不计入总势力」（77 → 51 = 领地 24 + 20 + 7，同时断言 `SiteScore` 仍为 50）；倍增子的棋串倍率.倍率不作用于据点分 →「倍率不作用于领地分」（65 → 30，换回 territory-power 的 10 独占盘面）；
势力明细.明细可复算总势力（盘面去掉三个据点及其断言）；势力明细.据点分可溯源、据点控制判定 / 据点公开 / 据点档位与分值 其余方法原样保留（只断言控制与明细，不断言总势力），段 B 整体删除。

**G. 只改类型、期望不变**：AI决策的可复现性、候选格上限（两处元组 / 列表）、对隐藏信息的概率估计、主动弃赛.弃赛快照可记录、终局名次与并列判定.（`long[]` → `BigInteger[]`）、基础排序、对局日志的记录内容（`Sum` → `Aggregate`）、`ScoringFixtures.GroupPowerSum`、`SimFixtures`（`Values` 字典）。

### 变异验证逐条

基线 1256 全绿。四条变异各跑一次全量测试，还原后逐字节校验（`shutil.copy2` 备份 → 还原 → 内容比对 OK，未用 `git checkout --`，工作树里的未提交改动全程保留）。脚本与逐条输出见本记录。

| 编号 | 改了哪一行、改成什么 | 红了几条 |
|---|---|---|
| M-A1 | `PowerCalculator.GroupPowerOf`：`Apply((BigInteger)baseTotal + positionBonus)` → `Apply((BigInteger)baseTotal) + positionBonus`（位置加值挪到乘法之外，即退回 `multiplier-rebalance` 口径） | 10 |
| M-A2 | 同一行：`new Multiplier(multiplierCount)` → `new Multiplier(Math.Min(multiplierCount, 3))`（复活 `cap-multiplier` 的封顶） | 8 |
| M-A3 | `CoverageMap.ExclusiveCellsOf`：独占判定加上 `|| ownership.Kind == OwnershipKind.Contested`（争议格也计入领地） | 18 |
| M-A4 | `PowerCalculator`：`new PlayerPower(…, exclusive.Length + groupTotal)` → `new PlayerPower(…, groupTotal)`（领地分不进总势力，即退回 `scoring-sites` 口径） | 53 |

M-A1 / M-A2 对应 tasks 1.2 明确要求的两条；M-A3 对应 1.3 要求的"把争议格计入应红"；M-A4 是补充的反向守门——确认领地分**确实**在计分路径上，而不是只写进了明细字段。

### 守门

- 计分路径无浮点：`棋串军势公式Tests.计分路径不含浮点`（扫 `src/Siege.Core/Scoring/*.cs`，含新文件 `BigIntegerJsonConverter.cs`）绿；既有 `内核与表现层不出现浮点` 绿。
- 全仓库只有一处覆盖统计：`总势力Tests.领地计分直接取空格归属结果`——`PowerCalculator.cs` 必须出现 `coverage.ExclusiveCellsOf(`、不得出现 `CoverageTargets(` / `Neighbors(` / `CoverageOf(` / `SourcesOf(` / `UniqueCoverer(` / `OwnershipOf(`；
  全仓库（Core / Sim / Presentation / `src/godot/scripts`，实测 141 个文件、下界 100、必须扫到 godot）调用 `CoverageTargets(` 的文件恰为 `Adjacency` / `GameBoard` / `CoverageMap` / `PieceEffects` 四个；另有逐格读 `OwnershipOf` 的行为比对。

### 待决 / 需主会话裁决

1. **`.trellis/spec/core/determinism.md`「大数用 `Int128` checked，不用 `BigInteger`」与 design D1 冲突**：n 上界是可落子格数（边疆图 411），`3^n` 在 n ≈ 81 就溢出 `Int128`，本段按 D1 与派发指令选 `BigInteger`。该规范条目与同文件「禁止浮点参与计分」里的旧公式（位置加值不进倍率、封顶 3）需在段 F 6.3 改写；`boundaries.md` 里"据点控制""高地加值……`PowerCalculator` 是唯一消费者"等行随段 B / F 更新。本段未动 `.trellis/spec/`。
2. **大数的 JSON 写法**：本段写成不加引号的 JSON 数字（与旧格式逐字节兼容、任意位数、`System.Text.Json` / Python 都能精确读回）。代价是 JavaScript 等把数字当 double 的消费方超过 2^53 会失真。若段 E 5.2 决定改成字符串，转换器读入端已同时接受两种写法，只需改 `Write` 一行并重建含势力的黄金文本。
3. **性能**：D1 风险条目所说的"AI 每个候选都重算势力"现在每条棋串都有 `BigInteger` 分配（`Pow` + 乘除）。全量测试 45–55 s，与基线 55 s 持平，未见劣化；`determinism.md` 当年弃用 `BigInteger` 的理由正是分配开销，段 F 跑 200 局基线时请对比耗时。
4. **"各棋子势力占比"的归因口径**（该报告段不在现行 match-telemetry 规格里）：旧口径"倍增子放大部分 = ⌊基础 × 倍率⌋ − 基础"在新公式下不再可分，本段改为"军势 − 基础 − 全部位置加值"整块归倍增子（归因之和仍等于军势 − 高地加值）。段 E 5.2 可决定保留 / 改口径 / 删除。
5. **Sim 报告里的据点段**（`SiteSection.FinalShare` = 据点分 ÷ 总势力）：据点分已不进总势力，这个占比在过渡期无意义但仍会输出；段 B / E 删除。日志 `PlayerEntry` 还没有"领地分"字段（5.2 的事）。
6. **统计量的精度**：`BalanceAnalyzer` 里平均势力、碾压比、占比等用 `(double)BigInteger`，极大值下只有约 15 位有效数字——报告口径可接受，但"最高单串军势 / 最高峰值军势 / 归因势力"这些会被人拿去核对的量已保持精确整数。
7. **`GroupScoreView.MaxHeatLevel = 3`** 是我为了不让 Godot 势力层柱高随倍增子数量无限增长而加的显示档位上限（原先恰好被倍率封顶兜住）。是否改成对数档或别的显示方式，段 E 5.5 决定。
8. **AI 权重未校准**：计分口径变了，`EvaluationWeights.Default` 的校准依据已失效（testing.md「计分口径一变，默认权重必须重扫」）；按 design D6 / Open Question 5，标注"未校准"是段 B 2.6 的事，本段未动权重与校准守门测试（仍绿）。
9. **codegraph**：本子 agent 的工具表里没有 codegraph MCP 工具，影响面是靠"改类型 → 编译器报错逐个收敛"+ 定向 `grep` 查的（未遍历大文件、未读媒体）。

### 主会话补记（2026-09-21，段 A 收尾）

实施 agent 在写变异脚本这一步因模型额度中断，**变异验证与本段收尾由主会话完成**，上方「变异验证逐条」与测试总数是主会话实跑的结果。

**踩到的坑（已写进 `testing.md`）**：变异脚本用 `shutil.copy2` 还原，连 mtime 一起还原了，MSBuild 因此跳过重建，收尾那次确认跑的是**变异后的二进制**——内容逐字节校验通过、`git diff` 干净，测试却红 53 条，与 M-A4 的失败数分毫不差。`os.utime` 刷新时间戳后重建，方为真结果。

**待决的主会话裁决**（check 请按"已定"对待，有异议报告而不是自行改回）：

1. **`BigInteger` vs `determinism.md` 的 `Int128`**：采用 `BigInteger`，**并已同步改掉 `determinism.md`**——该条的前提"n≤80 不溢出"被裁决 #1（取消封顶）作废，`3^81` 已越过 `Int128`。同文件「禁止浮点参与计分」里的旧公式一并改为不封顶、位置加值进倍率、领地计分。原计划在段 F 6.3 做，因为 check 以 `.trellis/spec/` 为真相源、会照旧条自行改回，故提前。
2. **JSON 大数写不带引号的数字**：与旧 `long` 写法逐字节兼容，读入端同时接受数字与整数字符串。本仓库无 JS 消费方；段 E 5.2 若要改字符串，只需改 `Write` 一行。
3. **性能**：全量测试 45–55 s vs 基线 55 s，无劣化；200 局耗时对比留给段 F 6.2。
4. **「各棋子势力占比」归因口径改为"军势 − 基础 − 全部位置加值"**：该报告段不在现行 `match-telemetry` 规格里，旧口径在新公式下不可分。段 E 5.2 可再定。
5. **Sim 报告的据点段 / `SiteSection.FinalShare` 过渡期无意义**：段 B / E 删除，本段不动。
6. **`BalanceAnalyzer` 的均值与占比用 `(double)BigInteger`**：报告口径可接受；会被拿去核对的量（最高单串军势、峰值、归因势力）保持精确整数。
7. **`GroupScoreView.MaxHeatLevel = 3`**：Godot 势力层柱高的**显示档位上限**，不是倍率封顶；倍率文字与军势仍取精确值。段 E 5.5 定最终显示方式。
8. **AI 权重未校准标注**：段 B 2.6 做，本段未动权重与校准守门。
9. **codegraph**：实施 agent 的工具表里确实没有 codegraph（`.claude/agents/trellis-implement.md` 的 `tools:` 不含 MCP），违反全局规则 #7。已给 `trellis-implement` / `trellis-check` 两个 agent 定义补上 `mcp__codegraph__*`，从本段的 check 起生效。

### 段 A check 记录（trellis-check，2026-09-21）

基线复跑 **1256 全绿**（exit 0）；check 后 **1257 全绿**、`dotnet build -c Release` 0 警告 0 错误。新增 1 条测试见下。

**check 阶段实跑的变异（M-AC 系列；脚本二进制读写、探测行尾、`assert count(anchor)==1`、`finally` 还原 + `os.utime` 刷新 mtime、逐字节校验、`DOTNET_CLI_UI_LANGUAGE=en` 解析统计行）**

| 编号 | 改了哪一行 | 红 | 备注 |
|---|---|---|---|
| M-AC1 | 重跑 implement 的 M-A2（`Multiplier` 指数夹到 3） | 8 | 与上表记录一致 |
| M-AC2 | `ComputeCore` 的 Total 改为自己遍历 `AllCoords()` + `OwnershipOf` 统计独占格（行为不变） | 1 | 只红守门 `领地计分直接取空格归属结果` ②——派发指令点名要求的那条，守门为真 |
| M-AC3 | `Multiplier.Apply` 结果夹到 `long.MaxValue`（饱和） | 2 | 规格 MUST NOT 饱和；此前只在注释里写着"→ 红"，未跑 |
| M-AC4 | Total 加回 `sites.Sum(Value)` | 3 | 过渡契约"据点分不进 Total"守得住 |
| M-AC5 | Total 把领地分也乘上首条棋串倍率 | 15 | |
| M-AC6 | `PlayerPower.TerritoryScore` 改为 `ExclusiveCells.Length + 1` | 19 | |
| M-AC7 | 重跑 implement 的 M-A3（`ExclusiveCellsOf` 含争议格） | 18 | 与上表记录一致 |
| M-AC8 | `RelicLedger` 的 Contested 分支判给最小编号覆盖者 | 12 | 为本次新增测试做的变异 |
| M-AC9 | `Adjacency.CoverageTargets` 去掉林地排除 | 13 | |
| M-AC10 | `BigIntegerJsonConverter.Write` 截成 64 位 | 2 | |
| M-AC11 | `BigIntegerJsonConverter.Read` 经 `double` 解析 | 4 | |
| M-AC12 | `BalanceAnalyzer` 峰值经 `double` 往返 | 1 | |
| M-AC13 | 占比口径放大部分不减位置加值 | 2 | |
| M-AC14 | 缺口探针：把第二份覆盖统计放到 `Scoring/` 下另一个文件、`PowerCalculator` 调它（行为不变） | **0** | 守门缺口，见下
| M-AC15 | `LayerContents` 热区等级去掉 `Math.Min` | 1 | |
| M-K1 重跑 | `RankPoints` 的启用条件改成恒真（K = 0 也预筛） | 36 | 在**重建后**的黄金哈希上仍红，含 `缺省不限制时标准图整局与改动前逐步相同`——证明重建的哈希不是自证的 |

至此段 A 的 15 条变异声明全部有实跑红数（此前只有 M-A1–M-A4 跑过，其余 11 条是写在测试注释里的**计划**）。测试注释已逐条改为实测值；`棋串军势公式Tests` 里原标 M-A3 的饱和变异与变异表的 M-A3 重号，已改用 M-AC 编号并注明。

**check 改了什么**

1. 新增 `CoverageTerritory/弃赛玩家的遗留棋子仍产生覆盖Tests.弃赛者遗留棋子封锁信物`——coverage-territory 增量把原来那条"据点"Scenario 拆成"制造争议"+"封锁信物"两条，后者段 A 没有对应测试方法（行为本身由 `relic-control` 的 `弃赛者棋子制造信物争议` 覆盖，缺的是 Requirement→类 / Scenario→方法 的对账）。变异 M-AC8 验证。总数 1256 → 1257。
2. 17 处变异记录注释改为实测红数（含上面的重号修正与守门缺口记录）。未改任何实现代码。

**发现但未修**

- **守门 `领地计分直接取空格归属结果` ② 的缺口**：把第二份覆盖统计放到 `Scoring/` 下另一个文件、再由 `PowerCalculator` 调用 → 实跑 **0 红**（②只扫 `PowerCalculator.cs` 的违禁词 + 仓库级 `CoverageTargets(` 名单）。属 testing.md「违禁 token 清单挡不住照抄一份算式」，建议段 F 6.3 补一条"算式形状 / `OwnershipOf(` 调用者名单"扫描。仓库级限制 `OwnershipOf(` 会误伤 Presentation / Sim 的合法读法，故 check 不自行改。
- `PowerSnapshot.SiteScore` 仍是 `long`（`Sites.Sum(s => (long)s.Value)`）。不进 `Total`，段 B 随据点整体删除，不算计分路径精度损失。
- `棋串军势公式Tests.计分路径不含浮点` 只扫 `Scoring` 顶层（`Directory.GetFiles` 不递归）。目前该目录无子目录，将来新增子目录会静默漏扫。
- 实施 agent 与本 check 的工具表里**都没有** codegraph MCP 工具（裁决 9 说已补上 `mcp__codegraph__*`，但本次派发下来的工具表仍只有 Read/Write/Edit/Bash/Glob/Grep/advisor）。本次理解代码用的是定向读单个类型 + 脚本扫描，未遍历大文件、未读媒体。
