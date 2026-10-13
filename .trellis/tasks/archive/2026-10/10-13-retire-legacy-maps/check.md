# retire-legacy-maps 质检报告（trellis-check）

> 2026-10-13。对象：相对 `74fbf28` 的未提交改动。未提交、未归档。

## 1. 覆盖结论（重点）

**仪器**：在临时 worktree 里构建 `74fbf28`，用 `dotnet test --list-tests` 取开工前全部 2395 个用例全名，与当前树逐名做差（含 Theory 参数），再逐条看 `git diff 74fbf28 -- tests`。另按文件统计"保留方法里被删的断言行"，找"方法还在、断言被删"的情况。

**总数**：用例名层面删 474、增 56。其中 29 条只是改名或改了参数（专家一层前瞻 6、公开规则纯函数 3、单子禁手对照 3、地图文件健壮性 7、对局配置 2 等），真正删除 **445 条**（通过 440 + 跳过 5），真正新增 27 条（通过 26 + Slow 1）。2395 − 445 − 29 + 27 + 29 = 1977，与当前 1971 + 6 吻合。

**分类（按用例计）**：
- **(a) 只关乎旧地图或已删规则，删除合理：约 384 条**。包括 MapGeneration 全部 11 类（226 条，旧生成器）、旧图数据 / 对称 / 人数适配 / 边疆档 / 棋盘档预算中只测旧档的部分、冒险概率与 zone-pick 选区（原型插旗 24 条、盲选 1 条、冒险概率记录 2 条）、地图专属权重覆盖（L9）、旧档规则（距离均衡、到中央入口可达、咽喉必须标注、地标可达，L3）、v5 / board:1 两条基线（L2）、截断率两条与 AI 小回合耗时（L8）、旧标识族 / 文件路径读图 / 内置图存档。
- **(b) 规则还在、只是载体是旧地图：约 61 条**。逐条去向如下。

