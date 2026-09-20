# 09-19-frontier-map 实施记录

每段追加：改了什么 / 既有测试改写逐条 / 变异验证逐条 / 待决。

## 段 A（2026-09-19）——规格档与校验分流、选图入口、AI 选区

范围：`tasks.md` 1.1–1.7、2.1–2.4。结果：`dotnet test -c Release` **970 项全绿**（既有 897 + 新增 73）、`dotnet build -c Release --no-incremental` 零警告；`dotnet build src/godot/Siege.Godot.csproj` 零警告零错误（本机有 Godot.NET.Sdk 4.7.2，可命令行编译）。图形版只做了编译确认，没有起引擎实跑 `--map=`。

### 改动文件

| 文件 | 改了什么 |
|---|---|
| `src/Siege.Core/Board/MapProfile.cs`（新） | 规格档枚举 `Standard = 0` / `Frontier` |
| `src/Siege.Core/Board/MapData.cs` | `Profile` 属性，缺省 `Standard`；`MaxPlayers` 注释改为两档口径 |
| `src/Siege.Core/Board/MapFile.cs` | DTO 加可空 `Profile`（`WhenWritingNull`）：**标准档不写出**，四份既有 `maps/*.json` 逐字节不变；缺字段读成标准档；未定义的数字值抛指名 `FormatException` |
| `src/Siege.Core/Board/MapValidator.cs` | 预算表改成**规格档声明表** `Rules: MapProfile → ProfileRules(人数预算, ZonesMustExceedPlayers, DistanceHandling)`；`.Profile` 全文件只在 `RulesOf` 读一次；`MapValidationFailure.Severity`（`Reject` 缺省 / `Report`）；`MapValidationResult.Failures` 仍只含拒绝项、新增 `Reports`，`ToString` 在有报告项时追加一段；距离规则拆成"不可达一律拒绝"+"极差按 `RejectOnImbalance` / `AlwaysReport`"；新增拒绝码 `BIRTH_ZONE_COUNT_OUT_OF_RANGE`、`BIRTH_ZONE_COUNT_NOT_ABOVE_PLAYERS`、`MAP_TOO_WIDE`（> 25 列，报出后提前返回）、`MAP_PROFILE_UNKNOWN`，报告码 `BIRTH_ZONE_DISTANCE_REPORT` |
| `src/Siege.Core/Board/Maps/MapCatalog.cs`（新） | "标识 → 地图"唯一解析：`DefaultId`、`BuiltinIds`、`Resolve(string?)`；内置表一行一张图（段 B 在此加 frontier-v1）；未知标识抛 `FileNotFoundException` 并列出可用标识 |
| `src/Siege.Core/Board/Maps/FourPlayerBaseMap.cs` | 标识提成常量 `Id` |
| `src/Siege.Sim/Config/MapCatalog.cs`（删） | 上移到 Core |
| `src/Siege.Sim/Config/RunConfig.cs` | `MapId` 缺省取 `MapCatalog.DefaultId` |
| `src/Siege.Core/Determinism/GameSeed.cs` | 新子流名常量 `ZonePick = "zone-pick"` |
| `src/Siege.Core/Match/PrototypeZoneAssignment.cs`（新） | AI 选区唯一实现：区数 ≤ 地图人数上限 → 顺排（跳过人选）；否则 `zone-pick` 子流在升序空闲表里逐人均匀抽取、互不同区 |
| `src/Siege.Core/Match/MatchFlow.cs` | `PlantPrototype((PlayerId, int)? manual)`：选区 + 依次插旗 + 锁定，返回各人选择 |
| `src/Siege.Sim/Running/MatchSession.cs`、`BatchRunner.cs` | 改用 Core 的 `MapCatalog` 与 `PlantPrototype()`；`ExecuteToDirectory` **先解析地图再建输出目录**（未知地图不再留下半份输出） |
| `src/Siege.Sim/Play/PlayCommand.cs` | `Run(..., MapData? map = null)`；选区改 `PlantPrototype((me, zone))`；非缺省地图时多打一行地图信息（缺省地图转录不变） |
| `src/Siege.Sim/Program.cs` | `play --map <id或文件>`（登记进严格解析，开局前解析）；`map` 子命令的"出生区 1/2/3/4"与图例"1-4"改为按区数生成 |
| `src/Siege.Sim/Play/BoardRenderer.cs` | 区号底色改中性色常量 `ZoneColor`，不再 `ColorOf(new PlayerId(z))`；`ColorOf` 改 internal 供测试 |
| `src/Siege.Sim/Analysis/BalanceAnalyzer.cs` | 各区胜率基线由 `1/Max(区数, 人数)` 改为 `1/人数`（见"偏离与补充"3） |
| `src/godot/scripts/GameRoot.cs` | 解析 `--map=`；经 `MapCatalog.Resolve`；解析 / 校验失败在建局前 `GD.PrintErr` + `Quit(1)`，不回落缺省；启动行打印地图标识 |
| `src/godot/scripts/MatchSession.cs` | `Create(MapData map, ...)`；`ChooseZone` 改 `Match.PlantPrototype((Me, zone))` |
| 测试（新） | `FrontierFixtures.cs`（合法 6 平台边疆图 360/6/16/16，不对称）、`MapDefinition/地图规格档Tests.cs`、`MapDefinition/边疆档静态校验Tests.cs`、`MapDefinition/两位数行号贯通Tests.cs`、`SimulationHarness/各入口按地图标识选图Tests.cs`、`SimulationHarness/出生区编号显示Tests.cs` |
| 测试（追加） | `MapDefinition/人数适配预算Tests.cs`（边疆档 10 个方法）、`BoardTopology/坐标记法Tests.cs`（2 个）、`MatchSetup/原型插旗替代路径Tests.cs`（8 个） |

### 既有测试改写逐条

没有改任何既有测试的期望值或断言。唯一触及既有测试类的改动：

1. `SimulationHarness/批量跑局Tests.cs`：类上加 `[Collection("控制台重定向")]`。原因：新增的 `各入口按地图标识选图Tests` 同样重定向进程级的 `Console.Error`，两个类并行会互相收走对方的输出；放进同一个 xunit 集合串行。测试体未动。

### 改动前后对照（一次性，不进测试）

改任何 `src/` 之前用原代码抓了一份：v4 上种子 1 / 42 / 20260919 × 2 / 3 / 4 人共 9 局批量跑局的 `DeterministicText()` 全文、选区、首回合顺序、信物分布，加 5 份终端版转录（不同座位 / 人数 / 选区）。改完后同样再抓一份，`diff -r` **15 个文件全部相同**。其中选区、首回合顺序、信物分布已写成字面量钉进 `原型插旗替代路径Tests`（黄金值取自提交 a572877 的实际运行，不是用新实现算的；其中"人选最后一个区"那一行是按旧循环手算的，已在该行注明）。

口径说明：5 份转录的脚本写成了连续 `pass`（征募提示不认它），所以每份只覆盖到本机玩家第一次征募提示为止（428–472 行，含此前全部 AI 小回合）；完整对局的逐步一致由 9 份批量日志保证。抓取用的临时测试已删除。

### 变异验证逐条

脚本在会话 scratchpad（不入库）：二进制读写、按文件探测行尾、锚点命中恰 1 次、`finally` 还原并与原字节比对、带时间戳备份、`DOTNET_CLI_UI_LANGUAGE=en`、解析 `Failed!/Passed!` 统计行、附加条件一律运行时恒假（不用 `if (false)`）。每条都跑全量 970 项；基线 970 绿。25 条**全部红、全部有统计行（无编译失败假红）**，跑完后全量复绿，改动文件无混用行尾。

| 编号 | 改了哪里 → 改成什么 | 红 | 红的测试 |
|---|---|---:|---|
| M-A1 | `MapFile.ToJson` 的 `Profile` 恒写 `null` | 3 | 规格档读写往返、未定义的规格档数字被指名报出、其余标识按文件路径读入 |
| M-A2 | `MapFile.FromJson` 不读 `Profile` | 3 | 同上 |
| M-A3 | 声明表边疆档可落子 300–420 → 95–110 | 14 | 边疆档按自己的区间校验、上 / 下界端点，及一切用合法边疆夹具建局的测试 |
| M-A4 | 边疆档距离处理 `AlwaysReport` → `RejectOnImbalance` | 12 | 边疆档距离只报告 等 |
| M-A5 | `AlwaysReport` 分支加"极差超容差才报" | 1 | 均衡的边疆图同样给出距离报告 |
| M-A6 | 不可达的拒绝只在 `RejectOnImbalance` 下生效 | 3 | 边疆档不可达仍拒绝（另 2 条因空距离解引用抛异常而红） |
| M-A7a | `ValidateBudgets` 里加 `if (map.Profile == MapProfile.Frontier && 恒假) return;` | 1 | 校验器对规格档的分支只在声明表里 |
| M-A7b | `ValidateDistanceBalance` 里加 `if (map.Profile != 0 && 恒假) return;`（不写枚举字面量） | 1 | 同上（靠"`.Profile` 只读一次"判据） |
| M-A7c | `ValidateSites` 里加 `if (map.Profile != MapProfile.Standard && 恒假) return;` | 1 | 同上 |
| M-A8 | `MatchFlow.PlantPrototype` 里注入读 `MapProfile.Frontier` 的分支 | 1 | 规格档只被地图数据文件格式与校验器引用 |
| M-A9 | `MapCatalog.DefaultId` → `"siege-4p-base-v3"` | 39 | 缺省地图不变 + 所有走缺省地图的跑局 / 终端测试 |
| M-A10 | `Resolve` 找不到时返回内置图 | 2 | 未知标识报错、终端版未登记的地图选项拼写被拒绝 |
| M-A11 | `Program.Play` 不读 `map` 选项 | 2 | 同上两条 |
| M-A12 | `src/godot/scripts/MatchSession.cs` 改回 `FourPlayerBaseMap.Create()` | 1 | 三个入口都经同一份目录解析地图（源码扫描；godot 不在 sln 里，只有它能红） |
| M-A13 | 顺排去掉"跳过人选的区" | 4 | 区数等于人数时保持现状、标准图上人工选区后的顺排…（3 行） |
| M-A14 | `PlantPrototype` 多消费一次 `MatchFlow` 的 `setup` 子流实例 | 8 | 批量侧顺排与首回合顺序…（6 行）、选区不扰动其他随机、种子选区不扰动信物与首回合顺序 |
| M-A15 | 种子选区抽中后不从空闲表移除 | 4 | 区数多于人数时种子选区、中立平台、子流契约、选边疆图 |
| M-A16 | `BoardRenderer` 区号底色改回 `ColorOf(new PlayerId(z))` | 1 | 区号底色是中性色不借用玩家色 |
| M-A17 | 分析器基线改回 `1/Max(区数, 人数)` | 1 | 各区胜率段在六区下输出六行 |
| M-A18 | 超宽地图不提前返回 | 1 | 超宽地图被拒绝而不是抛异常 |
| M-A19 | 边疆档 `ZonesMustExceedPlayers` → `false` | 1 | 边疆档出生区数不得等于人数 |
| M-A20 | **只改测试**：信物区间期望文本换成据点的 `12–22` | 2 | 边疆档信物数区间端点（越界的两行）——证明不是测试抄实现 |
| M-A21 | 选区判据 `区数 > 地图人数上限` → `区数 > 参赛人数` | 5 | 批量侧 2 / 3 人行、标准图上 2 / 3 人行 |
| M-A22 | 种子选区改从 `GameSeed.Setup` 同名子流派生 | 1 | 种子选区的子流名与消费方式是可复现契约 |
| M-A23 | 边疆档单区下界 20 → 12 | 2 | 边疆档平台过小、单区下界端点（19 那行） |

已知的守门盲区：区号底色的"中性"只能靠常量不等 + 源码扫描钉住（终端颜色不进 `StringWriter`）；M-A16 红的是源码扫描那条断言。

### 1.6 全仓 grep 结论

