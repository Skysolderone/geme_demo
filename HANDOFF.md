# 续接说明（HANDOFF）

> 更新于 2026-10-20（`cycle-stop`：大回合上限安全网 + AI 循环提子停手，设计文档 v1.29；`open-map` 段 A / B 已提交 `a2bb3a4` / `7ae2f65`）。仓库：`git@github.com:Skysolderone/geme_demo.git`。
> 读完本文即可在新会话中继续；最近进度以 `.trellis/workspace/rubioc/journal-1.md` 最后一节为准。

## 一句话

《围杀 Siege》：4 人回合制策略构筑游戏，玩法是围棋式提子加征募构筑。现在只有一个模式——**开放地图**（`link:<地图种子>`，open-map v1.28）：5 块主题出生棋盘建局时就边对边紧贴成一张连通地图（3+2 两行），四人盲选其中一块插旗；第 1–3 大回合只能在自己锁定的出生棋盘内落子与改造（同区者共享），第 4 大回合起全图可落；锁定后全图全可见。整张地图是唯一战场：四人整轮 Pass / 地图填满 / 只剩一人即结算、即终局，名次按势力与判定链；另有大回合上限安全网（缺省 100，到达即按势力结算）。配对 / 拼接 / 等待者 / 决赛 / 决赛倒计时 / AI 全知开关已全部删除，代码里没有这些概念。
规则内核 `src/Siege.Core`（.NET 8，零 Godot 依赖，AI 在 `Ai/`）；批量、分析与终端版在 `src/Siege.Sim`；视图模型在 `src/Siege.Presentation`；3D 表现层在 `src/godot`（Godot 4.7.2 .NET，不在 `siege.sln` 里）。

## 怎么玩

> 终端是 **Windows PowerShell**：exe 路径带引号时前面加 `&`；给 Godot 传参先写 `--%`，自定义参数一律放在 `--` 之后，写成 `--名=值`。
> **试玩带入带出请用临时档案**：缺省档案是真实的 `%APPDATA%\Siege\profile.json`（终端与图形版共用）。
> 地图一律是 `link:<地图种子>`；旧地图标识在任何入口一律报错（见「旧地图（已删除）」）。

**图形版（推荐）**。不带 `--map=` 时先进"开始新局"界面：显示随机地图种子与 `link:<种子>`，可输入种子、"换一张"、选难度、"开始"。
```powershell
& "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe" --% --path E:/wws/geme_demo/src/godot -- --difficulty=Hard --profile=%TEMP%\siege-try.json
& "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe" --% --path E:/wws/geme_demo/src/godot -- --map=link:12345 --no-carry
```
- 流程：点 5 个占位之一插旗（选中即锁定，5 块棋盘随即全部展开、整张地图可见）→ 征募 → 左下手牌选类型、点格子暂放（第 1–3 大回合只能落在自己的出生棋盘内，匠人的改造目标也限自己棋盘；第 4 大回合起全图）→ 右侧看预演 → Enter 确认 / P 键 Pass；有"弃赛"按钮。四人整轮 Pass、地图填满或只剩一人即终局。
- 按键（`InputBindings.cs`）：1/2/3/4 盘面 / 势力 / 信物 / 顺序层；Tab 切读法；H 手牌面板；V 收起征募面板；T 按住 / 点击切换；E 轮换改造目标；M 全局预览（一屏看不全时才有）；方向键或 WASD 平移；空格回自己的出生棋盘；Esc 返回。

**终端版**
```powershell
dotnet run --project src/Siege.Sim -c Release -- play --map link:12345 --difficulty Hard --profile $env:TEMP\siege-try.json
```
对局中输入 `resign` / `弃赛` 弃赛，`q` 退出。流程同图形版：插旗锁定后整张地图可见，第 1–3 大回合只能在自己的出生棋盘落子，第 4 大回合起全图。

**批量跑局与分析**（每个配置不超过 20 局）
```powershell
dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/<目录> --map link:1 --seed 1 --count 10 --difficulty Standard --turn-limit 600
dotnet run --project src/Siege.Sim -c Release -- analyze --dir sim-out/<目录>
dotnet run --project src/Siege.Sim -c Release -- map --map link:1
```
`replay` 凭日志首部的种子与配置回放（没有配对事件可读）。不带参数运行打印完整用法。

