# map-elements-v2 段 A：建模工具与地块

需求、裁决（M1–M10、O-1–O-4，2026-10-21 全按推荐确认）与验收标准以 `openspec/changes/map-elements-v2/`（proposal / design / tasks / specs）为准，这里不重复。

- 范围：tasks 1.1–1.3（`LowPolyMesh`、部件目录扩展 `tile_top` / `side_slope` / `side_cliff` / `liner`、`AddTileStack` 改取部件）。1.4 截图由主会话做。
- 硬约束：只动 `src/godot/`（含 `parts/terrain/`）；不改 Core / Presentation / Sim、规则、AI、地图生成；几何尺寸与拾取不变；压暗、出生区淡染、明暗棋盘格机制不变。
- 前置：0.1 改前基线已取（见 implement.md）。
