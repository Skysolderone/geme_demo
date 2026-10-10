# 10-10-board-link-mode-c

> 规格权威：`openspec/changes/board-link-mode/`（proposal 裁决表含 A4 / B1 / B2 / B5 延后项与 C1–C6；design D3 / D4 / D8 / D10；specs：`simulation-harness`、`match-telemetry`、`ai-decision`、`map-selection`「批量入口缺省」、`match-setup`「原型插旗替代路径」中冒险概率废除部分）。

段 C：Sim 与 AI。对应 tasks.md 3.0–3.6（3.0 只做 C1 范围：Sim 四个入口；Core 地图解析与图形版留段 D）。

段 B 接线点：`MatchSession.Create` 解析到 `link:` 时以 `Mode = Link` 建局并挂接配对来源、在配对结果上注明来源；日志转写 `Pairings` / `Links` / `Settlements`、`PlayerStates` 的出局原因 / 落败势力 / 等待大回合数、`LockedFlagCounts`、`Result.Reason`。

## Acceptance Criteria

- [ ] tasks.md 3.0–3.6 勾掉（3.0 按 C1 范围），每个 Scenario 有可追溯测试
- [ ] Legacy 对局日志与黄金哈希逐字节不变；全部既有测试断言不改（必须改先停下报告）
- [ ] 图形版（Godot）仍能以旧地图建局：段 C 不改 `MapCatalog.Resolve` 的旧标识转交行为，不改 Presentation / Godot
- [ ] `dotnet build` 零警告、`dotnet test -c Release` 全绿
- [ ] 20 局冒烟报告写进 implement.md

约束：子 agent 不得 git commit；冒烟只跑 C6 规定的 20 局，不跑别的批量；同一时间只跑一个 dotnet；理解代码优先 codegraph。