## 命令行选项（已与代码核对）

**图形版**（`src/godot/scripts/GameRoot.cs` `_Ready` / `GameRoot.Carry.cs` 经 `LaunchArgs` 严格解析：合法选项 = 被读取过的名字，拼错或值不对即退出码 1）：
- 对局：`--map=link:<种子>`（只写 `link` 随机取种子并打印）`--seed=<对局种子>` `--difficulty=Easy|Standard|Hard|Expert` `--cell-limit=<K，0=不限>` `--map-select`（强制进新局界面，配合截图 / 自检）。
- 带入带出：`--profile=<路径>` `--no-carry` `--carry-preview=supply|settlement`。
- 无人值守：`--auto-demo` `--rounds=<N，缺省 4，0=跑到终局>` `--zone=<1–5，本机锁定的出生棋盘，缺省 1>` `--pick-check`。
- 截图：`--screenshot=<绝对路径>[:帧]` `--overview` `--shot-cell=<坐标>` `--shot-board=<棋盘下标>` `--shot-show=placement|capture|power|banner`（`expand` / `field` 随拼接演出与战场横幅删除）`--shot-power` `--shot-groups` `--shot-ownership` `--shot-forbidden` `--shot-recruit-collapsed` `--perf-sample=<秒>`。
- 其他：`--mute`（演出不发声）`--export-parts=<目录>` `--piece-gallery=<PNG>`（不建局，导完退出）。
- 未给 `--map=`：有人值守进新局界面，无人值守取 `link:1`。无人值守（`--auto-demo` / `--pick-check` / `--screenshot=`）固定标准难度（同时给 `--difficulty=` 报错）、关闭带入带出；`--auto-demo` 缺省对局种子 20260915。

**Siege.Sim**（`src/Siege.Sim/Program.cs` `PrintUsage`，与各子命令的读取一致）：
- `play`：`--seed` `--players` `--seat`（缺省 1）`--difficulty` `--map`（缺省 `link:1`）`--cell-limit` `--profile <路径>` `--no-carry`（两者不能同给）。
- `run`：`--out`（必填）`--config <json>` `--seed` `--count` `--parallel` `--serial` `--map` `--players` `--difficulty` `--turn-limit`（缺省 600，0 = 不截断）`--artisan-weight`（缺省 10）`--cell-limit` `--pass-threshold`（缺省 20）`--round-cap`（大回合上限，缺省 100，0 = 不启用）`--stagnation-rounds`（AI 停滞大回合数 K，缺省 6，0 = 不启用；与 `--pass-threshold` 同口径，只作用于未显式配置 `Players[].Search` 的玩家——显式 `Search` 须自带 `StagnationRounds`，不写即 0）`--carry-in 0|1`（缺省 0）`--map-per-match`（第 i 局用 `link:<起始+i>`）`--retention SnapshotsOnly|Full` `--sample-permille` `--gzip`。
- `map`：`--map`（只接受 `link:`，缺省 `link:1`）`--out`。输出含"连通分量 1"一行（按几何四邻实算）。
- `replay`：`--file <match-*.jsonl>` 或 `--dir <目录> --seed <十六进制种子>`（只回放 `link:` 日志）。`analyze`：`--dir` `--include-contaminated` `--review-defeats`（按日志回放复盘整轮 Pass 结算的落败者与决胜一手，耗时约等于跑局；缺省不做）`--review-artisan` `--review-cycles`（循环提子诊断：只读日志快照、不回放，末段 12 个大回合窗口；缺省不做）`--out`。
- 已删除：`--flag-risk`（冒险概率随盲选废除，`retire-legacy-maps`）；`--pairing-seed` / `--final-countdown` / `map --links`（open-map 段 B，严格命令行下报错退出）；`--config` 文件里写 `FlagRisk` / `PairingSeed` / `FinalCountdownRounds` / `SeatVision` 字段即报错退出（后三者提示"拼接规则已移除"；`Players[].Search.SeatVision` 同样点名拒绝）。Link 局固定 4 人，`--players` 给别的值建局报错。AI 一律按座位视野（锁定后 = 全图），没有全知开关。难度名不区分大小写、不接受数字（`AiDifficultyNames`）。

