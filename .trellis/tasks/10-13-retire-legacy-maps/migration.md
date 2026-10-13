# retire-legacy-maps 测试迁移清单（tasks 1.1）

> 状态：第 1–4 组完成，第 5 组文档同步完成（归档 5.4 留给主会话）。本文件逐条记录"依赖旧地图的测试类 → 测的规则 → 处理 → 迁移后的测试名"，以及每组的通过数对账。
> 处理取值：**删**（只测旧对象本身，或所守规则随被删代码一起消失）/ **迁 link**（改到 `link:` 地图）/ **迁手写**（改到手写夹具）/ **不动** / **改钉**（同一规则，黄金值在删除前按当时实现于新载体重钉）。
> 组：G1 迁移与护栏、G2 删数据与生成器、G3 删 Legacy 入口与过渡口、G4 校验器与规格档。

## 开工前基线

- `dotnet build siege.sln -c Release`：0 警告 0 错误
- `dotnet test tests/Siege.Core.Tests -c Release`：通过 2385 / 跳过 10 / 失败 0（共 2395）

## 第 1 组：新护栏与组内增删（对账用）

| 变化 | 条数 | 说明 |
|---|---|---|
| 增 `决策序列基线Tests.链接图决策序列与基线相同`（种子 1 / 2） | +2 通过 | 新护栏 L2：`link:1`、固定配对脚本、4 名标准、截断第 12 大回合；种子 3 沿用 `决策序列基线_链接模式Tests`（文件 git diff 为空） |
| 删 `难度分级Tests.三档旧难度逐步不变`（Easy / Standard / Hard 三行） | −3 通过 | L8 / tasks 1.5：v5 整局的三档黄金哈希；三档今天的走法由三条链接图基线（标准）与手写局面算例守 |
| 删 `默认评价权重的校准Tests.校准后截断率达标` | −1 通过 | L8：v5 种子 1–20 截断率；校准口径字符串守门照旧 |
| 删 `默认评价权重的校准Tests.校准后截断率达标_种子1至200`（Slow） | −1 跳过 | 同上（200 局完整版） |
| 合并 `活形分析的决策内缓存Tests` 两条 Slow（标准图 / 边疆图 种子 1–20）→ `缓存开关不改变决策序列_链接图种子1至20` | −1 跳过 | 只剩一族地图；默认套件的缩小版照旧 |
| **第 1 组预期** | 通过 2385 − 4 + 2 = **2383**；跳过 10 − 2 = **8** | |

### 改名（通过数不变，名字变）

| 原名 | 新名 | 原因 |
|---|---|---|
| `专家难度的一层前瞻Tests.一层配置与改动前的专家逐步相同` | `一层配置的专家黄金值` | 规格 Scenario 改名（ai-decision 增量） |
| `专家难度的一层前瞻Tests.缺省预设的专家与改动前的专家逐步相同` | `缺省预设的专家与一层配置逐步相同` | 同上 |
| `候选格上限Tests.缺省不限制时标准图整局与改动前逐步相同` | `小图不限制` | 规格 Scenario「小图不限制」（9×9 手写夹具） |
| `停手阈值Tests.地图覆盖不改阈值` | `阈值不随地图变化` | 规格 Scenario「阈值不随地图变化」（link:1 / link:2） |
| `公开规则纯函数Tests.v5真实对局每个小回合新旧两条路径逐项相等` | `真实对局每个小回合新旧两条路径逐项相等` | 载体改 9×9 手写夹具 |
| `每局换图Tests.第i局的地图标识是起始种子加i_平台数不变` | `第i局的地图标识是起始种子加i` | 平台数是 gen: 参数，随生成器删除 |

**第 1 组实测**：build 0 警告 0 错误；test 通过 **2389** / 跳过 **8** / 失败 0。与预期 2383 的差 +6 = 「链接档地图标识与确定性」等 G1 新增规格用例（地图静态校验规则 +5、棋盘清单「空清单不写出」+1），见逐类清单。三条链接图基线首次钉入；`link:1`–`link:20` 快照存 `snapshots/g1/`（80 个文件）。

第 1 组其余记录：
- 1.2 画面对照（窗口模式，`--auto-demo --rounds=0 --seed=5 --overview --screenshot=<路径>:<帧>`）：`shots/g1-build.png`（第 20 帧，第 2 大回合构筑期）绘制调用 381、对象 770、图元 8908；`shots/g1-final-link.png`（第 139 帧，第 13 大回合决赛链接后）绘制调用 1090、对象 1663、图元 27587。
- 1.3 新基线：`decisions-link1-seed1.txt`（第 10 大回合决赛结算收尾）、`decisions-link1-seed2.txt`（第 11 大回合），样本下界：配对行、结算行、≥ 30 个小回合、终局或满 12 大回合；种子 3 文件未动。
- 1.4 G1 黄金值（`link:1`、1 专家 + 3 标准、固定配对、截断第 12 大回合）：种子 1 / 2 / 3 → 专家决策 18 / 22 / 24 次、子流消费 3069 / 7502 / 6510、前瞻记录 9 / 11 / 12 条（哈希见 `专家难度的一层前瞻Tests.G1Golden`）。
- L12（1.6，只跑生成器、不跑对局）：出生区信物区域强度预算"未收敛"比例——缺省内容集 V2：`link:1`–`link:100` × 对局种子 1 为 0 / 100；`link:i` × 对局种子 i 为 15 / 100；`link:1` × 对局种子 1–100 为 13 / 100（0–499 为 96 / 500）。内容集 V1 同口径依次为 16 / 100、24 / 100、29 / 100（109 / 500）。测试 `区域强度预算Tests` 的均衡断言在 `link:1`（n = 5）上按"偏差 ≤ 8% 或标记未收敛"判定。
- 区域强度预算的"同区同类型"与"高风险区"交叉检查：`link:1` 上惩罚效应只有 272‰ / 339‰，低于原口径要求的 1/3 显著差，改用同构的 9×9 手写高阶图（`HighTierMap`），断言不变（偏离，记录在此）。

1.4 Perf 单跑（`SIEGE_PERF=1`，G2 结束时的构建，`link:1` 局面集）：高难中位数 27.8 ms / p90 55.7 ms；专家中位数 43.9 ms / p90 116.4 ms；**比值 1.576（上限 4）**，通过。

## 第 2 组：删数据与生成器（对账）

实测：build 0 警告 0 错误（含 `src/godot/Siege.Godot.csproj`）；test 通过 **1967** / 跳过 **6** / 失败 0（2389 → 1967，−422；跳过 8 → 6）。

