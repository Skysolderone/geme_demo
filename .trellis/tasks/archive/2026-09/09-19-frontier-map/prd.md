# 09-19-frontier-map

> 规格权威：`openspec/changes/frontier-map/`。本文件只承载目标、分段计划与范围边界。

## Goal

多平台大地图的**可玩验证版**：地图加规格档（标准 / 边疆），新增 6 平台约 360 格的 `siege-frontier-v1`，图形版加推屏相机（贴边 / WASD / 滚轮 / 夹取 / 空格回家）。对局规则零改动；标准档与 v4 行为零变化。要量出三个数：边疆图平均大回合数、AI 单步耗时、各平台被选率与胜率。

## 分段执行计划（顺序、单 agent；每段独立成绿，段末主会话检查）

| 段 | tasks.md 组 | 内容 | 段末状态 |
|---|---|---|---|
| A | 1、2 | 规格档字段与校验分流；两位数行号贯通；统一选图入口与 `--map`；AI 种子选区；区号支持到 8 | 全量测试绿、零警告；v4 同种子对局逐步不变 |
| B | 3 | `siege-frontier-v1` 生成器与 JSON、资源布点、容量、20 局跑局量数、终端试玩 | 边疆图通过校验；跑局统计入 `implement.md` |
| C | 4、5 | Presentation 相机视图模型；Godot 接入、多位姿 `--pick-check`、悬停坐标、6 平台着色、性能 | 三条 Godot 自检命令退出码 0；v4 截图基线不变 |
| D | 6 | 人工整局试玩结论、`.trellis/spec/core` 补充、HANDOFF | 归档 |

## Acceptance Criteria

- [ ] **map-definition** 增量全部 Requirement 与 Scenario（规格档、边疆预算、校验分流、边疆基准图）
- [ ] **match-setup**「原型插旗替代路径」全部 Scenario（含 v4 不扰动回归）
- [ ] **simulation-harness**「各入口按地图标识选图」全部 Scenario
- [ ] **viewport-camera** 全部 Requirement 与 Scenario
- [ ] 守门：校验器对规格档的分支只出现在一张声明表里（含变异记录）
- [ ] 既有 897 项测试全绿、零警告；不得改期望凑绿
- [ ] 每段 `implement.md` 追加记录：改了什么、既有测试改写逐条、变异逐条、待决
- [ ] `openspec validate frontier-map --strict` 通过

## Out of Scope

小地图、中键拖拽、相机跟随对手；节奏参数调整；最终规模（8 平台 / 15×15）与突破 25 列；替换 v4；2 / 3 人边疆图；AI 大地图专门策略（仅允许裁决 12 的"候选落点数量上限"旋钮）；设计文档升版。
