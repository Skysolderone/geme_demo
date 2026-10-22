# ai-whole-batch 实施记录（tasks 1.1–1.8，trellis-implement）

## 改动文件

Core
- `src/Siege.Core/Ai/AiDifficulty.cs`：`AiSearchConfig.WholeBatch`（int，构造缺省 0，`JsonIgnore WhenWritingDefault`）、`DefaultWholeBatch = 1`，四档预设带 1（与 `StagnationRounds` 同口径；简单不生成由代码按 `ImmediateOnly` 判）；`Validated()` 拒绝 0 / 1 以外。
- `src/Siege.Core/Ai/HeuristicTurnController.cs`：`ChooseByCandidates` 在 M 条贪心 / 扰动之后、`LastCandidates` 与选优 / 前瞻之前，开关为 1 且非 `ImmediateOnly` 时调 `AppendWholeBatch`：
  Pass 评价 1 次 → 剪枝候选（`Greedy(ranking, …, int.MinValue)` 即决胜一手同一调用 → `PrunedCandidate` 留一法剪枝，并列取靠后者，拿空以 Pass 为准，不合法视为 +∞）→
  配对候选（`PairedCandidate`：前 N 中 `单点 − Pass ≤ 阈值` 的两两组合、格不同、合法且过活形硬约束、两枚留一法下降都 > 阈值，取 `CandidateSelection.Compare` 最优，复摆后 `ContinueGreedy`）；
  空不加、键重复不加。`Greedy` 拆成 `Greedy` + `ContinueGreedy`（行为不变）；`EvaluateSet` 走同一 `rehearse` + `TryEvaluate`；留一法比较只在 `LeaveOneOutHolds` 一处。
  不调用 `Perturb`，零随机。新留痕 `LastWholeBatch`（`WholeBatchCandidates`）。停滞停手与决胜一手的分支未动。
- `src/Siege.Core/Ai/CandidateBatch.cs`：`WholeBatchSource`、`WholeBatchCandidates`（剪枝 / 配对候选、是否加入、`SourceOf(choice)`）。
- `src/Siege.Core/Ai/ExpertLookahead.cs`：`SimulatedConfig` 追加 `WholeBatch = 0`。
- `src/Siege.Core/Ai/EvaluationBreakdown.cs`：`Version` 9 → 10（文档补"10 = 整批候选"）。

Sim
- `src/Siege.Sim/Config/RunConfig.cs`：`WholeBatch`（int?，`[JsonIgnore]`）+ 私有序列化影子 `WholeBatchRecorded`（0 写成缺省 → 不写出）；`Validated` 拒绝 0 / 1 以外；`ResolvedFor` 未配置落成 1，专家 `Search` 落成时带上实际值。
- `src/Siege.Sim/Running/MatchSession.cs`：`WholeBatch = Config.WholeBatch ?? (recorded ? 0 : 1)`，只派生给未显式 `Search` 的玩家（与 `StagnationRounds` 同一行）。
- `src/Siege.Sim/Program.cs`：`--whole-batch`（`cli.GetInt` 严格整数，0 / 1 由 `Validated` 把关，报"整批候选开关须为 0 或 1"后退出、不写输出）；`PrintUsage` 一行。
- `src/Siege.Sim/Logging/MatchLog.cs`：`WholeBatchOf(player)`（显式 Search 优先，否则首部跑局配置，缺项按 0）。

