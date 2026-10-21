# 段 B 实现记录

## 段 B 实施（trellis-implement，2026-10-21，tasks 2.1–2.2）

### 改动

- `src/godot/scripts/LowPolyMesh.cs`：新增 `Merge`（多块网格按各自变换并成一个网格、仍是 1 个 surface，基于 `SurfaceTool.AppendFrom`）与 `Beam`（两点之间的直梁，截面宽 × 高，六面都建）。既有四个函数不动。
- `src/godot/scripts/TerrainParts.cs`：新增常量 `BankLipWidth = 0.06`、部件 `bank_lip`（3 档，锚点"水格边中点（高度取该格地砖上表面）"，内置岩灰 = 崖壁侧面那份 `matte_887c74_r90_vc`），追加在 `All` 末尾；`BankLipMesh(v)`。**`water_bed` 未加**（理由见"偏离 / 待裁决 1"）。
- `src/godot/scripts/LowPoly.cs`：`Fence(alongX)` 与 `Bridge()` 改用 `LowPolyMesh` 拼成单网格（栅栏 1 个 MeshInstance3D "Mesh"；桥 2 个："Deck" 桥面板、"Timber" 深色木料），节点名写死；材质沿用 `Visuals.Timber` / `Visuals.BridgeDeck`、粗糙度 1，打开顶点色（新私有 `Shaded`）。`Piece` / `Body` / `Prism` 未动，`LowPoly.cs` 44 123 字符（≥ 10 000），仍含 `LowPoly.Body`。
- `src/godot/scripts/BoardView.cs`（只动 `Build` 深水分支与一个新的放置函数）：
  - 格循环前按 `board.Cells` 建 `Dictionary<Coord, Surface> surfaceAt`，本地函数 `DeepAt(x, y)` 先判界（`x < 0 || y < 0 || x >= _width || y >= _height` 一律"不是深水"，`Coord` 构造对负数抛异常）再查表；格 → 世界方向由 `BoardGeometry.Center` 求出 `stepX` / `stepY`，不另写坐标换算（`坐标映射在Godot侧唯一` 的 `_width - 1` 正则不命中）。没有调用任何 Core 邻接 API，`Godot层不含规则计算Tests` 的 token 表不涉及。
  - 深水格（含架桥的）：`AddWater` 照旧（水面板、流纹 shader、`WaterDrop`、压暗表登记都不变）；四向逐边查表，邻格不是深水就放一件 `bank_lip`（`TerrainParts.PieceOf`，变体 `TileVariant(coord, 10 + 方向)` 按全图坐标散列），挂在 `tiles` 下（不挂 `_decoration`，免得被 `Refresh` 的装饰缩放带着缩）。新函数 `AddBankLip`：按朝内方向绕 Y 旋转、`CastShadow = Off`、材质用部件自带岩灰（不登记压暗表，与侧面同口径）。沿 X 走向的边（上下两侧）的石沿再压低 0.005：格角上相遇的两件必是一纵一横，顶面不共面、不闪。
  - 桥：仍 `TerrainParts.Create(TerrainParts.Bridge)`，位置不变；网格里过桥方向沿 Z（栏杆沿 Z），按查表"上下邻格是深水的多于左右"时绕 Y 转 90°（河道沿格的 y 向走 → 过桥沿 x 向）。只是朝向，同一部件。
  - 新自证行 `[water] 深水 N 格（架桥 B 格）、河岸石沿 M 件`，紧挨 `[parts]` 之前打印。
  - `AddPlaceholders`、`ObstacleArtAt` 分流、`_decoration` 挂点、`AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);`、`AddFence` / `AddFloatingIsland`（含瀑布）、`Refresh` 均未动。`Visuals.cs` 未动。
- 资源（`src/godot/parts/terrain/`）：新增 `bank_lip_0–2.tscn`、材质 `materials/matte_705436_r100_vc.tres`（木料）、`materials/matte_b0804e_r100_vc.tres`（桥面）；重写 `bridge.tscn`、`fence_x.tscn`、`fence_z.tscn`。其余既有资源导出后用 `git checkout --` 还原（仅自动节点名漂移）。

### 新旧同形（对局中架桥 / 立栅）

预置与改造出的桥 / 栅栏走同一条链：`Refresh` 发现 `LayoutKey` 变了 → `Build` → 深水分支 `TerrainParts.Create(Bridge)` / `AddFence` → `TerrainParts.Create(FenceX|FenceZ)`。没有第二条路径；改造落成反馈（`DrawFlash` 的亮环 / `EditEdgeMark` 亮栏）只是瞬时标记，未动。

### 包围盒对照（改前从 `HEAD` 的 `.tscn` 尺寸 + 变换量出；改后取导出产物里 ArrayMesh 的 `aabb`）