## 现行规则速览（设计文档 v1.29）

- **地图**（§3.3，`link-map-terrain` + `open-map`）：`link:<种子>` 生成 5 块出生棋盘（宽高 6–8），五主题高台 / 河流 / 林地 / 栅栏阵 / 混合各一块；外圈平坦，中央 3×3 核心区是出生区（2 个出生区信物），中间带按主题放高差 / 深水与桥 / 林地 / 栅栏 / 岩石，另有 0–1 个公共区档信物在难到达格；5 块按种子次序贴成 3+2 两行（第一行底边对齐东贴，后两块北贴，共享边最长、偏西优先，放不下即换锚），画布原点固定、留白 2；全图可落子 212–280、信物 10–15；地图整局不变（`MapVersion` 恒 0）。插旗占位公开主题名。棋盘外是不可落子、不可改造的场景格。**战场** = 可落子格连通分量，开局即恰 1 个（整张地图）。
- **盲选**（§4.1）：只见 5 个占位，选中即锁定；AI 用 `flag-pick` 子流均匀选；多人可同区。锁定后 5 块棋盘一起展开、全图可见。
- **构筑期**（§4.2）：第 1–3 大回合合法落子范围 = 自己锁定的整块出生棋盘的可落子格（同区共享）− 禁入格，改造目标（搭桥 / 烧林的格、立栅的边两端）也限自己棋盘；第 4 大回合起全图 − 禁入格。靠接缝的棋子的气与覆盖照常伸入邻盘（O7），构筑期内不可能跨接缝提子。
- **结算与终局**（§12.3–12.4）：每个大回合结束检查唯一战场——在局 ≥ 2 人且全员 Pass / 填满 / 只剩一人（减员）/ **到达大回合上限**（`cycle-stop` 安全网：对局配置缺省 100、0 = 不启用，排在整轮 Pass 与填满之后，结算原因记"上限"；旧存档缺字段按 100 回填留痕；图形版剩 ≤ 5 大回合时提示条"第 X 大回合结束时按势力结算"，终端大回合行打印"上限 N（还剩 M）"，新局界面无此项）；势力第一胜出，其余"战场落败"出局，结算即终局（原因名 `LastBattlefieldSettled` 保留）。终局只有"只剩一名参赛玩家"与"最后战场结算"两类。名次：胜者 → 落败者按结算时势力与判定链（信物数 / 独占空格 / 棋子数 / 种子兜底）→ 更早出局者倒序 → 弃赛者（§12.5）。
- **信息**（§13）：锁定后全图全可见（地形、棋子、设施、信物揭示、全员势力明细与手牌类型、全员 Pass 名单）；插旗阶段只见占位。AI 按座位视野（= 全图），没有全知开关；专家前瞻名册 = 全部在局玩家。
- **势力** = 领地分（独占空格，荒漠不计）+ 棋串军势，军势 = ⌊(基础 + 位置加值) × 1.5ⁿ⌋，不封顶，BigInteger（§10.1）。部署上限 3 / 4 / 5（第 1–3 / 4–6 / 7+ 大回合）+ 军令；征募展示 5、免费取 3。
- **活形**（§6.4）：`EYE_SPACE_MAX = 12`，眼值 ≥ 2 为已活；已活棋串的眼空间对他人禁入；单子不成活。同形判重不含棋子类型（§6.2）。
- **十种棋子 / 十类信物**，内容集 v2 缺省（§8、§9）。
- **AI**（§15.2）：九维评价；停手阈值 20，己方无子或"不是全部在局玩家里势力第一"时取 0（C15）；**决胜一手**（`ai-pass-review` P7）：本将 Pass、该 Pass 会让整轮 Pass 结算、自己落后时，若有合法单点或强制贪心批次能让势力追平 / 反超最高者，落下差值最大者；可落子格 > 150 时候选格上限 K = 24。**循环提子停手**（`cycle-stop`）：停滞大回合数 6（四档共用）——盘上有子、连续 6 个大回合本人公开势力无净增、且排序前 N 候选全是提子 / 落在最近 6 个大回合被提过的格时视同无改善手 Pass（决胜一手优先；只读公开量）。AI 评价版本 6。难度 简单 / 标准 / 困难 / 专家（一层前瞻）。
- **带入带出**（§21）：备用子 3 / 征召签 2 / 换型令 4 点，每局至多 1 件；名次点 24/16/12/10；完赛全额、弃赛 50% 并返还补给、出局（势力归零或战场落败）带出 0 且补给丢失。
- **开放地图冒烟**（open-map 段 A 1.7，`link:1` ×10 + 每局换图 ×10，标准 × 4，`turn-limit` 600）：20 局全部规则终局、截断 0；大回合中位 24 / 最小 20 / 最大 29；单小回合耗时中位 72 ms；五主题平均名次 Plateau 2.37 / Mixed 2.50 / Palisade 2.52 / River 2.53 / Forest 2.64（`sim-out/open-map/smoke`，报告 `.trellis/tasks/10-19-open-map-a/smoke.md`）。

