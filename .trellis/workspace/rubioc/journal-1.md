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


## Session: 2026-09-24 — ③ ai-eye 完成并归档；terrain-surfaces / life-single-stone 已合入

- ai-eye 段 D1 / D2 / E：`168bba6` → `762078d` → `38d70d8` → 归档 `c6cc2c1` → 任务归档 `19e870e`。定值 Eye 200 / Safety 35 / Threat 25 / 停手阈值 80（三档共用），简单难度 = 即时势力 / 敌损 / 眼位三维（R26）。设计文档 v1.9。1407 通过 / 5 门控跳过。
- 复核（标准难度）：v5 截断 1%、无提子 32%、结束 9.81；`gen:<i>:s1` 截断 0、无提子 0%、结束 42.5；边疆图 n=81（负责人叫停，**200 局完整复核延后**）截断 1.2%、结束 43.4。
- **已知问题**：简单难度 v5 截断 18%（34/36 为同形判重含棋子类型导致的互提循环 = R25）、74 局首回合一子不落；v5 领先者胜率约 71%；大图对局约为 v5 的 4 倍长。
- **待负责人**：人 vs 3 AI 实机（清单在归档任务 implement.md 段 E；HANDOFF「边疆图验证结论」为占位）；HANDOFF 全文仍是 09-19 内容。
- **下一步**：R25 规则 change——同形判重是否忽略棋子类型；修完复跑简单难度 200 局。


## Session: 2026-09-24（续）— superko-occupancy 完成并归档

- 同形判重忽略棋子类型：`828c592` 实施 → `ebd397d` 归档 → `f9f8f69` 任务归档；设计文档 v1.10；1410 通过 / 5 门控跳过。
- **负责人：不再跑 200 局**（已存记忆）。本 change 的简单 / 标准难度复核延后，修正对截断（原简单 36/200、标准 2/200）的实际效果暂无数据；需要数据先问可接受规模。
- 仍待负责人：人 vs 3 AI 实机（清单在 `09-23-ai-eye` 归档任务 implement.md 段 E）；HANDOFF 全文过时；简单难度首回合一子不落（阈值问题，另议）。
- 当前无进行中的 change / Trellis 任务。


## Session: 2026-09-25 — 简单难度首回合修复、插旗竞争

- `pass-threshold-first-stone`：己方无子时停手阈值视为 0（`068a0ab`，归档 `89acccf` / `b637510`）。简单难度 v5 20 局首回合全员 Pass 8/20 → 0/20；截断 4/20 只报告。设计文档 v1.11。
- `flag-contest`：原型插旗冒险概率 p（缺省 15，子流 `flag-risk`，CLI `--flag-risk`），p = 0 逐项不变（`0952c29`，归档 `bd2fdee` / `5f40c4d`）。v5 Standard 20 局：同区 7/20、截断 1/20（同区局）。设计文档 v1.12。1425 通过 / 5 门控跳过。
- 注意：`0952c29` 顺带提交了 Godot 生成的 `PartExport.cs.uid`、`TerrainParts.cs.uid`（对应 `fafe15b` 新增脚本，仓库本就跟踪同类 .uid）。
- 文档中剩余未开发：2 / 3 人专属地图（§19-2）、更多棋子与信物（§19-3）、正式 AI（§19-4）、联网（§19-5）、带入带出（§19-6）、正式美术音效。遗留：落后者无处可下、简单难度截断、边疆图 200 局延后、人机实机、HANDOFF 过时。


## Session: 2026-09-25（续）— 2 人 / 3 人专属地图

