# Research: 依赖旧地图的测试与黄金值（问题 3、4）

- **Query**: 按依赖类型分组统计依赖 v5 / 2p / 3p / 边疆 / `gen:` 的测试；列出全部黄金值与基线；本机 11 条红测是否在其中
- **Scope**: internal（静态扫描 + 在 `git archive HEAD` 拷贝上实跑全量测试）
- **Date**: 2026-10-06

## 0. 方法与总量

- 全量实跑（scratchpad 拷贝，Mac 本机）：**Total 2364，Passed 2344，Failed 11，Skipped 9**，1 分 32 秒。
- 静态扫描脚本把 318 个测试文件切成测试方法（`[Fact]`/`[Theory]`，`[InlineData]` 行另计；方法 1817 个、InlineData 520 行），按方法体及同文件辅助成员（传递闭包）里出现的标记打标签：
  - `LEG`：`FourPlayerBaseMap|TwoPlayerBaseMap|ThreePlayerBaseMap|FrontierMapV2|siege-*-base-*|siege-frontier-v2|CameraFixtures.V4/Frontier|maps/siege`
  - `GEN`：`FrontierMapGenerator|GeneratedMapId|MapGenParameters|MapGenFixtures|gen:|FrontierMapLayout|FrontierSurfaces`
  - `V5FIX`：`SimFixtures.Config/Sample/RankedSample/RankedMatch/PinPreCalibration`、`LookaheadFixtures.V5Config/ProbePositions`
  - `PROFILE`：`MapProfile.Standard/Frontier`、`FrontierFixtures.Map`、`MapSymmetry`
- 结果：**依赖旧图 / 旧档的测试方法 439 个，分布在 101 个文件**；其余 1378 个不依赖。
- 钉 v5 的夹具：`tests/Siege.Core.Tests/SimFixtures.cs:24–40`（`Config` → `FourPlayerBaseMap.Id`，`Sample` / `RankedSample` 都建在它上面）、`LookaheadFixtures.cs:116–129`（`V5Config`）、`:141–165`（`ProbePositions`：v5 种子 1–3 共 92 个部署决策）、`ViewportCamera/CameraFixtures.cs:13–15`（`V4` = v5、`Frontier` = 边疆 v2）。纯边疆档 / `gen:` 夹具：`FrontierFixtures.cs:21–60`（合成边疆档图，`RepoRoot()` 另被 24 个文件用于找仓库根，与旧图无关）、`MapGenFixtures.cs`（186 行，全是 `gen:`）。
- 不依赖旧图的地形测试：大量规则测试本来就用 `TestMaps.Blank/Synthetic`（`TestMaps.cs:13–70`）、`MatchFixtures.Map(terrain)`（`MatchFixtures.cs:52–68`，9×9 合成图，档位缺省 Standard、经 `CreateUnvalidated`）、`LifeShapeFixtures`、`RelicFixtures.Scene` 构造盘面，全部绕过校验。

## 1. 分组统计（方法数；"a/b" = 该文件依赖旧图的方法 / 文件方法总数）

### (b) 测地图本身（删除或改写成棋盘图测试）——共 287

**b1 地图定义 118（16 个文件）**
`MapDefinition/` 三人基准地图 8/8、两人基准地图 7/7、四人基准地图 9/9、基准地图对称性 12/12、边疆档基准地图 12/12、边疆档静态校验 6/6——这 54 个随图整删；地图静态校验规则 22/27、人数适配预算 13/16、地图规格档 4/8、地图文件往返 7/7、地图文件健壮性 6/15、地图静态数据 3/4、出生区归属 4/4、棋盘清单 3/6、两位数行号贯通 1/3、内置棋盘图 1/7——这 64 个是"拿 v5 / 2p 当底图测校验器 / 文件格式 / 出生区"，其中测 Standard / Frontier 档专属规则（距离均衡拒绝、咽喉、入口通路、口袋、对称、标准档预算）的随规则删，测共用规则的要改用棋盘图或合成图。

