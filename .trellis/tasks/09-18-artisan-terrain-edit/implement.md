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

## 段 B（tasks 2.1–2.6、3.1–3.4：改造规则本体 + AI、跑局、日志与分析）

不含第 4 组界面。Presentation 只做了让编译与既有守门通过的最小改动（见「越段的最小改动」）。

### 改动文件

**规则层（新增）**

| 文件 | 内容 |
|---|---|
| `src/Siege.Core/Board/TerrainEdit.cs` | `TerrainEditKind`（Bridge / Fence / Burn）与 `TerrainEdit` 值类型：格目标 / 边目标（`FenceEdge` 构造即归一，`(a,b)` 与 `(b,a)` 是同一个改造——「批内唯一」靠值相等判定，归一是前提）。规范记法 `B:D4` / `F:G6-H6` / `X:F4` 与 `Parse` 同在一处；盘面序列化的改造段、日志目标字段、失败文案共用 |
| `src/Siege.Core/Board/TerrainWriter.cs` | **地形写入口（唯一实现）**：`Apply(TerrainData, edit)` / `ApplyAll(MapData, edits)`。只做加法（加桥 / 加栅 / 林地→草地），没有逆向入口，不碰高度、障碍、信物与据点（R-4）。`ApplyAll` 的动作前提一律按传入的**那一份**地形判，因此整组同时生效、与顺序无关 |
| `src/Siege.Core/Board/TerrainEditRules.cs` | **改造合法性（唯一实现）**：`LegalTargets(map, artisanCell)` 枚举全部合法目标（确定性序），`IsLegal` / `Reject` 是它的两个出口（守门 `拒绝理由与合法目标集合一致` 穷举比对）。口径：几何四邻（T-2）、不含自身格、已改造过的目标不在集合里（R-5）。**批内唯一与「不链式」不在本类**——那是批次层的事 |
| `src/Siege.Core/Match/TerrainEditRecord.cs` | 对局层留痕：大回合、本局第几次、改造方、动作与目标、匠人落点、是否致提子。只供日志与分析（R-3：公开视图不显示改造者）；不随存档往返 |

**规则层（改动）**

| 文件 | 改动 |
|---|---|
| `Board/GameBoard.cs` | `Map` 由只读改为 `{ get; private set; }`，新增 `BaseMap`（开局那份）与 `TerrainEdits`；新增最小写入原语 `ApplyTerrainEdits`（只经写入口）；`Clone` 复制地形与改造列表；`Serialize` 在非空时追加改造段，`Fill` **先读改造段再写棋子**（否则本局架过桥的格会被当成未架桥深水而拒绝落子） |
| `Batch/Placement.cs` | `Placement` 加 `TerrainEdit? Edit = null`（源码兼容）；`ToString` 含改造（`CandidateBatch.Key` 拼的就是它，漏了会把「同落点带/不带改造」两个候选静默去重成一个）。新增 `AppliedTerrainEdit`；`CaptureRecord` 加 `Edits` |
| `Batch/StagedBatch.cs` | `Stage` 加可选 `edit` 形参（旧调用点源码兼容） |
| `Batch/BatchFailure.cs` | 新增两类：`TerrainEditIllegal`（非匠人带改造 / 不相邻 / 类型不匹配 / 已被改造过）与 `DuplicateEditInBatch` |
| `Batch/BatchRehearsal.cs` | 预演由六步变**七步**：① 落点 + 改造目标合法性 → ② 额度与库存 → ③ 放置 → ④ **应用全部改造** → ⑤ 同时提子 → ⑥ 自杀手 → ⑦ 同形。第 1 步全部按 `board.Map`（= 批次开始前的地形），「当批不能站上新桥」由既有的「地形可落子」判定天然成立，没有额外规则 |
| `Batch/SettlementDriver.cs` | 正式结算插入第 3 步「同时应用本批全部改造」；新增 `AttributeEdits`（只在确认路径、且只在真有改造时算） |
| `Match/MatchFlow.cs` | `Map` 由建局时的字段改为 `=> Board.Map`；删掉 `_playableCells` 缓存，`LegalRangeFor` 现算；`Confirm` 把 `CaptureRecord.Edits` 追加进 `TerrainEdits` 留痕（大回合取结算**前**的值） |
| `Match/MatchPublicView.cs` | 新增 `int ArtisanWeight`（段 A 检查第 5 项结论，R-2 照 `SiteValues` 的口径），`Publish()` 投影 |

**表现 / 跑局层**

| 文件 | 改动 |
|---|---|
| `Siege.Presentation/Preview/PreviewPresentation.cs` | `FailurePresentation.TitleOf` 补两类标题（枚举是穷举的，不补会抛）。**越段的最小改动**，正向呈现属 4.1 |
| `Siege.Sim/Program.cs` | 新增 `--artisan-weight`（段 A 遗留；`strict-cli` 已上线，未注册选项直接报错）；用法行与跑局抬头打印匠人权重 |
| `Siege.Sim/Logging/MatchLog.cs` | 类文档的 §17 记录映射由七类改八类并顺延编号；`LogHeader.ArtisanWeight`（`int?`，旧日志为 null）；新增 `TerrainEditEntry`；`TurnSnapshot.Edits`（`List<TerrainEditEntry>?`，**新日志一律写 `[]`**，旧日志才是 null） |
| `Siege.Sim/Running/MatchSession.cs` | 建局时校验 `config.ArtisanWeight == match.ArtisanWeight`；按游标把 `Match.TerrainEdits` 写进每条小回合快照；首部写 `ArtisanWeight`；`PlayableCells` 口径改为 `Board.BaseMap.PlayableCount`（见「口径决定」） |
| `Siege.Sim/Analysis/BalanceAnalyzer.cs` | 新增 `TerrainEditActionStat` / `TerrainEditSection` 与 `TerrainEdits(logs)`（§17 第 11 项全部指标） |
| `Siege.Sim/Analysis/ReportWriter.cs` | 新增「§17-11 地形改造」段，三种动作逐行输出（0 次也给出） |
| `Siege.Core/Ai/CandidateBatch.cs`、`Ai/HeuristicTurnController.cs` | `PointScore` 加 `Edit`；`RankPoints` 对匠人枚举「不改造 + 全部合法目标」；`Greedy` 与 **`Deploy` 末尾的复摆**都带上 `Edit`。**未改 `EvaluationWeights`**（D-J：不新增评估维度） |

### 待决 B-1（**必须上报主会话，本段没有自行改规则**）

**现行规则下，任何改造都不可能直接导致提子。** 三行证明：

1. 立栅的目标边 `(A, N)` 必有一端是匠人落点 `A`（D-B / T-2），而第 5 步算提子时 `A` 已被己方匠人占据。敌串的气是「(敌子 s, 空格 c) 之间的气边」：`s = A` 不成立（A 是己子），`c = A` 不成立（A 已占）；栅栏也切不断敌串的**内部**连接（A 不是敌子）。故栅栏碰不到任何敌串的气。
2. 搭桥只把未架桥深水变成空的可落子格 → 只**加**气，永不减气。
3. 烧林只改地表，`Adjacency.LibertyNeighbors` 不读地表 → 气边一条不变。

实证：20 局 97 次改造，`CausedCapture` **0 次**（不是样本问题，是结构性的）。

受影响的文本（**未改**，请裁决）：

- design **D-C** 的理由「立栅因此是真正的战术武器：可以敲掉敌串最后一口气」与裁决 **T-3**「立栅断气：能提子」；
- `specs/terrain-edit`「改造先于提子生效」的 Scenario **立栅导致提子**、**多个改造同时生效**（后者的算例是「两道栅栏共同使一条敌串无气」）；
- `specs/capture-resolution`「以整批最终状态判定合法性」的 Scenario **改造在提子之前应用**；
- tasks **3.1** 的验证「AI 在能一手立栅提子时选择该手」。

**反向仍然成立**：立栅可以把自己堵死（匠人与相邻己子被切开后无气）→ 自杀手，规格的「改造把自己堵死」可实现且已测。**搭桥的顺序敏感性也成立**（见下），所以「改造先于提子」这条规则本身不是空转。

可裁的备选（属规则变更，段 D 扫档前必须先定）：把边目标放宽为「至少一端是匠人的几何四邻格」（即匠人 1 环内的边，不要求以落点为端点），D-C 的意图才成立。

本段的处置：

- `AppliedTerrainEdit.CausedCapture` 字段与日志 / 分析的相应口径**保留**（规格要求日志有它，分析第 11 项照常输出 0）；
- 不可实现的规格 Scenario 换成**结构性 tripwire** `立栅不改变任何敌串的提子结果`（三个局面 × 每个空落点 × 每条合法栅栏穷举，共 >100 条），它红了就说明口径被放宽，届时改回规格场景；
- 「改造先于提子」的正向守门改由两条**搭桥**用例承担（`搭桥先于提子可救活敌串`、`搭桥先于提子可使自杀手变合法`），它们对顺序严格敏感。

### 其它口径决定（design 未写死，本段取定）

| 口径 | 取值 | 理由 |
|---|---|---|
| 「是否直接导致提子」的定义 | 把该条改造**单独去掉**后重算，无气敌串集合**严格变小** | design 未定义。这样两道栅栏合围时两条都记 true（各自必要），顺带架的桥记 false。算在 `Confirm` 路径（每批至多一次、且只在真有改造时算），MUST NOT 放进 `Rehearse`——AI 每小回合调预演成千上万次 |
| 同形 / 存档的改造表示 | **增量**（只写本局新增的改造，地图预置设施不写），排序后即规范形，空时不写 `｜` 分隔符 | ① 预置设施一局内恒定，写不写对同形等价；② 无改造时输出与改造上线前**逐字节相同**，旧存档与旧 `BoardHistory` 天然按「无改造」回填（R-6），既有 `Serialize()` 字面量期望一条都没改；③ 改造不可逆且每目标只能改一次，「已应用集合 ⇔ 当前地形」一一对应 |
| 日志占用率的分母 `PlayableCells` | `Board.BaseMap.PlayableCount`（**开局**地图） | 首部是一局一条、终局时才写出；写终局值等于把「未来的分母」塞进第 9 项。代价：本局架的桥不进分母，占用率略偏高（20 局共 76 座桥，对 105 的基数 < 1 个百分点）。**口径变化，扫档时需注意与第二轮基线的可比性** |
| 栅栏另一端可以是不可落子格 | 允许 | 规格字面只要求「几何四邻」，R-5 只排已有栅栏。未自行加限制 |
| `Move` / `Replace` 碰到带改造的暂放 | 不特判，让既有校验自然拒绝（`Replace` 成非匠人 → `TerrainEditIllegal`） | UI 语义属段 C |

### 2.6 缓存排查清单（守门测试：`地形派生数据不缓存Tests`）

排查口径：全仓搜「持有 `MapData` / `GameBoard` 的字段」「`static readonly` 的坐标派生表」「`cache` / `memo` / `Lazy<`」，以及 `DistanceTable` / `EntireBoard` / `CoverageMap.Compute` 的全部调用方，逐处判定。

| # | 位置 | 判定 | 处置 / 对应测试 |
|---|---|---|---|
| 1 | `MatchFlow._playableCells`（建局时算好的可落子格集合） | **曾经是缓存，改造后过期** | **已删**，`LegalRangeFor` 改为按 `Board` 现算。测试 `合法落子范围在保护期后含本局新架的桥`（走真实 `MatchFlow.LegalRangeFor`：E5 做成深水 → 不在范围内；架桥后在范围内，且范围只多这一格）；变异 **M-B15** 证红 |
| 2 | `MatchFlow.Map`（建局时拷进字段的 `MapData`） | **曾经是缓存，改造后过期** | **已改**为 `=> Board.Map`。测试 `MatchFlow的Map随改造更新`；变异 **M-B13** 证红 |
| 3 | `Adjacency.LibertyNeighbors` / `CoverageTargets`（气边、覆盖） | 不缓存（每次按传入 `MapData` 实时导出，类型注释明写） | 测试 `气边覆盖与棋串每次调用重算`（先各读一遍制造「若有缓存就会命中」的时机，再改造后复读） |
| 4 | `GameBoard.GroupAt` / `LibertiesOf`（棋串与气） | 不缓存（「每次调用重算，不做增量维护也不缓存」） | 同上 + `地形写入口Tests.架桥后两格同串` / `立栅后分串` |
| 5 | `CoverageMap.Compute`（覆盖表） | 不缓存（每次结算重算一次；5 个调用点都是现算） | 测试 `覆盖表与据点控制按新地形重算`（烧林 → 据点由「无人」变「被 P0 控制」） |
| 6 | `SiteControl.Compute`（据点控制） | 不缓存，只读 `CoverageMap` | 同上 |
| 7 | `MapValidator.DistanceTable`（出生区距离表） | 纯函数，不缓存；唯二调用方 `Validate`（建局）与 `Siege.Sim map` 都只看开局地图 | 测试 `距离表按传入地图现算`。**结论：本局架的桥不回头改变距离均衡校验——这是有意的**，校验是「地图设计」的守门，不是对局态 |
| 8 | `BatchContext.EntireBoard(board)` | 每次调用现算；唯一调用点是 `LegalRangeFor`（已改现算） | 测试 1 |
| 9 | `MatchFlow.Flags`（`FlagPlanting.Map`，建局时的地图） | 持有开局地图，**但只用于插旗**（出生区、时限）；出生区与高度都不可改造 | 不缓存地形派生量，无需改；出生区集合本身 MUST NOT 随改造变 |
| 10 | AI：`HeuristicTurnController` / `BatchEvaluator` / `GroupSafety` / `RelicEstimate` | 不持有 `MapData` 字段；每次从 `MatchPublicView.Board`（Clone，带当前地形）现取 | 测试 `AI枚举改造目标Tests.单点枚举…`（枚举出的目标按当前 `Board.Map` 判定合法） |
| 11 | `Siege.Presentation` `DefaultBoardView.From` | 不缓存：每次从公开快照的 `Board.Map` 现取地表与栅栏集合 | 测试 `表现层的地形视图随改造更新` |
| 12 | `src/godot/scripts/BoardView`（`_tileMaterials` / `_tileBase` / `_levels`） | 缓存的是**渲染态**，且 `GameRoot` 每次刷新都重跑 `_board.Build(...)` 整体重建 | 无失效机制需要加。`_levels` 缓存的高度不受影响（改造 MUST NOT 改高度）；「被烧林地呈现为草地」「新桥 / 新栅栏的外观」属 4.2，段 C |
| 13 | `Siege.Sim` 日志首部 `PlayableCells` | 不是缓存，是**口径**：一局一条、终局时写出 | 已改取 `BaseMap`，见上表 |
| 14 | `RelicLedger` / `RelicGenerationRecord` | 不持有 `MapData`；信物位置由地图给定，改造不动信物（R-4） | 无需处理 |

