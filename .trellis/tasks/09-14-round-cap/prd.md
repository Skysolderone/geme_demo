# 09-14-round-cap

> 规格权威：`openspec/changes/round-cap/`（proposal + specs + design）。本文件只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §12.3, §13.1, §16, §18.2

## Goal

规则层目前只有三类终局条件，标准局的常规收尾只能靠整轮 Pass；AI 实测不收敛率 75–100%，对局只能靠跑局层硬停。本任务新增第 4 类终局条件——大回合上限（标准局初值 15，0 = 不限）——作为规则级兜底，让面向玩家的对局有确定终点，让 §16 / §17 的终局类指标在任何规则组合下都有定义。

## Background / 确认事实

- 规则变更已由项目负责人确认（2026-09-14）。
- 设计文档 §18.2 "固定轮数暂不实现"仍成立：上限是兜底不是固定轮数，上限为 0 保留原行为。
- 检查点唯一：大回合结束、生成下一顺序之前；条件 1–3 优先。
- `MajorRoundEnded` 事件发出时序号已推进（heuristic-ai 阶段 B 踩过）——比较用刚结束的轮次。
- 跑局层 `MaxMajorRounds` 改为写对局配置，跑局层自己的"达上限"语义删除。

## Requirements

- 对局配置新增大回合上限：开局固定、公开、入存档、旧存档回填 15。
- 终局原因新增「达大回合上限」；名次复用条件 2/3 的唯一实现。
- Sim `--max-rounds` 写配置；报告按规则原因统计不收敛率；日志首部记上限。
- 设计文档 §12.3 / §18.2 / 变更记录（与 cap-multiplier 共用 v1.1）。

## Acceptance Criteria

每条对应 `openspec/changes/round-cap/specs/` 的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **elimination-endgame** / 大回合上限终局（6 个 Scenario）
- [ ] **elimination-endgame** / 终局名次与并列判定（6 个 Scenario，5 个既有 + 1 新增）
- [ ] **match-setup** / 对局配置公开大回合上限（2 个 Scenario）
- [ ] `openspec validate round-cap` 通过
- [ ] `implement.md` 清单全部完成（5.2 的 200 局回归在 cap-multiplier 也归档后跑）
- [ ] 既有 497 条测试语义不变；heuristic-ai 中依赖跑局层"达上限"的用例改为规则级原因并列在提交信息

## Dependencies

前置：`add-match-flow`（归档）、`add-heuristic-ai`（归档）。
并行：`cap-multiplier`（独立可归档；建议在它之后实现，避免同时改 Sim 报告）。

## Out of Scope

- 固定轮数、按人数差异化上限、运行中改上限、条件 2 的保护期语义、两眼活、AI 末轮行为调参。
