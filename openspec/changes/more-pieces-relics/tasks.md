## 1. 段 A：四种棋子的规则与计分（Siege.Core）

- [ ] 1.1 先写测试：`piece-effects` 四条 ADDED（旗手 / 铁链 / 哨兵 / 界碑）与四条 MODIFIED 的新 Scenario、`power-score` 两条 MODIFIED 的新 Scenario（七项来源、新来源被倍率放大）。验证：新增用例全部先红，既有用例不动。
- [ ] 1.2 `PieceType` 末尾追加四值（D9）；`PieceEffects.BasePower` 四种各 1；`GameBoard` 类型码 `N C T K`。验证：盘面序列化往返测试含十种类型；一份只含 `B F L M S A` 的旧盘面串照常恢复；类型码两两不同的守门测试。
- [ ] 1.3 实现 D1 / D2：`BannerBonus / ChainBonus / SentryBonus / BoundaryBonus`；`SynergyBonus` 类型数覆盖十种；`GroupPower` 与势力明细扩为七项来源；`PowerCalculator` 汇总。验证：1.1 全绿；变异逐项应红——旗手改用几何四邻（栅栏 / 崖壁 Scenario）、旗手读揭示状态（不看揭示 Scenario）、铁链用棋子数而非 −1（单子 Scenario）、哨兵计入己方棋子、哨兵不计弃赛者、界碑滤掉荒漠（荒漠 Scenario）、界碑计入争议格、任一新来源不乘倍率。
- [ ] 1.4 征募棋池十档（新四种各 8，代码显式标注未校准，不可配置）与 D8 对局内容集骨架：`MatchOptions.ContentSet`（缺省 v2）、存档 / 日志首部 / 批次配置字段、缺字段按 v1 并留痕；v1 棋池只含原六种。先写 `recruitment`「初始配置与基础棋池」「流派徽记调整征募权重」的新 Scenario 与 `match-setup`「对局内容集」四个 Scenario（先红）。验证：转绿；变异——v1 仍抽到新棋子应红；缺字段按 v2 恢复应红。
- [ ] 1.5 守门：依赖走法的黄金哈希 / 期望写死 v1，数值不变（`match-setup`「v1 逐步相同」）；若分叉，探针归因，不挑种子凑绿。全部 `dotnet test -c Release` 绿。

## 2. 段 B：四类信物与生成权重（含工坊）

- [ ] 2.1 先写测试：`relic-effects` 三条 ADDED（驿站 / 工坊 / 计分信物）与两条 MODIFIED 的新 Scenario；`relic-generation` 两条 MODIFIED 的新 Scenario；`terrain-edit`「匠人落子即改造」六个新 Scenario；`recruitment`「私人征募面板」驿站 Scenario。验证：先红。
- [ ] 2.2 `RelicType` 末尾追加四值；`RelicContent` 拒绝新四类强度 2；`RelicWeights` 千分制 v2 两表 + 原百分制 v1 两表，按内容集取表，`RarityScale` 按 D6；`RelicGenerator.Draw` 新四类跳过升级抽签（D7），徽记绑定集合按内容集显式列出。验证：2.1 生成类 Scenario 绿；10000 个种子**只跑生成**的分布测试（新四类在出生区 / 公共区各约 50‰ / 70‰，原六类按 80% / 72% 等比）；v1 下逐格与改动前一致；变异——新四类照常消费升级抽签应红（v2 同种子期望）、徽记绑定仍用 `Enum.GetValues` 于 v1 应红。
- [ ] 2.3 出生区预算收敛：v2 下 10000 个种子统计"生成未收敛"比例并与 v1 对照，只报告，写进 implement 记录；明显恶化时提负责人，不私调容差。
- [ ] 2.4 `EffectSnapshot`：驿站加成并入展示数并在来源拆分中逐枚列出，工坊生效标记；值相等 / 哈希 / 弃赛快照存档字段（缺省 0 / 否）。验证：驿站六个 Scenario、工坊三个 Scenario 绿；变异——驿站不计其他驿站、驿站计入争议信物、高阶信物按 2 枚计、工坊当小回合生效，逐项应红。
- [ ] 2.5 计分信物（D3）：`PowerCalculator.Compute` 增加"已知信物内容"输入（旧签名保留为无计分信物的重载）；连营并入连珠、犄角并入协同；弃赛 / 出局者不享受；结算传真实内容。验证：计分信物七个 Scenario 绿；变异——连营走快照（"失去控制立即失效"应红）、争议仍生效、弃赛者享受、连营不乘倍率，逐项应红；已结算盘面上"真实内容"与"公开已揭示内容"两种输入结果相同的性质测试（v2、若干种子的**生成 + 随机合法盘面**，不跑整局）。
- [ ] 2.6 工坊（D5）：`TerrainEditRules.LegalTargets / Reject` 增加工坊参数（旧签名 = 否），批次预演与结算按玩家本小回合快照传入。验证：`terrain-edit` 新 Scenario 绿；既有「拒绝理由与合法目标集合一致」守门扩到工坊为真时；变异——含斜角、扩到距离 3、扩了边目标、无工坊也能隔一格，逐项应红。
- [ ] 2.7 v2 下依赖走法的新期望：按行为变更重建并逐项归因（信物分布 / 征募序列 / 计分来源），不挑种子。全部测试绿。