### 既有测试改写逐条

| # | 文件 / 用例 | 旧 → 新 | 依据 |
|---|---|---|---|
| 1 | `BatchPreview/非法批次必须说明原因并高亮Tests.八类失败标题互不相同` → 改名 `十类失败标题互不相同` | 标题数 `8` → `10` | 新增两类改造失败类别；该用例穷举 `Enum.GetValues<BatchFailureKind>()`，不补标题会直接抛 |
| 2 | `BoardTopology/四邻接Tests.几何邻居枚举只在允许名单内直接调用` | 允许名单加第 ③ 条 `TerrainEditRules` | 裁决 T-2 把改造目标口径定成**几何四邻**（不是气边——深水没有气边，搭桥会变成不可能），而 `TerrainEditRules` 只接受 `MapData`，走不了 `GameBoard.Neighbors`。名单只放这一个类型，任何第二处「自己遍历四邻判改造目标」仍会红。**`.trellis/spec/core/boundaries.md` 需同步补两行（地形写入口 / 改造合法性）与这条名单——属 6.2，段 E** |
| 3 | `tests/…/BatchFixtures.cs` | `P(...)` 加可选 `edit`；新增 `Artisan(notation, edit)` | 夹具 |
| 4 | `tests/…/MatchFixtures.cs` | `Map` / `Create` / `Started` 各加一个可选 `TerrainData` 重载 | 9×9 合成对局图原本是全平地，测不了改造 |
| 5 | `tests/…/SimFixtures.cs` | `Synthetic` 加 `artisanWeight`；`Turn` 加 `edits` 与 `legacyNoEdits`。**默认写空表 `[]`（新日志形态），`legacyNoEdits: true` 才是缺字段的旧日志** | R-6 的判定靠「字段缺失」，默认必须是新日志形态，否则所有合成样本都会被当成旧日志 |

「因 R-1 重新取值」的逐条：**0 条**（段 A 已论证全仓无「种子 → 具体信物内容」的期望值）。

### 新增测试（48 条：827 → 875）

| 文件 | 条数 | 覆盖 |
|---|---|---|
| `TerrainEditing/地形写入口Tests` | 9 | 2.1：架桥后同串 / 立栅后分串 / 烧林后可覆盖且气边不变 / 只加不减且不动高度障碍信物据点 / 对局中改造与预置设施完全等价（逐格比可落子性、气边、覆盖）/ 同一目标不能写两次 / 同时生效与顺序无关 / 副本不影响原盘 / **守门：写入口之外不得构造改造后的地形**（IL 扫描 `new TerrainData(`，白名单 = 写入口 + `TerrainData.Flat` + `MapFile` + 基准图，另有「扫描器确实命中写入口」的反面断言） |
| `TerrainEditing/改造合法性Tests` | 11 | 2.2 / 2.3：四邻且不含自身格 / 已改造过的目标不在集合 / **`Reject` 与 `LegalTargets` 穷举一致** / 非匠人不得改造 / 目标必须相邻 / 不带目标与无目标可改仍可落子 / 批内同一目标唯一（两端顺序相反也算同一个）/ 当批不能站上新桥也不能拿它当跳板、下一批次可以 / 改造不额外占额度 / 改造随暂放留在批次里 / 记法往返 |
| `TerrainEditing/改造先于提子Tests` | 8 | 2.4：**搭桥先于提子可救活敌串** / **搭桥先于提子可使自杀手变合法**（两条对顺序严格敏感）/ 改造导致的自杀手 / **立栅不改变任何敌串的提子结果**（待决 B-1 的 tripwire）/ 多个改造同时生效且顺序无关 / 本批改造一律不记致提子 / 烧林后的揭示与控制（钩子看到的已是改造后地形）/ 预演不污染正式盘面的地形 |
| `TerrainEditing/同形与存档纳入设施Tests` | 5 | 2.5：多一道栅栏 / 桥 / 烧痕即不同形（四份两两不同 + `BoardHistory` 实测）/ 无改造时逐字节与改造上线前相同 / 改造段排序是规范形 / 盘面序列化往返（站在新桥上、站在烧痕上）+ 旧存档回填 + 损坏段抛 `FormatException` / **对局存档往返保留改造与匠人权重**（含 `restored.Publish().ArtisanWeight == 18` 与旧存档 `== 10` 两处） |
| `TerrainEditing/地形派生数据不缓存Tests` | 6 | 2.6，见上表（全部走各自的**真实调用路径**，不自己重算一遍恒真断言） |
| `AiDecision/AI枚举改造目标Tests` | 4 | 3.1：单点枚举同时产出带 / 不带改造且三种动作都在、每个目标都经唯一实现判过合法 / 非匠人没有改造候选 / **选中的批次连改造一起摆回暂放** / 候选去重的键区分带不带改造 |
| `MatchTelemetry/地形改造日志与分析Tests` | 5 | 3.2–3.4：真实跑局写日志 + 往返 + **离线重放重建地形** / 匠人权重写进批次配置与日志首部（含反向 10）/ 第 11 项手算样本（含烧林 0 次照常输出、旧日志整局排除并计数）/ 一条快照缺字段就整局排除 / 真实批次的第 11 项分析自洽 |

### 验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build` | EXIT 0，零警告（`TreatWarningsAsErrors` 已开） |
| 全量 | `dotnet test -c Release` | EXIT 0，**875** 通过（基线 827 + 48） |
| Godot 编译 | `--headless --path src/godot --build-solutions --quit` | EXIT 0 |
| 冒烟跑局 | `run --out sim-out/b-smoke --seed 1 --count 1 --difficulty Standard` | EXIT 0；`config.json` 写出 `"ArtisanWeight": 10`；日志首部 `ArtisanWeight = 10`；该局真的发生了 3 次搭桥 |
| 权重选项 | `run --out sim-out/b-w18 … --artisan-weight 18` | EXIT 0；`config.json` 与日志首部都是 **18**，其余配置不变（分值仍 5/15/45） |
| 20 局 | `run --out sim-out/b-20 --seed 1 --count 20 --difficulty Standard --gzip` | EXIT 0，0 失败局 |
| 分析 | `analyze --dir sim-out/b-20` | EXIT 0，第 11 项见下 |

`sim-out/` 不提交。

### 20 局第 11 项报告（种子 1–20，Standard，分值 5/15/45，匠人权重 10）

```
## §17-11 地形改造（artisan-terrain-edit）
- 纳入 20 局，排除缺改造字段的旧日志 0 局
- 改造总次数 97，每局平均 4.85 次；整局无改造 0 局
  - 搭桥：76 次（78.4%），其中直接导致提子 0 次
  - 立栅：20 次（20.6%），其中直接导致提子 0 次
  - 烧林：1 次（1.0%），其中直接导致提子 0 次
- 带改造的匠人占已落匠人：89.0%（97/109）
- 改造直接导致提子 0 次；首次改造平均第 3.4 大回合
- 改造过的玩家胜率 25.5% (12/47，95% 区间 15.3%–39.5%)
- 终局新增：桥 76 座，栅栏 20 道，被烧林地 1 格
```

要点（20 局样本，只供段 D 参考，不作结论）：

- **搭桥占 78%**；落盘的匠人里 89% 都带了改造，说明「改造可选」在 AI 手里不是摆设；
- **烧林 1 次**（全图只有 4 格林地，与 D-H 的预期一致；报告照常输出该行，未省略）；
- **改造致提子 0 次**——见待决 B-1，这是结构性的，不是样本不足；
- 与第二轮基线只作结构性对照（R-1 已使基线作废）。

### AI 枚举取舍与耗时对照（3.1 / D-J）

**保留「全部合法目标 + 不改造」的全枚举**，不退到退化版。

| 口径 | 实测 |
|---|---|
| 第二轮基线 | 19.2 秒/局 |
| 本段 20 局（第一次跑） | 均值 **20.1** s/局（min 7.5，max 27.4）→ +4.7% |
| 本段 20 局（补完测试后复跑同一批种子） | 均值 **14.8** s/局（min 5.9，max 19.4） |

两次跑之间的差异远大于「有没有枚举改造」可能带来的差异（本机并行度 28，第一次跑时同时在跑 `dotnet test`），因此**没有观察到显著上升**。搜索规模上也解释得通：匠人至多是 5 种持有类型之一，且只对四邻枚举（每个合法落点至多 +8 个候选），其余五种类型一个都不多枚。

**顺带一条重要事实**：D-J 给的退路「只枚举能直接导致提子的改造 + 不改造」在现行规则下是**空集**（待决 B-1），即退路本身也失效——将来若真要降规模，得换别的剪枝（例如只枚举搭桥与烧林，或只枚举与己方棋串相邻的目标）。

### 变异验证逐条

脚本纪律照 `.trellis/spec/core/testing.md`：二进制读写、锚点按各文件**实测行尾**归一并 `assert count == 1`、备份名带时间戳、还原写在 `finally` 并与「改之前读到的原文」逐字节 `assert`、`DOTNET_CLI_UI_LANGUAGE=en`、红绿以退出码为准。基线 EXIT 0 / 875。

| 编号 | 变异 | 文件（行尾） | 结果 | 代表性红测 |
|---|---|---|---|---|
| **M-B1** | `GameBoard.ApplyTerrainEdits` 绕开写入口，自己 `new TerrainData(...)` 加桥（第二份地形写入实现） | `Board/GameBoard.cs`（CRLF） | EXIT 1，红 **3** | `地形写入口之外不得构造改造后的地形`、`同一目标不能被写两次`、`盘面序列化往返保留改造` |
| **M-B2** | 预演去掉「同一目标批内唯一」 | `Batch/BatchRehearsal.cs`（CRLF） | EXIT 1，红 **2** | `同一批次内同一目标只能被改造一次`、`AI…选中的批次连改造一起摆回暂放` |
| **M-B3** | 改造目标口径由几何四邻改成**气边**（`Adjacency.LibertyNeighbors`） | `Board/TerrainEditRules.cs`（LF） | EXIT 1，红 **4** | `合法目标枚举只含几何四邻且不含自身格`、`拒绝理由与合法目标集合一致`、两条 AI 枚举用例（深水无气边 → 搭桥全部消失） |
| **M-B4** | 删掉「只有匠人能带改造」的检查 | `Batch/BatchRehearsal.cs`（CRLF） | EXIT 1，红 **1** | `非匠人不得改造` |
| **M-B5** | **把改造挪到提子之后**（预演第 4、5 步对调） | `Batch/BatchRehearsal.cs`（CRLF） | EXIT 1，红 **2** | `搭桥先于提子可救活敌串`、`本批改造一律不记致提子` |
| **M-B6** | 同形 / 存档表示**去掉设施段**（`Serialize` 不写改造） | `Board/GameBoard.cs`（CRLF） | EXIT 1，红 **4** | `棋子分布相同但多一道栅栏即不同形`、`改造段的排序是规范形`、`盘面序列化往返保留改造`、`对局存档往返保留改造与匠人权重` |
| **M-B7** | 改造合法性改按「本批其余改造已生效后的地形」判（**批内链式放行**） | `Batch/BatchRehearsal.cs`（CRLF） | EXIT 1，红 **1** | `同一批次内同一目标只能被改造一次`（失败类别由「批内重复」变成「已被改造过」） |
| **M-B8** | AI 不枚举改造目标（`EditOptions` 只 yield `null`） | `Ai/HeuristicTurnController.cs`（LF） | EXIT 1，红 **4** | 两条 AI 枚举用例 + `真实跑局把改造写进日志且可离线重建地形`、`真实批次的第11项分析自洽`（真实样本里改造归零） |
| **M-B9** | AI **复摆丢掉改造**（`Deploy` 末尾 `Stage` 不传 `Edit`） | `Ai/HeuristicTurnController.cs`（LF） | EXIT 1，红 **3** | `选中的批次连改造一起摆回暂放` + 两条真实跑局遥测 |
| **M-B10** | 正式结算不写地形（只有预演副本写） | `Batch/SettlementDriver.cs`（CRLF） | EXIT 1，红 **14** | 预演副本与正式盘面的逐字节核对当场抛；`日志覆盖七类记录`、`真实跑局把可落子格写进日志首部`、`领先者胜率回归` 等大面积红 |
| **M-B11** | 分析把缺改造字段的旧日志当成「这局没改造」（不排除） | `Sim/Analysis/BalanceAnalyzer.cs`（CRLF） | EXIT 1，红 **2** | `一局里只要有一条快照缺改造字段就整局排除`、`第11项改造分析按手算样本输出` |
| **M-B12** | 跑局不把匠人权重写进日志首部 | `Sim/Running/MatchSession.cs`（LF） | EXIT 1，红 **2** | `匠人权重写进批次配置与日志首部`、`真实跑局把改造写进日志且可离线重建地形` |
| **M-B13** | `MatchFlow.Map` 退回「建局时的快照」（`=> Board.BaseMap`） | `Match/MatchFlow.cs`（LF） | EXIT 1，红 **1** | `MatchFlow的Map随改造更新`（2.6 缓存排查第 2 项） |
| **M-B14** | `Publish()` 把匠人权重写成默认值（读到了但没传） | `Match/MatchFlow.cs`（LF） | EXIT 1，红 **1** | `对局存档往返保留改造与匠人权重`（`restored.Publish().ArtisanWeight == 18`） |
| **M-B15** | `LegalRangeFor` 退回按**开局地图**算可落子格（等价于把建局时的缓存加回去） | `Match/MatchFlow.cs`（LF） | EXIT 1，红 **1** | `合法落子范围在保护期后含本局新架的桥`（2.6 缓存排查第 1 项） |

