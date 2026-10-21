# 段 D 实现记录

## 段 D 实施（trellis-implement，2026-10-21，tasks 4.1–4.3）

### 改动

- `src/godot/scripts/LowPolyMesh.cs`：新增 `Loft(rings, shades, capTop, capBottom)`：若干圈点数相同的环自上而下连成四边面，某圈收成一点即成尖（退化三角面照旧跳过），可选扇形封顶 / 封底；面朝向按"从环中轴朝外"判定。与段 B / C 的 `Merge` / `Beam` / `Frustum` / `Patch` 同属 D1 表外的工具函数，D1 四个函数语义不变。
- `src/godot/scripts/TerrainParts.cs`：新增 7 类部件（追加在 `All` 末尾，既有导出顺序不变）：`rim`（3）、`zone_strip`（1）、`plate_frame`（2）、`plate_corner`（1，D2 表外，见偏离 1）、`island_layer`（3）、`island_spike`（4）、`cloud`（3）；常量 `RimLength` / `RimDepth` / `RimOverhang` / `RimRise`；建模函数 `RimMesh` / `ZoneStripMesh` / `PlateFrameMesh` / `PlateCornerMesh` / `IslandLayerMesh` / `IslandSpikeMesh` / `CloudMesh`，材质辅助 `IslandColor` / `VertexMaterial` / `CloudMaterial`。全部经 `Single`（根下一个名为 `Mesh` 的 `MeshInstance3D`、单位变换）+ `PieceOf` 取用，计数按放置件数。
- `src/godot/scripts/PartExport.cs`：材质去重键对不受光材质改前缀 `flat_`、半透明的再带 `_a<α 百分比>`（只有云团用到：`flat_f0f7ff_r100_vc_a55.tres`）；哑光材质的键与文件名不变（导出后既有 35 份材质归一比对全同）。
- `src/godot/scripts/BoardView.cs`：
  - `Build`：底座盒子保留，其后调 `AddRim`；出生区描边那一处 `tiles.AddChild(...)` 改为 `zone_strip`（`IsZoneEdge`、计数与 `[zone]` 行不动）；`[water]` 行后多一行 `[rim] 底座外圈石沿 N 件`；`[batch]` 第二行"描边 / 边框材质"= `_flatMaterials + _inlayMaterials`（这一行不在逐字清单里）。
  - 新增 `AddRim`、`Facing`（"沿 X、朝 +Z 展开"的部件转到朝某水平方向的基，直接写 0 / ±1 基向量，不经三角函数）、`InlayOf`（不受光 + 打开顶点色的嵌条材质，同色一份；`FlatOf` 不动，占位边框与单块分支仍用它），都放在 `AddBankLip` 旁，不在 `AddPlaceholders(` → `WaterDrop` 的扫描区内。
  - `AddBoardPlates` 连体外轮廓分支：非接缝段改为逐格边摆 `plate_frame` + 段端 `plate_corner`（局部函数 `FramePiece` / `CornerPiece`），接缝格线、内部格线、单块分支四条 `Strip` 原样；`[plates]` 后多一行 `[frame] 连体外轮廓边框件 N 件、转角件 K 件（按 S 段逐格边摆放）`。
  - `AddFloatingIsland`：岩层 / 垂岩 / 云团改取部件；散列公式（层表、`h` / `t` / `length` / `alongX` / `side` / `i * 37°`、云的 `angle` / `radius` / `size` / `i % 4`）一个没改；**瀑布段（`const float drop = 9f;` 起到方法末）一字未动**。
  - 未动：`AddPlaceholders`（扫描区内无 `TerrainParts.` / `AddTileStack(` / `lineWidth`，脚本核过）、`BuildCoordinateLabels` / `Label`、`BoardGeometry.cs`、`Visuals.cs`、`LowPoly.cs`、`ObstacleArtAt` 分流与 `_decoration` 挂点、`AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);` 字面、段 A–C 的部件；Core / Presentation / Sim 未动。
- 资源（`src/godot/parts/terrain/`）新增 17 件 `.tscn`：`rim_0–2`、`zone_strip`、`plate_frame_0–1`、`plate_corner`、`island_layer_0–2`、`island_spike_0–3`、`cloud_0–2`；新增 4 份材质：`matte_8b633f_r100_vc`（土层 = `SlopeSide.Darkened(0.12)`）、`matte_7a7068_r100_vc`（岩层 = `CliffSide.Darkened(0.10)`）、`matte_5f5751_r100_vc`（深岩 = `CliffSide.Darkened(0.30)`）、`flat_f0f7ff_r100_vc_a55`（云团）。`rim` 复用 `matte_887c74_r90_vc`（与崖壁侧面、河岸石沿同一份），`zone_strip` / `plate_frame` / `plate_corner` 导出时带既有中性白 `matte_ffffff_r90_vc`、加载后由 BoardView 覆盖。

