# chibi-world：全部素材卡通化 · 人工检查清单

任务：`.trellis/tasks/11-21-chibi-world`。风格依据与单位相同：`art/chibi-pieces/README.md`、`art/chibi-pieces/reference.png`。特征是卡通分阶明暗、粗黑描边、饱和配色。

三段串行，本文件按段追加：
- 段 ①：装饰 18 类（本节）
- 段 ②：地块 + 浮岛
- 段 ③：天空 + 标记 + 界面

## 段 ①：装饰 18 类

`tools/blender/chibi_terrain.py` 生成 `src/godot/parts/terrain_chibi/<名>[_<档>].glb`，共 19 种 Kind、67 个文件。栅栏的 `fence_x` / `fence_z` 合算一类。

运行时由 `TerrainParts` 加载，装饰的加载链是：

1. `terrain_chibi/<名>.glb`
2. `terrain_full_art/<名>.tscn`（改前样式）
3. `LowPoly` 程序生成

删掉 `terrain_chibi/` 即回到改前样式。地块部件（地砖、侧面、浮岛等）不走这条链，本段不动。

### 生成与拍图（Windows，PowerShell，在仓库根目录）

```powershell
$B = "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe"
$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
$R = (Get-Location).Path -replace '\\','/'
# 1. 生成 67 个 glb。逐件打印零件数、本体 / 描边三角形数、材质槽、包围盒；任一断言不过，该件不导出，最后退出码 1。
#    也可以只生成几种：在输出目录后追加 Kind 名，如 ... -- src/godot/parts/terrain_chibi rock chest
& $B -b --factory-startup --python tools/blender/chibi_terrain.py -- src/godot/parts/terrain_chibi
# 2. 重新导入（改了 glb 必须做），再编译
& $G --headless --path src/godot --import
dotnet build src/godot/Siege.Godot.csproj -v q
# 3. 装饰对照图（不能加 --headless）
& $G --path src/godot -- "--terrain-gallery=$R/art/chibi-world/terrain-gallery.png"
#    全部 67 档
& $G --path src/godot -- --terrain-gallery-variants "--terrain-gallery=$R/art/chibi-world/terrain-gallery-variants.png"
#    去色
python -c "from PIL import Image; Image.open('art/chibi-world/terrain-gallery.png').convert('L').save('art/chibi-world/terrain-gallery-gray.png')"
# 4. 对局远景 / 近景：60×60 大图、种子 9、4 人、第 300 帧（第 24 大回合）。与 art/chibi-pieces 的两张同机位
& $G --path src/godot -- --auto-demo --rounds=30 --seed=9 --map=link:9:4:l "--screenshot=$R/art/chibi-world/board-far.png:300"
& $G --path src/godot -- --auto-demo --rounds=30 --seed=9 --map=link:9:4:l --shot-cell=BD17 --shot-near "--screenshot=$R/art/chibi-world/board-near.png:300"
# 5. 改前对照：把 terrain_chibi 挪出 src/godot 再拍，拍完挪回
#    得到 terrain-gallery-before.png 和 board-far-before.png
```

脚本断言（超限不导出，退出码 1）：

| 断言 | 内容 |
|---|---|
| 档数 | 从 `TerrainParts.cs` 读各 Kind 的档数（字面量或 `LowPoly.*Variants`），与脚本里的 `VARIANTS` 逐个核对，不一致则什么都不导出 |
| 预算 | 本体 ≤ 600 三角形；描边 ≤ 本体；材质槽（含 Line）≤ 7（裁决 6 加 Glow 槽后由 6 放宽，与单位上限一致） |
| 包围盒 | 本体不超出 `terrain_full_art` 同名旧部件的实测包围盒，容差 0.006 |
| 包围盒的比较口径 | 格角点缀（草丛、小石、小花、石碑）逐档比；宝箱两档取并集；其余按同类各档的并集比 |
| 不挡格心与单位 | 放在可落子格上的装饰（林地小树、荒漠、沼泽、岩台、浅滩、桥、草丛、小石、小花、石碑），格心半径 0.36（棋子脚下圆环）内的最高点 ≤ 0.007，即只许贴地薄片。0.007 与旧水洼同高，领地着色在 +0.008 |
| 法线朝外 | 每个零件的面法线按面积加权后须朝外，否则描边外壳会整件发黑。荒漠第 4、5 档是镜像档，镜像后把全部面翻回 |
| 石碑例外 | 石碑格心圈内上限放宽到 0.045：碑座内角与旧石碑同位，在 0.315 处，高 0.04 |

本轮做过两次变异，均不导出、退出码 1：

