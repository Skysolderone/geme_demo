# 10-19-open-map-a 实施记录（段 A）

## 与 design / 规范冲突（先记后做）

- C1 `boundaries.md` 单一实现表「链接档生成与标识」一行写"棋盘清单 `MapData.Boards` 不参与结算——保护期…都不读它"，`BoardPlate` 注释与 `棋盘清单Tests.清单不参与结算` 同口径。design D2 / D3 明确把 `BirthBoard(zone)` 定为构筑期合法范围与改造范围的几何基，因此段 A 按 design 执行：`PublicRules.LegalRange` / `EditScope` 读 `Map.BirthBoard(zone)`；`清单不参与结算` 按新口径改写（清空清单后对局在构筑期无法给出合法范围，改为"清单只影响构筑期范围，不影响气 / 提子 / 覆盖 / 计分"）。段 B 2.5 需同步 boundaries.md 该行与 `BoardPlate` / `BatchContext` 注释。

## 逐条证据

### 1.1 生成器初始布局紧贴（design D1 / A1 / A2）

- 新文件 `src/Siege.Core/Board/Maps/InitialLayout.cs`：纯函数 `Overlaps` / `SharedEdge` / `AttachNorth` / `Arrange`（不消费随机）；`LinkMapGenerator.Layout` 改为 `InitialLayout.Arrange(次序排列的 5 块, Margin)`，`Gap` 常量只剩 `Link` 读（段 B 删）。
- `MapValidator.ValidateSpliced`：`BATTLEFIELD_COUNT_MISMATCH` 改为"含出生棋盘的连通分量恰 1 个且含全部 5 块"（删"分量数 = 5 − 版本号"）；`ValidatePlateGaps` 保留（唯一战场上不触发）；链接行画布区间 `ColumnRange (20,85)→(22,30)`、`RowRange (8,24)→(16,20)`（3+2 的理论边界），可落子预算 170–320 不变。
- 实测（临时探针程序，种子 1–20）：画布宽 23–27、高 17–20，可落子 212–280，分量恒 1，校验全过；种子 1–2000：宽 22–28、高 16–20、可落子 184–300、0 张失败、每块第二行棋盘与锚的共享边 ≥ 4。
- **R-1（需负责人裁决，已按缺省继续）**：design D1 断言"x = o1.x + 2 总是合法候选"只排除了 o3，漏了 o2——o4 比 o1 宽且 o2 比 o1 高时，o1 北侧没有任何不重叠候选（同理 o3 比 o0 宽且 o1 比 o0 高时 o0 北侧放不下 o3）。实测按 D1 原文种子 1–400 有 21 张生成失败（MAP_TOO_TALL，塔状）+22 张退化。实施缺省：锚北侧放不下或只剩 < 4 格的缝时，改锚第一行三块里共享边最长者（并列取编号小者），仍是 3+2 两行、每块北贴第一行的一块。种子 1–20 里只有 `link:7` 的 o4 改锚 o2；1–2000 里 o3 / o4 换锚 413 张（20.6%）。建议：段 B 同步规格「初始布局」与设计文档 §3.3 写入该换锚规则。
- 测试：`初始布局Tests` 删「出生棋盘互不相邻」，新增「地图只有出生棋盘且连成一片」「每条接缝至少一条气边」「三加二布局」，「画布按布局裁出」区间改 23–27 / 17–20；`物化状态校验Tests`「校验失败中止」改写为「分量数不为一被拒」、新增「生成后校验」(1/7/20)、「接缝没有气边被拒」载体改为初始图 0–1 接缝（H/I 列）、「链接后校验」「未链接战场不相邻」Skip；`战场的定义与规则作用域Tests`「隔绝棋盘各成战场」改写为「开局即唯一战场」、「场景格不可改造」场景格由 (8,4)/(9,4) 改为 (20,4)/(21,4)、战场数 5→1。
- 夹具 `LinkMapFixtures.Births` 改为 3+2 紧贴（0 C3–H8、1 I3–N8、2 O3–T8、3 C9–H14、4 I9–N14，画布 22×16；棋盘 0 坐标不变），旧隔绝布局保留为 `IsolatedBirths` / `Isolated()` 只给不经校验的用例；`Linked01` 改基于 `IsolatedBirths`（只服务已 Skip 的拼接用例）。

### 1.2 合法落子范围按大回合两段（design D3）

