# 续接说明（HANDOFF）

> 更新于 2026-09-28（`v2-recalibration` 归档后，HEAD `f47c1ec`，main 与 origin/main 一致）。仓库：`git@github.com:Skysolderone/geme_demo.git`。
> 读完本文即可在新会话中继续，不需要翻聊天记录。最近进度以 `.trellis/workspace/rubioc/journal-1.md` 最后一节为准。

## 一句话

《围杀 Siege》：2–4 人共享棋盘的回合制策略构筑游戏，玩法是围棋式提子加上征募构筑。现在已经可以玩：规则回到围棋内核（领地 + 军势、活形与活棋禁入），有四张内置图和随机生成图，十种棋子、十类信物，四档 AI（含一层前瞻的专家），带入带出也已做完。
规则内核 `src/Siege.Core`（.NET 8，零 Godot 依赖，AI 在 `Ai/`）；批量跑局、分析与终端版在 `src/Siege.Sim`；视图模型在 `src/Siege.Presentation`；3D 表现层在 `src/godot`（Godot 4.7.2 .NET 版，不在 `siege.sln` 里）。测试 `dotnet test -c Release`：**1775 通过 / 6 门控跳过**（2026-09-28 实测，约 3 分钟）。

## 现在就能玩

> 终端是 **Windows PowerShell**：exe 路径带引号时，前面要加 `&`；给 Godot 传参数时先写 `--%`，自定义参数一律放在 `--` 之后，并写成 `--名=值`。
> **试玩带入带出时请用临时档案**：缺省档案是真实的 `%APPDATA%\Siege\profile.json`（终端和图形版共用）。

**图形版（推荐）**。不带 `--map=` 启动时，先进入选图界面。界面上可以选四张内置图（标准 13×13、双人 9×9、三人 11×11、边疆 25×30）或随机图，也可以选难度。
```powershell
& "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe" --% --path E:/wws/geme_demo/src/godot -- --map=siege-2p-base-v1 --difficulty=Expert --profile=%TEMP%\siege-try.json
```
- 用户参数全部由 `src/godot/scripts/GameRoot.cs` / `GameRoot.Carry.cs` 经 `LaunchArgs` 严格解析，拼错会退出（退出码 1）：`--map=` `--seed=` `--difficulty=` `--cell-limit=` `--rounds=` `--profile=` `--no-carry` `--auto-demo` `--pick-check` `--screenshot=<路径>[:帧]` `--overview` `--shot-power` `--shot-groups` `--map-select` `--carry-preview=supply|settlement` `--export-parts=` `--piece-gallery=` `--shot-show=placement|capture|reveal|power|banner` `--reveal-preview=play|ladder` `--reveal-at=<毫秒>`。
- `--auto-demo` / `--pick-check` / `--screenshot=` 固定使用标准难度（同时给 `--difficulty=` 会报错），并关闭带入带出。
- 操作：点出生区插旗 → 征募 → 左下手牌选类型、点格子暂放 → 右侧看预演 → Enter 确认 / P 键 Pass；有“弃赛”按钮。
- 按键：1/2/3/4 分别看盘面、势力、信物、顺序层；Tab 切换盘面读法；H 打开手牌面板；T 切换按住 / 点击；E 轮换改造目标；M 切到全局预览；方向键或 WASD 平移；空格回到自家（`InputBindings.cs`）。

**终端版**
```powershell
dotnet run --project src/Siege.Sim -c Release -- play --map siege-3p-base-v1 --difficulty Hard --profile $env:TEMP\siege-try.json
```
可用选项：`--seed` `--players`（缺省取地图人数上限）`--seat` `--difficulty` `--map` `--cell-limit` `--profile <路径>` `--no-carry`（关闭带入带出，不读写档案；不能与 `--profile` 同时给）。对局中输入 `resign` / `弃赛` 弃赛，`q` 退出。