| 类 | 通过数变化 | 处理 / 原因 |
|---|---|---|
| MapDefinition：四人 9、两人 7、三人 8、边疆档基准 18、基准地图对称性 12、棋盘档预算与校验 24、旧地图降级为测试夹具 2、人数适配预算 26、边疆档静态校验 5 | −111 | 删：只测旧地图 / 旧档本身（规格 REMOVED） |
| MapGeneration 全部 11 类（地图随机源 8、布局速览 1、新地表投放 17、既有生成器降级 7、棋盘档布局规则 41 + Perf 1 跳过、棋盘档生成参数 22、棋盘档生成图标识 23、生成参数与标识 47、生成图布局规则 36、生成校验闭环 8、生成确定性 16） | −226（跳过 −1） | 删：只测旧生成器 |
| AiDecision.AI小回合耗时（Perf） | 跳过 −1 | 删（L8） |
| AiDecision.决策序列基线 v5 / board:1 两行 | −2 | 删；两份旧基线文件与旧导出器 `DecisionSequence` 一并删除；链接图三条基线保留 |
| AiDecision.地图专属评价权重覆盖 | −8 | 删（L9；原定 G3，提前，见 L16） |
| SimulationHarness：各入口支持生成图 9、冒险概率记录 2、各入口的地图专属AI权重 11、地图子命令 3、边疆图终端试玩脚本 1 | −26 | 删（后三类原定 G3，提前，见 L16） |
| SimulationHarness.各入口按地图标识选图 | −9 | 删旧标识族用例：缺省地图不变、未知标识报错、改名前的旧地图标识 ×2、其余标识按文件路径、选边疆图、两人图、三人图、参赛人数缺省（`RunLegacy` 夹具一并删）；留 4 条，「三个入口都经同一份目录」禁用记号改 `LinkMapGenerator.Generate(` |
| SimulationHarness.各入口支持生成图_链接模式 | −1 +7 | 删「map子命令仍可打印旧标识」；增「map子命令拒绝旧标识」×5、「map子命令缺省地图」、守门「生成器只经目录调用_裸link只在入口最外层取种子」 |
| MatchSetup.原型插旗替代路径 | −24 | 删种子选区 / 顺排黄金值 / 中立平台 / 冒险概率用例（载体随本组删除；原定 G3，见 L16）；留 3 条 |
| MapDefinition：地图规格档 −6（缺省为标准档 ×4、标准档写出…逐字节不变、棋盘档读写往返）、棋盘清单 −8（旧地图没有棋盘清单 ×7、标准档与边疆档带清单被拒绝）、地图静态校验规则 −3（地标不可达、出生区距离失衡、咽喉必须显式标注——v5 载体）、地图文件往返 −1（v3 历史文件）、地图文件健壮性 −1（磁盘基准文件一致）、地图静态数据 −1（拒绝裁切适配） | −20 | 删：依赖 `maps/` 或旧地图本身 |
| LinkMapGeneration.链接档地图标识与确定性「夹具仍可加载_夹具生成不变」 | −1 | 删（`MapCatalog.Legacy` 删除） |
| MatchSetup.对局配置公开完整地图标识「内置图上的存档_地图标识仍是内置图的标识」 | −1 | 删 |
| RelicGeneration.公共争夺区信物权重与高阶升级「基准图上公共信物升级率落在宽口径」 | −1 | 删（链接图无公共区信物；合成图上「高阶比例」「高档升级率严格高于标准档」照守） |
| MapSelection.选图视图模型「短种子折叠迁入后输出逐位相同」 | +1 | 2.1：先对 7 个输入与旧 `GeneratedMapId.FriendlySeed` 逐位比对通过，再删旧实现并钉值 |
| **合计** | −430 + 8 = **−422**（通过）；跳过 −2 | 2389 − 422 = 1967 ✔（逐类由 trx 对比对上；跳过 −2 = AI小回合耗时 + 棋盘档布局规则 Perf）；全部减少项都在清单的"删"里（其中 L16 所列为 G3 → G2 提前） |

改而不减（条数不变）：对局模式「链接模式不能配旧地图」载体改 9×9 手写夹具；带入带出配置「缺省关闭」改 `link:1` 链接模式；对局日志的记录内容_链接模式「旧模式日志不写链接模式字段」改 9×9 手写夹具经 `MatchSession.ForMatch`；各入口按地图标识选图_链接模式「链接图建局为链接模式…」去掉 Legacy 一半、「缺省地图」并入 `DefaultId` / `new RunConfig().MapId == "link:1"`；物化状态校验「非链接档不可增长」改 9×9 夹具标三种旧档；选图界面守门三条（反面命中改字面样本 / 反射断言目录只剩 `DefaultId` 与 `Resolve`；折叠函数改 `LinkMapId.FriendlySeed`）；四邻接豁免 ④ 收回并加"`Siege.Core.Board.Maps` 内无直接几何四邻调用者"；新地表判断唯一去掉 `FrontierSurfaces.cs`；地形写入口白名单去掉三个旧地图定义。

护栏：三条链接图决策基线全过，种子 3 文件 git 状态干净；`snapshots/g2/` 与 `g1/` `diff -rq` 逐字节相同（80 个文件）。

变异（G2，均实跑后还原）：
- MUT-A `BatchRunner` 加 `LinkMapGenerator.Generate(1)` + MUT-E 图形版 `MatchSession` 同 → 「生成器只经目录调用_裸link」「三个入口都经同一份目录」「选图界面不自行生成地图」红
- MUT-B `PlayCommand` 加 `LinkMapId.IsBareRequest("link")`（单独一轮）→ 「生成器只经目录调用_裸link」红（识别点集合多出 PlayCommand.cs）
- MUT-C `MapCatalog` 加公开成员 `BuiltinIds` → 「选图界面不自带地图清单」红
- MUT-D `GameRoot.MapSelect` 加一处未折叠的 `Stopwatch.GetTimestamp()` → 「视图模型不读时钟…」红
- MUT-F/G/H `LinkMapGenerator` 直接调 `Adjacency.Neighbors` / 写 `Surface.Marsh` / `new TerrainData(...)` → 四邻接、新地表判断唯一、地形写入口三条守门各红

L10（本组）：`MapRandom` 删除只为旧生成器存在的 `ForAttempt`（`map-gen`）、`ForSurfaces`（`map-gen-surfaces`）、`ForBoardAttempt`（`map-board`）与四个域常量；`ForLinkLayout` 一字未改（被 `LinkMapGenerator` 使用；快照逐字节相同）。`MapGenerationException` 自已删除的 `FrontierMapGenerator.cs` 抽出为独立文件（被 `LinkMapGenerator` 复用）。

2.3 Grep（`siege-4p-base|siege-2p-base|siege-3p-base|siege-frontier|FrontierMap|BoardMap|GeneratedMapId|BoardMapId`，`src/`）剩余：`AiDifficulty.cs` 87 / 97 / 107（校准口径历史说明与口径字符串）、`EvaluationWeights.cs` 72 / 101 / 110 / 154–156（校准口径注释与字符串）及 122 / 144（地图覆盖表代码，第 3 组 3.3 删）、`LinkMapId.cs` 61（FriendlySeed 迁入说明）、`MapGenerationException.cs` 5（抽出说明）、`BoardCamera.cs` 196（注释举例）。

## 第 3 组：删 Legacy 入口与过渡口（对账）

实测：build 0 警告 0 错误（含 Godot 工程）；test 通过 **1965** / 跳过 **6** / 失败 0（1967 → 1965）。

| 类 | 通过数变化 | 处理 / 原因 |
|---|---|---|
| MatchSetup.盲选插旗「链接模式不用冒险概率」 | −1 | 删（3.1：MatchOptions.FlagRisk 删除，无从设置） |
| AiDecision.默认评价权重的校准「两人图覆盖表的校准记录随值一起更新」 | −1 | 删（3.3 / L9：覆盖表与口径常量删除） |
| MatchSetup.对局模式「链接图不能以旧模式建局」→「公开建局恒为链接模式」 | 0（改名） | 3.5：公开 Create 恒为链接模式；同时断言非链接标识经公开 Create 报"链接模式只能配链接图" |
| **合计** | **−2** | 1967 − 2 = 1965 ✔ |

