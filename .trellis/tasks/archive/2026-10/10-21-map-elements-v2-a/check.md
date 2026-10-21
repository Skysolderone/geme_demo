# 段 A 质检（trellis-check，2026-10-21，tasks 1.1–1.3）

结论：**通过**（代码审查无阻塞项；复跑全部达标）。未自修代码。

## 核对

1. 几何逐值不变：`TopYOf(l) = 0.06 + 0.35l`。侧面锚在 `(top.X, bandTop, top.Z)`、`Strata` 占 y ∈ [−0.35, 0]，等于改前盒子 [bandBottom, bandTop]（第 1 层 [−0.06, 0.29]、第 2 层 [0.29, 0.64]）；衬底 `BeveledSlab(top = 0.015)` 占 [−0.015, 0.015]、锚 `top − 0.105`，即 [top − 0.12, top − 0.09]，与改前相同；顶板占 [−0.12, 0]、锚 `top`，0.9 见方，与改前相同。缓坡外沿只向内凹；崖壁岩沿与格边齐平，岩体内缩，均不超出格边。仓库内 9 个新 `.tscn` 的 AABB 与改前盒子相同。拾取走 `BoardGeometry` 射线–平面，不读网格。
2. 确定性：抖动 / 色差只来自 `LowPolyMesh.Hash / Unit / Signed`（整数位混合）；`TileVariant` 是整数异或后散列；四个文件里没有 `Random` / 时钟 / `GetHashCode`。`_tileMeshes`（HashSet）只用于计数，不影响产物。
3. 材质：`SurfaceOf` 仍按底色共用并登记 `_tileMaterials`，`Refresh` 改 `AlbedoColor`；打开 VC 后是乘法，压暗、出生区淡染（改 `color`）、明暗棋盘格（`Darkened(0.05)`）都在 `SurfaceOf(color)` 之前，仍然生效。`LinerOf` 不登记压暗表，与改前一致。
4. 绕序 / 法线：`Tri` 用 `(b−a)×(c−a)·outward` 定号，`>0`（从外侧看是逆时针）时改发 a,c,b，符合 Godot 正面顺时针的约定，与 `LowPoly.PrismFace` 一致；法线由叉积重算，并朝 hint 一侧。逐函数核过 hint：`BeveledSlab` 的 `faces[k]` 与 `inner[k]→inner[k+1]` 所在边对应（Forward/Right/Back/Left）；`Strata` 的 `outward[face]` 与 `corners` 顺序对应，崖壁岩沿底面（朝外下）与坡脚斜面（朝外上）与水平 hint 的点积都为正；`FacetRock` 以面心作 hint，凸形下成立。解码 `tile_top_0` 首个三角面：叉积朝下，从上方看是顺时针，正面朝上。**没有发现会朝内的面**。边界：`dot == 0` 时走顺时针分支，现有调用方触发不到。`Ribbon(upright: true)` 是单面，段 C 用作芦苇 / 立条时背面会被剔除，需要双面时要另行处理（段 A 未调用）。
5. 守门与边界：`AddPlaceholders` 内 `TerrainParts.` / `AddTileStack(` / `lineWidth` 0 命中；`AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);` 1 处；`ObstacleArtAt` 分流与 `_decoration.AddChild` 挂点保留；`LowPoly.cs` 39786 字符、含 `Body`、未改动；Core / Presentation / Sim / tests 无改动；新文件只 `using Godot`，没有 Core 邻接 API。
6. 风格：中文 XML 注释，密度与周边一致；`FacetRock` / `Ribbon` 段 A 未被调用，但 1.1 要求有，不算死代码；`Kind.Anchor` 只作文档字段，符合 1.2"加锚点说明"。`SlopeMaterial` / `CliffMaterial`（`SideFallback`）只在资源不带材质时兜底，现有两条路径都带材质，留作防御，可以接受。`AddTileStack` 的 `<summary>` 挂在 `SlopeMaterial` 字段上是沿袭的旧写法，不算回归。

## 复跑（同一时间只跑一个进程）

- `dotnet build src/godot/Siege.Godot.csproj --no-incremental`（Debug）：0 警告 0 错误。
- `dotnet test`：失败 0、通过 2100、跳过 6（总 2106，5 分 57 秒）。
- Godot `-- --auto-demo`：退出 0；`[plates]` / `[zone]` 两行用 `grep -xF` 与 0.1 基线逐字相同；`[parts] 资源 457 / 458 件、程序生成 0 件`；`[batch] … 盒网格 15 份、地块部件网格 7 份`。
- Godot `-- --auto-demo --pick-check --seed=5 --map=link:7`：退出 0；`[plates]` 与基线逐字相同（`grep -xF`）；`[zone] … 各 12 段；棋盘内岩石 2 格（按棋盘格画）` 与基线一致；`[parts] 资源 475 件、程序生成 0 件`；拾取 218 / 218 通过。
- 两份日志 error / exception 0 处。

## 三处偏离（只判是否属实）

1. 顶点色 > 1 被截断：**属实**。`.tscn` 的 `attribute_data` 每顶点 4 字节 RGBA8（例如 `f6f6f6ff` ≈ 0.965）。解码实测：`tile_top` 0.937–0.996，顶面与倒角均值约 0.964，三档都没有触顶；`side_cliff` 最低 0.918（0.92 量化后），`liner` 恒 1。补充（推断，未截图验证）：Godot 4 的 `vertex_color_is_srgb` 缺省为假，顶点色按线性值乘进线性底色，所以显示亮度的变化比系数本身小，0.98 大约只暗 1%。"整体暗约 2%"偏高估。
2. 导出不能逐字节相同：**部分属实**。新 `.tscn` 里确有 `ext_resource id="1_66e7y"` 和节点 `unique_id=…`，形如随机值；`ArrayMesh` 子资源 id 已固定为"部件名_序号"。"不可由脚本指定"这一点没有验证：headless 用的是编辑器二进制，`Resource.SetIdForPath` 能否在这种运行方式下钉住外部资源 id，可以再试，不阻塞。本次没有重跑导出比对。
3. 崖壁格角内缩 0.02：**属实**。`lip * 0.4 = 0.02`（段点 `lip * 0.7 = 0.035`，加散列 ≤ 0.02，岩缝再加 0.05）。补充：内缩也作用在相邻两格崖壁相对的内侧面上，所以两格之间是一条通长的内腔，宽 0.04（格角）到约 0.2（岩缝处），上面由岩沿、下面由坡脚收口。从外面看，只露出格角那道 0.04 的竖缝，缝里能看到的是正面朝缝的崖壁内侧面，不会透到虚空。是否读成"立柱"，要看 1.4 截图。

## 遗留 / 备注（不阻塞）

- `tasks.md` 1.1 已标完成，但它的验证判据"两次导出逐字节相同"字面上不成立（只在归一化随机 id 后相同），所以这个勾要看偏离 2 怎么裁决，裁决前应视为有条件完成。
- `SurfaceOf` 打开 VC 后，`AddSceneSlabs` 场景面砖的 MultiMesh 也会用到这份材质。BoxMesh 没有 COLOR 属性时 Godot 按白色处理，画面应当不变，但链接图上场景面砖是 0 格，验证不到。
- 面朝向 / 发黑 / 顶点色观感只能在 1.4 截图里确认。
- `PieceOf` 中，资源根节点不是 `Node3D` 时实例不会被释放（仅资源被换成非 Node3D 根时触发，现有资源不涉及）。
