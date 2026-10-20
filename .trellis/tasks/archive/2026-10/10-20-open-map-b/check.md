# 10-20-open-map-b 质检记录（trellis-check，2026-10-20）

对象：段 B（tasks 2.1–2.5）全部未提交改动（约 118 修改 + 41 删除 + 3 新增）；依据 `openspec/changes/open-map/`（proposal O1–O8、design D1–D10 / A1–A8 / B1–B10、13 份规格差分）、`.trellis/spec/core/`、实施记录 `notes.md` / `tests-diff.md` / `skip-list.txt` / `deleted-tests.txt`。
硬约束遵守：未 commit / archive、未改 `openspec/` 正文、未动 `VisualBaseline.cs` 与 SettlementShow 飘字测试；`ShowTimeline.cs` 只删死分支（见 8）；同一时间只跑一个 dotnet / Godot；没有跑批量（最大一次为测试里的 3 局）。

## 质检项逐条结论

1. **删除范围对照 B1：通过（修了 4 处残留注释）**。Grep `Pairing|PairingKind|LinkRecord|LinkStage|FinalCountdown|Countdown|Waiter|WaitingRounds|ApplySplice|SeatVision|pairing-seed|--links` 在 `src/`（含 `src/godot/scripts/`）只剩：B2 拒绝表字面量（`MatchFlow.RetiredLinkFields`、`MatchLog.RetiredConfigFields / RetiredEventFields / RetiredLinkEventTypes / RetiredLinkFields`、`RunConfig.RetiredKeys`、`CommandLine.RetiredOptions`）与两条"已删除"注释（`HeuristicAi.cs`、`Program.cs`）——拒绝要点名字段，字面量必要。`tests/` 只剩拒绝守门用例的字段字符串与"已删除"注释；另有 3 处旧口径注释（不是"已删除"字样）被我改掉：`决策序列基线_链接模式Tests.cs` 第 12 / 20 行（`<see cref="ScriptedPairingSource"/>` 与 MC-27 变异描述）、`链接模式对局记录Tests.cs` 第 10 行（`PairingRecord` / `LinkRecord` cref）、`各入口支持生成图_链接模式Tests.cs` 第 13 行（"批量配对可重复 / 回放重建链接 / 模拟链接"）；`候选格上限Tests.cs` 第 39 行同一成员两个 `<summary>` 合并为 `<para>`（测试工程不生成 XML 文档，所以失效 cref 没报警）。B1 保留项仍在：`Battlefield` / `Battlefields.Of`、`EndReason.LastBattlefieldSettled`、`MatchFlow.PublishSeatProjection` / 按座位投影。
2. **B2 旧存档 / 日志拒绝：通过（补 1 处嵌套配置拒绝）**。恢复：`MatchFlow.Persistence.cs` `Parse` 第一句即 `RejectRetiredLinkFields(json)`，先于反序列化、`RequireLinkModeField` 与地图摘要比对；回放 / 离线解析：`MatchLog.Parse` 逐行 `FindRetired` 先于反序列化与首部比对，`replay` / `analyze` 共用。固定位置清单对照真数据：段 A 存档旧 `LinkSaveData`（`git show a2bb3a4`）顶层字段 FirstPairingStepDone / FinalLinked / PendingPairing / Pairings / Links / FinalCountdownRounds / CountdownStart / Deadlines / Countdowns 全部在 `RetiredLinkFields`，嵌套 `Settlements[*].Deadline` / `Players[*].WaitingRounds` 由 `NestedRetired` 覆盖；段 A 真日志 `sim-out/open-map/smoke/link1/match-0000000000000001.jsonl` 用 python 统计全部键：首部 `Config` 含 `FinalCountdownRounds` / `SeatVision`（命中 `RetiredConfigFields`）、快照 `PlayersState[*].WaitingRounds`（命中）、事件类型集合里没有三种旧事件、`BattlefieldSettled.Settlement` 无 `Deadline`（有也命中）。守门测试：`对局存档Tests.含拼接字段的旧存档被拒` 9 行 + 嵌套、`可复现回放Tests.旧拼接日志被拒` 7 行（`MatchLog.Parse` + `replay` + `analyze`）、`批量跑局Tests.配置文件里的已删除配置项被拒绝` 3 行；实施方变异 M-B2a / M-B2b 有记录。**盲点修复**：`boundaries.md` 旧行"`Players[].Search.SeatVision` 写进配置文件即报错"原由 `RejectSearchVision` + `全知仅作显式配置` 守，二者随开关删除后 `RunConfig.JsonOptions` 没开 `UnmappedMemberHandling`，`{"Players":[{"Search":{"SeatVision":false}}]}` 会被静默忽略（违反 testing.md"静默忽略的输入"）——在 `RunConfig.FromJson` 顶层扫描之后加 `Players[*].Search.SeatVision` 点名拒绝（大小写不敏感，文案复用 `RetiredKeys["SeatVision"]`），新增 `批量跑局Tests.玩家搜索配置里的视野开关被拒绝`（含反面：Search 合法键照常）。R-B4：`可复现回放_链接模式Tests.replay命令拒绝旧日志`（命令级、非 `link:` 地图标识）确无同名替代，原方法体原样搬进 `可复现回放Tests.replay命令拒绝旧日志`（合成日志、`LinkSimFixtures.RunMain`）。
3. **B3 / B4 测试对账：通过**。`skip-list.txt` 140 条按类分组计数与 `git grep -c "open-map 段 B 删除" a2bb3a4 -- tests` 逐文件计数完全相同（39 个文件）；段 A check.md 的 147 = 7 Perf/Slow + 140 对得上。删 204 = Skip 139（`主题表现可查` 按 B6 恢复）+ 搬迁 21 + 随功能删 42 + Theory→Fact 2，新增 50，2032 − 64 + 50 + 1 = 2019 通过数吻合。"随功能删 42 条"逐条看：K0 / 全知副本 ×10、全知配置 ×5、全知分流 / 显式配置 / 可关 / 旧日志按全知 / 缺省不写出 / 关闭全知评价 ×6（全部只测 `SeatVision`）、`专家单次部署耗时…`（`git show` 确认 `Measure(seatVision: false)` 全知口径，按座位视野那条仍在）、`图形版配对来源Tests` ×4、`他场结算提示条队列Tests` ×5（`NoticeQueue` 两个生产者都删）、`同战场不遮蔽`（`Conceal` 删除；正向覆盖在 `始终公开的信息Tests` 的 `PlayerPowerRowView` 用例）、`非链接档不可增长`、`链接图建局…挂配对来源`（同名替代 `链接图建局为链接模式`）、`map子命令的链接参数校验` ×6 与 `命令行配对种子写入配置记录`（替代 `配对与链接相关选项已删除` ×4）、`配对来源不是对局子流`（替代 `配对来源已随拼接规则删除`）——没有可疑项。搬迁 21 条：用脚本按代码 token（去注释）比对旧 / 新方法体：16 条逐字相同；差异只有删 `Assert.Empty(match.Pairings) / Links / PendingPairing`（成员已删）、去 `new ScriptedPairingSource()` 实参、`Scoring.PlayerStatus` → `PlayerStatus`（using）；`第4大回合不发生任何事件` 额外新增"事件种类只剩五种"断言（强化，偏离 B3 字面、可接受）；`回放接口拒绝非链接日志` 载体换成本类真跑局（实施方已登记）。10 份副本基线与 3 条比对用例已删；`git diff --quiet HEAD -- tests/Siege.Core.Tests/AiDecision/Fixtures/` 对 5 份缺省基线为真（只有 10 个 D）。Skip 只剩 6 条 Perf / Slow（PRD 写 7：`专家单次部署耗时中位数不超过高难的四倍` 是全知口径的 PerfTheory，随 B1 / B4 删除，不是缺陷）。
4. **MODIFIED 条目仍有测试：通过**（按删后清单 `tests-after.txt` 对名）。battlefield「阶段推进」→ `开放地图阶段推进Tests` 7 条；「战场结算」→ `战场结算Tests` 10 条（整轮 Pass / 一方落子不结算 / 填满 / 减员 / 构筑期也结算 / 已结算不再结算…）；「战场的定义与规则作用域」→ 6 条。link-map-generation「初始布局」→ `初始布局Tests` 162 条（含三加二布局 / 换锚 / 接缝气边）；「物化状态校验」→ 10 条（生成后校验 = 版本号 0，兼守 map-definition「地图为设计师固定的静态数据」的"不再增长"）；「链接档地图标识与确定性」→ 5 条。turn-sequence 三条 → `战场内合法落子范围Tests` 7 条 + `公开规则纯函数Tests` 7 条。terrain-edit → `构筑期内的改造范围Tests` 4 条。information-visibility「始终公开的信息」→ `始终公开的信息Tests` 6 + `按座位视图Tests` 9 + `视图模型按座位过滤Tests` 8；「Pass 名单」→ `锁定后全图可见Tests.全员Pass可见`。elimination-endgame → `两类终局条件Tests` 7 条。simulation-harness「可复现回放」→ `可复现回放Tests` 13（+2 本次）；「各入口支持生成图」→ 18 条（`map子命令打印链接初始图` 断言"连通分量 1"、`配对与链接相关选项已删除` ×4）；「自检四步」→ `自动演示全流程自检Tests` 4 条（`真对局走一遍_四步全通过`）。match-telemetry「对局日志的记录内容」→ `对局日志的记录内容Tests` 11 + `_链接模式Tests` 2；「战场落败复盘」→ `战场落败复盘Tests` 6；「AI 改造与主题诊断」→ `AI改造与主题诊断Tests` 8（含 `主题表现可查` 整局胜率）。ai-decision 三条 → `正式对战AI的信息边界_链接模式Tests` 5、`前瞻中的下一名对手Tests` 7、`前瞻模拟的公平信息Tests` 10。visual-style-baseline「未展开区域的可视表现」→ `占位轮廓灰度Tests` 3 + `棋盘台面的可视表现Tests`（无同名类，按 Scenario 名可追溯）。
5. **行为不变：通过**（实证见下「走法对比」）。
6. **文档：通过（修 6 处）**。设计文档 v1.28 改动章节逐段读过（§1、§3.3、§4、§4.1、§4.2、§11、§12.2–12.5、§13、§14.4、§15.1、§15.2、§17、§19、§21.3、变更记录），与 proposal / design / 规格差分一致；残留并已改：§14.3 第 672 行"其他战场的玩家只显示名字与在局状态…（§13.3）"与 §13 全员可见矛盾 → 改为全员显示；§15.2 C15 句"或独自在单人战场等待时沿用配置值" → "势力为全员最高时沿用配置值"；§19 划线项 2 / 4 补"open-map v1.28 已删除全知开关 / 决赛倒计时，原 §4.3 已删"（否则引到不存在的章节）。`.trellis/spec/core/determinism.md` 第 18 行"拼接本身不消费随机" → "建局布局 `InitialLayout` 不消费随机（拼接已删除）"。HANDOFF 通读：命令与 `GameRoot` 的 `--shot-show` 合法值（placement / capture / power / banner）一致、自检四步、基线 5 份、观察项含 R-3 / 对局长度 / 布局公平；「后续 change」第 4 项"拼接节拍类型残留"随本次清理删除并重编号，测试数更新；boundaries.md 链接 / 配对 / K / AI 视野四行已改为"已删除"口径，通过。R-B5：`art/board-link-mode/README.md` 第 10 / 11 / 15 行与 `art/link-map-terrain/README.md` 第 15 行的 `--shot-show=expand|field` 命令划掉并注"已随 open-map 删除，命令不可用"。`art/board-link-mode/README.md` 文首另加一句历史索引说明（拼接相关行只作记录、不要重截，现行截图见 `art/open-map/README.md`）。另：实施记录 notes.md 的 N2（节拍类型保留）与 HANDOFF 原第 4 项已被本次第 8 项取代。
7. **构建与测试**：见下「验证」。
8. **R-B2 残留类型：已删**。`BoardSlideBeat` / `CameraPullBeat` / `OpponentRevealBeat` / `CoordRect` 与 `ShowMask.Slide` / `SlideOffsetOf` / `CameraPull` 在 `src/godot/scripts`、`tests/`、Presentation 其余文件零引用（grep）；`ShowTimeline.cs` 只删：`ShowMask` 的 `Slide` / `SlideOffsetOf` / `CameraPull` 成员与 `IsEmpty` 末尾两项、`Mask()` 的"未来节拍"循环（只处理这两种节拍）、三个 `case`、两个局部变量与返回处两个 init；飘字年龄计算、`CalloutTailMs`、尾段与节拍时序未动。整文件删 `LinkShowBeats.cs`。SettlementShow 全部测试照过。

