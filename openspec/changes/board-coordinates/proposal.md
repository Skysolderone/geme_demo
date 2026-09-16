## Why

`board-topology` 的「坐标记法」要求：面向人的输出 MUST 使用围棋记法（列 `A`–`L` 跳过 `I`，行自下而上从 `1` 起）。对局日志、错误信息都照做了，**只有游戏界面没有**——3D 棋盘上没有任何坐标标注。

界面是最面向人的那个输出。现状下玩家在屏幕上看到一个格子，无法把它和日志里的 `J4`、规格里的 `A1`、或者同伴口头说的"E5"对上；复盘、报 bug、讨论棋局都缺一个共同的指称。

## What Changes

- 3D 棋盘的四边显示坐标标注：列字母沿上下两边，行数字沿左右两边，与 `BoardGeometry` 的 `Coord` ↔ 3D 映射对齐。
- 标注随相机旋转保持正对玩家（始终可读），MUST NOT 随棋盘一起翻转导致镜像或倒置。
- 标注 MUST NOT 遮挡任何格子的落子判读，遵守既有的「方格边界与合法落点始终清晰」。
- `visual-style-baseline` 新增 Requirement「棋盘坐标标注」。

## Capabilities

### New Capabilities
无。

### Modified Capabilities
- `visual-style-baseline`: 新增 Requirement「棋盘坐标标注」。

## Impact

- 受影响代码：`src/godot/scripts/BoardView.cs`、`BoardGeometry.cs`（取格心与棋盘边界的唯一映射）。
- 不影响 `Siege.Core` 与 `Siege.Sim`，不改任何规则。
- 验证需要人工看图（Godot 截图），无法只靠单元测试。

## Non-goals

- 不做坐标的点击定位、跳转或搜索。
- 不改相机控制与棋盘朝向。
- 不为 2 人 / 3 人地图适配（那两张图尚未制作）。
