# 《围杀 Siege》路线图

权威设计来源：`2026-09-10-siege-core-gameplay-design-v1.md`（v1.30）。现行规格在 `openspec/specs/`，已完成的 change 在 `openspec/changes/archive/`，现状与接手说明见根目录 `HANDOFF.md`。

## 已完成

- **首轮原型（设计文档 §18.1）**：`add-board-core`、`add-batch-deployment`、`add-territory-power`、`add-relic-system`、`add-recruit-hand`、`add-match-flow`、`add-heuristic-ai`、`add-tactical-ui` 共 8 项，均已归档。
- **规则回归与活形**：`restore-go-core-rules`、`life-shape`、`life-single-stone`、`superko-occupancy`、`ai-eye`。
- **地图与地表**：`terrain-surfaces`（四种新地表）、`small-maps`（2 人 / 3 人图，已被棋盘图取代并删除）；母任务 `board-terrain`：`board-map` / `board-isolated-gen`（互不连通的棋盘、2 / 3 / 4 人）、`builtin-board-maps`（三张内置棋盘图成为缺省）、`retire-legacy-maps`（删除全部旧地图与 `gen:`，旧标识明确报错；分支 `feat/board-terrain`，未合入）。
- **后续扩展（§19）**：
  - 第 2 项 2 人 / 3 人专属地图 —— `small-maps`，现由棋盘档生成器按人数生成（`board-isolated-gen`、`builtin-board-maps`）；
  - 第 3 项更多棋子与信物 —— `more-pieces-relics`（十种棋子、十类信物、内容集 v1 / v2）；
  - 第 4 项公平信息下的正式 AI —— `expert-lookahead`（专家难度一层前瞻）；
  - 第 6 项带入带出 —— `carry-in-out`。
- **可玩性修正与校准**：`pass-threshold-first-stone`、`flag-contest`、`v2-recalibration`（停手阈值 20、2 人图 AI 权重覆盖）。

时间线与提交号见 `HANDOFF.md`「已完成的 change」。

## 未完成

| 优先级 | 事项 | 说明 |
|---:|---|---|
| 1 | 棋盘图上的 AI 校准 | 九维权重与停手阈值都在已删除的 v5 上校准，棋盘图上未校准（冒烟全部整轮 Pass 终局、52–74 大回合）；专家耗时比棋盘图实测 6.16 超出原规格 4 倍；内置棋盘图是否登记地图专属权重一并定。原"2 人图交战不足"随 2 人图删除失效 |
| 2 | 地形规则去留 | 棋盘图上不产生高度、深水、桥、林地、土路与四种新地表，规则保留待用；删除，还是给棋盘图加地形，另开 change（负责人 2026-10-06 裁决） |
| 3 | 工坊信物在棋盘图上无效果 | 只扩搭桥 / 烧林的格目标，棋盘图上恒为空却仍会生成；随地形去留或内容校准处理（移出权重表要新开内容集版本） |
| 4 | 专家难度强度 | `expert-strength`（v1.18）加多样候选与近似两层（S = 8、λ = 1000‰）后前瞻改变 41% 的选择，但 v5 成对种子 1–60 扩样中专家弱于高难：配对 好 7 / 同 33 / 差 20，胜 6/60 对 12/60，平均名次 2.87 对 2.53（配对差区间不含 0，符号检验 p = 0.019）。负责人裁决：专家预设退回一层（S = 0、λ = 0，与改动前逐步相同），两项保留为可配置项（`Search` 显式给出时生效）；下一步需先归因（专家落子更少、Pass 更多），另开 change |
| 5 | 简单难度多人近循环 | V2 下 0/20 截断（v5 数据），机制仍在；不改规则 |
| 6 | 联网（§19 第 5 项） | 同步、断线、匹配、15 秒匿名同时插旗；需先定架构 |
| 7 | 正式美术与音效 | 当前为程序化低多边形部件，无音效 |

任何扩展都不得改变基础四邻接、气、批次围杀、占据优先和唯一覆盖规则（设计文档 §19）。
