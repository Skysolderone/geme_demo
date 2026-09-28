# 《围杀 Siege》路线图

权威设计来源：`2026-09-10-siege-core-gameplay-design-v1.md`（v1.18）。现行规格在 `openspec/specs/`，已完成的 change 在 `openspec/changes/archive/`，现状与接手说明见根目录 `HANDOFF.md`。

## 已完成

- **首轮原型（设计文档 §18.1）**：`add-board-core`、`add-batch-deployment`、`add-territory-power`、`add-relic-system`、`add-recruit-hand`、`add-match-flow`、`add-heuristic-ai`、`add-tactical-ui` 共 8 项，均已归档。
- **规则回归与活形**：`restore-go-core-rules`、`life-shape`、`life-single-stone`、`superko-occupancy`、`ai-eye`。
- **地图与地表**：`terrain-surfaces`（四种新地表）、`small-maps`（2 人 / 3 人图）。
- **后续扩展（§19）**：
  - 第 2 项 2 人 / 3 人专属地图 —— `small-maps`；
  - 第 3 项更多棋子与信物 —— `more-pieces-relics`（十种棋子、十类信物、内容集 v1 / v2）；
  - 第 4 项公平信息下的正式 AI —— `expert-lookahead`（专家难度一层前瞻）；
  - 第 6 项带入带出 —— `carry-in-out`。
- **可玩性修正与校准**：`pass-threshold-first-stone`、`flag-contest`、`v2-recalibration`（停手阈值 20、2 人图 AI 权重覆盖）。

时间线与提交号见 `HANDOFF.md`「已完成的 change」。

## 未完成

| 优先级 | 事项 | 说明 |
|---:|---|---|
| 1 | 2 人图交战不足 | 权重覆盖后仍 13/20 整局无提子；诊断主因是地图结构，候选为 `siege-2p-base-v2` |
| 2 | V2 内容集下九维权重完整重扫；边疆图 / 生成图复核 | 目前只重扫了停手阈值，且只在 v5 上 |
| 3 | 专家难度强度 | `expert-strength`（v1.18）加多样候选与近似两层（λ = 1000‰）后前瞻改变 41% 的选择，但 v5 成对种子 1–60 扩样中专家弱于高难：配对 好 7 / 同 33 / 差 20，胜 6/60 对 12/60，平均名次 2.87 对 2.53（配对差区间不含 0，符号检验 p = 0.019）；下一步需先归因（专家落子更少、Pass 更多） |
| 4 | 简单难度多人近循环 | V2 下 0/20 截断，机制仍在；不改规则 |
| 5 | 联网（§19 第 5 项） | 同步、断线、匹配、15 秒匿名同时插旗；需先定架构 |
| 6 | 正式美术与音效 | 当前为程序化低多边形部件，无音效 |

任何扩展都不得改变基础四邻接、气、批次围杀、占据优先和唯一覆盖规则（设计文档 §19）。
