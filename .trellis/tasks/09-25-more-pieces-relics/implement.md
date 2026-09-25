# 实施记录：more-pieces-relics

## 段 A：内容集骨架 + 四种棋子的规则与计分（tasks 1.1–1.5）

基线（改动前）：`dotnet test -c Release` 通过 1443 / 跳过 5 / 失败 0。段末：通过 1484 / 跳过 5 / 失败 0（新增 41 条用例，含 4 个 Theory 行）。

### 1.1 测试（先红）

新增 Requirement 类（`tests/Siege.Core.Tests/PieceEffects/`）：`旗手子的位置加值Tests`（5）、`铁链子的位置加值Tests`（4）、`哨兵子的位置加值Tests`（5）、`界碑子的位置加值Tests`（4）。
既有类追加 MODIFIED 的新 Scenario：`六种原型棋子的基础军势Tests`（`各类型基础军势` 加四行 InlineData、`四种新棋子按1计`、`新棋子不免死`）、`协同子的位置加值Tests.新类型计入类型数`、
`倍增子的棋串倍率Tests.倍率放大新棋子加值` 与 `倍率作用于哨兵与界碑加值`（Requirement 正文"倍率作用于七项"，补哨兵 / 界碑两项，使"任一新来源不乘倍率"四条变异都有落点）、
`PowerScore/棋串军势公式Tests.新来源一并被倍率放大`、`PowerScore/势力明细Tests.位置加值七来源可溯源` 与 `不含新棋子的棋串新来源为0`。
夹具：`TestMaps.WithRelicCells`（带静态信物格的合成盘）。

- 连营 / 犄角两条 MODIFIED Scenario（`连营为连珠线额外加值`、`犄角提高每种加值`、`两枚犄角叠加`）依赖 `RelicType` 新值与"已知信物内容"输入，属段 B（tasks 2.5），本段未写。
- `铁链子「棋串分裂后重算」`：提子只会整串移除，一条己方棋串被一分为二只能来自改造切断气边——用例用立栅 E5–F5 切开 6 子串（全量重算与分裂成因无关），规格措辞"一次提子"见待决。

先红记录：先落 API 骨架（`PieceType` 追加四值、`GroupPower` 四个新字段恒 0），使红是断言 / 前提红而不是编译红：
① 骨架态（`BasePower` 仍对新类型抛）：红 30 = 新增 29 + 既有 `军势表穷举六种类型`（6 → 10 的改写）；绿 1：`不含新棋子的棋串新来源为0`（描述的就是改动前行为，作回归守门）。
② 1.2 落地后（基础军势与类型码已有，四项加值仍恒 0）：红 18，全部是加值断言；此时绿的"零值 Scenario"（栅栏 / 崖壁隔开、单子铁链、己方不计、高差 2 不计……）由 1.3 的变异证其会红。

### 1.2 枚举、基础军势、类型码

- `PieceType` 末尾追加 `Bannerman, Chain, Sentry, Boundary`（D9）；`PieceEffects.BasePower` 四种各 1（裁决 ②）。
- `GameBoard.TypeCode / TypeFromCode`：`N C T K`，旧码不动；`BatchFailure.DisplayName` 补四个中文名（该 switch 有兜底、不抛，仅为文案）。
- 测试：`盘面序列化Tests.十种类型的盘面码两两不同且往返保留类型`（原"六种"改写，见下表）、新增 `只含原六种类型码的旧盘面串照常恢复`（字面量旧串，恢复后逐格类型不变、再导出逐字节相同）、`新四种类型码固定为NCTK`。

### 1.3 四项位置加值（D1 / D2）

- `PieceEffects.BannerBonus / ChainBonus / SentryBonus / BoundaryBonus(board, coverage, group)`，常量 `BannerPerRelicCell = 3`、`SentryPerForeignStone = 2`、`BoundaryPerExclusiveCell = 1`。
  旗手读 `Cell.IsRelicCell`（地图静态位置）+ `LibertyNeighbors`；铁链只读 `Group.Size`；哨兵读 `LibertyNeighbors` 上的非己方占用者（不看玩家状态）；界碑读 `LibertyNeighbors` + 传入 `CoverageMap.OwnershipOf` 的独占结果（不看地表，荒漠计入）。