- 预算变异：副本里 `MAX_BODY` 改成 400。拱门报"本体 492 > 400"。
- 档数变异：副本里岩石改成 5 档。报"rock: 脚本 5 档，TerrainParts 6 档"。
- 法线变异：副本里去掉镜像后的翻面。荒漠第 4、5 档报"法线朝内的零件 […]"。

### glb 契约与运行时

**命名与坐标**

- 物体名 `<槽>_<部件>`，不建父子层级，导出时应用变换。
- 各档的整体朝向（遗迹、晶簇、火盆、拱门、宝箱转角，荒漠镜像）直接烘进网格。
- 原点 = 所在格地砖上表面，与 `Kind.Anchor` 一致。Blender 的 −Y = Godot 的 +Z。

**材质槽**

| 槽 | 用途 | 运行时材质 |
|---|---|---|
| Stone | 石材 | 卡通分阶（`Visuals.Toon`） |
| Shade | 深色石 / 深木 / 沙纹 / 裂纹 | 卡通分阶 |
| Wood | 木料 | 卡通分阶 |
| Leaf | 深绿 | 卡通分阶 |
| Moss | 浅绿 / 青苔 | 卡通分阶 |
| Water | 水洼 | 卡通 + 卡通高光 |
| Crystal | 紫晶 | 卡通 + 卡通高光 + 自发光 0.22 |
| Gold | 金饰 | 卡通 + 卡通高光 |
| Flame | 火舌 | 不受光、不投影 |
| Bloom | 花瓣 / 浅色点缀（桥板、箱盖、浅晶、余烬按种类取色） | 卡通分阶 |
| Bone | 象牙：兽骨、绳、白水纹 | 卡通分阶 |
| Line | 描边外壳 | 见下文"描边" |

颜色按（种类, 档, 槽）取：`TerrainParts.ChibiColor`。同槽同色全图共用一份材质。

**描边**

- `Line_Hull` 由本体沿顶点法线外推得到，运行时成为名为 `Outline` 的节点。
- 材质是全图共用的一份 `Visuals.Outline(Visuals.OutlineInk)`：只画背面，不受光，墨色 (28,24,30)，不投影。
- 拾取沿用 `PieceModels.IsOutline` 的约定。
- 贴地薄片（沙纹、裂纹、水洼、睡莲叶、浅滩水纹）标 flat，不进外壳。所以荒漠、沼泽、岩台、浅滩的描边面数少于本体。

**加载与合批**

- glb 实例化后，`TerrainParts` 按槽合并成"根下一层、每槽一个网格 + 一个 Outline"。每档只合并一次并缓存，网格与材质在各实例、各区块之间共用。
- `BoardView.PartBatchSet` 无需改结构即可合批：每个槽、外加描边，各一个 MultiMesh。

**共用材质工厂**

- `Visuals.Toon` / `Visuals.Outline` / `Visuals.OutlineInk` 从 `PieceModels` 提出，单位与装饰共用。
- 单位外观不变：改前、改后的 `--piece-gallery` 两张图（十种 + 四阵营）逐像素一致（MD5 相同）。

描边厚度按装饰尺寸定（起点 0.02），由脚本中的 `OUTLINE` 设定：

| 档位 | 厚度 | 种类 |
|---|---|---|
| 大件障碍 | 0.018–0.022 | 岩石、遗迹、松树、晶簇、火盆、拱门、宝箱 |
| 中件 | 0.012–0.015 | 小树、荒漠、沼泽、岩台、栅栏、桥、石碑 |
| 小件 | 0.010 | 浅滩、草丛、小石 |
| 最小件 | 0.007 | 小花 |

### 造型约定

