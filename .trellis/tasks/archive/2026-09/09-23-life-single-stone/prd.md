# 09-23-life-single-stone

> 规格权威：`openspec/changes/life-single-stone/`。

## Goal

单子棋串（1 枚子）不受活形保护：眼值之和 ≥ 2 时也只判"未定"。改动只在 `LifeShapeReport` 三态判定一处。

## Acceptance Criteria

- [ ] 「活形三态」四个新 Scenario 有测试且先红后绿；变异两条逐条红
- [ ] 既有夹具只改盘面不改期望，逐条记录；性质测试种子 1–200 无反例
- [ ] 黄金哈希按规则变更重建并归因；v5 50 局复核数字与实验 K1 并列
- [ ] 设计文档 §6.4 同步；零警告、全绿、`openspec validate life-single-stone --strict` 通过

## Out of Scope

贴地形小空区眼（K2 / K3）；眼值表 / 上限；AI 校准（ai-eye 段 D）。

## 子 agent 约束

不得 git commit；codegraph 优先；不读大文件 / 媒体回传；用 `python`；不碰 `.claude/worktrees/`。
