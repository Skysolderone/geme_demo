# 09-25-flag-contest

> 规格权威：`openspec/changes/flag-contest/`。

## Goal

原型插旗路径中，未由人指定的玩家依次选区、看得到此前的旗，以冒险概率 p（缺省 15%）加入已有人的出生区；独立子流 `FlagRisk`；p = 0 时锁定结果逐项不变。配置写日志首部，CLI `--flag-risk`，分析报告同区统计。

## Acceptance Criteria

- [ ] 新 / 改写 Scenario 先红后绿；变异逐条红；p = 0 逐项不变
- [ ] 依赖走法的测试写死 p = 0，不挑种子凑绿
- [ ] v5 Standard 20 局冒烟报告同区对局数等
- [ ] 设计文档 v1.12；零警告、全绿、`openspec validate flag-contest --strict` 通过

## 子 agent 约束

不得 git commit；不跑 200 局（含 200 局规模慢测试）；codegraph 优先；不读大文件 / 媒体回传；用 `python`；同一时间只跑一个 dotnet；不碰 `.claude/worktrees/` 与未跟踪的 `src/godot/scripts/*.cs.uid`。
