# expert-opponent-model 实施记录（trellis-implement，tasks 1.1–1.7）

状态：tasks 1.1–1.7 完成；未 commit，未改 `openspec/`。1.5 一度按原判据阻断（种子 1 / 6 的首个分歧只差模拟预演次数），主会话修订判据后（首个分歧落在已前瞻决策的前瞻记录上，模拟回应键或模拟预演次数不同都算；构筑期记录逐字相同）已重钉，见 `rebaseline.txt`。

## 改动文件

- `src/Siege.Core/Ai/ExpertLookahead.cs`：`SimulatedConfig` 改为 `_config with { CandidateBatchCount = 1, LookaheadWidth = 0, DiverseSupplementLimit = 0, TwoPlyWeightPermille = 0, TwoPlyTieBand = 0 }`；类注释、`SimulatedConfig`、`Respond` 的 XML 注释由"标准启发式"改为"专家本人的高难口径"。
- `src/Siege.Core/Ai/HeuristicTurnController.cs`：只改 `ForSimulation` 的 XML 注释（标签说明，见下）；守门不变。
- `src/Siege.Core/Ai/EvaluationBreakdown.cs`：`Version` 8 → 9，版本说明加第 9 条。
- `tests/Siege.Core.Tests/AiDecision/前瞻模拟的公平信息Tests.cs`：新增 4 个 Scenario 的测试（5 个用例）与独立复算辅助 `CheckSimulatedResponses` / `ResponseCheck`。
- `tests/Siege.Core.Tests/MatchTelemetry/专家前瞻的记录Tests.cs`：只因版本号——两处 `":8"` → `":9"` 断言、`PreCycleStop` 的还原字面量 8 → 9 及其注释（哈希值未动）。
- `tests/Siege.Core.Tests/AiDecision/启发式评价维度Tests.cs`：期望版本 8 → 9（加注释一行）。
- 临时导出测试 `Tmp前瞻导出Tests.cs` 已删除。导出文本留在 scratchpad 的 `before/`、`after/`（会话临时目录，不入库）。

## ForSimulation 难度标签

`AiDifficulty.Standard` 只是构造参数：配置显式给出、不按难度取预设；`HeuristicTurnController.Difficulty` 在 src 中没有任何读取方（只有 GameRoot / MatchSession 读会话自己的难度），模拟控制者是 `Respond` 内的局部变量，不进入前瞻记录 ToText、日志或任何确定性文本。**不改**，只补注释。

## 先红（改前代码 HEAD 4013dd1 + 新测试，Release）

- 「模拟对手取专家本人的口径」：**红**（模拟配置 N 为 12，期望 24）。
- 「模拟对手随专家显式配置」：Scenario 原值 N 12 一例**改前不红**——12 恰等于标准预设的 N，标准预设的 S / λ / B 本就为 0，改前改后派生结果相同；另补 N 16 一例，**红**（12 ≠ 16）。这条的主要用途是守"带宽必须清零"（见变异 M2）。
- 「模拟回应等于专家口径的不扰动贪心」：**红**。改前在 G1 种子 1 / 2 / 3（截断第 12 大回合）与种子 6（整局）逐条探查：记录的模拟回应与"标准预设派生"的独立复算逐条相同（36 / 35 / 35 / 69 条）；两种口径给出不同回应的候选只在种子 3 出现（小回合 28 第 7 大回合、小回合 45 第 12 大回合各一条），故样本取种子 3（测试在遇到第一条可区分的候选后停）。
- 「被模拟玩家的难度不影响模拟」：**改前不红**（结构上前瞻拿不到任何对手控制者；同一运行器上挂标准 / 高难的 P1，跑专家一个完整小回合）。没有能在信息边界内写出的使其变红的变异，由信息边界守门兜底。

## 变异验证（Release，改后各用例先全绿）

- M1：`SimulatedConfig` 改回 `AiSearchConfig.Standard with { CandidateBatchCount = 1, CandidateCellLimit = _config.CandidateCellLimit, PassThreshold = _config.PassThreshold, LookaheadWidth = 0 }` → 红 3：取专家本人的口径、随专家显式配置(16)、模拟回应等于…不扰动贪心（小回合 28 的候选：记录 4 子、专家口径复算 5 子 `+D12`）。同一过滤里 G1 种子 1–3 与「一层配置的记录与改动前相同」在 M1 下**全绿**——全量里那 5 条红完全由本改动造成。
- M2：只删 `TwoPlyTieBand = 0` → 红 2：随专家显式配置(12)、(16)（带宽 20 未清零）。
- 还原：备份副本 `cp` 回写，`cmp` 一致，SHA-256 与变异前相同（3254192c…83b5），`touch` 刷新 mtime；还原后重跑新守门全绿。

## 全量测试（默认配置，`DOTNET_CLI_UI_LANGUAGE=en`）

