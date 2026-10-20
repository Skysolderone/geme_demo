# 10-20-cycle-stop-2

> 规格权威：`openspec/changes/cycle-stop/`（proposal 裁决 C1–C6；design D2 安全网 A、D3 停手 B、S1–S5、**第二段裁决 D1–D6**；tasks 第二段 2.1–2.5；specs `battlefield` / `elimination-endgame` / `match-setup` / `match-telemetry` / `ai-decision`）。第一段诊断（任务 `10-20-cycle-stop-1`，`diagnosis.md` / `notes.md`）已完成、未提交，其 `CycleAnalyzer` 与 `--review-cycles` 已在工作区。本任务做 **2.1–2.5**；2.6（归档）与提交由负责人线做。

双保险：A 规则层"大回合上限"安全网（对局配置，缺省 100，0 = 不启用，到达按现有战场结算判定链按势力结算、原因"上限"，终局原因仍"最后战场结算"；旧存档缺字段按 100 回填留痕）；B AI "停滞大回合数" K = 6 的循环提子停手（连续 K 个大回合本人势力无净增且排序前 N 候选全部会提子或落在最近 K 大回合被提过的格 → 视同无改善手 Pass；决胜一手优先；无子豁免；AI 评价版本 +1）。同形禁则不改（D1）。

## Acceptance Criteria
- [ ] tasks 2.1–2.5 勾选，证据记本目录 `notes.md`
- [ ] A：`MatchOptions.RoundCap`（缺省 100、负数报错）、`SettlementReason.RoundCap`（"上限"）、`JudgeAtRoundEnd` 第 (c) 条排在整轮 Pass / 填满之后且先手预测复用；存档 / 日志首部 / 批次配置写入；旧存档缺字段按 100 回填留痕；`battlefield` 三条新 Scenario、`match-setup` 四条、`elimination-endgame`「上限结算即终局」、`match-telemetry`「上限结算可查」都有同名测试
- [ ] B：`AiSearchConfig.StagnationRounds`（缺省 6、四档共用）、停滞判定只读公开量（信息边界守门通过）、`ai-decision`「循环提子停手」六条 Scenario 有同名测试；K = 0 对照逐步相同；AI 评价版本 +1
- [ ] 5 份 `decisions-link1-*` 基线：A 不触发（最长 29 < 100）；B 若改走法则重钉并在 notes 记录每份改变的首个分歧大回合与原因
- [ ] 2.3：图形版上限提示条（剩 ≤ 5 大回合，每大回合开始一次，无人值守零时长）、终端大回合开始行打印上限，新局界面不加配置项；Godot 四条 headless 自检退出 0
- [ ] 2.4 验证 ≤ 10 局：简单 ×3 种子 40–47（`run --difficulty Easy --turn-limit 600`，`--review-cycles`）报上限结算次数、循环 / 拖延局数、大回合中位，对照第一段（中位 65.5、循环 1 / 拖延 4、最长 94）；标准 `link:1` 种子 1–2 大回合数不变。报告存本目录
- [ ] 2.5：设计文档 v1.29（§12.3 第 (c) 条与安全网措辞、§12.4 改口、§15.2 停滞停手、§17、§19 R-3 已处理 + D1 观察）、HANDOFF（配置项、`--review-cycles`、观察项）、`boundaries.md` 对局配置缺省与回填一行
- [ ] `dotnet build` 0 警告、全量 0 失败、Skip 仍 6

约束：子 agent 不得 git commit / archive；跑局合计 ≤ 10、不跑批量；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改停手阈值缺省、评价权重、候选剪枝、同形禁则；不改 `openspec/` 规格正文；不动飘字动效逻辑；不给 `play` 加日志（D5）；与 design 冲突先记 notes.md 再按 S1–S5 / D1–D6 继续。
