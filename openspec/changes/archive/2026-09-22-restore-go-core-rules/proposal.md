## Why

2026-09-21 以 `siege-rules-and-ai.md`（GDScript 原型的规则与 AI 整理稿，与设计文档 v1.0 同源）为基准，对 09-13 之后叠加的数值类 change 做了逐项裁决。结论是：这些 change 为了压平数值曲线与对局时长，把玩法从"围棋式抢地 + 数值爆炸"带偏了——领地不计分、倍率封顶、保护期免死、轮数兜底、落后补偿都在削弱围杀与抢地本身的分量。本 change 把计分、出局、终局、征募四块规则回归基准文档，对局时长改由地图规模与信物数量调节。

裁决记录（用户逐项确认）：

| # | 项 | 裁决 | 理由 |
|---|---|---|---|
| 1 | 棋串军势公式 | 回归文档 | 需要数值爆炸 |
| 2 | 总势力 = 独占空格 + 军势 | 回归文档 | 围棋的精髓 |
| 3 | 势力降 0 立即出局 | 回归文档 | 有出生区包含，保护期误伤问题不存在 |
| 4 | 只保留三类终局 | 回归文档 | 围棋的拉扯精髓；用网格数量、信物数量控制时长 |
| 5 | 地图 | 保持仓库 | 按生成算法来 |
| 6 | 匠人 / 地形 / 高地加值 | 保持仓库 | — |
| 7 | 征募固定 5 展示 / 3 免费 | 回归文档 | 落后就要挨打 |
| 13 | 分阶段基础部署上限 3→4→5 | 保持仓库 | — |
| 14 | 据点 | 整体移除 | 不计分后不留无功能概念 |
| 15 | 大回合上限 / 势力碾压 / 落后补偿 | 规范与代码都删 | — |

（#8 活棋禁入、#11 活形眼位算法 → 后续 change `life-shape`；#9、#10 AI → 后续 change `ai-eye`。）

对应设计文档 §4、§5.3、§7、§10、§11、§12、§16。

## What Changes

- **BREAKING** 棋串军势公式改为 `⌊(基础军势总和 + 位置加值) × 1.5^倍增子数量⌋`：倍率作用于基础军势与全部位置加值（连珠、协同、高地），取消倍率指数封顶。回退 `cap-multiplier`、`growth-pass-1` 的封顶部分、`multiplier-rebalance`。
- **BREAKING** 总势力改为 `独占空格数 + 所有己方棋串军势`：每个独占空格 1 分，争议格与中立格不计分，领地分不参与倍率。回退 `scoring-sites` 的计分部分。
- **BREAKING** 据点整体移除：删除 `site-control` 能力；地图数据、静态校验、人数适配预算、4 人基准地图、边疆档地图与种子生成器不再含据点，两张内置图标识递增为 `siege-4p-base-v5`、`siege-frontier-v2`（地形与信物格不变），同一 `gen:` 标识生成的图与此前不同；信息公开、结算顺序、战术信息层、据点地标、改造规则、批量跑局配置中的据点条款一并删除。
- **BREAKING** 出局判据改为"首次建立正势力后，任意结算势力降至 0 立即出局"，保护期不豁免；开局空盘 0 势力不算清零。删除「保护期暂停出局检查」「逐玩家的开局出局保护解除」。
- **BREAKING** 终局只保留三类：只剩一名参赛玩家 / 完整大回合全员 Pass / 盘面无可落子空格。删除「大回合上限终局」「势力碾压」及对局配置中对应的两个公开项。
- **BREAKING** 删除「落后者征募补偿」及对局配置开关；征募展示数 / 免费选取数只来自默认值与信物。排名的作用范围回到"信息展示 + 下一大回合行动顺序"。
- 终局并列判定链维持"信物控制数 → 独占空格数 → 棋子数"，其中独占空格数恢复为计分项的同一口径。
- 分阶段基础部署上限（3 / 4 / 5）**保留**，不在本 change 范围内。
- 批量跑局保留一个纯技术性的小回合数截断（防死循环），以独立结束原因记录，MUST NOT 产生名次，不属于游戏规则。
- 数值目标回归：保留指标项，删除据点、碾压、大回合上限、落后补偿相关的统计段；"对局时长"的调节手段在报告中改为指向地图可落子格数与信物格数。
- 设计文档 `2026-09-10-siege-core-gameplay-design-v1.md` 同步：撤回上述 change 在 §5.3、§9.2、§10、§11、§12 的增补，文末变更记录加一行。

## Capabilities

### New Capabilities
无。

