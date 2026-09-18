# 09-18-artisan-terrain-edit 实施记录

每段追加：改了什么 / 既有测试改写逐条 / 变异验证逐条 / 待决。

## 段 O

`strict-cli` 已独立完成并归档（提交 9418aa2、75f6f6e）：未知选项报错退出并给出建议；`replay` 的 `--file` 与 `--dir/--seed` 混用直接报错（design D2）；`LegalOptions` 白名单机制删除，合法集合唯一来自读取动作。测试 808 → 815。教训已入 `.trellis/spec/core/testing.md`：变异脚本必须二进制读写，锚点串换行用 `
`。

## 段 A（tasks 1.1–1.4：匠人棋子）

本段只把第六种棋子接进既有体系，**不含任何地形改造规则**：匠人在规则上与普通子完全相同（军势 1、有气、被围杀、无免死、无持续效果）。

### 改动文件

**规则层**

| 文件 | 改动 |
|---|---|
| `src/Siege.Core/Board/Primitives.cs` | `PieceType` 新增 `Artisan`（第六个枚举值，排在 `Synergy` 之后） |
| `src/Siege.Core/Scoring/PieceEffects.cs` | 基础军势表加 `Artisan => 1`（裁决 T-1） |
| `src/Siege.Core/Recruit/RecruitWeights.cs` | `Order` / `BaseTable` 加匠人一档；新增 `DefaultArtisanWeight = 10`、`BaseWeightOf(type, artisanWeight)`、`AdjustedTable(snapshot, artisanWeight)`、`AdjustedWeightOf(snapshot, type, artisanWeight)` 与 `RequireValidArtisanWeight`。**只有匠人那一档随配置变化**，徽记调权公式（`基础 × (4 + 3n)`）一字未动 |
| `src/Siege.Core/Recruit/HandLedger.cs` | 构造函数增加 `artisanWeight` 参数（旧签名保留、默认 10），`EnterRecruit` 的抽样表改用它；`Restore` 同增重载 |
| `src/Siege.Core/Match/FlagPlanting.cs`（`MatchOptions`） | 新增 `DefaultArtisanWeight = 10` 与 `ArtisanWeight`（R-2：开局固定、公开、入存档，照 `SiteValues` 的口径） |
| `src/Siege.Core/Match/MatchFlow.cs` | 建局校验非负；暴露 `ArtisanWeight` 与 `ArtisanWeightBackfilled`；把配置传进 `HandLedger`。顺手改掉 `DebugSetSnapshotTransform` 上"原型只有五种棋子而基础类型槽也是 5"的过期注释 |
| `src/Siege.Core/Match/MatchFlow.Persistence.cs` | 存档写 `ArtisanWeight`；旧存档缺字段 → 回填 10 并在 `ArtisanWeightBackfilled` 留痕（R-6） |
| `src/Siege.Core/Board/GameBoard.cs` | 盘面序列化字符 `'A' ⇄ Artisan`（这是存档路径，漏了匠人落子后存档会抛 `FormatException`） |
| `src/Siege.Core/Batch/BatchFailure.cs` | 失败文案显示名「匠人」 |

**表现 / 跑局层**

| 文件 | 改动 |
|---|---|
| `src/Siege.Presentation/Text/Labels.cs` | `Piece(Artisan) => "匠人"` |
| `src/Siege.Presentation/Style/VisualBaseline.cs` | 新增 `PieceSilhouette.Scaffold` 与 `SilhouetteLanguage.Tooling`，`PieceStyleTable` 加匠人一行 |
| `src/godot/scripts/LowPoly.cs` | `Scaffold` 的低多边形占位几何（四立柱 + 一横梁） |
| `src/Siege.Sim/Play/BoardRenderer.cs` | 文本图字母 `A`、简名「匠人」、输入解析 `A / 匠 / 匠人` |
| `src/Siege.Sim/Play/ConsoleController.cs` | 类型提示行补「A 匠人」 |
| `src/Siege.Sim/Config/RunConfig.cs` | 新增 `ArtisanWeight`（默认 10）与非负校验 |
| `src/Siege.Sim/Running/MatchSession.cs` | 把 `config.ArtisanWeight` 传进 `MatchOptions` |

未动：`BalanceAnalyzer`、`HeuristicTurnController`（两处都已按 `Enum.GetValues<PieceType>()` / `default:` 写，匠人自然落到「无附加加值」分支，正是 D-A 要的语义）。

### 匠人在文本图 / 视图里的占位表示

