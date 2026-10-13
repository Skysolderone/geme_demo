# 10-13-retire-legacy-maps

> 规格权威：`openspec/changes/retire-legacy-maps/`（proposal 裁决表 L1–L14；design D1 四步删除顺序与不变量；tasks.md 5 组 26 条）。

删除旧地图（四张内置图与 `maps/*.json`）、`gen:` / `board:` 生成器、冒险概率、地图专属 AI 权重覆盖、`MapCatalog.Legacy` / `RestoreLegacy` / `resolveMap` 等过渡口；公开建局恒为链接模式。L6 = C：Legacy 规则分支暂留只服务手写夹具，登记后续 `retire-legacy-rules`。

## Acceptance Criteria
- [ ] tasks.md 全部勾选；1.1 测试迁移清单逐条落实，删 / 迁 / 不动的测试与通过数增减逐条对得上
- [ ] 链接图决策序列基线（种子 3 现有 + 新增种子 1 / 2）逐字节不变；链接模式行为不变
- [ ] `dotnet build` 零警告、`dotnet test -c Release` 全绿；三条 Godot 自检与 `--auto-demo --rounds=0 --seed=5` 退出码 0
- [ ] 质检 agent 复核"删除未丢覆盖"

约束：子 agent 不得 git commit；不跑批量；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改地形规则、AI 行为与权重（除删除地图覆盖）、链接模式规则。