| (b) 被删测试 | 规则 | 迁移后 / 现有等价覆盖 | 结论 |
|---|---|---|---|
| 地图随机源Tests.地图随机源不与任何对局子流同构 / 对局四条子流的取值不因地图随机源而变 / 生成器不见对局种子_对局流程不见地图种子（3） | 地图随机源与对局随机互不可见（D2） | 原有的 `对局种子不影响地图` 只扫 LinkMapGenerator / LinkMapId，而且只扫单向 | **丢 → 已补** `链接档地图标识与确定性Tests.地图随机源与对局随机互不可见` |
| 生成确定性Tests.生成器源码不含…浮点_时钟与环境（MapRandom.cs 那部分） | MapRandom 禁浮点 / 时钟 / 环境 | 没有测试再扫 MapRandom.cs | **丢 → 已补**（同上一条测试） |
| 边疆档静态校验Tests.超宽地图被拒绝而不是抛异常 | 外接列 / 行区间（链接档列 20–75、行 8–21） | MAP_TOO_* 四个码原先没有任何链接档用例 | **丢 → 已补** `地图静态校验规则Tests.外接尺寸越界被拒绝而不是抛异常` |
| 地图静态校验规则Tests.距离沿气边计算 | `DistanceTable` / `MultiSourceDistances` 沿气边算距离（L10 保留，供 `map` 打印） | 保留下来的代码没有任何测试 | **丢 → 已补** `地形派生数据不缓存Tests.距离表沿气边计算` |
| 棋盘档预算与校验Tests：信物格必须在棋盘内、出生棋盘与出生区必须一一对应、出生棋盘边长 ×2、棋盘内有障碍、棋盘小于5乘5、不得含深水…栅栏、尺寸与清单要求（8） | 棋盘清单规则（链接档仍在执行同一份 ValidatePlateList 代码） | BOARDS_REQUIRED / BOARD_SIZE_OUT_OF_RANGE / BOARD_CELL_NOT_PLAYABLE / BOARD_MAP_DEEP_WATER / BOARD_MAP_FENCE / BIRTH_BOARD_MISMATCH / RELIC_OUTSIDE_BOARD / BIRTH_ZONE_COUNT_NOT_ABOVE_PLAYERS 都降到 0 个用例 | **丢 → 已补** `物化状态校验Tests.棋盘清单规则逐条报出`（8 个码逐一断言） |
| 棋盘清单Tests.棋盘清单项的文件写法里的 `"Kind": "Public"` 断言（方法保留，断言被删） | 棋盘类别"公共"仍是文件格式（规格「棋盘清单」，BoardPlateKind.Public 按 L10 保留） | 被删后没人守 | **丢 → 已补**（同一方法内补回公共棋盘的往返断言） |
| 边疆档静态校验Tests.校验器对规格档的分支只在声明表里 | 规格档分流守门 | `规格档分流守门Tests.校验器对规格档的分支只在声明表里` | 已迁 |
| 人数适配预算Tests：不支持的人数、信物格数不足、出生区数量、格数超出预算（4） | 规模预算 | `地图静态校验规则Tests.不支持的人数被拒绝 / 信物格数越界 / 出生区数越界 / 可落子格越界` | 已迁 |
| 边疆档基准地图.保护期容量（6）、保护期边界在边疆图上照常（2）、边疆图终端试玩脚本（1） | 保护期容量与落子范围 | `地图静态校验规则Tests.出生区容不下九枚部署 / 出生区恰好九格时通过`；`构筑保护期的落子限制Tests`、`第4大回合的全图解禁Tests`、`合法落子范围的对外契约Tests.范围随大回合切换`、`战场内合法落子范围Tests` | 已有覆盖 |
| 基准地图对称性.全盘可落子格连通且无小口袋 | 必死口袋（连通性规则本身 L3 已删） | `地图静态校验规则Tests.必死口袋` 等 4 条 | 已有覆盖 |
| 各入口支持生成图Tests（8 条，gen:） | 入口取种子、记录完整标识、目录唯一、终端开局、map 打印 | `各入口支持生成图_链接模式Tests`：裸link取种子并打印完整标识 / 终端版链接图能开局并按座位渲染 / 生成器只经目录调用_裸link… / map子命令打印链接初始图；`链接档地图标识与确定性Tests.目录解析link标识`；`日志首部地图摘要Tests` | 已迁（孪生类） |
| 地图子命令Tests（2） | map 缺省、拒绝未登记的拼写、打印 | `map子命令缺省地图`、`map子命令拒绝旧标识`×5、`map子命令打印链接初始图` | 已迁 |
| 各入口按地图标识选图Tests.未知标识报错 / 缺省地图不变 | 同名规则 | `_链接模式Tests.未知标识报错 / 缺省地图` | 已迁 |
| 各入口按地图标识选图Tests.参赛人数缺省取地图人数上限_显式人数照旧 | small-maps D3 | 链接图恒为 4 人，`DefaultPlayerCount` 退化为恒等；`缺省地图` 只间接守 4 人 | **退化**（见 L20） |
| 旧地图降级…玩家入口不可达（1）、既有生成器降级…玩家入口拒绝旧标识（6） | 入口拒绝旧标识 | `目录解析link标识`、`_链接模式Tests.旧标识报错`（含配置文件） | 已有覆盖 |
| 各入口的地图专属AI权重.显式权重不被覆盖替换 / 新日志按首部回放；地图专属评价权重覆盖.显式权重整表优先 / 未登记的地图取默认表 | 显式权重 → 默认表两级、按首部回放 | `批量跑局Tests`（显式整表进首部）、`启发式评价维度Tests.九维权重经配置往返…`、`默认评价权重的校准Tests`（留空与显式 Default 逐步一致）、`可复现回放Tests` | 已有覆盖 |
| 公共争夺区….基准图上公共信物升级率落在宽口径 | 高阶升级 | 合成图上的 `高阶比例`、`高档升级率严格高于标准档` | 已有覆盖 |
| 活形缓存 Slow ×2 | 缓存不改决策 | `缓存开关不改变决策序列_链接图种子1至20`（Slow）+ 默认套件缩小版 | 已迁 |
| 决策序列基线 v5 / board:1（2） | AI 决策回归 | `链接图决策序列与基线相同`（种子 1 / 2）+ 种子 3 | 按 L2 换护栏 |
| 难度分级.三档旧难度逐步不变（3） | Easy / Standard / Hard 整局逐步不变 | Standard 由三条链接基线守，Expert 由 G1Golden 守；**Easy / Hard 已没有整局逐步黄金值** | L8 已裁决删除，不算违规；记为残余风险（L21） |

