## Why

`board-link-mode` 归档后，游戏只剩链接模式（`link:<地图种子>`：盲选、拼接、战场结算），玩家入口已经到不了任何旧地图。但旧地图（标准图 `siege-4p-base-v5`、2 人图 `siege-2p-base-v1`、3 人图 `siege-3p-base-v1`、边疆图 `siege-frontier-v2`）、`gen:` 边疆档生成器、`board:` 棋盘档生成器、冒险概率与地图专属 AI 权重覆盖仍以"测试夹具"的身份留在 Core，经 `MapCatalog.Legacy`、`MatchFlow.RestoreLegacy`、Sim 入口的 `resolveMap` 参数、`MapCatalog.Legacy.DefaultId` 等过渡口使用。它们占着约 9 份主规格的 120 余处条文与 100 余个测试文件，每次改规则都要连带守住一套玩家永远看不到的行为。本 change 把它们彻底删除，只留链接模式与围棋内核。

对应设计文档章节：§3（棋盘与地图）、§4（开局与插旗，冒险概率）、§16–§17（基线与遥测口径）、§19（后续扩展顺序）。设计文档的历史变更记录不改（L5）。

## 负责人裁决记录（2026-10-13）

| # | 事项 | 裁决 |
|---|------|------|
| L1 | 删除范围 | **全缺省**：旧地图数据（四张内置图与 `maps/*.json`）、`gen:` 与 `board:` 生成器（含 `FrontierMapGenerator`、`BoardMapGenerator`、布局类、参数与标识类）、Legacy 模式分支（`MatchMode.Legacy` 及所有 `Mode == Legacy` 分支）、`MapCatalog.Legacy`、`RestoreLegacy`、`resolveMap` 参数、冒险概率（`MatchOptions.FlagRisk`、`RunConfig.FlagRisk`、`PrototypeZoneAssignment.Assign` 旧路径、`GameSeed.FlagRisk` / `ZonePick` 子流）、只服务旧地图的地图专属 AI 权重覆盖（2 人图 Eye 50），以及只为这些存在的测试。**Legacy 规则分支的处理见 L6（已裁 C，偏离本条字面）** |
| L2 | 回归护栏 | **全缺省**：围棋内核规则测试（气、提子、活形、覆盖、计分、同形、改造……）多用手写小盘面，保留不动；依赖旧地图的规则测试改用 `link:` 地图或手写夹具，不得丢覆盖——实施第一步是"逐类列出依赖旧地图的测试、每条测的规则、迁移去向"的清单。旧 v5 与 `board:1` 决策序列基线删除；新增 `link:1` 三个种子的决策序列基线（截断口径与现有链接图基线一致）作为新护栏 |
| L3 | 规格档 | **全缺省**：`MapProfile` 只留链接档（或删枚举，择一见 L7），`MapValidator` 声明表随之简化为链接一行 |
| L4 | 地形系统 | **全缺省**：河流、桥、林地、栅栏、高差、新地表等地形规则代码与规格保留不动；只删旧地图里的地形数据 |
| L5 | 历史资料 | **全缺省**：`art/` 旧截图与 `sim-out/` 不动；`maps/*.json` 删除；设计文档历史变更记录不动 |
| L6 | Legacy 规则分支 | **2026-10-13 选 C**：本 change 删除一切旧地图依赖与"选模式"的对外入口（公开建局恒为链接模式）；依模式分流的规则分支暂留、只服务经 `CreateUnvalidated` / `RestoreUnvalidated` 建局的手写夹具，登记后续 change `retire-legacy-rules`（偏离 L1 字面，负责人确认） |
| L7–L14 | 规划中发现的其余事项 | **2026-10-13 全按缺省**（见 design.md「Open Questions」）：L7 规格档保留单值枚举 `Link`、文件恒写规格档、缺失或旧值拒绝；L8 v5 校准记录留作历史，删两条截断率场景 / "三档旧难度逐步不变" / "大图耗时下降"，专家黄金值与前瞻耗时比改钉 `link:1` 种子 1–3；L9 地图专属 AI 权重覆盖整套删除；L10 只被旧生成器用到的代码一并删除，被拼接演出复用者保留并报告；L11 改带入带出与跑局规格两处措辞，战场规格历史记述不改；L12 信物区域强度预算按链接图改写并允许兜底，迁移时实测种子 1–100 报告未收敛比例；L13 遮挡分类由 Core 相机测试在手写高差夹具上守护；L14 `map-generation` 保留一条"地图生成器已退役"说明性条目 |
| L15–L22 | 实施与质检中发现的事项 | **2026-10-13 全按建议**：L15 终端链接局拼接后 `PlayCommand.Summarize` 越界崩溃，本 change 内修；L16 部分删除提前到第 2 组，接受；L17 Sim 读对局模式的日志分支与终端渲染分支并入 `retire-legacy-rules`；L18 校验器"中央入口 / 咽喉须可落子"与"容差与放宽理由"两条规则连同地图字段并入 `retire-legacy-rules`；L19 校验器中链接图走不到的 `BIRTH_ZONE_COUNT_MISMATCH` 分支现在删除（不改行为）；L20 Sim"参赛人数缺省取地图上限"逻辑删除（恒为 4）；L21 在 `link:1` 上为简单、困难难度各补钉一条决策序列基线（只新钉不改行为）；L22 验证产物 `snapshots/` 与 `shots/` 不入库，只保留两张最终截图。另：负责人定后续排序 A——先做"链接档出生棋盘主题化地形"（另一会话已建任务 `10-13-link-map-terrain`），AI 停手复核排在其后 |

