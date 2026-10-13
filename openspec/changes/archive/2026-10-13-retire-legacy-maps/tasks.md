> 前置：负责人对 design.md「Open Questions」L6–L14 批注。下列任务按缺省建议编写；L6 若裁为 A，第 3 组按组头说明展开。
> 每组对应 design D1 的一步：组内全部完成且 `dotnet test` 全绿、三条链接图决策基线逐字节不变，才进入下一组；每组单独提交。

## 1. 迁移清单与新护栏（D1 第 1 步，实施第一步）

- [x] 1.1 产出"依赖旧地图的测试迁移清单"，写入 `.trellis/tasks/10-13-retire-legacy-maps/implement.md`：对每个测试类给出 ① 依赖的旧对象（v5 / 2 人图 / 3 人图 / 边疆图 / `gen:` / `board:` / 冒险概率 / 地图覆盖 / `MapCatalog.Legacy` / `RestoreLegacy` / `resolveMap`）② 每条测试测的规则 ③ 去向（删 / 迁 `link:` / 迁手写夹具 / 不动）与所属步骤。初判如下，清单须逐条核实后定稿（数据来源：`tests/` 下直接引用旧标识或旧生成器的 114 个文件、`MapCatalog.Legacy` / `RestoreLegacy` 的 68 个文件）。验收：清单覆盖上述全部文件，每条"删"都写明为何没有规则覆盖损失（人工检查）
  - **删（只测旧对象本身）**：`MapDefinition/` 的 四人基准地图、两人基准地图、三人基准地图、边疆档基准地图、边疆档静态校验（其中的"规格档分流守门"源码扫描用例不删，迁到 `地图规格档Tests` 或新守门类，在 1.6 完成，见 4.2）、基准地图对称性、棋盘档预算与校验、旧地图降级为测试夹具、人数适配预算（其中链接档预算用例若有则迁）；`MapGeneration/` 全部 11 个；`SimulationHarness/` 的 冒险概率记录、边疆图终端试玩脚本、各入口的地图专属AI权重；`AiDecision/` 的 地图专属评价权重覆盖、AI小回合耗时（`board:1` 大图耗时，Perf）；夹具 `FrontierFixtures`、`BoardMapFixtures`、`BoardGenFixtures`、`MapGenFixtures`；数据 `AiDecision/Fixtures/decisions-siege-4p-base-v5-seed1.txt`、`decisions-board1-seed1.txt`
  - **迁（测规则但建在旧图上）**：`AiDecision/` 的 停手阈值、候选格上限、默认评价权重的校准、难度分级、专家难度的一层前瞻、专家前瞻的确定性与耗时、专家前瞻耗时计时、决策序列基线、启发式评价维度、活形分析的决策内缓存、公开规则纯函数；`MapDefinition/` 的 地图静态校验规则、地图规格档、地图文件往返、地图文件健壮性、地图静态数据、棋盘清单、出生区归属；`RelicGeneration/` 五个类、`RelicEffects/` 六类原型信物的效果与计分信物连营与犄角、`RelicControl/` 信物控制判定；`BoardTopology/` 棋盘格子状态模型、盘面副本与批量写入、盘面序列化、盘面派生量提速对照；`LifeShape/` 活形分析性能基线；`TerrainEditing/` 地形写入口；`TacticalLayers/` 单子禁手的标示（对照）、盘面层视图模型带地形；`MatchSetup/` 原型插旗替代路径（盲选部分）、对局内容集、对局配置公开完整地图标识、带入带出配置、盲选插旗、链接模式存档、对局模式；`MatchTelemetry/` 九个类（专家前瞻的记录、地形改造日志与分析、各棋子势力占比、分析排除测试污染、活形记录与统计、插旗同区统计、对局日志的记录内容及其链接版、链接模式平衡分析）；`MatchFlowRegression/` 百局端到端；`Recruitment/` 初始配置与基础棋池、征募随机可复现；`CarryInOut/` AI带入、本地玩家档案、终端的带入选择弃赛与结算显示；`Determinism/` 随机子流隔离；`InformationVisibility/` 视图模型按座位过滤；`InitiativeOrder/` 先手顺序预测_链接模式；`TurnSequence/` 战场内合法落子范围；`Battlefield/` 战场的定义与规则作用域；`LinkMapGeneration/` 链接档地图标识与确定性、物化状态校验；`SimulationHarness/` 其余引用旧标识的类（可复现回放、各入口按地图标识选图、各入口支持生成图、地图子命令、批量跑局、日志首部区数、日志首部地图摘要、每局换图、终端对局、终端专家对局、终端活形与禁入标示、防死锁硬停、随机子流隔离及各自的链接版）；`MapSelection/` 选图界面守门、选图视图模型；`ViewportCamera/` 经 `CameraFixtures.V4 / Frontier` 的全部用例；夹具 `AiFixtures`、`LookaheadFixtures`、`SimFixtures`、`CameraFixtures`、`PresentationFixtures` 中引用旧图的部分
  - **不动**：`MatchFixtures` 及只经它建局的用例（Legacy 规则下的手写 9×9 夹具，L6）；`ContentSets.Legacy`（内容集 v1）相关用例
