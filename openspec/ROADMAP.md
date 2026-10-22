# 《围杀 Siege》路线图

权威设计来源：`2026-09-10-siege-core-gameplay-design-v1.md`（v1.39）。现行规格在 `openspec/specs/`，已完成的 change 在 `openspec/changes/archive/`，现状与接手说明见根目录 `HANDOFF.md`。

现在整个游戏只有一个模式：4 人开放地图（链接模式，`link:<地图种子>`，5 块出生棋盘建局即紧贴成一张连通地图，前 3 个大回合只在自己棋盘落子，第 4 大回合起全图混战）。下表列到设计文档 v1.30；被后续 change 删除或取代的项在括号里注明。

## 已完成

- **首轮原型（设计文档 §18.1）**：`add-board-core`、`add-batch-deployment`、`add-territory-power`、`add-relic-system`、`add-recruit-hand`、`add-match-flow`、`add-heuristic-ai`、`add-tactical-ui` 共 8 项。
- **规则与活形**：
  - 早期数值与终局修正：`cap-multiplier` / `round-cap`（v1.1）、`dominance-victory`、`growth-pass-1`、`multiplier-rebalance`、`catch-up-recruit`、`scoring-sites`（v1.3）——倍率封顶、大回合上限、势力碾压、落后者征募补偿、据点等后被 `restore-go-core-rules` 回退（`growth-pass-1` 的分阶段部署上限、`scoring-sites` 的高地压制加值保留）；
  - 回归围棋内核：`restore-go-core-rules`（v1.5）、`life-shape`（v1.6）、`life-single-stone`（v1.7）、`superko-occupancy`（v1.10）；
  - 更多棋子与信物（§19 第 2 项）：`more-pieces-relics`（v1.14，十种棋子、十类信物、内容集 v1 / v2）；
  - 带入带出（§19 第 5 项首期）：`carry-in-out`（v1.15）；
  - 只留一套链接规则：`retire-legacy-rules`（v1.27）；
  - 大回合上限安全网：`cycle-stop`（v1.29，缺省 100，与 AI 循环提子停手一起处理 R-3）。
- **地图与模式**：
  - 地形与改造：`terrain-model`（v1.2）、`artisan-terrain-edit`（v1.4）、`terrain-surfaces`（v1.8）；
  - 旧地图族：`denser-map`、`frontier-map`、`map-generator`、`small-maps`（v1.13）、`board-map`（v1.19，棋盘档生成图）——旧基准图、边疆档、生成图、棋盘档与 2 / 3 人图后被 `board-link-mode` / `retire-legacy-maps` 删除；
  - 插旗：`flag-contest`（v1.12）；
  - 链接模式：`board-link-mode`（v1.20）、`retire-legacy-maps`（v1.21）；
  - 主题化出生棋盘：`link-map-terrain`（v1.23，五主题、外圈平坦、3×3 核心区）；
  - 决赛倒计时：`final-countdown`（v1.25，随拼接规则被 `open-map` 删除）；
  - 开放地图：`open-map`（v1.28，取消区域隔绝与配对 / 链接，建局即紧贴成一张连通地图）；
  - 新地表投放：`link-map-surfaces`（v1.30，中间带按主题放荒漠 / 沼泽 / 岩台 / 浅滩）。
- **AI**：
  - 启发式 AI 校准：`ai-safety-weight`、`ai-eye`（v1.9）、`pass-threshold-first-stone`（v1.11）、`v2-recalibration`（v1.17，停手阈值 20）；
  - 专家难度（§19 第 3 项首期）：`expert-lookahead`（v1.16，一层前瞻）、`expert-strength`（v1.18，多样候选与近似两层保留为可配置项，预设退回一层）、`expert-tiebreak`（v1.35，构筑期不前瞻、两层加分改为容差带平局裁决，扫档未优于困难、预设不改值）、`expert-opponent-model`（v1.37，模拟对手改用专家本人的困难口径，20 局走法与改前相同）；
  - 停手复核与决胜一手：`ai-pass-review`（v1.22）；
  - 地形与主题诊断：`ai-terrain-review`（v1.24）；
  - 按座位视野：`ai-seat-vision`（v1.26）；
  - 循环提子停手：`cycle-stop`（v1.29，停滞大回合数 K = 6）；
  - 决策提速：`ai-turn-speed`（只改性能，决策逐步不变）；
  - 整批候选：`ai-whole-batch`（v1.38，补"逐枚为负、整批为正"的组合，`ai-pass-review` P14）；
  - 搭桥候选：`ai-bridge-candidate`（v1.39，"路线价值"诊断后改为候选生成修法，§19 第 22 条）。
- **表现与工具**：战术信息层合并 `merge-board-layer`、棋盘坐标标注 `board-coordinates`、禁手事先标示 `forbidden-marks`（v1.19）、`recruit-panel-collapse`、`turn-settlement-show`、`settlement-show-callouts`、`board-render-perf`、`show-sound-cues`（程序合成占位音）；跑局命令行严格校验 `strict-cli`。

时间线与提交号见 `HANDOFF.md` 与设计文档文末变更记录。

## 未完成

| 优先级 | 事项 | 说明 |
|---:|---|---|
| 1 | 试玩待定项 | 负责人试玩后再定，只登记不调参：**对局长度**（开放地图下标准 × 4 中位 24–25 大回合，是否缩棋盘边长 / 构筑期）、**主题强弱**（两次 20 局冒烟最强 / 最弱主题互相矛盾，在噪声内）、**栅栏造眼是否太便宜**（`ai-terrain-review` T13，规则层观察） |
| 2 | 专家强度后续方向 | 归因（`10-21-expert-diagnosis`）、修法 A（`expert-tiebreak`，v1.35）、修法 B（`expert-opponent-model`，v1.37，模拟对手改用专家本人困难口径）均已完成：开放地图上预设专家对困难配对 2 / 13 / 5（不显著），修法 B 未改变实际走法；剩余失配来自困难的扰动择优（信息边界内不可建模）。方向待负责人另定 |
| 3 | 画面精修重规划 | `map-elements-v2`（地图元素造型精修，0/17，未开工）按开放地图与新地表重新规划后再实施 |
| 4 | ~~AI 整批评价~~ | 已完成（`ai-whole-batch`，v1.38）：剪枝候选 + 配对候选，开关缺省开 |
| 5 | 联网（§19 第 4 项） | 同步、断线、匹配、匿名同时插旗；需先定架构 |
| 6 | 正式美术与音效 | 当前为程序化低多边形部件与程序化提示音 |

任何扩展都不得改变基础四邻接、气、批次围杀、占据优先和唯一覆盖规则（设计文档 §19）。
