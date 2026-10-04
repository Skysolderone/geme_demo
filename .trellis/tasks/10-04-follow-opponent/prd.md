# 10-04-follow-opponent

> 规格权威：`openspec/changes/follow-opponent/`。

对手行动的镜头跟随与数值显示（负责人 2026-10-04）：跟过去再回来、默认开可按键关；对手回合摘要、飘字停留更久更醒目、棋串军势常驻标注。

验收：`tasks.md` 各条的验证项。约束：同一时间只跑一个 dotnet / Godot；不跑批量对局；纯逻辑放 `Siege.Presentation` 并配规格场景测试与变异验证；Godot 层不算任何数值。

## 实现记录（2026-10-04）

- 呈现层：`Camera/CameraFollow`、`Show/TurnSummary`、`Layers/GroupPowerLabels`；`Callout.LifetimeMs` 600 → 1400；`CalloutLabelStyle.Near` 48 / 6 → 72 / 10。
- Godot：`GameRoot`（开演前问跟随、移动期间不推进演出、轮到本机返回、平移 / 缩放 / 回家 / 全局预览即让位、`F` 键）、`Hud`（摘要条、「跟随 [F]」按钮、通知行让位）、`BoardView.DrawGroupPower`、`InputBindings`、`MatchSession.LastAiActor`。
- 测试：新增 `跟随对手行动Tests`（8）、`对手回合摘要Tests`（4）、`飘字的停留时长Tests`（3）、`棋串军势常驻标注Tests`（4）；既有 `落子与提子节拍的内容Tests`、`信物揭示节拍Tests` 的飘字年龄期望值随寿命更新；浮点豁免名单加 `CameraFollow`。
- 变异（改实现一行 → 红的测试数）：M1 每次跟随覆盖返回位姿 → 1；M2 中央区域 && 改 || → 6；M3 手动操作一律置不再跟随 → 1；M4 摘要不按行动者筛势力 → 3；M5 并列取坐标序最大 → 1；M6 寿命改回 600 → 4；M7 关闭后仍跟随 → 1；M8 轮到本机不清返回位姿 → 1。还原后 0 红。
- 验证：见 `art/follow-opponent/README.md`。返回、让位、关闭三条的接线只有状态机层面的测试，需实际对局确认。
- `project.godot` 里两行的顺序被 Godot 导出时重排（内容不变），一并提交。