- `GroupPower` 在 `HighGroundBonus` 之后加 `BannerBonus / ChainBonus / SentryBonus / BoundaryBonus`；`PositionBonus` 与 `ToString` 扩为七项。唯一构造点 `PowerCalculator.Evaluate`。
- `PowerCalculator.Evaluate(board, coverage, group)`：签名多一个覆盖表（全仓无外部调用者，直接改签名；`ComputeCore` 传入已算好的那一份，不重复计算覆盖）；七项先求和再整体乘倍率。
- `SynergyBonus` 未改：类型数本就按 `HashSet<PieceType>` 计，新四种自然计入（`新类型计入类型数` 在 1.2 后即绿、1.3 前因铁链断言红）。

变异：见下「变异验证」表 M-A1…M-A9。

### 1.4 征募棋池十档 + 对局内容集骨架（D8）

- `Siege.Core.Board.ContentSet { V1 = 1, V2 = 2 }` + `ContentSets`：`Default = V2`、`Legacy = V1`、`PieceTypesOf(set)`（**全仓唯一的"内容集有哪些棋子类型"清单**，v1 显式六种、v2 = v1 + 四种，v1 是 v2 的前缀）、`RequireValid`。
- `RecruitWeights`：`Order` = 十种（= v2）；`UncalibratedNewPieceWeight = 8`（const，注释标未校准、不可配置）；`BaseTable` 十档；`OrderOf(set)` / `BaseWeightsOf(set)`；
  `AdjustedTable(snapshot, artisanWeight, contentSet)`（表长 = 该内容集类型数，v1 六档——新四种不在池中而不是权重 0，累积权重与改动前逐位相同）；不带内容集的重载取 `ContentSets.Default`。
- `HandLedger`：新增 `(players, seed, artisanWeight, contentSet)` 构造与 `Restore(..., contentSet)`，公开 `ContentSet`；`EnterRecruit` 按账本内容集取表。
- `MatchOptions.ContentSet`（缺省 v2）；`MatchFlow.ContentSet` / `ContentSetBackfilled`；`MatchFlow.Build` 校验并传给账本；`MatchPublicView` 在 `ArtisanWeight` 之后加 `ContentSet`（插旗阶段可读；唯一构造点 `Publish`）。
- 存档：`MatchSaveData.ContentSet`（可空，按枚举名写 `"V1"/"V2"`）；`RestoreCore` 缺字段按 v1 并置 `ContentSetBackfilled`，非法值响亮失败。
- 批次配置 / 日志首部：`RunConfig.ContentSet`（可空）；`Validated` 拒绝未定义值；`ResolvedFor` 未配置落成 v2（与 `FlagRisk` 同理：缺省 ≠ 旧日志回填值，必须落成才能区分）；
  `MatchSession.Create`：`config.ContentSet ?? (recorded ? v1 : v2)`；会话构造核对跑局配置与对局配置的内容集一致（同匠人权重）。首部随 `Config` 记录，未另起 `LogHeader` 字段。
