# 09-27-expert-lookahead

> 规格权威：`openspec/changes/expert-lookahead/`（design.md 含裁决记录）。

## Goal

新增第四档难度「专家」：取前 W = 4 个候选批次，预演后用标准启发式（单条贪心、零随机）模拟下一名对手的回应，按回应后局面重打分选择；停手在前瞻前；模拟对手只用公开信息（部署上限数量的普通子、未揭示信物按期望）。三档旧难度逐步不变。

## 分段（顺序）

| 段 | tasks.md 组 | 内容 |
|---|---|---|
| A | 1 | Core 前瞻、公平信息纯函数抽取、守门、确定性与性能 |
| B | 2 | 入口（终端 / Godot 四档选择 / CLI 严格解析）、遥测、设计文档 v1.16、20 局冒烟、全量回归 |

## Acceptance Criteria

- [ ] 全部 Scenario 先红后绿；守门变异逐条红
- [ ] 简单 / 标准 / 高难逐步不变；W = 1 时专家与高难逐步相同
- [ ] 信息边界守门覆盖新代码；耗时 ≤ 4 × 高难（超过停下报告）
- [ ] 零警告、全绿、`openspec validate expert-lookahead --strict` 通过

## 子 agent 约束

不得 git commit；不跑 200 局（含 200 局规模慢测试）；codegraph 优先；不读大文件 / 媒体回传；用 `python`；同一时间只跑一个 dotnet；不碰 `.claude/worktrees/`；不写真实 `%APPDATA%\Siege`。
