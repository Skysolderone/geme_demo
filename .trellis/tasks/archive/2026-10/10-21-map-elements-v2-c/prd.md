# map-elements-v2 段 C：障碍与装饰

需求、裁决（M1–M10、O-1–O-4、A1–A3、B1–B5）与验收标准以 `openspec/changes/map-elements-v2/`（proposal / design / tasks / specs）为准，这里不重复。

- 范围：tasks 3.1–3.2（`rock` 4→6 与 `trees` 重建；`desert` / `marsh` / `crag` / `shallows` 四种新地表点缀重建；`pines` / `ruins` 按 M3 保留不重建）。3.3 截图由主会话做。
- 硬约束：只动 `src/godot/`（含 `parts/terrain/`）；不改 Core / Presentation / Sim、规则、AI、地图生成；装饰仍挂 `_decoration`（信息层压淡装饰机制不变）；规格「新地表的灰度可辨」的形状约束不变。
- 前置：段 A（3f3210d）、段 B（b41f5b6）已提交，记录在 `.trellis/tasks/archive/2026-10/10-21-map-elements-v2-a|b/`。
