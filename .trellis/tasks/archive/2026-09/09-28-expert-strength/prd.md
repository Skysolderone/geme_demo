# 09-28-expert-strength

> 规格权威：`openspec/changes/expert-strength/`（design.md 含裁决记录）。

## Goal

专家档：前瞻集多样候选（落点格去重 + 排除重跑补足，S = 8）+ 近似两层加分（λ‰ × 下一手最佳单点增量，下限 0）；λ 扫 250 / 500 / 1000，成对种子 3 组扩样与高难比较。

## 分段

| 段 | tasks.md 组 | 内容 |
|---|---|---|
| A | 1 | Core：多样候选与两层加分、G1 / G2 守门、确定性、公平信息、耗时 |
| B | 2 | λ 扫档、扩样验证（共新跑 160 局）、遥测、设计文档 v1.18、ROADMAP、全量回归 |

## 约束

每批 20 局、严禁 200 局；简单 / 标准 / 高难逐步不变；不得 git commit；同一时间只跑一个 dotnet；不写真实 `%APPDATA%\Siege`；不碰 `.claude/worktrees/`；`variants.patch` 的环境变量开关不得进正式代码。
