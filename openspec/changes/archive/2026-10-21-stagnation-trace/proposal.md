## Why

`cycle-stop` 给 AI 加了"停滞大回合数 K = 6"的循环提子停手，但停手成立时决策条目仍记普通的 `D:pass`，离线分析数不出它触发了几次（HANDOFF 观察项 1b、cycle-stop 质检裁决 2）。同时现行 5 份决策序列基线的首部还写着已删除的"配对 / 链接"口径，ROADMAP 停在 v1.18、HANDOFF 第 8 项状态过期。本 change 是一次小收尾：给停滞停手留痕，顺带清掉这几处旧口径。

对应设计文档章节：§15.2（循环提子停手）、§17（日志）、§19 观察项。设计文档 v1.30 → v1.31。

## 负责人裁决（2026-10-21，G1–G5 全按推荐）

| # | 事项 | 裁决 |
|---|---|---|
| G1 | 停滞停手留痕 | 停滞成立且最终 Pass 时决策条目记 `D:stall`（决胜一手接管时照常记落点）；`analyze --review-cycles` 报告加停滞停手次数；AI 行为不变、评价版本不变 |
| G2 | 基线首部旧字样 | 重写 5 份基线首部：删 `mode=Link pairing=script（缺省：升序两两配）` 与"P 配对、L 链接"；决策行逐字节不变 |
| G3 | ROADMAP | 按现状重写：已完成列到 v1.30；未完成改为试玩待定项、专家强度归因、画面精修重规划、AI 整批评价、联网、美术；删失效旧地图项 |
| G4 | HANDOFF 第 8 项 | 改为已归档 2026-10-20、已提交 f750eeb；观察项 1b 标为已处理 |
| G5 | 派发 | 单 agent 顺序 G1 → G2 → G3 / G4，回传后质检；提交与推送前问负责人 |

## What Changes

- **AI 留痕**：`HeuristicTurnController` 停滞成立且最终 Pass 时，决策条目由 `D:pass` 改为 `D:stall`；不改任何决策与随机消费。
- **离线分析**：`CycleAnalyzer` / `analyze --review-cycles` 报告每局与汇总的停滞停手次数（旧日志计 0）。
- **基线首部**：5 份 `decisions-link1-*` 首部删除配对 / 链接旧字样；基线生成器同步；决策行逐字节不变。
- **文档**：ROADMAP 重写；HANDOFF 第 8 项与 1b；设计文档 v1.31（§15.2、§17、§19 一句）。

## Capabilities

### New Capabilities

（无）

### Modified Capabilities

- `ai-decision`：「循环提子停手」加 `D:stall` 留痕与 Scenario。
- `match-telemetry`：「循环提子诊断」加停滞停手次数与 Scenario。

## Impact

- Core：`HeuristicTurnController`（一处条目文案）。Sim：`CycleAnalyzer`、`ReportWriter`、日志读取（若决策条目经日志写出）。测试：基线生成器首部、新守门；决策条目若出现在任何日志 / 报告哈希中，按格式变化重钉并记录。
- 不影响：规则、AI 决策、地图、Godot。

## Non-goals

- 不改 K、停手阈值、评价权重；不处理停滞记忆入存档（观察项 1c）；不开工 ROADMAP 里列出的任何新项。

## 前置 change

`cycle-stop`、`link-map-surfaces`（均已归档）。
