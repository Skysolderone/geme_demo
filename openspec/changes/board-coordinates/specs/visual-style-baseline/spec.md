## ADDED Requirements

### Requirement: 棋盘坐标标注

棋盘 SHALL 在四边显示围棋记法的坐标标注：列字母沿棋盘的上下两边，行数字沿左右两边。字母与数字的取值 MUST 与 `board-topology`「坐标记法」一致（列 `A`–`L` 跳过 `I`，行自下而上从 `1` 起），且 MUST 由 `Coord` ↔ 3D 的唯一映射推出，MUST NOT 在视图层另算一份。

标注 SHALL 在相机旋转时保持正对玩家，MUST NOT 出现镜像或倒置。

标注 MUST NOT 遮挡任何格子，也 MUST NOT 影响合法落点、气与领地归属的判读。

#### Scenario: 标注与日志一致
- **WHEN** 玩家在界面上看到某个格子的坐标标注，并在对局日志里查找同一格
- **THEN** 两处的坐标记法完全一致

#### Scenario: 旋转后仍可读
- **WHEN** 相机绕棋盘旋转 180°
- **THEN** 四边的标注仍正对玩家，既不镜像也不倒置，且字母仍在上下两边、数字仍在左右两边

#### Scenario: 跳过字母 I
- **WHEN** 读取 11 列棋盘的列标注
- **THEN** 依次为 `A B C D E F G H J K L`，其中没有 `I`

#### Scenario: 标注不遮挡棋盘
- **WHEN** 棋盘四边均有标注且边缘格上有棋子
- **THEN** 边缘格的落子判读不受标注影响
