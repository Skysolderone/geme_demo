# Directory Structure

> How frontend code is organized in this project.

---

## Overview

<!--
Document your project's frontend directory structure here.

Questions to answer:
- Where do components live?
- How are features/modules organized?
- Where are shared utilities?
- How are assets organized?
-->

(To be filled by the team)

---

## Directory Layout

```
<!-- Replace with your actual structure -->
src/
├── ...
└── ...
```

---

## Module Organization

<!-- How should new features be organized? -->

(To be filled by the team)

---

## Naming Conventions

<!-- File and folder naming rules -->

(To be filled by the team)

---

## Examples

<!-- Link to well-organized modules as examples -->

(To be filled by the team)

## 棋子模型资产（chibi-pieces，2026-11-21）

- 首选 `src/godot/parts/pieces_chibi/<key>.glb`。它由 `tools/blender/chibi_pieces.py` 生成，**不要手改 glb**，改造型就改脚本后重新生成：
  `Blender -b --factory-startup --python tools/blender/chibi_pieces.py -- src/godot/parts/pieces_chibi [key...]`。
- 生成后必须用 4.7.2 mono 版执行 `Godot --headless --path src/godot --import`。不要用 4.5 标准版打开工程，它会把 `project.godot` 的 features 改坏、去掉 C#。
- 回退链：`PieceModels.TryBuild` 依次尝试 glb → `parts/pieces/<key>.tscn`（旧样式，带八角底座和印章）→ `LowPoly`。删掉 `pieces_chibi/` 就能回到旧造型。
- glb 契约：
  - 物体名 `<槽>_<部件>`，槽 ∈ Stone/Shade/Metal/Enamel/Cloth/Crystal/Ink/Line；
  - `Line_Hull` 是描边外壳，运行时为独立的 `Outline` 节点，拾取时跳过；
  - `Anchor_Emblem` 是空物体，作为徽记锚点。
- 脚本内有断言：本体 + 圆环 ≤ 1200 三角形，加描边 ≤ 2400，材质槽 ≤ 7，`RUNTIME_RING_TRIS` 与 `PieceModels.FootRingSegments` 一致。任何一条不满足就不导出。
- Blender 每次导出的 glb 字节都不一样，不能用逐字节比对守门。
