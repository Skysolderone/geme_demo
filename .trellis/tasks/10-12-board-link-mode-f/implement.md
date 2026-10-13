# 10-12-board-link-mode-f 段 F 实施报告（F.1–F.2：Core 生成与拼接）

对应 `openspec/changes/board-link-mode/tasks.md` F.1、F.2（已勾上）。规格权威：`link-map-generation`（拼接版）、`battlefield`「链接事件」、design D2、proposal 裁决 A / J1–J7。F.3–F.5 未动。

## 1. 勾选与测试类（`tests/Siege.Core.Tests/`）

| task / Scenario | 状态 | 测试 |
|---|---|---|
| F.1 初始布局（J4 一字排开、J2 双信物、画布四周 2 格裁出一次） | 完成 | `LinkMapGeneration/初始布局Tests`：初始图只有出生棋盘 ×20、出生棋盘互不相邻 ×20、画布按布局裁出 ×20 |
| F.1 标识与确定性 | 完成（改写） | `LinkMapGeneration/链接档地图标识与确定性Tests`：同种子同初始图、同链接序列同最终图（步骤带贴边）、对局种子不影响地图（源码扫描照旧） |
| F.1 `MapValidator` 链接行 | 完成 | `LinkMapGeneration/物化状态校验Tests`：链接后校验、校验失败中止（两个未链接战场相邻 → `BATTLEFIELD_COUNT_MISMATCH` 指出两者）、未链接战场不相邻（角对角 → `BOARD_TOO_CLOSE`）、每块出生棋盘恰两个信物格（`BIRTH_BOARD_RELIC_COUNT`）、可落子格都属于棋盘_场景格不可落子（`SCENERY_CELL_PLAYABLE` / `BATTLEFIELD_WITHOUT_BIRTH_BOARD` / `BOARD_OVERLAP`）、规格档读写往返、非链接档不可增长、规格档不改规则 |
| F.2 「链接时的棋盘拼接」五个 Scenario | 完成 | `LinkMapGeneration/链接时的棋盘拼接Tests`：首轮拼接、共享边最长（手搭"高低不齐"夹具，并列取偏南）、决赛拼接、全部链接序列都能拼接（种子 1–20 穷举三类 55 条序列）、平移保内容（GameBoard + RelicLedger 层：3 枚棋子 / 1 道栅栏 / 1 枚已揭示信物） |
| F.2 `battlefield`「链接事件」 | 完成 | 数据层 `Battlefield/链接事件Tests`：链接合并战场、链接不改内容、接缝两侧朝间隙的边界栅栏合并为一条、链接可重放、ApplySplice拒绝不是逐格搬运的地图；对局层 `Battlefield/链接执行Tests`：链接不改内容（含两次链接的向量合成与占位原点）、链接合并战场、决赛贴北侧、链接可重放 |
| F.2 存档 | 完成 | `MatchSetup/链接模式存档Tests`（既有 8 条照过；「链接重放与存档不符即拒绝」加篡改平移向量 / 贴边两条断言） |
| F.2 账本副本 | 完成 | `Battlefield/链接后的公开补充载荷Tests`（改写为"拼接后揭示被平移的信物"两条 + 旧模式对照） |
| 段 B 端到端 | 照过 | `Battlefield/链接模式端到端Tests.从插旗跑到终局` ×5（含第 4 大回合存档往返） |
| 链接图决策序列基线 | 重钉 | `AiDecision/决策序列基线_链接模式Tests`（夹具 `decisions-link1-seed3.txt` 按原口径 `SIEGE_REBUILD_DECISION_BASELINE=1` 重钉，见 §3） |

### 删除 / 改写的旧测试（逐条理由）

| 测试 | 处置 | 理由 |
|---|---|---|
| `LinkMapGeneration/链接时的公共棋盘生成Tests`（首轮链接生成公共棋盘、决赛链接、全部链接序列都能生成、链接序列清单覆盖三类、退化为直通道） | 删除，由 `链接时的棋盘拼接Tests` 取代 | 全部绑定公共棋盘 / 通道 / 退化 / 备选棋盘对（R1 / R3 / R4 / R6 作废） |
| `LinkMapGeneration/槽位图案诊断Tests` | 删除 | 槽位白名单（R6）作废 |
| `初始布局Tests.出生棋盘间距`（≥ 15）、`画布固定且不超过校验器上限`（73×73）、`槽位图案每一对棋盘都能接上` | 改写为互不相邻（≥ 3）、画布按布局裁出 | 73×73 槽位模型作废（J4） |
| `物化状态校验Tests.校验失败中止`（2 格宽通道）、`无出生棋盘的分量即校验失败`（孤立公共棋盘）、`可落子格都属于棋盘或通道` | 改写为相邻未链接战场 / 游离可落子格 / 重叠 / 信物数 | 没有通道与公共棋盘 |
| `链接事件Tests.链接不动已有格`、`ApplyLink拒绝改写已有格的地图` | 改写为链接不改内容（平移）、`ApplySplice` 拒绝非逐格搬运 | J3：坐标在链接时允许变化，"只增不改"核对换成"逐格搬过去"核对 |
| `链接执行Tests.公共棋盘信物由链接子流生成并追加进账本`、`信物子流独立`、`信物内容只由对局种子与战场对决定` | 删除 | J2：`relic-link` 子流、`GenerateLinked`、`RelicLedger.Append` / `LinkedPlacements` 已删 |
| `链接后的公开补充载荷Tests` 三条 | 改写为"拼接后揭示被平移的信物" | 公共棋盘信物不存在；缺陷模式改为"账本坐标已平移、副本不得按开局记录重建" |
| `链接模式存档Tests.AssertSameState` 链接记录字段、`LinkedPlacements` 比对 | 改为比对新 `LinkRecord` 文本 | 字段变化 |
| `对局日志的记录内容_链接模式Tests.配对与链接可查` 的 `CorridorCells > 0` / `PublicBoard` / `NewRelics` 断言 | 改为断言中性值与 `Summary` 含"拼接" | 退役字段；日志 schema 属 F.3 |
| `对局日志的记录内容_链接模式Tests.地形可离线重建` | **Skip**（注明 F.3） | 改造记录坐标按发生时布局解释（design D2 / J3），离线重建要靠日志里的平移向量 |
| `链接模式对局记录Tests.配对与链接可查` 的 `CorridorCells > 0` / `Degraded == PublicBoard is null` / `NewRelicCells` | 改为共享边 ≥ 1、锚点零向量、被贴方非零、贴边序列 E/E/N | 字段变化 |
| `链接模式平衡分析Tests.配对与链接可查` 的退化率 / 通道长度断言、`真实跑局的链接日志可完整分析` 的"链接新增信物进统计"块 | 改为退化 0、通道空、`AllRelics == Header.Relics` | 没有退化 / 通道 / 新增信物；分析段改口径属 F.3 |
| `各入口支持生成图_链接模式Tests.map子命令打印链接初始图` 的 `73×73` | 改为读初始图尺寸 | 画布随布局 |
| `视图模型按座位过滤Tests.链接后合并战场的格一起可见`（多出公共棋盘格、3 块棋盘） | 改为格数 = 两块棋盘之和、2 块棋盘 | 拼接不新增格 |
| `战场内合法落子范围Tests.链接后可进入公共棋盘` / `范围随链接扩大`（`>`） | 改为"链接后可进入对方棋盘"、`==` | 同上 |
| `按座位视图Tests.链接后立即可见`（旧坐标）、`数据层过滤`（`> 100`）、`插旗阶段的可见信息`（`Single` 信物） | 坐标经 `LinkModeFixtures.Track` 追踪、阈值 60、信物 2 | J3 / J2 |
| `盲选插旗Tests.选区不扰动其他随机` 的 `Relics.Coords.Count() == 5` | 10 | J2 |
| `SettlementShow/棋盘展开节拍Tests.首轮链接演出_四个节拍依次`、`他人链接不演出` | **Skip**（注明 F.4） | 公共棋盘升起的节拍不存在；J7 下他人链接会平移本战场，`LinkShowBeats.Expansion` 的按格差分会误判——F.4 改为按向量差分（J5 滑入贴合） |
| `盘面派生量提速对照Tests` 的三次 `Link(...)` 调用 | 加贴边参数 | 签名变化；断言不变 |
| `战场的定义与规则作用域Tests` 的夹具坐标 | 按新夹具布局改坐标 | `LinkMapFixtures` 重写（间距 3、2 信物、原点固定） |

Legacy 既有测试断言一条未改；v5 / board:1 决策序列基线、黄金值、日志哈希全部照过。

## 2. 文件清单

新增：
- `tests/Siege.Core.Tests/LinkMapGeneration/链接时的棋盘拼接Tests.cs`

