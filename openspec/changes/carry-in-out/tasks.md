## 1. 段 A：Core 规则、补给效果、结算、对局配置与存档兼容（Siege.Core）

- [x] 1.1 先写测试：`carry-in-out` 中「补给种类与开局效果」「换型候选类型」「名次补给点表」「完赛结算」「弃赛结算」「出局结算」的全部 Scenario（结算部分只测纯函数）、`match-setup`「带入带出配置」六个 Scenario、`recruitment`「带入改变开局手牌」、`elimination-endgame`「主动弃赛」新增的三个 Scenario。验证：新增用例全部先红，既有用例不动。
- [x] 1.2 `Siege.Core.Carry`：`SupplyKind`（`SpareStone / DraftLot / Commission`，末尾追加式枚举）、`CarryIn(Kind, PieceType?)`、价格表与名次点数表（代码显式标注未校准）、`CarryCandidates.Of(ContentSet)`（去掉普通子 / 倍增子 / 匠人，按基础权重）。验证：候选类型的 v1 / v2 Scenario 绿；守门测试——对任一名次 r 和任一补给，`⌈P(r)/2⌉ > 价格` 恒成立（改表或改价导致违反时应红）；变异——候选含倍增子、候选含匠人、v1 候选含旗手子，逐项应红。
- [x] 1.3 `MatchOptions.CarryInOut` / `CarryIns`（缺省关闭 / 空；关闭时有带入则拒绝；换型令类型不在候选内则拒绝）；`GameSeed.CarryAi` 与 `CarryDraft(int player)`；`MatchFlow.Create` 解析征召签并写回配置；`HandLedger` 按带入构造初始手牌，不消费 `recruit`。验证：1.1 中开局手牌类 Scenario 转绿；变异——征召签改用共享子流（"与本机玩家的选择无关"应红）、初始手牌构造消费了 `recruit`（"不扰动征募序列"应红）、备用子给出 5 + 2 枚，逐项应红。
- [x] 1.4 弃赛时势力名次：`MatchFlow.Resign` 计算并写入 `ResignationSnapshot.RankAtResign`，`FinalStandings` 不读取它。验证：`elimination-endgame` 新 Scenario 绿；变异——计入出局者、只在参赛者中排、并列取较低名次、`FinalStandings` 改用弃赛名次（"弃赛名次不影响最终名次"应红），逐项应红。
- [x] 1.5 结算纯函数 `CarryOutSettlement`：输入对局结果（或弃赛 / 出局事件）、带入与人数，输出结局类别、所用名次、点数与是否返还；完赛全额且不返还、弃赛 `⌊P/2⌋` 且返还、出局 0 且丢失、截断为未结算。验证：结算类 Scenario 全绿；变异——弃赛用最终名次、弃赛向上取整、出局仍返还、完赛也返还、并列取位置而非共享名次，逐项应红。
- [x] 1.6 存档：`MatchSaveData.CarryInOut` / `CarryIns`、`ResignationSaveData.RankAtResign`（全部可空）；缺字段按关闭回填，并用 `CarryInOutBackfilled` 留痕；`MatchPublicView` 暴露开关与各玩家带入。验证：「旧存档按无带入读取」「新存档往返」「弃赛名次随存档往返」「带入公开」绿；一份既有旧存档夹具照常恢复；变异——缺字段回填为开启应红。
- [x] 1.7 守门："关闭时逐步相同"与"开启但无人带入时逐步相同"。走法、征募与结算序列的黄金哈希和期望一律不改；若出现分叉，先用探针归因，不挑种子凑绿。存档原文或日志首部原文的快照测试（如有）只因新增的关闭字段而变化，按新增字段重建并逐项归因，不算规则回归。`dotnet test -c Release` 全绿。

## 2. 段 B：档案读写、AI 带入、终端