改而不减：各入口按地图标识选图_链接模式「冒险概率选项已删除」（对象内设 FlagRisk → 配置文件写 FlagRisk 经 FromJson / `run --config` 报"FlagRisk 已删除…冒险概率已移除"、不建输出目录）；默认评价权重的校准 / 批量跑局各一行 `Assert.Null(config.FlagRisk)` → 断言 `ToJson()` 不含该键；专家前瞻的记录「本change之前的专家日志照常解析」写回比对去掉旧首部里的 `"FlagRisk":15,`（字段删除后读入忽略、写回不再出现）；原型插旗替代路径守门禁用记号 / 反面命中换成 `AssignBlind(` / `GameSeed.FlagPick`。

源码：
- 3.1 删 `MatchOptions.FlagRisk / DefaultFlagRisk`、`PrototypeZoneAssignment.Assign / RequireValidFlagRisk`（及 RiskScale）、`GameSeed.ZonePick / FlagRisk`；`MatchFlow.Build` 去掉冒险概率校验；`PlantPrototype` 恒走 `AssignBlind`（盲选子流 flag-pick 未动）；PlayCommand 的 `flagRisk` 测试接缝删除。
- 3.2 删 `RunConfig.FlagRisk` 及其校验 / 落成；`RetiredKeys` 增 `FlagRisk`（配置文件写了即 FormatException，`run` 退出 1 且不建输出目录）。
- 3.3 删 `EvaluationWeights.MapOverrides / ForMapId(×2) / MapOverrideCalibrationOf / TwoPlayerOverrideCalibrationStatus`；`RunConfig.ResolvedFor` 去掉覆盖表参数与落成；PlayCommand `weights ?? Default`（`mapOverrides` 接缝删除）；图形版 `MatchSession` 取 `EvaluationWeights.Default`。
- 3.4 删 `BatchRunner.Execute / ExecuteToDirectory`、`MatchSession.Create`、`Replayer.Replay / ReplayFile` 的 `resolveMap` 参数与 `MatchFlow.RestoreLegacy`（`map` 子命令、RunConfig 缺省、裸 gen / board 已在 G2 完成，见 L16）。
- 3.5 `MatchOptions.Mode` 的 `init` 降为 internal（Core 友元：测试、Sim）；公开 `MatchFlow.Create` 恒 `Mode = Link`；`RequireModeMatchesMap` 删第三条（链接图须链接模式）；公开 `Restore` 另加"链接图上缺模式字段的存档拒绝"（原由第三条兜住，`链接图上缺模式字段的存档被拒` 照旧通过）。`MatchFixtures.cs` git diff 为空。
- 3.6 建局分叉删除：Sim `MatchSession.Create`（恒挂配对来源）、`PlayCommand`（终端只剩链接一侧：观察者恒按座位投影、地图 / 规则提示、盲选占位、锁定提示、配对 / 链接 / 结算播报）；图形版 `MatchSession.Create`（恒挂配对来源）、开局锁定提示、`GameRoot` 拾取自检的模式判定、`Hud.SameBoardText`。保留项见 L17。
- 3.7（L10）删图形版通道地砖色 `Visuals.CorridorPath` 与 `BoardView` 的通道分支（链接图 link:1–20 初始与链接后各一张实测通道格 0、公共棋盘 0）；`Visuals.PublicBoardFrame` / Presentation `BoardPlatePalette.PublicFrame` 保留——被链接图连体棋盘外轮廓复用（`BoardView` 合并边框），并由「占位轮廓灰度」守。

codegraph_callers（删除前核对公开成员）：`PrototypeZoneAssignment.Assign` → 仅 `MatchFlow.PlantPrototype`；`RequireValidFlagRisk` → `Assign`、`MatchFlow.Build`；`MatchOptions.FlagRisk` → 仅 `PlantPrototype`；`DefaultFlagRisk` / `GameSeed.ZonePick` 无调用者记录（常量引用 codegraph 不建边，另以 Grep 覆盖 src / tests / godot：Sim RunConfig、MatchSession、PlayCommand 三处与测试 4 处，均已处理）；`BlindPicks` 用 `GameSeed.FlagPick`（与被删子流无关，链接图取值不变）。`MapOverrides / ForMapId / RestoreLegacy / resolveMap` 同样以 Grep 覆盖（codegraph 对属性 / 可选参数不建边）。G2 的删除以 Grep（src + tests + src/godot）核对。

护栏：三条链接图基线全过（种子 3 文件干净）；`snapshots/g3/` 与 `g1/` 逐字节相同；`link:1` 种子 1 / 2 截断 24 小回合的完整日志与 config.json（`snapshots/g2-full` vs `g3-full`）除计时字段（`*Ms`、`MajorRoundMs`）外逐字节相同（3.3 / 3.6 验收）。

2.3 Grep 终态（G3 删除覆盖表后在完成的源码树上重跑同一正则，`src/`）：只剩注释与校准口径字符串——`AiDifficulty.cs` 87 / 97 / 107、`EvaluationWeights.cs` 72（缺省表校准口径注释）与 109–111（`CalibrationOf` 三维的口径字符串）、`LinkMapId.cs` 61、`MapGenerationException.cs` 5、`BoardCamera.cs` 196；无代码引用。

变异（G3，均实跑后还原）：MUT-3A 公开 Create 不强制链接模式 → 「公开建局恒为链接模式」红；MUT-3B 公开 Restore 的模式把关失效 → 「链接图上缺模式字段的存档被拒」红；MUT-3C `RetiredKeys` 去掉 FlagRisk → 「冒险概率选项已删除」红；MUT-3D 图形版 MatchSession 出现 `PlantSequentially(` → 「三个入口的选区都走内核的唯一实现」红。

## 第 4 组：简化校验器与规格档（对账）

实测：build 0 警告 0 错误；test 通过 **1971** / 跳过 **6** / 失败 0（1965 → 1971）。

| 类 | 通过数变化 | 处理 / 原因 |
|---|---|---|
| MapDefinition.地图静态校验规则：距离沿气边计算、出生区被孤立、距离极差恰好等于容差时通过 | −3 | 删：所守规则（距离均衡、出生区须到达中央入口）随旧三档删除（4.2）；夹具 `Isolation` / `Column` 一并删 |
| MapDefinition.地图规格档：新增「已移除的规格档被拒绝」×8（缺字段 / Standard / Frontier / Board / board / 0 / 1 / 2）、「缺省规格档为链接档且恒写出」 | +9 | 4.1 / L7 新 Scenario |
| **合计** | **+6** | 1965 − 3 + 9 = 1971 ✔ |

改而不减：规格档分流守门（口径改为字面量只剩 `MapProfile.Link` 一处、声明表 < 2000 字符、六个字段各读一次、已删字段 / 枚举 / 规则函数名不得出现）；四邻接豁免 ⑤（ValidateCorridors）收回并断言校验器整个类型无直接几何四邻调用；物化状态校验「非链接档不可增长」改用已移除旧档的原数值 0 / 1 / 2；地图文件健壮性 10 条手写残缺 JSON 补 `"Profile":"Link"`（仍落在原错误上），「同一坐标重复写会被合并」载体改 link:1 导出文本（10 → 9 个信物格）。原计划 G4 删的「超宽地图被拒绝而不是抛异常」已随 边疆档静态校验Tests 整类在 G2 删除（计在 G2 的 −5 里）。

