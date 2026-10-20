# 10-20-cycle-stop-1 notes（第一段 1.1–1.3，不改任何对局行为）

## 1.1 诊断段（done）

- 新增 `src/Siege.Sim/Analysis/CycleAnalyzer.cs`：`CycleAnalyzer.Review / ReviewMatch`、`CycleReviewSection / CycleReviewEntry / CyclePlayerEntry`。只读日志快照：占用者取 `PlayersState[*].Groups[*].Stones`（不含类型，与同形比对键同口径）、提子取 `Captures`（`坐标:P编号:类型`）、设施取累计 `Edits`（目标触及局部格的 `动作:目标`）；四邻经 `Adjacency.Neighbors`（唯一实现，宽高取日志坐标上界 + 2）。
- 窗口 12 个大回合（S1）；窗口首势力 = 窗口前最后一条快照（没有即 0）；**在局玩家 = 窗口首 Active 的玩家**（不按窗口末：正常终局的最后一次结算把落败者整批出局，按末快照只剩胜者，"≥ 2 名净 ≤ 0"永远不成立、判定等于没被检验；出局玩家末势力仍按末快照读，快照保留）。
- 局部周期：局部键逐小回合求串，先压缩连续相同键（连续 Pass 不算"回到先前状态"），最短周期 = 相同状态对的小回合距离最小值，重复次数 = 压缩序列里回到先前状态的次数。
- 判定：窗口首在局玩家 ≥ 2 名净势力 ≤ 0 且周期存在。另报归因量：C 之外新增落子、局部外盘面变化回合数、窗口内局部新增设施数（同形键含改造段，这一项解释了 0x2C 为何全局占用重复 9 个大回合却不触发同形）。另报代理量（不判定）：窗口内有落子回合里提子回合占比、落在本窗口内此前被提过格上的落子占比、"提子拖延"局数（≥ 2 名净 ≤ 0 且提子回合占比 ≥ 75%）、每名玩家 D3 口径停滞（终局时 / 整局最大）。
- 接线：`AnalysisOptions.ReviewCycles`、`BalanceReport.CycleReview?`、`ReportWriter.AppendCycleReview`（只在开关开时追加在整份报告最末）、`Program.Analyze` 读 `--review-cycles` 并进用法文本。
- 测试 `tests/Siege.Core.Tests/MatchTelemetry/循环提子诊断Tests.cs` 三条同名 Scenario：`循环局被识别`（手写 3 人在 D4 / E4 / D5 / E5 轮流提子、P / Q / R 列每大回合各加 1 子、14 大回合截断 → 周期 3、|C| 4、局部格 12、C 之外新增落子 36、恰好 2 名净 ≤ 0）、`正常收官不误判`（整轮 Pass、末段两次零星提子、2 名净 ≤ 0 → 周期无、不判；含 `ShortestPeriod` 压缩算例）、`诊断不改对局`（真实 `Round4Log`：开关开着分析前后 `FullText` 逐字节相同、缺省报告无该段、开启报告以缺省报告为前缀、文本往返后结论逐字段相同）。
- 变异验证（实跑，每条只跑本类，二进制读写 + finally 还原 + 锚点唯一 + 还原后刷新时间戳，脚本 `%TEMP%/mutate.py`）：
  - M-CS1 判定 `≥ 2` → `≥ 3`：红 1（循环局被识别）。首版夹具三人全净 ≤ 0 时 0 红，已把样本改成恰好 2 名。
  - M-CS2 判定去掉 `period is not null`：红 1（正常收官不误判）。
  - M-CS3 配对循环 `i < j - 1` → `i < j`：0 红——**等价变异**（压缩后相邻状态必不同），不作证据；改为 M-CS3b 去掉压缩（每个键都记成状态）：红 1（正常收官不误判，末段连续 Pass 被当成周期 1）。
  - M-CS4 `Analyze` 不看开关一律做诊断：红 1（诊断不改对局）。
  - 还原后重跑本类 3/3 绿，`git status` 无 .bak。
- 对 open-map 段 A 冒烟 20 局（剥离已删字段后）：循环计数 **0**，`analyze` 缺省报告与改动前逐字节相同（`cmp`），开启报告以缺省报告为前缀（222 → 249 行）。
- 5 份 `decisions-link1-*.txt` sha256 与开工前逐字节相同（本段不碰 Core / AI）。

