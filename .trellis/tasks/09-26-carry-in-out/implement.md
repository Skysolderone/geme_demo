# 09-26-carry-in-out 实施记录

## 段 A——Core 规则、补给效果、结算、对局配置与存档兼容（tasks 1.1–1.7）

### 改动文件

| 文件 | 内容 |
|---|---|
| `src/Siege.Core/Carry/Supplies.cs` | 新增。`SupplyKind`（`SpareStone / DraftLot / Commission`，末尾追加式）、`CarryIn(Kind, Type?)`、`Supplies`（固定次序；价格常量名带 `Uncalibrated`：3 / 2 / 4；`InitialHand` 给出开局手牌：无带入 5、备用子 6、征召签 / 换型令 4 + 1） |
| `src/Siege.Core/Carry/CarryCandidates.cs` | 新增。`CarryCandidates.Of(ContentSet)`：`ContentSets.PieceTypesOf` 去掉普通子、倍增子、匠人，权重取 `RecruitWeights.BaseWeightOf`，不另抄表。`Draw(seed, player, set)`：每名玩家用自己的子流 `carry-draft:<编号>` 加权抽一次 |
| `src/Siege.Core/Carry/CarrySetup.cs` | 新增。`Resolve` 负责校验并解析带入：关闭时有带入、带入者不在名册、备用子带类型、换型令缺类型或类型不是候选，一律抛 `ArgumentException` 并说明原因；征召签在这里解析；已记录的征召签类型必须与种子抽签一致。无带入时原样返回，不派生子流 |
| `src/Siege.Core/Carry/CarryOutSettlement.cs` | 新增。`CarryPoints`（2–4 人点数表，字段名带 `Uncalibrated`）、`CarryOutcome`、`CarryOutResult(Outcome, Rank, Points, CarryIn, Returned)`、`CarryOutSettlement`：`Finished` / `Resigned` / `Eliminated` / `Unsettled` / `ResignShare`（向下取整）/ 整局 `Settle`。弃赛者读快照里的名次与大回合；`result == null` 表示截断局，全员未结算；保护期边界用 `MatchFlow.BuildProtectionRounds` |
| `src/Siege.Core/Determinism/GameSeed.cs` | 新增 `CarryAi = "carry-ai"`、`CarryDraft(int) => "carry-draft:<n>"`。`carry-ai` 在段 B 使用 |
| `src/Siege.Core/Match/FlagPlanting.cs` | `MatchOptions` 新增 `CarryInOut`（缺省 false）与 `CarryIns`（`ImmutableSortedDictionary<PlayerId, CarryIn>`，缺省用 `.Empty` 单例，`Default` / `Immediate` 的相等性不受影响） |
| `src/Siege.Core/Match/MatchFlow.cs` | `Build`：仅在有带入时调用 `CarrySetup.Resolve`，把解析结果写回 `options`，再交给 `HandLedger`；无带入时选项对象不换。新增属性 `CarryInOut` / `CarryIns` / `CarryInOutBackfilled`。`Resign`：改状态前用 `ResignationRank.Compute` 算出名次并写进快照。`Publish` 输出开关与带入 |
| `src/Siege.Core/Match/ResignationSnapshot.cs` | 快照末尾加 `int? RankAtResign`。新增纯函数 `ResignationRank.Compute`：`1 + 严格高于本人的未出局玩家数`，此前已弃赛者计入，并列共享较高名次。`FinalStandings` 不读取它 |
| `src/Siege.Core/Match/MatchPublicView.cs` | 末尾追加 `CarryInOut`、`CarryIns` |
| `src/Siege.Core/Match/MatchFlow.Persistence.cs` | 新增 `MatchSaveData.CarryInOut`（bool?）、`CarryIns`（`List<CarryInSaveData>?`）、`ResignationSaveData.RankAtResign`（int?），以及新类 `CarryInSaveData`。新存档总是写出这些字段，关闭时也写 `false` / `[]`。旧存档缺 `CarryInOut` 时按关闭、无带入回填，`CarryInOutBackfilled` 留痕；旧弃赛记录的名次为 null，不重算。恢复时对带入调用同一个 `Resolve` 做校验 |
| `src/Siege.Core/Recruit/HandLedger.cs` | 新增带 `carryIns` 的构造重载，原 4 参重载转发并传 null。开局手牌按 `Supplies.InitialHand` 计入基数；`_recruit` 子流的派生位置不变，构造过程不消费它。`Restore` 仍走 null |
| `tests/.../CarryInOut/` | 新增：`CarryFixtures` 与 6 个测试类（补给种类与开局效果 5、换型候选类型 7、名次补给点表 5、完赛结算 2、弃赛结算 6、出局结算 3） |
| `tests/.../MatchSetup/带入带出配置Tests.cs` | 新增 8 条：6 个 Scenario，另加「关闭时有带入即拒绝（含未知玩家）」「征召签与其他玩家的带入无关」 |
| `tests/.../Recruitment/初始配置与基础棋池Tests.cs` | 追加「带入改变开局手牌」 |
| `tests/.../EliminationEndgame/主动弃赛Tests.cs` | 追加 4 条：弃赛时势力名次、此前已弃赛者计入弃赛名次、弃赛名次不影响最终名次、弃赛名次随存档往返。「弃赛快照可记录」补一条断言 `RankAtResign == 1`，因为该 Scenario 已被修改为包含名次 |
| `tests/.../InformationVisibility/始终公开的信息Tests.cs` | 追加「带入公开」 |

