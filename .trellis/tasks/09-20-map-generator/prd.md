# 09-20-map-generator

> 规格权威：`openspec/changes/map-generator/`。本文件只承载目标、分段计划与范围边界。

## Goal

边疆档地图由**地图种子**确定性生成（平台数 5–8、边长 5–9、25×30），生成结果必须通过现有边疆档静态校验；地图种子与对局种子互相独立；三个入口用 `gen[:<种子>[:p<N>]]` 选图；日志首部带地图内容摘要、回放按标识重建；批量支持每局换图并按平台边长统计；图形版加开局选图界面（v4 / 手工边疆图 / 随机图，背景全局预览）。对局规则零改动；v4 与 `siege-frontier-v1` 行为零变化。

## 分段执行计划（顺序、单 agent；每段独立成绿，段末主会话检查）

| 段 | tasks.md 组 | 内容 | 段末状态 |
|---|---|---|---|
| A | 1 | 地图随机源、参数与标识、生成六步、校验闭环、确定性、50 种子布局速览 | 全量测试绿、零警告；种子 1–50 × 平台数 5–8 全过校验 |
| B | 2 | `MapCatalog` 接 `gen:`、三入口选项、公开视图 / 存档 / 日志首部、内容摘要与回放、批量每局换图、`map` 子命令 | 全量测试绿；20 局每局换图跑通 |
| C | 3 | 选图视图模型、`GameRoot` 选图阶段、HUD 选图面板、删编辑器运行参数、生成图上的拾取自检 | Godot 自检命令退出码 0；v4 截图基线不变 |
| D | 4 | `.trellis/spec/core` 与 HANDOFF | 归档 |

## Acceptance Criteria

- [ ] **map-generation** 全部 Requirement 与 Scenario
- [ ] **map-selection** 全部 Requirement 与 Scenario
- [ ] **map-definition** / **simulation-harness** / **match-setup** 增量全部 Scenario
- [ ] 守门：生成器不见 `GameSeed`、对局流程不见地图种子；选图界面不自带地图清单、不直接调生成器（含变异记录）
- [ ] 既有 1085 项测试全绿、零警告；不得改期望凑绿
- [ ] 每段 `implement.md` 追加记录：改了什么、既有测试改写逐条、变异逐条、待决
- [ ] `openspec validate map-generator --strict` 通过

## Out of Scope

平台边长到 15 / 突破 25 列；2 / 3 人与标准档生成；对称或按平衡指标筛图；地图编辑器；节奏参数与 AI 调整。
