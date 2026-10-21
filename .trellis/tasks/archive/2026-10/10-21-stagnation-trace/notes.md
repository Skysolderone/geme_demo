# stagnation-trace 实施记录（tasks 1.1–3.1）

## 1.1 `D:stall` 留痕

### 决策条目是否随对局日志写出（codegraph 核实）

- `HeuristicTurnController.Decisions`（`_decisions`）在 `src/` 下**没有任何读取方**（codegraph_callers 无调用方；`src/` 内无 `.Decisions` 引用）；只被测试侧 `LinkDecisionSequence.Export`（决策序列基线导出）与若干单元测试读。
- Sim 的对局日志由 `MatchSession.RecordTurn` 写 `TurnSnapshot`，AI 相关只采 `LastChoice`（Settled 事件的评价值）、`LastCandidates`（Candidates 事件）、`LastLookahead`（快照 `Lookahead`），**不含决策条目**。
- 结论：决策条目不随日志写出 → 按实施裁决 **S1** 在快照加最小字段：
  - `TurnSnapshot.Stalled`（`bool?`，`[JsonIgnore(WhenWritingNull)]`，放在 `Lookahead` 之后、`ElapsedMs` 之前）：只在本小回合部署决策条目为 `D:stall` 时写 `true`，其余为 `null` 不写出 → 不含停手的日志逐字节不变。
  - 采集：`TurnTrace.Stalled`（每小回合 `Reset` 清空），`LoggingController.Deploy` 在内层 `Deploy` 之后读 `Heuristic.Decisions[^1] == HeuristicTurnController.StallDecision`——与条目单一真相源，决胜一手接管的小回合不会被计。

### 实现

- `HeuristicTurnController.Deploy` 末行：`choice.IsPass ? (LastStagnation is null ? "D:pass" : StallDecision) : $"D:{choice.Key}"`；新增常量 `StallDecision = "D:stall"`。
- 早退路径（无候选 / 部署上限 0）在停滞判定之前、`LastStagnation` 已清空，仍记 `D:pass`，未改。
- 不改 `LastStagnation`、候选、评价、随机消费；`EvaluationBreakdown.Version` 未动（AI 评价版本不变）。

### 测试（`tests/Siege.Core.Tests/AiDecision/循环提子停手Tests.cs`）

- 新增「停滞停手留痕」：`CornerCapture` 局面、唯一候选 = 会提子的那口气、同一停滞采样，两份控制器只差 K（3 / 0）。
  为让 K = 0 也落到普通 Pass，两份都把停手阈值写死为 1,000,000（远高于候选收益，样本口径断言 `only.Total <= HighThreshold`）：K = 3 因停滞 Pass → `D:stall`；K = 0 因阈值 Pass → `D:pass`；同一候选、同一评价。
  "此后逐步相同"：同一大回合再决策一次（远处格，停滞不成立），两份末条目与选择相同；把 `D:stall` 换成 `D:pass` 后整列条目相同；扰动子流 `Consumed` 相同（简单难度候选批次数 1）。另加对照：K = 3、势力仍增长 + 高阈值 → 普通 Pass 记 `D:pass`。
  - 解读说明（供负责人确认）：在 `CornerCapture` 上用缺省阈值 20 时 K = 0 会落子（`K为0零变化` 已断言），不是 `D:pass`；为满足 Scenario"同一局面 K = 0 记 D:pass"，两份同时写死高阈值——局面与配置只差 K。
- 「连续无净增且候选全是提子则停手」末行断言 `D:pass` → `D:stall`。
- 「决胜一手优先」：接管时断言条目 = `D:{LastChoice.Key}` 且 `Decisions` 不含 `D:stall`；对照组（停滞成立、无追平手、Pass）断言 `D:stall`。

### 变异验证（脚本 finally 还原 + 二进制比对 + touch；每条只跑 `FullyQualifiedName~循环提子`，15 条）

| 编号 | 改动 | 结果 |
|---|---|---|
| M-TR1 | 条目恒记 `D:pass`（`choice.IsPass ? "D:pass" : …`） | 红 4：停滞停手留痕、连续无净增且候选全是提子则停手、决胜一手优先、停滞停手次数可查 |
| M-TR2 | 去掉 Pass 门（`LastStagnation is not null ? StallDecision : …`，决胜一手接管也记 stall） | 红 1：决胜一手优先 |
| M-TR3 | 不看停滞（Pass 一律记 `D:stall`） | 红 1：停滞停手留痕 |

