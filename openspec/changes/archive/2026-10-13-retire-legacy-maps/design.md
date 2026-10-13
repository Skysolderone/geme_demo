## Context

`board-link-mode` 把游戏收口到链接模式时，为了不在同一个 change 里同时改规则和删旧物，留下了一组过渡口：

| 过渡口 | 引入裁决 | 现在的使用者 |
|---|---|---|
| `MapCatalog.Legacy.Resolve`（内置图、`gen:`、`board:`、文件路径、`maps/<标识>.json`） | D8 / D9 | 68 个测试文件共 306 处；Sim `map` 子命令 |
| `MapCatalog.Legacy.DefaultId`（= v5） | C10 / D16 | Sim `map` 子命令缺省、`RunConfig.MapId` 内部缺省 |
| Sim 入口的 `resolveMap` 参数（`BatchRunner` / `Replayer` / `MatchSession`） | D9 | 测试显式传 `MapCatalog.Legacy.Resolve` |
| `MatchFlow.RestoreLegacy`（internal） | D14 | 测试 |
| `MatchOptions.Mode`（缺省 `Legacy`）与 `RequireModeMatchesMap` | B1 | Sim / 图形版 `link ? Link : Legacy`；全部手写夹具（缺省 Legacy） |
| `MatchOptions.FlagRisk`、`RunConfig.FlagRisk`、`PrototypeZoneAssignment.Assign`、`GameSeed.FlagRisk` / `ZonePick` | B2 / C2 / C8 | Legacy 局的 `PlantPrototype`；测试 |
| `EvaluationWeights` 的地图覆盖表（`siege-2p-base-v1` → Eye 50） | v2-recalibration 段 B | 三入口按地图取权重；测试 |

源码侧（`src/`）直接引用旧地图、旧生成器或上述过渡口的共 34 个文件、140 处；测试侧直接引用旧标识或旧生成器的 114 个文件、915 处。

**关键事实（决定 L6）**：`MatchMode.Legacy` 不只是"旧地图的模式"。`tests/Siege.Core.Tests/MatchFixtures` 用一张 9×9 单连通合成图（四角 3×3 出生区、`MaxPlayers = 4`）经 `MatchFlow.CreateUnvalidated` 以缺省 `MatchOptions.Immediate`（Mode 缺省 Legacy）建局；`AiFixtures`、`PresentationFixtures`、`LookaheadFixtures` 等在它之上搭建。约 155 个测试文件建局，其中 33 个已是链接模式，其余约 120 个跑在 Legacy 规则上——保护期限出生区、整轮 Pass / 填满即终局（`EndReason.AllPassed` / `BoardFull`，约 11 个文件直接断言）、`FinalStandings.Compute`、插旗非盲选（公开旗数）、存档直接存盘面、AI 不受 C15 落后者阈值影响。L2 要求"保留不动"的围棋内核测试，正是这一批。

## Goals / Non-Goals

**Goals:**

- 删除旧地图数据、旧生成器、冒险概率、地图专属权重覆盖与全部过渡口，使 Core / Sim / 图形版的任何路径都到不了旧地图。
- 规格档与校验器收口为链接一档（L3）。
- 回归护栏不丢：依赖旧地图的规则测试逐条迁移；以 `link:1` 三个种子的决策序列基线替换 v5 与 `board:1` 基线（L2）。
- 链接模式的一切可观察行为（地图导出、日志、存档、决策序列、画面）逐字节不变。

**Non-Goals:**

- 地形规则（L4）、AI 评价与阈值、链接模式规则不改。
- 不重做 AI 校准（v5 上的校准记录保留为历史口径，L8）。
- 不彻底删除依模式分流的 Legacy 规则分支（L6 缺省）：登记后续 change `retire-legacy-rules`。
- `map-elements-v2` 无关。

## Decisions

### D1 删除顺序与每一步的不变量

四步顺序执行，每一步结束都必须全绿再进入下一步；每一步单独提交，便于逐步回退。