`tasks` 点名要求的六条：2.1 的「写入口之外不得直接构造带不同设施的 `TerrainData`」= **M-B1**；2.2 的「去掉批内唯一 / 改用气边口径 / 允许非匠人带目标」= **M-B2 / M-B3 / M-B4**；2.4 的「把改造挪到提子之后」= **M-B5**；2.5 的「同形表示去掉设施」= **M-B6**。
自做九条：**M-B7**（批内链式放行）、**M-B8**、**M-B9**（AI 两条）、**M-B10**（结算不写地形）、**M-B11**（旧日志不排除）、**M-B12**（日志漏配置）、**M-B13** / **M-B15**（两处地形缓存回归）、**M-B14**（公开视图漏传）。

三条纪律注记：

1. 初版的 M-B4 / M-B6 / M-B11 用 `if (false)` 短路，结果 `TreatWarningsAsErrors` 下 **CS0162 不可达代码**直接编译失败——EXIT 1 但一条测试都没跑。这不是「守门证红」，是**假红**。已全部改成运行时恒假的条件（删掉整个检查块 / `_edits.Count > 0 && Height < 0` / `log.Turns.Count < 0`）后重跑，才拿到真实的红测名单。脚本因此加了一条：抓不到统计行就打印输出尾部，免得把编译失败当成守门生效。
2. 脚本按各文件**实测行尾**归一锚点并 `assert count == 1`；本批同时命中 CRLF 文件（`GameBoard.cs`、`BatchRehearsal.cs`、`SettlementDriver.cs`、`BalanceAnalyzer.cs`）与 LF 文件（`TerrainEditRules.cs`、`HeuristicTurnController.cs`、`MatchFlow.cs`、`MatchSession.cs`），两类都正确变异。
3. 全部变异在 `finally` 里还原并与「改之前读到的原文」逐字节 `assert`；还原后复跑全量 → EXIT 0、**875** 通过。

### 越段的最小改动（语义留段 C）

1. `FailurePresentation.TitleOf` 补「改造目标非法」「同一批次内重复的改造目标」两条标题——`TitleOf` 的 `switch` 以 `throw` 收尾且有穷举守门，不补会直接抛。**只补了标题**，详情沿用 Core 文案；4.1 要做的「可改造目标高亮、预演按改造后地形算气与自杀风险、棋串读法区分栅栏侧」一概未做。
2. `src/godot/` **零改动**（`--build-solutions` EXIT 0）。4.2 的匠人轮廓定稿、可改造目标高亮、改造落成反馈、被烧林地呈现为草地全部留给段 C；`BoardView` 每次刷新整体重建，段 C 不需要再加失效机制（2.6 第 12 条）。

### 偏离与待决（汇总给主会话）

1. **待决 B-1：立栅不可能致提子**（详见上文）。影响 design D-C / 裁决 T-3、`terrain-edit` 与 `capture-resolution` 的三条 Scenario、tasks 3.1 的验证方式。**段 D 扫档前必须裁决**——若决定放宽边目标口径，规则、AI 枚举与相关测试都要改，届时扫档数据作废。
2. **`PlayableCells` 口径改为开局地图**。第 9 项占用率的分母因此不含本局架的桥（20 局 76 座）。
3. **`boundaries.md` 需补两行**（地形写入口、改造合法性）并写明「地形不再是对局内不变量」，另需把 `Adjacency.Neighbors` 守门名单的第 ③ 条 `TerrainEditRules` 写进文档。属 6.2，段 E。
4. **openspec 的 Requirement 标题仍叫「五种…」**（段 A 检查第 6 项遗留），属 6.1，段 E。
5. 段 C 需要的接口都已就位：`TerrainEditRules.LegalTargets`（可改造目标）、`GameBoard.TerrainEdits`（本局改造）、`MatchPublicView.Board.Map`（含设施的当前地形）、`Placement.Edit`（预演里的改造目标）。`MatchFlow.TerrainEdits` 含改造方，**MUST NOT** 进公开视图（R-3）。

---

## T-11 放宽栅栏目标

负责人 2026-09-18 裁决 T-11（design D-B′）：立栅的边目标由"匠人格与其四邻之间"（4 条）放宽为
"**至少一端是匠人落点的几何四邻格**"（16 条，含原来的 4 条）。格目标（搭桥、烧林）仍是几何四邻，不变。
段 B 的待决 B-1（立栅不可能致提子）随之关闭。

### 改动文件

| 文件 | 改动 |
|---|---|
| `src/Siege.Core/Board/TerrainEditRules.cs` | `LegalTargets` 的立栅枚举由"匠人格–四邻"一条边改为"四邻格 n 的全部边"（`Neighbors(n)` 嵌套一层）：`m == 落点` 给出 4 条内圈边，其余给出 12 条外圈边；`Reject` 的立栅分支重写为三道检查——① 两端都在棋盘内 ② 两端**互相**几何相邻 ③ **至少一端**是落点的四邻。类级 remarks 改写口径与 T-11 理由 |
| `src/Siege.Core/Board/TerrainEdit.cs` | `TerrainEditKind.Fence` 的 XML 注释改成新口径（过期文案） |

**枚举与合法性本来就是同一实现**，未新增第二份：`LegalTargets` / `IsLegal` / `Reject` 同在 `TerrainEditRules`，
AI（`HeuristicTurnController.EditOptions`）只调 `LegalTargets`，批次层（`BatchRehearsal`）只调 `Reject`，
表现层与预演都经这两个出口。核对过 `grep -rn "TerrainEditRules\." src`：全仓只有这两处调用点，**不需要合并**。
变异 M-T2 正面证明了"改其一即红"（见下）。`src/Siege.Sim`、`src/Siege.Presentation`、`src/godot` 零改动。

### 去重与形状的两条事实

1. **16 条边互不重复**，不必去重：两个四邻格互不相邻（曼哈顿距离 2），外圈端点离落点是 2 格，
   因此不同 `n` 产出的边集合两两不交。`边目标可落在外圈但不得更远` 用 `Distinct().Count() == 16` 钉住。
2. 放宽只动"边离落点多远"，**边本身仍必须是一条几何边**（两端互相四邻）且两端都在盘内。
   旧代码靠 `Connects(artisanCell, other) && neighbors.Contains(other)` 隐含了这两条，放宽后必须显式补回——
   漏了会让 `TerrainData` 构造期的"栅栏两端必须几何相邻"校验在写入时才抛。

### 测试清单

**删除 1 条**

- `改造先于提子Tests.立栅不改变任何敌串的提子结果` —— 段 B 为待决 B-1 留的结构性 tripwire，
  钉的是**旧规则的结构性事实**（穷举三个局面的每条合法栅栏，带 / 不带栅栏的提子集合恒等）。
  新规则下立栅可以提子，这条事实不再成立，按裁决删除（不是反转：它的断言形式"恒等"没有有意义的反面，
  正面覆盖改由下面四条具体算例承担）。类级 `<remarks>` 里关于待决 B-1 的整段一并改写为 T-11 的结论。

**新增 6 条**

| 测试 | 钉的规格 / 事实 |
|---|---|
| `改造合法性Tests.边目标可落在外圈但不得更远` | 规格「边目标可落在外圈」「边目标不得离得更远」：F6 → `F:F7-F8` 合法、`F:F6-F7`（内圈）仍合法、`F:F8-F9` 非法且文案含"两端都不是匠人落点的几何四邻格"；候选边恰 16 条、互不重复、其中 4 条以落点为端；两端不相邻的斜边（`F:F7-G8`）非法；贴边落点（A1）不产出越界边 |
| `改造合法性Tests.边目标不得离得更远时整批非法` | 同一场景走到批次层：`BatchFailureKind.TerrainEditIllegal` + 文案，换成外圈边即合法 |
| `改造先于提子Tests.立栅导致提子` | 规格「立栅导致提子」：P1 孤子 E5 只剩 E5–E6 一口气，匠人落 **E7（不与 E5 相邻）** 给 E5–E6 立栅 → 当场提走 E5；对照组同落点不带栅栏提子为空；`CausedCapture == true` |
| `改造先于提子Tests.立栅切断敌串连接后各自算气` | 规格「立栅切断敌串连接」：P1 的 {E5, E6} 本是一串（合起来有 D6/F6 两口气），匠人 E7 立栅 E5–E6 后二者分属两串，E5 单独 0 气被提、E6 单独仍有 D6/F6；且 E5 空出来后从 E6 看仍不是气（中间有栅栏） |
| `改造先于提子Tests.外圈立栅把自己堵死也算自杀手` | 规格「改造把自己堵死」的外圈变体：匠人 E7 给**己方**孤子 E5 与它唯一的气 E6 之间立栅 → 自杀手、整批被拒、地形一个字节没变 |
| `AI枚举改造目标Tests.AI在能一手立栅提子时选择该手` | **tasks 3.1 点名的验证**，旧口径下不可实现。AI 只有匠人、只能落 E7，选中的正是 `E7:Artisan+F:E5-E6`，复摆后预演提子集合为 `[E5]` |

**改写 4 条**

| 测试 | 为什么改 |
|---|---|
| `改造合法性Tests.合法目标枚举只含几何四邻且不含自身格` → 更名 `格目标只含几何四邻且不含自身格` | 原名与末尾断言"全部目标都以落点为一端 / 就是落点的四邻"钉的是旧口径，外圈边必红。改为：格目标 ∈ 四邻；边目标至少一端 ∈ 四邻 |
| `改造合法性Tests.拒绝理由与合法目标集合一致` | 原穷举只造 `Fence(落点, n)` 形状的边，外圈边压根没进穷举——放宽后"一致"只在子集上成立。改为穷举**全盘每一条几何边**（144 条）+ 两条坏边（斜边、远边），并加断言 `targets.Except(candidates) 为空`，保证合法集合被穷举完全覆盖 |
| `改造合法性Tests.匠人不带目标与无目标可改时仍可落子` ② | `boxed` 原只封 E5 的 4 条边，放宽后还剩 12 条外圈边可立 → `Assert.Empty` 会红。改为把 16 条候选边全部预置栅栏 |
| `改造先于提子Tests.本批改造一律不记致提子` → 改写为 `两道栅栏合围时两条都记致提子` | 原测试钉的是"`CausedCapture` 恒为 false"（待决 B-1 的推论）。在 `Cornered()` 上它**仍会绿**，是假绿。改成正向算例：两枚匠人（E3、E7）各立一道栅栏合围 P1 的 E5，两条都记 `true`（各自都是必要的），顺带架的桥记 `false`——一并覆盖规格「多个改造同时生效」里"两道栅栏共同使一条敌串无气"的算例 |

`AI枚举改造目标Tests` 另有一处**非语义**调整：`单点枚举同时产出带改造与不改造两类候选` 原来在
`C4`/`E4` 两个落点上断言"三种动作都进了前 12 名"，放宽后同分的栅栏候选把烧林（记法 `X:` 排序最后）挤出榜外。
改为在专门造的 `Boxed()` 地形上把 E4 的 16 条候选边封掉 14 条，只留 `E3–E4`（内圈）与 `E2–E3`（**外圈**），
单点落点收窄到 `E4`；新增一条断言"外圈边 `F:E2-E3` 确实进了 AI 枚举"。挤占本身记在下面「取舍」里。

### 20 局第 11 项关键数与耗时对照

命令（单独跑，未与 `dotnet test` 并行——段 B 两次跑差 5 秒就是并行污染）：
`run --out sim-out/t11-20 --seed 1 --count 20 --difficulty Standard --gzip`，EXIT 0，0 失败局；
`analyze --dir sim-out/t11-20`，EXIT 0。

```
## §17-11 地形改造（artisan-terrain-edit）
- 纳入 20 局，排除缺改造字段的旧日志 0 局
- 改造总次数 99，每局平均 4.95 次；整局无改造 0 局
  - 搭桥：30 次（30.3%），其中直接导致提子 0 次
  - 立栅：69 次（69.7%），其中直接导致提子 32 次
  - 烧林：0 次（0.0%），其中直接导致提子 0 次
- 带改造的匠人占已落匠人：100.0%（99/99）
- 改造直接导致提子 32 次；首次改造平均第 1.6 大回合
- 改造过的玩家胜率 35.7% (15/42，95% 区间 23.0%–50.8%)
- 终局新增：桥 30 座，栅栏 69 道，被烧林地 0 格
```

| 指标 | 段 B（`sim-out/b-20`） | T-11（`sim-out/t11-20`） |
|---|---|---|
| 改造总次数 / 每局 | 97 / 4.85 | 99 / 4.95 |
| 搭桥 | 76 次（78.4%） | **30 次（30.3%）** |
| 立栅 | 20 次（20.6%） | **69 次（69.7%）** |
| 烧林 | 1 次（1.0%） | **0 次（0.0%）** |
| **改造直接致提子** | **0 次** | **32 次**（全部是立栅） |
| 带改造的匠人占比 | 89.0%（97/109） | 100.0%（99/99） |
| 首次改造平均大回合 | 3.4 | **1.6** |
| 改造过的玩家胜率 | 25.5%（12/47） | 35.7%（15/42） |
| 终局原因 | AllPassed 9 / 回合上限 8 / 碾压 3 | AllPassed 11 / 回合上限 5 / 碾压 4 |
| 总小回合 / 大回合 | 952 / 241 | 844 / 217 |

结构性解读（20 局样本，只供段 D 参考，不作结论）：

- **T-11 的目的达成**：立栅致提子从结构性的 0 次变成 32 次（占全部改造的 32%），T-3「改造先于提子」终于有实际战术含义。
- **立栅与搭桥的占比对调**（20.6% → 69.7%）：一是候选边由 4 增至 16，二是致提子的立栅评分很高。
- **对局更快收敛**：总小回合 952 → 844、终局大回合 241 → 217、回合上限局 8 → 5，与"多了一种直接提子的手段"一致。
- **首次改造提前到第 1.6 大回合**：保护期内就有大量立栅（D-F 的风险"四家开局把自己封成铁桶"要在段 D 的 200 局里盯）。

### AI 耗时与取舍（3.1 / D-J）