- 文本盘面：字母 `A`（`B/F/L/M/S/A`），简名「匠人」；控制台输入 `A`、`匠`、`匠人` 均可。
- 正式名称：`Labels.Piece` → 「匠人」；失败文案同名。
- 存档字符：`A`。
- Godot：`PieceSilhouette.Scaffold` / `SilhouetteLanguage.Tooling`，几何为四根立柱 + 一道阵营色横梁，与其余五种明显不同。**这是占位**，Open Question 3 的正式轮廓与截图属于段 C。

### 既有测试改写逐条

依据分两类：**[R-1]** = 徽记绑定类型由 5 类变 6 类（信物生成结果变化）；**[六种]** = 权重表 / 军势表 / 轮廓表 / 遥测口径从五种扩到六种。本段**没有一条**改写属于 R-1（见下节），全部是 [六种]。

| # | 文件 / 用例 | 旧期望 → 新期望 | 依据 |
|---|---|---|---|
| 1 | `Recruitment/初始配置与基础棋池Tests.棋池全局一致` | `BaseWeights [40,20,18,12,10]` → `[40,20,18,12,10,10]`；`Order` 五项 → 六项；`AdjustedTable [160,80,72,48,40]` → `[…,40]`；新增 `BaseWeightOf(Artisan) == 10` | [六种] 规格 recruitment「匠人在池中」 |
| 2 | 同上 `无徽记大样本分布贴合权重表` | 期望 `[4000,2000,1800,1200,1000]`（总权重 100）→ `[3636,1818,1636,1091,909,909]`（总权重 110，= 10000 × 权重 / 110），容差仍 ±250（±2.5 pp） | [六种] 总权重 100 → 110 |
| 3 | `Recruitment/流派徽记调整征募权重Tests.单枚徽记调权` | `AdjustedTable` 五项 → 六项，末位 `10 × 4` | [六种] |
| 4 | 同上 `候选位独立且可重复` | 独立算式的字面量权重表 `[160,80,72,48,40]` → `[…,40]` | [六种] |
| 5 | `Recruitment/征募随机可复现Tests.征募只消费recruit子流` | 同上，两处字面量表 | [六种] |
| 6 | `Recruitment/私人征募面板Tests.未选候选消失` | 同上，一处字面量表 | [六种] |
| 7 | `VisualStyleBaseline/五种棋子的轮廓语言Tests` → 文件与类改名 `六种棋子的轮廓语言Tests` | 轮廓 / 语言去重计数 `5` → `6`；新增匠人 = `(Scaffold, Tooling)` 一条 | [六种] |
| 8 | `PieceEffects/五种原型棋子的基础军势Tests` → 改名 `六种原型棋子的基础军势Tests` | `[Theory]` 增 `(Artisan, 1)` 一行 | [六种] 裁决 T-1 |
| 9 | `MatchTelemetry/各棋子势力占比Tests.快照棋串明细往返保留各棋子类型计数` | 计数字典五项 → 六项（`Artisan = 6`，仍保持「各不相同」以便看出写反 / 漏写）；读回期望串增 `Artisan=6` | [六种] |
| 10 | 同上 `各棋子势力占比按口径手算` | P1 棋串由 `倍增×5 + 协同×1`（基础 6、协同加值 2、军势 22）改为 `倍增×5 + 协同×1 + 匠人×1`（基础 7、协同加值 4、军势 27）；合计 `18 枚 / 势力 67` → `19 枚 / 势力 72`；归因串增 `Artisan:1/1`；报告文本 3 行重算并新增匠人一行 | [六种] + testing.md「期望值是 0 的遥测断言抓不到写入端漏写」：**故意让匠人取非零样本** |
| 11 | 同上 `真实跑局快照的类型计数与明细自洽` | 反证算式 `Base = … + Synergy` → 补 `+ Artisan`；`otherTypes` 集合补 `Artisan`（匠人算协同子的「其他类型」之一） | [六种]。注：改前该用例恰好绿，属于「运气绿」——**段 A 自评「已修正」不成立**，根因是 Easy 样本结构性地一枚匠人都不落盘，补进算式的匠人项是死代码（检查阶段 M-C1a / M-C1b 实证全绿）。已在检查阶段改用 Easy + Standard 双样本并补样本口径下界，见「段 A 检查」问题 1 |
| 12 | 同上 `含连珠线协同倍增的真实棋串写快照读回并按口径归因` | 读回键集合与归因串各补 `Artisan=0` / `Artisan:0/0`（该盘面本就没有匠人，非零样本由 #10 承担）——**检查阶段已推翻**：#10 是合成日志，绕过写入路径；该盘面已加一枚匠人 `H2`，期望改为 `Artisan=1` / `Artisan:1/1`，见「段 A 检查」问题 2 | [六种] |