- 日志棋子计数 `MatchSession.PieceCountsOf(board, group, contentSet)`：键集合取 `ContentSets.PieceTypesOf`（原先 `Enum.GetValues`——枚举一长，v1 局快照会多出四个 0 键，`TurnHash` 在走法不变时假分叉）。
- 信物生成 `RelicGenerator.EmblemPieces`：由 `Enum.GetValues<PieceType>()` 改为 v1 六种的显式列表——否则徽记绑定从 6 选 1 变成 10 选 1，v1 信物分布整体改变。**段 A 的 v1 / v2 都绑原六种**，按内容集分流属段 B（2.2）。
- 分析器 `BalanceAnalyzer.PieceShares`：类型列表按样本内容集（有 v2 局列十种，否则六种），v1 报告与改动前逐字节相同；完整的"按内容集展开 / v1 单列不适用"属段 C（3.7）。
- 表现层最小占位（均为穷举 switch / 查表、缺项即抛，v2 局一出现新棋子就崩）：`Labels.Piece` 四个名称；`PieceSilhouette` 追加 `Pennant / ChainLinks / CrossedSpears / Stele`、`SilhouetteLanguage` 追加 `Banner / Interlock / Crossed / Slab`，`PieceStyleTable` 补四行；
  Godot `LowPoly.Body` 四种新轮廓先共用一枚方柱占位。正式造型、灰度可辨、图例与手牌面板来源留段 C / D。终端 `BoardRenderer.Letter / Name` 有兜底（`'?'` / 英文名）不抛，本段未动（见待决）。
- 终端 `PlayCommand.Run` 加测试接缝 `contentSet`（同 `flagRisk` 先例，终端入口不传）。

新增测试：`Recruitment/初始配置与基础棋池Tests.新棋子在池中`（8 / 142 实抽样 + 反射确认对局 / 跑局配置里除匠人外没有"权重"项）、`内容集v1的棋池`（v1 表 = 前六项；v1 账本 1000 个面板逐位等于"用改动前的六档字面量表直接抽"，一枚新棋子都没有）；
`流派徽记调整征募权重Tests.新棋子徽记调权`；`MatchSetup/对局内容集Tests`：`旧存档照常读取`、`v1逐步相同`、`新局缺省v2`、`新存档往返`、`首部缺内容集的旧日志按v1回放`、`内容集须为已定义的值`。

先红记录（API 骨架态：`MatchOptions / RunConfig / HandLedger` 已有内容集参数但一律忽略，棋池仍六档、存档与首部不写字段）：红 11 = 新增 7（`新棋子在池中`、`新棋子徽记调权`、`对局内容集Tests` 除 `新存档往返` 外 5 条）+ 按 MODIFIED 改写的既有 4 条；
绿 2：`内容集v1的棋池`（骨架态就是 v1 行为）与 `新存档往返`（骨架不区分内容集），二者由变异 M-C1 / M-C4 证其会红。

### 1.5 守门：v1 逐步相同 / 黄金值写死 v1

写死 v1 的位置（均为依赖走法的期望或样本口径下界）：
- 夹具 `SimFixtures.PinPreCalibration` 加 `ContentSet = V1`（`Sample` / `RankedSample` / 黄金哈希 `候选格上限Tests.V4GoldenTurnHash`、`停手阈值Tests` 等全部经它）。
- 终端脚本：`PlayCommand.Run` 新测试接缝 `contentSet`，`终端对局Tests`、`终端活形与禁入标示Tests`、`边疆图终端试玩脚本Tests`、`各入口按地图标识选图Tests`（3 次调用）写死 v1（与 flag-contest 写死 `flagRisk: 0` 的同一批调用点）。
- 逐条：`两位数行号贯通Tests`（对局配置须与已写死 v1 的跑局配置一致）、`批量跑局Tests.批量执行并汇总`（"终局大回合 1–3"依赖走法，v2 下 1 局到第 4 大回合）、`各棋子势力占比Tests.真实跑局快照的类型计数与明细自洽`（`withArtisan` 样本）、
  `地形改造日志与分析Tests` 三条（`地形可离线重建`、`改造可查`、`真实批次的第11项分析自洽`）与 `活形记录与统计Tests.真实跑局的活形字段自洽`（后四条由探针 P1 找出，见下）；
  征募类 5 条改用 v1 账本 / 对局（期望序列用六档字面量表独立抽取，见改写表）。

**没有任何黄金哈希分叉。** `对局内容集Tests.v1逐步相同` 以 v1 重跑种子 31 / Standard / 24 小回合，快照哈希恰为改动前钉下的 `35E25329…8B88D3D7`；同配置 v2 与之不同。

