# 10-14-final-countdown 实施报告

对应 `openspec/changes/final-countdown/tasks.md` 1.1–4.2（已全部勾上）。未 commit、未 `openspec archive`；批量只跑了 2.2 那一批 20 局；dotnet / Godot 全程顺序单跑。

## 1. 勾选与测试类

| task | 状态 | 测试类（`tests/Siege.Core.Tests/`） |
|---|---|---|
| 1.1 对局配置 K | 完成 | `MatchSetup/决赛倒计时配置Tests`（缺省值、负数建局报错、旧存档按不启用读取、决赛倒计时随存档往返） |
| 1.2 倒计时规则 | 完成 | `Battlefield/决赛倒计时Tests`（首轮一侧先结算即开始倒计时、首轮配对的等待者也触发倒计时、K为0不启用、K为0时链接图决策序列与改前基线逐字节相同 ×5、无人等待者不倒计时、之后出现的战场至少给满一个大回合、外部提交首轮配对后同样赋期限、决赛战场不倒计时）；`Battlefield/战场结算Tests.倒计时到期强制结算`；`InitiativeOrder/先手顺序预测_链接模式Tests.期限大回合的预测剔除将到期落败者` |
| 1.3 测试与变异 | 完成 | 同上；变异见 §5 |
| 1.4 基线重钉 | 完成 | `AiDecision/决策序列基线Tests`、`决策序列基线_链接模式Tests`（重钉说明写在类注释） |
| 2.1 Sim 记录与分析 | 完成 | `MatchTelemetry/决赛倒计时的记录Tests`（到期结算可查、首部K按记录回放、分析统计到期结算） |
| 2.2 验证批次 | 完成 | 见 §3 |
| 3.1 提示条视图模型 + 接线 | 完成 | `SettlementShow/决赛倒计时提示Tests`（倒计时提示、等待者看到期限、每个大回合只出一次、没有倒计时不出提示、无人值守时提示条零时长） |
| 3.2 Godot 自检 + 截图 | 完成 | 见 §5 |
| 4.1 HANDOFF / 设计文档 v1.25 | 完成 | — |
| 4.2 回归 | 完成 | 构建 0 警告；全量 2144 条，通过 2138，跳过 6，失败 0 |

### 实现要点

- **Core**：`MatchOptions.FinalCountdownRounds`（缺省 2，负数建局 / 恢复报错）。新文件 `Match/MatchFlow.Countdown.cs`：起点 R、当前期限表、倒计时记录 `FinalCountdownRecord`、`IsWaitingForFinal(p)`（在局、未进决赛、所在战场只剩本人，且所在战场已结算或首轮配对步骤已完成且不在待首轮配对中）、幂等的 `UpdateFinalCountdown(completed)`（K = 0 / 决赛已链接 / 非进行中直接返回；无起点且有等待者 → R = completed；给每个还没有期限的未结算多人战场赋 max(R + K, completed + 1)）。调用点两处：`EndMajorRound` 末尾（结算与 `AdvanceLinkPhase` 之后，覆盖挂接来源时当场配对与候选 ≤ 1 的情形）、`ApplyPairing`（外部提交配对，按 `MajorRound - 1`）。`ExecuteLink` 同时清掉两个旧标识的期限。`JudgeAtRoundEnd` 加第三条（c）`CountdownExpired`，排在整轮 Pass、填满之后，原因 `SettlementReason.Expired`（枚举末位追加）；先手预测复用判定、无需改动。`BattlefieldSettlementRecord.Deadline`（init 属性）。`FlowEventKind.FinalCountdown`（末位追加）。存档：K、R、期限表、倒计时记录、结算记录的期限全部放在 Link 段（旧模式存档不写任何新字段）；缺 K 的旧存档按 0 读、`FinalCountdownBackfilled` 留痕。
- **Sim**：`RunConfig.FinalCountdownRounds`（`ResolvedFor` 落成 2；回放首部缺项按 0；会话核对配置与对局一致）；`run` / `play` 新增 `--final-countdown`（`PrintUsage` 同步）；`PlayCommand.Run` 新增 `finalCountdown` 测试接缝，K > 0 时开局多打一行规则说明。日志：结算记录带 `Deadline`，新事件 `FinalCountdown`（`Countdown` 段：序号、战场、R、期限、赋期限的大回合；事件的 `MajorRound` 与配对事件同口径记已推进到的下一大回合）；`MatchLog.FinalCountdownRounds`（首部缺项按 0）。`analyze` 新段「决赛倒计时」（`FinalCountdownAnalyzer`）：K 分布与缺项计数、启动倒计时的局与 R 分布、按原因的结算分布、到期次数与所在大回合、到期时势力差（胜者 − 第二名）分位数与明细；排在「主题表现」之后、`--review-artisan` 段之前；样本里没有 K > 0 的局时整段不出现（旧报告逐字节不变）。等待者评估沿用。
- **Presentation / Godot**：`Siege.Presentation/Show/FinalCountdownNotices.cs`（文案、纯函数 `At`、按大回合去重的 `Take`；只收基本类型输入——守门 `UI层不含规则计算Tests` 禁止 Presentation 持有 `MatchFlow`）。`src/godot/scripts/MatchSession.cs` 在既有的结算接线 `WithLinkBeats` 里读对局公开状态、调 `Take`，结果进同一个 `_pendingNotices` → `NoticeQueue`（无人值守零时长；`[strip]` 日志行照打）。到期结算的横幅沿用既有「战场结算横幅」。

