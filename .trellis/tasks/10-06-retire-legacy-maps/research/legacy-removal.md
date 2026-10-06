# Research: 第三个 change（retire-legacy-maps）总览——规模、拆分建议、待裁决问题

- **Query**: 删除 `siege-4p-base-v5` / `siege-2p-base-v1` / `siege-3p-base-v1` / `siege-frontier-v2` / `gen:` / `maps/*.json`，测试改写到棋盘图，重定黄金值，旧存档 / 日志明确报错——摸清范围并给出拆分与裁决清单
- **Scope**: internal（静态扫描 + scratchpad 拷贝实跑全量测试）
- **Date**: 2026-10-06
- **母任务依据**: `.trellis/tasks/10-06-board-terrain/prd.md` 裁决 4（旧地图彻底删除、旧存档 / 日志明确报错）、9（规则测试全部改写到新棋盘图，不保留 v5 作夹具）、10（拆 3 个 change）

## 分文件

| 文件 | 内容 |
|---|---|
| `research/product-code.md` | 问题 1、2：要删 / 要改的产品代码，`MapProfile` 与校验器各规则的去留，地形元素来源核对，"不可达但规则仍在"清单 |
| `research/tests-and-golden.md` | 问题 3、4：439 个依赖旧图的测试方法分组，黄金值清单，本机 11 条红测的根因 |
| `research/saves-and-docs.md` | 问题 5、6：旧存档 / 日志现在如何失败、明确报错落点；设计文档、openspec、HANDOFF / README / ROADMAP / 发布页 / art 的连带 |

## 要点摘要

1. **产品代码**：可整删约 3290 行（`FourPlayerBaseMap`、`TwoPlayerBaseMap`、`ThreePlayerBaseMap`、`FrontierMapV2`、`FrontierMapGenerator`、8 个 `FrontierMapLayout*`、`FrontierSurfaces`、`MapGenParameters`/`GeneratedMapId`、`MapSymmetry`、`MapRandom.ForAttempt/ForSurfaces`）+ 7 个 json + 2 条嵌入资源。有两处"藏在要删文件里的共用件"必须先迁出：`MapGenerationException`（在 `FrontierMapGenerator.cs:37`，棋盘生成器与三个入口在用）、`GeneratedMapId.FriendlySeed`（棋盘图的随机种子入口在用）。另有一项功能会失去来源：**每局换图 `--map-per-match` 只支持 `gen:`**（`RunConfig.cs:191–239`）。
2. **MapProfile / 校验器**：删后产品只剩棋盘档。Standard / Frontier 两行预算、距离均衡拒绝、咽喉、入口通路、`BOARDS_NOT_ALLOWED` 都只服务旧档；口袋、距离容差、出生区新地表禁令在棋盘档上**永不触发**（被棋盘档规则覆盖）。但 `MapFile` 导出文本对棋盘图也写出 `ChokePoints`、`DistanceTolerance`、`MinTwoEyeArea`、`PocketExemptions`、`Profile`——删字段会改三张内置棋盘图的摘要，按 `boundaries.md:62–68` 须升 `-v2`。
3. **地形**：棋盘图开局全 h=0 草地无桥无栅；匠人三种改造里**只有立栅在棋盘图上有合法目标**（`TerrainEditRules.cs:129–147` 只要求边在外接范围内）。高度 / 崖壁 / 高地压制、深水 / 隔水覆盖、桥与搭桥、林地与烧林、土路、四种新地表、工坊信物的效果——全部**不可达但规则仍在**；新地表删掉 `gen:` 后没有任何来源。栅栏必须保留。
4. **测试**：依赖旧图的方法 439 个（101 个文件）：测地图本身 287（其中可整删约 140）、黄金值 / 逐步相同 97、真实样本遥测 24、把 v5 当棋盘 29（约一半只需换图改坐标，一半是终端脚本要重写）、依赖旧图地形摆局面只有 2 个。另有 258 个地形测试用合成盘面（`LoadUnvalidated` / `CreateUnvalidated`），**不依赖旧图**，保留地形规则就不用动。
5. **黄金值**：随图删除 6 组（v5 摘要、`gen:` 摘要、`:s1` 摘要、50 个边疆生成图摘要、`ForAttempt` 黄金值、v5 决策基线文件）；必须在新图上重定约 13 组，其中 `候选格上限Tests.V4GoldenTurnHash` 被 4 个以上测试复用。
6. **11 条本机红测**全部是旧图上的黄金值，根因与地图无关：机器核数（`RunConfig.EffectiveParallelism` 被序列化进被哈希的首部，Windows 28 核 / 本机 8 核——设 `DOTNET_PROCESSOR_COUNT=28` 实测 6 条转绿）和行尾（剩 5 条，CRLF / LF，推断）。改写到新图重钉时若不先修这两处泄漏，换一台机器仍会红。
7. **旧存档 / 日志**：现在会报通用的"找不到地图"，不是"明确报错"；文件回落 `maps/<标识>.json` 还可能把旧标识静默复活。明确报错的单一落点是 `MapCatalog.Resolve`（三个入口与回放都经过它）。产品里**没有读档入口**（`MatchFlow.Restore` 只有测试调用）；`analyze` 不读地图，旧日志照常可分析；档案 `profile.json` 不含地图字段。
8. **文档 / 规格**：设计文档 §3.2、§3.3（v5、2/3 人图）整段重写；openspec 删 `map-definition` 5 条、`map-generation` 3 条，改写约 25 条（`map-generation`「地图种子与确定性」「校验闭环」被棋盘档引用，不能直接删）；`docs/index.html:260` 的"四张内置图"在第二个 change 后已经过时。

