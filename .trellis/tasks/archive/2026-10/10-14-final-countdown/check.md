# 10-14-final-countdown 检查报告（trellis-check，2026-10-15）

范围：`git diff 7f47c1f` 的全部未提交改动 + 未跟踪文件。未 commit、未 `openspec archive`、未跑批量；dotnet / Godot 顺序单跑（另有用户自开的 Godot 编辑器进程 `--editor` 在运行，未动它）。

## 逐项结论

| # | 检查项 | 结论 |
|---|---|---|
| 1 | 规格符合 | **通过**。四份增量 10 个 Scenario 全有同名测试：battlefield「倒计时到期强制结算」（`战场结算Tests`）、「首轮一侧先结算即开始倒计时」「首轮配对的等待者也触发倒计时」「构筑期结算按第3大回合起算」「K为0不启用」「无人等待者不倒计时」（`决赛倒计时Tests`）；match-setup「缺省值」「旧存档按不启用读取」；match-telemetry「到期结算可查」；settlement-show「倒计时提示」。实现逐条一致：等待者两种来源（`IsWaitingForFinal`：已结算战场 ‖ 首轮配对步骤已完成且不在待首轮配对）；R = max(首次出现, 3)（`WaitingStarted` 早退）；期限 = max(R+K, completed+1)；到期是 `JudgeAtRoundEnd` 第三条 (c)，排在 Pass / 填满之后，原因 `Expired`；`_finalLinked` 后不赋期限、`CountdownExpired` 也恒假；K = 0 早退；`ExecuteLink` 清两个旧标识的期限；旧存档缺 K → 0 + `FinalCountdownBackfilled`。 |
| 2 | K = 0 不变的证据 | **通过**。5 份 `*-k0.txt` 与 `git show 7f47c1f:` 对应基线**逐字节相同**（cmp：seed1 3623 B、seed2 4059 B、seed3 4243 B、easy-seed1 3612 B、hard-seed1 3808 B）。`决赛倒计时Tests.K为0时链接图决策序列与改前基线逐字节相同` 以 `Export(..., finalCountdown: 0)` → `RunConfig.FinalCountdownRounds = 0` → 会话核对与 `MatchOptions` 一致，确实以 K = 0 跑出并比对；另断言夹具无 `C` 行与小回合数下界。 |
| 3 | 既有测试改动 | **通过**。被改的既有测试 15 个文件（3 份重钉夹具 + 12 个 .cs），与 implement.md §4 #1–#10 一一对上。#7 `批量跑局Tests` 两处、#8 `默认评价权重的校准Tests` 正反两处，期望值都**只多了 `FinalCountdownRounds = DefaultFinalCountdownRounds` 一项**（这两条钉的就是"缺省落成"，必须是 2）。其余依赖走法的夹具（`LinkModeFixtures.Options`、`SimFixtures.PinPreCalibration`、`LookaheadFixtures.LinkConfig`、终端脚本 `finalCountdown: 0`、`带入带出配置Tests.Golden` 透传）一律 K = 0、期望值不动；#4 两条黄金哈希只遮掉 `"FinalCountdownRounds":0,` 并加 `Assert.Contains` 证明该项存在。 |
| 4 | 先手预测 | **通过（已收紧）**。原测试末行用 `.Order()` 比集合、且收尾全员 Pass 实际走的是 (a) 整轮 Pass。已改为精确顺序比较 + 断言原因 `AllPassed` 的口径注释；另补「期限大回合最后一人Pass前的预测与实际顺序一致」：第 8 大回合两名交战者各落 1 子、等待者 Pass，末位行动前取预测，末位 Pass 后**实际以 `Expired` 结算**，`forecast.NextOrder` 与 `ActionOrder` 逐项相同、落败者已剔除。两条都绿；M-FC1 下两条都红。 |
| 5 | 存档与回放 | **通过（已补）**。存档往返（`决赛倒计时随存档往返`）样本取非缺省值：K = 2、R = 6、期限 {0:8}、倒计时记录非空、到期后结算记录 `Deadline = 8` 再往返。回放按首部 K（`首部K按记录回放`）。**缺口**：原先没有"篡改日志 → 报分歧"的测试，已补 `决赛倒计时的记录Tests.篡改首部K或结算期限即报分歧`：①首部 K 2 → 0：回放按 0 重建、分歧行 > 1、回放里没有倒计时事件与到期结算；②只把到期结算记录的 `Deadline` 9 → 10：分歧恰在该结算事件那一行。存档侧没有"分歧"机制（存档即事实，恢复不交叉校验期限与 R + K 的一致），与既有字段同口径，不另加。 |
| 6 | Presentation / Godot | **通过**。`FinalCountdownNotices` 只收 int / bool / `IEnumerable<int>`，零 Godot（`using Siege.Core.Match` 只为 cref）；Godot 只在 `WithLinkBeats` 里读对局公开属性、调 `Take`，进既有 `_pendingNotices` → `NoticeQueue`，无规则计算（守门 `UI层不含规则计算Tests` / `Godot层不含规则计算Tests` 在全量里绿）。W7 之后 implement 未重跑 Godot，本次先 `dotnet build src/godot/Siege.Godot.csproj`（0 警告 0 错误）再跑 `--headless --path src/godot -- --auto-demo --rounds=0 --seed=5`：**退出码 0、`ERROR` 0 行**，自检 13 条失败 0、七步齐全；日志第 91 帧"本战场还剩 2 个大回合"、第 103 帧"还剩 1 个大回合"、第 115 帧战场 1 `Expired` 结算 → 本机横幅「战场落败（第 2 名）」通过。 |
| 7 | 硬边界 | **通过**。新增 Core / Presentation / Sim 文件无 `double` / `float` / `Random` / `GameSeed` / `Godot`；倒计时不消费随机（比较链的种子兜底顺序是锁定时既有的）。`Expired` 只在 `JudgeAtRoundEnd` 一处产生，`IsWaitingForFinal` 只在 Countdown.cs 定义、Godot 只调用。观察（不算违规，见 W12）：遥测 `WaitingRounds` 的递增条件是"所在战场只剩 1 名在局玩家"（`SettleAtRoundEnd`，既有代码），与 `IsWaitingForFinal` 是两种写法；第 4 大回合起两者实际一致，起算点已共用 `WaitingStarted`。 |
| 8 | 文档 | **通过（已补）**。HANDOFF 与设计文档 §4.3 / §12.3 第 4 条 / §19 第 4 项与实现一致（含 W7）。已补：设计文档 v1.25 变更记录行加"R 不早于第 3 大回合（W7）"；openspec `design.md` D1 加 W7；battlefield 增量规格"至少给满 1 个大回合"后加注 W7 之后首轮拼接不会触发这一兜底；`.trellis/spec/core/boundaries.md` 单一实现表新增两行——「决赛倒计时」唯一归属，「对局配置项的缺省值与缺项回填值」约定（新局缺省 2 由 `ResolvedFor` 落成；旧日志 / 旧存档缺项按 0 + `…Backfilled` 留痕；缺项不得走缺省；依赖走法的夹具写死旧口径）。 |
| 9 | 改动范围 | **通过**。只有 `src/`、`tests/`、`openspec/changes/final-countdown/`、`.trellis/`、`art/final-countdown/`（2 张 png）、HANDOFF、设计文档；无意外文件。 |

