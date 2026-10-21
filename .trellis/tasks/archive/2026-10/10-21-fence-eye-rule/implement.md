# fence-eye-rule 实施记录（任务 1.1–1.3、2.1–2.2、3.1）

基线 HEAD deb2793。裁决与验收见 `openspec/changes/fence-eye-rule/`。

## 1.1 眼边查询

- `Adjacency.EyeNeighbors(MapData, Coord)`：两格可落子、`|Δh| < TerrainData.CliffDrop`，不看栅栏；唯一实现。
- `Adjacency.LibertyNeighbors` 改为"`EyeNeighbors` 再去掉有栅栏的一对"（可落子与高差判定只剩一份，行为不变）。
- `GameBoard.EyeNeighbors(Coord)` 经 `MapTables.EyeNeighbors` 逐格记忆；`_eye` 表在派生构造里克隆、在 `Invalidate` 里作废（`盘面派生量提速对照Tests` / `盘面派生量优化对照Tests` 已加眼边比对）。
- 守门同步：`四邻接Tests.几何邻居枚举只在允许名单内直接调用` 的反面断言由 `LibertyNeighbors` 改为 `EyeNeighbors`（直接枚举几何邻居的是眼边导出）；`Godot层不含规则计算Tests` 禁用 token 加 `.EyeNeighbors(`。

## 1.2 LifeShape

三处改眼边：② 空区起点（沿眼边贴子的空格，design D2 的"只隔栅栏贴着"由此进入）、③ flood fill、方四 `IsRing`。文件内不再出现 `LibertyNeighbors`（源码扫描守门改名 `活形分析只经眼边邻格查询取邻接`，并禁止 `LibertyNeighbors(`）。

## 1.3 `LifeShapeReport` 调用者清单（codegraph `codegraph_callers LifeShapeReport` / `Analyze`）

| 调用者 | 用途 | 自带栅栏 / 眼逻辑？ |
|---|---|---|
| `BatchRehearsal.ValidateShape` / `Settle` / `BrokenLife` | 预演第 1 步禁入、第 6 步破坏活形 | 无，只读 `IsForbiddenFor` / `GroupLifeAt` |
| `ForbiddenMoves.Query` + `Quiet` | 单子禁手（经 `BatchRehearsal.Settle`） | 无；预筛条件 1/2/4 读气边（判气与提子，合法用途），条件 3 读 `EyeSpaceAt`。眼边下"不在他人眼空间 ⇒ 不改他人眼空间"的论证照旧成立（空格贴着封闭空区必属于该空区），预筛与完整预演逐格一致的对照测试全绿 |
| `PublicRules.LegalRange` / `MatchFlow.LegalRangeFor` | 合法落子范围扣禁入格 | 无 |
| `MatchFlow.Publish` / `PublishSeatProjection` / `SeatPublicView.PublishFor` | 公开视图带活形 | 无 |
| `BatchPreviewBuilder`（`Preview/BatchPreview.cs`） | 富预演 | 无；其中 `LibertyNeighbors` 只用于把预计提子分组成串（棋串语义，合法） |
| `BatchEvaluator`（`Life` / `SafetyOf` / `EyeOf` / `ThreatOf`）、`GroupSafety.Analyze` | AI 安全 / 眼位 / 威胁 | 无眼判定；`GroupSafety` 的 `LibertyNeighbors` 只数分散的气（气语义，合法） |
| `ExpertLookahead`、`HeuristicAi.Create`、`HeuristicTurnController`（`_lifeQuery`） | AI 决策内活形查询 / 缓存 | 无；`HeuristicTurnController` 里唯一的 `TerrainEditKind.Fence` 分支是改造诊断（`ArtisanKindReview`），不涉活形 |
| `GameBoard.LifeMemo` | 记忆化 | 无 |
| `Siege.Sim` `MatchSession.LifeEntry` | 遥测 | 无规则判定；见待裁决 ③（`IsTerrainSmall` 的统计口径） |
| 表现层 / Godot | 只读公开视图 | 守门 `表现层不调用活形分析只读公开视图`、`Godot层不调用规则计算入口` 照旧 |