## 自检命令（Godot 用 `..._console.exe`，headless、顺序单跑、看真实退出码）

`$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"`，以下都是 `& $G --headless --path src/godot ...`：
- 构建 C#：`dotnet build src/godot/Siege.Godot.csproj`（**Debug**——Godot 在编辑器外读 Debug 程序集，只做 Release 构建会跑到旧代码；`--build-solutions --quit` 会挂住不自退，不用）。
- 四步自检（规格 simulation-harness「自检四步」）：
  1. `-- --auto-demo`：插旗锁定 → 构筑期（第 1–3 大回合各一条），`[flow]` 已见各步通过 + 开局对准自证，退出 0。
  2. `-- --auto-demo --pick-check --seed=5`：锁定后对全图跑一轮拾取（214/214）即退出 0。
  3. `-- --map-select --auto-demo`：新局界面五步（非法种子 / 合法种子 / 换一张 / 回到进入时的种子 / 开始）再接演示；第 4 步节点守门为"建局后 = 进入时 + 主题名标注数"。
  4. `-- --auto-demo --rounds=0 --seed=5`：跑到终局，`[flow]` 要求四步齐全（插旗锁定 → 构筑期 → 全图解禁 → 终局），任一步不符退出 1。
- 换出生棋盘看取景：`-- --auto-demo --zone=<1–5> --seed=<S>`。
- 截图（非 headless）命令与人工试玩清单：`art/open-map/README.md`（锁定后全图、第 4 大回合跨棋盘落子）、`art/link-map-terrain/README.md`（五主题、棋盘内岩石 `link:9`）。
- 规格：`openspec validate open-map --strict`（归档后 `openspec validate --specs --strict`）。

## 测试现状

- `dotnet build siege.sln -c Release` 与 Godot 工程（`src/godot/Siege.Godot.csproj`）均 0 警告 0 错误（cycle-stop 第二段实测，2026-10-20）；Godot 四条 headless 自检退出 0。临近上限时图形版打印 `[round-cap] 第 N 帧、第 R 大回合：…` 一行（自检四条都在第 95 大回合前结束，不会出现）。
- `dotnet test`：**2051 条，通过 2045，跳过 6**（cycle-stop 第二段实施后实测，2026-10-20，退出码 0；新增 `循环提子诊断Tests` 3、`大回合上限配置Tests` 4、`战场结算Tests` 上限三条、`两类终局条件Tests.上限结算即终局`、`对局日志的记录内容_链接模式Tests.上限结算可查`、`循环提子停手Tests` 9、`大回合上限提示条Tests` 3；跳过全是 Perf / Slow）。此前 open-map 段 B 质检：2027 / 2021 / 6。
- 决策序列基线：**只剩 5 份**——`link:1` + 标准难度、对局种子 1 / 2 / 3，简单 / 困难种子 1（截到第 12 大回合；`AiDecision/Fixtures/decisions-link1-*.txt`），open-map 段 A 重钉、段 B 逐字节不变。`*-omniscient.txt` / `*-k0.txt` 十份副本与比对用例已随全知开关与 K 删除。专家 G1 黄金值与前瞻计时局面集钉在 `link:1` 种子 1–3。基线首部仍写 `mode=Link pairing=script（缺省：升序两两配）` 与"P 配对、L 链接"字样——那是历史文案，为保 5 份基线逐字节不变未改（见 notes.md 待裁决项）。

