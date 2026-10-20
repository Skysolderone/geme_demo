# 10-20-cycle-stop-2 质检记录（trellis-check，2026-10-20）

范围：cycle-stop 两段全部未提交改动（HEAD 7ae2f65）。只记修复与结论；不提交、不归档。

## 修复 / 补充

1. `tests/Siege.Core.Tests/Battlefield/战场结算Tests.cs` `上限结算`：补对照断言——第 59 大回合"假设此刻结束"未到上限，先手预测照常给出下一轮（三名在局者都在）；第 60 大回合的 `NextOrderForecast == null` 注释标明它就是规格"先手预测复用同一判定，上限大回合同样剔除将落败者"的守门（唯一战场上剔除落败者即战场结算终局，null 是唯一可观察形式）。不另写重复测试。
2. `src/Siege.Core/Ai/AiDifficulty.cs` `DefaultPassThreshold` 的 XML 注释末尾补"校准与复核数据都在 K = 0 下取得、阈值 20 未在 K = 6 下重扫"一句（裁决项 4：只改注释，常量与 `PassThresholdCalibrationStatus` 字符串不动；`默认停手阈值被改动` 守门仍绿，见全量）。
3. `openspec/changes/cycle-stop/tasks.md` 2.4 末尾加"（实跑种子 1–2，≤ 10 局约束）"，规格正文未动。
4. `HANDOFF.md` 后续 change 加 1c"停滞记忆不入存档"观察项（见下文第 3 项 c）。

## 逐项结论

- 质检项 2（安全网 A）：第 (c) 条只在 `JudgeAtRoundEnd`，排在 (a)(b) 之后；`ForecastInitiative` 调同一函数；`RoundCap > 0` 门；`members.Length < 2` 先 continue；终局原因不变（`上限结算` / `上限结算即终局` 断言 `LastBattlefieldSettled`）。
- 质检项 3（停手 B 的信息边界）：
  - a) 占用差分误判：`GameBoard.Place` 要求空格（不会"覆盖"），`RemoveStones` 只在 `SettlementDriver`（提子）、`BatchRehearsal`（预演副本）、`SeatProjection.StripOutside`（投影副本；唯一战场 = 全图，无可剥离）三处；地形改造不改占用者。"有子 → 空 / 换主人"只能来自提子，不会误判。同主人被提又补回同格看不出（漏计，保守方向）。
  - b) 采样错位：`MatchRunner` 每小回合都调 `Deploy`（Pass 也在 `Deploy` 内决定），`Deploy` 入口在任何提前返回之前就 `ObserveForStagnation`；即便某大回合缺采样，`StagnationHolds` 要求 K + 1 个连续采样才成立，缺口只会让停手不触发（保守方向）。本人上一次部署造成的提子在下一次观察时归档，窗口最多偏一个大回合。
  - c) 存档：记忆不入存档。`MatchFlow.Restore` 在 `src/`（含 Godot）下无调用方，没有中局恢复入口；回放（`replay`、`LogReplay` 驱动的 `--review-defeats` / `--review-artisan`）从第 1 小回合重建控制器，记忆随之重建。实证：K = 6 的 5 局日志（简单 0x2C / 0x2D / 0x2E、标准 1 / 2）`analyze --review-defeats` 回放无分歧、复盘一致 6/6 + 6/6；`战场落败复盘Tests.落败者复盘可查` 的真实局里含停滞停手 Pass，回放同样逐小回合核对通过。存档续跑测试都用脚本控制器，不涉及 K。登记为 HANDOFF 1c 与裁决项（不在本次实现持久化）。
  - 信息边界守门：`正式对战AI的信息边界Tests` 的类型闭包从 `HeuristicTurnController` 出发（含新字段类型），源码扫描覆盖 `src/Siege.Core/Ai/*.cs` 全部文件，新代码在内。