**b2 生成器 51（13 个文件）**
`MapGeneration/` 生成参数与标识 10/10、生成图布局规则 9/9、生成校验闭环 5/5、生成确定性 10/10、新地表投放 6/6、布局速览 1/1——41 个整删；地图随机源 2/8（`ForAttempt` 黄金值）、棋盘档生成参数 1/17（"边疆档生成图不变"50 个 `gen:` 摘要）、棋盘档生成图标识 3/7、棋盘档人数参数化 1/4（与 `gen:` 的互不干扰对照）——删对应断言；`Terrain/新地表判断唯一` 1/2、`BoardTopology/四邻接` 1/8、`TerrainEditing/地形写入口` 1/11（架构守门里的类型名清单 `:247–249`）——改清单。

**b3 入口 / 选图 / 日志首部 43（10 个文件）**
`SimulationHarness/` 各入口支持生成图 9/9、每局换图 6/6、边疆图终端试玩脚本 1/1（随 `gen:` / 边疆删或改 `board:`）；各入口按地图标识选图 8/11、地图子命令 3/3、日志首部地图摘要 5/5、日志首部区数 4/6、出生区编号显示 1/4（改用棋盘图）；`MapSelection/` 选图界面守门 3/5、选图视图模型 3/14（"旧图不在清单上"类断言，改成"旧标识报错"）。

**b4 地图专属 AI 权重 15**：`AiDecision/地图专属评价权重覆盖` 7/8、`SimulationHarness/各入口的地图专属AI权重` 8/8——全部围绕 `siege-2p-base-v1` 覆盖表；随覆盖表去留裁决。

**b5 信物生成（真实图预算）14**：`RelicGeneration/` 信物在开局一次性生成 3/3、出生区信物权重 5/5（含 4 行分布黄金值）、区域强度预算 4/6、公共争夺区 1/7、生成结果可完整记录 1/1——规则测试，但需要"一张通过校验的真实地图"，换成内置棋盘图后计数 / 分布 / 黄金值要重算。

**b6 相机 46**：`ViewportCamera/` 全局预览 9/9、回到出生平台 12/14、缩放 8/8、推屏与平移 7/9、边界夹取 6/6、拾取 4/4。v5（13×13，"最远缩放一屏可见 = 等价旧固定相机"）当小图、边疆 25×30 当大图。棋盘图都是大图（4 人 39×41），"小图"情形需改用 2 人棋盘图或合成小图，场景要重新设计。

### (c) 黄金值 / 决策基线 / 逐步相同——共 97（20 个文件）

AiDecision：决策序列基线 1/1、专家难度的一层前瞻 4/16、专家前瞻的确定性与耗时 4/4、难度分级 1/7（3 行 InlineData）、候选格上限 6/15、停手阈值 6/15、默认评价权重的校准 6/9、活形分析的决策内缓存 1/4（v5 种子 1–20 与边疆种子 1–20 两条）；MatchSetup：原型插旗替代路径 14/17、对局内容集 6/6、对局配置公开完整地图标识 7/7、带入带出配置 3/8、计分规则版本 6/10；MatchTelemetry：专家前瞻的记录 9/10、对局日志的记录内容 8/11、平衡分析方向 2/9；SimulationHarness：批量跑局 9/22、批量跑局的计分规则版本 2/3；Determinism/随机子流隔离 1/8；Recruitment/征募随机可复现 1/5。

### (e1) 真实样本遥测——共 24（11 个文件）

全靠 `SimFixtures.Sample`（v5、Easy、种子 11–14、截断 16 小回合）或 `SimFixtures.Config` 小样本：MatchTelemetry 冲突占用率口径 1、分析排除测试污染 1、各棋子势力占比 1、地形改造日志与分析 4/7、插旗同区统计 1、数值目标回归 2/5、活形记录与统计 6/8、阵型的记录 2/3；SimulationHarness 可复现回放 3/4、防死锁硬停 1/1、随机子流隔离 2/2。夹具一换图样本全变；断言多是"样本口径"（如 4 局全是截断局、种子 11 恰有 1 枚未揭示信物、Standard 小样本里有匠人改造），需逐条核对是否仍成立。`地形改造日志与分析Tests.cs:47–137` 用 v5 上的实际搭桥 / 立栅 / 烧林；棋盘图上只有立栅可发生。

