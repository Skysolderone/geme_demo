# 续接说明（HANDOFF）

> 更新于 2026-10-16（`retire-legacy-rules` 实施：删除 `MatchMode` 与全部 Legacy 规则分支、只剩链接规则，设计文档 v1.27；`ai-seat-vision`（2026-10-15）、`final-countdown`、`ai-terrain-review`、`link-map-terrain`、`ai-pass-review`、`retire-legacy-maps`、`board-link-mode` 均已归档）。仓库：`git@github.com:Skysolderone/geme_demo.git`。
> 读完本文即可在新会话中继续；最近进度以 `.trellis/workspace/rubioc/journal-1.md` 最后一节为准。

## 一句话

《围杀 Siege》：4 人回合制策略构筑游戏，玩法是围棋式提子加征募构筑。现在只有一个模式——**链接模式**：四人盲选 5 块隔绝的出生棋盘之一，构筑 3 个大回合后独占者两两配对、棋盘边对边拼接开战，战场逐个结算淘汰，胜者再拼接打决赛。
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
- 流程：点 5 个占位之一插旗（选中即锁定）→ 征募 → 左下手牌选类型、点格子暂放 → 右侧看预演 → Enter 确认 / P 键 Pass；有"弃赛"按钮。配对用真随机。
- 按键（`InputBindings.cs`）：1/2/3/4 盘面 / 势力 / 信物 / 顺序层；Tab 切读法；H 手牌面板；V 收起征募面板；T 按住 / 点击切换；E 轮换改造目标；M 全局预览（当前战场一屏看不全时才有）；方向键或 WASD 平移；空格回自己的出生棋盘；Esc 返回。

**终端版**
```powershell
dotnet run --project src/Siege.Sim -c Release -- play --map link:12345 --difficulty Hard --profile $env:TEMP\siege-try.json
```
对局中输入 `resign` / `弃赛` 弃赛，`q` 退出。配对用真随机。

**批量跑局与分析**（每个配置不超过 20 局）
```powershell
dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/<目录> --map link:1 --seed 1 --count 10 --difficulty Standard --turn-limit 600
dotnet run --project src/Siege.Sim -c Release -- analyze --dir sim-out/<目录>
dotnet run --project src/Siege.Sim -c Release -- map --map link:1 --links 1-4E,2-5E,1-2N
```
`run` 缺省由对局种子派生配对种子（同一批次可重复对比）；`replay` 按日志里的配对事件回放。不带参数运行打印完整用法。

## 命令行选项（已与代码核对）

**图形版**（`src/godot/scripts/GameRoot.cs` `_Ready` / `GameRoot.Carry.cs` 经 `LaunchArgs` 严格解析：合法选项 = 被读取过的名字，拼错或值不对即退出码 1）：
- 对局：`--map=link:<种子>`（只写 `link` 随机取种子并打印）`--seed=<对局种子>` `--difficulty=Easy|Standard|Hard|Expert` `--cell-limit=<K，0=不限>` `--map-select`（强制进新局界面，配合截图 / 自检）。
- 带入带出：`--profile=<路径>` `--no-carry` `--carry-preview=supply|settlement`。
- 无人值守：`--auto-demo` `--rounds=<N，缺省 4，0=跑到终局>` `--zone=<1–5，本机锁定的出生棋盘，缺省 1>` `--pick-check`。
- 截图：`--screenshot=<绝对路径>[:帧]` `--overview` `--shot-cell=<坐标>` `--shot-board=<棋盘下标>` `--shot-show=placement|capture|power|banner|expand|field` `--shot-power` `--shot-groups` `--shot-ownership` `--shot-forbidden` `--shot-recruit-collapsed` `--perf-sample=<秒>`。
- 其他：`--mute`（演出不发声）`--export-parts=<目录>` `--piece-gallery=<PNG>`（不建局，导完退出）。
- 未给 `--map=`：有人值守进新局界面，无人值守取 `link:1`。无人值守（`--auto-demo` / `--pick-check` / `--screenshot=`）固定标准难度（同时给 `--difficulty=` 报错）、关闭带入带出、用内置脚本固定配对；`--auto-demo` 缺省对局种子 20260915。

