# 10-09-board-link-mode-b 段 B 质检报告（check，2026-10-10）

范围：段 B 前半已提交 `6c7c0bd` + 工作区后半（tasks 2.6–2.10）+ 本次 check 的自修。未提交。

## 1. S8 落实

- `SeatCellView` 原来的字段 `PlayerId? ExclusiveHolder`（只有独占者）换成 `CellOwnership? Ownership`：直接透传势力快照里覆盖表的结果 `power?.Coverage.OwnershipOf(c)`，覆盖表是全项目唯一的归属实现。空格给出 独占+独占者 / 争议 / 中立；被占的格是 Occupied，障碍格是 Obstacle。插旗阶段还没有势力快照，这时为 `null`。只对 `visible` 里的格（本座位战场）逐格读取。原来的 `exclusive` 字典删掉了，`SeatPublicView.cs` 里没有 `OwnershipKind.Exclusive` 字面量。
- 守门白名单：`PowerScore/总势力Tests.领地计分直接取空格归属结果` 只在 `["OwnershipOf("]` 名单里加了 `Siege.Core/Match/SeatPublicView.cs`，另加两行理由注释（裁决 S8，只读投影、不统计领地分）。其余 token 名单和所有断言都没动。
- **字段改名（段 D 接线注意）**：`SeatCellView.ExclusiveHolder` 改名为 `Ownership`，类型改为 `CellOwnership?`。目前没有调用方，段 D 接线时按新名字读。
- 新测试 `InformationVisibility/按座位视图Tests.同战场空格归属三态可读`，验证了这几点：
  - 同战场里能读到 P0 独占、P1 独占、争议、中立四种情况；
  - 每格的归属都与势力快照的覆盖表逐格相等；
  - 独占格集合与 `Players[*].ExclusiveCells` 是同一份；
  - 他人战场（C 的独占格）`CellAt` 为 null，视图里没有任何归属指向 C；
  - 反面：C 自己的视图能读到自己的独占格。
- 变异 M-C1（把争议格改报为中立）：红 1，就是这条新测试。M-C2（把白名单里 SeatPublicView 那一项删掉）：`总势力Tests` 红 1。

## 2. 逐项检查结论（按严重度）

