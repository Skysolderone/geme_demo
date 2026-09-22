## 0. 前置

- [x] 0.1 提交当前工作区的 `frontier-map` / `map-generator` 改动，完成 `frontier-map` 剩余 1 项任务，两个 change 依次归档。验证：`openspec list` 不再列出二者；`openspec/specs/map-definition` 含「地图规格档」「边疆档基准地图」，`openspec/specs/map-generation` 存在；`dotnet test -c Release` 全绿。
- [x] 0.2 归档后逐条核对本 change 的 `map-definition` / `map-generation` 增量：每个 MODIFIED Requirement 的标题在主规范里存在，正文除据点相关改动外与主规范逐字一致。验证：`openspec validate restore-go-core-rules --strict` 通过。 （已核对：差异全部只与据点、内置图标识有关；另补了 5 个能力的标识改名增量。`frontier-map` 6.1 人工试玩移交 `ai-eye` 5.3。）

## 1. 段 A——军势公式与领地计分（Siege.Core/Scoring）

- [x] 1.1 先写测试：`power-score`「棋串军势公式」八个 Scenario（标准算例 20；位置加值 5 + 基础 6 + 1 倍增 = 16；4 连珠 + 2 倍增 = 40；高地 = 15；逐串取整；4 倍增 = 40；60 枚倍增子精确值；倍率显示 3.375）与 `piece-effects`「倍增子的棋串倍率」四个 Scenario（12 枚倍增子 = 1556）。验证：新测试先红。
- [x] 1.2 改 `PowerCalculator`：`⌊(基础 + 位置加值) × 3^n / 2^n⌋`，去掉封顶常量与"生效倍率指数"；倍率分子、棋串军势、总势力改为不溢出的整数表示（design D1），沿调用链同步改类型（`PowerSnapshot`、`PowerScoreboard`、势力明细、名次比较、公开视图）。验证：1.1 全绿；守门——`Scoring` 下不出现 `double` / `float` / `decimal` 参与计分；变异验证（把位置加值挪到乘法之外应红；把指数夹到 3 应红）。
- [x] 1.3 领地计分：总势力 = 独占空格数 + 棋串军势；独占空格直接取空格归属结果，不新增覆盖统计。测试：`power-score`「总势力」六个 Scenario（12 + 20 + 7 = 39；孤立普通子 = 5；领地不参与倍率 = 19）、`coverage-territory`「空格归属三态」五个 Scenario（含空林地格恒为中立）。验证：测试全绿；守门——全仓库只有一处覆盖统计实现；变异（把争议格计入应红）。
- [x] 1.4 势力明细：去掉据点分与据点清单，加领地分总计与独占空格坐标集合，去掉"生效倍率指数"。测试：「势力明细」四个 Scenario。验证：明细可复算总势力与每条棋串军势。
- [x] 1.5 段 A 回归：重算全部受影响的既有算例（军势、预演中的势力变化、AI 评价的势力增量断言）。验证：`dotnet build` 零警告，`dotnet test -c Release` 全绿（据点相关测试此时仍在，按"据点分恒为 0"过渡或在段 B 一并删除，二选一写入 implement 记录）。

## 2. 段 B——据点摘除（Board / 地图 / Scoring / Preview / Ai）

- [x] 2.1 删除 `SiteTier`、`SiteAttribution`、`SiteControl`、`SiteValues` 及 `MapData` / `MapFile` / `MapProfile` / `MapSymmetry` / `GameBoard` / `TerrainWriter` 中的据点成员；`MapFile` 读到据点字段时拒绝加载并指出该字段已废弃。测试：`map-definition`「含据点字段的旧地图被拒绝」。验证：编译通过；变异（改成静默忽略应红）。
- [x] 2.2 `MapValidator`：去掉据点数区间、落点、与信物重合、到篝火 / 石碑距离四类检查；距离报告项由五项改三项。测试：`map-definition`「人数适配预算」「地图静态校验规则」的全部 Scenario。验证：全绿。
- [x] 2.3 内置图：`FourPlayerBaseMap` → `siege-4p-base-v5`，`FrontierMapV1` → `siege-frontier-v2`，`maps/*.json` 同步去掉据点并改名；缺省地图改 v5；`MapCatalog` 对旧标识报"未知地图"。测试：「地形与 v4 一致」逐格比较（用 v4 的导出文本去掉据点段作为基准）、边疆档「资源布点」「不是缺省地图」。验证：全绿；黄金值随标识重建并在 implement 记录里写明差异只有据点。
- [x] 2.4 生成器：删除 `FrontierMapLayout.Sites.cs` 的据点布置，保留信物布置；填充步骤不再避让据点。测试：`map-generation`「布局规则」六个 Scenario（种子 1–50 × 平台数 5–8 全过校验）；生成确定性测试的基准值重建。验证：全绿；重新出一份"种子 1–50 布局速览"放 `sim-out/`。
- [x] 2.5 结算与预演：结算第 6 步去掉据点控制；`BatchPreview` 去掉据点变化项，势力变化改为"领地分变化 + 棋串军势变化"。测试：`capture-resolution`「正式结算顺序」五个 Scenario。验证：全绿。
- [x] 2.6 AI 最小改动：`BatchEvaluator` / `EvaluationWeights` 删除据点估值项；默认权重表的校准记录改写为"未校准（restore-go-core-rules 起失效，待 ai-eye）"并加显式未校准标注，守门测试保持存在且通过。验证：`ai-decision` 既有测试全绿；一局 4 AI 对局能跑到终局或被截断，不抛异常。
- [x] 2.7 删除据点相关测试类；`grep -rE "Site|据点" src tests` 只剩与据点无关的命中（如 `WebSite` 之类的误命中需逐条确认为零）。验证：`dotnet build` 零警告，`dotnet test -c Release` 全绿。