- `small-maps`：`768d5e7` 段 A（`siege-2p-base-v1` 9×9 C2，入口人数缺省取人数上限）→ `42bd718` 段 B（`siege-3p-base-v1` 11×11 竖直中轴镜像，三区距离 3/4/2 相等）→ 归档 `16cab81` / `be9e5b1`。地图以手写 JSON 为准、嵌入 Siege.Core。设计文档 v1.13。1443 通过 / 5 门控跳过。
- 20 局冒烟（Standard，只报告）：2 人图截断 0、均 8.15 大回合、**整局无提子 18/20**（已知问题）；3 人图截断 0、均 9.45、无提子 1/20、先手胜率 25%。
- 待负责人：两图截图过目；两图都有不过桥的侧翼路（按缺省保留）；3 人图信物 10 个在区间下限。
- 文档剩余：更多棋子与信物（§19-3）、正式 AI（§19-4）、联网（§19-5）、带入带出（§19-6）、正式美术音效；遗留：落后者无处可下、简单难度截断、小图 AI 校准、边疆图 200 局延后、人机实机、HANDOFF 过时。


## Session: 2026-09-26 — ① 新棋子与新信物（more-pieces-relics）完成

- 四段：`854d371` 段 A（内容集 v1 / v2、旗手 / 铁链 / 哨兵 / 界碑）→ `1199e78` 段 B（连营 / 犄角 / 驿站 / 工坊、千分制生成）→ `71df14f` 段 C（AI / 终端 / 遥测，`RevealedRelics.Of`）→ `85d3371` 段外修复（预计提子按改造后气边分组）→ `7f17416` 段 D（Godot 新轮廓、七项势力层、设计文档 v1.14）→ 归档 `ec07fc6` / `69423d9`。1572 通过 / 5 门控跳过。
- v1 下走法与改动前逐步相同（旧存档 / 旧日志按 v1）；新局缺省 v2。
- v5 v2 20 局冒烟：截断 0、均 8.4 大回合、新棋子选择率 31–37%。
- 待负责人：`art/more-pieces/` 十种棋子灰度对照（界碑 / 普通子灰度差最小 8.4）、工坊高亮截图；权重与估值均未校准。
- **下一步（负责人定序 1 → 4 → 2）**：④ 带入带出（先讨论设计），再 ② 正式 AI（一层前瞻，新"困难"档）。


## Session: 2026-09-26（续）— ④ 带入带出（carry-in-out）完成

- `c9e6209` 段 A（补给效果、结算、配置与存档兼容）→ `c21d03f` 段 B（本地档案、AI 带入、终端 resign）→ `42ff775` 段 C（Godot 面板、遥测回放、设计文档 v1.15）→ 归档 `b0ceeaa` / `18a802f`。1685 通过 / 5 门控跳过；真实 `%APPDATA%\Siege` 全程未创建。
- 补给：备用子 3 / 征召签 2 / 换型令 4；名次点 4 人 24/16/12/10；弃赛 50%（保护期内 0）并返还；出局 0 丢失；完赛不返还；AI 同数量随机带入；批量缺省关闭。
- 待负责人：手动试玩一局有人值守链路（带 `--profile=<临时路径>`）；面板截图 `art/carry-in-out/`。已知缺口：终端出局 / 完赛结算无脚本测试。
- **下一步**：② 正式 AI（一层前瞻，新"困难"档）。


## Session: 2026-09-27 — ② 正式 AI（专家难度一层前瞻）完成

- `3f8c7bd` 段 A（Core 前瞻、公开规则纯函数、守门；负责人裁决：被回应提走的己方棋子按危险计入前瞻后安全维）→ 段 B（难度严格解析、四档入口、前瞻遥测、设计文档 v1.16）→ 归档。1754 通过 / 6 门控跳过。
- 三档旧难度黄金值不变；W = 1 与高难逐步相同；耗时专家 / 高难 1.36。
- 冒烟 20 局（1 专家 + 3 标准）：专家胜 5/20（= 均等基线）、平均名次 2.25、前瞻改变选择仅 1.6% —— 强度提升不明显，待后续评估（可能需要前瞻宽度 / 对手模型 / 评价口径调整）。
- 用户排定的 1 → 4 → 2 全部完成。待负责人：带入带出与专家难度的人工试玩；各项截图过目。


## Session: 2026-09-28 — 诊断与 V2 复核（engagement-diagnosis → v2-recalibration）