源码：
- 4.1 `MapProfile` 只留 `Link = 3`（数值不变）；`MapData.Profile` 缺省 `Link`；`MapFile` 恒写 Profile，读入改为原文字符串 + 判定：缺字段、`Standard / Frontier / Board`（不分大小写）或 0 / 1 / 2 → FormatException"X 已移除…只接受 Link"，其余未定义值同样 FormatException。
- 4.2 `MapValidator` 声明表只剩链接一行（字段：SupportedPlayers、Budgets、ZonesMustExceedPlayers、ColumnRange、RowRange、PlateList）；删 `DistanceHandling`、`PlateConnectivity`、`RequireLandmarks`（CHOKE_NOT_ANNOTATED）、`BirthZonesMustReachEntrance`（`ValidateBirthZoneConnectivity`，BIRTH_ZONE_ISOLATED）、`ValidateDistanceBalance`（DISTANCE_IMBALANCE / LANDMARK_UNREACHABLE / BIRTH_ZONE_DISTANCE_REPORT）、`ValidateCorridors`、`ValidatePlateConnectivity`、通道宽 / 长、"清单必须为空"（BOARDS_NOT_ALLOWED）；保留结构、地形边界、规模预算、出生区、信物格、地标位于可落子格、容差、必死口袋与拼接后校验。
- L10 保留：`MapValidator.DistanceTable` / `MultiSourceDistances`（`Siege.Sim map` 打印距离表仍用）、`MapFindingSeverity.Report` 与 `MapValidationResult.Reports`（公开 API，物化状态校验断言其为空）、`BoardPlateKind.Public`（文件格式与棋盘描述仍认）、`MapData` 的咽喉 / 中央入口 / 容差字段（链接图文件里写着，删了会改链接图导出字节）。

护栏：三条链接图基线全过；`snapshots/g4/` 与 `g3/`（即 `g1/`）逐字节相同（link:1–20 初始图、`--links 1-2,3-4` 后的图、首部地图摘要与 config）；`g4-full` 与 `g3-full` 除计时字段外逐字节相同；`链接时的棋盘拼接Tests.全部链接序列都能拼接` 通过。

变异（G4，均实跑后还原）：MUT-4A `ValidatePockets` 里读 `map.Profile == MapProfile.Link` → 「校验器对规格档的分支只在声明表里」红；MUT-4B `Validate` 里第二次读 `rules.ZonesMustExceedPlayers` → 同一守门红；MUT-4C 去掉已移除规格档的判定 → 「已移除的规格档被拒绝」8 行全红；MUT-4D 校验器直接调 `Adjacency.Neighbors` → 「几何邻居枚举只在允许名单内直接调用」红。

## 全程对账（开工前 → 第 4 组结束）

通过 2385 → **1971**（净 −414）；跳过 10 → **6**（−4：AI小回合耗时 Perf、默认评价权重截断率 Slow、活形缓存 Slow 合并 1、棋盘档布局规则 Perf）。
净变化 = 新增护栏 / Scenario − 清单"删"：

- 新增 **+26**：决策序列基线（链接图种子 1 / 2）+2；地图静态校验规则 +5；棋盘清单「空清单不写出」+1；规格档分流守门（自 边疆档静态校验 迁入）+1；选图视图模型「短种子折叠迁入后输出逐位相同」+1；各入口支持生成图_链接模式 +7；地图规格档 +9。
- 删 **−440**（按类，逐条见 G1–G4 各表）：MapDefinition 旧图 / 旧档 9 类 −112（含 边疆档静态校验 6 条：1 条迁出计入上面的 +1、5 条删）；MapGeneration 11 类 −226；AiDecision −3（难度分级）−2（默认权重校准 G1 −1、G3 −1）−8（地图覆盖）−2（决策序列基线旧两行）；SimulationHarness −26（四类整删）−9（按地图标识选图）−1（孪生 map 子命令旧标识）；MatchSetup −24（原型插旗）−1（对局配置内置图存档）−1（盲选冒险概率）；MapDefinition 部分删 −20（G2）−3（G4）；LinkMapGeneration −1；RelicGeneration −1。
- 核对：2385 + 26 − 440 = 1971 ✔（逐类数字与上表 `trx` 对比一致：`g0.trx` → `g4b.trx` 46 个类有变化，均能落到上面的条目）。

## 第 5 组：文档与最终回归

