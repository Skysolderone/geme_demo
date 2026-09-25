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

## 段 B：四类信物、生成权重与工坊（tasks 2.1–2.7）

基线（段 A 提交 854d371）：`dotnet test -c Release` 通过 1484 / 跳过 5 / 失败 0。段末：通过 1526 / 跳过 5 / 失败 0（新增 42 条用例，含 Theory 行）。

### 改动文件

- Core 规则：`Relics/RelicContent.cs`（`RelicType` 末尾追加 `Encampment, Pincer, Relay, Workshop`；`HasAdvancedTier` 是"哪些类型可升级"的唯一定义，构造拒绝新四类强度 2）、
  `Relics/RelicWeights.cs`（重写：`Order` 十类、`OrderOf / TableOf(zone, set) / TotalOf / RarityScaleOf / WeightOf / RarityOf` 全部按内容集，删掉不带内容集的旧重载与 `RarityScale` 常量）、
  `Relics/RelicGenerationOptions.cs`（`ContentSet`，缺省 v2；`BudgetOf` 按内容集、升级只计原六类）、`Relics/RelicGenerationRecord.cs`（记录 `ContentSet` 进值相等；`RelicPlacement.Rarity` 改为 `RarityIn(set)`，记录上 `RarityOf(placement)`；`Serialize()` 不变）、
  `Relics/RelicGenerator.cs`（`Generate(map, seed, ContentSet)`；`Draw` 按内容集取表、新四类不消费升级抽签、徽记绑定 `ContentSets.PieceTypesOf(set)`；重抽与惩罚按内容集的稀有度刻度）、
  `Relics/EffectSnapshot.cs`（`RelaySources`（坐标 → 该枚加成）/ `RelayBonus` / `WorkshopActive`，进值相等与哈希；构造多两个可选参数）、
  `Relics/RelicLedger.cs`（快照：驿站、工坊；`TrueContents()`；控制判定抽走）、`Relics/RelicControl.cs`（`RelicControl.Of`：控制判定唯一实现，账本第 5 步与势力计算共用）、
  `Scoring/ScoringRelicCounts.cs`（新）、`Scoring/PieceEffects.cs`（`LineBonus(…, encampments)` / `SynergyBonus(…, pincers)`、`SynergyPerType`）、`Scoring/PowerCalculator.cs`（`Compute(board, roster, knownRelics)`、`Evaluate(…, ScoringRelicCounts)`）、`Scoring/PowerScoreboard.cs`（带已知内容的重算重载）、
  `Board/TerrainEditRules.cs`（`LegalTargets / IsLegal / Reject` 带工坊参数的重载，旧签名 = 否；格目标候选 `CellTargets` 一处给出）、`Batch/BatchContext.cs`（`WorkshopActive`）、`Batch/BatchRehearsal.cs`、`Preview/BatchPreview.cs`、
  `Match/MatchFlow.cs`（建局按内容集生成信物；三处势力榜重算传 `Relics.TrueContents()`；`EnterDeploy` 从快照带工坊）、`Match/MatchFlow.Persistence.cs`（恢复按存档内容集生成信物；弃赛快照存驿站来源与工坊）、`Match/MatchFlow.Preview.cs`（展示数来源逐枚列驿站；顺序预测传真实内容）。
- 最小占位（穷举 switch 缺项即抛，v2 局一揭示新信物就崩；均标注属段 C）：`Ai/RelicEstimate.cs`（`ValueOf` 补 D10 初值 驿站 6 / 工坊 4 / 连营 3 / 犄角 3；先验表与期望暂钉 v1 百分制表，AI 行为与改动前相同）、`Siege.Presentation/Text/Labels.cs`（`Relic` 补四个名称）。终端 `BoardRenderer.RelicName / RelicLetter` 有兜底不抛，未动。

### 2.1 测试（先红）

新增 Requirement 类（`tests/Siege.Core.Tests/RelicEffects/`）：`驿站的展示数加成Tests`（6）、`工坊扩大匠人的格改造范围Tests`（3，其中「控制工坊即标记生效」另走一遍真实对局流程：快照 → 部署上下文 → 隔一格暂放）、`计分信物连营与犄角Tests`（7 + 2.5 的性质测试 1）。
既有类追加 MODIFIED 的新 Scenario：`六类原型信物的效果Tests.新四类信物没有高阶版`、`效果快照在小回合开始时生成Tests.驿站按快照时的控制计数`、`同类信物叠加且无统一硬上限Tests.工坊不按数值相加`；
`出生区信物权重Tests`：`新四类在出生区各约5`、`内容集v1保持旧表`、`徽记可绑定匠人`（改 Theory v1 / v2）、`权重分布收敛`（改 v2 表）；`公共争夺区信物权重与高阶升级Tests`：`新四类不升级`、`新四类在公共区各约7`、`v2随机消费顺序与规格一致`、`高阶比例`（改 Theory）；
`改造合法性Tests`：`工坊下隔一格搭桥`、`工坊下隔着深水烧林`、`无工坊不得隔一格`、`工坊不含斜向与更远的格`（Theory：G7 / J6 / F9 / E8）、`工坊不扩大边目标`、`新棋子不得改造`；`私人征募面板Tests.驿站生效`；
`改造在预演中的呈现Tests.工坊下列出隔一格目标`（batch-preview 该 Scenario 是 2.6"批次预演按快照传入"的验证，放在本段；同 Requirement 的两条计分信物预演 Scenario 留段 C 3.5）。
2.4 追加（值对象 / 存档字段）：`效果快照在小回合开始时生成Tests.快照值相等含驿站来源与工坊标记`、`对局持久化Tests.弃赛快照的驿站来源与工坊标记随存档往返且旧存档缺省为空与否`——写于 2.4、在实现之后，由变异 MB-S6 / MB-S7 证其会红。

先红记录：先落 API 骨架（枚举四值、`RelicWeights` 按内容集的表、`EffectSnapshot` 两个新属性恒空 / 否、`BatchContext.WorkshopActive`、`TerrainEditRules` 工坊重载忽略参数、`PowerCalculator.Compute(…, knownRelics)` 计数恒 0、`HasAdvancedTier` 恒真、生成器 `Draw` 强制按 v1 取表），红都是断言红：
全量红 36 = 本段新增 / 改写的 35 条 + `总势力Tests.领地计分直接取空格归属结果`（OwnershipOf 读者名单，见改写表）。骨架态就绿的：`无工坊不得隔一格`、`工坊不扩大边目标`、`新棋子不得改造`、`争议不生效`、`内容集v1保持旧表`、`高阶比例(V1)`、`徽记可绑定匠人(V1)`、`拒绝理由与合法目标集合一致(False)`——描述的是不变量或 v1 行为，分别由 MB-W4 / MB-W3 / MB-W8 / MB-P2 / MB-G2（`内容集v1保持旧表`、`徽记可绑定匠人(V1)`）/ MB-W4（`拒绝理由…(False)`）证其会红；`高阶比例(V1)` 沿用改写前已记录的 M-G6 / M-G7。
`工坊扩大匠人的格改造范围Tests.控制工坊即标记生效` 里"真实对局流程"那一段（快照 → 部署上下文 → 隔一格暂放）同样是实现之后补的，由 MB-W6 证其会红。

