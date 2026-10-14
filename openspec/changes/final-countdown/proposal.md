## Why

链接模式下先打完首轮的玩家进入等待，自己的战场已无有用之手，势力基本停住；另一对继续交战，军势按倍率增长，差距越拉越大。决赛基本由开局势力决定：拼接版 20 局等待者决赛胜率 27.8%（对首轮刚结算的胜者 68.2%），主题化地形后 20 局为 20.0% 对 80.0%，等待中位 6、最长 14 个大回合，最大开局势力差 7636。对应设计文档 v1.24 §4.3、§12、§19「决赛开局势力差评估」。

## 负责人裁决（2026-10-14）

| # | 事项 | 裁决 |
|---|---|---|
| W1 | 修法 | **C 首轮同步收尾**：出现等待决赛的玩家后，其余未结算的多人战场最多再打 K 个大回合，到期按当前势力强制结算 |
| W2 | K | 2 个大回合，写成对局配置，可调（0 = 不启用） |
| W3 | 强制结算判定 | 沿用现有战场结算比较链（势力、信物、独占空格、棋子、种子兜底），只多"到期"触发 |
| W4 | 图形版提示 | 倒计时提示条"对面战场已结算，本战场还剩 N 个大回合"；等待者看到期限；结算横幅照旧 |
| W5 | 验证 | `link:1`–`link:20` 各 1 局共 20 局，与 `ai-terrain-review` 主题批次逐项对比 |
| W6 | 范围 | Core 规则、AI 不改、Sim 分析、图形版提示条 |

## What Changes

- Core：新增决赛倒计时（`battlefield`「决赛倒计时」，修改「战场结算」加入到期条件）；对局配置 K（`match-setup`）；存档与日志记录 K、期限与"到期"原因。
- Sim：日志首部写 K；`analyze` 统计到期结算次数，等待者评估沿用。
- Godot / Presentation：倒计时提示条（`settlement-show`）。
- **BREAKING（对局行为）**：缺省 K = 2 改变链接模式对局走向；链接图决策序列基线须重钉。

## Capabilities

### Modified Capabilities
- `battlefield`：修改「战场结算」，新增「决赛倒计时」。
- `match-setup`：新增「决赛倒计时配置」。
- `match-telemetry`：新增「决赛倒计时的记录」。
- `settlement-show`：新增「决赛倒计时提示」。

## Impact

- Core：`MatchFlow.Link.cs`（结算判定 `JudgeAtRoundEnd` / 执行、阶段推进）、`MatchOptions`、存档 Link 段、`BattlefieldSettlement`（新原因）、`MatchFlow.Preview`（先手预测按同一判定）。
- Sim：`MatchLog` 首部与结算记录、`RunConfig`、`analyze`。
- Presentation / Godot：提示条视图模型与接线。
- 测试：新规则单元测试；链接图 5 条决策序列基线按 K = 2 重钉并说明；K = 0 时逐步与改前相同的对照测试。

## Non-goals

- 不改 AI 评价与停手（决胜一手照旧，到期结算不经 Pass）。
- 不改决赛规则、名次规则与带入带出（E6 照旧）。

## 前置 change

`board-link-mode`、`ai-pass-review`、`link-map-terrain`、`ai-terrain-review`（均已归档）。