**抽样 5 条"断言未改、只换载体"**（git diff 逐行看过）：
1. `棋盘格子状态模型Tests`：v5 换成 13×13 手写夹具（`WithRelicCells(["B2"])` / `Synthetic(13)`），断言一字未改。✔
2. `信物控制判定Tests`：v5 换成 `MapCatalog.Resolve("link:1")`，断言未改。✔
3. `盘面派生量提速对照Tests`：v5 / board:1 两个样本换成 link:2 初始图和 link:7 链接一次之后，比对断言未改。✔
4. `冲突占用率口径Tests`：期望值 105 改成 157。这是随载体变的常量，我独立核算过：link:1 是 43×11 − 316 个障碍 = 157，与 `snapshots/g1/header1.txt` 的 `PlayableCells` 一致，不是凑出来的。断言结构没变。✔（归为"数值随夹具变"）
5. `ViewportCamera/*`：只改了 `CameraFixtures.cs`（新加两份手写夹具）和一个文件的 RepoRoot，75 条用例的期望一字未动。✔

## 2. 其余各项

| # | 项 | 结论 |
|---|---|---|
| 2 | 数字对账 | **通过**。按用例全名独立重算：2395 → 1977，−474 + 56；去掉 29 条改名或改参数，正好是 migration.md 的 −440 / +26（通过）加跳过 −5 / +1。跳过数从 10 降到 6，少掉的 4 条是 AI小回合耗时（Perf）、截断率种子1至200（Slow）、活形缓存两条 Slow 合并后少的 1 条、棋盘档布局规则.生成耗时（Perf），全部随旧图删除或合并。✔ |
| 3 | 护栏 | **通过**。三份基线都在，`决策序列基线Tests`（种子 1 / 2）和 `决策序列基线_链接模式Tests`（种子 3）引用它们，csproj 用 `AiDecision\Fixtures\*.txt` 复制到输出；种子 3 相对 74fbf28 无 diff；G1Golden = link:1 种子 1–3（18/3069/9、22/7502/11、24/6510/12）。**注意**：`decisions-link1-seed1.txt` / `seed2.txt` 现在还是未跟踪（`??`），提交时必须一起 add。 |
| 4 | L6 = C | **通过**。`MatchOptions.Mode { get; internal init; }`，InternalsVisibleTo 只有 Siege.Core.Tests 和 Siege.Sim，Godot / Presentation 拿不到；Sim、Godot、Presentation 里没有任何地方写 `Mode =`。公开 `Create` 强制 `Mode = Link`；`CreateUnvalidated` / `RestoreUnvalidated` 按传入或存档里的模式走；公开 `Restore` 拒绝非 link: 标识，也拒绝缺模式字段的存档。`MatchFixtures.cs` 相对 74fbf28 无 diff。 |
| 5 | 残留引用 | **通过**。src / tests 里的旧记号只剩注释、校准口径字符串（历史，L5）、反面样本（被拒绝的标识）和守门正则，没有代码引用。spec 的 5 处已自修，另顺手修了 4 处过期陈述（见 §3）。 |
| 6 | 规格一致性 | `openspec validate retire-legacy-maps --strict` 通过。能力增量实际是 **8 个**（与 proposal 一致；派发说明写的"9 个"是笔误）。「地图规格档」：缺字段、Standard / Frontier / Board（不分大小写）、0 / 1 / 2 都拒绝，有 8 行用例。✔ 「地图静态校验规则」的 6 条与校验器对得上：口袋、信物格可落子与分区、规模、容量 9、出生区无新地表都有对应实现；第 4 条桥 / 栅栏由 `TerrainData` 构造期强制。但校验器另外还跑 `ValidateLandmarks`（中央入口 / 咽喉须可落子）和 `ValidateTolerance`（容差与放宽理由），这两条原本挂在已删的距离均衡规则下面，现在规格里没写，成了孤儿（**L18**）。`BIRTH_ZONE_COUNT_MISMATCH` 分支在链接档下不可达（区间恒为 5 = 5，不等于人数 4），注释还写着"标准档"（**L19**）。`map-generation` 只剩说明性的「地图生成器已退役」，其余 9 条全部 REMOVED。✔ |
| 7 | 硬边界 | **通过**。Core 的 diff 没有新增浮点、时钟、`Random` 或 Godot；csproj 只删了两项嵌入资源，`NoGodotDependency` 守门还在；`GameSeed` 只删了 zone-pick / flag-risk 两个常量，其余子流不变；`MapRandom.ForLinkLayout` 一字未改，三条链接基线全过。 |
| 8 | 验证产物 | `snapshots/` 实测 **4.0 MB**（implement.md 写的"约 2 MB"偏小），内容是 g1–g4 的 link:1–20 地图、首部、config，加 g2/g3/g4-full 的完整日志；`shots/` 1.2 MB，4 张 PNG。两者都只是验证产物，可以重新生成，结论已经记在 migration.md。**建议不入库**（见 L22）。 |

