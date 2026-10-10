# 段 C 质检报告（board-link-mode tasks 3.0–3.7）

范围：`d55127b`（段 C 前半）+ 工作区未提交的 3.4–3.6 + 本次落实的 3.7（裁决 C15）。基准对照 `3fb4978`。

## 1. C15 落实（tasks 3.7，已勾）

- **实现**：`src/Siege.Core/Ai/HeuristicTurnController.cs` 的 `EffectivePassThreshold` 改为 `己方盘上无子 || TrailsInBattlefield(view, Player) ? 0 : 配置值`，与无子豁免是同一个表达式，没有第二个判定点。
  新增的 `internal static TrailsInBattlefield`：旧模式、无势力快照、未锁定出生棋盘时返回 false；否则取 `PublicRules.BattlefieldOf(view.Board, zone)`，同战场对手 = 出生棋盘在本战场内的其他在局玩家（与按座位投影同一口径）；只有某个对手的总势力**严格大于**自己才返回 true，所以并列最高算最高；单人战场里没有对手，返回 false。评价权重未改。
- **前瞻复用点**：不需要另写。理由有二：
  1. 专家前瞻的模拟对手（`ForSimulation`）走同一个 `Deploy` → `EffectivePassThreshold`，读的是 `ExpertLookahead.Project` 生成的投影视图（`with` 保留了 `Mode`，`Power` 已按 B1 重算）。
  2. 多样补充的排除重跑（`Rerun`）用的是同一次算出的 `passThreshold`。
  `SimulatedConfig.PassThreshold` 仍是配置值，没有动。
- **测试**：`tests/Siege.Core.Tests/AiDecision/链接模式下落后者的停手阈值Tests.cs`，共 7 例。局面为 link:1 棋盘 0 上的孤子点，简单难度，PowerGain 写死为 1，收益恰为 5，阈值写死为 20。
  - 落后者不轻易停手 ×2（正式 AI / 前瞻模拟对手）
  - 领先者照常停手 ×2（同上）
  - 并列最高视为最高
  - 等待者照常停手：P0 独占一个战场，全局势力低于别人也照常停手
  - 旧模式不变：Legacy 局里落后也 Pass。逐字节口径由既有 v5 / board:1 决策序列基线与 Legacy 黄金哈希守住
- **链接图基线重钉**：按 3.4 的口径（`SIEGE_REBUILD_DECISION_BASELINE=1`）重钉，结果与改动前的备份**逐字节相同**，差异为 0：0 个小回合的决策改变，Pass 次数 0 → 0。
  证据是重钉后与备份 `cmp` 逐字节相同，这本身就证明没有任何落点因阈值从 20 降到 0 而改变保留结果。
  解释（不是证明）：用临时探针（已删除）统计，种子 3 前 12 大回合的 48 个小回合里，有 18 个处于"本战场势力落后"，这些小回合的最好单点收益都 ≥ 260。
  结论：这条基线守不住 C15。已在 `决策序列基线_链接模式Tests` 的注释里写明，C15 只由上面的单元测试守。旧 v5 / board:1 基线和全部 Legacy 黄金值不变（全量绿）。
- **变异**（只跑相关几类测试；每次都已还原，最后全量绿）：

| # | 变异 | 结果 |
|---|---|---|
| MC-C15a | `EffectivePassThreshold` 恒取 0 | 红 12：领先者 ×2、并列、等待者、旧模式、停手阈值Tests 4 条（恰等于 / 低于阈值不落子、有子后恢复阈值、无落点被保留则Pass）、前瞻中的停手口径.高难会Pass时专家也Pass、v5 决策序列基线、候选格上限 Legacy 黄金哈希 |
| MC-C15b | 去掉落后者判定（恒不取 0） | 红 2：落后者不轻易停手 ×2（链接图基线不红，原因见上） |
| MC-C15c | 比较改为 `>=` | 红 1：并列最高视为最高 |

## 2. 逐项检查结论（按严重度）