| # | 严重度 | 结论 | 位置 | 处理 / 建议 |
|---|---|---|---|---|
| 1 | 中 | **先手顺序预测在 Link 下不准，已实证。** `ForecastInitiative` 按当前在局者算下一轮顺序，没有先做战场结算。探针局面：`[0,0,3,4]`，P0、P1 本大回合都已 Pass，取预测时只剩最后一人未行动。预测为 `P2,P0,P1,P3`，大回合实际结束后是 `P2,P0,P3`，P1 已经战场落败。除了多出这名落败者，`active` 数和名次也会跟着偏。 | `src/Siege.Core/Match/MatchFlow.Preview.cs` `ForecastInitiative` | **未修，记为 S10。** 改起来不局部：要把 `SettleAtRoundEnd` / `SettleBattlefield` 拆成"无副作用判定 + 执行"两段，否则就会冒出第二份比较链。另外"假设此刻结束"在 Link 下有两种读法："未行动者算没 Pass"或"其余人全 Pass"，结果不同，需要先定口径。目前表现层只拿它做提示，段 D 显示之前须定下来。 |
| 2 | 低 | 规格 Scenario 的可追溯性有缺口（已补 3 条，其余记录在此）。 | — | 已补：`阶段推进Tests.减员触发的决赛等到大回合结束`、`InitiativeOrder/行动顺序跨战场统一Tests`（两个战场共用一轮、等待者在序）。未补，但有覆盖或已排到别的段：<br>• 「空棋盘不阻止终局」：由 `两类终局条件Tests.决赛结算即终局`（开局 `[0,1,3,4]`，棋盘 3 无人且始终未链接，断言终局原因为最后战场结算，且 `OccupiedBattlefields` 只剩一个）与 `四人同区一次终局` 覆盖。两条都没有显式断言空棋盘仍是独立分量，是开局布局保证了这一点；<br>• 「回放按记录配对」：Core 侧由存档恢复按记录重放来覆盖，Sim 回放属段 C；<br>• turn-sequence「构筑期内规则照常」：Link 下同区玩家构筑期内的提子、揭示、势力更新**没有单独验证**。这是推断：规则路径与旧模式共用，旧模式有测试；战场结算部分由 `构筑期也结算` 覆盖；<br>• match-setup「旧存档被拒绝」、match-telemetry「旧日志被拒绝」：按 B5 和实施报告偏离 2 留到段 C。 |
| 3 | 低 | 恢复路径的"Link 须 4 人"校验没有专门测试。M-C5 删掉整个恢复校验后，只有模式那条测试红。 | `MatchFlow.Persistence.cs` `RestoreCore` | 建议段 C 补一条手改的 3 人 Link 存档测试。本次没补：要手工拼一份存档，成本高于收益。 |
| 4 | 低 | **Legacy 存档"逐字节同前"没有和改动前的黄金文本比对过**，靠的是 `WhenWritingNull` 加"字段不出现"断言。原来只查了顶层的 `Mode` / `Link`，名次项里的 `Reason` 没人守：M-C7 去掉它的 `WhenWritingNull`，原有测试 0 红。 | `MatchSetup/链接模式存档Tests` | 已补 `旧模式终局存档不写出局原因`，局面是有出局者也有弃赛者的旧模式终局。补上后 M-C7 红 1。`对局持久化Tests` 原有的往返逐字节断言一直保持全绿。 |
| 5 | 信息 | 实施报告 M-B15 记"`Finish` 模式门反转后旧模式侧 0 红"，不准确。原因是当时只跑了链接相关的测试类；旧测试 `三类终局条件Tests.唯一参赛者获胜` 其实已经会红。 | — | 新增 `终局名次与并列判定Tests.旧模式流程中弃赛者排在势力归零出局者之前`。M-C3 实跑红 3：这条新测试、旧模式的唯一参赛者获胜、Link 的唯一参赛者获胜。 |
| 6 | 低 | 注释过期：`Pairings` / `Links` 的注释还写着"序列化属 2.9"；`SettleAtRoundEnd` / `SettleBattlefield` 还写着"裁决 S6 / S7 待定"。 | `MatchFlow.Link.cs` | 已改，注释引用 S6 / S7 / S9 已确认，并指向 `ComputeLink`。 |
| 7 | 低 | `RelicLedger.RestoreState` 新增时用了 `public`，等于给表现层留了一个能改写账本揭示状态的入口。 | `src/Siege.Core/Relics/RelicLedger.cs` | 已收窄为 `internal`，唯一的调用方是同程序集的 `MatchFlow`。 |
| 8 | 通过 | S6 / S7 / S9 与新写进规格的文字一致。<br>• S6：比较链排完后 `ThenBy(SeedRank)` 定胜者，全同也照样结算。<br>• S7：同一批结算共享一个出局序号，`ComputeLink` 在同一序号内用 `DefeatComparer` 跨战场按比较链排序，链全同才共享名次。<br>• S9：`SettleAtRoundEnd` 跳过在局人数 < 2 的战场，填满也不结算；`SettleByAttrition` 只看原来在局 ≥ 2 的战场。 | `MatchFlow.Link.cs` / `FinalStandings.cs` | — |
| 9 | 通过 | Legacy 不变：`git diff 6c7c0bd~1 -- tests` 里，段 B 之前就存在的测试文件共改了**四个**，既有断言一条未改：<br>① `LifeShape/活形分析性能基线Tests.cs`：S5，新增 `PerfFactAttribute`；<br>② `MapGeneration/棋盘档布局规则Tests.cs`：S5，`生成耗时` 换特性并加一行 using；<br>③ `PowerScore/总势力Tests.cs`：S8 授权的白名单追加一项，另加注释；<br>④ `EliminationEndgame/终局名次与并列判定Tests.cs`：只追加了一个新方法（派发第 3 条要求补的旧模式流程测试）。<br>其余有改动的测试文件都是段 B 新建的。 | — | — |
| 10 | 通过 | 硬边界逐条查过：<br>• 零 Godot，没有 `double` / `float`，没有 `new Random`；<br>• 新子流 `flag-pick`、`relic-link:<min>:<max>` 与现有的 `relic-gen` / `recruit` / `setup` / `zone-pick` / `flag-risk` / `carry-ai` / `carry-draft:<n>` 不撞名；<br>• 坐标都经 `Coord`；<br>• `HasPlayableEmptyCell(battlefield)` 是逐格读 `IsPlayableEmpty`，不是邻接遍历；<br>• 座位视图的栅栏取自 `TerrainData.Fences`，归属来自唯一的覆盖表；<br>• 对局层不读 `MapProfile`（守门测试全绿，用的是 `LinkMapId.IsLinkMap`）；<br>• AI 只在 `ExpertLookahead` 透传了 `Mode`，评价代码没动。 | — | — |
| 11 | 通过 | 存档健壮性：<br>• Link 恢复的顺序是：建空盘 → 重放链接并核对版本号、摘要和新信物格 → `LoadState` 写盘面 → `RestoreState` 恢复揭示；<br>• 模式、地图、人数在恢复时也校验，不符抛 `FormatException`；<br>• 重放不符时的报错会指明链接序号和两个战场。<br>变异 M-C4（不比对摘要）红 1，M-C5（去掉恢复校验）红 1。 | `MatchFlow.Persistence(.Link).cs` | — |
| 12 | 通过 | 代码质量：<br>• `MatchFlow.cs` 里的 Link 分支都很薄，只是调用 `Link.cs` 里的方法，没有需要抽出的局部方法。`Finish` 里落败者那个分支可以抽成 `StandingInputFor`，但收益小，没动；<br>• 异常消息都指明了对象；<br>• 没有发现死代码或遗留桩，`Debug.SettleBattlefield` 是有注释的测试接缝。 | — | — |
| 13 | 通过 | 改动范围只在 `src/Siege.Core/**`、`tests/**`、`openspec/**`、`.trellis/**`；`Siege.Sim`、`Siege.Presentation`、godot 都没有改动。 | — | — |