### 部件与摆法

| 部件 | 档 | 锚点 | 尺寸（网格局部） | 三角形 / 件 | 摆法 |
|---|---|---|---|---|---|
| `rim` | 3 | 段中点：底座外缘线上、高度取底座顶面（`TopY − WaterDrop` = −0.04） | x ∈ ±0.475（件长 0.95）、z ∈ [−0.02, 0.28]（外凸 0.02、进深 0.28）、y ∈ [−0.14, ≤ 0.08]；第 v 档 v + 1 块倒角石（倒角 0.03，块间缝 0.012，各块顶按散列再低 0–0.012） | 20 / 40 / 60 | 南北两边贯通全长（含转角，长 = 底座边长 + 2 × 0.02），东西两边只排两条南北石沿之间的内段（长 = 底座边长 − 2 × 0.28）；每边 n = round(边长) 件、节距 = 边长 / n（≈ 1 ± 3%），件长不缩放，余量读作石块间的缝。朝向用 `Facing(朝内)`。变体 = `Hash(边号 + 1, 段序) % 3`。投影照常（默认） |
| `zone_strip` | 1 | 格边中点（高度取该格地砖上表面） | x ∈ ±0.5、y ∈ [0, 0.03]、z ∈ [0, 0.07]——与改前描边盒逐值同包围盒；顶边倒角 0.008，侧面亮度 0.92 | 20 | 每条描边边一件，位置 `center + (dx, 0, −dy) × 0.5`、朝向 `Facing((−dx, 0, dy))`（朝格内）；材质 `InlayOf(归属色)`（`Visuals.Flat` + 顶点色）、`CastShadow = Off` |
| `plate_frame` | 2 | 段中点 → 实际逐格边一件：格边中点，高度取 h=0 地砖上表面（与改前外轮廓边框同用 `TopYOf(0)`） | x ∈ ±0.5、y ∈ [0, 0.065]、z ∈ ±0.065（高 0.065 ≤ 0.07、宽 0.13 ≤ 0.14）；顶边倒角 0.015，顶面四个三角面 ±2% 色差（两档种子不同），侧面 0.92 | 20 | 每段（`Runs` 的一段）按格逐个摆；竖边用 `Facing(+X)` 转 90°。变体 = `TileVariant(格, 20–23)`（按边的朝向加盐）。材质 `InlayOf(Visuals.PublicBoardFrame)`，不投影 |
| `plate_corner` | 1 | 外轮廓段端点（h=0 地砖上表面） | x、z ∈ ±0.07、y ∈ [0, 0.07]，倒角 0.02 | 20 | 每段两端各一件，按端点去重（相邻两段在同一角相接只放一件）。比边框件高 0.005、宽 0.01，相交处不共面 |
| `island_layer` | 3 | 底座中心（单位块顶面中心） | 顶面 1 × 1、高 1：四角切 0.035 的八边形外廓（长边各 4 段），自上而下 4 圈 3 条色带（1.0 / ≈0.97 / ≈0.95），底圈收到 0.92 倍，中间两圈的段点按散列再向内凹 ≤ 0.012；顶面不建、底面封上 | 140 | 5 层按 0 / 1 / 1 / 2 / 2 取档（三档 = 土 / 岩 / 深岩三种内置岩色），`Scale = (画幅 X × 收缩, 层厚, 画幅 Z × 收缩)`，位置 = 改前盒子的顶面中心 |
| `island_spike` | 4 | 尖顶（单位长倒锥的尖端） | 顶圈 y = 1、半径 1；中段两圈 y 0.62 / 0.30、半径 0.58 / 0.27，散列歪斜 ±0.08、径向抖 ±12%；尖在原点；0、2 档五棱，1、3 档六棱；顶面封上 | 30 / 36 / 30 / 36 | 尖放在改前圆锥底端（`baseTop − 0.9 − 长`），`Scale = (0.55 + 0.12 × 长, 长, 同左)`，顶面仍贴土层底面、顶半径与改前同；`i % 3 == 0` 取 0 档（深岩，内置），其余取 `1 + Hash(37, i) % 3`（岩灰，内置）。转角 `i × 37°` 不变 |
| `cloud` | 3 | 云心 | `FacetRock` 半径 1、拉伸 y 0.26–0.29（改前扁球全高 0.55 × 半径 → 半高 0.275），x / z 各档 0.92–1.08；经线 8 / 9 / 7 | 48 / 54 / 28 | 档 = `i % 3`（与改前三档大小同序），`Scale = size` 等比；新加自转 `i × 53°`；材质内置 `Visuals.Flat((0.94, 0.97, 1, 0.55))` + 顶点色（顶点 α 恒 1，乘出来仍 0.55），不受光、半透明、`CastShadow = Off` |

