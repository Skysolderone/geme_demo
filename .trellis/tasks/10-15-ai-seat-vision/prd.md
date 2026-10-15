# 10-15-ai-seat-vision

> 规格权威：`openspec/changes/ai-seat-vision/`（proposal 裁决表 V1–V6；design D1–D4；tasks；specs：ai-decision「正式对战 AI 的信息边界」「前瞻中的下一名对手」）。

AI 缺省改为按座位视野（不再全知），专家前瞻名册限同战场（修 C12），全局信息读取点收口；全知只作显式配置。

## Acceptance Criteria
- [ ] tasks 1.1–3.2 勾选
- [ ] 显式全知配置下链接图 5 条基线与改前逐字节相同；缺省重钉逐条说明
- [ ] `dotnet build` 零警告、全量测试全绿、Godot 四条自检退出码 0
- [ ] 两批验证（共 40 局）写进 implement.md

约束：子 agent 不得 git commit / archive；批量只跑 2.2 两批共 40 局；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改权重与阈值数值、规则、图形版代码。
