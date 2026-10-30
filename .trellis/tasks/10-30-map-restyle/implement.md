# 实施说明：map-restyle（粉彩浮岛）

分支 `art/map-restyle`。段A（配色 / 材质 / 光照）已由主会话提交（41f431d 起）。本文件是段B、段C 子 agent 的作业单；共用"流水线"一节。

## 流水线（每段都按这个跑）

- 构建：在 `src/godot` 下 `dotnet build Siege.Godot.csproj -nologo -v q`，要求零警告零错误。
- 部件快照：改了 `LowPoly.cs` / `LowPolyMesh.cs` / `TerrainParts.cs` 之后必须重导：
  ```
  rm src/godot/parts/terrain_full_art/materials/*.tres
  <Godot> --headless --path src/godot -- --export-parts=res://parts/terrain_full_art
  ```
  游戏运行时只认 `res://parts/terrain_full_art`（`TerrainParts.Directory`）；`--export-parts` 的帮助文字写的 `res://parts/terrain` 是旧目录，不要导到那里。导完检查 `git status`，没有悬空引用（`grep -rn "<删掉的材质名>" src/godot`）。
- Godot 可执行：`D:\software\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`（Git Bash 写成 `/d/software/godot/.../..._console.exe`）。
- 实机截图（非 headless，窗口会弹出）：
  ```
  <Godot> --path src/godot -- --map=link:5:4 --auto-demo --rounds=1 --mute --screenshot=<绝对路径>.png:150
  <Godot> --path src/godot -- --map=link:5:4 --auto-demo --rounds=1 --mute --overview --screenshot=<绝对路径>_overview.png:150
  ```
  截图写到会话的 scratchpad 目录，不要写进仓库。运行输出不要只 grep 截图行，还要 `grep -iE "error|failed|无法|失败"` 看一遍资源加载错误。
- 拾取自检：`<Godot> --headless --path src/godot -- --map=link:5:4 --pick-check`，输出里的拾取自检行必须是通过。
- 代码理解一律先用 codegraph（`codegraph_explore` / `codegraph_node`），不要整文件 Read `BoardView.cs`（2700 行）。
- 子 agent 不提交；改完汇报改动文件、截图路径、构建 / 拾取自检的输出行。

## 硬约束（两段共用）

- 不改规则、地图数据、`Siege.Core` / `Siege.Presentation`、HUD、角色模型、相机交互。
- `BoardGeometry` 的格距、`TopY`、`TileHeight`、`LayerHeight`、`WaterDrop` 不改；任何新网格不得高出所在格的地砖上表面（装饰除外，见段C），不得侵入相邻格的格心上方。
- 相机俯角恒 60°（`BoardView` 注释里有拾取依据），不动。
- 拾取按模型（`BoardView.Picking.cs`）：新加的纯装饰网格必须确认不进拾取路径（和现有装饰一样挂在装饰层 / 不参与拾取的节点下），改完跑 `--pick-check`。
- 八种地表的面砖基色在 `Visuals.cs`，段A 已定，不要再改；要改色只改部件自带的 `TerrainParts.Art*`。
- `BoardView.TerrainShadow.cs` 画的台阶 / 阴影贴着侧面部件的形状，改了 `side_cliff` / `side_slope` 的外形就要回看它。
- 同一张图永远同一副样子：造型只按坐标 / 变体散列，不用随机数（沿用 `LowPolyMesh.Hash / Unit / Signed`）。

## 段B：几何（崖壁 / 水面 / 桥）

目标画面（参考图）：岛缘是层叠的淡紫灰岩壁，一层层外凸的平台式断崖，不是平滑收窄的锥体；水面亮青，岸边一圈浅色泡沫 / 浅滩台阶；桥是暖色木板面配两侧绳栏（立柱 + 绳）。

改动点：
1. `BoardView.AddFloatingIsland`：岛缘墙面从 `Shrink` 的均匀收窄改成 3–4 层带外凸岩沿的阶梯状断崖（每层顶面略外凸、侧面近竖直、层间留浅缝），顶点色按层做明暗，仍用一份材质、一个 `MeshInstance3D`。岛底石笋 / 云海保留。瀑布口与 `WaterDrop` / `WaterSurfaceLift` 的接缝关系不变。
2. 深水：`AddWater` 的水面材质沿用，`bank_lip` 改成浅色泡沫 / 浅滩带（比水面亮、比地砖低），`TerrainParts.BankLipMesh` 重做外形：薄而宽、带不规则内沿；配色走 `ArtIvory` 偏青。
3. 桥：`LowPoly.Bridge`（`TerrainParts.Themed("bridge")` 会重染）加两侧立柱 + 绳栏（细 `LowPolyMesh.Beam`），桥面木板留缝；桥仍锚在 `waterCenter`、桥面与地砖齐平、过桥方向的转向规则不变。
4. `side_cliff` / `side_slope`：外形改为带外凸岩沿的层叠石台（对应参考图高台），顶点色明暗差收敛到段A 定的 0.62 / 0.76 附近；`TerrainShadow` 的台阶随之核对。

验收：截图（近景 + 全局）与参考图同类；`dotnet build` 零警告；`--pick-check` 通过；部件快照重导、无悬空材质。

## 段C：装饰套件 + 中央平台

目标：草丛（青绿簇）、紫水晶簇、火盆（石座 + 暖光焰）、石拱门、宝箱，以及中立平台的圆形符文台面。

改动点：
1. 新部件加进 `TerrainParts.All` 清单（名字、档数、建模函数、锚点），导出后进 `terrain_full_art`。
2. 摆放只在不可落子格（障碍 / 场景格）或可落子格的格角 0.12 以内，绝不占格心；草丛可以散在可落子格的格角，火盆 / 拱门 / 宝箱只在不可落子格。按坐标散列选位与变体，同一张图永远同样。
3. 中立平台（`board.NeutralPlates` / `AddNeutralOutlines` 那条路径）顶面加一圈圆形符文装饰（贴地薄片 + 顶点色），不改平台格数、不改边框。
4. 全部挂在装饰层（`_decoration` / `_batch`，同变体合 MultiMesh），不进拾取；`--pick-check` 通过。

验收：截图里能看见草丛 / 水晶 / 火盆 / 拱门 / 符文台且不遮任何格心；构建、拾取自检通过。