- 本段 17 件部件三角形：rim 120、zone_strip 20、plate_frame 40、plate_corner 20、island_layer 420、island_spike 132、cloud 130，合计 882。导出包围盒（只读脚本逐顶点统计，`winding_mismatch` 全 0）：`rim_*` x ±0.475、y [−0.14, 0.071–0.078]、z [−0.02, 0.28]；`zone_strip` x ±0.5、y [0, 0.03]、z [0, 0.07]；`plate_frame_*` x ±0.5、y [0, 0.065]、z ±0.065；`plate_corner` ±0.07 × [0, 0.07]；`island_layer_*` x / z ±0.5、y [−1, 0]；`island_spike_*` y [0, 1]、水平 ≤ 1.07；`cloud_*` 水平 ≤ 1.08、y [−0.27, 0.30]。
- **标注不被石沿压 / 挡（4.1）**：底座伸出格外缘 `FarLabelMargin + 0.45` = 2.15；坐标标注锚点离边缘格格心 1.2（近边、左右）/ 1.7（远边），即离格外缘 0.7 / 1.2。石沿只占离格外缘 [1.87, 2.17] 这一带（外缘那 0.45 格的边里），石沿顶 ≤ 底座顶 + 0.08 = 世界 y 0.04（实测网格顶 ≤ 0.078 → ≤ 0.038），比标注所在的 h=0 平面（y = `TopY` = 0.06）低 0.02 以上：远边石沿在标注身后，近边石沿比标注平面低、挡不到身后的标注。`BoardGeometry.cs`、`BuildCoordinateLabels`、`Label` 一字未动，标注的字形、字号、位置与锚点算法不变。
- **边框 / 亮条不遮挡边缘格（4.2）**：亮条与改前描边盒同包围盒（格内侧 0.07、高 0.03）；外轮廓边框件与转角件的并集落在改前"段长 + 0.14"的边框盒之内（高 ≤ 0.07、骑线各 0.07），所以落点、棋子底座与标记的可见范围与改前相同。
- **非均匀缩放（4.3）**：岩层与垂岩靠节点 `Scale` 做非均匀缩放；Godot 4 对带非均匀缩放的实例用模型矩阵的逆转置（`INSTANCE_FLAGS_NON_UNIFORM_SCALE` 分支）变换法线，平直着色的法线仍正确。垂岩 X / Z 等比，与绕 Y 的旋转可交换。云团等比缩放。倒角在浮岛部件上不存在（岩层外廓的切角随画幅放大，读作岩体的切面）。
- 确定性：变体与形状只来自 `LowPolyMesh.Hash` / `Unit` / `Signed`、`TileVariant` 与段序号，没有 `Random` / `GetHashCode` / 时间。

### 绘制调用

- 估算：本段把各自独立的网格换成"种类 × 档数"份共用网格——改前浮岛 5 个岩层盒 + 约 21 个各不相同的垂岩 `CylinderMesh` + 26 个各不相同的云 `SphereMesh`（每个都是独立网格，合并不了），连体外轮廓 8–10 个不同尺寸的边框盒，描边 2 种盒 × 归属色；改后浮岛 3 + 4 + 3 份网格、边框 2 + 1 份、亮条 1 份、石沿 3 份。按段 A 的实测（同网格同材质的节点会被合并），新增的石沿 84 件、边框件 68–84 件 + 转角 8–10 件只多出个位数的调用，浮岛与边框净减。没有上 MultiMesh。
- 读数（窗口模式，`-- --map=link:<N> --auto-demo --seed=5 --overview --rounds=1 --screenshot=<scratchpad>:12 --perf-sample=4`，截图只落 scratchpad、未读回）：