### (a) 只把 v5 当"一块能摆子的棋盘"——共 29

- **a1 换图 + 改坐标即可（约 15）**：`BoardTopology/棋盘格子状态模型` 2/4（信物格 B2、越界即障碍）、盘面副本与批量写入 1/8（`:74` 用 `CHOKE_NOT_ANNOTATED` 当"过不了校验"的诱饵——若咽喉规则删，诱饵改用棋盘内栅栏等）、盘面序列化 1/10；`RelicControl/信物控制判定` 1/8（1000 个随机盘面交叉校验，换图即可）；`RelicEffects/计分信物连营与犄角` 1/8；`Recruitment/初始配置与基础棋池` 2/8；`MatchFlowRegression/百局端到端` 1/1；`AiDecision/` 启发式评价维度 3/14、公开规则纯函数 1/5、前瞻的近似两层加分 1/9（`ProbePositions` 真实局面，换图后局面数等钉值要改）；`TacticalLayers/单子禁手的标示Tests.对照` 1/5（`:112–113` 两行 v5 种子与计数）。
- **a2 需要重新设计局面 / 脚本（约 14）**：`CarryInOut/终端的带入选择弃赛与结算显示` 8/11、`CarryInOut/AI带入` 1/7、`SimulationHarness/终端对局` 1/3、终端专家对局 1/1、终端活形与禁入标示 1/6、冒险概率记录 2/3——终端脚本按 v5 坐标与走法写死（`终端对局Tests.cs:26`、`终端专家对局Tests.cs:24`、`终端活形与禁入标示Tests.cs:128` 等），换图要重写输入脚本与期望输出。

估计：(a) 29 个里约一半"换图平移"、一半"重写脚本"；若把 b5（14）与 e1（24）也视为"规则跑在真实图上"，则"直接换图、只重钉计数"约 50、"需重新设计"约 15–20。

### (d) 依赖地形元素摆局面

- **依赖旧图上的地形摆局面：只有 2 个**——`TacticalLayers/盘面层视图模型带地形Tests.cs:11–18`（钉在 v5 地形上对照文本图），以及 b1/b2/e1 里随图删除的若干条（如四人基准地图的地形断言、新地表投放、地形改造日志的实际搭桥 / 烧林）。
- **其余 258 个地形测试方法不依赖旧图**：直接用合成盘面构造高度 / 深水 / 桥 / 栅栏 / 新地表（`Terrain/` 格属性 10、气边 7、覆盖关系 23、边属性 3、崖壁阈值唯一 2；`TerrainEditing/` 改造合法性 19、改造先于提子 11、地形写入口 10、地形派生数据不缓存 6、同形与存档纳入设施 5、改造不可逆与公开 4；`PieceEffects/高地压制加值` 11；`CaptureResolution/以整批最终状态判定合法性` 17；`TacticalLayers/` 新地表的规则标示 8、棋串军势常驻标注 12、改造在默认棋盘与棋串读法里的呈现 4；`AiDecision/` AI对地表的感知 5、AI枚举改造目标 5；`BatchPreview/改造在预演中的呈现` 8；`PowerScore`、`CoverageTerritory`、`LifeShape`、`BoardTopology/气的计算` 等若干）。

改写方案与代价：
1. **保留地形规则时**：这 258 个不用动——`GameBoard.LoadUnvalidated` / `MatchFlow.CreateUnvalidated` / `RestoreUnvalidated`（`internal`，`Siege.Core.csproj:11–12` 只对测试与 Sim 可见）本来就允许测试构造任意地形的非棋盘档盘面，棋盘档校验（禁障碍 / 高差 / 栅栏）只在 `GameBoard.Load` 走。代价 ≈ 0。唯一隐患：若删掉 `MapProfile.Standard`，`MapData.Profile` 缺省值要换成 `Board`，而这些合成图不满足棋盘档校验——由于全走 Unvalidated，不受影响；但少数"故意走校验"的测试（`地图静态校验规则Tests` 一类）需另建合成图。
2. **要在测试里得到"通过校验的、带地形的棋盘档地图"**：做不到（`MapValidator.cs:381、404–415、499–508` 明确拒绝）。只能 ① 继续 Unvalidated；或 ② 先 `Load` 一张棋盘图，再用 `TerrainWriter` / 改造写入栅栏（只有栅栏有合法来源）；高度 / 深水 / 林地 / 新地表无合法途径。
3. **删除地形规则时**：这 258 个里只测被删元素的随规则删（估计 150–200 个，栅栏相关的约 40–60 个保留），属于"删测试"而非"改写"。

