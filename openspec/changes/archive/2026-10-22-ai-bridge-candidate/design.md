## Context

诊断见 proposal 与 `.trellis/tasks/10-22-route-value-diagnosis/research/report.md`。现行 `RankPoints` → `ScorePoints`（候选格上限 K 时先 `PrefilterCells` 收窄，再对每格 × 持有类型 × `EditOptions` 预演打分）→ 取前 N；`ChooseByCandidates` 做 M 次贪心 + `AppendWholeBatch`（整批候选，开关 `WholeBatch`）。搭桥单点名次中位 30，前 12 名只占 41 / 154。

## Goals / Non-Goals

**Goals**：让收益已过阈值的搭桥单点能成为候选起点；零随机；开关 0 逐步不变；开销小（每次决策多枚举"合法空格 × 搭桥目标"中预筛外的部分，诊断每局面合法搭桥选项约 7 个）。

**Non-Goals**：评价维度；立栅 / 烧林；寻路。

## Decisions

### D1 机制（裁决 1、2）

`AppendBridgeSeed`（名称实施时定）追加在 `AppendWholeBatch` 之后：
- 手中无匠人、部署上限 0、或合法范围内没有任何搭桥目标时不生成。
- 枚举：合法落子范围全部空格（不经 `PrefilterCells`）× 匠人 × `EditOptions` 中的搭桥目标；预演 + 活形硬约束与 `ScorePoints` 同一路径（已在 `ScorePoints` 里评过的同一单点可复用其分数，不重复预演）。
- 起点：单点总分 − Pass 总分 > 实际生效阈值者中总分最高（并列按 `Rank` 的确定性次序）。
- 续组：复摆起点后 `ContinueGreedy(ranking 前 N, …, 同阈值)`。
- 去重后加入候选集；选优照旧（专家：基础候选含它，前瞻集第一个仍是高难选择）。

### D2 适用难度（裁决 3）

标准、高难、专家；`ImmediateOnly`（简单）不生成；`ExpertLookahead.SimulatedConfig` 清零开关。

### D3 开关与记录（裁决 4）

`AiSearchConfig.EditSeed`（int 0 / 1，0 不写出，`Validated` 拒绝其他值）；`RunConfig.EditSeed` / `--edit-seed`（缺省 1），与 `WholeBatch` 同一派生、同一"0 不写出"影子序列化；首部缺字段按 0 回放。HANDOFF 命令行一节同 `--whole-batch` 写复跑提示。

### D4 版本、守门与重钉（裁决 5、6）

AI 评价版本 10 → 11。开关 0 时全部 5 份基线（含 `*-wb0.txt` 那组口径）与各黄金值逐步不变；简单基线开关 1 也不变；其余按实测重钉，首个分歧处被选中的须是搭桥候选，记 `rebaseline.txt`。专家门控两条 ≤ 4。

### D5 验证与采纳（裁决 7、8）

同一构建 on / off，4 标准，`--map link:1 --map-per-match --seed 1 --count 20`（E0 = `--edit-seed 0`，E1 = 缺省 1；两批 `--whole-batch` 都取缺省 1）。指标：搭桥次数、搭桥致提子（日志改造事件）、大回合中位、主题平均名次、单小回合耗时中位、截断、上限结算、搭桥候选被选次数（临时探针）。

预登记采纳：E1 截断 0、上限结算 0；大回合中位 E1 − E0 ≤ 2；耗时中位比 ≤ 1.15；搭桥次数 E1 > E0 → 采纳提交；否则停下回报、不提交。

### D6 分段与派发（裁决 9）

一段：1 implement → 主会话前台复核 → 1 check → 主会话跑 40 局 → 文档 v1.39 → 归档（含诊断任务）→ 提交。

## Risks / Trade-offs

- [枚举开销] → 只枚举搭桥目标（每局面约 7 个选项）；准则卡耗时 1.15。
- [搭桥多了但无名次收益] → 只看准则；名次只记录。

## Migration Plan

旧日志缺开关按 0 重建；版本 10 日志按既有规则报版本不符。

## Open Questions

（无）

## 裁决记录（已确认）

负责人 2026-10-22 裁决 1–9 全按推荐，派发"一段串行"：1A 搭桥候选保留位（不加评价维度）；2 只限搭桥；3 标准 / 高难 / 专家，简单与模拟对手不生成；4 `--edit-seed` 缺省 1、0 不写出；5 版本 10 → 11，不改权重；6 开关 0 守门、开关 1 按实测重钉（首个分歧须为搭桥候选）；7 on / off 各 20 局 4 标准 + 专家门控；8 预登记准则（截断 0、上限 0、中位 +≤ 2、耗时 ≤ 1.15、搭桥次数 on > off）；9 设计文档 v1.39、§19 第 22 条、HANDOFF 第 10e 项。
