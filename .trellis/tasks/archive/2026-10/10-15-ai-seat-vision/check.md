# 10-15-ai-seat-vision 检查报告

**结论**：建议提交（自修已并入，构建零警告、全量全绿）。需裁决 V12–V14 都不阻塞提交。

## 逐项结论

| # | 检查项 | 结论 |
|---|---|---|
| 1 | 规格符合 | 通过。两条 MODIFIED Requirement 的新 Scenario 都有同名测试：`缺省按座位视野`、`全知仅作显式配置`、`全知配置下与改前相同`（Theory ×5）、`他战场玩家不进名册`、`同战场无对手时退化`；原 Scenario 测试保留。缺省 `SeatVision = true`，`VisionFor` 只在 `== false` 时给全知。全知只能经 `--config` 顶层 `SeatVision` 打开。终端版 `PlayCommand` 与图形版 `src/godot/scripts/MatchSession.cs` 都只用 `AiSearchConfig.ForMap(...)`，`LaunchArgs` 与 `play` 选项里没有视野项，守门测试会扫描入口源码。名册与 `RivalsInBattlefield` 取交集。`Project(view, board, me)` 只对同战场判出局。缺口见 V12 |
| 2 | 读取点盘点（抽 5 项：#7、#9、#10、#14、#15） | 判定成立。#7 先手维：`BatchEvaluator` 用 `_before.RankOf(me)`，投影里他战场玩家势力为 0，不会排到自己前面，所以等于同战场名次。`_roster` 取的是 `view.Players.Status`，投影不改在局状态，所以他战场玩家不会被评价器当成出局；维度 2 敌方损失对他们前后都是 0、增量为 0；维度 3 他场信物前后不变。#9 C15 `TrailsInBattlefield` 与 #10 决胜一手 `DecisiveTriggered` / `DecisiveMoveFor` / `Outcome` 只看同战场玩家（出生棋盘与地形划出的战场），所以不受他战场 0 势力影响。#14 / #15 改法正确，两参数 `Project` 只剩测试在用 |
| 3 | 全知对照 | 通过。5 份 `*-omniscient.txt` 与 `git show 0d472c0:` 的对应基线 `cmp` 逐字节相同。`全知配置下与改前相同` 以 `seatVision: false` 导出后比对；输出目录由 csproj 的 `AiDecision\Fixtures\*.txt` 通配拷贝 |
| 4 | 既有测试改动 | `git diff 0d472c0 -- tests` 共 12 个 .cs 与 2 个夹具，与 implement §5 的 14 条逐条吻合，理由成立。V7 只影响专家：标准难度在全知下逐字节不变有三条证据：① 5 份 omniscient 全过（标准 / 简单 / 困难）；② `非专家没有前瞻记录` 的黄金值未改，只遮掉了字段；③ `G1Golden` 三颗种子的决策序列哈希、条数、子流消费次数都没动，只重钉了 RecordsHash |
| 5 | 配置与回放 | 通过。`ResolvedFor` 把未配置的视野落成 true；`MatchSession` 取 `Config.SeatVision ?? !recorded`；`AttachConfigured` 对全部 AI 套用会话值。`FromJson` 遇到 `Players[i].Search.SeatVision` 报错，文案点名 `Players[0].Search.SeatVision` 并要求改写到顶层；只有 `run --config` 走 `FromJson`，日志首部不经它，所以旧首部不会因此报错。变异复现见下 |
| 6 | V9 测试缺口 | 已补，见自修 4 |
| 7 | 硬边界 | 通过。Core / Sim 没有 Godot 引用（`MatchDebugView.cs` 只在注释里提到）；diff 里没有 `double` / `float` / `Random`；视野分流只在 `HeuristicAi.VisionFor`；`PublishSeatProjection` / `ProjectRehearsal` 在 Ai 下只有 `HeuristicAi` 调用，没有第二处投影实现。同战场口径原来在 `TrailsInBattlefield` 里有一份内联副本，已收口（见自修 2） |
| 8 | 文档 | HANDOFF、设计文档 v1.26（§13.3、§15.1、§15.2 第 2 条、§19 第 2 项、变更记录）与实现一致。过期措辞已改（见自修 1）。`boundaries.md` 新增行引用的两个测试名都存在；行里写的"C15 / 决胜一手 / 名册共用 `RivalsInBattlefield`"原来对 C15 不成立，已按自修 2 改成成立 |
| 9 | 改动范围 | 只涉及 `src/`、`tests/`、`openspec/`、`.trellis/`、HANDOFF 与设计文档；`src/godot` 没有改动 |

## 自修清单

1. **过期注释**
   - `src/Siege.Core/Ai/HeuristicAi.cs` 类顶注释：原为"只取 Publish"，改为经 `VisionFor`（缺省按座位投影，显式全知时取 `Publish`）。
   - `src/Siege.Core/Match/MatchFlow.SeatProjection.cs`：类注释与 `ProjectRehearsal` 注释里的"全知例外集中可关""关闭全知的 AI"已改写。
   - `src/Siege.Core/Match/SeatPublicView.cs`：注释"AI 全知例外仍用它"已改写。
   - `src/Siege.Core/Ai/HeuristicTurnController.cs`：`RehearsalView` 注释（原写"全知（缺省）为 null"）与 `TrailsInBattlefield` 注释（原写"关闭全知时"）已改写。