## What Changes

- **BREAKING** 删除四张内置地图（`FourPlayerBaseMap`、`TwoPlayerBaseMap`、`ThreePlayerBaseMap`、`FrontierMapV2`）、`maps/` 目录下全部地图文件与程序集内嵌资源；删除 `MapCatalog.Legacy`（含 `Legacy.DefaultId` 与按文件路径 / `maps/<标识>.json` 读图的分支）与 `MapCatalog.BuiltinIds` / `BuiltinMaps`。`MapCatalog.Resolve` 只认 `link:`，行为不变。
- **BREAKING** 删除 `gen:` 边疆档生成器与 `board:` 棋盘档生成器全家：`FrontierMapGenerator`、`FrontierMapLayout.*`、`FrontierSurfaces`、`MapGenParameters`、`GeneratedMapId`、`BoardMapGenerator`、`BoardMapLayout`、`BoardMapParameters`、`BoardMapId`，以及只为旧图存在的 `MapSymmetry`。入口取随机地图种子用的"短种子折叠"迁入 `LinkMapId`，输出不变。`map-generation` 能力的全部 Requirement 移除。
- **BREAKING** 删除冒险概率：`MatchOptions.FlagRisk`、`RunConfig.FlagRisk`（旧批次配置写了该字段即报错，不静默忽略）、`PrototypeZoneAssignment.Assign` 与 `RequireValidFlagRisk`、`GameSeed.FlagRisk` / `GameSeed.ZonePick` 子流名；`MatchFlow.PlantPrototype` 恒走盲选 `AssignBlind`。
- **BREAKING** 删除地图专属评价权重覆盖：覆盖表、"按地图标识取权重"的解析、批量入口把覆盖落成显式权重的逻辑与对应守门；AI 权重只剩"显式给出 → 默认表"两级。
- 删除过渡口：`MatchFlow.RestoreLegacy`、Sim 入口（`BatchRunner` / `Replayer` / `MatchSession`）的 `resolveMap` 参数、Sim 与图形版建局时 `link ? Link : Legacy` 的模式分叉、`map` 调试子命令对旧标识族的支持与其 v5 缺省（改为 `link:1`）、`RunConfig.MapId` 的 v5 内部缺省（改为 `link:1`）。
- 规格档收口（L3 / L7）：`MapProfile` 只留 `链接`，`MapValidator` 声明表只剩链接一行；只服务标准 / 边疆 / 棋盘三档的分流（距离均衡、到中央入口可达、通道规则、全部连通、地标必填、单字母列宽上限）随之删除，各档共用规则（必死口袋、信物格合法、保护期容量、出生区无新地表、桥在深水、栅栏在邻格）保留。链接图的地图文件导出与日志首部地图摘要逐字节不变。
- Legacy 模式（L6 缺省）：`MatchOptions.Mode` 的设置降为 Core 内部（公开的 `MatchFlow.Create` 恒以链接模式建局，Sim 与图形版不再写模式分叉）；`MatchMode` 枚举与只读的 `Mode` 属性保留；Core / Presentation / AI 中依模式分流的规则分支暂留，只服务经 `CreateUnvalidated` / `RestoreUnvalidated` 建局的手写夹具，登记后续 change `retire-legacy-rules`。Sim 与图形版里只在 Legacy 局出现的分支（日志字段、渲染、自动演示判定）删除。
- 回归护栏（L2）：删除 v5 与 `board:1` 决策序列基线文件；新增 `link:1` 三个种子的决策序列基线（既有 `link:1` 种子 3 基线逐字节不变）。依赖旧地图的规则测试按清单迁到 `link:` 地图或手写夹具；只测旧地图本身的测试随数据删除。