算例：`禁入格Tests.隔着栅栏贴眼的他人棋子解除禁入`（真实流程：合法落子范围含该格、单子禁手查询不标、P1 落子提子后确认合法）+ 对照 `深水贴眼时禁入照旧`。

## 测试改写（因规则变更）

| 测试 | 改法 |
|---|---|
| `空区与封闭眼空间Tests.崖壁与栅栏算墙` | 拆为 `崖壁算墙` + `栅栏不是墙`（规格改名） |
| `活形三态Tests.被栅栏隔开的2x2不是方四` | 改为 `栅栏隔开的2x2仍是方四`（F4 反转） |
| 新增 | `栅栏不切分空区`、`崖壁隔开的2x2不是方四`、`只隔栅栏贴着空区的棋串也拥有该眼空间`（D2）、`眼边只去掉气边的栅栏一条`（1.1）、`隔着栅栏贴眼的他人棋子解除禁入`、`深水贴眼时禁入照旧` |
| `以整批最终状态判定合法性Tests.FencedLineTwoEyes` | 第 3 行之上加一行、A4 放 B 子：栅栏不挡眼边后第 3 行空区会成为 A 的眼空间、B 的匠人落不进去 |
| `立栅隔开眼与棋子被拒` | 改为 `立栅隔开眼与棋子不再破坏活形`（批次合法、A 仍两眼活）；见待裁决 ② |
| `提子后恢复活形的批次合法` | 夹具重做：原"立栅隔开眼"失效，改为"搭桥漏眼 + 同批立栅提子补眼"，仍经过"提子前未定、提子后活" |
| `活形硬约束Tests.用改造拆自己的活形也被淘汰`、`活形分析的决策内缓存Tests.地形改造不同的盘面不共用分析` | 两端的墙由预置栅栏 C3–C4 / K3–K4 改为未架桥深水 C4 / K4 |
| `单子禁手的标示Tests.与完整预演一致_暂放立栅造出他人的新眼` | 改名 `…_暂放搭桥造出他人的新眼`，夹具重做（暂放搭桥：一处造新眼、一处漏旧眼），仍是预筛条件 3 的守门 |

## 2.1

`EvaluationBreakdown.Version` 6 → 7；`EvaluationWeights.FenceEyeUncalibratedStatus = "未校准（fence-eye-rule 后）"`，接在 Safety / Eye / Threat 三维 `CalibrationOf` 末尾，`CalibrationStatus` 末尾补"活形无视栅栏后 Safety / Eye / Threat 三维未校准（fence-eye-rule 后）"；权重值不变；停手阈值口径不动（见待裁决 ④）。守门 `默认评价权重的校准Tests` 三处同步、`启发式评价维度Tests` 版本号、`专家前瞻的记录Tests` 三处版本字面量。

## 2.2 重钉清单

反面对照（逐字节不变）：5 份决策序列基线首部（含 `digest=E1110126…`，地图摘要不变）、地图摘要 / 地形穷举 / 链接图生成相关测试全部未改且全绿。