2. **同战场口径收口**（`HeuristicTurnController.TrailsInBattlefield`）：原来自带一份与 `RivalsInBattlefield` 相同的过滤条件，现改为 `RivalsInBattlefield(view, player).Any(p => power.Of(p).Total > mine)`。行为等价，全量测试与决策基线全绿，`boundaries.md` 与 implement §1 的"三处共用"因此成立。
3. **测试注释**：`MatchTelemetry/专家前瞻的记录Tests.cs` 原写"三名标准的走法不变"，与"小回合 46 → 45"字面矛盾，已改写。
4. **补测 V9**：
   - `LookaheadFixtures` 新增 `ProbePositions(bool seatVision, ...)` 重载，原重载转调 `false`，行为不变。
   - 同时新增 `ShadowWithVision`：经 `HeuristicAi.VisionFor` 取观察视图，并设置 `RehearsalView`。原 `Shadow` 直接读 `match.Publish`，不随配置的视野变化，所以原耗时测试实际一直按全知计时。
   - `专家前瞻耗时计时Tests` 新增 `按座位视野下专家单次部署耗时中位数不超过高难的四倍`：`[PerfTheory]`、`Category=Perf`、与原条同一不并行 collection。局面来自按座位视野的对局，高难与专家都按座位视野，其余口径与原条相同；两条共用 `Measure` 测试体。
   - `SIEGE_PERF=1` 单跑结果：

     | 口径 | 局面数 | 高难中位 | 专家中位 | 比值 |
     |---|---|---|---|---|
     | 按座位视野 | 47 | 53.1 ms | 92.0 ms | **1.734** |
     | 全知 | 46 | 34.5 ms | 70.8 ms | 2.054 |

     两条都在 ≤ 4 以内。按座位视野下高难自身约为全知的 1.5 倍，即投影开销。
5. **HANDOFF**：标题"现行规则速览（设计文档 v1.25）"改为 v1.26。

过程中出现过一次红并已修复：自修 1 起初在 `HeuristicTurnController.cs` 的注释里写了 `AiSearchConfig.SeatVision` 的 cref，触发守门测试 `全知例外只在一处分流`（"SeatVision 只出现在 AiDifficulty.cs / HeuristicAi.cs"）。已改成不含该字样的写法。

## 变异复现

| 变异 | 结果 |
|---|---|
| MV-7：注释掉 `FromJson` 里的 `RejectSearchVision(doc.RootElement);` | 只跑 `视野开关缺省不写出`：RC = 1，红（`Assert.Throws() Failure: No exception was thrown`）。从备份还原后 `cmp` 逐字节一致，重跑 RC = 0，绿 |
| 探针：`PredictNextOrder` 的 `among` 强制为 `null` | 跑 `前瞻|专家` 相关 79 条（含 G1Golden、专家前瞻的记录）：**全绿**，即没有测试能杀死这一项（见 V12）。已还原，`cmp` 一致 |

还原后又做了一次 `--no-incremental` 全量重建再跑全量，排除增量构建残留变异。

## 构建与测试

- `dotnet build siege.sln -c Release --no-incremental`：0 警告、0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：总计 2157 条，通过 2150，跳过 7，失败 0，RC = 0，耗时 1 分 49 秒。跳过数比 implement 多 1，就是新增的 Perf 条。
- 本次没有跑 Godot 自检，因为 `src/godot` 和 Godot 读取的 Core 接口都没改。

## 需裁决事项

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| V12 | Scenario「跨大回合预测只在名册玩家之间比较」没有判别性测试 | `among` 强制为 `null` 后全部测试仍绿。要判别，需要同战场至少 2 名名册玩家（链接后 3 人战场），并让他战场玩家的名次或先锋修正改变两人的相对先后。按座位视野下他战场玩家势力为 0，`among` 几乎只在全知下起作用，构造成本高 | 登记为测试缺口，接受现状；或另开小任务补一个手搭局面的单元测试 |
| V13 | `RejectSearchVision` 用 `TryGetProperty("Players")`，大小写精确匹配 | 与既有 `RetiredKeys` 的做法一致，`ToJson` 产出 PascalCase。只有手写小写 `"players"` 的配置能绕过 | 知悉，不改 |
| V14 | implement §5 第 7 条提到 `不消费新随机` 在缺省视野下会抛 NRE | 已核实：产品代码无候选时 `LastChoice = null`、照常 Pass，NRE 出在测试里的 `hard.LastChoice!`，不是产品缺陷 | 知悉；夹具写死全知是合理做法 |

## 本次改动的文件（检查阶段）

- `src/Siege.Core/Ai/HeuristicAi.cs`
- `src/Siege.Core/Ai/HeuristicTurnController.cs`
- `src/Siege.Core/Match/MatchFlow.SeatProjection.cs`
- `src/Siege.Core/Match/SeatPublicView.cs`
- `tests/Siege.Core.Tests/LookaheadFixtures.cs`
- `tests/Siege.Core.Tests/AiDecision/专家前瞻耗时计时Tests.cs`
- `tests/Siege.Core.Tests/MatchTelemetry/专家前瞻的记录Tests.cs`
- `HANDOFF.md`

没有 `git commit`，没有运行 `openspec archive`，没有跑批量。
