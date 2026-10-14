# 10-14-ai-terrain-review

> 规格权威：`openspec/changes/ai-terrain-review/`（proposal 裁决表 T1–T7；design D1–D4；tasks 第 1 组）。

第一段：只加诊断、不改行为。Core 改造诊断入口 → Sim `analyze --review-artisan` 与「主题表现」→ 回归 → 主题批次 20 局 + 权重对照 6 档 × 10 局（合计 80 局，负责人确认）只报告。第二段待负责人按数据从 T6 裁决。

## Acceptance Criteria
- [ ] tasks 1.1–1.6 勾选
- [ ] 链接图决策序列基线与旧报告前缀逐字节不变
- [ ] `dotnet build` 零警告、`dotnet test -c Release` 全绿
- [ ] 诊断数据写进 implement.md

约束：子 agent 不得 git commit；批量只跑 1.5 / 1.6 两批（共 80 局）；同一时间只跑一个 dotnet；理解代码优先 codegraph；不改 AI 决策、权重缺省值、阈值、规则、地形生成、图形版。