**Siege.Sim**（`src/Siege.Sim/Program.cs` `PrintUsage`，与各子命令的读取一致）：
- `play`：`--seed` `--players` `--seat`（缺省 1）`--difficulty` `--map`（缺省 `link:1`）`--cell-limit` `--profile <路径>` `--no-carry`（两者不能同给）`--final-countdown <K>`（缺省 2，0 = 不启用）。
- `run`：`--out`（必填）`--config <json>` `--seed` `--count` `--parallel` `--serial` `--map` `--players` `--difficulty` `--turn-limit`（缺省 600，0 = 不截断）`--artisan-weight`（缺省 10）`--cell-limit` `--pass-threshold`（缺省 20）`--pairing-seed` `--carry-in 0|1`（缺省 0）`--map-per-match`（第 i 局用 `link:<起始+i>`）`--final-countdown <K>`（决赛倒计时，缺省 2，0 = 不启用；落成值写进 config.json 与日志首部）`--retention SnapshotsOnly|Full` `--sample-permille` `--gzip`。
- `map`：`--map`（只接受 `link:`，缺省 `link:1`）`--links <如 1-4,2-5 或 1-4E,2-5E,1-2N>`（单棋盘之间不写贴边一律东贴，3+1 决赛须显式写 N）`--out`。
- `replay`：`--file <match-*.jsonl>` 或 `--dir <目录> --seed <十六进制种子>`（只回放 `link:` 日志）。`analyze`：`--dir` `--include-contaminated` `--review-defeats`（按日志回放复盘整轮 Pass 结算的落败者与决胜一手，耗时约等于跑局；缺省不做）`--out`。
- 已删除：`--flag-risk`（冒险概率随盲选废除）；`--config` 文件里写 `FlagRisk` 字段即报错退出（`retire-legacy-maps`）。Link 局固定 4 人，`--players` 给别的值建局报错。AI 视野只在 `--config` 顶层 `SeatVision`（缺省 true = 按座位视野；`false` = 全知，只作测试与离线分析，对全部 AI 生效），没有命令行选项；写进 `Players[].Search.SeatVision` 即报错（`ai-seat-vision`）。批次配置与日志首部照写该项，旧日志缺该项按全知回放。难度名不区分大小写、不接受数字（`AiDifficultyNames`）。

## 现行规则速览（设计文档 v1.27）

- **地图**（§3.3，`link-map-terrain`）：`link:<种子>` 生成 5 块出生棋盘（宽高 6–8、间距 3 排开），五主题高台 / 河流 / 林地 / 栅栏阵 / 混合各一块；外圈平坦，中央 3×3 核心区是出生区（2 个出生区信物），中间带按主题放高差 / 深水与桥 / 林地 / 栅栏 / 岩石，另有 0–1 个公共区档信物在难到达格；全图可落子 212–280、信物 10–15。插旗占位公开主题名。棋盘外是不可落子、不可改造的场景格。**战场** = 可落子格连通分量，战斗阶段可见本战场各棋盘外接矩形内全部格。
- **盲选**（§4.1）：只见 5 个占位，选中即锁定，互相看不到；AI 用 `flag-pick` 子流均匀选；多人可同区。
- **构筑期**（§4.2）：第 1–3 大回合只能在自己的战场落子；没有第 4 大回合全图解禁，任何时候都只能在所在战场落子。
- **配对与拼接**（§4.3、§3.3）：第 3 大回合结束时独占者两两随机配对，拼接成连体棋盘（锚点 = 最小编号者不动，首轮贴东侧、决赛贴北侧，共享边最长、并列偏南 / 偏西）；多人同区就地对局；奇数时配对事件指定一人等待。配对不可复现，作为事件写日志与存档。
- **战场结算**（§12.3）：每个大回合结束检查——本战场在局 ≥ 2 人且全员 Pass / 填满 / 只剩一人 / 决赛倒计时到期；势力第一胜出，其余"战场落败"出局。单人战场不因 Pass 或填满结算。
- **决赛倒计时**（§4.3，`final-countdown`）：第一次出现"等待决赛的玩家"（已结算战场的胜者，或第 3 大回合结束时未被配对的独占者）的那个大回合记为 R（不早于第 3 大回合，W7），其余未结算多人战场最迟第 R + K 大回合结束时按势力强制结算（原因 `Expired`）；之后才出现的战场期限 max(R + K, 出现大回合 + 1)；决赛战场不倒计时。K 是对局配置，缺省 2，0 = 不启用；旧存档按 0。图形版每个大回合开始出提示条"对面战场已结算，本战场还剩 N 个大回合" / "等待决赛：对面战场最迟第 X 大回合结束"。
- **决赛与终局**（§12.4）：全部多人战场结算且在局 ≥ 2 → 决赛拼接；终局只有"只剩一名参赛玩家"与"最后战场结算"两类。名次：决赛胜者 → 决赛其他玩家（同样以战场落败出局，出局最晚）→ 更早落败者倒序（同次按势力排）→ 弃赛者（§12.5）。因此带出补给点的只有决赛胜者（§21.3）。
- **信息**（§13）：同战场公开与全局公开两层；其他战场的盘面与势力数值不可见（`MatchPublicView.ForSeat` 数据层过滤）。AI 同样按座位视野（`ai-seat-vision`，四档一律；先手维名次只在同战场内比较），全知只作批量跑局的显式配置；专家前瞻名册限同战场。
- **势力** = 领地分（独占空格，荒漠不计）+ 棋串军势，军势 = ⌊(基础 + 位置加值) × 1.5ⁿ⌋，不封顶，BigInteger（§10.1）。部署上限 3 / 4 / 5（第 1–3 / 4–6 / 7+ 大回合）+ 军令；征募展示 5、免费取 3。
- **活形**（§6.4）：`EYE_SPACE_MAX = 12`，眼值 ≥ 2 为已活；已活棋串的眼空间对他人禁入；单子不成活。同形判重不含棋子类型（§6.2）。
- **十种棋子 / 十类信物**，内容集 v2 缺省（§8、§9）。
- **AI**（§15.2）：九维评价；停手阈值 20，己方无子或"链接模式下不是本战场势力第一"时取 0（C15）；**决胜一手**（`ai-pass-review` P7）：本将 Pass、该 Pass 会让本战场整轮 Pass 结算、自己落后时，若有合法单点或强制贪心批次能让势力追平 / 反超本战场最高者，落下差值最大者；可落子格 > 150 时候选格上限 K = 24（链接档按建局时 5 块之和判定）。难度 简单 / 标准 / 困难 / 专家（一层前瞻）。
- **带入带出**（§21）：备用子 3 / 征召签 2 / 换型令 4 点，每局至多 1 件；名次点 24/16/12/10；完赛全额、弃赛 50% 并返还补给、出局（势力归零或战场落败）带出 0 且补给丢失。
- **拼接版冒烟**（F.5，`link:1`、标准 × 4、10 局）：大回合 10 / 11 / 15，首轮交战平均 5.3、决赛 3.2，等待过的人平均等 2.33 个大回合，AI 单小回合中位 47 ms。`ai-pass-review` 诊断（20 局）：整轮 Pass 结算落败者 48% 无可落之手、43% 无追平手、8.6% 有追平 / 反超手（已由决胜一手处理）；等待者在决赛吃亏（胜率 27.8% 对首轮胜者 68.2%）。