- [x] 1.2 取画面对照：以固定参数对 `link:1` 取构筑期与决赛链接后的定帧截图各一张存入 Trellis 任务目录，供 `visual-style-baseline`「大图渲染开销」的"画面不变"在第 3、5 组对照；同时记录两帧的绘制调用数。验收：截图与数字存档（人工检查）
- [x] 1.3 钉新决策序列基线：在当前实现上导出 `link:1`、对局种子 1 与 2、内置固定配对脚本、4 名标准难度、截断到第 12 大回合的 AI 决策序列，存入 `AiDecision/Fixtures/`（单份 ≤ 200 KB），`决策序列基线Tests` 改为对 `link:1` 种子 1 / 2 / 3 三份基线逐字节比对（种子 3 沿用既有文件、不重钉）。验收：单元测试通过；种子 3 文件 git diff 为空（算例：`ai-decision`「决策序列基线与耗时」）
- [x] 1.4 专家前瞻黄金值改钉 `link:1`：以前瞻宽度 4、多样补充上限 0、两层权重 0，在 `link:1` 对局种子 1–3（1 专家 + 3 标准、固定配对脚本、截断第 12 大回合）上取下专家决策、`ai-<玩家>` 子流消费与前瞻记录确定性文本的黄金值；「缺省预设」与之逐项相同；`专家前瞻耗时计时` / `专家前瞻的确定性与耗时` 的固定局面集改取同一批对局（不少于 30 个部署决策）。验收：单元测试通过；Perf 类别单独跑一次记录专家 / 高难中位数之比 ≤ 4
- [x] 1.5 按清单迁移 `AiDecision/` 中建在 v5 / 2 人图 / 边疆图上的规则用例：候选格上限改为"≤ 150 格手写夹具 → K = 0、> 150 格地图 → K = 24"（算例：`ai-decision`「候选格上限」小图不限制 / 大图自动取上限）；停手阈值"阈值不随地图变化"改 `link:1` 与 `link:2`；删除"三档旧难度逐步不变""校准后截断率达标（默认套件 / 200 局慢测试）"；校准口径字符串守门照旧。验收：单元测试通过，清单逐条勾销
- [x] 1.6 按清单迁移 `MapDefinition/` 与 `RelicGeneration/`：静态校验规则用例改为手写的链接档地图（算例：6 块出生棋盘 → 报出生区数超出恰 5 个；出生棋盘 2 内 `C3` 为浅滩 → 报棋盘内须为高度 0 草地；出生棋盘 3 有 3 个信物格 → 拒绝）；只能在旧三档上触发的距离均衡 / 中央入口通路用例随规则删除，必死口袋与保护期容量用例改在不经清单规则的手写夹具上直接调用对应校验步骤（若校验器不暴露该接缝则记入清单为"被链接档清单规则蕴含、改由清单规则用例覆盖"）；出生区信物权重与区域强度预算改 `link:` 初始地图（算例：10 枚出生区信物全为 +1；5 个出生区稀有度偏差 ≤ 8% 或标记未收敛），并实测种子 1–100 的未收敛比例写入清单（L12）。验收：单元测试通过；未收敛比例记录在案
- [x] 1.7 按清单迁移 `ViewportCamera/`：`CameraFixtures` 的 `V4` 改为"一屏看全的单块棋盘"手写夹具，`Frontier` 改为含 h=2 高台、竖长的手写夹具，保留遮挡分类与"每格至少一种缩放下严格选中自身"的断言（L13）。验收：单元测试通过，原有遮挡与严格选中断言条数不减
- [x] 1.8 按清单迁移其余"迁"类（`BoardTopology`、`LifeShape`、`TerrainEditing`、`TacticalLayers`、`Relic*`、`MatchSetup`、`MatchTelemetry`、`MatchFlowRegression`、`Recruitment`、`CarryInOut`、`Determinism`、`InformationVisibility`、`InitiativeOrder`、`TurnSequence`、`Battlefield`、`LinkMapGeneration`、`SimulationHarness`、`MapSelection`）：地形相关用例改用含地形的手写夹具（地形规则正文不动，L4），其余改 `link:` 地图。验收：单元测试通过；全量通过数 = 步前 + 1.3 / 1.4 新增数（跳过数只含既有 Perf / Slow）