- `PublicRules.LegalRange(board, birthZone, player, life, majorRound)`：构筑期（≤ `BuildProtectionRounds` = 3）= 所在战场可落子格 ∩ `Map.BirthBoard(zone).Contains`（同一可落子谓词，A3），第 4 起 = 战场（= 全图）；禁入扣除仍只在此一处（守门 `禁入扣除只在LegalRangeFor且与预演共用同一查询` 期望 `PublicRules.cs:LegalRange` 不变）。
- 调用点：`MatchFlow.LegalRangeFor`（当前大回合）、`ExpertLookahead.NextMoveContext`（`round = 当前 + 1`）、`ExpertLookahead.SimulatedContext`（模拟对手的行动大回合）。`BoardRenderer` 构筑期提示改为"只能在自己的出生棋盘落子"。
- 测试：`战场内合法落子范围Tests` 改写「范围随大回合扩大」「第4大回合进入对方棋盘」「无人棋盘可落子」「空盘面玩家重新进入」「禁入格不在范围内」（载体换到画布角上的棋盘 3，原棋盘 4 的左下角自 open-map 起与棋盘 3 相邻、不再是角），「构筑期内越区落子被拒」零改动；「等待者可落子」「等待者无法越界」Skip。`公开规则纯函数Tests` 补大回合实参。

### 1.3 构筑期内改造目标限自己棋盘（design D4）

- `PublicRules.EditScope(board, birthZone, majorRound)`：构筑期 = 出生棋盘全部格，否则全图（不扣禁入）；`MatchFlow.EditScopeFor` 只读查询，`EnterDeploy` 写进 `BatchContext.EditScope`（可空，null = 不限，旧上下文不变）；专家前瞻两处上下文同样带上。
- `TerrainEditRules.LegalTargets / IsLegal / Reject` 各加带 `scope` 的重载：格目标 ∈ scope、立栅边两端 ∈ scope，拒绝理由常量 `OutOfScopeReason` = "构筑期内改造目标须在自己的出生棋盘内"；枚举与拒绝同源。调用点：`BatchRehearsal.ValidateShape`（Reject）、`BatchPreviewBuilder.Build`、`HeuristicTurnController` 三处（都传 `context.EditScope`），调用点数不变。
- 守门测试：见 `TerrainEditing/构筑期内的改造范围Tests`（已有 4 条：跨接缝立栅被拒 / 候选不含棋盘外目标 / 第 4 大回合可跨接缝烧林 / 枚举与拒绝同源）。

### 1.4 可见性与占位（design D5）

- Core / Presentation 零改动：`PublishFor` / `SeatProjection` 的过滤边界仍是所在战场，开局即唯一战场 → 自然含全图；`DefaultBoardView.ForSeat` 的占位 `Expanded` 随可见格全真。
- 新增 `InformationVisibility/锁定后全图可见Tests`（他人棋盘从第1大回合起可见 / 按座位序列化含全部五块棋盘 / HUD视图模型不遮蔽任何玩家 / 全员Pass可见）。
- 原「其他战场不可见」类用例按反向重导：`按座位视图Tests`（他人棋盘锁定后即展开、他人势力可见、数据层含全图、多人同区锁定后全员互见、战场结算结果全局公开、岩石深水、归属三态）、`视图模型按座位过滤Tests`（锁定后全图可见_五块占位全部展开、序列化含全部出生棋盘的坐标、同区与异区都互见、信息层含全图的格、岩石 36→180）；`他战场玩家数值不进HUD视图模型Tests` 四条遮蔽用例 Skip（反向由新文件覆盖），「同战场不遮蔽」保留。

### 1.5 测试整理与基线重钉（design D9 / A4 / A8）

**整文件 Skip（`open-map 段 B 删除`，不改断言）**：
- Battlefield：配对事件由对局外部提供Tests（11 Fact + 1 Theory）、决赛倒计时Tests（7+1）、链接事件Tests（6）、链接执行Tests（4）、链接后的公开补充载荷Tests（2）、链接模式端到端Tests（1 Theory）、阶段推进Tests（10；新流程的「第 4 大回合不发生任何事件」「构筑期内整轮 Pass 即终局」由 自动演示全流程自检Tests / 两类终局条件Tests.构筑期内整轮Pass即终局 守）
- LinkMapGeneration：链接时的棋盘拼接Tests（7）
- SettlementShow：棋盘展开节拍Tests（9+1，拼接演出）、战场结算横幅Tests（5）、决赛倒计时提示Tests（5）
- MatchTelemetry：决赛倒计时的记录Tests（4）、战场落败复盘与等待者评估Tests（7）、坐标随链接平移Tests（3）
- MatchSetup：链接模式存档Tests（10）、决赛倒计时配置Tests（4）
- SimulationHarness：可复现回放_链接模式Tests（8+1）

