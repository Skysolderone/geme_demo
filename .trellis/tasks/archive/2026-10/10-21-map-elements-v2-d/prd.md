# map-elements-v2 段 D：外框与底座

需求、裁决（M1–M10、O-1–O-4、A1–A3、B1–B5、C1–C3）与验收标准以 `openspec/changes/map-elements-v2/`（proposal / design / tasks / specs）为准，这里不重复。

- 范围：tasks 4.1–4.3（底座外圈石沿 `rim`；出生区描边 `zone_strip`、连体外轮廓边框 `plate_frame`；浮岛岩层 / 垂岩 / 云团部件化）。截图由主会话做。
- 硬约束：只动 `src/godot/`（含 `parts/terrain/`）；坐标标注字形与位置不变；占位轮廓与单块边框分支不动；`[zone]` / `[plates]` 自证行逐字不变；瀑布不变；台面颜色只取 Presentation 调色板。
- 前置：段 A–C 已提交（3f3210d、b41f5b6、0983803），记录在 `.trellis/tasks/archive/2026-10/10-21-map-elements-v2-a|b|c/`。
