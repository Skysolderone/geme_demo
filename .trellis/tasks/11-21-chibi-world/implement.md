# 执行计划：chibi-world

前置：Blender 5.3.0 已装（2026-11-21，winget）。`chibi_pieces.py` 在 5.3 上试跑退出码 0，十种单位的三角形数与 `art/chibi-pieces/README.md` 数据表逐个一致。

通用命令（PowerShell）：

```powershell
$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
$B = "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe"
dotnet build src/godot/Siege.Godot.csproj -v q
& $G --headless --path src/godot --import
& $G --headless --path src/godot -- --auto-demo --pick-check          # 退出码 0
& $G --headless --path src/godot -- --auto-demo --rounds=6 --seed=9 --map=link:9:4:l
```

截图须非 headless，`--screenshot=` 给绝对路径（`E:/wws/geme_demo/art/chibi-world/<名>.png:<帧>`）。

## 段 ①：装饰 18 类（worktree `../geme_demo-chibi-world-1`，分支 `chibi-world-1`）

1. 读 `tools/blender/chibi_pieces.py`、`TerrainParts.cs`、`LowPoly` 里 18 类的造型函数与档数。
2. 写 `tools/blender/chibi_terrain.py`：公共部分（描边外壳、预算断言、导出）照单位脚本；逐类建模，档数表与 `TerrainParts` 对齐。
3. `& $B -b --factory-startup --python tools/blender/chibi_terrain.py -- src/godot/parts/terrain_chibi`，然后 `--import`。
4. `Visuals.Toon` / `Visuals.Outline` 提取；`PieceModels` 改为调用它们（单位外观不变）。
5. `TerrainParts.SceneOf` 加 glb 优先；按槽换卡通材质、描边节点命名 `Outline`；确认 `PartBatchSet` 合批生效、拾取跳过描边。
6. 拍图：装饰对照图（彩色 + 去色，需加一个 `--terrain-gallery=` 参数，仿 `PieceGallery`）、对局远景 / 近景（种子 9、`link:9:4:l`）。
7. 自检：build、pick-check、`--rounds=6`；删 `terrain_chibi/` 验证回退。
8. 写 `art/chibi-world/README.md` 段 ① 部分（命令、检查清单、三角形数据），交用户看图。

## 段 ②：地块 + 浮岛（段 ① 合回 main 后开 worktree `chibi-world-2`）

1. 七种地表 + 侧面 + 浮岛的色板草案，出色块对照图（彩色 + 去色），用户先定色。
2. 地块与浮岛材质换 `Visuals.Toon`。
3. 高差描边外壳：SideSlope、SideCliff、PlateFrame、PlateCorner、IslandLayer、IslandSpike。
4. 对局远景、近景、领地着色局面截图；同局面绘制调用前后对比。
5. 自检同上。

## 段 ③：天空 + 标记 + 界面（段 ② 合回后开 `chibi-world-3`）

1. 天空 A / B 两版截图，用户选定。
2. 标记、界面改色值与描边；改前、改后对照图。
3. 自检同上。

## 风险点

- `PartBatchSet.IsPlainPart` 对 glb 子节点的判定（逐面材质、导入后层级）。glb 导入后若带额外层级，要在 `TerrainParts` 里拍平。
- 装饰描边让 MultiMesh 个数翻倍，看合批日志确认数量级。
- 段 ② 色板一改，所有截图与读数相关的视觉判据都要重看（领地着色、出生区亮条、可落子格压暗）。
- terrain-bonus 段 B 将来重做岩石，届时以它为准。