## 3. 自修清单

**spec 文本**（与现状对齐）：
- `.trellis/spec/core/boundaries.md`：第 26 行规格档分流改为只剩 Link、守门改为 `规格档分流守门Tests`；第 27 行"标识 → 地图"改为只认 `link:`、缺省 `link:1`、`LinkMapId.FriendlySeed`；第 28 行摘要改为 `link:`；第 29 行原型插旗改为 `AssignBlind` / `flag-pick`；第 33 行删去"对称检查"；第 35 行地形写入口白名单删去"基准图"；第 48 行棋盘档生成改为链接档生成与标识；第 62–69 行"内置图内容一变，标识必须递增"改为"地图标识与地图内容"，旧约定留作背景。
- `.trellis/spec/core/determinism.md`：子流表去掉 zone-pick / flag-risk、加 flag-pick，"五条"改为"以下几条"；第 19 行地图随机源段改写为 `ForLinkLayout` / 裸 `link` / `LinkMapId.FriendlySeed`，并注明守门测试。
- `.trellis/spec/core/testing.md` 第 83 行：`gen:12345` 摘要黄金值改为 link 快照与三条基线；注明 v4 / 边疆图实测数字属于历史。

**补测试**（只加断言，不碰规则代码）：
- `tests/Siege.Core.Tests/LinkMapGeneration/链接档地图标识与确定性Tests.cs`：新增 `地图随机源与对局随机互不可见`（同构、三条子流黄金值、双向源码扫描、MapRandom.cs 禁浮点和时钟）。
- `tests/Siege.Core.Tests/LinkMapGeneration/物化状态校验Tests.cs`：新增 `棋盘清单规则逐条报出`（8 个失败码）。
- `tests/Siege.Core.Tests/MapDefinition/地图静态校验规则Tests.cs`：新增 `外接尺寸越界被拒绝而不是抛异常`（四个方向 + 宽 1000 + 端点）。
- `tests/Siege.Core.Tests/TerrainEditing/地形派生数据不缓存Tests.cs`：新增 `距离表沿气边计算`，同时修了 `距离表按传入地图现算` 里过期的"唯二调用方"注释。
- `tests/Siege.Core.Tests/MapDefinition/棋盘清单Tests.cs`：`棋盘清单项的文件写法` 补回 `"Kind": "Public"` 的往返断言。