## 自修清单

1. `tests/Siege.Core.Tests/InitiativeOrder/先手顺序预测_链接模式Tests.cs`：`期限大回合的预测剔除将到期落败者` 末行由集合比较改为精确顺序比较，补原因 `AllPassed` 断言与口径注释；新增 `期限大回合最后一人Pass前的预测与实际顺序一致`（实际以 `Expired` 结算）。
2. `tests/Siege.Core.Tests/MatchTelemetry/决赛倒计时的记录Tests.cs`：新增 `篡改首部K或结算期限即报分歧`。
3. `tests/Siege.Core.Tests/Battlefield/战场结算Tests.cs`：`倒计时到期强制结算` 前补空行与变异记录注释。
4. `tests/Siege.Core.Tests/CarryInOut/终端的带入选择弃赛与结算显示Tests.cs`：同一行两段 `//` 注释合并成一段。
5. `src/Siege.Sim/Analysis/BalanceAnalyzer.cs`：`BalanceReport.FinalCountdown` 文档注释改为"没有 K > 0 的链接模式局时为 null"（原写"没有链接模式局"，与实现不符）。
6. 文档：HANDOFF 首行日期改 2026-10-15（含 W7）、设计文档变更记录（W7）、`openspec/changes/final-countdown/design.md` D1（W7）、battlefield 增量规格兜底分支注、`.trellis/spec/core/boundaries.md` 两行。
7. `check.jsonl`：删除 `_example` 占位行，补四份增量规格与 `determinism.md`。

## 构建与测试

- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2146 条，通过 2140，跳过 6，失败 0**（implement 报 2144；+2 为本次新增的两条）。
- Godot `--auto-demo --rounds=0 --seed=5`（headless）：退出码 0，见第 6 项。

## 变异复现（cp 备份 → sed 改一行 → 只跑相关类 → cp 写回 → cmp 逐字节 → touch）

| 编号 | 变异 | 结果 | 还原 |
|---|---|---|---|
| M-FC1 | `MatchFlow.Countdown.cs` `CountdownExpired` 的 `MajorRound >= deadline;` → `MajorRound < 0;` | 红 12（implement 记 9；+1 为 `决赛倒计时的记录Tests.分析统计到期结算`——implement 记日志两条、实红三条；+2 为本次新增两条） | cmp 相同，绿 |
| M-FC8 | `Siege.Sim/Running/MatchSession.cs` 结算日志 `Deadline = settled.Deadline,` → `Deadline = null,` | 红 3（「到期结算可查」「首部K按记录回放」「篡改首部K或结算期限即报分歧」） | cmp 相同，绿 |

变异后全量重跑即上面的 2146 / 2140 / 6、失败 0。

## 是否建议提交

**建议提交**。规格、K = 0 不变证据、存档 / 回放、先手预测、Godot 自检都已核实，测试缺口已补，没有阻断问题。

## 需裁决事项

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| W12 | 遥测 `WaitingRounds` 与规则 `IsWaitingForFinal` 是两种写法 | `SettleAtRoundEnd` 以"所在战场只剩 1 名在局玩家"计等待大回合；倒计时以 `IsWaitingForFinal` 判定。起算点已共用 `WaitingStarted`，第 4 大回合起两者实际一致 | 维持（检查阶段不重构，K = 0 逐步不变靠基线守着）；以后动遥测时改为调用 `IsWaitingForFinal` |
| W13 | 旧模式（Legacy）存档不写 K | Legacy 局的 K 不入存档，恢复后 `FinalCountdownRounds` 为 0（建局缺省 2）；K 只在链接模式生效，行为无差别 | 维持；随 `retire-legacy-rules` 一并消失 |