修改（Core）：
- `Board/Maps/LinkMapGenerator.cs` — 整体重写：`LinkSide`、`BattlefieldShift`、`LinkResult`（`Map / Side / Anchor / Mover / SharedEdge / Shifts / Summary / Translate`）、`LinkStep` / `LinkSequence` / `Sequences`（带贴边）；`Generate(seed)`（一字排开、2 信物、四周 2 格裁出一次，按种子缓存）；`Link(current, X, Y, side)`（锚点 = 标识小者；贴东 / 北；共享边最长并列偏南 / 偏西；其余战场按当前最西列升序重排到合并战场东侧、间距 3、底边不动；画布东 / 北外留 2、原点固定；被贴方落到原点之外抛 `MapGenerationException`）。删除槽位 / 白名单 / 公共棋盘 / 通道 / 退化 / 生成期穷举核对与 `LinkCorridor` / `GeneratedLinkMap` / `LinkVerification`。
- `Board/Maps/MapRandom.cs` — `ForLinkAttempt` → `ForLinkLayout(seed)`（只由地图种子决定）；删 `ForLink` 与 `LinkPairDomain`、`LinkAttemptDomain`（保留的 `LinkDomain` / `LinkLayoutDomain` 与其余域常量不同）。
- `Board/MapValidator.cs` — 链接行：可落子 125–245、信物 10–10、边长 5–7、`MinGap: 1`、`BirthRelics: 2`、列 20–140、行 8–32；`PlateRules` 加 `BirthRelics`；`ValidatePlateList` 单分支（`spliced` 局部变量，`PlateConnectivity.*` 表外仍恰 1 次）；`ValidatePlateGaps` 对同一战场的棋盘跳过；新 `ValidateSpliced`（游离格、重叠、出生棋盘信物数、分量含出生棋盘、分量数 = 出生棋盘数 − 版本号）取代 `ValidateComponentsHaveBirthBoards`。
- `Board/GameBoard.cs` — `ApplyLink` / `LinkMismatch` / `SameTerrain` → `ApplySplice(spliced, move)` + `SpliceTargets`（逐格搬运核对）+ `MoveEdit`（改造迁移规则；接缝两侧朝间隙的两道边界栅栏平移后为同一条边，按边去重保留先出现者——advisor 复核指出的崩溃路径：`TerrainWriter.ApplyAll` 对重复边抛“该边已有栅栏”）；`_occupants` 不再 `readonly`（画布重裁换新数组）。
- `Relics/RelicLedger.cs` — 删 `_linked` / `LinkedPlacements` / `Append`；加 `Translate(move)`、`ContentAt`；`Copy` 改为逐枚复制活状态（私有拷贝构造）；`RelicState.MovedTo / Clone`。
- `Relics/RelicGenerator.cs` — 删 `GenerateLinked`。`Determinism/GameSeed.cs` — 删 `RelicLink`。
- `Match/Pairing.cs` — `LinkRecord(Sequence, PairingSequence, X, Y, Side, MapVersion, SharedEdge, Shifts, Summary) { MapDigest }` + `ShiftOf(id)`。
- `Match/MatchFlow.Link.cs` — `ExecuteLink(x, y, pairingSequence, side, replay)`：`Link` → `Board.ApplySplice` → `Relics.Translate`；`ResolvePairing` 按 `PairingKind` 传贴边。
- `Match/MatchFlow.Persistence.Link.cs` — Link 段条目加 `Side / SharedEdge / Shifts`（`BattlefieldShiftSaveData`）、删 `NewRelicCells`；恢复按记录贴边重放并核对版本、共享边、摘要、向量。
- `Match/MatchDebugAccess.cs` — `Link(x, y, side = East)`。`Match/SeatPublicView.cs` — 占位改读 `Board.Map.BirthZones`。`Ai/MatchDebugView.cs` — `ContentOf` 改读账本当前坐标。

修改（Sim / Godot，最小适配，不含 F.3 / F.4 功能）：
- `Siege.Sim/Running/MatchSession.cs` — ① 日志条目退役字段填中性值（`Degraded=false`、`PublicBoard=null`、`CorridorCells=0`、`CorridorLengths=[]`、`NewRelics=[]`）；② `RecordTurn` / `LifeEntry`：本小回合内发生拼接时，先把拼接前盘面的格沿本回合链接记录平移再做落子 / 提子 / 活形差分（否则按旧坐标索引新盘面越界——所有跑局类测试的共同根因）；无链接时与原先逐格相同，旧模式日志不变。
- `Siege.Sim/Analysis/BalanceAnalyzer.cs` — `Selection` 对快照 / 揭示里查不到的信物坐标 `TryGetValue` 跳过（信物随拼接平移后与首部坐标对不上；按向量对齐属 F.3）。
- `Siege.Sim/Program.cs` — `map --links` 传贴边（两战场都只含一块出生棋盘按东、否则按北——占位口径，F.3 定）；去掉"退化"字样。`Siege.Sim/Play/PlayCommand.cs` — 文案改为拼接。`godot/scripts/GameRoot.cs` — 去掉 `Degraded` 字样。

其余：`tests/.../LinkMapFixtures.cs`（重写：41×9 五块 5×5 间距 3、各 2 信物、`Linked01` 为手搭拼接态、`Build(id, boards, mapVersion)`、`Gap`）、`LinkModeFixtures.Track`、`AiDecision/Fixtures/decisions-link1-seed3.txt`（重钉）、`openspec/.../tasks.md` F.1 / F.2 勾上。

## 3. 构建、测试、变异、基线

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2376 条，通过 2363，跳过 13，失败 0**（跳过 = 原 10 条 Perf / Slow + 本段 3 条注明 F.3 / F.4 的 Skip）。
- 链接图决策序列基线 `decisions-link1-seed3.txt`：拼接改变了地图（摘要 `BF39…` → `7A69…`）、信物数（5 → 10）与链接后坐标，必然重钉；口径不变（`link:1`、种子 3、脚本缺省配对、4 名标准 AI、截到第 12 大回合）。新基线 55 行：链接 (0,1)E 共享边 5、(2,3)E 共享边 6、(0,2)N 共享边 5；对局在第 12 大回合以 `LastBattlefieldSettled` 结束、41 个小回合——比旧几何短得多（F.5 冒烟会看到）。同种子重跑逐字节相同照过。
- 变异验证（脚本 `mutate.py`：二进制备份 → 锚点按文件行尾归一且恰命中 1 次 → 只建测试工程 → 过滤跑 LinkMapGeneration + Battlefield + 链接模式存档 + 按座位视图 → finally 还原并逐字节校验 → `os.utime` → 还原后确认红 0 / 绿 160）：

| 编号 | 变异 | 红 |
|---|---|---|
| M-F1 | 生成器第二信物格候选不排除同行同列（可相邻） | 13（`初始图只有出生棋盘` 多个种子） |
| M-F4 | 拼接并列取偏北 / 偏东（`>` → `>=`） | 1（`共享边最长`） |
| M-F6 | 拼接不平移信物格 | 45 |
| M-F7 | 校验器不报 `BATTLEFIELD_COUNT_MISMATCH` | 1（`校验失败中止`） |
| M-F8 | 校验器不查出生棋盘信物数 | 1（`每块出生棋盘恰两个信物格`） |
| M-F9 | 校验器对不同战场也不查间隔 | 1（`未链接战场不相邻`） |
| M-F10 | `ApplySplice` 不迁移改造列表 | 4（两处`链接不改内容`、`链接后立即可见`、`平移保内容`） |
| M-F11 | `SpliceTargets` 不核对可落子格总数 | 1（`ApplySplice拒绝不是逐格搬运的地图`） |
| M-F12 | `ExecuteLink` 不调 `Relics.Translate` | 34 |
| M-F13 | 决赛也贴东侧 | 1（`决赛贴北侧`） |
| M-F14 | `RelicLedger.Copy` 改回按生成记录重建 | 2（公开补充载荷两条） |
| M-F15 | 存档恢复不比对平移向量 | 1（`链接重放与存档不符即拒绝`） |
| M-F16 | 座位视图占位改读开局图 | 首跑 0 → 补 `链接执行Tests.链接合并战场` 的占位断言后红 1 |
| M-F17 | `ApplySplice` 迁移改造不按边去重 | 1（`接缝两侧朝间隙的边界栅栏合并为一条`） |

## 4. 拼接几何统计（种子 1–20 穷举：四人四区 15×3 步 + 2+1+1 30×2 步 + 3+1 10×1 步，去重 1600 次拼接）

- 共享边分布：首轮东贴 5:725、6:538、7:204（= 两块矩形较矮者的高）；决赛北贴 10:38、11:58、12:23、13:11、14:3。全部 ≥ 1、全部通过校验、合并后格数 = 参与战场之和、锚点逐格不变、被贴方单一向量。
- 画布：初始 41–51 × 9–11；拼接后宽 **31–111**、高 9–18。宽到 111 是 J7"未参与链接的战场全部重排到合并战场东侧"的直接后果（锚点越靠东、每次链接把其余战场推得越远），见 J8-1。
- 合并战场可落子格 50–182；为腾位置而平移的其他战场 2402 次（几乎每次链接都有，含同一配对事件里第一对合并出的战场被第二次链接再次东移）。
- 按裁决摆法（并列偏南 / 偏西）落到画布原点之外：**0 次**（该情形实现为抛 `MapGenerationException`，见 J8-3）。

## 5. F.3 / F.4 接线点

