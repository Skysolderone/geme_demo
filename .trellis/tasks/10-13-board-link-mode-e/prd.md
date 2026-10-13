# 10-13-board-link-mode-e

> 规格权威：`openspec/changes/board-link-mode/`（proposal 全部裁决表，含 2026-10-09 13 项、Q1–Q10、A1–A5、R1–R6、B1–B6、S1–S10、C1–C19、P1–P7、D1–D42、拼接 / J1–J13、E1–E5；design 全文；tasks.md）。

段 E：收尾。负责人 2026-10-13 裁决 E1–E5 全缺省，派发方式 1：agent 改设计文档与 HANDOFF（E1 / E2 / E4 登记），主会话做归档、最终回归与提交（E3 / E5）。

- E1 设计文档 `2026-09-10-siege-core-gameplay-design-v1.md`：版本 v1.19 → **v1.20**（文件头已是 v1.19）；改写 §3 地图（只剩链接档 `link:`，标准 / 边疆 / 棋盘三档与 `gen:` / `board:` 降级为测试夹具一段）、§4 开局（盲选、构筑期限在自己战场、配对与拼接、等待）、§12 出局与终局（战场落败、战场结算、两类终局、名次）、§13 信息公开（同战场 / 全局两层、他战场不可见）、§19 扩展顺序；变更记录写摘要并引用 proposal 裁决表，不全文复制。与新模式冲突的其它段落（§11 行动顺序跨战场统一、§14 界面、§15 AI 的落后者停手阈值 C15 与全知例外、§17 遥测字段、§21 带入带出的战场落败结算）做最小同步。
- E2 HANDOFF.md 整体改写（≤ 200 行）。
- E4 后续 change 登记（只登记，不建目录）：AI 停手复核、AI 视野收紧、retire-legacy-maps、等待者失衡评估、Godot 无头偶发崩溃观察。

约束：子 agent 不得 git commit、不得运行 `openspec archive`；不改代码；不跑批量；数字一律取自 proposal 裁决表、各段 implement.md / check.md 与冒烟报告，不得凭记忆写。
