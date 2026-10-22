# expert-opponent-model（修法 B：专家对手模型对齐）

需求、裁决（1–8，负责人 2026-10-22 全按推荐，派发"一段串行"）与验收以 `openspec/changes/expert-opponent-model/` 为准（proposal / design / tasks / specs 差分）。
专家前瞻模拟下一名对手时，改用专家本人生效的搜索配置派生的高难口径（预设 N 24、M 1 不扰动、同一停手阈值与候选格上限），不读对手配置，信息边界不变；AI 评价版本 8 → 9。
依据：`.trellis/tasks/10-21-expert-diagnosis/report.md`、`.trellis/tasks/archive/2026-10/10-21-expert-tiebreak/sweep.md`。

## 验收

- openspec 差分中新增 / 改动的 Scenario 全部有测试，新守门做过变异验证。
- 5 份 `decisions-link1-*` 基线、链接图 / 严格提高 / 小图黄金哈希、`构筑期专家与高难逐步相同` 不变。
- 专家 G1（种子 1–3）与前瞻记录黄金值（种子 6）重钉，`rebaseline.txt` 逐种子写首个分歧（须落在已前瞻决策、模拟回应键不同）。
- 两条门控（预演次数代理、计时）≤ 4，数字记入 `implement.md`。
- `dotnet build siege.sln -c Release` 0 警告；全量 `dotnet test` 退出码 0。