`src/` 内除 `FourPlayerBaseMap.Size = 13`（基准图生成器自己的尺寸）外，没有写死 13 / 11 或单字符行号的假设：`Coord.TryParse` 用 `int.TryParse(text[1..])`；`MapFile` 的坐标、栅栏（`Split('-')`）、高度 / 地表行都经 `Coord`；`BoardRenderer` 行标 `{y + 1,3}`；日志落点经 `ToNotation()`。清掉的三处"≤ 4 区"假设：`Program.cs` 的"出生区 1/2/3/4"与图例"1-4"、`BoardRenderer` 的区号借用玩家色。`PlayerColors` 长度 4 对应人数上限 4，不动。`.trellis/spec/core/boundaries.md` 里"出生区编号的对人显示（1–4）"的文字留给 6.2。

### 偏离与补充（请裁决）

1. **选区判据用"地图人数上限"而不是规格字面的"玩家数"。** `match-setup` 增量写的是"出生区数等于玩家数 → 顺排；多于玩家数 → 种子选区"。按字面，v4 上的 2 / 3 人局（区数 4 > 参赛 2 / 3）会从顺排变成种子选区，改变既有对局（变异 M-A21 实测红 5：批量侧 2 / 3 人行与终端版 2 / 3 人行）；与 Goals"标准档行为零变化"冲突。现实现取 `BirthZones.Length > map.MaxPlayers`：标准档恒相等 → 永远顺排；边疆档恒大于 → 永远种子选区；规格的全部 Scenario 两种读法都满足。若负责人要字面读法，改 `PrototypeZoneAssignment.Assign` 一行并重抓 2 / 3 人黄金值。建议同时把规格文字改成"地图支持的最大人数"。
2. **图形版没有严格命令行解析。** `GameRoot` 把 `OS.GetCmdlineUserArgs()` 与引擎自己的 `GetCmdlineArgs()` 合在一起逐项 `StartsWith`，从来没有"未知选项报错"（引擎参数混在里面，做不了白名单）。段 A 只按既有形状加了 `--map=`：未知**地图标识**会报错退出，但拼错的**选项名**（`--mapp=`）在图形版仍被静默忽略，规格"地图选项 MUST 在各入口的严格命令行解析中登记"在图形版只做到"登记"。要不要只对 `GetCmdlineUserArgs()`（`--` 之后那段）做严格解析，请裁决；本段没有为它造框架。
3. **分析器各区胜率基线改为 `1/人数`**（2.4 顺带）。原式 `1/Max(区数, 人数)` 在 6 区 4 人图上取 1/6，公平的 25% 会被系统性判"显著"。v4（4 区 4 人）数值不变，既有 `出生区公平性` 测试未动仍绿。
4. 新增校验码 `MAP_TOO_WIDE`：规格只说"外接宽度 MUST NOT 超过 25 列"，没说由谁拒绝；不加的话 26 列地图会在校验器枚举格子时抛 `ArgumentOutOfRangeException`。
5. 批量侧 `ExecuteToDirectory` 先解析地图再建目录：原来未知地图会留下只有 `config.json` 的输出目录，与 strict-cli D4 的口径不一致，顺手改了。

### 待决 / 留给后续段

- **各区胜率的行数取的是"被选到过的最大区号 + 1"**（`BalanceAnalyzer.BirthZones`）：日志首部没有地图区数，6 区图上若 6 号台整批没人选，报告只有 5 行。修法是首部加 `ZoneCount`（新日志字段，要配往返与旧日志回填测试），段 B 的 3.5（各平台被选次数与胜率）之前需要做；报告目前也不单列"被选次数"（只在 Wilson 的 `(胜/选)` 里）。段 A 的"6 区输出 6 行"测试用的是 6 个区都被选到的合成日志。
- `map` 子命令仍只打印 / 导出 v4（3.4 才要求能打印边疆图）；报告项已随 `MapValidationResult.ToString()` 打印。
- 段 B 收录 `siege-frontier-v1`：在 `MapCatalog.Builtins` 加一行即可；生成器放 `src/Siege.Core/Board/Maps/`（规格档守门的白名单目录）。"`play --map siege-frontier-v1` 提示 1–6"在段 A 用测试内构造的 6 平台图 + 地图文件路径验证，真标识要等段 B。
- 图形版出生区着色（`BoardView` 借用玩家色的部分）属 5.5，本段未动；`--map=` 选到 6 区图时图形版的着色 / 插旗界面尚未适配，也未实跑。
- `.trellis/spec/core/` 的文字更新（声明表约定、`BirthZoneLabel` 的"1–4"、子流表加 `zone-pick`）属 6.2。

### 检查（2026-09-19，trellis-check）

结论：段 A 实现与 design D1 / D2 / D4 / D5 / D9、三份规格增量一致；`git diff tests/` 零删除行（唯一触及既有测试类的是 `批量跑局Tests` 的 `[Collection]`）；`maps/` 无改动；改动文件无单文件内混用行尾；`src/godot` 只引用 Core / Presentation。三处顺带偏离（分析器基线 1/人数、`MAP_TOO_WIDE`、批量侧先解析地图再建目录）合理且都有按值 / 按行为的守门（抽查重跑 M-A17 红 1、M-X1「改回先建目录」红 1）。修完后 `dotnet test -c Release` **973 项全绿**（970 + 3）、`dotnet build -c Release --no-incremental` 零警告、`dotnet build src/godot/Siege.Godot.csproj` 零警告。

发现并已修（全部在测试侧，`src/` 只有两处风格修正）：

1. **规格档声明表守门可被属性模式绕过**（`边疆档静态校验Tests.校验器对规格档的分支只在声明表里`）。旧判据②数的是 `.Profile` 成员访问，`if (map is { Profile: not 0 }) …` 前面没有点、也不写枚举字面量，两条正则都不命中（在变异体上复算确认）。改为数裸标识符 `Profile`（环视只排除类型名 `MapProfile` / `ProfileRules`）；另加④：`RulesOf(` 只出现 2 次、声明行四个字段各只被读一次、`DistanceHandling.Xxx` 表外只 1 次——防止拿某个字段的取值当"是不是边疆档"的代理去给别的规则分流。
2. **"下游不感知规格档"守门只扫类型名 `MapProfile`**（`地图规格档Tests.规格档只被…引用`）。下游写 `(int)Board.BaseMap.Profile == 1` 不含该词，整条漏过。判据改为"`MapProfile` 或裸标识符 `Profile`"；另钉 `MapData.cs` 里裸 `Profile` 只出现一次（属性声明），防止包一层 `IsFrontier` 给下游用。
3. **选区"三处调用点共用一份"没有守门**：图形版不在 sln 里，把 `ChooseZone` 改回手写 `PlantSequentially` 全套 0 红（M-C5-old 实测）。新增 `原型插旗替代路径Tests.三个入口的选区都走内核的唯一实现`（源码扫描 `src/Siege.Sim` + `src/godot/scripts`，含反面命中与样本口径）。
4. 单区 20–225 的**上界没有行为端点**（此前只靠报文文本 "20–225" 钉住）：新增 `边疆档单区上界端点`（225 接受 / 226 拒绝）。
5. `中立平台` 只断言了 Scenario 前半句；补后半句：第 4 大回合起每名玩家的合法范围含两个中立平台全部格子且等于全图可落子格数。
6. 风格：`PlayCommand.cs`、`RunConfig.cs` 的全限定类型名改为 `using`。

检查阶段变异（脚本在 scratchpad：二进制读写、探测行尾、锚点恰 1 次、`finally` 还原并逐字节比对、解析统计行、附加条件运行时恒假；每条跑全量）：

| 编号 | 改了哪里 → 改成什么 | 红 | 红的测试 |
|---|---|---:|---|
| M-C1 | `ValidateSites` 开头加 `if (map is { Profile: not 0 } && 恒假) return;` | 1 | 校验器对规格档的分支只在声明表里 |
| M-C2 | `ValidatePockets` 之前加 `if (rules.ZonesMustExceedPlayers && 恒假) return …;` | 1 | 同上（④） |
| M-C3 | `MatchFlow.PlantPrototype` 加 `if ((int)Board.BaseMap.Profile == 1 && 恒假) return [];` | 1 | 规格档只被地图数据文件格式与校验器引用 |
| M-C4 | `MapData` 加 `public bool IsFrontier => Profile != MapProfile.Standard;` | 1 | 同上 |
| M-C5 | `src/godot/scripts/MatchSession.cs` 的 `ChooseZone` 改回 `Match.PlantSequentially(…)` | 1 | 三个入口的选区都走内核的唯一实现（滤掉该测试重跑：0 红） |
| M-C6 | 声明表单区上界 225 → 224 | 3 | 边疆档单区上界端点 ×2、边疆档平台过小（滤掉新测试重跑：只红后者 1 条，且红的是报文文本） |
| 抽查 | M-A7a / M-A21 / M-A17 / M-A22 重跑 | 1 / 5 / 1 / 1 | 与实现方记录一致 |

未修、留给负责人：

- 偏离 1（选区判据用地图人数上限）：规格已澄清为同一口径，**已对齐**，无需再裁决。
- 偏离 2（图形版没有严格命令行解析，`--mapp=` 静默忽略）：维持原样，待裁决。
- 图形版"不带 `--map=` → v4 / 未知标识报错退出"只有源码扫描与编译确认，没有起引擎实跑（godot 不在 sln，行为测试够不着）。
- `MapCatalog` 的 `maps/<标识>.json` 是相对**当前工作目录**解析（沿用批量侧原写法）；图形版的工作目录未必是仓库根，非内置标识在图形版要给完整路径。段 B 把 frontier-v1 做成内置图后不受影响。
- 声明表守门仍挡不住刻意的引用比较（如 `rules == Rules.Values.Last()`），属刁钻写法，未守。

## 段 B（2026-09-19）——`siege-frontier-v1` 地图、日志首部区数、跑局统计

范围：`tasks.md` 3.1–3.6（3.6 的人工试玩部分留给负责人）。结果：`dotnet test -c Release` **999 项全绿**（段 A 973 + 新增 26）、`dotnet build -c Release --no-incremental` 零警告、`dotnet build src/godot/Siege.Godot.csproj` 零警告零错误。四份既有 `maps/*.json` 字节不变（`git status maps/` 只多出 `siege-frontier-v1.json`）；AI 未改动，v4 对局逐步不变（段 A 的黄金值测试全绿）。

### 地图概况

25 列 × 30 行；可落子 **377**（h0 / h1 / h2 = 105 / 26 / 246；目标约 360，区间 300–420）。平台编号 → 边长 → 方位 → 可落子格（占外接面积）：1 → 9×9 → 西北 → 70（86%）；2 → 8×8 → 东南 → 54（84%）；3 → 7×7 → 东北 → 43（88%）；4 → 6×6 → 西南 → 31（86%）；5 → 5×5 → 中西 → 24（96%）；6 → 5×5 → 中东 → 24（96%）。每平台 2 处缓坡（朝河岸 / 广场一处、朝支巷一处），5×5 朝广场的那处 3 格宽，其余 2 格宽。
据点 16（营帐 6：每平台腹地 1；篝火 6：`L7 P8 T11 E19 L21 P23`；石碑 4：`O13 L14 P16 M17`，风车形，非林地，不与中心相邻）。信物 16（平台内 9：1–3 号台各 2、4–6 号台各 1，全部出生区分区 / 出生区预算；公共 7：桥头 `M8 O11 O19 M22` + 支巷尽头 `E10 W20` 标准档 + 中心 `N15` 高档兼中央入口）。桥 4（`N8 N11` 南段、`N19 N22` 北段），栅栏 4，林地 4，咽喉 = 全部缓坡格 + 四座桥。
到中央入口的气边距离（1–6 号台）：10 / 10 / 11 / 11 / 4 / 4；到最近石碑 7 / 7 / 10 / 10 / 2 / 2；到最近公共信物 3 / 5 / 3 / 3 / 4 / 4；到最近篝火 2 / 2 / 2 / 2 / 3 / 2。

### 改动文件

