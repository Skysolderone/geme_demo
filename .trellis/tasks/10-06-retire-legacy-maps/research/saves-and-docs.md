# Research: 旧存档 / 旧日志的报错，文档与规格的连带（问题 5、6）

- **Query**: 加载引用已删地图的存档 / 日志现在会怎样、"明确报错"要改哪里；设计文档、openspec 主规格、HANDOFF / README / ROADMAP / art / tools 里要删改的引用
- **Scope**: internal
- **Date**: 2026-10-06

## 1. 旧存档 / 旧日志加载（问题 5）

### 1.1 地图解析的唯一入口

`MapCatalog.Resolve`（`src/Siege.Core/Board/Maps/MapCatalog.cs:62–102`）的顺序：内置表 → `gen:`（`GeneratedMapId.IsGenerated`）→ `board:` → 文件路径 / `maps/<标识>.json` 回落 → 都不是则抛 `FileNotFoundException`，消息列出可用内置标识与 `gen:` / `board:` 写法（:96–100）。

删除后（若只删表项与 `gen:` 分支、不加专门处理）：
- `siege-4p-base-v5` / `siege-2p-base-v1` / `siege-3p-base-v1` / `siege-frontier-v2`：落到文件回落。`maps/` 下 json 已删 → `FileNotFoundException("找不到地图 …：既不是内置地图，也不是存在的地图文件。可用的地图标识：…")`。**但**：若运行目录下还有旧 checkout 留下的 `maps/<标识>.json`，或用户手上有导出文件，文件回落会**静默加载**它（它是 Standard 档：若同时删掉 Standard 档，`MapFile.FromJson` 缺 `Profile` 时按 `MapFile.cs:121` 缺省 Standard 读入，再在 `GameBoard.Load` 的校验处被 `MAP_PROFILE_UNKNOWN` 或棋盘档规则拒绝——取决于实现）。
- `gen:12345`：`GeneratedMapId` 删除后同样落到文件回落；`File.Exists("gen:12345")` 为假，报同一条通用"找不到地图"。

结论：现在会**报错但不"明确"**——不说明"该地图已在 retire-legacy-maps 删除、旧存档 / 旧日志不再支持"。

### 1.2 各入口如何呈现

| 入口 | 调用 | 异常处理 | 呈现 |
|---|---|---|---|
| 批量 `run` / 终端 `play` / `map` | `src/Siege.Sim/Program.cs:102、176、383`；`BatchRunner.cs:61、69、97`；`MatchSession.cs:147`；`PlayCommand.cs:36` | `Program.Execute` 捕获 `ArgumentException / FormatException / FileNotFoundException / SiegeRuleException / MapGenerationException`（:53–57） | 标准错误输出 `错误：<消息>`，退出码 1 |
| 回放 `replay` | `Program.cs:437` → `Replayer.Replay`（`src/Siege.Sim/Running/Replayer.cs:44–50`：`MapCatalog.Resolve(header.Config.MapPerMatch ? header.MapId : header.Config.MapId)`） | 同上 | `错误：找不到地图 …`，退出码 1。**注意**：地图解析在摘要比对（:53–62，"地图不一致"）之前，所以旧日志走不到"地图不一致"那条已有的明确报文 |
| 分析 `analyze` | `src/Siege.Sim/Analysis/*` 不调用 `MapCatalog`（只读日志字段） | — | **旧日志照常可分析**，不报错（要不要拦需裁决） |
| 图形版 `--map=` | `src/godot/scripts/GameRoot.cs:330` | 捕获 `FileNotFoundException / FormatException / JsonException / MapValidationException / MapGenerationException`（:335–343） | 控制台 `[siege] 错误：…`，`Quit(1)` |
| 图形版选图界面 | `GameRoot.MapSelect.cs:64`（只会给内置棋盘图 / `board:`） | :104 | 不受影响 |
| 对局存档 | `MatchFlow.SavedMapId`（`src/Siege.Core/Match/MatchFlow.Persistence.cs:128`）读标识，调用方解析地图后 `Restore(map, json)`（:135–145），`RequireMap`（:189–208）比标识与 `MapDigest` | — | **产品里没有任何存档读入入口**：`SavedMapId` / `Restore` 只有测试调用（图形版、终端版都没有读档）。存档的"明确报错"目前只落在 Core API 层 |

已有的"明确报错"先例：`MatchFlow.Persistence.cs:160–187` 的 `RetiredSaveFields`（据点、大回合上限、碾压等废弃字段，读到即抛 `FormatException("对局存档含已废弃的 … 字段：…该存档不再支持恢复。")`）；以及 `boundaries.md:67` 要求旧内置标识请求时报"未知地图"（守门 `各入口按地图标识选图Tests.改名前的旧地图标识报未知地图`）。

### 1.3 "明确报错"需要改的地方