- 诊断（`ddf983f`）：2 人图不交战主因是地图结构（保护期后对方区被封死、近侧侧翼易做贴墙眼），阈值不是原因；落后者问题较旧数据减半，接受不改；简单难度截断是多人近循环（V2 0/20、V1 4/20），不改规则。
- `v2-recalibration`：`925af6f` 停手阈值 V2 复核 80 → 20（v5 无提子 8/20 → 3/20、提子 31 → 118）→ `b1fe427` 2 人图专属 AI 权重覆盖 Eye 50（无提子 18/20 → 13/20）→ 段 C 设计文档 v1.17 → 归档。全程每批 20 局，共 220 局。1775 通过 / 6 门控跳过。
- 仍开放：2 人图仍 13/20 无提子（地图 v2 可进一步改善）；V2 上九维权重未完整重扫；边疆图 / 生成图未复核；专家强度提升不明显；人工试玩（带入带出、专家）；HANDOFF 过时。


## Session: 2026-09-28（续）— 专家强度实验与 expert-strength（负结果）

- 对比实验（8 变体 × 20 局）：原专家 ≈ 高难；前瞻少改选择的原因链——候选落点同质、对手回应与候选无关、对手收益为常数、无可避之候选。
- `expert-strength`：`ad18657` 段 A（多样候选 + 近似两层，G1 / G2 守门）→ `fb0addb` 段 B（λ 扫档、成对扩样 60 对）→ 结论：新专家**弱于**高难（好 7 / 同 33 / 差 20，p = 0.019）→ 负责人裁决预设退回一层（λ = 0、S = 0），机制保留为可配置项 → 归档。1806 通过 / 6 门控跳过。设计文档 v1.18。
- 下一步候选：专家归因实验（为何两层偏向少落子 / 更多 Pass）、2 人图 v2、V2 权重重扫、联网、美术。


## Session: 2026-09-29 → 09-30 — 棋盘化地图 / 禁手标记 / 结算演出（全部未提交，待人工验收）

- 负责人四条试玩反馈 → 三个 change 顺序实施，全部未 commit，工作区约 70 个文件改动。2011 通过 / 8 跳过。
- `board-map`：新增棋盘档（`board:<种子>[:n7–10]`），坐标第 26 列起双字母，棋盘清单入地图数据，选图预选棋盘图（批量 / 终端缺省仍 v5）。两次修正后：通道 3–4 宽、出生棋盘两条出路、公共棋盘 9–11、目标带 300–800。**冒烟 2+2 局（AI 对 AI）负结果**：封门只推迟（最早被封 7/13 → 21/25 大回合），终局通道全部不通、3–4 名玩家被封；对局 46–63 大回合（目标 7–10）；AI 小回合中位 1.3–2.0 s、最大 7.7 s。判断：对局长度随可落子格数线性增长，通道几何不是根因。待负责人真人试玩后在"接受长局 / 缩图 / 通道不可落子规则"里选。
- `forbidden-marks`：自杀手 / 同形 / 破坏活形按"暂放批次之上再落一子"事先打叉（`ForbiddenMoves`，复用预演 `BatchRehearsal.Settle`），预筛后 853 格图约 22 ms；全局预览下放大；双字母列标字号。教训：原设计"不提子即不可能同形"不成立。
- `turn-settlement-show`（另一会话规划、本会话实施）：节拍生成 + 时间线在 Presentation.Show，Godot 接线；提速按住空格 / 左键（负责人 09-30 裁决）；无人值守零时长，53 帧基线一致。4.4 人工试玩未做。
- 待负责人：验收清单已交（三张禁手截图、两张地图截图、两张演出截图在 art/ 下）；是否 commit 由负责人定。
- （09-30 续）`settlement-show-callouts`：落子飘字（类型名 + `GroupPowerView.ShortFormulaText` 短算式）、提子飘字与合计、势力两段到账（领地 / 军势各 350 ms + 定格）、信物揭示节拍、出局 / 终局横幅（终局面板延后到横幅播完）。零时长与 53 帧基线不变。本会话最后一次全量：2025 通过 / 8 跳过 / 0 失败。四个 change 均未提交，等负责人人工验收与提交指示。
- （09-30 续 2）负责人已自行提交并归档前四个 change（`1a718ad`/`842465b`/`d25110a`）。`ai-turn-speed` 提交 `6a9d716`：决策逐步不变（v5 + board:1 决策序列基线逐字节相同），board:1 中盘单个 AI 小回合 1562 → 132 ms；GameBoard 版本号记忆化、地图实例记忆化邻接、CoverageMap 惰性集合。`show-sound-cues`（未提交）：程序合成六段占位音、提示导出纯函数、`--mute`、无人值守零音频节点，53 帧不变；`ShowSounds.cs` 的 .uid 需负责人开一次编辑器生成。仍待负责人：棋盘图长局 / 封门裁决（A/B/C/D）。