| 文件 | 改了什么 |
|---|---|
| `src/Siege.Core/Board/Maps/FrontierMapV1.cs`（新） | 边疆图生成器：一张 25×30 的字符画是唯一数据来源（平台格 / 缓坡 / 过渡带 / 林地 / 深水 / 桥 / 岩石 / 营帐 / 篝火 / 石碑 / 信物 / 中央入口），另列平台外接方块表（编号、西南角、边长）与栅栏表；行宽、未知字符、平台格越出方块、缺 / 重复中央入口当场抛出；注释写明"编号 → 边长 → 方位"与布点理由 |
| `src/Siege.Core/Board/Maps/MapCatalog.cs` | `Builtins` 加 `siege-frontier-v1` 一行；`DefaultId` 不变 |
| `maps/siege-frontier-v1.json`（新） | `map --map siege-frontier-v1` 导出 |
| `src/Siege.Sim/Program.cs` | `map [--map <id或文件>]`（登记进严格解析；缺省仍是 v4；只有"按内置标识请求"才导出——判据是请求的标识而不是读到的 `map.Id`，设计师拿 v4 副本改地形后 `map --map 副本.json` 不会覆盖 `maps/` 里的权威文件）；图例里写死的"入口 G7 / 四座桥"改成通用表述 |
| `src/Siege.Sim/Logging/MatchLog.cs` | `LogHeader.ZoneCount`（`int?`，旧日志为 `null`） |
| `src/Siege.Sim/Running/MatchSession.cs` | 首部写入 `ZoneCount = 地图出生区数` |
| `src/Siege.Sim/Analysis/BalanceAnalyzer.cs` | 各区胜率行数 = max(首部区数, 被选到过的最大区号 + 1)（旧日志回填后者）；`ZoneStat.Picks`、`BirthZoneSection.ZoneCount`；`TargetsSection.AiStep`（`AiStepTiming`：小回合墙钟的样本数 / 均值 / 最大值） |
| `src/Siege.Sim/Analysis/ReportWriter.cs` | 第 5 节加"区数 N"行，各区行尾加"；被选 N 次"；§16.4 加"AI 单步决策耗时 均值 / 最大" |
| 测试（新） | `MapDefinition/边疆档基准地图Tests.cs`（11 个方法 / 17 例：尺寸与校验、平台规模、崖壁与缓坡、连通、小平台更靠中央、地形要素、资源布点、六个平台的保护期容量、确定性与磁盘文件一致、已收录但不是缺省、保护期边界第 3 / 4 大回合）、`SimulationHarness/日志首部区数Tests.cs`（5）、`SimulationHarness/地图子命令Tests.cs`（3）、`SimulationHarness/边疆图终端试玩脚本Tests.cs`（1） |
| 测试（改） | `SimFixtures.Synthetic` 加可选参数 `zoneCount`；`TerrainEditing/地形写入口Tests.cs` 白名单加一项（见下） |

### 既有测试改写逐条

1. `TerrainEditing/地形写入口Tests.地形写入口之外不得构造改造后的地形`：允许 `new TerrainData(...)` 的白名单加 `Siege.Core.Board.Maps.FrontierMapV1`，注释同步。原因：白名单的第三类是"基准图定义"，原先只有 v4 一个生成器；边疆图是第二张内置图的生成器，与 v4 同类，必须构造地形。断言逻辑未动。
2. `SimFixtures.Synthetic`：加可选参数 `zoneCount = null`（缺省即旧日志口径），既有调用点不变。

没有改任何既有测试的期望值。另：首稿生成器的列标尺注释写了完整的 `ABCDEFGH…`，被 `坐标记法Tests.映射实现只有一处` 抓住（第二份列字母表）；改成每 5 列一个字母的标尺，守门测试未动。

### 3.6 自动化部分

- `边疆图终端试玩脚本Tests`：`PlayCommand.Run`（种子 42、座位 1、三名 Easy AI、真标识 `siege-frontier-v1`）脚本输入走到第 5 大回合。人选 5 号台，AI 由种子选到 3 / 1 / 4 号台。第 1–3 大回合对 `R13`（中立 6 号台）、`J8`（玩家 4 的 4 号台）、`N15`（中央广场）共 4 次暂放全部被拒（"落点不在当前合法落子范围内"），自家 `E13 E14 E15` 成功；第 4 大回合同样的 `N15 J8 R13` 一批确认（"玩家1(你) 落子 J8B R13B N15B"），进入第 5 大回合后输入耗尽退出。
- `边疆档基准地图Tests.保护期边界在边疆图上照常`：第 3 大回合落他人平台 / 中立平台 / 过渡带 → `OutOfLegalRange`；第 4 大回合同一落点合法。
- **人工试玩未做**，留给负责人：`dotnet run -c Release --project src/Siege.Sim -- play --map siege-frontier-v1`。

### 3.5 跑局统计表

口径：4 名 AI、`siege-frontier-v1`、大回合上限 15、其余全部缺省；`--serial`（并行度 1）。规格 / 任务里的"Normal"= 本仓库的 `Standard`（枚举只有 Easy / Standard / Hard）。"单步"= 一个小回合（控制者在其中整理手牌、征募、并一次性决定整批部署——AI 对外可观察的最小决策单位），取日志既有的 `TurnSnapshot.ElapsedMs`（墙钟，只记录、不进确定性文本、不参与任何决定）；**没有新增计时**。输出在 `sim-out/frontier-std20/`（20 局合并，原始四批在 `frontier-std20-0..3`）、`sim-out/frontier-hard5/`，已被 `.gitignore` 忽略。报告由 `analyze --dir` 产出。

| 批次 | 局数（种子） | 正常终局 | 平均大回合数 | 到上限终局比例 | 单步耗时均值 | 单步耗时最大 | 备注 |
|---|---|---:|---:|---:|---:|---:|---|
| Standard | 20（201–220） | 20 / 20，失败 0 | 15 | **100%**（20 / 20，全部 `MajorRoundLimit`；势力碾压 0） | 2075 ms | 11432 ms | 1200 个小回合；跑局期间机器上没有其他构建 / 测试 |
| Hard（干净复测） | 5（101–105） | 5 / 5 | 15 | 100%（5 / 5） | **1994 ms** | 11401 ms | 300 个小回合；逐局均值 2391 / 1315 / 1615 / 1990 / 2658；跑局期间只写文件 |
| Hard（首测，作废） | 5（101–105） | 5 / 5 | 15 | 100% | 2108 ms | 12984 ms | 跑局期间并发跑了两次全量 `dotnet test` 与数次构建，墙钟被污染；且地图是 3 号台岩石调整之前的版本。只留作对照，不作裁决依据 |

Standard × 20 各平台被选次数与胜率（基线 = 1 / 人数 = 25%；80 个选区样本）：

| 平台 | 边长 / 方位 | 被选次数 | 胜场 | 胜率（Wilson 95%） |
|---|---|---:|---:|---|
| 1 | 9×9 西北 | 13 | 0 | 0.0%（0.0%–22.8%），显著偏低 |
| 2 | 8×8 东南 | 14 | 3 | 21.4%（7.6%–47.6%） |
| 3 | 7×7 东北 | 11 | 2 | 18.2%（5.1%–47.7%） |
| 4 | 6×6 西南 | 15 | 3 | 20.0%（7.0%–45.2%） |
| 5 | 5×5 中西 | 13 | 7 | 53.8%（29.1%–76.8%），显著偏高 |
| 6 | 5×5 中东 | 14 | 5 | 35.7%（16.3%–61.2%） |

其他读数（Standard × 20）：首次提子平均在第 6.05 大回合（分布 4×2 / 5×8 / 6×3 / 7×3 / 8×2 / 9×2），此时盘面占用率平均 17.6%；第 3 大回合领先者最终胜率 40%（8 / 20）。

读法：
- **节奏**：25 局全部打满 15 个大回合靠上限收场，没有一局出现势力碾压或其他终局——design 风险里"15 个大回合在 360 格上打不出结果"成立。本 change 不调参（裁决 6），只报告。
- **平台强弱**：两个 5×5（贴着广场，出门就是石碑）合计 12 / 27 胜，三个大平台（7–9）合计 5 / 38 胜，9×9 的 1 号台 13 次 0 胜。方向与"小平台近、大平台远"的设计一致，但幅度偏大；样本小（每台 11–15 次），只能当趋势看。调图旋钮：石碑离 5×5 缓坡口的距离、5×5 朝广场缓坡的宽度、大平台到广场的步数。
- **耗时**：Hard 干净复测均值 1994 ms，**未超过** 2 秒阈值 → 按裁决 12 不动 AI。但它只低 0.3%，Standard 反而是 2075 ms——两档耗时相近、都贴着 2 秒。尖峰（8–13 秒）逐条核对过：Hard 首测里 `ElapsedMs > 5000` 的 24 个小回合，23 个的落子里有匠人；根因与建议旋钮见"待决 1"。

### 变异验证逐条

脚本在会话 scratchpad（不入库）：二进制读写、锚点命中恰 1 次、`finally` 还原并逐字节比对、`DOTNET_CLI_UI_LANGUAGE=en`、解析 `Failed!/Passed!` 统计行；每条跑全量，基线全绿（M-B1a–M-B13 时 998 项；M-B14 与 M-B11 复跑时 999 项）。15 条**全部红、全部有统计行**；跑完后 `--no-incremental` 重建、全量复绿。

| 编号 | 改了哪里 → 改成什么 | 红 | 红的测试（边疆图 / 本段新增的） |
|---|---|---:|---|
| M-B1a | 字符画把 5 号台挖掉 5 格（24 → 19，低于 80% 与单区下界 20） | 15 | 平台规模、平台四周是崖壁…、外接尺寸与校验通过、保护期容量 ×6、保护期边界 ×2、磁盘文件一致、地图子命令、真实跑局写区数、终端试玩脚本（校验器拒绝整张图，凡建局的都红） |
| M-B1b | 生成器把营帐格的高度写成 1 | 4 | 平台规模、平台四周是崖壁…、磁盘文件一致、地图子命令 |
| M-B2 | 5 号台朝广场的缓坡 `K14–K16` 改成岩石 | 4 | 小平台更靠中央、平台四周是崖壁…、磁盘文件一致、地图子命令 |
| M-B3 | `MapCatalog.DefaultId` → `FrontierMapV1.Id` | 20 | 已收录但不是缺省地图、缺省地图不变、地图子命令（缺省那条）+ 所有走缺省地图的跑局 / 终端 / 遥测测试 |
| M-B4 | `MatchFlow.LegalRangeFor` 的 `<=` → `<` | 4 | 保护期边界（第 3 大回合那一行）、终端试玩脚本、既有的 范围随大回合切换 等 2 条 |
| M-B5 | `BuildHeader` 不写 `ZoneCount` | 2 | 真实跑局把区数写进首部…、旧日志缺区数字段…（它的前置断言要求新日志含该字段） |
| M-B6 | 分析端忽略首部 `ZoneCount` | 1 | 整批没人选的平台仍占一行且被选零次 |
| M-B7 | 单步耗时最大值写成均值 | 1 | 报告给出AI单步耗时的均值与最大值 |
| M-B8 | 生成器把平台内信物标成公共 / 标准档（违反裁决 9） | 14 | 资源布点、外接尺寸与校验通过 + 凡建局的 |
| M-B9 | 字符画西北巷一格草地改林地 | 1 | 生成器确定且与磁盘文件一致（通用漂移守门；与 v4 同样，说不出违反了哪条约束） |
| M-B10 | 报告行去掉"；被选 N 次" | 1 | 整批没人选的平台… |
| M-B11 | `Builtins` 去掉边疆图那一行 | 4 | 已收录但不是缺省地图、地图子命令（按标识打印）、真实跑局把区数写进首部…、终端试玩脚本。首跑只红 1：`地图子命令Tests` 在测试输出目录留下的 `maps/siege-frontier-v1.json` 被目录的文件回落读了回来，替别的测试兜了底；已让该测试类用完即清，复跑红 4 |
| M-B12 | `map` 子命令不读 `--map` | 1 | 按标识打印边疆图的文本图与距离报告项 |
| M-B13 | `ZoneStat.Picks` 误填成胜场数 | 2 | 整批没人选的平台…、区数取首部与被选区号的较大者… |
| M-B14 | `map` 子命令的导出判据改回按读到的 `map.Id` | 1 | 给地图文件路径时只打印不导出 |

