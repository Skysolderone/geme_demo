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

## 段 C——Godot 面板、遥测、设计文档、冒烟与回归（tasks 3.1–3.5）

起点：`%APPDATA%\Siege` 不存在；改代码之前用 HEAD 跑了基线 `run --count 3 --seed 1 --turn-limit 60 --serial`（scratchpad，用于 3.1 的"关闭时逐局相同"比对）。

### 改动文件

| 文件 | 内容 |
|---|---|
| `src/Siege.Sim/Config/RunConfig.cs` | 新增 `int? CarryIn`（与 `FlagRisk` / `ContentSet` 同一套三态：`null` = 未配置，`ResolvedFor` 落成 0 写进 `config.json` 与首部；首部缺该项 = 旧日志，回放不读它）。`Validated` 拒绝非 0 / 1，以及 `CarryIn = 1` 而人数没有点数表 |
| `src/Siege.Sim/Program.cs` | `run --carry-in <0|1>`（严格解析，非法值报错退出、不写输出）；用法说明补一行 |
| `src/Siege.Sim/Logging/MatchLog.cs` | 首部新增 `CarryInOut`（bool?）与 `CarryIns`（`CarryInEntry` 列表，玩家 / 补给名 / 类型名）；结果行新增 `CarryOut`（`CarryOutEntry`：玩家 / 结局 / 名次 / 点数 / 补给 / 类型 / 是否返还）。`MatchLog.CarryInOut` / `CarryIns`：缺字段按关闭、全员无带入；补给名或类型名解析不了、玩家重复即 `FormatException` |
| `src/Siege.Sim/Running/MatchSession.cs` | 新建的局按 `CarryIn` 用 `CarryAi.Draw` 给每名 AI 抽带入（0 时不派生子流）；`recorded` 回放只读首部带入（`RecordedCarry`，首部缺该项 = 旧日志 = 关闭，重建首部也不写这两项）。首部写开关与带入；结果行只在开启时写带出结算（`CarryOutSettlement.Settle`，截断局全员 `Unsettled`，失败局不结算）。`PlayerResigned` 事件的 `Values["RankAtResign"]` 取自弃赛快照（段 A 待决 1）。配置 `CarryIn = 1` 而对局未开启带入即 `SiegeRuleException` |
| `src/Siege.Sim/Running/Replayer.cs` | 把首部带入交给 `MatchSession.Create(..., recorded: true, carry)` |
| `src/Siege.Presentation/Text/CarryTexts.cs` | 新增。补给名、效果、带入、带入列表（玩家名由调用方给）、弃赛 / 出局 / 结算行、档案概览、带入物去向。终端与 Godot 共用（段 B 后裁决 3） |
| `src/Siege.Sim/Siege.Sim.csproj` | 引用 `Siege.Presentation`（同样零 Godot 依赖；`Sim程序集不引用Godot` 照常绿） |
| `src/Siege.Sim/Play/CarryTerminal.cs` | 文案全部改走 `CarryTexts`，终端输出逐字不变（棋子名 `Labels.Piece` 与原 `BoardRenderer.Name + "子"` 对十种候选逐一相同）；删去本地的 `Name` / `Effect` / `CarryText` / `ItemText` |
| `src/godot/scripts/MatchSession.cs` | `Create` 新增 `carryInOut` / `carry`（AI 经 `CarryAi.ForHumanMatch`，与终端同一实现）；`BeginCarry`（`store.Begin` 写在途）、`CanResign`、`Resign`（弃赛立即结算写档）、`SettleCarryIfDue`（出局即结算、终局结算完赛者，只结算一次） |
| `src/godot/scripts/GameRoot.Carry.cs` | 新增。`--profile=` / `--no-carry` / `--carry-preview=`（经 `LaunchArgs`、在 `EnsureRecognized` 之前读；`--no-carry` 与 `--profile=` 同给报错）；构造 `CarryProfileStore` 时注入墙钟 `TimeProvider.System.GetLocalNow`（图形版唯一一处）；自动化模式（`Unattended`：auto-demo / pick-check / 截图）与预览一律不构造存取。补给阶段（建局之前：预览会话显示盘面 → 面板确认 → 按所选带入重建对局 → `BeginCarry`）；版本不符时面板照常显示提示、只能"不带入，开始"（本局关闭）；残留在途按中途退出结算并提示。弃赛二次确认后结算、弹结算面板；每帧推进后检查出局 / 终局结算；关窗：有带入且未结算时 `AutoAcceptQuit = false` + 确认框。截图取景自证一行 `[carry] …` |
| `src/godot/scripts/Hud.Carry.cs` | 新增。独立根节点上的四块：补给选择面板（补给点与库存、三种补给的效果 / 价格 / 库存、兑换、带入并开始、换型令类型下拉、不带入开始、档案提示）、对局中的带入一栏（全部玩家的公开带入）+"弃赛"按钮、结算面板、二次确认框。只用现有控件与 `Ui` 样式 |
| `src/godot/scripts/GameRoot.cs` / `GameRoot.MapSelect.cs` | 钩子：读带入选项、两条建局路径（`--map=` 直达与选图"开始"）之后 `BeginSupply`、`_Process` / `RefreshViews` / `_Input` / `_UnhandledInput` 对补给阶段与选图阶段同样处理、推进后 `CheckCarrySettlement`、`_Notification` 转 `OnCarryNotification`、截图打印一行 |
| `2026-09-10-siege-core-gameplay-design-v1.md` | v1.14 → v1.15（见下文 3.3） |
| 测试 | 见下表 |