测试
- 新增 `tests/Siege.Core.Tests/AiDecision/整批候选Tests.cs`（Requirement「整批候选」7 个 Scenario + 补充：留一法恰等于阈值、配对补上剪枝漏掉的组合、专家首部落成、模拟对手清零、文本往返、旧日志按 0 回放、命令行写入记录）。
- `停手阈值Tests.无落点被保留则Pass`（MODIFIED：整批候选生成过且为空）；`高部署上限下的候选剪枝Tests.整批候选不计入候选批次上限`（MODIFIED：≤ M + 2）。
- 开关 0 守门：`决策序列基线Tests.整批候选关闭时与改前基线相同`（5 份 `*-wb0.txt`）、`整批候选关闭时一层配置的专家黄金值与改前相同`、`整批候选关闭时一层配置的记录与改动前相同`、`整批候选关闭时非专家日志与改动前相同`、小图 / 严格提高 / 链接图黄金的开关 0 断言。
- 重钉与格式 / 版本字面量见 `rebaseline.txt`。夹具：`decisions-link1-*-wb0.txt` 5 份（改前原文逐字节复制），`decisions-link1-seed3.txt` / `hard-seed1.txt` 重建（CRLF）。

## 先红（1.1）

分两步以免"编译失败的假红"：先只加字段与 Sim 管线（预设带 1、`LastWholeBatch` 恒 null、不生成候选），再跑新测试。
改前代码上新测试引用 `WholeBatch` / `LastWholeBatch`，无法编译——不算红；第一步代码上红 7 条行为测试：
两枚一起才有收益、剪枝去掉不出力的子、留一法下降恰等于阈值也剪掉、配对候选补上剪枝漏掉的组合、整批候选不消费随机、停手阈值Tests.无落点被保留则Pass、整批候选不计入候选批次上限。
另红 2 条是测试自身写错（`无效开关被拒绝` 嵌套 Search 抛 `ArgumentOutOfRangeException` 应用 `ThrowsAny`；`关闭时逐步不变` 读的是内存日志而非解析后首部），修测试后转绿。
配置 / 记录类测试在第一步即绿（它们测的就是管线），其守门力由变异 e 与下表证实。实现后 22 条全绿。

构造局面说明：规格算例写单点 −5 / 10、组合 +600；构造局面（G7 被夹只剩 F7 / H7 两气）里 F7、H7 对称，单点都是 +2（同样不过阈值 20），组合取眼位权重 595 使其恰为 +600；剪枝算例"拿掉 C 只降 5、拿掉 A / B 降 ≥ 300"逐数相符（D5 降 5，F7 / H7 各降 323）。

## 变异验证（1.7）

脚本 `scratchpad/wb/mutate.py`：二进制读写、行尾归一锚点且命中恰 1 次、还原在 finally、逐字节比对原文、`os.utime` 刷新；每条跑全量 `dotnet test`（2224 条）。

| 变异 | 改动 | 红 |
|---|---|---|
| a 去掉剪枝候选 | `if (prunedAdded)` → `&& candidates.Count < 0` | 13：整批候选不计入候选批次上限、剪枝去掉不出力的子、两枚一起才有收益、seed3 基线、严格提高（开启对照）、非专家 / 一层记录、G1 ×2、缺省预设专家 ×2、诊断不改对局、复盘不改对局 |
| b 去掉配对候选 | `if (pairedAdded)` → `&& candidates.Count < 0` | 12：配对候选补上剪枝漏掉的组合、整批候选不消费随机、小图不限制、困难基线、非专家 / 一层记录、G1 ×3、缺省预设专家 ×3 |
| c 留一法 `>` → `>=` | `LeaveOneOutHolds` | 2：留一法下降恰等于阈值也剪掉、非专家没有前瞻记录 |
| d 开关失效 | `WholeBatch == 1` → `>= 0` | 22（含全部开关 0 守门：5 份 wb0 中 2 份、G1 关闭 ×3、关闭的记录 / 非专家、关闭时逐步不变、小图开关 0 等） |
| e 回放缺项按 1 | `MatchSession`：`?? (recorded ? 0 : 1)` → `?? 1` | 1：首部缺整批候选开关的旧日志按0回放 |
| f 模拟对手不清零 | 删 `SimulatedConfig` 的 `WholeBatch = 0` | 10：模拟对手取专家本人的口径、模拟回应等于专家口径的不扰动贪心、前瞻模拟的对手不生成整批候选、一层记录、G1 ×3、缺省预设专家 ×3 |

