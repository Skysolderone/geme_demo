# Journal - rubioc (Part 1)

> AI development session journal
> Started: 2026-09-12

---


## 2026-09-16 — denser-map 200-match regression

**Map v2 (`siege-4p-base-v2`, 85 playable / 36 obstacles) achieved its stated goals but exposed an AI calibration defect.**

Same seeds (1–200), 4 players, Standard AI, round cap 15.

| metric | v1 + catch-up (`sim-out/cur-regress`) | v2 (`sim-out/denser-map-200`) | v2 + Safety=5 (`sim-out/safety5`) |
|---|---|---|---|
| first capture (major round) | 6.52 | 5.46 | 4.68 |
| matches with zero captures | — | 60 / 200 | 3 / 200 |
| non-convergence (MajorRoundLimit) | 11.5% | 27.5% | 9.5% |
| finished-match length | 11.15 | 9.43 | 10.23 |
| round-3 leader win rate | 46.5% | 64.5% | 51.0% |

**Root cause of the non-convergence spike: `EvaluationWeights.Default.Safety = 20`.**
That value was tuned on the sparse v1 map and its own doc-comment flags it as "阶段 B 的首个校准项" (stage-B's first calibration item). Sweep on v2 (200 matches each, `sim-out/safety*`):

| Safety | 5 | 10 | 20 | 30 | 40 | 60 |
|---|---|---|---|---|---|---|
| non-convergence | 9.5% | 31.5% | 27.5% | 34.0% | 42.0% | 57.5% |

Non-monotonic; raising it makes things worse. High Safety keeps a score-improving move always available (patching liberties on threatened groups), so the AI never passes. In the 29 matches that stayed unresolved even at a 40-round cap, rounds 31–40 averaged 40.6 placements against 40.6 captures — net zero, concentrated on ~18 distinct cells.

**Snowball root cause is different from what catch-up-recruit assumed.** By rank, from major round 5 (120 matches): deploy limit ~5 for everyone, actual placements 1.61 / 0.92 / 0.68 / 0.75 for ranks 1–4, pass rate 22.9% / 42.7% / 52.8% / 45.2%. Nobody comes close to their deploy limit. Trailing players do not lack pieces, they lack worthwhile cells — so recruit-side compensation cannot reach them on a dense map. Compensation must grant space/position, not cards.

**Follow-ups queued**: (1) calibrate `Safety` as its own change with a 3/5/7/8 sweep — it shifts every existing baseline; (2) rework catch-up compensation toward space; (3) `maps/siege-4p-base-v1.json` no longer loads (109 playable vs the new 80–95 rule) yet §3.3 says it is kept for comparison; (4) `CommandLine` silently drops unknown options — `--matches` was accepted and ignored, running 1 match instead of 200.


## Session: 2026-09-21 — 以 `siege-rules-and-ai.md` 为基准的规则回归（仅规划，未写代码）

**起因**：用户给出 `D:\ws_feature\Jungle\siege-rules-and-ai.md`（GDScript 原型的规则 + AI 整理稿）并要求作为规范真相源。逐项比对后发现它落后 / 冲突于 09-13 之后的七个数值 change，用户在冲突表上逐行裁决。

**裁决索引**（全文与理由见 `openspec/changes/restore-go-core-rules/proposal.md` 的裁决表，及三个 change 各自 design.md 的「裁决记录」）：

- 回归文档：#1 军势 `⌊(基础+位置加值)×1.5ⁿ⌋` 不封顶；#2 总势力 = 独占空格 + 军势；#3 势力降 0 立即出局、保护期不豁免；#4 只留三类终局、时长靠可落子格数与信物格数调；#7 征募固定 5 / 3、删落后补偿；#8 引入活棋禁入；#11 引入活形 / 眼位算法（分类表与 `EYE_SPACE_MAX=12` 原样）。
- 保持仓库：#5 地图按生成算法；#6 匠人 / 地形 / 高地加值；#9 UI 对局用启发式 AI；#10 保留批次级评价架构、并入眼位；#13 分阶段基础部署上限 3→4→5。
- 派生：#14 据点整体移除；#15 大回合上限 / 碾压 / 落后补偿规范与代码都删（Sim 留技术性 `turn_limit` 截断，默认 600 小回合，不产生名次）；#16 非所有者不得以任何手段（落子 / 立栅 / 搭桥）破坏他人已确定活形；#17 无气边即墙。
- 追认：空林地格不计领地分；内置图改名 `siege-4p-base-v5` / `siege-frontier-v2`，`gen:` 不加版本段、同种子产物会变。

**产物**（均 `openspec validate --strict` 通过，0 任务已做）：

| 顺序 | change | 内容 | 任务数 |
|---|---|---|---|
| ① | `restore-go-core-rules` | 计分 / 出局 / 终局 / 征募回归 + 据点摘除，20 个能力的增量 | 31 |
| ② | `life-shape` | 新能力 `life-shape`（空区、封闭眼空间、眼值三态、禁入格、查询）+ 预演两步新检查 | 23 |
| ③ | `ai-eye` | 七维→九维（眼位、威胁）、活形硬约束、停手阈值、活形中性、全量重新校准 | 22 |

**开工闸门**：当前工作区有 `frontier-map`（29/30）与 `map-generator`（已完成未归档）的未提交改动，① 的 `map-definition` / `map-generation` 增量以它们**归档后**的规范为基线——必须先提交、做完 `frontier-map` 最后 1 项、两者归档，再开始 ①。

**已知并接受的中间状态**：只做完 ① 时 AI 对局不收敛（无上限、无停手阈值，会大面积触发 `turn_limit`）；②③ 做完才是可玩状态。第 3 大回合领先者胜率可能回到 50% 以上（裁决 #7 接受，只报告不拦截）。

**实施中要盯的点**：势力 / 军势必须用不溢出的整数类型（`3^n` 远超 64 位）；`EvaluationWeights` 注释里的 `Safety = 35` 与主规范「默认评价权重的校准」写的 5 早已不一致，③ 统一重写；`PowerGain` 在不封顶倍率下可能淹没其余维度，对数刻度是备选但**启用前须用户确认**；codegraph 在本仓库未初始化（`codegraph init`），子 agent 规则 #7 目前无法满足。

**未做**：`.trellis/spec/core/` 的 AI / 计分编码约定没有现在写，而是挂在 ①6.3、②4.5、③5.2（代码落地后按实写）；`.trellis/tasks/` 尚未建任务。