| 口径 | 每局耗时 | 每小回合 |
|---|---|---|
| 第二轮基线 | 19.2 s | — |
| 段 B 20 局（干净复跑） | 14.8 s（296186 ms / 20） | 311 ms（/952） |
| **T-11 20 局** | **19.2 s（383060 ms / 20）** | **454 ms（/844）** |
| D-J 退档阈值（基线 × 1.5） | 28.8 s | — |

**结论：保留"全部合法目标 + 不改造"的全枚举，不退档。** 19.2 s/局与第二轮基线持平、远低于 28.8 s 的阈值。
诚实记一笔：与段 B 那次干净复跑（14.8 s）相比是 **+29.4%**，按每小回合算是 **+45.9%**——
这就是"匠人落点的候选由 ~2 增到 ~17"的真实代价；它没有过线，只是因为匠人只占六种类型之一、且每局落盘的匠人不多。
D-J 的退路（"只枚举致提子的改造 + 不改造"）在 T-11 之后**不再是空集**（段 B 记录的"退路本身失效"随之解除），
将来若在 200 局扫档中过线，可以真的退到那一档。

### 一条要留给段 D 的副作用（不在本补丁修）

`RankPoints` 的单点排行榜只留前 `CandidatePointCount`（Standard = 12）名，同分时按坐标 → 类型 → **改造记法**排序。
放宽后一个匠人落点一次就能产出 17 个候选，其中大量零收益的栅栏**同分**，于是：

1. **挤占机制已在单元测试里确凿复现**：`单点枚举同时产出带改造与不改造两类候选` 原来在 `C4`/`E4` 两个落点上
   就因为一大批同分（37 分）的栅栏候选填满 12 个名额，把记法排序最后的烧林 `X:F4` 顶出榜外而变红——
   这是"同分 → 按记法排序 → `X:` 垫底"的直接证据（不同分时排序键根本轮不到记法，例如同一批里搭桥 `B:D4` 是 172 分，
   靠分数稳进榜，与字母序无关）。
2. 20 局里烧林 1 次 → **0 次**与该机制方向一致，但**样本不足以归因**：全图只有 4 格林地，1 次和 0 次在 20 局里区分不了挤占与噪声。
3. 搭桥 78% → 30% 的主因不是排序，而是**立栅候选变多且致提子的立栅评分很高**（致提子的 32 次全在立栅）；
   搭桥的绝对次数 76 → 30 才是真正需要段 D 盯的数。
4. 结构上，单点排行榜有可能被**同一个落点的 16 条边**占满，其它落点进不来——这条尚未在真实跑局里单独测量。

这不是本任务要修的（规则层正确、耗时没过线），但它会直接影响段 D 扫档里三种动作的使用率与 AI 强度。
可选处置留给段 D 裁决：① 调大 `CandidatePointCount`；② 单点排行榜按"每落点至多保留 k 个改造候选"去重；
③ 把"改造目标"下沉到批次组合层而不是单点层。**在此之前，第 11 项里的烧林 0 次 MUST NOT 被读成"烧林没用"。**

### 变异验证逐条

脚本纪律同段 B：二进制读写、锚点按各文件**实测行尾**归一并 `assert count == 1`、备份名带时间戳、
还原写在 `finally` 并与"改之前读到的原文"逐字节 `assert`、`DOTNET_CLI_UI_LANGUAGE=en`、红绿以退出码为准、
不用 `if (false)`（`TreatWarningsAsErrors` 下 CS0162 = 假红）。基线 EXIT 0 / **880**。

| 编号 | 变异 | 文件（行尾） | 结果 | 红测 |
|---|---|---|---|---|
| **M-T1** | 边目标放宽**改回旧口径**：`LegalTargets` 只产出 `Fence(落点, n)`，`Reject` 只认"以落点为一端" | `Board/TerrainEditRules.cs`（LF） | EXIT 1，红 **8** | `边目标可落在外圈但不得更远`、`边目标不得离得更远时整批非法`、`立栅导致提子`、`立栅切断敌串连接后各自算气`、`外圈立栅把自己堵死也算自杀手`、`两道栅栏合围时两条都记致提子`、`AI在能一手立栅提子时选择该手`、`单点枚举同时产出带改造与不改造两类候选` |
| **M-T2** | **只放宽 `Reject` 一侧**（`neighbors.Length < 0`，运行时恒假 → 任何盘内几何边都放行），`LegalTargets` 不动——等价于"枚举与合法性各写一份且口径分歧" | `Board/TerrainEditRules.cs`（LF） | EXIT 1，红 **3** | **`拒绝理由与合法目标集合一致`**（守门本身）、`边目标可落在外圈但不得更远`、`边目标不得离得更远时整批非法` |
| **M-T3** | **结算顺序改回提子先于改造**（预演第 4、5 步对调，与段 B 的 M-B5 同锚点） | `Batch/BatchRehearsal.cs`（CRLF） | EXIT 1，红 **5** | **`立栅导致提子`**、**`立栅切断敌串连接后各自算气`**、`两道栅栏合围时两条都记致提子`、`AI在能一手立栅提子时选择该手`、`搭桥先于提子可救活敌串` |

三条的对应关系：任务点名的"边目标放宽逻辑改回旧口径应红"= **M-T1**；
"枚举与合法性走两份实现应红（或说明已是同一实现，改其一即红）"= **M-T2**——本仓已是同一实现，
M-T2 证明的正是"改其一即红"，且红的第一条就是那条同源守门；
"'立栅提子'用例对应的结算顺序改回提子先于改造应红"= **M-T3**，新增的立栅用例全部在红测名单里
（段 B 的 M-B5 只红了两条搭桥用例，证不了立栅这一半）。

三条变异全部在 `finally` 里还原并与原文逐字节比对通过，还原后复跑全量 → EXIT 0、**880** 通过。
（脚本首跑时控制台按 GBK 输出把中文测试名打成乱码；已 `PYTHONIOENCODING=utf-8` 整批重跑一次取干净名单，
两次的红测条数与名字完全一致，上表用的是重跑的原始输出。）

### 验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build` | EXIT 0，0 Warning 0 Error |
| 全量测试 | `dotnet test -c Release` | EXIT 0，**880** 通过（段 B 基线 875：删 1 条 tripwire、新增 6 条 → 880） |
| 20 局 | `run --out sim-out/t11-20 --seed 1 --count 20 --difficulty Standard --gzip` | EXIT 0，0 失败局，`FailedFiles: []` |
| 分析 | `analyze --dir sim-out/t11-20` | EXIT 0，第 11 项如上 |
| 变异 | M-T1 / M-T2 / M-T3 | 三条全红（EXIT 1），还原后逐字节一致 |

### 偏离与待决

1. **待决 B-1 关闭**：立栅可以提子（20 局 32 次），规格「立栅导致提子」「立栅切断敌串连接」已由正面算例守住。
2. **新待决 T-11-a（给段 D）**：单点排行榜被同分栅栏候选挤占，烧林在 20 局里归零（见上「一条要留给段 D 的副作用」）。
   段 D 扫档前应先裁决是否调 `CandidatePointCount` 或给单点候选去重，否则三种动作的使用率数据会被这个剪枝口径带偏。
3. **段 B 的 D-J 记录需更正**：段 B 写"退路（只枚举致提子的改造）是空集、退路本身失效"，T-11 之后不再成立，已在本节更正。
4. **spec 措辞已由负责人更新**（`terrain-edit/spec.md` 的立栅条目、相邻约束、三条新场景与「立栅导致提子」措辞；
   `design.md` 的 D-B′ 与裁决 18），本段只做实现与测试，未再改 openspec 文本。
   `TerrainEditKind.Fence` 的 XML 注释属**代码内过期文案**，随实现一并改。
5. 段 C（界面）需要注意：可改造目标高亮现在是 16 条边而不是 4 条，`TerrainEditRules.LegalTargets` 的返回规模翻了两番，
   高亮的视觉密度与"哪条边属于哪个落点"的读法要重新设计。
6. **"两端可落子"这条约束在代码与规格里都不存在，本补丁未新增**。派工文本把它列在"其余既有约束…保持"里，
   但核对过 `TerrainEditRules` 与 `TerrainWriter`：两者都只查"该边是否已有栅栏"，`Adjacency.Neighbors` 也不过滤可落子性——
   旧口径下匠人同样能对未架桥深水格、障碍格的那条边立栅，`terrain-edit` 规格与 design 也没有这条要求。
   放宽后的副作用是：这类"两端都不可落子"的**无效边**从每落点最多 4 条变成最多 16 条进入 AI 枚举（无功能影响，只是候选噪声）。
   若负责人本意确实要"至少一端可落子"或"两端都可落子"，那是一条**新约束**，需要先裁决、改规格，再动 `TerrainEditRules`。

---

## 段 B 检查（trellis-check，2026-09-18）

范围：`git diff d4bfe02..HEAD -- src tests`（段 B 9d4b8ad + T-11 bf57a73 + 094c2a8）。基线 EXIT 0 / **880**；
检查后 EXIT 0 / **887**（新增 7 条，全部用于补规格缺口或把弱断言换成真比对）。

### 问题清单

**已修（7 条，均为"规格 Scenario 没有测试"或"注释写了却没真比"）**

| # | 问题 | 处置 |
|---|---|---|
| 1 | `terrain`「架桥切断隔水覆盖」**无测试**。规格是两半（s 不再覆盖对岸 t、改为覆盖新桥格），一条都没钉 | 新增 `地形写入口Tests.架桥切断隔水覆盖`，两半都断言。变异 **M-C1** 证红 |
| 2 | `terrain`「立栅不影响覆盖」**无测试**。`立栅后分串` 只断言气边与棋串；`对局中架的桥与预置桥完全等价` 比的是"改造出来的 = 预置的"，证不了"立栅前后覆盖不变" | 新增 `地形写入口Tests.立栅不影响覆盖`（覆盖集合逐项相等 + 反面：那条气边确实没了） |
| 3 | `terrain-edit`「改造不可逆且设施无归属」的两条 Scenario（匠人被提走后设施仍在、弃赛不撤销改造）**都无测试** | 新增 `改造不可逆与公开Tests` 前两条：前者走**两次 `SettlementDriver.Confirm`**（先立栅、后围杀），不直接 `RemoveStones`；后者走真实 `MatchFlow.Resign` |
| 4 | `terrain-edit`「改造公开」**无测试**，R-3「公开视图 MUST NOT 显示改造者」只有代码注释 | 新增 `改造不可逆与公开Tests.改造结果人人可见且不显示改造者`：正向断言 `Publish().Board.Map` 与 `BoardSerialized` 含三种改造；守门用 `PresentationFixtures.ReachableTypes(typeof(MatchPublicView))` 断言类型闭包里没有 `TerrainEditRecord`；再加反面（改造方确实被记下来了，只是不在公开视图里） |
| 5 | `capture-resolution`「结算的原子性」在**带改造的批次**上没有测试。既有 `正式结算顺序Tests.结算的原子性` 那一批不带改造，证不了规格新加的"设施已写入但敌串尚未移除" | 新增 `改造先于提子Tests.结算的原子性含改造`：四个回调点采样正式盘面，第 1 步是结算前、其余全是终态，并逐个否掉两种中间态的具体形状。变异 **M-C4** 证红 |
| 6 | `match-telemetry`「回放全部改造可重建终局地形」**注释写了"与该局终局地形逐格一致"，代码没这么比**——`真实跑局把改造写进日志且可离线重建地形` 只把重放结果与**日志自己**算出的条数相比，日志漏记一条改造它照样绿 | 新增 `回放日志改造可重建终局地形并与对局逐项一致`：走 `MatchSession`（唯一同时拿得到日志与活对局终态的入口），把日志改造重放到 `Board.BaseMap` 上，与 `Board.Map` **逐项**比（桥集合、栅栏集合、每格地表 / 可落子性 / 高度）；另加"日志是 Core 留痕的逐条转录"（五字段）与两条反面（开局≠终局、少放一条就对不上）。变异 **M-C5 / M-C6** 证红 |
| 7 | 第 11 项的 `CausedCapture` 全线只断言 **0**（手算样本里没有致提子的改造），把它恒写 false 的实现照样绿——而 T-11 之后这是主力指标 | 手算样本的立栅改成 `caused: true`，补 `t.CausedCaptures == 1` 与逐动作 `CausedCaptures`，报告行断言收紧到含"其中直接导致提子 N 次"与"改造直接导致提子 1 次；首次改造平均第 2 大回合"。变异 **M-C3** 同时证明"烧林 0 次那一行"是**字符串**断言，跳过零行即红 |

另补两条小口子（同批修）：

- `当批不能站上新桥也不能拿它当跳板` 只覆盖了「下一批次可以使用」的前半句（可落子），补上后半句"以新桥为起点继续向外架桥"（两格宽河，T-4）。
- `TerrainWriter.ApplyAll` 的注释说"同一目标被改两次必然在**前提校验**处抛出"与实现不符（前提按 `before` 判，两次都过得去，实际是第二次 `Apply` 按累加后的 `terrain` 抛的）。行为对、注释错，已改。

**未修（3 条，均属后续段或需裁决）**

1. **`.trellis/spec/core/boundaries.md` 的守门名单仍写"只允许 `Adjacency` 自身与 `GameBoard.Neighbors`"**，而测试名单已加第 ③ 条 `TerrainEditRules`，文档滞后于代码。属 **6.2（段 E）**，且 6.2 要求三处一起改（两行唯一实现 + "地形不再是对局内不变量"），不在检查阶段自行动刀——段 B 偏离 3 已记。
2. **`距离表按传入地图现算`（缓存清单第 7 项）名不副实**：测试断言的是 `IsUnbridgedDeepWater` 与 `PlayableCount`，**没有调用 `MapValidator.DistanceTable`**。结论本身（纯函数、唯二调用方只看开局地图）经代码核对属实，但这条测试挡不住"有人给 `DistanceTable` 加缓存"。建议段 E 或下轮补一条真调它的用例。
3. **真实跑局样本里"致提子的改造"依赖种子**：种子 1 那一局没有致提子的改造，逐条转录里的 `CausedCapture` 要靠种子 1–3 合起来才校得到（已加 `Assert.Contains(records, r => r.CausedCapture)` 作样本下界）。若将来 AI 或地图变动使这三局都不再致提子，该断言会响亮失败而不是静默失效——这是有意的。