**「因 R-1 重新取值」的逐条：0 条。** 排查结论：全仓没有任何测试钉住「某种子 → 某具体信物内容 / 徽记绑定」。`RelicEffects/六类原型信物的效果Tests.cs:88` 的 `EmblemPiece == Basic` 用的是手工构造的信物（`RelicFixtures`），不经 `RelicGenerator`；`SimulationHarness/随机子流隔离Tests` 比的是 A 与 A' 的一致性，不钉具体值。因此 R-1 没有逼出任何期望值改写。

R-1 的实际波及范围已实测（临时把 `EmblemPieces` 过滤掉匠人，跑 20 局对照，脚本二进制还原并逐字节校验）：

| 口径 | 实测（20 局） |
|---|---|
| 信物格总数 | 260 |
| `(坐标, 类型, 强度)` 三元组与五种世界不同的 | **0** |
| 徽记格总数 | 89 |
| 绑定棋子类型不同的 | **75（84%）** |
| 其中绑定到匠人的 | 14 |
| 徽记绑定发生变化的局 | **20 / 20** |

即：信物的位置、类型与强度**逐格实测不变**，只有流派徽记绑定的棋子类型变了。机理与实测相符——`RandomStream.NextInt` 用拒绝采样，上界 5 与 6 的拒绝概率都趋近于 0，子流消费次数不变，只有取模结果变了。结论与 design R-1 一致：**第二轮基线作废**，本轮数据只对照结构性指标。

### 新增测试（11 条）

| 规格 Scenario | 测试 |
|---|---|
| piece-effects「匠人按 1 计」 | `六种原型棋子的基础军势Tests.匠人按1计`（匠人×2 + 堡垒子 = 6） |
| piece-effects「匠人落子后无持续效果」 | `…匠人落子后无持续效果`（与同形状普通子串逐项相等：基础 / 连珠 / 协同 / 倍增 / 高地 / 军势 / 气数） |
| piece-effects 军势规则一致 | `…匠人不免死`（与「堡垒子不免死」同一局面，被围两枚换成匠人） |
| tasks 1.1 守门 | `…军势表穷举六种类型`（恰六种；只有堡垒子是 4；其余五种是 1；总和 9） |
| piece-effects 表 | `…各类型基础军势` 增 `(Artisan, 1)` |
| recruitment「匠人在池中」 | `初始配置与基础棋池Tests.匠人在池中`（总权重 110、匠人 10；5000 个候选位实抽样落在 10/110 的 ±2.5 pp 内） |
| recruitment「匠人权重可配置」 | `…匠人权重可配置`（`MatchOptions.ArtisanWeight = 18` 端到端；期望序列在测试内用字面量表 `[160,80,72,48,40,72]` 独立抽一遍，**不调用** `AdjustedTable`；并反向断言默认权重 10 下的序列与之不同） |
| relic-generation「徽记可绑定匠人」 | `出生区信物权重Tests.徽记可绑定匠人`（3000 种子、6000+ 样本，六种各 16.7% ±2 pp，匠人在列） |
| relic-generation「匠人徽记与其他徽记按同一征募权重公式生效」 | `流派徽记调整征募权重Tests.匠人徽记按同一公式调权`（1 枚匠人徽记 → `10 × 7 = 70`，其余五档不变；配置为 18 时叠加成 `18 × 7 = 126`。期望用独立算式 `基础 × (4 + 3n)` 写死，不调用 `EmblemWeightNumerator`） |
| hand-management「六种类型挤不进默认槽位」 | `手牌类型槽Tests.六种类型挤不进默认槽位`（已持五种 + 5 槽 → 选匠人抛 `SiegeRuleException`，文案含「类型槽已被占满」与「匠人」；对照组同种子同面板先整类弃掉协同子，同一候选位即可选取）。**实现零改动**，既有 `HandLedger` 的整类弃牌路径已覆盖，本条只补测试 |
| 持久化（R-2 / R-6） | `对局持久化Tests.匠人权重随存档往返且旧存档回填`（非回填值 18 往返 + 再存档逐字节相等；剥掉字段的旧存档回填 10 并留痕；**外加独立读路径**：从恢复出来的 `HandLedger` 实抽一次面板，与 18 权重下的字面量表 `[160,80,72,48,40,72]` 对齐，并反向断言默认权重 10 下的序列不同——`restored.ArtisanWeight` 与 `restored.Options.ArtisanWeight` 是同一字段的两个读法，钉不住「读到了但没传给账本」） |