### 2.2 生成（D6 / D7 / D8）

- v1 = 原百分制两表 + 刻度 10000 + 徽记六种，v2 = 千分制两表 + 刻度 100000（= 100 × 表合计）+ 徽记十种；升级判定与消费只对原六类（`HasAdvancedTier`）。
- 建局 `MatchFlow.Create` 按 `MatchOptions.ContentSet` 生成；`MatchFlow.Restore` **先按存档内容集（缺字段按 v1）再生成**（原先在解析内容集之前就按缺省生成）。
- v1 逐格一致：动生成器之前用临时测试抓取各内置图种子 0–499 的 `Serialize()` 拼接 SHA-256 与未收敛局数（改动前代码，已删临时测试），`内容集v1保持旧表` 钉住这 4 个黄金值。
- 分布（标准图 v5，10000 种子只跑生成；第一阶段 MaxRerolls = 0，单位 0.01%）：

| 分区 | 内容集 | 徽记 / 探勘 / 兵站 / 征召 / 军令 / 先锋 / 连营 / 犄角 / 驿站 / 工坊 | 期望 |
|---|---|---|---|
| 出生区 | v1 | 45.11 / 19.88 / 15.14 / 7.92 / 7.00 / 4.92 / – | 45 / 20 / 15 / 8 / 7 / 5 |
| 出生区 | v2 | 35.82 / 16.01 / 12.17 / 6.22 / 5.47 / 4.06 / 5.21 / 5.07 / 4.97 / 4.97 | 36 / 16 / 12 / 6.4 / 5.6 / 4 / 5 × 4 |
| 公共区 | v1 | 29.95 / 15.20 / 10.09 / 14.94 / 14.81 / 14.98 / – | 30 / 15 / 10 / 15 / 15 / 15 |
| 公共区 | v2 | 21.38 / 11.04 / 7.05 / 10.76 / 10.82 / 10.92 / 6.98 / 7.10 / 6.97 / 6.93 | 21.6 / 10.8 / 7.2 / 10.8 × 3 / 7 × 4 |

  公共区原六类升级率：v1 17.9%、v2 17.6%（该图 4 Standard + 1 High，期望 18%）；新四类升级 0。测试口径（合成 4:2 图 / 标准图、容差 ±5‰–±10‰）见各用例注释。

### 2.3 出生区预算收敛（10000 个种子只跑生成，临时测试，已删）

| 地图 | v1 未收敛 | v1 平均重抽 | v2 未收敛 | v2 平均重抽 |
|---|---|---|---|---|
| siege-4p-base-v5 | 24.29% | 57.83 | 19.48% | 51.39 |
| siege-2p-base-v1 | 19.50% | 43.36 | 12.82% | 29.93 |
| siege-3p-base-v1 | 22.34% | 50.58 | 16.07% | 39.69 |
| siege-frontier-v2 | 52.25% | 118.09 | 33.27% | 87.84 |

v2 全部四张图都**下降**，未恶化，不提负责人。第二阶段校正后的出生区终态分布（标准图 v2）：徽记 27.9 / 探勘 22.4 / 兵站 16.8 / 征召 10.7 / 军令 6.0 / 先锋 1.2 / 新四类各约 3.7%（v1：42.4 / 30.9 / 15.5 / 6.3 / 3.8 / 0.9）——校正把高稀有度类型挤出出生区，新四类（稀有度 2000）同样被压低，与既有机制一致，只报告。
`区域强度预算Tests.出生区稀有度均衡` 改为 v1 / v2 各跑一遍（v2 收敛率满足该用例的 ≥ 50% 下界）。

### 2.4 效果快照（D4 / D5）

- 驿站：`BuildSnapshot` 数出本玩家 `GrantsEffectTo` 的全部信物（含先锋、其他驿站，每枚按 1 计），每枚驿站加 `总数 − 1`，逐枚记入 `RelaySources` 并并入 `RevealCount`；来源拆分（`PublishSupplement`）直接取 `RelaySources`，不重算，一致性抛出照常把关。只控制驿站本身时来源列一条 +0（见待决 4）。
- 工坊：受控即置 `WorkshopActive`，不叠加。连营 / 犄角在快照里 `break`（计分信物不进快照）。
- 弃赛快照存档：`ResignationSaveData.RelaySources`（列表）/ `WorkshopActive`（可空），缺字段 → 空 / 否。

### 2.5 计分信物（D3）

- `PowerCalculator.Compute(board, roster, knownRelics)`：对已知内容里的连营 / 犄角逐格用 `RelicControl.Of`（与账本同一份）按本次覆盖表与名册判定控制，只计 `GrantsEffectTo`（参赛中且控制）；连营并入 `LineBonus`（每线 `+k × L`）、犄角并入 `SynergyBonus`（每种 `2 + k`），与其余位置加值一起被倍率放大。旧签名 = 无计分信物。
- 正式结算（结算第 5 步、大回合结束、弃赛 / 恢复重算）与顺序预测传 `Relics.TrueContents()`。预演、AI（`BatchPreview` / `BatchEvaluator`）与终端仍走旧签名，属段 C 3.3 / 3.5（见待决 3）。
- 性质测试：v2、标准图种子 0–149 的生成 + 种子驱动随机盘面（十种类型、四名所有者，1/3 密度），结算（揭示 + 控制）后"真实内容"与"已揭示内容"两种输入的势力明细逐项相同；样本下界：≥ 8 个盘面的势力确实受计分信物影响（种子 0–59 实测 6 个，故取 150 个种子）。

### 2.6 工坊（D5）