### 偏离与补充（请裁决）

1. **AI 没有动**：干净复测的 Hard 单步均值 1994 ms，未超过 2 秒阈值，按裁决 12 不加旋钮——但只差 0.3%，详见上面"耗时"一节与待决 1。
2. **`LogHeader.ZoneCount` 进了确定性文本**（它不是耗时字段）：v4 同种子日志首部多一个字段；拿段 B 之前的旧日志做 `replay --file` 会报首部不一致（历次加首部字段都是这个效果，`SchemaVersion` 仍为 1）。对局逐步不变。
3. **旧日志缺 `ZoneCount` 时分析端回填**为"被选到过的最大区号 + 1"（派发要求）。与 `MatchLog.cs` 里其余"MUST NOT 回填"的字段不同：旧日志全部来自区数 = 人数的标准档图，回填值即真值，字段注释里写明了理由。另加一条防御：首部区数小于实际被选到的区号时取较大者，不静默丢样本。
4. **`map` 子命令对 v4 的可见变化**：图例里"入口 G7 是高档信物 R、四座桥即咽喉 ="改成通用表述；新增 `--map`。v4 的 JSON 导出字节不变。
5. **难度枚举没有 `Normal`**（只有 `Easy / Standard / Hard`）：任务与规格里的 Normal 按 `Standard` 跑。
6. **可落子 377**，略高于目标 360（区间内）。平台内岩石已用到 84%–88%（5×5 为 96%），再压只能缩河岸带或支巷，会破坏"过渡带 2 格宽"；留给调图。
7. **咽喉取"全部缓坡格 + 四座桥"**（design D3 的字面），于是"到最近咽喉"六个平台都是 1，这一项报告没有区分度。若要有区分度，可改成只标桥与广场入口；不影响任何规则（AI 不读咽喉）。
8. **据点主人口径在边疆图上不适用**：`SiteAttribution` 的"河外低地 / 桥头那家"是 v4 的地貌概念，边疆图的大陆在广场处不经桥就连通，篝火 / 石碑的 `HomeZone` 全部推为 `null`（实测：日志首部 6 个篝火、4 个石碑的 `HomeZone` 均缺省，营帐为所在平台；Standard × 20 的报告里两行主人口径都是"无样本（0/0）……推不出主人 120 个 / 80 个"）。不是 bug，规则也不读它。
9. `sim-out/` 已在 `.gitignore`，跑局输出未入库。

### 待决 / 留给后续段

1. **AI 耗时贴着阈值**。尖峰全部来自匠人：`RankPoints` 对"每个合法空格 × 每种持有类型 × 匠人的每个合法改造目标"逐个预演，全图开放后约 350 格，手里有匠人的小回合 8–13 秒（无匠人约 2–3 秒）；开销与现有的 N / M（`CandidatePointCount` / `CandidateBatchCount`）几乎无关——它们在穷举**之后**才截断，所以 Standard 与 Hard 耗时相近。若负责人认为 10 秒级的单步不可接受（图形版里玩家会干等），建议的旋钮形态：`AiSearchConfig` 加 `CandidateCellLimit`（缺省 0 = 不限制，v4 零变化）；大于 0 时先用一种代表类型对每格预演一次得格分，取前 K 格，再只对这 K 格做完整的"类型 × 改造"枚举。不改评估函数、不消费随机流。本段未实现。
2. 各平台胜率样本太小（20 局 × 4 人 = 80 个选区样本摊到 6 个平台），只能看趋势；要下结论建议 200 局（并行跑约 1 小时）。
3. 到上限终局的比例见统计表——节奏参数是否要动，属裁决 6 留待验证后的决定。
3a. 6×6 的 4 号台到中央入口 11 步，与 9×9（10 步）一样远：design D3 写"6×6 与 7×7 居中环"，而负责人草案把它放在西南角，现布局按草案做，6×6 没有拿到"中"的位置优势（跑局 15 次 3 胜，与大平台同档）。属调图旋钮，不是偏离。
4. 段 A 遗留的图形版事项（严格命令行解析、6 平台着色、实跑 `--map=`）不在本段范围，仍待段 C。

## 段 B 补（2026-09-20）——候选格上限 `CandidateCellLimit`（裁决 12）

根因核实属实：`HeuristicTurnController.RankPoints` 是"合法空格 × 持有类型 × 改造选项"三重循环逐个预演，`Take(N)` 在穷举之后；N / M 不影响预演次数。

### 做法

- `AiSearchConfig` 加第 4 个参数 `CandidateCellLimit`（K，缺省 0 = 不限制）。`K > 0` 且合法空格数 `> K` 时才预筛：用代表类型（持有类型里枚举序最前的一种）、不带改造，对每格预演一次，格分 = 既有评估函数的总分，降序、同分按坐标序取前 K 格，再对这 K 格走原来的三重循环。否则原路径一字不改。不改评估函数、不消费随机流。
- 阈值逻辑只有一处（Core）：`AiSearchConfig.DefaultCellLimitFor(可落子格数)`——`> 150` 取 `LargeMapCellLimit = 24`，否则 0；`AiSearchConfig.ForMap(难度, 可落子格数, 显式K?)` 三个入口共用，显式值（含 0）优先。下游只传 `map.PlayableCount`，不读规格档。v4 = 105 格 → 0；边疆图 = 377 格 → 24。
- 批量：`RunConfig.CandidateCellLimit`（`int?`，`null` = 按地图自动；`--cell-limit`）。`RunConfig.ResolvedFor(map)` 把自动值落成具体数（仅当自动值 > 0），`config.json` 与日志首部记录实际生效的 K；v4 上两者与改动前逐字节相同（不多出这一项）。玩家显式配了 `Search` 的原样生效。
- 回放：段 B 的边疆图旧日志首部没有该项，若回放时也按地图自动取 K = 24 会中途分歧（看起来像非确定性 bug）。`Replayer` 改为按首部原样重建（没有该项 = 不限制）。实测：`sim-out/frontier-hard5/match-…65.jsonl`（段 B、K 出现之前）回放一致 305 行；`sim-out/fb-k24/match-…C9.jsonl`（首部带 24）回放一致 2950 行。
- 终端版 `play --cell-limit`、图形版 `--cell-limit=K`；未给出走 `ForMap`。
- 紧急格豁免：仓库里**没有**现成的"救命点 / 立即提子点"识别（`GroupSafety` / `LibertiesOf` 只是原料），按派发口径不新造。提子走"敌方损失"维、补气走"安全"维，两维与落下的类型无关，代表类型的格分自然把它们排进前 K——由 `小K下仍找到妙手`（K = 4，三个妙手局面）守门。

### 改动文件

| 文件 | 改动 |
|---|---|
| `src/Siege.Core/Ai/AiDifficulty.cs` | `AiSearchConfig` 加 `CandidateCellLimit`、两个常量、`DefaultCellLimitFor`、`ForMap`；`Validated` 拒绝负 K |
| `src/Siege.Core/Ai/HeuristicTurnController.cs` | `RankPoints` 加预筛分支、`PrefilterCells`、`LastCandidateCells`（供检视 / 测试） |
| `src/Siege.Sim/Config/RunConfig.cs` | `CandidateCellLimit`、校验、`ResolvedFor` |
| `src/Siege.Sim/Running/MatchSession.cs` | `Create` 里 `ResolvedFor`；建 AI 走 `ForMap`；`CellLimit`（实际生效的 K）；`internal Create(..., recorded)` 供回放按首部原样重建 |
| `src/Siege.Sim/Running/Replayer.cs` | 回放走 `recorded: true`：首部没有该项 = 当时不限制，不再按地图取缺省 K |
| `src/Siege.Sim/Running/BatchRunner.cs` | 写 `config.json` 之前 `ResolvedFor` |
| `src/Siege.Sim/Play/PlayCommand.cs`、`src/Siege.Sim/Program.cs` | `play` / `run` 的 `--cell-limit` 与用法行 |
| `src/godot/scripts/MatchSession.cs`、`src/godot/scripts/GameRoot.cs` | `Create(..., cellLimit)`、`--cell-limit=K`，建 AI 走 `ForMap` |
| `tests/Siege.Core.Tests/AiDecision/候选格上限Tests.cs` | 新增 16 项（999 → 1015）；既有测试零改动 |

### K 对比（边疆图、4 × Standard、种子 201–205、`--serial`、15 大回合；Release exe 直接跑，期间无构建 / 测试）

| K | 单步均值 | 中位 | P95 | 最大 | > 2.5 s 的小回合 | 平均大回合 | 首次提子大回合（逐局） | Pass | 落子 | 提子 | 改造 | 终局势力均值 |
|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|
| 0（不限制） | 2228 ms | 1670 | 9690 | 11207 ms | 35 / 300 | 15 | 6 / 5 / 6 / 6 / 4 | 0 | 936 | 45 | 44 | 168.4 |
| 40 | 866 ms | 858 | 1956 | 3345 ms | 12 | 15 | 8 / 7 / 12 / 8 / 4 | 0 | 960 | 44 | 39 | 186.1 |
| **24** | **730 ms** | 736 | 1821 | **2226 ms** | 0 | 15 | 8 / 7 / 11 / 7 / 4 | 0 | 936 | 47 | 46 | 177.3 |
| 16 | 649 ms | 690 | 1422 | 1699 ms | 0 | 15 | 无 / 7 / 11 / 7 / 4 | 0 | 926 | 28 | 43 | 177.8 |

输出在 `sim-out/fb-k0`（种子 201–202）+ `sim-out/fb-k0b`（203–205，单次前台调用有 10 分钟上限故分两批）、`sim-out/fb-k40`、`fb-k24`、`fb-k16`。第一轮跑局被一个没退干净的后台批次并发污染，已整轮作废重跑，上表全部是干净数据。

**选定 K = 24**：最大值 2226 ms（< 2.5 s 目标，K = 40 是 3345 ms 不达标）；提子 47 / 改造 46 / 落子 936 与不限制（45 / 44 / 936）持平，无 Pass；K = 16 提子掉到 28、有一局全程无提子，属明显退化。

**均值 < 500 ms 的目标没有达到**（730 ms），且这是本旋钮形态的下限、与 K 基本无关：预筛本身每个小回合要做"合法空格数"次预演（全图开放后约 350 次 × 约 1.5–2 ms ≈ 0.6 s），K 从 40 降到 16 均值只降 217 ms。要再降只能换更便宜的格分（不经预演的静态分），那已越出裁决 12"不改评估函数、只加一个旋钮"的范围，未做。另：Hard 档的组合阶段自身有 M × (N + 1) = 800 次预演，K 管不到，Hard 的耗时未单独复测。

### 变异验证逐条

脚本在会话 scratchpad（不入库）：二进制读写、锚点命中恰 1 次、`finally` 还原并逐字节比对、`DOTNET_CLI_UI_LANGUAGE=en`、解析 `Failed!/Passed!` 统计行；每条跑全量（基线 1014 全绿；M-K12 与 M-K9 复跑时 1015）。12 条全部红、全部有统计行；跑完 `--no-incremental` 重建零警告、全量复绿。

