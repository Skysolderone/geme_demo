# map-elements-v2 段 B：水系与设施

需求、裁决（M1–M10、O-1–O-4、A1–A3）与验收标准以 `openspec/changes/map-elements-v2/`（proposal / design / tasks / specs）为准，这里不重复。

- 范围：tasks 2.1–2.2（`water_bed`、`bank_lip`；桥与栅栏重建）。2.3 截图由主会话做。
- 硬约束：只动 `src/godot/`（含 `parts/terrain/`）；不改 Core / Presentation / Sim、规则、AI、地图生成；几何尺寸、水面下沉量与拾取不变；水面流纹与瀑布 shader 及瀑布触发条件不变；邻格是否深水只按视图模型格子地表查表，不调 Core 邻接 API。
- 前置：段 A 已提交（3f3210d，归档 `.trellis/tasks/archive/2026-10/10-21-map-elements-v2-a/`，含 0.1 基线与段 A 记录）。