## Session: 2026-10-03 — 收尾

- `show-sound-cues` 提交 `bc77588`，`.uid` 由 headless `--editor --quit` 生成（可行，约 1 分钟）并提交 `8192b64`；`ai-turn-speed` / `show-sound-cues` OpenSpec 归档 `c71c0a1`、Trellis 归档 `f5d5a8d`。
- 负责人授权全部由实施方决定：棋盘图长局 / 封门裁决为 **A 接受长局**（设计文档 §3.3 与变更记录已写明理由：长度随格数线性；"被封"只是几何指标，保护期后全盘可落子）。音效音色 / 音量未实机试听，参数表 `ShowSounds.Table` 待负责人反馈后再调。
- 当前无进行中的 change / Trellis 任务。
- （10-03 续）`board-render-perf`：原方案前提错（以为开销在场景装饰；实测 72%–76% 的绘制调用来自可落子地砖的独立网格与材质）。两轮后 board:1 静止帧绘制调用 4097 → 792、n10 / n7 之比 1.635 → 1.179，14 张截图 0 像素差异；演出期间棋子增量重画。截图证据在 `sim-out/board-render-perf/`（不入库）。剩余大头是棋子（每枚若干独立网格与材质）与水面，未做。


## Session: 2026-10-04 — map-elements-v2

- `map-elements-v2`（地图元素全套精修与部件化）在 **Mac（Apple M3 / Metal）** 上做完并归档：提交 `e9a7ea1`（前置 + 段 A）、`9356632`（B）、`c5c464c`（C）、`6308c00`（D）、`40c6cc5`（E）。负责人先定"在本机做"，段 A 之后授权实施方全权决策；决策清单在 `art/map-elements-v2/README.md` 文末。
- 做了什么：新增 `LowPolyMesh`（Builder：倒角板 / 分层侧面 / 多面岩块 / 方盒 / 棱台 / 多边形片 / 条带 / 折板，整数散列，顶点色当亮度系数）；部件目录 11 类 → 24 类、资源 79 件；地块、水系、桥与栅栏、障碍与点缀、外圈石沿、出生区亮条、台面边框、浮岛全部取部件，缺失时退回程序建模（`board:1` 两种来源 0 像素差）。
- 读数（本机静止帧绘制调用）：v5 642 → 512、`board:1` 520 → 472、`board:1:n10` 466 → 444、`gen:12345:s1` 1317 → 728；`board:1` 合批与 `--no-batch` 0 像素差（改前因岩石的非均匀缩放相差 54042 像素）。
- 环境：本机 Godot 4.7.2 .NET 装在 `~/Applications/godot-4.7.2-mono/`，构建 Godot 工程要 `-p:RestoreConfigFile=~/Applications/godot-4.7.2-mono/nuget.config`（仓库里的 `nuget.config` 是 Windows 路径，没动）。
- 遗留：本机 `dotnet test` 11 条 Core / Sim 日志黄金哈希红（干净的 00a6747 上同样红，原因未查，疑与平台有关）——需在 Windows 机上确认本 change 之后全绿；五个实现提交加归档提交尚未推送。`LowPolyMesh.cs.uid` 已用无头编辑器生成并提交。
- 当前无进行中的 change / Trellis 任务。
- （10-04 续）发布网站与下载包：`docs/` 是 GitHub Pages 发布页（需在仓库 Settings → Pages 选 `main` / `/docs`），下载按钮指向 Releases 的固定文件名 `Siege-macos.zip` / `Siege-windows-x64.zip`；`tools/export-release.sh <版本>` 在 Mac 上导出两个包到 `build/`（不入库）。macOS 包本机验过能跑；Windows 包只验了结构。Release 由负责人在网页上手动建并上传。
- （10-04 续 2）发布落地：网页改放 **Cloudflare**（Workers 静态资源，`wrangler.jsonc` 指向 `docs/`，`npx wrangler deploy` 即更新）→ https://siege.wws741.workers.dev ；GitHub Pages 没有开。Release `v0.1.0` 已用 `gh` 建好并上传两个包，网页下载按钮走 `releases/latest/download/…`。仓库主页链接已指向发布页。发新版流程：`tools/export-release.sh <版本>` → `gh release create v<版本> build/Siege-*.zip` → 改 `docs/index.html` 的版本号与更新记录 → `npx wrangler deploy`。
- （10-04 续 3）`follow-opponent`：对手行动时镜头跟过去、轮到本机再回来（默认开，`F` 关；状态机 `CameraFollow` 在呈现层，推翻了 viewport-camera 的"不跟随对手"），对手回合摘要条，飘字寿命 0.6 → 1.4 秒，棋串军势常驻标注。新增 19 条测试、8 条变异全抓到；无人值守下相机仍不动（自检口径改为"无人值守下相机不得因对手行动移动"）。返回 / 让位 / 关闭的接线没有自动化验证，等负责人实际玩一局反馈。发布页与 Release 还是 v0.1.0，没有这个功能，下次发版再更新。


