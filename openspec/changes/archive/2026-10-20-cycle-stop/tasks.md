## 第一段：诊断（裁决 C1，不改任何行为）

- [x] 1.1 Sim 新增「循环提子诊断」分析段（design D1 / S1 / S2）：`analyze --review-cycles` 显式开关；对每局取末段窗口 12 个大回合（截断局按截断时刻），输出每名在局玩家落子 / 提子 / 被提数与净势力变化、被提格集合、局部键（被提格 ∪ 四邻的占用者 + 设施）逐小回合哈希的最短重复周期与重复次数、被提格之外的新增落子数；≥ 2 名玩家净势力 ≤ 0 且有局部周期 → 判"循环提子"并计数。只读日志。算例：`match-telemetry`「循环提子诊断」Scenario「循环局被识别」——手写一份 3 人在 4 格上轮流提子、别处每大回合各加 1 子的日志 → 判循环、周期 = 3 名玩家各一小回合。验收：单元测试三条 Scenario；对 open-map 段 A 冒烟 `sim-out/open-map/smoke/link1/`（20 局正常终局）跑一遍，循环计数 0；`decisions-link1-*` 5 份基线逐字节不变
- [x] 1.2 诊断批次（≤ 10 局，单进程顺序跑，turn-limit 600）：`run --map link:1 --difficulty Easy --seed 40 --count 8 --out sim-out/cycle-stop/easy`；终端脚本种子 43 的 `弃赛命令` 局面经 `PlayCommand` 脚本路径复现一局（用测试里的写死权重 / 阈值 80）并写日志。`analyze --review-cycles` 两批；报告存 `.trellis/tasks/10-20-cycle-stop/diagnosis.md`：截断率、循环局数、每局周期 / 参与者 / 净势力变化 / 被提格数 / 别处新增落子数，以及"全局盘面不重复"的归因。验收：报告齐全，只报数不调参
- [x] 1.3 回传：按诊断给出 K 的建议值（连续无净增大回合数的分布）与"候选全是提子"判定是否需收窄；列出需负责人裁决的事项。验收：回传含建议与数据依据

## 第二段：实施（裁决 C2 A + B；K 等参数待第一段裁决）

- [x] 2.1 规则层安全网 A（design D2）：`MatchOptions.RoundCap`（缺省 100（D4）、负数报错）、`SettlementReason.RoundCap`（文案"上限"）、`JudgeAtRoundEnd` 第 (c) 条（排在整轮 Pass / 填满之后，先手预测复用）、存档 / 日志首部 / 批次配置写入，旧存档缺字段按 100 回填留痕，旧日志按 100 读。算例：`battlefield`「战场结算」Scenario「上限结算」——上限 60，第 60 大回合结束 A 300 / B 280 / C 90、B 有落子 → A 胜、原因"上限"；「上限与整轮 Pass 同时满足记整轮 Pass」。验收：单元测试全部新 Scenario（含 `match-setup` 四条、`elimination-endgame`「上限结算即终局」）；5 份基线逐字节不变；日志 / 报告哈希类测试因首部新字段重钉并记录
- [x] 2.2 AI 停手 B（design D3，K = 6（D2））：`AiSearchConfig.StagnationRounds`、`HeuristicTurnController` 部署入口的停滞判定（只读公开势力历史与公开落子 / 提子记录；无子豁免；决胜一手优先）、首部与批次配置写入、回放重建、AI 评价版本 +1。算例：`ai-decision`「循环提子停手」六条 Scenario。验收：单元测试六条；信息边界守门通过；K = 0 对照逐步相同；K 为裁决值时 5 份基线若改变则重钉并在 notes 记录原因
- [x] 2.3 图形版 / 终端：上限提示条（剩 ≤ 5 个大回合时每大回合开始提示一次，无人值守零时长，S3）；终端版在大回合开始行打印"上限 N（还剩 M）"；新局界面不加配置项（S4，缺省 100）；上限结算沿用终局横幅。验收：Presentation 视图模型测试一条；Godot 四条 headless 自检退出 0
- [x] 2.4 验证（≤ 10 局）：简单 ×3 种子 40–47 以第 2 段配置重跑（`run --difficulty Easy`，D5 不改 `play`），报上限结算次数、循环局数（`--review-cycles`）、大回合中位，对照第一段诊断（中位 65.5、循环 1 / 拖延 4）；标准 ×4 `link:1` 种子 1–5 大回合中位不变。验收：报告存任务目录（实跑种子 1–2，≤ 10 局约束）
- [x] 2.5 文档：设计文档 v1.29（§12.3 第 (c) 条与"安全网"措辞、§12.4 "MUST NOT 大回合上限"改口、§15.2 停滞停手、§17 首部与原因、§19 R-3 标为已处理并记录数据、登记 D1"改造让盘面常新"观察）；HANDOFF 加 `--review-cycles` 一行；HANDOFF（配置项、自检、观察项）；`.trellis/spec/core/boundaries.md` 对局配置缺省与回填一行。验收：人工检查
- [x] 2.6 `openspec validate cycle-stop --strict` 通过后归档；归档后 `openspec validate --specs --strict` 全部通过。验收：命令退出 0
