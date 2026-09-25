# 09-25-more-pieces-relics

> 规格权威：`openspec/changes/more-pieces-relics/`（design.md 含裁决记录 1–11）。

## Goal

新增四种棋子（旗手 / 铁链 / 哨兵 / 界碑，只给位置加值）与四种信物（连营 / 犄角 / 驿站 / 工坊），对局内容集 v1 / v2 兼容旧存档；接入征募、生成、AI、终端、Godot、遥测。

## 分段（顺序，每段一个 implement，主会话前台复核提交）

| 段 | tasks.md 组 | 内容 |
|---|---|---|
| A | 1 | 内容集配置 + 四种棋子规则与计分 |
| B | 2 | 四种信物与生成权重（含工坊） |
| C | 3 | AI 适配 + 终端 + 遥测 |
| D | 4 | Godot 表现 + 设计文档 v1.14 + 20 局冒烟 + 全量回归 |

## Acceptance Criteria

- [ ] 全部 Scenario 有测试且先红后绿；守门变异逐条红
- [ ] 内容集 v1 下对局与改动前逐步相同（依赖走法的期望写死 v1）；旧存档 / 旧日志可读
- [ ] 零警告、全绿、`openspec validate more-pieces-relics --strict` 通过

## 子 agent 约束

不得 git commit；不跑 200 局；codegraph 优先；不读大文件 / 媒体回传；用 `python`；同一时间只跑一个 dotnet；不碰 `.claude/worktrees/`。
