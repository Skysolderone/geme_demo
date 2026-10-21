# stagnation-trace 质检记录（2026-10-21）

## 结论速览

| # | 项 | 结论 |
|---|---|---|
| 1 | 决策不变 | 通过：Core 只改 `HeuristicTurnController` 一处三元式 + `StallDecision` 常量；`EvaluationBreakdown.Version` 未动。实跑佐证见下"跑局比对" |
| 2 | 留痕路径 | 通过：a) `LoggingController.Heuristic` 只对 `HeuristicTurnController` / `DebugTurnController.Inner` 非空，人类 / 脚本控制器恒 `Stalled = false`；`Deploy` 两条出口都写 `D:` 条目，不会读到上一小回合的残留；b) 标准种子 1 与 HEAD 逐行相同（去耗时）；c) `MatchLog.Parse` 走 `JsonSerializer` + `LogJson.Options`，`bool?` 往返对称（「停滞停手次数可查」已断言） |
| 3 | 旧日志回放 | 已修复：见下 |
| 4 | 统计 | 通过（实现）；规格措辞与实现口径差异写回传，未改规格 |
| 5 | 基线 | 通过：脚本复核 5 份与 HEAD 只差第 3 / 5 行；简单种子 1 另有第 45 / 49 / 53 行 `D:pass` → `D:stall`；新旧均 0 个 CR |
| 6 | 测试 | 通过：「停滞停手留痕」断言 `LastStagnation` 非空（走停滞路径而非阈值路径）、K = 0 份为 null，高阈值构造写回传裁决；全量重跑见下 |
| 7 | 文档 | 已修复 ROADMAP 分组 / 括注 4 处；HANDOFF 1b、设计文档 §17 与变更记录补回放兼容一句 |
| 8 | 构建与测试 | 见下 |

## 3. 旧日志回放（已修复）

- 核实：分歧来自 `Replayer.Compare` 逐行比 `DeterministicText()`（只剥耗时），回放会多写 `"Stalled":true`；**不是决策分歧**。`LogReplay.SameMove`（`--review-defeats` / `--review-artisan` 用）只比行动玩家 / 大回合 / Pass / 落子 / 提子，本就不受影响。
- 规格「可复现回放」要的是"征募候选、选择、批次与结算结果完全一致"，未被破坏；但 `replay` 会对一局完全相同的旧日志报"第 N 行分歧"，属工具误报，且与分析"缺字段计 0"口径不一致。
- 修复（`src/Siege.Sim/Running/Replayer.cs`）：`Compare` 先经 `WithoutUnrecordedStalls`——原日志某小回合 `Stalled` 为 null 时，回放同序号小回合的 `Stalled` 不参与比对；反向（原日志 true、回放无）仍报分歧；`ReplayResult.Replayed` 返回未改动的回放日志。不改 `DeterministicText` / 日志格式。
- 守门：`SimulationHarness/可复现回放Tests.旧日志缺停滞停手字段按未停手回放`（`link:1` 简单、K = 1、写死定值前权重、80 小回合；剥掉全部 `Stalled` 得旧式日志 → `Replay` 逐行一致、返回日志仍含 `Stalled`；新日志照常一致；`Compare(原, 旧式)` 不一致且分歧行含 `"Stalled"`）。
- 变异 M-RP1：`Compare` 直接比 `actual`（去掉归一化）→ 红 1（"回放在第 165 行分歧"，即实施方所说的首个停手小回合分歧，顺带证实其说法）；还原（`cmp` 一致 + touch）后 `~可复现回放` 15 / 15。

## 跑局比对（共 4 局，输出在 scratchpad，未进 sim-out）

HEAD c4df89d 用临时 worktree 构建（用完已 `git worktree remove`）；比对脚本剥 `ElapsedMs` / `TotalMs` / `MajorRoundMs`。

1. HEAD `run --map link:1 --difficulty Standard --seed 1 --count 1 --turn-limit 600 --serial`
2. 新代码同参数 → 352 行逐行相同，0 个 `Stalled`，`config.json` 逐字节相同（2b：不含停手的日志不变）。
3. HEAD `--difficulty Easy --seed 40 --count 1` → 与实施方 `sim-out/stagnation-trace/easy/match-0000000000000028.jsonl`（新代码）比：955 行，53 行只差 `,"Stalled":true`，首部只差 `Config.Count`（1 vs 8），其余逐行相同（1：决策不变的强证据；53 = 实施方报告的 0x28 停手数）。
4. 新代码（含修复）`replay --file` 第 3 局的 HEAD 日志 → "回放一致：956 行逐字节相同"，退出码 0。

## 7. ROADMAP 改正（对照 archive proposal 与设计文档变更记录）

- 版本号 v1.1–v1.30 逐条与变更记录核对，全部正确；57 个归档 change 全部列到。
- 改 1：首轮数值组括注"据点、固定轮数终局等后被 restore 移除"→ 按 restore-go-core-rules 变更记录"被回退的已归档 change"改为"倍率封顶、大回合上限、势力碾压、落后者征募补偿、据点等被回退（growth-pass-1 分阶段部署上限、scoring-sites 高地压制加值保留）"。
- 改 2：`merge-board-layer`（战术信息层合并）、`board-coordinates`（3D 棋盘坐标标注）是界面 change，原放在"地图"组并括注"后被 board-link-mode / retire-legacy-maps 删除"，不实 → 移到"表现与工具"，不再写删除。
- 改 3：`board-map`（v1.19）是棋盘档生成图（地图 change，后被 retire-legacy-maps 删除），原放在"表现与工具" → 移入新拆出的"旧地图族"条，与 denser-map / frontier-map / map-generator / small-maps 同列。
- 改 4：`ai-turn-speed` 是 AI 决策提速（决策逐步不变），从"表现与工具"移到 AI 组。
- 核对无误未改：`show-sound-cues` = 程序合成占位音；`strict-cli` = 命令行严格校验；`final-countdown` 被 open-map 删除；专家"好 7 / 同 33 / 差 20"与"落子更少、Pass 更多"出自 expert-strength design；`map-elements-v2` 0/17；T13、P14 出处。

## 8. 构建与全量

（见下方追记）

追记：
- `dotnet build siege.sln -c Release`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`（后台，含「停滞停手次数可查」写死权重补丁与本次回放修复之后）：**2099 条，通过 2093，跳过 6，失败 0，退出码 0**。HANDOFF 测试数已同步。
- `git diff --stat HEAD -- src/godot` 为空，未跑 Godot 自检。
