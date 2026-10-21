# 段 C 实现记录

## 段 C 实施（trellis-implement，2026-10-21，tasks 3.1–3.2）

### 改动

- `src/godot/scripts/LowPolyMesh.cs`：
  - `FacetRock` 加可选参数 `stretch`（各轴拉伸，乘在顶点上再按三角面求法线；缺省 `null` = 不拉伸，既有输出逐值不变）。压扁的鹅卵石 / 碎石、竖长的香蒲穗都用它，**不在 `Merge` 的变换里做非均匀缩放**（`AppendFrom` 只用 basis 转法线，非均匀缩放会让法线歪）。
  - 新增 `Frustum(bottomRadius, topRadius, height, sides, shade, lean, yaw, bottomCap)`：竖立的棱台 / 棱锥（顶面可水平偏移做斜叶；`topRadius ≤ 0` 收尖）。树干、锥形树冠、仙人掌柱、芦苇叶都用它。网格闭合，竖起的细叶两面都看得到（`Ribbon` 立起只有一面；`Beam` 竖直输入返回空网格）。
  - 新增 `Patch(radius, sides, thickness, seed, jitter, aspect, shade)`：贴地的散列抖边扁斑块（水洼）。
  - 两个新函数都在 design D1 表外，与段 B 加 `Merge` / `Beam` 同一做法：工具函数扩充，不改 D1 的四个函数语义。
- `src/godot/scripts/LowPoly.cs`：重写 `Rock` / `Trees` / `Desert` / `Marsh` / `Crag` / `Shallows`；`Pines` / `Ruins` / `Piece` / `Body` / `Prism` / `Fence` / `Bridge` 未动。
  - 每件部件：根节点下一层若干 `MeshInstance3D`（节点名写死：`Stone` / `Shade`、`Canopy` / `Trunk`、`Cactus` / `Bone` / `Ripple`、`Puddle` / `Reed`、`Rim` / `Crack`、`Pebble` / `Ripple`），每个 1 个 surface（`LowPolyMesh.Merge`），材质沿用 `Visuals` 现有色、打开顶点色（顶点色只当亮度系数）。场景格合批 `NewPartBatch` 要求的"根下一层、各带一个网格"形状保持。
  - 档数常量放在 `LowPoly` 一处：`RockVariants = 6`、`TreesVariants = 4`、`DesertVariants = 6`、`MarshVariants = 6`、`CragVariants = 4`、`ShallowsVariants = 6`；建模函数先按档数取模，程序生成与资源同形（O-4）。
  - `Shaded(color, roughness = 1)`：加粗糙度参数（水洼 0.35、浅滩水纹 0.4 保留原来的水光）；新私有 `Merged`、`QuarterTurn`（90° 整数倍的基向量直接写 0 / ±1）、`Wrap`。
  - 颜色：全部沿用 `Visuals` 既有色（`Rock`、`TreeCanopy`、`Timber`、`Cactus`、`Bone`、`MarshPuddle`、`Reed`、`CragCrack`、`Pebble`、`Ripple`），衍生色只有改前就有的三处就地 `Darkened` / `Lightened`（岩石暗块 `Rock.Darkened(0.14)`、沙纹 `TileDesert.Darkened(0.16)`、岩台石沿 `TileCrag.Lightened(0.08)`）。`Visuals.cs` 未动。
  - 变体与形状变化只取自 `LowPolyMesh.Hash` / `Unit` / `Signed` 与变体号，没有 `Random` / `GetHashCode` / 时间。
- `src/godot/scripts/TerrainParts.cs`：`rock` 4→6、`trees` 3→4（其余四种档数不变：desert 6、marsh 6、crag 4、shallows 6），档数取 `LowPoly.*Variants`；`pines` / `ruins` 按 M3 不动（7 / 4 档）。改了一句类注释。
- `src/godot/scripts/PartExport.cs`：只改类注释里"岩石取前 4 档"那句。
- `BoardView.cs` 未动：放置处 `variant++` 计数、`ObstacleArtAt` 分流、`_decoration` 挂点、`Refresh` 的装饰缩放、`AddPlaceholders`、`AddTint(cell.Coord, Visuals.ForbiddenShade, 0.45f);` 都原样。
- 资源（`src/godot/parts/terrain/`）：重写 `rock_0–3`、`trees_0–2`、`desert_0–5`、`marsh_0–5`、`crag_0–3`、`shallows_0–5`；新增 `rock_4`、`rock_5`、`trees_3` 与 12 份 `_vc` 材质（`matte_8e8a86_r100_vc` 岩、`matte_7a7773_r100_vc` 暗岩、`matte_308048_r100_vc` 树冠、`matte_6e9656_r100_vc` 仙人掌、`matte_eee6d4_r100_vc` 兽骨、`matte_cbbe8f_r100_vc` 沙纹、`matte_465c60_r35_vc` 水洼、`matte_b09c60_r100_vc` 芦苇、`matte_9e9383_r100_vc` 岩台石沿、`matte_60564a_r100_vc` 裂纹、`matte_a8a092_r100_vc` 鹅卵石、`matte_e8f6fa_r40_vc` 水纹；树干用段 B 已有的木料 `matte_705436_r100_vc`，本段共用 13 份 `_vc` 材质）。档数只增不减，没有残留旧档文件。其余导出漂移（`pines_*`、`ruins_*`、段 A / B 的部件、仍被 pines / ruins 引用的旧材质，只差自动节点名序号 / `unique_id` / 行尾）已 `git checkout --` 还原。