### 验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 基线 | `dotnet test -c Release` | EXIT 0，**815** 通过 |
| 编译 | `dotnet build` | EXIT 0，零警告（`TreatWarningsAsErrors` 已开） |
| 全量 | `dotnet test -c Release` | EXIT 0，**826** 通过（815 + 11） |
| Godot 编译 | `--headless --path src/godot --build-solutions --quit` | EXIT 0 |
| Godot 演示 + 拾取自检 | `--headless --path src/godot -- --auto-demo --pick-check` | EXIT 0，`可落子格 105，往返一致 105，失败 0` |
| 实跑局 | `Siege.Sim run --count 5` | EXIT 0；`config.json` 写出 `"ArtisanWeight": 10`；匠人真的被征募并落子 |
| 分析 | `Siege.Sim analyze` | EXIT 0；报告出现 `棋子 Artisan：展示 93 次，选取 24 次…`、`盘面 3 枚（1.1%），势力 3（0.3%），每颗平均 1`（= 每枚 1 点，与 T-1 相符） |
| 段 A 检查后 | `dotnet build` + `dotnet test -c Release` | EXIT 0，零警告，**827** 通过（826 + 检查阶段补的 1 条） |

### 变异验证逐条

脚本纪律：备份名带时间戳、二进制读写、锚点按各文件实际行尾归一（本仓 CRLF / LF 混用，`MatchFlow.cs`、`VisualBaseline.cs`、`ConsoleController.cs` 等是 LF）、还原写在 `finally`、还原后与「改之前读到的原文」逐字节 `assert`。`dotnet test` 强制 `DOTNET_CLI_UI_LANGUAGE=en`，红绿以退出码为准。基线 EXIT 0 / 825（M-A1…A7）、826（M-A8、M-A9 补跑时）通过。

| 编号 | 变异 | 结果 | 代表性红测 |
|---|---|---|---|
| M-A1 | `PieceEffects.BasePower` 删掉 `Artisan => 1` 分支（军势表漏掉匠人） | EXIT 1，红 **33** | `军势表穷举六种类型`、`匠人按1计`、`百局端到端`、`千例随机局面预演与结算一致` |
| M-A2 | 匠人基础军势写成 4（与堡垒子同档） | EXIT 1，红 **7** | `军势表穷举六种类型`、`匠人按1计`、`各棋子势力占比按口径手算` |
| M-A3 | `RecruitWeights.Order` / `BaseTable` 去掉匠人一档（棋池权重表漏掉匠人） | EXIT 1，红 **9** | `匠人在池中`、`棋池全局一致`、`无徽记大样本分布贴合权重表`、`六种类型挤不进默认槽位` |
| M-A4 | `HandLedger.EnterRecruit` 改回 `AdjustedTable(snapshot)`（匠人权重配置不生效） | EXIT 1，红 **1** | `匠人权重可配置` |
| M-A5 | `RelicGenerator.EmblemPieces` 过滤掉 `Artisan`（徽记绑定排除匠人） | EXIT 1，红 **1** | `徽记可绑定匠人` |
| M-A6 | `MatchFlow.Serialize` 不写 `ArtisanWeight`（存档漏字段） | EXIT 1，红 **1** | `匠人权重随存档往返且旧存档回填` |
| M-A7 | **只改测试不改实现**（testing.md「钉常量表」）：权重表期望对调 倍增 12 ↔ 匠人 10 | EXIT 1，红 **1** | `棋池全局一致` |
| M-A8 | `MatchFlow.Persistence` 恢复时不把 `options.ArtisanWeight` 传给 `HandLedger.Restore`（读到了但没用上） | EXIT 1，红 **1** | `匠人权重随存档往返且旧存档回填` |
| M-A9 | `EffectSnapshot.AdjustedWeight` 对匠人恒用分子 4（匠人徽记不调权） | EXIT 1，红 **1** | `匠人徽记按同一公式调权` |

全部变异逐字节还原完毕（脚本在 `finally` 里还原并 `assert` 字节相等）；还原后重跑全量 `dotnet test -c Release` → EXIT 0，826 通过。

M-A7 之所以对调 **倍增 12 ↔ 匠人 10**（而不是协同 ↔ 匠人）：协同与匠人的权重都是 10，对调是等价变异，测不出「测试照抄实现」。

### 偏离与待决