| 图 | 0.1 基线 | 段 C 后 | 段 D 后 | 上限（1.2 × 基线） |
|---|---|---|---|---|
| `link:1` | 729 | 608 | **575**（对象 1938、图元 45591） | 874 |
| `link:7` | 710 | 615 | **578**（对象 1991、图元 48491） | 852 |

- `link:1` 那次跑完 `[perf-steady]` 后自退（退出 0，`绘制调用 575 … 平均 60.2 帧/秒`）；`link:7` 打出 `[perf]` 行、截图落盘后停在 `[perf-steady]` 冻结阶段，300 秒 `timeout` 收掉（退出 124；窗口在后台、帧率 1）。不是自检命令，读数已取到。

### 验证（同一时间只跑一个 dotnet / Godot 进程）

- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：退出 0，0 警告 0 错误（改码后、导出前；收尾再跑一次同样 0 / 0）。
- 导出：`--export-parts=res://parts/terrain` 退出 0，`[export-parts] 部件 75 / 75、共享材质 27 → res://parts/terrain`（58 → 75：+17 件；23 → 27：+4 份）。三角形行新增：`rim_0 20、rim_1 40、rim_2 60、zone_strip 20、plate_frame_0 20、plate_frame_1 20、plate_corner 20、island_layer_0 140、island_layer_1 140、island_layer_2 140、island_spike_0 30、island_spike_1 36、island_spike_2 30、island_spike_3 36、cloud_0 48、cloud_1 54、cloud_2 28`；既有部件的三角形数与段 C 记录逐个相同。
- 导出漂移：导出后用 `git archive HEAD` 取仓库原件，`norm_cmp.py` 比对：既有 93 个文件里 82 个归一后相同，不同的只有 `pines_0–6`、`ruins_0–3`（自动节点名序号，段 C 同款）；其余 `M` 都只差随机 id / 行尾。`git checkout -- src/godot/parts/terrain` 还原全部既有文件，只留 21 个新增文件（17 `.tscn` + 4 `.tres`）。
- 可复现：再导两次到 scratchpad 的 d1 / d2（各退出 0，各 `部件 75 / 75、共享材质 27`），`python -I norm_cmp.py d1 d1 d2 d2`：`files=102 raw_same=27 norm_same=102 diff=0 missing=0`。d1 vs 仓库：新增 21 件归一后全同；仍不同的只有 `pines_*` / `ruins_*`（同上）与仓库里段 C 留下的 12 份孤儿材质（B4 / 段 C 偏离 2，留给 5.1）。
- Godot headless（Debug 构建后，顺序单跑；六份日志 error / exception 0 处）：
  1. `-- --auto-demo`：退出 0。`[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`、`[zone] 出生区描边：出生区 1 12 段、出生区 2 12 段、出生区 3 12 段、出生区 4 12 段、出生区 5 12 段；棋盘内岩石 0 格（按棋盘格画）` 与 0.1 基线 `diff` 无输出；`[parts] 资源 745–746 件、程序生成 0 件`（插旗阶段 136 件 = 石沿 84 + 浮岛 5 + 垂岩 21 + 云 26）；`[rim] 底座外圈石沿 84 件`；`[frame] 连体外轮廓边框件 68 件、转角件 8 件（按 8 段逐格边摆放）`；`[water] 深水 6 格（架桥 2 格）、河岸石沿 16 件`；`[flow] 自检 5 条，失败 0 条`。
  2. `-- --auto-demo --pick-check --seed=5`：退出 0，`可落子格 214，至少在一个位姿下验到 214；通过`（"最近"75 / 75、"最远"214 / 214，失败 0）。
  3. `-- --map-select --auto-demo`：退出 0，`[flow] 自检 5 条，失败 0 条`；全程 `程序生成 0 件`。
  4. `-- --auto-demo --rounds=0 --seed=5`：退出 0，`[flow] 自检 7 条，失败 0 条，已见步骤：插旗锁定 → 构筑期 → 全图解禁 → 终局；通过`；全程 `程序生成 0 件`。
  5. `-- --auto-demo --pick-check --seed=5 --map=link:7`：退出 0，`可落子格 218，至少在一个位姿下验到 218；通过`；`[parts] 资源 787 件、程序生成 0 件`。
  6. `-- --auto-demo --map=link:7`：退出 0。`[plates] 连体棋盘（出生区 1+2+3+4+5，227 格）：外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段`、`[zone] … 各 12 段；棋盘内岩石 2 格（按棋盘格画）` 与 0.1 基线 `diff` 无输出；`[parts] 资源 784–787 件、程序生成 0 件`；`[rim] 84 件`；`[frame] 边框件 84 件、转角件 10 件（按 10 段逐格边摆放）`；`[water] 深水 9 格（架桥 2 格）、河岸石沿 22 件`。
  - 退路抽查：把本段 17 个新 `.tscn` 临时挪到 scratchpad 后跑 `-- --auto-demo`：退出 0，`[parts] 资源 473–474 件、程序生成 272 件`（= 石沿 84 + 亮条 60 + 边框件 68 + 转角 8 + 浮岛 52；插旗阶段 `资源 0 件、程序生成 136 件`），`[zone]` / `[plates]` 不变；随后挪回（`.tscn` 共 75 件）。