- `LinkRecord`：`Sequence, PairingSequence, X（锚点）, Y（被贴方）, Side（East / North）, MapVersion, SharedEdge, Shifts: ImmutableArray<BattlefieldShift(Battlefield, Dx, Dy)>, Summary, MapDigest`；`ShiftOf(id)`。`Shifts` 含拼接前每个战场一条（锚点零向量；其余为腾位置而东移的也在内）。存档 Link 段同结构（`BattlefieldShiftSaveData`）。
- 演出（F.4，J5）：对本机战场 = X 时，被贴方 Y 的格按 `ShiftOf(Y)` 从旧坐标滑到新坐标；本机 = Y 时本机自己被平移（锚点不动的是对方）——演出要在相对坐标系里做，或把相机跟到新坐标。**同一配对事件两次链接时，第一对合并出的战场会被第二次链接当作"其余战场"再东移**（`Links[1].ShiftOf(Links[0].X)` 非零），表现层要把同一帧里两条记录的向量合成（`LinkModeFixtures.Track` 是参考写法：沿记录链平移，被贴方合并后标识变为锚点标识）。
- 坐标变化的消费者：`SeatPublicView.Placeholders` 已改读当前地图；`DefaultBoardView` / `BoardView` 按格重搭即可，但 `LinkShowBeats.Expansion` 的"新增格 = after − before"在 J7 下会把平移误判为新增（两条 Skip 的节拍测试），F.4 需改为用向量映射 before 再差分。相机重定界读合并战场的外接矩形；画布宽可达 111，底座若按整张画布铺会很空，建议按战场铺。
- 日志（F.3）：`LinkLogEntry` 退役字段现填中性值，需换成 `Side / SharedEdge / Shifts`；回放 / 离线重建按"链接前坐标按链接前布局解释"逐条平移改造与落子（`MatchSession.RecordTurn` 里已有本回合内的平移写法可搬）；`analyze` 的通道长度 / 退化率行改为平移向量统计；`map --links` 的贴边口径要定（现占位：双单棋盘东、否则北）。
- 信物：只有开局 10 枚；`RelicLedger.Generation` 保留开局坐标（历史），当前坐标以账本为准（`Coords` / `PublicStates` / `ContentAt`）；Sim 首部 `Relics` 与快照坐标会对不上，F.3 决定首部是否补写平移后的坐标。

## 6. 偏离与需裁决（编号从 J8 起）

三类性质：**J7 字面执行的后果**（J8-1、J8-2，附穷举数字，不是假设）；**响亮失败、口径未定**（J8-3，穷举 0 次命中）；**自行取的缺省，可按裁决改**（J8-4、J8-5、J8-8、J8-9）；**规格文字 / 区间待同步**（J8-6、J8-7）。

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| J8-1 | **画布向东持续增长**：J7 把未参与链接的战场全部重排到合并战场东侧，锚点偏东时每次链接都把其余战场再推远；穷举宽 31–111（初始 41–51），西侧留下大片空白；`MapValidator` 列上限只能放到 140（原本想反推"决赛 20×20"） | 已按 J7 字面实现并穷举验证 | 可选：合并外接矩形以西的战场原地不动、只把以东的收拢到间距 3（画布不增长，锚点仍不动）；或接受现状，表现层按战场铺底座 |
| J8-2 | **同一配对事件内第二次链接会平移第一对合并出的战场**（它是"未参与者"）：锚点不动只在单次链接内成立，一个配对事件结束时首轮两对都可能移动过 | 已按 J7 实现，记录两条向量；测试按合成向量断言 | 与 J8-1 同一处决定；若采纳"以西不动"，第一对若在第二对以西即不再移动 |
| J8-3 | **并列偏南 / 偏西可能把被贴方推到画布原点之外**（x 或 y 为负）：决赛北贴且锚点在最西、锚点顶行只剩西块、被贴方底行只剩东块时可构造出来；种子 1–20 穷举 0 次 | 实现为抛 `MapGenerationException`（响亮失败、不换摆法） | 裁决该情形的摆法：在最大共享边的候选里取画布内最南 / 最西者，没有即允许全局平移（锚点也记向量） |
| J8-4 | **边界栅栏随棋盘走（自行取的缺省）**：以场景格为一端的栅栏（R5 合法无效果）按可落子端所属战场平移；若拼接后另一端恰落在接缝对面的格上，这道栅栏就变成有效的接缝栅栏；接缝两侧各自朝间隙立的两道栅栏平移后是同一条边，只保留先出现的一条（否则 `TerrainWriter.ApplyAll` 抛重复边、配对事件中途崩溃）；两端都是场景格的栅栏没有锚点——新图上两端仍在画布内且不可落子则原样保留，否则丢弃。去重与丢弃都会让 `GameBoard.TerrainEdits` 条数变少 | 已实现，有注释与测试 | 可按裁决改：例如拼接时丢弃全部“无效果”栅栏 |
| J8-7 | **规格算例的数字是旧 link:1 的**：Scenario「首轮拼接」写 7×7 / 5×5 / 共享边 5 / 74 格，新生成器下 link:1 的战场 0 / 3 为 5×6 / 5×7；测试按结构事实断言（共享边 = 较矮者的高、格数 = 两者之和、锚点逐格不变、被贴方单一向量），实际尺寸打印在测试输出 | 未改规格 | 主会话同步规格算例文字，或改成不带具体数字 |
| J8-5 | **链接档静态地形必须为空（自行取的缺省）**：生成器拼接时不构造 `TerrainData`（地形写入口守门只允许写入口 / 文件读入 / 地图定义构造），带静态地形的链接档图在 `Link` 时抛 `MapGenerationException` | 生成器从不产生静态地形，手写夹具也无 | 接受 |
| J8-6 | 校验器画布区间 列 20–140 / 行 8–32 按穷举极值加余量（S2 决赛多战场的边缘情形未穷举） | 已设 | 接受；若采纳 J8-1 备选可收到 60 |
| J8-8 | `Program.SimulateLinks`（`map --links`）的贴边口径（自行取的占位）：两战场都只含一块出生棋盘按东、否则按北 | 占位 | F.3 定 |
| J8-9 | 三条 `Skip`（`地形可离线重建`、`首轮链接演出_四个节拍依次`、`他人链接不演出`）与 testing.md “Skipped 只用于 Perf / Slow” 相悖 | 特性里写明 F.3 / F.4 恢复 | 请主会话认可这一过渡态，或改为删除并在 F.3 / F.4 重写 |

## 7. 遗留与风险

- 三条 Skip（`地形可离线重建`、`首轮链接演出_四个节拍依次`、`他人链接不演出`）在 F.3 / F.4 完成后必须恢复；Skip 理由已写在特性里。
- 决策基线显示 AI 局在第 12 大回合以 `LastBattlefieldSettled` 结束（41 小回合）：拼接后对局长度大幅缩短，F.5 冒烟的"过早 Pass 落败"口径要重新看。
- `Sim` 的日志 / 分析 / `map --links` 只做了编译与不崩溃级别的适配，F.3 要按向量重写；`Program.SimulateLinks` 的贴边口径是占位。
- `BoardHistory` 同形键在拼接后画布变化即自然失配（已按任务书接受）；`MatchFlow.TerrainEdits` 记录保留发生时坐标（历史），`GameBoard.TerrainEdits` 已平移。
- `LinkMapGenerator.Generate` 按种子进程级缓存无上限（与段 A 相同）。

---

# F.2b（负责人 2026-10-12 裁决 J8 落实）

在 F.1–F.2 的未提交改动之上实施；`tasks.md` F.2b 已勾。本节以下内容覆盖上文 §4 / §6 中 J8-1、J8-3、J8-6 的旧描述。

## 1. 改动清单

- `src/Siege.Core/Board/Maps/LinkMapGenerator.cs`（`Link`）：
  - **J8-1**：未参与链接的战场里，外接矩形整体位于合并战场以西的原地不动；其余按当前最西列升序收拢到合并战场东侧（间距 3，底边不动）。"以西"取**至少隔一列空列**（`e.MaxX + 1 < 合并战场最西列`）——恰好紧贴合并战场西沿的按"其余"收拢，以满足"MUST 不相邻"（自行取的口径，见 J9-1）。
  - **J8-3**：先算全部候选的共享边，取最长者；最长者里先排除会让被贴方越出画布原点（负坐标）的候选，再取偏南 / 偏西；若最长者全部越界，取其中偏南 / 偏西者，然后把**整个布局（含锚点与以西不动的战场）**整体向东 / 北平移到恰好不越界，锚点向量非零并记入 `Shifts`。删除越界抛 `MapGenerationException` 的路径（"找不到共享边"仍是生成器缺陷，照旧抛）。
  - 注释：`BattlefieldShift` / `LinkResult.Shifts` / `LinkRecord` / `GameBoard.ApplySplice` 里"锚点恒为零向量"改为"通常为零、整体越界平移时非零"。
- `src/Siege.Core/Board/MapValidator.cs`：**J8-6** 链接行画布区间按新穷举实测收紧为 **列 20–75、行 8–21**（实测含初始图宽 23–68、高 9–18，向外留余量）；测试里 `链接时的棋盘拼接Tests.LinkCanvas` 同值、穷举断言每张图都在区间内。
- 存档 / 回放：无需改代码——`RestoreLink` 按记录贴边重放、逐项比对含锚点在内的全部向量（`Shifts`）；新测试验证锚点非零向量的往返与篡改拒绝。
- 测试：
  - `LinkMapGeneration/链接时的棋盘拼接Tests`：新增「合并战场以西的战场不动」「越界候选被排除」（手搭夹具两种情形：有不越界候选 → 选最西的不越界者、锚点不动；全部越界 → 整体东移 3、锚点 (3,0)，并在 `GameBoard` 层验证锚点棋子随之平移）；「全部链接序列都能拼接」改为锚点按其向量断言、以西者只随整体平移、其余落在合并战场东侧且不相邻，输出新统计。
  - `MatchSetup/链接模式存档Tests`：新增「锚点非零向量的拼接可存档恢复」（手搭 `link:900001` 图、四人四区脚本配对，决赛北贴触发整体平移；存档往返逐字节相同、链接记录相同、篡改锚点向量即 `FormatException`）。
  - 三条 Skip 理由前缀改为"board-link-mode 裁决 J8-9：F.3 / F.4 恢复。"
