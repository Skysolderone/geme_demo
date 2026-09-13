# 09-12-relic-system

> 规格权威：`openspec/changes/add-relic-system/`（proposal + specs + design）。本文件不复制规格原文，只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §5.1, §7.3, §8

## Goal

信物是《围杀 Siege》把"空间博弈"翻译成"构筑成长"的唯一通道：展示更多、拿得更多、装得更多、一次落得更多、行动更早。它也是地图上唯一的随机源——地形与信物格位置固定，只有信物内容由对局种子生成，因此"未知格把路线变成选择题"。

这一层有三个容易做错的地方：**揭示 ≠ 授予**（第一次被覆盖就永久公开，但只有控制才生效）、**快照时机**（本小回合新占的信物不改变已生成的参数）、以及**先锋的读取时机与其他信物不同**（大回合结束时读取，而非小回合开始时）。

本 change 对应设计文档 §5.1（信物快照）、§7.3（信物发现与控制）、§8（信物系统）。

## Background / 确认事实

- 玩法设计已于 v1.0 确认并冻结，本任务不重新设计玩法，只把已确认规则落成可执行、可测试的实现。
- 规格保持实现无关：`openspec/` 下的 spec 不指定语言、引擎、框架；技术栈决策属于本 trellis 任务层。
- 该 change 对应的 37 条设计歧义已全部裁决，记录在 `openspec/changes/add-relic-system/design.md` 的「裁决记录（已确认）」一节，实现时 MUST 遵循。
- 全局硬约束见 `openspec/ROADMAP.md`：单一四邻接、一切实时重算、随机可复现、围棋记法坐标。

## Requirements

- 实现信物的一次性种子生成：开局按地图的强度预算分区与权重表生成全部信物内容，之后不刷新、不迁移。
- 实现出生区与公共争夺区两套权重表，公共区约 20% 的结构信物升级为效果 +2；出生区不生成 +2 高阶信物。
- 实现"随机类型 + 区域强度预算"的生成过程，保证各出生区总稀有度接近而具体组合仍随机。
- 实现发现与揭示：信物格首次进入任意玩家覆盖范围时，内容向所有玩家永久公开；即使该格同时成为争议格也照常揭示。
- 实现控制判定：直接占据该格，或唯一覆盖该格；争议信物不向任何玩家提供效果。
- 实现六类信物效果（探勘 / 征召 / 兵站 / 军令 / 先锋 / 流派徽记）与同类叠加规则，不设统一数值硬上限。
- 实现效果快照：小回合开始时读取非先锋信物，生成本回合的展示数、免费选取数、手牌类型槽与部署上限；本小回合新占信物从下一小回合起生效。
- 实现先锋的独立读取时机：仅在大回合结束、生成下一轮顺序时读取。
- 实现弃赛玩家遗留棋子仍可控制或封锁信物，但弃赛者不再获得任何效果收益。

## Acceptance Criteria

每条对应 `openspec/changes/add-relic-system/specs/` 中的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **relic-control** / 信物内容在首次被覆盖时永久公开
- [ ] **relic-control** / 揭示不授予收益
- [ ] **relic-control** / 信物控制判定
- [ ] **relic-control** / 控制权在每次结算后重算
- [ ] **relic-control** / 弃赛玩家的遗留棋子仍影响信物
- [ ] **relic-effects** / 六类原型信物的效果
- [ ] **relic-effects** / 同类信物叠加且无统一硬上限
- [ ] **relic-effects** / 效果快照在小回合开始时生成
- [ ] **relic-effects** / 先锋的独立读取时机
- [ ] **relic-effects** / 默认基础值
- [ ] **relic-effects** / 兵站丢失导致的槽位缩水
- [ ] **relic-effects** / 争议与失控信物不提供效果
- [ ] **relic-generation** / 信物在开局一次性生成
- [ ] **relic-generation** / 出生区信物权重
- [ ] **relic-generation** / 公共争夺区信物权重与高阶升级
- [ ] **relic-generation** / 区域强度预算
- [ ] **relic-generation** / 生成结果可完整记录

- [ ] `openspec validate add-relic-system` 通过
- [ ] `openspec/changes/add-relic-system/tasks.md` 对应的实现清单（见 `implement.md`）全部完成
- [ ] 新增测试全部通过，且已纳入持续回归

### 能力覆盖

- `relic-control` — 5 条 Requirement / 14 个 Scenario：`openspec/changes/add-relic-system/specs/relic-control/spec.md`
- `relic-effects` — 7 条 Requirement / 13 个 Scenario：`openspec/changes/add-relic-system/specs/relic-effects/spec.md`
- `relic-generation` — 5 条 Requirement / 13 个 Scenario：`openspec/changes/add-relic-system/specs/relic-generation/spec.md`

## Dependencies

前置任务：`09-12-board-core`、`09-12-territory-power`、`09-12-batch-deployment`

- 前置 change：`add-board-core`（信物格位置与强度预算分区）、`add-territory-power`（唯一覆盖查询）、`add-batch-deployment`（结算顺序第 4、5 步）。
- 下游依赖：`add-recruit-hand`（展示数、免费选取数、手牌类型槽、流派徽记权重）、`add-batch-deployment`（部署上限）、`add-match-flow`（先手修正）、`add-heuristic-ai`（信物发现/控制/阻断价值评价）、`add-tactical-ui`（信物层、公开属性面板）。
- 本 change 引入对局的第一个随机源，必须与对局种子绑定并可完整复现（设计文档 §17 要求记录完整信物分布及揭示时间）。
- 验收依赖三个前置 change 完成；在此之前可用桩实现覆盖查询与结算触发点进行单元测试。

## Out of Scope

- 不实现信物效果的消费方逻辑（征募面板怎么展示 5 枚、手牌槽怎么整类弃牌）→ `add-recruit-hand`。
- 不实现先手值公式与行动顺序生成 → `add-match-flow`；本 change 只提供"先手修正"这一整数。
- 不实现信物层的可视化与"将揭示"占位显示 → `add-tactical-ui`。
- 不实现高阶信物之外的新信物类型（设计文档 §19 的后续扩展）。
- 不实现带入带出与局外成长对信物的读取（设计文档 §18.2 明确排除）。
