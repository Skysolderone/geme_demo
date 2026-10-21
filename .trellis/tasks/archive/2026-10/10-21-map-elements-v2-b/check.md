# 段 B 质检（trellis-check，2026-10-21，tasks 2.1–2.2）

## 结论：通过（2.1 的 `water_bed` 待负责人裁决）

自修：无。没有发现必须改的问题；动 `LowPolyMesh` / `TerrainParts` / `LowPoly` 网格函数会让仓库里的 `.tscn` 与代码对不上（需要重导、重做归一比对），所以小瑕疵只记录、不改。

## 逐项核对

1. **邻格查表**：`surfaceAt` 由 `board.Cells` 建；`DeepAt` 先判界再 `TryGetValue`，越界或不在视图里都算"不是深水"。新增行里 `Godot层不含规则计算Tests` 的禁用 token、`(_width|_height) - 1`、`RoundToInt` 都没有出现。方向只经 `BoardGeometry.Center` 推出：`stepY = (0,0,−1)`；边 (0,+1) 的 inward 为 (0,0,+0.5)，转角 0；边 (+1,0) 的 inward 为 (−0.5,0,0)，转角 −π/2，网格 +Z 转到 −X，Z 符号正确。
2. **几何**：石沿 aabb `(−0.495,−0.10,0)+(0.99,0.085,0.06)`，宽 0.06，顶 −0.015（上下两侧再低 0.005），低于地砖面和桥面（0）。桥 Deck / Timber、fence_x / fence_z 的 aabb 与改前一致（`.tscn` 里实测）。栏杆 x 在 0.40–0.43，横杆顶 0.2475，都小于 0.30；六块板 0.14 宽，末板外沿 0.45。`AddWater`、流纹、`WaterDrop`、瀑布和压暗表登记没动（BoardView 只有新增行）。链接图上的深水全是 h0：River 主题整块不设高度；Mixed 先放台，再在 `Free` 格放深水；Plateau 不放水。所以石沿顶不会高出邻格。`terrain-barriers` 合入后如果出现高层深水，要重核这一条。
3. **确定性**：只用 `Hash` / `Unit` / `Signed` / `TileVariant`，没有 `Random`、`GetHashCode`、时间。
4. **绕序与法线**：`Tri` 按 outward 自动定绕序。`Beam` 四个侧面和两端面的 outward 正确，底面是 k==2。栅栏的 `turn = Basis((0,0,−1),Up,(1,0,0))` 是列构造，行列式 +1，纯旋转不镜像。Godot 4 的 `SurfaceTool.AppendFrom` 用 basis 变换法线，在只有旋转加平移时正确。桥的 90° 是节点旋转，由渲染器处理。观感留给 2.3 截图。
5. **守门**：`AddPlaceholders` 里 `TerrainParts.` / `AddTileStack(` / `lineWidth` 都是 0 处；`AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);` 有 1 处；`ObstacleArtAt`、`_decoration` 没动；`LowPoly.cs` 44 249 字符，`Body` / `Piece` / `Prism` 没改；Core / Presentation / Sim / tests / `Visuals.cs` 都没有 diff。
6. **新旧同形**：`DefaultBoardView.LayoutKey` 含每格的 Surface、HasBridge 和栅栏边集，对局中架桥或立栅会触发 `Build`，走的是同一部件、同一朝向规则。
7. **风格**：与段 A 一致（节点名写死、单 surface、材质用 `_vc`），没有死代码。
8. **复跑**（顺序单进程）：
   - `dotnet build src/godot/Siege.Godot.csproj --no-incremental`：0 警告 0 错误。
   - `dotnet test siege.sln`：失败 0、通过 2100、跳过 6。
   - `--auto-demo`：退出 0；`[plates]`、`[zone]` 与 0.1 基线逐字相同；`[water] 深水 6 格（架桥 2 格）、河岸石沿 16 件`，与手算 3+2+2+3+3+3 相符；`[parts] 资源 473–474 件、程序生成 0 件`；`[flow]` 5 条，失败 0。
   - `--auto-demo --pick-check --seed=5 --map=link:7`：退出 0；`[plates]`（227 格，10 / 4 / 0）、`[zone]`（各 12 段，岩石 2 格）与基线相同；`[water] 深水 9 格（架桥 2 格）、河岸石沿 22 件`，与手算 3+2+2+2+2+2+3+3+3 相符；`[parts]` 资源 497 件、程序生成 0 件；pick-check 218 / 218。
   - 两份日志 error / exception 0 处；`git status` 没有 `.godot/`、`.import` 漂移。

## 实施者报告的偏离（只判断真假与副作用，不裁决）

1. **`water_bed` 未加**：说法属实。`Visuals.Matte` 的第二个参数是粗糙度，水体是不透明盒、铺满整格，顶在 −0.092，水下的东西看不见。不过规格写的是"呈现为…河床与河岸石沿"，tasks 2.1 也点了 `water_bed`，现在 2.1 已经勾上，这一项要等负责人裁决。
2. **桥按河道转 90°**：属实，只是节点旋转、同一部件，包围盒是对称的，不受影响。F8、T4 不转，K6、N6 转，朝向都对。副作用：K6 / N6 的板缝方向和改前不同（改前一律沿 X），这是有意为之。
3. **桥墩没垂到水面**：属实。改前的角柱 y 在 [−0.03, 0.13]，同样没到水面，不算变差。
4. **旧桥面材质成了孤儿**：属实，`matte_b0804e_r100.tres` 已经没有 `.tscn` 引用；`matte_705436_r100.tres` 仍被 pines 引用。没有副作用。
5. **石沿关投影**：没有守门涉及；石沿很矮、贴着水面，观感由 2.3 确认。

## 遗留（不阻塞）

- tasks.md 2.1 的勾要等负责人对 `water_bed` 的裁决。
- `Beam` 遇到竖直输入会返回空网格，`Merge` 再对它 `AppendFrom(...,0,...)` 会出错。现有调用都是水平的，碰不到。
- 横杆各跨相接处有背对背的内部端面，白占三角形（240 个三角形之内），看不见。
- L 形河道的内角：对角那格是陆地时，格角会留约 0.06 见方的水面没有石沿（规则只看四邻）。2.3 截图时可以看一眼。
- 格角两件石沿的叠压（实施者已报）留给 2.3 确认。