### 造型

| 部件 | 档 | surface | 三角形 / 件 | 造型 |
|---|---|---|---|---|
| `rock` | 6 | 2 | 120 | 大块浅岩（8 经线多面岩，半径 0.27，拉伸 1.12 × 0.80 × 1.0）+ 中块暗岩（0.17）+ 小石（0.12）+ 两粒碎石，底部没入地砖；位置按散列错开 ±0.015，每块自转散列角，整组每档绕 Y 转 60°。灰色、块状，没有锥形与绿色，读作"一格岩石" |
| `trees` | 4 | 2 | 117 | 四个格角每档空出一个、其余三角各一棵：五棱短干 + 两层六棱锥形树冠（下层半径 0.085、上层 0.06 错转 30°），大小 0.88–1.0、朝向与明暗按散列错开；锥形树冠保留（林地的形状提示） |
| `desert` | 6 | 3 | 160 | 两道波状贴地沙纹（高 0.004）斜穿格心；格角一株六棱仙人掌（尖顶，顶高 0.23）+ 沿格角切向伸出的曲臂（横段 + 竖段 + 尖顶）；对角一株矮仙人掌（0.14）；第三个格角两根交叉兽骨（骨干 + 两端骨节，高约 0.04）。布局先镜像（只改位置）再整组转 90° 的整数倍，六档各异 |
| `marsh` | 6 | 2 | 92 / 113 | 2–3 块散列抖边水洼（七边形，高 0.007，粗糙度 0.35）；两个格角各一丛五片三棱尖叶芦苇（叶高 0.15–0.21，从丛心向外斜出），每丛一枚香蒲穗（顶 ≤ 0.211）。不用锥形树冠 |
| `crag` | 4 | 2 | 210 / 206 | 石沿每边两块倒角石（分块点按散列错开、块间缝 0.008、顶高 0.026–0.03，与改前 0.03 同高、占位同为内缘 0.05 宽一圈；块底下探到 −0.03，填上地砖顶板倒角处的空隙）；3–4 道贴地折线裂纹（高 0.004）；石沿以内两个格角各一颗压扁碎石（顶 ≤ 0.056）。不读成高一层 |
| `shallows` | 6 | 2 | 156 / 132 | 6 / 5 颗压扁多面鹅卵石排在格心半径 0.402 的一圈（角度按散列错开）；两道贴地弧形水纹（高 0.003，粗糙度 0.4）。全部 ≤ 0.026，水面（地砖本身）与同层齐平不变 |

改前（`HEAD` 资源实测）每件 surface 数：rock 3、trees 6、desert 8、marsh 12–13、crag 9–10、shallows 7–8；改后 2–3。导出日志三角形数（全部 `LowPolyMesh` 部件）：`rock_0–5 各 120、trees_0–3 各 117、desert_0–5 各 160、marsh 92/113/113/92/113/113、crag 210/206/210/206、shallows 156/132/156/132/156/132`；本段部件三角形合计 4 480（32 件：rock 720、trees 468、desert 960、marsh 636、crag 832、shallows 864）。

### 高度与"在底座之外"核对

方法：导出产物里合并网格的 `aabb` 只能核高度上限（一个 surface 的盒子覆盖整格），所以另写了一个只读的 GDScript，存在本任务目录 `.trellis/tasks/10-21-map-elements-v2-c/check_parts.gd`（不在 `src/godot` 下，不进 Godot 工程）：`& $G --headless --path src/godot --script <绝对路径>/check_parts.gd -- res://parts/terrain`（也可给导出的临时目录）。它加载每件 `.tscn`，把所有 `MeshInstance3D` 的三角面按节点变换到部件原点坐标系，在每个三角面上按重心坐标 11 × 11 网格采样（含顶点与边），统计：最高 y、|x| / |z| 最大值、**格心半径 0.36 圆柱内采样点的最高 y**、**y > 0.02 的采样点离格心的最小水平距离**。