1. **`--artisan-weight` 命令行选项未接**。`RunConfig.ArtisanWeight` 已就位、`config.json` 已如实写出、`MatchOptions` 已贯通，但没有加命令行开关——`strict-cli` 已上线，未注册的选项会直接报错退出。按 tasks 分工，命令行与日志属于 **3.2（段 B）**；段 D 扫档前必须补上，否则只能靠配置文件指定。本段先用配置文件可以跑通。
2. **匠人轮廓是占位**。`PieceSilhouette.Scaffold` / `SilhouetteLanguage.Tooling` 与 Godot 的四柱一梁几何只保证「六种都渲染得出、`--pick-check` 105/105、不崩」。Open Question 3（工具 / 支架状轮廓）的定稿与截图属于 **段 C（4.2）**。
3. **匠人权重未进 `MatchPublicView`**（已核实：`MatchPublicView` 是逐字段挑选的 record，`SiteValues` 在列、`ArtisanWeight` 不在，`Publish()` 不透传 `Options`）。R-2 说匠人权重「公开」，本段没有加这个字段——新增公开字段会牵动信息边界的反射闭包守门，且规格增量里没有对应 Scenario。`MatchFlow.ArtisanWeight` 是公开属性、入存档、写进 `config.json`，已满足「可查」。**若主会话认为它必须与 `SiteValues` 一样出现在公开视图里（并配一条 `restored.Publish().ArtisanWeight` 的守门），请在段 B 一并处理。**
4. **`真实跑局快照的类型计数与明细自洽` 改前是「运气绿」**。加了匠人后它本该红（反证算式漏了匠人一项），实测没红。段 A 当时判断是「该样本终局盘面上恰好没留下匠人」并只补了算式——**检查阶段证明这个判断与修法都不成立**：共用样本是 Easy 难度，Easy 结构性地一枚匠人都不会落盘（4 局 × 12 大回合 2718 条棋串含匠人 0 条），所以补进去的算式是死代码，M-C1a / M-C1b 全绿。已修，见「段 A 检查」问题 1、2。教训同 testing.md「期望值是 0 的遥测断言抓不到写入端漏写」+「真实跑局覆盖不到的写入路径要补真实盘面端到端测试」，段 E 汇总时并入。

## 段 A 检查（trellis-check，2026-09-18）

基准：design D-A / T-1 / R-1 / R-2、tasks 第 1 组、规格增量 `piece-effects` / `recruitment` / `hand-management` / `relic-generation`、`.trellis/spec/core/testing.md` 与 `boundaries.md`。基线 826 / EXIT 0，收尾 **827 / EXIT 0**（`dotnet build` 零警告）。未改 `src/godot/`，故未重跑 Godot。

### 问题清单

| # | 问题 | 处理 |
|---|---|---|
| 1 | **「运气绿」并未真正补齐（偏离 4 的自评不成立）**。`各棋子势力占比Tests.真实跑局快照的类型计数与明细自洽` 里新加的 `+ c["Artisan"]` 与 `otherTypes` 的匠人项**是死代码**：变异 M-C1a / M-C1b（把两处改回加匠人之前）全绿 826。根因不是「这个样本恰好没匠人」，而是**结构性的**——共用样本是 Easy 难度，Easy 的 `RecruitScore` 只看基础军势，匠人 1 分在平手里排枚举末位，实测 4 局 × 12 大回合 **2718 条棋串含匠人 0 条**（Standard 难度同参数下 1707 条里有 92 条）。 | **已修**：该测试另起一份 Standard 难度小样本（2 局 × 3 大回合，约 1 秒）与 Easy 样本合并统计，并按 testing.md「样本口径下界」补两条断言：`含匠人的棋串 > 0`、`匠人与协同子同串 > 0`。修后 M-C1a / M-C1b **各红 1** |
| 2 | 同一条的第二腿：`含连珠线协同倍增的真实棋串写快照读回并按口径归因` 是唯一走真实写入路径（`MatchSession.PieceCountsOf`）的测试，但匠人的期望值是 `Artisan=0` / `Artisan:0/0`——testing.md 明写「期望值是 0 的遥测断言抓不到写入端漏写」。 | **已修**：盘面加一枚匠人 `H2`，基础 6→7、协同加值 4→6（匠人成为协同子的「其他类型」之一，真实走 `PowerCalculator`）、军势 23→27；期望改为 `Artisan=1` / `Artisan:1/1`，并新增一行报告断言 |
| 3 | **存档字符 `'A'` 的往返无人钉住**。变异 M-C2（`GameBoard` 把 `Artisan` 的码由 `'A'` 改成 `'S'`，与协同子撞码）**全绿 826**——撞码是静默的：读回来变成协同子，且两个不同盘面会被同形禁则判成同形。实现记录只论证了「漏码会抛 `FormatException`」，没覆盖撞码。 | **已修**：`BoardTopology/盘面序列化Tests` 新增 `六种类型的盘面码两两不同且往返保留类型`（六种逐一 `Place → Serialize → RestoreUnvalidated`，断言类型保留 + 再序列化相等 + 六份文本两两不同）。修后 M-C2 **红 1** |
| 4 | **`RunConfig.ArtisanWeight → MatchOptions` 的接线无人钉住**。变异 M-C3（`MatchSession.Create` 不传 `ArtisanWeight`）**全绿 826**。规格 Scenario 的原话是「批量跑局把匠人权重配置为 18」，而既有 `匠人权重可配置` 只走 `MatchOptions`，没经跑局层。 | **已修**：`匠人权重可配置` 补两行——`RunConfig{ArtisanWeight=18} → MatchSession.Create → Match.ArtisanWeight == 18`，并反向断言未配置时为 10。修后 M-C3 **红 1** |
| 5 | 过期注释：`MatchSession.PieceCountsOf` 与 `MatchLog.GroupEntry.PieceCounts` 的文档写「五种全写（含 0）」。 | **已修**：改为「六种全写」 |
| 6 | `openspec/changes/artisan-terrain-edit/specs/piece-effects` / `visual-style-baseline` 的 Requirement 标题仍叫「**五种**原型棋子的基础军势」/「**五种**棋子的轮廓语言」，正文已是六种。 | **未修**（超出段 A）：openspec 里改 Requirement 名要走 REMOVED + ADDED，属 6.1 文档段。测试类已改名为六种并在 `<summary>` 注明对应关系，暂不产生歧义 |
| 7 | 偏离 1（`--artisan-weight` 未接）、偏离 2（匠人轮廓占位）。 | **未修**，与 tasks 分工一致（3.2 / 4.2，段 B / 段 C）。**段 D 扫档前必须先补 3.2**，否则三档只能靠配置文件 |

