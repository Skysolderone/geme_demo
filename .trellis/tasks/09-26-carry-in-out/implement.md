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

## 段 B——档案读写、AI 带入、终端（tasks 2.1–2.5）

### 改动文件

| 文件 | 内容 |
|---|---|
| `src/Siege.Core/Carry/CarryProfile.cs` | 新增。`CarryInFlight(MatchId, CarryIn?)`、`CarryProfile`（版本 / 补给点 / 库存 / 在途记录；`StockOf`、`Carriable`）。`ToJson` 固定键序、不缩进，与规格字面一致；`Parse` 容忍空白与 CRLF，先读版本（高于 1 抛 `CarryProfileVersionException`，不再往下校验），其余非法一律抛 `FormatException`：非 JSON、缺 version / points / inventory、负数、非整数、重复键、未知字段、未知补给名或棋子类型、在途记录不合形（备用子带类型、征召签 / 换型令缺类型、空对局标识）。缺的库存项按 0；缺 `inFlight` 按 null |
| `src/Siege.Core/Carry/CarryProfileStore.cs` | 新增。Core 里唯一的文件 I/O 类，构造不做 I/O。`DefaultPath()` = `ApplicationData/Siege/profile.json`；`Load` 返回新建 / 已读 / 损坏已备份 / 版本更新只读四类，附给用户的提示；原子写：先写 `.tmp`，再 `File.Move(overwrite)`，写成功后才更新内存；损坏时先改名为 `.corrupt-<yyyyMMdd-HHmmss>`（名字已被占用就加序号，不覆盖）再写空档案；只读时任何写入都抛异常。`TryExchange`、`Begin`（残留在途记录未处理、库存为 0 都拒绝）、`SettleAbandoned`（补给丢失、带出 0）、`Settle(matchId, result)`：标识不匹配或在途已清除就忽略，`Unsettled` 拒绝写入，结算里的带入与在途记录不一致就响亮失败。时钟必须由入口注入（`Func<DateTimeOffset>`）；公开属性叫 `Current` 不叫 `Profile`，原因见待决 2 |
| `src/Siege.Core/Carry/CarryAi.cs` | 新增。`Draw(seed, aiPlayers, count, contentSet)`：count 只能是 0 或 1；为 0 时不派生子流；按编号升序，用 `carry-ai` 子流先等概率抽种类，抽到换型令再在候选固定次序里等概率抽类型；征召签的类型留给建局时的 `carry-draft:<n>` 解析。`internal Draw(RandomStream, …)` 是测试接缝。`ForHumanMatch(seed, players, human, humanCarry, set)`：把"AI 数量 = 本机玩家的带入数"这条规则收在一处 |
| `src/Siege.Sim/Play/CarryTerminal.cs` | 新增。插旗前的补给界面 `Prepare`：读档并提示；版本更新，或人数不在 2–4，本局都关闭；有残留在途记录先按中途退出结算并提示；然后显示补给表和档案，支持 `buy n` 兑换、`n` 带入（换型令再选类型）、回车不带入、`q` 退出。另有带入列表、弃赛 / 出局 / 局终结算行和档案概览的文本 |
| `src/Siege.Sim/Play/PlayCommand.cs` | `Run` 新增 `CarryProfileStore? profile = null`，缺省为关闭。开启时：建局前调 `Prepare`，再建局（`CarryInOut = true`，`CarryIns` 由 `ForHumanMatch` 给出），然后 `Begin(对局标识, 本机带入)`，开局说明之后列出全部带入。`resign` 抛出的异常在小回合外层接住，调 `MatchFlow.Resign`，立即结算并写档；AI 继续到终局。每个小回合后检查本机是否出局，出局就立即结算。局终只补完赛者的结算（只在开关开启时结算，落实段 A 待决「入口只在开关开启时结算」；人数不在 2–4 时本局关闭带入，也不会走到 CarryPoints 的响亮失败）。对局标识为"种子十六进制 + Stopwatch 读数"。新增 `Confirm` |
| `src/Siege.Sim/Play/ConsoleController.cs` | 新增 `PlayResignException`；`Read()` 支持 `resign` / `弃赛` 并二次确认；有带入时 `q` 先提示再确认（输入耗尽仍直接退出）；部署帮助加一项"resign 弃赛"。构造新增可选参数 `quitWarning` |
| `src/Siege.Sim/Program.cs` | `PlayProfile(cli)`：缺省用缺省档案；`--profile <路径>` 改用其他文件；`--no-carry` 返回 null，此时不读写档案。严格解析：`--no-carry` 后面跟了值、`--profile` 缺路径、两者同时给出，都报错。在 `EnsureRecognized` 之前读取，只构造对象、不做 I/O。在这里注入墙钟 `TimeProvider.System.GetLocalNow`，只用于损坏备份的文件名。用法说明补一行 |
| `tests/.../CarryInOut/` | 新增 `CarryProfileFixtures.cs`：`TempProfile` 在系统临时目录下为每个实例建一个独立子目录，释放时删除；`RealProfileDir.State()` 只读地取真实 `%APPDATA%\Siege` 的状态快照。新增 8 个测试类，见下表 |