### 第 2 项结论：唯一实现成立，缓存清单抽查 3 项属实，另核掉一个盲点

- **地形写入口**：全仓 `new TerrainData(` 的调用方只有 `TerrainWriter` / `TerrainData.Flat` / `MapFile` / `FourPlayerBaseMap`，由 IL 扫描守门 `地形写入口之外不得构造改造后的地形` 钉住（含"扫描器确实命中写入口"的反面断言），M-B1 已证红。`GameBoard.ApplyTerrainEdits` 是唯一写入路径，调用点四处：`BatchRehearsal`（预演第 4 步）、`SettlementDriver.Confirm`（正式第 3 步）、`SettlementDriver.AttributeEdits`（探针，在 `Clone` 上）、`GameBoard.Fill`（存档回放）。
- **改造合法性**：`TerrainEditRules.*` 全仓调用点**只有两处**——`BatchRehearsal.ValidateShape`（`EditorType` + `Reject`）与 `HeuristicTurnController.EditOptions`（`EditorType` + `LegalTargets`）。枚举与判定同类同源，守门 `拒绝理由与合法目标集合一致` 穷举全盘 144 条几何边比对；M-T2 已证"改其一即红"。
- **`Adjacency.Neighbors` 守门名单**：测试侧已按需加 ③ `TerrainEditRules`（只放这一个类型），文档侧未更新——见上「未修 1」。
- **缓存清单自行抽查 3 项**：
  - 第 3 项（`Adjacency` 不缓存）**属实**：`Adjacency.cs` 里没有任何 `static readonly` 坐标派生表 / `Lazy<` / 字典缓存，唯一的 `private static` 是纯函数 `Receives`。
  - 第 10 项（AI 不持有 `MapData`）**属实**：`src/Siege.Core/Ai/*.cs` 里 `MapData` 只出现一次，是 `EditOptions(MapData map, …)` 的形参；无字段。
  - 第 12 项（godot 渲染态每次重建）**属实**：`BoardView.Build` 开头就 `_tileMaterials.Clear() / _tileBase.Clear() / _levels.Clear()` 再整体重填，`GameRoot` 在建局与两处刷新点都调 `_board.Build(...)`。
- **另核一个盲点（implement.md 未记）**：`GameBoard.Fill` 现在会重放改造段，若有人拿**已含改造的** `Board.Map` 去 `GameBoard.Restore(map, serialized)`，改造会二次应用而抛 `FormatException`。核对结果：`Restore` / `RestoreUnvalidated` 的调用方只有 `MatchFlow.Persistence`（传的是调用方给的开局地图），`Siege.Presentation` 与 `src/godot` 一次都没调过——**无此调用，无需处理**。

### 第 5 项结论：日志与分析

- **"回放可重建终局地形"原先是假绿**，已按上表第 6 条改成真的逐项比对（桥集合 / 栅栏集合 / 每格地表、可落子性、高度），并补了"日志是 Core 留痕的逐条转录"。M-C5（游标差一，本局最后一条改造永远写不进日志）与 M-C6（`CausedCapture` 恒写 false）在旧断言下**全绿**、在新断言下**各红 1**——这正是旧测试挡不住的两类缺陷。
- **第 11 项每个指标都有测试**：`Matches` / `Skipped` / `TotalEdits` / `MeanEditsPerMatch` / `MatchesWithoutEdit` / `MeanFirstEditRound` / 三种动作的 `Count` 与 `Share` / `ArtisansPlaced` / `ArtisansWithEdit` / `EditingArtisanShare` / `WinRateOfEditors` / `FinalBridges` / `FinalFences` / `FinalBurns` 原已逐项断言；**`CausedCaptures`（总数与逐动作）本次补上**，此前只钉 0。
- **缺改造字段的旧日志整局排除并计数**：`第11项改造分析按手算样本输出`（`Skipped == 1`、报告文案"排除缺改造字段的旧日志 1 局"）+ `一局里只要有一条快照缺改造字段就整局排除`（半旧半新也整局排除）；M-B11 已证红。
- **烧林 0 次仍输出该行**：报告断言是**字符串** `烧林：0 次（0.0%），其中直接导致提子 0 次`，不是"Count == 0"。M-C3（0 次跳过该行）当场红。本次 3 局真实跑局（种子 1–3，`--max-rounds 6`）实测报告确实输出了烧林 0 次那一行，与 T-11 的 20 局报告一致。

### 第 1 / 3 / 4 / 6 / 7 / 8 项

- **第 1 项（Scenario 覆盖）**：`terrain-edit` 的 21 条与 `terrain` 改造相关的 8 条，补完上表 1–4 后**逐条有测试**。任务点名的六项：批次内不链式三条（当批不能站上新桥 / 同一目标只能改一次 / 下一批次可以使用，后者本次补齐"继续向外架桥"）、不可逆且无归属两条（本次新增）、同形纳入设施（`棋子分布相同但多一道栅栏即不同形` + `BoardHistory` 实测 + 存档往返）、改造公开（本次新增）、`terrain` 四条改造后重算（架桥产生气边 / 立栅移除气边 / 烧林不改气边 / 烧林后可被覆盖 原已有；**架桥切断隔水覆盖 / 立栅不影响覆盖 本次新增**）。
- **第 3 项（顺序与原子性）**：预演七步与正式结算七步的实现顺序与规格逐条对得上；`SettlementDriver.Confirm` 的"应用改造"在 `RemoveStones` 之前、`OnRevealRelics` 之前，且 `AttributeEdits` 在改盘之前按批前地形算好。原子性本次补齐带改造的分支（M-C4 证红）。
- **第 4 项（自杀手与同形）**：改造导致的自杀手（内圈 `改造导致的自杀手` + 外圈 `外圈立栅把自己堵死也算自杀手`，两条都断言整批被拒后地形一个字节没变）；设施差异不构成同形（四份两两不同 + `BoardHistory`）；旧存档按无改造回填（`无改造时序列化与改造上线之前逐字节相同` + `对局存档往返保留改造与匠人权重` 含 `restored.Publish().ArtisanWeight == 18` 与旧存档 `== 10`）。M-B6 / M-B14 已证红。
- **第 6 项（`--artisan-weight` 与 `config.json`）**：`RunConfig.ArtisanWeight`（默认 `MatchOptions.DefaultArtisanWeight`、`Validated()` 校验非负）→ `MatchSession` 建局时与对局配置一致性校验 → 日志首部；`Program.cs` 经 `cli.GetInt("artisan-weight", …)` 注册（`strict-cli` 已上线，未注册选项直接报错）。`MatchPublicView.ArtisanWeight` 照 `SiteValues` 口径投影，`输出契约Tests` 的公开视图守门仍绿。
- **第 7 项（越段）**：`src/Siege.Presentation` 只改了 `FailurePresentation.TitleOf` 的两条 `case`（穷举 `switch` 不补会抛），`src/godot` **零改动**（`git diff --stat d4bfe02..HEAD -- src/godot` 为空）。4.1 / 4.2 / 4.3 一概未做。**本次检查也未动这两处**，因此未跑 Godot `--build-solutions`。
- **第 8 项（两处实锤过期缓存）**：`合法落子范围在保护期后含本局新架的桥` 读的是 `match.LegalRangeFor(P0)`、`MatchFlow的Map随改造更新` 读的是 `match.Map`——**都走真实 `MatchFlow` 调用路径**，不是自己重算一遍的恒真断言；M-B15 / M-B13 各红 1 与之吻合。

### 检查阶段变异验证逐条

脚本纪律同段 B / T-11：二进制读写、锚点按各文件**实测行尾**归一并 `assert count == 1`、备份名带时间戳、
还原写在 `finally` 并与"改之前读到的原文"逐字节 `assert`、`DOTNET_CLI_UI_LANGUAGE=en`、`PYTHONIOENCODING=utf-8`、
红绿以退出码为准、**不用 `if (false)`**（`TreatWarningsAsErrors` 下 CS0162 = 假红；M-C6 用的是运行时恒假的 `e.Sequence < 0`）。
六条全部与 M-B1～M-B15、M-T1～M-T3 不同。

| 编号 | 变异 | 文件（行尾） | 结果 | 红测 |
|---|---|---|---|---|
| **M-C1** | `Adjacency.CoverageTargets` 跨一格深水时不再检查是否已架桥（`IsUnbridgedDeepWater(t)` → `SurfaceAt(t) == DeepWater`，架了桥仍当水穿过） | `Board/Adjacency.cs`（LF） | EXIT 1，红 **1** | `地形写入口Tests.架桥切断隔水覆盖` |
| **M-C2** | `SettlementDriver.AttributeEdits` 的致提子口径由"严格变小"放宽成"不变也算"（`<` → `<=`） | `Batch/SettlementDriver.cs`（CRLF） | EXIT 1，红 **1** | `改造先于提子Tests.两道栅栏合围时两条都记致提子`（顺带架的桥被误记 true） |
| **M-C3** | `ReportWriter` 第 11 项跳过 0 次的动作行（烧林 0 次就不输出那一行） | `Sim/Analysis/ReportWriter.cs`（LF） | EXIT 1，红 **1** | `地形改造日志与分析Tests.第11项改造分析按手算样本输出` |
| **M-C4** | `SettlementDriver` 把第 1 步扣手牌挪到放置之后、写设施之前（外部观察者看到"棋子已落下但设施未写入"） | `Batch/SettlementDriver.cs`（CRLF） | EXIT 1，红 **3** | **`改造先于提子Tests.结算的原子性含改造`**、`正式结算顺序Tests.结算的原子性`、`结算驱动器步骤顺序` |
| **M-C5** | `MatchSession` 的改造游标差一（`_editCursor + 1 < Count`）：本局最后一次改造永远写不进日志 | `Sim/Running/MatchSession.cs`（LF） | EXIT 1，红 **1** | `地形改造日志与分析Tests.回放日志改造可重建终局地形并与对局逐项一致` |
| **M-C6** | `MatchSession` 把改造事件的"是否致提子"恒写 false（`e.CausedCapture && e.Sequence < 0`，运行时恒假） | `Sim/Running/MatchSession.cs`（LF） | EXIT 1，红 **1** | 同上 |

**M-C5 的红测只有新用例、M-C6 在补测试前实跑 `Passed! 44/44`**——两者都说明旧的"离线重建"用例挡不住（它只拿日志跟日志自己比），这是本次最大的一处假绿。
六条全部在 `finally` 里还原并与原文逐字节比对通过，还原后复跑全量 → EXIT 0、**887** 通过。

### 检查阶段改动的文件

| 文件 | 改动 |
|---|---|
| `src/Siege.Core/Board/TerrainWriter.cs` | 只改注释：`ApplyAll` 里"同一目标被改两次在何处抛出"的说法与实现对齐 |
| `tests/…/TerrainEditing/地形写入口Tests.cs` | 新增 `架桥切断隔水覆盖`、`立栅不影响覆盖` |
| `tests/…/TerrainEditing/改造合法性Tests.cs` | `当批不能站上新桥也不能拿它当跳板` 补第 ④ 段（下一批次可以继续向外架桥，两格宽河） |
| `tests/…/TerrainEditing/改造先于提子Tests.cs` | 新增 `结算的原子性含改造` |
| `tests/…/TerrainEditing/改造不可逆与公开Tests.cs`（新） | `匠人被提走后设施仍在` / `弃赛不撤销改造` / `改造结果人人可见且不显示改造者` |
| `tests/…/MatchTelemetry/地形改造日志与分析Tests.cs` | 新增 `回放日志改造可重建终局地形并与对局逐项一致`；`第11项改造分析按手算样本输出` 的手算样本加"致提子"并收紧报告断言 |

### 验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build` | EXIT 0，0 Warning 0 Error |
| 全量测试 | `dotnet test -c Release`（`set -o pipefail`） | EXIT 0，**887** 通过（基线 880 + 7） |
| 3 局冒烟 + 分析 | `run --out sim-out/check-3 --seed 1 --count 3 --max-rounds 6 --difficulty Standard` → `analyze` | EXIT 0，`FailedFiles: []`；第 11 项照常输出三行（烧林 0 次那行在），6 次改造 / 立栅 4 次其中致提子 3 次 |
| 变异 | M-C1 ～ M-C6 | 六条全红（EXIT 1），还原后逐字节一致，复跑 EXIT 0 / 887 |
| Godot | 未改 `src/godot` 与 `Siege.Presentation` | 按约定未跑 `--build-solutions` |

`sim-out/` 不提交（`sim-out/check-3` 已清理）。本次检查**未 commit**。

---

## 段 C（tasks 4.1–4.3：Presentation 与 Godot 的改造呈现）

不含第 5 / 6 组。**没有改规则、参数与任何 Core 规则口径**；Core 只加了一个把既有规则结果投影出去的预演字段。

### 改动文件

**Core（只投影，不新增规则）**

| 文件 | 改动 |
|---|---|
| `src/Siege.Core/Preview/BatchPreview.cs` | 新增 `EditOutlook(ArtisanCell, Chosen, Legal)` 与 `BatchPreview.EditOptions`；`BatchPreviewBuilder.Build` 对每枚暂放匠人调 `TerrainEditRules.LegalTargets(board.Map, 落点)`（改造合法性**唯一实现**，按批次开始前的地形，与预演第 1 步同源）。失败路径与第 1–2 步失败的早退路径都照样填——玩家正是要靠它换目标。**不在这里剔除"本批别人已选走的目标"**：批内唯一是 `BatchRehearsal` 的判定（`DuplicateEditInBatch`），复制一份就是第二实现 |

**Presentation**