## Session: 2026-10-05 — tiered-number-show（数值呈现五档递增）

- 负责人反馈"游戏数值需要呈现效果更强烈一点，产生层级递增"。先跑 10 局取分布（v5、4 名标准 AI、种子 1–10）：落子所在棋串军势中位 4、90 分位 13、最大 106，带倍率的落子只占 24%，终局势力中位 56——数字本身偏小。负责人五项裁决：**先呈现、后数值**；按数值大小分档；五档、阈值 4 / 8 / 16 / 32；全套手段（字形分档、冲击环、逐步揭示、震屏）；揭示逐串依次、上限 1.6 秒。其余十条初值一次确认，授权按表执行到底。
- 分支 `feat/tiered-number-show`（负责人同日指示直接合并，已快进合入 main 并删除分支；**未推送**）：`c2123d1` 段 A（呈现层：`NumberTier` / `NumberTierStyle`、`PowerRevealBeat`、遮罩加揭示 / 亮环 / 轻震、音效提示带档位）→ `601bd5b` 段 B（Godot：揭示条目、亮环、轻震、势力栏与常驻标注按档、揭示音与按档升调、`--reveal-preview=play|ladder`）→ 段 C（设计文档 v1.22、归档、本节）。2138 条 = 2118 通过 / 11 失败 / 9 跳过，失败集合与本机基线逐条相同；变异 67 条全红；`--auto-demo` 53 帧不变、相机位姿变化 0 次；`--pick-check` 三张图通过。
- 实施中改了两处初值：四、五档末步加定格 0.44 / 0.66 秒（最大的棋串排在最后，结果停留会被演出结束截断）；势力栏增量文案不分档着色（红色已用于负增量）。检查阶段另定：名次变动提示不跟着段首放大。
- 教训写进 `testing.md`：引擎层硬约束要自带源码扫描守门（段 B 交付时 8 条引擎层变异全绿）；运行期自证要在分辨得出问题的位姿上做并给非零退出码；按节拍个数断言在新增节拍后会假绿；`--screenshot=` 要绝对路径。
- **待负责人**：试玩一局（人对 3 名 AI）确认档位观感、节奏（每回合演出估算 1.45 → 2.2 秒）与音量；音效完全没试听（揭示音、五档升一个八度是否刺耳）；`--reveal-preview=play` 可不开对局循环看五档。截图与清单在 `art/tiered-number-show/`。
- 已知不足：一、二档与现状一样小；档位色与阵营色无关（蓝方棋串的五档结果是红字）；全局预览下相邻棋串同回合揭示互相压住；四位数势力配五档增量与名次变动时势力栏可能蹭到行动顺序条。
- **下一步**：负责人试玩反馈之后另开数值规则 change（让数值本身拉开层级），届时档位阈值重定（只改 `NumberTier.Thresholds` 一处）。发布页与 Release 仍是 v0.2.0，没有本功能。
- 工作区里的 `.trellis/tasks/10-05-gamedev-agent-skills/` 是别的会话建的未跟踪目录，本会话没有动。
- （10-05 续）`reveal-label-handoff`：负责人看过后指出"本来棋子放置后已经有个数值了，现在重复显示了"——常驻标注一结算就按终态显示新军势，军势揭示又在同一格揭示同一个数（设计时漏了，`reveal-real-tier5.png` 上"= 111"与"111"同屏）。修法：遮罩加 `GroupLabelPermille`，被揭示棋串的标注演出期间隐藏，揭示结果停留过半时随其淡出而淡入。2146 条 = 2126 通过 / 11 失败 / 9 跳过；变异 23 条全红；53 帧基线不变。设计文档 v1.23。**教训**：在某一格新增一种数值呈现之前，先列出这一格上已经常驻显示的数值，裁决谁让位；本次检查方另挑的 8 条变异有 7 条交付时不红，引擎层与"三条目以上 / 压缩节拍"这类边界仍要靠检查阶段补。已合入 main，未推送。