改后（导出到 `res://parts/terrain` 的同一份产物）：

| 部件 | 最高 y | max \|x\| / \|z\| | r < 0.36 内最高 y | y > 0.02 的最近半径 | 判定 |
|---|---|---|---|---|---|
| `rock_0–5` | 0.362–0.400 | ≤ 0.407 / ≤ 0.397 | 同最高 y（障碍格，不可落子，格心可占） | — | 高 ≤ 0.75 ✓，不出地砖 ✓ |
| `trees_0–3` | 0.247–0.259 | ≤ 0.418 / ≤ 0.420 | 圆柱内无任何几何 | 0.388–0.404 | ✓ |
| `desert_0–5` | 0.230 | ≤ 0.430 / ≤ 0.430 | 0.004（沙纹） | 0.399–0.416 | ✓ |
| `marsh_0–5` | 0.210 | ≤ 0.402 / ≤ 0.400 | 0.007（水洼） | 0.391–0.412 | ✓ |
| `crag_0–3` | 0.050–0.056 | 0.450 / 0.450（石沿贴地砖外缘，与改前同） | 0.004（裂纹） | 0.3995–0.4004（石沿内缘 0.40） | ✓ |
| `shallows_0–5` | 0.023–0.025 | ≤ 0.428 / ≤ 0.428 | 0.003（水纹） | 0.388–0.395 | ✓ |

同一脚本对改前（`HEAD`）资源的结果，作对照：trees 最近半径 0.229–0.246（格边中点那棵树在底座里，r < 0.36 内高到 0.28）、desert 0.298–0.349（仙人掌侧臂朝内的档 r < 0.36 内高到 0.205）、shallows 0.307–0.338（`(−0.10, −0.35)` 那颗鹅卵石）、crag 0.376–0.382、marsh 0.369–0.373。改前就有三类越界，本段一并修正。

另：`Refresh` 在信息层打开时把装饰朝格心缩到约 0.846（`0.72 + 0.28 × 45%`），缩后的几何会进入 0.36 圆——这是既有的"压淡装饰"机制，规格核的是默认视图，本段不处理。

### 导出可复现

- 先导到 `res://parts/terrain`：退出 0，`[export-parts] 部件 58 / 58、共享材质 23 → res://parts/terrain`（55 → 58：+ `rock_4`、`rock_5`、`trees_3`）。
- 再导两次到全新的临时目录 c1 / c2（各退出 0）。归一规则同段 A / B（行尾、目录前缀、节点 `unique_id`、外部资源 id `N_xxxxx`、引擎随机子资源 id `Type_xxxxx`，其余不改；脚本存在本任务目录 `norm_cmp.py`，用法 `python -I norm_cmp.py <目录A> <A的前缀> <目录B> <B的前缀> [文件名正则]`）：c1 vs c2 `files=81 raw_same=23 norm_same=81 diff=0 missing=0`。
- c1 vs 仓库：本段 32 件、本段用到的 13 份 `_vc` 材质与其余段 A / B 部件归一后都相同；仍不同的只有 `pines_0–6`、`ruins_0–3`（自动节点名 `@MeshInstance3D@N` 序号不同——本段的部件不再建无名节点，进程内计数随之变化；按 M3 资源不动，已还原）；仓库多出 12 份孤儿材质（见偏离 2）。

### 验证（同一时间只跑一个 dotnet / Godot 进程）

- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误（改码后、导出前；收尾再跑一次见下）。
- Godot headless（Debug 构建后，顺序单跑）：
  1. `-- --auto-demo`：退出 0。`[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`、`[zone] 出生区描边：出生区 1 12 段、出生区 2 12 段、出生区 3 12 段、出生区 4 12 段、出生区 5 12 段；棋盘内岩石 0 格（按棋盘格画）` 与 0.1 基线逐字相同（`diff` 无输出）；`[parts] 资源 473–474 件、程序生成 0 件`（与段 B 相同：件数按放置计，档数不影响）；`[water] 深水 6 格（架桥 2 格）、河岸石沿 16 件`；`[flow] 自检 5 条，失败 0 条`。
  2. `-- --auto-demo --pick-check --seed=5`：退出 0，214 / 214。
  3. `-- --map-select --auto-demo`：退出 0（`[flow] 自检 5 条，失败 0 条`）。
  4. `-- --auto-demo --rounds=0 --seed=5`：退出 0，`[flow] 自检 7 条，失败 0 条，已见步骤：插旗锁定 → 构筑期 → 全图解禁 → 终局；通过`；全程 `[parts] 程序生成 0 件`。
  5. `-- --auto-demo --pick-check --seed=5 --map=link:7`：退出 0，218 / 218；`[plates]`、`[zone]` 与基线逐字相同；`[parts] 资源 497 件、程序生成 0 件`。
  6. `-- --auto-demo --map=link:7`：退出 0。`[plates] 连体棋盘（出生区 1+2+3+4+5，227 格）：外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段`、`[zone] … 各 12 段；棋盘内岩石 2 格（按棋盘格画）` 与基线逐字相同；`[parts] 资源 494–497 件、程序生成 0 件`；`[water] 深水 9 格（架桥 2 格）、河岸石沿 22 件`。
  - 六份日志 error / exception 0 处。