| 文件 | 改动 |
|---|---|
| `Preview/PreviewPresentation.cs` | `HighlightKind` 加 `EditTarget` / `ChosenEdit`；新增 `EdgeHighlight(FenceEdge, HighlightKind)`（立栅的目标是**边**，格高亮表达不了）、`EditTargetView`、`ArtisanEditView`（带 `BridgeCells` / `BurnCells` / `FenceEdges` 三个读数）；`StagedPieceView` 加 `Edit` / `EditText`；`PreviewPresentation` 加 `ArtisanEdits` 与 `EdgeHighlights`。`ArtisanEdit()` 只标"哪个被选中"并拼文案，**不增删条目**（增删 = 表现层重判一次合法性） |
| `Style/VisualBaseline.cs` | `HighlightStyle` 加 `EditTargetHint`（半透明贴地虚线）/ `EditChosenMark`（实心立起）；`StyleOf` / `LayerOf` 补两条，两者都落在 `RenderLayer.PreviewHighlights`——tactical-layers 要求目标在**默认棋盘**上就可见，MUST NOT 依赖打开信息层 |
| `Layers/LayerContents.cs` | `LibertyGroupView` 加 `FenceSides`：地形里**恰有一端落在本串棋子上**的边。同一串两枚子之间不可能有栅栏（立栅当场分串），所以"恰一端"就是全部栅栏侧。纯数据过滤（读 `TerrainData.Fences`），**不碰 `Adjacency`**（它在表现层禁表里） |
| `Visibility/DefaultBoardView.cs` | 加 `Edits`（`GameBoard.TerrainEdits` 的投影，无归属）——**唯一用途**是让渲染层认出"这一帧刚多出来的那条改造"给一次落成反馈；注释写明 MUST NOT 据此把新设施画得与预置不同。`HasBridge` / `Fences` 的注释改写为"当前"（预置与本局新增在这里本来就分不出） |
| `Text/Labels.cs` | `TerrainEdit(edit)` →「搭桥 D4」「立栅 E5–E6」「烧林 F4」（动作名取 `TerrainEdit.DisplayName` 唯一一份）；`Edge(FenceEdge)` |

**Godot**

| 文件 | 改动 |
|---|---|
| `scripts/LowPoly.cs` | `ScaffoldParts` 由占位（四立柱 + 横梁）改为定稿：**偏心斜立木柄 + 柄顶横置宽槌头 + 一道斜撑**；新增 `EditEdgeMark(alongX, material, upright)`——贴边的三段虚线短条，`upright` 时另立一道 0.18 高亮板 |
| `scripts/BoardView.cs` | `TurnFlash` 加 `Edits`；`Build` 记下 `_zoneOwners` 与 `TerrainKeyOf(board)` 地形指纹，`Refresh` 开头指纹不符即整体重搭（**2.6 第 12 条的更正，见下**）；`Refresh` 加 `focus` 形参；`DrawPreview` 画候选 / 已选目标；`DrawFlash` 画落成反馈；新增 `AddEditFence`（位置算式与 `AddFence` 同一份，不引入第二份坐标换算） |
| `scripts/Visuals.cs` | `EditTarget`（冷青白 150,230,240）/ `EditChosen`（暖亮黄 255,226,120）/ `EditDone`（近白 255,250,220）三色，都不是木料色——与真设施一眼分得开 |
| `scripts/MatchSession.cs` | `CycleEdit(preferred)`：候选**整份**取自 `ArtisanEditView.Targets`，只做"取下一位"的下标运算；换目标靠撤回后原位重暂放（`StagedBatch.Replace` 只换类型、不带改造目标）。`StageArtisanPreferringBurn()`（演示用，见下）。`RunAiTurn` 的 `TurnFlash` 补 `[]` |
| `scripts/InputBindings.cs` | `CycleEditAction`（`E` / 手柄左肩键） |
| `scripts/Hud.cs` | 预演面板加「改造」一节：每枚匠人一行"落点 → 已选动作与目标（可选 N 个，[E] 轮换）" |
| `scripts/GameRoot.cs` | `NewEdits()` 按 `DefaultBoardView.Edits` 增量给出落成反馈（AI 结算 / 本人确认 / 演示三条路径统一走它，不改 `RunAiTurn` 与 `Confirm` 的签名）；`E` 键接 `CycleEdit(_hover)`；`Refresh` 传 `_hover` 作为 focus；`--rounds=N` 启动选项；`Capture` 另打四行（六种棋子各多少枚及坐标 / 本局改造 / 当刻暂放与改造高亮 / 落成反馈）；无人值守演示改动见下 |

**测试**

| 文件 | 内容 |
|---|---|
| `tests/…/BatchPreview/改造在预演中的呈现Tests.cs`（新，5 条） | `显示改造目标` / `显示可改造目标` / `可改造目标逐条来自改造合法性唯一实现`（全盘每格与 `TerrainEditRules.LegalTargets` 逐条相等且同序）/ `改造后的气与自杀风险` / `改造目标非法时仍列出全部合法目标` |
| `tests/…/TacticalLayers/改造在默认棋盘与棋串读法里的呈现Tests.cs`（新，3 条） | `暂放匠人时标出可改造目标`（全程不调 `world.Layer(...)`）/ `棋串读法区分栅栏侧` / `立栅后栅栏侧与差集都随改造更新` |
| `tests/…/TerrainEditing/改造不可逆与公开Tests.cs` | 新增 `默认棋盘上的新旧设施同形且不带改造者`（预置桥 D4 与本局架的 F4 三个字段逐项相同；两道栅栏混在同一集合；烧过的 H4 读作草地而 G4 仍是林地；`DefaultBoardView` 与 `PublicWorld` 的闭包里都没有 `TerrainEditRecord`） |
| `tests/…/BatchPreview/UI层不含规则计算Tests.cs` | `ForbiddenTypes` 加 `TerrainEditRules` / `TerrainWriter` |
| `tests/…/BatchPreview/Godot层不含规则计算Tests.cs` | token 表加 `"TerrainEditRules."` / `"TerrainWriter."` / `"ApplyTerrainEdits"`（**4.3**） |
| `tests/…/VisualStyleBaseline/六种棋子的轮廓语言Tests.cs` | 只改注释：匠人轮廓由"占位"改为"定稿"，指向 `art/artisan-v4/README.md` |

### 16 条边高亮的读法方案与理由

**方案：候选边一律贴在边上画半透明的三段虚线短条，不做任何偏移；一次只显示一枚匠人的候选。**

1. **几何事实替我们选了**：一枚匠人的 16 条候选边恰好是"以匠人格为中心的十字五格区域的 12 条外轮廓边 + 匠人格自身的 4 条边"。
   屏幕上就是**一个十字轮廓套一个小方框**，中心正是匠人——"这条边属于哪个落点"由图形本身回答，不需要额外线索。
2. **否决"朝匠人落点一侧收缩"**：判断一条边的哪一端靠匠人需要四邻判定，而 `FenceEdge` 构造即按字典序归一、端点角色已丢。
   表现层禁用 `Adjacency`、Godot 禁用 `.Neighbors(`，两边都不能自己算邻接；为此改 `TerrainEditRules` 的返回形状属于越界。
3. **多匠人靠"只显示一枚"解决**：光标停在哪枚已暂放的匠人上就显示那枚的候选，否则显示最后暂放的那枚——两个十字叠在一起才是真正看不清的情形。
   **已选**目标不受此限，全部匠人的一直都画：它是要提交的动作。
4. **候选 / 已选 / 真设施三层分开**：候选 = 贴地青白半透明虚线；已选 = 同样虚线 + 立起的暖黄实心亮板；真栅栏 = 棕木三柱两杆、高 0.30。
   明暗、实虚、立卧三条通道都不同，灰度下也分得开。
5. **选目标用键不用点边**：拾取原语是格（`BoardGeometry.TryPick` 的数学投影），边要再做一次消歧，代价不值。
   `E` 键在"不改造 → 目标 1 → … → 目标 N → 不改造"之间轮换。

### 2.6 第 12 条的更正

段 B 写的是"`BoardView` 的渲染态缓存不需要失效机制，因为 `GameRoot` 每次刷新都重跑 `_board.Build(...)`"。
**实际不是**：`Build` 只在 `_Ready`、选出生区、重开局三处被调用，`Refresh` 完全不碰地砖、水面、栅栏与 `_levels`。
不改的话，AI 架的桥 / 立的栅 / 烧的林一处都不会显示，且新桥格不进 `_levels` → 拾取拿不到它（`--pick-check` 在第 2 帧就跑完，抓不到这个回归）。
本段补上：`Build` 时记一份**地形指纹**（每格可落子 / 高度 / 地表 / 有无桥 + 全部栅栏边，只读默认棋盘视图模型），`Refresh` 开头指纹不符即整体重搭。

### 为什么没给 `MatchPublicView` 加"可改造目标"字段

派发提示提醒过这条路会先撞上 `改造结果人人可见且不显示改造者` 的反射守门。**结论是这条路根本不该走**：
"暂放匠人的合法改造目标"依赖**本人尚未确认的批次**，而暂放是私有信息（§13.2「对手尚未确认的批次部署」，
`敌方未确认批次不可读` 已钉住公开视图闭包里没有 `StagedBatch` / `Placement`）。所以它只能随**预演**走，进不了公开快照。

4.1 说的"公开视图含当前设施"由 `DefaultBoardView` 承担，且**段 B 就已经成立**（`HasBridge` / `Surface` / `Fences` 都从活的 `Map` 现取，
2.6 第 11 条有测试）；本段只把注释改准、加上供落成反馈定位用的 `Edits`，并补一条"新旧设施同形 + 闭包无改造者"的守门
（`DefaultBoardView` 与 `PublicWorld` 两个根都断言了 `TerrainEditRecord` 不可达）。
`ArtisanEditView` 与 `EdgeHighlight` 只出现在 `PreviewPresentation`（`ViewerWorld.Preview()` 的返回值，结构上只给本人）。

### 无人值守演示（`--auto-demo`）的三处改动

原先本人席位只 Pass 与摆第一种手牌，匠人与三种改造在自检里**一次都跑不到**。本段补三条（全在演示代码路径里，
不是 AI 估值、不动任何规则）：

1. `AutoPick()`：征募阶段真的挑一个候选（原先只渲染面板不点），有匠人就挑匠人。
2. `StageArtisanPreferringBurn()`：把匠人摆到"此刻能烧林"的落点（用"暂放 → 看预演给的目标 → 不合意就撤回"的笨办法找；
   判断只读 `ArtisanEditView.BurnCells`，界面一次都没自己算过合法性）。另外加了 `CanConfirm` 这一关——
   `LegalRange` 只过了预演第 1–2 步，落到会被闷死的格上整批会以自杀手被拒（实测第一版就踩了：D5 那座刚架的桥四面是敌子）。
3. 摆下匠人后按 `E` 轮换到目标；有烧林目标就一路轮到烧林——**烧林是三种动作里最难被跑到的**（全图 4 格林地 + 裁决 T-12 的候选拥挤，
   AI 实测 0 次；本段另扫了种子 1–30 × 14 大回合的 `--auto-demo`，AI 烧林仍是 **0 次**）。

副作用：`--auto-demo` 的终局数值与段 B 不同（本人现在会征募、会落匠人、会改造）。这是自检脚本的行为变化，不影响任何规则测试。

### 验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build siege.sln -c Release` | EXIT 0，0 警告 0 错误 |
| 全量测试 | `dotnet test -c Release --nologo`（`set -o pipefail`） | EXIT 0，**896** 通过（基线 887 + 9） |
| Godot 构建 | `$G --headless --path src/godot --build-solutions --quit` | **EXIT 0** |
| Godot 自检 | `$G --headless --path src/godot --quit-after 3000 -- --auto-demo` | **EXIT 0** |
| Godot 拾取 | `$G --headless --path src/godot --quit-after 3000 -- --auto-demo --pick-check` | **EXIT 0**，`可落子格 105，往返一致 105，失败 0`（**105/105**） |

`$G = D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`

### 截图

`art/artisan-v4/`，8 张 PNG + 1 张灰度版 + `README.md` 人工清单（画法约定、16 条边的读法、逐项核对 9 条、已知限制、存档表）。
全部出自同一条可复现跑法：默认种子 20260915、`--auto-demo --rounds=9`（不带 `--headless`）。

| 文件 | 尺寸 | 内容 |
|---|---|---|
| `art/artisan-v4/artisan-v4-edit-targets.png` | 1600×900 | 第 47 帧：J6 暂放匠人，已选 烧林 J5，候选 桥 1 / 栅 15 / 林 1；信息层关 |
| `art/artisan-v4/artisan-v4-fence-before.png` | 1600×900 | 第 39 帧：本局改造 0 处；D1 暂放匠人 + 已选 立栅 B1–C1（候选 10 条边） |
| `art/artisan-v4/artisan-v4-fence-after.png` | 1600×900 | 第 40 帧：改造 1 处（立栅 B1–C1）+ 落成反馈 |
| `art/artisan-v4/artisan-v4-bridge-before.png` | 1600×900 | 第 41 帧：改造 1 处，D5 仍是深水 |
| `art/artisan-v4/artisan-v4-bridge-after.png` | 1600×900 | 第 42 帧：改造 2 处（+ 搭桥 D5）+ 落成反馈 |
| `art/artisan-v4/artisan-v4-burn-before.png` | 1600×900 | 第 52 帧：改造 2 处，J5 仍是林地；J6 暂放匠人 + 已选 烧林 J5 |
| `art/artisan-v4/artisan-v4-burn-after.png` | 1600×900 | 第 53 帧：改造 3 处（+ 烧林 J5）+ 落成反馈；匠人 3 枚 |
| `art/artisan-v4/artisan-v4-six-pieces.png` | 1600×900 | 第 96 帧：六种棋子同屏——普通 10 / 堡垒 23 / 连珠 4 / 倍增 5 / 协同 8 / **匠人 4**（C1、M4、E5、C7） |
| `art/artisan-v4/artisan-v4-six-pieces-gray.png` | 1600×900 | 上图灰度版（PIL `convert('L')`） |

**截图未读进会话**；上表内容取自截图时控制台打印的视图模型读数。

### 变异验证逐条

脚本（`scratchpad/mutate_c.py`，**不入库**；命令 `python mutate_c.py M-SC1`，键名即下表编号）二进制读写、
按每文件实测行尾归一锚点并 `assert count == 1`、`finally` 还原后与原文逐字节 `assert`；
全部用**运行时恒假**的条件而不是 `if (false)`（段 B 教训：`TreatWarningsAsErrors` 下 CS0162 会变成"编译失败的假红"）。

