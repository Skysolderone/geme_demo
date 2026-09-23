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


## Session: 2026-09-22 — ① restore-go-core-rules 实施完成并归档

**状态**：① 六段（A–F）全部实施、复核、提交并归档为 `openspec/changes/archive/2026-09-22-restore-go-core-rules`；Trellis 任务已归档。测试 **1197 全绿**，`siege.sln` 与 `src/godot/Siege.Godot.csproj` 均 0 警告，`openspec validate --all --strict` 31 项全过。

**提交链**：`d668d2f` 段 A（军势整体乘倍率不封顶、领地计分、BigInteger）→ `1682f97` 段 B（据点摘除、内置图改名 v5 / frontier-v2）→ `66e410f` 段 C（势力归零即出局、三类终局）→ `697bcbe` 段 D（删落后补偿）→ `c30484b` 段 E（Sim 截断与报告、领地显示）→ `940f090` 段 F（设计文档 v1.5、守门补强、20 局冒烟）→ 归档提交。

**下一步：② `life-shape`（0/23）**，之后 ③ `ai-eye`（0/22）。开新会话后从 `openspec/changes/life-shape/tasks.md` 起步，照 ① 的节奏：建 Trellis 任务 → 每段一个 trellis-implement → 主会话前台复核（构建 + Godot 单独构建 + 全量测试 + 必要时补变异）→ 提交 → 下一段。

**本会话形成的工作约定（新会话必须沿用）**：
- **同一时间只跑一个任务**（用户 2026-09-22 明确要求，已存记忆）：agent 在跑时主会话不起后台 dotnet / 变异 / 第二个 agent。
- 变异还原必须逐字节校验 **并 `os.utime` 刷新 mtime**，否则 MSBuild 跳过重建、下一次"全绿"是假的（`testing.md` 已写）。识别信号：失败数恰等于上一条变异的红数。
- Godot 在编辑器外运行读 **Debug** 程序集：只做 Release 构建会截到旧代码（段 F 踩到，`testing.md` 已写）。
- `openspec archive` 遇到"某能力全部 REMOVED → 空 spec"会报错中止，但**中止前已写入按字母序排在它前面的主规范**（提示却说 No files were changed）。退役整个能力时：先把该能力的增量移出 change → 归档 → 再手工 `git rm` 该 spec 目录，并把增量放回归档目录留档。
- `.claude/agents/trellis-implement.md` / `trellis-check.md` 已加 `mcp__codegraph__*`，**需新会话才生效**；codegraph 索引已在 `.codegraph/`（已 gitignore）。

**负责人本会话裁决（2026-09-22）**：
- 6.2 只跑 20 局冒烟，**不补 200 局**；完整 200 局基线由 `life-shape` 4.4 首次给出，`ai-eye` 校准以它为准。
- 匠人征募权重统一取代码缺省 **10**（设计文档 §9 已改），旧扫档值 5 随计分口径失效。

**20 局冒烟关键数字**（`sim-out/restore-smoke20/`；Standard、v5、种子 1–20、七维权重未校准、匠人 10）：截断 0/20，终局 20/20 为整轮 Pass；平均结束第 18.7 大回合（中位 15、最长 69，目标 7–10）；单串军势峰值 29776；倍增子选择率 88.1%；整局无提子 5/20；第 3 大回合领先者胜率 35%（样本不足）。

**修正了 ai-eye 的前提**：原以为删上限后 AI 不收敛，实测 **Standard 已收敛**（贪心"总分严格提高才落子"自带停手）；当初"200 局会近 100% 截断"的判断是拿 Easy 数据套到 Standard 上，是主会话的错误。ai-eye 真正要解决的是**对局偏长**与**不打仗**；Easy 是否收敛仍未验证。

**遗留给后续的已知项**：`M-C12`（Pass 后出局检查）只能靠伪造存档触发，最薄；`改造可查` 未钉住 Scenario 的具体局面；势力排名面板左缘离按钮条约 140 px；`.trellis/tasks/` 下 `09-19-frontier-map`、`09-20-map-generator` 两个 Trellis 任务对应的 openspec change 已归档，但 Trellis 任务本身尚未归档（上一会话遗留，未处理）。


## Session: 2026-09-22 — ② life-shape 实施完成并归档

**状态**：四段（A–D）实施、主会话前台复核、提交，归档为 `openspec/changes/archive/2026-09-22-life-shape`；Trellis 任务已归档。测试 **1266 通过 / 2 门控跳过**（`SIEGE_PERF` / `SIEGE_SLOW`），两处构建 0 警告，`openspec validate --all --strict` 31 项全过，`ai-eye` 仍 valid。

**提交链**：`a14414f` 段 A（`LifeShapeReport`）→ `3781fa5` 段 B（预演八步、`LegalRangeFor` 唯一扣禁入、公开视图）→ `c358936` 段 C（预演提示 / 表现层 / 终端 / Godot）→ `33e929b` 段 D（遥测、分析、设计文档 v1.6、200 局基线）→ `48146b2` 归档 → `e045be1` 任务归档。