### Modified Capabilities
- `power-score`: 「棋串军势公式」「总势力」「势力明细」「实时重算与公开排名」。
- `piece-effects`: 「倍增子的棋串倍率」「高地压制加值」中"不被倍率放大"的表述。
- `site-control`: 全部 Requirement 移除（能力退役）。
- `coverage-territory`: 「空格归属三态」恢复计分语义；「唯一覆盖查询」「弃赛玩家的遗留棋子仍产生覆盖」去掉据点。
- `elimination-endgame`: 移除保护期两条、大回合上限终局、势力碾压；修改「出局判定」「主动弃赛」「三类终局条件」「终局名次与并列判定」。
- `match-setup`: 移除三条对局配置公开项（大回合上限、碾压起始、落后补偿开关）。
- `recruitment`: 移除「落后者征募补偿」；「私人征募面板」的数值来源去掉补偿。
- `relic-effects`: 「效果快照在小回合开始时生成」「默认基础值」去掉名次读取与补偿。
- `initiative-order`: 「排名的作用范围」去掉补偿。
- `hand-info-panel`: 「公开结构参数与信物来源」去掉补偿来源。
- `turn-sequence`: 「大回合的定义与推进」去掉大回合上限的引用。
- `capture-resolution`: 「正式结算顺序」第 6 步去掉据点控制。
- `information-visibility`: 「始终公开的信息」去掉据点项，补上空格归属计分。
- `tactical-layers`: 「五种战术信息层」势力层改为显示领地分与棋串分。
- `visual-style-baseline`: 移除「据点地标」。
- `terrain-edit`: 「改造动作集」「改造不可逆且设施无归属」去掉据点。
- `map-definition`: 「地图为设计师固定的静态数据」「地图规格档」「人数适配预算」「4 人基准地图」「地图静态校验规则」「边疆档基准地图」去掉据点。
- `map-generation`: 「布局规则」去掉据点布置；「地图种子与确定性」内置图标识改名。
- `ai-decision`、`map-selection`、`viewport-camera`: 仅内置图标识改名（v4 → v5、frontier-v1 → frontier-v2），行为不变：「候选格上限」「开局选图界面」「推屏与平移」「回到出生平台」「全局预览」「动态相机下的拾取正确」「悬停格坐标读数」。
- `simulation-harness`: 「批量跑局」去掉据点分值配置，加入技术性截断；「各入口按地图标识选图」缺省图改为 v5。
- `match-telemetry`: 「对局日志的记录内容」「数值目标回归」「平衡分析方向」去掉据点 / 碾压 / 上限 / 补偿统计。

## Impact

- **前置 change**：`frontier-map` 与 `map-generator`（均已于 2026-09-21 归档；本 change 的地图类增量已按归档后的主规范逐条核对，差异只与据点和内置图标识有关）。
- **被回退的已归档 change**：`cap-multiplier`、`multiplier-rebalance`、`growth-pass-1`（仅倍率封顶部分）、`round-cap`、`dominance-victory`、`catch-up-recruit`、`scoring-sites`。`ai-safety-weight` 的校准数据因计分规则变化而失效，重新校准放在 `ai-eye`。
- **受影响代码**：`Siege.Core/Scoring/*`（`PowerCalculator`、`SiteControl`、`SiteValues`、`PowerSnapshot`、`PowerScoreboard`）、`Siege.Core/Board/*`（`SiteTier`、`SiteAttribution`、`MapData`、`MapFile`、`MapValidator`、`MapProfile`、各内置地图、`FrontierMapLayout.Sites.cs` 与生成器）、`Siege.Core/Match/*`（`MatchFlow`、`MatchOptions`、`FinalStandings`、`MatchPublicView`、存档）、`Siege.Core/Preview`、`Siege.Core/Ai`（`BatchEvaluator` 中的据点估值项，最小改动到能编译、能跑，调权留给 `ai-eye`）、`Siege.Presentation`（`SiteView`、层内容、标签）、`Siege.Sim`（配置、日志、分析、报告、控制台）、`src/godot/scripts`（据点地标、HUD）。
- **数据**：`maps/*.json` 去掉据点字段；旧存档不兼容，原型阶段不做迁移，加载时明确报错。
- **数值**：势力值不再有上限，倍率计算与势力值 MUST 使用不溢出的精确整数运算（见 design）。
- **既有测试**：据点、碾压、大回合上限、落后补偿、保护期免死的测试类整类删除；军势算例按新公式重算；`EvaluationWeights` 校准守门在 `ai-eye` 完成前标注为未校准口径。

## Non-goals

- 活形 / 眼位算法与活棋禁入 → `life-shape`。
- AI 并入眼位评估、停手阈值、权重重新校准 → `ai-eye`。
- 不改匠人、地形、气边、高地压制加值、分阶段基础部署上限、地图规格档与种子生成的布局逻辑（只摘除据点）。
- 不在本 change 内调地图尺寸与信物格数来校准对局时长——先跑出新规则下的基线，再单开数值 change。
- 不做旧存档、旧地图 JSON 的自动迁移。
