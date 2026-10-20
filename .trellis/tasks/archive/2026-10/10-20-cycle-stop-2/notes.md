# 10-20-cycle-stop-2 notes（第二段 2.1–2.5）

## 2.1 规则层安全网 A（done）

- `MatchOptions.RoundCap`（缺省 `DefaultRoundCap = 100`，`init`），`MatchFlow.RoundCap` / `RoundCapBackfilled`，建局与恢复共用 `RequireValidRoundCap`（负数 `ArgumentException`；恢复路径包成 `FormatException`）。
- `SettlementReason.RoundCap`（追加在末尾，按名序列化）；`JudgeAtRoundEnd` 第 (c) 条 `Options.RoundCap > 0 && MajorRound >= Options.RoundCap`，排在整轮 Pass / 填满之后；`ForecastInitiative` 自然复用（上限大回合"假设此刻结束"返回 null = 没有下一轮）。终局原因不新增。
- 公开：`MatchPublicView.RoundCap`（init 属性，`Publish` 写入，`PublishSeatProjection` 的 `with` 自然带过；手写视图缺省 0）。
- 存档：`MatchSaveData.RoundCap`（int?），新存档恒写；旧存档缺字段按 100 回填 + `RoundCapBackfilled`（C4 / D4，与 K 按 0 相反）。
- 批次 / 日志：`RunConfig.RoundCap`（int?，`Validated` 非负，`ResolvedFor` 落成 100 进 config.json 与首部 `Config.RoundCap`）；`MatchSession.Create` 建局 `config.RoundCap ?? 100`（按旧日志回放时缺项也按 100——design D2"旧日志按 100 读"；旧日志最长 94 大回合，到不了上限，回放不受影响）；`MatchLog.RoundCap => Header.Config.RoundCap ?? 100`。**没有**另加 `LogHeader.RoundCap`（advisor：会让旧日志回放首行多一项）。
- CLI：`run --round-cap`；`--max-rounds` / `MaxMajorRounds` 的"已删除"文案改口指向 `--round-cap` / `RoundCap`（原文说"只剩三类终局"已过时）。
- 分析：`LinkModeSection.SettlementReasons`（按结算原因计数）+ 报告行"战场结算原因 … 上限结算 N 次"（链接模式一段多一行；无整文本哈希测试受影响）。
- 文案：`Labels.Settlement(SettlementReason)`（整轮 Pass / 填满 / 减员 / 上限），终端结算行带原因。
- 过时注释改口：`MatchOptions`、`EndMajorRound`、`FlowEventKind.BattlefieldSettled`、`MatchPublicView` 类注释。
- 测试（同名 Scenario）：`Battlefield.战场结算Tests` 上限结算 / 上限与整轮Pass同时满足记整轮Pass / 上限为0不启用；`MatchSetup.大回合上限配置Tests` 缺省值 / 负数报错 / 旧存档按缺省读取 / 不启用；`EliminationEndgame.两类终局条件Tests.上限结算即终局`；`MatchTelemetry.对局日志的记录内容_链接模式Tests.上限结算可查`（含分析端计数、报告行、旧日志缺项按 100 读）。
- 样本口径教训：三人上限结算夹具初版用 `[0,0,3,4]` 让 P0 多落 6 子，把同棋盘的 P1 提光（势力归零出局）→ 改四人四棋盘 `[0,1,3,4]`，P0 的子都落在自己核心区。
- 变异验证（实跑，脚本 `%TEMP%/.../mutate.py`：二进制备份、finally 还原、还原后 `os.utime` 刷新 mtime、终了重建 rc 0）：
  - M-RC1 去掉第 (c) 条：红 4（上限结算、上限结算即终局、旧存档按缺省读取、上限结算可查）
  - M-RC2 `>=` 改 `>`：红 4（同上）
  - M-RC3 第 (c) 条挪到整轮 Pass 之前：红 1（上限与整轮Pass同时满足记整轮Pass）
  - M-RC4 去掉 `RoundCap > 0`：红 3（上限结算即终局、上限为0不启用、不启用）
  - M-RC5 会话建局不写 RoundCap：红 1（缺省值——靠"配置 7 → 对局 7"的非缺省值断言；初版只断言缺省 100 时是等价变异，已补）
  - M-RC6 `Serialize` 不写 RoundCap：红 3（旧存档按缺省读取、不启用、缺省值）
  - M-RC7 `RestoreCore` 缺字段按 0 回填：红 1（旧存档按缺省读取）
  - M-RC8 `RequireValidRoundCap` 不抛：红 2（旧存档按缺省读取、负数报错）

## 2.2 AI 停手 B（done）