| 步 | 内容 | 不变量（每步结束时核对） |
|---|---|---|
| 1 迁移测试与钉新护栏 | 按迁移清单把"测规则但建在旧图上"的测试改到 `link:` 地图或手写夹具；在当前实现上钉入 `link:1` 种子 1、2 的决策序列基线与专家前瞻黄金值（种子 1–3）；旧地图专属测试暂不删 | 既有 `link:1` 种子 3 基线逐字节不变；全量测试通过数 = 步前 + 新增护栏数；被迁移的每条测试在新载体上通过，且清单上标明它测的规则 |
| 2 删数据与生成器 | 删四张内置图、`maps/`、嵌入资源、两套生成器全家、`MapSymmetry`、`FrontierSurfaces`、`MapCatalog.Legacy` / `BuiltinIds` / `BuiltinMaps`；`FriendlySeed` 迁入 `LinkMapId`；删只测这些对象的测试与 `FrontierFixtures` / `BoardMapFixtures` / `BoardGenFixtures` / `MapGenFixtures` | 链接图三条基线逐字节不变；`link:1`–`link:20` 初始地图导出与日志首部地图摘要与步前逐字节相同；通过数减少量 = 清单中本步标"删"的测试数 |
| 3 删 Legacy 入口与过渡口 | 删 `resolveMap` 参数、`RestoreLegacy`、冒险概率全家、地图覆盖全家、Sim / 图形版的模式分叉与只在 Legacy 局出现的分支；`MatchOptions.Mode` 设置降 internal、公开 `Create` 恒链接（L6 缺省）；`map` 子命令与 `RunConfig.MapId` 缺省改 `link:1` | 同上；另：`MatchFixtures` / `AiFixtures` / `PresentationFixtures` / `LookaheadFixtures` 的全部使用者逐条通过且期望值文件未改 |
| 4 简化校验器与规格档 | `MapProfile` 只留 `Link`（L7）；`MapValidator` 声明表只剩链接一行，删只为旧三档存在的分流（距离均衡、到中央入口可达、通道、全部连通、地标必填、单字母列宽上限）；`MapData.Profile` 缺省改 `Link`；`MapFile` 恒写规格档、缺字段或旧档值拒绝 | 同上；`link:1`–`link:20` 链接序列穷举（`link-map-generation`「全部链接序列都能拼接」）照常通过 |

"通过数"口径：`dotnet test` 的通过 / 跳过 / 失败三项，跳过数只允许是既有 Perf / Slow 类别；每步的增减必须能逐条对上迁移清单。

### D2 L6 缺省：两段式退役 Legacy

- **本 change**：所有让 Legacy 与旧地图相连的东西删除；`MatchOptions.Mode` 的 `init` 降为 internal，公开 `MatchFlow.Create` 恒以链接模式建局（`RequireModeMatchesMap` 只剩"链接模式须 4 人、须链接图"一侧）；`MatchFlow.CreateUnvalidated` / `RestoreUnvalidated`（internal，测试专用）照旧按 `options.Mode` 建局，缺省仍为 Legacy，所以 `MatchFixtures` 一行不改。`MatchMode` 枚举与只读 `Mode` 属性保留（`PublicRules`、`SeatProjection`、`HeuristicTurnController`、`DefaultBoardView` 等的模式分支继续服务手写夹具）。Sim 与图形版只会见到链接局，它们里面判 `Mode` 的分支（日志字段、`BoardRenderer`、自动演示判定、开局提示）收为链接一侧。
- **后续 change `retire-legacy-rules`**：评估并执行"手写夹具迁到链接规则"——需要能表达多块隔绝出生棋盘的手写夹具、改写约 11 个断言旧终局原因的文件与手写图上的 AI 黄金值，之后删除 `MatchMode`。
- **为什么不在本 change 做（方案 A）**：按 L1 字面删除，约 120 个测试文件要换规则底座（保护期范围、终局原因、名次算法、旗数视图、持久化形状、AI 决策都会变），与 L2"保留不动"、步 3 不变量和 Non-goal"AI 行为不改"同时冲突，且规模不跑测试无法量化。
- 本方案的代价：Core 里保留一组没有任何主规格条目背书、只服务测试的规则分支；设计文档与 HANDOFF 须写明这一债务。