## 2. 链接图 5 条决策序列基线

**K = 0 对照**：改前的 5 份基线在动代码之前原样复制为 `AiDecision/Fixtures/decisions-link1-*-k0.txt`；`LinkDecisionSequence.Export` 新增 `finalCountdown` 参数（导出格式不变：首部不写 K，只有发生倒计时的局才多出 `C` 行与 `S` 行末的 `deadline=`）。`决赛倒计时Tests.K为0时链接图决策序列与改前基线逐字节相同` 对 5 条（种子 1 / 2 / 3 标准、种子 1 简单 / 困难）逐字节比对：**5 / 5 相同**。

**K = 2 重钉**（`SIEGE_REBUILD_DECISION_BASELINE=1`）：

| 基线 | 布局 | 结果 |
|---|---|---|
| 种子 1 标准 | 2+1+1（zones 3,3,1,0） | 同区战场 4（P0、P1）第 7 大回合整轮 Pass 结算、P0 胜 → R = 7，首轮战场 1（P2、P3）期限第 9 大回合；第 9 大回合结束时到期结算 P3 164 胜 P2 108，第 10 大回合起决赛。改前该战场到第 12 大回合仍未结算。第 1–9 大回合决策行逐字节不变。 |
| 种子 2 标准 | 2+1+1（zones 3,0,1,0） | 同区战场 1（P1、P3）第 8 大回合整轮 Pass 结算、P3 胜 → R = 8，首轮战场 2（P0、P2）期限第 10 大回合；第 10 大回合结束时到期结算 P0 140 胜 P2 101，第 11 大回合起决赛。改前第 12 大回合才整轮 Pass 结算（P0 144 胜 P2 108）。第 1–10 大回合不变。 |
| 种子 3 标准 | 四人四区 | 截到第 12 大回合两个首轮战场都未结算、无等待者，**逐字节不变**（文件未动）。 |
| 种子 1 简单 | 2+1+1 | 截到第 12 大回合没有任何战场结算，**逐字节不变**（文件未动）。 |
| 种子 1 困难 | 2+1+1 | 同区战场 4 第 7 大回合整轮 Pass 结算、P1 胜 → R = 7，首轮战场 1 期限第 9 大回合；第 9 大回合结束时到期结算 P2 84 胜 P3 80，第 10 大回合起决赛。改前第 11 大回合才整轮 Pass 结算（同样 P2 84 胜 P3 80）。第 1–9 大回合不变。 |

