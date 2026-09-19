# 09-18-artisan-terrain-edit

> 规格权威：`openspec/changes/artisan-terrain-edit/`。本文件只承载目标、分段计划与范围边界。

## Goal

去围棋化第三轮：地形至今只读，一局里没有任何一手能改变通路、覆盖或高差。本轮加入第六种棋子「匠人」（军势 1、征募权重 10 可配置）与三种改造——搭桥 / 立栅 / 烧林：落子即改造、占 1 枚部署额度、目标为几何四邻、可选、不可逆、设施无归属、批次内不链式；改造在结算中先于提子生效，同形禁则纳入设施。

## 分段执行计划（每段独立成绿、段末本地 wip 提交不 push）

| 段 | tasks.md 组 | 内容 | 段末状态 |
|---|---|---|---|
| O | 0 | `strict-cli` | 已完成并归档（`2026-09-18-strict-cli`），测试 808 → 815 |
| A | 1 | 匠人棋子：类型、军势、征募权重、徽记绑定、类型槽；改写按五种写死的既有测试 | 全量测试绿；匠人可被征募、可落子，规则上等同普通子 |
| B | 2、3 | 地形写入口与改造合法性、批次条目带目标、预演与结算顺序、同形与存档、缓存排查；AI 枚举、跑局配置、日志、分析第 11 项 | 全量测试绿；20 局跑通并出改造分析 |
| C | 4 | Presentation 与 Godot：匠人轮廓、可改造目标高亮、落成反馈、截图 | 三条 Godot 命令退出码 0，截图交负责人 |
| D | 5 | 主会话扫档：匠人权重三档 → 推荐档下 Safety 复查 → 拍板 → 确认 200 局 | 对比表 |
| E | 6 | 设计文档 v1.4、`.trellis/spec/core`、变异汇总 | 归档 |

## Acceptance Criteria

- [ ] **terrain-edit** 全部 Requirement 与 Scenario
- [ ] **piece-effects** 六种棋子基础军势；**recruitment** 匠人在池中且权重可配置；**hand-management** 六种类型与 5 槽；**relic-generation** 徽记可绑定匠人
- [ ] **terrain** 格属性 / 边属性 / 气边 / 覆盖关系的改造后重算
- [ ] **batch-deployment** 改造不额外占额度；**capture-resolution** 预演与结算顺序、同形纳入设施
- [ ] **batch-preview** / **tactical-layers** / **information-visibility** / **visual-style-baseline** 改造相关场景
- [ ] **match-telemetry** 改造事件与第 11 项分析；**simulation-harness** 匠人权重写入配置
- [ ] 守门：地形写入口唯一、改造合法性唯一、`src/godot/` 不自算改造合法性
- [ ] 每段 `implement.md` 追加记录：改了什么、既有测试改写逐条、变异逐条、待决
- [ ] `openspec validate artisan-terrain-edit` 通过

## Out of Scope

逆向改造、改高度 / 据点 / 信物；地图调整（加林地、改河宽）；新 AI 维度；保护期与终局条件；第二轮遗留三项（打法保守、据点分占比 22.1%、篝火归属）只观察不调。
