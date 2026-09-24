# 09-24-superko-occupancy

> 规格权威：`openspec/changes/superko-occupancy/`。

## Goal

盘面同形禁则的比对键忽略棋子类型（每格占用者 + 桥 / 栅栏 / 地表），堵住"换类型绕过超级劫"的多人互提循环；序列化与存档格式不变。复跑简单 / 标准难度 v5 200 局。

## Acceptance Criteria

- [ ] 新 / 反转 Scenario 有测试且先红后绿；比对键投影只有一处实现；变异逐条红
- [ ] 存档往返保持绿；黄金哈希按规则变更重建并归因
- [ ] 简单 / 标准难度 v5 200 局与 ai-eye 数据并列
- [ ] 设计文档 v1.10；零警告、全绿、慢测试全过、`openspec validate superko-occupancy --strict` 通过

## 子 agent 约束

不得 git commit；codegraph 优先；不读大文件 / 媒体回传；用 `python`；同一时间只跑一个 dotnet；不碰 `.claude/worktrees/`。