未改动：`Siege.Sim`、`src/godot`。本段不需要任何占位；档案 I/O、AI 带入（`carry-ai` 的消费方）、终端和 Godot 都放在段 B / C。

### 先红后绿

- 先写最小骨架，方法体一律 `throw new NotImplementedException()`，保证测试能编译、在运行时变红，避免"编译失败的假红"。新增或修改的用例共 47 条，其中红 40 条、绿 7 条。
- 绿的 7 条：「主动弃赛」中 4 条原有用例没动；另外 3 条本身就是断言"不变"的 Scenario，改动前就应该成立：「不带入者不变」「效果只在开局」「开启但无人带入时逐步相同」。它们是否有约束力，由下文的 M-B2 / M-F1 / M-F2 证明。
- 本段新增用例 42 条（另有 1 条既有用例追加断言）。实现完成后，全量 `dotnet test -c Release` 为 1614 通过、5 跳过。改动前的全量没有实测过。

### 变异（`scratchpad/mutate_a.py`，全量测试工程，逐条红数）

脚本做法：二进制读写，按每个文件实际的行尾归一锚点，并 `assert count == 1`；还原放在 `finally` 里，逐字节比对后调用 `os.utime` 刷新 mtime。还原后重新 build，全量测试仍为 1614 绿。

