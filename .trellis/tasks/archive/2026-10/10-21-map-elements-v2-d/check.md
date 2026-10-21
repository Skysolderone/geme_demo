# 段 D 质检记录（trellis-check，2026-10-21，tasks 4.1–4.3）

结论：**通过**（待负责人看图裁决偏离 1–8；无阻塞项）。

## 核对

1. **坐标标注（4.1）**：`BuildCoordinateLabels` / `Label` / `BoardGeometry.cs` 无 diff（BoardView 最后一个改动 hunk 止于 `AddBoardPlates` 内，标注代码在 2381 行之后），字形、字号、位置、锚点算法未变。石沿几何：底座伸出格外缘 2.15，石沿占离格外缘 [1.87, 2.17]；标注锚点离格外缘 0.7（近边、左右）/ 1.2（远边）。导出 `rim_*` 的 AABB 顶 ≤ 0.078 → 世界 y ≤ −0.04 + 0.078 = 0.038 < 标注平面 `TopY` = 0.06：近边石沿在相机与标注之间，但下行射线到标注前始终高于 0.06，挡不到；远边石沿在标注身后，标注平面在其上方，深度测试标注胜出。`CellSize = 1`。
2. **亮条 / 边框（4.2）**：
   - `zone_strip` AABB(−0.5, 0, 0, 1, 0.03, 0.07)，锚在格边中点、`Facing((−dx, 0, dy))` 朝格内 → 占格内侧 [0.43, 0.5] × y [0, 0.03]，与改前描边盒（中心偏 0.465、宽 0.07、y 偏 0.015、高 0.03）逐值相同。
   - `plate_frame_*` AABB(−0.5, 0, −0.065, 1, 0.065, 0.13)（高 0.065 ≤ 0.07、宽 0.13 ≤ 0.14），逐格边摆在 `TopYOf(0)`；`plate_corner` AABB(±0.07, 0–0.07) 盖段端。并集落在改前"段长 + 0.14、高 0.07、骑线 ±0.07"的边框盒之内，不越界；竖边 `Facing(Right)` 把局部 x 映到世界 −Z，正确。接缝分支仍是墨色 `Strip` 后 `continue`，接缝上无边框件；单块分支四条 `Strip` 与 `FlatOf` 原样。
   - 实跑：`link:1` `[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`、`[zone] 出生区描边：出生区 1 12 段、…出生区 5 12 段；棋盘内岩石 0 格（按棋盘格画）`；`link:7` `…227 格）：外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段`、`[zone] … 棋盘内岩石 2 格（按棋盘格画）`——与段 A 记录的 0.1 基线逐字相同。
3. **占位轮廓**：`AddPlaceholders`（1407 起）到 `private const float WaterDrop` 区间无改动 hunk，无 `TerrainParts.` / `AddTileStack(` / `lineWidth`；`Visuals.cs` 无 diff；台面边框色只经 `InlayOf(Visuals.PublicBoardFrame)`（`Visuals.Flat` + 顶点色），无新颜色字面量。
4. **浮岛（4.3）**：层表、`h` / `t` / `length` / `alongX` / `side` / `i × 37°`、云的 `angle` / `radius` / `size` / `i % 4` 未改；垂岩 `at.Y` 由 `− length × 0.5` 改为 `− length` 只是锚点从圆锥中心换到尖端的补偿，顶面仍在 `baseTop − 0.9`。瀑布段（`const float drop = 9f;` 起）无 diff。非均匀缩放：`LowPolyMesh.Tri` 写的是三角面自身的几何法线，Godot 4（工程用默认 Forward+）对非均匀缩放实例用模型矩阵逆转置变换法线，正缩放不翻绕序——论证成立；垂岩 X / Z 等比，与绕 Y 旋转可交换；云等比。云团材质导出为 `transparency = 1`、`shading_mode = 0`（不受光）、`cull_mode = 2`、α 0.55，节点 `CastShadow = Off`。
5. **确定性**：新增行无 `Random` / `GetHashCode` / 时间 / `RoundToInt` / `(width|height) − 1`；变体只来自 `LowPolyMesh.Hash` / `Unit` / `Signed`、`TileVariant`、序号。`Facing` 四个方向行列式均为 +1（纯旋转），方向只取原循环的 `dx` / `dy` 与原有的"y 北 → 世界 −Z"约定，没有第二份坐标映射。
6. **守门与边界**：`AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);` 字面仍在（1 处）；`board.ObstacleArtAt(cell.Coord)` / `ObstacleArt.BoardRock` / `_decoration.AddChild(rock)` 未动；`LowPoly.cs`、`BoardGeometry.cs`、Core / Presentation / Sim / tests 无 diff；`Godot层不含规则计算Tests` 的 token 不出现。
7. **死代码**：`Strip` 仍被接缝格线、棋盘内格线、单块分支调用；`frameWidth` / `frameHeight` 单块分支在用；`FlatOf` 由单块分支与占位使用；`BoxOf` 仍被其他处使用。
8. **资源**：本次导出到 scratchpad（`部件 75 / 75、共享材质 27`，三角形数与实施记录逐项相同），`norm_cmp.py` 对仓库：21 个新文件归一后全同（与实施者那次导出是两次独立运行，可复现）；其余仅 `pines_*` / `ruins_*`（自动节点名，段 C 同款）与 12 份孤儿材质（留给 5.1），导出的 27 份材质文件名在仓库里都已存在——材质键改动对既有材质零影响。