**批量跑局与分析**（每个配置不超过 20 局，见下文约定）
```powershell
dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/<目录> --map siege-4p-base-v5 --seed 1 --count 20 --difficulty Standard --flag-risk 15 --carry-in 0
dotnet run --project src/Siege.Sim -c Release -- analyze --dir sim-out/<目录>
```
`run` 另有这些选项：`--config <json>`（逐玩家的权重、内容集 `ContentSet` 只能在这里配）`--players` `--parallel` `--serial` `--turn-limit`（缺省 600，0 = 不截断）`--artisan-weight`（缺省 10）`--cell-limit` `--pass-threshold`（缺省 20）`--flag-risk`（插旗冒险概率 0–100，缺省 15）`--carry-in 0|1`（缺省 0 = 关闭）`--map-per-match` `--retention` `--sample-permille` `--gzip`。
其他子命令：`map --map <标识> [--out 文件]`（打印文本图、导出地图）、`replay --file <match-*.jsonl>`。不带参数运行会打印完整用法（`src/Siege.Sim/Program.cs` `PrintUsage`）。

**选项归属（已与代码核对）**：`--flag-risk`、`--pass-threshold`、`--carry-in` 只有 `run` 有，`play` 和图形版都没有；内容集没有命令行开关，新局一律用 v2。
**地图标识**（三个入口共用 `MapCatalog`）：`siege-4p-base-v5`（缺省）/ `siege-2p-base-v1` / `siege-3p-base-v1` / `siege-frontier-v2` / `gen:<地图种子>[:p5–8][:s1]` / 地图文件路径。只写 `gen` 时随机取一个种子并打印完整标识（带 `:s1`，即投放新地表）。
**难度**：`Easy|Standard|Hard|Expert`，不区分大小写，不接受数字（`AiDifficultyNames`），缺省 Standard。

## 现行规则与关键数值速览（设计文档 v1.18）

- **势力** = 领地分（独占空格数，荒漠不计）+ 全部棋串军势；军势 = ⌊(基础 + 位置加值) × 1.5ⁿ⌋，**不封顶**，用 BigInteger 计算（§10.1）。
- **部署上限**：第 1–3 大回合 3，第 4–6 大回合 4，第 7 大回合起 5，军令信物在此之上叠加（§5.4）。征募固定展示 5 张、免费取 3 张，已删除落后补偿（§5.3）。
- **出局**：曾有正势力、之后降到 0 即出局，保护期内也照样出局（§12.1）。
- **终局只有三类**：只剩一名参赛玩家 > 棋盘填满 > 整轮所有人 Pass；没有大回合上限、没有碾压胜。跑局里的 `turn_limit`（600 小回合）只是技术性截断，不产生名次（§12.3）。
- **活形**（§6.4）：沿气边判定空区，`EYE_SPACE_MAX = 12`，眼值按表相加，≥ 2 为已确定活形；已活棋串的眼空间对他人禁入，非所有者不得以任何手段破坏它。**单子不成活**：只有 1 枚子的棋串最多判“未定”。
- **同形判重**：比对键只看每格占用者 + 设施 + 地表，**不含棋子类型**（§6.2，`superko-occupancy`）。
- **十种棋子**：普通 / 堡垒 / 连珠 / 倍增 / 协同 / 匠人 / 旗手 / 铁链 / 哨兵 / 界碑，征募权重依次为 40/20/18/12/10/10/8/8/8/8。新增四种只提供位置加值，权重 8 未校准（§9）。
- **十类信物**：探勘 / 征召 / 兵站 / 军令 / 先锋 / 流派徽记 / 连营 / 犄角 / 驿站 / 工坊（§8.1）。
- **内容集**：v1 = 原来六种棋子 + 六类信物；v2 = 十种 + 十类。新局缺省用 v2；缺少内容集字段的旧存档或旧日志按 v1 处理（§8.1）。
- **插旗冒险概率** p = 15（初值，未校准；p = 0 时与旧行为逐项相同）（§4.1）。
- **AI**（§15.2）：九维评价，其中眼位 200 / 安全 35 / 威胁 25 已在 V1 上校准，其余六维沿用旧值，在 V2 上未重扫。**停手阈值 20**（在 V2 上复核，旧值 80）；己方盘上无子时阈值取 0。**2 人图专属权重覆盖为 Eye 50**，其余地图都用缺省表。
- **难度**（§15.2）：简单只看三维；候选点 / 候选批次：简单 6/1、标准 12/8、困难 24/32、**专家**在困难的基础上加一层前瞻，宽度 W = 4，模拟下一名对手回应一次。`expert-strength`（v1.18）新增的多样候选（多样补充上限 S）与近似两层加分（两层权重 λ‰）是可配置项，只在配置文件 `Search` 显式给出时生效；扩样结果为负（S = 8、λ = 1000‰ 的专家弱于困难），专家预设已退回一层（S = 0、λ = 0，与 v1.17 的专家逐步相同）。可落子格超过 150 的大图自动启用候选格上限 K = 24。
- **带入带出**（§21）：备用子 3 点、征召签 2 点、换型令 4 点，每局至多带 1 件；名次补给点（4 人局）为 24/16/12/10。三种结局：完赛全额、补给不返还；弃赛带出 50% 并返还补给（保护期内带出 0）；出局带出 0、补给丢失。AI 带入的件数与本机玩家相同；价格与点数都是初值。