| 值 | 变化原因 |
|---|---|
| `decisions-link1-seed1.txt` | 首个分歧第 1 个决策行（大回合 1 P0）：旧"F7 匠人立栅 F8–F9"，新只落 D6 堡垒——栅栏不再造眼，立栅不再有眼位 / 安全收益 |
| `decisions-link1-seed2.txt` | 首个分歧第 3 行（大回合 1 P0）：同上，旧 F7 匠人立栅 F8–F9，新不立 |
| `decisions-link1-seed3.txt` | 首个分歧第 6 行（大回合 2 P3，栅栏阵 2 号棋盘）：旧落 M12（贴预置栅栏 M12–N12）+ J13，新落 J13 / N13 / K14——预置栅栏 M11-N11 / M12-N12 / L13-L14 / M13-M14 不再算墙（F3） |
| `decisions-link1-easy-seed1.txt` | 首个分歧第 1 行（P0）：旧 F7 匠人立栅 F8–F9，新不立 |
| `decisions-link1-hard-seed1.txt` | 首个分歧第 1 行（P0）：旧 F7 匠人立栅 F8–F9，新 E7 匠人搭桥 E8 |
| `候选格上限Tests.LinkGoldenTurnHash`（带入带出 ×2、对局内容集 v1、批量跑局缺省关闭带入共用） | link:1 种子 31 的 AI 走法随上面同一原因变 |
| `候选格上限Tests.SmallMapGoldenHash` | **派发时被列为"只依赖地图"，实为 AI 决策日志 + 终了盘面**。首个分歧：第 1 大回合首位行动者 P3（手里有匠人）的第一手，旧"F3 匠人立栅 G3–G4"（贴 5×5 棋盘外沿的格被栅栏围成眼），新"D4 普通子"。见待裁决 ① |
| `停手阈值Tests.StrictImprovementTurnHash`（种子 35） | 同上；另：种子 35 在阈值 0 / 20 下走法变得相同，反面断言改为写死区间 31–46 内至少一颗不同 |
| `专家难度的一层前瞻Tests.G1Golden` 三颗种子 | 决策 / 记录哈希变，条数 24、消费 8556、记录 12 不变 |
| `专家前瞻的记录Tests` | 一层专家：小回合 76 → 116、前瞻记录 19 → 29、哈希变；非专家：小回合 92 → 132、哈希变 |

样本下界（依赖 AI 走法、规则变更后落空）改为不依赖走法：

| 测试 | 改法 |
|---|---|
| `活形记录与统计Tests.真实跑局的活形字段自洽` | 种子 3 不再出现预演阶段破坏活形 → 脚本局：P0 一字两眼活形（眼贴地形），P1 每小回合预演一次"匠人立栅切开"再 Pass，P2/P3 为会话 AI |
| `各棋子势力占比Tests.真实跑局快照的类型计数与明细自洽` | 种子 10–11 不再有"匠人与协同同串"（第四次换样本）→ 脚本局：P0 落匠人 + 协同 + 普通 + 堡垒同串 |
| `终端活形与禁入标示Tests.脚本对局里出现禁入与已活标示` | 种子 1–10 / 1–40 都没有满足的局 → `PlayCommand` 加 internal 测试接缝 `arrange`（锁定后摆子，生产重载传 null），给 P1 摆一条两眼活形 |

## 变异验证

脚本：二进制读写、锚点命中恰 1 次、备份带时间戳、还原放 finally 并逐字节比对、`os.utime` 刷新 mtime；过滤运行相关 13 个测试类（277 条），抓 `Failed!/Passed!` 统计行。还原后确认 0 红 / 277 绿、退出码 0。

| 变异 | 改动 | 红 | 红的测试（节选） |
|---|---|---:|---|
| M-F1 | `EyeNeighbors` 加回 `!HasFence` | 10 | 栅栏不是墙、栅栏不切分空区、栅栏隔开的2x2仍是方四、只隔栅栏贴着…、眼边只去掉…、隔着栅栏贴眼…解除禁入、立栅隔开眼与棋子不再破坏活形、提子后恢复活形、专家前瞻记录 ×2 |
| M-F2 | 空区起点改回 `LibertyNeighbors` | 4 | 只隔栅栏贴着空区的棋串也拥有该眼空间、立栅隔开眼与棋子不再破坏活形、源码扫描守门、非专家记录 |
| M-F3 | flood 改回 `LibertyNeighbors` | 9 | 栅栏不是墙、栅栏不切分空区、只隔栅栏…、隔着栅栏贴眼…、源码扫描守门等 |
| M-F4 | 方四判定改回 `LibertyNeighbors` | 2 | 栅栏隔开的2x2仍是方四、源码扫描守门 |
| M-F5 | 眼边去掉高差一条 | 11 | 崖壁算墙、崖壁隔开的2x2不是方四、眼边只去掉…、崖壁成串 / 气等 |
| M-F6 | `Invalidate` 不作废 `_eye` | 8 | 改造派生表与从零建表逐项相同、搭桥漏眼被拒、暂放搭桥造出他人的新眼等 |
| M-P4 | 预筛去掉"不在他人眼空间里" | 1 | 与完整预演一致_暂放搭桥造出他人的新眼 |
| M-E1 | Eye 的 `CalibrationOf` 去掉 fence-eye-rule 补注 | 1 | 规则变更使校准失效 |
| M-E2 | `CalibrationStatus` 去掉补注 | 2 | 规则变更使校准失效、默认权重的校准依据随值一起更新 |
| M-E4（裁决 #4 后补） | `PassThresholdCalibrationStatus` 去掉 fence-eye-rule 补注 | 2 | 规则变更使校准失效、默认停手阈值被改动 |
| M-E3 | 评价版本退回 6 | 3 | 日志首部写入AI评价版本…、专家前瞻记录 ×2 |
| M-S1 | `PlayCommand` 不调用 `arrange` | 1 | 脚本对局里出现禁入与已活标示 |
| M-S2 | 日志 `BreaksRehearsed` 恒 0 | 3 | 真实跑局的活形字段自洽、专家前瞻记录 ×2 |
| M-C1b（只改测试） | `otherTypes` 去掉匠人 | 1 | 真实跑局快照的类型计数与明细自洽（脚本样本确实触发匠人项） |

