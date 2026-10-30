# 实施说明：map-surface（表现层去棋盘感）

分支 `art/map-surface`（从 main c09971d 之后开）。两段顺序进行，共用"流水线"与"硬约束"。流水线与 `.trellis/tasks/archive/2026-10/10-30-map-restyle/implement.md` 相同，这里只重复要点。

## 流水线

- 构建：`src/godot` 下 `dotnet build Siege.Godot.csproj -nologo -v q`，零警告零错误。
- 改了 `LowPoly.cs` / `LowPolyMesh.cs` / `TerrainParts.cs` 之后必须重导部件：`rm src/godot/parts/terrain_full_art/materials/*.tres` 再 `<Godot> --headless --path src/godot -- --export-parts=res://parts/terrain_full_art`；导完 grep 确认无悬空材质引用。
- Godot：`/d/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`。
- 截图（非 headless）：`<Godot> --path src/godot -- --map=link:5:4 --auto-demo --rounds=1 --mute --screenshot=<绝对路径>.png:150`，加 `--overview` 一张全局；近景可用 `--shot-cell=<格> --shot-near`。信息层打开状态的截图看 `GameRoot` 的 `--shot-*` 系列参数（codegraph 查 `_shotGroups` / `_shotOwnership`），任选一个能把信息层打开的拍一张对照。
- 拾取自检：`<Godot> --path src/godot -- --pick-check --auto-demo --rounds=1 --seed=5 --map=link:5:4 --mute`，退出码 0 且 `[pick-check] 逐格居中·合计` 为 1861 / 1861。
- 快速测试：仓库根 `dotnet test tests/Siege.Core.Tests/Siege.Core.Tests.csproj --filter "Category!=Slow&Category!=Perf"`。
- 代码理解一律先用 codegraph，不整文件 Read `BoardView.cs`（2900 行）。
- 子 agent 不提交；回报改动文件、自证输出行、截图路径、疑虑。

## 硬约束

- 不改规则、地图数据、`Siege.Core` / `Siege.Presentation`、HUD、相机位姿 / 缩放 / 俯角、`BoardGeometry` 常量（含 `FarLabelMargin`，相机可行矩形仍按它算）。
- 拾取按模型（`BoardView.Picking.cs`）：删掉底板与石沿后要确认拾取路径不依赖它们；新加网格不进拾取；跑 `--pick-check`。
- 八种地表面砖基色在 `Visuals.cs`；段E 的分片只对 `Surface.Grass` 生效，其它地表不动。
- 同一张图永远同一副样子：只按坐标散列（`LowPolyMesh.Hash / Unit / Signed`）。
- 行尾：`BoardView.cs` 是 CRLF/LF 混合、`LowPoly.cs` 全 CRLF、`LowPolyMesh.cs` / `TerrainParts.cs` / `Visuals.cs` 看 HEAD；未改动行保持原行尾，`git diff --stat` 与 `--ignore-space-at-eol --stat` 要一致。

## 段D：格缝淡化 / 撤坐标环底板石沿 / 光影

1. 格缝：`AddTileStack` 里衬底（`LinerOf`，现为基色压暗 30%）改成基色压暗约 8%，同地表相邻格读作连续地面；信息层打开时（`Refresh(treatment)` 那条路径，装饰对比 / 饱和度就是在那里随信息层变的）把衬底切到压暗 30% 的那份——做法建议：每个基色备两份衬底材质，`Refresh` 里按 `treatment` 整批换 `AlbedoColor`，不重建网格。悬停格的清晰格线靠现有光标即可，不另做。
2. 坐标标注：`BuildCoordinateLabels` 建出来的标签默认 `Visible = false`，信息层打开时显示（同样挂在 `Refresh(treatment)`）；标签的位置、字号、文案不动（`BoardView.Reveal.cs` 里的揭示标签不受影响）。
3. 撤底板与石沿：`Build` 开头那四块 `labelBacking` 盒子与 `AddRim(...)` 调用删掉（`TerrainParts.Rim` 部件保留在清单里，不删文件）；`AddFloatingIsland` 的岛面（`IslandSurface`）与断崖保持，确认岛面在原底板位置下没有露出黑洞——岛面现在只铺 `islandCells`，底板撤掉后坐标标注区域（apron 带）将是空的，断崖应从岛面边缘直接落下，如有缝隙把 `IslandSurface` 的外沿延到原 apron 内沿即可。`[rim]` 自证行改成"底座外圈石沿 0 件（map-surface 裁决 2 撤除）"。
4. 光影：`WorldEnvironment` 开 `SsaoEnabled = true`（半径 / 强度取 Godot 默认后微调到看得见格间与装饰根部的暗角，不发黑）；主光 `ShadowOpacity` 0.48→0.65，`LightColor` 再暖一档（约 255,238,200）；不改能量，避免岛面再过曝。
5. 验收截图：默认一张（格线应只剩淡刻痕、坐标环消失、岛缘断崖在默认机位可见）、信息层打开一张（格线与坐标清晰）、全局一张。

## 段E：地面分片 / 装饰放大加密

1. 分片：`Build` 的面砖基色选择处（`Surface.Grass` → `Visuals.TilePlayable`），按坐标噪声（两层散列叠加、片块尺度约 4–7 格）把草地格分到三片基色：淡紫石（现 `TilePlayable`）、淡草绿、淡泥褐；后两色新增到 `Visuals.cs`，亮度与土路（190,150,120）/ 荒漠（236,212,150）/ 岩台（140,130,160）拉开 ≥ 20 级灰度。分片只改基色，仍走 `SurfaceOf` 的按基色共用材质与压暗表；出生区淡染、明暗交替逻辑不动。
2. 草丛：`LowPoly.Grass` 放大（带宽 0.22、高 0.20），`Build` 里撒布阈值 13→28；位置仍只在格角带内（带宽 0.22），不占格心。
3. 新部件 `pebble`（小石块，3 档）与 `flower`（小花，3 档，粉 / 黄 / 白）：加进 `TerrainParts.All`，放在草地 / 土路可落子格的格角，与草丛错开（散列不同盐），高 ≤ 0.08；合批、不进拾取。
4. 重导部件，验收截图：近景（看得见成片分色、草丛、石块、小花）、全局一张；八种地表目视可辨。