**实施裁决 R1–R14** 全文在归档 design.md「裁决记录」。要点：慢测试 = 环境变量 + `Category` 门控（testing.md）；性能分母含气；扣除禁入只在 `LegalRangeFor`、读取放开；"已活"优先于"危险"。

**200 局基线**（`sim-out/life-shape/baseline200/`，v5、种子 1–200、Standard、AI 未校准）：截断 0、整轮 Pass 200/200；结束大回合 平均 13.15 / 中位 10 / 最长 83（① 冒烟 18.7 / 15 / 69）；整局无提子 41%；首次活形确立第 1.02 大回合；终局禁入格占可落子格 34.9%；他人致失活 0。

**待负责人裁决（R8）**：活形过早过易——终局单子活形 77.3%、贴地形 ≤3 格小空区眼空间 85.1%。是否另开 change 修规则（例如眼空间须贴 ≥ N 枚子 / 地形墙不算封闭）尚未定，**`ai-eye` 开工前应先定**，否则其校准以这个口径为准。

**待负责人过目**：`sim-out/life-shape/v5-groups.png`、`frontier-groups.png`（已活标记在信息层降饱和下与草地难区分）。

**已知薄弱点**：破坏活形在确认阶段被拒的日志路径无专门测试；活形失去原因（所有者自拆 vs 其它）为启发式；"有活形玩家胜率"在 v5 上区分度低。

**工作树里非本任务的改动**（未提交，一直未碰）：`src/godot/scripts/GameRoot.cs` 的 `--export-parts` 一段、`src/godot/scripts/PartExport.cs`、`src/godot/parts/`。

**下一步**：先裁决 R8 → ③ `ai-eye`（0/22）。


## Session: 2026-09-23 — ③ ai-eye 段 A–C 完成，暂停于段 D 前

**状态**：ai-eye 段 A–C 实施、前台复核、提交；**按用户"不要并行处理"暂停**（另一会话在 `.claude/worktrees/terrain-surfaces` 做 `terrain-surfaces`）。Trellis 任务 `09-23-ai-eye` 仍为当前任务（未归档）。测试 1305 通过 / 4 门控跳过，两处构建 0 警告，`openspec validate ai-eye --strict` 通过。

**提交链**：`e4aa4b4` 段 A（九维、眼位 / 威胁、Safety 活形中性）→ `048c069` 段 B（活形硬约束、停手阈值、CLI `--pass-threshold`）→ `ee739b7` 段 C（预筛七维口径、决策内活形缓存、评价版本 3）。实施裁决 R1–R14 与待裁项见 `openspec/changes/ai-eye/design.md`「裁决记录」。

**段 D（校准）开工前必须先裁决**：
1. life-shape R8 活形过易（200 局：单子活形 77%、贴地形小空区眼 85%）；段 B 后 20 局整局无提子 19/20。若改规则 → 先另开 change。
2. terrain-surfaces（浅滩改气与眼空间）是否先于校准合入——若是，校准要在新地表规则上做；两边都改 AI 候选剪枝 / 预筛，合并可能冲突。
3. 预筛"七维"口径（现行：安全维不查活形、眼值记 0）。
4. R10 旧日志"只重放落子结果"未做，是否另立任务。
5. 段 C 新增 / 改写的规格增量（「候选格上限」「活形分析的决策内缓存」）待过目。

**参考数字**（未校准）：段 B 后 v5 20 局平均结束 7.70 大回合、截断 0；段 C 耗时对 c18ad97 标准图 0.45×、边疆图 1.05×；边疆图基线本身约 2.4 s / 小回合，大量跑边疆图建议另开性能任务。简单难度 v5 仍有 1/20 局打到 600 小回合。


## Session: 2026-09-23（续）— R8 裁决为 K1，life-single-stone 实施并归档

- ai-eye 段 D 前裁决 R15–R18 已记入 `openspec/changes/ai-eye/design.md`（`ad199af`）。
- R8 五变体对比实验（v5、各 50 局，数据 `sim-out/r8-explore/`）：负责人选 **K1 单子棋串不受活形保护**。
- change `life-single-stone`：`e1d23ce` 实施（只改 `LifeShapeReport` 三态一处）→ `78b8e33` 归档 → `9a4df63` 任务归档；设计文档 v1.7。复核与实验逐局相同：整局无提子 22/50（原 47/50）、结束 8.02 / 最长 13、终局禁入 25.1%、第 3 大回合领先者胜率 76%（n=50，留给段 D 200 局观察）。1310 通过 / 4 门控跳过。
- 当前 Trellis 任务切回 `09-23-ai-eye`。**下一步**：等 `terrain-surfaces`（`.claude/worktrees/terrain-surfaces`，另一会话负责）完成 → 主会话合入 main（与 `LifeShape.cs` 单子上限相邻，需复跑活形测试）→ ai-eye 段 D 校准。