| 变异 | 红 | 红的测试 |
|---|---:|---|
| M-A1 候选含倍增子 | 4 | v2候选 / v1候选 / 征召签的抽取概率 / 拒绝非候选类型(Multiplier) |
| M-A2 候选含匠人 | 4 | 同上（Artisan） |
| M-A3 v1 候选含旗手子 | 2 | v1候选 / 拒绝不合形的带入 |
| M-A4 换型令价改 5（违反 ⌈P/2⌉ > 价） | 3 | 任一名次半数向上取整大于任一价格 / 完赛优于同名次弃赛 / 补给价格 |
| M-A5 4 人末名改 8（违反不等式） | 5 | 不等式守门 / 四人局查表 / 完赛优于同名次弃赛 / 并列共享 / 向下取整 |
| M-A6 只改测试：对调价格期望 | 1 | 补给价格 |
| M-A7 只改测试：对调点数表期望 | 1 | 四人局查表 |
| M-B1 征召签改用共享子流 `carry-draft` | 1 | 征召签与其他玩家的带入无关 |
| M-B2 有带入时构造手牌消费 `recruit` | 2 | 效果只在开局 / 带入改变开局手牌 |
| M-B3 备用子给出 5 + 2 | 2 | 备用子 / 对局中不可改 |
| M-C1 弃赛名次计入出局者 | 1 | 已出局者不计入弃赛名次（合成样本：出局者势力 99） |
| M-C2 只在参赛者中排 | 2 | 此前已弃赛者计入弃赛名次（纯函数与真实局各 1 条） |
| M-C3 并列取较低名次（`>=`） | 4 | 弃赛百分之五十 / 此前已弃赛者… / 弃赛时势力名次 / 弃赛名次随存档往返 |
| M-C4 `Finish` 把弃赛名次第 1 者当完赛者（FinalStandings 读弃赛名次） | 4 | 弃赛名次不影响最终名次 + 3 条既有终局 / 报告测试 |
| M-D1 弃赛用最终名次 | 1 | 弃赛时第1名 |
| M-D2 弃赛向上取整 | 2 | 向下取整（真正的守门）+ 既有源码扫描「名次半数阈值算式不在源码中出现」（它禁止 `(x + 1) / 2` 这种写法，是顺带红，与取整规则无关） |
| M-D3 出局仍返还 | 1 | 出局丢失 |
| M-D4 完赛也返还 | 2 | 完赛第1名 / 完赛优于同名次弃赛 |
| M-D5 并列取位置而非共享名次 | 1 | 并列共享 |
| M-D6 保护期判据 `<=` 改 `<` | 1 | 保护期内弃赛不带出补给点 |
| M-E1 缺字段回填为开启 | 1 | 旧存档按无带入读取 |
| M-E2 存档漏写 RankAtResign | 1 | 弃赛名次随存档往返 |
| M-E3 存档漏写各玩家带入 | 1 | 新存档往返 |
| M-E4 公开视图不给带入 | 2 | 征召签 / 带入公开 |
| M-F1 建局时（任何局）多消费一次 `recruit` | 12 | 关闭时逐步相同 / 开启但无人带入时逐步相同；另有 10 条既有黄金 / 走法测试被顺带触发，说明逐步相同还有既有守门兜底 |
| M-F2 开启且无人带入时给 P0 塞一件备用子 | 1 | 开启但无人带入时逐步相同（钉开局手牌那条断言） |
| M-F3 关闭时有带入不拒绝 | 1 | 关闭时有带入即拒绝 |
| M-F4 未知带入者不拒绝 | 1 | 关闭时有带入即拒绝 |

### 逐步相同的证明

- 「关闭时逐步相同」和「开启但无人带入时逐步相同」按 `MatchSession.Create` 的三步复刻建局：先 `ResolvedFor`，再建 `MatchOptions.Immediate with {…}`，然后 `PlantPrototype`。开关在测试里写死，前者为 false，后者为 true 且带入为空。随后用 `MatchSession.ForMatch(...).Run()` 跑种子 31、Standard、24 个小回合，得到的 `TurnHash` 等于既有黄金值 `候选格上限Tests.V4GoldenTurnHash`。另外在插旗前钉住四人开局手牌 `Basic×5+0` 和 `recruit=0`。
- 反面对照：同一局让全员带换型令（堡垒子），开局手牌和哈希都不同。原先对照用的是"全员带备用子"，结果哈希相同：快照只记手牌类型、不记枚数，多一枚普通子在这 24 个小回合里没有改变落点。所以对照样本改成换型令，并单独钉住开局手牌。
- 既有黄金哈希、期望值和走法测试一条都没改，全部保持绿。带入缺省关闭，Sim 和 Godot 的建局路径都没有传入带入。

### 既有测试改写

- `主动弃赛Tests.弃赛快照可记录` 只追加了一条 `RankAtResign == 1` 断言，原因是 elimination-endgame 修改了该 Scenario，要求快照记录名次。其余既有测试没有改写。存档新增的关闭字段总是写出；既有的"删字段再恢复"类测试比对的是原文，因此不受影响。

### 待决 / 留给后续段

1. 流程事件 `PlayerResigned` 的文本（进入日志事件）暂未加名次。match-telemetry 要求日志中记录"弃赛（含弃赛时势力名次）"，这项留给段 C 一并设计，以免现在改动事件文本影响日志类测试。
2. `CarryOutSettlement.Settle` 遇到名次为空的旧弃赛快照会抛异常。按 D8，旧存档的开关必为关闭，没有读取方；只要入口保证"只在开关开启时结算"，这条异常就不会触发。
3. 人数不在 2–4 的对局：现有地图最多 4 人，建局时没有另加拒绝；`CarryPoints.For` 在结算时会响亮失败。
4. 「对局中不可改」按结构守门实现：`Options` 没有公开写入口，`CarryIns` 是不可变字典并且只有 `init`，通过 `with` 得到的副本不会影响对局。对局没有任何修改带入的入口。