### 中（已自修）
1. **"过早 Pass 落败"在报告文字里没有写明是上界**（`ReportWriter.AppendLinkMode`）。口径是"手里有子"，没有逐格核对禁入格和部署上限。已在报告行和 `LinkModeSection` 的参数注释里写明"为上界"，并在 `链接模式平衡分析Tests.等待时长可统计` 补了断言。
2. **match-telemetry「改造与链接记录足以离线重建地形」在链接模式下没有测试**。已补 `对局日志的记录内容_链接模式Tests.地形可离线重建`：只读文本往返后的日志，按首部重建开局图并核对摘要，按顺序把每个链接事件交给生成器并逐次核对摘要，再重放全部改造（样本口径断言确有改造，实测 2 次），结果与真实终局地形逐格相同；另有反面断言：少重放一次链接，摘要就对不上。
3. **simulation-harness「带入对局可回放」只有 Legacy 测试**。已补 `可复现回放_链接模式Tests.带入对局可回放`：开启带入的链接图局（CarryIn = 1，四人四区种子，含配对与链接）回放后逐行一致。

### 低
4. **`MatchLog.AllRelics` 修复**：旧日志的报告逐字节不变，由 `平衡分析方向Tests` 的报告黄金哈希守住；链接侧由 MC-33 守。已补一条显式回归断言：旧日志的 `AllRelics` 逐项等于首部信物（已自修）。
5. **`RunConfig.FlagRisk` 的注释仍写着"命令行 `--flag-risk`"**。已改为注明 C2 / C8 下选项已删除、字段只留给夹具、链接图配置写了即报错（已自修）。
6. **既有分析段在链接日志上的分母**（实施报告"遗留与风险"第 3 条）：已核实，未修改。
   - 冲突占用率（`BalanceAnalyzer` 约 1010 行）和地图规模段（约 736 行）用的是首部 `PlayableCells`，即开局 5 块出生棋盘的格数。链接后实际可落子格远多于这个数，所以链接局的占用率偏高，可能超过 100%；地图规模只反映开局规模。
   - 活形段的禁入占比（约 629 行）用的是最后一个快照的 `PlayableCells`，链接后的口径是对的。
   - 新的链接模式段的"终局全图可落子格"同样取快照的 `Life.PlayableCells`。
7. **match-telemetry Scenario「日志覆盖十二类记录」**没有同名的链接模式测试。第 8–11 类分别由 `配对与链接可查` / `战场结算可查` / `等待时长可查` / `旧模式日志不写链接模式字段` 覆盖，第 1–7、12 类由 Legacy 的 `日志覆盖八类记录` 覆盖。可以追溯，但比较分散。建议段 E 回归时补一条汇总测试，不阻塞。
8. **C7 改写的 `各入口支持生成图Tests`**：去掉了"每局换图 gen:100:s1..gen:101:s1"这条打印文案断言，改成 `MapIdAt(1)` 断言。这在 C7 的改写范围内，但该打印文案在 Legacy 侧已经没有测试守。知悉即可。