## Capabilities

### New Capabilities

（无）

### Modified Capabilities

- `map-definition`：移除「人数适配预算」「4 人基准地图」「边疆档基准地图」「2 人基准地图」「3 人基准地图」「棋盘档预算与校验」「旧地图降级为测试夹具」；「地图为设计师固定的静态数据」去掉内置图与标准档 / 边疆档 / 棋盘档的来源；「地图静态校验规则」只保留链接档仍执行的规则；「地图规格档」只剩 `链接`；「棋盘清单」去掉旧档条款与旧图场景。
- `map-generation`：原有 9 条 Requirement 全部移除（边疆档与棋盘档生成器退役，新模式生成见 `link-map-generation`）；新增一条说明性的「地图生成器已退役」——OpenSpec 归档时不允许能力不含 Requirement（L14）。
- `ai-decision`：移除「地图专属评价权重覆盖」；「难度分级」「默认评价权重的校准」「候选格上限」「停手阈值」「专家难度的一层前瞻」「专家前瞻的确定性与耗时」「决策序列基线与耗时」「链接模式下落后者的停手阈值」改写旧地图场景、去掉 Legacy 与地图覆盖的引用，规则正文不变。
- `simulation-harness`：移除「各入口的地图专属 AI 权重」；「各入口支持生成图」中 `map` 子命令只接受 `link:`、缺省 `link:1`；「各入口按地图标识选图」把"已降级的旧标识族"改为"已删除"（措辞，行为不变）。
- `viewport-camera`：「推屏与平移」「动态相机下的拾取正确」「悬停格坐标读数」的旧地图场景改写为链接图或手写夹具，规则正文不变。
- `visual-style-baseline`：「棋盘台面的可视表现」「禁手标记的远近可辨」「双字母列标不重叠」「大图渲染开销」的旧地图场景改写为链接图或手写夹具。
- `relic-generation`：「出生区信物权重」「区域强度预算」中写死"4 人基准地图 / 8 枚 / 四个出生区"的场景改为链接图（5 块出生棋盘 × 2 枚），规则正文不变。
- `carry-in-out`：「AI 带入」去掉已删除的"冒险抽签"子流引用。

不改的能力（复核过、条文对删除后仍成立）：`match-setup`、`match-telemetry`、`map-selection`——它们引用旧标识只是作为"被拒绝的非法标识"示例，冒险概率在 `match-setup` 中本就写明已废除；`battlefield`「战场的定义与规则作用域」中 R5 的理由句是历史记述；`terrain`、`terrain-edit` 及其余能力没有引用旧地图。