- `AiSearchConfig.StagnationRounds`（构造缺省 0 = 旧记录按 0 读，为 0 不写出；`DefaultStagnationRounds = 6`，Easy / Standard / Hard 预设显式带 6，Expert 经 `Hard with` 继承；`Validated` 非负）。
- `RunConfig.StagnationRounds`（int?，`ResolvedFor` 落成 6，并进专家落成的 `Search`）；`MatchSession.StagnationRounds = Config.StagnationRounds ?? (recorded ? 0 : 6)`，`AttachConfigured` 的 `with`；CLI `run --stagnation-rounds`。Godot / 终端经 `AiSearchConfig.ForMap` 自动带 6（四档共用）。
- `HeuristicTurnController`：
  - 记忆只记本人、只读公开量：`_powerByRound`（每个大回合**本人首次部署决策**时采样 `view.Power.Of(Player).Total`）；`_capturedByRound`（相邻两次观察之间"有子 → 空或换主人"的格，按观察所在大回合归档——棋子只会因提子离开盘面，判据精确；同一主人两次观察之间被提又补回同格看不出，保守不计）。**没有**给 `MatchPublicView` 加提子历史（advisor 建议 (a)：不动 Core 视图与存档）。
  - 判定 `StagnationHolds`：无子豁免（`GroupsOf(Player).IsEmpty`）→ 需要 K + 1 个采样（本大回合及之前 K 个）且最近 K 个逐一 ≤ 前一个（与 `CycleAnalyzer.StagnationOf` 同口径，只差采样点：那边是上一大回合结束时，这边是本人该大回合首次决策时）→ 排序前 N 的候选每个 `Captures > 0`（`PointScore` 新增 `Captures`，唯一构造点 `ScoreOne` 取预演提子数）或落在最近 K 个大回合（本大回合及之前 K − 1 个）观察到被提过的格。
  - 入口：`ObserveForStagnation` 在 `Deploy` 开头（K ≤ 0 直接返回：不采样不记占用）；停滞成立 → 不组批不扰动，`choice` = 空批次（一次额外的 Pass 预演），`LastCandidates = []`，留痕 `LastStagnation`（`StagnationStop` 记录）；随后既有的决胜一手检查照常跑（决胜一手优先）。候选生成中段原样抽成 `ChooseByCandidates`。K = 0 路径零额外预演、零额外随机消费。
  - `ReviewBestMove`（复盘）不经观察入口，不改记忆；`ForSimulation` 的模拟对手每次新建、无记忆（前瞻里不触发）；控制器记忆不随存档走（Godot 恢复后从零起算；现在没有中局恢复入口）。
- AI 评价版本 5 → 6（`EvaluationBreakdown.Version`）；回放会在首部拒绝版本 5 的日志（含第一段的 9 局，预期内——只 `analyze`）。
- 既有夹具按 boundaries.md"写死旧口径"：`LookaheadFixtures.OneLayerConfig` 加 `StagnationRounds = 0`（G1 黄金值与记录字面量不随预设改动）；`SimFixtures.PinPreCalibration` 未动（其样本 ≤ 4 个大回合，K 不可能触发）。
- 期望值 / 格式类重钉：`难度分级Tests`（预设含 K 6）、`候选格上限Tests.剪枝参数的文本往返`（旧文本按 0）、`专家难度的一层前瞻Tests` 两条（预设序列化多 `"StagnationRounds":6`，另加 K = 0 三行证明按 0 的记录与改动前逐字节相同）、`默认评价权重的校准Tests.引用未校准维度产出的数据`（config.json 多 RoundCap 100 / StagnationRounds 6）、`启发式评价维度Tests` 版本 6。
- **5 份 `decisions-link1-*` 基线逐字节不变**（`决策序列基线Tests` / `决策序列基线_链接模式Tests` 全绿，未重钉）：截到第 12 大回合时标准 / 简单 / 困难都没有"连续 6 个大回合无净增 + 前 N 候选全是提子 / 反提"的局面（K 要 7 个采样，第 7 大回合起才可能触发）。无需 `-k0` 副本：K = 0 对照直接与现基线比。
- 测试 `AiDecision/循环提子停手Tests`（`[Collection(ConsoleRedirect.Collection)]`，含 RunMain）：六条同名 Scenario（连续无净增且候选全是提子则停手 / 势力仍在增长不停手 / 有非提子候选不停手 / 决胜一手优先 / K为0零变化 / 配置进入记录）+ 盘上无子不触发 + 采样与被提格由公开量推导（真实流程：第 5 大回合采样、提走角上子、第 6 大回合差分出被提格）。K = 0 对照 = `LinkDecisionSequence.Export(... configure: c => c with { StagnationRounds = 0 })` 与基线 `decisions-link1-seed1.txt` 逐字节相同 + 同一停滞局面 K = 0 不停手。
- 信息边界：`正式对战AI的信息边界Tests` / `_链接模式Tests` 全绿（类型闭包新增 `StagnationStop` / `SortedDictionary<int, BigInteger>` / `Dictionary<Coord, PlayerId>`，不含禁用类型；源码扫描 `TrueContents(` 无命中）。
- 变异验证（实跑，同脚本）：
  - M-ST1 去掉候选条件：红 1（有非提子候选不停手）
  - M-ST2 势力比较 `>` 改 `>=`：红 3（连续无净增且候选全是提子则停手、有非提子候选不停手、决胜一手优先）
  - M-ST3 停滞成立时跳过决胜一手：红 1（决胜一手优先）
  - M-ST4 K = 0 也进入判定：红 1（K为0零变化）
  - M-ST5 去掉无子豁免：红 1（盘上无子不触发）
  - M-ST6 采样恒为 0 且 K = 0 也采样：红 2（K为0零变化、采样与被提格由公开量推导）

