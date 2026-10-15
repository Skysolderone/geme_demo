# retire-legacy-rules 实施进度

> 每完成一项追加一行（项号 / 结果 / 关键数字 / 产物），便于中断后续接。产物目录：`g1/`（第 1 组）。

## 第 1 组（tasks 1.1–1.7）

- 开始前基线：`dotnet test` 全量 Passed 2150 / Skipped 7 / Total 2157（RC=0，4 m 12 s）；`g1/test-before.log`、测试名清单 `g1/tests-list-before.txt`（2157 行）。
- 1.1 完成：脚本 `g1/scan_legacy.py`（方法切分 + 同文件 / 跨文件助手闭包；L = Legacy 建局 / 名次，D = 只用 9×9 地图数据，M = MatchMode / ComputeLink 写法，K = 只引常量）→ `g1/scan-before.txt`：138 个文件，L 381、D 13、M 17、K 20 条方法；助手闭包 `g1/helpers-before.txt`。比对 `g1/compare_list.py` → `g1/compare-before.txt`：27 个文件计数 / 分类按脚本改正后出入 0；(c) 28 + (j) 5 + L18 2 共 35 条逐名命中、无增减；第 4 节迁出源方法全部存在。migration.md §1 各行加"1.1 复核"注、§2 汇总改为 (a) 18 / (b) 324 / (c) 28 / (j) 5 / (d) 7 / (e) 14 / (m) 67。
- 1.2（基础部分）：`TestMaps` 收 P0..P3 / All / Seed、9×9 地图 `NineByNine` 与手工信物 `Relics`；`MatchFixtures` 只剩旧模式建局助手（常量 / 地图委托到 TestMaps）；`AtRound` / `Stones` / `PlayTurn` / `PassTurn` / `StoneCount` / `CountEvents` 迁入 `LinkModeFixtures`；新增平坦 8×8 棋盘 0 链接档图 `PlainMap`（`link:90008`，CreateUnvalidated，强制 Mode = Link、K = 0 并断言）与 `PlainCreate` / `PlainStarted`（缺省四人同锁棋盘 0）、`Shift`。`AiFixtures.Round5` 改建在 `PlainStarted` 上，未迁调用点暂改名 `Round5Legacy`（迁完删除）。坐标改写工具 `g1/migrate.py`（整体 (+2,+2) / (+1,+1) / 四角贴角 split）。期望重钉逐条记 `g1/r8.md`（第 1 组末并入清单第 6 节）。
- 试点：`主动弃赛Tests` 9 条、`停手阈值Tests` 8 条迁完全绿；`停手阈值Tests.无落点被保留则Pass` 按 P2 给 P0 加三枚孤子使其居本战场首位并加前提断言（未改期望）。
- 1.3 完成：(a) 18 条——`终局名次与并列判定Tests` 8 条改调 `ComputeLink`（`出局者倒序` 按 P3 去弃赛者），`CarryFixtures.Result` 改 `ComputeLink` + `LastBattlefieldSettled`（CarryInOut 7 条期望不变），`候选格上限Tests` 阈值解析改读 `TestMaps.NineByNine`，`棋盘清单Tests.清单不参与结算`（P7）改结算层逐步比对（AI 跑局一半换为 60 步随机批次，见 r8），`棋盘台面的可视表现Tests.棋盘清单为空时一律是场景装饰`（P4）在 9×9 GameBoard 上直接组装公开世界（期望不变）。相关类 133 条全绿。
- 1.5 进行中（流程类）：TurnSequence / EliminationEndgame / MatchSetup / MatchFlowRegression / InitiativeOrder / Recruitment / RelicEffects / LifeShape / BatchDeployment / BatchPreview / TerrainEditing / HandInfoPanel / InformationVisibility / SettlementShow / CarryInOut 已迁完并各自全绿；四条迁出（势力悬殊不提前结束、Pass计数跨回合重置 → 两类终局条件Tests；时限可配置 → 盲选插旗Tests；保护期内规则照常 → 战场内合法落子范围Tests.构筑期内规则照常）已做。坐标映射工具加 `--map=auto`（逐轴保住盘边身份）、`--from` / `--until`（只改对局段）；共用环改为 `RingF7H7`（列 +1、行 +2，不贴边）。
- 1.5 完成：上列 13 个目录全部迁完（另含 SettlementShow / CarryInOut 的 (b)）；中途全量 `g1/test-mid1.log`：Passed 2150 / Skipped 7 / Total 2157（RC=0），与开始前相同，15 份基线随全量通过。
- 1.4 进行中：AiDecision 21 个文件迁完（除 (c) 两条「旧模式不变」与 (m) 反面段），AiDecision 全目录 231 通过 / 3 跳过；`Round5` 已建在平坦载体上，`Round5Legacy` 只剩 (c) 两条与 TacticalLayers 未迁调用点。P2 探针（临时在 `TrailsInBattlefield` 返回真时抛异常、跑完按备份逐字节还原 src，`git diff -- src` 为空）：被测 AI 落后且停手阈值非 0 的 4 处（候选格上限 / 高部署上限 的局面 a、b 与种子扰动）已用 `AiFixtures.AsLeader` 补子居首；其余落后处阈值本为 0、决胜一手不触发，链接门不改变行为，另有全员 AI 跑局 3 处无法人人居首——见回报。
- 1.4 完成：两条 (c)「旧模式不变」改直接调 `MatchFixtures.Started()`，`AiFixtures.Round5Legacy` 删除。
- 1.6 进行中：TacticalLayers 11 个文件迁完，目录 102 通过 / 2 跳过；`PresentationFixtures.StartedWithZonedRelics` 改到平坦载体（1.2 剩余项）。
- 1.6 进行中：SimulationHarness 6 个文件迁完，目录 157 通过。
- 1.6 进行中：MatchTelemetry 3 个文件迁完（(c) 一条留第 2 组），目录 121 通过；`活形确立可查` 的贴地形小空区 0 → 2 记入回报。
- 1.6 完成：TacticalLayers / SimulationHarness / MatchTelemetry / MapDefinition（两位数行号贯通）迁完，各目录全绿；残余扫描只剩 (c) / (j) 与 1.7 的 (m) 反面段。
- 1.7 完成：(m) 8 条反面段删完（`盲选插旗Tests` 两条、`全知例外集中可关`、`旧模式与同战场不遮蔽` → `同战场不遮蔽`（同战场半段改到四人同锁棋盘 1）、`默认棋盘视图模型透传棋盘清单`（助手 `Started` 改链接 4 人局、夹具标识换 link:90046）、`预览说明读默认棋盘视图模型`（`ViewOf` 恒链接）、`公开规则纯函数Tests` 两条（1.4 已做））；(d) / (e) 常量与 9×9 地图数据在 (c) / (j) 之外全部改引 `TestMaps`（16 个文件 32 行）。
- 第 1 组收尾验证：`dotnet build --no-incremental` 0 警告 0 错误（`g1/build-after.log`）；全量 `g1/test-after.log`：通过 2150 / 跳过 7 / 总计 2157（RC=0），与开始前相同（首次全量有 1 条 `批量跑局Tests.已删除的选项报已删除(no-catch-up)` 控制台捕获为空的偶发失败，单独重跑与第二次全量均通过）；15 份基线未改动、随全量通过。残余扫描 `g1/scan-after.txt` + 比对 `g1/compare-after.txt`：Legacy 建局残留 33 个方法，与清单 §3 的 (c) / (j) 逐名一致（L18 两条不属 Legacy 建局）；关键字 grep 存 `g1/grep-after.txt`。测试名单比对 `g1/names-compare.txt`：13 个名字变化全部是 InlineData 键随平移、改名表与 4 条迁出（无增删）。§6 已回填 117 行。
- 1.7 验收补充：`g1/grep-after-annotated.txt` 把 51 行关键字命中逐行归类（§3 (c)/(j) 41 + 其专用助手 FullBoard 1 + MatchFixtures.cs 定义 2 + 注释 4 + 续行误报 3，无未归类）。

