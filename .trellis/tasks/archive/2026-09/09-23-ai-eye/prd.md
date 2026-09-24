# 09-23-ai-eye

> 规格权威：`openspec/changes/ai-eye/`（design.md 含裁决记录 R1–R6）。本文件只承载目标、分段计划与范围边界。

## Goal

让 AI 在活形规则下会做活、不拆己方活形、不攻打活棋、无利可图时停手，并在新规则下重新校准：

- 评价七维 → 九维（`Eye` 眼位、`Threat` 威胁），全部取增量；眼信息只经 `LifeShapeReport` 查询；
- `GroupSafety` 活形中性，删自带眼点循环；
- 活形硬约束为候选过滤（预演合法之后、打分之前），不进入 `LastPointRanking`；
- 停手阈值 `PassThreshold`（写入日志首部、严格 CLI）；
- 性能：预筛只算七维、单次决策内按盘面指纹缓存活形分析；
- 按 design D6 顺序扫档校准，定值落地。

## 分段执行计划（顺序、每段一个 implement；段末主会话前台复核并提交）

| 段 | tasks.md 组 | 内容 | 段末状态 |
|---|---|---|---|
| A | 0 + 1 | 前置核对；九维结构、眼位、威胁、活形中性、EnemyLoss 排除活形 | `dotnet build` 零警告、`dotnet test -c Release` 全绿 |
| B | 2 | 活形硬约束、停手阈值、日志 / CLI、难度 | 同上 |
| C | 3 | 预筛七维、决策内缓存、耗时对比 | 同上 + 耗时上界 |
| D | 4 | 校准（**开工前须负责人裁决 life-shape R8**） | 同上 + 报告 |
| E | 5 | 设计文档、spec 约定、自动实机检查、全量回归 | 可归档 |

## Acceptance Criteria

- [ ] `openspec/changes/ai-eye/specs/` 下全部 Requirement 与 Scenario 有对应测试（测试类名 = Requirement 名，方法名 = Scenario 名）
- [ ] 每个新守门测试做过变异验证并记录（还原逐字节校验 + 刷新 mtime）
- [ ] AI 的眼信息只经活形查询获取；`Siege.Core/Ai` 下无自带眼判定
- [ ] 确定性：新两维权重为 0 时决策序列与改动前逐步相同；缓存开关不改变决策序列
- [ ] 全量测试全绿、零警告；不得改期望凑绿
- [ ] 每段 `implement.md` 追加记录：改了什么、既有测试改写 / 删除逐条、变异逐条、性能 / 校准数字、待决
- [ ] `openspec validate ai-eye --strict` 通过

## Out of Scope

规则本身的修改（含 life-shape R8，如需修改另开 change）；对数刻度（未经用户确认不得启用）；人 vs AI 实机局（负责人下）。

## 子 agent 约束

- 不得 `git commit`；不得读取大文件 / 媒体并回传。
- 理解代码优先用 codegraph（`codegraph_explore` / `codegraph_callers` / `codegraph_impact`，`projectPath = E:\wws\geme_demo`），不用 grep/read 遍历大文件；只回传精炼结论。
- Windows 环境：用 `python` 不用 `python3`。
- 同一时间只跑一个 dotnet 任务；长跑（200 局）前台顺序执行。
- 不碰工作树里非本任务的 Godot 改动（见 design R6）。
