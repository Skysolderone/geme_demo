# 全部素材按卡通单位风格重做（chibi-world）

## Goal

单位造型已换成 chibi-pieces 风格：卡通分阶明暗、粗黑描边、饱和配色（`art/chibi-pieces/README.md`，参考图 `art/chibi-pieces/reference.png`）。用户 2026-11-21 要求其余素材也按同一风格重做，让地形、装饰、浮岛、天空、标记、界面和单位成为一套美术。

## 背景（代码盘点）

- 单位风格的实现：`tools/blender/chibi_pieces.py` 生成 glb，运行时由 `PieceModels` 套 `DiffuseMode = Toon` 材质（`ToonMaterialFor`，`src/godot/scripts/PieceModels.cs:516`），并另建 `Outline` 网格作描边：法线外推 0.03、`CullMode = Front`、无光照、深色 (28,24,30)（`OutlineMaterial`，`PieceModels.cs:563`）。
- 运行时地形 / 装饰部件目录是 `res://parts/terrain_full_art`（`TerrainParts.Directory`，`src/godot/scripts/TerrainParts.cs:18`），共 31 类。有 `.tscn` 就加载，没有就退回 `LowPoly` 程序生成。
- 装饰按 (网格, 覆盖材质, 投影, 层) 归组画成 MultiMesh（`BoardView.PartBatchSet.Build`）。模板必须是"根下一层、各带一个网格"，否则不合批。
- 3D 哑光材质的公共工厂是 `Visuals.Matte`（`src/godot/scripts/Visuals.cs:186`）。
- 天空 `src/godot/art/sky/floating-realm-panorama.png`（2109×745）是当初用图像生成工具画的（`art/full-art-v1/source/sky-background.md`），本机无法重新生成。
- 本机（Windows）此前没有 Blender，单位 glb 是在 Mac 上生成的。
- `11-08-terrain-bonus` 进行中（worktree `../geme_demo-terrain-bonus`，段 A 只改规则）；它的段 B 会把岩石改成可落子地表，也含美术。本地未跟踪的 `src/godot/parts/terrain_plateau_temp/` 是另一批没提交的地形美术。

## 用户裁决（2026-11-21，"全按推荐"）

| # | 裁决 |
|---|---|
| 1 | 范围 A–F 全部纳入，分段做。E 标记、F 界面放最后一段，只统一描边与配色，不改标记形状和含义。 |
| 2 | A 装饰用 Blender 脚本重新建模出 glb（与单位同一套管线）；B 地块、C 浮岛不重新建模，在运行时换卡通分阶材质、调色，高差处加描边。 |
| 3 | 用户在本机安装 Blender，此后都在 Windows 上生成。 |
| 4 | 地块只描崖壁、台沿、岛的外轮廓，平地格之间不描。 |
| 5 | 统一为单位那套饱和配色，放弃粉彩浮岛；七种地表提饱和后仍互相可分，领地着色仍可见。 |
| 6 | 天空：保留原图只调色调，或改为代码绘制的渐变天空加卡通云，看图后再定。 |
| 7 | 本任务先做。岩石、峭岩先按障碍造型做，等 terrain-bonus 段 B 再按可落子地表改；`terrain_plateau_temp/` 不碰。 |
| 8 | 回退链与单位一致：glb → `terrain_full_art` 的 `.tscn` → 程序生成；旧目录不删。 |
| 9 | 三段串行，各在独立 worktree：① 装饰 18 类 ② 地块 + 浮岛 ③ 天空 + 标记 + 界面。每段交对照图与对局远、近景截图，跑 `--rounds=6`，不跑长局。装饰每件本体 ≤ 600 三角形，描边同面数。 |

## 段 ① 看图裁决（2026-11-21，"全按推荐"）

岩台（crag）保持贴边矮石沿，只有岩石按障碍做；色板留到段 ② 与地砖一起微调；小件描边不加粗；岩石点缀保留；稀有石碑保持现状（实机看后再议）；火盆加黄色内焰（Glow 槽）；贴地薄片不描边。详见 `art/chibi-world/README.md`「负责人裁决」。

## Requirements

### 段 ①：装饰 18 类（A 组）

