## Context

活形判定在 `Board/LifeShape.cs`。空区划分与封闭判定复用 `Adjacency.LibertyNeighbors`（气边），所以栅栏既把空区切开，又被当作墙。AI 的眼位维度与安全维度都读活形结果；立栅之所以加权分最高，就是因为同时吃到了这两维（`ai-terrain-review`）。裁决见 proposal（F1–F8）。

约束：
- 单一实现（`.trellis/spec/core/boundaries.md`）：气边只有 `LibertyNeighbors` 一处，新的眼边也只能有一处；
- 确定性；不跑 200 局，每个配置 ≤ 20 局；
- 同一时间只跑一个任务：`stagnation-trace` 归档后才实施。

## Goals / Non-Goals

**Goals：** 活形判定无视栅栏；其余规则逐字节不变；AI 不调权重；冒烟量化立栅占比的变化。

**Non-Goals：** 见 proposal 的 Non-goals。

## Decisions

### D1 眼边作为第二种导出关系

在几何邻接之上新增"眼边邻格"查询，与 `LibertyNeighbors` 并列、同处实现：条件是两格都可落子、且 |Δh| ≤ 1，不查栅栏。`LifeShape` 的空区 BFS、封闭判定、眼空间归属、方四成环判定全部改用眼边；棋串连通与气仍用气边。

- 备选：在 `LifeShape` 里临时绕过栅栏，不新增查询。不选，因为会违反"邻接遍历只有一处"。

### D2 眼空间归属

"空区与棋串有眼边相连"的棋串，就算这个空区的眼空间所属。会出现一种情况：某棋串只隔着栅栏贴着空区，空区不是它的气，却是它的眼空间。接受这种情况：活形只决定"禁入"和"不得破坏他人活形"，不决定提子；已确定活形受"他人不得破坏"保护，所以不会出现"活棋被提"的矛盾。实施时用测试钉住。

### D3 受影响的下游

`ForbiddenMoves`（禁手打叉，经预演结算）、`BatchPreview`、`GroupSafety`、禁入格与"不得破坏他人活形"的判定，全部经由 `LifeShape` 间接变化，不应另写判定。实施时用 codegraph 列出 `LifeShape` 的全部调用者，逐个确认没有自带栅栏逻辑。

### D4 AI 与基线

AI 评价版本加 1。Safety / Eye / Threat 三维的校准口径补注"未校准（fence-eye-rule 后）"，权重值不变。`link:1` 决策序列基线与依赖 AI 走法的黄金值按"规则变了"重钉，逐项说明原因；只依赖地图的黄金值 MUST 不变，作为反面对照。

## Risks / Trade-offs

- [栅栏阵主题失去造眼价值，变弱] → 冒烟看按主题统计；栅栏仍挡气、切串、致提，价值没有归零。
- [AI 在新规则下改为不改造，匠人退化成普通子] → 冒烟统计改造次数与动作分布；如果改造率大幅下降，登记给后续 AI change。
- [对局变长：造眼变难，活棋变少，提子变多] → 冒烟对比大回合中位（`link-map-surfaces` 冒烟为 25）；超过 30 时停下回负责人。
- [循环局：立栅不再造眼，但同形键仍含改造段] → 冒烟统计停滞停手次数（`stagnation-trace` 提供 `D:stall`）。

## Migration Plan

规则一步落地，没有数据迁移。回滚只需回退本 change 的提交。

## Open Questions

无。F1–F8 已裁决。
