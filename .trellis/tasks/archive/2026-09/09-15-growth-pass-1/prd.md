# 09-15-growth-pass-1

> 规格权威：`openspec/changes/growth-pass-1/`（proposal + specs + design）。本文件只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §5.4、§9.2、§10.1、§16

## Goal

200 局回归对照 §16 还剩两项数值偏离：第 7+ 大回合部署上限均值 3.36（目标 5–8，靠军令信物供给算术上达不到）；倍增子选择率 86.9%（"出现就选"）。本任务把基础部署上限改为按大回合分阶段 3 / 4 / 5（军令继续叠加），倍率封顶 5 → 4。

## Background / 确认事实

- 两项裁决已由项目负责人确认（2026-09-15），见 design.md 裁决 1–5。
- 实现顺序在 `dominance-victory` 之后：碾压胜利（起始第 7 大回合）已生效，回归基线取 dominance-victory 裁决 11 表中"起始 7"那一行。
- 部署提速会改变冲突时点与碾压触发时点；回归须复核碾压起始大回合 7 是否仍合适（dominance-victory 裁决 11 已预留）。

## Requirements

- 效果快照的基础部署上限按生成快照时的当前大回合取 3 / 4 / 5（第 1–3 / 4–6 / 7+），阶段表只定义一处；军令在其上 +1 叠加；来源拆分区分"分阶段基础"与"军令加成"。
- 倍率封顶常量 `Multiplier.MaxExponent` 5 → 4，倍率上限 5.0625。
- 设计文档 §5.4 / §9.2 / §10.1 / §16 与变更记录。
- Sim 报告的部署上限目标区间、倍率封顶说明随之更新。

## Acceptance Criteria

每条对应 `openspec/changes/growth-pass-1/specs/` 的一条 Requirement；验收 = 全部 Scenario 都有通过的自动化测试。

- [ ] **batch-deployment** / 批次数量与库存约束（5 个 Scenario，含"基础值随阶段提高"）
- [ ] **relic-effects** / 六类原型信物的效果（3 个 Scenario）
- [ ] **relic-effects** / 同类信物叠加且无统一硬上限（3 个 Scenario，含"来源可拆分"）
- [ ] **power-score** / 棋串军势公式（7 个 Scenario，封顶边界按 4 改写）
- [ ] **piece-effects** / 倍增子的棋串倍率（3 个 Scenario）
- [ ] `openspec validate growth-pass-1` 通过
- [ ] `implement.md` 清单全部完成（5.2 的 200 局回归由主会话执行）
- [ ] 既有测试中写死"基础部署上限 3"或"封顶 5"的算例按新规则改写，逐条列入提交信息，不改实现语义凑绿

## Dependencies

前置：`add-relic-system`、`add-batch-deployment`、`cap-multiplier`、`dominance-victory`（均已归档）。

## Out of Scope

- 不改保护期、地图、出生区距离、征募权重表、军令生成权重、终局条件、碾压起始大回合（回归后另议）。