### 测试（新增 9 个方法、10 个用例；扩充 2 条既有测试；改写 4 条既有期望）

| 测试 | 覆盖 |
|---|---|
| `MatchTelemetry/对局日志的记录内容Tests.首部记录带入` | 规格 Scenario 原样（换型令堡垒子 / 征召签抽得哨兵子——取第一颗让玩家 2 抽得哨兵子的种子 / 两件备用子）：首部字面值、`MatchLog.CarryIns` 与对局配置逐项相同、文本往返；反面：未知补给名响亮失败 |
| 同上 `.带出结算可查` | 同 `主动弃赛Tests.弃赛时势力名次` 的真实局面：D 第 6 大回合弃赛名次 2 → `Resigned:2:8:Commission:Fortress:True`；另三人各取非缺省值（完赛 24 不返还、B 弃赛名次 2 带出 8、C 出局丢失换型令）；`PlayerResigned` 事件带 `RankAtResign = 2` |
| 同上 `.旧日志按无带入解析` | 合成旧日志 + 新带入日志删掉全部带入字段：开关读关闭、带入为空、无带出结算 |
| `SimulationHarness/批量跑局Tests.缺省关闭带入` | 不配带入数量：`config.json` 写 `CarryIn: 0`、首部 `false` / `[]`、无带出结算；小回合快照哈希等于既有黄金值 `V4GoldenTurnHash`（逐局相同） |
| 同上 `.开启带入` | `CarryIn = 1`、种子 1–20、每局 4 个小回合：每局 4 条带入、与 `carry-ai` / `carry-draft` 独立复算一致、三种补给都出现（样本下界）、截断局全员 `Unsettled`、种子 7 重跑带入与走法相同、真实档案目录前后不变 |
| 同上 `.非法带入数量`（2、−1） | CLI 报错退出、不写输出、错误含"带入数量"；配置文件同样拒绝 |
| `SimulationHarness/可复现回放Tests.带入对局可回放` | `CarryAi.ForHumanMatch`（P0 换型令连珠子、三名 AI 各 1 件）的真实会话写文件 → 回放逐行一致；开局手牌含 `Line×1` |
| 同上 `.旧日志按无带入回放` | 新日志删掉首部三项当作旧日志 → 回放逐行一致，重建首部不写这三项 |
| `SimulationHarness/随机子流隔离Tests.带入子流不扰动其他子流` | 种子 1–5：`--carry-in 0 / 1` 的信物分布与第 1 大回合顺序逐项相同；样本下界：1 那局 4 人各带 1 件、5 局开局手牌都不同 |
| `MatchSetup/带入带出配置Tests.缺省关闭`（追加断言） | 段 A 留的"日志首部在段 C"：首部写 `false` / `[]` |
| `批量跑局Tests.Sim源码不含不受控随机与时间且浮点只在分析层`（扩充） | 裁决 1：`TimeProvider` 除 `Program.cs` 外零处；`Program.cs` 恰好一处、位于 `PlayProfile` 之内、形如 `TimeProvider.System.GetLocalNow)`；图形版脚本恰好一处、在 `GameRoot.Carry.cs` |

既有测试改写（全部是新写出的关闭字段 `"CarryIn": 0`，逐项归因，不是规则回归）：`可复现回放Tests.失败局可复现`、`默认评价权重的校准Tests.引用未校准维度产出的数据`、`批量跑局Tests.批量执行并汇总`（两处，第二处原被第一处的红挡住） `config.json` / 首部配置的期望补 `CarryIn = 0`。`终端的带入选择弃赛与结算显示Tests` 的脚本入口写死 AI 权重与停手阈值（裁决 2：抄写段 B 落定时的缺省值字面量，不读 `Default`；「弃赛命令」仍在第 6 大回合弃赛、弃赛后自然终局）。