| 类 | 放在哪 | 造型（卡通版） | 档差 |
|---|---|---|---|
| rock 岩石 | 障碍格（棋盘内岩石 + 场景格） | 一大一中两块圆胖石块（蓝灰 / 深蓝灰），底部没入地砖，加小石与碎石；偶数档顶上两枚紫晶，奇数档顶上一撮青苔。按"障碍"造型做（裁决 7、R1.6） | 每档转 60° |
| ruins 遗迹 | 场景格 | 深色石台 + 砂岩础 + 断柱（断口斜削）+ 倒伏柱身 + 碎石 + 金色碎片；偶数档带青苔 | 每档转 90°，奇数档柱更高 |
| pines 松树丛 | 场景格 | 两三棵三层圆锥卡通松（深 / 浅绿相间），短粗树干 | 三种站位 × 配色 |
| crystal 晶簇 | 障碍格 | 扁圆石座 + 粗主晶 + 三根外倾副晶 + 一根浅色小晶（六棱尖顶） | 每档转 120°，主晶渐长 |
| brazier 火盆 | 障碍格 | 方石座 + 收腰石柱 + 外张石盆，金色盆沿，余烬 + 黄色内焰（Glow，裁决 6）+ 橙色火尖与三条火舌（都不受光） | 两档差 45° 与火舌方位 |
| arch 拱门 | 障碍格 | 两根砂岩方柱（深色柱础、柱头）托半椭圆拱圈，金色拱心石，柱头一撮青苔，门前一块碎石 | 两档差 90° |
| chest 宝箱 | 障碍格 | 木箱体 + 半圆拱浅木箱盖，两道金箍、金锁扣，箱旁三枚金币、两块碎石 | 两档差 90°（整体斜 18°） |
| trees 林地小树 | 林地可落子格 | 三个格角各一棵棒棒糖小树（短干 + 圆树冠 + 浅绿小冠），全部在 0.36 圈外 | 每档空出一个格角 |
| desert 荒漠 | 荒漠可落子格 | 带侧臂的胖仙人掌（顶上一朵粉花）、矮仙人掌、交叉兽骨分放三个格角；两道贴地沙纹 | 4 个朝向 × 镜像，侧臂左右 |
| marsh 沼泽 | 沼泽可落子格 | 两三块贴地水洼（一片睡莲叶），两个格角各一丛芦苇（四片细叶 + 一根香蒲穗） | 水洼布局 × 镜像 |
| crag 岩台 | 岩台可落子格 | 沿地砖内缘一圈圆胖矮石沿（高 ≤ 0.045，每边两块），三四道贴地裂纹，两颗格角碎石。**没有**做成障碍块：`BoardView` 把它放在可落子的 `Surface.Crag` 格上，R1.3 要求不挡格心与单位，旧包围盒高只有 0.056 | 分块点、裂纹朝向 |
| shallows 浅滩 | 浅滩可落子格 | 0.40 圈上五六颗圆润鹅卵石（一颗带青苔），两道白色贴地水纹 | 石子方位 × 镜像 |
| fence 栅栏 | 两格之间的缝 | 三根深木立柱（金色圆钮）+ 两道浅木横杆，厚 ≤ 0.05 | fence_x / fence_z |
| bridge 桥 | 深水格（桥面可落子） | 七块原木色桥板（面与地砖齐平），深木托梁，两侧各三根立柱（金帽），象牙色下垂绳栏 | 1 档，BoardView 按河道转 |
| grass 草丛 | 草地 / 土路格角 | 七片圆润草叶（深浅两绿）+ 矮草墩 | 档 = 格角 |
| pebble 小石 | 草地 / 土路格角 | 一颗浅色圆石 + 两颗深色小圆石 | 档 = 格角 |
| flower 小花 | 草地 / 土路格角 | 三朵五瓣小花，金色花心 | 档 = 花色（粉 / 黄 / 白）兼格角 |
| stele 遗址石碑 | 信物遗址格西北角 | 深色碑座 + 圆顶砂岩小碑，碑面嵌宝石 | 0 普通：紫晶宝石；1 稀有：金宝石 + 金色圆顶 |

### 数据

三角形数（脚本输出）：

| 种类 | 档 | 本体 | 描边 | 材质槽（含 Line） |
|---|---:|---:|---:|---|
| rock | 6 | 280 / 340 | 同本体 | 4（Stone Shade Crystal / Moss，Line） |
| ruins | 4 | 326 / 246 | 同本体 | 4–5（Stone Shade Gold [Moss] Line） |
| pines | 7 | 164 / 246 | 同本体 | 4（Wood Leaf Moss Line） |
| trees | 4 | 468 | 468 | 4（Wood Leaf Moss Line） |
| desert | 6 | 424 | 352 | 5（Leaf Bone Bloom Shade Line） |
| marsh | 6 | 232 / 268 | 136 | 4（Water Leaf Wood Line） |
| crag | 4 | 472 / 452 | 392 | 3（Stone Shade Line） |
| shallows | 6 | 344 / 296 | 288 / 240 | 4（Stone Moss Bone Line） |
| fence_x / fence_z | 1 / 1 | 364 | 364 | 4（Shade Wood Gold Line） |
| bridge | 1 | 452 | 452 | 6（Bloom Shade Wood Gold Bone Line） |
| grass | 4 | 78 | 78 | 3（Leaf Moss Line） |
| crystal | 3 | 270 | 270 | 5（Stone Shade Crystal Bloom Line） |
| brazier | 2 | 472 | 472 | 7（Stone Shade Gold Bloom Flame Glow Line） |
| arch | 2 | 492 | 492 | 5（Stone Shade Gold Moss Line） |
| chest | 2 | 356 | 356 | 5（Wood Bloom Gold Shade Line） |
| pebble | 3 | 192 | 192 | 3（Stone Shade Line） |
| flower | 3 | 456 | 456 | 4（Leaf Bloom Gold Line） |
| stele | 2 | 120 / 124 | 同本体 | 4（Stone Shade Crystal / Gold，Line） |

