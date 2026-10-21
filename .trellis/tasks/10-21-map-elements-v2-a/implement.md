# 段 A 实现记录

## 0.1 改前基线（主会话，2026-10-21，提交 f484857 之上 + `--shot-near`）

截图（非 headless，`$G = Godot_v4.7.2-stable_mono_win64_console.exe`，均 `--auto-demo --seed=5 --rounds=1`，第 12 帧）存 `art/map-elements-v2/before/`：

| 文件 | 命令附加参数 |
|---|---|
| `link1-overview.png` | `--map=link:1 --overview --perf-sample=4` |
| `link7-overview.png` | `--map=link:7 --overview --perf-sample=4` |
| `link1-S4-near.png` | `--map=link:1 --shot-cell=S4 --shot-near`（崖壁 S4–T4、缓坡 U4） |
| `link1-E8-near.png` | `--map=link:1 --shot-cell=E8 --shot-near`（一格宽河 D8–G8、桥 F8） |
| `link7-Q16-near.png` | `--map=link:7 --shot-cell=Q16 --shot-near`（棋盘内岩石 Q16、U15） |

`--shot-near`（新增，仅截图用）：配合 `--shot-cell=` 把缩放拉到最近；一屏看得全的图上 `--shot-cell=` 单用不会拉近。

`[perf]`（窗口模式，锁定后全局预览静止帧；帧率受窗口前后台影响，只比绘制调用）：

| 图 | 绘制调用 | 对象 | 图元 |
|---|---|---|---|
| `link:1` | 729 | 1979 | 27295 |
| `link:7` | 710 | 2025 | 27419 |

headless `--auto-demo` 自证行（锁定后；`link:1` 缺省，`link:7` 加 `--map=link:7`）：

- `link:1`：`[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`；`[zone] 出生区描边：出生区 1 12 段、出生区 2 12 段、出生区 3 12 段、出生区 4 12 段、出生区 5 12 段；棋盘内岩石 0 格（按棋盘格画）`；`[batch] 地砖与台面归并…：面砖材质 18 份、衬底材质 18 份、描边 / 边框材质 6 份、盒网格 18 份`；`[parts] 资源 27–28 件、程序生成 0 件`。
- `link:7`：`[plates] 连体棋盘（出生区 1+2+3+4+5，227 格）：外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段`；`[zone] … 各 12 段；棋盘内岩石 2 格（按棋盘格画）`；`[batch] … 面砖材质 18 份、衬底材质 18 份、描边 / 边框材质 6 份、盒网格 20 份`；`[parts] 资源 29–32 件、程序生成 0 件`。
- 两图 `[batch] 障碍装饰合批 0 格 … 场景面砖 0 格、MultiMesh 0 个`。

## 段 A 实施（trellis-implement，2026-10-21，tasks 1.1–1.3）

### 改动

- 新增 `src/godot/scripts/LowPolyMesh.cs`：`BeveledSlab` / `Strata` / `FacetRock` / `Ribbon`，SurfaceTool → `ArrayMesh`，每件 1 个 surface、平直法线 + 灰度顶点色；绕序沿用 `LowPoly.PrismFace` 的叉积判定。抖动 / 色差只取 `Hash(seed, i)`（整数位混合）及其换算 `Unit` / `Signed`，无 `Random`。`LowPoly.cs` 未动。
- `TerrainParts.cs`：`Kind` 追加位置参数 `Anchor`（缺省"所在格的地砖上表面"，既有 11 处构造不改）；新增 `tile_top`（3 档）、`side_slope`（2）、`side_cliff`（3）、`liner`（1），追加在 `All` 末尾；新增单网格入口 `PieceOf(kind, variant)` → `Piece(Mesh, Material?)`：有资源取资源里第一个 `MeshInstance3D` 的网格与材质（计 Loaded），否则程序生成（计 Generated），每档缓存一次，计数按放置件数。新常量 `LinerThickness = 0.03`、`TileBevel = 0.03`。`Create` / `Template` / `CountLoaded` 不变。
- `PartExport.cs`：材质去重键对打开顶点色的材质多加 `_vc`（既有材质文件名不变）；`ArrayMesh` 子资源 id 按"部件名_序号"固定；导出日志多一行各部件三角形数。
- `BoardView.cs`：`AddTileStack` 多一个 `Coord` 参数（两处调用传 `cell.Coord`），侧面 / 衬底 / 顶板都经 `TerrainParts.PieceOf` 取网格、逐格一个 `MeshInstance3D`；`SurfaceOf` / `LinerOf` 的材质打开 `VertexColorUseAsAlbedo`（仍按底色共用，面砖材质仍登记压暗表）；`[batch]` 第二行末尾追加"地块部件网格 N 份"。`AddPlaceholders`、`ObstacleArtAt` 分流、`_decoration` 挂点、`AddTint(...)` 字面未动。
- 资源：`src/godot/parts/terrain/` 新增 `tile_top_0–2.tscn`、`side_slope_0–1.tscn`、`side_cliff_0–2.tscn`、`liner.tscn` 与材质 `matte_ffffff_r90_vc.tres`（中性白）、`matte_9e7048_r90_vc.tres`（SlopeSide）、`matte_887c74_r90_vc.tres`（CliffSide）。

