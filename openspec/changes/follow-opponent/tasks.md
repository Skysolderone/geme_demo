## 1. 呈现层（Siege.Presentation）与测试

- [x] 1.1 `CameraFollow` 状态机（design D1 / D3 / D4）。验证：`ViewportCamera/跟随对手行动Tests`，逐场景；变异验证记录。
- [x] 1.2 `TurnSummary`（D5）。验证：`SettlementShow/对手回合摘要Tests`，四个场景。
- [x] 1.3 飘字寿命 1.4 秒、近景字号加大（D6）。验证：`SettlementShow/飘字的停留时长Tests`；既有钉 600 的断言改为 1400。
- [x] 1.4 `GroupPowerLabels`（D7）。验证：`TacticalLayers/棋串军势常驻标注Tests`，四个场景。

## 2. Godot 接线

- [x] 2.1 `GameRoot`：AI 结算开演前跟随、移动期间演出不推进、轮到本机返回、手动相机操作即取消；`F` 键切换；开局对准自检改口径。验证：`--auto-demo` 帧序列与自检不变（v5、`board:1`）。
- [x] 2.2 `Hud`：对手回合摘要条、底部「跟随 [F]」按钮状态。验证：截图。
- [x] 2.3 `BoardView`：棋串军势常驻标注（全局预览下不画）；飘字新字号。验证：截图。

## 3. 验收

- [x] 3.1 `--shot-show=placement` 在 `board:1` 上截一张：相机已移到对手落子处、飘字与摘要可见；对照同一帧不跟随时的画面。
- [x] 3.2 构建 0 警告；`dotnet test` 失败集合与本机基线相同；`--pick-check` 通过；`openspec validate follow-opponent --strict` 通过。
- [x] 3.3 设计文档 §14.4 与变更记录；README 的操作表加 `F` 键。发布页（`docs/`）随下一个发布版本一起更新，本 change 不动它（线上是 v0.1.0，没有这个功能）。
