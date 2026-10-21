# 10-21-stagnation-trace

> 规格权威：`openspec/changes/stagnation-trace/`（proposal 裁决 G1–G5；design D1–D4、S1–S2；tasks 1.1–3.1；specs `ai-decision`「循环提子停手」、`match-telemetry`「循环提子诊断」）。本任务做 **1.1–3.1**；3.2（归档）、提交与推送由负责人线做。

小收尾：停滞停手成立且最终 Pass 时决策条目记 `D:stall`（AI 决策不变），`analyze --review-cycles` 报告停手次数；5 份决策序列基线首部删掉已删除的配对 / 链接字样（决策行逐字节不变）；ROADMAP 按现状重写；HANDOFF 第 8 项与 1b 更新；设计文档 v1.31。

## Acceptance Criteria
- [ ] tasks 1.1–3.1 勾选，证据记本目录 `notes.md`
- [ ] 两条新 Scenario 有同名测试；决胜一手接管时不记 `D:stall`；变异验证
- [ ] AI 评价版本不变；除 `D:pass` → `D:stall` 文案外，决策与随机消费逐步相同
- [ ] 5 份基线与 HEAD 相比只有首部两行不同（若第 12 大回合内出现 `D:stall` 单列）；核对输出存本目录
- [ ] 缺省 `analyze` 报告逐字节不变；实测一批简单局停手次数 > 0（≤ 8 局）
- [ ] ROADMAP、HANDOFF、设计文档 v1.31 同步
- [ ] `dotnet build siege.sln -c Release` 0 警告；全量 0 失败、Skip 仍 6；Godot 构建 0 警告（不必跑自检，Godot 未改则写明）

约束：子 agent 不得 git commit / archive / push；跑局 ≤ 8；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改 K、停手阈值、评价权重与任何决策；不改 `openspec/` 规格正文；不动飘字动效。
