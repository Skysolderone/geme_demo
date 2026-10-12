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