## 地图

- **地图数据权威来源**：`siege-2p-base-v1` / `siege-3p-base-v1` 以手写的 `maps/*.json` 为准，编译时嵌入 `Siege.Core`（`Siege.Core.csproj` 的 EmbeddedResource）；v5 与边疆图由代码生成（`FourPlayerBaseMap.cs` / `FrontierMapV2.cs`），`maps/` 下的同名 json 是 `map` 子命令的导出物。`maps/siege-4p-base-v1/v2/v3.json` 只作历史参考，v1、v2 无法加载。
- **v5**：13×13、C4 对称，可落子 105 格，4 个出生区各 13 格（h=2），中央岛靠 4 座桥进出，信物 13 个（§3.3）。
- **2 人图**：9×9、C2 对称，可落子 61 格，信物 7 个；**3 人图**：11×11、竖轴镜像，可落子 86 格，信物 10 个（§3.3）。
- **边疆图**：25×30，6 个平台即出生区，前三大回合只能落自家平台。候选格上限 K = 24 自动启用。
- **生成器**：`gen:<种子>`，尺寸 25×30、4 人，平台 5–8 个（缺省 6），`:s1` 表示投放荒漠 / 沼泽 / 岩台 / 浅滩。日志与存档里记录地图摘要，生成器一改，旧日志会报“地图不一致”。
- 内置图内容一变，标识就要升号（`.trellis/spec/core/boundaries.md`）。

## 权威来源（按优先级）

1. `openspec/specs/`（31 个能力，Requirement / Scenario 是验收基准）与 `2026-09-10-siege-core-gameplay-design-v1.md`（**v1.18**，文末有变更记录，§15.2 列出已知问题）。`Siege-玩法介绍-v1.docx` 是玩家向介绍稿，与设计文档冲突时以 md 为准。
2. `openspec/changes/archive/<日期>-<change>/design.md` 末尾的“裁决记录”：设计文档没写到的地方以它判定。
3. `.trellis/spec/core/`：`index` / `boundaries` / `determinism` / `coordinates` / `testing`，都是踩过的坑，**必读**。
4. `.trellis/workspace/rubioc/journal-1.md`：各会话的结论与待办；`.trellis/tasks/archive/2026-09/<任务>/implement.md`：逐段记录。
5. 路线图 `openspec/ROADMAP.md`（已完成 / 未完成）；历史参考：`art/*/README.md`（各项人工检查清单）。

## 已完成的 change（2026-09-21 起）