| 编号 | 文件 | 变异 | 红掉的测试 | 还原 |
|---|---|---|---|---|
| **M-SC1** | `Core/Preview/BatchPreview.cs` | `EditOptions` 只留以落点为端的内圈边（回到 T-11 之前的口径） | EXIT 1，红 **4**：`显示可改造目标`、`可改造目标逐条来自改造合法性唯一实现`、`改造目标非法时仍列出全部合法目标`、`暂放匠人时标出可改造目标` | 逐字节一致 |
| **M-SC2** | `Presentation/Layers/LayerContents.cs` | `LibertyGroupView.FenceSides` 恒给 `[]` | EXIT 1，红 **2**：`棋串读法区分栅栏侧`、`立栅后栅栏侧与差集都随改造更新` | 逐字节一致 |
| **M-SC3** | `Presentation/Visibility/DefaultBoardView.cs` | `From` 把 `Edits` 恒传 `[]`（落成反馈失去数据来源） | EXIT 1，红 **1**：`默认棋盘上的新旧设施同形且不带改造者` | 逐字节一致 |
| **M-SC4** | `Presentation/Preview/PreviewPresentation.cs` | `ArtisanEdit` 里加一条恒假分支调 `TerrainEditRules.Reject(...)`（表现层自判合法性） | EXIT 1，红 **1**：`UI层不含规则计算Tests.表现层不调用规则计算入口` | 逐字节一致 |
| **M-SC5**（4.3 点名） | `godot/scripts/BoardView.cs` | `DrawPreview` 加 `if (_width < 0) { _ = Siege.Core.Board.TerrainEditRules.LegalTargets(null!, default); }` | EXIT 1，红 **1**：`Godot层不含规则计算Tests.Godot层不调用规则计算入口` | 逐字节一致 |

五条全部还原后复跑全量：EXIT 0、**896** 通过。

### 偏离与待决

1. **`--rounds=N` 是本段新增的启动选项**（缺省行为不变：自动演示 4、手动 15）。只为让自检跑得到岛心、拍得到烧林前后；
   规格与规则不受影响。需要负责人点头的话在段 E 一并处理。
2. **`--auto-demo` 的本人席位行为变了**（会征募、优先匠人、优先烧林落点、按 `E` 选目标）。终局数值与段 B 不同。
   这是自检脚本的覆盖度改进，不是 AI 估值改动；若负责人希望自检保持"纯 Pass"的旧形态，这三条可以整块回退——
   代价是三种改造的截图与 Godot 侧的改造链路再次无人跑过。
   **开销**：`StageArtisanPreferringBurn` 每个匠人回合最多把合法落子范围走一遍（105 格 × 一次暂放 + 重建世界 + 预演 + 撤回）。
   实测 `--auto-demo --rounds=9` 全程 11.7 秒、默认 4 大回合的 `--quit-after 3000` 通过；若以后收紧 `--quit-after`，先看这里。
3. **AI 仍然从不烧林**：本段另扫种子 1–30 × 14 大回合的 `--auto-demo`，AI 烧林 0 次，与裁决 T-12 的观察一致。
   扫档（段 D）时第 11 项的烧林次数若仍接近 0，D-H 的"再单开一轮加林地"就该被提上来。
4. **2.6 第 12 条的说法有误，已在本段更正并补实现**（见上）。段 E 写 `boundaries.md` 时请连带把这条更正带上。
5. **第 9 项人工检查（棋串读法的栅栏侧）没有截图**：它要在运行时按住 `1` + `Tab` 才出现，`--auto-demo` 的信息层轮换帧
   与"挨着栅栏的棋串"不一定同时成立。数据层由 `棋串读法区分栅栏侧` 与 `立栅后栅栏侧与差集都随改造更新` 钉住，
   画面需要人工跑一次。
6. **落成反馈是 1.2 秒的瞬时效果**，静态截图只能证明"有"，"够不够显眼"要跑一次真机看（人工清单第 4a / 5a / 6a 项）。
7. **未 commit**（按派发要求）。`art/artisan-v4/` 是新增目录，需要随本段一起提交。

---

## 段 C 检查

检查范围：tasks 第 4 组 4.1–4.3 的未提交改动。**未 commit**。没有动规则、参数、日志与分析（越段项一条也没碰）。

8 张截图**全部作废重拍**（三处系统性取景缺陷，见下表 1 / 2 / 6）；`six-pieces` 的帧号由 96 改为 **91**，其余 7 张帧号不变。

### 已修的问题

| # | 位置 | 问题 | 修法 |
|---|---|---|---|
| 1 | `src/godot/scripts/GameRoot.cs` `Capture` | **截图差一帧**：`GetViewport().GetTexture()` 拿的是<b>上一帧已绘制</b>的画面，在 `_Process` 里直接取，截到的是本帧刷新<b>之前</b>的状态。控制台读数与图像因此系统性错位一帧——旧 `artisan-v4-edit-targets.png` 打印"J6 暂放匠人 + 候选 桥1/栅15/林1"，图上却是上一轮确认之后的盘面：<b>一枚暂放匠人都没有，一条候选边高亮都没有</b>，这张图不成立 | 新增 `BeginCapture` + `CaptureWhenDrawn`：`await ToSignal(RenderingServer.Singleton, FramePostDraw)` 之后才取画面；并置 `_shotPending` 冻结 `Drive` 与刷新，画面不再变，打印的读数即图像所示 |
| 2 | 同上 | **面板遮挡**：无人值守演示每个部署回合都会开一次「全玩家手牌信息面板」（`AutoDeployStep` 第 `all.Length+1` 步），它整块盖住棋盘。旧 `artisan-v4-fence-after.png` 与旧 `artisan-v4-burn-after.png` 都被它盖住，看不到栅栏落成 / 烧林落成 | `BeginCapture` 取图前先 `_layers.Back()` + `_handPanel.Back()` 再重刷一次；`Capture` 那行另打「手牌信息面板 开/关」，让每张图自证取景 |
| 3 | `art/artisan-v4/*.png` | 8 张图全部作废（问题 1 是系统性的） | **全部重拍**，帧号不变（`--auto-demo --rounds=9`，默认种子 20260915）；灰度版一并重生成 |
| 4 | `art/artisan-v4/README.md` 第 5b 项 | **证据不成立**：原写"新桥可被拾取：`--pick-check` 105/105"，但 `--pick-check` 在第 2 帧就跑完，此刻一处改造都没发生，105/105 证明不了"重搭后新桥格能被拾取" | 改写为以像素差为证据的"新桥被画出来了 = 重搭确实发生"（地砖/水面/桥只在 `Build` 里搭，画面变了即证明 `Refresh` 触发了重搭；`_levels` 同一次 `Build` 更新）。原口径的真证据需要给 `--pick-check` 加"第几帧再跑"的形参，**本段没做**，见「未修」 |
| 5 | `src/godot/scripts/GameRoot.cs` + 4 个测试文件 | **凭空多出 UTF-8 BOM**：`GameRoot.cs`、`Godot层不含规则计算Tests.cs`、`UI层不含规则计算Tests.cs`、`改造不可逆与公开Tests.cs` 原本无 BOM 却被加上，两个新测试文件也带 BOM。全仓库其余 `.cs` 一律无 BOM | 全仓扫描并剥除，扫完仓库 BOM 数为 0 |
| 6 | `art/artisan-v4/artisan-v4-six-pieces.png` + `Hud.cs` / `GameRoot.cs` | **第三处遮挡**：中央面板还有两种关不掉的——终局结算面板（`Hud.RefreshCenter` 首个分支，700×220 压在棋盘正中）与征募面板（520×330）。原 `six-pieces` 取第 96 帧，正落在终局之后，中央被结算面板压住（紧中心框里面板底色占 **0.151**，其余图 0.000）；第 84 帧则撞上征募面板（**0.552**） | `six-pieces` 改取**第 91 帧**（终局前最后一帧，六种棋子齐全）；`Hud` 新增 `CenterPanelOpen`，`Capture` 那一行改打「中央面板 开/关」——这两种面板是对局状态、不是按键开关，`BeginCapture` 不该去动它们，改成**选帧时自证** |
| 7 | `tests/…/BatchPreview/改造在预演中的呈现Tests.cs` | **覆盖缺口**：「可改造目标按<b>批次开始前</b>地形枚举」（design 默认 2 / D-B′）只被 `显示改造目标` 间接钉住（经 `IsChosen`），没有直说这件事的用例 | 新增 `可改造目标按批次开始前的地形枚举`：同批两枚匠人，第一枚要烧的林地对第二枚<b>仍然</b>是候选，且与 `TerrainEditRules.LegalTargets(批前 Map, …)` 逐条相等。全量 896 → **897** |

### 未修（留给负责人 / 后续段）

1. **`--pick-check` 拿不到"改造之后"的盘面**。要给第 5b 项一个真证据，得让 `--pick-check` 支持"第几帧再跑"（默认仍是第 2 帧、三条正式命令不变，架桥后应得 106 格）。这是**新增功能**，属实现方的取舍，检查侧不替它决定。
2. **`--rounds=N` 不按 `--auto-demo` 收口**（`ReadRounds(args) ?? (_autoDemo ? 4 : Default)`）：手动开局带 `--rounds=3` 也会生效。这与既有的 `--seed` 完全同形（`ReadSeed` 也不收口），缺省行为不变，判定为**可接受**，但需要负责人在段 E 点头。
3. `art/artisan-v4/artisan-v4-edit-targets.png` 与 `artisan-v4-burn-before.png` **逐字节相同**（同一刻：J6 暂放 + 已选烧林 J5；中间几帧只在轮换信息层，而截图一律先关面板）。保留两份是为让第 3 项与第 6 项各自成对，README 已注明。
4. **F12 手动截图仍走旧的直取 `Capture`**（`GameRoot` 第 559 行附近），同样差一帧、也不关面板。它不在段 C 的自检路径上，检查侧**没有改**——要不要一并走 `BeginCapture`，由实现方定。
5. **`--auto-demo` 下改造落成反馈实际只活一帧**：`bridge-before`（第 41 帧）控制台打「落成反馈：无」，而第 39 帧的立栅亮板按 1.2 秒本应还在——原因是 `MatchSession.RunAiTurn` 用 `new TurnFlash(placed, captured, [])` 把 `Edits` 覆盖掉了。自动演示 `_pause = 0`，下一次 AI 小回合紧接着就来；真机有 `AiPauseSeconds` 缓冲，人能看到。这是段 C 实现的可视表现口径问题，**检查侧没有改**（改它要动 `TurnFlash` 的合并语义），留给负责人判断。
6. 人工清单第 1 / 2 / 3a / 3b / 4a / 5a / 6a / 6b / 7 / 8 / 9 项仍需人看图确认（检查侧同样没有把截图读进会话，只数了像素）。

### 重拍后每张图应当看到什么（供人工核对）

帧号全部未变；每一行都与截图那一刻控制台打印的视图模型读数逐项对上，8 张实测均为「信息层 关，手牌信息面板 关」。

| 文件 | 帧 | 应当看到 |
|---|---|---|
| `artisan-v4-edit-targets.png` | 47 | **J6 上有一枚半透明发光的暂放匠人**；以它为中心的"十字轮廓套小方框"青白虚线短条 **15 条**（十字五格区域 16 条边减去预置栅栏 J7–J8）；**J5 是已选目标**，暖亮黄亮底 + 实线环；另有 **1 个青白候选深水格**可搭桥。盘上本局改造 2 处（栅 B1–C1、桥 D5）。棋盘中央无任何面板 |
| `artisan-v4-fence-before.png` | 39 | 本局改造 **0** 处；**D1 上有一枚暂放匠人**，其候选边 **10 条**青白虚线短条；**B1–C1 之间是暖亮黄的立起亮板**（已选立栅），该处**还没有**棕木栅栏 |
| `artisan-v4-fence-after.png` | 40 | **B1–C1 之间有一道新栅栏**（棕木三柱两杆，与预置的 G5–H5 / E6–E7 / J7–J8 / F9–G9 外观一致）；**候选虚线已全部消失**（批次已确认）；那道边上另叠一道近白亮板 = 落成反馈。D1 上现在是一枚实体匠人。中央无面板 |
| `artisan-v4-bridge-before.png` | 41 | 本局改造 1 处（只有栅 B1–C1）；**D5 仍是蓝色深水**；无暂放、无落成反馈 |
| `artisan-v4-bridge-after.png` | 42 | **D5 由深水变成与同层地砖齐平的木板桥面**（与预置桥 G4 / D7 / K7 / G10 外观一致）；D5 格亮底 + 近白亮环 = 落成反馈；**E5 上多了一枚实体匠人** |
| `artisan-v4-burn-before.png` | 52 | 与 edit-targets 同一刻（逐字节相同）：**J5 仍是深绿林地 + 小树**；J6 上有暂放匠人，J5 是暖亮黄的已选目标 |
| `artisan-v4-burn-after.png` | 53 | **J5 由深绿林地变成普通草地、三棵小树全部消失**（读起来就是草地）；J5 格亮底 + 近白亮环 = 落成反馈；**J6 上是一枚实体匠人**（盘上匠人 3 枚：D1、E5、J6）；本局改造 3 处。中央无面板 |
| `artisan-v4-six-pieces.png` | 91 | **棋盘中央没有任何面板**（终局前最后一帧）。六种棋子同屏：普通 10 / 堡垒 23 / 连珠 4 / 倍增 5 / 协同 8 / **实体匠人 3（C1、E5、C7）**；匠人是画面里唯一不对称的轮廓（斜立木柄 + 顶部横置槌头 + 斜撑）。另有 **M4 上一枚半透明的暂放匠人**、其 15 条青白候选边、K4–L4 的暖黄已选亮板；B2–C2 上一道近白亮板（当刻的立栅落成反馈）；本局改造 5 处 |
| `artisan-v4-six-pieces-gray.png` | — | 上图灰度版：匠人仍是唯一"歪"的轮廓，与塔楼子（顶部一圈四枚小方块）分得开 |

### 取景自查（脚本数像素，没把图读进会话）