**按方法 Skip（同一文案）**：链接模式平衡分析Tests（配对与链接可查 / 等待时长可统计 / 真实跑局的链接日志可完整分析 / 战场结算可查）、链接模式对局记录Tests（配对与链接可查 / 锁定旗数与等待时长可查）、对局日志的记录内容_链接模式Tests（地形可离线重建 / 链接那一小回合的改造按链接后布局记 / 配对与链接可查 / 等待时长可查 / 日志往返且配对可从日志重建）、各入口支持生成图_链接模式Tests（批量配对可重复 / 回放重建链接 / map子命令模拟链接 / map子命令按阶段推断贴边_也接受显式贴边）、终端对局Tests（终端链接局跨过拼接后小回合播报不崩溃且不误报）、行动顺序跨战场统一Tests（等待者在序）、链接档地图标识与确定性Tests（同链接序列同最终图）、链接模式终局名次Tests（对局里战场落败者按结算时的比较量排名）、先手顺序预测_链接模式Tests（期限大回合的预测剔除将到期落败者 / 期限大回合最后一人Pass前的预测与实际顺序一致 / 填满判定同样进预测）、他战场玩家数值不进HUD视图模型Tests（4 条遮蔽用例）、按座位视图Tests（链接后立即可见）、视图模型按座位过滤Tests（链接后合并战场的格一起可见）、战场内合法落子范围Tests（等待者可落子 / 等待者无法越界）、战场结算Tests（等待者Pass不结算 / 倒计时到期强制结算 / 他战场有人落子不妨碍本战场整轮Pass结算 / 出局者出序）、两类终局条件Tests（决赛结算即终局）、前瞻中的下一名对手Tests（同战场无对手时退化）、链接模式下落后者的停手阈值Tests（等待者照常停手）、AI改造与主题诊断Tests（主题表现可查）、按主题统计Tests（匠人改造…坐标随链接换算 / 公共信物个数…）、随机子流隔离_链接模式Tests（配对不扰动子流）、物化状态校验Tests（链接后校验 / 未链接战场不相邻）、自动演示全流程自检Tests（配对记录来源必须是脚本 / 决赛链接须在首轮之后… / 链接后可见格数等于本战场格数 / 战场结算三种口径）。

**按新载体重导的字面期望**（除 1.1–1.4 已列者）：
- 战场结算Tests：整轮Pass结算（名单 4 人、结算即终局）、填满结算（在局 3→1）、三人战场结算（4 人名单）、减员结算（改为"只剩 C、D 时 D 弃赛"）、势力归零的减员结算（载体改画布角上的棋盘 3；不再触发减员结算）、弃赛后按剩余在局者判整轮Pass（3 人名单、终局）、构筑期也结算（终局）、全零局面（4 人、名次 [1,2,2,2]）。
- 两类终局条件Tests：唯一参赛者获胜（两名出局者改由势力归零产生）、非最后战场的整轮Pass不终局 → 构筑期内整轮Pass即终局。
- 先手顺序预测_链接模式Tests：质检局面（预测含四人）、预测与大回合结束后的实际顺序一致（P3 落子后末位 Pass 不结算）、此刻结束即终局时没有预测（填满 5 块）。
- 前瞻中的下一名对手Tests：他战场玩家不进名册 → 不同棋盘的玩家进名册（构筑期模拟对手范围 ⊆ 其棋盘）。
- 正式对战AI的信息边界_链接模式Tests：候选不越战场 → 构筑期候选不越棋盘_第4大回合候选可全图；全知例外集中可关 / 缺省按座位视野 → 全图可读（两条路径相同）。
- 链接模式下的决胜一手Tests：对手 = 全部其他在局玩家（P1/P2/P3 先 Pass）；追平算例 P0 4 + 5 = P1 9；并列算例 P1 改 (0,1)。链接模式下落后者的停手阈值Tests.并列最高：P1 改东北角。
- 对局日志的记录内容_链接模式Tests.战场结算可查：结算 3 → 1。链接图批量跑局：断言无配对 / 链接事件。终端对局 / 终端专家对局：落点 X3 → R3（link:1 的 1 号棋盘左下角）。盘面派生量提速对照Tests.Maps：拼接样本改为 link:1 / link:2 / link:7 初始图。批量跑局Tests.批量执行并汇总：跨到第 4 大回合允许"有出局事件"。百局端到端Tests：战场落败出局不要求曾建立正势力。棋盘台面的可视表现Tests：岩石在任一棋盘内、场景格取 A1。各入口按地图标识选图(_链接模式)Tests / 各入口支持生成图_链接模式Tests：终端文案"5 块紧贴成一张连通地图的出生棋盘"、不再有"其他战场（势力不可见）"、列标为 5 块包围盒。地图文件往返Tests：U6 / M5 / J10。公开规则纯函数Tests：样本种子改为"从给定种子起第一个含军令 / 工坊 / 先锋的对局种子"。
- 黄金值 / 哈希：候选格上限Tests.LinkGoldenTurnHash、停手阈值Tests.StrictImprovementTurnHash、专家难度的一层前瞻Tests.G1Golden ×3、平衡分析方向Tests 两份报告哈希、出生区信物权重Tests（未收敛 113→114 + 哈希）、专家前瞻的记录Tests 小回合数（45→84、65→80）——见下文重钉记录。

