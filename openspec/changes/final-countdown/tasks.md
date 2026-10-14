## 1. Core 规则

- [ ] 1.1 对局配置 K（缺省 2、≥ 0、负数报错）；存档与日志首部写入；旧存档缺字段按 0 留痕。
- [ ] 1.2 决赛倒计时：起点 R、各战场期限、`JudgeAtRoundEnd` 增加到期条件、结算原因 `Expired`；先手预测随之。
- [ ] 1.3 测试：`battlefield`「决赛倒计时」四个 Scenario 与「战场结算」新增 Scenario；K = 0 逐步不变对照；存档往返；变异各一。
- [ ] 1.4 链接图 5 条决策序列基线按 K = 2 重钉并逐条说明；K = 0 下与改前逐字节相同的断言。

## 2. Sim

- [ ] 2.1 `RunConfig` / 日志首部写 K；结算记录期限与"到期"原因；`analyze` 统计到期结算次数与到期时势力差分布，等待者评估沿用。
- [ ] 2.2 验证批次（只跑这一批）：`run --out sim-out/final-countdown/verify --map link:1 --map-per-match --count 20 --seed 1 --difficulty Standard`，`analyze` 后与 `sim-out/ai-terrain-review/themes` 逐项对比：等待者决赛胜率、等待大回合、决赛开局势力差、到期结算次数、对局长度。

## 3. 图形版

- [ ] 3.1 Presentation 提示条视图模型（倒计时 / 等待者两种文案，零时长规则）与测试；Godot 接线。
- [ ] 3.2 自检：三条 Godot 自检与 `--auto-demo --rounds=0 --seed=5` 退出码 0；截一张倒计时提示条。

## 4. 收尾

- [ ] 4.1 HANDOFF 与设计文档（§4.3、§12、§19 第 4 项改为已实施）同步，v1.24 → v1.25 变更记录。
- [ ] 4.2 回归：`dotnet build` 零警告、全量测试全绿。