还原后同过滤重跑：15 / 15 通过，退出码 0。

## 1.2 统计

- `CycleReviewEntry` 加 `Stalls`（整局 `turns.Count(t => t.Stalled == true)`，不限窗口；缺字段的旧日志计 0），`CycleReviewSection.Stalls` 合计（只含纳入局）。
- `ReportWriter.AppendCycleReview`（只在 `--review-cycles` 时调用）：汇总加一行 `- 停滞停手 N 次（按 D:stall 计；整局计，不限窗口），涉及 M 局`；逐局行加 `停滞停手 N 次（按 D:stall 计）`；逐局说明行补"停滞停手次数"。缺省报告不经过该函数，逐字节不变（`诊断不改对局` 的 `StartsWith(off, on)` 与新测试 `DoesNotContain("停滞停手", 缺省报告)` 守）。

### 测试（`tests/Siege.Core.Tests/MatchTelemetry/循环提子诊断Tests.cs`「停滞停手次数可查」）

- 手写批次：A 局 3 次（第 1 / 7 / 14 大回合，第 1 大回合在窗口外）、B 局旧式日志（无字段）0 次、C 局无结果行被排除（5 次不计）→ 逐局 [3, 0]、合计 3；文本往返同值；报告汇总行与逐局行文案；缺省报告不含"停滞停手"。
- 真跑交叉核对：`link:1` 简单 × 4、种子 1、截断 80 小回合、`StagnationRounds = 1`（只为短局触发，缺省 K 不变）、四名 AI 权重写死为 `SimFixtures.PreCalibrationWeights`（样本下界不随缺省权重漂移；实测 `D:stall` 47 次）：四名 AI 的 `D:stall` 条目数 = 快照 `Stalled` 数 = 诊断次数，且 > 0（样本下界）；停手小回合全部 `Passed`。

### 变异验证

| 编号 | 改动 | 结果 |
|---|---|---|
| M-TC1 | 计数改数 `Passed` | 红 1：停滞停手次数可查 |
| M-TC2 | 跑局层快照不写 `Stalled`（`_trace.Rehearsals < 0 ? true : null`） | 红 1：停滞停手次数可查 |
| M-TC3 | 只数窗口内快照（`window.Count(...)`） | 红 1：停滞停手次数可查 |

### 实测（≤ 8 局，单进程顺序）

- `dotnet run --project src/Siege.Sim -c Release --no-build -- run --out sim-out/stagnation-trace/easy --map link:1 --difficulty Easy --seed 40 --count 8 --turn-limit 600 --serial`：退出码 0，2089 小回合 / 535 大回合，墙钟 141 s。
- `analyze --dir sim-out/stagnation-trace/easy --review-cycles --out sim-out/stagnation-trace/easy-report-cycles.txt`：汇总 **停滞停手 336 次（涉及 7 局）**；逐局 0x28 53 / 0x29 13 / 0x2A 71 / 0x2B 40 / 0x2C 71 / 0x2D 0 / 0x2E 18 / 0x2F 70。与日志里 `"Stalled":true` 的逐文件计数逐局相同。
- 缺省报告 `easy-report-off.txt` 不含"停滞停手"，且是开关报告的前缀（缺省报告不变）。
- 顺带观察（非本 change 引入，AI 决策未变）：8 局里 0x2F 打到 R100 由大回合上限结算（`RoundCap×1`）；cycle-stop 第二段同种子验证时上限结算为 0，差别来自其后 `link-map-surfaces` 改了 `link:1` 地图。循环 0、拖延 1、截断 0。

## 2.1 基线首部

- `LinkDecisionSequence.Export` 首部第 3 行删 ` mode=Link pairing=script（缺省：升序两两配）`、第 5 行删"；P 配对、L 链接"（保留 S 战场结算）；类注释同步（G2 已裁决）。
- `SIEGE_REBUILD_DECISION_BASELINE=1 dotnet test --filter 决策序列基线` 重建 5 份（首跑比对 bin 旧副本红 5，属预期），再不带开关跑同过滤 + 依赖 seed3 / seed1 基线的 `循环提子`、`AI改造与主题诊断`、`战场落败复盘`：35 / 35 通过，退出码 0。
- 脚本核对（`git show HEAD:` 二进制逐行比对，输出存 `baseline-check.txt`）：5 份都只有首部第 3 / 5 行不同；**简单难度种子 1 在第 12 大回合内出现 `D:stall`**，单列 3 行：第 45 / 49 / 53 行（第 10 / 11 / 12 大回合 P1）`D:pass` → `D:stall`——行为不变、只改文案（格式变化）。其余决策行逐字节相同；行尾 LF，无 CR。
  - 说明：设计文档 v1.29 写"5 份基线截断范围内未出现停滞局面"是 cycle-stop 当时的情形；`link-map-surfaces` 重钉后简单种子 1 的这三次 Pass 已是停滞停手（当时同记 `D:pass`，逐字节不变所以没被发现）。v1.31 在 §15.2 补了一句。