| 日期 | change | 要点 |
|---|---|---|
| 09-22 | `restore-go-core-rules` | 领地 + 军势计分、倍率不封顶、据点摘除、归零出局、三类终局、删除落后补偿；v5 / 边疆 v2（设计文档 v1.5） |
| 09-22 | `life-shape` | 空区 / 眼空间 / 眼值、活棋禁入、不得破坏他人活形（v1.6） |
| 09-23 | `life-single-stone` | 单子不成活（R8 裁决 K1）（v1.7） |
| 09-23 | `terrain-surfaces` | 荒漠 / 沼泽 / 岩台 / 浅滩四种地表，另一会话完成后合入 main（`254964e`） |
| 09-24 | `ai-eye` | AI 扩为九维、活形硬约束、停手阈值；定值 Eye 200 / Safety 35 / Threat 25（v1.9） |
| 09-24 | `superko-occupancy` | 同形判重不再看棋子类型（v1.10） |
| 09-25 | `pass-threshold-first-stone` | 己方无子时阈值取 0，修复简单难度首回合全员 Pass（v1.11） |
| 09-25 | `flag-contest` | 插旗冒险概率 p = 15，使用 `flag-risk` 子流（v1.12） |
| 09-25 | `small-maps` | 2 人图 / 3 人图；参赛人数缺省取地图人数上限（v1.13） |
| 09-26 | `more-pieces-relics` | 新增四种棋子、四类信物，引入内容集 v1/v2（v1.14） |
| 09-26 | `carry-in-out` | 补给带入、战利品带出、本地档案、弃赛结算（v1.15） |
| 09-27 | `expert-lookahead` | 专家难度一层前瞻，难度名严格解析（v1.16） |
| 09-27 | `engagement-diagnosis` | 只是诊断（Trellis 任务，不是 openspec change）：2 人图不交战的原因是地图结构；负责人裁决规则不改 |
| 09-28 | `v2-recalibration` | 在 V2 上复核停手阈值，80 → 20；2 人图专属权重覆盖为 Eye 50（v1.17） |
| 09-28 | `expert-strength`（已实施、未归档） | 专家的多样候选与近似两层加分，作为可配置项保留；扩样结果为负，专家预设退回一层（v1.18） |

更早的 change（首轮原型 8 项、去围棋化三轮等）都已归档，对应口径已被上表覆盖。

## 数据现状（`sim-out/`，已加入 gitignore）

> **负责人：不再跑 200 局**。复核、基线和慢测试的 200 局版本都不安排；需要数据时先问可以接受的规模，默认每个配置不超过 20 局。

- **现行口径（内容集 V2、停手阈值 20、缺省权重）**：`sim-out/v2-recalibration/`
  - `pass-20`：v5、4 名标准 AI、种子 1–20。截断 0/20，整局无提子 3/20，已终局的对局平均在第 9.25 大回合结束，提子 118。
  - `2p-eye50-el8`：2 人图。整局无提子 13/20，平均第 8.35 大回合结束。
  - `easy20-pass20`（简单难度，截断 0/20）、`expert20-pass20`（1 专家 + 3 标准，截断 0/20）。
  - 同目录的 `pass-0/40/80` 与 `2p-eye*` 是扫档的其余档位。
- **专家强度（V2、阈值 20）**：`sim-out/expert-strength/`（诊断 H0 / E0–E6）与 `v118/`（高难 3 批、λ 扫档 3 批、专家扩样 2 批，共 160 局；配对 好 7 / 同 33 / 差 20，p = 0.019）。
- **V2、但停手阈值是 80**：`engagement-diagnosis/b01–b09`（`b10` 是 V1）、`more-pieces-relics/smoke20`、`carry-in-out/smoke20`、`expert-lookahead/smoke20`（专家胜 5/20，前瞻改变选择的比例只有 1.6%）。
- **V1 口径（只作历史参考）**：`ai-eye-*`（其中 `ai-eye-pass-80` 是当年的选定组合，另有 `ai-eye-final-gen` / `-frontier` / `-easy-eye`）、`life-shape/baseline200`、`life-single-stone`、`r8-explore`、`restore-smoke20`、`small-maps/*-smoke20`、`superko-occupancy`、`pass-threshold-first-stone`、`flag-contest`。
- **已作废**（规则回归之前的计分口径）：`sites-*`、`artisan-*`、`terrain-v3`、`baseline`、`safety*`、`denser-map-*` 等，09-21 之前的目录都属于这一类。
- 胜率类数字都是 20 局的小样本，只能看方向。数据口径的说明见设计文档 §15.2 与 §16。