- 源码扫描自核（脚本）：`AddPlaceholders(` → `private const float WaterDrop` 区间内无 `AddTileStack(` / `lineWidth` / `TerrainParts.` / `DrawPieces(` / `BuildPiece(`；`AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);` 仍在；`BoardGeometry.cs` / `Visuals.cs` / `LowPoly.cs` / Core / Presentation 无 diff。
- `dotnet build siege.sln -c Release`：退出 0，0 警告 0 错误。
- `dotnet test siege.sln`：退出 0，`Siege.Core.Tests` 失败 0、通过 2100、跳过 6（总 2106，12 分 11 秒；同时段另一 worktree 也在跑测试，耗时偏长）；源码扫描守门（`占位轮廓灰度Tests`、`双字母列标不重叠Tests`、`棋盘坐标标注Tests`、`禁手标记的远近可辨Tests`、`六种棋子的轮廓语言Tests`、`棋盘台面的可视表现Tests`、`Godot层不含规则计算Tests`）随之通过。
- 不做正式截图（主会话做）。headless 验不到：石沿 / 亮条 / 边框 / 浮岛的观感（面朝向、顶点色、半透明云的叠加），留给段 D 截图。

### 偏离 / 待裁决

1. **新增 `plate_corner`（1 档，D2 表外）**：外轮廓段长不一，`plate_frame` 按格边逐件摆（件长 1、不缩放），段端改前边框盒外伸的 0.07 由一件 0.14 见方、高 0.07 的转角件补齐；转角件比边框件略高略宽（0.07 / 0.14 对 0.065 / 0.13），相交处没有共面。备选：边框件做成 1.14 长、相邻重叠 0.14（不需要转角件，但重叠处顶面共面，两档顶面色差会闪）。
2. **`plate_frame` 的锚点**：design 写"段中点"，实际是逐格边一件、锚在格边中点（`Kind.Anchor` 字串已写明）；尺寸 0.065 × 0.13，略小于上限 0.07 × 0.14。
3. **`island_layer` 三档 = 三种岩色**（土 / 岩 / 深岩，材质内置），不是三种外形；5 层按 0 / 1 / 1 / 2 / 2 取档，暗度递进与改前三份材质同色（多了 0.95–1.0 的顶点色色带）。**`island_spike` 0 档专作深岩**、只有一种外形；`i % 3 == 0` 的深岩占比与改前相同，其余三档岩灰。
4. **岩层外形**：改前是直壁盒，改后每层向下收到 0.92 倍、四角小切角、侧面三条色带与微凹凸；下一层的顶仍窄于上一层的底（0.914 / 0.80 / 0.67 / 0.46 < 0.92），层次关系不变。垂岩尖端半径 0（改前 0.02），中段微歪。
5. **云团**：加了 `i × 53°` 的自转（扁球改成多面团后才有朝向）；面上有 0.94–1 的顶点色差，仍不受光、半透明、不投影。
6. **`PartExport` 材质键**：不受光 / 半透明材质加 `flat_` 前缀与 `_a<α>` 后缀；哑光材质键与文件名不变。
7. **日志**：新增 `[rim]`、`[frame]` 两行；`[batch]` 第二行"描边 / 边框材质"现在是 `_flatMaterials + _inlayMaterials`（数值 6 份不变）；`FlatOf` 链接图上只剩占位边框在用。
8. **看图旋钮**：`plate_frame` 的顶面色差 `facetJitter 0.04` 在不受光材质上会显出逐格的斜向明暗差；若截图里边框读成"花"，改 0 即可（两档随之只差种子）。石沿用的是崖壁 / 河岸同一份岩灰（比底座 `IslandRim` 亮），若读得太跳，可改用 `IslandRim.Lightened(…)` 新开一份材质。
