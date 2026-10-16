> 前置：负责人已过目迁移清单，2026-10-15 裁决 R6–R12 全按缺省（R7 = 窄解，R4）。下列任务按此编写。
> 每组对应 design D1 的一步：组内全部完成、组末验收全部满足才进入下一组；每组单独提交。全程不跑批量（R5）。"15 份基线"= `AiDecision/Fixtures/` 下 5 条链接图决策序列基线 + 5 份 `*-omniscient.txt` + 5 份 `*-k0.txt`。

## 1. 按迁移清单逐类迁移（D1 第 1 步：Legacy 分支仍在，证明原断言在新载体上成立）

- [x] 1.1 复核清单：用脚本按方法名重列受影响方法（口径同清单第 0 节），与清单逐文件比对，分类有出入的按 R1 定义改正并回填条数；(c) / (j) 名单若有增减，先报负责人再继续。验收：清单第 1–3 节与脚本输出逐名一致（人工检查 + 脚本输出存入任务目录）
- [x] 1.2 建载体：9×9 地图数据与常量 `P0..P3` / `All` / `Seed` 迁入 `TestMaps`；`AtRound` / `Stones` 等扩展迁入 `LinkModeFixtures`；新增手写链接档图助手（新地表 / 深水 / 林地 / 高差 / 栅栏放棋盘 0 中间带，经 `CreateUnvalidated`）与 8×8 棋盘 0 平坦变体（需要时）；`AiFixtures.Round5`、`PresentationFixtures.StartedWithZonedRelics` 改建在 `StartedFlat([0,0,0,0])` 上（两条 `旧模式不变` 用例暂改直接调 `MatchFixtures`，第 2 组随之删除）。验收：`dotnet build` 零警告；全量测试全绿、通过数不变
- [x] 1.3 迁 (a) 规则层 21 条：`终局名次与并列判定Tests` 8 条与 `CarryInOut` 出局 / 名次补给点表 / 完赛 / 弃赛结算 10 条改调唯一名次实现的现行链接版（此时仍名 `ComputeLink`），`候选格上限Tests` 的阈值解析、`棋盘清单Tests.清单不参与结算`、`棋盘台面的可视表现Tests` 一条。算例：设计文档 §12.5 名次比较链——势力相同（60 / 60）、信物 2 / 2、独占空格 14 / 9 → 14 者名次高（`信物相同比独占空格数`，期望不变）；`出局者倒序` 按清单 P3 去掉输入中的弃赛者，断言"第 8 大回合出局者高于第 5 大回合出局者"。验收：单元测试通过，(a) 类期望值逐字未改（diff 只改调用与夹具）
- [x] 1.4 迁 (b) AiDecision 21 个文件：缺省载体同区四人 + `AtRound(5)`，被测 AI 构造为本战场势力首位或独处（清单 P2）；`前瞻模拟的公平信息Tests.保护期内只在出生区回应` 改名 `构筑期内只在本战场回应`。算例：设计文档 §15.2 专家前瞻——第 2 大回合模拟下一名对手，部署上限 = 3 + 其控制的军令强度，回应落点全在其战场内（`ai-decision`「前瞻模拟的公平信息」Scenario「构筑期内只在本战场回应」）。验收：单元测试通过；数值期望变化逐条记入清单第 6 节（R8），关系断言不变；15 份基线逐字节通过
- [x] 1.5 迁 (b) 对局流程类（EliminationEndgame、TurnSequence、MatchSetup、MatchFlowRegression、InitiativeOrder、Recruitment、Relic*、LifeShape、BatchDeployment、BatchPreview、TerrainEditing、HandInfoPanel、InformationVisibility）：按清单迁移，四条迁出（`势力悬殊不提前结束`、`Pass计数跨回合重置` → `两类终局条件Tests`、`时限可配置` → `盲选插旗Tests`、`保护期内规则照常` → `战场内合法落子范围Tests.构筑期内规则照常`）。算例：设计文档 §12.2 / §12.5——同区四人第 5 大回合 P3 以弃赛时名次第 1 弃赛，其余三人整轮 Pass → 该大回合结束战场结算、终局原因"最后战场结算"，P3 名次第 4（`主动弃赛Tests.弃赛名次不影响最终名次`）。验收：单元测试通过，通过数不变；15 份基线逐字节通过
- [x] 1.6 迁 (b) 表现与 Sim 类（TacticalLayers、SettlementShow、SimulationHarness、MatchTelemetry、MapDefinition、MapSelection）：终端渲染快照、两位数行号、出生区编号显示、盘面层视图模型带地形按新载体重导并记入清单第 6 节（R8）。算例：`turn-sequence`「合法落子范围的对外契约」——同区四人第 5 大回合，A 的已活棋串眼空间 `E5`、`G5` 平移到棋盘 0 后，B 的禁入格标示恰为平移后的两格、A 本人看到可落子（`活形与禁入格的标示Tests`）。验收：单元测试通过，通过数不变
- [x] 1.7 (d) / (e) / (m) 机械修改：9×9 地图数据与常量改引 `TestMaps`；删"反面段"的 8 条按清单只删 Legacy 对照段（`盲选插旗Tests` 两条、`正式对战AI的信息边界_链接模式Tests.全知例外集中可关`、`他战场玩家数值不进HUD视图模型Tests.旧模式与同战场不遮蔽` 改名 `同战场不遮蔽`、`棋盘清单Tests.默认棋盘视图模型透传棋盘清单`、`选图视图模型Tests.预览说明读默认棋盘视图模型`、`公开规则纯函数Tests` 两条的合法范围旧路径段）。验收：全量测试全绿，**通过数与第 1 组开始前相同**；除 (c) / (j) 方法外 Legacy 建局调用点归零（Grep 输出存档并逐个对上清单）