1. `MapCatalog.Resolve`：在文件回落**之前**识别已删除的标识——`siege-4p-base-v1..v5`、`siege-2p-base-v1`、`siege-3p-base-v1`、`siege-frontier-v1/v2`、`gen` / `gen:*`——抛带"已删除（retire-legacy-maps，2026-10-…）、所有地图都由棋盘组成，可用 …"的异常（类型沿用 `FileNotFoundException` / `FormatException` 即可被三个入口的现有 catch 接住；或新增类型则要同步改 `Program.cs:53` 与 `GameRoot.cs:335`）。这一处同时覆盖 `run --map`、`play --map`、`replay`、图形版 `--map=`、存档恢复调用方。
2. 文件回落是否保留（裁决项）：保留则旧导出文件仍能加载（若档位也删，要在 `MapFile.FromJson` / 校验处给出"标准档 / 边疆档已不支持"的明确报文，而不是 `MAP_PROFILE_UNKNOWN`）；删除则 `--map <文件路径>` 与 `Siege.Sim map --out` 导出文件可再加载的能力（`各入口支持生成图Tests` 有一条"给 out 才导出且按路径加载逐项相同"）一起没了。
3. `Replayer`（可选）：现在旧日志会在地图解析处报错退出；如果希望回放给出与"地图不一致"同风格的报文，可在 `Replayer.Replay` 里先识别已删除标识再返回 `ReplayResult`。
4. `analyze`（可选）：是否拒绝含已删除地图标识的日志（当前不读地图，能分析）。
5. `MatchFlow.Persistence`：`RequireMap` 本身无需改（地图由调用方解析，解析失败已经抛出）。

### 1.4 档案 `profile.json`（带入带出）

`src/Siege.Core/Carry/CarryProfile.cs`、`CarryProfileStore.cs` 里**没有任何地图字段**（grep `MapId|map` 无命中）；档案只存补给点与在途记录。删除旧地图对档案无影响。

## 2. 文档与其他（问题 6）

### 2.1 设计文档 `2026-09-10-siege-core-gameplay-design-v1.md`（1056 行）

| 位置 | 内容 | 处理 |
|---|---|---|
| §3.1（:40–74） | 格属性含高度 / 深水 / 桥 / 林地 / 新地表；"新地表……内置图不含新地表，生成图带 `:s1` 段时投放"（:46）；标准算例两张表 | 视地形裁决：删规则则大改；留规则则至少改 :46 的来源说明（新地表无来源） |
| §3.2 人数适配（:76–90） | 标准档 2/3/4 人预算表、C4/C2 对称、3 人镜像、距离均衡容差、口袋、入口通路、咽喉 | 改写为棋盘档预算（或并入 §3.3 棋盘档一节） |
| §3.3（:92–160） | v5 全节（含文本图）、`#### 2 人与 3 人基准图`（:145–160，含冒烟表） | 删除 |
| §3.3 `#### 棋盘档生成图`（:162–176） | 末条"现状：`标准` / `边疆` 档地图与 `gen:` 仍可用 `--map=` 指定……第三个 change 删除"（:176） | 改为"已删除" |
| §15.2（约 :732–826） | AI 校准口径写 v5（:778、786、788、797、802、806）、2 人图覆盖表（:821–826） | 保留为历史口径并标注"v5 已删除"，或随覆盖表裁决改写 |
| §16（:842–872） | 原型数值目标的实测在 v5 上 | 同上 |
| §19（:933） | 后续扩展提到边疆 / 生成图 | 改 |
| 变更记录（:1015–1056） | 历次 v5 / 边疆 / `gen:` 条目 | 历史保留，追加新条目（v1.30） |

### 2.2 openspec 主规格（`openspec/specs/`）

**删除**（Requirement 整条）：
- `map-definition`：「4 人基准地图」(:80)、「边疆档基准地图」(:212)、「2 人基准地图」(:254)、「3 人基准地图」(:272)、「人数适配预算」(:36，标准档 2/3/4 人表)。
- `map-generation`：「生成参数」(:30)、「布局规则」(:46，含 `:s1` 新地表投放)、「生成图的地图标识」(:117)。