### 2.2 续（会话中断后补齐，2026-10-20）

- **复盘缺口（已补）**：首轮全量里 `战场落败复盘Tests.落败者复盘可查` 红（复盘一致 2/3）——真实简单局（四人四区种子、跑到终局）里有一名落败者是"循环提子停手"Pass 的，而 `ReviewBestMove` 不知道停滞判定，按"最好一手收益 > 阈值"判成不该 Pass。改：`ReviewBestMove` 在本人停滞记忆的**副本**上走同一观察 + 判定（`PreviewStagnation`，finally 原样还原三份记忆），结果放进 `BestMoveReview.Stagnation`，`ImpliesPass` 加 `|| Stagnation is not null`。这同时是 K = 6 在真实对局里确实触发的证据。新守门 `循环提子停手Tests.复盘不改停滞记忆`。
- 首个红断言修好后暴露的后续断言（testing.md「红测变绿后必须整段复审」）：`批量跑局Tests.批量执行并汇总` 第二处首部期望补两项；`平衡分析方向Tests` RankedSample 报告哈希重钉（报告多一行"战场结算原因"，格式变化：样本 ≤ 4 个大回合，K 与上限都触发不到）——Sample 8F6ED482… → D9D33978…、RankedSample 5CBE36F0… → 8FB438E5…；`专家前瞻的记录Tests` 两条整局黄金值**不改**，改为比对前遮掉首部新增的 `RoundCap` / `StagnationRounds` 两项、版本 6 还原为 3（`PreCycleStop`）——哈希与改前相等，证明标准 × 4 整局（80 小回合）与一层专家整局（84 小回合）在 K = 6 下逐字节同走法。
- 追加变异（实跑）：M-RV1 `ImpliesPass` 不计停滞 → 红 1（落败者复盘可查）；M-RV2 复盘预演不还原上次占用 → 首次 0 红（「复盘不改对局」只跑 13 小回合，K 触发不到——不是等价变异，是覆盖缺口），补 `复盘不改停滞记忆` 后红 1；M-RV3 不还原采样 → 红 1（同条）。
- **全量（2.1 + 2.2 + 2.3 的 Core/Presentation 部分，复盘补丁后）**：`dotnet build siege.sln -c Release` 0 警告 0 错误；`dotnet test` 总 2050、通过 2044、跳过 6、失败 0，**退出码 0**（3 m 43 s；第一段收尾为 2030 / 2024 / 6）。另加「复盘不改停滞记忆」后本类 9/9 绿（总数应为 2051，2.3 后再跑一次全量确认）。
- **5 份 `decisions-link1-*` 基线：K = 6 下逐字节不变，未重钉**（`决策序列基线Tests` / `决策序列基线_链接模式Tests` 绿，基线文件 git 无改动）。原因：截到第 12 大回合，K = 6 需要第 r − 6 … r 七个采样且连续 6 个大回合无净增，同时前 N 候选全是提子 / 反提——五份序列里没有这样的局面。K = 0 对照 = `LinkDecisionSequence.Export(... configure: StagnationRounds = 0)` 与 `decisions-link1-seed1.txt` 逐字节相同（`循环提子停手Tests.K为0零变化`）。
- **AI 评价版本：6**（5 → 6）。

## 最终验证（2.1–2.5 全部完成后）

- `dotnet build siege.sln -c Release`：0 警告 0 错误（BUILD RC 0）。
- `dotnet test tests/Siege.Core.Tests -c Release`：总 **2051**、通过 **2045**、跳过 **6**、失败 **0**，**退出码 0**（3 m 22 s）。
- Godot Debug 构建 0 警告；四条 headless 自检 RC 0 / 0 / 0 / 0。

## 2.3 表现层 / 终端（done）