## 自检命令（Godot 用 `..._console.exe`，headless、顺序单跑、看真实退出码）

`$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"`，以下都是 `& $G --headless --path src/godot ...`：
- 构建 C#：`--build-solutions --quit`（Godot 在编辑器外读 **Debug** 程序集，只做 Release 构建会跑到旧代码）。
- `-- --auto-demo`：插旗 → 构筑 → 配对 → 拼接，`[flow]` 已见各步通过 + 开局对准自证，退出 0。
- `-- --auto-demo --pick-check --seed=5`：开局一轮拾取 42/42，本机参与首轮拼接后第二轮 78/78（`link-map-terrain` 后，棋盘变大）（缺省种子的本机不在首轮链接里，会以 1 退出并提示换种子）。
- `-- --map-select --auto-demo`：新局界面五步（非法种子 / 合法种子 / 换一张 / 回到进入时的种子 / 开始）再接演示；第 4 步节点守门为"建局后 = 进入时 + 主题名标注数"（新局界面背景不带主题名）。
- `-- --auto-demo --rounds=0 [--seed=5]`：跑到终局，`[flow]` 要求七步齐全（锁定 → 构筑 → 首轮配对 → 拼接 → 战场结算 → 决赛 → 终局），任一步不符退出 1。
- 换出生棋盘看取景：`-- --auto-demo --zone=<1–5> --seed=<S>`（F.6 已验 5 × 4 = 20 次全过）。
- 截图（非 headless）命令与人工试玩清单：`art/board-link-mode/README.md`、`art/link-map-terrain/README.md`（五主题、棋盘内岩石 `link:9`、拼接）。
- 规格：`openspec validate board-link-mode --strict`。

## 测试现状