### D3 规格档与地图文件（L3 / L7 缺省）

- `MapProfile` 保留枚举、只留 `Link` 一个成员（删枚举需要同时改 `MapFile` 字段与日志摘要输入，风险大于收益）。
- `MapFile` 写出时**恒写**规格档字段（今天链接图本来就写出 `Link`，因为它不是旧缺省值 `Standard`）——链接图导出与日志首部地图摘要逐字节不变。读入时字段缺失或为 `Standard` / `Frontier` / `Board` 一律拒绝并说明已移除。
- `MapData.Profile` 缺省改 `Link`：手写夹具不经校验加载，不受影响；手写夹具的导出文件会多出规格档字段，相关往返测试按清单改期望。
- 校验器保留的共用规则：结构、地形边界（桥在深水、栅栏在邻格）、可落子格与预算、出生区（数量、单区规模、保护期容量、出生区内无新地表）、信物格合法、容差字段、必死口袋、棋盘清单与拼接后校验。删除：距离均衡三态（`RejectOnImbalance` / `AlwaysReport` 随旧档去掉，链接档本就 `Skip`）、`BirthZonesMustReachEntrance`、`RequireLandmarks = true` 分支、`AllConnected` 连通性、通道宽 / 长规则、列宽 25 上限。链接行的棋盘清单规则（出生棋盘 5–7、棋盘内全为 h=0 草地、无障碍 / 深水 / 栅栏、每块 2 枚信物）先于其余规则执行、失败即提前返回，所以必死口袋、保护期容量与出生区无新地表在合规链接图上被清单规则蕴含、不会触发；它们与规格档无关，保留为底线，其用例按迁移清单处理（能直接调用对应校验步骤就保留断言，否则改由清单规则用例覆盖并记录）。规格档分流守门（现位于 `MapDefinition/边疆档静态校验Tests`，该文件其余用例随旧档删除，守门用例须先迁出）的口径（规格档字面量只在声明表内、规格档属性只在 `RulesOf` 读一次）不变。

### D4 冒险概率与插旗

`PlantPrototype` 恒走 `AssignBlind`（盲选、`flag-pick` 子流）；`Assign`、`RequireValidFlagRisk`、`MatchOptions.FlagRisk`、`GameSeed.FlagRisk` / `ZonePick` 删除。子流按名字派生，删掉两个从未在链接局里被消费的名字不改变任何其他子流的取值。`RunConfig.FlagRisk`：批次配置文件写了该字段即报错（与 C8 对链接图配置的既有口径一致，不静默忽略）。手写夹具用 `PlantSequentially`，不受影响；Legacy 下的 `FlagPlanting.LockAll`（未选者取旗数最少的区）随 L6 保留。

### D5 地图专属权重覆盖（L9 缺省）

删除覆盖表、`TwoPlayerOverrideCalibrationStatus` 与按地图取权重的解析；三入口的取用退化为"显式权重 → 默认表"。批量跑局"把覆盖落成为显式权重写入首部"的逻辑删除——链接图本来就没有登记，首部与配置记录逐字节不变。回放"首部权重为空按默认表"的既有口径不变。

### D6 回归护栏（L2 / L8）