**改写**：
- `map-definition`「地图为设计师固定的静态数据」(:6)、「地图静态校验规则」(:116，去掉距离均衡拒绝 / 咽喉 / 入口通路 / 标准档口袋等标准档专属条款)、「地图规格档」(:188，只剩棋盘档或整条并入「棋盘档预算与校验」)、「棋盘清单」(:290，"标准档与边疆档清单必须为空"一句)、「出生区归属与共享」(:174，核对措辞)。
- `map-generation`「地图种子与确定性」(:6)、「校验闭环」(:99)：现文写的是"边疆档 4 人地图"，但「棋盘档生成参数」(:145)明文规定"这两条对棋盘档同样生效"——**不能直接删，要改写成对棋盘档生成器的表述**；「棋盘档生成参数」(:139)、「棋盘档布局规则」(:183)、「棋盘档生成图标识」(:232)去掉与 `gen:` 互不干扰的条款。
- `simulation-harness`「各入口按地图标识选图」(:122，Scenario 显式指定旧地图 / 选边疆图 / 边疆图批量跑局)、「各入口支持生成图」(:150，`gen:` 标识与每局换图——改成 `board:` 或删)、「各入口的地图专属 AI 权重」(:218，2 人图覆盖)。
- `ai-decision`「难度分级」「候选格上限」「停手阈值」「默认评价权重的校准」「地图专属评价权重覆盖」「专家难度的一层前瞻」「专家前瞻的确定性与耗时」「决策序列基线与耗时」：Scenario 里写死 v5 / 边疆的数值。
- `match-setup`「原型插旗替代路径」「对局配置公开完整地图标识」；`map-selection`「开局选图界面」（"旧图不在界面上"）。
- `viewport-camera`「全局预览」「推屏与平移」「回到出生平台」「悬停格坐标读数」「动态相机下的拾取正确」（v5 / 边疆为例）。
- `visual-style-baseline`「视觉方向基准」「地图元素部件化」「双字母列标不重叠」「棋盘台面的可视表现」「禁手标记的远近可辨」「大图渲染开销」。

**视地形裁决而定**（只在删地形规则时动）：`terrain` 全部 4 条（格属性 / 边属性 / 气边 / 覆盖关系）、`terrain-edit` 7 条中搭桥 / 烧林相关条款、`coverage-territory`「棋子向四邻接相邻格提供覆盖」「空格归属三态」、`life-shape`「空区与封闭眼空间」、`piece-effects`「高地压制加值」、`power-score`「总势力」（荒漠）、`tactical-layers`「五种战术信息层」「新地表的规则标示」、`ai-decision`「AI 对地表的感知」、`visual-style-baseline`「新地表的灰度可辨」「地块与侧面的造型」「改造的可视表现」、`board-topology`「气的计算」等。保留地形规则时，至少 `terrain`「格属性」里"内置图不含新地表，生成图带 `:s1`"一类来源描述要改。

### 2.3 Trellis 规范（只列出，不在本调研范围内修改）

`.trellis/spec/core/boundaries.md`（5 处，含 :62–68"内置图内容一变标识必须递增"举 `siege-4p-base-vN` / `siege-frontier-vN` 为例）、`.trellis/spec/core/determinism.md`（2 处）、`.trellis/spec/core/testing.md`（3 处）。

### 2.4 HANDOFF / README / ROADMAP / 发布页

- `HANDOFF.md`：:16（"旧地图……仍可用 `--map=`"）、:18（Windows 启动示例 `--map=siege-2p-ba…`）、:27（`play --map siege-3p-base-v1`）、:40（地图标识列表含旧图）、:61（2p/3p 权威 json 嵌入）、:64–65（边疆图、生成器）、:109、:116、:119（边疆图试玩待办）、:80（历史表，保留）。
- `README.md`：:77（`play --map siege-3p-base-v1` 示例）、:84 附近地图标识说明。
- `openspec/ROADMAP.md`：:23（2 人图交战不足，候选 `siege-2p-base-v2`）、:24（边疆图 / 生成图复核）——随删除失效。
- `docs/index.html:260`（Cloudflare 发布页）：仍写"四张内置图（标准 13×13、双人 9×9、三人 11×11、边疆 25×30）或随机图"——**这一句在第二个 change 之后已经过时**，与本 change 一起改。

### 2.5 art / tools

- `art/*/shots.sh` 里显式用旧图的复拍脚本：`art/hud-panels/shots.sh:34–40`（v5、边疆）、`art/board-labels/shots.sh:40–57`（v5、边疆）、`art/hud-theme/shots.sh:30`（v5）、`art/builtin-board-maps/shots.sh:59`（`autodemo-v5`）。删图后这些脚本无法复跑（历史留档，是否改 / 标注需定）。
- `art/**/*.log`（约 60 个，`map-elements-v2`、`tiered-number-show`、`formation-tiers`、`board-labels` 等）与 `art/surfaces-v1/README.md`、`art/map-elements-v2/README.md`、`art/formation-tiers/README.md`、`art/tiered-number-show/README.md`、`art/recruit-panel-collapse/README.md`：历史记录，提到 v5 / `gen:12345:s1`，一般不改。
- `tools/export-release.sh`：不引用地图（无命中）。

## Caveats / Not Found

- 没有实际构造旧存档跑 `Restore`；"产品里没有读档入口"是 grep `SavedMapId|MatchFlow.Restore` 只命中定义处得出的。
- 设计文档行号是 `grep -n` 的命中行，章节边界按标题行划分。