- 5.1 `HANDOFF.md`：测试现状（1977 / 1971 / 6、三条链接基线）、「旧地图测试夹具」→「旧地图（已删除）」、`map` 选项与 `FlagRisk` 字段说明、后续 change（retire-legacy-maps 标已实施，登记 `retire-legacy-rules`）、设计文档版本号 v1.21。
- 5.2 设计文档 v1.20 → v1.21：§3.2 / §3.3（旧地图段改"已删除"）/ §4.1（冒险概率字段已删）/ §15.2（G1 守门基线改 link:1、校准口径注明 v5 已退役且链接图上未复核、地图专属权重注明已删除）/ §19（retire-legacy-maps 已实施、登记 retire-legacy-rules）/ 变更记录加 v1.21 一行；其余历史记录未改（L5）。
- 5.3 最终回归（顺序单跑，真实退出码）：`dotnet build siege.sln -c Release` 0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj` 0 / 0；`dotnet test` 通过 1971 / 跳过 6 / 失败 0；Godot `--build-solutions --quit` 退出 0；headless 自检：`-- --auto-demo` 退出 0（[flow] 6 条通过：锁定 → 构筑 → 首轮配对 → 链接，开局对准自证通过）、`-- --auto-demo --seed=5` 的 [flow] / [camera] 行与第 1 组逐行相同、`-- --auto-demo --pick-check --seed=5` 退出 0（开局一轮 30 / 30，链接后第二轮 55 / 55）、`-- --map-select --auto-demo` 退出 0（五步全过再接演示）、`-- --auto-demo --rounds=0 --seed=5` 退出 0（13 条，七步齐全；[flow] / [camera] 行与第 1 组逐行相同）；各次日志无 ERROR。
- 画面对照（3.7 / 1.2 验收）：`shots/final-build.png` 与 `g1-build.png` 逐像素相同、绘制调用 381 / 对象 770 / 图元 8908 相同；`shots/final-final-link.png` 与 `g1-final-link.png` 仅 4 个像素不同（右侧棋子边缘的抗锯齿，人工比对裁切图无可见差异），绘制调用 1090 / 对象 1663 / 图元 27587 相同。
- 新增 `.uid`：本 change 没有新增 Godot 脚本，无需生成。

## 待决（编号 L15 起）

- **L15 终端链接局在链接后的小回合播报越界崩溃（既有缺陷，本 change 未修）**：`src/Siege.Sim/Play/PlayCommand.cs` `Summarize`（约第 340–345 行）用 `after.Board.AllCoords()` 逐格去索引 `before.Board`；链接后画布变大（如 43×11 → 更宽、坐标 AT1），`before.Board[c]` 抛 `ArgumentOutOfRangeException: 坐标超出棋盘范围`。复现：`PlayCommand.Run(1, 4, 1, Standard, "1\n" + 8×"\npass\n", map: link:1, pairing: ScriptedPairingSource)`。探针实测：人类每回合 Pass 8 次的脚本在种子 1–40 × 棋盘 1–5 中至少 135 / 200 崩溃；弃赛脚本（第 6 大回合弃赛）种子 40–70 × 棋盘 1–5 中 140 / 155 崩溃。终端 `play` 缺省地图就是 `link:1`，玩家入口受影响；图形版不走这段代码。修法需按 `LinkLayout` 把 `before` 坐标映射到 `after` 版本，属于链接演出行为，建议另开 change。本 change 的终端测试一律停在第 3 大回合（链接前）或挑无崩溃种子，并在注释里引用本条。

- **L16 部分 G3 删除项提前到 G2（偏离 tasks 分组，不改范围）**：地图专属评价权重覆盖、各入口的地图专属AI权重、冒险概率记录、地图子命令四个测试类与 原型插旗替代路径 的 24 条用例原定 G3 删，但其载体（2 / 3 人图、v4 / v5、边疆图、`gen:`）在 G2 删除后无法编译，只能随 G2 删除；所守代码（地图覆盖表、FlagRisk / ZonePick / Assign）仍按计划在 G3 删。同理 3.4 的部分内容（`RunConfig.MapId` 缺省改 `link:1`、Program 裸 `gen` / `board` 取种子分支、`map` 子命令只接受 `link:` 与两条新测试）随 G2 完成，因为它们直接引用被删的生成器 / 内置图。

- **L17 Sim 中读 `Match.Mode` / `view.Mode` 的日志字段与终端渲染分支保留（3.6 字面"Legacy 侧日志字段分支"未删）**：L6 = C 下手写 9×9 夹具仍以旧模式建局，并经 `MatchSession.ForMatch` 进 Sim 跑出日志（两位数行号贯通、棋盘清单、活形记录与统计、对局日志的记录内容 4 处、对局日志的记录内容_链接模式「旧模式日志不写链接模式字段」），`BoardRenderer` 也被手写夹具测试直接调用（两位数行号贯通、出生区编号显示、合法落子范围的对外契约）。这些分支只读 Core 对象上的模式属性、不再"按地图分叉建局"；若删除，旧模式日志会写出 `Mode: Link` 等链接字段、整图视口会对非链接图求战场。按最小解读保留，建局分叉（Sim / 终端 / 图形版）已全部删除。待负责人裁决：是否在 `retire-legacy-rules` 一并处理。

- **遗留（规范真相源）**：`.trellis/spec/core/` 仍有旧地图的陈述——`boundaries.md` 26（规格档分流写"标准 / 边疆"）、48（`BoardMapGenerator` / `BoardMapId` 一行）、69（`gen:` 标识说明），`determinism.md` 19（地图随机源段提到 map-generator），`testing.md` 83（`gen:12345` 摘要黄金值）。本 change 第 5 组只改 HANDOFF 与设计文档，未动 spec；建议主会话归档时一并处理。

（逐类清单见下节。）

## 逐类清单（tasks 1.1 定稿，全部勾销）

列：测试类 ｜ 依赖的旧对象 ｜ 所测规则 ｜ 处理 ｜ 组 ｜ 迁移后 / 备注。处理取值见文首；"部分删"的删除条数见各组对账表。

| 测试类 | 依赖 | 规则 | 处理 | 组 | 迁移后 / 备注 |
|---|---|---|---|---|---|
| AiDecision/AI小回合耗时Tests | v5、board:1 | 大图 / v5 单个小回合耗时（Perf，只出数字） | 删 | G2 | tasks 1.1 初判；Perf 默认跳过（跳过数 −1）；L8「大图耗时下降」删除 |
| AiDecision/专家前瞻的确定性与耗时Tests | v5（LookaheadFixtures） | 专家前瞻确定性 / 缓存中性 / 不消费随机 / 预演次数代理 ≤ 4 | 迁link | G1 | 局面集改 link:1 种子 1–3、固定配对脚本、截断第 12 大回合（1.4）；断言不变 |
| AiDecision/专家前瞻耗时计时Tests | v5（Perf 行） | 专家 / 高难耗时中位数比 ≤ 4（Perf） | 迁link | G1 | Perf 行改 link:1；同一局面集 |
| AiDecision/专家难度的一层前瞻Tests | v5、Legacy.Resolve、resolveMap | G1 一层配置黄金值、缺省预设逐步相同、W=1 与高难逐步相同、旧专家日志回放 | 迁link+改钉 | G1 | G1Golden 在 link:1 种子 1–3 重钉（1.4）；测试名随规格改为「一层配置的专家黄金值」「缺省预设的专家与一层配置逐步相同」；缺省预设一条显式跑局 K=0 |
| AiDecision/前瞻的近似两层加分Tests | LookaheadFixtures（v5） | 两层加分改变选择（真实局面） | 迁link | G1 | 经 ProbePositions 自动改到 link:1；断言不变 |
| AiDecision/难度分级Tests | v5 | 三档旧难度逐步不变（3 行） | 部分删 | G1 | 「三档旧难度逐步不变」删（L8）；其余 6 条不动 |
| AiDecision/候选格上限Tests | v5、边疆图、FrontierFixtures.RepoRoot | 候选格上限各条 | 迁link/手写+改钉 | G1 | 「缺省不限制时标准图整局与改动前逐步相同」→「小图不限制」（9×9 手写夹具、新钉 SmallMapGoldenHash）；新增 LinkGoldenTurnHash（link:1 种子 31 / 24 小回合）供下游；大图 = link:1，小图 = 9×9；批次记录小图一段改在 ResolvedFor 上断言 |
| AiDecision/停手阈值Tests | v5、2 人图、地图覆盖 | 阈值 0 = 严格提高（改钉）、阈值进入记录、首部缺阈值回放、阈值不随地图变化 | 迁link+改钉 | G1 | StrictImprovementTurnHash 在 link:1 重钉；「地图覆盖不改阈值」→「阈值不随地图变化」（link:1 / link:2，规格新 Scenario） |
| AiDecision/公开规则纯函数Tests | v5 | 四个公开规则纯函数与旧实现逐项相等（真实对局） | 迁手写 | G1 | 「v5真实对局…」→「真实对局每个小回合新旧两条路径逐项相等」，9×9 手写夹具 + 三枚信物（旧路径只在 Legacy 规则下成立） |
| AiDecision/决策序列基线Tests | v5、board:1 | 决策序列与基线相同（旧两行） | 部分删+新增 | G1/G2 | G1 新增「链接图决策序列与基线相同」种子 1 / 2（+2）；G2 删旧两行（v5 / board:1）及基线文件 |
| AiDecision/启发式评价维度Tests | resolveMap | 首部 AI 评价版本 / 旧版本回放 | 迁link | G1 | 去 resolveMap，共用配置即 link:1 |
| AiDecision/地图专属评价权重覆盖Tests | 地图覆盖、2 / 3 人图、v5、边疆图、gen: | 地图专属权重覆盖 | 删 | G2 | L9：整套删除（规格 REMOVED）；原定 G3，载体（2 人图 / 边疆图 / gen:）G2 删除后无法编译，提前到 G2（−8） |
| AiDecision/活形分析的决策内缓存Tests | v5、边疆图（Slow） | 缓存开关不改变决策序列 | 迁link | G1 | 缩小版随共用配置到 link:1；两条 Slow（标准图 / 边疆图）合并为一条 link:1（跳过数 −1） |
| AiDecision/默认评价权重的校准Tests | v5、2 人图、地图覆盖 | 截断率达标（默认 / Slow）、引用未校准维度、两人图覆盖表校准记录 | 部分删 | G1/G3 | 截断率两条删（L8，G1）；「两人图覆盖表的校准记录随值一起更新」G3 删（L9）；口径字符串守门照旧（v5 字样改字面量）；引用未校准维度期望里的落成项改为 K=24、无冒险概率；G3：同上一行改写；「两人图覆盖表的校准记录随值一起更新」删。 |
| Battlefield/战场的定义与规则作用域Tests | Legacy.Resolve（v5） | 非链接档全图一个战场、孤立格为 Id −1 | 迁手写 | G1 | 9×9 手写夹具（MatchFixtures.Map） |
| BoardTopology/四邻接Tests | FrontierSurfaces（豁免名单） | 邻接唯一实现守门 | 改 | G2 | 随 FrontierSurfaces 删除去掉豁免 ④ 及其反面命中；G4 随通道规则删除去掉豁免 ⑤；G4 已去掉豁免 ⑤ 并断言校验器无直接几何四邻调用。 |
| BoardTopology/棋盘格子状态模型Tests | v5 | 信物格可落子、越界坐标是障碍 | 迁手写 | G1 | 13×13 手写夹具 |
| BoardTopology/盘面副本与批量写入Tests | v5 | 副本不重跑校验 | 迁link | G1 | 诱饵改为 link:1 拿掉一枚出生棋盘信物（BIRTH_BOARD_RELIC_COUNT） |
| BoardTopology/盘面序列化Tests | v5 | 非盘面信息不影响序列化 | 迁手写 | G1 | 13×13 带信物格的手写夹具 |
| BoardTopology/盘面派生量提速对照Tests | v5、board:1（样本地图） | 派生量提速前后逐项相同 | 迁link | G1 | 两张旧图样本换成 link:2 初始图与 link:7 链接一次之后 |
| CarryInOut/AI带入Tests | Legacy.Resolve（v5） | AI 带入（同等数量 / 本机不带则 AI 不带 / 可复现 / 无关 / 不扰动征募） | 迁link | G1 | link:1、链接模式建局 |
| CarryInOut/本地玩家档案Tests | v5（终端） | 路径可配置 | 迁link | G1 | link:1 + 固定配对脚本 |
| CarryInOut/终端的带入选择弃赛与结算显示Tests | v5（终端） | 终端带入选择 / 弃赛 / 结算 | 迁link | G1 | link:1 + 固定配对脚本；插旗提示文本随模式改；弃赛命令换种子 43 / 3 号棋盘（L15） |
| Determinism/随机子流隔离Tests | v5 | 改变征募决策后信物生成逐格不变 | 迁link | G1 | link:1 |
| FrontierFixtures / BoardMapFixtures / BoardGenFixtures / MapGenFixtures | 旧档夹具 | — | 删 | G2 | RepoRoot → TestPaths.RepoRoot；Rect → TestMaps.Rect（原样迁出） |
| LookaheadFixtures | v5 | 专家前瞻夹具 | 迁link | G1 | V5Config → LinkConfig（link:1、无冒险概率）+ CreateSession（固定配对脚本）+ RunToRound(12) |
| SimFixtures | v5（RunConfig 缺省）、resolveMap、FlagRisk=0 | 共用样本 / 配置 | 迁link | G1 | Config 显式 MapId=link:1；PinPreCalibration 去掉 FlagRisk；Sample / RankedMatch 去 resolveMap |
| ViewportCamera/CameraFixtures | Legacy.Resolve（v5 / 边疆图） | 相机夹具 | 迁手写 | G1 | V4 → 13×13 一屏看全手写夹具（出生区同 v5）；Frontier → 25×30 竖长、六个 h=2 正方形平台（出生区同边疆图）；75 条相机用例期望一字不改（1.7 / L13） |
| LinkMapFixtures | FrontierFixtures.Rect | — | 改 | G1 | Rect 改取 TestMaps.Rect |
| TestMaps / TestPaths | — | — | 新增 | G1 | TestMaps.Rect、TestPaths.RepoRoot（自 FrontierFixtures 原样迁出） |
| LifeShape/活形分析性能基线Tests | v5、边疆图（Perf） | 活形分析耗时（Perf） | 迁link | G1 | Perf 行改 link:1 / link:2 |
| LinkMapGeneration/物化状态校验Tests | BoardMapFixtures、Legacy.Resolve（非链接档不可增长） | 物化状态校验 | 改 | G1/G2/G4 | Rect 迁 TestMaps（G1）；「非链接档不可增长」的旧档载体 G2 换手写 / G4 换未定义规格档值；G4：非链接档改用已移除旧档原数值 0 / 1 / 2。 |
| LinkMapGeneration/链接档地图标识与确定性Tests | Legacy.Resolve、内置图、gen:、board: | 目录解析 link 标识、夹具仍可加载 | 部分删 | G2 | 「夹具仍可加载_夹具生成不变」删；「目录解析link标识」去掉 Legacy 一段 |
| MapDefinition/四人基准地图Tests、两人基准地图Tests、三人基准地图Tests、边疆档基准地图Tests、基准地图对称性Tests、棋盘档预算与校验Tests、旧地图降级为测试夹具Tests、人数适配预算Tests | 旧地图 / 旧档 | 只测旧地图 / 旧档本身（规格 REMOVED） | 删 | G2 | 人数适配预算里与链接档共用的两条（信物格数、 不支持的人数）迁到 地图静态校验规则Tests |
| MapDefinition/边疆档静态校验Tests | 边疆档 | 边疆档距离只报告等；规格档分流守门 | 删+迁 | G1/G2/G4 | 守门迁到新类 规格档分流守门Tests（G1，G4 改口径并做变异）；边疆档 4 条 G2 删；「超宽地图被拒绝而不是抛异常」G4 删（列宽 25 上限随旧档删） |
| MapDefinition/地图静态校验规则Tests | v5、v2 文件 | 地图静态校验规则 | 迁link/手写+新增 | G1/G2/G4 | 共用规则 22 条改到手写链接档地图；新增「合规的手写链接档地图通过」「出生区数越界」「出生棋盘信物数不对」「信物格数越界」「不支持的人数被拒绝」（+5）；改名「信物格不在可落子格上」「出生棋盘内有新地表」；旧三档规则：v5 载体 3 条 G2 删，合成图载体 3 条 G4 删；G4 删合成图载体 3 条（距离沿气边计算、出生区被孤立、距离极差恰好等于容差时通过）。 |
| MapDefinition/地图规格档Tests | maps/*.json、v5、FrontierFixtures、BoardMapFixtures | 规格档读写 / 缺省 / 未定义值 / 不改规则 / 只被白名单引用 | 迁link+部分删 | G1/G2/G4 | 「规格档读写往返」改 link 手写图；「棋盘档按自己的声明行…」改名「未定义的规格档值被拒绝而不是抛异常」；「缺省为标准档」(4 行)、「标准档写出时…逐字节不变」、「棋盘档读写往返」G2 删；G4 补「已移除的规格档被拒绝」；G4 新增「已移除的规格档被拒绝」×8、「缺省规格档为链接档且恒写出」。 |
| MapDefinition/地图文件往返Tests | v5、maps/v3 | 文件往返、据点字段拒绝、未知字段宽容、确定性、围棋记法 | 迁link | G1/G2 | 载体 link:1；「v3历史文件仍可读入并通过校验」G2 删（maps/ 删除） |
| MapDefinition/地图文件健壮性Tests | v5、maps/v2、maps/v5 | 文件健壮性 | 迁link/手写 | G1/G2/G4 | 载体 link:1；缺地形字段改手写最小文本；「磁盘上的基准地图文件与代码一致」G2 删；G4 视规格档必填补字段；G4：残缺 JSON 补 "Profile":"Link"；「同一坐标重复写」载体改 link:1。 |
| MapDefinition/地图静态数据Tests | v5 | 加载不引入随机、基准校验通过、拒绝裁切适配 | 迁link+部分删 | G1/G2 | 前两条改 link:1；「拒绝裁切适配」G2 删（人数适配预算 REMOVED） |
| MapDefinition/棋盘清单Tests | maps/*.json、BoardMapFixtures、v5、FrontierFixtures | 棋盘清单 | 迁link/手写+新增 | G1/G2 | 新增「空清单不写出」（+1）；出生棋盘即出生区 → link:1；清单不参与结算 → 9×9 手写夹具补清单；默认视图透传 / 文件写法 → 手写链接档；「旧地图没有棋盘清单」(7 行)、「标准档与边疆档地图带棋盘清单被拒绝」G2 删 |
| MapDefinition/出生区归属Tests | v5 | 出生区归属与共享 | 迁link/手写 | G1 | link:1；「出生区标记不自带限制」改 9×9 手写夹具 |
| MapDefinition/两位数行号贯通Tests | FrontierFixtures.Rect | 两位数行号 | 改 | G1 | Rect 迁 TestMaps |
| MapDefinition/规格档分流守门Tests（新） | — | 规格档分流守门 | 迁入 | G1 | 自 边疆档静态校验Tests 原样迁入；G4 改口径（只剩 Link 一处、六字段各读一次、已删字段不得出现），变异 2 次红。 |
| MapGeneration/全部 11 类 | gen: / board: 生成器 | 生成器本身（规格 REMOVED） | 删 | G2 | L1；G2 新增说明性 Requirement「地图生成器已退役」的用例 |
| MapSelection/选图界面守门Tests | BuiltinMaps、GeneratedMapId.FriendlySeed、生成器名 | 选图界面守门 | 改 | G2 | 随 BuiltinMaps / 生成器删除与 FriendlySeed 迁入 LinkMapId 改反面命中 |
| MapSelection/选图界面难度选择Tests | FrontierFixtures.RepoRoot | — | 改 | G1 | RepoRoot 迁 TestPaths |
| MapSelection/选图视图模型Tests | v5（旧图文案）、GeneratedMapId.FriendlySeed | 预览说明、短种子折叠 | 迁手写+改 | G1/G2 | 旧图文案改 9×9 手写夹具（G1）；FriendlySeed 改 LinkMapId（G2，2.1）；新增「短种子折叠迁入后输出逐位相同」（+1，G2；先与旧实现逐位比对通过，再钉值） |
| MatchFlowRegression/百局端到端Tests | v5、RestoreLegacy | 百局端到端无死锁无非法状态 | 迁link | G1 | link:1 链接模式 + 固定配对脚本；存档经 Restore 往返 |
| MatchSetup/原型插旗替代路径Tests | v5、gen:、冒险概率、resolveMap | 原型插旗 / 冒险概率 / 顺排 | 部分删 | G2 | 种子选区 / 顺排黄金值 / 中立平台 / 冒险概率 24 条删（载体 v4 / v5 / 边疆图 / gen: 随 G2 删除，原定 G3）；留「调试依次插旗」「出生区锁定结果可持久化」「三个入口的选区都走内核的唯一实现」（G3 改反面命中） |
| MatchSetup/对局内容集Tests | RestoreLegacy、resolveMap、v5 黄金哈希 | 对局内容集 | 迁link | G1 | Restore 改玩家入口；v1 逐步相同改对 LinkGoldenTurnHash；新存档恢复后挂种子派生配对来源 |
| MatchSetup/对局模式Tests | Legacy.Resolve（v5）、Legacy 缺省模式 | 对局模式 | 改 | G2/G3 | 「链接模式不能配旧地图」载体 G2 改 9×9 手写夹具（MatchFixtures.Create + Mode=Link）；G3 随 Mode 降 internal / Create 恒链接改写；G3：「链接图不能以旧模式建局」→「公开建局恒为链接模式」。 |
| MatchSetup/对局配置公开完整地图标识Tests | gen:、内置图、RestoreLegacy | 对局配置公开完整地图标识、存档摘要、对局种子不影响地图、地图种子不扰动对局随机 | 迁link+改钉 | G1/G2 | 全部改 link:；「地图种子不扰动对局随机」黄金值在 link:1 重钉；「内置图上的存档_地图标识仍是内置图的标识」G2 删 |
| MatchSetup/带入带出配置Tests | v5、冒险概率 | 带入带出配置 / 关闭时逐步相同 | 迁link | G1/G3 | Golden 建局改 link:1 链接模式；逐步相同对 LinkGoldenTurnHash；「缺省关闭」里的 Legacy 建局 G2 改 link:1 链接模式（原定 G3） |
| MatchSetup/盲选插旗Tests | 冒险概率 | 链接模式不用冒险概率 | 部分删 | G3 | 随 FlagRisk 删除 |
| MatchSetup/链接模式存档Tests | v5、RestoreLegacy | 旧存档被拒绝 | 迁手写 | G1 | 旧存档改 9×9 手写夹具；内部口改 RestoreUnvalidated |
| MatchTelemetry/专家前瞻的记录Tests | v5（LookaheadFixtures）、Legacy 回放 | 专家前瞻的记录 | 迁link+改钉 | G1 | 样本 link:1 种子 6 / 1（探针挑选）；两条哈希黄金值重钉；冒烟 gz 旧日志的回放一段删（地图已不可解析），解析一段保留；G3：旧日志写回比对去掉首部 "FlagRisk":15,。 |
| MatchTelemetry/对局日志的记录内容Tests | 共用样本 v5、冒险概率、Legacy 建局 | 对局日志的记录内容 | 迁link | G1 | 样本 link:1；揭示时间改截断 11 个小回合（链接前坐标可对照）；CarriedSession 改链接模式 |
| MatchTelemetry/对局日志的记录内容_链接模式Tests | Legacy 日志对照 | 旧模式日志不写链接模式字段 | 迁手写 | G2 | G1 显式写回 v5；G2 改为 9×9 手写夹具经 MatchSession.ForMatch 跑出旧模式日志（L6 = C 下 Sim 仍可承载手写夹具的 Legacy 局，断言不变） |
| MatchTelemetry/链接模式平衡分析Tests | 共用样本（作旧日志）、v5 | 旧日志被拒绝、等待时长 | 迁手写 | G1 | 旧日志改合成日志（首部地图标识 siege-4p-base-v5） |
| MatchTelemetry/各棋子势力占比Tests | 共用样本 | 真实跑局快照自洽（样本口径：匠人与协同同串） | 迁link | G1 | 匠人样本换种子 14–15（探针） |
| MatchTelemetry/地形改造日志与分析Tests | v5（开局地图） | 地形可离线重建等 | 迁link | G1 | 样本 link:1 种子 34 / 36 / 43、截断 12（链接前）；开局地图按首部取 |
| MatchTelemetry/插旗同区统计Tests | 冒险概率 100 / 0 | 真实跑局的同区经首部进入统计 | 迁link | G1 | 改 link:1 种子 1（同区）/ 3（四区） |
| MatchTelemetry/平衡分析方向Tests | 共用样本 v5（报告黄金值） | v1 报告除第 12 项外与引入之前逐字节相同 | 改钉 | G1 | 两份黄金哈希在 link:1 样本上重钉 |
| MatchTelemetry/冲突占用率口径Tests | 共用样本 v5（105 格） | 真实跑局把可落子格写进首部 | 迁link | G1 | 期望 105 → 157 |
| MatchTelemetry/分析排除测试污染Tests、活形记录与统计Tests、数值目标回归Tests | 共用样本 / resolveMap | — | 迁link | G1 | 随共用样本到 link:1，断言未改即通过 |
| Recruitment/初始配置与基础棋池Tests | Legacy.Resolve（v5）、共用样本 | 带入改变开局手牌 | 迁link | G1 | link:1 链接模式 |
| Recruitment/征募随机可复现Tests | v5 | 改变征募决策后信物生成逐格不变 | 迁link | G1 | link:1 |
| RelicControl/信物控制判定Tests | v5 | 控制判定与覆盖交叉一致 | 迁link | G1 | link:1 |
| RelicEffects/六类原型信物的效果Tests | 内置图 ×4 | 新四类不升级（样本） | 迁link | G1 | link:1–link:4 |
| RelicEffects/计分信物连营与犄角Tests | v5 | 真实内容与已揭示内容结果相同（性质） | 迁link | G1 | link:1 |
| RelicGeneration/信物在开局一次性生成Tests、生成结果可完整记录Tests | v5 | — | 迁link | G1 | link:1 |
| RelicGeneration/出生区信物权重Tests | v5、四张内置图（v1 黄金值） | 出生区信物权重 | 迁link+改钉 | G1 | link:1（10 枚出生区信物）；样本数 80000 → 100000；v1 黄金值在 link:1 重钉 |
| RelicGeneration/区域强度预算Tests | v5 | 区域强度预算 | 迁link/手写 | G1 | 均衡用 link:1（n=5）；同区同类型与高风险区交叉检查改同构 9×9 手写图（link:1 上惩罚效应 272‰ / 339‰ 小于原口径 1/3） |
| RelicGeneration/公共争夺区信物权重与高阶升级Tests | v5（真实图升级率） | 基准图上公共信物升级率落在宽口径 | 部分删 | G2 | 链接图无公共区信物；升级率由合成图上「高阶比例」「高档升级率严格高于标准档」守 |
| SimulationHarness/冒险概率记录Tests、各入口的地图专属AI权重Tests | 冒险概率、地图覆盖 | — | 删 | G2 | L1 / L9；原定 G3，载体随 G2 删除提前（−2 / −11） |
| SimulationHarness/边疆图终端试玩脚本Tests | 边疆图 | — | 删 | G2 |  |
| SimulationHarness/各入口按地图标识选图Tests | Legacy 入口、2 / 3 人图、文件路径 | 各入口按地图标识选图（旧标识族） | 部分删 | G2 | 由 _链接模式 孪生覆盖；保留并迁 link 的：未登记拼写被拒绝、显式选缺省地图与不带选项逐字相同（种子 5，L15）、三个入口同一目录（G2 改反面命中）、图形版选项登记 |
| SimulationHarness/各入口支持生成图Tests | gen: / board: | 各入口支持生成图（gen:） | 删 | G2 | 由 _链接模式 孪生覆盖；守门「生成器只经目录调用_裸gen…」改为链接档版本「生成器只经目录调用_裸link只在入口最外层取种子」加到孪生类（变异 2 次红） |
| SimulationHarness/地图子命令Tests | 边疆图、v5 缺省、文件路径 | map 子命令 | 删 | G2 | 原定 G3；ExportMap 的旧标识与 maps/ 导出随 G2 删除，提前；新增「map子命令拒绝旧标识」（5 行）「map子命令缺省地图」于 各入口支持生成图_链接模式Tests（3.4） |
| SimulationHarness/日志首部区数Tests | 边疆图、共用样本 | 区数写入与回填 | 迁link/合成 | G1 | link:7（5 区 4 人）；回填一条改合成日志 |
| SimulationHarness/日志首部地图摘要Tests | gen:、v5、边疆图 | 地图摘要 | 迁link | G1 | link:12345 / link:1 / link:7；旧日志缺摘要只删首行字段 |
| SimulationHarness/每局换图Tests | gen: | 每局换图 | 迁link | G1 | link:100 起；平台数断言随生成器删；平台边长改由出生区现算 |
| SimulationHarness/可复现回放Tests | resolveMap、带入 CarriedSession | 可复现回放 | 迁link | G1 | 失败局首部期望补 K=24 |
| SimulationHarness/可复现回放_链接模式Tests | v5 旧日志 | replay 拒绝旧日志 | 迁合成 | G1 | 旧日志改合成；Legacy 内部回放一行删 |
| SimulationHarness/批量跑局Tests | 共用配置 v5、V4 黄金哈希 | 批量执行并汇总、扫档、缺省关闭带入 | 迁link | G1 | 期望落成项改 K=24 / 无冒险概率；LinkGoldenTurnHash；G3：Assert.Null(config.FlagRisk) → ToJson 不含该键。 |
| SimulationHarness/防死锁硬停Tests | FlagRisk=0 | 硬停 | 迁link | G1 | 去 FlagRisk |
| SimulationHarness/随机子流隔离Tests | resolveMap | — | 迁link | G1 | 随共用配置 |
| SimulationHarness/终端对局Tests、终端专家对局Tests、终端活形与禁入标示Tests | v5（终端） | 终端对局 | 迁link | G1 | link:1 + 固定配对脚本；落点 B1 → V3；种子 45 / 2 号棋盘等（L15） |
| SimulationHarness/出生区编号显示Tests | FrontierFixtures | 六区图插旗快照等 | 迁手写 | G1 | 六平台图原样搬入本类（SixZoneMap），CreateUnvalidated |
| SimulationHarness/各入口按地图标识选图_链接模式Tests | v5（Legacy 建局对照） | 链接图建局为链接模式 | 部分删 | G2 | Legacy 一半 G2 删（原定 G3；条数不变）；「缺省地图」并入原「缺省地图不变」的 DefaultId / RunConfig 缺省断言；G3：「冒险概率选项已删除」改为配置文件写 FlagRisk 即报错、不建输出目录。 |
| SimulationHarness/各入口支持生成图_链接模式Tests | board:7（map 仍可打印旧标识）、v5（配对种子） | map 子命令仍可打印旧标识 | 部分删+新增 | G2 | 「map子命令仍可打印旧标识」G2 删（−1）；新增「map子命令拒绝旧标识」×5、「map子命令缺省地图」、守门「生成器只经目录调用_裸link…」（+7）；链接参数校验 v5 行期望改「不受支持」 |
| TacticalLayers/单子禁手的标示Tests（对照） | v5、board:1 | 与完整预演一致（真实中盘） | 迁link | G1 | link:1 种子 1 / 4、link:2 种子 3；Slow / Perf 行改 link:2 / link:3 |
| TacticalLayers/盘面层视图模型带地形Tests | v5（v3 地形） | 盘面层视图模型带地形 | 迁手写 | G1 | 13×13 手写地形夹具（逐格照搬 v3 被断言的地形） |
| Terrain/崖壁阈值唯一Tests、新地表判断唯一Tests | MapSymmetry / FrontierSurfaces（扫描豁免） | 唯一实现守门 | 改 | G2 | 随被删文件去掉豁免 |
| TerrainEditing/地形写入口Tests | FourPlayerBaseMap / FrontierMapV2 / FrontierMapGenerator（白名单） | 地形写入口唯一 | 改 | G2 | 白名单去掉已删类型 |
| ViewportCamera/全部用例 | CameraFixtures（v5 / 边疆图） | 相机 | 迁手写 | G1 | 见 CameraFixtures；期望一字不改 |
| VisualStyleBaseline/占位轮廓灰度Tests | FrontierFixtures.RepoRoot | — | 改 | G1 | RepoRoot 迁 TestPaths |

处理分布（行数，一行可含多个类）：删 9、删+迁 1、改 10、改钉 1、新增 1、迁link 38、迁link+改钉 5、迁link+部分删 2、迁link/合成 1、迁link/手写 3、迁link/手写+改钉 1、迁link/手写+新增 2、迁入 1、迁合成 1、迁手写 11、迁手写+改 1、部分删 8、部分删+新增 2；共 98 行。

