# 09-16-board-coordinates

> 规格权威：`openspec/changes/board-coordinates/`。
> 设计文档来源：`board-topology`「坐标记法」、`visual-style-baseline`。

## Goal

`board-topology` 要求面向人的输出一律使用围棋记法。日志、错误信息、终端版都照做了，唯独 3D 界面没有任何坐标标注——
界面恰恰是最面向人的那个输出。玩家在屏幕上看到一个格子，无法与日志、规格或旁人的口头描述对上。

本任务在棋盘四边显示围棋记法坐标：列字母沿上下两边，行数字沿左右两边。

## Acceptance

| 验收项 | 对应规格 |
|---|---|
| 四边有标注，列字母在上下、行数字在左右 | 「棋盘坐标标注」正文 |
| 文本与日志一致（跳过 `I`，行自下而上，A1 左下） | Scenario 1、3 |
| 不镜像、不倒置，左右同行读数相同 | Scenario 2 |
| 不遮挡边缘格的落子判读 | Scenario 4 |
| 表现层不得自带第二份字母表 | 守门测试 |

## Out of scope

- 坐标的点击定位、跳转、搜索。
- 相机控制与棋盘朝向。
- 2 人 / 3 人地图（尚未制作）。

## Notes

- **不要用 billboard**。Godot 的 Label3D 在 billboard 下渲染文字面的背面，四边标注全部左右镜像，
  而 `1` `0` `8` 字形对称看不出异常。已改为平铺在棋盘平面，守门测试挡住回退。见 design.md §2.1。
- 视觉部分靠人工看图，清单在 `art/coord-labels/README.md`。截图不能加 `--headless`。