- `TerrainEditRules.CellTargets`：几何四邻 ∪（工坊）同行 / 同列距离 2 的棋盘内格；`LegalTargets` 与 `Reject` 共用。边目标仍按几何四邻枚举。工坊为真时越界的格目标报"超出工坊扩展后的范围"，为假时仍报原文案"不是几何四邻"。
- 批次层：`BatchContext.WorkshopActive` ← `EnterDeploy` 取本小回合快照；`BatchRehearsal` 第 1 步与 `BatchPreview` 的改造目标枚举都传它。AI 的 `HeuristicTurnController.EditOptions` / K 预筛回退仍走无工坊旧签名（段 C 3.3）。
- 守门 `拒绝理由与合法目标集合一致` 改 Theory，工坊真 / 假各穷举一遍，并断言工坊一轮确实穷举到隔一格目标。

### 2.7 v2 依赖走法的期望（归因）

本段改动后全量只有两条既有测试因 v2 分叉，均为"信物分布"一类，且二者的黄金值本身就是**引入新信物之前**的分布、测试钉的是随机子流互不扰动（与内容集无关），故写死 v1、黄金值不重建：
`原型插旗替代路径Tests.选区不扰动其他随机`（种子 42 标准图的逐格分布）、`对局配置公开完整地图标识Tests.地图种子不扰动对局随机`（边疆图信物摘要）。
其余依赖 AI 走法的黄金哈希 / 样本在段 A 已写死 v1（探针 P1 / P3），本段没有新的分叉；未挑种子。探针 PB-2（`ContentSets.Default` V2 → V1，全量跑）红 9，与段 A P2 的 9 条逐条相同（全是按设计断言 v2 缺省的用例），**本段新增 / 改写的用例与未改写的既有用例在 v1 下 0 红**。

### 变异验证

脚本沿用段 A 的 `mut.py`（二进制读写、按文件行尾归一锚点、`count == 1`、finally 还原并逐字节比对、`os.utime`、`DOTNET_CLI_UI_LANGUAGE=en`），每条先 `dotnet build siege.sln -c Release` 再全量 `dotnet test -c Release --no-build`。29 条变异 + 1 个探针全部编译通过、restored = True；跑完后对全部未提交文件做 SHA-256 比对，与跑前 0 差异。

| 变异 | 改动 | 红 | 红的用例 |
|---|---|---|---|
| MB-G1 | 新四类照常消费升级抽签（结果丢弃） | 1 | v2随机消费顺序与规格一致 |
| MB-G2 | 徽记绑定改回 `Enum.GetValues`（v1 也 10 选 1） | 8 | 内容集v1保持旧表、徽记可绑定匠人(V1)、v1逐步相同、两条黄金哈希、选区 / 地图种子不扰动、批量执行并汇总 |
| MB-G3 | 稀有度刻度恒 100000（v1 也按千分制刻度） | 2 | 稀有度按权重倒数计分且高阶两倍计、高风险区预算更高 |
| MB-G4 | 恢复存档时 v1 存档按 v2 生成 | 1 | 旧存档照常读取 |
| MB-G5 | 建局不按对局内容集生成（恒 v2） | 10 | v1逐步相同、旧存档照常读取、两条黄金哈希、选区 / 地图种子不扰动、揭示时间可查、地形可离线重建、类型计数与明细自洽、批量执行并汇总 |
| MB-G6 | 新四类允许强度 +2 | 1 | 新四类信物没有高阶版 |
| MB-S1 | 驿站不计其他驿站（`总数 − 驿站数`） | 1 | 两枚驿站互相计入 |
| MB-S2 | 驿站计入争议信物 | 1 | 争议信物不计 |
| MB-S3 | 高阶信物按强度计枚数 | 1 | 高阶信物按1枚计 |
| MB-S4 | 工坊当小回合生效（`WorkshopActive` 改为回读账本的活视图） | 2 | 新占工坊下一小回合生效、弃赛快照往返 |
| MB-S5 | 驿站加成不并入展示数 | 7 | 驿站 4 条、驿站按快照时的控制计数、驿站生效（征募面板）、弃赛快照往返 |
| MB-S6 | 弃赛快照存档漏写驿站来源 | 1 | 弃赛快照的驿站来源与工坊标记随存档往返… |
| MB-S7 | 快照值相等漏比驿站来源 | 1 | 快照值相等含驿站来源与工坊标记 |
| MB-S8 | 结构参数来源不列驿站 | 1 | 与探勘相加（一致性校验抛出） |
| MB-P1 | 连营 / 犄角走快照（每名玩家小回合开始时冻结计数，[ThreadStatic] 探针） | 6 | 失去控制立即失效 + 5 条纯计算用例（探针为线程静态、同线程前一局的冻结值泄漏过来——附带红，不作守门依据） |
| MB-P2 | 争议仍生效 | 2 | 争议不生效、失去控制立即失效 |
| MB-P3 | 弃赛者享受（封锁也计） | 1 | 弃赛者不因计分信物得分 |
| MB-P4 | 连营加值挪到倍率之外 | 5 | 连营加值被倍率放大、连营加成、两枚连营、失去控制立即失效、弃赛者不因计分信物得分 |
| MB-P5 | 结算第 5 步不传真实内容 | 1 | 失去控制立即失效 |
| MB-P6 | 犄角不生效 | 2 | 犄角加成、已结算盘面上真实内容与已揭示内容结果相同 |
| M-A2r | **段 A M-A2 的真正变异**：旗手只数"已知信物内容"里的格（预演 / AI 传的是已揭示内容） | 6 | 旗手「不看揭示与控制」「同一信物格分别为两枚旗手子计分」「站在信物格上并邻接另一信物格」、七来源可溯源、新来源一并被倍率放大、千例随机局面预演与结算一致 |
| MB-W1 | 工坊格目标含斜角 | 4 | 工坊不含斜向与更远的格 ×4 |
| MB-W2 | 工坊扩到距离 3 | 6 | 工坊不含斜向与更远的格 ×4、工坊不按数值相加、多枚工坊不叠加 |
| MB-W3 | 工坊扩了边目标（枚举与拒绝两侧） | 1 | 工坊不扩大边目标 |
| MB-W4 | 无工坊也能隔一格 | 7 | 无工坊不得隔一格、拒绝理由与合法目标集合一致(False)、控制工坊即标记生效、新占工坊下一小回合生效、工坊下列出隔一格目标、v1逐步相同、V4 黄金哈希（AI 改造枚举变多） |
| MB-W5 | 批次预演第 1 步不传工坊 | 9 | 工坊下隔一格搭桥 / 隔着深水烧林、工坊不含斜向 ×4、三条工坊效果用例 |
| MB-W6 | `EnterDeploy` 不从快照带工坊 | 1 | 控制工坊即标记生效（对局流程段） |
| MB-W7 | 富预演的改造目标不传工坊 | 1 | 工坊下列出隔一格目标 |
| MB-W8 | 新四种棋子可携带改造 | 1 | 新棋子不得改造 |
| PB-2（探针） | `ContentSets.Default` V2 → V1 | 9 | 与段 A P2 相同的 9 条 v2 缺省断言 |