### 第 3 项结论：R-1 的处理是诚实的

抽验口径与结果：

- `grep -rn "EmblemPiece" tests/`：4 个文件命中。`RelicEffects/六类原型信物的效果Tests.cs:88` 的 `Assert.Equal(PieceType.Basic, state.Content!.Value.EmblemPiece)` 来源是 `RelicFixtures.Scene(("E7", RelicFixtures.Emblem(PieceType.Basic)))`，**手工构造**，不经 `RelicGenerator`；`RelicGeneration/生成结果可完整记录Tests.cs:37` 是 `Assert.Contains(piece.ToString(), line)`，拿生成值自比（序列化自洽），不钉具体类型；`SimulationHarness/随机子流隔离Tests.cs:37-38` 比的是 A/B/C 三条日志之间相等，不钉绝对值。
- `grep -rn "PieceType.(Basic|…|Artisan)" tests/ | grep -i emblem`：19 处全部是 `RelicFixtures.Emblem(...)` / `HandFixtures.Snapshot(emblems: …)` 的手工输入，无一条是「某种子生成出来的徽记类型」。
- 8 个调用 `RelicGenerator.Generate` 的测试文件逐一看断言：只有「同种子相等 / 不同种子不等 / 坐标集合 / `Spec` 与地图一致 / `Rerolls` 与 `Converged`」，没有任何 `(种子 → 具体信物内容或绑定类型)` 的字面量期望。
- `tests/` 下无黄金文件（`*.json` / `*.txt` 全部在 `bin/` `obj/` 下，是构建产物）。

**结论：全仓确实 0 条测试钉住「某种子 → 某具体信物内容 / 绑定类型」，implement.md 的「因 R-1 重新取值 0 条」属实，没有被悄悄改过期望值的用例**（`git diff` 里也没有任何 relic 期望值改动）。旁证：`区域强度预算Tests.cs:71` 的 `Assert.Equal(50, record.Rerolls)` 是唯一一条种子相关的生成结果断言，它依赖稀有度预算而非绑定类型，本轮不受影响且实测仍绿。

### 第 4 项结论：「运气绿」原先没修好，现已用变异证明修好

见问题 1、2。要点：**这不是运气问题而是口径问题**——只要遥测守门的样本是 Easy 难度，匠人就永远不会出现在盘面上，任何「把匠人加进算式」的改法都是惰性的。修法是给该测试补一份会真正落子匠人的样本 + 两条样本口径下界断言；并把匠人的**写入路径**（`PieceCountsOf`）用非零期望的真实盘面测试钉住。

**顺带的结构性事实（段 B / 段 D 需要知道）**：`HeuristicTurnController.RecruitScore` 在 `ImmediateOnly`（Easy）下只返回 `PieceEffects.BasePower(type)`，匠人与普通 / 连珠 / 倍增 / 协同同为 1 分，选优用 `score > bestScore` 严格大于 + 候选位下标先到先得；实测 Easy 下匠人会进面板（日志文本里出现 1376 次）却**一枚都不会落盘**。段 D 扫档若用 Easy 难度，匠人权重三档会测不出任何差别；段 B 的 3.1（AI 枚举改造目标）也要以此为前提。

### 第 5 项结论：偏离 3——`ArtisanWeight` **应当**进 `MatchPublicView`（段 B 补）

判据是既有边界，不是「要不要多一个字段」：

