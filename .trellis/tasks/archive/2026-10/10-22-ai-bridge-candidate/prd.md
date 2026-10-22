# ai-bridge-candidate（AI 路线价值修法：搭桥候选保留位）

需求、裁决（1–9，负责人 2026-10-22 全按推荐，1A 改判为候选生成修法）与验收以 `openspec/changes/ai-bridge-candidate/` 为准。诊断依据：`.trellis/tasks/10-22-route-value-diagnosis/research/report.md`。

## 验收
- 规格差分 Scenario 全有测试；新守门做过变异验证。
- 开关 0：全部决策基线与黄金值逐步不变；简单基线开关 1 也不变。
- 受影响基线 / 黄金值重钉，`rebaseline.txt` 逐份写首个分歧（被选中的是搭桥候选）。
- 专家门控两条 ≤ 4；sln Release 0 警告；全量 `dotnet test` 退出码 0。