## 规模估算

| 部分 | 量 |
|---|---|
| 产品代码 | 删约 3290 行 / 18 个文件；改约 15 处（MapCatalog、Program、RunConfig、PlayCommand、GameRoot ×2、EvaluationWeights、MapValidator、MapData / MapFile 视裁决） |
| 测试 | 涉及 101 个文件 439 个方法：整删约 140，换图 + 重钉约 250，重写脚本 / 场景约 50（终端脚本 14、相机 46 里的"小图"情形、信物生成 14） |
| 黄金值 | 删 6 组、重定约 13 组 + `SimFixtures.Sample` 派生的样本口径 |
| 跑测时间风险 | 现在钉 v5 的整局测试（`难度分级` 3 局整局、`ProbePositions` 种子 1–3 整局、`活形分析的决策内缓存` v5 种子 1–20 + 边疆种子 1–20）换到 465 格的 `siege-4p-board-v1` 会显著变慢——HANDOFF 冒烟是单局 6–21 秒、52–74 大回合；要么加回合截断，要么改用 2 人图 |
| 文档 / 规格 | 设计文档 §3 重写 + §15/§16 标注；openspec 约 10 个 capability；HANDOFF / README / ROADMAP / 发布页 |

## 拆分建议

建议仍是一个 change（`retire-legacy-maps`）分 5 段，每段结束全量测试可判定：

- **段 0（前置，小）**：去掉被哈希文本里的机器相关值——`EffectiveParallelism` 剔出日志首部 / `config.json`（或相关测试显式给 `Parallelism`），哈希前统一行尾。目标：在本机先把 11 条红变绿（旧图黄金值不动），以后重钉的值才能两台机器通用。这一段可以先合进 main，与删图无关。
- **段 A（迁测试，旧代码仍在）**：把 `SimFixtures.Config`、`LookaheadFixtures.V5Config/ProbePositions`、`CameraFixtures`、终端脚本、信物生成测试、(a) 组规则测试改到内置棋盘图；在新图上重定 2.2 节黄金值；决定整局测试的回合截断。此时 v5 等仍在，失败都能归因到"换图"，不会混进"删代码"。
- **段 B（删代码与数据）**：删旧图类、`gen:` 全套、`maps/*.json`、嵌入资源、`MapSymmetry`；迁出 `MapGenerationException`、`FriendlySeed`；处理每局换图；删 (b) 组只测旧图的测试与 2.1 节黄金值；`MapCatalog` 加已删除标识的明确报错（及文件回落的处理）。
- **段 C（档位与校验器收口）**：删 Standard / Frontier 声明行与专属规则（距离拒绝、咽喉、入口通路、`BOARDS_NOT_ALLOWED`、出生区新地表禁令）；按裁决决定 `MapData` / `MapFile` 字段动不动（动则内置棋盘图升 `-v2`）。
- **段 D（文档与规格）**：设计文档 v1.30、openspec 增量规格（删 / 改 Requirement）、HANDOFF、README、ROADMAP、`docs/index.html`、art 复拍脚本标注。

**地形规则的去留不放进本 change**（见裁决 Q1）：涉及 258 个测试、约 15 条规格、Godot 部件，体量与"删旧图"相当，且与之正交。