- `MatchOptions` 目前有 5 个对局配置字段，`MatchPublicView` 逐一投影了其中 4 个——`MaxMajorRounds`、`DominanceStartRound`、`CatchUpRecruit`、`SiteValues`，类型注释对每个都写「始终公开，插旗阶段即可读」。`ArtisanWeight` 是**唯一漏项**，属于不一致而非有意收敛。
- R-2 白纸黑字「照 `SiteValues` 的口径：开局固定、公开、入存档」，实现记录自己也是这么写的。
- 实现方给的两条理由不成立：① `int` 字段不会触发信息边界守门——`正式对战AI的信息边界Tests` 的反射闭包查的是「闭包里出现私有类型」（`RelicGenerationRecord` 之类），加一个 `int` 不影响；② 「规格增量里没有对应 Scenario」——`recruitment` 增量已写「匠人的权重 SHALL 为对局配置」，而 `MatchPublicView` 是 Godot `Hud` 与 AI `RecruitScore` 唯一能读到对局配置的通道，`MatchFlow.ArtisanWeight` 对表现层不可达。

**段 B 要补的守门（本段不实现）**：
1. `MatchPublicView` 加 `int ArtisanWeight`，`MatchFlow.Publish()` 投影；
2. `对局持久化Tests.匠人权重随存档往返且旧存档回填` 补 `restored.Publish().ArtisanWeight == 18` 与 `fromLegacy.Publish().ArtisanWeight == 10`（结果对象与活对象两处都钉，testing.md 规则）；
3. 变异：`Publish()` 里改传 `MatchOptions.DefaultArtisanWeight` → 上面那条须红；
4. 两条信息边界守门**都不需要改**（已核实，不是推断）：`Godot层不含规则计算Tests` 是 token 黑名单，读配置不是规则计算；
   `AiDecision/正式对战AI的信息边界Tests` 是「可达类型闭包里不得出现 `RelicLedger` / `RelicGenerationRecord` / `MatchFlow` 等私有类型」的**黑名单**，
   给 `MatchPublicView` 加一个 `int` 不引入新类型；该文件里唯一按属性类型做的白名单式断言（第 57–60 行禁 `int` / `long` / `HandEntry`）针对的是 `HandPublicView`，不是 `MatchPublicView`。

### 第 6 项：偏离 4 核实 + testing.md 已加严一档

实测（`.cs`，排除 `bin/obj/.git`）：**295 个文件，纯 CRLF 181、纯 LF 114、单文件内混用 0**。LF 阵营含 `MatchFlow.cs`、`PieceEffects.cs`、`VisualBaseline.cs`、`ConsoleController.cs`、`Siege.Sim/Running/MatchSession.cs`、`MatchPublicView.cs` 与 30 余个测试文件。偏离 4 成立。

`.trellis/spec/core/testing.md` 已把原来那句「锚点串里的换行也要写成 `\r\n`」替换为新的一节**「锚点按『每个文件实际的行尾』归一，并断言命中次数恰为 1」**（比原条目严一档：写死 `\r\n` 在 LF 文件上一样假绿，只是把假绿换了一半文件），含 `detect_eol` 示例与 `assert data.count(anchor) == 1` 的硬性要求，判据是「变异到底改没改到文件必须由脚本自证」。本次检查的变异脚本即按此实现（LF 文件 `MatchSession.cs` 与 CRLF 文件 `GameBoard.cs` 在同一次运行里各自正确归一）。

### 第 7 项：越段检查通过

- 全仓搜 `改造 / 搭桥 / 立栅 / 烧林 / TerrainEdit / 地形写入口`：`src/` 下命中全部是**既有**的桥 / 栅栏渲染（`LowPoly.Bridge/Fence`、`BoardView`）与只读判定（`IsUnbridgedDeepWater`、`TerrainData` 注释里「为第三轮留写入口」），**没有任何改造动作、目标合法性或地形写入口**。
- `Primitives.cs` 里 `Artisan` 的 XML 注释提到 `terrain-edit` 只是交叉引用，不含规则。
- `RunConfig` 只新增 `ArtisanWeight` 字段 + 非负校验，并透传到 `MatchOptions`；**没有**命令行选项、日志字段或分析项（属 3.2 / 3.3 / 3.4，段 B）。

### 匠人与普通子等同性的逐项核对（第 2 项）