## 旧地图 / 旧规则（已删除）

- `retire-legacy-maps` 删除：标准档 v5 / 2 人 / 3 人图、边疆档与 `gen:` 生成图、棋盘档 `board:`、`maps/*.json`、全部旧生成器与 `MapCatalog.Legacy`；规格档只剩链接档。
- `retire-legacy-rules` 删除：`MatchMode` 与全部 Legacy 规则分支。
- `open-map` 段 B 删除：待配对 / 待决赛链接阶段、配对事件与来源（`Pairing.cs`、`PairingSources.cs`、`RandomPairingSource`）、链接事件与记录、`LinkStage`、等待者与 `WaitingRounds`、决赛倒计时与 K（`MatchFlow.Countdown.cs`、`FinalCountdownAnalyzer`、`FinalCountdownNotices`）、`LinkMapGenerator.Link` / `Sequences` / `LinkLayout` / `LinkLogLayout`、`GameBoard.ApplySplice` / `LinkedMap` / `RelicLedger.Translate`、校验器 `ValidatePlateGaps`、AI 全知开关 `SeatVision`、拼接节拍 `LinkShowBeats.cs`（生成器与 `BoardSlideBeat` / `CameraPullBeat` / `OpponentRevealBeat` 三个节拍类型，`ShowTimeline` 的对应分支一并删）与战场横幅 / 他场提示条 / `NoticeQueue`、Godot 滑入演出与被贴方相机跟随、"未与本机链接"遮蔽分支。改动前的链接存档 / 日志 / 批次配置含这些字段时在三处入口明确拒绝并提示"拼接规则已移除"。

## 后续 change（只登记，未建目录；设计文档 §19 同）

