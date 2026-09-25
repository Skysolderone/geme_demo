# 09-25-pass-threshold-first-stone

> 规格权威：`openspec/changes/pass-threshold-first-stone/`。

## Goal

AI 决策开始时正式盘面上没有己方棋子 → 本次决策停手阈值为 0；有子后恢复配置值（80，三档共用）。修复简单难度首回合全员 Pass。

## Acceptance Criteria

- [ ] 三个新 Scenario 先红后绿；变异两条逐条红；既有停手阈值测试全绿
- [ ] 黄金哈希分叉按 AI 行为变更重建并探针归因
- [ ] 简单难度 v5 20 局冒烟：首回合全员 Pass 局数报告（期望 0）
- [ ] 设计文档 v1.11；零警告、全绿、`openspec validate` 通过

## 子 agent 约束

不得 git commit；不跑 200 局（含 200 局规模慢测试）；codegraph 优先；不读大文件 / 媒体回传；用 `python`；同一时间只跑一个 dotnet；不碰 `.claude/worktrees/`。