- 六条自检跑在改码后的那次 Debug 构建上；其后只改了 `Rock` 的一行 `<remarks>` 注释，并在收尾重新 Debug 构建（见下），语义无差，未重跑。
- 全仓 `git status --short`：4 个脚本（`LowPoly.cs`、`LowPolyMesh.cs`、`PartExport.cs`、`TerrainParts.cs`）、29 个重写 `.tscn`（rock 4、trees 3、desert 6、marsh 6、crag 4、shallows 6）、3 个新 `.tscn`（`rock_4`、`rock_5`、`trees_3`）、12 份新 `_vc` 材质、`tasks.md`、本记录与两个核对脚本；无 `.godot/`、`.import`、`art/` 漂移。
- 收尾构建与测试（顺序单跑）：
  - `dotnet build src/godot/Siege.Godot.csproj`（Debug）：退出 0，0 警告 0 错误。
  - `dotnet build siege.sln -c Release`：退出 0，0 警告 0 错误。
  - `dotnet test siege.sln`：退出 0，`Siege.Core.Tests` 失败 0、通过 2100、跳过 6（总 2106，8 分 17 秒）；源码扫描守门（`占位轮廓灰度Tests`、`禁手标记的远近可辨Tests`、`六种棋子的轮廓语言Tests`——`LowPoly.cs` 55 405 字符、含 `Body`——、`棋盘台面的可视表现Tests`、`Godot层不含规则计算Tests`）随之通过。
- 不截图（3.3 由主会话做）。headless 验不到：岩石是否读作"一格岩石"（`link:7` Q16、U15）、灰度下七种地表逐格可辨（`link:1` K14 / M14 荒漠、E15 / G12 沼泽、P4 / V4 / V8 岩台、D9 / G9 / K8 浅滩、C11–H16 林地），留给 3.3 截图。

### 偏离 / 待裁决

1. **荒漠 3 个 surface**（任务写"尽量每件 1–2 个"）：仙人掌绿、兽骨白、沙纹深沙三种底色差得远，顶点色只有 0.92–1 的亮度系数，合不进同一份材质。去掉沙纹能压到 2 个，但会少一条灰度通道（"这块地是沙"），所以保留。改前是 8 个。
2. **12 份孤儿材质**：本段之后 `matte_465c60_r35`、`matte_60564a_r100`、`matte_6e9656_r100`、`matte_7a7773_r100`、`matte_8e8a86_r100`、`matte_9e9383_r100`、`matte_a8a092_r100`、`matte_b09c60_r100`、`matte_cbbe8f_r100`、`matte_e8f6fa_r40`、`matte_eee6d4_r100`（本段换成 `_vc` 版后无人引用）与段 B 的 `matte_b0804e_r100` 已无 `.tscn` 引用。按 B4 先例不删，留给 5.1 全量重导时清理。`matte_308048_r100`、`matte_705436_r100` 仍被 `pines_*` 引用，不是孤儿。
3. **`trees` 3→4 档**：任务只要求岩石扩档；林地小树改成"四角空一角"的布局后，4 档正好让每个格角都轮到空一次，林地连片（`link:1` C11–H16）时重复感更低。放置处 `variant++` 不变。
4. **小树与改前的差异**：改前三棵树里有一棵在格边中点（r ≈ 0.31，在底座内），改后三棵都在格角；树冠从单锥（高 0.28）改为两层锥（高 ≤ 0.26）。仍是锥形树冠。
5. **岩台石沿下探**：石沿块底从 y = 0 延到 −0.03，填上地砖顶板倒角（0.03）处原本露出的空隙；顶高与占位不变（≤ 0.03、内缘 0.05 宽）。
6. **岩石变矮一点**：最高 0.36–0.40（改前 0.43），水平占位与改前相近（≤ 0.41）。若 3.3 看图觉得"一格岩石"的体量不够，可把主块半径从 0.27 调大。