## 3. 段 C：AI 适配、终端、遥测

- [ ] 3.1 先写测试：`ai-decision`「对隐藏信息的概率估计」改写的 Scenario 与「新棋子与新信物的 AI 适配」六个 Scenario。验证：先红。
- [ ] 3.2 `RelicEstimate`：`ValueOf` 新四类（6 / 4 / 3 / 3，标注未校准，不再对新类型抛）；期望按所在内容集的表与表合计归一。验证：v2 期望 5 / 5、v1 期望 5 / 6；变异——分母写死 100 应红。
- [ ] 3.3 AI 改造枚举与预筛回退传入快照工坊标记；预筛代表类型按固定次序；`BatchEvaluator` 以批次开始前已揭示的公开内容计算计分信物，"组合成长"维口径不变。验证：3.1 绿；变异——AI 读真实内容（"未揭示的连营不影响 AI 评价"应红）、Growth 计入新来源（既有 Growth 期望应红）；K = 0 与 K > 0 在标准图上的既有等价测试保持绿。
- [ ] 3.4 `EvaluationWeights.CalibrationStatus` / `CalibrationOf` 按 D10 补注口径，数值不变；守门测试同步。验证：守门绿；去掉补注应红。
- [ ] 3.5 `batch-preview`「计分信物与新棋子的预演」三个 Scenario：先红后绿；变异——预演用真实内容（"将揭示的连营不计入预演"应红）。
- [ ] 3.6 终端（`BoardRenderer` / `ConsoleController`）：四种新棋子字母与名称、中文快捷输入、图例、新信物名称与字母、驿站来源与工坊目标（含隔一格）的文字列出。验证：渲染快照测试；字母两两不同的守门。
- [ ] 3.7 遥测：日志首部内容集、七项来源、驿站来源、工坊标记、改造"经工坊扩展"；分析器第 12 项与"每种棋子 / 每类信物"列表按内容集展开，v1 单列不适用；旧日志缺字段按 v1 / 0 / 否。验证：`match-telemetry` 新 Scenario 绿（构造日志）；一份既有旧日志离线解析与回放保持绿。

## 4. 段 D：Godot 表现、设计文档、冒烟与回归

- [ ] 4.1 `Siege.Presentation`：`PieceSilhouette` / `SilhouetteLanguage` 追加四值，`PieceStyleTable` 覆盖十种；`Labels` 名称。验证：`visual-style-baseline`「映射覆盖全部类型」守门（对 `PieceType` 全部枚举值各有一条、两两不同）。
- [ ] 4.2 Godot：四种新棋子几何（D11）；手牌面板新类型与驿站来源（`hand-info-panel` 新 Scenario）；势力层七项拆分；工坊生效时的隔一格目标高亮（目标集合直接来自 `LegalTargets`）。验证：`dotnet build` 零警告；人工检查清单与十种棋子灰度缩略图、工坊高亮截图落 `art/more-pieces/`，交负责人过目（易混对：铁链 / 连珠、界碑 / 堡垒、旗手 / 匠人）。
- [ ] 4.3 设计文档 v1.13 → v1.14：§8（信物表、生成权重千分制表）、§9（棋池、十种棋子表与四种位置加值）、§20（十种轮廓语言），并同步受影响的 §3.4（工坊目标）、§10.1（位置加值公式与算例）、§15 的先验表；变更记录加一行。验证：文档中的数值与规格 Scenario 逐项一致（人工核对清单写进 implement 记录）。
- [ ] 4.4 冒烟：`siege-4p-base-v5`、种子 1–20、4 名 Standard、内容集 v2。报告截断、终局原因、结束大回合、各类型选择率、四种新来源占比、驿站平均加成、工坊扩展改造次数、军势峰值、小回合耗时、生成未收敛局数；数据 `sim-out/more-pieces-relics/smoke20/`。只报告，不调任何权重（负责人：严禁 200 局）。
- [ ] 4.5 全量回归：两处 `dotnet build` 零警告；`dotnet test -c Release` 全绿（不跑 200 局规模慢测试）；`openspec validate more-pieces-relics --strict` 通过。