## 走法对比（质检项 5）

- 方法：`git worktree add --detach <scratchpad>/wt-a2bb3a4 a2bb3a4`（不用 `git stash`——160 个改动含 rename），两棵树各放一个临时 Fact（`BatchRunner.Execute(候选格上限Tests.LinkGoldenConfig(), parallelism: 1)[0]`，把 `SimFixtures.TurnTexts(log.Turns)` 逐行写到 scratchpad、另存 `TurnHash`），`--filter` 只跑这一条；用完删临时文件、`git worktree remove --force`。
- 结果：新树哈希 `C898F792…`（= 现值）、旧树哈希 `8A03DA5E…`（= 旧值）；旧文本 24 行含 96 处 `"WaitingRounds":n`，按正则剔掉后与新文本 **24 行逐字节相同**；`Placements / Captures / Edits / Passed / Player / MajorRound` 逐行相等。结论：`LinkGoldenTurnHash` 的变化只来自快照格式（去 `WaitingRounds`），走法不变；其余 5 个重钉哈希同一机制（首部去 K / 视野、报告删段），加上 5 份缺省基线逐字节不变（`git diff --quiet` 为真）与 G1 黄金值未动即过，行为不变成立。


## 验证（全部顺序单跑、看真实退出码）

- 改动前基线：`dotnet build siege.sln -c Release` 0 警告 0 错误；`dotnet test tests/Siege.Core.Tests -c Release`：总 2025 / 通过 2019 / 跳过 6 / 失败 0，RC 0（与实施记录一致）。
- 改动后最终：`dotnet build siege.sln -c Release` 0 警告 0 错误；`dotnet test tests/Siege.Core.Tests -c Release --no-build`：**总 2027 / 通过 2021 / 跳过 6 / 失败 0，RC 0，3 m 26 s**（+2 = 两条新守门；跳过 6 条全是既有 Perf / Slow）。
- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误，RC 0。
- Godot headless（`..._console.exe --headless --path src/godot -- …`，顺序单跑）：`--auto-demo` RC 0（锁定 218 / 218、构筑期 42 ×3、第 43 帧全图解禁 214 = 214 − 0，自检 5 条 0 失败）；`--auto-demo --pick-check --seed=5` RC 0（214 / 214 往返一致）；`--map-select --auto-demo` RC 0（新局界面五步 + 锁定 223 / 223、构筑期 48 ×3、解禁 208 = 215 − 7；种子随机，与实施记录的数字不同属正常）；`--auto-demo --rounds=0 --seed=5` RC 0（第 289 帧、第 26 大回合 `LastBattlefieldSettled`、四步齐全、自检 7 条 0 失败）。前两条与第四条的 `[flow]` 帧号与实施记录 / 段 A 逐个相同（截图基线帧不变）。
- 5 份缺省基线：`git diff --quiet HEAD -- tests/Siege.Core.Tests/AiDecision/Fixtures/decisions-link1-{seed1,seed2,seed3,easy-seed1,hard-seed1}.txt` 为真。
- 临时产物已清理：`ZzTmpDump.cs` 已删、`git worktree list` 只剩主工作树；`git status` 不含临时文件。