- 质检项 4（`ReviewBestMove`）：`PreviewStagnation` 在记忆副本上走同一 `ObserveForStagnation` + `StagnationHolds`，finally 还原三份记忆；传入的前 N 候选与 `Deploy` 的 `RankPoints` 同一 `ScorePoints` + `Rank` + `Take(N)`。`复盘不改停滞记忆` 在全量里执行。
- 质检项 5（基线与哈希）：5 份 `decisions-link1-*.txt` `git diff --quiet HEAD` 全真。抽查 `平衡分析方向Tests` 两份样本：临时（二进制备份 + finally 还原 + `os.utime`）把 `Hash` 改为先删掉"- 战场结算原因（cycle-stop"一行（断言恰 1 行）、期望改回 HEAD 的 `8F6ED482…` / `5CBE36F0…`，`内容集v1的报告除第12项外钉值` 绿——重钉只因报告多一行。`专家前瞻的记录Tests` 两份整局黄金值本就按遮罩比对未改。
- 质检项 6（兼容）：旧存档回填 100 + `RoundCapBackfilled`（`大回合上限配置Tests.旧存档按缺省读取`）；旧日志缺 `RoundCap` 分析按 100（`上限结算可查`）；回放缺项按 100（`MatchSession.Create`）——另：AI 评价版本门（5 → 6）让所有现存旧日志在回放首部即被拒，这条回填路径对现存日志不可达，`boundaries.md`"回放不分歧"的前提因此是双重保障。第一段版本 5 的日志：`analyze --dir sim-out/cycle-stop/easy` 退出 0、能读（链接模式段"AllPassed×8；上限结算 0 次"）；`replay --file …/match-000000000000002C.jsonl` 退出 4，提示"回放在首部（第 1 行）分歧：AI 评价版本不一致。日志首部记录的 AI 评价版本为 5，当前为 6——旧版本的 AI 决策不能用当前评价逐步重现；未重跑对局。"
- 质检项 7（`--max-rounds` 文案）：新文案仍含"--max-rounds 已删除"与"restore-go-core-rules"，`已删除的选项报已删除` / `配置文件里的已删除配置项被拒绝` 等在全量里执行。
- 质检项 8（文档）：设计文档 diff 逐段对照规格一致（§12.3 第 4 条、§12.4、§15.2、§17、§19 第 16 / 18 项、变更记录）；HANDOFF `--round-cap` / `--stagnation-rounds` 实跑 1 局小样本（`run --round-cap 3 --stagnation-rounds 2 --turn-limit 40 --count 1`）退出 0，`config.json` 写 3 / 2，日志结算原因 `RoundCap`，`analyze --review-cycles` 退出 0、出"上限结算 1 次"与循环提子诊断段。

## 验证

- 质检项 1（规格对照）：cycle-stop 新增 / 改写的 Scenario 全部有同名测试——battlefield 3（`上限结算` / `上限与整轮Pass同时满足记整轮Pass` / `上限为0不启用`）、elimination-endgame 1（`上限结算即终局`）、match-setup 4（`大回合上限配置Tests` 四条）、match-telemetry 1 + 3（`上限结算可查`；`循环提子诊断Tests` 三条）、ai-decision 6（`循环提子停手Tests` 六条同名 + `盘上无子不触发` / `采样与被提格由公开量推导` / `复盘不改停滞记忆`）。MODIFIED 需求里沿用的旧 Scenario（如「多名落败者按判定链排」↔ `三人战场结算`）与「小回合内减员同时触发结算按只剩一名记录」↔ `只剩一名优先于最后战场结算`，Scenario 来自 retire-legacy-rules、测试名来自 open-map 段 A）是 cycle-stop 之前留下的命名差异，按注释可追溯，不属本 change。
- 边界说明：`--review-defeats` 只复盘整轮 Pass（AllPassed）结算的落败者（`DefeatReplay.TargetsOf`），上限结算的落败者不在分母里；"复盘一致 6/6 + 6/6"的样本是 5 局（简单 0x2C / 0x2D / 0x2E + 标准 1 / 2），不是第二段全部 10 局。5 份基线不变是因为基线截到第 12 大回合、K = 6 最早第 7 大回合才可能触发且需前 N 候选全是提子；停手确实生效的证据是 `落败者复盘可查` 真实简单局里的停滞 Pass 与 verify.md 的简单局最长 94 → 74。
- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release --no-build`（全量一次，后台）：总 2051、通过 2045、跳过 6（均为 Perf / Slow）、失败 0，退出码 0，2 m 37 s。
- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误。
- Godot headless 四条（顺序单跑）：`--auto-demo` 0；`--auto-demo --pick-check --seed=5` 0；`--map-select --auto-demo` 0；`--auto-demo --rounds=0 --seed=5` 0（第 26 大回合终局，`[flow]` 7 条 0 失败）。
- 本次跑局 / 回放：新跑 1 局（`--round-cap 3`，40 小回合截断）；`--review-defeats` 回放 5 局既有日志；无批量。