## 1.2 诊断批次（done）→ `diagnosis.md`

- A：`run --out sim-out/cycle-stop/easy --map link:1 --difficulty Easy --seed 40 --count 8 --turn-limit 600 --serial`，8 局 0 截断，大回合 35–94（中位 65.5）；B：`run --config sim-out/cycle-stop/script43-config.json --serial`，1 局 R93 不截断；两批 config.json 逐项核对。合计 9 局。
- 循环提子 1 局（种子 44）、提子拖延（代理）4 局；正常 20 局 0 误判。

## 1.3 回传（done，见回传正文）

## 冲突 / 偏差记录

1. **冒烟日志解析被拒**：`sim-out/open-map/smoke/*` 是段 B 删字段前写的，首部 `Config.FinalCountdownRounds / SeatVision` 与快照 `WaitingRounds` 命中 `MatchLog.FindRetired` → `analyze` 报"拼接规则已移除"。没有放宽拒绝逻辑（有守门），改为脚本剥离这些键复制到 `sim-out/cycle-stop/smoke-stripped-all`（20 局，7720 个字段）再分析；PRD 写的 `smoke/link1/` 只有 10 局，20 局对应 `smoke/all/`。
2. **种子 43 脚本局面无法经 `PlayCommand` 写日志**：`PlayCommand.Run` 不接 `MatchSession`、不写日志也无截断；按任务后备路线用 `run --config` 跑同权重 / 阈值 / 内容集的四 AI 局（差异见 diagnosis.md）。R-3 的三 AI 互提局面本段没有拿到日志。
3. design D1 写"每名在局玩家"未定义在局时刻，按上面的口径取窗口首（已在代码注释与报告段首写明）。
4. `tasks.md` 1.2 写的报告路径 `.trellis/tasks/10-20-cycle-stop/diagnosis.md` 是旧路径，报告按 PRD 放本目录。
5. 既有并行竞态：`可复现回放Tests` 没挂 `ConsoleRedirect` 集合却经 `RunMain` 重定向控制台，全量三跑两红（见「验证」）；本段补上特性，不改测试体。
6. 工具插曲：`sed -i` 没动行尾（`ReportWriter.cs` 本就是 LF），但我中途误把它转成 CRLF 又转回 LF，最终与 HEAD 行尾一致（`git diff --stat` 只有 +47）；新文件 `CycleAnalyzer.cs` / 测试文件按仓内多数用 CRLF。

## 验证

- `dotnet build siege.sln -c Release`：0 警告 0 错误（三次）。
- 全量 `dotnet test tests/Siege.Core.Tests -c Release`（退出码单独记录）：
  - 第一次（加 LocalEdits 之前）：总 2030、通过 2022、跳过 6、失败 2，RC 1——`批量跑局Tests.replay两种定位方式不得混用` 与 `可复现回放Tests.旧拼接日志被拒(turn/WaitingRounds)` 互换了 stderr（前者抓到后者的"拼接规则已移除"，后者抓到空串）：前者挂 `[Collection(ConsoleRedirect.Collection)]`、后者没挂，并行时的既有竞态，与本改动无关；两条单跑 8/8 绿。
  - 第二次（同一二进制重跑）：总 2030、通过 2024、跳过 6、失败 0，RC 0（3 m 50 s）。
  - 第三次（加 LocalEdits 后）：总 2030、通过 2022、跳过 6、失败 2，RC 1——又是 `可复现回放Tests.旧拼接日志被拒(turn/WaitingRounds)`，这次与 `各入口按地图标识选图_链接模式Tests.参赛人数不是4即按S1报错(players: "5")` 互换 stderr。同一根因，三次里两次红，于是给 `可复现回放Tests` 补 `[Collection(ConsoleRedirect.Collection)]`（tests/ 范围内一行，`LinkSimFixtures.RunMain` 重定向控制台的类都该串行）。
  - 第四次（补特性后，最终二进制）：总 2030、通过 2024、跳过 6、失败 0，RC 0（5 m 10 s）。
- 变异验证见 1.1；基线 5 份 sha256 不变。