- [ ] 2.1 先写测试：`carry-in-out` 中「本地玩家档案」「档案缺失、损坏与版本不符的恢复」「补给兑换」「开局带入与在途记录」「AI 带入」「中途退出与截断」的全部 Scenario（档案测试一律用临时目录）。验证：先红。
- [ ] 2.2 `CarryProfile` 模型与 `CarryProfileStore`（路径注入；缺省路径解析为 `ApplicationData/Siege/profile.json`；先写 `.tmp` 再替换；损坏时改名为 `.corrupt-<yyyyMMdd-HHmmss>` 备份再建空档案；版本更新时只读不写）；兑换、开局扣除与在途、按对局标识结算且只结算一次、残留在途按中途退出结算。验证：2.1 档案类 Scenario 绿；变异——损坏时直接覆盖（"损坏时备份并重置"应红）、重复结算再加一次点数、残留在途返还补给、新版本档案被写回，逐项应红；"原子写入"用注入的写入失败桩验证。
- [ ] 2.3 AI 带入：`CarryAi.Draw(seed, aiPlayers, count, contentSet)`（按玩家编号升序，种类等概率，换型令的类型等概率）。验证："同等数量""本机不带则 AI 不带""AI 带入可复现""与本机玩家的选择无关""不扰动征募序列"绿；变异——AI 抽取改用 `setup` 子流（信物 / 首回合顺序应红）、数量不随本机玩家（"本机不带则 AI 不带"应红），逐项应红。
- [ ] 2.4 终端：`PlayCommand` / `ConsoleController` 在插旗前显示档案、提供兑换 / 带入（换型令选类型）/ 不带入；开局后列出全部带入；新增 `resign` / `弃赛` 命令并二次确认，立即结算并写档，AI 继续直到终局；`q` 在有带入时先给出提示；局终打印结算；`--profile` 与 `--no-carry` 走严格解析。`PlayCommand.Run` 的档案参数缺省为关闭。验证：终端的三个 Scenario 用脚本化输入 + 临时档案实现；既有终端脚本测试不变且保持绿；守门——跑完全部终端测试后，真实 `%APPDATA%\Siege` 下没有新文件。
- [ ] 2.5 全部 `dotnet test -c Release` 绿。

## 3. 段 C：Godot 面板、遥测、设计文档、冒烟与回归

- [ ] 3.1 遥测与跑局：`RunConfig.CarryIn`（0 / 1，缺省 0，严格解析，非法值报错），写入 `config.json`；日志首部记录开关与各玩家带入，结果记录带出结算（截断局为未结算）；`MatchLog` 旧日志缺字段按关闭；`Replayer` 按首部带入重建。先写 `match-telemetry` 三个新 Scenario 与 `simulation-harness` 五个新 Scenario（先红），再实现。验证：转绿；一份既有旧日志离线解析与回放保持绿；`--carry-in 0` 与改动前同种子逐局日志相同（除首部新增的关闭字段）；变异——回放不读首部带入（"带入对局可回放"应红）应红。
- [ ] 3.2 Godot：选图之后、插旗之前的补给选择面板（补给点、三种补给的效果 / 价格 / 库存，兑换、带入含换型令类型下拉、不带入，档案新建 / 损坏 / 版本提示）；对局中可查看全部带入；"弃赛"按钮加二次确认；本机玩家弃赛、出局或终局时弹出结算面板；`--profile=` / `--no-carry` 用户参数；`--auto-demo`、拾取自检和截图模式关闭带入。只用现有控件。验证：`dotnet build` 零警告；`--auto-demo` 运行后档案文件不存在或未改变；两个面板的截图落 `art/carry-in-out/`，交负责人过目。
- [ ] 3.3 设计文档 v1.14 → v1.15：改写 §12.2；新增 §21「带入带出」（补给表、候选类型、点数表、三种结局与算例、AI 带入、档案与在途记录、"不属于永久成长"的论证）；变更记录加一行。另外单列、待负责人确认的最小一致性修改：§18.2 删去"带入带出……弃赛收益结算"一项，§19 第 6 项补实现说明，§9.1 与 §13.1 各补一句，§17 日志项补带入与结算。验证：文档中的数值与规格 Scenario 逐项一致（人工核对清单写进 implement 记录）。
- [ ] 3.4 冒烟：`siege-4p-base-v5`、种子 1–20、4 名 Standard、内容集 v2、`--carry-in 1`。报告截断局数、终局原因、结束大回合、各补给的带入次数、按补给分组的平均名次与胜局数、带出结算分布（完赛 / 弃赛 / 出局 / 未结算），以及换型候选各类型出现次数；数据放 `sim-out/carry-in-out/smoke20/`。只报告，不调任何数值。**严禁 200 局。**
- [ ] 3.5 全量回归：两处 `dotnet build` 零警告；`dotnet test -c Release` 全绿（不跑 200 局规模的慢测试）；`openspec validate carry-in-out --strict` 通过。