## 需要负责人裁决的问题（附建议）

| # | 问题 | 建议 |
|---|---|---|
| Q1 | 不可达的地形规则（高度 / 崖壁 / 高地压制、深水 / 桥 / 搭桥、林地 / 烧林、土路、荒漠 / 沼泽 / 岩台 / 浅滩）删还是留？ | **本 change 保留**，设计文档与规格标注"当前所有地图都不产生这些地形，规则保留待用"；另开 change 定是否删除或给棋盘图加地形。栅栏有立栅来源，必须保留 |
| Q2 | 工坊信物在棋盘图上效果恒为空（只扩搭桥 / 烧林的格目标），但仍会被生成，怎么办？ | 本 change 不动，记为已知问题，放到 Q1 的地形 change 或 AI / 内容校准 change 里处理（移出权重表要新开内容集版本） |
| Q3 | `MapProfile.Standard/Frontier` 与 `MapData` 的旧档字段（`ChokePoints`、`DistanceTolerance`、`ToleranceRelaxReason`、`MinTwoEyeArea`、`PocketExemptions`）删到什么程度？ | 删 Frontier 枚举值与 Standard / Frontier 两行声明及专属规则；**`MapData` 字段与 `MapFile` 导出格式本 change 不动**，保住三张内置棋盘图的摘要（否则要升 `-v2`）。Standard 只作合成测试图的缺省值，校验器对它明确报"标准档已删除" |
| Q4 | 保留"按地图文件路径加载"与 `maps/<标识>.json` 隐式回落吗？ | 保留显式文件路径（`--map <path>`、`map --out` 导出后可再加载）；**删除 `maps/<标识>.json` 隐式回落**（防止旧标识被旧文件复活，`boundaries.md:67` 的风险）；旧档位文件给明确报错 |
| Q5 | 每局换图 `--map-per-match`（只支持 `gen:`）删还是扩到 `board:`？ | 扩到 `board:`（`BoardMapId.Parse/Format` 现成），后续 AI 校准 change 需要多图样本；若要压范围则删 |
| Q6 | 2 人图 AI 覆盖表（只登记 `siege-2p-base-v1`）与 `ForMapId` 机制 | 删条目，保留机制（空表）；15 个相关测试改用注入表重载（`EvaluationWeights.cs:147`）；内置棋盘图是否登记覆盖留给 AI 校准 change |
| Q7 | "与改动前逐步相同"类守门（内容集 v1、计分 v1、带入关闭、旧难度、一层专家）在新图上没有"改动前"可比，怎么处理？ | 在新图上重钉为"本 change 的基线"，保留 v1 / v2、开 / 关之间的反面对照断言；在测试注释写明黄金值改钉于 retire-legacy-maps |
| Q8 | 黄金值跨机器问题（11 条红的根因）在本 change 一起修吗？ | 修，作为段 0 先做；之后"全量测试全绿"可以作为两台机器的统一判据 |
| Q9 | 测试夹具用哪张图？整局测试变慢怎么办？ | 跑局类默认 `siege-4p-board-v1` 并加小回合截断；需要整局到终局的黄金值优先用 `siege-2p-board-v1`；规则单元测试继续用合成盘面 |
| Q10 | 相机测试里"整盘一屏可见的小图"情形，棋盘图都是大图 | 用 `LoadUnvalidated` 构造的合成小棋盘档图，或 `siege-2p-board-v1`，场景随之改写 |
| Q11 | `analyze` 遇到旧图日志要拒绝吗？存档的明确报错放哪？ | `analyze` 不拒绝（不依赖地图）；`run` / `play` / `replay` / 图形版 `--map=` / 存档恢复统一在 `MapCatalog.Resolve` 报"已删除"；`replay` 可选给出与"地图不一致"同风格的报文 |
| Q12 | `art/*/shots.sh` 里用 v5 / 边疆的复拍脚本 | 不改，作为历史留档，在对应 README 加一行"所用地图已于 retire-legacy-maps 删除" |

## Caveats / Not Found

- 测试分组与数量来自正则静态扫描（脚本在 scratchpad：`analyze.py`、`group.py`），按方法计，不含 InlineData 展开；个别归类可能有出入。
- 11 条红测：6 条已实测（核数），5 条（行尾）是推断。
- 整局测试换图后的耗时没有实测，只按 HANDOFF / 设计文档的冒烟数据估计。
- Godot 部件"不可达"的判断没在引擎里实跑核对。