先红：先加只声明不实现的骨架（`RunConfig.CarryIn` / 首部与结果字段 / `MatchLog.CarryIns` 抛 `NotImplementedException` / `Create` 收下但忽略带入参数），当时的 9 个新用例加 `缺省关闭` 的追加断言共 10 条全部运行时红（非编译失败），实现后全绿。计数：段 B 末 1675 + 本段 10 个新用例 = 1685。`旧日志按无带入回放` 是实现后补的，约束力由 M-T7 证明。

### 3.1 另两项核对

- **`--carry-in 0` 与改动前逐局相同**：同一命令（`run --count 3 --seed 1 --turn-limit 60 --serial`）在 HEAD 与本段各跑一次，去掉首部新增的三项（`Config.CarryIn`、`CarryInOut`、`CarryIns`）与耗时后逐行比对：三局 123 / 120 / 146 行全部相同；`config.json` 只差 `CarryIn`。
- **既有旧日志回放**：`sim-out/more-pieces-relics/smoke20/match-0000000000000001.jsonl`（引入带入之前、首部无带入字段）经 CLI `replay --file` → "回放一致：124 行逐字节相同"。单测里的对应项是 `旧日志按无带入回放`。

### 3.2 Godot 结果

- `dotnet build src/godot/Siege.Godot.csproj`（Debug）零警告。
- 无头自检（均带自动化模式，带入关闭）：`--auto-demo` 退出码 0（种子 20260915，跑满 4 大回合停止，开局对准自检通过）；`--auto-demo --pick-check` 退出码 0（逐格居中两档 105/105）；`--auto-demo --map-select` 退出码 0（选图自检 10 步通过，建局后照常演示）。
- 严格参数：`--no-carry --profile=…`、`--carry-preview=bad`、`--no-carry=1` 均退出码 1 并报出原因。
- 有人值守路径的无头冒烟：`--map=siege-4p-base-v5 --profile=<scratchpad>/gprof/profile.json --quit-after 90` 进入补给阶段，打印"已新建档案"，只在 scratchpad 写出空档案 `{"version":1,"points":0,…,"inFlight":null}`。
- 截图（窗口模式，`--carry-preview` 只用内存示例档案 / 示例结算，不构造档案存取；截图模式本身带入关闭）：
  - `art/carry-in-out/carry-supply-panel.png`：`--carry-preview=supply --screenshot=…:40`，控制台自证"补给面板 开 (14, 14) 440×491；补给 备用子 价 3 库存 1、征召签 价 2 库存 0、换型令 价 4 库存 2；档案存取 无"。
  - `art/carry-in-out/carry-settlement-panel.png`：`--carry-preview=settlement --screenshot=…:40`，"结算面板 开 (540, 110) 520×162"，内容为"完赛 · 第 2 名 · 带出 16 · 补给已消耗"。
  - 像素核对（脚本，不看图）：两张 1600×900；面板矩形内深色面板底占比 0.916 / 0.927，棋盘中央框内 0.0（面板不盖棋盘中心）。交负责人过目。
- 每次 Godot 运行之后 `%APPDATA%\Siege` 均不存在。

### 3.3 设计文档 v1.14 → v1.15

点名范围：第 3 行版本号；§12.2 最后一条改写为"弃赛快照 + 弃赛时势力名次（口径、并列、随存档往返），只供 §21 弃赛结算读取、不参与最终名次"；新增 §21「带入带出」（21.1 补给与候选类型、21.2 点数表与不等式、21.3 三种结局与算例、21.4 AI 带入、21.5 档案与在途记录、21.6 为什么不是永久成长）；变更记录加一行（含冒烟数据）。

**单列、待负责人确认的最小一致性修改**：

| 节 | 修改 |
|---|---|
| §9.1 | "每名玩家开局获得 5 枚普通子"后补一句带入例外（至多多 1 枚普通子或换 1 枚候选类型；未带入者不变；构造不消费征募随机） |
| §13.1 | 加一条：带入带出开关与各玩家带入从插旗阶段起公开，开局之后的手牌数量仍隐藏 |
| §17 | 首条加"带入带出开关与各玩家带入"；弃赛条加"含弃赛时势力名次"与"开启时每名玩家的带出结算（截断局未结算）、旧日志按关闭读取" |
| §18.2 | 删去"带入带出、局外成长和弃赛收益结算"，改为"局外成长（…）；带入带出与弃赛收益结算已由 `carry-in-out` 实现，见 §21" |
| §19 第 6 项 | 补"首期已由 `carry-in-out`（v1.15）实现……局外成长仍不做" |