## 3. 验证批次对比

命令（只跑这一次）：`run --out sim-out/final-countdown/verify --map link:1 --map-per-match --count 20 --seed 1 --difficulty Standard`，墙钟 6.2 s，20 / 20 完成、0 截断；`analyze --dir sim-out/final-countdown/verify --out …/report.txt`（未加 `--review-defeats`）。两份 `config.json` 逐项比对只差 `"FinalCountdownRounds": 2`（权重、阈值 20、候选格上限 24、内容集 V2、截断 600 全同）。对照 = `sim-out/ai-terrain-review/themes/report.txt`（同口径、K 缺项 = 不启用）。

| 指标 | 主题批次（改前） | 验证批次（K = 2） |
|---|---|---|
| 等待者决赛胜率 | 20.0%（4 / 20） | 25.0%（5 / 20） |
| 首轮刚结算的胜者决赛胜率 | 80.0%（16 / 20） | 75.0%（15 / 20） |
| 等待大回合分布（决赛等待者） | 1×1，2×3，3×1，5×2，6×3，7×5，9×2，11×2，14×1 | 1×1，2×19 |
| 等待大回合 全员均值 / 等待者均值 / 最大 | 1.59 / 6.35 / 14 | 0.49 / 1.95 / 2 |
| 决赛开局势力差（首轮胜者 − 等待者）均值 / 中位 | 1564.4 / 418 | 233.95 / 19 |
| 决赛开局 \|势力差\| 最大 | 7636 | 3838 |
| 决赛开局势力领先者获胜 | 18 / 20 | 15 / 20 |
| 到期结算次数 | 0（全部结算 60 次均为整轮 Pass） | 16（全部 59 次：整轮 Pass 43、到期 16） |
| 到期时势力差（胜者 − 第二名） | — | 最小 10，P25 50，中位 74，P75 247，P90 833，最大 4762 |
| 倒计时起点 R 分布 | — | 3×1，5×1，6×1，7×3，8×5，9×5，10×1，11×1，12×1，14×1（20 / 20 局启动） |
| 对局结束大回合 中位 / 均值 / 最大（最小） | 17.5 / 17.95 / 24（12） | 16 / 16 / 21（10） |
| 首轮交战大回合（均值） | 11.7 | 7.3 |
| 决赛大回合（均值） | 3.25 | 5.7 |
| 首次跨出生区冲突（均值） | 7.4 | 7.35 |
| 第 3 大回合领先者最终胜率（口径 A） | 25.0% | 10.0% |

**判断**：倒计时把"等待"从中位 6、最长 14 个大回合压到 ≤ 2，决赛开局势力差中位从 418 收窄到 19（均值 1564 → 234，最大 7636 → 3838），决赛反而更长（3.25 → 5.7），整局短约 2 个大回合，都是 W1 想要的方向。等待者决赛胜率 20% → 25%、开局领先者胜 18 → 15 / 20，改善幅度在 20 局样本里不显著，首轮刚结算的胜者仍占优（同场战斗多打出的势力仍在）。到期结算出现在 16 / 20 局，是首轮第二个战场的主要收尾方式；其中 2 次势力差 ≤ 14（种子 F 差 10、种子 A 差 14），即强制结算确实会打断胶着的战场（design Risks 已列）。是否把 K 调到 1 或 3、或再加别的手段，建议负责人试玩后定（见 W10）。

## 4. 改动的既有测试（逐条）

全部带 `final-countdown：` 注释。原则：能用 K = 0 守住原期望的一律显式设 K = 0，不改期望值。