- `tests/.../AiDecision/Fixtures/decisions-link1-seed3.txt`：再次重钉（见 §3）。

## 2. 测试、变异

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2379 条，通过 2366，跳过 13，失败 0**（真实退出码 0）。
- 变异（同一脚本，过滤 LinkMapGeneration + Battlefield + 链接模式存档 + 按座位视图，还原后确认红 0 / 绿 164）：

| 编号 | 变异 | 红 |
|---|---|---|
| M-F18 | 以西的未参与战场也收拢（J8-1 条件改恒假） | 25（含「合并战场以西的战场不动」；其余多为画布超出新上限 75） |
| M-F19 | 并列不排除越界候选（直接取最长者第一个） | 1（「越界候选被排除」） |
| M-F20 | 全越界时不整体平移（`globalX` 乘 0） | 4（「越界候选被排除」「锚点非零向量的拼接可存档恢复」「全部链接序列都能拼接」「链接可重放」） |

## 3. 穷举新统计（种子 1–20，去重 1600 次拼接）

| 项 | F.2（J7 字面） | F.2b（J8-1 / J8-3） |
|---|---|---|
| 画布宽（含初始图） | 31–111（拼接后） | **23–68** |
| 画布高 | 9–18 | 9–18 |
| 为腾位置而平移的其他战场 | 2402 次 | **1088 次**（以西不动 1581 次；其中"以西但紧贴而被收拢" 3 次） |
| 整体越界平移（锚点非零向量） | 0（旧实现抛异常，0 次命中） | **22 次** |
| 共享边分布 | 5:725 6:538 7:204 10:38 11:58 12:23 13:11 14:3 | 不变 |
| 合并战场可落子格 | 50–182 | 不变 |

整体越界平移从 0 变成 22 次：以西战场不再被推走后，合并战场可以贴近画布西沿，决赛北贴向西对齐时更容易越过原点——这正是 J8-3 要兜住的情形，全部通过校验。

## 4. 链接图决策序列基线（再次重钉）

按原口径（`link:1`、种子 3、脚本缺省配对、4 名标准 AI、截到第 12 大回合）`SIEGE_REBUILD_DECISION_BASELINE=1` 重钉。与 F.2 版的差异**只有坐标**：链接 1 的其余战场向量 `2:31,3:31,4:23` → `2:0,3:0,4:5`（2、3 号在以西不动）、链接 2 `0:36,4:18` → `0:5,4:5`，第 4–6 大回合的落点坐标随之整体平移（如 `BJ3` → `AC3`）；征募、类型、落子个数、配对、结算、终局（第 12 大回合 `LastBattlefieldSettled`、41 个小回合）全部相同。链接 3 的向量不变。行数 55 不变。

## 5. 新增需裁决（J9 起）

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| J9-1 | "整体位于合并战场以西"的边界：恰好紧贴合并战场西沿（`MaxX = 合并战场最西列 − 1`）的未参与战场算"以西"会与合并战场相邻（违反 MUST 不相邻），实现为按"其余"收拢到东侧（自行取的口径） | 穷举 3 次 | 接受；或改为"以西且外接矩形间隔 ≥ 3 才不动" |
| J9-2 | 整体越界平移时，以西不动的战场也随整体平移（"整个布局"字面） | 已实现，22 次 | 接受 |

其余 J8 项已按裁决落实，上文 §6 的 J8-1 / J8-3 / J8-6 建议栏作废。

---

# F.3（Sim：日志、analyze、map --links、回放）

在提交 9f76306 之上实施；`tasks.md` F.3 已勾。

## 1. 改动清单

- **新增 `src/Siege.Sim/Logging/LinkLayout.cs`**：作为“坐标随链接平移”的**唯一**实现。
  - `LinkShiftStep(X, Y, Shifts)` 可由 `LinkRecord` 或 `LinkLogEntry` 构造。
  - `LinkLayout(开局出生棋盘, 链接序列)` 按版本累计每块棋盘的位移，战场标识随合并改名。
  - 提供 `Translate` / `ZoneAt` / `BoardAt` / `TranslateEdit`，其中 `TranslateEdit` 与 `GameBoard.ApplySplice` 同口径：
    - 一端在场景格的栅栏随另一端平移；
    - 两端都在场景格的栅栏不动，但被棋盘占到或越出画布时丢弃；
    - 落到负坐标的丢弃。
  - `LinkLayout.Of(log)` 只读日志首部的开局棋盘和链接事件。
  - 版本口径工具：`VersionAfterTurn` / `VersionAtEvent`。
- **日志 `MatchLog.cs`**：
  - `LinkLogEntry` 删掉 `Degraded` / `PublicBoard` / `CorridorCells` / `CorridorLengths` / `NewRelics`，换成 `Side` / `SharedEdge` / `Shifts`（新类型 `BattlefieldShiftEntry`，含锚点）；`BattlefieldCells`、`MapDigest`、`Summary` 保留。
  - 首部新增 `Boards`（新类型 `BoardEntry`，开局出生棋盘），只在链接模式写出（`WhenWritingNull`）。
  - 删除 `AllRelics`：完整信物分布就是首部，按开局坐标。
  - 类注释写明各行坐标的版本口径：
    - 首部：开局布局；
    - 小回合快照：该回合结束时（含本回合内的链接）的布局；
    - 事件：发生时的布局；
    - 末行揭示表：终局布局；
    - 驿站来源与前瞻记录：回合开始时的布局。
- **跑局 `MatchSession.cs`**：
  - 首部写 `Boards`，链接事件写新字段。
  - `RecordTurn` 的本回合平移改为调用 `LinkLayout`，删掉原先的那份实现。
  - 本回合改造的 `Target` / `Artisan` 也平移到链接后的布局，与快照口径一致；若平移结果为空则保留原记法。
- **分析**：
  - `LinkModeAnalyzer`：删 `DegradedLinks` / `DegradedShare` / `CorridorLengths`，新增 `SharedEdges`（共享边长度分布）和 `GlobalShifts`（锚点向量非零的次数）。`FinalBattlefieldCells` 即决赛合并战场的格数。
  - `ReportWriter`：链接段两行改写，不再出现“退化”“通道长度”。
  - `BalanceAnalyzer.Selection`：信物统计按“首部开局坐标经 `LinkLayout` 换算到该快照或揭示表的版本”读；撤掉 F.2 的“查不到就跳过”，对不上即抛 `FormatException`。`Life.PlayableCells` 的口径未动。
- **`map --links`（J8-8）**：
  - 保留 `1-4,2-5` 写法，并支持显式 `1-4E` / `1-2N`。
  - 不写贴边时按阶段推断：一旦某项连到已合并的战场即进入决赛（北贴），之后都按北贴；在此之前按东贴。
  - 每步打印贴边（推断的会标出）和平移向量；最后照旧打印拼接后的文本图与校验报告。帮助文案已同步。
- **回放**：`Replayer` 没有改代码——链接按记录的配对重放，贴边由配对阶段决定，向量与开局棋盘随确定性文本逐行比对。
- **测试**：
  - 新增 `MatchTelemetry/坐标随链接平移Tests`：
    - 种子 1–20 的全部链接序列、每对起止版本、每个可落子格，`LinkLayout` 都与 Core `LinkResult.Translate` 逐次复合的结果逐格相同；
    - 改造随棋盘平移，并与 `ApplySplice` 的结果逐条一致；
    - 缺少向量的记录被拒。
  - `对局日志的记录内容_链接模式Tests`：
    - **恢复「地形可离线重建」**（去掉 Skip）：只读日志，用生成器重放链接并核对摘要与向量；改造按 `VersionAfterTurn` 经 `LinkLayout` 平移到终局，重放后与真实终局逐格相同；反面断言为把任一次链接的向量置零即对不上。
    - 新增「链接那一小回合的改造按链接后布局记」：第 3 大回合每人立一道棋盘内栅栏，日志里的改造平移到终局后，与 Core 盘面迁移后的改造列表逐条相同。
    - 「配对与链接可查」改为核对 `Side` / `SharedEdge` / `Shifts` 和首部 `Boards`。
  - `链接模式平衡分析Tests`：
    - 改为核对共享边分布和整体越界平移计数（用一份把锚点向量改成非零的日志验证计数）；报告不含“退化”“通道长度”；
    - 信物统计用“已平移的信物被控制后计数加 1”验证；坐标损坏时抛 `FormatException`。
  - `可复现回放_链接模式Tests`：新增「篡改平移向量或开局棋盘即在对应行分歧」：改向量，分歧报在该链接事件那一行；改首部棋盘，分歧报在第 1 行。
  - `各入口支持生成图_链接模式Tests`：
    - 新增「map子命令按阶段推断贴边_也接受显式贴边」：`1-4,2-5,1-2` 推断为 东/东/北；与显式 `1-4E,2-5E,1-2N` 的结果逐行相同；`1-2N` 可让两块单棋盘北贴；
    - 参数校验加 `1-4X`。
  - `LinkModeFixtures.Track` 改为调用 `LinkLayout`，测试侧也不再保留第二份实现。
