# 实施记录：pass-threshold-first-stone

## 1.1 测试（先红）

`tests/Siege.Core.Tests/AiDecision/停手阈值Tests.cs` 新增三个 Scenario + 夹具 `LoneStones`：
- 局面：`Round5()`，P0 在四邻全空的格上落普通子（孤子势力增量 = 棋串军势 1 + 独占领地 4 = 5，实测）；简单难度、阈值 80（`DefaultPassThreshold`）；
  权重写死 `PreCalibrationWeights with { PowerGain = 6 }`（眼位 0），每枚边际提升恰为 30，逐维断言原始值（PowerGain 5 / EnemyLoss 0 / Eye 0）与总分。
- `无子时不受阈值限制`：无子、上限 1、E5 → 保留，总分 30。
- `有子后恢复阈值`：同局面多一枚远处己方子 A1 → Pass。
- `同批次内不中途切换`：无子、上限 2、B2 / E5 → 两枚都保留，总分 60（Easy 只有 1 个候选批次）。
- 每条都用 `Assert.True/False(match.Board.GroupsOf(Me).IsEmpty)` 钉住"无子 / 有子"前提。

实现前实跑：`无子时不受阈值限制`、`同批次内不中途切换` 红；`有子后恢复阈值` 绿（它描述的就是改动前的行为，这里只作反面对照）。

既有夹具修正：`无落点被保留则Pass` 的 `Open()` 里 P0 原本无子，豁免后 `int.MaxValue` 阈值会变成 0 → 给 P0 加一枚远离候选区的 A1，并断言 P0 有子。改后在改动前后都绿；`eager` 分支（阈值 0）仍落子。

## 1.2 实现（D1）

`src/Siege.Core/Ai/HeuristicTurnController.cs`：
- `Deploy` 只调一次 `_observe()`，同一份公开快照既建评价器（新增私有 `CreateEvaluator(MatchPublicView)`，公开无参版委托它）又算实际阈值。
- 新增私有 `EffectivePassThreshold(view)`：`view.Board.GroupsOf(Player).IsEmpty ? 0 : Config.PassThreshold`。决策入口算一次，作为参数传给 `Greedy`（单点排序本来就不看阈值），组批过程中不重算。
- `Config.PassThreshold`、日志首部、`_decisions` 都不动（D2：不加新字段）。

变异（全量 `dotnet test -c Release`，脚本二进制读写、try/finally 还原、逐字节比对、`os.utime` 刷新 mtime）：

| 变异 | 改动 | 结果 |
|---|---|---|
| M-F1 去掉豁免 | `EffectivePassThreshold` 改成 `…IsEmpty && Config.PassThreshold < 0 ? 0 : Config.PassThreshold`（运行时恒取配置值） | 红 2：无子时不受阈值限制、同批次内不中途切换 |
| M-F2 按暂放后盘面判定 | `Greedy` 保留条件改成 `> (batch.Count > 1 ? Config.PassThreshold : passThreshold)`（批次里已有先前保留的暂放子即恢复配置阈值） | 红 1：同批次内不中途切换（无子时不受阈值限制仍绿） |
| M-F3 豁免条件反转（补做，给改动前即绿的「有子后恢复阈值」一条能红的证据） | `EffectivePassThreshold` 改成 `IsEmpty ? Config.PassThreshold : 0` | 红 8：有子后恢复阈值、无子时不受阈值限制、同批次内不中途切换、低于阈值不落子、恰等于阈值不落子、无落点被保留则Pass、候选格上限Tests.缺省不限制时标准图整局与改动前逐步相同、地形改造日志与分析Tests.地形可离线重建 |

三条还原后均逐字节一致并刷新 mtime；M-F1 / M-F2 之后先重建 Release 再跑冒烟，M-F3 之后重跑全量确认（见 2.3）。

## 1.3 黄金哈希

**实测事实**：实现后全量直接全绿，`V4GoldenTurnHash`、`StrictImprovementTurnHash` 等依赖走法的期望都没有分叉，无需重建。
探针即 M-F1（临时去掉豁免）：去掉前后，除本 change 新增的两条之外全量结果完全一致（M-F1 只红那两条），没有任何哈希测试翻转——两份黄金哈希与豁免无关。
反面证据（豁免确实生效）：同配置的简单难度冒烟首回合全员 Pass 由 8 / 20 变为 0 / 20（见 2.1）。哈希不分叉是因为仓内没有钉在阈值 80 口径下、开局无子的走法期望，不是改动没生效。
**推测（未逐步比对）**：两份黄金哈希钉在阈值 20（`PinPreCalibration`）或 0 的口径，开局首批每枚的边际提升都超过 20，豁免不改变选择。

## 2.1 冒烟（只报告，未调参）

配置 `sim-out/pass-threshold-first-stone/cfg-easy20.json`（抄 `superko-occupancy/cfg-easy.json`，只把 Count 改成 20）：v5、种子 1–20、4 名 Easy、默认权重、阈值 80、截断 600 小回合。
数据 `sim-out/pass-threshold-first-stone/easy20/`；指标脚本 `sim-out/pass-threshold-first-stone/smoke.py` → `easy20/smoke-metrics.json`。耗时 255 s。

| 指标 | 值 |
|---|---|
| 首回合全员 Pass | 0 / 20（第 1 大回合 80 个小回合里 Pass 0 次） |
| 截断 | 4 / 20（种子 1、3、10、12，均 turn_limit R150） |
| 终局原因 | AllPassed 16、turn_limit 4 |
| 结束大回合（全部 20 局） | 平均 48.05、中位 17.5、最长 150、最短 8 |
| 结束大回合（16 局已终局） | 平均 22.56、中位 15、最长 78 |

参考：`sim-out/superko-occupancy/easy-aborted-partial`（已核对其 config.json：v5、4 名 Easy、同一组权重、阈值 80、截断 600，与本次一致；豁免之前的二进制）种子 1–20 中，首回合全员 Pass 8 / 20（种子 1、2、3、6、9、12、16、18）。

## 2.2 文档

`2026-09-10-siege-core-gameplay-design-v1.md`：版本 v1.10 → v1.11；§15.2 停手阈值段补"无子豁免"；§15.2 已知问题第 1 条补修正说明与冒烟数；变更记录加一行。

## 2.3 回归

- `dotnet build siege.sln`：0 警告 0 错误
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误
- `dotnet test -c Release`：通过 1413、跳过 5（慢测试 / 计时，按门控默认跳过），退出码 0
- `openspec validate pass-threshold-first-stone --strict`：valid

## 待决

- 工作树里有两份未跟踪的 `src/godot/scripts/PartExport.cs.uid` / `TerrainParts.cs.uid`（14:25 生成，Godot 生成物；仓库跟踪同类 .uid 15 份，对应 fafe15b 新增的两个脚本）。不属于本 change，未删除、未纳入，由主会话决定。

- 简单难度截断 4 / 20（都停在 R150、600 小回合），只报告；是否另开 change 处理由负责人定。