- `dotnet build siege.sln --no-incremental` 与 Godot 工程（`src/godot/Siege.Godot.csproj`）均 0 警告 0 错误（`retire-legacy-rules` 5.5 实测，2026-10-16）。
- `dotnet test`：**2126 条，通过 2119，跳过 7**（`retire-legacy-rules` 质检补测后；跳过全是 Perf / Slow，用 `SIEGE_PERF=1` / `SIEGE_SLOW=1` 加 `Category` 过滤开启）。逐类迁移与对账见 `.trellis/tasks/10-15-retire-legacy-rules/migration.md`（质检 `check.md`；旧地图一轮见 `.trellis/tasks/archive/2026-10/10-13-retire-legacy-maps/migration.md`）。
- 决策序列基线：链接图 `link:1` + 固定配对脚本 + 标准难度、对局种子 1 / 2 / 3（截到第 12 大回合）；旧 v5 / board:1 基线已删除。`final-countdown` 起按缺省 K = 2 重钉（种子 1 / 2 / 困难种子 1 变、种子 3 / 简单种子 1 不变），改前五条原样另存 `*-k0.txt`，由 `Battlefield/决赛倒计时Tests` 以 K = 0 逐字节对照；依赖走法的既有测试夹具（`LinkModeFixtures.Options`、`SimFixtures.PinPreCalibration`、`LookaheadFixtures.LinkConfig`）写死 K = 0。专家 G1 黄金值与前瞻计时局面集钉在 `link:1` 种子 1–3。

## 旧地图（已删除）

- `retire-legacy-maps` 已删除：标准档 v5 / 2 人 / 3 人图、边疆档与 `gen:` 生成图、棋盘档 `board:`、`maps/*.json`、全部旧生成器与 `MapCatalog.Legacy`；规格档只剩链接档（地图文件 `Profile` 字段必填，`Standard` / `Frontier` / `Board` 读入即报"已移除"）；冒险概率与地图专属评价权重覆盖一并删除。
- 任何入口（图形版、`play`、`run`、`replay`、`map`）给旧标识都报错并说明只接受 `link:<地图种子>`；`map` 不给 `--map` 时打印 `link:1`，不再导出任何文件；批次配置的内部缺省也是 `link:1`。

## 后续 change（只登记，未建目录；设计文档 §19 同）

1. ~~AI 停手复核~~：已实施（`ai-pass-review`，诊断 + 决胜一手）。
2. ~~AI 视野收紧~~：已实施（`ai-seat-vision`，AI 评价版本 4 → 5）——验证 20 + 20 局：标准 × 4 对局中位 16 → 15.5 大回合、逐小回合落点差异率 40.2%、战场落败 / 到期结算次数不变、单小回合耗时中位 68 → 128 ms（投影开销）；1 专家 + 3 标准专家胜 7 / 20（35%）、平均名次 2.40，前瞻改变选择 12.9%，名册为空退化 30 次。
3. ~~retire-legacy-maps~~：已实施并归档（2026-10-13）。
4. ~~决赛开局势力差评估~~：已实施（`final-countdown`，K = 2）——验证 20 局：等待 ≤ 2 个大回合（原中位 6、最长 14），决赛开局势力差中位 418 → 19，等待者决赛胜率 20% → 25%，到期结算 16 次，对局中位 17.5 → 16 大回合；截图 `art/final-countdown/countdown-strip(-gray).png`（`--auto-demo --seed=5 --rounds=7 --screenshot=…:290`）。是否再调 K 待试玩。
5. **Godot 无头运行偶发崩溃观察**：一次 `Illegal instruction`（退出码 132）未复现；再现时抓崩溃日志（J12-7）。
6. ~~retire-legacy-rules~~：已实施（2026-10-16，v1.27，裁决 R1–R12 全缺省、R7 窄解；已归档 `2026-10-16-retire-legacy-rules`）——删 `MatchMode` 与全部 Legacy 规则分支：出生区保护期 3×3 限制与第 4 大回合全图解禁、整轮 Pass / 填满即终局（`EndReason.AllPassed` / `BoardFull`）、旧名次算法（`ComputeLink` 更名唯一的 `Compute`）、非盲选插旗、Legacy 存档恢复路径、视图与 AI 的模式门、Sim 读模式分支（L17，`Replayer` 拒绝非链接日志）；校验器删中央入口 / 咽喉与容差两条旧规则（L18，字段保留）；链接存档缺 K 回填 0 保留（W13 窄解）。手写 9×9 夹具测试迁到链接载体（主载体手写 8×8 平坦链接图 `link:90008`）：(a) 规则层 18 条、(b) 链接夹具 324 条；删 (c) 28 条 + (j) 合并 5 条，`MatchFixtures.cs` 删除；通过数 2150 → 2117（质检补测 Q1 / Q2 两条后 2119）。15 份基线、存档快照与 `link:1`–`link:20` 地图导出逐字节不变。
7. **AI 整批评价观察**（`ai-pass-review` P14）：逐枚贪心会漏掉"整批为正、逐枚为负"的组合（种子 D R5 整批 +323）。
8. **决胜一手拉长战场观察**（P15）：20 局均值 +0.4 大回合，个例 14 → 20。
9. **链接档新地表**（`link-map-terrain` M4，暂名 `link-map-surfaces`）：荒漠 / 沼泽 / 岩台 / 浅滩进入出生棋盘中间带。
10. ~~AI 地形与主题观察~~：已诊断（`ai-terrain-review`，裁决 T16 = E 维持现状）——AI 只立栅是评价的理性选择：一道栅栏能切小空区或隔开敌串而造眼（同落点加权分立栅 +876、搭桥 +286、烧林 +5.6），预筛漏掉的搭桥 / 烧林从未更好；五主题首轮胜率无显著失衡；三维权重只影响节奏。
11. **对局长度观察**（L1）：主题化地形后对局中位 15–16 大回合（原 11）、首轮交战约 10（原 5.3）；按试玩手感决定是否缩边长或缩构筑期。
12. **格线高度观察**（第 6 组）：格线画在 h=0 面上，高台上只能靠地砖缝看格界，深水上方悬一条格线；负责人看图后暂不立项。
13. **栅栏造眼是否太便宜**（`ai-terrain-review` T13，规则层观察）：人类玩家同样可用；试玩体会后再决定是否改规则（例如栅栏不参与眼空间判定）。
14. **Sim 贴地形小空区口径观察**（`retire-legacy-rules` G1）：Sim"贴地形小空区"统计把棋盘外的画布障碍格算作地形墙（链接局本已如此；`活形记录与统计Tests.活形确立可查` 在链接载体上钉为 2），待决定是否修口径。
15. **前瞻投影缺链接字段**（G2，已知缺口）：前瞻投影不填 `EliminationReason` / `PowerAtDefeat` / `WaitingRounds` 三个链接字段（只用于前瞻、不影响决策）。
16. **偶发失败观察**（G5）：`批量跑局Tests.已删除的选项报已删除(no-catch-up)` 偶发失败一次（控制台捕获为空），重跑通过。