| 编号 | 改了哪里 → 改成什么 | 红 | 红的测试 |
|---|---|---:|---|
| M-K1 | `RankPoints` 启用条件去掉 `K > 0`（K = 0 也预筛，取 0 格） | 36 | 缺省不限制时标准图整局与改动前逐步相同 + 几乎所有 AI / 跑局测试 |
| M-K2 | `PrefilterCells` 去掉 `Take(K)` | 6 | 启用后只对前K格做完整枚举、预筛按代表类型…、小K下仍找到妙手 ×3、启用后同种子两次运行逐步相同 |
| M-K3 | 预筛 `OrderByDescending` → `OrderBy` | 4 | 预筛按代表类型…、小K下仍找到妙手 ×3 |
| M-K4 | 启用条件去掉 `cells.Length > K` | 2 | K不小于合法空格数时等价于不限制 ×2（预演次数多一轮） |
| M-K5 | `DefaultCellLimitFor` 的 `>` → `>=` | 1 | 大图缺省取上限小图为零显式配置优先 |
| M-K6 | 图形版建 AI 改回不传 `config` | 1 | 三个入口共用同一处阈值逻辑 |
| M-K7 | `RunConfig.CandidateCellLimit` 标 `[JsonIgnore]` | 2 | 跑局配置的文本往返、批次配置记录写入实际生效的上限 |
| M-K8 | `BatchRunner.ExecuteToDirectory` 去掉 `ResolvedFor` | 1 | 批次配置记录写入实际生效的上限 |
| M-K9 | Sim `MatchSession` 建 AI 不传跑局配置的 K | 1 | 启用后同种子两次运行逐步相同（与不限制的黄金值相同） |
| M-K10 | 终端版建 AI 改回不传 `config` | 1 | 三个入口共用同一处阈值逻辑 |
| M-K11 | 预筛同分次序改成坐标逆序 | 1 | 预筛按代表类型的格分取前K同分按坐标序 |
| M-K12 | `Replayer` 改回不带 `recorded` 的 `Create` | 1 | 首部没有上限项的大图旧日志按不限制回放 |

等价变异（未计入）：单删预筛的 `ThenBy(坐标)`——LINQ 排序稳定而输入已按坐标序，行为不变。

### 偏离与待决（请裁决）

1. **均值目标未达**（730 ms vs 500 ms），原因与下限见上。最大值目标达到。是否另起 change 做"不经预演的格分"由负责人定。
2. **首次提子推迟约 2 个大回合**（均值 5.4 → 7.4；种子 203 从 6 推到 11）。提子总数、落子数、改造数不降，终局势力均值反而略升；判为轻度变化而非退化，但样本只有 5 局。
3. **批量跑局在大图上也自动取 K = 24**（派发口径"三个入口共用"）。后果：段 B 的 3.5 统计表（K = 0 口径）与今后同配置的边疆图批次不可直接对比；要复现旧口径用 `--cell-limit 0`。`config.json` 与日志首部会写出 `CandidateCellLimit: 24`，可追溯。
4. **紧急格没有豁免名单**（无现成识别，未新造），靠格分自然入选，见"做法"。
5. **已知偏差**：代表类型在该格暂放被拒 / 预演不合法的格不进候选——"只有带改造的匠人才落得下"的格在 K 启用时会被漏掉。K 未启用时不受影响。
6. v4 上显式 K = 8（种子 31、6 大回合）与不限制逐步相同——旁证预筛在小图上几乎无损；确定性测试因此改用 K = 2 才能证明 K 真的传到了 AI。
7. **规格增量未补**：`--cell-limit` 与 `config.json` 的 `CandidateCellLimit` 是新的用户可见行为，`openspec/changes/frontier-map/specs/` 与 `.trellis/spec/` 里目前没有对应条目；派发要求不生成额外文档，是否补由负责人裁。
8. 图形版未实跑（只过了 `dotnet build src/godot/Siege.Godot.csproj`）；`--cell-limit=` 与既有 `--rounds=` 一样是宽松解析（非法值按未给出处理），严格解析仍属段 C。

### 段 B 检查（2026-09-20，trellis-check；含「段 B 补」）

结论：段 B 与段 B 补的实现与 design D3 / 裁决 9、12、三份规格增量一致；起点 1015 项全绿，检查后 **1017 项全绿**、`dotnet build -c Release --no-incremental` 零警告、`dotnet build src/godot/Siege.Godot.csproj` 零警告零错误。四份既有 `maps/*.json` 无 diff；`git diff tests/` 里没有被改的期望值（`SimFixtures.Synthetic` 只加可选参数、地形写入口白名单只加一项，均正当）。

逐项核对：

- **地图**：字符画逐行对过注释里的坐标（六个平台的方块 / 缓坡、六处篝火、四块石碑、四座桥与桥头信物、四段栅栏），无出入；生成器可读（字符画 + 方块表 + 栅栏表，笔误当场抛）。终端 `map --map siege-frontier-v1` 在临时目录导出的 JSON 与 `maps/siege-frontier-v1.json` 逐字节相同。测试的距离 / 连通 / 缓坡分簇都在测试内独立推出，不抄生成器常量。
- **保护期容量**：`[Theory]` 六个平台各跑一遍（0–5），沿用 v4 模式。3.6 两条规则测试与终端脚本测试断言的是拒绝文案次数、落子成功行与第 4 大回合之后无拒绝，不是"跑完不抛"；M-B4 的红属实。
- **v4 零变化**：把 `候选格上限Tests.V4GoldenTurnHash` 拿到提交 `a572877` 的干净 worktree（scratchpad，已删除）里用同一算式复算——**相等**。即该黄金值确实等于段 A / B / B 补全部改动之前的已提交代码的结果，不只是"K 之前的工作树"。
- **K = 0 路径**：`RankPoints` 只多一个恒假的条件判断与 `LastCandidateCells = cells` 赋值；不多预演、不改枚举次序。预筛只用 `Stage` / `rehearse` / `Evaluate`，不碰随机流；缺省规则只在 `AiSearchConfig.DefaultCellLimitFor`、只读可落子格数；三个入口都经 `ForMap`；`Replayer` 走 `recorded: true`。`play --cell-limit -1` 由 `Validated()` 抛 `ArgumentOutOfRangeException`，被 `Main` 的 `ArgumentException` 分支接住，干净报错。
- **`map` 子命令安全性**：导出判据 `requested ∈ BuiltinIds` 与 `MapCatalog.Resolve` 的内置分支用同一归一化（`Trim` 后全等），命中即意味着读到的就是生成器产物；任何文件路径（含 `maps/x.json`、Id 与内置图相同的副本）都不导出。实测 `map --map <v4 副本>.json` 后 `maps/` 下无新文件。

发现并已修：

1. **预筛漏掉"只有带改造的匠人才落得下"的格（原「偏离与待决 5」，已修，该条作废）**。核实：暂放 / 预演的拒绝里与类型有关的只有改造两类，Suicide / Superko 与落子类型无关（六种类型在盘面上气的口径相同）——所以"换下一种持有类型再试"没有意义，唯一有意义的回退是匠人带改造（T-3：改造先于提子与自杀手判定）。`PrefilterCells` 改为：代表类型落不下**且**持有匠人的格，用匠人按 `TerrainEditRules.LegalTargets` 的确定性次序逐个预演，格分取最高的合法总分；其余格仍只预演一次。K = 0 路径一字未动；不消费随机流。开销只发生在自杀格上：边疆图 Standard 种子 201 单局实测 44 s / 60 小回合 ≈ 0.73 s / 步，与修之前的 730 ms 持平。规格 `specs/ai-decision/spec.md` 同步补了一句正文与一个 Scenario；`AiSearchConfig` 参数注释同步。新测试 `候选格上限Tests.只有带改造的匠人才落得下的格不被预筛漏掉`（A2 深水、B1 敌子、B2 己子：A1 不带改造是自杀手，匠人落 A1 立栅 B1–C1 提一子；测试内独立复算格分与名次，K 取 A1 的名次）。
2. **段 B 之前旧日志的 `replay` 行为没有测试**（偏离 2 只是文字说明）。新增 `日志首部区数Tests.回放缺区数字段的旧日志只在首部分歧对局逐步相同`：第 1 行（首部）报分歧、重建首部含 `ZoneCount`、其余各行与小回合快照逐项相同；带字段的新日志回放一致。行为判为合理：与历次加首部字段一致，不静默放过也不伪造一致。
3. `边疆档基准地图Tests.小平台更靠中央` 只与 7–9 的平台比；规格正文是"比**其余**平台更靠近"，补上与 6×6 的比较（现值 4 < 10 / 11）。

检查阶段变异（scratchpad 脚本：二进制读写、锚点恰 1 次、`finally` 还原并逐字节比对、解析统计行；每条跑全量 1017）：

| 编号 | 改了哪里 → 改成什么 | 红 | 红的测试 |
|---|---|---:|---|
| M-K13 | `PrefilterCells` 的匠人回退条件加恒假（等于去掉回退） | 1 | 只有带改造的匠人才落得下的格不被预筛漏掉 |
| M-K14 | 回退条件去掉"代表类型落不下"（每格都逐个改造预演） | 1 | 同上（预演次数上界） |
| M-K15 | 格分取第一个合法改造而不是最高分 | 1 | 同上（A1 名次掉出前 K） |

未修、留给负责人：

- **K 对比表（K = 24 那一行）是回退加入之前量的**。回退只在"有自杀格且手里有匠人"的小回合改变候选，统计口径不受影响，但 `sim-out/fb-k24` 等已有的 K > 0 日志用现代码 `replay` **实测会分歧**：`fb-k24/match-…C9.jsonl` 在第 372 行（第 6 大回合、完整事件流里的一条 `Rehearsal` 失败事件）分歧——原日志此处是别的自杀格，现代码多出了回退对自杀格 `H3` 做的"匠人 + 立栅"预演事件。也就是说回退在真实对局里确实会触发（第 6 大回合起就有自杀格 + 手里有匠人），K = 24 那一行是回退之前的口径；K = 0 与 v4 的日志不受影响。未重跑批量，是否用现代码复测 K = 24 由负责人定。
- 预筛仍有的偏差：代表类型落得下的格只按"不带改造"计分，改造的额外收益不进格分（属旋钮形态本身，已写进 `PrefilterCells` 注释）。另：同形禁则与类型有关（`GameBoard.Serialize` 含棋子类型），代表类型因同形被拒而别的类型不被拒的格会漏，极罕见，未处理（注释已写明）。
- `平台四周是崖壁只经缓坡下到过渡带` 末行把"咽喉 = 全部缓坡格 + 四座桥"钉死了；这是生成器的取法（偏离 7），若负责人改咽喉口径，这条断言要同步改。
- "过渡带宽度 2–3 格"没有自动断言（广场本身 5 格宽，"宽度"难以无歧义地机械定义），只靠字符画人工核对；规格的六个 Scenario 均已覆盖。
- 单点排序的前 N 会被同一格的多个等分匠人改造变体占满（检查时观察到：C2 的 11 个立栅变体同为 360 分，把 279 分的 A1 提子手挤出前 12）。这是 artisan-terrain-edit 既有行为、与 K 无关，不在本 change 范围，记录备查。
- 段 B 原有的待决 1–3a、段 B 补的偏离 1–4、6–8 维持原样待裁决。

## 段 C（2026-09-20）——相机视图模型（Siege.Presentation，零 Godot）与 Godot 接入

范围：tasks 4.1–4.4、5.1–5.6；第 6 组未做。起点 1017 项全绿；段末 **1067 项全绿**（+50，全在 `tests/Siege.Core.Tests/ViewportCamera/`），`dotnet build -c Release --no-incremental` 零警告，`dotnet build src/godot/Siege.Godot.csproj --no-incremental` 零警告零错误。未提交。

### 做法