| # | 位置 | 改动 | 理由 |
|---|---|---|---|
| 1 | `LinkModeFixtures.Options` | 显式 `FinalCountdownRounds = 0`；新增 `CountdownOptions(k)`，`Started` 加可选 `options` 参数 | K = 2 缺省下 `战场结算Tests.等待者Pass不结算`（到期结算）与 `阶段推进Tests.减员触发的决赛等到大回合结束` 变红；这些期望写于倒计时之前，用 K = 0 守住 |
| 2 | `SimFixtures.PinPreCalibration` | 加 `FinalCountdownRounds = 0` | "写死引入前口径"的夹具（与已写死的内容集 v1 同理）；K = 2 下 `对局内容集Tests.v1逐步相同`、`带入带出配置Tests` 两条、`批量跑局Tests.缺省关闭带入`、`停手阈值Tests.阈值为0时零变化` 的黄金哈希变红 |
| 3 | `LookaheadFixtures.LinkConfig` | 加 `FinalCountdownRounds = 0` | 专家 G1 黄金值（`专家难度的一层前瞻Tests` 两条）与前瞻记录黄金值在倒计时之前钉下 |
| 4 | `专家前瞻的记录Tests.一层配置的记录与改动前相同` / `非专家没有前瞻记录` | 哈希前把首部的 `"FinalCountdownRounds":0,` 遮掉（另加一条 `Assert.Contains` 证明该项存在） | 写死 K = 0 后首部配置多一项；黄金哈希值不改，遮掉后逐字节相同（与既有"版本字段还原"同一手法） |
| 5 | `带入带出配置Tests.Golden` | 建局时透传 `config.FinalCountdownRounds` | 该助手复刻 `MatchSession.Create` 的建局；不透传则会话核对"配置与对局 K 不一致"直接抛 |
| 6 | `终端的带入选择弃赛与结算显示Tests.Play` | 传 `finalCountdown: 0` | K = 2 下 `弃赛命令` 的脚本在第 6 大回合前本机已到期出局，脚本失步 |
| 7 | `批量跑局Tests.批量执行并汇总` | config.json 与日志首部两处"落成项"期望多 `FinalCountdownRounds = 2` | 新的落成字段（match-setup「决赛倒计时配置」要求写进批次配置与首部），必须更新 |
| 8 | `默认评价权重的校准Tests.引用未校准维度产出的数据` | 正 / 反两处落成项期望多 `FinalCountdownRounds = 2` | 同上；反面断言不补就会因缺项而恒不等 |
| 9 | `决策序列基线Tests` / `决策序列基线_链接模式Tests` | 种子 1 / 2 / 困难种子 1 三份夹具按 K = 2 重钉；类注释写重钉说明；`LinkDecisionSequence.Export` 新增 `finalCountdown` 重载、`C` 行与 `deadline=` 后缀（只在发生倒计时时出现） | 规则有意变更（BREAKING，proposal 已列）；K = 0 对照守住改前基线 |
| 10 | `先手顺序预测_链接模式Tests`、`战场结算Tests` | 只新增测试方法与注释，既有断言未动 | — |

## 5. 构建、测试、Godot 自检、变异

- `dotnet build siege.sln -c Release`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误；Godot `--headless --build-solutions --quit` 退出码 0。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2144 条，通过 2138，跳过 6，失败 0**（改前 2118 / 2112 / 6；新增 26 条）。全量约 50–65 s。
- Godot 自检（console exe，headless，顺序单跑，均退出码 0、`ERROR` 0 行）：
  - `--auto-demo`：自检 6 条全过。
  - `--auto-demo --pick-check --seed=5`：开局一轮 + 链接后第二轮 78 / 78。
  - `--map-select --auto-demo`：新局界面五步全过，演示 6 条全过。
  - `--auto-demo --rounds=0 --seed=5`：七步齐全、13 条全过；`[strip]` 行可见接线生效——第 91 帧（第 8 大回合开始）"战场 出生区 5 已结算，胜者 紫方(P3)" + "对面战场已结算，本战场还剩 2 个大回合"，第 103 帧"还剩 1 个大回合"，第 115 帧战场 1 到期结算（`Expired`），本机横幅"战场落败（第 2 名）"自检通过。