## 2. 删数据与生成器（D1 第 2 步）

- [x] 2.1 新增 `LinkMapId.FriendlySeed`（自 `GeneratedMapId` 原样迁入），Sim `ClockMapSeed`、图形版 `GameRoot` / `GameRoot.MapSelect` 改调新位置；加一条对照测试：同一批输入下新旧实现输出逐位相同，旧实现删除后该测试改为钉值。验收：单元测试通过
- [x] 2.2 删除 `src/Siege.Core/Board/Maps/` 下 18 个文件（四张内置图、`FrontierMapGenerator`、`FrontierMapLayout` 8 个分部、`FrontierSurfaces`、`MapGenParameters`、`BoardMapGenerator`、`BoardMapLayout`、`BoardMapParameters`）与 `Board/MapSymmetry.cs`；`MapRandom`、`LinkMapGenerator`、`LinkLayout`、`LinkMapId` 保留。验收：`dotnet build` 零警告零错误
- [x] 2.3 删除 `MapCatalog.Legacy`、`BuiltinIds`、`BuiltinMaps`、`BuiltinMapInfo`；删除 `maps/` 目录（7 个 JSON）与 `Siege.Core.csproj` 中两项 `EmbeddedResource`。验收：`dotnet build` 通过；Grep `siege-4p-base|siege-2p-base|siege-3p-base|siege-frontier|FrontierMap|BoardMap|GeneratedMapId|BoardMapId` 在 `src/` 只剩错误信息与注释中的历史引用（逐条列出）
- [x] 2.4 删除清单中第 2 步标"删"的测试、四个旧夹具与两个旧基线文件。验收：全量测试全绿；通过数减少量恰等于清单本步"删"的条数；`link:1`–`link:20` 初始地图导出与日志首部地图摘要与第 1 组结束时逐字节相同（对照脚本输出存入 Trellis 任务目录）

## 3. 删 Legacy 入口与过渡口（D1 第 3 步，按 L6 缺省 C）

> 若 L6 裁为 A：本组须先补"手写夹具迁到链接规则"的量化表（受影响文件、断言旧终局原因的用例、手写图上的 AI 黄金值），再展开为夹具迁移子项目，不得在本组直接删除规则分支。

- [x] 3.1 删除冒险概率：`MatchOptions.FlagRisk`、`PrototypeZoneAssignment.Assign` 与 `RequireValidFlagRisk`、`GameSeed.FlagRisk` / `ZonePick`；`MatchFlow.Build` 去掉冒险概率校验；`PlantPrototype` 恒走 `AssignBlind`。验收：单元测试通过；算例（`match-setup`「原型插旗替代路径」）：本机选棋盘 2、三名 AI 自动插旗，同一对局种子开局两次锁定结果相同，改变人工选择时 AI 结果与信物分布不变
- [x] 3.2 删除 `RunConfig.FlagRisk`：批次配置文件写了该字段即报错退出并指出字段已移除。验收：单元测试覆盖"写了即报错、不建输出目录"
- [x] 3.3 删除地图专属权重覆盖：覆盖表、`TwoPlayerOverrideCalibrationStatus`、按地图取权重的解析与三入口落成逻辑；取用退化为"显式权重 → 默认表"。验收：单元测试通过；`link:1` 批量跑局的批次配置记录与日志首部与本组开始前逐字节相同
- [x] 3.4 删除过渡口：`BatchRunner` / `Replayer` / `MatchSession` 的 `resolveMap` 参数（内部一律 `MapCatalog.Resolve`）、`MatchFlow.RestoreLegacy`；`map` 子命令只接受 `link:`、缺省 `link:1`；`RunConfig.MapId` 内部缺省改 `link:1`；删除 Sim `Program` 中裸 `gen` / `board` 的取种子分支。验收：单元测试覆盖 `map --map siege-4p-base-v5` 与 `map --map gen:12345` 报错、`map` 无参数打印 `link:1`（算例：`simulation-harness`「各入口支持生成图」）
- [x] 3.5 模式收口：`MatchOptions.Mode` 的 `init` 降为 internal；公开 `MatchFlow.Create` 恒以链接模式建局，`RequireModeMatchesMap` 只保留"链接模式须 4 人、须链接图"；`CreateUnvalidated` / `RestoreUnvalidated` 照旧按 `options.Mode`。先 Grep 核对测试中调用公开 `Create` 而未设 `Mode = Link` 的点并改走 `CreateUnvalidated` 或链接夹具。验收：`MatchFixtures` 文件 git diff 为空，其全部使用者通过且期望值未改
- [x] 3.6 Sim 与图形版去掉模式分叉：Sim `MatchSession`（建局分叉与 Legacy 侧日志字段分支）、`BoardRenderer`；图形版 `MatchSession`（建局分叉）、`GameRoot`（自动演示判定、开局提示）只留链接一侧。验收：单元测试通过；`link:1` 日志与存档逐字节不变（三条决策基线 + 链接模式存档 / 回放测试）
- [x] 3.7 L10：删除只服务通道 / 公共棋盘的渲染样式（`BoardView`、`Visuals` 等）；若发现被链接演出复用则保留并在清单中记录。验收：`dotnet build` 通过；后续 5.3 的三条自检与截图无回归