## 3. 段 C——出局、终局与对局配置（Siege.Core/Match）

- [x] 3.1 先写测试：`elimination-endgame`「出局判定」六个 Scenario（开局零势力不出局；势力归零立即出局；手牌有子也出局；保护期内不豁免；从未落子者不出局；同时归零同时出局）。验证：先红。
- [x] 3.2 实现"曾建立正势力"单调标记（入存档）与新出局检查：每次结算后 / Pass 后检查全部参赛玩家；删除保护期暂停、逐玩家解除及其状态字段。验证：3.1 全绿；变异（标记可复位应红；只检查行动者应红）。
- [x] 3.3 终局：删除势力碾压（候选、待回应名单、公开视图与存档字段）与大回合上限终局；终局原因枚举只剩三类，优先级"只剩一名 > 棋盘填满 > 整轮 Pass"。测试：「三类终局条件」六个 Scenario（含"势力悬殊不提前结束""轮数再多也不结束"）。验证：全绿。
- [x] 3.4 名次：并列链改为势力 → 信物数 → 独占空格数 → 棋子数；同一结算同时出局者共享名次。测试：「终局名次与并列判定」七个 Scenario。验证：全绿；变异（跳过独占空格数应红）。
- [x] 3.5 `MatchOptions` / `MatchPublicView` / 存档 / 日志首部：删除大回合上限、碾压起始、落后补偿开关三项；读到含这些字段的旧存档时明确报错。测试：`match-setup` 既有测试中三条 Requirement 的测试类删除；新增"旧存档报错"测试。验证：全绿。
- [x] 3.6 删除碾压、大回合上限、保护期免死的测试类与夹具开关；此前为避开这些规则而在建局时显式关闭它们的测试，去掉关闭语句。验证：`dotnet build` 零警告，`dotnet test -c Release` 全绿。

## 4. 段 D——落后补偿摘除（快照 / 征募 / 面板）

- [x] 4.1 效果快照不再读取势力名次；展示数 / 选取数 = 默认值 + 信物；来源拆分只剩"分阶段基础值""信物"。测试：`relic-effects`「效果快照在小回合开始时生成」四个 Scenario（含"名次不影响快照"）、「默认基础值」、`recruitment`「私人征募面板」五个 Scenario（含"最后一名没有补偿"）、`initiative-order`「排名的作用范围」、`hand-info-panel`「公开结构参数与信物来源」三个 Scenario。验证：全绿；守门——快照生成代码不引用势力名次；变异验证。
- [x] 4.2 删除落后补偿的实现、测试类与 Sim 报告段。验证：`grep -rE "CatchUp|落后|补偿" src tests` 无命中；`dotnet test -c Release` 全绿。

## 5. 段 E——Sim、表现层、Godot