- R1.1 新脚本 `tools/blender/chibi_terrain.py` 生成 18 类装饰的 glb：rock、ruins、pines、trees、desert、marsh、crag、shallows、fence_x、fence_z、bridge、grass、crystal、brazier、arch、chest、pebble、flower、stele。档数与 `TerrainParts` 现有档数一致，文件名同 `Kind.FileName`（扩展名改为 `.glb`），输出到 `src/godot/parts/terrain_chibi/`。
- R1.2 风格与单位一致：卡通分阶材质、饱和配色、`Line_Hull` 反向外壳描边（厚度按装饰尺寸定，起点 0.02，看图定）。
- R1.3 占格与锚点契约不变：原点在所在格地砖上表面；包围盒不超过现有部件，可落子格上的点缀（草丛、小石、小花、信物石碑）不挡格心与单位。
- R1.4 预算：每件本体 ≤ 600 三角形，描边 ≤ 本体（贴地薄片不进外壳）；材质槽 ≤ 7（含 Line；段 ① 裁决 6 为火盆黄色内焰加 Glow 槽，由 6 放宽，与单位上限一致）。脚本断言，超限不导出、退出码 1。
- R1.5 运行时：`TerrainParts` 加载顺序改为 `terrain_chibi/<名>.glb` → `terrain_full_art/<名>.tscn` → `LowPoly`。glb 部件仍能被 `PartBatchSet` 合批（每个槽、外加描边各一个 MultiMesh）。
- R1.6 岩石、峭岩按"障碍"造型做（裁决 7）。

### 段 ②：地块 + 浮岛（B、C 组）

- R2.1 地砖、缓坡、崖壁、衬底、河岸泡沫、石沿、出生区亮条、台面边框，以及浮岛岩层、垂岩、云团、岛面、岛崖、根须、瀑布，都改用卡通分阶材质。不重新建模。
- R2.2 调色：放弃粉彩（淡紫石、青绿），改用与单位协调的饱和色板。七种地表底色两两可分（去色后也要能分），领地着色叠上去仍清楚。
- R2.3 描边只加在高差处：崖壁、缓坡的外侧轮廓，台面外框，浮岛外轮廓。平地格之间不加描边。
- R2.4 不增加逐格绘制调用；描边外壳与地块同批（按地块种类 × 档合批），全图绘制调用增量 ≤ 地块种类数 × 档数。

### 段 ③：天空 + 标记 + 界面（D、E、F 组）

- R3.1 天空：先出两版对照图，一版原图调色调，一版代码绘制的渐变天空加卡通云，用户看图后定一版。
- R3.2 地图标记（领地着色、圆环、角标、插旗预览、信物标记、符文环、占位）：只统一配色与描边，形状、大小、含义不变。
- R3.3 界面（HUD、征募 / 手牌面板、势力卡片、小地图）：只统一配色、描边、圆角与字重，布局和文案不变。

### 全段通用

- RG1 回退链（裁决 8）：删掉新目录即回到当前样式；旧目录与 `LowPoly` 不删。
- RG2 规则、AI、地图生成逻辑不改；不碰 `src/godot/parts/terrain_plateau_temp/`。

## Acceptance Criteria

- [ ] 段 ①：`art/chibi-world/` 下有 18 类装饰的对照图（彩色 + 去色），以及对局远景、近景各一张；**由用户看图确认**。
- [ ] 段 ①：脚本断言全部通过；启动日志显示装饰全部来自 glb，无回退；合批仍生效（日志里的 MultiMesh 个数与未合批格数）。
- [ ] 段 ②：七种地表色块对照图（彩色 + 去色），对局远景、近景，带领地着色的局面各一张；**由用户看图确认**。
- [ ] 段 ②：同一局面前后绘制调用对比，增量在 R2.4 以内。
- [ ] 段 ③：天空两版对照图，由用户选定；标记与界面改前、改后对照图；**由用户看图确认**。
- [ ] 每段：`dotnet build` 0 警告 0 错误；`--headless --auto-demo --pick-check` 退出码 0；一次 `--rounds=6` 演示局跑完。
- [ ] 每段：删掉该段新增目录后回到改前样式（日志可证）。

## Out of Scope

- 单位造型（chibi-pieces 已完成）。
- 规则、AI、地图生成逻辑。
- 岩石的可落子地表造型（terrain-bonus 段 B）。
- 动画、相机。