- 决策序列基线：`link:1`、对局种子 1 / 2 / 3、内置固定配对脚本、4 名标准难度、截断到第 12 大回合（与 C5 一致）。种子 3 沿用既有 `decisions-link1-seed3.txt`；种子 1、2 在步 1 用当前实现钉入，单份 ≤ 200 KB。`decisions-siege-4p-base-v5-seed1.txt`、`decisions-board1-seed1.txt` 在步 2 删除。
- 专家前瞻黄金值（原 v5 种子 1–3）在步 1 改钉 `link:1` 种子 1–3（1 专家 + 3 标准、同一配对脚本、截断第 12 大回合）。前瞻耗时比（Perf）改在同一局面集上测。
- "校准后截断率达标"（v5 种子 1–20 默认套件、1–200 慢测试）与"大图耗时下降"（`board:1`）删除；校准口径字符串作为历史保留，`默认评价权重的校准Tests` 中断言口径字符串含 `siege-4p-base-v5` 的守门照旧。

### D7 入口与图形版

- Sim：`Program` 去掉裸 `gen` / `board` 的取种子分支与 `map` 子命令的 Legacy 分支，`map` 缺省 `link:1`；`ClockMapSeed` 改调 `LinkMapId.FriendlySeed`（同一折叠函数，输出不变）。`BatchRunner` / `Replayer` / `MatchSession` 的 `resolveMap` 参数删除，内部一律 `MapCatalog.Resolve`。
- 图形版：`MatchSession` 建局不再传模式；`GameRoot` / `GameRoot.MapSelect` 改调 `LinkMapId.FriendlySeed`；`BoardView` / `Visuals` 中只为 `board:` 通道与公共棋盘存在的样式按 L10 处理。

### 与其他 change 的接口契约

| 接口 | 改动 | 调用方 |
|---|---|---|
| `MapCatalog.Resolve(string?)` | 签名与行为不变（只认 `link:`，缺省 `link:1`） | Sim、图形版、测试 |
| `MapCatalog.Legacy`、`BuiltinIds`、`BuiltinMaps`、`BuiltinMapInfo` | 删除 | — |
| `LinkMapId.FriendlySeed(ulong)` | 新增（自 `GeneratedMapId` 迁入，输出逐位相同） | Sim `ClockMapSeed`、图形版新局界面 |
| `MatchFlow.Create(map, seed, players, options)` | 恒链接模式；`options.Mode` 不再可由外部设置 | Sim、图形版 |
| `MatchFlow.Restore(map, json)` | 不变；`RestoreLegacy` 删除 | Sim、图形版 |
| `MatchOptions` | 删 `FlagRisk`；`Mode` 的 `init` 降 internal | Sim、图形版、测试 |
| `BatchRunner` / `Replayer` / `MatchSession` 构造或入口 | 删 `resolveMap` 参数 | 测试 |
| `RunConfig` | 删 `FlagRisk`（写了即报错）；`MapId` 缺省 `link:1` | Sim、批次配置文件 |
| `EvaluationWeights` | 删覆盖表与按地图取权重 | AI 层、三入口 |
| `MapProfile` / `MapFile` | 只剩 `Link`；恒写字段；缺字段或旧档拒绝 | Core、测试 |
| 存档 / 日志格式 | 链接局逐字节不变 | `match-telemetry`、`match-setup` |

触发时机：本 change 不新增任何对局内事件；所有改动在建局、读档、读图与入口解析时生效。

### 影响清单（使用点）

