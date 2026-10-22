# ai-bridge-candidate 实施记录（tasks 1.1–1.8，trellis-implement）

## 改动文件

Core
- `src/Siege.Core/Ai/AiDifficulty.cs`：`AiSearchConfig.EditSeed`（int，构造缺省 0，`JsonIgnore WhenWritingDefault`，参数表末尾、接在 `WholeBatch` 之后）、`DefaultEditSeed = 1`，四档预设带 1；`Validated()` 拒绝 0 / 1 以外（文案含"搭桥候选"）。简单不生成由代码按 `ImmediateOnly` 判。
- `src/Siege.Core/Ai/HeuristicTurnController.cs`：
  - `Deploy` 把单点评价的全部单点 `ranked`（已排序、未截断）传进 `ChooseByCandidates`。
  - `ChooseByCandidates`：Pass 的评价改为两种候选共用、至多一次（整批候选开着时仍在 `AppendWholeBatch` 原时机最先算；只开搭桥候选时等到真有可评的搭桥单点才算）。`AppendWholeBatch` 改收 `pass` 参数，行为不变。
  - 新 `AppendBridgeSeed`（design D1）：开关为 1 且非 `ImmediateOnly` 时在 `AppendWholeBatch` 之后调用。手中无匠人 → 留痕无起点。枚举合法范围全部可落子空格（**不经** `PrefilterCells`、不受前 N）× 匠人 × `EditOptions` 中 `Kind == Bridge` 的目标：进入过完整枚举的格（`LastCandidateCells`）上的单点从 `ranked` 复用（落选的不再预演），只有预筛挡掉的格经 `ScoreOne` 新评（与 `ScorePoints` 同一路径）。起点 = `Rank(收益 > 阈值的搭桥单点).First`（比较只在 `BridgeSeedHolds` 一处）；复摆起点后 `ContinueGreedy(ranking, …, 同阈值, 起点评价)`；键重复不加。不调用 `Perturb`，零随机。停滞停手与决胜一手分支未动。
  - 新留痕 `LastBridgeSeed`（`BridgeSeedCandidate`）：生成路径走过即非空。
- `src/Siege.Core/Ai/CandidateBatch.cs`：`BridgeSeedCandidate(Seed, Candidate, Added, Scored)` + `IsSourceOf(choice)`；`Scored` = 本次新评的搭桥选项数（预筛挡掉的格上的）。
- `src/Siege.Core/Ai/ExpertLookahead.cs`：`SimulatedConfig` 追加 `EditSeed = 0`。
- `src/Siege.Core/Ai/EvaluationBreakdown.cs`：`Version` 10 → 11（文档补"11 = 搭桥候选"）。

Sim
- `src/Siege.Sim/Config/RunConfig.cs`：`EditSeed`（int?，`[JsonIgnore]`）+ 私有序列化影子 `EditSeedRecorded`（0 不写出）；`Validated` 拒绝 0 / 1 以外；`ResolvedFor` 未配置落成 1，专家 `Search` 落成时带上实际值。
- `src/Siege.Sim/Running/MatchSession.cs`：`EditSeed = Config.EditSeed ?? (recorded ? 0 : 1)`，只派生给未显式 `Search` 的玩家。
- `src/Siege.Sim/Program.cs`：`--edit-seed`（`cli.GetInt` 严格整数，0 / 1 由 `Validated` 把关）；`PrintUsage` 一行。
- `src/Siege.Sim/Logging/MatchLog.cs`：`EditSeedOf(player)`（显式 Search 优先，否则首部跑局配置，缺项按 0）。

测试
- 新增 `tests/Siege.Core.Tests/AiDecision/搭桥候选Tests.cs`：Requirement「搭桥候选」7 个 Scenario（前N之外的搭桥被看见、预筛挡掉的格也枚举、收益不过阈值不生成、搭桥候选不消费随机、关闭时逐步不变、简单难度不受影响、无效开关被拒绝 ×2）+ 补充：开关进入记录、专家首部落成、模拟对手清零、文本往返、旧日志按 0 回放、命令行写入记录。
- MODIFIED：`停手阈值Tests.无落点被保留则Pass`（搭桥候选走过生成且为空）+ 新 `搭桥单点收益不过阈值时仍Pass`（搭桥局面，阈值 1351 → Pass；149 → 搭桥）；`高部署上限下的候选剪枝Tests.不穷举排列`（≤ M + 3）+ 新 `整批候选与搭桥候选都不计入候选批次上限`。
- 开关 0 守门：`决策序列基线Tests.搭桥候选关闭时与改前基线相同`（5 份 `*-es0.txt`）、`搭桥候选关闭时一层配置的专家黄金值与改前相同`、`搭桥候选关闭时一层配置的记录与改动前相同`、`搭桥候选关闭时非专家日志与改动前相同`、链接图 / 小图 / 严格提高的开关 0 常量与断言；整批候选的旧守门样本同时关掉两个开关。
- 夹具：`decisions-link1-*-es0.txt` 5 份（改前原文在任何改动之前逐字节复制）；`decisions-link1-{seed1,seed2,seed3,hard-seed1}.txt` 重建（CRLF）。`LookaheadFixtures.ProbePositionsWith`（局面集对局配置钩子，`null` 时与 `ProbePositions` 逐字节相同）。
- 重钉、版本 / 格式字面量与样本写死旧口径见 `rebaseline.txt`。

## 构造局面（搭桥候选Tests.Moat）