## 待裁决

1. `SmallMapGoldenHash` 的分类：派发说"只依赖地图、必须不变"，实为 AI 决策日志；已按规则变更重钉（首个分歧可由立栅解释）。是否认可，或改为另找真正只依赖地图的反面对照。
2. capture-resolution 主规格 Scenario「立栅隔开眼与棋子被拒」：新规则下 WHEN（"使该眼不再与这条棋串相连"）不再可能发生，测试已改名 `立栅隔开眼与棋子不再破坏活形`；需主会话在本 change 增量里 MODIFIED 该 Scenario（建议："立栅隔开眼与棋子不破坏活形 → 第 6 步通过"）。
3. `MatchSession.IsTerrainSmall`（R8 贴地形小空区统计）仍按"几何邻居多于气边邻居"判，贴栅栏的小眼空间被算作"地形围出"；新规则下栅栏不造眼，口径是否改为眼边。
4. 【已裁决：补】`PassThresholdCalibrationStatus` 末尾已加"；未校准（fence-eye-rule 后）"，守门同步、M-E4 验证。原问题：`AiSearchConfig.PassThresholdCalibrationStatus` 未加 fence-eye-rule 补注（F5 只点名三维；testing.md 写"活形规则变更时停手阈值同改未校准"）。
5. `.trellis/spec/core/boundaries.md` 单一实现表：已补眼边一行并改活形行（见该文件），请主会话复核。

## 3.1 冒烟（各 10 局，Standard ×4；命令按派发原文，未加 `--serial`，默认并行 28 → 耗时受争用影响偏高）

`sim-out/fence-eye-rule/{link1,per-map}`，analyze 报告 `sim-out/fence-eye-rule/analyze-{link1,per-map}.txt`（`--review-cycles`）。对照 `sim-out/link-map-surfaces-f1f2`（同一统计脚本复算；注意其 per-map 是 `link:100` 种子 11 起、`--serial`，本次 per-map 是 `link:1` 种子 1 起）。

| 指标 | 旧 link1 | 新 link1 | 旧 per-map | 新 per-map | 旧合计 | 新合计 |
|---|---|---|---|---|---|---|
| 大回合 最小 / 中位 / 最大 | 19 / 21.5 / 32 | 25 / 26 / 31 | 21 / 26.5 / 32 | 21 / 30 / 33 | 19 / 25 / 32 | 21 / 28 / 33 |
| 截断 / 上限结算 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| 提子总数 | 502 | 802 | 649 | 798 | 1151 | 1600 |
| 改造（立栅 / 搭桥 / 烧林） | 83（83/0/0） | 128（110/15/3） | 95（92/1/2） | 136（114/19/3） | 178（175/1/2） | 264（224/34/6） |
| 致提子改造 | 19 | 31 | 31 | 42 | 50 | 73 |
| 停滞停手 D:stall（analyze） | — | 5（1 局） | — | 11（3 局） | — | 16（4 局） |
| 单小回合耗时中位 | 33 ms | 82 ms | 37 ms | 91 ms | 35 ms | 86 ms |