- Godot / Presentation 没有改动（`LinkRecord` 之外的共享类型没有变化）。

## 2. 构建、测试、变异

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2386 条，通过 2374，跳过 12，失败 0**（退出码 0）。跳过数从 13 降到 12，剩下的两条 J8-9 Skip 留给 F.4。Legacy 黄金哈希、v5 / board:1 基线照常通过。
- 变异（每条还原后都复跑确认红 0）：

| 编号 | 变异 | 红 |
|---|---|---|
| MF3-1 | `LinkLayout` 不随合并改写战场标识 | 16 |
| MF3-2 | 一端在场景格的栅栏只平移可落子端 | 1（「改造随棋盘平移」） |
| MF3-3 | 跑局层记录改造时不平移 | 1（「链接那一小回合的改造按链接后布局记」） |
| MF3-4 | 信物统计不按版本换算 | 1（「真实跑局的链接日志可完整分析」） |
| MF3-5 | 整体越界平移不计数 | 1（「配对与链接可查」） |
| MF3-6 | `map --links` 不推断决赛 | 1（「map子命令按阶段推断贴边_也接受显式贴边」） |

## 3. 链接图决策序列基线

**没有变化**，`decisions-link1-seed3.txt` 未改动。基线只导出 AI 决策和 Core 的配对 / 链接 / 结算记录，不读 Sim 日志字段。

## 4. F.4 接线点补充

- 表现层若要把“链接前的坐标”换算到链接后（例如演出时对方棋盘滑入的起止位置、`LinkShowBeats.Expansion` 改为按向量差分），可以直接用 `LinkLayout`：
  - 由 `Match.Board.BaseMap.Boards` 和 `Match.Links.Select(LinkShiftStep.From)` 构造；
  - `BoardAt(zone, v)` 给出每块出生棋盘在各版本的外接矩形，即滑入动画的起点和终点。
  - 注意：`LinkLayout` 在 Siege.Sim 里，Presentation 不得引用 Sim。如需共用，要把它下沉到 Core 或 Presentation，并保持只有一份实现——这需要主会话决定位置（见 J10-1）。
- 同一配对事件内有两条链接时，用 `BoardAt(z, 起始版本)` → `BoardAt(z, 结束版本)` 即可得到合成后的位移。

## 5. 需裁决

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| J10-1 | `LinkLayout` 放在 Siege.Sim（日志 / 解析层，按派发要求）。F.4 演出若要同样的换算，Presentation 不能引用 Sim | 未动 | F.4 前把它下沉到 Core（例如 `Siege.Core.Board.Maps`），Sim / Presentation / 测试共用 |
| J10-2 | 小回合快照里驿站来源与前瞻记录是回合开始时的决策量，按回合开始时的布局记；快照其余字段按回合结束时的布局记——同一行里有两种口径 | 已写进 `MatchLog` 类注释 | 接受（只在发生链接的那个小回合里两者不同）；或把这两项也平移到回合结束时 |
| J10-3 | `map --links` 推断规则：前面的单棋盘配对在没写贴边时一律按东贴，所以“3+1 决赛”（两块单棋盘北贴）必须显式写 `N` | 已实现 | 接受 |

---

# F.3b / F.4（LinkLayout 下沉；Godot / Presentation：按布局重搭与接缝、拼接演出、自检与截图）

在提交 d52934e 之上实施；`tasks.md` 已勾 F.3b、F.4。

## 1. F.3b：LinkLayout 下沉（裁决 J10-1）

- 新增 `src/Siege.Core/Board/Maps/LinkLayout.cs`：`LinkShiftStep` 记录与 `LinkLayout` 类（构造、`Versions` / `BoardAt` / `ZoneAt` / `Translate` / `TranslateEdit`），从 Sim 原样搬过来，零行为改动；不含浮点，不依赖 Godot。
- `LinkRecord.ShiftStep`（Core，新）取代 `LinkShiftStep.From(LinkRecord)`，原因是 Core 的 `Board.Maps` 不反向引用 `Match`。
- 新增 `src/Siege.Sim/Logging/LinkLogLayout.cs`，承接原先放在 `LinkLayout` 上、与日志相关的部分：`StepOf(LinkLogEntry)`、`Of(MatchLog)`、`VersionAfterTurn`、`VersionAtEvent`。删除 `src/Siege.Sim/Logging/LinkLayout.cs`。
- 调用方改为新入口：`BalanceAnalyzer`、`MatchLog` 注释、`MatchSession`，以及测试中的 `LinkModeFixtures`、`对局日志的记录内容_链接模式Tests`、`链接模式平衡分析Tests`。没有守门测试按文件名或命名空间扫描它，所以白名单无需调整。
- 全量测试：2386 条，通过 2374，跳过 12。

## 2. F.4 第 1 步：按新布局重搭、接缝、拼接核对

- **相机（Presentation `BoardCamera`）**：`Rebound(bounds, focus, shiftX, shiftZ)` 先把注视点按给定位移平移，再夹取；不传位移时行为与原来一致。
- **相机接线（Godot `BoardView.Build`）**：
  - 记录上一次本机出生棋盘（占位 Own）的原点和画布尺寸。
  - 重搭时取本机棋盘格心的平面位移（新格心 − 旧格心）传给 `Rebound`，这样他人链接收拢本机战场、或整体越界平移时，画面里本机棋盘不跳。
  - 底座、坐标标注、相机可行矩形照旧按新的画幅包围盒重算。
- **接缝**：
  - `BoardView.PlateGroups`：外接矩形边对边紧贴的棋盘归为一组，即拼接出来的连体棋盘。
  - 组内每块棋盘照常画内部格线；接缝处补一条同样的格线（只从东 / 北一侧画一次）；接缝上不画边框。
  - 整组只沿外轮廓画一圈边框，同类边按行 / 列合并成段，颜色为中性的台面边框色。
  - 出生区阵营色描边在接缝一侧不描，只沿自己那块的外缘。
  - 自证行例：`[plates] 连体棋盘（出生区 1+2，55 格）：外轮廓边框 6 段，接缝格线 1 段，接缝边框 0 段`；决赛后 `1+2+5，80 格：外轮廓 6 段、接缝格线 2 段、接缝边框 0 段`。
  - 旧地图（棋盘两两至少隔 2 格）每块各成一组，画法不变。
- **`LinkDemoAudit.Linked`**：
  - 新增参数 `expandedBefore`（拼接前已展开的出生棋盘）。
  - 本机参与时要求：可见格 = 本战场格数 = 拼接前可见格 + 新展开的对方出生棋盘格数，且确有新展开；他人链接要求本机可见格数不变。
  - GameRoot 改为按同一帧的整批链接核对，再一次性更新基准；修掉了"同帧本机另有拼接时，他人那条按拼接后的格数比"的潜在误判。

## 3. F.4 第 2 步：拼接演出

- **`LinkShowBeats`**（Presentation）：
  - 新节拍 `BoardSlideBeat(Cells, FromDx, FromDy)`，900 ms，取代已删除的 `BoardRiseBeat`。
  - `Expansion(before, after, newOpponents, layout, fromVersion)`：先把结算前本机的格经 `LinkLayout` 映射到拼接后坐标，再与结算后的格比。没有新增格就返回空序列，平移不再被误判为新增。
  - 锚点 / 被贴方按两侧最小出生棋盘编号判定：本机是锚点时滑对方棋盘；本机是被贴方时滑本机棋盘。起点偏移 = 两侧平移向量之差，同一配对事件里的两条链接自然合成一次。
  - 节拍依次为：滑入（900）→ 相机重定界（500）→ 对方盘面显现（600）→ 横幅"棋盘拼接：与 X 交战"。
  - 提速、零时长沿用时间线原规则。
- **`ShowMask`**：`Rising` 换成 `Slide`（格、起点偏移、进度），并新增 `SlideOffsetOf(coord)`，单位是千分之一格。
- **`SettlementBeats.Generate(..., moved)`**：拼接发生在某小回合末尾时，结算前的盘面与信物先经映射换到拼接后坐标再比。修掉了被平移的棋子被当成"落子 + 提子"、以及结算前画布较小时越界抛出的问题（见 J11-3）。不传映射时与原来逐行相同。
- **Godot 接线**：
  - `BoardView.ApplySlide`：滑动格的地砖节点按偏移逐帧平移，其上棋子按格心 + 偏移整份重写；完整刷新与演出帧都调用。原来的 `ApplyRising` / `RiseDepth` 已删除。
  - `MatchSession`：本机确认、Pass、AI 小回合三处都记录拼接前的链接数，用 `LinkLayout` 生成映射，传给 `SettlementBeats` 与 `Expansion`。
  - `GameRoot`：`[show]` 行改为"拼接 滑入 a 格、显现 b 格、重定界"；`[field]` 匹配"棋盘拼接"；`--shot-show=expand` 改为取滑入节拍半程；`--shot-show=field` 改为取第一条横幅（"战场落败 / 胜出"）的半程——原先取整拍半程，那时已切到第二条"出局"横幅。