**数值核对清单**（文档 ↔ 规格 Scenario，逐项一致）：价格 3 / 2 / 4；备用子共 6 枚、征召签 / 换型令普通子 × 4 + 1 枚、占 2 个类型槽；v2 候选七种 20 / 18 / 10 / 8 / 8 / 8 / 8 = 80，堡垒子 25%、界碑子 10%；v1 三种 20 / 18 / 10 = 48；点数表 4 人 24 / 16 / 12 / 10、3 人 24 / 16 / 10、2 人 24 / 10，并列共享；不等式 `⌈P(r)/2⌉ > 价格`、`⌈10/2⌉ = 5 > 4`、算例 `10 − 4 = 6 > ⌊10/2⌋ = 5`；完赛第 1 名 +24 不返还、只剩一名参赛玩家 +24；弃赛算例 D = 30、A = 50、B = 30、C = 12 → 第 2 → `⌊16/2⌋ = 8`、换型令 +1；已出局 C、D = 5、A = 40、B = 20 → 第 3 → 6；第 5 大回合第 1 名弃赛 → 12、最终第 4；保护期第 1–3 大回合弃赛带出 0、补给返还；出局丢失、带出 0；截断局未结算；档案字面 `{"version":1,"points":17,"inventory":{"SpareStone":1,"DraftLot":0,"Commission":2},"inFlight":null}`、损坏备份名 `profile.json.corrupt-<yyyyMMdd-HHmmss>`；子流名 `carry-ai` / `carry-draft:<玩家编号>`；`--carry-in` 0 / 1 缺省 0；退出提示原文"退出将丢失带入的补给；弃赛可返还补给并带出 50%"。

### 3.4 冒烟（20 局，只报告）

命令：`Siege.Sim run --out sim-out/carry-in-out/smoke20 --map siege-4p-base-v5 --seed 1 --count 20 --players 4 --difficulty Standard --carry-in 1`（内容集缺省 v2）。`config.json` 核对：`CarryIn 1`、`ContentSet V2`、`PassThreshold 80`、`FlagRisk 15`、`TurnLimit 600`，四名玩家权重均为实际生效的缺省值（权重口径"more-pieces-relics 扩展计分后未重扫"）。统计脚本 `sim-out/carry-in-out/smoke20_report.py`，输出 `smoke20/report.txt`。

**AI 带入口径**：按规格 simulation-harness「批量跑局」与 carry-in-out「AI 带入」执行——批量跑局没有本机玩家，4 名 AI 都按 `--carry-in 1` 各带 1 件，全部由 `carry-ai` 子流按编号升序等概率抽种类、换型令再等概率抽类型，征召签经各自的 `carry-draft:<编号>` 按候选权重解析。口径明确，没有另选做法。

| 项 | 结果 |
|---|---|
| 局数 / 失败 / 截断 | 20 / 0 / 0 |
| 终局原因 | 整轮 Pass 20 |
| 结束大回合 | 平均 8.80，中位 8，最小 3，最大 16 |
| 带入次数 | 备用子 24，征召签 26，换型令 30（共 80） |
| 按补给分组的平均名次 / 胜局 | 备用子 2.58 / 6；征召签 2.65 / 4；换型令 2.30 / 10 |
| 带出结算分布 | 完赛 79（24 点 ×20、16 ×20、12 ×20、10 ×19），出局 1（0 点），弃赛 0，未结算 0 |
| 征召签抽得类型 | 堡垒 9、铁链 5、协同 4、连珠 3、界碑 2、哨兵 2、旗手 1 |
| 换型令指定类型 | 界碑 6、哨兵 6、铁链 5、旗手 4、连珠 4、协同 4、堡垒 1 |

样本仅 20 局，只报告、不下结论、未调任何数值。

### 变异（`scratchpad/mutate_c.py`，全量测试工程，逐条红数）

做法同段 A / B：二进制读写，锚点按文件实际行尾归一并 `assert count == 1`；还原放 `finally`，逐字节比对后 `os.utime`；`DOTNET_CLI_UI_LANGUAGE=en`，按统计行计数。15 条全部红；还原后全量 1685 绿。
（首轮跑 M-T1–M-T4 时 `批量执行并汇总` 在每条里都红：它的第 57 行首部配置期望被第 48 行的红挡住、从没执行过，同样是 `CarryIn = 0` 的关闭字段。修正后整段复审该测试，再把 15 条全部重跑，下表是重跑数。）

