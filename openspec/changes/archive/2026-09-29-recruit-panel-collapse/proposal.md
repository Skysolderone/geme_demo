## Why

图形版征募阶段的中央面板（约 520×330）一直盖在棋盘中央，无法收起；玩家选子时需要看全场地图局面（负责人 2026-09-29）。只能靠推屏把区域挪出面板，体验差。负责人同时裁决：不做倒计时。

## What Changes

- 征募面板可用 `V` / 面板上的"收起"按钮收起为顶部窄提示条，再按 `V` / 点提示条展开；收起期间推屏、缩放、信息层照常；征募状态不变。
- 每个新征募阶段默认展开；收起状态只在表现层，不进对局状态、存档、日志。
- 截图选项 `--shot-recruit-collapsed`（仅截图用）用于给负责人出图。

## Capabilities

### New Capabilities

（无）

### Modified Capabilities

- `hand-info-panel`：新增「征募面板可收起」。

## Impact

- `Siege.Presentation`：一个纯状态模型（收起 / 展开、按征募阶段重置），可单元测试。
- Godot：`InputBindings`（`V` 与手柄键）、`Hud`（提示条与按钮）、`GameRoot`（按键处理、截图选项）。
- Core / Sim 不改；终端不变。

## Non-goals

- 倒计时（负责人裁决不做）。
- 其他面板（手牌信息面板、结算、补给）的收起。
