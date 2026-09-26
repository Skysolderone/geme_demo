# 09-26-carry-in-out

> 规格权威：`openspec/changes/carry-in-out/`（design.md 含裁决记录）。

## Goal

补给带入 + 战利品带出：本地档案（补给点与库存）、开局至多带入 1 件补给（备用子 / 征召签 / 换型令）、局终按名次带出；弃赛 50%（保护期内为 0）并返还补给；出局丢失；AI 同预算随机带入；批量跑局缺省关闭。

## 分段（顺序，每段一个 implement，主会话前台复核提交）

| 段 | tasks.md 组 | 内容 |
|---|---|---|
| A | 1 | Core 规则、补给效果、结算、对局配置与存档兼容 |
| B | 2 | 档案读写、AI 带入、终端 |
| C | 3 | Godot 两个面板、遥测、设计文档 v1.15、20 局冒烟、全量回归 |

## Acceptance Criteria

- [ ] 全部 Scenario 有测试且先红后绿；守门变异逐条红
- [ ] 带入关闭时对局与改动前逐步相同；旧存档 / 旧日志按无带入读取
- [ ] 零警告、全绿、`openspec validate carry-in-out --strict` 通过

## 子 agent 约束

不得 git commit；不跑 200 局；测试不得读写真实用户档案（用临时目录）；codegraph 优先；不读大文件 / 媒体回传；用 `python`；同一时间只跑一个 dotnet；不碰 `.claude/worktrees/`。