| 部件 | 改前 | 改后 | 说明 |
|---|---|---|---|
| `bridge` | `(−0.45, −0.06, −0.45) + (0.9, 0.19, 0.9)`：桥面 0.9×0.06×0.9 占 y [−0.06, 0]；四角柱 0.07 见方 @ (±0.40, ±0.40)，y [−0.03, 0.13] | Deck `(−0.45, −0.04, −0.45) + (0.9, 0.04, 0.9)`；Timber `(−0.45, −0.06, −0.45) + (0.9, 0.19, 0.9)` | 与改前逐值相同；桥面顶 y = 0，与地砖上表面齐平 |
| `fence_x` | `(−0.48, 0, −0.025) + (0.96, 0.30, 0.05)`：三柱 0.05×0.30×0.05 @ x ∈ {−0.40, 0, 0.40}；两杆 0.96×0.035×0.03 @ y 0.11 / 0.23 | `(−0.48, 0, −0.025) + (0.96, 0.30, 0.05)` | 相同；只占边、不占格 |
| `fence_z` | `(−0.025, 0, −0.48) + (0.05, 0.30, 0.96)` | `(−0.025, 0, −0.48) + (0.05, 0.30, 0.96)` | 相同（基向量直接写 0 / ±1，避免三角函数舍入超出一丝） |
| `bank_lip`（新） | — | `(−0.495, −0.10, 0) + (0.99, 0.085, 0.06)`（三档相同） | 水格边缘 0.06；顶 −0.015（上下两侧再低 0.005），低于两岸地砖上表面与桥面 |

### 造型、档数与三角形数

| 部件 | 档 | 三角形 / 件 | 造型 |
|---|---|---|---|
| `bank_lip` | 3 | 60 | 三块倒角石（倒角 0.012）沿边排开，块间缝 0.008；分块点按散列错开 ±0.06，中间那块顶再低 0.003–0.009、内沿窄 0–0.01（只往低、往窄走）；底 −0.10 没入水面（水面 −0.092），水面与石沿之间不露缝；侧面系数 0.95 |
| `bridge` | 1 | 276（Deck 120 + Timber 156） | 六块横板（各 0.14 宽、板间缝 0.012，倒角 0.006，亮度按散列 0.95–1.0），板下一层深色垫木（y [−0.06, −0.04]，板缝里露出的就是它）；两侧栏杆：四根角墩 0.07 见方从桥底 −0.06 立到 0.13（倒角帽，即"桥墩"），两根中柱 0.04、扶手 0.03×0.025 @ y 0.105，接在角墩内侧 |
| `fence_x` / `fence_z` | 1 / 1 | 240 / 240 | 三根立柱 0.04 见方（亮度按散列 0.95–0.99）+ 倒角柱帽 0.05×0.035（顶 0.30）；两道横杆 0.03×0.035 @ y 0.11 / 0.23，每跨在跨中（±0.20）下垂 0.012，柱外伸出段水平（端点 ±0.48） |

几何约束核对：一格宽河 D8–G8 水面可见宽度 = 1 − 2 × 0.06 = 0.88 格（石沿高出水面约 0.07，60° 俯角下近岸石沿最多再挡 ≈ 0.04 → ≥ 0.84，均 ≥ 0.78；截图量像素在 2.3）；石沿顶（≤ −0.015）低于地砖上表面（0）与桥面（0）；桥面盒 0.9 宽、石沿内沿在 0.44，二者在桥格上重叠 0.01 的带被桥面盖住，不挡桥面；拾取走 `BoardGeometry` 射线–平面，不读网格。

### 验证（同一时间只跑一个 dotnet / Godot 进程）

- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误。
- `dotnet build siege.sln -c Release`：退出 0，0 警告 0 错误。
- `dotnet test`：退出 0，`Siege.Core.Tests` 失败 0、通过 2100、跳过 6（总 2106，8 分 12 秒）；源码扫描守门随之通过。
- 导出 `--export-parts=res://parts/terrain`：退出 0。`[export-parts] 部件 55 / 55、共享材质 22 → res://parts/terrain`；`[export-parts] 程序建模网格（LowPolyMesh）三角形数：fence_x 240、fence_z 240、bridge 276、tile_top_0 20、tile_top_1 20、tile_top_2 20、side_slope_0 96、side_slope_1 96、side_cliff_0 192、side_cliff_1 192、side_cliff_2 192、liner 12、bank_lip_0 60、bank_lip_1 60、bank_lip_2 60`。（共享材质 21 → 22：+ 木料 / 桥面两份 `_vc`，− 旧桥面 `matte_b0804e_r100` 不再被引用。）
- 导出可复现：再导两次到临时目录（各退出 0），按段 A 规则（外部资源 id、子资源 id、节点 `unique_id`、目录前缀、行尾）归一后比对：`files=77 raw_same=22 norm_same=55 diff=0 missing=0`。仓库内资源与新导出比：`bridge` / `fence_x` / `fence_z` / `bank_lip_*` 归一后相同；仍不同的只有段 A 已记的 `rock_1–3`、`ruins_0–2`（自动节点名序号），另 `matte_b0804e_r100.tres` 只在仓库里（孤儿，见偏离 4）。
- `git status src/godot/parts`：`M bridge.tscn`、`M fence_x.tscn`、`M fence_z.tscn`，`?? bank_lip_0–2.tscn`、`?? materials/matte_705436_r100_vc.tres`、`?? materials/matte_b0804e_r100_vc.tres`；其余导出漂移已 `git checkout --` 还原。
- Godot headless（Debug 构建后，顺序单跑）：
  1. `-- --auto-demo`：退出 0。`[plates]`、`[zone]` 用 `grep -xF` 与 0.1 基线逐字相同（3 / 3 行，无其他变体）；`[water] 深水 6 格（架桥 2 格）、河岸石沿 16 件`（D8 E8 G8 F8 K7 K6，手算 3+2+2+3+3+3 = 16，相符）；`[parts] 资源 473–474 件、程序生成 0 件`（段 A 457–458 + 16 件石沿）；`[flow] 自检 5 条，失败 0 条`。
  2. `-- --auto-demo --pick-check --seed=5`：退出 0，214 / 214（含桥格 F8、K6）。
  3. `-- --map-select --auto-demo`：退出 0（`[flow] 自检 5 条，失败 0 条`）。
  4. `-- --auto-demo --rounds=0 --seed=5`：退出 0，`[flow] 自检 7 条，失败 0 条，已见步骤：插旗锁定 → 构筑期 → 全图解禁 → 终局；通过`；全程 `[parts] 程序生成 0 件`。
  5. `-- --auto-demo --pick-check --seed=5 --map=link:7`：退出 0，218 / 218（含桥格 N6、T4）；`[water] 深水 9 格（架桥 2 格）、河岸石沿 22 件`；`[parts] 资源 497 件、程序生成 0 件`。
  6. `-- --auto-demo --map=link:7`：退出 0。`[plates]`（227 格，外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段）、`[zone]`（各 12 段；棋盘内岩石 2 格）用 `grep -xF` 与 0.1 基线逐字相同（4 / 4 行）；`[parts] 资源 494–497 件、程序生成 0 件`。
  - 六份日志 error / exception 0 处。
- 不截图（2.3 由主会话做）。headless 验不到：石沿 / 桥 / 栅栏的面朝向与观感、水面可见宽度像素、桥朝向是否读对，留给 2.3 截图。另请 2.3 看一眼格角：同一水格的两件石沿在格角有约 0.055 见方的体积重叠，东西向那件端头的倒角会斜切到南北向那件顶面上方一条很窄的带（不共面、不闪，应读作石块叠压）；`link:1` 只在 D8、G8 两端的格角出现。
- 全仓 `git status --short`：只有上述 4 个脚本、3 个重写 `.tscn`、5 个新文件（`bank_lip_0–2.tscn` 与两份 `_vc.tres`）、`tasks.md`、本记录；无 `.godot/`、`.import`、`art/` 漂移。
- `tasks.md` 2.1 的勾以偏离 1（`water_bed` 未加）的裁决为准，视为有条件完成。

### 偏离 / 待裁决

1. **`water_bed` 未加**：现有水体是不透明 `Matte` 盒，铺满整格（1.0 × 0.12 × 1.0），顶面在地砖上表面 −0.092；底座顶在 −0.10，流纹透明层在 −0.088。水体之下的任何东西从任何角度都看不见；河道两壁在水面以上的部分是本段的 `bank_lip` 内侧面（以及邻格的 `liner` 边与内缩 0.05 的 `tile_top` 侧面）。所以 `water_bed`（"水格上表面中心"锚点、整格一件）没有可见面，不进 `TerrainParts.All`、不留 0 件放置的空部件。"水下暗沿"若做成整格一件，会在相邻两格水之间也画出暗边、把连成一片的河切成一格格；做成逐边一件则与 `bank_lip` 重复，且暗带若被算作非水面，一格宽河近岸处可见宽度会贴近 0.78。建议：负责人看 2.3 截图后裁决是否要"水下暗沿"，要的话另开一件逐边部件。
2. 桥按河道走向绕 Y 转 90°（`link:1` F8 不转、K6 转；`link:7` N6、T4 视查表）：栏杆沿过桥方向，若不转，一半的桥栏杆会横在通行方向上。是渲染朝向，部件不变。
3. 桥墩留在原包围盒内（角墩从桥底 −0.06 立到 0.13），没有垂到水面（−0.092）：严格按"外形不超出现有包围盒"。若负责人想要露出水面以上的墩身，需放宽 y 下界约 0.04（没入水面以下）。
4. `materials/matte_b0804e_r100.tres`（旧桥面材质）改后已无 `.tscn` 引用，成了孤儿；本段不删，留给 5.1 全量重导时清理。`matte_705436_r100.tres` 仍被 `pines_*` / `trees_*` 引用。
5. 石沿 `CastShadow = Off`：石沿只有 0.085 高、贴着水面，影子几乎看不到，关掉省阴影通道的绘制调用（D6 1.2 倍预算）。