1. ~~R-3：唯一战场上 AI 对局可能不收尾~~：**已处理**（`cycle-stop`，v1.29）——大回合上限安全网（缺省 100）+ AI 循环提子停手（K = 6）。验证（简单 × 8 种子 40–47）：循环 1 → 0、拖延 4 → 1、中位 65.5 → 58、最长 94 → 74、上限结算 0；标准种子 1–2 终局大回合不变（`.trellis/tasks/10-20-cycle-stop-2/verify.md`）。R-3 原局面（1 人类 + 3 AI 互提）未复现（不给 `play` 加日志，裁决 D5）。
1a. **改造让盘面常新观察**（`cycle-stop` 裁决 D1，设计文档 §19 第 18 项）：同形键含改造段，循环局里匠人每轮带新改造使占用相同的盘面每轮都算新局面；同形禁则不改，靠上限 + 停手兜住。
1b. **停滞停手无逐次留痕**：控制器有 `LastStagnation`，但日志决策行仍记 `D:pass`，离线分析数不出停手触发了几次；要量化时再加日志字段。
1c. **停滞记忆不入存档**（cycle-stop 质检登记）：`HeuristicTurnController` 的停滞记忆（每大回合势力采样、被提格、上次占用）只在控制器内存里。回放（`replay` / `--review-defeats`）从第 1 小回合重建控制器，记忆随之重建，逐步一致；但若以后加"中局存档恢复后由启发式 AI 续跑"的入口，恢复后 K 窗口从零计数，最多 K 个大回合内停手不会触发，与不中断的局可能分歧。现在没有这样的入口（`MatchFlow.Restore` 在 `src/` 下无调用方），加入口时再定：记忆入存档，或由公开势力历史重建。
2. **对局长度观察**（裁决 B8）：开放地图下标准 × 4 中位 24 大回合（旧链接模式 15–16），只登记不调参；负责人试玩后决定是否缩棋盘边长 / 构筑期。
3. **3+2 布局公平观察**：第一行中间块邻居最多（3 块），插旗位置公开可自选；20 局五主题平均名次 2.37–2.64 看不出失衡，不处理。
4. **决策序列基线首部文案**：5 份基线首部的 `mode=Link pairing=script（缺省：升序两两配）` 与"P 配对、L 链接"字样是历史口径；要改须重钉 5 份基线（内容不变、只改首部两行），待负责人裁决。
5. **Godot 无头运行偶发崩溃观察**：一次 `Illegal instruction`（退出码 132）未复现；再现时抓崩溃日志（J12-7）。
6. **AI 整批评价观察**（`ai-pass-review` P14）：逐枚贪心会漏掉"整批为正、逐枚为负"的组合。
7. **决胜一手拉长对局观察**（P15）：均值 +0.4 大回合，个例 14 → 20。
8. **链接档新地表**（`link-map-terrain` M4，暂名 `link-map-surfaces`）：荒漠 / 沼泽 / 岩台 / 浅滩进入出生棋盘中间带。
9. **格线高度观察**（第 6 组）：格线画在 h=0 面上，高台上只能靠地砖缝看格界；负责人看图后暂不立项。
10. **栅栏造眼是否太便宜**（`ai-terrain-review` T13，规则层观察）：试玩体会后再决定是否改规则。
11. **Sim 贴地形小空区口径观察**（`retire-legacy-rules` G1）。
12. **前瞻投影缺出局字段**（G2）：前瞻投影不填 `EliminationReason` / `PowerAtDefeat`（只用于前瞻、不影响决策）。
13. **偶发失败观察**（G5）：`批量跑局Tests.已删除的选项报已删除(no-catch-up)` 偶发失败一次，重跑通过。

另有未开工的 change `map-elements-v2`（地图元素造型精修，0/17）。已归档（2026-09-28 之后）：`recruit-panel-collapse`、`board-map`、`forbidden-marks`、`turn-settlement-show`、`settlement-show-callouts`、`ai-turn-speed`、`board-render-perf`、`show-sound-cues`、`board-link-mode`、`retire-legacy-maps`、`ai-pass-review`、`link-map-terrain`、`ai-terrain-review`、`final-countdown`、`ai-seat-vision`、`retire-legacy-rules`；`open-map` 待段 B 质检后归档（2.6）。

## 权威来源（按优先级）

1. `openspec/specs/`（Requirement / Scenario 是验收基准）与 `2026-09-10-siege-core-gameplay-design-v1.md`（**v1.28**，文末变更记录）。`Siege-玩法介绍-v1.docx` 是旧玩家向稿，与 md 冲突时以 md 为准。
2. `openspec/changes/archive/<日期>-<change>/` 的 proposal 裁决表与 design.md：设计文档没写到的以它判定（开放地图的裁决 O1–O8 / B1–B10 在 `open-map` 的 proposal / design，归档前在 `openspec/changes/open-map/`）。
3. `.trellis/spec/core/`：`index` / `boundaries` / `determinism` / `coordinates` / `testing`，**必读**。
4. `.trellis/workspace/rubioc/journal-1.md` 与 `.trellis/tasks/archive/<月>/<任务>/implement.md`（逐段记录、冒烟数字、自检输出）。
5. `openspec/ROADMAP.md`；`art/*/README.md`（截图索引与人工检查清单）。

## 执行流程与工作约定