## Session 1: 引入三个 Godot agent skill；评估 awesome-godot 与一批候选插件

**Date**: 2026-10-05
**Task**: 引入三个 Godot agent skill；评估 awesome-godot 与一批候选插件
**Branch**: `main`

### Summary

从 awesome-gamedev-agent-skills 固定提交原样装入 godot-shaders / godot-csharp / godot-ui-control，随附许可证与来源说明；打击感、镜头、粒子、主题类插件与 godot-mcp 评估为不加。

### Main Changes

- **起因**：负责人让看 `godotengine/awesome-godot` 有什么可用，随后贴来一批候选（打击感插件、主题工具、粒子与 shader 库、Phantom Camera、agent skill 合集、godot-mcp）。
- **装了什么**：从 `gamedev-skills/awesome-gamedev-agent-skills`（Apache-2.0，固定提交 `d4b0e35`）原样复制 `godot-shaders`、`godot-csharp`、`godot-ui-control` 到 `.claude/skills/`；许可证、`NOTICE` 与来源说明（含校验和与升级步骤）在 `.claude/third-party/awesome-gamedev-agent-skills/`。
- **负责人裁决**：装三个引擎向的。主会话推荐的是只装 `godot-shaders`，另两个是入门内容、与项目先例重复，代价是多占两条 skill 描述的上下文。
- **评估为不加的**（理由记在归档任务的 `prd.md`）：
  - Juicee / Game Feel Flow、Phantom Camera、`game-feel` skill：都靠 Tween、计时器或时间缩放自驱动，与"演出由 `Siege.Presentation` 按时间算出"（零时长、按住提速、定帧基线）对不上。
  - 粒子与 VFX 库：`visual-style-baseline` 禁过量粒子。
  - ThemeGen：UI 已在 C# 里用代码构造样式；godot-liquid-ui 仓库没有许可证，不能拷代码。
  - godot-mcp：只管启动、抓日志与编辑 `.tscn`，本项目节点在 C# 里构造，增益很小。
- **留意**：`godot-ui-control/SKILL.md` 第 23、123 行各有一句把 UI 过渡指向未安装的 `godot-animation`（Tween），遇到时以 `openspec/specs/settlement-show` 为准。`docs/` 是发布页静态目录，第三方声明不要放进去。
- **验证**：实现、检查、主会话三次独立克隆上游该提交，`diff -r` / `cmp` 逐字节相同。无代码改动，未跑测试。未推送。


### Git Commits

| Hash | Message |
|------|---------|
| `553e78c` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete


## Session 2: HUD 主题统一（hud-theme）：面板三级、按钮五态与语义、字号与间距阶梯、样式总览页

