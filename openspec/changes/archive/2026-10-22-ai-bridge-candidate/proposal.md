## Why

设计文档 §19 第 22 条（`terrain-density` 裁决 K2）、HANDOFF「后续 change」第 10e 项：九维评价没有"路线价值"，搭桥只为眼位 / 安全。负责人裁决 A1–A5（2026-10-22）要求先诊断后定修法。

诊断（`.trellis/tasks/10-22-route-value-diagnosis/research/report.md`，新跑 20 局标准 × 4，逐小回合回放 20 / 20 一致，另 40 局旧日志只读）：
- 字面"路线价值"（落子 / 改造后沿气边可达的空格与信物增量）对一座桥中位只 +1（就是桥格），信物项 93% 为 0；隔断线远侧公共信物归锁定者 9 / 11（旧 20 / 24），控制时长 84%，没有被冷落；搭桥致提子 0 / 25（旧 0 / 62）——"路线价值"维度不值得做。
- 但未搭桥的 194 个匠人局面中 **67 个（35%）**，按**现行评价器自己的打分**，以最佳搭桥单点为起点的批次比 AI 实际批次高出 20 以上；这些搭桥单点收益都已大于停手阈值，没被选只因排在前 12 之外（42）或被候选格预筛挡掉（20）。缺口在候选生成。

负责人 2026-10-22 裁决 1–9 全按推荐（1A：改造候选保留位，偏离原 A3"只改评价"的改判）。

## What Changes

- **搭桥候选**（标准、高难、专家）：在既有候选（含整批候选）之后，至多再加 1 个不扰动候选——对全部合法空格（不经预筛、不受前 N 限制）枚举带搭桥的匠人单点，取收益严格大于停手阈值的最佳者为起点，按不扰动排序前 N 续贪心至部署上限；与既有候选一起去重选优。零随机；简单与前瞻模拟对手不生成；只限搭桥。
- **停手阈值**：Pass 条件加"搭桥候选也为空"；候选数上限说明 + 3。
- **开关**：跑局层 `--edit-seed 0/1`（缺省 1；0 与引入前逐步相同、不写出）。
- **AI 评价版本 10 → 11**；评价维度、权重、阈值、N、M 不改（不标"未校准"）。
- 设计文档 v1.38 → v1.39（§15.2、§19 第 22 条）；HANDOFF 第 10e 项、ROADMAP 同步。

## Capabilities

### New Capabilities

（无）

### Modified Capabilities

- `ai-decision`：ADDED「搭桥候选」；MODIFIED「停手阈值」（Pass 条件）、「高部署上限下的候选剪枝」（上限 + 3）。

## Impact

- **Siege.Core**：`HeuristicTurnController.ChooseByCandidates` 追加搭桥候选（复用 `ScorePoints` 的单点评价路径与 `ContinueGreedy`）；`AiSearchConfig` 新开关字段；`ExpertLookahead.SimulatedConfig` 清零；`EvaluationBreakdown.Version` 10 → 11。
- **Siege.Sim**：`RunConfig` / `--edit-seed` / `MatchSession` 派生 / 首部读写与回放（同 `--whole-batch`）。
- **测试**：新 Requirement 的 Scenario；开关 0 守门；按实测重钉。
- **Godot / 终端 / 规则 / 地图**：不改。

## Non-goals

- "路线价值"评价维度（诊断否决）。
- 立栅、烧林的保留位。
- 改权重 / 阈值 / N / M / 候选格上限。