## 2. 黄金值与基线清单（问题 4）

### 2.1 随旧图 / `gen:` 删除而整条删除（不重定）

| 位置 | 内容 |
|---|---|
| `MapDefinition/四人基准地图Tests.cs:180` | `V4JsonDigest`（v5 导出摘要 `D6366F99…`） |
| `MapGeneration/生成确定性Tests.cs:47–49` 等 | `gen:12345` 摘要 / 尝试序号 / 可落子数，多种子摘要（共 3 个 64 位哈希） |
| `MapGeneration/新地表投放Tests.cs` | `:s1` 生成图导出摘要（3 个哈希） |
| `MapGeneration/棋盘档生成参数Tests.cs:336–413` | "边疆档生成图不变"：`FrontierGolden` 50 个 `gen:1..50` 摘要 |
| `MapGeneration/地图随机源Tests.cs:66–69` | `ForAttempt` 黄金值 `GoldenA/B/C/K1`（`:82–85` 对局子流黄金值与地图无关，保留） |
| `AiDecision/Fixtures/decisions-siege-4p-base-v5-seed1.txt`（47 行） | v5 决策序列基线（`决策序列基线Tests` 的一半；`decisions-board1-seed1.txt` 54 行保留） |

### 2.2 测试改写到棋盘图后必须重定（v5 / 2p / 3p / 边疆 / gen: 上的实跑值）

| 位置 | 内容 | 被复用 |
|---|---|---|
| `AiDecision/候选格上限Tests.cs:61` `V4GoldenTurnHash` | v5、种子 31、Standard、24 小回合的逐步快照哈希 | **枢纽**：`对局内容集Tests.v1逐步相同`（:83）、`带入带出配置Tests.关闭时逐步相同`（:91）、`计分规则版本Tests` v1 逐步相同、`批量跑局Tests`（:239） |
| `AiDecision/停手阈值Tests.cs:26` | `StrictImprovementTurnHash` | |
| `AiDecision/难度分级Tests.cs:165–167` | 三档旧难度：小回合数、行数、logHash、decisionHash（3 行） | `专家前瞻的记录Tests.非专家没有前瞻记录` 复用 Standard 行 |
| `AiDecision/专家难度的一层前瞻Tests.cs:224` | `G1Golden`（逐种子决策数 / 哈希 / 消耗 / 记录哈希） | |
| `AiDecision/默认评价权重的校准Tests.cs` | v5 上的校准状态 / 样本断言 | |
| `MatchTelemetry/专家前瞻的记录Tests.cs:158` 等 | 一层配置记录哈希；`MatchTelemetry/Fixtures/expert-lookahead-smoke20-match-0000000000000008.jsonl.gz`（v5 专家日志，只做解析往返） | |
| `MatchTelemetry/平衡分析方向Tests.cs:158` | 内容集 v1 报告逐字节哈希 | |
| `SimulationHarness/各入口的地图专属AI权重Tests.cs:21–24` | 4 行（v5 / 3p / 边疆 / `gen:12345`）首部与 config 哈希 | |
| `RelicGeneration/出生区信物权重Tests.cs:92–97` | `PreChangeGolden`：v5 / 2p / 3p / 边疆的信物分布 SHA 与不收敛数 | |
| `MatchSetup/原型插旗替代路径Tests.cs:59–130` | v4/v5 上"改动前"的选区顺排、首回合顺序、信物分布（多组 InlineData） | |
| `MatchSetup/对局配置公开完整地图标识Tests.cs:203–205` | `GoldenFrontierOrder/Zones/RelicDigest`（边疆图上地图种子不扰动对局随机） | |
| `SimulationHarness/各入口按地图标识选图Tests.cs` | 1 个哈希 | |
| `SimFixtures.Sample/RankedSample` 派生的样本口径 | 见 (e1) | |

