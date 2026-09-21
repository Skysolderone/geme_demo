# 09-21-restore-go-core-rules

> 规格权威：`openspec/changes/restore-go-core-rules/`（proposal 含 2026-09-21 裁决表）。本文件只承载目标、分段计划与范围边界。

## Goal

以 `siege-rules-and-ai.md` 为基准，把计分、出局、终局、征募四块规则回归文档，并整体移除据点：

- 棋串军势 `⌊(基础 + 位置加值) × 1.5^n⌋`，不封顶，精确整数、不溢出；
- 总势力 = 独占空格数 + 棋串军势；空林地格恒为中立、不计分；
- 曾建立正势力后势力降 0 立即出局，保护期不豁免；
- 终局只剩三类（只剩一名 / 棋盘填满 / 整轮 Pass），删大回合上限与势力碾压；
- 删落后者征募补偿；排名只影响展示与行动顺序；
- 据点从规范、地图数据、校验、生成器、计分、预演、AI、表现层完整摘除；内置图改名 `siege-4p-base-v5` / `siege-frontier-v2`；
- `Siege.Sim` 保留技术性小回合数截断（默认 600，记 `turn_limit`、无名次）。

保持不变：匠人、地形、气边、高地压制加值、分阶段基础部署上限、地图规格档与生成器的布局逻辑（只摘据点）。

## 分段执行计划（顺序、每段一个 implement + 一个 check；每段独立成绿，段末主会话检查并提交）

| 段 | tasks.md 组 | 内容 | 段末状态 |
|---|---|---|---|
| A | 1 | 军势公式、领地计分、势力明细；数值类型改为不溢出的整数 | `dotnet build` 零警告、`dotnet test -c Release` 全绿 |
| B | 2 | 据点摘除：Board / 地图文件 / 校验 / 内置图改名 / 生成器 / 结算与预演 / AI 最小改动 | 同上；`grep -rE "Site|据点" src tests` 无据点残留 |
| C | 3 | 出局标记与检查、三类终局、名次并列链、`MatchOptions` 与存档摘三项 | 同上 |
| D | 4 | 效果快照不读名次、来源拆分、删落后补偿 | 同上 |
| E | 5 | Sim 配置 / 截断 / 日志 / 分析、终端版、Presentation、Godot | 同上 + Godot 自检命令退出码 0 |
| F | 6 | 设计文档同步、200 局新基线、`.trellis/spec/core` 约定、全量回归 | 可归档 |

## Acceptance Criteria

- [ ] `openspec/changes/restore-go-core-rules/specs/` 下 25 个能力增量的全部 Requirement 与 Scenario 有对应测试（测试类名 = Requirement 名，方法名 = Scenario 名）
- [ ] 每个新守门测试做过变异验证并记录
- [ ] 计分路径无 `double` / `float` / `decimal`；势力 / 军势无定宽整数溢出
- [ ] 全量测试全绿、零警告；不得改期望凑绿——既有算例按新公式**重算**并在 implement 记录里逐条写明
- [ ] 每段 `implement.md` 追加记录：改了什么、既有测试改写 / 删除逐条、变异逐条、待决
- [ ] `openspec validate restore-go-core-rules --strict` 通过

## Out of Scope

活形判定与活棋禁入（→ `life-shape`）；AI 眼位 / 停手阈值 / 权重校准（→ `ai-eye`）；调地图规模与信物格数；旧存档 / 旧地图 JSON / 旧 `gen:` 标识的迁移。

## 子 agent 约束

- 不得 `git commit`；不得读取大文件 / 媒体并回传。
- 理解代码优先用 codegraph（`codegraph_explore` / `codegraph_callers` / `codegraph_impact`，`projectPath = E:\wws\geme_demo`），不用 grep/read 遍历大文件；只回传精炼结论。
- Windows 环境：用 `python` 不用 `python3`。