- [ ] 5.1 （**部分完成于段 C**：`RunConfig.TurnLimit` / `--turn-limit` / 结束原因 `turn_limit` / 截断局无名次 / `LogResult.Truncated` 已就位；剩旧选项"已删除"报错、配置文件旧键报错、六个 Scenario 测试）`Siege.Sim`：`RunConfig` 去掉据点分值、大回合上限、碾压、补偿选项（严格 CLI：传入旧选项报错并说明已删除）；加入"小回合数截断"（默认 600，0 不截断），只存在于跑局驱动循环；被截断的局记 `turn_limit`、无名次。测试：`simulation-harness`「批量跑局」六个 Scenario。验证：全绿；守门——`Siege.Core` 中不出现截断相关符号。
- [ ] 5.2 日志与分析：`MatchLog` 去掉据点记录、加领地分与结束原因，大数以精确整数文本写入；`BalanceAnalyzer` / `ReportWriter` 删据点、碾压、补偿段，加终局原因分布、截断率、领地分占比，对局长度与地图可落子格数 / 信物格数并列；胜率类指标排除截断局并注明样本数。测试：`match-telemetry` 三条 Requirement 的全部 Scenario（含"大数不失真"）。验证：全绿；旧日志缺字段时按既有约定整局排除并计数。
- [ ] 5.3 终端版：`BoardRenderer` / `ConsoleController` 去掉据点符号与图例，势力栏显示"领地 + 棋串"；大数显示不换行溢出。验证：脚本化终端测试全绿；人工看一局文本盘面。
- [ ] 5.4 `Siege.Presentation`：删除 `SiteView`；势力层内容改为独占格着色 + 领地分 + 棋串分；`Labels` 去掉据点文案。测试：`tactical-layers`「势力层显示领地分」及既有层测试；`information-visibility`「领地分公开」。验证：全绿。
- [ ] 5.5 Godot：删除三档地标与控制旗（`BoardView` / `Visuals` / `LowPoly`）；HUD 势力显示改"领地 + 棋串"，≥ 10^6 用缩写、明细给精确值；选图界面缺省 v5。验证：`--auto-demo`、`--auto-demo --pick-check`、`--screenshot` 在 v5、`--map=siege-frontier-v2`、`--map=gen:12345` 上退出码 0；各截一张图放 `sim-out/` 供负责人过目。

## 6. 段 F——收尾

- [ ] 6.1 设计文档 `2026-09-10-siege-core-gameplay-design-v1.md` 同步：§5.3 去掉落后补偿；§9.2 / §10 改回整体乘倍率、不封顶、领地计分；§11 排名作用范围；§12 出局与三类终局；§3 / §7 去掉据点；文末变更记录加一行并列出被回退的 change。验证：全文 `据点|碾压|大回合上限|落后者|封顶` 无残留（变更记录除外）。
- [ ] 6.2 新规则基线：4 人 v5 图、种子 1–200、同强度 AI 跑 200 局，出报告；重点记录截断率、终局原因分布、平均结束大回合、单串军势峰值、倍增子选择率、第 3 大回合领先者胜率。验证：报告放 `sim-out/`，关键数字写入 implement 记录，明确标注"AI 权重未校准口径"。
- [ ] 6.3 `.trellis/spec/core/` 补充本 change 形成的编码约定（势力值用不溢出的整数类型、截断只在 Sim、内置图内容变更必须递增标识）。验证：`index.md` 的 Quality Check 同步一条"势力 / 军势不得用定宽整数"。（`determinism.md` 的「势力 / 军势用 BigInteger」与 `testing.md` 的「还原后必须刷新时间戳」已在段 A 提前写入，本项只需补其余条目。）
- [ ] 6.4 补强段 A check 报出的两处守门缺口（`implement.md`「段 A check 记录」）：（a）`总势力Tests.领地计分直接取空格归属结果` ② 只扫 `PowerCalculator.cs` 的违禁词与仓库级 `CoverageTargets(` 名单，挡得住"同文件另写统计"（M-AC2 红 1）却挡不住"另起文件照抄一份"（M-AC14 红 0）——补"算式形状 / `OwnershipOf(` 调用者名单"扫描，注意仓库级限制 `OwnershipOf(` 会误伤 Presentation / Sim 的合法读法；（b）`计分路径不含浮点` 用 `Directory.GetFiles` 不递归，`Scoring/` 将来加子目录会静默漏扫，改为递归。验证：两条各配一次变异（照抄到新文件应红、子目录里塞浮点应红）。
- [ ] 6.4b 生成图黄金值扩到 2–3 颗种子（段 B 待决 8）：`MG-14`（河道拐弯加价 6→1）在新的 `gen:12345` 上实跑 **0 红**，单颗种子的导出摘要挡不住布局参数改动；非自证目前只靠 M-B19（桥头相位反转，红 1）。验证：扩种子后重跑 MG-14 必须红，并记录红数。
- [ ] 6.4c 修正往返恢复测试的地图来源（段 D 待决 5）：`对局持久化Tests`、`百局端到端Tests:101`、`原型插旗替代路径Tests:43`、`人工接管Tests:73` 恢复时传的是 `match.Map`，应为 `Board.BaseMap`——有地形改造的局面下会恢复出错误地形（`人工接管Tests.交还后继续` 在段 D 已因此暴露并修正）。验证：给其中至少一条构造含改造的局面，改前红、改后绿。
- [ ] 6.5 全量回归：`dotnet build` 零警告；`dotnet test -c Release` 全绿；`openspec validate restore-go-core-rules --strict` 通过。