- **无处特判**：`grep -rn "PieceType.Artisan" src/` 去掉表项 / 标签 / `=>` 分支后只剩 **1 处**类型判断——`RecruitWeights.BaseWeightOf(type, artisanWeight)` 里的 `type == PieceType.Artisan ? artisanWeight : …`，是 R-2 的配置旋钮本身，合法。`Ai/`、`Batch/`、`Scoring/PowerCalculator`、`Board/`（除类型码）**零命中**。
- **气 / 围杀**：`匠人不免死` 用与「堡垒子不免死」相同的局面，两枚被围匠人整体被提。
- **位置加值与类型计数**：原先只有「三匠人 vs 三普通子逐项相等」，证明不了「协同子把匠人算作其他类型、倍增子放大匠人的 1」。本次检查把 `含连珠线协同倍增…` 的真实盘面加上匠人后，`SynergyBonus` 由 4 变 6（其他类型 2 → 3）、倍率放大部分由 7 变 8，**真实走 `PowerCalculator`** 钉住了这两条。
- **同形**：同形键取 `Board.Serialize()`（`MatchFlow.Preview` / `Persistence` / `Publish` 三处共用），新增的 `六种类型的盘面码两两不同…` 同时钉住「匠人盘面与协同子盘面不同形」。
- **存档 `'A'` 往返**：见问题 3，已补测试 + M-C2 变异证红。旧存档回填 `ArtisanWeight` 由 `匠人权重随存档往返且旧存档回填` 覆盖（M-A6 / M-A8 已证）。

### 检查阶段变异验证逐条

脚本 `mutate.py`：二进制读写；按各文件实测行尾归一锚点并 `assert count == 1`；备份名带时间戳；还原在 `finally`，还原后与「改之前读到的原文」逐字节 `assert`；`DOTNET_CLI_UI_LANGUAGE=en`，红绿以退出码为准。基线 826 / EXIT 0（M-C1a…M-C4 首轮）、827 / EXIT 0（补测试后复跑）。全部为**与 M-A1～M-A9 及主会话那条（默认权重 10→12）不同的新变异**。

| 编号 | 变异 | 文件行尾 | 补测试前 | 补测试后 |
|---|---|---|---|---|
| M-C1a | 只改测试：`自洽` 的 `Base` 反证等式去掉 `+ c["Artisan"]` | CRLF | EXIT 0，**绿 826（缺口）** | EXIT 1，红 1（`真实跑局快照的类型计数与明细自洽`） |
| M-C1b | 只改测试：`otherTypes` 数组去掉 `"Artisan"`（协同加值不把匠人算作其他类型） | CRLF | EXIT 0，**绿 826（缺口）** | EXIT 1，红 1（同上） |
| M-C2 | `GameBoard.TypeCode`：`Artisan => 'A'` 改成 `'S'`（与协同子**撞码**，静默变类型 + 误判同形） | CRLF | EXIT 0，**绿 826（缺口）** | EXIT 1，红 1（`六种类型的盘面码两两不同且往返保留类型`） |
| M-C3 | `Siege.Sim/Running/MatchSession.Create` 不传 `ArtisanWeight = config.ArtisanWeight` | **LF** | EXIT 0，**绿 826（缺口）** | EXIT 1，红 1（`匠人权重可配置`） |
| M-C4 | `MatchSession.PieceCountsOf` 把匠人并进 `Basic` 键（「写反」形状，Σ计数与 Base 等式都仍成立） | **LF** | EXIT 1，红 2 | EXIT 1，红 2（`含连珠线协同倍增的真实棋串写快照读回并按口径归因`、`真实跑局快照的类型计数与明细自洽`） |

全部变异在 `finally` 里还原并逐字节 `assert` 通过；还原后重跑全量 `dotnet test -c Release` → EXIT 0、827 通过。

### 检查阶段改动的文件

| 文件 | 改动 |
|---|---|
| `tests/…/MatchTelemetry/各棋子势力占比Tests.cs` | `自洽` 加 Standard 难度小样本 + 两条样本口径下界断言；`含连珠线协同倍增…` 盘面加匠人 `H2` 并重算全部期望（基础 7 / 连珠 6 / 协同 6 / 倍增 2 / 军势 27） |
| `tests/…/BoardTopology/盘面序列化Tests.cs` | 新增 `六种类型的盘面码两两不同且往返保留类型`（**+1 条测试**：826 → 827） |
| `tests/…/Recruitment/初始配置与基础棋池Tests.cs` | `匠人权重可配置` 补 `RunConfig → MatchSession → MatchFlow` 的接线断言（正反两向） |
| `src/Siege.Sim/Running/MatchSession.cs`、`src/Siege.Sim/Logging/MatchLog.cs` | 文档注释「五种全写」→「六种全写」 |
| `tests/Siege.Core.Tests/SimFixtures.cs` | `Sample` 的文档注释写明「Easy 从不落匠人」与替代做法，免得下一个人重跑一遍探针 |
| `.trellis/spec/core/testing.md` | 变异脚本行尾纪律加严一档（见第 6 项） |
