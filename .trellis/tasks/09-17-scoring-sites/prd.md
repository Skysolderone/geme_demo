# 09-17-scoring-sites

> 规格权威：`openspec/changes/scoring-sites/`。本文件只承载目标、分段计划、验收映射与范围边界。
> 设计文档来源：`2026-09-10-siege-core-gameplay-design-v1.md` §3.3、§6.3、§7.2–7.4、§10.1、§12.3、§13.1、§14.2、§16、§17、§20

## Goal

去围棋化第二轮：空格领地退出计分，势力 = 据点分 + 军势；12 个固定据点（营帐 / 篝火 / 石碑 5 / 15 / 45 初值）布在 v3 地形上，地图升为 `siege-4p-base-v4`；落地高地压制加值（覆盖关系上有更低处敌子 → +1，进位置加值、不被倍率放大）；界面加据点地标；扫档由负责人拍板后落默认值。

## 分段执行计划（每段独立成绿、段末本地 wip 提交不 push）

| 段 | tasks.md 组 | 内容 | 段末状态 |
|---|---|---|---|
| A1 | 1 | 据点数据、MapFile、校验规则 8、C4 比对纳入据点、v4 布点与导出、默认地图切 v4 | 全量测试绿；文本图写入 implement.md，**负责人审布点后才派 A2** |
| A2 | 2、3 | 据点控制、据点分值配置、高地加值、势力公式与明细、弃赛、并列链与存档、改写既有测试；RunConfig、日志、分析 | 全量测试绿；跑 1 局核 config.json、20 局看报告 |
| B | 4 | Presentation 势力层 / 公开视图；Godot 地标、旗帜、守门 | 三条 Godot 命令退出码 0，截图 `art/sites-v4/` |
| C | 5 | 主会话扫档：分量 3 档 → Safety 9 档 → 负责人拍板 → 落默认值 + 确认 200 局 | 对比表 |
| D | 6 | 设计文档 v1.3、`.trellis/spec/core`、变异汇总 | 归档 |

## Acceptance Criteria

- [ ] **site-control** 全部场景
- [ ] **power-score** 棋串军势公式、总势力、势力明细
- [ ] **piece-effects** 高地压制加值（7）、倍增子倍率（含"倍率不作用于据点分"）
- [ ] **coverage-territory** 空格归属三态、唯一覆盖查询、弃赛遗留覆盖；领地分移除
- [ ] **map-definition** 静态数据、人数预算、4 人基准地图（含"据点布点""篝火可被邻家居高覆盖""保护期内篝火归邻家"）、静态校验
- [ ] **elimination-endgame** 主动弃赛、终局名次与并列判定
- [ ] **match-telemetry** / **simulation-harness** 据点字段、据点分析（含篝火主人控制占比）、config.json 写出分值
- [ ] **tactical-layers** / **information-visibility** / **visual-style-baseline** 据点相关场景；Godot 人工清单写入 `art/sites-v4/README.md`
- [ ] 守门：据点控制只读 `CoverageMap`；高地加值只走 `CoverageTargets`；`src/godot/` 不自算据点控制与高地加值
- [ ] 每段 `implement.md` 追加记录：改了什么、既有测试改写逐条（旧期望 → 新期望 → 依据）、变异验证逐条、待决
- [ ] `openspec validate scoring-sites` 通过

## Out of Scope

匠人与地形改造（第三轮）；据点专属效果、随机分值、迁移；保护期、部署上限、倍率封顶、落后者补偿、终局种类；Safety 以外的 AI 权重与新 AI 维度；strict-cli。