## 4. 简化校验器与规格档（D1 第 4 步）

- [x] 4.1 `MapProfile` 只留 `Link`；`MapData.Profile` 缺省改 `Link`；`MapFile` 恒写规格档字段，读入时字段缺失或为 `Standard` / `Frontier` / `Board` 拒绝并说明已移除（L7）。验收：单元测试覆盖三种拒绝；`link:1`–`link:20` 初始地图与各一条链接序列后的导出文件、日志首部地图摘要与第 3 组结束时逐字节相同
- [x] 4.2 `MapValidator` 声明表只剩链接一行；删除 `DistanceHandling` 的 `RejectOnImbalance` / `AlwaysReport`、`BirthZonesMustReachEntrance`、`RequireLandmarks` 为真的分支、`AllConnected` 连通性、通道宽 / 长规则与旧档的列宽区间；保留结构、地形边界、规模预算、出生区（保护期容量、出生区内无新地表）、信物格、容差、必死口袋与拼接后校验。验收：规格档分流守门（原在 `边疆档静态校验Tests`，1.6 迁出后的位置；口径：规格档字面量只在声明表内、规格档属性只在 `RulesOf` 读一次）通过；`link-map-generation`「全部链接序列都能拼接」穷举通过；算例（`map-definition`「地图静态校验规则」）：6 块出生棋盘被拒、出生棋盘内浅滩被拒、出生棋盘 3 枚信物被拒、信物格落在场景格被拒
- [x] 4.3 删除清单中第 3、4 步标"删"的测试，复核 1.1 清单全部勾销。验收：全量测试全绿；相对第 1 组开始前，通过数的净变化 = 新增护栏数 − 清单全部"删"的条数，逐条可对上

## 4b. 实施与质检后的补充（L15 / L19 / L20 / L21）

- [x] 4b.1 L15：修终端链接局拼接后 `PlayCommand.Summarize` 越界崩溃，补一条终端链接局跑过拼接的测试。
- [x] 4b.2 L19：删除校验器中链接图走不到的 `BIRTH_ZONE_COUNT_MISMATCH` 分支（不改行为）。
- [x] 4b.3 L20：删除 Sim"参赛人数缺省取地图上限"逻辑及其恒等路径。
- [x] 4b.4 L21：在 `link:1` 上为简单、困难难度各补钉一条决策序列基线（口径同现有链接图基线）。

## 5. 文档同步与回归

- [x] 5.1 更新 `HANDOFF.md`：去掉旧地图、`gen:` / `board:`、冒险概率、地图覆盖、`map` 子命令旧标识的说明；登记后续 change `retire-legacy-rules`（L6 缺省）与"Core 保留只服务手写夹具的 Legacy 规则分支"这一债务。验收：人工检查
- [x] 5.2 同步设计文档当前章节（§3 地图来源只剩链接档、§4 冒险概率已删、§16–§17 基线改 `link:1` 三种子、AI 校准口径注明"校准地图已退役、链接图上未复核"、§19 后续扩展登记 `retire-legacy-rules`）；历史变更记录不改（L5）。验收：人工检查
- [x] 5.3 最终回归（顺序单跑，看真实退出码）：`dotnet test` 全量；Godot console 版 headless 三条自检 `-- --auto-demo`、`-- --auto-demo --pick-check --seed=5`、`-- --map-select --auto-demo`；`-- --auto-demo --rounds=0 --seed=5`（七步齐全）。验收：全部退出 0，`[flow]` 与拾取自证行与 `board-link-mode` 段 E 记录一致
- [x] 5.4 `openspec validate retire-legacy-maps --strict` 通过后归档；`map-generation` 保留说明性 Requirement（L14 缺省）。验收：归档后 `openspec validate --specs --strict` 34 份主规格全过