- **视图模型**（`Siege.Presentation.Camera`）：`BoardCamera` 的状态只有注视点 (x, z) 与距离 d；`CameraPose` 由这三个数推出 `Eye` / `Target`（俯角恒 60°、注视目标相对注视点固定抬 0.2、朝近处偏 0.3、Fov 54——都是旧固定相机的原值）。所见范围线性近似：纵向 = d × 12.7 ÷ 14.6，横向再乘窗口宽高比；可行矩形 = 外接矩形向内收缩所见之半，某方向所见 ≥ 跨度（容差 1e-4）即锁中线；每个改状态的入口末尾统一夹取。最远 = min(一屏看全所需, 上限 28)，最近 = 显示 7 格跨度（5×5 平台四周各留一格）≈ 8.05。平移速度 0.6 × d / 秒；滚轮每格 ×0.9。
- **v4 等价**：外接矩形取"全部格心 ± (半格 + `FarLabelMargin`)"，v4 上跨度 = 13 + 2 × 1.7，与旧公式 `Max(w, h) + 2 × FarLabelMargin` 同值、且 `14.6 × 跨度 ÷ 12.7` 运算次序不变 → 最远距离、相机位置、注视点与旧 `BoardView` **逐位相等**（单测用测试内抄写的旧算式断言；截图逐像素差 0）。
- **Godot 侧**：`BoardView` 持有 `Rig`（跨 `Build` 保留——插旗锁定与地形改造都会重搭场景）；`ApplyCameraPose` 是全仓唯一写相机位置 / 朝向的地方（守门测试钉"`.LookAt(` 只此一处"）。地图外接矩形由遍历 `BoardGeometry.Center` 的最值得到，不新增 `width - 1` 形态的换算（`坐标映射在Godot侧唯一` 的 6 / 2 计数未动）。回家目标：出生区格子读默认棋盘视图模型，外接矩形两角经 `BoardView.PlaneCenterOf`（仍是 `BoardGeometry.Center`）换成世界坐标，由 `CameraHome.Target` 取中点。
- **输入**：平移键注册为 InputMap 动作、`_Process` 里逐帧轮询；相机键在 `_Input` 里先于界面控件认领（否则空格会按下获得焦点的按钮＝误确认 / 误 Pass，方向键会挪按钮焦点）。贴边推屏的三种抑制：`GetWindow().HasFocus()`、窗口鼠标进出通知（`_mouseInside`，初值 false，首次鼠标移动或进入通知置 true）、`Viewport.GuiGetHoveredControl()`（面板本来就是 `MouseFilter.Stop`，没有另画感应区表）。无人值守模式（`--auto-demo` / `--screenshot` / `--pick-check`）不采贴边，截图才可复现。滚轮在 `_UnhandledInput`，指针在面板上时到不了。
- **严格命令行**：新增 `LaunchArgs`——合法选项集合 = 被读取过的名字（与 Siege.Sim 的 strict-cli 同法，无白名单表）；只校验 `--` 之后的用户参数；引擎参数仍可被读取但不参与未知项校验。未知选项、开关带值、值解析不了（`--rounds=abc`、`--cell-limit=-1`、`--seed=x`）都在建局前报错退出码 1 并列出合法选项。
- **出生区着色**：插旗阶段全部平台统一提示色（原样）；锁定后有主平台染主人色（原样），**无主平台褪成 `Visuals.Neutral` 0.34 混合**（新）。v4 四区全有主，无变化。插旗界面本身不含"4 个区"的假设，未改；一屏看不全的地图上插旗提示多一行推屏说明（v4 不显示——否则 v4 插旗画面会差 3513 像素，已实测）。
- **悬停读数**：`HoverReadout.Of(Coord?)` 只转调 `Coord.ToNotation()`；HUD 在回合横幅右侧固定位置显示；指针在面板上 / 窗外 / 不在格上为空（顺带：指针在面板上时盘面光标也不再显示）。

### 相机键位

| 操作 | 键 |
|---|---|
| 平移 | 方向键 / `W A S D`（可同时两个方向；与贴边同速，叠加不加倍） |
| 贴边推屏 | 指针进入窗口边缘 24 px 感应带（角上走对角） |
| 缩放 | 滚轮（上 = 拉近） |
| 回出生平台 / 未选区回地图中心 | 空格 |

既有键位未动：1–4、Tab、H、T、E、Enter、P、Esc、F12、鼠标右键。

### 改动文件

| 文件 | 改动 |
|---|---|
| `src/Siege.Presentation/Camera/BoardCamera.cs`（新） | `PlaneRect` / `CameraPose` / `BoardCamera`（平移、缩放、回家、开局对准、夹取、7 个自检位姿表） |
| `src/Siege.Presentation/Camera/CameraInput.cs`（新） | `EdgePan.Intent`、`CameraHome.Target`、`HoverReadout.Of` |
| `src/godot/scripts/LaunchArgs.cs`（新） | 严格命令行解析 |
| `src/godot/scripts/BoardView.cs` | 固定相机 → `Rig` + `ApplyCameraPose`；外接矩形；`PlaneCenterOf`；无主平台中性色 |
| `src/godot/scripts/GameRoot.cs` | `LaunchArgs` 接入（删 `ReadSeed/ReadMap/ReadRounds/ReadCellLimit`）；`UpdateCamera` / `FocusHome` / `_Input` / `_Notification`；滚轮；悬停抑制与读数；`RunPickCheck` 两层；`[camera]` / `[perf]` 读数 |
| `src/godot/scripts/InputBindings.cs` | 五个相机动作 |
| `src/godot/scripts/Hud.cs` | 悬停读数标签；`CameraHintVisible` |
| `tests/Siege.Core.Tests/ViewportCamera/*`（新，6 个文件） | 夹具 + 五个 Requirement 各一个测试类 + 拾取位姿表 |
| `tests/Siege.Core.Tests/BatchPreview/UI层不含规则计算Tests.cs` | 见下 |

### 既有测试改写逐条

1. `UI层不含规则计算Tests.内核与表现层不出现浮点`：**加了一条豁免**。原断言"Core 与 Presentation 两个程序集里不出现任何浮点（唯一豁免 `MatchOptions..cctor`）"与 design D6"相机纯计算放在零 Godot 的表现层视图模型里"直接冲突——注视点 / 距离 / sin cos 是连续几何量，且 v4 位姿要与旧 float 算式逐位相等。豁免按**类型名单**（`PlaneRect` `CameraPose` `BoardCamera` `EdgePan` `CameraHome`，须在 `Siege.Presentation.Camera` 命名空间且名单里每个类型都真实存在），不是按命名空间放行；期望值 `["MatchOptions..cctor ldc.r8"]` 未动。变异 M-C2（往该命名空间塞一个带 float 的新类型）红 1、M-C12（原 M-D5 形态：`GroupPowerView` 加 `double`）仍红 1。**请裁决**是否接受；不接受的话只能把相机视图模型挪到一个不受该守门扫描的新程序集。

无其他既有测试改动；没有改任何既有期望值。

### 变异验证逐条

脚本 `scratchpad/segC/mutate.py`：二进制读写、锚点恰 1 次、备份名带时间戳、`finally` 还原并逐字节比对；每条跑全量 1067。P 系列另重编 Godot 工程并在两张图上跑 `--auto-demo --pick-check`。跑完已还原、全量复绿、Godot 工程已用还原后的代码重编。

| 编号 | 改了哪里 → 改成什么 | 红 | 红的测试 / 自检 |
|---|---|---:|---|
| M-P5 | `CameraPose.Eye` 的俯角随距离线性变化：d = 28 时 60°，d = 8 时 **55°** | 4 + 自检 | 缩放不改俯角、平移只改注视点不改距离与俯角、全部自检位姿俯角相同、v4 的初始位姿与旧固定相机逐位相等；`--pick-check` **v4 与边疆图都退出码 1**：逐格居中·最近层 v4 `B5(h0) → B4`、边疆图 `R10(h0) → R9`；7 位姿层的"遮挡"计数 v4 1、边疆图 0 → 6 |
| M-P6 / P7 / P10 | 同上，6° / 7° / 10° | 同上 | 同上两格；10° 时遮挡计数 v4 3、边疆图 13 |
| M-C2 | Camera 命名空间里加 `record struct Smuggled(float Ratio)` | 1 | 内核与表现层不出现浮点 |
| M-C12 | `GroupPowerView` 加 `public double Ratio`（原 M-D5 形态，确认豁免没把守门弄瞎） | 1 | 同上 |
| M-C3b | 锁中线分支返回 `(min, max)`（不锁） | 6 | v4 在最远缩放下两个方向都锁中线、小地图上等价于固定相机、某方向所见不小于地图跨度时…、拉远后重新夹取、回家结果同样经过夹取、开局对准·一屏看全的地图上位姿不变 |
| M-C4 | `GameRoot` 滚轮处多写一处 `_board.Camera.LookAt(...)` | 1 | Godot 侧的悬停读数走视图模型_相机位姿只在一处写入 |
| M-C5 | 平移速度用常数 12 代替 d | 1 | 平移速度与距离成正比 |
| M-C6 | `Home` 顺手把距离设成最远 | 4 | 空格回家_只改注视点不改距离 等 |
| M-C7 | 回家目标取外接矩形一角而不是中心 | 5 | 出生平台中心是…外接矩形中心、不规则出生区… 等 |
| M-C8 | 距离夹取去掉上下限 | 6 | 缩放夹取、最近限值下画面完整显示一个 5×5 平台 等 |
| M-C9 | `CheckPoses` 不恢复原位姿 | 1 | 取自检位姿不改变相机当前状态 |
| M-C10 | 贴边上下意图写反 | 4 | 贴边感应带_四边四角与窗外（4 个算例） |
| M-C11 | `Open` 不拉近 | 2 | 锁定 3 号平台后画面中心是 3 号平台的中心、六个平台开局时都居中 |

作废的一条：M-C3（把锁中线条件改成恒假）红 32——`Math.Clamp(min > max)` 抛异常造成的整片假红，换成 M-C3b。

### Godot 自检命令与结果（最终二进制）

| 命令（`-- ` 之后） | v4 | `--map=siege-frontier-v1` |
|---|---|---|
| `--auto-demo` | 退出码 0；`[camera]` 行 **0** 条；启动到首帧 ≈ 1.4–2.4 s，整局 ≈ 4.3–6.2 s / 53 帧 | 退出码 0；`[camera]` 行 **恰 1** 条（第 0 帧开局对准，注视点 (−7.978, −9.5)、距离 8.047）——对手行动全程相机不动；首帧 ≈ 1.6 s，整局 ≈ 7.7–8.1 s / 53 帧 |
| `--auto-demo --pick-check` | 退出码 0：逐格居中 最近 105/105、最远 105/105；7 位姿失败 0，**「左下角」位姿 1 格被遮挡 `B5(h0) → B4(h2)`**；105 格全覆盖 | 退出码 0：逐格居中 377/377 ×2；7 位姿失败 0、遮挡 0；377 格全覆盖 |
| `--seed=12345 "--screenshot=….png:90"` | 退出码 0；与改动前基线（同命令截 2 张，互差 0）**逐像素差 0 / 最大通道差 0** | 退出码 0 |

严格解析实测：`--sed=5` → `未知选项 --sed。合法选项：--auto-demo --cell-limit --map --pick-check --rounds --screenshot --seed`，退出码 1；`--rounds=abc`、`--auto-demo=1`、`--map=nope` 均报错退出码 1。

### 截图（负责人人工看；本段未读取图片）

- `sim-out/frontier-shots/frontier-farthest-flagplanting.png`——边疆图、种子 12345、第 240 帧、插旗阶段、最远缩放（注视点 (0, 0)、距离 28）。
- `sim-out/frontier-shots/frontier-opening-zone1-f8.png`——边疆图、种子 12345、`--auto-demo`（自动选 1 号平台）、第 8 帧、第 1 大回合、中央面板关；开局对准（注视点 (−7.978, −9.5)、距离 8.047）。**此图展示的正是「待裁决 3」里被最近限值夹住的边界情形**（1 号平台贴左缘、9×9 看不全）；3 号平台精确居中只有单测、没有截图——自动演示硬选 1 号平台，图形版没有选区的命令行参数。

### 5.6 渲染开销读数

边疆图最远缩放第 240 帧：**帧率 60**（垂直同步封顶）、单帧处理 16.8 ms（含等垂直同步）、绘制调用 2337、对象 2518、图元 37909。v4 第 90 帧：绘制调用 765。达到 60，**未做 MultiMesh**——`Refresh` 的信息层靶染色靠每格独立材质，合批会同时砸掉它与 v4 像素基线。第 8 / 90 帧读到的帧率（3 / 39）是启动首秒均值，不可用。关垂直同步后的真实余量留给负责人在目标机上读。