- **Core 删除文件**（`src/Siege.Core/Board/Maps/`，18 个）：`FourPlayerBaseMap`、`TwoPlayerBaseMap`、`ThreePlayerBaseMap`、`FrontierMapV2`、`FrontierMapGenerator`、`FrontierMapLayout`（含 `.Checks` `.Corridors` `.Fill` `.Platforms` `.Ramps` `.Relics` `.River` 共 8 个）、`FrontierSurfaces`、`MapGenParameters`（含 `GeneratedMapId`）、`BoardMapGenerator`、`BoardMapLayout`、`BoardMapParameters`（含 `BoardMapId`）；另 `src/Siege.Core/Board/MapSymmetry.cs`。保留：`MapRandom`（链接生成器与配对源在用）、`LinkMapGenerator`、`LinkLayout`、`LinkMapId`、`MapCatalog`。
- **Core 修改**：`MapCatalog`、`MapValidator`、`MapData`、`MapFile`、`Siege.Core.csproj`（两项 `EmbeddedResource`）、`Determinism/GameSeed`、`Match/FlagPlanting`（`MatchOptions`）、`Match/PrototypeZoneAssignment`、`Match/MatchFlow`（`Build`、`PlantPrototype`、`RequireModeMatchesMap`）、`Match/MatchFlow.Persistence`（`RestoreLegacy`）、`Ai/EvaluationWeights`、`Ai/AiDifficulty`（只动注释中的地图引用，校准口径常量不动）。
- **Sim 修改**：`Program`、`Config/RunConfig`、`Running/BatchRunner`、`Running/Replayer`、`Running/MatchSession`（`resolveMap`、第 193 行模式分叉、第 696–698 / 772 / 914–919 / 1002 行的 Legacy 侧分支）、`Play/PlayCommand`、`Play/BoardRenderer`（第 271 / 297 行）。
- **Presentation / 图形版修改**：`Presentation/Camera/BoardCamera`（注释）、`godot/scripts/MatchSession`（第 186 行）、`godot/scripts/GameRoot`（第 306、685、691 行）、`godot/scripts/GameRoot.MapSelect`、`BoardView` / `Visuals`（L10）。
- **不动**：`ContentSets.Legacy`（内容集 v1，与旧地图无关，名字巧合）；依模式分流的 Core / Presentation / AI 规则分支（L6）。
- **测试**：见 tasks 第 1 组的迁移清单；`MatchFixtures` 不动。
- **数据**：`maps/` 目录 7 个 JSON（含早已不用的 v1–v3）；`tests/Siege.Core.Tests/AiDecision/Fixtures/` 两个旧基线文件。

## Risks / Trade-offs

- [迁移丢覆盖：某条规则只在旧图上被测过] → 步 1 的清单逐条写"测的规则 + 新载体"，清单本身作为验收物；新载体上的断言须是同一规则，不得退化成"能跑通"。
- [链接图字节漂移：`MapFile` 规格档写法或 `MapData` 缺省改动导致导出与日志摘要变化] → 步 4 前后对 `link:1`–`link:20` 导出与摘要做逐字节对照；三条决策基线每步都跑。
- [短种子折叠迁移改变输出] → `LinkMapId.FriendlySeed` 原样搬运函数体，加一条对旧实现取样的对照测试后再删旧实现。
- [Legacy 夹具在公开 `Create` 收口后无法建局] → 夹具全部走 `CreateUnvalidated`；收口前用 Grep 核对测试中直接调公开 `Create` 且不给 `Mode = Link` 的点，列入清单。
- [L6 债务长期化] → HANDOFF 与设计文档登记 `retire-legacy-rules`，写明前置评估项。
- [区域强度预算在链接图上 8% 收敛率未知（L12）] → 迁移时用种子 1–100 实测，未收敛按规格兜底条款处理并报告比例，不改规则。
- [图形版遮挡分类在链接图上不可达（L13）] → 由 Core 相机测试在含高差的手写夹具上守护；Godot `--pick-check` 只验链接图。
- [OpenSpec 不允许能力不含 Requirement] → `map-generation` 留一条"地图生成器已退役"的说明性 Requirement（试归档于临时副本验证：全部 REMOVED 时归档报"Spec must have at least one requirement"并中止，加此条后归档成功、34 份主规格 `--strict` 全过）。

## Migration Plan

1. 按 D1 四步实施，每步单独提交、全绿再继续；任一步失败只回退该步提交。
2. 第 4 步之后跑完整回归：`dotnet test`（全量）、三条 Godot 自检（`-- --auto-demo`、`-- --auto-demo --pick-check --seed=5`、`-- --map-select --auto-demo`，console 版 headless 顺序单跑、看真实退出码）、`-- --auto-demo --rounds=0 --seed=5`。
3. 同步 `HANDOFF.md` 与设计文档当前章节（§3、§4、§19 与 AI 校准口径的说明）；历史变更记录不改（L5）。
4. 归档时 `map-generation` 目录保留说明性条目；若负责人选择整体删除该能力目录，在归档后手工删除（L14）。

