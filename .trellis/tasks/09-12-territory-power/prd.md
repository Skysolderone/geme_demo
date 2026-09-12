# 09-12-territory-power

> 规格权威：`openspec/changes/add-territory-power/`（proposal + specs + design）。本文件不复制规格原文，只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §7.1–7.2, §9, §10

## Goal

势力值是《围杀 Siege》的唯一记分轴：它决定排名、决定下一大回合的先手、决定终局名次。它同时也是"构筑爆发"的落点——连珠、倍增、协同三种棋子把盘面结构翻译成数值。设计文档 §10 把公式写死了，但它依赖两个尚不存在的前置层：覆盖产生的领地归属（§7），以及棋子效果产生的位置加值与倍率（§9）。

这一层必须做对两件事：**领地分绝不参与棋串倍率**，以及**所有效果随盘面实时重算、不保留成长层数**。任一条做错，数值曲线会立刻偏离 §16 的目标区间。

本 change 对应设计文档 §7.1（占据优先）、§7.2（空格归属）、§9（棋子与征募权重中的棋子效果部分）、§10（势力值与排名）。

## Background / 确认事实

- 玩法设计已于 v1.0 确认并冻结，本任务不重新设计玩法，只把已确认规则落成可执行、可测试的实现。
- 规格保持实现无关：`openspec/` 下的 spec 不指定语言、引擎、框架；技术栈决策属于本 trellis 任务层。
- 该 change 对应的 37 条设计歧义已全部裁决，记录在 `openspec/changes/add-territory-power/design.md` 的「裁决记录（已确认）」一节，实现时 MUST 遵循。
- 全局硬约束见 `openspec/ROADMAP.md`：单一四邻接、一切实时重算、随机可复现、围棋记法坐标。

## Requirements

- 实现覆盖：棋子向四邻接相邻格提供覆盖；占据优先于覆盖，敌方覆盖不能改变已占据格的归属。
- 实现空格归属三态：独占（仅一名玩家覆盖）、争议（多名玩家覆盖）、中立（无人覆盖）；障碍不属于任何玩家也不计分。
- 实现五种棋子的军势与效果：普通子（1）、堡垒子（4）、连珠子（成线位置加值 `L×(L−1)`）、倍增子（棋串军势依次 ×1.5）、协同子（按同串其他类型数每种 +2）。
- 实现棋串军势公式：`⌊(基础军势总和 + 位置加值) × 1.5^倍增子数量⌋`。
- 实现总势力：`独占空格数 + 所有己方棋串的军势值`；领地分不参与倍率；棋子所在格不重复计作领地。
- 实现实时重算与公开排名更新；势力不可消耗、不累计历史积分。
- 提供势力明细（领地分 / 各棋串的基础军势、位置加值、倍率、最终军势），供 UI 与遥测使用。

## Acceptance Criteria

每条对应 `openspec/changes/add-territory-power/specs/` 中的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **coverage-territory** / 棋子向四邻接相邻格提供覆盖
- [ ] **coverage-territory** / 占据优先于覆盖
- [ ] **coverage-territory** / 空格归属三态
- [ ] **coverage-territory** / 领地分
- [ ] **coverage-territory** / 唯一覆盖查询
- [ ] **coverage-territory** / 弃赛玩家的遗留棋子仍产生覆盖
- [ ] **piece-effects** / 五种原型棋子的基础军势
- [ ] **piece-effects** / 连珠子的位置加值
- [ ] **piece-effects** / 协同子的位置加值
- [ ] **piece-effects** / 倍增子的棋串倍率
- [ ] **piece-effects** / 效果随盘面实时重算
- [ ] **power-score** / 棋串军势公式
- [ ] **power-score** / 总势力
- [ ] **power-score** / 势力明细
- [ ] **power-score** / 实时重算与公开排名
- [ ] **power-score** / 势力名次

- [ ] `openspec validate add-territory-power` 通过
- [ ] `openspec/changes/add-territory-power/tasks.md` 对应的实现清单（见 `implement.md`）全部完成
- [ ] 新增测试全部通过，且已纳入持续回归

### 能力覆盖

- `coverage-territory` — 6 条 Requirement / 13 个 Scenario：`openspec/changes/add-territory-power/specs/coverage-territory/spec.md`
- `piece-effects` — 5 条 Requirement / 14 个 Scenario：`openspec/changes/add-territory-power/specs/piece-effects/spec.md`
- `power-score` — 5 条 Requirement / 13 个 Scenario：`openspec/changes/add-territory-power/specs/power-score/spec.md`

## Dependencies

前置任务：`09-12-board-core`

- 前置 change：`add-board-core`（棋串、气、四邻接）、`add-batch-deployment`（结算顺序第 5 步在此调用）。
- 下游依赖：`add-relic-system`（信物控制依赖唯一覆盖判定）、`add-match-flow`（势力名次 → 先手值；终局按势力排名）、`add-heuristic-ai`（即时势力增量、敌方势力损失、组合成长评价）、`add-tactical-ui`（领地层、势力层、势力明细面板）。
- 势力明细结构会被遥测（§17）逐棋串记录，需在本 change 定为稳定结构。

## Out of Scope

- 不实现信物的发现、控制归属与效果 → `add-relic-system`；本 change 只提供"唯一覆盖某格的玩家"这一查询供其消费。
- 不实现先手值与行动顺序生成 → `add-match-flow`；本 change 只提供势力值与名次。
- 不实现棋子的征募权重与流派徽记调权 → `add-recruit-hand`；本 change 只负责棋子在盘面上的效果。
- 不实现任何永久成长、击杀分或历史积分（设计文档 §18.2 明确排除）。
- 不实现势力层的可视化 → `add-tactical-ui`。
