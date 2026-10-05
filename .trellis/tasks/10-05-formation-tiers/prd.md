# 10-05-formation-tiers

> 规格权威：`openspec/changes/formation-tiers/`（proposal / design / specs / tasks）。

数值规则第二步（负责人 2026-10-05：先呈现、后数值）。棋串规模阶梯"阵型"：棋子数达到 3 / 5 / 8 / 12 枚为一至四阶，阶数加进倍率指数（每级 ×1.5，与倍增子连乘，四阶封顶）。作为计分规则版本 v2 引入：新局缺省 v2，旧存档与旧日志按 v1，批量跑局可配置。

验收：`tasks.md` 各条。约束：计分全程任意精度整数、不经浮点；公开视图不含私有字段；同一时间只跑一个 dotnet / Godot；每个配置跑局不超过 20 局；既有回归与黄金值测试显式钉 v1、不重录；本机 `dotnet test` 原有 11 条日志黄金值为红，判据是失败集合与改动前逐条相同。

分段：A = tasks 第 1 组（Core / Sim）；B = 第 2 组（主会话跑局与裁决）；C = 第 3 组（Presentation / Godot）；D = 第 4 组（主会话）。

## 实现记录

### 段 A（2026-10-05，Core / Sim）

- 新增 `Scoring/ScoringVersion.cs`（`ScoringVersion` V1 / V2、`ScoringVersions`、`FormationTiers.Thresholds = [3, 5, 8, 12]` 全仓唯一门槛表）；`GroupPower` 新增 `FormationTier`、`MultiplierExponent`，`Multiplier` 改为总倍率，`MultiplierCount` 保持倍增子枚数。
- 对局配置 / 存档 / 公开视图（`MatchPublicView.ScoringVersion`）/ 日志首部 / 批次配置带版本，缺字段按 v1；`run --scoring v1|v2` 严格解析。所有产品代码的计分调用传对局版本；不带版本的入口语义固定为 v1，源码扫描（含 `src/godot`、别名与 `using static`）+ IL 扫描两条守门。
- 日志：`FormationTier` 只在非 v1 局写出，v1 日志除首部一项外逐字节不变。分析端倍率还原走 `GroupEntry.Multiplier`；阵型放大部分不归任何棋子类型（design A8）。
- 既有回归 / 黄金值测试经夹具显式钉 v1，期望值未改；v1 证据：`V4GoldenTurnHash` 保持绿、旧专家日志回放一致、HEAD 与 `--scoring v1` 跑 v5 种子 1–6 日志逐行相同、11 条本机红测的实际哈希与改动前相同。
- 测试 2178 条 = 2158 通过 / 11 失败 / 9 跳过，失败集合与基线逐条相同。变异：实现 34 + 检查 7 + 主会话 1（二阶门槛 5 → 6，红 9）全部变红。
- 检查补上：源码扫描判"带版本"的规则过宽（`ScoringRelicCounts` 会被误认），已收紧；预演 / 顺序预测 / AI 评价的"开始前势力"补了 v1 / v2 结果必然不同的行为测试。`ExpertLookahead.PredictNextOrder` 只有两条扫描守门。
- 过渡期：段 C 之前，v2 局的短算式与揭示仍按倍增子判断是否写倍率（3 枚普通子显示"3"、实际 4），热区仍按倍增子数。