探针（同一变异脚本，全量 `dotnet test -c Release`，均已还原并逐字节校验）：

| 探针 | 改动 | 结果 | 结论 |
|---|---|---|---|
| P1 | `UncalibratedNewPieceWeight` 8 → 400（放大 v2 与 v1 的差异） | 首跑红 12：按设计钉 v2 十档表的 8 条 + 4 条跟随缺省内容集、依赖走法、在 v2 下只因种子凑巧才绿的测试（地形改造日志 3 条、活形字段 1 条） | 后 4 条写死 v1；复跑只红那 8 条 |
| P2 | `ContentSets.Default` V2 → V1（整个既有套件在 v1 下跑） | 红 9，全部是按设计断言 v2 缺省的新增 / 改写用例（`新局缺省v2`、`首部缺内容集的旧日志按v1回放`、十档表 7 条）；**未改写的既有测试 0 红** | v1 路径与改动前逐步相同（覆盖全部既有黄金值与回放测试） |
| P3 | 去掉 `PinPreCalibration` 里的 `ContentSet = V1` | 红 5：`缺省不限制时标准图整局与改动前逐步相同`、`阈值为0时零变化`、`真实跑局快照的类型计数与明细自洽`、`领先者胜率回归`、`失败局可复现` | 夹具写死是承重的 |

### 变异验证

脚本 `mut.py`（scratchpad）：二进制读写、按文件实际行尾归一锚点并断言命中恰 1 次、try/finally 还原、还原后与变异前读到的原文逐字节比对、`os.utime` 刷新 mtime、备份名带时间戳、`DOTNET_CLI_UI_LANGUAGE=en` 解析统计行；每条先 `dotnet build siege.sln -c Release`（全部编译通过，无编译假红）再全量 `dotnet test -c Release --no-build`。基线：1484 通过 / 5 跳过。21 条变异 + 3 个探针（P1 写死后复跑一次）全部 restored = True。

| 变异 | 改动 | 红 | 红的用例 |
|---|---|---|---|
| M-A1 | 旗手改用几何四邻（`LibertyNeighbors` → `Neighbors`） | 3 | 旗手「栅栏隔开的信物格不计」「崖壁隔开的信物格不计」+ `四邻接Tests.几何邻居枚举只在允许名单内直接调用` |
| M-A2 | 旗手读控制：只计空的或己方占据的信物格（"读揭示状态"在计分层写不出来：本层只拿盘面、没有揭示状态；段 B `Compute` 有"已知信物内容"输入后补跑读揭示变异） | 1 | 「不看揭示与控制」 |
| M-A3 | 铁链用棋子数而非 −1 | 9 | 铁链 4 条全红 + 倍率放大新棋子加值、新棋子不免死、新类型计入类型数、七来源可溯源、新来源一并被倍率放大 |
| M-A4 | 哨兵计入己方棋子 | 3 | 「己方棋子不计」、倍率作用于哨兵与界碑加值、七来源可溯源 |
| M-A5 | 哨兵不计弃赛者（近似：排除 3 号玩家——计分层没有名册，写不出"只计参赛中"，用测试里弃赛者的编号代替） | 1 | 「弃赛者遗留棋子计入」 |
| M-A6 | 界碑滤掉荒漠 | 2 | 「独占荒漠格计入」+ `新地表判断唯一Tests`（源码扫描） |
| M-A7 | 界碑计入争议格 | 2 | 「争议格不计」、七来源可溯源 |
| M-A8a | 旗手加值挪到倍率之外 | 1 | 新来源一并被倍率放大 |
| M-A8b | 铁链加值挪到倍率之外 | 2 | 倍率放大新棋子加值、新来源一并被倍率放大 |
| M-A8c | 哨兵加值挪到倍率之外 | 1 | 倍率作用于哨兵与界碑加值 |
| M-A8d | 界碑加值挪到倍率之外 | 1 | 倍率作用于哨兵与界碑加值 |
| M-A9 | 界碑类型码 K → T（与哨兵撞码） | 3 | 十种类型盘面码两两不同、新四种类型码固定为NCTK、新存档往返 |
| M-C1 | v1 仍抽到新棋子（征募表恒取 v2 十档） | 24 | 内容集v1的棋池、v1逐步相同、旧存档照常读取 + 全部写死 v1 的黄金 / 样本 / 回放测试 |
| M-C2 | 存档缺字段按 v2 恢复 | 1 | 旧存档照常读取 |
| M-C3 | 日志回放缺字段按 v2 重建 | 1 | 首部缺内容集的旧日志按v1回放 |
| M-C4 | 存档漏写内容集 | 3 | 新存档往返、新局缺省v2、`对局持久化Tests.小回合边界存档恢复后状态完全一致` |
| M-C5 | `ResolvedFor` 不落成内容集 | 12 | 新局缺省v2、首部缺内容集的旧日志按v1回放、`引用未校准维度产出的数据` + 9 条回放测试（首部缺项 → 按 v1 回放 v2 局而分歧） |
| M-C6 | 日志棋子计数键仍取整个枚举 | 5 | v1逐步相同、两条黄金哈希、各棋子势力占比 2 条 |
| M-C7 | 徽记绑定仍用 `Enum.GetValues` | 7 | 徽记可绑定匠人、v1逐步相同、两条黄金哈希、选区 / 地图种子不扰动其他随机、批量执行并汇总 |
| M-A10 | 哨兵加值不再要求棋子是哨兵子（删掉类型判断） | 13 | `不含新棋子的棋串新来源为0`（骨架态先绿的那条，由此证其会红）、三来源可溯源、堡垒 / 匠人不免死、v1逐步相同、黄金哈希与 AI 走法类 8 条 |
| M-T1 | 只改测试：`棋池全局一致` 期望表对调前两档 | 1 | 棋池全局一致（测试没有照抄实现） |

