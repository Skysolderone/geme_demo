# 09-25-small-maps

> 规格权威：`openspec/changes/small-maps/`。

## Goal

两张手工标准档内置图：`siege-2p-base-v1`（C2）与 `siege-3p-base-v1`（竖直中轴镜像）；接入内置表、选图界面；三个入口参赛人数缺省取地图人数上限。

## 分段

| 段 | tasks.md 组 | 内容 |
|---|---|---|
| A | 1 | 2 人图 + 入口接线 + 自检截图 + 20 局冒烟 |
| B | 2 | 3 人图 + 自检截图 + 20 局冒烟 + 文档 + 回归 |

段间负责人过目截图。

## Acceptance Criteria

- [ ] 新 Scenario 先红后绿；对称 / 公平守门变异逐条红
- [ ] 两图通过静态校验与人数预算；4 人图行为不变
- [ ] Godot 三条自检退出码 0，截图交负责人
- [ ] 每图 20 局冒烟只报告；零警告、全绿、`openspec validate small-maps --strict` 通过

## 子 agent 约束

不得 git commit；不跑 200 局；codegraph 优先；不读大文件 / 媒体回传（截图只存盘）；用 `python`；同一时间只跑一个 dotnet；不碰 `.claude/worktrees/`。