### 人工检查清单（留给负责人）

在 `--map=siege-frontier-v1` 上，除注明外：

1. 四边各推一次、四角各推一次：画面朝该方向（角上走对角）匀速平移，指针离开 24 px 感应带即停；推到尽头停住，画面里仍有地图。
2. 指针停在贴着窗口底边的手牌面板 / 右下操作面板 / 顶部顺序条上：不推屏；从面板移到面板外的感应带：恢复推屏。
3. Alt-Tab 切走、指针留在感应带位置：不推屏；切回后恢复。指针移出窗口：不推屏。
4. **启动时指针就在窗外 / 就在窗口边上**：不应一启动就自己往左上推（`_mouseInside` 初值问题的回归点）。
5. 方向键与 WASD 都能推、可同时两个方向；按住方向键时按钮焦点框不乱跳。
6. 点过"确认 / Pass"按钮之后再按空格：只回家，不会再触发一次确认 / Pass。
7. 滚轮到最近 / 最远各自停住；缩放时棋盘"上"始终朝屏幕上方、四边标注不倒置；指针在面板上滚轮不缩放。
8. 在地图一角拉到最远：画面被拉回，地图不会大半落在画面外。
9. 插旗阶段推屏浏览六个平台后再点选；锁定后自家平台在画面中心（1 号平台见"待裁决 3"）；四个有主平台染主人色、两个无主平台是灰的中性色，与"可点"的金色提示明显不同。
10. 对手回合（尤其在画面外落子）相机不动；空格回到自家平台、缩放不变；插旗前按空格回地图中心。
11. 悬停读数：与该格落子后对局日志里的坐标一致；`A27` 读作 `A27`；指针在面板上 / 深水外空白处读数为空；读数位置（回合横幅右侧）在窄窗口下是否与顺序条相撞。
12. v4：不动滚轮时推四边画面不动、整盘完整可见；拉近后可推；`B5`（左下高台 `B4` 身后的低地）在屏幕上半部时点它会点到 `B4`——把它推到画面中部即可点中（见"待裁决 1"）。
13. 帧率：Godot 性能监视器在边疆图最远 / 最近、推屏过程中的读数。

### 偏离与待裁决

1. **设计层发现：D6 的"遮挡论证只依赖俯角、不依赖距离"只在注视点附近成立。** 透视相机下屏幕上部的射线俯角远小于 60°（Fov 54° → 顶边只有 33°，与距离无关）；h=2 高台向远处的遮挡是 0.70 ÷ tan(射线俯角)，在射线俯角 < 54.5° 的屏幕区域（约画面中线以上 5.5° 起）就超过半格。旧固定相机下这片区域恰好没有"紧贴 h=2 身后的 h=0 格"，推屏后任何格都可能出现在那里。实测：v4「左下角」位姿下 `B5` 的格心被 `B4` 的顶面真实遮住，拾到 `B4`——按规格"点在某格顶面的屏幕投影内即选中该格"这是**正确**结果，不是拾取错。因此 `--pick-check` 写成两层：①逐格居中（最近 / 最远，全严格，0 容忍）——直接验证 D6 论证与 Scenario「崖后低地仍可点」；②7 位姿——不一致时分类，"拾到的格层数严格更高且离相机更近"记为遮挡并打印、不算失败，其余（未命中、同层或更低）才失败，另要求全格覆盖。**没有**为了变绿去动俯角 / Fov / 位姿表，也没有剔格。请裁决：这个口径是否接受；若要求"屏幕任意位置格心都可点"，只能加大俯角或收窄 Fov（会动 v4 画面）。规格 Scenario「多位姿自检…全部通过」的"通过"按上述口径理解。
2. **5° 俯角变异的实际敏感度**：逐格居中层在 5° 就红（两张图各 1 格），靠的是注视目标相对注视点有 0.3 的近移、格心略高于画面中线；理论边际很薄（正中线上 55° 的遮挡是 0.49 格 < 0.5）。Presentation 的 4 条单测对任何俯角变化都红，是更可靠的那一层。
3. **开局对准 = 拉近到"目标刚好落进可行矩形"的最大距离再居中**（规格只说"对准中心"，没说缩放）。理由：夹取是 MUST，最远缩放下边疆图横向锁中线，不拉近就不可能"画面中心是 3 号平台的中心"。后果：3 号平台开局距离 ≈ 9.3（精确居中）；**1 号平台（A21–J29，9×9，贴左缘）要居中得比最近限值还近**，停在最近限值 8.05、注视点差 0.02 格，此时纵向只看得到约 7 行——9×9 平台开局看不全，要自己滚轮拉远。请裁决：居中优先（现状）还是"平台整个可见"优先（那样贴边平台不居中）。一屏看全的地图（v4）上 `Open` 不拉近、位姿不变。
4. **最远上限 28、最近跨度 7、平移系数 0.6、滚轮倍率 0.9、感应带 24 px** 都是本段拟的初值，未经手感校准。上限 28 下边疆图最远缩放：横向看全（锁中线）、纵向要推。
5. 所见范围的横向按窗口宽高比估（tasks 只说"按 d 线性近似"）：宽屏上横向更早锁中线。v4 的最远距离在宽高比 ≥ 1 时与旧公式逐位相等；**竖长窗口**下会比旧公式退得更远（旧公式不看宽高比，竖窗本来就看不全）——有单测。
6. **可行矩形按规格的字面规则**（外接矩形收缩所见之半）实现，比规格同段的描述句"空白最多占到画面一侧的边缘"更紧：线性近似下画面里不出现地图外空白。两句不完全等价，取了可测的那句。
7. **v4 对局逐步不变**：相机不进规则，`--auto-demo` 缺省种子终局为"蓝 178 / 金 49 / 紫 30 / 红 3"，但改动前没有留同命令的终局行作对照（截图基线留了）；Sim 侧的 v4 黄金值测试仍绿。
8. 指针在界面面板上时盘面光标不再显示（为满足"面板上读数为空"顺带的行为变化，v4 同样生效；无人值守截图不受影响）。
9. 图形版 `--seed=x` / `--rounds=abc` / `--cell-limit=-1` 从"按未给出处理"变为报错退出（段 A / B 遗留项，按派发要求补上）。
10. **v4 像素基线的覆盖范围**：基线是第 90 帧、第 0 大回合、插旗阶段——它证明的是"锁定前画面不变"。锁定后的出生区着色没有像素证据：有主分支代码一字未动、无主分支在 v4 上不可达（四区全有主）、`Open` 在一屏看全的地图上位姿不变（单测），这一段靠代码审读与单测，不是像素比对。
11. **规范漂移，段 D 要同步**：`.trellis/spec/core/testing.md`「`--pick-check` 只对远半盘的格心敏感」一节描述的还是旧的单位姿自检（"要求 105/105"）；两层口径、"遮挡不算失败"、俯角变异的实测阈值都不在里面。本段不做第 6 组，未改。`coordinates.md`「高度进 3D 坐标」仍成立。HANDOFF 的键位说明也留给段 D。
12. 规格增量未补：开局拉近（第 3 条）、自检两层口径（第 1 条）、图形版严格解析在 `openspec/changes/frontier-map/specs/` 里没有对应文字；是否补由负责人裁。

### 段 C 检查（2026-09-20，trellis-check）

范围：tasks 4.x / 5.x + 负责人三条裁决（开局对准改"整个可见优先"、`--pick-check` 两层口径入规格、浮点守门类型名单豁免）。起点 1067 全绿；段末 **1076 全绿**，`dotnet build -c Release --no-incremental` 与 Godot 工程 `--no-incremental` 均零警告零错误。未提交。上文「待裁决 3、12」已由负责人裁决并在此落实；上文截图说明与人工清单第 9 条里"居中 / 被最近限值夹住"的描述以本节为准。

**问题与修复（按严重度）**

1. **［规格修订落实］开局对准改为"平台整个可见优先于精确居中"**。`BoardCamera.Open(float, float)` → `Open(PlaneRect? platform)`（出生区**格心**外接矩形；`null` = 未选区回地图中心）：需要推屏的地图上距离 = max(`DistanceToShow(纵深 + 1 + 2 × OpeningMargin)`, `DistanceToShow((横宽 + 1 + 2 × OpeningMargin) ÷ 宽高比)`)，`OpeningMargin = 2`，**双向设定**（已拉得更近的相机会被拉远——旧实现的"不拉远"与新规格"缩放距离取…"矛盾，连同其断言一并删除），再 `Home(平台中心)` 经夹取；一屏看全的地图上 `_distance = Farthest` 后回家 = 最远缩放初始位姿（规格 MUST；插旗阶段拉近 / 推过屏也回到初始位姿）。新增 `CameraHome.Platform`（只取两角格心的 min / max，不假设行向），`Target` 改为转调它（数值不变）。`GameRoot.FocusHome` 传矩形；空格回家仍走 `Home`，只改注视点。
2. **［自检漏洞］`--pick-check` 中途抛异常时退出码是 0**：异常只被引擎记一笔，`_pickCheck` 已置假，自动演示接着跑完 `Quit(0)`，等于自检没跑却报通过（做 PK2 变异时撞到：v4 退出码 0、0 条 `[pick-check]` 行）。改为 `try / finally` 保证 `Quit(passed ? 0 : 1)`。变异 M-PK3（`TryPick` 里抛异常）→ 两图退出码 1。
3. **［守门可绕过］"相机位姿只在一处写入"原来只数 `.LookAt(`**：`Camera.Position += …`、`LookAtFromPosition(`、`Camera.GlobalTransform = …` 都绕得过去。守门改为列举"对相机节点的写"整类（位姿 / 投影属性的赋值与复合赋值、`LookAt* / Translate* / Rotate* / Set*` 调用），全仓恰两处（`ApplyCameraPose` 里的 `Camera.Position=` 与 `Camera.LookAt(`）。局限：经局部变量别名（`var cam = _board.Camera; cam.Position = …`）写入，文本扫描抓不到。
4. **［缺自动化证据］"平台整个可见"与"不跟随对手"原来只有线性近似单测 / 人读日志**。① 单测夹具加 `InFrame` / `ZoneInFrame`：用 Eye / Target / 视场角现算视图与投影矩阵，按**真实透视**判定格角（地面与 h=2 台面两个高度）在画面内，不拿视图模型的线性近似给自己作证；② `--auto-demo` 终局时加 `[camera] 开局对准自检` 一行：用引擎 `UnprojectPosition` 核对本机出生平台每格四角都在视口内，且开局对准之后位姿变化次数为 0，任一不符退出码 1。
5. **［派发要求］值类错误不列合法选项**：原来只有未知选项列；开关带值 / 值为空 / 值解析失败在读取当刻抛出，那时合法集合还不完整。`LaunchArgs` 改为读取时记错、按未给出返回，`EnsureRecognized` 统一报出并附全部合法选项（建局之前）。新增文本守门：地图选项经 `LaunchArgs` 读取、结算先于 `MapCatalog.Resolve(`、全仓 `GetCmdline*(` 只在构造 `LaunchArgs` 处出现两次。
6. 既有测试改写（全在 `回到出生平台Tests`，原因均为规格修订）：`…锁定3号平台后画面中心是3号平台的中心` → `…3号平台整个可见且四周留约2格`（原断言"精确居中 + 距离 14.6 × 8.1 ÷ 12.7"与新规格矛盾）；`…六个平台开局时都居中` → `…六个平台开局时都整个可见`（4 种宽高比）；`…位姿不变_也不会把已拉近的相机拉远` → `小图开局不拉近_…`（删去"不拉远"半段，加"拉近 / 推屏后锁定仍回初始位姿"）。新增：平台外接矩形、1 号 9×9 贴边整个可见、距离双向设定、竖长窗口横向成瓶颈、未选区。