- 条件与文案只在 `Siege.Presentation.Text.Labels`：`RoundCapNotice(roundCap, majorRound)`（上限 > 0 且 `上限 − 大回合 ≤ 5` 才给"第 X 大回合结束时按势力结算（还剩 M 个大回合 / 本大回合即上限）"，否则 null；`RoundCapNoticeWindow = 5`）、`RoundCapStatus`（终端"上限 N（还剩 M）" / "上限 不启用"）、`Settlement(SettlementReason)`（整轮 Pass / 填满 / 减员 / 上限）。
- Godot（open-map 删了 `NoticeQueue`，最小接线）：`MatchSession.RoundCapNotice` 在每次 `Rebuild` 时按公开视图的 `RoundCap` / `MajorRound` 取（终局为 null）——文字只在大回合变化时变，即"每个大回合开始提示一次"；`Hud` 把它接在现有状态提示标签前面（同一条 `_notice`，不打断操作、无演出，无人值守零时长）；`GameRoot.RefreshViews` 文字变化时打一行 `[round-cap]` 留痕。新局界面不加配置项（S4）；上限结算沿用终局横幅 / 终局面板（`EndReason` 不变）。
- 终端：`BoardRenderer.Status` 大回合开始行加 `Labels.RoundCapStatus`；`PlayCommand` 战场结算行带原因（"1 号战场结算（上限）：…"）。
- 测试 `Battlefield/大回合上限提示条Tests`：临近上限才提示 / 提示读公开视图的上限 / 结算原因文案。变异 M-RC9 窗口 `>` 改 `>=`：红 2；M-RC10 上限 0 也提示：红 2。
- Godot：`dotnet build src/godot/Siege.Godot.csproj`（Debug）0 警告 0 错误。四条 headless 自检顺序单跑（`..._console.exe --headless --path src/godot -- …`）：`--auto-demo` RC 0；`--auto-demo --pick-check --seed=5` RC 0；`--map-select --auto-demo` RC 0；`--auto-demo --rounds=0 --seed=5` RC 0（第 26 大回合整轮 Pass 终局，`[flow]` 7 条 0 失败，四步齐全）。四条都没到上限 95 大回合，`[round-cap]` 行未出现——提示条的条件与文案由上面的 Presentation 测试守，Godot 侧只验证了编译与不破坏既有流程（没有给图形版加 `--round-cap` 之类的入口，S4 不加配置项）。

## 2.4 验证（done）→ `verify.md`

- 10 局（简单 × 8 种子 40–47 + 标准 × 2 种子 1–2），单进程顺序，退出码全 0。上限结算 0；简单：循环 1 → 0、拖延 4 → 1、中位 65.5 → 58、最长 94 → 74、截断 0；标准种子 1–2：R20 / R20，与冒烟相同。

## 2.5 文档（done）

- 设计文档 v1.28 → v1.29：文首版本；§12.3 第 4 条"上限（安全网）"；§12.4 改口"MUST NOT 以固定轮数作为正常终局，上限只是安全网"；§15.2 新增"循环提子停手"段（K = 6 依据、只读公开量、K = 0 逐步相同、复盘同一判定、版本 6、基线不变）；§17 结算原因加"上限"、首部记 RoundCap / StagnationRounds、`--review-cycles`；§19 第 16 项 R-3 标已处理并记诊断 / 验证数据，新增第 18 项"改造让盘面常新"（D1）；变更记录一行。只按章节号定位改相关段落（脚本锚点替换），没有整文件读。
- HANDOFF：头注、`run --round-cap / --stagnation-rounds`、`analyze --review-cycles`、规则速览 v1.29（结算第 (c) 条、停滞停手、版本 6）、后续 change 第 1 项 R-3 已处理 + 1a 改造常新观察 + 1b 停手无逐次留痕、测试现状。
- `.trellis/spec/core/boundaries.md`：在"对局配置项的缺省值与缺项回填值"一行之后加 cycle-stop 一行（上限缺项按 100 是裁决例外、K 缺项按 0、判定唯一实现位置、夹具写死旧口径）。

## 冲突 / 偏差记录

1. 回放缺项口径：design 只写"旧日志缺字段分析按 100 读"，没写回放；按 advisor 建议回放也按 100（`MatchSession.Create` 一处），理由见 2.1。`boundaries.md` 的"缺项 MUST NOT 走缺省值"对本项是负责人裁决的例外（C4 / D4），2.5 在 boundaries.md 登记。
2. 2.4 的 D4 效果指标退化：第一段诊断截断本就是 0，"上限结算次数明显低于截断次数"无从比较（见 verify.md 结论）。
3. 停滞停手没有日志留痕（决策行仍是 `D:pass`）：2.4 数不出触发次数；不在本段加日志字段（范围外），登记为 HANDOFF 后续 1b。
4. Godot 自检覆盖不到提示条出现的时刻（四条都在 95 大回合之前结束）；不加图形版配置入口（S4），由 Presentation 测试守。