### 通过
- **C1**：`MapCatalog.cs`、`src/Siege.Presentation`、`src/godot` 自 `3fb4978` 起零改动；只切了 Sim 的四个入口。
- **C2 / C8**：`--flag-risk` 已删；`RunConfig.FlagRisk` 保留；链接图配置写了即报错（`RunConfig` 第 256 行）；`PlayCommand` 的 `flagRisk` 只是测试接缝，命令行不再认领。
- **C3**：Legacy 日志写出逐字节不变（黄金哈希全绿）；`replay` / `analyze` 只在命令层拒绝旧日志（MC-11 / MC-32），内部的 `Replayer` / `BalanceAnalyzer` 仍能读旧日志。
- **C4**：全知开关只在 `HeuristicAi.VisionFor` 一处分流；`SeatVision` 缺省为 false 时不写出。
- **C9**：`RandomNumberGenerator` 只出现在 `PairingSources.cs` 的 `RandomPairingSource` 内，有守门测试。
- **C11**：`AiSearchConfig.ForMap` 与候选格上限的逻辑自 `3fb4978` 起未改动。
- **Legacy 不变**：`git diff 3fb4978 -- tests` 中改动的既有测试只有 C7 的改写（棋盘档生成图标识 / 各入口按地图标识选图 / 各入口支持生成图 / 各入口的地图专属AI权重 / 冒险概率记录删 1 条）、C9 守门（批量跑局Tests）和段 B 测试文件的追加（链接模式存档Tests.链接恢复须四人），其余既有断言未改。
- **配对来源**：`run` 缺省由对局种子派生，在工具层的 `SeededPairingSource` 里做，不经过 Core `GameSeed`（MC-13 守）；`--pairing-seed` 覆盖缺省（MC-8）；`play` 用真随机；`replay` 用 `RecordedPairingSource` 读记录（MC-9 / MC-12）；`PairingDecision.Source` 写入日志。三种来源共用 `PairingDraw.Draw`，没有重复实现。
- **抽查复现的变异**：把 `Replayer` 首部的地图摘要比对改为恒不比对 → `各入口支持生成图_链接模式Tests.生成器变了` 红 1；还原后绿。
- **analyze 链接模式段的口径**：同区 = 锁定时某块棋盘旗数 ≥ 2；首轮交战轮数扣除构筑期（MC-29）；链接后可落子格取快照；旧日志的报告没有这一段（断言 + 黄金哈希）。
- **硬边界**：Core 的改动不含 Godot、浮点、系统随机或时钟，坐标只经 `Coord`；Sim 的浮点只出现在 `Analysis/`（有源码扫描守门）；`LinkRecord.Corridors` 不在 `LinkSaveData.Entry` 里，不入存档，规则也不读它。
- **代码质量**：`RebuildPlayers` / `DefaultPlayerCount` 各只有 `Program.cs` 一处实现，测试直接调用它们；报错信息都点名了标识或字段；注释引用了裁决编号。
- **改动范围**：只涉及 `src/Siege.Core/**`、`src/Siege.Sim/**`、`tests/**`、`openspec/**`、`.trellis/**`。

## 3. 自修清单
- `src/Siege.Core/Ai/HeuristicTurnController.cs`：C15 的判定点与 `TrailsInBattlefield`。
- `tests/Siege.Core.Tests/AiDecision/链接模式下落后者的停手阈值Tests.cs`（新增）：C15 四个 Scenario + 并列 + 前瞻模拟对手，共 7 例。
- `tests/Siege.Core.Tests/AiDecision/决策序列基线_链接模式Tests.cs`：注释记录 C15 重钉结果（零差异及原因）。
- `src/Siege.Sim/Analysis/ReportWriter.cs`、`src/Siege.Sim/Analysis/LinkModeAnalyzer.cs`：在报告文字和注释里写明"过早 Pass 落败"是上界。
- `tests/Siege.Core.Tests/MatchTelemetry/链接模式平衡分析Tests.cs`：补"为上界"文字断言和旧日志 `AllRelics` 回归断言。
- `tests/Siege.Core.Tests/MatchTelemetry/对局日志的记录内容_链接模式Tests.cs`：新增「地形可离线重建」（链接模式）。
- `tests/Siege.Core.Tests/SimulationHarness/可复现回放_链接模式Tests.cs`：新增「带入对局可回放」（链接模式）。
- `src/Siege.Sim/Config/RunConfig.cs`：`FlagRisk` 的注释去掉已删除的命令行选项。
- `openspec/changes/board-link-mode/tasks.md`：勾上 3.7。

## 4. 构建与测试
- `dotnet build siege.sln -c Release --no-incremental`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：通过 2303，失败 0，跳过 11（Perf / 长跑类，按惯例跳过），耗时 6 分 50 秒。
- 未跑冒烟或任何批量对局。

## 5. 提交建议
建议提交，没有阻塞项。新的需裁决事项：

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| C19 | C15 在固定基线上效果为零：种子 3 前 12 大回合里，落后者的最好收益都远高于阈值；冒烟报告中"过早 Pass 落败 57/58"发生在长局后段，C15 能改善多少目前没有数据 | 只有单元测试守住 C15 的判定 | 段 C2 剖析之后，或另开一次有限规模的冒烟（例如 `link:1` 标准难度 5–10 局）复核过早 Pass 落败次数；规模由负责人定，本次未跑 |