### 几何与造型

| 部件 | 档 | 锚点 | 包围 | 三角形 / 件 | 造型 |
|---|---|---|---|---|---|
| `tile_top` | 3 | 地砖上表面中心 | 0.9 × 0.12 × 0.9（导出 AABB 实测 `(-0.45,-0.12,-0.45, 0.9,0.12,0.9)`） | 20 | 倒角 0.03；顶面按偏心扇心切 4 个三角面，色差去均值（整块平均 0.98，四面在约 0.96–1.0 之间）；侧面 0.94 |
| `side_slope` | 2 | 该层带顶面中心 | 1.0 × 0.35 × 1.0 | 96 | 3 条土层色带（0.94–0.98 ± 散列），每面 4 段、中间段点向内凹 ≤ 0.014，棱与格边齐平 |
| `side_cliff` | 3 | 同上 | 1.0 × 0.35 × 1.0 | 192 | 顶边岩沿 0.045 高、与格边齐平；岩沿下岩体内缩（格角 0.02、段点 0.035 + 散列 ≤ 0.02），每面一道岩缝再深 0.05；坡脚收回格边；格角内缩让相邻崖壁之间读出竖向节理 |
| `liner` | 1 | 衬底中心 | 1.0 × 0.03 × 1.0 | 12 | 平板，顶点色 1 |

- 位置与改前逐值相同：侧面锚在 `(top.X, TopYOf(layer) − TileHeight, top.Z)`、体高 LayerHeight（= 改前 `bandTop − bandBottom`）；衬底 `top − (0, TileHeight − 0.015, 0)`；顶板锚在 `top`。外沿凹凸只向内，外轮廓不超出格边。
- 变体：`TileVariant(coord, salt)` 按全图坐标散列，顶板 salt 0、第 1 / 2 层侧面 salt 1 / 2（档位互不同步）。
- 顶点色：Godot 4 的顶点色按每通道 8 位存，大于 1 的系数会被截掉，所以实际取值区间是 0.92–1.0（以 1 为本色、只往暗处走），不是 design D1 写的 0.92–1.06（见"偏离"）。

### 验证（同一时间只跑一个 dotnet / Godot 进程）

- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误。
- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet test`：退出 0，`Siege.Core.Tests` 失败 0、通过 2100、跳过 6（总 2106，6 分 41 秒）；源码扫描守门（占位轮廓灰度、禁手标记、六种棋子轮廓、棋盘台面、Godot 层不含规则计算 / 坐标映射唯一）随之通过。
- 导出可复现：改前代码先导两次（临时目录 base1 / base2）——Godot 4.7 存盘时外部资源 id（如 `1_13mnk`）、子资源 id（如 `BoxMesh_xrxux`）与节点 `unique_id` 每次随机，**改前就不是逐字节相同**；归一化这三类 id、目录前缀与行尾后 43 件 + 18 份材质全同。改后导两次（a1 / a2，退出 0）：52 / 52 件、共享材质 21；raw 相同 21 份（材质），其余 52 份归一化后相同，diff 0。既有 43 件改后导出与改前导出归一化后全同。新部件节点名写死（`Mesh`）、`ArrayMesh` 子资源 id 固定（`tile_top_0_0` 等），剩下的随机项只有外部材质 id 与 `unique_id`。比对脚本：只做 id / 目录 / 行尾归一，不改别的。
- 导出日志（`--export-parts=res://parts/terrain`，退出 0）：`[export-parts] 部件 52 / 52、共享材质 21 → res://parts/terrain`；`[export-parts] 程序建模网格（LowPolyMesh）三角形数：tile_top_0 20、tile_top_1 20、tile_top_2 20、side_slope_0 96、side_slope_1 96、side_cliff_0 192、side_cliff_1 192、side_cliff_2 192、liner 12`。
- 已提交的既有 44 个文件与当前代码导出的结果归一化后仍有 9 件不同（`bridge`、`fence_x/z`、`rock_1–3`、`ruins_0–2`：节点自动名 `@MeshInstance3D@N` 的序号不同，是早先导出时进程内节点计数不同，改前代码导出也一样）；所以导进 `res://parts/terrain` 后用 `git checkout -- src/godot/parts/terrain` 还原既有文件，只留 12 个新增文件。`git status src/godot/parts` 只有 12 个 `??`。
- 仓库内 9 个新 `.tscn` 的 AABB：`tile_top_*` `(-0.45, -0.12, -0.45, 0.9, 0.12, 0.9)`，`side_slope_*` / `side_cliff_*` `(-0.5, -0.35, -0.5, 1, 0.35, 1)`，`liner` `(-0.5, -0.015, -0.5, 1, 0.03, 1)`——与改前的盒子逐值相同、不超出格边；材质引用全是 `res://parts/terrain/materials/…_vc.tres`。
- 段 E 5.1"全量重导"时上面 9 件既有部件会因自动节点名序号被重写，属预期。
- headless 验不到：面朝向 / 法线 / 顶点色观感只能在 1.4 截图里确认（重点看有无面朝内、发黑）。
- Godot headless 自检（Debug 构建后）：
  - `-- --auto-demo`：退出 0。`[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`、`[zone] 出生区描边：出生区 1 12 段、出生区 2 12 段、出生区 3 12 段、出生区 4 12 段、出生区 5 12 段；棋盘内岩石 0 格（按棋盘格画）`与 0.1 逐字相同；`[parts] 本次搭建地形部件：资源 457–458 件、程序生成 0 件`；`[batch] … 面砖材质 18 份、衬底材质 18 份、描边 / 边框材质 6 份、盒网格 15 份、地块部件网格 7 份`。
  - `-- --auto-demo --map=link:7`：退出 0。`[plates] …（227 格）：外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段`、`[zone] … 各 12 段；棋盘内岩石 2 格（按棋盘格画）`与 0.1 逐字相同；`[parts] 资源 472–475 件、程序生成 0 件`；`[batch] … 面砖材质 18 份、衬底材质 18 份、描边 / 边框材质 6 份、盒网格 17 份、地块部件网格 8 份`。
  - `-- --auto-demo --pick-check --seed=5`：退出 0，214 / 214。
  - `-- --map-select --auto-demo`：退出 0。
  - `-- --auto-demo --rounds=0 --seed=5`：退出 0，`[flow] 自检 7 条，失败 0 条，已见步骤：插旗锁定 → 构筑期 → 全图解禁 → 终局；通过`。
  - `-- --auto-demo --pick-check --seed=5 --map=link:7`：退出 0，218 / 218。
  - 退路抽查：把 9 个新 `.tscn` 临时挪出后跑 `-- --auto-demo`，退出 0，`[parts] 资源 27–28 件、程序生成 430 件`，`[zone]` 不变；随后挪回。
- `[batch]` 第二行"盒网格"从 18 / 20 降到 15 / 17（地块不再走 `BoxOf`），末尾多"地块部件网格 N 份"；这一行不在逐字相同清单里。

### 偏离 / 待裁决

1. 顶点色区间：8 位顶点色存不下 > 1 的系数，实际 0.92–1.0；顶板平均亮度系数 0.98（比改前整体暗约 2%，明暗棋盘格 5% 的交替不受影响）。若要保持改前的平均亮度，可把顶板平均取 1、色差只往暗处走（平均约 0.99），或接受现状。
2. "两次导出逐字节相同"：Godot 4.7 的外部资源 id 与节点 `unique_id` 每次存盘随机、不可由脚本指定（`Resource.SetIdForPath` 仅编辑器内有效），改前就不满足逐字节；按 id / 目录归一后逐字节相同。建议把 D7 / 规格"导出可复现"的判据写成"归一化随机 id 后逐字节相同"。
3. 崖壁格角内缩 0.02，相邻两格崖壁之间有约 0.04 宽的竖向节理缝（设计用意：岩缝）；若截图里读成"立柱"，可把格角内缩改为 0（崖壁会在格边出现竖棱）。
