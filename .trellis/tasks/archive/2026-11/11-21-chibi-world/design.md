# 设计：全部素材卡通化（chibi-world）

## 总体

三段串行，每段一个 worktree，合回 main 后再开下一段（裁决 9；同一时间只有一条线改地图美术）。

| 段 | 管线 | 主要改动文件 |
|---|---|---|
| ① 装饰 | Blender 脚本 → glb → Godot 导入 → `TerrainParts` 加载 | `tools/blender/chibi_terrain.py`（新）、`src/godot/parts/terrain_chibi/`（新）、`TerrainParts.cs`、`BoardView.cs`（合批识别描边） |
| ② 地块 + 浮岛 | 运行时材质与色板 | `Visuals.cs`（卡通材质工厂）、`TerrainParts.cs`（色板、地块描边网格）、`BoardView.cs`（地表底色、浮岛材质） |
| ③ 天空 + 标记 + 界面 | 运行时色板、可选程序天空 | `BoardView.cs`（背景、标记）、HUD 相关脚本 |

## 共用：卡通材质与描边

- 把 `PieceModels.ToonMaterialFor` / `OutlineMaterial` 里不依赖阵营的部分提到 `Visuals`：`Visuals.Toon(Color, ...)` 与 `Visuals.Outline(...)`。单位和地形共用同一份描边墨色 (28,24,30) 与分阶参数，保证"同一套美术"。单位行为不变（只是调用新工厂）。
- 描边做法同单位：反向外壳（法线外推）、`CullMode = Front`、Unshaded、不投影；拾取时跳过（`PieceModels.IsOutline` 的名字约定 `Outline` 沿用）。

## 段 ①：glb 契约（装饰）

- 物体名 `<槽>_<部件>`。槽 ∈ Stone / Shade / Wood / Leaf / Water / Crystal / Gold / Flame / Line（取代旧的部件名配色 `TerrainParts.ArtColor`；运行时按槽上色）。
- `Line_Hull`：全部本体零件沿顶点法线外推得到；运行时成为名为 `Outline` 的独立 MeshInstance。
- 不建父子层级，`export_apply=True`，`export_materials="NONE"`（与单位一致）。
- 坐标与锚点：原点 = 所在格地砖上表面（或 `Kind.Anchor` 所写的点，如草丛在第 v 个格角内侧）。Blender -Y = Godot +Z。
- 档数：沿用 `LowPoly.*Variants` 与 `TerrainParts` 里写死的档数；脚本里的档数表与之核对，不一致即断言失败。
- 运行时加载：`TerrainParts.SceneOf` 先试 `res://parts/terrain_chibi/<名>[_v].glb`，再试 `terrain_full_art/*.tscn`。glb 实例化后由 `TerrainParts` 按槽把各 MeshInstance 换成卡通材质 + 描边材质，结构保持"根下一层、各带一个网格"，`PartBatchSet` 无需改结构即可合批。描边 MeshInstance 的材质全图共用一份，所以所有装饰的描边按网格归组。
- 预算：本体 ≤ 600 三角形、描边同面数、槽 ≤ 6（含 Line），脚本断言。

## 段 ②：地块与浮岛

- 材质：地块现有的 `Visuals.Matte` 调用改为 `Visuals.Toon`；顶点色色带保留（分阶明暗叠在色带上）。
- 色板：`TerrainParts` 顶部的 `Art*` 常量与 BoardView 七种地表底色一起换成饱和色板；先出色块对照图（彩色 + 去色）再定值。
- 描边只在高差处：
  - 崖壁 / 缓坡：给 `SideSlope` / `SideCliff` 网格各配一份只含外侧竖面的描边外壳（同档同批）。
  - 台面外框 `PlateFrame` / `PlateCorner`、浮岛 `IslandLayer` / `IslandSpike`：各配描边外壳。
  - 平地 `TileTop` / `Liner` 不配描边（裁决 4）。
- 领地着色、出生区亮条用的是 Unshaded 覆盖材质，保持不受光，不卡通化。

## 段 ③：天空、标记、界面

- 天空两版：A 原图在 `RealmBackdrop` 上叠调色（饱和度 / 色温）；B 代码绘制的竖向渐变 + 几朵卡通云（2D 画布，不进 3D）。截图给用户选，落选的不入库。
- 标记、界面：只换色值、描边、圆角、字重；形状、尺寸、文案、布局不动。

## 取舍

- 地块不重新建模：逐格铺满，重建形状性能风险最大，收益却主要来自材质和色板。
- 描边只在高差处：平地全描会形成满图黑网格，盖住领地着色。
- 装饰用新目录 `terrain_chibi/`，不覆盖 `terrain_full_art/`：回退只需删目录。

## 回滚

- 段 ①：删 `src/godot/parts/terrain_chibi/`。
- 段 ②、③：代码改动，回滚靠 git revert；色板集中在常量处，便于单独回退。