- 截图：`art/final-countdown/countdown-strip.png` 与 `countdown-strip-gray.png`（1600×900，非 headless）：`--auto-demo --seed=5 --rounds=7 --screenshot=E:/wws/geme_demo/art/final-countdown/countdown-strip.png:290`。第 91 帧两条提示入队、第一条停留约 3 s（16 ms / 帧 × 188 帧），第 290 帧第二条"对面战场已结算，本战场还剩 2 个大回合"在顶部提示条位置；`--rounds=7` 使演示停在第 8 大回合开始。
- 变异（脚本：二进制读写、锚点按文件行尾归一且命中恰 1 次、finally 写回原字节 + `os.utime` + 逐字节校验；只跑倒计时相关类，`DOTNET_CLI_UI_LANGUAGE=en` 抓 `Failed`）：

| 编号 | 变异 | 红 |
|---|---|---|
| M-FC1 | 到期不结算（`CountdownExpired` 的 `MajorRound >= deadline` → `MajorRound < 0`） | 9 |
| M-FC2 | R 错一回合（`_countdownStart = completed` → `completed + 1`） | 13 |
| M-FC3 | 外部提交配对后按 `MajorRound` 而非 `MajorRound - 1` | 1 |
| M-FC4 | 存档不写期限表 | 1 |
| M-FC5 | 旧存档缺 K 时按 2 回填 | 1 |
| M-FC6 | 提示条剩余数不含本大回合 | 2 |
| M-FC7 | 提示条去重 `<=` → `<` | 1 |
| M-FC8 | 日志结算记录不写期限 | 1 |
| M-FC9 | 分析端势力差取反 | 1 |
| M-FC10 | 等待者判定 `已结算 \|\| 首轮步骤已完成` → `&&` | 3 |
| M-FC11 | 链接时不清旧期限 | 1 |
| M-FC12 | 回放无视首部 K、一律按 0 重建 | 1（「首部K按记录回放」） |

  变异后全量重跑：2144 / 2138 / 6，退出码 0（失败数 0，不等于任一变异的失败数）。

## 6. 需裁决事项与遗留风险

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| W7 | 构筑期结算的胜者算不算等待者 | 按规格字面算：2+1+1 若同区战场在第 1 / 2 大回合结算，R = 1 / 2，第 3 大回合结束才拼接出的首轮战场期限 = max(R + K, 4) = 4，只打 1 个大回合。另外遥测 `WaitingRounds` 只从第 4 大回合起计，两处口径不一致。验证批次 R 最小为 3（3+1 的独占者），未出现构筑期结算 | 维持；或把 R 的起算下限定为第 3 大回合（首轮战场至少打 K 个大回合） |
| W8 | K / R / 期限是否进公开快照 | 规格写 K "始终公开"，现只在 `MatchFlow`（公开属性）、存档、日志与批次配置；`MatchPublicView` / `SeatPublicView` 未加，图形版经宿主读对局；AI 不读 | 维持（AI 不改）；联网或 AI 需要时再补进公开视图 |
| W9 | 提示条灰度对比度 | 复用既有他场结算提示条样式（橙字、半透明底），灰度图里文字偏淡 | 维持（不在本 change 范围）；如需可在 `map-elements-v2` 一类视觉 change 里统一提亮 |
| W10 | K 的取值 | 验证批次势力差大幅收窄，但等待者胜率 20% → 25% 改善不显著，2 次到期时势力差 ≤ 14 | 负责人试玩后决定是否调 K（1 / 3）；本次不再跑批 |
| W11 | 终端 `play` 是否也出每大回合倒计时提示 | 只在开局多打一行规则说明；规格只要求图形版 | 维持 |