## 已知问题与待办（按优先级）

1. **2 人图仍有 13/20 局整局无提子**：原因是地图结构（保护期结束时对方区已被封死，近侧侧翼容易做贴墙眼）。可以考虑做 2 人图 v2（`engagement-diagnosis`，§3.3）。
2. **V2 上九维权重没有完整重扫**；边疆图、生成图在阈值 20 下都没有复核，边疆图只有 V1 的 81 局部分样本（§15.2 已知问题第 2 条）。
3. **专家强度仍待提升**：一层前瞻的专家 20 局胜 4–5 局，与高难相近。`expert-strength`（v1.18）加的多样候选 + 近似两层（S = 8、λ = 1000‰）让前瞻改变 41% 的选择，但 v5 成对种子 1–60 扩样中弱于高难：配对 好 7 / 同 33 / 差 20，胜 6/60 对 12/60，平均名次 2.87 对 2.53，符号检验 p = 0.019。专家预设因此退回一层，两项保留为可配置项；下一步先归因（专家落子更少、Pass 更多），另开 change（设计文档 §15.2）。
4. **简单难度多人近循环**：V2 下 0/20 截断，V1 下 4/20，负责人裁决不改规则（§15.2 已知问题第 1 条）。
5. **需要负责人亲自试玩**：带入带出链路（用临时 `--profile`）、专家难度、人对 3 名 AI 打一局边疆图（检查清单在 `.trellis/tasks/archive/2026-09/09-23-ai-eye/implement.md` 段 E）。
6. **需要负责人看截图**：`art/carry-in-out/`、`art/expert-lookahead/`、`art/more-pieces/`（十种棋子灰度对照）、`art/surfaces-v1/`；另有 `sim-out/small-maps/*.png`、`sim-out/life-shape/*-groups.png`。
7. **设计文档中还没做的部分**：联网同步与匹配（§19-5），正式美术与音效（§18.2）。局外永久成长明确不做（§21.6）。
8. 小项：GitHub 默认分支已于 2026-09-28 改为 `main`（旧分支 `wip/match-flow` 已合入并于同日删除）；终端的出局 / 完赛结算没有脚本测试（journal 09-26）；种子 1–200 的截断慢测试在阈值改动后没有复核。

## 执行流程与工作约定

```
opsx:propose 开 change（proposal + design + specs + tasks）→ task.py create + start 建 Trellis 任务
  → 每段一个 Agent(trellis-implement)：写代码 + 测试 + 变异记录，不 commit、不改 openspec/
  → 主会话前台复核：sln 构建 + Godot 单独构建 + 全量测试（真实退出码）+ 必要时自己补一条变异 → 提交
  → 下一段 … → 设计文档升版 → openspec archive → task.py archive → journal 记一节
```
- **派子 agent 前**，把待决项写成编号裁决，连同派发方式选项一次性交负责人确认；逐项裁决时沿用负责人看到的表格行序和编号。设计级的问题问负责人，常规问题自己定并写明理由。
- **同一时间只跑一个任务**，跨会话也算：agent 在跑时，主会话不另起 dotnet、变异脚本或第二个 agent；另一会话在忙时，本线做完当前段就暂停。
- **不跑 200 局**，每个配置不超过 20 局；需要更多时先问。**可玩优先**：负责人问“能不能玩”时，立刻给出可玩入口。
- **先红后绿**；变异还原要**逐字节校验，并刷新 mtime**（`os.utime`），否则 MSBuild 会跳过重建，得到假的全绿（testing.md）。
- **Godot 在编辑器外运行时读的是 Debug 程序集**：只做 Release 构建会跑到旧代码（journal 09-22）。
- **测试不得写真实的 `%APPDATA%\Siege`**：脚本测试不传档案参数，即视为关闭带入带出（`openspec/specs/carry-in-out`）。
- **依赖 AI 实际走法的断言**要在测试里写死权重和停手阈值，不跟随默认值，**不许挑种子凑绿**（testing.md；v2-recalibration D12）。
- 提交信息写进文件用 `git commit -F`；提交前用真实退出码把关，不要把 `dotnet test` 接进管道再 `&&`；不加 Co-Authored-By 署名（负责人全局规则）。