**黄金值 / 哈希重钉记录（link:1 digest 03AEAD07… → 728C79E4626795561728E10A9D0F61543B7F45DC333E820A2B912C2AA09CE98E）**：
- 决策序列基线 5 份（`SIEGE_REBUILD_DECISION_BASELINE=1`，截到第 12 大回合，无 P / L 行），`*-omniscient.txt` / `*-k0.txt` 由缺省文件原样复制，新增 `决策序列基线Tests.全知与K0副本与缺省逐字节相同` ×5 守住；样本口径断言改为"无配对 / 链接行、含第 4 大回合"。
- `候选格上限Tests.LinkGoldenTurnHash`：1CFBFF61… → 8A03DA5EA2B81E5D233B55145161AC101498D71FFD976E9B02CF0E225C1CD1BE（种子 31、24 小回合）；三处调用改经 `AssertLinkGolden`（失败时打印完整哈希）。
- `停手阈值Tests.阈值为0时零变化`：种子 31 在阈值 0 / 20 下走法相同（探针种子 31–46：34 / 35 / 37 / 42 / 44 / 45 不同），改种子 34，钉 D0EB62C1AFA4432791B0283985210D4C09A1DC0529BCAF6598FFF562016630A2；"反面"改为实跑缺省阈值局比对。
- `专家难度的一层前瞻Tests.G1Golden`：种子 1 / 2 / 3 → (24, 0BEAB462…, 8556, 12, D5D6C56B…) / (24, D8A9DF81…, 8556, 12, E560CE02…) / (24, 17DC195B…, 8556, 12, B06D5D80…)。
- `平衡分析方向Tests`：Sample 65989D85FBCD0A4242831076A7BB0E7285D17A55A8621A35CF08D2693621DCC7（RankedSample 见下一轮）。
- `出生区信物权重Tests`：未收敛 113 → 114，哈希 AEA0BA6CE5ADEB7B31EC3C7068B76B61DCAAB9FEFB5A713DDCAC5D3DA0FCE281。
- `专家前瞻的记录Tests`：一层样本 45 → 84 小回合、前瞻记录 10 → 21；非专家样本 65 → 80 小回合（哈希见下一轮）。

- **R-3（需负责人裁决，已记录、未处理）**：唯一战场上 AI 对局可能不收尾。`终端的带入选择弃赛与结算显示Tests.弃赛命令`（link:1、种子 43、3 名 Easy AI、写死权重 / 阈值 80）在 open-map 下挂住整个测试宿主：探针显示 75 s 内跑过 180 个大回合，三名 AI 在同一片格（U3 / W3 / U4 / W4 …）互相提子循环——盘面同形禁则只禁完全重复的局面，三人轮流提子的局面不重复，整轮 Pass / 填满 / 只剩一人三条终局都到不了；`PlayCommand` 没有跑局层的小回合截断。探针种子 40–52（各限 35 s）只有 42 / 46 / 47 能终局；测试改用种子 42。旧版隔绝棋盘 36–64 格时同一现象很快被填满终止。建议段 B 或另开 change 裁决：终端 / 图形版是否加"小回合上限"类硬停，或 AI 在循环提子时停手（属 AI 评价改动，段 A 不碰）。冒烟（1.7）的 `turn_limit` 截断率是该问题的量化。