全部还原后逐字节一致并 touch；之后全量 2224 / 2217 / 7 跳过，退出码 0。

## 测试数与构建

- 改前基准 2198 / 2191 / 7；改后 **2224 / 2217 / 7 跳过，`dotnet test` 退出码 0**（`DOTNET_CLI_UI_LANGUAGE=en`）。
- `dotnet build siege.sln -c Release`：0 警告 0 错误。

## 门控（1.8）

- 预演次数代理（默认套件 `预演次数代理计入新增部分`）：**2.954**（改前 2.948），局面 48、高难 37285 / 专家 110132；≤ 4。
- 计时（`SIEGE_PERF=1 … Category=Perf&FullyQualifiedName~专家前瞻耗时计时`）：中位数比 **3.475**（改前 3.58），高难 205.9 ms / 专家 715.5 ms；≤ 4。

## Godot / 终端 play 路径

- 终端 `play`（`src/Siege.Sim/Play/PlayCommand.cs:47`）与 Godot（`src/godot/scripts/MatchSession.cs:37`）都直接取 `AiSearchConfig.ForMap(difficulty, …)` 预设，不经跑局层派生。
  预设在 Core 带 `WholeBatch = 1`，两者随之缺省开（简单难度由代码不生成）。未改 `src/godot`、未改 Presentation；未跑 Godot。
- 日志 / 配置：只有跑局层（`RunConfig` / `MatchSession` / 首部）写开关；play 与 Godot 不写日志首部，无需改动。

## 待决项

1. **`--whole-batch 0` 的 config.json 再当输入**：规格要求开关 0 不写出，于是一份开关 0 批次的 `config.json` 被 `run --config` 当新配置再跑时缺该项、按缺省 1 落成（回放不受影响：回放走 recorded，缺项按 0）。需要时在 HANDOFF 命令行一节提示"关闭整批候选须在命令行显式 `--whole-batch 0`"。
2. **80 局验证的"整批候选被选次数（按来源）"**：日志里没有该字段（只有控制器留痕 `LastWholeBatch`），从 CLI 跑出的日志算不出来。两个选项：
   (a) 临时探针：逐小回合驱动 `MatchSession.RunTurn`，每步读 `AiOf(p).LastWholeBatch?.SourceOf(LastChoice)` 计数（本次首个分歧诊断即用此法，不改日志格式、不动黄金值）；
   (b) 加快照字段：会改 `DeterministicText`，刚重钉的开关 1 侧黄金值（前瞻记录、非专家、链接图等哈希）要再钉一次。请主会话选。
3. `OneLayerConfig`（`LookaheadFixtures`）**未**像 cycle-stop 那样把新开关写死为 0：按 design D5 / 派发要求，G1 与前瞻记录在缺省开下重钉，开关 0 另设守门（与 boundaries.md「依赖走法的既有夹具显式写死旧口径」的先例不同，已在夹具注释说明）。
   `停手阈值Tests.阈值为0时零变化` 则显式关掉（其对照实现早于整批候选），另钉缺省开的同局哈希 `StrictImprovementTurnHashWholeBatch`。
4. design Open Question（配对起点之后是否也做留一法剪枝）：按当前口径不做。
5. 规格 Scenario「开关进入记录」（"标准玩家的搜索配置记有整批候选开关 1"）的读法：实现为首部跑局配置 `"WholeBatch":1` + `MatchLog.WholeBatchOf(player)`（与会话派生同一口径）+ 会话 AI 的 `Config.WholeBatch`；
   标准玩家的 `Players[].Search` 仍不落成（保住 `停手阈值Tests.阈值不随地图变化` 的 `Assert.Null(p.Search)`，与停滞大回合数同口径）。若要求逐玩家 Search 落成，需主会话裁决。
6. design Open Question 之外无其他未决。改前副本 worktree 已移除，未用于重钉；首个分歧诊断的中间输出在 scratchpad `wb/div/*.txt`。