## 2. 删 (c) 与 (j) 测试（D1 第 2 步）

- [x] 2.1 删清单第 3 节 (c) 28 条与 (j) 5 条；整文件删除 `三类终局条件Tests`、`匿名同时插旗Tests`、`构筑保护期的落子限制Tests`、`第4大回合的全图解禁Tests`；`对局模式Tests` 改名 `建局约束Tests`（保留两条）。验收：全量测试全绿；通过数减少 = 33、Theory 行数减少与清单一致，**逐名对上**（脚本比对删除前后的测试名清单，差集存入任务目录）
- [x] 2.2 删 `MatchFixtures.cs`（`TwoPlayer` 无调用者）。验收：`dotnet build` 零警告；Grep `MatchFixtures` 在 `tests/` 为零

## 3. 删 Legacy 分支与 `MatchMode`（D1 第 3 步，design D4–D9）

- [x] 3.1 插旗恒盲选（D7）：删 `MatchOptions.Mode`、`FlagPlanting` 的 `mode` 参数、`IsBlind`、`LeastPopulatedZone`、`FlagPublicView.FlagCountByZone`。算例：`match-setup`「匿名同时插旗」——玩家选中棋盘 1 后改选棋盘 3 被拒，锁定结果仍为棋盘 1。验收：单元测试通过；`盲选插旗Tests` 全过
- [x] 3.2 合法范围恒按战场（D8）：`PublicRules.LegalRange` 去 `mode` 与 `majorRound` 参数，删保护期 / 全图分支；`MatchFlow`、`ExpertLookahead` 调用点随改。算例：`turn-sequence`「合法落子范围的对外契约」Scenario「范围随链接扩大」——第 3 大回合为出生棋盘格集合，首轮链接后第 4 大回合为合并战场格集合，都不含禁入格。验收：单元测试通过；15 份基线逐字节通过
- [x] 3.3 终局与名次（D6）：`CheckEndConditions` 只剩"只剩一名"；`Finish` 恒走链接口径；删 `FinalStandings.Compute`（旧），`ComputeLink` 更名 `Compute`；删 `EndReason.AllPassed` / `BoardFull`，Sim `BalanceAnalyzer.RuleReasons` / `ReportWriter` 与相关测试改用字符串常量（R9），`PlayCommand` 与 `godot/scripts/Names.cs` 删两条文案。算例：设计文档 §12.4——4 人同锁一块棋盘、某大回合全员 Pass → 该大回合结束战场结算，胜者第 1，其余三人以战场落败出局（`两类终局条件Tests.四人同区一次终局`）。验收：单元测试通过；`MatchTelemetry/平衡分析方向Tests`、`收敛口径Tests` 的报告文本逐字节不变
- [x] 3.4 流程与视图其余分支（D4）：`MatchFlow`（建局、弃赛 / 出局的减员结算、发布、`EndMajorRound`、`RequireModeMatchesMap` → `RequireLinkMatch`）、`MatchFlow.Link`（`Mode`、`RequireLinkMode`、`LinkStage` 改非空）、`.Countdown`、`.Preview`、`.SeatProjection`、`MatchPublicView` / `SeatPublicView`（去 `Mode`）、`ExpertLookahead` / `HeuristicTurnController` 的模式门、Presentation `DefaultBoardView` / `Worlds`；删 `MatchMode` 枚举。算例：`ai-decision`「前瞻中的下一名对手」Scenario「他战场玩家不进名册」——专家 P1 与 P2 同在战场 1，P3、P4 在未链接的战场 4，本大回合顺序 P1、P3、P2、P4 → 下一名对手为 P2。验收：`dotnet build` 零警告；全量测试全绿、通过数不变；15 份基线逐字节通过；Grep `MatchMode` 在 `src/`、`tests/` 为零
- [x] 3.5 存档只剩链接路径（D5 / D9）：`Serialize` 恒写 `Mode = "Link"` 与 `Link` 段；`Restore` / `RestoreUnvalidated` 删"直接恢复盘面与信物账本"分支，删 `RelicLedger.Restore`（R12，测试对照改用 `Copy()`）；W13 只删 `FinalCountdownBackfilled` 的模式条件，缺 K 回填为 0 并留痕保留（R7 窄解）。算例：`match-setup`「决赛倒计时配置」Scenario「旧存档按不启用读取」——恢复一个不含 K 的链接存档 → K = 0，恢复后行为与保存前一致。验收：`链接模式存档Tests`、`决赛倒计时配置Tests`、`对局持久化Tests` 全过；一份链接存档 `Serialize` 的输出与第 3 组开始前逐字节相同（固定种子对照，输出存入任务目录）

