# 10-13-ai-pass-review

> 规格权威：`openspec/changes/ai-pass-review/`（proposal 裁决表 P1–P6 与排序变更；design D1–D3；tasks 第 1 组）。

第一段：只加诊断、不改行为。Core 复盘入口（复用正式候选 + 预演 + 评价）→ Sim `analyze` 两段报告（战场落败复盘、等待者评估）→ 回归 → 20 局诊断批次只报告。第二段修法待负责人按诊断数据从 P5 裁决。

## Acceptance Criteria
- [ ] tasks 1.1–1.5 勾选
- [ ] 链接图 5 条决策序列基线与旧日志报告逐字节不变
- [ ] `dotnet build` 零警告、`dotnet test -c Release` 全绿
- [ ] 诊断批次报告写进 implement.md

约束：子 agent 不得 git commit；批量只跑 1.5 那一批 20 局；同一时间只跑一个 dotnet；理解代码优先 codegraph；不改 AI 决策、停手阈值、规则、图形版。