## 规范要点（`.trellis/spec/core/` 浓缩）

- **零 Godot 依赖**：Core / Sim / Presentation 不得引用 `Godot.*`。`src/godot/` 不在 sln 里，守门测试看不到它，必须另外补源码级扫描。
- **单一实现**：几何四邻只在 `Adjacency.Neighbors`；气边只在 `LibertyNeighbors`，覆盖只在 `CoverageTargets` / `CoverageMap`，两者不可互相替代；地图解析 `MapCatalog`；结算顺序 `SettlementDriver`；地形写入 `TerrainWriter`；活形 `LifeShapeReport`；同形键 `GameBoard.SuperkoKey`；图形版命令行 `LaunchArgs`；出生区显示编号 `BirthZoneLabel`。
- **AI 评价**：眼信息只经 `LifeShapeReport` 获取；每一维都取“结算后 − 开始前”的增量。
- **确定性**：随机只用 `GameSeed` 子流（`relic-gen` / `recruit` / `setup` / `zone-pick` / `flag-risk` / `carry-ai` 等），子流之间互不扰动；禁止用浮点计分；势力与军势一律用 `BigInteger`；并列判定要确定性地打破。
- **信息边界**：公开视图在结构上就不含私有字段；AI 与界面共用 `MatchPublicView`；UI 不做规则计算；一切实时全量重算，不做增量。
- **测试**：Requirement 对应测试类，Scenario 对应测试方法，算例取自设计文档；新守门必须做变异验证；慢测试和计时测试用环境变量（`SIEGE_SLOW=1` / `SIEGE_PERF=1`）加 `Category` 过滤开启。
- **数据口径**：规则一变，权重先改标为“未校准”再谈重扫；调参要双向扫；标着“待校准 / 初值”的常量不能当基线；入口遇到未识别的输入必须报错，不得静默回退到缺省值；扫档后要核对 `config.json` 里的实际生效值。
- **坐标**：围棋记法，`A1` 在左下，列字母跳过 `I`。

## 环境

- Windows 11；Bash 工具是 Git Bash，负责人的终端是 PowerShell。**用 `python`，不要用 `python3`**。
- .NET SDK **8.0.425**；Release 与 Debug 都需要构建（Godot 读 Debug）。`dotnet test` 的输出是中文且为 GBK 编码，脚本里要设 `DOTNET_CLI_UI_LANGUAGE=en` 并用 `Failed` 抓失败名，红绿以退出码为准。
- Godot **4.7.2 .NET**（`Godot.NET.Sdk/4.7.2`）：`D:\software\godot\Godot_v4.7.2-stable_mono_win64\`，命令行用 `..._console.exe`。
  - 构建 C#：`--headless --path src/godot --build-solutions --quit`
  - 拾取自检（换相机、层高或地图后必须跑）：`--headless --path src/godot -- --auto-demo --pick-check [--map=<标识>]`
- codegraph 索引在 `.codegraph/`（已 gitignore）；`trellis-implement` / `trellis-check` 两个 agent 已配置 `mcp__codegraph__*`。
- 工作树：`.claude/worktrees/` 已清空，`terrain-surfaces` 的 worktree 与分支都已删除（合入于 `254964e`）。

## 可直接粘贴的开场 prompt

```
读 E:\wws\geme_demo\.trellis\workspace\rubioc\journal-1.md 的最后一节，再读 E:\wws\geme_demo\HANDOFF.md。
从 HANDOFF「已知问题与待办」里按优先级选下一项，先给我方案和编号裁决，我确认后再开 change / 派 agent。
遵守 HANDOFF「执行流程与工作约定」与 .trellis/spec/core/：同一时间只跑一个任务；不跑 200 局，每配置 ≤ 20 局；先红后绿。
每段结束汇报：测试数、变异验证情况、待决项。使用中文，每次回答开头报数（ws:N）。
```