- **恢复 J8-9 剩余两条 Skip**（`棋盘展开节拍Tests` 整体重写）：
  - 「首轮链接演出_四个节拍依次」恢复；
  - 「他人链接不演出」恢复，并补断言：本机棋盘确实随他人链接平移；平移后可见格与内容逐格对应；本机占位原点 = 换算后的位置。
  - 新增「本机是被贴方_本机棋盘滑过去贴合」「遮罩_滑入前停在起点_对方格先隐藏后渐显_相机目标带进度」「拼接那一回合的结算节拍不把平移当成落子与提子」。
  - 「无人值守零时长」「提速照旧」按新签名照旧。
- **其他测试**：
  - `战场范围的相机Tests` 新增「拼接后注视点随本机棋盘平移」。
  - `自动演示全流程自检Tests` 的两处 `Linked` 调用补上 `expandedBefore`，并加"拼接前后格数不守恒即失败"的反例（注释"board-link-mode F.4："）。

## 4. 构建、测试、变异

- `dotnet build siege.sln -c Release`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2389 条，通过 2379，跳过 10，失败 0**（退出码 0）。
- 跳过数从 12 降到 **10**，不是派发里写的 11：F.3 时剩 12 条，其中 2 条是 J8-9 Skip，本段都已恢复；剩下 10 条都是 Perf / Slow（见 J11-1）。
- 变异（每条还原后复跑都是红 0 / 绿 153）：

| 编号 | 变异 | 红 |
|---|---|---|
| MF4-1 | `Expansion` 不经布局换算、直接按坐标差分 | 3（含「他人链接不演出」） |
| MF4-2 | 锚点 / 被贴方判反 | 2 |
| MF4-3 | `SettlementBeats` 不用拼接映射 | 1 |
| MF4-4 | `Rebound` 不随本机棋盘平移 | 1 |
| MF4-5 | 拼接核对不看格数守恒 | 1 |

- 接缝画法在 Godot 层，xunit 测不到；靠 `[plates]` 自证行（接缝边框 0 段）加截图像素统计。

## 5. 自检与终局跑（headless，顺序单跑，0 条 ERROR）

- `--auto-demo`：退出 0。`[flow]` 6 条通过（缺省种子第 39 帧他人链接 3+5，本机可见格 30 → 30）；开局对准 30/30。
- `--auto-demo --pick-check --seed=5`：退出 0。开局一轮 30/30；第 39 帧本机参与拼接 1+2（30 + 拼入 25 → 55）；第二轮逐格居中 55/55 ×2，各位姿通过。
- `--map-select --auto-demo`：退出 0，`[flow]` 6 条通过。
- `--auto-demo --rounds=0`（缺省种子）：退出 0，12 大回合、105 帧、约 3.9 s，`[flow]` 13 条全通过，七步齐全：
  - 第 91 帧本机战场落败横幅；
  - 第 100 帧决赛拼接 1+3，本机所在战场参与（30 + 拼入 67 → 97）；
  - 第 104 帧决赛结算，终局不出横幅。
- `--auto-demo --rounds=0 --seed=5`（本机参与首轮拼接）：退出 0，14 大回合、142 帧、约 4.9 s，`[flow]` 13 条全通过：
  - 第 39 帧首轮拼接 1+2（30 + 25 → 55），演出：滑入 25 格、显现 25 格、重定界；
  - 第 137 帧决赛拼接 1+5（55 + 25 → 80）。
- 对局长度比段 D（93 / 156 大回合）短很多，与 F.2 的基线观察一致；F.5 冒烟会看到。

## 6. 截图（`art/board-link-mode/`，1600×900，非 headless，彩色 + 灰度；未读入会话，按自证行与 PIL 统计判定）

- **删除**（旧公共棋盘 / 通道或旧 73×73 布局）：
  - `linked-battlefield`、`linked-battlefield-closeup`
  - `d-c-expand-mid`、`d-c-overview-field`、`d-c-field-lost`、`d-c-strip-elsewhere`
  - `d-d-final-link-overview`、`d-d-match-end`
  - `placeholder-closeup`：新画布 40×11 一屏看全，`--shot-cell` 近景与全图同一张；该图改名为 `f-spliced-overview`
  - 以上各含 `-gray`，共 18 个文件。
- **重拍**（同名覆盖）：`new-game`、`flags-placeholders`、`locked-own-board`。
- **新拍**：`f-splice-mid`、`f-spliced-overview`、`f-spliced-closeup`、`f-final-overview`、`f-field-lost`、`f-strip-elsewhere`、`f-match-end`。
- 像素统计（灰度均值 / 暗 <40 / 亮 ≥200 / 台面边框色像素）：

| 文件 | 均值 | 暗 | 亮 | 台面边框色像素 |
|---|---|---|---|---|
| `flags-placeholders` | 155.7 | 3.0% | 35.7% | 2153 |
| `new-game` | 167.2 | 2.2% | 43.1% | 457 |
| `locked-own-board` | 149.4 | 9.6% | 36.9% | 2243 |
| `f-splice-mid` | 158.0 | 4.3% | 35.8% | 6768 |
| `f-spliced-overview` | 158.0 | 4.3% | 35.9% | 6891 |
| `f-spliced-closeup` | 133.3 | 4.3% | 12.5% | 35058 |
| `f-final-overview` | 143.2 | 4.4% | 23.9% | 12518 |
| `f-field-lost` | 154.4 | 5.4% | 34.4% | 1678 |
| `f-strip-elsewhere` | 145.7 | 10.5% | 34.8% | 4203 |
| `f-match-end` | 136.4 | 5.4% | 25.6% | 7002 |

- 截图时的自证行：
  - `f-splice-mid`：当前节拍 BoardSlideBeat，进度 508‰；
  - `f-field-lost`：横幅「战场落败（第 2 名）」第 1/2 条，515‰；
  - `f-spliced-overview` 的边框色竖线只在合并战场西沿（x≈800，约 190 px）一处，估算的接缝列附近没有同等长度的竖线（最高约 37 px）。
- `README.md` 已重写：新索引；人工检查清单第 2 项"展开动画"改为"拼接动画"，新增"接缝连成一片""他人链接画面不跳"两项，共 9 项。

## 7. 偏离与需裁决（J11 起）

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| J11-1 | 跳过数实测 10，派发写 11 | F.3 后 12，减去恢复的 2 条 J8-9 = 10；其余都是 Perf / Slow | 以 10 为准 |
| J11-2 | 连体棋盘外轮廓边框用中性台面边框色；本机阵营色只保留在出生区外缘描边（接缝一侧不描） | 二选一里选了"阵营色提示仍只覆盖自己那块" | 接受；或改为外轮廓按本机阵营色描 |
| J11-3 | 派发没列，但必须修：拼接发生在某小回合末尾时，该小回合的结算节拍原本按坐标直接差分，会把被平移的棋子当成落子 + 提子，结算前画布较小时还会越界抛出 | `SettlementBeats.Generate` 加可选映射参数，Godot 三处传入；补测试与变异 MF4-3 | 接受 |
| J11-4 | 滑入只移动地砖（含阵营描边）与其上的棋子；台面边框 / 格线、信物标记、改造标记滑动期间停在终点 | 为控制改动面 | 接受；要求全部随之滑动需另加节点登记 |
| J11-5 | 本机是被贴方时，本机棋盘在滑入节拍里自己滑过去，相机在随后的重定界节拍才跟上，滑入期间不跟随 | 已实现 | 接受；或滑入期间相机按同样偏移跟随 |
| J11-6 | 新增自证行 `[plates]`；`--shot-show=field` 改为取第一条横幅半程 | 截图工具口径 | 接受 |
| J11-7 | "他人链接平移本机战场时画面不跳"由 Presentation 测试与相机测试守；Godot 自检的两个种子里没出现"他人链接平移本机棋盘"的帧（缺省种子第 39 帧他人链接 3+5，本机棋盘在合并战场以西、不动） | 未专门找种子 | 接受；或另找种子做人工核对 |

遗留：
- 接缝无边框的像素判据是估算的接缝列位置，不是逐像素定位。
- `--pick-check` 第二轮仍只在本机参与的第一次链接后跑。
- 决赛后的连体棋盘没有拾取自检。

---

# F.4b / F.5（滑入整体平移与相机跟随；拼接后小冒烟）

在提交 157f905 之上实施；`tasks.md` 已勾 F.4b、F.5。

## 1. F.4b：滑入时整体平移，被贴方相机跟随（裁决 J11-4 / J11-5）

- **滑动父节点（Godot `BoardView`）**
  - 新增 `Sliding` 父节点。每次画完（完整刷新与演出帧末尾）调用一次 `ApplySlide`：把六类容器里属于滑动格的节点挪到父节点下对应的镜像容器，父节点按遮罩的偏移整体平移。
  - 六类容器：地砖（含阵营描边与地形栅栏）、台面、叠加层（信物 / 禁手 / 改造标记与信息层叠加物）、棋子、预览、演出层。
  - 没有滑动时全部并回原容器、偏移归零。不按种类逐个登记。
  - 节点归属：台面按块挂在 `Plate{i}` 节点下，带元数据（取棋盘左下角格）；其余节点按位置经唯一反算 `BoardGeometry.TryFromWorld` 求格。原点处的纯容器节点取第一个子节点的位置。
  - 逐帧重画的容器改用 `ClearLayer`，连镜像一起清；棋子移除改为从实际父节点上摘下。F.4 的逐格地砖 / 棋子偏移写法已删掉。