### 既有测试改写（逐条理由）

| 测试 | 改写 | 理由 |
|---|---|---|
| `势力明细Tests.明细可复算总势力` | 恒等式 `PositionBonus = 连珠 + 协同 + 高地` 改为七项之和 | 规格「位置加值可溯源」来源扩为七项（该盘面只有原六种，值不变） |
| `六种原型棋子的基础军势Tests.军势表穷举六种类型` | 6 / 5 / 9 → 10 / 9 / 13，加"新四种在末尾" | 规格 MODIFIED：基础军势表十种、新四种各 1 |
| `盘面序列化Tests.六种类型的盘面码…` → `十种类型的…` | 6 → 10，方法名随之改 | tasks 1.2：往返测试含十种类型 |
| `总势力Tests.领地计分直接取空格归属结果` | `OwnershipOf(` / `OwnershipKind.Exclusive` 读者名单加 `Scoring/PieceEffects.cs` | 界碑（D2）逐格读传入覆盖表的独占结果、不另算覆盖；名单要求"新合法读者改表并写明理由" |
| `初始配置与基础棋池Tests`：`棋池全局一致`、`无徽记大样本分布贴合权重表`、`匠人在池中`、`匠人权重可配置` | 六档 → 十档（总权重 110 → 142，期望按 /142 重算） | 规格 MODIFIED；缺省内容集 v2。v1 六档的原期望原样移到新增的 `内容集v1的棋池` |
| `流派徽记调整征募权重Tests.单枚徽记调权`、`匠人徽记按同一公式调权` | 调整后权重表末尾加四档 8 × 4 | 缺省内容集 v2 的表为十档，Scenario 数值不变 |
| `流派徽记…调权后重新归一化`、`候选位独立且可重复`；`私人征募面板Tests.未选候选消失`；`征募随机可复现Tests.征募只消费recruit子流`；`对局持久化Tests.匠人权重随存档往返且旧存档回填` | 改用 v1 账本 / v1 对局 | 期望由六档字面量表独立抽取 / 按六档总权重算，依赖征募序列 → 写死 v1 |
| `出生区信物权重Tests.徽记可绑定匠人` | 期望由 `Enum.GetValues<PieceType>()` 改为 v1 六种 | 徽记绑定集合改为原六种显式列表（生成逐格不变）；v2 绑定十种属段 B |
| `六种棋子的轮廓语言Tests.轮廓可辨` | 6 → 10 | 表必须覆盖全部枚举值；段 A 按 D11 补四条占位标识 |
| `各棋子势力占比Tests.真实跑局快照的类型计数与明细自洽` | 键期望由 `Enum.GetNames` 改为 v1 六种；`withArtisan` 样本写死 v1 | 棋子计数键按内容集；样本口径下界依赖走法 |
| `各棋子势力占比Tests.含连珠线协同倍增的…` | `PieceCountsOf` 多传 `ContentSet.V1` | 签名多了内容集 |
| `批量跑局Tests.批量执行并汇总` | 配置写死 v1 | 见 1.5 |
| `默认评价权重的校准Tests.引用未校准维度产出的数据` | 期望配置加 `ContentSet = V2`，并断言未配置时为 null | D8：未配置的内容集落成缺省值写进 config.json（同 FlagRisk） |
| 其余写死 v1 的（`两位数行号贯通`、地形改造日志 3 条、活形字段 1 条、终端脚本 6 处调用） | 只加 `ContentSet = V1` | 见 1.5 |

