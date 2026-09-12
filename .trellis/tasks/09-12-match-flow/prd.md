# 09-12-match-flow

> 规格权威：`openspec/changes/add-match-flow/`（proposal + specs + design）。本文件不复制规格原文，只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §4, §5, §11, §12

## Goal

前五个 change 各自实现了一块规则，但没有任何东西把它们串成一局游戏。本 change 提供状态机：谁先行动、行动时能做什么、什么时候解除限制、什么时候有人出局、什么时候结束。

这里藏着整个玩法最精巧也最容易写错的一段——**开局出局保护的解除是逐玩家的**。设计文档 §4.2 与 §12.1 反复强调：所有玩家都保证获得第四大回合中的**自己那一次**小回合，先手玩家的行动不得让尚未行动的玩家提前出局，保护在各自完成第四大回合小回合后**单独**解除。写成"第四大回合开始时全体解除"会直接产生"开局被围死就再也回不来"的体验事故。

另一个关键点是先手正反馈：`先手值 = (参赛人数 − 势力名次) + 先手修正`。它是"抢先流"的核心收益，也是 §17 要重点监控的滚雪球风险来源。

本 change 对应设计文档 §4（开局与前三大回合）、§5（玩家小回合的阶段顺序）、§11（大回合行动顺序）、§12（出局、弃赛与终局）。

## Background / 确认事实

- 玩法设计已于 v1.0 确认并冻结，本任务不重新设计玩法，只把已确认规则落成可执行、可测试的实现。
- 规格保持实现无关：`openspec/` 下的 spec 不指定语言、引擎、框架；技术栈决策属于本 trellis 任务层。
- 该 change 对应的 37 条设计歧义已全部裁决，记录在 `openspec/changes/add-match-flow/design.md` 的「裁决记录（已确认）」一节，实现时 MUST 遵循。
- 全局硬约束见 `openspec/ROADMAP.md`：单一四邻接、一切实时重算、随机可复现、围棋记法坐标。

## Requirements

- 实现匿名插旗：地形/出生区/信物格位置公开、信物内容隐藏；联网模式 15 秒内同时插旗、只显示位置不显示身份、锁定前可更换；多名玩家可选同一出生区；锁定后第一大回合顺序随机。
- 实现原型插旗替代路径：由 AI 或调试界面依次完成插旗，不把热座匿名性作为验证目标。
- 实现构筑保护期：第 1–3 大回合只能在自己锁定的出生区落子；共享出生区的玩家共享合法范围；围杀/覆盖/信物发现/势力计算照常运行；保护期暂停"盘面与手牌同时为空即出局"的检查。
- 实现第 4 大回合的全图解禁与盘面为空玩家的重新进入。
- 实现逐玩家的开局出局保护解除。
- 实现小回合五阶段顺序：信物快照 → 整理手牌 → 私人征募 → 批次部署 → 结算/Pass。
- 实现大回合定义与推进：所有参赛玩家各完成一次小回合。
- 实现先手值公式、同值判定链与下一大回合顺序生成。
- 实现出局、主动弃赛与三类终局条件，以及终局名次与并列判定。

## Acceptance Criteria

每条对应 `openspec/changes/add-match-flow/specs/` 中的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **elimination-endgame** / 保护期暂停出局检查
- [ ] **elimination-endgame** / 逐玩家的开局出局保护解除
- [ ] **elimination-endgame** / 出局判定
- [ ] **elimination-endgame** / 主动弃赛
- [ ] **elimination-endgame** / 三类终局条件
- [ ] **elimination-endgame** / 终局名次与并列判定
- [ ] **initiative-order** / 基础排序
- [ ] **initiative-order** / 先手值公式
- [ ] **initiative-order** / 同值判定链
- [ ] **initiative-order** / 排名的作用范围
- [ ] **match-setup** / 开局信息公开范围
- [ ] **match-setup** / 匿名同时插旗
- [ ] **match-setup** / 首回合顺序随机
- [ ] **match-setup** / 原型插旗替代路径
- [ ] **turn-sequence** / 小回合的五个阶段
- [ ] **turn-sequence** / 大回合的定义与推进
- [ ] **turn-sequence** / 构筑保护期的落子限制
- [ ] **turn-sequence** / 第 4 大回合的全图解禁
- [ ] **turn-sequence** / 合法落子范围的对外契约

- [ ] `openspec validate add-match-flow` 通过
- [ ] `openspec/changes/add-match-flow/tasks.md` 对应的实现清单（见 `implement.md`）全部完成
- [ ] 新增测试全部通过，且已纳入持续回归

### 能力覆盖

- `elimination-endgame` — 6 条 Requirement / 22 个 Scenario：`openspec/changes/add-match-flow/specs/elimination-endgame/spec.md`
- `initiative-order` — 4 条 Requirement / 10 个 Scenario：`openspec/changes/add-match-flow/specs/initiative-order/spec.md`
- `match-setup` — 4 条 Requirement / 9 个 Scenario：`openspec/changes/add-match-flow/specs/match-setup/spec.md`
- `turn-sequence` — 5 条 Requirement / 11 个 Scenario：`openspec/changes/add-match-flow/specs/turn-sequence/spec.md`

## Dependencies

前置任务：`09-12-board-core`、`09-12-batch-deployment`、`09-12-territory-power`、`09-12-relic-system`、`09-12-recruit-hand`

- 前置 change：`add-board-core`（出生区、可落子空格查询）、`add-batch-deployment`（结算完成事件、Pass 事件）、`add-territory-power`（势力值与名次）、`add-relic-system`（效果快照、先手修正）、`add-recruit-hand`（手牌是否为空、强制弃牌门）。
- 下游依赖：`add-heuristic-ai`（按流程驱动 AI 决策、先手位变化评价）、`add-tactical-ui`（顺序层、行动顺序展示、弃赛与出局标记）。
- 本 change 定义对局的顶层状态与存档边界：插旗结果、当前大回合/小回合、行动顺序、每名玩家的保护与出局/弃赛状态，都必须可持久化。

## Out of Scope

- 不实现正式联网同步、断线处理与匹配（设计文档 §18.2 明确排除）；15 秒同时插旗的规则照常定义，但本轮只在单机/AI 路径上验证。
- 不实现带入带出、局外成长与弃赛收益结算（设计文档 §12.2/§18.2 明确排除）；本轮只保证弃赛时的盘面、手牌与资源快照可被记录。
- 不实现 AI 的决策逻辑 → `add-heuristic-ai`；本 change 只定义流程在何时向玩家/AI 请求决策。
- 不实现顺序层的可视化 → `add-tactical-ui`。
- 不实现 2 人、3 人正式地图的对局验证（设计文档 §18.2）。
