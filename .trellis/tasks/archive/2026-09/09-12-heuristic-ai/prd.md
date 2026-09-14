# 09-12-heuristic-ai

> 规格权威：`openspec/changes/add-heuristic-ai/`（proposal + specs + design）。本文件不复制规格原文，只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §15, §16, §17

## Goal

前六个 change 做完只得到一套可以正确运行的规则，但无法回答唯一真正重要的问题：**这套规则好不好玩、平不平衡**。设计文档 §16 给了一组硬指标（部署上限的成长曲线、势力量级、首次冲突轮次、平均时长、第 3 大回合领先者胜率 ≤ 50%），§17 给了必须记录的数据与必须分析的六个方向。要验证这些，需要能自动打完成千上万局的 AI 与跑局设施。

同时，AI 的信息边界是公平性的底线：正式人机混合模式中 AI 只能读公开信息与自己的私有信息（§15.1）。这条必须在数据层成立——靠"约定 AI 不去读"是守不住的。调试 AI 可以读全量状态，但必须显式标记为测试专用，且不得用于面向玩家的对局（§15.3）。

本 change 对应设计文档 §15（AI 设计）、§16（原型数值目标）、§17（数据记录与平衡验证）。

## Background / 确认事实

- 玩法设计已于 v1.0 确认并冻结，本任务不重新设计玩法，只把已确认规则落成可执行、可测试的实现。
- 规格保持实现无关：`openspec/` 下的 spec 不指定语言、引擎、框架；技术栈决策属于本 trellis 任务层。
- 该 change 对应的 37 条设计歧义已全部裁决，记录在 `openspec/changes/add-heuristic-ai/design.md` 的「裁决记录（已确认）」一节，实现时 MUST 遵循。
- 全局硬约束见 `openspec/ROADMAP.md`：单一四邻接、一切实时重算、随机可复现、围棋记法坐标。

## Requirements

- 实现正式对战 AI 的信息边界：只能访问公开信息与自身私有信息，对未知信物与敌方库存只能按公开规则与概率估计。
- 实现启发式评价函数，覆盖设计文档 §15.2 列出的七个维度：即时势力增量、敌方势力损失与提子规模、信物发现/控制/阻断价值、棋串剩余气与两眼潜力与被围杀风险、连珠/倍增/协同组合成长、下一大回合先手位变化、手牌供给与部署能力匹配度。
- 实现难度分级：简单难度只考虑即时收益；更高难度扩大候选批次数量，且 MUST NOT 通过读取隐藏信息提高强度。
- 实现高部署上限下的候选剪枝：先筛高价值落点，再组合有限数量的候选批次，避免穷举排列。
- 实现原型调试 AI：可选读取完整权威状态，显式标记测试专用；支持在任意小回合由设计者人工接管任何 AI。
- 实现批量跑局设施：种子批次、并行执行、结果汇总、失败局可复现。
- 实现完整对局日志：覆盖 §17 列出的全部七类记录。
- 实现平衡分析报告：覆盖 §17 列出的六个重点分析方向与 §16 的数值目标回归。

## Acceptance Criteria

每条对应 `openspec/changes/add-heuristic-ai/specs/` 中的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **ai-decision** / 正式对战 AI 的信息边界
- [ ] **ai-decision** / 对隐藏信息的概率估计
- [ ] **ai-decision** / 启发式评价维度
- [ ] **ai-decision** / 难度分级
- [ ] **ai-decision** / 高部署上限下的候选剪枝
- [ ] **ai-decision** / AI 决策的可复现性
- [ ] **match-telemetry** / 对局日志的记录内容
- [ ] **match-telemetry** / 数值目标回归
- [ ] **match-telemetry** / 平衡分析方向
- [ ] **match-telemetry** / 分析排除测试污染
- [ ] **simulation-harness** / 原型调试 AI
- [ ] **simulation-harness** / 人工接管
- [ ] **simulation-harness** / 批量跑局
- [ ] **simulation-harness** / 可复现回放
- [ ] **simulation-harness** / 随机子流隔离

- [ ] `openspec validate add-heuristic-ai` 通过
- [ ] `openspec/changes/add-heuristic-ai/tasks.md` 对应的实现清单（见 `implement.md`）全部完成
- [ ] 新增测试全部通过，且已纳入持续回归

### 能力覆盖

- `ai-decision` — 6 条 Requirement / 14 个 Scenario：`openspec/changes/add-heuristic-ai/specs/ai-decision/spec.md`
- `match-telemetry` — 4 条 Requirement / 11 个 Scenario：`openspec/changes/add-heuristic-ai/specs/match-telemetry/spec.md`
- `simulation-harness` — 5 条 Requirement / 11 个 Scenario：`openspec/changes/add-heuristic-ai/specs/simulation-harness/spec.md`

## Dependencies

前置任务：`09-12-match-flow`

- 前置 change：`add-board-core`、`add-batch-deployment`、`add-territory-power`、`add-relic-system`、`add-recruit-hand`、`add-match-flow`（全部六个）。
- 下游依赖：`add-tactical-ui`（复用公开信息视图；调试界面的人工接管入口）。
- 本 change 的产出直接决定后续数值调整：§16 的目标区间与 §17 的六个分析方向都在这里回归。若指标不达标，修改发生在 `add-recruit-hand` 的权重、`add-relic-system` 的生成预算或 `add-board-core` 的地图，而非在本 change 内打补丁。

## Out of Scope

- 不实现完整博弈树 AI（设计文档 §18.2 明确排除）；本轮只做启发式。
- 不实现正式联网对战中的 AI 托管与断线接管。
- 不实现任何 AI 通过读取隐藏信息获得强度的路径。
- 不实现玩家面向的 AI 难度 UI → `add-tactical-ui`。
- 不实现局外成长、带入带出的数据分析。
- 不在本 change 内直接修改玩法数值；本 change 只负责测量与报告。

## 从上游带来的待决项

- **遥测峰值字段**（来自 territory-power 的 check）：`PowerScoreboard.Peak` 记的是倍增子数量 n（倍率单调，等价于倍率峰值），但没记该串当时的军势值，也没有"峰值串被摧毁"信号。§17 要"高倍率棋串的形成轮次、峰值及被摧毁概率"，建议给 `MultiplierPeak` 加 `Power` 字段，并在每次 `Recalculate` 后比对 `Latest` 中是否仍有包含 `Peak.Stones` 的同主棋串。
- **覆盖表分配**：`CoverageMap.Compute` 每个被覆盖格分配一个 `HashSet<PlayerId>`，批量跑局下每次重算约百次分配。按 `boundaries.md`"先实测再优化"，Sim 有单局耗时数据后再决定是否改为按 `PlayerId` 位掩码。
- **信物生成的校准项**（来自 relic-system 的 check）：8% 稀有度容差下，出生区先锋/军令占比约 0.8%/3.7%（权重表 5%/7%），探勘约 36%（权重表 20%），24% 的种子走"未收敛"兜底。§17 分析"出生区随机资源是否造成显著胜率差异"时要把这三个数当作已知输入，并统计未收敛局与收敛局的胜率差异。
- **AI 只能拿只读公开接口**（来自 recruit-hand 的 check）：`HandLedger.AccessFor(PlayerId)` 是 public，持有账本引用者可拿到任何玩家的私有句柄。D6 信息边界成立的前提是流程层只把 `PlayerHandAccess`（自己的）+ `HandPublicView`（他人的）交给 AI。本任务必须定义一个只含公开成员的读取接口给正式 AI，不传 `HandLedger` / `RelicLedger` 本体；调试 AI 走 `internal` 旁路。