- **台面**：二选一里选了"滑入期间两块各自带边框，到位后切成合并外轮廓"。滑入开始那帧按各自独立一块重画（滑动方带自己的边框与格线、随滑动父节点走），滑到位那帧重画为合并外轮廓（接缝格线、无边框）。
- **相机跟随**
  - Presentation 新增 `CameraPose.FollowSlide(start, mask, own, planeCenterOf)`：本机在滑动格里时，注视点 = 滑入开始位姿 + 本机棋盘此刻的平面偏移（与滑入同一插值），距离不变；否则返回 null。
  - 平面偏移经引擎侧的格心映射换算：取 (0,0)、(1,0)、(0,1) 三格得两个方向的单位位移，不另写换算。
  - 放在 `CameraPose` 上，是因为它在浮点守门的相机类型豁免名单里。
  - Godot `GameRoot.ApplySlideCamera` 在每帧推进时间线之后、取图判定之前调用，所以取图那帧的位姿已经到位。重定界节拍再从跟随后的位姿过渡到合并战场一屏可见。
- **`--zone=N`**（Godot，新）：无人值守演示里本机锁定的出生棋盘编号（1 起，缺省 1）。本机在 1 号时永远是锚点，要演示被贴方必须换号。
- **自证行**
  - 锚点（种子 5）：`[slide] 滑入半程（508‰，25 格，偏移 5.41,0 格）：滑动战场节点 69 个，其中非地砖 3 个（台面 1、叠加 2、棋子 0、预览 0、演出 0），全部挂在滑动父节点下`
  - 被贴方（种子 2、`--zone=3`）：`[slide] 滑入半程（500‰，42 格，偏移 -15.5,0.5 格）：滑动战场节点 111 个，其中非地砖 6 个（台面 1、叠加 2、棋子 3…），全部挂在滑动父节点下`，以及 `[slide] 本机为被贴方：滑入半程相机注视点 (-12, 0)，起点 (3.5, 0.5)，随本机棋盘同步平移`。
- **测试**：`棋盘展开节拍Tests` 新增「本机为被贴方时滑入期间相机跟随本机棋盘」——0 ms 为起点 + 整段偏移、过半约一半、播完为 null；本机是锚点时为 null。
- **变异**
  - MF4b-1：`FollowSlide` 不加偏移 → 红 1。
  - MF4b-2（Godot 层，手工）：台面容器不挪进滑动父节点 → 自证行变为"台面 0 …，仍有 1 个留在原容器"。用 `cp` 备份，还原后 `cmp` 一致、`touch`、重新构建。
- **截图**：重拍 `f-splice-mid`（灰度均值 157.6 / 暗 4.3% / 亮 35.4%）；新拍 `f-splice-mid-mover`（157.8 / 3.6% / 34.5%）。各含灰度版。README 已加行，人工检查清单第 2 项已补充。

## 2. 构建、测试、自检

- `dotnet build siege.sln -c Release`：0 警告 0 错误；Godot 工程同样 0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2390 条，通过 2380，跳过 10，失败 0**（退出码 0）。
  - 中途 `Godot层不含规则计算Tests.坐标映射在Godot侧唯一` 红过一次：我在 `BoardView` 里自写了一份格心反算，已改为调用 `BoardGeometry.TryFromWorld`，守门原样通过、未改测试。
- 最终构建上 headless 单跑，退出码全部 0、0 条 ERROR：
  - `--auto-demo`：`[flow]` 6 条通过；
  - `--auto-demo --pick-check --seed=5`：30/30 → 拼接后 55/55；
  - `--map-select --auto-demo`：`[flow]` 6 条通过；
  - `--auto-demo --rounds=0 --seed=5`：`[flow]` 13 条通过，七步齐全。
- 既有测试断言未改。

## 3. F.5：拼接后小冒烟（只跑这一批）

- 命令：`dotnet src/Siege.Sim/bin/Release/net8.0/Siege.Sim.dll run --out sim-out/board-link-mode/smoke-splice --map link:1 --count 10 --seed 1 --difficulty Standard --turn-limit 600`。配对种子缺省由对局种子派生，并行 28。
- 随后 `analyze --dir … --out …/report.txt`。`sim-out/` 不入库。

| 量 | 拼接版（本批，10 局） | 通道版（段 C，20 局） |
|---|---|---|
| 截断 / 失败 | 0 / 0 | 0 / 0 |
| 终局原因 | LastBattlefieldSettled ×10 | LastBattlefieldSettled ×20 |
| 胜者 | P0 2、P1 4、P2 1、P3 3 | P0 7、P1 2、P2 6、P3 5 |
| 大回合数 最小 / 中位 / 最大（均值） | **10 / 11 / 15（11.5）** | 51 / 85 / 138（89.1） |
| 小回合数 最小 / 中位 / 最大 | 30 / 36 / 48 | 114 / 235 / 361 |
| 构筑 / 首轮交战 / 决赛（均值） | 3 / **5.3** / **3.2**（10 局都到决赛） | 3 / 45.65 / 40.45 |
| 同区率 | 8/10 = 80% | 18/20 = 90% |
| 开局布局 | 2+1+1 ×5，1+1+1+1 ×2，2+2 ×2，3+1 ×1 | 2+1+1 ×14，2+2 ×3，1+1+1+1 ×2，3+1 ×1 |
| 首轮配对 | 1-2 ×2，2-3 ×2，1-3 2-4 ×1，1-3 2-5 ×1，2-4 ×1，无 ×3 | 无 ×4，1-2 ×3，2-3 ×3，… |
| 链接次数 | 19（拼接） | 38（通道） |
| 共享边分布 | 5 ×16，6 ×2，10 ×1 | —（通道长度 3–54） |
| 整体越界平移 | 0 次 | — |
| 决赛合并战场可落子格 | 50–132（每局一个） | 304–800 |
| 等待大回合数 | 全体均值 0.53；等待过的 9 人次均值 2.33，最大 5 | 全体 9.3；等待过 19 人次均值 39.16，最大 71 |
| 出局 | 战场落败 28，势力归零 2 | 战场落败 58，势力归零 2 |
| 过早 Pass 落败（上界口径同前） | **28 / 28** | 57 / 58 |
| 单小回合耗时 中位 / 最大 | 47 ms / 884 ms | 1266 ms / 38336 ms |
| 整局耗时合计 / 批次墙钟 / 最长一局 | 28.5 s / **4.8 s** / 4.6 s | 10717 s / 714 s / 712 s |

观察（只陈述数据，不调参）：
- 拼接后对局很短，中位 11 个大回合，约为通道版的八分之一。首轮交战平均 5 个大回合，决赛平均 3 个。
- 战场结算仍然全部来自"整轮 Pass"，落败者都是手里还有子时 Pass 的（28/28，与通道版 57/58 同一现象，C15 仍在）。
- 等待时长大幅缩短（全体均值从 9.3 降到 0.53）。
- 本批 19 次拼接里没有出现整体越界平移；种子 1–20 的穷举里是 22/1600。
- AI 单小回合耗时降到中位 47 ms（画布小、可落子格少）。

## 4. 偏离与需裁决（J12 起）

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| J12-1 | `--zone=2 --seed=4` 的开局对准自检失败（出生平台 25 格，画面内 23）。这是开局取景的既有问题：以前无人值守固定锁 1 号、从没跑过别的号，与本段的滑动 / 相机改动无关（开局时还没有任何链接与平移） | 只报告，未修 | 登记后续：开局对准在画布边缘的棋盘上留边不足 |
| J12-2 | 被贴方的滑入起点偏移可能很大（种子 2 实测 31 格），跟随的注视点会被相机可行矩形夹住：首帧被夹在边缘、之后才同步，所以"画面里本机棋盘不动"只在可行矩形范围内成立 | 已实现 | 接受；或把滑入起点偏移封顶（例如不超过 3–5 格），让滑入始终"从接缝一侧"短距离滑进来 |
| J12-3 | 台面二选一：滑入期间两块各自带边框（滑动方的阵营色边框随之滑），到位那帧切成合并外轮廓 | 已实现 | 接受 |
| J12-4 | 节点归属靠位置反推（台面除外）。地形栅栏、改造标记这类落在两格之间的节点，若恰好压在接缝上，可能被归到另一侧（四舍五入），表现为滑入时它不跟着走 | 拼接前两块棋盘不相邻，栅栏不会恰在未来的接缝上；本段截图与自证行里未见遗留（"仍有 0 个留在原容器"） | 接受 |
| J12-5 | 新增截图 / 演示参数 `--zone=N` | 只给无人值守演示用 | 接受；HANDOFF 改写时并入命令清单 |
| J12-6 | 冒烟结论：对局很短（中位 11 大回合），战场结算全靠整轮 Pass、落败全是手里有子时 Pass（28/28） | 只报告 | 登记到 C15（AI 不知道 Pass 会触发战场结算）的后续 change；必要时复核构筑期长度或结算门槛 |
| J12-7 | 一次 headless 运行偶发 `Illegal instruction`（退出码 132，`--zone=2 --seed=2`），同命令重跑退出 0 | 无法复现 | 观察；若再现，抓 Godot 崩溃日志 |

