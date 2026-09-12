# 09-12-board-core

> 规格权威：`openspec/changes/add-board-core/`（proposal + specs + design）。本文件不复制规格原文，只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §2, §3

## Goal

《围杀 Siege》的每一条规则——围杀、覆盖、领地、势力、信物控制——都建立在"格子 / 四邻接 / 棋串 / 气"这一层之上。设计文档 §3 把拓扑判定收敛成一句"统一使用上下左右四邻接"，但尚无可执行的权威状态定义。没有这一层，后续 7 个 change 无法各自独立验收，也无法复现同一盘面。

本 change 对应设计文档 §2（核心术语）、§3（棋盘与地图），并承接 §12.3 终局条件 3 对"是否存在可落子空格"的查询需求。

## Background / 确认事实

- 玩法设计已于 v1.0 确认并冻结，本任务不重新设计玩法，只把已确认规则落成可执行、可测试的实现。
- 规格保持实现无关：`openspec/` 下的 spec 不指定语言、引擎、框架；技术栈决策属于本 trellis 任务层。
- 该 change 对应的 37 条设计歧义已全部裁决，记录在 `openspec/changes/add-board-core/design.md` 的「裁决记录（已确认）」一节，实现时 MUST 遵循。
- 全局硬约束见 `openspec/ROADMAP.md`：单一四邻接、一切实时重算、随机可复现、围棋记法坐标。

## Requirements

- 定义棋盘权威状态模型：格子坐标系、格子种类（可落子格 / 障碍 / 出生区归属 / 信物格标记）、占用信息（所有者 + 棋子类型）。
- 定义四邻接关系，并把它确立为全项目唯一的邻接语义（连接、气、覆盖、围杀共用）。
- 定义棋串：同一玩家、四邻接相连的全部棋子，跨棋子类型合并为同一棋串并共享气。
- 定义气：与棋串四邻接的可落子空格集合；障碍与棋盘外沿一律视为封堵边界。
- 定义地图数据格式：地形、障碍、出生区、信物格位置由设计师固定，随机性只作用于信物内容（由 `add-relic-system` 消费）。
- 提供 4 人基准地图（外接约 11×11、100–115 可落子格、4 个出生区、约 14 个信物格）及 2/3 人地图的预算约束。
- 定义地图静态校验规则：格数预算、障碍占比 8%–12%、出生区容量、出生区到公共信物/中央入口/咽喉的最短落子距离均衡、禁止出生区必死口袋。

## Acceptance Criteria

每条对应 `openspec/changes/add-board-core/specs/` 中的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **board-topology** / 坐标记法
- [ ] **board-topology** / 棋盘格子状态模型
- [ ] **board-topology** / 四邻接是唯一邻接语义
- [ ] **board-topology** / 棋串构成
- [ ] **board-topology** / 气的计算
- [ ] **board-topology** / 盘面查询接口
- [ ] **board-topology** / 盘面状态可序列化且确定性
- [ ] **map-definition** / 地图为设计师固定的静态数据
- [ ] **map-definition** / 人数适配预算
- [ ] **map-definition** / 4 人基准地图
- [ ] **map-definition** / 地图静态校验规则
- [ ] **map-definition** / 出生区归属与共享

- [ ] `openspec validate add-board-core` 通过
- [ ] `openspec/changes/add-board-core/tasks.md` 对应的实现清单（见 `implement.md`）全部完成
- [ ] 新增测试全部通过，且已纳入持续回归

### 能力覆盖

- `board-topology` — 7 条 Requirement / 18 个 Scenario：`openspec/changes/add-board-core/specs/board-topology/spec.md`
- `map-definition` — 5 条 Requirement / 11 个 Scenario：`openspec/changes/add-board-core/specs/map-definition/spec.md`

## Dependencies

前置任务：无（依赖根）

- 前置 change：无。本 change 是全部后续 change 的依赖根。
- 下游依赖：`add-batch-deployment`（合法性预演与提子）、`add-territory-power`（覆盖与领地）、`add-relic-system`（信物格位）、`add-match-flow`（出生区限制与终局条件 3）、`add-heuristic-ai`（盘面查询）、`add-tactical-ui`（气层与领地层渲染）。
- 引入"对局种子"概念的占位：本 change 只要求棋盘与地图完全确定性，不产生任何随机数。

## Out of Scope

- 不含落子、提子与合法性判定 → `add-batch-deployment`。
- 不含覆盖、领地归属与势力计分 → `add-territory-power`。
- 不含信物内容生成与控制判定 → `add-relic-system`。
- 不含出生区限制的生效时机（前三大回合） → `add-match-flow`。
- 不实现 2 人、3 人正式地图（设计文档 §18.2），本轮只固化其预算约束供后续使用。
- 不含任何渲染、美术资源或坐标到屏幕的映射 → `add-tactical-ui`。
