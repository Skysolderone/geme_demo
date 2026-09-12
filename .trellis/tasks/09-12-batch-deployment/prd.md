# 09-12-batch-deployment

> 规格权威：`openspec/changes/add-batch-deployment/`（proposal + specs + design）。本文件不复制规格原文，只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §5.4–5.5, §6

## Goal

"一次规划整批落子"是《围杀 Siege》区别于传统围棋的核心操作，也是全部战术深度的来源：同一批次可以同时补气、成线、围杀、抢信物。这要求合法性必须以**整批最终状态**判定，而不是逐枚结算——一枚单看无气的暂放棋子，只要整批完成提子后获得气，就是合法落子。这条规则一旦实现成"逐枚落子"，整个玩法就塌了。

本 change 对应设计文档 §5.4（批次部署）、§5.5（Pass）、§6.1（合法性预演）、§6.2（盘面同形禁则）、§6.3（正式结算顺序）。

## Background / 确认事实

- 玩法设计已于 v1.0 确认并冻结，本任务不重新设计玩法，只把已确认规则落成可执行、可测试的实现。
- 规格保持实现无关：`openspec/` 下的 spec 不指定语言、引擎、框架；技术栈决策属于本 trellis 任务层。
- 该 change 对应的 37 条设计歧义已全部裁决，记录在 `openspec/changes/add-batch-deployment/design.md` 的「裁决记录（已确认）」一节，实现时 MUST 遵循。
- 全局硬约束见 `openspec/ROADMAP.md`：单一四邻接、一切实时重算、随机可复现、围棋记法坐标。

## Requirements

- 引入批次概念：玩家在一个小回合内暂放 0 至部署上限枚棋子，确认前可自由撤销、换位、替换，正式盘面不发生任何变化。
- 实现六步合法性预演：落点合法性 → 数量与库存 → 模拟整批放置 → 同时移除所有因此无气的敌方棋串 → 己方自杀手检查 → 盘面同形禁则。
- 实现盘面同形禁则：只比较每次合法批次结算后的盘面，不比较预览中间态；盘面状态只含每格占用者与棋子类型。
- 实现正式结算顺序（扣手牌 → 加入盘面 → 同时提子 → 揭示信物 → 重算控制与势力 → 更新排名与出局/终局检查），并以事件形式驱动下游 change。
- 定义 Pass：确认 0 落子即 Pass；未使用的部署额度不跨回合保存。
- 明确围杀无通用奖励：不产生金币、抽棋、经验或击杀分。
- 非法批次必须给出具体原因与相关落点/棋串，供 `add-tactical-ui` 高亮。

## Acceptance Criteria

每条对应 `openspec/changes/add-batch-deployment/specs/` 中的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **batch-deployment** / 批次暂放不改变正式盘面
- [ ] **batch-deployment** / 批次数量与库存约束
- [ ] **batch-deployment** / 批次内落点的多样性
- [ ] **batch-deployment** / 确认批次
- [ ] **batch-deployment** / Pass
- [ ] **batch-deployment** / 非法批次必须给出可定位的原因
- [ ] **batch-deployment** / 围杀不产生通用奖励
- [ ] **capture-resolution** / 以整批最终状态判定合法性
- [ ] **capture-resolution** / 盘面同形禁则
- [ ] **capture-resolution** / 正式结算顺序
- [ ] **capture-resolution** / 已提交盘面历史的持久化

- [ ] `openspec validate add-batch-deployment` 通过
- [ ] `openspec/changes/add-batch-deployment/tasks.md` 对应的实现清单（见 `implement.md`）全部完成
- [ ] 新增测试全部通过，且已纳入持续回归

### 能力覆盖

- `batch-deployment` — 7 条 Requirement / 15 个 Scenario：`openspec/changes/add-batch-deployment/specs/batch-deployment/spec.md`
- `capture-resolution` — 4 条 Requirement / 12 个 Scenario：`openspec/changes/add-batch-deployment/specs/capture-resolution/spec.md`

## Dependencies

前置任务：`09-12-board-core`

- 前置 change：`add-board-core`（盘面状态、棋串、气、确定性序列化）。
- 下游依赖：`add-territory-power`（结算后重算领地与势力）、`add-relic-system`（结算中的信物揭示与控制更新）、`add-recruit-hand`（手牌扣减与 Pass 的征募撤销）、`add-match-flow`（部署上限来源、出生区限制、出局/终局检查时机）、`add-heuristic-ai`（候选批次枚举与评估）、`add-tactical-ui`（批次预演显示与非法原因高亮）。
- 本 change 引入对局的第一个"提交历史"：已提交盘面集合，必须与对局状态一起持久化和序列化。

## Out of Scope

- 不定义部署上限的数值来源（基础 3 + 军令信物）→ 由 `add-relic-system` 与 `add-match-flow` 的效果快照提供，本 change 只消费一个整数。
- 不定义出生区限制何时生效（前三大回合）→ `add-match-flow`；本 change 只接受一个"当前合法落子范围"约束。
- 不实现手牌库存管理与 Pass 的征募撤销细则 → `add-recruit-hand`；本 change 只定义扣减时机与 Pass 事件。
- 不实现势力重算与排名更新 → `add-territory-power`；本 change 只定义它们在结算顺序中的位置。
- 不实现击杀触发型棋子效果（设计文档 §6.3 提到未来可能出现）→ 本轮不做，仅预留"本批次最后一手归功"的裁决记录。
- 不实现 UI 预演面板 → `add-tactical-ui`。