## 第 2 组（tasks 2.1–2.2）

- 开始前：重生成测试名清单 `g2/tests-list-before.txt`（2157 行），与 `g1/tests-list-after.txt` 逐行相同；即通过 2150 / 跳过 7 / 总计 2157。
- 2.1 完成：脚本 `g2/delete_methods.py` 按清单 §3 (c)/(j) 名单删 15 个文件里的 21 条方法（`g2/delete-log.txt`）；整文件删除 `三类终局条件Tests`（6）、`匿名同时插旗Tests`（3）、`构筑保护期的落子限制Tests`（1）、`第4大回合的全图解禁Tests`（2）；`对局模式Tests` 改名 `建局约束Tests`（保留 `链接模式不能配旧地图`、`链接模式只有四人局`，类头注释随改）。孤立助手复核：只 `合法落子范围的对外契约Tests.CornerG1J1` 随删（`非法批次必须给出可定位的原因Tests.RingE5G5` 为 internal、现只剩 cref 引用，未动）。差集 `g2/diff_names.py` → `g2/names-diff.txt`：2157 → 2124，减少 33 行 / 33 个方法，经 §4 改名映射后与 §3 #1–#33 逐名一致（(c) 28 + (j) 5），无清单外增减；33 条全是 Fact，Theory 行数减少 0（与清单一致）。L18 两条（#34、#35）留第 4 组。
- 2.2 完成：`RandomTurnController` 原样迁出为独立文件 `tests/Siege.Core.Tests/RandomTurnController.cs`（`流程回归Tests`、`输出契约Tests`、`百局端到端Tests` 在用）；`MatchFixtures.cs` 删除（残余调用点已随 2.1 归零，`TwoPlayer` 无调用者）；`tests/` 内 `MatchFixtures` 字样只剩 5 个文件的注释，措辞改为"原 9×9 流程夹具"（TestMaps、LinkModeFixtures、ShowFixtures、区域强度预算Tests、战场的定义与规则作用域Tests），Grep `MatchFixtures` 在 `tests/`（不含 bin / obj）为零。
- G4：migration.md §1"迁移去向"栏按第 1 组实际载体更正（逐文件扫描 `g2/carriers.py` → `g2/carriers.txt`，改写脚本 `g2/g4_fix.py`）：同区四人缺省短语 73 处、地形类 7 处改为 `PlainStarted` / `PlainMap(terrain)`（`link:90008`），13 行特例写明 `link:90005 / 90006 / 90012 / 90013 / 90032 / 90046` 的实际用途；§1 首加 G4 注；§0 (b) 定义同步改（超出"第 1 节"字面，为免与 §1 自相矛盾）。§3 / §4 / §6 未动。
- 第 2 组收尾验证：`dotnet build siege.sln --no-incremental` 0 警告 0 错误（`g2/build-after.log`）；全量 `g2/test-after.log`：通过 2117 / 跳过 7 / 总计 2124（RC=0，2 m 59 s），通过数减少 33 = (c) 28 + (j) 5；15 份基线（`AiDecision/Fixtures/`）未改动、随全量通过；`git status -- src` 为空。
- 产物：`g2/`（`delete_methods.py`、`delete-log.txt`、`diff_names.py`、`names-diff.txt`、`carriers.py`、`carriers.txt`、`g4_fix.py`、`build-after.log`、`test-after.log`）；两份 `g2/tests-list-*.txt` 可重生成，按 G6 口径不入库。