**Date**: 2026-10-05
**Task**: HUD 主题统一（hud-theme）：面板三级、按钮五态与语义、字号与间距阶梯、样式总览页
**Branch**: `feat/hud-theme`

### Summary

克制精修方向的 HUD 主题：取值集中到 UiTheme，Godot 主题按类型下发，选中底边自定义样式盒，--ui-gallery= 总览页，源码扫描守门；负责人过目后取值保持，合入 main 并推送。

### Main Changes

- **起因**：负责人要求做界面样式（业务改动由另一会话并行）；裁决范围「HUD 主题统一」、方向「克制精修」，看完改前改后截图后裁决取值保持。
- **做法**：独立工作树 `.claude/worktrees/hud-theme`、分支 `feat/hud-theme`，不碰主工作树；完成后不切分支，用 `git fetch . feat/hud-theme:main` 快进 main。
- **内容**：面板三级（主 / 次 / 提示条）；按钮五态与三种语义，选中态是金字加 2 像素金色底边（`Ui.EdgeStripBox`，StyleBoxFlat 只有一个边框色）；字号阶梯 13 / 14 / 15 / 17 / 19、间距阶梯 0 / 2 / 4 / 6 / 8 / 12；样式总览页 `--ui-gallery=<PNG 路径>`。取值全在 `UiTheme`，HUD 脚本只说"是什么"，写法由源码扫描守门。设计文档 v1.24。
- **验证**：全量 2147 通过 / 11 失败 / 9 跳过，失败名单与改动前逐条相同；两处构建 0 警告；`--auto-demo` 53 帧不变；`--pick-check` 105/105；变异 段 A 95 条、段 B 44 条全部变红。截图在 `art/hud-theme/before|after/`。
- **留意**：
  - 新守门禁止 HUD 脚本里首参为字面量的目标类型 `new(…)`（含 `Vector2`）、写死颜色、字号加减、间距数字、逐处改按钮字色；改 `Hud*.cs` 时走 `Ui` 工厂与 `UiTheme` 的名字。
  - 遗留：对局「开局插旗」面板末行被裁半行（改动前就有）；`Ui.MutedText` 的透明度 0.62 未进 `UiTheme`；回合摘要横幅新内边距与征募收起条未在截图里出现过。
  - 主工作树当时在另一会话的 `feat/formation-tiers`（基于 `ccafe40`），合入 main 时需与 hud-theme 合并，`Hud*.cs` 可能有逐行冲突。


### Git Commits

| Hash | Message |
|------|---------|
| `7eaa99d` | (see git log) |
| `7799184` | (see git log) |
| `4ab3af2` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete

## Session: 2026-10-05（续）— formation-tiers（阵型：棋串规模阶梯）