E5 深水，P0 七子把它三面围住只空 E6；匠人落 E6 搭桥 E5 → 单格眼。P1 孤子 G9 只剩 G8 一口气。合法范围 {E6, G8}、普通 5 + 匠人 5、部署上限 2；
权重写死"势力 1、敌损 300、眼位 148"，阈值 20：搭桥单点 = 150（规格算例的收益），排第 19 名（G8 一格 18 个单点都是 1351，占满前 N = 12）；
以它为起点续组 {搭桥, G8} = 1501，高于全部既有候选（只落 G8 = 1351）。候选格上限 1 时预筛只留 G8（E6 在完整枚举之外）。
与规格算例的字面差异：名次第 19（算例第 30，同在前 N 之外）；「预筛挡掉的格也枚举」取候选格上限 1（算例 24；构造局面只有 2 个合法空格，K 须小于空格数预筛才生效）。

## 先红（1.1）

分两步：先只加字段与 Sim 管线（预设带 1、`LastBridgeSeed` 恒 null、不生成候选），再跑新测试。第一步代码上红 8 条行为测试：
前N之外的搭桥被看见、预筛挡掉的格也枚举、收益不过阈值不生成、搭桥候选不消费随机、停手阈值Tests.无落点被保留则Pass、停手阈值Tests.搭桥单点收益不过阈值时仍Pass、
高部署上限下的候选剪枝Tests.不穷举排列、整批候选与搭桥候选都不计入候选批次上限（都因留痕为空或未生成）。配置 / 记录 / 守门类测试在第一步即绿（测的就是管线），其守门力见下表变异。实现后全绿。

## 变异验证（1.7）

脚本 `scratchpad/es/mutate.py`：二进制读写、锚点命中恰 1 次、还原在 finally、逐字节比对原文、`os.utime` 刷新；每条跑全量 `dotnet test`（2250 条）。

| 变异 | 改动 | 红 |
|---|---|---|
| a 去掉搭桥候选 | `bool added = keys.Add(candidate.Key)` → `&& candidates.Count < 0` | 26：搭桥候选Tests 4 条（前N之外、预筛挡掉、收益不过阈值、不消费随机）、停手阈值 2（搭桥单点收益不过阈值时仍Pass、阈值为0时零变化）、候选数上限 1、决策基线 3（seed1 / seed2 / hard）+ 链接模式 seed3 + K为0零变化、G1 ×3、缺省预设专家 ×3、前瞻记录 / 非专家、诊断 / 复盘不改对局、带入带出 ×2 / 内容集 v1 / 缺省关闭带入 |
| b 枚举改成经预筛 | 枚举格加 `&& false`（只剩已完整枚举的格） | 16：预筛挡掉的格也枚举、搭桥候选不消费随机（K = 1 局面）、阈值为0时零变化、决策基线 3 + 链接模式 + K为0、前瞻记录 / 非专家、诊断 / 复盘、带入带出 ×2 / 内容集 v1 / 缺省关闭带入（G1 用 K = 0，不红） |
| c 起点阈值 `>` → `>=` | `BridgeSeedHolds` | 1：收益不过阈值不生成（收益恰等于阈值 150） |

全部还原后逐字节一致（sha256 复核）并 touch；之后全量见下。

## 测试数与构建

- 改前基准 2224 / 2217 / 7；改后 **2250 / 2243 / 7 跳过，`dotnet test` 退出码 0**（`DOTNET_CLI_UI_LANGUAGE=en`；变异还原后重跑同）。
- `dotnet build siege.sln -c Release`：0 警告 0 错误。

## 门控（1.8）

- 预演次数代理（`预演次数代理计入新增部分`）：**2.985**（改前 2.954），局面 48、高难 37126 / 专家 110836；≤ 4。
- 计时（`SIEGE_PERF=1 … Category=Perf&FullyQualifiedName~专家前瞻耗时计时`）：中位数比 **3.591**（改前 3.48），高难 249.4 ms / 专家 895.5 ms；≤ 4。

## Godot / 终端 play 路径

终端 `play` 与 Godot 直接取 `AiSearchConfig.ForMap` 预设，预设带 `EditSeed = 1`，随之缺省开（简单不生成）。未改 `src/godot`、未跑 Godot；play 与 Godot 不写日志首部，无需改动。

## 待决项

1. `Deploy` 在 `ranking.IsEmpty` 时提前 Pass，先于候选生成：预筛保留的 K 格全部落选、而预筛外有只能靠搭桥才落得下的格时，不会走到搭桥候选（规格要求枚举全部合法空格）。结构未动，罕见；需要时另议。
2. 前瞻记录（种子 6）首个分歧是并列：构筑期专家取高难选择，搭桥候选与原选择同为 902，按 CandidateSelection 坐标字典序（O8 < P8）胜出。判据"被选中的是搭桥候选"成立，但不是"总分更高"。
3. 两个依赖走法的样本写死开关 0（公平信息的样本对局、两层加分的局面集），另加了缺省开的整段复核（公平信息）；如不接受写死旧口径需另选样本。
4. 同 ai-whole-batch 待决 1 / 2：`--edit-seed 0` 的 config.json 再当输入时缺项按缺省 1 落成；40 局验证的"搭桥候选被选次数"日志里没有字段，需临时探针读 `LastBridgeSeed.IsSourceOf(LastChoice)`（不改日志格式）或加快照字段（要再钉一次黄金值）。
5. 诊断探针副本：`scratchpad/es/ZzDivergence.final.cs`（同步跑局比对、两层加分局面集对照、夹具重建），已从测试工程删除。