## Impact

- **Core**：`Board/Maps/` 删 18 个文件（四张内置图、两套生成器与布局、参数与标识〔`GeneratedMapId` 、`BoardMapId` 随参数文件〕、`FrontierSurfaces`），`MapRandom`、`LinkMapGenerator`、`LinkLayout`、`LinkMapId` 保留；`MapCatalog`、`MapValidator`、`MapData`（规格档缺省）、`MapFile`（规格档读写）、`MapSymmetry`（删）；`Match/`：`FlagPlanting`（`MatchOptions.FlagRisk`、`Mode` 设置降 internal）、`PrototypeZoneAssignment`、`MatchFlow`（`Create` 恒链接、`PlantPrototype`、`RequireModeMatchesMap`）、`MatchFlow.Persistence`（`RestoreLegacy`）；`Determinism/GameSeed`；`Ai/EvaluationWeights`（覆盖表）；依模式分流的规则分支（`MatchFlow`、`PublicRules`、`SeatProjection`、`HeuristicTurnController`、`DefaultBoardView` 等）不动；`Siege.Core.csproj` 去掉两项嵌入资源。
- **Sim**：`Program`（`map` 子命令、`DefaultMapId`、裸 `gen` / `board` 取种子）、`RunConfig`（`FlagRisk`、`MapId` 缺省、覆盖落成）、`BatchRunner` / `Replayer` / `MatchSession`（`resolveMap`、模式分叉、Legacy 日志字段分支）、`PlayCommand`、`BoardRenderer`。
- **Presentation**：`BoardCamera`（以 v5 为例的注释）、`MapSelectModel`（若仍读 `BuiltinMaps`）、可能的通道 / 公共棋盘样式（L10）；`DefaultBoardView` 的模式分支按 L6 缺省不动。
- **Godot**：`MatchSession`（模式分叉）、`GameRoot` / `GameRoot.MapSelect`（`FriendlySeed` 迁移）、`BoardView` / `Visuals`（通道样式，L10）。
- **测试**：约 114 个文件直接引用旧标识或旧生成器；`AiDecision/Fixtures/` 删除 `decisions-siege-4p-base-v5-seed1.txt` 与 `decisions-board1-seed1.txt`，新增 `link:1` 两个种子的基线；`FrontierFixtures`、`BoardMapFixtures`、`BoardGenFixtures`、`MapGenFixtures` 删除；`AiFixtures`、`LookaheadFixtures`、`SimFixtures`、`CameraFixtures`、`PresentationFixtures` 改造；`MatchFixtures` 不动。
- **数据与文档**：`maps/` 目录删除；`HANDOFF.md` 与设计文档当前章节同步；`art/`、`sim-out/`、设计文档历史变更记录不动。

## Non-goals

- **不删地形规则**（L4）：河流、桥、林地、栅栏、高差、新地表的规则代码与规格（`terrain`、`terrain-edit`、`board-topology` 等）保留不动，只删旧地图里的地形数据。
- **不改 AI 行为**：评价权重、停手阈值、候选格上限规则、前瞻算法逐字节不变；不重做任何校准（v5 上的校准记录作为历史口径保留）。AI 停手与视野复核留在既已登记的后续 change。
- **不碰 `map-elements-v2`**：与本 change 无关。
- **不彻底删除 Legacy 规则分支**（L6 缺省）：手写夹具在 Legacy 规则下运行，迁到链接规则须改写约 120 个测试文件的期望，推迟到后续 change `retire-legacy-rules`。
- **不改链接模式任何行为**：链接图生成、拼接、战场结算、日志与存档格式逐字节不变。

## 前置 change

- `board-link-mode`（已归档，`openspec/changes/archive/2026-10-13-board-link-mode/`）：本 change 删除的过渡口全部由它引入。
