# 10-20-cycle-stop-1

> 规格权威：`openspec/changes/cycle-stop/`（proposal 裁决 C1–C6；design D1 诊断、S1 / S2 缺省；tasks 第一段 1.1–1.3；specs `match-telemetry`「循环提子诊断」）。本任务只做**第一段**（1.1–1.3），不改任何对局行为；第二段另建任务。

R-3：开放地图上唯一战场，简单 AI 三人在同一片格互相提子循环不终局（`link:1` 种子 43 终端脚本局 75 s 跑 180 个大回合；探针种子 40–52 只有 3 个能终局）。先用只读日志的诊断把循环量化（周期、参与者、净势力、被提格、为何全局盘面不重复），给第二段的 K 与"候选全是提子"判定提供数据。

## Acceptance Criteria
- [ ] tasks 1.1–1.3 勾选，证据记本目录 `notes.md`
- [ ] `analyze --review-cycles` 显式开关与「循环提子诊断」段，三条 Scenario 有同名测试；只读日志，不改任何决策或随机消费
- [ ] 对 open-map 段 A 冒烟 `sim-out/open-map/smoke/link1/`（20 局正常终局）跑诊断：循环计数 0（不误判）
- [ ] 诊断批次 ≤ 10 局：`run --map link:1 --difficulty Easy --seed 40 --count 8 --turn-limit 600 --out sim-out/cycle-stop/easy` + 终端脚本种子 43 局面一局；报告 `diagnosis.md`：截断率、循环局数、每局周期 / 参与者 / 净势力变化 / 被提格数 / 别处新增落子数、"全局盘面不重复"归因
- [ ] 回传含 K 的建议值（连续无净增大回合数分布）与判定是否需收窄的建议
- [ ] 5 份 `decisions-link1-*` 基线逐字节不变；`dotnet build` 0 警告、全量测试 0 失败

约束：子 agent 不得 git commit / archive；只跑 ≤ 10 局、不跑批量；同一时间只跑一个 dotnet；理解代码优先 codegraph；只动 `src/Siege.Sim`（分析与命令行）与测试，不动 Core 决策、AI、Presentation、Godot；不改 `openspec/` 规格正文；发现与 design 冲突先记 notes.md 再按 S1 / S2 继续。
