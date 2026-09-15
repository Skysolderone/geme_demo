# 09-15-dominance-victory

> 规格权威：`openspec/changes/dominance-victory/`（proposal + specs + design）。本文件只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §10、§12.3、§16

## Goal

设计文档 §12.3 条件 1「只剩一名参赛玩家获胜」在这套补给规则下不可达：出局要求盘面与手牌同时为空（§12.1），而私人征募每回合补牌（§5.3）。200 局回归实测出局 0 人次，终局只有整轮 Pass（150 局）与达大回合上限（50 局），没有"打赢"这件事。本任务新增第 5 类终局——**势力碾压**：某参赛玩家势力 ≥ 其余全部参赛玩家势力之和即直接获胜。

## Background / 确认事实

- 规则变更已由项目负责人确认（2026-09-15），并选择在 `growth-pass-1` 之前实现。
- 判定只用现有势力体系，不引入新资源；势力值本就是 §13.1 的始终公开信息。
- 优先级与判定时机见 design.md 裁决 3、4；名次复用 `FinalStandings`（裁决 5）。

## Requirements

- 新增终局原因「势力碾压」，判定式 `本人势力 ≥ 其余参赛玩家势力之和`，取等号即触发。
- 只统计参赛玩家；弃赛与出局者的势力不计入其余之和，本人也不能靠碾压获胜。
- 判定挂在既有的"每次合法批次结算后 / 每次 Pass 后"检查点。
- 优先级：只剩一人 > 碾压 > 整轮 Pass > 棋盘填满 > 达大回合上限。
- `Siege.Sim` 终局原因分布含碾压，平衡报告新增碾压段落（占比、平均触发大回合、获胜者与第 2 名势力比）。
- 设计文档 §12.3 增补条件 5 与优先级说明，文末变更记录加一行。

## Acceptance Criteria

每条对应 `openspec/changes/dominance-victory/specs/` 的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **elimination-endgame** / 三类终局条件（8 个 Scenario，含 4 个碾压新增）
- [ ] **elimination-endgame** / 终局名次与并列判定（8 个 Scenario，含碾压获胜者为第 1 名）
- [ ] **match-telemetry** / 平衡分析方向（5 个 Scenario，含碾压胜统计）
- [ ] `openspec validate dominance-victory` 通过
- [ ] `implement.md` 清单全部完成（4.2 的 200 局回归由主会话执行）
- [ ] 既有 583 条测试语义不变

## Dependencies

前置：`add-match-flow`（终局判定与名次）、`add-territory-power`（势力与参赛过滤）、`round-cap`（终局原因枚举与优先级）。
后继：`growth-pass-1`（数值调整，回归基线以本 change 生效后的数据为准）。

## Out of Scope

- 不改出局条件与征募补给、不改大回合上限、不调 AI、不做碾压专属 UI 演出、不评估 2 人局。