- 重钉后最终一次：总 2198 / 通过 2191 / 跳过 7 / 失败 0，**退出码 0**（改前基准 2193 / 2186 / 7；新增 5 个用例）。
- 重钉前那一次是 2198 / 2186 / 7 / 失败 5，5 条全部是待重钉的专家黄金值：`一层配置的专家黄金值(1)`、`(3)`、`缺省预设的专家与一层配置逐步相同(1)`、`(3)`、`专家前瞻的记录Tests.一层配置的记录与改动前相同`。
- MUST 不变项全部绿：5 份 `decisions-link1-*` 基线、`LinkGoldenTurnHash`、严格提高哈希、`SmallMapGoldenHash`、`构筑期专家与高难逐步相同`、`前瞻宽度为1且关闭多样候选时与高难逐步相同`、`非专家没有前瞻记录`（哈希未动）。
- `dotnet build siege.sln -c Release`：0 警告 0 错误。

## 改前 / 改后逐行比对（重钉判据核对）

| 样本 | 决策序列 / ai-P0 消费 | 前瞻记录 | 首个分歧 | 判据 |
|---|---|---|---|---|
| G1 种子 1 | 不变（24 / 8556） | 12 条中第 10、12 条不同 | 第 10 条（已前瞻，下一名对手 R=P1@11）：只有 `sim=` 251 → 299；第 12 条 208 → 256；**全部候选的模拟回应键不变** | **不满足** |
| G1 种子 2 | 不变 | 逐字相同 | 无分歧 | 黄金值不变 |
| G1 种子 3 | 第 24 条决策变：`…J5:Basic,E12:Line` → `…J5:Basic,F15:Line` | 第 7、12 条不同 | 第 7 条（小回合 28，第 7 大回合，已前瞻）：候选 2 `[G9:Fortress,N11:Artisan+F:O10-O11,E14:Fortress]` 回应键 `P3,M4,N4,U6` → `P3,M4,N4,U6,D12`（一层分数 107 → −83），`sim` 357 → 358；第 12 条候选 2 回应键加 `H9`、选择 #2 → #0 | 满足 |
| 种子 6 整局 | 落点 / 提子不变（小回合 92、记录 23 不变） | 23 条中 4 条不同 | 小回合 32 的记录：只有 `SimulatedRehearsals` 392 → 440；其后小回合 36 / 48 / 52 同样只差计数（328 → 376、90 → 94、174 → 180）；DeterministicText 3391 行只差 5 行（首部版本号 + 这 4 个计数）；**无一回应键变化** | **不满足** |

构筑期记录（各样本前 3 条 / 前 3 个 P0 小回合）改前改后逐字相同。

成因：模拟对手的贪心组批在前 12 个单点里没能填满部署上限时，N 24 会把第 13–24 名逐个预演再拒掉——预演次数增加、结果批次不变。design 中"模拟对手次数逐一相同"是在 4 名标准 AI 的 48 个探针局面上测得的，不覆盖专家对局。

已重钉的新黄金值（取自改后代码全量测试的实际输出）：G1 种子 1 RecordsHash `5F47E90672832C44688DE642083B42DFBABE496FE7B31CBC167217E167B1F083`（决策哈希不变）；种子 3 DecisionsHash `AFFC6C75B99086706B45E7B0435C069B8C8BE73A52117C212930F149E3B58FF2`、RecordsHash `C472B8516CC677B953B675B19CAD318978C38FF757E698513D4652D7BEAB371E`（条数 / 消费仍 24 / 8556 / 12）；种子 6 确定性文本哈希 `4668AB92F8178A93817E7F368851526C0C5F1231E633E6BBF48D3A46E8B603EB`（旧 `66501E9C…84A3`）。
重钉后另加：`专家难度的一层前瞻Tests.G1Golden` 种子 1 / 3、`专家前瞻的记录Tests.一层配置的记录与改动前相同` 的哈希（含旧值注释）；`rebaseline.txt` 已写。

## 门控重测（Release）

- 预演次数代理（`预演次数代理计入新增部分`，48 局面）：高难 36236、专家 106808，**比值 2.948**；多样补充 287（0.3%）、模拟对手 15575（14.6%）、两层扫描 54571（51.1%）——与改前逐项相同。
- 计时（`SIEGE_PERF=1`，`专家前瞻耗时计时`）：高难中位 107.2 ms、专家中位 384.3 ms，**比值 3.583**（改前 3.639；上限 4）。

## 先红例外（主会话已接受）

1. 「模拟对手随专家显式配置」：Scenario 原值 N 12 与标准预设的 N 相同，改前不红；补 N 16 一例先红；该测试的主要守门对象是"两层平局带宽必须清零"（变异 M2 红 2）。
2. 「被模拟玩家的难度不影响模拟」：改前不红，属结构性守门——前瞻拿不到任何对手控制者，在信息边界内写不出使其变红的变异，由信息边界守门（类型闭包 + 源码扫描）兜底。

## 待决项

无。
