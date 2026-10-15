## Why

链接模式里 AI 缺省是"全知"：能读到其他战场的盘面与势力，对人类玩家不公平（`board-link-mode` 裁决 Q7 / C4 的第一版例外）。打开按座位视野开关后，专家难度的一层前瞻会把其他战场的玩家当作已出局（C12），所以一直没有切换。对应设计文档 v1.25 §13、§15、§19「AI 视野收紧」。

## 负责人裁决（2026-10-15）

| # | 事项 | 裁决 |
|---|---|---|
| V1 | 目标 | AI 缺省改为按座位视野，四档难度一律不再全知；全知只留作测试与分析的显式配置项 |
| V2 | 修 C12 | 专家前瞻只模拟同战场的对手，他战场玩家不进名册、不被当作出局；下一名对手取同战场内行动顺序的下一位 |
| V3 | 全局信息读取点 | 盘点评价、停手、决胜一手、征募中读他人势力或全局排名的点，只读按座位投影或公开信息（行动顺序公开可读），读不到的按同战场可见范围重写并列出清单 |
| V4 | 基线 | 链接图基线按新缺省重钉并逐条说明；另加断言：显式全知时与改前逐字节相同 |
| V5 | 验证规模 | 两批共 40 局（负责人确认）：标准四人 `link:1`–`link:20` 各 1 局；1 专家 + 3 标准 20 局 |
| V6 | 范围 | Core AI、Sim 配置与日志首部、HANDOFF 与设计文档；图形版读缺省配置自动生效，不改代码 |

## What Changes

- **BREAKING（AI 行为）**：`SeatVision` 缺省由全知改为按座位视野；链接图决策序列基线重钉。
- 专家前瞻名册限同战场（`ai-decision`「前瞻中的下一名对手」）。
- 评价 / 停手 / 决胜一手 / 征募的全局信息读取点按 V3 收口。
- Sim：日志首部与批次配置写视野配置；全知只能经 `--config` 显式打开。

## Capabilities

### Modified Capabilities
- `ai-decision`：修改「正式对战 AI 的信息边界」「前瞻中的下一名对手」。

## Impact

- Core：`Ai/AiSearchConfig`（缺省）、`HeuristicAi.VisionFor`、`ExpertLookahead`（名册）、投影视图 `MatchFlow.SeatProjection` 的使用点；`EvaluationBreakdown.Version` 视是否改变候选保留规则决定是否递增（V3 若改了读取点则递增）。
- Sim：`RunConfig`、`MatchLog` 首部。
- 测试：链接图 5 条基线重钉；全知配置下逐字节不变的对照断言；名册新 Scenario。

## Non-goals

- 不改评价权重与停手阈值数值；不改规则；不改图形版代码。
- 不清除 Legacy 规则分支（`retire-legacy-rules`）。

## 前置 change

`board-link-mode`、`ai-pass-review`、`final-countdown`（均已归档）。