```
opsx:propose 开 change → task.py create + start 建 Trellis 任务
  → 每段一个 Agent(trellis-implement)：写代码 + 测试 + 变异记录，不 commit、不改 openspec/
  → 主会话前台复核：sln 构建 + Godot 单独构建 + 全量测试（真实退出码）→ 提交
  → 下一段 … → 设计文档升版 → openspec archive → task.py archive → journal 记一节
```
- 派子 agent 前，把待决项写成编号裁决，连同派发方式选项一次性交负责人确认；逐项裁决沿用负责人看到的表格行序和编号。
- **同一时间只跑一个任务**（跨会话也算）：agent 在跑时主会话不另起 dotnet、变异脚本、Godot 或第二个 agent。
- **不跑 200 局**，每个配置不超过 20 局；需要更多先问。**可玩优先**：负责人问"能不能玩"时立刻给可玩入口。
- **先红后绿**；变异还原要逐字节校验并刷新 mtime，否则 MSBuild 跳过重建得到假绿（testing.md）。
- 测试不得写真实的 `%APPDATA%\Siege`；依赖 AI 实际走法的断言要写死权重与停手阈值，不许挑种子凑绿。
- 提交信息写进文件用 `git commit -F`；不要把 `dotnet test` 接进管道再 `&&`；不加 Co-Authored-By 署名（负责人全局规则）。

## 规范要点（`.trellis/spec/core/` 浓缩）

- **零 Godot 依赖**：Core / Sim / Presentation 不得引用 `Godot.*`；`src/godot/` 不在 sln 里，守门靠源码级扫描。
- **单一实现**：几何四邻 `Adjacency.Neighbors`；气边 `LibertyNeighbors`；覆盖 `CoverageTargets` / `CoverageMap`；地图解析 `MapCatalog`；建局布局几何 `InitialLayout`（`Siege.Core.Board.Maps`）；合法落子范围 / 改造范围 `PublicRules.LegalRange` / `EditScope`；格坐标换算 `GameBoard.CoordOfIndex`；结算顺序 `SettlementDriver`；地形写入 `TerrainWriter`；活形 `LifeShapeReport`；同形键 `GameBoard.SuperkoKey`；图形版命令行 `LaunchArgs`；Godot 侧格 ↔ 3D 映射 `BoardGeometry`。
- **确定性**：随机只用 `GameSeed` 子流（`relic-gen` / `recruit` / `setup` / `flag-pick` / `carry-ai` / `carry-draft:<n>` 等），互不扰动；地图布局不消费任何对局子流；Sim 不再有任何真随机来源；禁止浮点计分，势力用 `BigInteger`。
- **信息边界**：公开视图按座位过滤（锁定后 = 全图）、结构上不含私有字段；UI 不做规则计算。
- **测试**：Requirement 对应测试类、Scenario 对应测试方法；新守门必须做变异验证；Skip 只给 Perf / Slow。
- **数据口径**：入口遇到未识别输入必须报错，不得静默回退缺省值；已删除的配置项 / 存档字段 / 日志字段读到即拒绝、不回填；规则一变权重先标"未校准"。坐标为围棋记法，`A1` 在左下，列字母跳过 `I`，第 26 列起双字母。

## 环境

- Windows 11；Bash 工具是 Git Bash，负责人终端是 PowerShell。**用 `python`，不要用 `python3`**。
- .NET SDK **8.0.425**；Release 与 Debug 都要构建。`dotnet test` 输出为中文 GBK，脚本里设 `DOTNET_CLI_UI_LANGUAGE=en` 并用 `Failed` 抓失败名，红绿以退出码为准。
- Godot **4.7.2 .NET**：`D:\software\godot\Godot_v4.7.2-stable_mono_win64\`，命令行用 `..._console.exe`；截图须非 headless、`--screenshot=` 给绝对路径。
- codegraph 索引在 `.codegraph/`（已 gitignore）；`sim-out/` 已 gitignore。

## 可直接粘贴的开场 prompt

```
读 E:\wws\geme_demo\.trellis\workspace\rubioc\journal-1.md 的最后一节，再读 E:\wws\geme_demo\HANDOFF.md。
从 HANDOFF「后续 change」里按优先级选下一项，先给我方案和编号裁决，我确认后再开 change / 派 agent。
遵守 HANDOFF「执行流程与工作约定」与 .trellis/spec/core/：同一时间只跑一个任务；不跑 200 局，每配置 ≤ 20 局；先红后绿。
每段结束汇报：测试数、变异验证情况、待决项。使用中文，每次回答开头报数（ws:N）。
```