| 变异 | 红 | 红的测试 |
|---|---:|---|
| M-T1 回放只读首部开关、不读首部带入（tasks 3.1 点名） | 1 | 带入对局可回放 |
| M-T2 `PlayerResigned` 事件的名次写错（+1） | 1 | 带出结算可查 |
| M-T3 结果行只在截断局写带出结算 | 1 | 带出结算可查 |
| M-T4 首部带入写成空表 | 3 | 首部记录带入 / 带入对局可回放 / 开启带入 |
| M-T5 CLI 读了 `--carry-in` 但不生效 | 2 | 非法带入数量 ×2 |
| M-T6 `Validated` 只拒绝负数 | 1 | 非法带入数量(2) |
| M-T7 按缺字段的旧日志回放仍写首部带入 | 1 | 旧日志按无带入回放 |
| M-T8 `ResolvedFor` 不把带入数量落成 0 | 6 | 缺省关闭带入 / 旧日志按无带入解析 / 旧日志按无带入回放 / 失败局可复现 / 批量执行并汇总 / 引用未校准维度产出的数据 |
| M-T9 `MatchLog.CarryIns` 不读首部 | 3 | 首部记录带入 / 带入对局可回放 / 开启带入 |
| M-T10 未知补给名静默当成缺省值 | 1 | 首部记录带入 |
| M-T11 `--carry-in 1` 时 AI 抽取数量为 0 | 2 | 开启带入 / 带入子流不扰动其他子流（样本下界） |
| M-P1 共用文案"已返还"改字 | 1 | 弃赛命令（终端输出经 `CarryTexts`） |
| M-G1 `Running/MatchSession.cs` 加一处 `TimeProvider.System` | 1 | Sim源码不含不受控随机与时间… |
| M-G2 `Program.cs` 再加一处 | 1 | 同上（`Single`） |
| M-G3 Godot `GameRoot.cs` 加一处 | 1 | 同上（图形版恰好一处） |

"带入子流不扰动其他子流"本身在结构上恒成立（`GameSeed.Stream` 按名派生，同段 B 待决 3），它的约束力只在样本下界（M-T11）。

### 段末自验

- `dotnet build siege.sln`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test -c Release`：退出码 0，1685 通过、5 跳过（段 B 末 1675 + 本段新增 10 个用例；5 条跳过是既有的慢 / 计时测试，本段未跑 200 局规模的任何东西）。
- `openspec validate carry-in-out --strict`：valid。
- `%APPDATA%\Siege`：段首、每次 Godot 运行后、冒烟后、变异后、段末都不存在。

### 待决

1. **图形版有人值守路径未自动验证**：补给面板确认 → 带入开局 → 弃赛 → 结算面板、出局 / 终局弹结算、有带入时关窗确认，只由编译、源码守门与 Core 层测试间接覆盖；自动化模式按规格关闭带入，无法走到。需要负责人手动玩一局（建议 `--profile=<临时路径>`，别用真实档案）。
2. **`--carry-preview=supply|settlement` 是本段新增的展示用启动选项**（规格未列）：为了在"截图模式不读写档案"的前提下给两个面板取图，用内存示例档案 / 示例结算，不构造档案存取。结算面板截图叠在插旗阶段的盘面上。请确认保留或改为别的取图方式。
3. **设计文档五处一致性修改**（§9.1 / §13.1 / §17 / §18.2 / §19）按 tasks 单列，待负责人确认。
4. **既有行为（未改）**：`MatchSession.Finish` 不再复制最后一个小回合之后的流程事件，所以"弃赛导致终局"的那次 `PlayerResigned` 不进事件流（`带出结算可查` 里 B 的弃赛即如此）；结果行的带出结算不受影响。批量跑局 AI 不弃赛，不影响冒烟。是否补复制留给后续。
5. **图形版的其他墙钟**：`GameRoot.cs` 的 F12 截图文件名一直用 `System.DateTime.Now`（既有）；本段守门只钉 `TimeProvider` 这一处注入，没有扩到 `DateTime`。
6. 段 A / B 待决的去向：段 A 待决 1（`PlayerResigned` 名次）、段 B 待决 1（Sim 墙钟登记，并扩到 Godot）、7（文案挪 Presentation）、9（弃赛脚本写死权重）本段落实；段 B 待决 4（终端"出局立即结算 / 完赛局终结算"无脚本测试）本段未动，仍是缺口；段 A 待决 2 / 3、段 B 待决 2 / 3 / 5（日志部分已由 `开启带入` 覆盖）/ 6 / 8 状态不变。
7. `--no-carry` 的有人值守对局里，左下会出现"带入带出未开启 + 弃赛"一栏（弃赛是规则本身，段 B 待决 6 同口径）；自动化模式隐藏该栏，画面与引入之前相同。
