# 全量地形资源源文件

可编辑源是 `src/godot/scripts/TerrainParts.cs`、`LowPolyMesh.cs` 与 `LowPoly.cs` 中的确定性程序网格。`PartExport` 将它们烘焙为 Godot 场景；运行时从 `res://parts/terrain_full_art/` 加载，缺失时从同一建模函数回退。旧 `res://parts/terrain/` 保留，不参与新包加载。

## 视觉与接口

- 雕刻角饰地砖、错缝暖灰紫砌石、暖褐木桥与栅栏、旧金晶饰/岛缘；森林为青绿并点缀暮紫。棋盘底色仍由 `BoardView` 按地表、出生区和信息层决定，以保留可读性。
- 全部 **23 类、75 档**沿用 `TerrainParts.All` 的名字、档数和锚点。`tile_top` 等地块单件仍只有一个网格、一个 surface；桥和装饰件保留根节点下的直接网格子节点。网格不改变格距、地砖尺寸或拾取面。
- 顶板角刻线在格心外；林地小树保持格角布局；金饰只出现在障碍、结构件和棋盘外缘。可落子格的中心与格线留白。

## 重生成

在仓库根目录运行：

```powershell
dotnet build src/godot/Siege.Godot.csproj --no-restore -v:q
& 'D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path src/godot -- --export-parts=res://parts/terrain_full_art
```

导出器只应指向新目录；它会覆盖该目录下的同名场景和材质。改了调色后重导出，检查 `materials/` 中是否有不再被任何 `.tscn` 引用的旧材质文件。`[export-parts]` 应报告 **75 / 75**；当前包使用 **21** 份共享材质。

## 验证

```powershell
& 'D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path src/godot -- --map=link:1 --auto-demo --seed=5 --rounds=1 --pick-check
```

检查 `[parts]` 的资源数大于零、程序生成数为零，以及 `[pick-check]` 覆盖全部可落子格。深水本体与流纹在 `BoardView.AddWater`，台面与地表覆盖色也在 `BoardView` / `Visuals`；它们不是 `TerrainParts` 场景文件。