另有未开工的 change `map-elements-v2`（地图元素造型精修，0/17）。2026-09-28 之后已归档：`recruit-panel-collapse`、`board-map`、`forbidden-marks`、`turn-settlement-show`、`settlement-show-callouts`、`ai-turn-speed`、`board-render-perf`、`show-sound-cues`，`board-link-mode`、`retire-legacy-maps`、`ai-pass-review`，以及本次的 `link-map-terrain`。

## 权威来源（按优先级）

1. `openspec/specs/`（Requirement / Scenario 是验收基准）与 `2026-09-10-siege-core-gameplay-design-v1.md`（**v1.27**，文末变更记录）。`Siege-玩法介绍-v1.docx` 是旧玩家向稿，与 md 冲突时以 md 为准。
2. `openspec/changes/archive/<日期>-<change>/` 的 proposal 裁决表与 design.md：设计文档没写到的以它判定（链接模式的全部裁决在 `board-link-mode` 的 proposal）。
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
- **单一实现**：几何四邻 `Adjacency.Neighbors`；气边 `LibertyNeighbors`；覆盖 `CoverageTargets` / `CoverageMap`；地图解析 `MapCatalog`；拼接坐标换算 `LinkLayout`（`Siege.Core.Board.Maps`）；格坐标换算 `GameBoard.CoordOfIndex`；结算顺序 `SettlementDriver`；地形写入 `TerrainWriter`；活形 `LifeShapeReport`；同形键 `GameBoard.SuperkoKey`；图形版命令行 `LaunchArgs`；Godot 侧格 ↔ 3D 映射 `BoardGeometry`。
- **确定性**：随机只用 `GameSeed` 子流（`relic-gen` / `recruit` / `setup` / `flag-pick` / `carry-ai` / `carry-draft:<n>` 等），互不扰动；配对不是对局内子流（`play` 真随机是守门测试登记的唯一例外）；禁止浮点计分，势力用 `BigInteger`。
- **信息边界**：公开视图按座位过滤、结构上不含私有字段；UI 不做规则计算。
- **测试**：Requirement 对应测试类、Scenario 对应测试方法；新守门必须做变异验证；Skip 只给 Perf / Slow。
- **数据口径**：入口遇到未识别输入必须报错，不得静默回退缺省值；规则一变权重先标"未校准"。坐标为围棋记法，`A1` 在左下，列字母跳过 `I`，第 26 列起双字母。

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