以旧 `fence-after`（已知被面板盖住的样本）中心区主色 `[22,26,34]` 为面板参照：

- 宽中心框（x 22–80%、y 18–86%）面板底色占比：旧 `fence-after` **0.339**、旧 `burn-after` **0.334**，其余 0.137–0.235。
- 更灵敏的**紧中心框**（x 35–65%、y 30–70%，面板在那里成一整块、地砖不会）：旧 `six-pieces`（第 96 帧）**0.151**、第 84 帧候选 **0.552**（征募面板）；
  **最终 8 张紧框全部 0.000**、宽框 0.137–0.141 → 三处遮挡（旧 `fence-after` / 旧 `burn-after` / 旧 `six-pieces`）全部修掉，没有新遮挡。
- 青白候选像素（棋盘中心区）：新 `edit-targets` / `burn-before` **2053**、新 `fence-before` **2014**、新 `six-pieces` **2463**（M4 的 15 条候选边）；旧 `edit-targets` 只有 **252**（= 无候选）→ 问题 1 坐实且已修。
- 三对"前后"在棋盘区各只有**一处**局部变化（`diff>40` 按 40×40 分块）：立栅 = y680–720 x520–720 的横带；搭桥 = y480–560 x600–720（深水蓝像素少 1671）；烧林 = y440–560 x840–960。没有整屏漂移 → 没有截错帧。
- 旧图与新图的错位可直接对上：旧 `bridge-before` 的像素统计 = 新 `fence-after`，旧 `bridge-after` = 新 `bridge-before`，正好差一帧。

### 逐条核对（派发清单）

| 项 | 结论 |
|---|---|
| 规格场景逐条有测试 | **成立**。预演显示改造目标与动作 = `显示改造目标`；按改造后地形算气与自杀风险 = `改造后的气与自杀风险`（含"不改造就没风险"的对照）；可改造目标标示且不依赖信息层 = `暂放匠人时标出可改造目标`（全程不调 `world.Layer(...)`，并断言 `LayerOf(EditTarget) == PreviewHighlights ≠ TacticalOverlay`）；棋串读法区分栅栏侧 = `棋串读法区分栅栏侧` + `立栅后栅栏侧与差集都随改造更新`；改造公开且不显示改造者 = `默认棋盘上的新旧设施同形且不带改造者`（`DefaultBoardView` 与 `PublicWorld` 两个闭包都断言 `TerrainEditRecord` 不可达）；六种棋子轮廓 = `六种棋子的轮廓语言Tests`；新旧设施同形 / 烧过的林地读作草地 = 同上那条 |
| 单一实现 | **成立**。`UI层不含规则计算Tests.ForbiddenTypes` 加了 `TerrainEditRules` / `TerrainWriter`（IL 级反射扫表现层全部方法）；`Godot层不含规则计算Tests` token 表加了 `TerrainEditRules.` / `TerrainWriter.` / `ApplyTerrainEdits`（4.3）。两条都有实做变异（M-SC4 / M-SC5）红过 |
| `BatchPreview` 调 `LegalTargets` 的地形与"批内唯一" | **成立**。传的是 `board.Map`，`board` 即 `Build(board, context, placements, …)` 的<b>批次开始前</b>盘面，与预演第 1 步同源；不是 `rehearsal.ProjectedBoard`。新变异 **M-SC6** 把它换成 `ProjectedBoard` → 红 2。**没有**重复实现"批内唯一"：`EditOptions` 不剔除本批别人选走的目标，该判定留在 `BatchRehearsal` 的 `DuplicateEditInBatch` |
| 偏离 3（`--auto-demo` 行为 + `--rounds`） | **只影响自检演示**。`AutoPick` / `StageArtisanPreferringBurn` / `AutoDeployStep` 三处全在 `if (_autoDemo)` 的演示驱动路径里，不在 AI 估值、不在规则层；`Siege.Sim` 的跑局完全不经过 `src/godot`。`--rounds` 只改 `MatchOptions` 的大回合上限，缺省不变。**未被 `strict-cli` 覆盖**：`CommandLine.EnsureRecognized`（未知选项检查）只在 `Siege.Sim/Program.cs` 里调用；Godot 侧走 `OS.GetCmdlineUserArgs()` + `Contains`/`StartsWith`，从来没有未知选项拒绝——`--seed` / `--screenshot` / `--auto-demo` / `--pick-check` 都是这个形态，`--rounds` 与它们同形，不是本段引入的新缺口 |
| 偏离 2（`BoardView.Build` 与地形指纹重搭） | **属实**。`_board.Build(...)` 全仓仅 3 处调用：`GameRoot._Ready`（第 67 行）、自动演示选出生区（第 326 行）、手动选出生区（第 646 行）——`Refresh` 路径上一处都没有。所以 2.6 第 12 条"`GameRoot` 每次刷新都重跑 `Build`"确实是错的，地形指纹重搭是必需的。**记给段 E**：更正 `.trellis/spec/core/boundaries.md` 2.6 第 12 条（注意 implement.md 段 C 把三处写成"`_Ready`、选出生区、重开局"，实际是"`_Ready` + 两处选出生区"，一并改准） |
| 越段 | **没有越段**。Core 只动 `Preview/BatchPreview.cs` 一个文件且只新增投影字段 `EditOptions`；规则层（`TerrainEditRules` / `TerrainWriter` / `BatchRehearsal` / 结算）、参数、日志与改造分析一行未动；4 个既有测试文件的改动全是**新增**断言 / 禁表项 / 注释，没有任何既有断言被改弱 |

### 变异验证（新增一条，与 M-SC1～M-SC5 不同）

脚本 `scratchpad/mutate_c.py`（**不入库**）：二进制读写、按文件实测行尾归一锚点并 `assert count == 1`、`finally` 还原后与原文逐字节 `assert`；不用 `if (false)`（`TreatWarningsAsErrors` 下 CS0162 会变成"编译失败的假红"），本条是直接替换实参、无死代码。

| 编号 | 文件 | 变异 | 结果 | 还原 |
|---|---|---|---|---|
| **M-SC6** | `Core/Preview/BatchPreview.cs` | `TerrainEditRules.LegalTargets(board.Map, p.Coord)` → `LegalTargets((rehearsal.ProjectedBoard ?? board).Map, p.Coord)`：可改造目标改按<b>本批结算后</b>的地形枚举，违反 design 默认 2「批次内不链式」 | EXIT 1，红 **2**：`改造在预演中的呈现Tests.显示改造目标`、`改造在预演中的呈现Tests.可改造目标按批次开始前的地形枚举` | 逐字节一致 |

（第一次跑 M-SC6 时只红 1 条，暴露出上表问题 6 的覆盖缺口；补完用例后红 2。）

### 验证结果

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build siege.sln -c Release` | **EXIT 0**，0 警告 0 错误 |
| Godot 构建 | `$G --headless --path src/godot --build-solutions --quit` | **EXIT 0** |
| 全量测试 | `dotnet test -c Release --nologo`（`set -o pipefail`） | **EXIT 0**，**897** 通过（段 C 实现 896 + 检查补 1） |
| Godot 自检 | `$G --headless --path src/godot --quit-after 3000 -- --auto-demo` | **EXIT 0** |
| Godot 拾取 | `$G --headless --path src/godot --quit-after 3000 -- --auto-demo --pick-check` | **EXIT 0**，`可落子格 105，往返一致 105，失败 0` |

## 段 D 扫档（主会话执行）

全部批次种子 1–200、4 人 Standard、大回合上限 15、v4、据点分值 5/15/45；每批 run / analyze 退出码 0，config.json 实际生效值逐批核对一致。

### 第 1 步：匠人征募权重三档（Safety 27）

| 指标 | artisan-w5 | artisan-w10 | artisan-w18 | sites-final |
|---|---|---|---|---|
| 领先者胜率A | 35.5% | 45.5% | 51.5% | 25.5% |
| 第4回合先手平均名次 | 2.13 | 1.92 | 1.88 | 2.19 |
| 整局无提子局 | 0 | 0 | 1 | 0 |
| 据点分占比 | 26.4% | 30.1% | 35.7% | 22.1% |
| 首次冲突 | 4 | 4.05 | 4.12 | 4 |
| 终局局平均结束 | 7.97 | 8.86 | 9.22 | 7.78 |
| 终局原因 | AllPassed×117，MajorRoundLimit×76，PowerDominance×7 | AllPassed×94，MajorRoundLimit×89，PowerDominance×17 | AllPassed×62，MajorRoundLimit×114，PowerDominance×24 | AllPassed×182，MajorRoundLimit×13，PowerDominance×5 |
| 不收敛率 | 38.0% | 44.5% | 57.0% | 6.5% |
| 碾压成立回合 | 11 | 11.29 | 10.33 | 10 |
| 篝火控制/争议 | 44.6%/27.7% | 41.3%/34.1% | 43.2%/33.3% | 43.8%/19.8% |
| 石碑控制/争议 | 17.0%/53.5% | 23.3%/49.8% | 28.8%/46.3% | 8.7%/52.4% |
| 篝火主人占被控制 | 13.7% | 16.6% | 13.5% | 10.6% |
| 石碑桥头家占被控制 | 26.7% | 25.2% | 25.4% | 25.5% |
| 高地加值占比 | 0.9% | 1.1% | 1.5% | 0.6% |
| Pass率 | 13.7% | 11.9% | 9.9% | 22.1% |
| 出生区胜率 | 24.5/24.5/29.5/21.5 | 23.0/28.0/24.0/25.0 | 26.0/26.5/25.0/22.5 | 20.0/24.5/28.5/27.0 |

改造分析：每局改造 2.53 / 5.6 / 10.6 次；搭桥 117/322/634、立栅 388/798/1485、烧林 0/0/1；改造致提子 196/365/697；每批次提子 0.67/0.75/0.91（scoring-sites 终版 0.29）。推荐权重 5：领先者胜率 35.5| 指标 | artisan-w5-s25 | artisan-w5 | artisan-w5-s30 | artisan-w5-s35 | artisan-w5-s40 | artisan-w5-s45 |
|---|---|---|---|---|---|---|
| 领先者胜率A | 44.0% | 35.5% | 34.5% | 24.0% | 22.5% | 23.0% |
| 第4回合先手平均名次 | 1.95 | 2.13 | 2.07 | 2.23 | 2.42 | 2.43 |
| 整局无提子局 | 0 | 0 | 0 | 0 | 78 | 94 |
| 据点分占比 | 24.6% | 26.4% | 26.5% | 31.5% | 34.9% | 35.6% |
| 首次冲突 | 4 | 4 | 4.01 | 4.01 | ? | ? |
| 终局局平均结束 | 8.31 | 7.97 | 7.78 | 7.56 | 7.55 | 7.52 |
| 终局原因 | AllPassed×116，MajorRoundLimit×76，PowerDominance×8 | AllPassed×117，MajorRoundLimit×76，PowerDominance×7 | AllPassed×129，MajorRoundLimit×67，PowerDominance×4 | AllPassed×143，MajorRoundLimit×52，PowerDominance×5 | AllPassed×121，MajorRoundLimit×76，PowerDominance×3 | AllPassed×123，MajorRoundLimit×74，PowerDominance×3 |
| 不收敛率 | 38.0% | 38.0% | 33.5% | 26.0% | 38.0% | 37.0% |
| 碾压成立回合 | 10.38 | 11 | 9.5 | 9.2 | 11.33 | 10 |
| 篝火控制/争议 | 43.4%/33.3% | 44.6%/27.7% | 44.5%/22.8% | 45.2%/16.8% | 53.6%/13.0% | 54.2%/10.8% |
| 石碑控制/争议 | 19.2%/52.0% | 17.0%/53.5% | 14.8%/54.4% | 18.5%/48.3% | 18.8%/51.1% | 19.1%/50.2% |
| 篝火主人占被控制 | 9.1% | 13.7% | 19.3% | 19.6% | 20.3% | 18.7% |
| 石碑桥头家占被控制 | 26.1% | 26.7% | 26.0% | 25.4% | 20.8% | 21.9% |
| 高地加值占比 | 0.9% | 0.9% | 1.0% | 1.1% | 1.2% | 1.3% |
| Pass率 | 15.2% | 13.7% | 15.2% | 16.4% | 16.1% | 17.3% |
| 出生区胜率 | 23.0/28.0/27.0/22.0 | 24.5/24.5/29.5/21.5 | 21.5/27.5/25.0/26.0 | 21.0/30.5/22.5/26.0 | 23.5/24.0/24.5/28.5 | 28.0/24.0/23.5/24.5 |

每批次提子 0.63 / 0.67 / 0.60 / 0.52 / 0.55 / 0.51。40 与 45 档整局无提子 78 / 94 局，超出 §16「≤ 5| 指标 | artisan-w5-s35 | artisan-final |
|---|---|---|
| 领先者胜率A | 24.0% | 24.0% |
| 第4回合先手平均名次 | 2.23 | 2.23 |
| 整局无提子局 | 0 | 0 |
| 据点分占比 | 31.5% | 31.5% |
| 首次冲突 | 4.01 | 4.01 |
| 终局局平均结束 | 7.56 | 7.56 |
| 终局原因 | AllPassed×143，MajorRoundLimit×52，PowerDominance×5 | AllPassed×143，MajorRoundLimit×52，PowerDominance×5 |
| 不收敛率 | 26.0% | 26.0% |
| 碾压成立回合 | 9.2 | 9.2 |
| 篝火控制/争议 | 45.2%/16.8% | 45.2%/16.8% |
| 石碑控制/争议 | 18.5%/48.3% | 18.5%/48.3% |
| 篝火主人占被控制 | 19.6% | 19.6% |
| 石碑桥头家占被控制 | 25.4% | 25.4% |
| 高地加值占比 | 1.1% | 1.1% |
| Pass率 | 16.4% | 16.4% |
| 出生区胜率 | 21.0/30.5/22.5/26.0 | 21.0/30.5/22.5/26.0 |

全部指标与 Safety 35 档逐项相同（确定性复现）。改造 425 次：搭桥 97、立栅 328（致提子 119）、烧林 0；每批次提子 0.52。

遗留（本轮不处理，写进设计文档 §16）：不收敛率 26.0