- 依赖决策条目文案或基线首部的哈希：全量测试里除简单基线外无别的红（第一次全量 2098 条只红 `链接图其他难度决策序列与基线相同(Easy)` 1 条）→ 没有别的哈希需要重钉；`Stalled` 只在停手小回合写出，现有日志 / 报告黄金值所用的局里没有停手。

## 3.1 文档

- `openspec/ROADMAP.md` 按 D4 重写：已完成按首轮原型 / 规则与活形 / 地图与模式 / AI / 表现与工具分组列到 v1.30（被删除或取代的项括注）；未完成按优先级：试玩待定（对局长度、主题强弱、栅栏造眼）→ 专家强度归因 → 画面精修重规划（`map-elements-v2`）→ AI 整批评价 → 联网（§19 第 4 项）→ 美术；删去 2 人图、V2 权重 / 边疆图复核、简单多人近循环等失效旧地图项。
- `HANDOFF.md`：文首更新日期与状态；规则速览标题 v1.31、AI 一行补留痕；`analyze --review-cycles` 说明补停手次数；后续 change 第 1b 项标已处理（含旧日志回放分歧提示）、第 4 项（基线首部）标已处理、第 8 项改"已归档 2026-10-20、提交 f750eeb"；已归档列表补 `link-map-surfaces`；决策序列基线段落改写；测试数见下。
- 设计文档 v1.30 → v1.31：文首版本行；§15.2「循环提子停手」加留痕一段（含简单种子 1 基线说明）；§17「开局与结算」条的 `--review-cycles` 句补停手次数；§19 第 16 项（R-3）末补"停手留痕已由 stagnation-trace 补上"（§19 无单独的 1b 条目，HANDOFF 1b 对应的就是 R-3 这条）；变更记录加 2026-10-21 一行。

## 构建与全量

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test siege.sln -c Release`（后台，`DOTNET_CLI_UI_LANGUAGE=en`，看退出码）：**2098 条，通过 2092，跳过 6（全是 Perf / Slow），失败 0，退出码 0**。
- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误。Godot 侧代码未改，未跑自检。

## 待负责人裁决 / 提示

1. 旧日志回放（质检已兼容，见 check.md 第 3 项：`Replayer.Compare` 对原日志缺 `Stalled` 的小回合按未停手比）：`Stalled` 字段只在停手小回合写出。`2a2c039`（cycle-stop）之后、本 change 之前跑出且含停手的日志（无 `Stalled`）用新代码 `replay` 会在首个停手小回合分歧（多出该字段）。`sim-out/cycle-stop/*` 的日志早于 `link-map-surfaces` 改图，回放本就停在首部地图摘要不一致，不受影响；实际只波及 `link-map-surfaces`（f750eeb）之后、本 change 之前的含停手日志。按 proposal 归为格式变化，未加兼容分支。
2. 「停滞停手留痕」中"同一局面 K = 0 记 D:pass"的构造：两份控制器都写死高停手阈值（只差 K），见 1.1 说明。

- 收尾补丁：「停滞停手次数可查」真跑段写死权重后，`~循环提子` 过滤 15 / 15 通过；M-TR1 / M-TC1–3 重跑仍各红（4 / 1 / 1 / 1），还原后 15 / 15、退出码 0。该补丁只改测试，在最后一次全量（2098 / 2092 / 6）之后。
3. ROADMAP「已完成」里 v1.2 之前与变更记录无版本号的条目（`dominance-victory`、`catch-up-recruit`、`merge-board-layer`、`board-coordinates`、`denser-map`、`strict-cli`、`show-sound-cues` 等）的分组与"后被谁删除"括注、"程序化提示音"描述，系按 change 名与 HANDOFF 旧规则段推断，未逐个核对 change 正文，请负责人过一眼。