## 变异验证（新增守门；`cp` 备份 → sed → 构建 → 只跑目标 → `cp` 还原 → `cmp` 逐字节 → `touch`）

- **M-C1** `RunConfig.FromJson` 的 `Players[*].Search.SeatVision` 扫描 `if (retired)` → `if (retired && json.Length < 0)`（裸 `false` 会触发 CS0162 当错误）→ `批量跑局Tests.玩家搜索配置里的视野开关被拒绝` **红 1**（同组 `配置文件里的已删除配置项被拒绝` 8 行仍绿，证明顶层扫描与嵌套扫描是两处）；还原 `cmp` 一致。
- **M-C2** `Program.Replay` 的 `if (!LinkMapId.IsLinkMap(original.Header.MapId))` → 加 `&& path.Length < 0` → `可复现回放Tests.replay命令拒绝旧日志` **红 1**（`旧拼接日志被拒` 7 行仍绿：那条由 `MatchLog.Parse` 拒、不经这一处）；还原 `cmp` 一致。
- 还原后重建再跑四组 17 条全绿。


## 改动文件（质检方）

- `src/Siege.Presentation/Show/ShowTimeline.cs`（删拼接死分支）、`src/Siege.Presentation/Show/LinkShowBeats.cs`（整文件删）
- `src/Siege.Sim/Config/RunConfig.cs`（`FromJson` 加 `Players[*].Search.SeatVision` 拒绝）
- `tests/Siege.Core.Tests/SimulationHarness/批量跑局Tests.cs`（+`玩家搜索配置里的视野开关被拒绝`）、`tests/Siege.Core.Tests/SimulationHarness/可复现回放Tests.cs`（+`replay命令拒绝旧日志`）
- 注释：`tests/Siege.Core.Tests/AiDecision/决策序列基线_链接模式Tests.cs`、`tests/Siege.Core.Tests/AiDecision/候选格上限Tests.cs`、`tests/Siege.Core.Tests/MatchTelemetry/链接模式对局记录Tests.cs`、`tests/Siege.Core.Tests/SimulationHarness/各入口支持生成图_链接模式Tests.cs`
- 文档：`2026-09-10-siege-core-gameplay-design-v1.md`（§14.3 / §15.2 / §19 项 2、4）、`HANDOFF.md`（后续 change 第 4 项删除并重编号、删除清单、`--config` 拒绝、测试数）、`.trellis/spec/core/boundaries.md`（AI 视野行补嵌套拒绝）、`.trellis/spec/core/determinism.md`（第 18 行）、`art/board-link-mode/README.md`、`art/link-map-terrain/README.md`
- 任务目录：`check.md`（本文）、`tests-diff.md`（追加质检新增 2 条）


## 需负责人裁决

1. **N1 基线首部文案**（实施方已登记）：5 份缺省基线首部第 3 / 5 行仍写 `mode=Link pairing=script（缺省：升序两两配）` 与 `P 配对、L 链接、S 战场结算`，导出器为保"逐字节不变"字面保留。建议：下一次需要重钉基线时顺手改首部两行（决策行不变），本次不动。
2. **B3 字面偏离**：搬迁的 `第4大回合不发生任何事件` 多了一条"事件种类只剩五种"断言（强化）；`回放接口拒绝非链接日志` 载体换成本类真跑局；`replay命令拒绝旧日志` 由质检方补回。建议接受。
3. **PRD"Skip 只剩 7 条"实为 6 条**：`专家单次部署耗时中位数不超过高难的四倍` 是全知口径的 PerfTheory，随 B1 / B4 必然删除。建议接受、PRD 口径改 6。