## 3. 自修清单

- `src/Siege.Core/Match/SeatPublicView.cs`：S8。`ExclusiveHolder` 换成 `CellOwnership? Ownership`，取自唯一的覆盖表；删掉自建的独占字典；注释改为引用 S8。
- `tests/Siege.Core.Tests/PowerScore/总势力Tests.cs`：S8 授权的白名单追加，只加一项，附两行理由注释。
- `tests/Siege.Core.Tests/InformationVisibility/按座位视图Tests.cs`：新增 `同战场空格归属三态可读`，并记下 M-C1。
- `tests/Siege.Core.Tests/EliminationEndgame/终局名次与并列判定Tests.cs`：新增旧模式流程名次测试，并记下 M-C3。
- `tests/Siege.Core.Tests/Battlefield/阶段推进Tests.cs`：新增 `减员触发的决赛等到大回合结束`。
- `tests/Siege.Core.Tests/InitiativeOrder/行动顺序跨战场统一Tests.cs`（新文件）：两个战场共用一轮、等待者在序。
- `tests/Siege.Core.Tests/MatchSetup/链接模式存档Tests.cs`：新增 `旧模式终局存档不写出局原因`，并记下 M-C4 / M-C5 / M-C7。
- `src/Siege.Core/Match/MatchFlow.Link.cs`：四处过期注释，改为引用 S6 / S7 / S9 与已完成的 2.9。
- `src/Siege.Core/Relics/RelicLedger.cs`：`RestoreState` 由 public 改为 internal。

## 4. 构建、测试与变异

- `dotnet build siege.sln -c Release`：0 警告，0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：总计 2249，通过 2238，跳过 11，失败 0。比后半实施报告多 6 条，都是本次新增的测试。
- 变异的做法：脚本备份原字节 → 改 → 构建 → 只跑相关类 → 写回原字节并刷新时间戳 → 逐字节校验，每次都确认还原。

| 编号 | 变异 | 结果 |
|---|---|---|
| M-C1 | `SeatCellView.Ownership` 的争议格改报为中立 | 红 1（`同战场空格归属三态可读`） |
| M-C2 | 白名单删掉 `SeatPublicView.cs` | 红 1（`总势力Tests.领地计分直接取空格归属结果`） |
| M-C3 | `Finish` 的 `ComputeLink` / `Compute` 模式门反转 | 红 3（新旧模式流程名次、旧 `三类终局条件Tests.唯一参赛者获胜`、`两类终局条件Tests.唯一参赛者获胜`） |
| M-C4 | `RestoreLink` 不比对地图摘要 | 红 1（`链接重放与存档不符即拒绝`） |
| M-C5 | 恢复路径去掉模式 / 地图 / 人数校验 | 红 1（`链接图上缺模式字段的存档被拒`） |
| M-C7 | `StandingSaveData.Reason` 去掉 `WhenWritingNull` | 红 1（`旧模式终局存档不写出局原因`）；补这条之前 0 红 |
| 探针 | 用临时测试实测 `ForecastInitiative` 的偏差（见 §2-1） | 预测与实际不符，已证实；探针文件已删除 |

## 5. 是否建议提交

**建议提交。** 没有阻塞项。S10 只影响表现层的提示，可以留到段 D 之前裁决。

需要裁决的事项：

| # | 事项 | 现状 | 建议 |
|---|---|---|---|
| S10 | Link 模式下的先手顺序预测 `ForecastInitiative` | 不知道大回合结束时要先做战场结算，会把即将落败的玩家算进下一轮，名次和参赛人数也会偏（已实证） | 先定口径："假设此刻结束"指未行动者算没 Pass（建议，与旧模式"此刻结束"字面一致）。然后在段 D 之前把战场结算拆成"无副作用判定 + 执行"，预测与 `SettleAtRoundEnd` 共用这一份判定；也可以在段 D 改用结算后的顺序。在定下来之前，段 D 不要在 Link 局显示这项预测，或者标注"未计战场结算"。 |