### 测试（新增 61 条，既有测试一条未改）

| 测试类 | 条数 | 覆盖 |
|---|---:|---|
| 本地玩家档案Tests | 4 | 档案内容（经兑换这一真实写入路径得到规格字面值，含 CRLF 与缩进读回）/ 在途记录的写出与读回 / 路径可配置（`--profile` 解析后跑一局脚本，只写入指定路径，真实目录不变）/ 原子写入（替换前抛出的桩） |
| 档案缺失损坏与版本不符的恢复Tests | 21 | 缺失时新建 / 缺失时连同目录一起新建（真实首跑时 Siege 目录不存在）/ 损坏时备份并重置（固定时钟 → `profile.json.corrupt-20260926-114000`）/ 负数视为损坏 / 非法内容视为损坏 ×15 / 同一秒内再次损坏不覆盖先前的备份 / 新版本档案不覆盖（四种写入都抛异常，文件逐字节不变，也不产生备份） |
| 补给兑换Tests | 6 | 兑换成功 / 每次兑换一件并按价扣点 ×3 / 补给点不足（提示原文）/ 补给点恰等于价格可以兑换 |
| 开局带入与在途记录Tests | 4 | 带入扣库存 / 不带入也有在途记录 / 库存为 0 不能带 / 残留在途记录未处理时不得开局 |
| 中途退出与截断Tests | 3 | 退出后下次开局 / 截断局不结算（纯函数给出全员未结算，存取层拒绝写入）/ 重复结算被忽略（含标识不匹配） |
| 带出结算写入档案Tests | 4 | 完赛不返还 / 弃赛返还（名次 2、第 6 大回合 → 8）/ 出局丢失 / 带入与在途记录不一致即拒绝 |
| AI带入Tests | 7 | 同等数量 / 本机不带则 AI 不带（`Consumed == 0`）/ AI 带入可复现 / 独立子流 carry-ai、按编号升序、等概率（测试内独立算式：子流名、种类次序、v1 / v2 候选次序都在测试里字面写出；样本下界是三种补给都出现，换型令至少出现 3 种类型）/ 数量只能为 0 或 1 / 与本机玩家的选择无关（12 颗种子，含 AI 征召签的解析结果、信物分布、首回合顺序）/ 不扰动征募序列（种子 1–400 中第一颗 AI 全抽到备用子的） |
| 终端的带入选择弃赛与结算显示Tests | 12 | 开局选择 / 库存为 0 的补给不在带入选项里 / 弃赛命令 / 弃赛需二次确认 / 新版本档案时本局关闭带入 / 关闭带入 / 档案选项严格解析 ×2 / 关闭与指定路径不能同时给出 / 退出提示与下次开局结算中途退出 / 未带入时 q 直接退出 / 脚本化终端对局不传档案时不碰真实档案目录（用反射确认 `profile` 缺省为 null，并比较真实目录前后状态） |

- 先红：先写骨架，全部 `throw new NotImplementedException()`，保证测试能编译。首批 58 条全部是运行时红，没有"编译失败的假红"；段 A 原有的 28 条保持绿。有 3 条是实现之后补的：「弃赛需二次确认」「新版本档案时本局关闭带入」的约束力由 M-D3 / M-D5 / M-B4 证明；「缺失时连同目录一起新建」钉的是既有的 `Directory.CreateDirectory`，没有单独做变异。
- 「弃赛命令」脚本：本机从不落子，5 个大回合都"回车 + pass"，第 6 大回合弃赛。名次和点数在测试里独立核对：点数 = 表值的一半，表值取自规格表。实测名次第 4、带出 5，弃赛后 3 名 AI 下到终局，整条约 3 秒。
- 全量：`dotnet test -c Release` 1675 通过、5 跳过（段 A 收尾时为 1614 / 5，新增 61）。

### 变异（`scratchpad/mutate_b.py`，全量测试工程，逐条红数）

做法与段 A 相同：二进制读写；锚点按文件的实际行尾归一，并 `assert count == 1`；还原放在 `finally`，还原后逐字节比对，再 `os.utime`；设 `DOTNET_CLI_UI_LANGUAGE=en`，按统计行计数。23 条全部红；还原后重新构建，全量为 1674 绿（变异跑在补「缺失时连同目录一起新建」之前）。