遗留与风险：
1. 验证只有 20 局，胜率类指标不可靠（报告首行已注明需 200 局）；按约定未扩样。
2. 到期结算不经 Pass，AI 不知道期限（评价与停手未改，W6）：期限大回合里落后方不会"抢分"，可能出现差距很小的到期结算。
3. 提示条与他场结算提示共用一个队列：同一帧两条时倒计时那条在约 3 s 后才出现（截图即此情形）。
4. 日志 `FinalCountdown` 事件的 `MajorRound` 记已推进到的下一大回合（与配对事件同口径），赋期限的大回合以 `AssignedInMajorRound` 为准。
5. `analyze` 的「决赛倒计时」段只在样本里有 K > 0 的局时出现；旧批次报告不变。
6. 文件改动：Core `FlagPlanting.cs`（`MatchOptions`）、`BattlefieldSettlement.cs`、`MatchPhases.cs`、`MatchFlow.cs`、`MatchFlow.Link.cs`、`MatchFlow.Persistence.cs`、`MatchFlow.Persistence.Link.cs`、新增 `MatchFlow.Countdown.cs`；Sim `RunConfig.cs`、`MatchSession.cs`、`MatchLog.cs`、`Program.cs`、`PlayCommand.cs`、`BalanceAnalyzer.cs`、`ReportWriter.cs`、新增 `FinalCountdownAnalyzer.cs`；Presentation 新增 `Show/FinalCountdownNotices.cs`；Godot `scripts/MatchSession.cs`；文档 `HANDOFF.md`、设计文档 v1.25、`openspec/changes/final-countdown/tasks.md`（只勾选）。

---

## W7（tasks 1.5，负责人 2026-10-15 裁决）

- **规则**：R = max(第一次出现等待决赛的玩家的大回合, 3)。`MatchFlow.Countdown.cs` 新增 `WaitingStartRound`（= `BuildProtectionRounds` = 3）与 `WaitingStarted(completed)`；`UpdateFinalCountdown` 在 `completed < 3` 时直接返回（构筑期内不设 R、不赋期限），到第 3 大回合结束时等待者仍在，R 即取 3。
- **口径统一**：遥测 `WaitingRounds` 原判据是 `completed > BuildProtectionRounds`，口径本就一致（第 4 大回合起计）；改为调用同一个 `WaitingStarted(completed - 1)`（逻辑等价），两处共用一处判定，不再各写一份。`IsWaitingForFinal` 仍照实回答（构筑期结算的胜者此刻就是等待者），只是起点不早于 3。
- **副作用**：K ≥ 1 时 R + K ≥ 4 = 首轮拼接大回合 + 1，`max(R + K, 出现大回合 + 1)` 的"至少给满 1 个大回合"分支已不可达（代码保留，设计文档 §4.3 删去该说法、补 W7）。
- **测试**：`Battlefield/决赛倒计时Tests` 中原「之后出现的战场至少给满一个大回合」（同区第 1 大回合结算 → R = 1、期限 4，正是 W7 否决的情形）替换为规格新 Scenario「构筑期结算按第3大回合起算」：[0,0,3,4]、同区第 2 大回合整轮 Pass 结算 → 构筑期内 R 为空、胜者 `WaitingRounds` 为 0；第 3 大回合结束拼接后 R = 3、首轮战场期限 5；第 4 大回合结束不结算、胜者 `WaitingRounds` = 1；第 5 大回合结束到期结算。「外部提交首轮配对后同样赋期限」的期限期望 4 → 5、加 R = 3 断言（本 change 自己的新测试，随裁决更新）。全量条数不变。
- **变异**：M-FC13 去掉 R 的下限（删早退）→ 红 2（「构筑期结算按第3大回合起算」「外部提交首轮配对后同样赋期限」）；脚本还原后逐字节校验并刷新 mtime。
- **基线**：K = 0 对照 5 / 5 仍逐字节相同；K = 2 的 5 份基线全部不变（R 为 7 / 8 / 7，不受下限影响），无需重钉。
- **回归**：`dotnet build siege.sln -c Release` 0 警告 0 错误；全量 2144 条，通过 2138，跳过 6，失败 0。Godot 侧代码未改、自检未重跑（`--auto-demo --rounds=0 --seed=5` 的 R = 7，不受影响）。
- 文档：设计文档 §4.3 补"R 不早于第 3 大回合（W7）"并删去已不可达的"至少给满 1 个大回合"；HANDOFF 规则速览同步一句；tasks.md 勾 1.5。