## 4. L17 与 L18（D1 第 4 步，design D5 / D10）

- [x] 4.1 L17 Sim 读模式分支：`MatchSession` 第 92、706–708、782、941–946、1026 行的模式条件变无条件，首部 `Mode` 写常量 `"Link"`；`BoardRenderer` 删旧模式全图视口与"旧模式恒同战场"；`Replayer` 对非链接日志报错；`MatchLog.IsLinkMode` 保留为读端谓词（R10）。验收：单元测试通过；`link:1` 种子 1 的日志首部与第 3 组结束时逐字节相同；`终端对局Tests` 的链接局转录逐字节不变
- [x] 4.2 L18：删 `MapValidator.ValidateLandmarks` 与 `ValidateTolerance`（四个失败码）；`MapData` / `MapFile` / `LinkMapGenerator` / `DistanceTable` 不动；删 `地图静态校验规则Tests` 的 `中央入口与咽喉必须位于可落子格`、`放宽容差必须写明理由` 两条与 `尺寸非正与越界障碍被报出` 的 `TOLERANCE_NEGATIVE` 段；按 `map-definition` 新 Scenario 补 1 条"中央入口在场景格、容差 3 无理由仍通过且字段往返不变"。验收：单元测试通过，通过数 = −2 + 1，逐名对上；`link:1`–`link:20` 初始地图与各一条链接序列后的导出文件、日志首部地图摘要与第 3 组结束时逐字节相同（对照脚本输出存入任务目录）

## 5. 文档同步与回归

- [x] 5.1 更新 `HANDOFF.md`：删"旧模式（Legacy）只剩 Core 测试里的手写 9×9 夹具……是登记的债务"一段，后续 change 第 6 项标为已实施并写一句结果（删除的分支、迁移条数、(c) 条数）。验收：人工检查
- [x] 5.2 同步设计文档（含 §19 登记 G1 / G2 / G5 三个观察项，HANDOFF 同步）：§3.3 删"Core 仍保留只服务手写 9×9 测试夹具的旧模式规则分支"一句；§19 第 6 项标为已实施；文首版本 v1.26 → v1.27；变更记录追加 v1.27 一行（R1–R12 裁决、删除范围、迁移与删除条数、W13 / L17 / L18 处理）。验收：人工检查
- [x] 5.3 同步 `.trellis/spec/core/boundaries.md`：Legacy / `MatchMode` / 模式分流相关条目改为只剩链接规则（"对局配置项的缺省值与缺项回填值"一行按 R7 保留 K 回填的说法）；`testing.md` 若有提及 `MatchFixtures` 的指引改为新载体。验收：Grep `MatchMode|MatchFixtures|旧模式` 在 `.trellis/spec/` 只剩历史引用（逐条列出）
- [x] 5.4 质检（R4）：逐名比对清单第 3 节（(c) / (j)）与第 4 节（改名 / 迁出）；落实补测建议 Q1（链接口径"只剩一名 > 最后战场结算"优先级）与 Q2（出局 / 弃赛后按战场剩余在局者判整轮 Pass，先查 `战场结算Tests`）；复核第 6 节期望重钉表每条的关系断言未变。验收：质检报告存入任务目录
- [x] 5.5 最终回归（顺序单跑，看真实退出码）：`dotnet test` 全量；Godot console 版 headless 四条自检 `-- --auto-demo`、`-- --auto-demo --pick-check --seed=5`、`-- --map-select --auto-demo`、`-- --auto-demo --rounds=0 --seed=5`。验收：全部退出 0，`[flow]` 与拾取自证行与 `retire-legacy-maps` 5.3 的记录一致；15 份基线逐字节通过
- [ ] 5.6 `openspec validate retire-legacy-rules --strict` 通过后归档。验收：归档后 `openspec validate --specs --strict` 全部主规格通过