## Open Questions

以下各项均给出缺省建议，负责人可在表上一次性批注。

| # | 事项 | 选项 | 缺省建议 |
|---|---|---|---|
| L6 | Legacy 规则分支是手写夹具的运行底座（约 120 个测试文件，9×9 单连通合成图，缺省 Legacy 建局；约 11 个文件断言 `AllPassed` / `BoardFull` / Legacy 侧 `LastPlayerStanding`） | **A** 按 L1 字面删除，手写夹具整体迁到链接规则（需新的多棋盘手写夹具、改写保护期 / 终局 / 名次 / 旗数 / 持久化 / AI 黄金值期望，规模不跑测试无法量化）；**C** 两段式：本 change 删一切旧地图依赖与选模式的对外入口，规则分支留给 `retire-legacy-rules` | **C**（偏离 L1 字面；代价是 Core 保留一组只服务测试、无主规格背书的分支） |
| L7 | 规格档收口方式 | 保留单值枚举 `Link` / 删枚举；读入缺字段的地图文件：拒绝 / 按 `Link` 读 | 保留单值枚举；`MapFile` 恒写字段，缺字段或旧档值拒绝；`MapData.Profile` 缺省改 `Link` |
| L8 | v5 上的 AI 校准、黄金值与耗时测试 | 校准记录：保留为历史 / 在链接图上重做；"校准后截断率达标"：删 / 改 `link:1` 只报告；专家黄金值：改钉 `link:1` / 删 | 校准记录保留并注明"校准地图已退役、链接图上未复核"；截断率两场景删除；专家黄金值与前瞻耗时比改到 `link:1` 种子 1–3；"三档旧难度逐步不变""大图耗时下降"删除 |
| L9 | 地图专属权重覆盖 | 整体删除（表、解析、入口落成、守门、口径常量）/ 保留空表机制 | 整体删除 |
| L10 | 只被旧生成器用到的代码：通道与公共棋盘的校验与渲染样式、`MapSymmetry`、`FrontierSurfaces` | 随生成器删除 / 保留 | 校验器分支与 `MapSymmetry`、`FrontierSurfaces` 删除；渲染层通道 / 公共棋盘样式若无可达路径也删除，实施中发现被链接演出复用则保留并报告；`visual-style-baseline` 中"通道 SHALL 呈现为窄路"一句保留（无通道时自然不触发） |
| L11 | 措辞类：`carry-in-out`「AI 带入」中的"冒险抽签"；`battlefield` R5 理由句"以免改变旧地图上 AI 的合法改造候选"；`simulation-harness`"已降级的旧标识族" | 改 / 不改 | 前一项与第三项改（已写入 specs 增量）；R5 理由句是历史记述，不改 |
| L12 | 区域强度预算在链接图（5 块出生棋盘 × 2 枚）上的 8% 收敛率未实测 | 场景改为链接图并允许兜底 / 保留原 4 人场景改手写夹具 | 改为链接图，场景允许"标记生成未收敛"的兜底；迁移时实测种子 1–100 并报告未收敛比例 |
| L13 | 图形版 `--pick-check` 的遮挡分类在链接图上不可达（链接图无高差） | 由 Core 相机测试在手写高差夹具上守护 / 给图形版留一个测试专用的高差地图入口 | 前者；图形版不新增任何非链接地图入口 |
| L14 | `map-generation` 能力目录与「地图为设计师固定的静态数据」标题 | 留说明性 Requirement / 归档后手工删目录；标题保留 / 用 RENAMED 改名 | 留说明性 Requirement；标题保留（改名不改行为，避免额外的 RENAMED 增量） |