所有档的本体最多 492（拱门），材质槽最多 7（火盆），其次 6（桥）。

**合批**

日志行为 `[batch]` / `[chunks]` / `[terrain-chibi]`。

| 局面 | 指标 | 改前（删掉 `terrain_chibi/`） | 改后 |
|---|---|---|---|
| 小图（`--auto-demo --pick-check` 缺省图） | 障碍装饰合批 | 1091 格 | 1091 格 |
| | MultiMesh | 106 个 | 150 个 |
| | 实例 | 2670 个 | 3820 个 |
| | 未合批 | 0 格 | 0 格 |
| 大图 `link:9:4:l`（`--rounds=6`），首次加载 16 区块 | MultiMesh | 1467 个 | 2036 个 |
| | 实例 | 10113 个 | 12449 个 |
| | 区块里未合批的装饰 | — | 0 格 |
| 大图远景截图帧（第 300 帧） | 绘制调用 | 2370 | 2570（+8.4%） |
| | 图元 | 744,614 | 1,170,992（+57%） |
| | 单帧处理 | 322.6 ms | 262.9 ms |

注意：

- 单帧处理时间主要花在演示局的 AI 上，不是渲染的读数。
- 图元增加来自更圆的造型和描边外壳（描边与本体同面数）。

**装饰来源**

- 改后：`[terrain-chibi] 装饰：卡通 glb N 件、回退旧样式 0 件；缺 glb 的档 无`。小图 1227 件，大图 `--rounds=6` 累计 10805 件。
- 删掉 `terrain_chibi/` 后：小图"卡通 glb 0 件、回退旧样式 1227 件"，合批数字与改前逐项相同（MultiMesh 106、实例 2670）。

### 逐项核对（待负责人看图确认）

| # | 检查项 | 看哪张 |
|---|---|---|
| 1 | 18 类都能认出来，和单位是一套美术（描边粗细、饱和度） | `terrain-gallery.png`。上行障碍：岩石、晶簇、遗迹、松树、拱门、火盆。中行：宝箱 + 可落子格地表点缀（小树、荒漠、沼泽、岩台、浅滩）。下行：草丛、小石、小花、石碑、栅栏、桥 |
| 2 | 去色后仍可辨 | `terrain-gallery-gray.png` |
| 3 | 改前 / 改后对照 | `terrain-gallery-before.png`（改前，同机位）/ `terrain-gallery.png` |
| 4 | 全部 67 档没有拼错、穿插、漂浮，格角件在正确的角上 | `terrain-gallery-variants.png`（每类一行，左栏前 10 类、右栏后 9 类） |
| 5 | 对局远景：装饰不抢单位与领地着色，障碍格仍读作"不可落子" | `board-far.png`；改前是 `board-far-before.png` |
| 6 | 对局近景：描边粗细合适，可落子格上的点缀不挡格心与单位 | `board-near.png`；改前可对照 `art/chibi-pieces/board-near.png`，同机位，那时装饰是旧样式 |
| 7 | 删掉 `terrain_chibi/` 回到改前样式 | 本轮已验证：日志"卡通 glb 0 件、回退旧样式 1227 件"，合批数字与改前相同 |

### 负责人裁决（2026-11-21，"全按推荐"）

1. 岩台（crag）保持贴边的圆胖矮石沿（高 ≤ 0.045），只有岩石（rock）按障碍块做。terrain-bonus 段 B 把岩石改成可落子地表时，以那边为准。
2. 色板段 ① 不再改；段 ② 定地砖色时，与装饰的 `Chibi*` 常量一起微调（只改常量，不用重新生成 glb）。
3. 小件（草丛、小石、小花、浅滩）描边不加粗。
4. 岩石点缀保留：偶数档两枚紫晶，奇数档一撮青苔。
5. 稀有石碑保持现状；负责人实机看后若觉得不够醒目，再改成金色碑身。
6. 火盆加黄色内焰：火心球改为新槽 Glow（不受光，`ChibiGlow` (255,222,72)），火尖与火舌仍是橙色。材质槽上限因此由 6 放宽到 7。已重新生成两档、重拍对照图。
7. 贴地薄片（水洼、睡莲叶、沙纹、裂纹、浅滩水纹）保持不描边。