# F.6（裁决 J12-1 开局取景、J12-2 滑入封顶）

## 1. J12-1：非 1 号出生棋盘"开局对准"只见 23 / 25

- **根因（不是相机）**：`BoardView` 的地形指纹 `TerrainKeyOf` 只拼逐格属性（可落子 / 高度 / 地表 / 桥）+ 栅栏，**不含格坐标、画布尺寸、占位**。`--zone=2 --seed=4` 第 39 帧他人链接（1 + 5）把本机所在 2 号战场收拢平移（x 36 → 33，裁决 J7 / J8-1），本机看得见的 25 格属性序列不变 → 指纹相同 → 不重搭、不 `Rebound`，地砖停在旧坐标，而视图模型已是新坐标；演示结束时 `HomePlatformFramed` 按新坐标投影本机棋盘的角点，于是 23 / 25。加 `--rounds=1` 时链接还没发生，所以看不出来。定位依据：临时调试行（已删，`git diff` 干净）显示链接后无重搭、无 `[camera]` 重定界行。
- **修法（Presentation）**：`DefaultBoardView.LayoutKey()`（新增，纯函数）= 画布宽高 + 每格坐标与属性 + 栅栏 + 棋盘清单 + 占位（编号、原点、展开、本机）。`BoardView` 的 3 处 `TerrainKeyOf(board)` 改为 `board.LayoutKey()`，`TerrainKeyOf` 删除。相机侧（`BoardCamera` / `CameraPose`）未改：重搭后既有 `Rebound(bounds, focus, shiftX, shiftZ)` 就能把相机带到新坐标。
- **测试**：`棋盘展开节拍Tests.他人链接平移本机棋盘_布局指纹变_引擎层据此重搭`。用 `[0,0,3,4]` 复现原失败局面（3 + 4 链接平移 P0 的棋盘），先断言"旧指纹前后相同"作为样本口径，再断言 `LayoutKey` 不同；只把格整体东移 1 格也必须不同；反面：没有链接、只 Pass 的大回合指纹不变（不会每回合重搭）。
- **变异**（`mutate.py`，二进制备份、锚点命中 1 次、finally 还原、逐字节比对）：
  - MF6-1：`LayoutKey` 退回只看逐格属性 → 红 1（即上面这条）。
  - "变红 / 修后变绿"就是这样验证的：修复前没有 `LayoutKey()`，新测试在 HEAD 上编译不过，所以把 `LayoutKey` 变回旧指纹作为"修复前"（红 1），还原后为"修复后"（绿）。
  - MF6-2：`LayoutKey` 不带格坐标 → 第一次跑红 0（占位原点也会随平移变化，把它补上了）。于是给测试加了"只东移格坐标"的断言，重跑红 1。
- **开局对准 20 次**（`--auto-demo --zone=N --seed=S`，N = 1..5，S = 1..4，headless 顺序单跑）：**20 / 20 通过**，退出码全 0，ERROR 0。
  - 没有用 `--rounds=1`：失败只出现在他人链接平移本机棋盘之后，`--rounds=1` 下链接还没发生，测不到。改用缺省 4 大回合，18 次发生了首轮链接，`z1s2`、`z2s2` 两次 4 大回合内没有链接。
  - 原失败局面 `z2s4` 现为 25 / 25，链接后有一次重搭。
  - 覆盖本机为被贴方并被平移的局面（如 `z4s2` 2 + 4、`z5s2` 2 + 5）与他人链接收拢平移本机的局面（如 `z2s4`、`z4s1`）。
  - 每局一行明细在 scratchpad `f6open/`（不入库）。

## 2. J12-2：滑入起点封顶 4 格

- `LinkShowBeats.Expansion`：新增 `SlideCap = 4`。演出起点 = 终点沿贴边方向偏 `min(拼接前两战场相对偏移的切比雪夫长度, 4)` 格：东贴 `(k, 0)`，从东侧往西滑入；北贴 `(0, k)`，从北侧往南滑入。贴边方向按几何判断：滑动方最西列 > 不动方最东列即为东贴，否则为北贴。本机是被贴方时，滑动的是本机棋盘，几何相同。相机跟随（`CameraPose.FollowSlide`）读的是同一份 `FromDx / FromDy`，没有改。`LinkRecord` / `LinkLayout` 的真实平移不变。
- **改动的既有断言**（均带 `board-link-mode F.6：` 注释）：
  1. `首轮链接演出_四个节拍依次`：原为 `(slide.FromDx, slide.FromDy) == (myDx - theirDx, myDy - theirDy)`，现为 `Capped(myDx - theirDx, myDy - theirDy, east: true)`。
  2. `本机是被贴方_本机棋盘滑过去贴合`：原为 `== (theirDx - myDx, theirDy - myDy)`，现为 `Capped(theirDx - myDx, theirDy - myDy, east: true)`。
- **新增** `滑入起点封顶四格_真实平移照旧`（Theory × 4，每例锚点 / 被贴方两种视角都测）：手搭两块 5×5 棋盘和一条链接。东贴偏移 31 → `(4, 0)`；东贴偏移 3 → `(3, 0)`；北贴偏移 33 → `(0, 4)`；北贴偏移 2 → `(0, 2)`。同时断言布局换算里的真实平移没有被封顶。
- **变异**：MF6-3 不封顶 → 红 4（2 条改过的既有测试 + Theory 的大偏移例）；MF6-4 贴边方向判反 → 红 7（另含"本机为被贴方时滑入期间相机跟随本机棋盘"）。还原后同批 152 绿。

## 3. 截图（非 headless，1600×900，未读入会话）

- `f-splice-mid`（`--auto-demo --seed=5 --shot-show=expand`，帧 39）：自证行 `滑入半程（508‰，25 格，偏移 1.97,0 格）`，F.4b 时为 5.41。灰度均值 157.7 / 暗 4.3% / 亮 35.4%。
- `f-splice-mid-mover`（`--auto-demo --seed=2 --zone=3 --shot-show=expand`，帧 38）：`滑入半程（500‰，42 格，偏移 2,0 格）`，F.4b 时为 -15.5,0.5；`本机为被贴方：滑入半程相机注视点 (5.5, 0.5)，起点 (3.5, 0.5)`。158.6 / 3.6% / 35.1%。
- 两张都重新生成了 `-gray`。README 两行补了"从东侧、起点封顶 4 格、半程偏移约 2 格"，帧号不变。

## 4. 构建、测试、自检

- `dotnet build siege.sln -c Release`：0 警告 0 错误；Godot 工程 0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**2395 条，通过 2385，跳过 10，失败 0**（新增 5 条：Theory 4 + Fact 1）。
- headless 顺序单跑，退出码全 0、ERROR 0：
  - `--auto-demo`：`[flow]` 6 条通过，开局对准 30 / 30；
  - `--auto-demo --pick-check --seed=5`：30 / 30 → 55 / 55；
  - `--map-select --auto-demo`：6 条通过，开局对准 35 / 35；
  - `--auto-demo --rounds=0 --seed=5`：13 条通过，七步齐全，开局对准 30 / 30。

## 5. 偏离与需裁决（J13 起）

| 编号 | 事项 | 现状 | 建议 |
|---|---|---|---|
| J13-1 | J12-1 的修复位置：裁决写"修在 `BoardCamera` / `CameraPose` 一侧"，实际根因在引擎层重搭判据（视图指纹不看坐标），相机无错 | 修在 Presentation 的 `DefaultBoardView.LayoutKey()`，有单元测试和变异；Godot 只改为调用它；相机代码未动 | 接受 |
| J13-2 | 20 次开局对准没有用 `--rounds=1`，用的是缺省 4 大回合 | `--rounds=1` 下链接还没发生，原缺陷测不到 | 接受 |
| J13-3 | 被贴方的演出方向按裁决固定为"从东侧 / 北侧"，可能与真实位移方向相反。例：种子 2、`--zone=3`，3 号实际是向东平移了 31 格，演出改成从东侧 4 格往西滑入 | 按裁决实现 | 接受；若想"方向随真实位移、只封顶距离"，再改一行 |
| J13-5 | "本机为被贴方时本机棋盘从反方向滑过去"的读法 | 按同一世界几何实现：滑动方总是从贴边一侧 4 格处滑向锚点，与谁在看无关；由于相机跟随本机棋盘，屏幕上看起来是锚点棋盘从反方向靠过来 | 接受；若要别的读法请裁决 |
| J13-6 | 北贴 / 决赛的封顶分支 `(0, k)` 只有合成 Theory 覆盖 | headless 演出为零时长，不输出 `[slide]`；两张截图都是首轮东贴，没有实际看过决赛滑入。东贴、北贴是同一个表达式，MF6-3 / MF6-4 都红 | 接受；需要时补拍一张决赛滑入中间帧 |
| J13-4 | 修复后，他人链接平移本机棋盘时引擎层会整体重搭一次（之前漏搭），会多一次 `[parts]` / `[batch]` 输出 | 无人值守下没有可见代价 | 接受 |