**过期注释**：
- `tests/Siege.Core.Tests/AiDecision/决策序列基线_链接模式Tests.cs` 类注释（"旧图两条基线不受影响"）。
- `src/Siege.Presentation/Camera/BoardCamera.cs:196` 举例 v5。
- `src/Siege.Sim/Program.cs` 的 run 段（"RunConfig 缺省仍是 Legacy"、裸 gen 三行）和 `ClockMapSeed` 注释（裸 gen → 裸 link）。

**未修**（Core 源码注释，价值低，留给 retire-legacy-rules）：`GameSeed.CarryAi` 注释里的"原型选区、冒险抽签"；`MapValidator.DistanceTable` 注释里的"规则第 1 条的唯一口径"；`ValidateBudgets` 注释里的"标准档 / 棋盘档"。

## 4. 构建与测试

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：**通过 1975 / 跳过 6 / 失败 0**（1971 + 本次补的 4 条）。
- 本次没有跑 Godot 和批量，没做 git 提交，没执行 `openspec archive`；临时 worktree 已删除。

## 5. 是否建议提交与待裁决

**建议提交**：可以提交。删除没有留下丢失的覆盖：找到的丢失项已全部补回，剩下的是退化或 L8 已裁决的残余。提交时注意：① 以下 5 个新文件目前未跟踪（`git status` 里是 `??`），必须一起 add，否则树编不过或基线缺失：`src/Siege.Core/Board/Maps/MapGenerationException.cs`、`tests/Siege.Core.Tests/AiDecision/Fixtures/decisions-link1-seed1.txt`、`tests/Siege.Core.Tests/AiDecision/Fixtures/decisions-link1-seed2.txt`、`tests/Siege.Core.Tests/MapDefinition/规格档分流守门Tests.cs`、`tests/Siege.Core.Tests/TestPaths.cs`（任务目录里的 implement.md / migration.md / check.md 另算）；② 按 L22 处理验证产物。L15–L17（实施方提出）和下面的 L18–L21 都不阻塞提交。

**待裁决（L18 起）**：
- **L18** `MapValidator.ValidateLandmarks`（中央入口 / 咽喉须可落子）与 `ValidateTolerance`（容差非负、放宽须写理由）原本挂在已删的距离均衡规则下，现在规格的 6 条里没有。建议：A 写进 map-definition「地图静态校验规则」作为底线条款；B 在 retire-legacy-rules 里连同 MapData 的咽喉 / 容差字段一起删（会改链接图导出字节，要重钉快照）。缺省建议 B。
- **L19** `ValidateBudgets` 里的 `BIRTH_ZONE_COUNT_MISMATCH` 分支（"区间退化成恰等于人数"，标准档遗留）在链接档下不可达。建议并入 retire-legacy-rules 删除，或现在删一个分支（不改行为）。
- **L20** `Program.DefaultPlayerCount` / `DeclaresPlayers`（small-maps D3"未指定人数取地图上限"）在只有 4 人链接图后退化成恒等，原测试已随 2 / 3 人图删除。建议在 retire-legacy-rules 里删掉，或保留并补一条"配置文件写了 Players 就按写的"用例。
- **L21**（L8 的后果，不是违规）Easy / Hard 已没有整局逐步黄金值，三条链接基线只守 Standard，G1 只守 Expert。是否补钉 `link:1` 上 Easy / Hard 各一条决策序列基线（新钉，不改行为）。
- **L22** 验证产物：`snapshots/`（4.0 MB）建议不入库；`shots/`（1.2 MB）建议不入库，若要留证据只留 `final-build.png` 与 `final-final-link.png`。（implement.md 里写的"约 2 MB"已改为 4 MB。）