7. **［接线顺序］开局对准早于宽高比同步**：`_Process` 里 `Drive` 在 `UpdateCamera` 之前，自动演示第 0 帧锁定时视图模型还是缺省 16:9；非 16:9 窗口上开局距离 / 夹取按错的宽高比算，随后的 `SetAspect` 重夹还会被新加的"开局后位姿变化"自检记成漂移（假红）。1600×900 恰等于缺省值，所以缺省窗口下看不到。抽出 `SyncAspect()`，`FocusHome(opening: true)` 开头先同步。实测 `--resolution 1200x900`（4:3）边疆图 `--auto-demo`：开局注视点 (−5.533, −9.5)、距离 14.945，`70 / 70、变化 0 次；通过`，退出码 0。

**开局对准实测（16:9；距离 / 注视点 / 相对平台中心的偏移；真实透视下格角最大 |NDC|）**

| 平台 | 外接 | 距离 | 注视点 | 偏心 (x) | 最大 \|NDC\| x / y | 整个可见 |
|---|---|---:|---|---:|---|---|
| 1（A21–J29，贴左缘） | 9×9，70 格 | 14.945 | (−2.644, −9.5) | +5.36 | 0.877 / 0.563 | 是 |
| 2 | 8×8，54 格 | 13.795 | (3.533, 10) | −3.97 | 0.764 / 0.535 | 是 |
| 3（R22–X28） | 7×7，43 格 | 12.646 | (4.422, −9.5) | −2.58 | 0.632 / 0.502 | 是 |
| 4 | 6×6，31 格 | 11.496 | (−5.311, 10) | +1.19 | 0.476 / 0.480 | 是 |
| 5 / 6 | 5×5，24 格 | 10.346 | (∓6, 0.5) | 0 | 0.313 / 0.464 | 是 |

纵向都精确居中、上下各 2 格余量；**横向除 5 / 6 号外都被夹取而偏心**——16:9 下横向所见 = 纵向 × 1.78（1 号平台开局横向看 23 格，地图含标注圈才 28.4 宽），可行矩形横向很窄。贴边一侧的余量到标注外圈为止（1.7 格），且透视下画面近处比线性近似略窄：1 号平台近左角的实际余量约 1 格（单测钉 ≥ 1）。v4：`Open` 后位姿与初始位姿相等（单测）、`--auto-demo` 全程 0 条位姿变化行。

**核对通过、未改动**

- 浮点守门豁免：按精确类型名单（5 个）+ 命名空间相等 + 名单须逐个真实存在；重跑 M-C2（命名空间里新塞带 float 的类型）红 1、M-C12（名单外 `GroupPowerView` 加 double）红 1。
- `--pick-check` 两层口径与规格一致：遮挡判据 = `LevelOf(picked) > cell.Height && 水平距离更近`，同层 / 更低 / 未命中一律失败。M-PK1（同层偏移一格）→ 两图 7 个位姿**全部**红（v4 20–62 格 / 位姿，边疆图 20–267 格 / 位姿）、逐格居中两层也红，退出码 1。
- v4 最远位姿单测对照的是测试内抄写的旧算式（与 `git show HEAD:src/godot/scripts/BoardView.cs` 第 218–228 行逐项一致：`Max(w, h) + 2 × 1.7`、`14.6 × span ÷ 12.7`、目标 (0, 0.2, 0.3)、60°、Fov 54），不是对照新实现自身。
- 命令行读取点：`HEAD` 上的 `--auto-demo` `--pick-check` `--seed=` `--rounds=` `--screenshot=` 加段 A / B 的 `--map=` `--cell-limit=` 共 7 个，全部经 `LaunchArgs`；引擎参数（`--path` 等，`--` 之前）不参与校验，实测全部自检命令带 `--path` 通过。
- 输入：相机键（WASD / 方向键 / 空格）与既有键位（1–4、Tab、H、T、E、Enter、P、Esc、F12）无重叠；`src/godot` 里没有 `LineEdit / TextEdit / SpinBox`，`_Input` 先行认领不会吞文本输入（**前提**：以后加文本框须让相机键让路）。贴边三种抑制与无人值守不采贴边，读码无误，手感项留人工清单。
- 出生区着色：判据 `zoneOwners.TryGetValue → 主人色；zoneOwners.Count > 0 → 中性色；否则插旗提示色`；v4 四区全有主，中性分支不可达，插旗阶段截图逐像素差 0。

**变异验证（本次；脚本 `scratchpad/segC/mutate2.py`，二进制读写、锚点恰 1 次、`finally` 还原并逐字节比对；跑完全量复绿、Godot 工程用还原后的代码 `--no-incremental` 重编）**

| 编号 | 改动 | 结果 |
|---|---|---|
| M-O1 | `OpeningMargin` 2 → 0 | 红 5（含 4 种宽高比的 Theory） |
| M-O2 | `Open` 不设距离 | 红 7 |
| M-O3 | `Open` 距离 = 最近限值（旧行为的后果） | 红 8 |
| M-O4 / O4b | 去掉一屏看全分支 / 该分支不回最远 | 各红 1（小图开局不拉近） |
| M-O5 | 距离只许变近不许变远 | 红 1（距离双向设定） |
| M-O6 | 横向不除宽高比 | 红 1（竖长窗口横向成瓶颈） |
| M-O7 | `Platform` 不归一 min / max | 红 6 |
| M-O8 | 漏掉格心 → 格边的半格 | 红 4 |
| M-C6 / C7（重跑，锚点已更新） | `Home` 顺手改距离 / 回家目标取角 | 红 9 / 红 4 |
| M-C2 / C12（重跑） | 见上 | 各红 1 |
| M-G1 / G2 / G3 | `GameRoot` 里 `Camera.Position +=` / `LookAtFromPosition(` / `GlobalTransform =` | 各红 1（加强后的守门；旧守门三条都漏） |
| M-L1 / L2 | 绕过 `LaunchArgs` 直接翻 `GetCmdlineUserArgs` / 删掉结算 | 各红 1 |
| M-P5（重跑） | 缩放带 5° 俯角变化 | 单测红 4；`--pick-check` 两图退出码 1（v4 `B5 → B4`、边疆图 `R10 → R9`） |
| M-PK1 | 拾取同层偏移一格 | 见上，两图退出码 1，多位姿层 7 / 7 红 |
| M-PK2 | 拾到身前更高的邻格（"更高且更近"型错误） | 两图退出码 1，但**只有逐格居中层红**（v4 4 格、边疆图 14 格）；多位姿层把它们全记成遮挡（v4 20、边疆图 32）——这是规格口径的固有盲区，见待裁决 2 |
| M-PK3 | `TryPick` 抛异常 | 两图退出码 1（修复前为 0） |
| M-D1 | `Open` 距离 = 最近限值，跑 `--auto-demo` | 边疆图退出码 1：`整格在画面内 62 / 70` |
| M-D2 | 相机每帧自己往右漂 | 边疆图退出码 1：`位姿变化 29 次`（v4 最远缩放锁中线，漂不动，退出码 0——符合规格） |

**Godot 自检（最终二进制）**

| 命令（`--` 之后） | v4 | `--map=siege-frontier-v1` |
|---|---|---|
| `--auto-demo` | 退出码 0；位姿变化行 0；`开局对准自检 13 / 13、变化 0 次；通过` | 退出码 0；位姿变化行恰 1（第 0 帧开局对准，注视点 (−2.644, −9.5)、距离 14.945）；`70 / 70、变化 0 次；通过` |
| `--auto-demo --pick-check` | 退出码 0：逐格居中 105 / 105 ×2；7 位姿失败 0、遮挡 1（左下角 `B5(h0) → B4(h2)`） | 退出码 0：377 / 377 ×2；7 位姿失败 0、遮挡 0 |
| `--seed=12345 --screenshot=…` | 退出码 0；第 90 帧与基线 `v4-base-a.png` **逐像素差 0 / 最大通道差 0** | 退出码 0（两张，见下） |

严格解析实测（均退出码 1 且列出 7 个合法选项，`--map=nope` 列可用地图标识）：`--mapp=x`、`--auto-demo=1`、`--rounds=abc`、`--cell-limit=-1`、`--seed=x`、`--screenshot=`、裸参数 `stray`。

截图已覆盖重截（未读取图片）：`sim-out/frontier-shots/frontier-farthest-flagplanting.png`（第 240 帧、插旗阶段、注视点 (0, 0)、距离 28；帧率 60、绘制调用 2337）；`sim-out/frontier-shots/frontier-opening-zone1-f8.png`（`--auto-demo`、第 8 帧、第 1 大回合、注视点 (−2.644, −9.5)、距离 14.945——1 号平台整个可见、偏在画面左半）。

**人工检查清单的增删**

- 改第 9 条："锁定后自家平台在画面中心" → "锁定后自家平台**整个在画面内**、上下各约 2 格余量；5 / 6 号居中，1–4 号横向偏向地图内侧（1 号偏得最多，平台在画面左半）是预期"。
- 加 14：1 号平台开局时**最靠近屏幕底边的几行是否被手牌面板 / 右下操作面板盖住**（自检只核对"在视口内"，不知道面板盖了哪）；若被盖，是把余量算到面板上沿，还是接受。
- 加 15：v4 上插旗阶段先滚轮拉近、推屏，再点选出生区：锁定后画面回到整盘可见的初始位姿（规格 MUST），确认这个"跳回去"不突兀。
- 加 16：边疆图插旗阶段先拉到最近再点选：锁定后相机会**拉远**到平台整个可见。
- 加 17：拼错选项启动（如 `--mapp=x`）：窗口一闪即退、控制台有错误与合法选项——确认这个退出方式可接受（没有图形界面提示）。
- 第 12 条保留；第 4、6 条（启动时指针在窗外、点过按钮后按空格）仍是只能人工验的回归点。

**仍需负责人裁决**

1. **横向偏心的观感**：规格"夹取是 MUST + 整个可见优先"的直接后果是 1–4 号平台开局都不在画面横向中心（1 号偏 5.4 格）。若希望贴边平台也大致居中，只能放宽可行矩形（例如按规格描述句"空白最多占到画面一侧的边缘"而不是字面的"收缩所见之半"——上文待裁决 6 指出这两句不等价），那是规格改动，本次未动。
2. **多位姿层对"更高且更近"型拾取错误是盲的**（M-PK2）：判据与规格原文一致、没有再放宽，这类错误靠逐格居中层兜住（实测兜得住）。若要多位姿层也能分辨，需要在自检里独立重算"视线是否真的先穿过该高台顶面"，等于把拾取几何再写一遍；是否值得，请裁。
3. **v4 上锁定时强制回最远初始位姿**：按规格 MUST 字面实现（"开局位姿 MUST 与最远缩放的初始位姿相同"）；Scenario 只说"与锁定前相同"（未动过相机时两者等价）。若本意是"不动相机"，改成一屏看全的地图上 `Open` 什么都不做即可（一行）。
4. `LaunchArgs` 对重复选项（`--seed=1 --seed=2`）取第一个、不报错；Sim 侧同类行为未核对，未动。
5. 上文待裁决 4（手感常数）、5、7、8、10 不变；11（`testing.md` 的 `--pick-check` 一节仍是旧口径，另需补"自检抛异常须非 0 退出""开局对准自检"两条）留给段 D。

## 段 C 补（主会话，段 C 检查之后）

- BoardCamera.Open：整盘一屏看全的地图上由「回到最远缩放初始位姿」改为**不动相机**（裁决 18）；上文「段 C 检查」里关于 v4 锁定跳回初始位姿的描述与待裁决 3 以此为准。
- 测试 小图开局不拉近_… 改名为 小图开局不动相机_一屏看全的地图上锁定前后位姿相同，后半段断言改为「拉近推屏后锁定，位姿保持」。规格 viewport-camera「回到出生平台」同步。
- 验证：全量 1076 绿、零警告；Godot 工程重编；--auto-demo 在 v4 与边疆图上退出码 0，[camera] 开局对准自检 均通过（v4 13/13、边疆图 70/70，位姿变化 0 次）。
- 段 D：6.2（determinism / boundaries / testing 三份规范）与 6.3（HANDOFF）已由主会话完成；6.1 人工整局试玩留给负责人。