### 既有测试改写（逐条理由）

| 测试 | 改写 | 理由 |
|---|---|---|
| `区域强度预算Tests`（全类） | 生成钉 v1（`V1` 选项）；`RarityOf / WeightOf` 显式传 v1；`.Rarity` → `RarityIn(V1)`；另补 v2 刻度（2500 / 277 / 2000 / 1428 / 925 / 1850）与 v2 预算（1000 / 1090 / 1180）断言；`出生区稀有度均衡` 改 Theory v1 / v2 | 字面量（222 / 2000、600 / 690 / 780、不收敛图的稀有度集合）出自 v1 百分制表；API 改为按内容集（删了不带内容集的重载） |
| `出生区信物权重Tests.权重分布收敛` | 期望改为 v2 千分制十类表；v1 旧表断言原样移到 `内容集v1保持旧表` | 规格 MODIFIED；缺省 v2 |
| `出生区信物权重Tests.徽记可绑定匠人` | Fact → Theory（v2 十种 / v1 六种），v2 种子 6000、容差 ±15‰ | 规格 MODIFIED（段 A 待决 3 按"v2 十种显式列表、v1 原六种"落地） |
| `公共争夺区…Tests.高阶比例` | Fact → Theory（v2 / v1），升级率只统计原六类 | 规格 MODIFIED："统计公共区信物中属于原有六类的部分" |
| `公共争夺区…Tests.基准图上公共信物升级率落在宽口径` | 升级率分母改为原六类枚数 | 同上（新四类不升级会把总体率稀释到约 13%） |
| `公共争夺区…Tests.高档升级率严格高于标准档` | 生成钉 v1 | 钉的是两档升级率本身；v2 下新四类稀释两档，区间是按"全部可升级"写的 |
| `总势力Tests.领地计分直接取空格归属结果` | `OwnershipOf(` 名单加 `RelicControl.cs`；`OwnershipKind.Exclusive` 名单 `RelicLedger.cs` → `RelicControl.cs` | 控制判定从账本私有方法抽成 `RelicControl.Of`，供势力计算读取计分信物共用（避免第二实现）；名单要求"新读者改表并写明理由" |
| `改造合法性Tests.拒绝理由与合法目标集合一致` | Fact → Theory（工坊真 / 假），加工坊轮确实含隔一格目标的下界 | tasks 2.6：守门扩到工坊为真 |
| `旗手子的位置加值Tests.不看揭示与控制` | 追加一条"只知 F7 不知 G6 的已知内容"输入下仍为 6 | 势力计算多了已知信物内容输入；让段 A 的 M-A2 真正变异有落点 |
| `原型插旗替代路径Tests.选区不扰动其他随机`、`对局配置公开完整地图标识Tests.地图种子不扰动对局随机` | 写死 v1 | 见 2.7 |

### 段末自验

