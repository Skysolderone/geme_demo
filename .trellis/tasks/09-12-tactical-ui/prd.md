# 09-12-tactical-ui

> 规格权威：`openspec/changes/add-tactical-ui/`（proposal + specs + design）。本文件不复制规格原文，只承载目标、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §13, §14, §20

## Goal

《围杀 Siege》的全部深度都建立在"玩家能看清盘面"之上：一次批次同时补气、成线、围杀、抢信物，玩家必须在确认前看到这四件事各自的后果；高分棋串是公开弱点，对手必须能看清它的连接点在哪。如果信息层做不到，这套规则在玩家眼里就只是"随机掉分"。

同时，信息公开边界（§13）不只是 UI 问题，它是公平性的一部分：哪些信息始终公开、哪些必须隐藏，决定了正式 AI 的读取边界（§15.1），也决定了玩家之间的博弈是否对称。

本 change 对应设计文档 §13（信息公开规则）、§14（信息界面）、§20（视觉风格基准）。

## Background / 确认事实

- 玩法设计已于 v1.0 确认并冻结，本任务不重新设计玩法，只把已确认规则落成可执行、可测试的实现。
- 规格保持实现无关：`openspec/` 下的 spec 不指定语言、引擎、框架；技术栈决策属于本 trellis 任务层。
- 该 change 对应的 37 条设计歧义已全部裁决，记录在 `openspec/changes/add-tactical-ui/design.md` 的「裁决记录（已确认）」一节，实现时 MUST 遵循。
- 全局硬约束见 `openspec/ROADMAP.md`：单一四邻接、一切实时重算、随机可复现、围棋记法坐标。

## Requirements

- 固化信息公开边界：始终公开的七类信息与必须隐藏的四类信息。
- 实现批次预演显示：落点与手牌消耗、部署上限与已用数、预计被围杀的棋串、己方棋串与气与自杀风险、各棋串的"基础军势 + 位置加值 × 倍率"预览、预计势力与排名变化、首次进入覆盖范围但仍未知的信物格显示"将揭示"而不提前显示内容。
- 实现非法批次的原因说明与相关落点/棋串高亮。
- 实现五种战术信息层：领地层、气层、势力层、信物层、顺序层；按住战术视图键进入临时信息模式，松开返回默认棋盘；键鼠与手柄可分别配置；辅助设置允许把"按住显示"改为"点击切换"。
- 实现信息层在批次部署与其他玩家行动时均可查看，且不改变任何游戏状态。
- 实现全玩家手牌信息面板：自己看类型/准确数量/槽位占用/本轮新征募未提交数量；对手只看类型；同时显示每名玩家公开的展示数、选取数、手牌槽、部署上限及其信物来源；弃赛玩家保留最后公开类型并标记不再行动。
- 固化视觉风格基准：明快低多边形奇幻棋盘、四方势力配色与轮廓区分、方格边界始终清晰、五种棋子的轮廓语言、深色半透明 UI 面板、批次预览的半透明发光与克制的提子提示、信息层的降饱和处理。

## Acceptance Criteria

每条对应 `openspec/changes/add-tactical-ui/specs/` 中的一条 Requirement；验收 = 该 Requirement 下的全部 Scenario 都有通过的自动化测试。

- [ ] **batch-preview** / 批次预演必须显示的信息
- [ ] **batch-preview** / 未知信物在预演中只显示"将揭示"
- [ ] **batch-preview** / 非法批次必须说明原因并高亮
- [ ] **batch-preview** / 预演不改变游戏状态
- [ ] **hand-info-panel** / 全玩家手牌对比面板
- [ ] **hand-info-panel** / 自己区域的显示内容
- [ ] **hand-info-panel** / 敌方区域的显示内容
- [ ] **hand-info-panel** / 公开结构参数与信物来源
- [ ] **hand-info-panel** / 弃赛玩家的面板表现
- [ ] **information-visibility** / 始终公开的信息
- [ ] **information-visibility** / 必须隐藏的信息
- [ ] **tactical-layers** / 五种战术信息层
- [ ] **tactical-layers** / 信息层一次只显示一层
- [ ] **tactical-layers** / 默认棋盘上的未发现信物
- [ ] **tactical-layers** / 信息层的进入与退出
- [ ] **tactical-layers** / 信息层的可用时机与无副作用
- [ ] **visual-style-baseline** / 视觉方向基准
- [ ] **visual-style-baseline** / 阵营区分不得只依赖颜色
- [ ] **visual-style-baseline** / 方格边界与合法落点始终清晰
- [ ] **visual-style-baseline** / 五种棋子的轮廓语言
- [ ] **visual-style-baseline** / UI 风格约束
- [ ] **visual-style-baseline** / 批次预览与信息层的视觉表现

- [ ] `openspec validate add-tactical-ui` 通过
- [ ] `openspec/changes/add-tactical-ui/tasks.md` 对应的实现清单（见 `implement.md`）全部完成
- [ ] 新增测试全部通过，且已纳入持续回归

### 能力覆盖

- `batch-preview` — 4 条 Requirement / 10 个 Scenario：`openspec/changes/add-tactical-ui/specs/batch-preview/spec.md`
- `hand-info-panel` — 5 条 Requirement / 9 个 Scenario：`openspec/changes/add-tactical-ui/specs/hand-info-panel/spec.md`
- `information-visibility` — 2 条 Requirement / 7 个 Scenario：`openspec/changes/add-tactical-ui/specs/information-visibility/spec.md`
- `tactical-layers` — 5 条 Requirement / 15 个 Scenario：`openspec/changes/add-tactical-ui/specs/tactical-layers/spec.md`
- `visual-style-baseline` — 6 条 Requirement / 8 个 Scenario：`openspec/changes/add-tactical-ui/specs/visual-style-baseline/spec.md`

## Dependencies

前置任务：`09-12-match-flow`

- 前置 change：全部六个规则 change（`add-board-core`、`add-batch-deployment`、`add-territory-power`、`add-relic-system`、`add-recruit-hand`、`add-match-flow`）。
- 与 `add-heuristic-ai` 并列：两者共享同一套"公开信息视图"。本 change 定义视图的内容边界，AI change 消费它。
- 视觉基准图：`art/style-exploration/siege-style-03-low-poly-fantasy.png`（设计文档 §20）。该图用于确定材质、色彩、轮廓与信息层级，不代表最终地图布局或固定 UI 尺寸。

## Out of Scope

- 不指定 UI 框架、渲染技术、分辨率或具体控件实现；本 change 只定义必须呈现什么、必须隐藏什么、以及交互的语义。
- 不实现正式美术资源与演出（设计文档 §18.2 明确排除超出玩法验证所需的部分）。
- 不实现动画、镜头与音效。
- 不实现联网状态下的观战视角与延迟处理。
- 不实现 AI 的决策展示或调试可视化（人工接管入口由 `add-heuristic-ai` 提供，本 change 只约定它可从调试界面触达）。
