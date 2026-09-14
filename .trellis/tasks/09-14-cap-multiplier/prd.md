# 09-14-cap-multiplier

> 规格权威：`openspec/changes/cap-multiplier/`（proposal + specs + design）。本文件只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §9.2, §10.1, §16

## Goal

heuristic-ai 36 局实测证明 `1.5^倍增子数量` 无上限让边际收益永远为正、对局不收敛、单串军势指数膨胀。本任务把倍率指数封顶为 5（倍率上限 243/32），推翻 `add-territory-power` 裁决 3，把倍增串天花板拉回设计文档 §16 "数百" 的量级。

## Background / 确认事实

- 规则变更已由项目负责人确认（2026-09-14）：先做 cap-multiplier + round-cap，两眼活留第二轮。
- 倍率是全项目唯一的"乘倍率并取整"实现（`Multiplier` 结构体，精确整数 3^n/2^n，checked）；封顶只能在这一处做。
- 势力明细与遥测峰值被 `Siege.Sim` 日志和将来的 tactical-ui 直接消费；原始倍增子数量的语义不能改。
- 设计文档需升 v1.1 并加变更记录（design.md D5）。

## Requirements

- 棋串军势 = ⌊(基础 + 加值) × 1.5^min(n, 5)⌋；第 6 枚起的倍增子只贡献基础军势 1，其他效果（共享气、类型计数）不变。
- 明细新增"生效倍率指数"，保留"倍增子数量"；倍率显示最大 `7.59375`。
- 遥测峰值保留原始数量、可得生效指数；Sim 报告双列输出；旧日志回填。
- 设计文档 §9.2 / §10.1 / 变更记录。

## Acceptance Criteria

每条对应 `openspec/changes/cap-multiplier/specs/power-score/spec.md` 的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **power-score** / 棋串军势公式（7 个 Scenario，含 3 个既有算例原样通过）
- [ ] **power-score** / 势力明细（3 个 Scenario）
- [ ] `openspec validate cap-multiplier` 通过
- [ ] `implement.md` 清单全部完成（4.2 的 200 局回归在 round-cap 也归档后跑）
- [ ] 既有 497 条测试语义不变（改写的"无上限"断言逐条列在提交信息）

## Dependencies

前置：`add-territory-power`（归档）、`add-heuristic-ai`（归档；Sim 报告与日志格式）。
并行：`round-cap`（独立可归档；200 局回归两者都归档后一起跑）。

## Out of Scope

- 终局条件 → `round-cap`；两眼活 → 第二轮；AI / 征募权重 → 视回归数据另开 change；2000 局基线 → 规则复议完成后。