| 变异 | 红 | 红的测试 |
|---|---:|---|
| M-B1 损坏时直接覆盖（不改名备份） | 18 | 损坏时备份并重置 / 负数视为损坏 / 非法内容 ×15 / 同一秒内再次损坏 |
| M-B2 重复结算再加一次点数（不认在途记录） | 1 | 重复结算被忽略 |
| M-B3 残留在途返还补给 | 2 | 退出后下次开局 / 终端退出提示与下次开局 |
| M-B4 新版本档案被写回 | 2 | 新版本档案不覆盖 / 新版本档案时本局关闭带入 |
| M-B5 非原子写（先直接写目标文件） | 1 | 原子写入 |
| M-B6 弃赛结算不返还补给 | 2 | 弃赛写入档案并返还 / 弃赛命令 |
| M-B7 开局不扣库存 | 8 | 带入扣库存 / 开局选择 / 弃赛命令 / 退出后下次开局 等 |
| M-B8 兑换判据 `<` 改 `<=` | 1 | 补给点恰等于价格可以兑换 |
| M-B9 负数不视为损坏 | 2 | 负数视为损坏 / 非法内容（库存为负） |
| M-B10 备份名冲突时不另取名 | 1 | 同一秒内再次损坏不覆盖先前的备份 |
| M-C1 AI 抽取改用 `setup` 子流 | 1 | AI 带入用独立子流 carry-ai（见待决 3） |
| M-C2 AI 数量不随本机玩家（恒为 1） | 2 | 本机不带则 AI 不带 / 库存为 0 的补给不在带入选项里 |
| M-C3 AI 不按编号升序抽取 | 1 | AI 带入用独立子流 carry-ai |
| M-C4 换型令类型按权重而非等概率 | 1 | 同上 |
| M-C5 数量为 0 仍消费子流 | 1 | 本机不带则 AI 不带 |
| M-D1 终端弃赛不写档 | 1 | 弃赛命令 |
| M-D2 有带入时 q 不提示 | 1 | 退出提示与下次开局 |
| M-D3 弃赛不二次确认 | 1 | 弃赛需二次确认 |
| M-D4 `--no-carry` 吞值不报错 | 1 | 档案选项严格解析（--no-carry 5） |
| M-D5 档案版本更新时仍开启带入 | 1 | 新版本档案时本局关闭带入 |
| M-D6 开局前不处理残留在途记录 | 1 | 退出提示与下次开局 |
| M-T1 只改测试：对调备用子与换型令的价格期望 | 2 | 每次兑换一件并按价扣点 ×2 |
| M-T2 只改测试：对调点数表第 3、4 名期望 | 1 | 弃赛命令 |

### %APPDATA% 守门

- 开工前：`%APPDATA%\Siege` 不存在。
- 段末复核：多次全量测试、23 轮变异、一次手工终端试跑（`--profile` 指向 scratchpad）之后，这个目录仍不存在。
- 套件内的守门：「脚本化终端对局不传档案时不碰真实档案目录」「路径可配置」「关闭带入」这三条都比较真实目录前后的状态。

### 既有测试改写

无。不传档案时，终端输出只多了部署帮助里的"resign 弃赛"一项。既有终端脚本测试只做包含性断言，全部保持绿。

### 待决

1. **Sim 里的墙钟**：规格要求损坏备份名带 `yyyyMMdd-HHmmss`。Core 源码扫描禁止读时钟（`各入口支持生成图Tests`），所以时钟改由入口注入。Sim 源码扫描禁止出现 `DateTime` 这个词，所以入口用的是 `TimeProvider.System.GetLocalNow`。这是 Sim 里唯一一处墙钟，只进备份文件名，不进对局、日志和配置。请裁决：可以接受，还是要在 Sim 扫描里显式登记这一处。Godot 入口（段 C）要自己注入时钟。
2. **`Profile` 标识符**：`地图规格档Tests` 禁止 `src` 下出现裸标识符 `Profile`（包括属性读取），所以存取的公开属性命名为 `Current`。段 C 的 Godot 代码同样不能写 `.Profile`。
3. **M-C1 的红点与任务原文不同**：`GameSeed.Stream(name)` 每次调用都返回一条全新的独立流，把名字改成 `setup` 并不会推进对局自己持有的 `setup` 流，所以信物分布和首回合顺序在结构上不可能变红。实际变红的是用测试内独立算式钉住 `"carry-ai"` 子流名的那条测试。
4. **终端覆盖缺口**：本机"出局立即结算"和"完赛局终结算"这两条终端路径没有脚本测试（脚本很难稳定地让本机出局或下完整局）。Core 这一层（`Eliminated` / `Settle` 纯函数、`store.Settle`）有测试；弃赛脚本已经覆盖局终打印"结算："。
5. **「截断局不结算」的日志部分**（日志里记"未结算"）按 tasks 属于段 C 的 3.1。本段只守纯函数和存取层。
6. **`--no-carry` 下也能用 resign**：弃赛是规则本身（§12.2），D10 把入口当作前提。关闭带入时只是不结算、不写档。
7. **补给显示名放在 Sim**（`CarryTerminal.Name`）。段 C 的 Godot 面板需要同样的文案，届时可以考虑挪到 `Siege.Presentation`，统一只留一处。
8. **档案读入口径**：缺的库存项按 0；缺 `inFlight` 按 null；未知字段按损坏处理（会备份，不丢数据），而不是忽略——忽略的话，写回时会把它悄悄丢掉。
9. **「弃赛命令」脚本依赖 AI 走法**：名次不写死，从输出中读出后按规格表核对；但脚本依赖缺省权重下的 Easy AI 能把对局撑到第 6 大回合，并在弃赛后自然终局。既有 `终端对局Tests` 同样没有写死权重，而 `各入口按地图标识选图Tests` 写死了（`weights:` / `passThreshold:`），两种做法都有先例。请裁决是否按 testing.md「依赖 AI 实际走法的断言要把权重写死」补上权重与阈值。