**1.5 全量结果**：`dotnet test tests/Siege.Core.Tests -c Release` → 总 2167、通过 2006、跳过 161（7 条 Perf / Slow + 154 条 `open-map 段 B 删除`）、失败 0（RC = 0，3 m 30 s）；`dotnet build siege.sln -c Release` 0 警告 0 错误。
- 中途两次全量被 `终端的带入选择弃赛与结算显示Tests.弃赛命令` 挂住（见 R-3），`--blame-hang` 定位后换种子解决。
- 新增守门：`TerrainEditing/构筑期内的改造范围Tests`（4 条）、`InformationVisibility/锁定后全图可见Tests`（4 条）、`决策序列基线Tests.全知与K0副本与缺省逐字节相同`（5 条）、`初始布局Tests` 三条新 Scenario（各 20 种子）、`物化状态校验Tests.分量数不为一被拒 / 生成后校验`、`自动演示全流程自检Tests.真对局走一遍_四步全通过 / 全图解禁`。
- 变异验证（实跑，每项只跑目标测试类，套用后原文件原样写回并刷新时间戳）：
  - M-O1 `InitialLayout.Arrange` 第一行间距改 1 → `初始布局Tests` 红 156 / 绿 6（三加二布局 / 地图只有出生棋盘且连成一片 / 每条接缝至少一条气边 / 画布按布局裁出 全红，其余因生成器自检抛异常连带红）。
  - M-O3a `TerrainEditRules.Reject` 立栅分支不看 scope → `构筑期内的改造范围Tests` 红 2（构筑期内跨接缝立栅被拒 / 候选枚举与拒绝理由同源）、绿 2。
  - M-O4 `MatchFlow.PublishFor` 锁定后可见格改回只取本座位棋盘 → `锁定后全图可见Tests` 红 3（他人棋盘从第1大回合起可见 / 按座位序列化含全部五块棋盘 / HUD视图模型不遮蔽任何玩家）、绿 1（全员Pass可见——Pass 名单经 SeatProjection 另一条路径）。
- 重跑全量（协调要求，单进程）：总 2167、通过 2006、跳过 161、失败 0，RC = 0（2 m 15 s）。

### 1.6 表现层与图形版（design D7 / A6 / A7）

- `LinkDemoAudit.FullFlow` 改四步（插旗锁定 / 构筑期 / 全图解禁 / 终局）：`FlagsLocked` 要求 5 块占位全部展开、可见格 = 全图各棋盘外接矩形之和；新增 `Unlocked`（范围 ⊆ 全图可落子、范围 ∪ 禁入 = 全图可落子、范围数 > 自己棋盘可落子数、大回合 ≥ 4）；`Ended` 接受最后战场结算 / 只剩一名参赛玩家；`Pairing / Linked / Settled` 保留为死代码。
- `GameRoot.cs`：第 4 大回合首次轮到本机喂 `Unlocked`（禁入格读按座位视图模型的 `LifeForbiddenBy`）；`--pick-check` 锁定后全图一轮通过即退出，删"等本机参与的链接"失败分支；`FinishAutoDemo` 注释改四步。`BoardView.cs` 零改动：按座位视图锁定后已含全图，渲染 / 取景 / 底座随之覆盖整张地图（截图核对）。
- Godot `--build-solutions`：C# 工程构建 DONE（`dotnet build src/godot/Siege.Godot.csproj` 0 警告 0 错误），但编辑器进程在 `--quit` 后未自退（600 s 超时后杀掉；不影响后续四条自检）。
- 四条 headless 自检全部退出 0：`--auto-demo`（link:1 缺省种子：锁定 218/218、构筑期 42 格 ×3、全图解禁 214 = 214 − 0、自检 5 条 0 失败）；`--auto-demo --pick-check --seed=5`（锁定后全图 214 格一轮拾取 214/214 通过即退出）；`--map-select --auto-demo`（新局界面五步 + 锁定 238/238、构筑期 47/48、全图解禁 225 = 230 − 5）；`--auto-demo --rounds=0 --seed=5`（第 26 大回合 LastBattlefieldSettled 终局，四步齐全，自检 8 条 0 失败，启动到终局 34.8 s / 289 帧）。
- 截图 `art/open-map/locked-full-map.png`（第 1 大回合、5 块全展开、接缝连片、底座覆盖包围盒）与 `round4-cross-board.png`（第 10 大回合，四色棋子遍布 5 块）+ README 人工检查清单。

### 1.7 冒烟与交付（design D10）

- 报告 `smoke.md`（含统计脚本口径与 `analyze` 节选，完整报告 `analyze-all.txt`）：20 局全部规则终局、`turn_limit` 截断 0/20 = 0%；大回合中位 24 / 最小 20 / 最大 29；单小回合耗时中位 72 ms；五主题平均名次 Plateau 2.37 / Mixed 2.50 / Palisade 2.52 / River 2.53 / Forest 2.64。不调参。
- HANDOFF：「一句话」与「怎么玩」（图形版流程行、终端版一行）改为新流程；其余段落（自检命令、测试现状等）留段 B 整体改写。