## 自修

- `BoardView.cs` `FlatOf` 摘要：原写"出生区描边条、棋盘边框"已过时，改为"占位边框、单块棋盘边框；亮条与连体外轮廓边框改用 `InlayOf`"。
- `TerrainParts.cs` `RimMesh` 摘要："底在 −0.14（没到底座底面之下 0.02）"易读成"没到"，改为"（底座厚 0.12，即探到底座底面之下 0.02）"。
- 只改注释，未动网格函数，无需重导。

## 偏离判定（属实与副作用，不替裁）

1. `plate_corner`（D2 表外）：属实。副作用：转角件比边框件高 0.005、宽 0.01，转角处有一级小台阶，观感看图定。
2. `plate_frame` 逐格边一件、锚在格边中点：属实，`Kind.Anchor` 已写明；尺寸略小于上限。
3. `island_layer` 三档 = 三种岩色、`island_spike` 0 档专作深岩：属实；`Kind.Variants` 在这里借作"颜色档"，与 D2 表"档数"本义（外形变体）不同。
4. 岩层收分 0.92 与切角、垂岩尖端半径 0：属实；下一层顶（0.914 / 0.80 / 0.67 / 0.46）仍窄于上一层底 0.92，层次关系不变，顶面不建由上一层底面盖住。
5. 云团自转 `i × 53°`：属实，纯观感。
6. 材质键 `flat_` 前缀 / `_a<α>` 后缀：属实；只影响不受光材质，既有材质文件名不变（第 8 条坐实）。
7. 新日志 `[rim]`、`[frame]`：属实，不在逐字清单内；`[batch]` 第二行"描边 / 边框材质"= flat + inlay，数值仍 6 份。
8. 看图旋钮（边框顶面色差、石沿取崖壁同一份岩灰）：属实，待看图。

## 备注（不修）

- `TerrainParts.IslandColor` 与 `AddFloatingIsland` 里 `earth` / `rock` / `deepRock` 的配方是两份拷贝（后者现只作资源缺失时的退路），改色需两处同步。
- 石沿复用 `matte_887c74_r90_vc`（崖壁侧面、河岸石沿同一份）；该材质不在压暗表 `_tileMaterials` 里，石沿也不挂 `_decoration`，信息层打开时不压暗、不缩放，与崖壁侧面一致。
- `AddRim` 节距 = 边长 / round(边长)，边长 < 10 格时节距可能 < 0.95 使相邻件重叠；链接图边长 ≥ 27，不触发。

## 复跑（顺序单跑；同时段另一条线在跑 Release 测试）

- `dotnet build src/godot/Siege.Godot.csproj`（Debug，改注释后）：0 警告 0 错误。
- Godot `--export-parts=<scratchpad>/d1`：退出 0。
- Godot `-- --auto-demo`：退出 0；`[flow] 自检 5 条，失败 0 条`；`[parts] 资源 136 / 745 / 746 件、程序生成 0 件`；`[rim] 84 件`；`[frame] 边框件 68 件、转角件 8 件`；日志 error / exception 0 处。
- Godot `-- --auto-demo --pick-check --seed=5 --map=link:7`：退出 0；`可落子格 218，至少在一个位姿下验到 218；通过`；`[parts] 资源 136 / 787 件、程序生成 0 件`；`[rim] 84 件`；`[frame] 边框件 84 件、转角件 10 件`；error / exception 0 处。
- `dotnet test siege.sln`（改注释后，Debug）：退出 0，`已通过! - 失败: 0，通过: 2100，已跳过: 6，总计: 2106，持续时间: 10 m 31 s`；源码扫描守门（`占位轮廓灰度Tests`、`双字母列标不重叠Tests`、`棋盘坐标标注Tests`、`禁手标记的远近可辨Tests`、`六种棋子的轮廓语言Tests`、`棋盘台面的可视表现Tests`、`Godot层不含规则计算Tests`）随之通过。
- 未做（主会话负责）：截图、`--perf-sample`；`--map-select`、`--rounds=0` 两条自检本轮未复跑（实施者已跑，退出 0）。
