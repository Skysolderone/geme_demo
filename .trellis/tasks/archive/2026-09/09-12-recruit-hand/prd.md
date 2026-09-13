# 09-12-recruit-hand

> 规格权威：`openspec/changes/add-recruit-hand/`（proposal + specs + design）。本文件不复制规格原文，只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §5.2–5.3, §5.5, §9.1

## Goal

征募是玩家把"信物供给"兑换成"盘面棋子"的唯一入口，也是随机构筑那 40% 体验的来源。它有三条互相咬合的约束：展示数 / 免费选取数决定每回合的吞吐，手牌类型槽决定组合的宽度，流派徽记决定抽到什么。三者都来自信物快照，但消费规则各不相同。

同时，Pass 的征募撤销（§5.5）是防止"无限囤牌拖延"的唯一护栏：确认 0 落子会撤销本小回合新获得的全部棋子，但只要落下 1 枚就全部保留。这条规则的账目必须精确到"本轮新增"与"进入回合前已有"的区分，否则玩家会用同类型棋子把两者混淆。

本 change 对应设计文档 §5.2（整理手牌）、§5.3（私人征募）、§5.5（Pass 的征募撤销）、§9.1（初始配置与征募权重）。

## Background / 确认事实

- 玩法设计已于 v1.0 确认并冻结，本任务不重新设计玩法，只把已确认规则落成可执行、可测试的实现。
- 规格保持实现无关：`openspec/` 下的 spec 不指定语言、引擎、框架；技术栈决策属于本 trellis 任务层。
- 该 change 对应的 37 条设计歧义已全部裁决，记录在 `openspec/changes/add-recruit-hand/design.md` 的「裁决记录（已确认）」一节，实现时 MUST 遵循。
- 全局硬约束见 `openspec/ROADMAP.md`：单一四邻接、一切实时重算、随机可复现、围棋记法坐标。

## Requirements

- 实现初始配置：每名玩家开局获得 5 枚普通子；所有玩家共用同一基础棋池。
- 实现基础棋池权重：普通子 40、堡垒子 20、连珠子 18、倍增子 12、协同子 10。
- 实现流派徽记调权公式 `调整后权重 = 基础权重 × (1 + 0.75 × 徽记数量)`，调整后重新归一化，每个候选位独立抽取并允许重复类型。
- 实现私人征募面板：默认展示 5 枚、最多免费选取 3 枚；无金币、无基础免费刷新；未选棋子在小回合结束时消失。
- 实现征募的信息边界：候选与选择过程只对当前玩家可见。
- 实现手牌类型槽：默认 5 槽，每槽一种棋子类型，同类型数量无限叠加；无空槽时不可选择新类型，已有类型可继续叠加。
- 实现主动整类弃牌，以及兵站丢失导致槽位缩水时的强制整类弃牌（征募前必须完成）。
- 实现 Pass 的征募撤销：只扣回本小回合新增数量，进入小回合前已有的手牌不受影响。

## Acceptance Criteria

每条对应 `openspec/changes/add-recruit-hand/specs/` 中的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **hand-management** / 手牌类型槽
- [ ] **hand-management** / 主动整类弃牌
- [ ] **hand-management** / 槽位缩水时的强制整类弃牌
- [ ] **hand-management** / 手牌库存与扣减
- [ ] **hand-management** / Pass 撤销本轮新增征募
- [ ] **hand-management** / 手牌的信息边界
- [ ] **hand-management** / 手牌为空的可查询状态
- [ ] **recruitment** / 初始配置与基础棋池
- [ ] **recruitment** / 流派徽记调整征募权重
- [ ] **recruitment** / 私人征募面板
- [ ] **recruitment** / 选取受类型槽约束
- [ ] **recruitment** / 征募的信息边界
- [ ] **recruitment** / 征募随机可复现

- [ ] `openspec validate add-recruit-hand` 通过
- [ ] `openspec/changes/add-recruit-hand/tasks.md` 对应的实现清单（见 `implement.md`）全部完成
- [ ] 新增测试全部通过，且已纳入持续回归

### 能力覆盖

- `hand-management` — 7 条 Requirement / 20 个 Scenario：`openspec/changes/add-recruit-hand/specs/hand-management/spec.md`
- `recruitment` — 6 条 Requirement / 16 个 Scenario：`openspec/changes/add-recruit-hand/specs/recruitment/spec.md`

## Dependencies

前置任务：`09-12-relic-system`、`09-12-batch-deployment`

- 前置 change：`add-relic-system`（效果快照：展示数、选取数、类型槽、徽记权重调整、超限种数）、`add-batch-deployment`（手牌扣减请求与 Pass 事件）。
- 下游依赖：`add-batch-deployment`（手牌库存约束）、`add-match-flow`（出局条件之一是手牌为空）、`add-heuristic-ai`（征募候选评价、供给与部署能力匹配）、`add-tactical-ui`（手牌信息面板：自己看数量，对手只看类型）。
- 本 change 是对局的第二个随机源（征募抽取），必须与对局种子绑定并逐回合可复现（设计文档 §17 要求记录每轮征募候选、玩家选择与被撤销的 Pass 征募）。

## Out of Scope

- 不实现棋子在盘面上的效果与计分 → `add-territory-power`。
- 不实现部署与批次合法性 → `add-batch-deployment`。
- 不实现信物效果的产生 → `add-relic-system`；本 change 只消费快照。
- 不实现金币、付费刷新、棋子升级或传统数量羁绊（设计文档 §18.2 明确排除）。
- 不实现手牌面板的可视化与对手信息遮挡的渲染 → `add-tactical-ui`；本 change 只定义可见性规则本身。