### 段末自验

- `dotnet build siege.sln`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test -c Release`：通过 1484 / 跳过 5 / 失败 0（改动前 1443 / 5 / 0）。
- `openspec validate more-pieces-relics --strict`：valid。
- 未跑批量跑局、未跑 200 局与 `Category=Slow` 慢测试；全程同一时间只有一个 dotnet。

### 待决（交主会话 / 段 B–D）

1. 规格 `铁链子「棋串分裂后重算」` 写"一次提子把它分裂"：提子只会整串移除，己方棋串被一分为二只能来自改造切断气边。用例用立栅实现；是否改规格措辞请定。
2. 段 A 的信物生成不区分内容集（v1 / v2 都绑原六种徽记、用原百分制表）；v2 下"流派徽记可绑定十种"未生效，留段 B 2.2。
3. `BalanceAnalyzer`：`Power − Base − Line − Synergy − HighGround` 把"放大部分"整块归倍增子（第 772 / 1282 行附近），v2 样本里四项新来源会被算进倍增子；棋子占比列表只做了"有 v2 局列十种"。完整口径属段 C 3.7，段 C 前 v2 样本的该项分析口径不准。
4. 终端 `BoardRenderer.Letter / Name / TryParseType`：新棋子显示为 `?` / 英文名，中文快捷输入不认——有兜底不抛，按"只补会抛的"未动；v2 终端局里人工玩家暂时无法按字母下新棋子，属段 C 3.6。
5. 表现层占位：`GroupPowerView`（`PreviewPresentation`）只有连珠 / 协同 / 高地三项字段，位置加值文案也只列三项（总数含新来源，三项之和对不上）；`势力层领地与高地Tests` 的恒等式因此仍是三项（视图没有新字段），随势力层七项拆分（段 D 4.2）一并改。Godot 四种新轮廓共用一枚方柱。属段 C / D。
6. `默认评价权重的校准Tests.校准后截断率达标` 读缺省配置，现在跑在 v2 上（与 flag-contest 对缺省 p 的处理一致：钉校准值本身的测试读默认值），段 A 下仍绿；若应与内容集脱钩可写死 v1。
7. `.trellis/spec/core/boundaries.md` 单一实现表尚未登记四项新加值（`PieceEffects.Banner/Chain/Sentry/BoundaryBonus`，只经 `LibertyNeighbors` / 传入的 `CoverageMap`）与 `ContentSets.PieceTypesOf`（内容集类型清单唯一来源），是否在本 change 收尾时补。
