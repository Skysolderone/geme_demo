# 首套实机美术样板

## 内容

本样板沿用 `art/style-exploration/siege-style-03-low-poly-fantasy.png` 的明快低多边形奇幻棋盘方向。所有展示图由当前 Godot 对局或 `--piece-gallery` 实际渲染。

| 资源 | 本次处理 | 源 |
|---|---|---|
| 崖壁 | 两排错缝砌石，保留原高差、岩沿和格边 | `src/godot/scripts/LowPolyMesh.cs` 的 `CoursedCliff`，烘焙为 `side_cliff_0..2.tscn` |
| 林地树 | 格角内三层多面树冠，高度不超过原 0.26 格 | `src/godot/scripts/LowPoly.cs` 的 `Trees`，烘焙为 `trees_0..3.tscn` |
| 木桥 | 加斜撑与低矮木钉，桥面和格心维持原状 | `src/godot/scripts/LowPoly.cs` 的 `Bridge`，烘焙为 `bridge.tscn` |
| 棋子 | 普通子、堡垒子、界碑子三个可替换模型；四方共用模型，底座徽记沿用既有实现 | `source/pieces-generate.py` 生成 `src/godot/parts/pieces/*.tscn` |

草地、深水和其余七种棋子仍用既有资源，作为同屏对照。样板未更改玩法、阵营映射、格距或拾取。

## 审阅图片

| 文件 | 检查重点 |
|---|---|
| `overview.png` / `overview-gray.png` | 全图下格线、地表、桥和高台仍可读 |
| `cliff-near.png` | 砌石崖壁与高台格心、棋子同屏 |
| `river-near.png` | 深水、浅滩、桥和格线同屏 |
| `forest-near.png` | 林地树留在格角，红方普通子及落点可见 |
| `four-factions.png` / `four-factions-gray.png` | 四方棋子、徽记和相邻棋子密集场面 |
| `piece-gallery.png` / `piece-gallery-gray.png` / `piece-gallery-gray-small.png` | 十种轮廓；重点对比普通子与界碑子、堡垒子与界碑子 |

## 生成与接入

棋子源文件：`python art/art-vertical-slice/source/pieces-generate.py`。三个场景只包含本体，`PieceModels` 每次实例化独立材质，并复用既有阵营底座与徽记；缺资源时回退 `LowPoly.Piece`。

地形源文件是 `LowPoly.cs`、`LowPolyMesh.cs` 和 `TerrainParts.cs`。构建 Debug 后，用 Godot `--headless --path src/godot -- --export-parts=<临时绝对目录>` 导出，再仅同步上表的八个 `.tscn`，把材质引用改为 `res://parts/terrain/materials/`。直接向正式资源目录全量导出会覆盖其他部件，不使用该方式。

所有对局截图使用 `--map=link:1 --auto-demo --seed=5 --mute`；`overview` 用 `--rounds=1 --overview --screenshot=<路径>:12`，`four-factions` 用 `--rounds=0 --overview --screenshot=<路径>:120`。近景分别加 `--shot-cell=R8`、`E4`、`Q4` 与 `--shot-near`，帧号为 12。十棋子图使用 `--piece-gallery=<路径>`。灰度图从对应实机 PNG 以 Pillow 的 `ImageOps.grayscale` 转换，缩略图用 LANCZOS 缩到 400×225。

## 验证记录

- `dotnet build src/godot/Siege.Godot.csproj -c Debug --nologo`：通过，0 警告、0 错误。
- `dotnet test tests/Siege.Core.Tests/Siege.Core.Tests.csproj -c Debug --filter FullyQualifiedName~VisualStyleBaseline --nologo`：27 项通过。
- Godot `--map=link:1 --auto-demo --pick-check --mute`：192 / 192 可落子格至少在一个位姿下拾取往返一致，退出码 0。
- 临时移走 `basic.tscn` 再运行 `--piece-gallery`：图库正常出图，普通子使用原程序模型；场景已恢复。
- 全量 `dotnet test siege.sln` 运行数分钟没有新输出，按项目工具等待约束中止；本次以直接相关的视觉基准测试作为回归结果。

## 人工审阅项

- [ ] 默认视角和灰度下，崖壁、高台、桥、深水、林地与草地分得开。
- [ ] 近景的树冠和桥部件没有遮挡格心、格线、落点、棋子底座。
- [ ] 十棋子原尺寸和 400×225 灰度缩略图仍可辨认；普通子与界碑子不混淆。
- [ ] 四方阵营在全图下通过底座色与不同徽记辨认。
- [ ] 暂放发光与盘上普通棋子同屏时，其他棋子的材质不被改变。

本批使用可编辑的 Godot 原生场景和生成脚本；尚未建立 Blender / glTF 导出链。图片供负责人审看后决定是否把这套造型扩展到全部棋子和地形。