### 2.3 随测试改写自然变化 / 视裁决而定

- `MapDefinition/内置棋盘图Tests.cs:21` 三张内置棋盘图摘要、`MapGeneration/棋盘档生成参数Tests.cs:327–332`（`board:12345` 摘要等）、`decisions-board1-seed1.txt` 第 2 行的 `digest=`：**只要不改 `MapFile` 导出字段就不变**；若删 `ChokePoints` / `DistanceTolerance` / `MinTwoEyeArea` / `PocketExemptions` / `Profile` 等字段，三者全变，且按 `boundaries.md:62–68` 内置名须升号。
- 设计文档 / art 里的 `--auto-demo` 帧数与读数（`art/*/shots.sh`、`art/**/*.log`）是历史留档，不是测试判据。

### 2.4 本机 11 条红测

实跑失败清单（全部在上表 2.2 中，全部钉在旧图上）：

1. `AiDecision.难度分级Tests.三档旧难度逐步不变` × 3（Easy / Standard / Hard，v5）
2. `SimulationHarness.各入口的地图专属AI权重Tests.未登记的地图首部逐字节不变` × 4（v5 / 3p / 边疆 / `gen:12345`）
3. `MatchTelemetry.专家前瞻的记录Tests` × 3（非专家没有前瞻记录、一层配置的记录与改动前相同、本change之前的专家日志照常解析）
4. `MatchTelemetry.平衡分析方向Tests.内容集v1的报告除第12项外与引入新内容之前逐字节相同`

**根因（新发现，已实测）**：这 11 条红与地图无关，是两类**机器 / 平台相关的值漏进了被哈希的文本**：

1. **CPU 核数**：`RunConfig.EffectiveParallelism`（`src/Siege.Sim/Config/RunConfig.cs:207`，`Parallelism > 0 ? Parallelism : Environment.ProcessorCount`）是只读属性、没有 `JsonIgnore`，被序列化进日志首部 `Config`。`本change之前的专家日志照常解析` 的差异在第 869 字符：期望 `"EffectiveParallelism":28`（Windows 机 28 核）、实际 `8`（本机）。**实测**：在 scratchpad 拷贝上设 `DOTNET_PROCESSOR_COUNT=28` 重跑，`难度分级Tests.三档旧难度逐步不变` ×3 与 `专家前瞻的记录Tests` ×3 共 **6 条转绿**。
2. **行尾（推断，未实测）**：剩下 5 条（`各入口的地图专属AI权重Tests` ×4 在第 35 行比 `config.json` 哈希——首部哈希那行已过；`平衡分析方向Tests` 比 `ReportWriter` 报告哈希）在 28 核下仍红。`config.json` 是缩进 JSON、报告用 `AppendLine`，.NET 8 下两者都按 `Environment.NewLine` 换行，Windows 为 CRLF、Mac 为 LF，与"Windows 钉的黄金值在 Mac 上红"吻合。

推论：测试改写到棋盘图时这 11 条都会被重定。若只在一台机器上重钉，另一台必红（8 核 / 28 核、LF / CRLF）——**除非同时把 `EffectiveParallelism` 剔出被哈希文本（或这些测试显式给 `Parallelism`），并在哈希前统一行尾**。母任务 PRD 已预告"11 条已知红"的判据在第三个 change 之后重新确定；这里给出的根因可以让新判据变成"全绿"。

## Caveats / Not Found

- 分组是按文件归类、静态打标签，个别方法可能跨组（例如 `原型插旗替代路径` 既是黄金值又用 `MapProfile.Frontier` 合成图）；总数 439 是方法数，不含 InlineData 展开。
- (a)/(e1) 里"换图即可 / 需重设计"的划分是读调用点判断的，没有逐条试改。
- 11 条红测：6 条用 `DOTNET_PROCESSOR_COUNT=28` 实测转绿；其余 5 条的行尾根因是推断，未实测。