- 数值规则第二步。诊断（v5、10 局终局）：每人 14.4 枚子分在 6.9 条棋串、规模中位 2 枚，73% 的棋串没有倍增子，人均势力第 6 大回合后走平。负责人裁决方向"棋串规模阶梯"、门槛 3 / 5 / 8 / 12、每级 ×1.5 与倍增子连乘；作为**计分规则 v2**（新局缺省；旧存档 / 旧日志 v1；`run --scoring v1|v2`）。
- 分支 `feat/formation-tiers`：`e53e910` 段 A（Core / Sim：阵型、计分规则版本、日志与分析；不带版本的计分入口语义固定 v1，源码 + IL 扫描禁止产品代码调用；既有回归与黄金值显式钉 v1、期望值未改；HEAD 与 `--scoring v1` 跑 6 局日志逐行相同）→ 段 B 跑局 → `3e54a59` 段 C（算式拆出阵型因子、揭示多一步、热区按总指数、呈现阈值 8 / 16 / 32 / 64）→ 段 D（设计文档 v1.25、归档、本节）。2182 条 = 2162 通过 / 11 失败 / 9 跳过，失败集合与本机基线逐条相同；变异 54 条全红；自动演示 53 帧不变（帧数由 `--rounds` 决定）。
- 跑局（v5、4 名标准 AI、种子 1–20、每批 20 局，`sim-out/formation-tiers/`）：v1 → v2 人均终局势力 68 → 329、冠军 108 → 777、冠军 / 亚军 1.62 → 2.88、最大落子棋串军势 212 → 2364、8 枚以上棋串 3% → 9%；**整局无提子 3 → 7、提子 118 → 82**。停手阈值 10 / 40 与 20 差别小。负责人裁决：接受 v2；停手阈值保持 20；呈现阈值 8 / 16 / 32 / 64。
- **已知问题**：领先者优势变大、交战变少——AI 权重是 v1 下定的，v2 下未校准，下一步候选是 v2 下的九维权重重扫；2 人图 / 3 人图 / 边疆图 / 棋盘图在 v2 下都没复核；本机 11 条日志黄金值原本就红，需在 Windows 机上复核全量全绿（tasks 4.3）。
- **待负责人**：试玩（阵型跳升的手感、领先者是否太难追）；短算式里 `×2.25` 分不出是两枚倍增子还是阵型二阶（完整算式有"阵型"二字）——可选 a 短算式加标记 `×阵2.25`、b 只给揭示的阵型一步换色或角标（检查方倾向）、c 维持现状；音效仍未试听。
- 教训：段 A 交付时守门判"带版本"的正则过宽（`ScoringRelicCounts` 被误认），检查方用变异抓出；呈现层测试改期望时有一条反面断言被拼进行尾注释而失效——改测试文件后要看 diff 里有没有被注释吞掉的断言。


## Session 3: 信息面板重排版（hud-panels）：排名六列对齐、顺序条徽记、手牌两列、插旗提示不裁字

**Date**: 2026-10-05
**Task**: 信息面板重排版（hud-panels）：排名六列对齐、顺序条徽记、手牌两列、插旗提示不裁字
**Branch**: `feat/hud-panels`

### Summary

势力排名 / 行动顺序 / 手牌按列排版，文字由呈现层 HudPanelRows 给出，引擎层只摆放；修插旗提示裁字；守门扫全部 Hud*.cs 并白名单文字实参。负责人过目后保持，合入 main 并推送。

### Main Changes

- **起因**：负责人说"继续"做样式，裁决下一项为「信息面板重排版」；看完改前改后截图后保持。业务会话同时在做 formation-tiers（段 B 开工前已合入 main，合进本分支无冲突）。
- **内容**：势力排名六列对齐（本机金色竖条、势力右对齐、明细拆成状态 / 增量 / 名次提示）；行动顺序改成带徽记的条，当前行动者金字加金色底边，保留"行动顺序"标题；手牌名称与数量分列；插旗提示自适应高度，修掉末行被裁。按列文字在 `Siege.Presentation.Hud.HudPanelRows`，引擎层只摆放。设计文档 v1.26。
- **过程中的裁决**：有状态的行保留领地 / 棋串拆分（初版丢了，违反信息不减少）；演出中本段增量随段首放大（保留 tiered-number-show 的强度）、名次提示恒为正文字号（避免五档压到顶部顺序条）；对局至多 4 人，生成图 p5–8 是平台数。
- **验证**：全量 2204 通过 / 11 失败 / 9 跳过，失败名单与基线逐条相同；`--auto-demo` 53 帧；`--pick-check` 105/105。截图 `art/hud-panels/before|after/`。
- **留意**：
  - 源码守门现在扫全部 `Hud*.cs`，并对 `RefreshRank` / `RefreshOrderBar` / `RefreshHand` 交给 `Ui.Text` 等的文字实参做白名单——这三处要显示新内容，先在 `HudPanelRows` 加字段。
  - 截图只能取到一档放大；出局行、弃牌阶段手牌、名次变动提示没有画面证据。
  - 已知遗留：`Ui.MutedText` 的透明度 0.62 未进 `UiTheme`；盘面标注可读性（棋串数字 / 飘字 / 坐标）是样式的下一个候选。


### Git Commits

| Hash | Message |
|------|---------|
| `7a7939f` | (see git log) |
| `6cd4b45` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete
