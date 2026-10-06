# Research: 内置棋盘图登记方式与各入口缺省地图切换

- **Query**: 第二个 change（`builtin-board-maps`）的现状调研：内置图登记、缺省地图出现处、选图界面、AI 权重 / K、Godot 通道死代码、文档耦合、内置种子挑选依据
- **Scope**: internal（代码 + 规格 + 实测）
- **Date**: 2026-10-06
- **基线**: 工作树 `feat/board-terrain` = main `c4a9e23`
- **实测方式**: 仓库拷贝到 scratchpad（`.../scratchpad/repo`、`repo2`）里构建与跑测，仓库本身未改动

---

## 1. 内置图的登记方式

### 1.1 现行登记（`src/Siege.Core/Board/Maps/MapCatalog.cs`）

| 位置 | 内容 |
|---|---|
| `MapCatalog.cs:20-26` | `Builtins` 表：`(Id, Title, Func<MapData> Create)` 四行：`FourPlayerBaseMap`（"标准图 13×13"）、`TwoPlayerBaseMap`（"双人图 9×9"）、`ThreePlayerBaseMap`（"三人图 11×11"）、`FrontierMapV2`（"边疆图 25×30（手工）"） |
| `MapCatalog.cs:29` | `public const string DefaultId = FourPlayerBaseMap.Id;`（`"siege-4p-base-v5"`，`FourPlayerBaseMap.cs:30`） |
| `MapCatalog.cs:32,35` | `BuiltinIds`、`BuiltinMaps`（标识 + 显示名，选图界面只从这里读） |
| `MapCatalog.cs:46-89` | `Resolve`：空白 → `DefaultId`；**先按字符串精确匹配 `Builtins`**；再 `gen:` / `board:`（`BoardMapGenerator.Generate(id)`，`:77`）；最后按文件路径 / `maps/<id>.json` |
| `MapCatalog.cs:84` | 未知标识报错文案列出 `BuiltinIds` 与 `gen:` / `board:` 写法 |

2 人 / 3 人图是嵌入资源 json（`Siege.Core.csproj:19-20`，`TwoPlayerBaseMap.cs:25-35` 用 `GetManifestResourceStream`）；v5 与边疆 v2 由代码构造。

### 1.2 "内置图内容一变，标识必须升号"

`.trellis/spec/core/boundaries.md:62-69`（restore-go-core-rules D6）：
- 内置图任何内容变化 MUST 同时递增标识；理由是日志首部、存档、`config.json` 只记标识与 `MapFile.Digest`。
- 旧标识 MUST 从 `Builtins` 删除，`maps/<旧标识>.json` 不得留在仓库（否则 `Resolve` 的文件回落会复活旧标识），守门 `各入口按地图标识选图Tests.改名前的旧地图标识报未知地图`。
- 类名随标识走。
- **生成图 `gen:` 标识不含生成器版本段，生成器改动后同一标识产出不同的图是已接受的不兼容**（`:69`）。`board:` 同理（`BoardMapGenerator.cs` 注释、`boundaries.md:43`）。

相关事实：
- 生成器产出的 `MapData.Id` = 规范化 `board:` 标识（`BoardMapGenerator.cs:77,260` `Id = id`）。
- `MapFile.Digest`（`MapFile.cs:73-77`）= `ToJson` 文本的 SHA-256，**`ToJson` 含 `Id` 字段**（`MapFile.cs:32`）→ 同一内容、不同 `Id` 摘要不同。
- 日志首部 `MapId = Match.Map.Id`（`Siege.Sim/Running/MatchSession.cs:758`）、`MapDigest = MapFile.Digest(BaseMap)`（`:765`）；回放按 `header.Config.MapId`（每局换图时按 `header.MapId`）重建后比摘要（`Replayer.cs:50-60`）。
- AI 地图专属权重按 `MapData.Id` 查表（`EvaluationWeights.cs:139-151`）。
- `boundaries.md:43`：`BoardMapGenerator` 的唯一生产调用方是 `MapCatalog`。

### 1.3 用固定种子棋盘图做内置图的三种写法

| | A. 别名：`siege-4p-board-v1` → `board:<种子>` | B. 内置图就是 `board:<种子>` | C. 固化成 json（像 2p/3p 那样嵌入资源） |
|---|---|---|---|
| 登记 | `Builtins` 加 `("siege-4p-board-v1", "<显示名>", () => BoardMapGenerator.Generate(seed, p) with { Id = "siege-4p-board-v1" })` | `Builtins` 加 `("board:<种子>", "<显示名>", () => BoardMapGenerator.Generate("board:<种子>"))`，或不进 `Builtins`、只另开一张"推荐清单" | `map --map board:<种子> --out` 导出 → `maps/siege-4p-board-v1.json` → `EmbeddedResource` |
| 日志 / 存档 `MapId` | 别名 | `board:<种子>` | 别名 |
| `MapDigest` | 与同内容的 `board:<种子>` **不同**（`Id` 进摘要） | 与随机选到同种子时相同 | 别名摘要 |
| `--map=` | `--map=siege-4p-board-v1` 与 `--map=board:<种子>` 都能开，得到内容相同、标识与摘要不同的两张图 | 只有一种写法；`board:<种子>:n7` 这种非规范写法不命中 `Builtins` 的字符串匹配，但会落到生成器，结果仍相同（Id 规范化） | 同 A |
| 生成器改版时 | 别名内容静默改变 → 违反 D6；须加一条"别名摘要黄金值"守门，变了就升号 `v2`（并按 D6 删除 `v1`） | 按 D6 第 4 条属"已接受的不兼容"，摘要比对在回放时报"地图不一致"；内置图的内容不再稳定 | 生成器改版不影响内置图；内置图与生成器脱钩 |
| `map` 子命令导出 | 按内置标识请求即写 `maps/siege-4p-board-v1.json`（`Program.cs:259-272`）——与 D6 "maps/ 下不留文件以免复活"互动要想清楚 | **`Path.Combine("maps", $"{map.Id}.json")` = `maps/board:<种子>.json`**（`Program.cs:269`），Windows 上冒号非法（NTFS 会当成备用数据流）→ 必须把 board 标识排除出导出分支 / 不进 `BuiltinIds` | json 本身就是权威文件 |
| AI 权重表 | 按别名登记 | 按 `board:<种子>` 登记 | 按别名登记 |
| 选图界面 | 作为 `Builtin` 项出现，标题来自目录 | 若进 `Builtins`：`TrySelectId("board:<种子>")` 先命中内置项（`MapSelectModel.cs` `TrySelectId` 先查 `BuiltinId`）；"棋盘图（随机）"项同种子会产出相同标识 | 同 A |
| 既有守门的冲突 | `选图界面守门Tests` 的禁用正则 `siege-\d+p` 已覆盖别名，无冲突 | `两人基准地图Tests.cs:156` 断言 `DefaultId == BuiltinIds[0]`；若缺省是 `board:` 而不进 `Builtins` 此条要改 | 需要改 csproj、`HANDOFF.md:61` 的"权威来源"说明 |

`GameRoot.MapSelect.cs:233-238` 的自检对每个内置项断言 `_session.World.Board().Boards.IsEmpty`——任何写法下，只要棋盘图进入内置项，这条都会红。

---

## 2. 缺省地图出现在哪里

### 2.1 生产代码（直接引用 `DefaultId` 或走 `Resolve(null)`）

| 文件:行 | 用法 |
|---|---|
| `Siege.Core/Board/Maps/MapCatalog.cs:29,48` | 定义与 `Resolve` 空白回落 |
| `Siege.Core/Board/Maps/FourPlayerBaseMap.cs:29` | 注释"也是各入口的缺省地图" |
| `Siege.Sim/Config/RunConfig.cs:92` | `MapId { get; init; } = MapCatalog.DefaultId`（`run` 缺省、`config.json` 缺项） |
| `Siege.Sim/Program.cs:97-102` | `play`：`--map` 缺省 `null` → `MapCatalog.Resolve(null)` |
| `Siege.Sim/Program.cs:170-175,261` | `map`：缺省打印 `DefaultId` 的图，且 `requested = DefaultId` 时**导出 `maps/<map.Id>.json`** |
| `Siege.Sim/Program.cs:354` | `run`：`cli.Get("map", config.MapId)` |
| `Siege.Sim/Program.cs:143-148` | 裸 `board` → `BoardMapParameters.Default`（4 人）——与缺省无关但同属入口 |
| `Siege.Sim/Play/PlayCommand.cs:17,91-96` | 终端版：`map.Id != DefaultId` 时才打印"地图 …"一行（缺省图不打印） |
| `Siege.Sim/Running/BatchRunner.cs:61`、`MatchSession.cs:147` | `MapCatalog.Resolve(config.MapId)` |
| `src/godot/scripts/GameRoot.cs:107,315-325` | 未给 `--map=` 且非无人值守 → 选图界面；**无人值守（`--auto-demo` / `--pick-check` / `--screenshot` / `_shotPending`，`GameRoot.cs:1579`）或 `--reveal-preview` 未给 `--map=` → `MapCatalog.Resolve(null)`** |
| `src/godot/scripts/GameRoot.Reveal.cs:103-113` | 揭示预览的示例棋串放在地图几何中心附近；种子 1–50 中 4 人棋盘图中心格可落子只有 37/50（3 人 32/50、2 人 32/50），新缺省图上示例可能落在场景格 |
| `src/godot/scripts/MatchSession.cs:112` | 注释"缺省四方标准地图" |
| `Siege.Presentation/Camera/BoardCamera.cs:120` | 注释举例 v5 |
| `Siege.Presentation/MapSelect/MapSelectModel.cs` 类注释 | "批量 / 终端入口与无人值守演示的缺省地图仍由目录决定，本类不碰" |

图形版对局人数：`Math.Min(4, map.MaxPlayers)`（`GameRoot.cs:324`、`GameRoot.MapSelect.cs:63,293`）；Godot 没有 `--players`。

### 2.2 测试依赖：实测把 `DefaultId` 改成 `board:1` 跑全量

基线（scratchpad 拷贝，无 `art/`）：2339 条 = 2317 过 / 13 失败（11 条本机日志黄金值 + 2 条因拷贝缺 `art/` 目录）/ 9 跳过，**用时 1 分 02 秒**。
变异 `DefaultId = "board:1"`：**65 失败 → 新增 52 条，分布在 30 个测试类；用时 19 分 01 秒**（`专家难度的一层前瞻Tests.一层配置与改动前的专家逐步相同` 单条 1.5–2.2 分钟）。
旁证：`各入口按地图标识选图Tests.cs:26` 的注释记录过变异 M-A9（换成 v3）"全套共红 39"。

按失败原因分组（文件:行 = 断言位置）：

**A. 显式断言"缺省 = v5"（9 条）**
- `SimulationHarness/各入口按地图标识选图Tests.cs:27` 缺省地图不变
- `MapDefinition/两人基准地图Tests.cs:156`（`DefaultId == BuiltinIds[0]`）
- `MapDefinition/边疆档基准地图Tests.cs:245`
- `MapSelection/选图视图模型Tests.cs:79`
- `MapDefinition/地图文件往返Tests.cs:94`（`new RunConfig().MapId`）
- `MapGeneration/棋盘档生成图标识Tests.cs:206`
- `AiDecision/默认评价权重的校准Tests.cs:198`（校准后截断率达标：断言首部地图 = v5）
- `MatchTelemetry/对局日志的记录内容Tests.cs:37`
- `SimulationHarness/地图子命令Tests.cs:49`（期望"地图 siege-4p-base-v5  13×13"）

**B. 终端版文案 / 脚本坐标（4 条）**
- `SimulationHarness/终端对局Tests.cs:30`、`终端专家对局Tests.cs:30`（脚本里的 `B1B` 落点是 v5 坐标）
- `SimulationHarness/终端活形与禁入标示Tests.cs:134`
- `SimulationHarness/各入口按地图标识选图Tests.cs:151`（显式 v5 现在会多打一行"地图 …"，`PlayCommand.cs:91`）

**C. 黄金值 / "与改动前逐步相同"钉在缺省图的跑局上（约 20 条）**
- `MatchSetup/原型插旗替代路径Tests.cs:109` ×6（首回合顺序）
- `MatchSetup/计分规则版本Tests.cs:101,133`、`MatchSetup/对局内容集Tests.cs:83`、`MatchSetup/带入带出配置Tests.cs:91,106`、`SimulationHarness/批量跑局Tests.cs:253`、`SimulationHarness/批量跑局的计分规则版本Tests.cs:63`（同一哈希 `35E25329…`）
- `AiDecision/停手阈值Tests.cs:151`
- `AiDecision/专家难度的一层前瞻Tests.cs:243` ×3、`:286` ×3（期望 `CandidateCellLimit = 0`，实际 K = 24）
- `SimulationHarness/日志首部区数Tests.cs:40,59` + 真实跑局一条（期望 `"ZoneCount":4`；**4 人棋盘图有 5 个出生区**）

**D. K = 24 自动启用改变 `config.json`（6 条）**
- `AiDecision/候选格上限Tests.cs:109,415,538`
- `AiDecision/默认评价权重的校准Tests.cs:185`、`SimulationHarness/可复现回放Tests.cs:133`、`SimulationHarness/批量跑局Tests.cs:50`

**E. 真实跑局样本覆盖不到某类事件（约 9 条）**
- `MatchTelemetry/地形改造日志与分析Tests.cs:58`（**棋盘图全图 h=0 草地、无水无林，样本里一次改造都没有**）
- `MatchTelemetry/活形记录与统计Tests.cs:161`（没有破坏活形）
- `MatchTelemetry/各棋子势力占比Tests.cs:92`
- `MatchTelemetry/阵型的记录Tests.cs:73`、`MatchSetup/计分规则版本Tests.cs:89,150,295`（没出阵型棋串）
- `MatchTelemetry/专家前瞻的记录Tests.cs:108`
- `MatchTelemetry/冲突占用率口径Tests`、`SimulationHarness/日志首部地图摘要Tests`（`Assert.All` 4/4 不过，未逐条展开）

另有约 40 个测试文件直接 `FourPlayerBaseMap.Create()` / `Resolve("siege-4p-base-v5")`（如 `地图静态校验规则Tests` 22 处、`原型插旗替代路径Tests` 13 处）：缺省切换不影响它们，属第三个 change 的改写范围。

**Godot 读数**：`--auto-demo` "v5 53 帧 / board:1 53 帧"、`--pick-check` "v5 105/105"是手工基线，`art/formation-tiers/README.md:50` 写明"全仓没有测试或自检钉 53 帧"。缺省换图后无人值守命令的默认图从 13×13 / 105 格变成大图，这些读数要重取。

---

## 3. 图形版选图界面

### 3.1 现状

- 选项（`MapSelectModel.cs` 构造函数）：`[棋盘图(Board), 目录 BuiltinMaps 四项(Builtin), 随机图（边疆档）(Random)]`；预选棋盘图（index 0），种子由入口注入。
- 状态 `State(Index, Seed, Platforms, NewSurfaces, Boards)`——**没有人数字段**。
- 棋盘数：`CanDecreaseBoards / CanIncreaseBoards / AdjustBoards` 用 4 人常量 `BoardMapParameters.MinBoards/MaxBoards`（7–10），`BoardMapParameters.cs:26-33` 注释"选图界面在第二个 change 之前只出 4 人图，读的是这三个常量"。
- `IdOf`：`BoardMapId.Format(seed, new BoardMapParameters { BoardCount = state.Boards })`——恒 4 人。
- `TrySelectId("board:6:p3")` 能解析出 3 人参数，但只取 `Seed` 与 `BoardCount`，人数丢掉，`CurrentId` 会变成 4 人标识（`--map-select --map=board:x:p3` 预选得到的是另一张图）。
- **人数不在界面上选**：对局人数 = 地图 `MaxPlayers`（`GameRoot.MapSelect.cs:63,293`），选了 2 人图就开 2 人局。棋盘图的人数就是标识里的 `:p` 段（`BoardMapGenerator.cs` `MaxPlayers = players`）。
- 面板（`Hud.MapSelect.cs:133-241`）：选项按钮列表 → 种子输入 / 生成 / 换一张 → 平台数行 → "棋盘数"行（`:194-209`）→ 完整标识 → 难度 → 开始。
- 一行说明 `MapPreviewInfo.Of`（`MapPreviewInfo.cs`）已能显示棋盘数与出生 / 公共块数，与人数无关。
- 自检 `SelfCheckMapSelect`（`GameRoot.MapSelect.cs:138-262`）：随机图 → 换一张 → 平台数 → 非法 / 合法种子 → 棋盘图 → 换一张 → 棋盘数（`:228` 用 4 人 `DefaultBoards` 判 `:n` 段）→ 逐个内置图（`:233-238` 断言内置图 `Boards.IsEmpty`）→ 回到进入项 → 开始。

### 3.2 改成"内置棋盘图若干 + 随机棋盘图，人数 2–4 可选"要动的地方

| 文件 | 内容 |
|---|---|
| `MapCatalog.cs` `Builtins` | 加内置棋盘图（写法见 §1.3）；是否保留 v5 / 2p / 3p / 边疆 四项在界面上（PRD：第三个 change 才删） |
| `MapSelectModel.cs` | `State` 加 `Players`；`IdOf` 带人数；`Can*Boards` / `AdjustBoards` 改用 `MinBoardsFor / MaxBoardsFor(players)`；新增 `AdjustPlayers` / `CanIncreasePlayers` 等；改人数时棋盘数怎么处理（重置为 `DefaultBoardsFor` 还是夹取）；`TrySelectId` 带上人数；"随机图（边疆档）"项是否保留 |
| `Hud.MapSelect.cs` | 加"人数"行与事件（`MapPlayersAdjusted`），`ShowMapSelect` 刷新禁用状态 |
| `GameRoot.MapSelect.cs` | `ConnectMapSelect` 接新事件；`SelfCheckMapSelect` 加人数步、修 `:228` 的 4 人常量、修 `:233-238` 内置图 `Boards.IsEmpty` 断言 |
| `BoardMapParameters.cs:26-33` | 三个 4 人常量与注释（可删或改注释） |
| 测试 | `选图视图模型Tests`（`:62-79` 选项顺序与缺省断言等）、`选图界面守门Tests`、`各入口按地图标识选图Tests` 的两人 / 三人图可选（`:216,237`）、`选图界面难度选择Tests` |
| 规格 | `openspec/specs/map-selection/spec.md` |

### 3.3 `map-selection` 规格现行条目（`openspec/specs/map-selection/spec.md`）

- `:8` 未给地图选项先进选图；给了即跳过。
- `:10` `--map-select` 例外；`--auto-demo` 自检步骤清单（含"选中棋盘图、换一张、调棋盘数、逐个内置图"）。
- `:12` **清单写死**：棋盘图、`siege-4p-base-v5`、`siege-2p-base-v1`、`siege-3p-base-v1`、`siege-frontier-v2`、随机图（边疆档）；预选棋盘图；参赛人数 = 地图人数上限；显示名登记在目录。
- `:20-22` 棋盘图：种子输入、换一张、**棋盘数 7–10**。
- `:26` **"批量与终端入口在未给地图选项时的缺省地图 MUST 仍是 `siege-4p-base-v5`"**。
- Scenario `:28-30` 缺省进入选图（列表另有标准图等）、`:56-58` 2 人与 3 人图可选、`:60-62` 调棋盘数 7→9、`:64-66` **批量入口缺省不变（v5）**。

其他写死 v5 缺省的规格：`simulation-harness/spec.md:124,130-132`（各入口按地图标识选图 + Scenario 缺省地图不变）、`map-definition/spec.md:224,250-252`（边疆图"不是缺省地图"写明缺省 v5）。

---

## 4. AI 权重 / K 与地图

- 地图专属权重：`EvaluationWeights.cs:139-142` 只登记 `"siege-2p-base-v1" → Default with { Eye = 50 }`；`MapOverrideCalibrationOf`（`:154-158`）同键。三个入口经 `ForMapId(map.Id, …)` 取值：`RunConfig.cs:346`、`PlayCommand.cs:124`、`src/godot/scripts/MatchSession.cs:39`。
- 规格 `ai-decision/spec.md:684`："只给 `siege-2p-base-v1` 登记；3p 与 v5 MUST NOT 登记；其他地图未登记时取默认表"。内置棋盘图**不登记 = 符合现行规格**，走缺省表；2 人内置棋盘图拿不到 Eye 50。
- 未登记地图的首部 / `config.json` 与引入覆盖前逐字节相同（`各入口的地图专属AI权重Tests.未登记的地图首部逐字节不变`，本机该组已在基线失败名单里）。
- PRD 第 6 条要求"AI 权重先标'未在新地图上校准'"：现有标注常量有 `EvaluationWeights.FormationScoringStatus`、`NotSweptStatus`、`ScoringExtendedStatus`，没有"地图"维度的标注；`AiDifficulty.cs:80-106` 停手阈值口径、`EvaluationWeights.cs:83,167-169` 九维口径都写着 v5。
- 候选格上限：`AiDifficulty.cs:148` `DefaultCellLimitFor(playable) => playable > 150 ? 24 : 0`；入口 `Siege.Sim/Running/MatchSession.cs:63`、`RunConfig.cs:330`、`AiSearchConfig.ForMap`（`:155`）。实测种子 1–1000 的棋盘图可落子格最小值：4 人 344、3 人 316、2 人 271（目标带下限 2 人恰为 150，`>150` 才启用，理论上 2 人恰 150 格的图会取 0，1000 个种子里没出现）→ **所有棋盘图都自动 K = 24**，生效值写进 `config.json` 与首部（这就是 §2.2-D 那 6 条红的原因），AI 走预筛分支。规格 `ai-decision/spec.md:219-221` "标准图零变化"Scenario 写的是 v5（105 格）。

---

## 5. Godot 通道染色死代码清单

| 文件:行 | 内容 |
|---|---|
| `src/godot/scripts/Visuals.cs:105-106` | `CorridorPath = TileRoad`（注释"通道（board-map D10）"） |
| `src/godot/scripts/BoardView.cs:190` | 注释"'可落子且不属于任何棋盘'即通道（D2 的定义）" |
| `src/godot/scripts/BoardView.cs:193` | `bool OnPlate(Coord c)`——只被 `:280` 用 |
| `src/godot/scripts/BoardView.cs:280-283` | `if (plated && playable && !OnPlate(cell.Coord)) color = Visuals.CorridorPath;` |
| `BoardView.cs:192,364,380` | `plated` 另有两处用途（场景格装饰、台面边框），不是死代码 |

- 测试：没有测试引用 `CorridorPath` / `OnPlate`。`tests/.../四邻接Tests.cs:133`、`生成图布局规则Tests.cs:195` 里的 "Corridor" 是边疆档 / 校验器历史注释，与 Godot 无关。
- 规格 `visual-style-baseline/spec.md`：`:183` "通道 SHALL 呈现为窄路，外观与棋盘台面不同"；`:185` "棋盘与通道之外的场景格"；`:285` "场景格（棋盘档地图中棋盘与通道之外的格）"。`map-generation/spec.md:189` 已写明"棋盘之间 MUST NOT 有通道"。
- 归档的 `board-isolated-gen` 已登记留给本 change：`proposal.md:33`、`design.md:15,72`。
- `boundaries.md:43` 那一行的标识写法还是 `board:<种子>[:n<棋盘数>]`（缺 `:p`），属第一个 change 遗留的文字。

---

## 6. 其他与缺省地图耦合的地方

| 文件:行 | 内容 |
|---|---|
| `HANDOFF.md:16` | "四张内置图（标准 13×13、双人 9×9、三人 11×11、边疆 25×30）或随机图" |
| `HANDOFF.md:33` | `run … --map siege-4p-base-v5` 示例 |
| `HANDOFF.md:40` | 地图标识清单"`siege-4p-base-v5`（缺省）" |
| `HANDOFF.md:55,61-62,102-103,117` | 2 人图覆盖、权威来源、v5 描述、`pass-20` 基线（v5）、专家扩样（v5） |
| `HANDOFF.md:158` | 拾取自检命令（`[--map=<标识>]` 可选 → 缺省图） |
| `README.md:65,84,117` | `--map=siege-4p-base-v5` 示例；"`siege-4p-base-v5`（缺省，13×13）"；自检命令 |
| `docs/index.html:166` | hero 图 alt "标准 13×13 地图上的四方对局" |
| `docs/index.html:223-224` | board-map.jpg 说明"由土路通道连起来 / 由通道相连"（第一个 change 后已过时） |
| `docs/index.html:260` | "四张内置图（标准 13×13、双人 9×9、三人 11×11、边疆 25×30）或随机图" |
| `docs/index.html:287` | `--map=board:1` 示例 |
| `art/hud-panels/shots.sh:34,37,40`、`art/board-labels/shots.sh:40-55`、`art/hud-theme/shots.sh:30` | 显式 `--map=siege-4p-base-v5`（不依赖缺省；第三个 change 删 v5 时才坏） |
| `art/hud-theme/shots.sh:31` | `--map-select` 截选图界面（选项清单变了，截图会变） |
| `art/board-isolated-gen/shots.sh:5,38-46` | 用 `board:6` / `:p3` / `:p2` 拍图 |
| `tools/export-release.sh` | 不涉及地图（已全文检查） |
| `.trellis/spec/core/boundaries.md:27` | "缺省恒为 `siege-4p-base-v5`" |
| `.trellis/spec/core/testing.md:116` | 拿 v5 当相机不能移动的例子 |
| 设计文档 `2026-09-10-siege-core-gameplay-design-v1.md` | 37 处 v5（多为校准口径），`:799` 地图专属权重的登记范围 |

---

## 7. 挑选内置种子的依据（实测）

脚本：`scratchpad/probe`（1–50 逐行表）、`probe2`（1–1000 汇总）、`probe3`（形状众数 / 最好 p 块极差 / 中心格）。全部在缺省棋盘数下（4 人 n7、3 人 n6、2 人 n5），1–1000 无一生成失败，且**全部首次尝试即成（attempt 0）**。

### 7.1 生成器机制决定的事实

`BoardMapLayout.TrySampleSizes`（`BoardMapLayout.cs:114-175`）：出生棋盘宽、高**各自独立均匀**取 5–7（9 种形状），主战场 11–15，其余公共 7–15；只对总面积和"主战场最大"做拒绝重抽。所以"出生棋盘全部同尺寸"的概率约 (1/9)^人数（4 人 5 块 ≈ 1/6561）。出生棋盘多是长方形，且比人数多一块（人数 + 1）。

### 7.2 分布（种子 1–50）

| 指标 | 4 人 | 3 人 | 2 人 |
|---|---|---|---|
| 可落子格 min / p25 / 中位 / p75 / max | 393 / 445 / 473 / 512 / 589 | 343 / 399 / 426 / 473 / 564 | 301 / 344 / 363 / 385 / 400 |
| 主战场面积 min / 中位 / max | 132 / 168 / 210 | 121 / 180 / 225 | 121 / 165 / 225 |
| 出生棋盘面积极差 min / 中位 / max | 5 / 17 / 24 | 5 / 13 / 24 | 0 / 12 / 24 |
| **出生棋盘全部同宽高** | **0 / 50** | **0 / 50** | **1 / 50**（种子 5：5×7 ×3） |
| 全部同形（允许转 90°） | 0 | 0 | 2（5、30） |
| 极差 ≤ 5 | 4（14、15、24、34） | 7（6、27、29、42、44、48、50） | 8 |
| 至少"人数"块同形（允许转向） | 2（15、34） | 5（9、18、27、29、48） | 24 |
| 地图外接尺寸 | 33–49 列 × 30–48 行 | 28–49 × 29–49 | 26–47 × 27–47 |
| 中心格可落子 | 37/50 | 32/50 | 32/50 |
| 信物格 | 9–11 | 8–10 | 7–9 |
| 公共棋盘块数 | 恒 2（n7） | 恒 2（n6） | 恒 2（n5） |

### 7.3 种子 1–1000

| | 4 人 | 3 人 | 2 人 |
|---|---|---|---|
| 可落子格 min / 中位 / max | 344 / 468 / 661 | 316 / 432 / 586 | 271 / 365 / 400 |
| 出生棋盘全部同宽高 | **0** | **0** | 15（5、64、76、305、398、412、448、536、627、643、670、799、922、950、961） |
| 全部同形（允许转向） | 1（652：7×6 / 6×7） | 6（151、233、479、685、847、901） | 45 |
| 极差 ≤ 5 | 31 | 73 | 175 |

### 7.4 1–50 内看起来合适的候选（只按客观指标，未看图）

- **4 人**：15（出生 5×7 ×4 + 6×5，公共 13×11 + 8×13，417 格，36×43）；34（出生 5×7、6×5、5×6、6×5、5×6：4 块面积 30 + 一块 35，公共 15×11 + 7×12，404 格）；14、24（极差 5）。`board:1`（447 格，极差 6）与 `board:6`（493 格，主战场 15×14，极差 19）是负责人已看过图的两张。
- **3 人**：48（出生 6×5 + 7×5 ×3，公共 12×14 + 12×8，399 格）；29（5×6、7×5、7×5、5×7，主战场 15×14 占 52%，401 格）；9、18、27。
- **2 人**：5（出生 5×7 ×3 全同，公共 11×12 + 10×7，307 格，偏小）；30（7×6、6×7、7×6，公共 12×15 + 7×10，376 格）。

可写进 PRD 的客观指标：出生棋盘面积极差（或"人数块同形"）、主战场面积及其占比、可落子格落在目标带中段、外接尺寸（相机 / 拾取负担）、中心格可落子（揭示预览）、信物格数。

---

## 第二个 change：必须改的清单

1. `MapCatalog.Builtins` 登记内置棋盘图，2 / 3 / 4 人各至少一张（写法待裁决，§1.3），附显示名。
2. `MapCatalog.DefaultId` 改为 4 人内置棋盘图；同步 `FourPlayerBaseMap.cs:29` 注释、`RunConfig.cs:92`（随常量自动变）、`Program.cs:261` 的导出分支（写法 B 必须改，否则会写出 `maps/board:<种子>.json`）。
3. `PlayCommand.cs:91` 的"非缺省才打印地图行"重新确认（显式 v5 现在会打印）。
4. 图形版：`GameRoot.cs:107,315` 注释；无人值守缺省图随之变化（`--auto-demo` / `--pick-check` / `--screenshot` / `--reveal-preview` / `--carry-preview`）。
5. 选图界面：`MapSelectModel`（人数字段、按人数的棋盘数范围、`IdOf` / `TrySelectId` 带人数、内置棋盘图选项）、`Hud.MapSelect`（人数行）、`GameRoot.MapSelect`（事件接线、自检步骤，修 `:228` 与 `:233-238`）。
6. Godot 通道死代码：`Visuals.cs:105-106`、`BoardView.cs:190,193,280-283`；规格 `visual-style-baseline` `:183,185,285` 的通道字样。
7. 规格增量：`map-selection`（`:10,12,20-22,26` 与 Scenario `:28,56,60,64`）、`simulation-harness`（`:124,130-132`）、`map-definition`（`:224,250-252`）、`ai-decision`（`:684` 是否提内置棋盘图；`:219-221` 标准图 K = 0 的 Scenario 不受缺省影响，但若引用"缺省图"要看）。
8. 测试：§2.2 的 52 条（A 9、B 4、C 约 20、D 6、E 约 9）逐条处理：改断言 / 显式钉回 `siege-4p-base-v5`（PRD 第 9 条说第三个 change 才把规则测试改写到新图）/ 重定黄金值。
9. `.trellis/spec/core/boundaries.md:27`（缺省恒为 v5）与 `:43`（`board:` 写法缺 `:p`）。
10. 文档：`HANDOFF.md:16,33,40,158`、`README.md:65,84,117`、`docs/index.html:223-224,260`。

## 可选项

- 揭示预览（`GameRoot.Reveal.cs:103-113`）在棋盘图上把示例棋串挪到某块棋盘上（中心格有约 1/4 的种子落在场景格）。
- 内置棋盘图登记 AI 权重覆盖（规格现行写"不登记"，PRD 说校准另开 change）。
- "地图未在棋盘图上校准"的标注常量（PRD 第 6 条的"先标未校准"可以在本 change 落，也可以在校准 change 落）。
- `BoardMapParameters.MinBoards / MaxBoards / DefaultBoards` 三个 4 人常量：选图界面改用 `*For(players)` 后可删。
- `docs/index.html:166` hero 图（v5 截图）换成棋盘图截图。
- 加一条"内置棋盘图摘要黄金值"守门（写法 A / B 都用得上）。

## 需要负责人裁决的问题（附建议）

1. **内置图写法**：A 别名 / B 直接 `board:<种子>` / C 固化 json。
   建议 **A（别名，如 `siege-4p-board-v1`）+ 摘要黄金值守门**：沿用 D6 的"内容变了标识升号"，日志 / 存档里的标识一眼看得出是内置图；`map` 子命令导出文件名合法；与将来生成器改版脱开时也有升号这条退路。C 更稳但多一份要维护的 json 与嵌入资源，等生成器真改版时再固化也来得及。
2. **出生棋盘尺寸不一致**：现生成器下"出生棋盘全部同尺寸"在 4 / 3 人种子 1–1000 里是 0 个（2 人 15 个）。可选：(a) 接受，挑"极差小 / 人数块同形"的种子；(b) 在生成器里让同一张图的出生棋盘同尺寸（这会改第一个 change 的生成器与黄金值）。
   建议 **(a)**，本 change 不改生成器；把"出生棋盘统一尺寸"记作第三个 change 前的独立议题。
3. **具体种子**：建议候选 4 人 `15`（或负责人看过的 `6` / `1`）、3 人 `48` / `29`、2 人 `30` / `5`，出图后由负责人定。
4. **选图界面保留哪些项**：第三个 change 才删旧图，本 change 里 v5 / 2p / 3p / 边疆 / 随机图（边疆档）是否还留在界面上？
   建议 **界面只留"内置棋盘图 + 随机棋盘图"**，旧图仍可用 `--map=` 开（目录照登记），这样界面不用再改一次；但规格 `map-selection` 的 Scenario"2 人与 3 人图可选"要改成"2 / 3 人棋盘图"。
5. **人数控件形式**：(a) 随机棋盘图项上加"人数 2–4"调节，内置棋盘图按人数各列一项；(b) 人数作为全局选择，内置项按人数过滤。
   建议 **(a)**，与"棋盘数"调节同构；改人数时棋盘数重置为该人数的缺省值（避免越界夹取，现有约定是"越界报错不夹取"）。
6. **测试迁移的口径**：§2.2 的 52 条是在本 change 里显式钉回 `siege-4p-base-v5`（行为不变、只改"缺省"断言），还是直接改写到新内置图并重定黄金值？
   建议 **钉回 v5**：PRD 第 9 条把规则测试改写放在第三个 change；本 change 只动"缺省是谁"的断言与入口文案测试，黄金值不重定。另外，若测试走新缺省图，全量用时从约 1 分钟涨到约 19 分钟（本机），也是钉回的理由。
7. **2 人内置棋盘图的 AI 权重**：建议**不登记**、走缺省表（符合 `ai-decision/spec.md:684` 与 PRD 第 6 条），在口径注释里标"棋盘图上未校准"。
8. **终端版地图行**：`PlayCommand.cs:91` 现在只在非缺省图时打印"地图 …"。建议**一律打印**（棋盘图标识带种子，打印出来便于复现），相关两条终端测试随之改。

## Caveats / Not Found

- §2.2 的分组 C / E 是按断言信息归类的，E 组里 `冲突占用率口径Tests`、`日志首部地图摘要Tests` 的具体不过原因没有展开。
- 变异实验用的是 `board:1`（447 格），没换成候选内置种子；换种子不会改变红的条目集合，但 E 组"样本里没触发某事件"的那几条可能因种子不同有出入。
- Godot 侧没有实际运行（没有启动引擎）；关于无人值守读数、揭示预览中心格的结论来自源码与生成器数据。
- scratchpad 拷贝没带 `art/`，所以基线多出 2 条 `视觉方向基准Tests` 失败，与本调研无关。