- `dotnet build siege.sln`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test -c Release`：通过 1526 / 跳过 5 / 失败 0。
- `openspec validate more-pieces-relics --strict`：valid。
- 未跑批量对局、未跑 200 局与慢测试；10000 种子统计只跑信物生成；全程同一时间只有一个 dotnet。

### 待决（交主会话 / 段 C–D）

1. 最小占位：`RelicEstimate.ValueOf` 已含 D10 初值（驿站 6 / 工坊 4 / 连营 3 / 犄角 3），先验表与期望仍钉 v1 百分制（v2 局的未揭示期望因此仍是 5 / 6，不是 D10 的 5 / 5）；段 C 3.2 改按内容集取表与表合计归一，届时"ValueOf 新四类"那部分已绿，先红只能落在期望值上。`Labels.Relic` 四个名称同属占位。
2. AI 改造枚举（`HeuristicTurnController.EditOptions` 与预筛回退）仍用无工坊旧签名：v2 局里 AI 控制工坊也不会隔一格改造（合法集合的子集，不违规）。段 C 3.3。
3. 预演（`BatchPreviewBuilder`）、AI 评价（`BatchEvaluator`）、终端预演仍用无计分信物的 `Compute`：段 C 之前，v2 局里只要有人控制已揭示的连营 / 犄角，预演势力就低于结算（不只是"首次揭示那一批"）。段 C 3.3 / 3.5。
   新暴露面：`RelicLedger.TrueContents()` 是 public、返回全部（含未揭示）内容，`MatchFlow.Relics` 也是 public，`Siege.Core/Ai` 同程序集可以调到它——D3 明令预演 / AI 只用已揭示内容。建议段 C 3.3 接 AI 时把 `TrueContents(` 加进 `正式对战AI的信息边界Tests` 的 `Siege.Core/Ai` 禁 token 表（本段未加）。
4. 只控制驿站本身时，结构参数来源里列一条"驿站 +0"（规格"逐枚列出"的字面做法）；手牌面板文案是否隐藏 +0 由段 C / D 定。
5. `RelicGenerationRecord.Serialize()` 首行不含内容集（为保 v1 导出文本逐字节不变）；离线只凭首行复现 v2 分布需另读日志首部的内容集。是否在首行追加 `;content=` 请定。
6. `BalanceAnalyzer.Selection` 的信物类型列表仍是 `Enum.GetNames<RelicType>()`（与段 A 留下的棋子列表同样），v1 报告的选择率段落会多出四个 0 行；按内容集展开属段 C 3.7。
7. 段 A 待决 6（`校准后截断率达标_种子1至200` 读缺省配置）现在跑在 v2 且会揭示新信物；该用例是 200 局慢测试、默认跳过，本段未跑、未改。
8. `.trellis/spec/core/boundaries.md` 单一实现表可补：`RelicControl.Of`（信物控制判定）、`RelicContent.HasAdvancedTier`（可升级类型）、`TerrainEditRules.CellTargets`（含工坊的格目标候选）、`RelicLedger.BuildSnapshot` 的驿站加成——与段 A 待决 7 一并在收尾时定。
9. 段 A 待决 2（生成不区分内容集）、待决 3 的徽记部分已在本段落地。

## 段 C：AI 适配、终端、遥测（tasks 3.1–3.7）

基线（段 B 提交 1199e78）：`dotnet test -c Release` 通过 1526 / 跳过 5 / 失败 0。段末：通过 1559 / 跳过 5 / 失败 0（新增 33 条，含 Theory 行：`按分区权重估计未知信物` 6 → 16 行）。

### 改动文件

- Core AI：`Ai/RelicEstimate.cs`（重写：`PriorWeight / ScaleOf / ExpectedValueScaled / Estimate` 一律带内容集，删 `PercentScale = 100` 与不带内容集的旧重载；新四类估值标注未校准）、`Ai/BatchEvaluator.cs`（估值取 `view.ContentSet`；前后两次 `Compute` 传同一份 `RevealedRelics.Of(view.Relics)`；Growth 口径注释）、
  `Ai/HeuristicTurnController.cs`（完整枚举 `EditOptions(…, workshop)` 与预筛匠人回退都传 `context.WorkshopActive`；代表类型次序注释）、`Ai/EvaluationWeights.cs`（`ScoringExtendedStatus` + 补注）、`Ai/AiDifficulty.cs`（停手阈值口径补注）。
- Core 其余：`Relics/RevealedRelics.cs`（新："已揭示的公开信物内容"唯一投影）、`Preview/BatchPreview.cs`（预演前后势力传已揭示内容）、`Board/TerrainEditRules.cs`（`IsWorkshopReach`：只作留痕 / 显示分类）、`Match/TerrainEditRecord.cs` + `Match/MatchFlow.cs`（改造留痕 `ViaWorkshop`）、
  `Scoring/PowerSnapshot.cs` + `Scoring/PowerCalculator.cs`（`GroupPower.EncampmentBonus / PincerBonus` 子拆分，见待决 1）。
- Sim：`Logging/MatchLog.cs`（`GroupEntry` 四项新来源 + 两项子拆分、`TurnSnapshot.RelaySources / WorkshopActive`、`TerrainEditEntry.ViaWorkshop`、`MatchLog.ContentSet`）、`Running/MatchSession.cs`（v2 才写新字段）、`Running/LoggingController.cs`（征募阶段采集本小回合效果快照）、
  `Analysis/BalanceAnalyzer.cs`（选择率列表按内容集、倍增子归因与高地分母减七项、第 12 项）、`Analysis/ReportWriter.cs`（第 12 项）、`Play/BoardRenderer.cs`、`Play/ConsoleController.cs`、`Play/PlayCommand.cs`。

### 3.1 测试（先红）

新增 Requirement 类：`AiDecision/新棋子与新信物的AI适配Tests`（六个 Scenario + Requirement 正文两条：`组合成长不计新来源与计分信物`、`AI源码不另写新加值公式`）、`BatchPreview/计分信物与新棋子的预演Tests`（两个 Scenario + 正文一条 `新棋子的位置加值计入预演`；第三个 Scenario「工坊下列出隔一格目标」段 B 已在 `改造在预演中的呈现Tests`）。
既有类追加：`正式对战AI的信息边界Tests.AI源码不读取真实信物内容`（段 B 待决 3 的禁用 token）、`默认评价权重的校准Tests.规则变更使校准失效` 追加补注断言（3.4）；MODIFIED 的 `对隐藏信息的概率估计Tests.按分区权重估计未知信物` 改为 v1 / v2 两表的 Theory。

先红（骨架：`RelicEstimate` 带内容集的新 API 仍钉 v1 表与 100、`ScoringExtendedStatus` 常量存在但未接进口径串）：红 16 = `按分区权重估计未知信物` v2 十行 + `未揭示信物的期望价值`（5152 ≠ 544）、`工坊扩展的目标进入候选`、`组合成长不计新来源与计分信物`（势力增量不含已揭示犄角）、`未揭示的连营不影响AI评价`（信物维 9 ≠ 7）、`已揭示连营计入预演`（6 ≠ 9）、`规则变更使校准失效`。
骨架态就绿的：`新信物的类型价值`（段 B 已放 D10 初值）、`内容集v1的期望价值不变`、`哨兵加值进入即时势力增量`（`Compute` 早已含哨兵）、`将揭示的连营不计入预演`、`新棋子的位置加值计入预演`、两条源码扫描、`按分区…` 的 v1 六行——描述的是现状或守门，逐条由下表变异证其会红（`新信物的类型价值` MC-V1、`内容集v1的期望价值不变` 与 v1 六行 MC-E3、`哨兵…` / `新棋子的位置加值计入预演` MC-X1 与 MC-G1、`将揭示…` MC-P2、两条扫描 MC-T1 / MC-T2）。

3.6 / 3.7 的测试（第二轮先红，骨架：日志字段已声明但写入端不写、终端新构造函数忽略两个委托、`RevealSourcesText` 返回空、分析器第 12 项恒空）：红 10 = `终端新棋子与新信物Tests` 4 条（字母、渲染快照、驿站来源、工坊目标）+ `新来源可查`、`驿站来源与工坊标记可查`、`新来源占比`、`棋子选择率与胜率`（v2 列十种）、`新来源归各自棋子不计入倍增子`、`内容集v1的报告除第12项外与引入新内容之前逐字节相同`。
骨架态就绿的：`内容集v1的图例不变`（描述 v1 现状）、`旧日志照常解析`（读取端），由 MC-R3、MC-L6 证其会红。

### 3.2 RelicEstimate（段 B 待决 1）

先验与期望按对局内容集取表（`RelicWeights.WeightOf / OrderOf(set)`），分母 = `RelicWeights.TotalOf(set)`（v1 100 / v2 1000，D6 不写死）。v2：出生区 5152 → 5、公共区 5692 → 5；v1：544 → 5、635 → 6。评价器从公开视图取内容集（`MatchPublicView.ContentSet`，开局固定、始终公开），调试旁路不变。

### 3.3 AI（段 B 待决 2、3）

- 已知信物内容：`RevealedRelics.Of(IEnumerable<RelicPublicState>)` 是"已揭示的公开内容"的唯一投影（以揭示标记为准，状态意外带了内容也不收），AI 评价、预演、终端预演都用它；评价器在构造时对批次开始前的公开视图取一次，结算前后两次势力计算共用——"将揭示"的计分信物天然不计，不另写判断。
- 改造枚举：完整枚举与预筛回退都把 `context.WorkshopActive` 传给 `TerrainEditRules.LegalTargets`；预筛代表类型 = 持有类型按枚举排序的第一个，枚举次序即规格的固定类型次序（D9），代码未改、只补注释。
- "组合成长"维口径不变：`GrowthOf` 仍走不带犄角的 `SynergyBonus` 旧重载、不含四项新来源（注释写明）。
- K = 0 / K > 0 在标准图上的既有等价测试保持绿（v1 与 v2 下预筛回退与完整枚举传的是同一个标记）。
- 段 B 待决 3 的禁用 token：`正式对战AI的信息边界Tests.AI源码不读取真实信物内容` 扫 `src/Siege.Core/Ai`（去注释）禁 `TrueContents(`，反面命中 `Match/MatchFlow.cs`，文件数下界 10。调试 AI 读真实内容走 `MatchDebugView.ContentOf`，不经 `TrueContents`，无白名单。
  MC-T1 证它比类型闭包多守了一层（private static 方法不进闭包，只有扫描会红）。

### 3.4 校准口径

`EvaluationWeights.ScoringExtendedStatus = "more-pieces-relics 扩展计分后未重扫"`：`CalibrationStatus`、`CalibrationOf` 九维（含已扫档的三维）与 `AiSearchConfig.PassThresholdCalibrationStatus` 一律以它结尾；权重与阈值数值不变（`默认权重被改动`、`默认停手阈值被改动` 照旧绿）。
testing.md「规则变更后权重 MUST 先重新标注」点名了停手阈值口径，故一并补注（tasks 3.4 未点名）。

### 3.5 预演

`BatchPreviewBuilder` 前后两次 `Compute` 传 `RevealedRelics.Of(relics.PublicStates())`。首次揭示计分信物的那一批，预演低于结算、差额恰为该信物的加成（`将揭示的连营不计入预演` 钉 3 点）；已揭示的计分信物预演与结算相同。

### 3.6 终端（段 A 待决 4、段 B 待决 4）

- `BoardRenderer`：四种新棋子 `N C T K` / 旗手 铁链 哨兵 界碑，中文快捷输入"旗 / 链 / 哨 / 碑"与全名；新信物 `y j r w` 与名称；`Letter / Name / RelicName / RelicLetter` 改为穷举、未知类型抛出（原兜底 `'?'` 与"未揭示信物"撞符号）。
  图例按对局内容集：v1 逐字不变，v2 另起一行列新四种棋子、已揭示信物一行末尾追加新四类。
- 驿站来源：征募阶段打印"展示数 N = 基础 5 + 探勘 … + 驿站 E5 +2（控制 2 枚其他信物）"，数值取 `PublicSupplement` 的结构参数来源，终端不重算；**+0 的驿站在终端隐藏**（没有可列来源时整行不打印）。图形面板（hand-info-panel「驿站来源逐枚列出」）的 +0 处理留段 D。
- 工坊目标：`ConsoleController` 新增两个观察委托（`PublishSupplement`、`PreviewCurrentBatch`，`PlayCommand` 传入）。`v` 预演改用 Core 富预演（势力前后、将揭示格、每枚暂放匠人的全部合法改造目标——目标集合来自改造合法性唯一实现；隔一格的目标注"（隔一格）"，工坊生效时另打一行提示），终端不再自己调 `PowerCalculator`。
  另支持 `D4 A B:D5` 暂放带改造的匠人（记法 `TerrainEdit.Parse`）——此前终端完全没有改造输入，只列目标不能选没有意义；超出 3.6 字面，见待决 3。

### 3.7 遥测（段 A 待决 3、段 B 待决 6）

- 日志字段（全部可空，**属性级** `JsonIgnore(WhenWritingNull)`——测试侧快照哈希用默认序列化选项，只有属性级忽略才能保证 v1 文本不多出 `"…":null`）：`GroupEntry` 旗手 / 铁链 / 哨兵 / 界碑四项来源与连营 / 犄角两项子拆分，`TurnSnapshot.RelaySources`（逐枚，含 +0）/ `WorkshopActive`，`TerrainEditEntry.ViaWorkshop`。
  **只在内容集 v2 的对局写出**（含 0 / 否 / 空表），v1 局一律不写 → v1 日志与改动前逐字节相同（`V4GoldenTurnHash`、`StrictImprovementTurnHash`、`v1逐步相同` 全部未动）。旧日志 / v1 日志读入按 0 / 否 / 空；内容集经 `MatchLog.ContentSet`（首部缺项按 v1）。
- 驿站来源与工坊标记取本小回合效果快照：快照在小回合结束即清空，会话给 `TurnTrace` 挂读取委托，装饰器在征募阶段取一次（装饰器本身仍不持有对局）。"经工坊扩展"由 Core 在结算留痕时按 `TerrainEditRules.IsWorkshopReach` 判定，日志层只转录。
- 分析器：选择率的棋子 / 信物列表按样本内容集展开（有 v2 局列十种 / 十类，否则六 / 六）；"各棋子势力占比"的倍增子放大部分改为减去七项来源，四项新来源各归旗手 / 铁链 / 哨兵 / 界碑（段 A 待决 3）；高地占比分母改为七项。
  第 12 项：四种新来源与连营 / 犄角额外加值各占终局位置加值的比例（六行固定、0 不省略）、驿站平均展示数加成（每小回合 / 有加成的小回合）、经工坊扩展的改造次数与占比；v1 对局（含首部缺内容集的旧日志）单列"不适用"，不进任何分母。
- v1 报告：黄金值取自引入新内容之前的提交 ad78d50（`git archive` 到 scratchpad 单独构建，同一共用样本去耗时字段后渲染的 SHA-256：共用样本 146 行 `8330…1FF1`、名次样本 152 行 `19E3…9B04`）。改动后去掉第 12 项那一段（v1 局只有"不适用"一行）逐字节相同，由 `内容集v1的报告除第12项外与引入新内容之前逐字节相同` 钉住。

### 变异验证

脚本 `mut.py`（本会话 scratchpad，沿用段 A / B 的做法）：二进制读写、按文件实际行尾归一锚点并断言命中恰 1 次、try/finally 还原、还原后与变异前读到的原文逐字节比对、`os.utime` 刷新 mtime、备份名带时间戳、`DOTNET_CLI_UI_LANGUAGE=en`；每条先 `dotnet build siege.sln -c Release` 再全量 `dotnet test -c Release --no-build`。
35 条全部编译通过（无编译假红）、restored = True；跑完后对全部未提交文件做 SHA-256 比对，与跑前 29 个文件 0 差异，随后重新构建并全量复跑确认全绿（1559 / 5 / 0）。

| 变异 | 改动 | 红 | 红的用例 |
|---|---|---|---|
| MC-V1 | 驿站估值 6 → 5（证骨架态就绿的 `新信物的类型价值`） | 12 | `新信物的类型价值`、`未揭示信物的期望价值`、`按分区…` v2 十行（期望和随之变） |
| MC-E1 | 期望的分母写死 100（tasks 3.2） | 12 | `按分区权重估计未知信物` v2 十行、`未揭示信物的期望价值`、`未揭示的连营不影响AI评价`（信物维） |
| MC-E2 | 先验恒取 v1 表 | 57 | 同上 v2 各条 + v2 缺省下凡是 AI 对未揭示信物估值的用例连带红（v1 表里没有新四类，`RelicWeights.WeightOf` 抛出——多数是 AI 估值抛异常，不是走法分叉） |
| MC-E3 | 分母写死 1000 | 10 | `按分区…` v1 六行、`内容集v1的期望价值不变`、三条 v1 黄金哈希（`缺省不限制时标准图整局与改动前逐步相同`、`阈值为0时零变化`、`v1逐步相同`） |
| MC-A1 | **AI 读真实内容**：正式 AI 的观察委托把未揭示信物按真实内容标成已揭示（tasks 3.3） | 9 | `未揭示的连营不影响AI评价`、`估计不得使用真实值`、`未揭示信物的期望价值`、`内容集v1的期望价值不变`、三条 v1 黄金哈希、`缓存开关不改变决策序列`、`地形可离线重建` |
| MC-A1b | 公开状态不看揭示即给出内容（`RelicState.ToPublic` 泄漏；揭示标记不变） | 14 | `未揭示的连营不影响AI评价`（信物维随内容变）、`未知信物不可读`、`将揭示提示`、`插旗阶段的可见信息`、`首次覆盖即揭示`、`调试AI可读全量状态` 等——`RevealedRelics.Of` 以揭示标记为准，势力增量不受影响，由信物维与既有信息边界测试抓 |
| MC-A2 | 评价器不传已揭示内容 | 2 | `未揭示的连营不影响AI评价`（已揭示对照 +2 → 0）、`组合成长不计新来源与计分信物`（势力增量） |
| MC-G1 | **Growth 计入新来源**（经势力计算取四项加值，tasks 3.3） | 2 | `哨兵加值进入即时势力增量`（成长 0 → 4）、`组合成长不计新来源与计分信物`（2 → 4）。tasks 写"既有 Growth 期望应红"：既有 Growth 断言的盘面只有原六种，新来源恒 0，改动前后都是 0 红——落点改由本段新增的两条承担 |
| MC-G2 | Growth 的协同按已揭示犄角计 | 1 | `组合成长不计新来源与计分信物`（2 → 3） |
| MC-W1 | 完整枚举不传工坊标记 | 1 | `工坊扩展的目标进入候选` |
| MC-W2 | 预筛匠人回退不传工坊标记 | **0** | 近等价变异，见待决 4 |
| MC-T1 | `BatchEvaluator` 加 `private static` 方法调用 `ledger.TrueContents()`（段 B 待决 3） | 1 | `AI源码不读取真实信物内容`（类型闭包守门 `未知信物不可读` 未红——private 成员不进闭包，扫描是真正多出的一层） |
| MC-T2 | AI 目录里直接调用 `PieceEffects.SentryBonus(…)` | 1 | `AI源码不另写新加值公式` |
| MC-C1 | 去掉 `CalibrationStatus` 的补注（tasks 3.4） | 2 | `规则变更使校准失效`、`默认权重的校准依据随值一起更新` |
| MC-C1b | 去掉 `CalibrationOf` 未扫档维的补注 | 1 | `规则变更使校准失效` |
| MC-P1 | 预演不传已揭示内容 | 1 | `已揭示连营计入预演` |
| MC-P2 | **预演改传真实内容**（tasks 3.5） | 1 | `将揭示的连营不计入预演` |
| MC-X1 | 势力计算不计哨兵加值（证"骨架态就绿"的两条） | 7 | `哨兵加值进入即时势力增量`、`新棋子的位置加值计入预演`、`新来源可查` + 段 A 的四条哨兵 / 倍率 / 七来源 |
| MC-R1 | 界碑字母 K → T | 2 | `棋子与信物字母两两不同`、`新棋子与已揭示新信物的渲染快照` |
| MC-R2 | 驿站 +0 不隐藏 | 1 | `驿站来源逐枚列出且隐藏零加成` |
| MC-R3 | 图例不按内容集（v1 也列新棋子） | 1 | `内容集v1的图例不变` |
| MC-R4 | 终端暂放丢掉改造 | 1 | `工坊目标含隔一格的文字列出` |
| MC-L1 | 日志漏写哨兵来源 | 1 | `新来源可查` |
| MC-L2 | 不采集效果快照（快照漏写驿站来源 / 工坊） | 1 | `驿站来源与工坊标记可查` |
| MC-L3 | 日志层"经工坊扩展"恒否 | 1 | 同上 |
| MC-L3b | Core 留痕"经工坊扩展"恒否 | 1 | 同上 |
| MC-L4 | v1 局也写新字段 | 6 | 三条 v1 黄金哈希、`新来源可查`、`旧日志照常解析`、`驿站来源与工坊标记可查` |
| MC-L5 | 哨兵来源去掉**属性级** `WhenWritingNull` | 3 | 三条 v1 黄金哈希（测试侧快照哈希用默认序列化选项，`"SentryBonus":null` 混进文本）——属性级忽略是承重的 |
| MC-L6 | 首部缺内容集的旧日志按 v2 读 | 5 | `旧日志照常解析`、`新来源占比`、`棋子选择率与胜率`、两条棋子势力占比 |
| MC-S1 | 选择率列表恒按 v1 | 2 | `棋子选择率与胜率`、`插旗同区统计Tests.真实跑局的同区经首部进入统计`（v2 真实日志里的新类型键缺失） |
| MC-A12a | 第 12 项把 v1 对局也计入 | 1 | `新来源占比` |
| MC-A12b | 工坊占比分子计入全部 v2 改造 | 1 | `新来源占比` |
| MC-A3 | 倍增子放大部分只减三项（段 A 待决 3 的原状） | 1 | `新来源归各自棋子不计入倍增子` |
| MC-A4 | 高地占比分母只含三项 | 1 | 同上 |
| MC-A5 | 哨兵加值不归哨兵子 | 1 | 同上 |

### 既有测试改写（逐条理由）

| 测试 | 改写 | 理由 |
|---|---|---|
| `对隐藏信息的概率估计Tests.按分区权重估计未知信物` | Theory 6 行 → v2 十行 + v1 六行（加内容集参数）；`PriorPercent(zone, type)` → `PriorWeight(zone, type, set)`，544 → 按内容集 5152 / 544 | 规格 MODIFIED Scenario 给出 v2 / v1 两张先验表；API 按内容集（D6 删 `PercentScale`） |
| `对隐藏信息的概率估计Tests.估计不得使用真实值` | `Estimate(state)` → `Estimate(state, match.ContentSet)` | 签名多了内容集，断言不变 |
| `正式对战AI的信息边界Tests.未知信物不可读` | `ExpectedValueScaled(zone) / PercentScale` → `ExpectedValueScaled(zone, set) / ScaleOf(set)` | 同上；常量 `PercentScale = 100` 已删（分母取表合计） |
| `默认评价权重的校准Tests.规则变更使校准失效` | 未扫档维 `Assert.Equal(NotSweptStatus, …)` → `StartsWith`；追加九维与三处口径串以补注结尾的断言 | tasks 3.4 补注，数值不变 |
| `默认评价权重的校准Tests.默认权重的校准依据随值一起更新` | 钉死的 `CalibrationStatus` 字面量末尾加"；more-pieces-relics 扩展计分后未重扫" | 同上 |
| `默认评价权重的校准Tests.引用未校准维度产出的数据` | 未扫档维的筛选由 `== NotSweptStatus` 改为 `StartsWith` | 同上（样本下界 6 维不变） |
| `平衡分析方向Tests.棋子选择率与胜率` | 列表期望由 `Enum.GetNames` 改为 v1 六种 / 六类，报告不含新类型行；追加 v2 局列十种 / 十类 | 规格 MODIFIED「该批次内容集中的每一种棋子（v2 为十种）」；段 B 待决 6 |

v2 下依赖走法的期望：本段改变了 v2 的 AI 行为（公共区未揭示期望 6 → 5、已揭示计分信物进入势力增量、工坊目标进入枚举），但**没有任何既有测试因此分叉**——依赖走法的黄金值 / 样本在段 A / B 已写死 v1，写死 v1 的全部照旧绿；读缺省配置跑在 v2 的 `校准后截断率达标`（20 局，默认套件）照旧绿。无需重建、未挑种子。

### 段末自验

- `dotnet build siege.sln`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test -c Release`：通过 1559 / 跳过 5 / 失败 0（改动前 1526 / 5 / 0）。
- `openspec validate more-pieces-relics --strict`：valid。
- 未跑批量对局、未跑 200 局与 `Category=Slow` 慢测试（段 B 待决 7 的 `校准后截断率达标_种子1至200` 仍未跑）；全程同一时间只有一个 dotnet。v1 报告黄金值是在 scratchpad 里 `git archive ad78d50` 单独构建、只跑一条临时探针测试取得的（与主工作树串行，未碰 `.claude/worktrees/`）。

### 待决（交主会话 / 段 D）

1. **连营 / 犄角额外加值的来源**：第 12 项要"连营、犄角带来的额外加值占比"，但 D3 把它们并进连珠 / 协同，`GroupPower` 里原本分不出来。本段给 `GroupPower` 加了两个 init 属性 `EncampmentBonus / PincerBonus`（唯一构造点 `PowerCalculator.Evaluate` 用同一份 `PieceEffects.LineBonus / SynergyBonus` 在"无计分信物"下再求一次取差，只在控制计分信物时多算一次），明确是子拆分、不是第八 / 九项来源，七项恒等式与 `ToString` 不变。改动了 Core 计分记录的形状，请确认；备选是日志层自己用无信物重载重算（Sim 里的第二份计算），未采用。
2. **v1 报告多了第 12 项的两行**：规格要求 v1 对局在第 12 项"单列不适用"，于是只含 v1 局的报告多出标题 + "不适用"一行；其余逐字节与引入新内容之前相同（黄金值来自 ad78d50）。若要 v1 报告完全逐字节不变（第 12 项在无 v2 局时整段省略），改 `ReportWriter.AppendNewContent` 一处即可，请定。
3. **终端的改造输入**：3.6 只要求"工坊目标的文字列出"，但终端此前完全没有改造输入（`Stage` 不带改造），只列不能选没有意义；本段加了 `D4 A B:D5` 记法（`TerrainEdit.Parse`）并让 `v` 预演改用 Core 富预演（顺带修掉终端预演自己调旧签名 `Compute`、不计已揭示计分信物的问题）。超出 3.6 字面，请确认保留。
4. **预筛匠人回退的工坊标记（MC-W2 0 红）是近等价变异**：回退只在代表类型落不下时进入；工坊多出的只是隔一格的搭桥 / 烧林，它们既不给落点补气也不致提子，因自杀手落不下的格带上它们照样自杀手（格分仍为空），工坊标记对回退结果没有影响路径。唯一能生效的情形是代表类型因**同形禁则**（而非自杀手）被拒——带任何改造都会改掉同形键。规格要求传标记，代码已传；专用测试须构造"代表类型同形被拒"的格，本段未做，是否需要请定。
5. **驿站 +0**：终端隐藏（段 B 待决 4）；图形面板（hand-info-panel「驿站来源逐枚列出」）的处理留段 D。遥测照记 +0（逐枚、含 0）。
6. **调试 AI**：信物估值仍读真实内容（旁路不变），计分信物的势力增量与正式 AI 一样只用已揭示内容——调试 AI 的"全量读取"范围是否要扩到计分信物，未改，请定。
7. `.trellis/spec/core/boundaries.md` 单一实现表可补（与段 A 待决 7、段 B 待决 8 一并在收尾时定）：`RevealedRelics.Of`（已揭示公开内容的唯一投影：AI、预演、终端共用）、`TerrainEditRules.IsWorkshopReach`（"经工坊扩展"的唯一分类，只供留痕与显示）；"改造合法性"一行的调用点清单已不止两处（段 B 起还有 `BatchPreviewBuilder`、AI 预筛回退），该行文字需要刷新。
8. 段 A 待决 6 / 段 B 待决 7：`校准后截断率达标`（20 局，默认套件，读缺省配置即 v2）在本段 AI 适配之后仍绿；200 局版本未跑。
9. **本段之前产生的 v2 日志不能逐行回放**：v2 局的快照现在多写了新字段（v1 局不写），段 A / B 提交后若手动跑过 v2 对局日志（`sim-out/` 下），回放会在第一条快照处报分歧。v2 日志都在本 change 内部、未发布，按"接受"处理；段 D 的 20 局冒烟用本段之后的二进制跑即可。
10. 终端名称沿用终端原有的短名约定（"旗手 / 铁链 / 哨兵 / 界碑"，同"普通 / 堡垒…"），`Siege.Presentation.Labels.Piece` 是"旗手子…"（同"普通子…"）。hand-info-panel「以与盘面、终端一致的名称」若要求逐字一致，段 D 做面板时一并定口径。
