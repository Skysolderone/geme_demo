# 10-15-retire-legacy-rules

> 规格权威：`openspec/changes/retire-legacy-rules/`（proposal 裁决表 R1–R12；design D1–D10；tasks 五组；specs：ai-decision、capture-resolution、information-visibility、map-definition 均为 MODIFIED）。迁移清单：本目录 `migration.md`。

删除 `MatchMode` 与全部 Legacy 规则分支，只服务手写 9×9 夹具的测试按清单迁到链接载体（(a) 规则层 / (b) 链接夹具）或随旧规则删除（(c) / (j)），L17 Sim 读模式分支与 L18 两条旧校验一并清理。

## Acceptance Criteria
- [ ] tasks 1.1–5.6 勾选；每组单独提交
- [ ] 15 份基线（5 条链接图决策序列 + 5 份 `*-omniscient.txt` + 5 份 `*-k0.txt`）全程逐字节通过
- [ ] 测试净减 33 条（第 4 组 −2 + 1），逐名对上清单；(a) 类期望逐字未改；(b) 类重导逐条记入清单第 6 节
- [ ] Grep `MatchMode`、`MatchFixtures` 在 `src/`、`tests/` 为零；链接存档 / 日志首部 / 地图导出与改前逐字节相同
- [ ] `dotnet build` 零警告、全量测试全绿、Godot 四条自检退出码 0

约束：子 agent 不得 git commit / archive；全程不跑批量；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改链接模式行为、AI 权重与阈值、图形版代码；(c) / (j) 名单有增减先报负责人。
