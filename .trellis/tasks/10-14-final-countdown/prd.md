# 10-14-final-countdown

> 规格权威：`openspec/changes/final-countdown/`（proposal 裁决表 W1–W6；design D1–D4；tasks 1–4 组；specs：battlefield「战场结算」「决赛倒计时」、match-setup、match-telemetry、settlement-show）。

首轮同步收尾：出现等待决赛的玩家后，其余未结算多人战场最多再打 K（缺省 2）个大回合，到期按现有比较链强制结算（原因"到期"）。K = 0 不启用、逐步与改前相同。

## Acceptance Criteria
- [ ] tasks 1.1–4.2 勾选
- [ ] K = 0 时链接图基线与改前逐字节相同；K = 2 重钉的基线逐条说明
- [ ] `dotnet build` 零警告、全量测试全绿、Godot 自检四条退出码 0
- [ ] 验证批次与主题批次对比写进 implement.md

约束：子 agent 不得 git commit / archive；批量只跑 2.2 那一批 20 局；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改 AI 评价与停手、决赛与名次规则。
