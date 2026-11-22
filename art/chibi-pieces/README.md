# chibi-pieces v2：十种卡通棋子 · 人工检查清单

任务 `.trellis/tasks/11-21-chibi-pieces`。风格依据：用户给的参考图 `reference.png`（闭盔骑士 + 塔 / 碑 / 修道院混搭，卡通分阶明暗，粗黑描边，饱和配色，棋子直接站在格子上）。

十种棋子由 `tools/blender/chibi_pieces.py` 生成 `src/godot/parts/pieces_chibi/<key>.glb`。`PieceModels.TryBuild` 优先加载 glb；glb 缺失或加载失败时回退到 `parts/pieces/<key>.tscn`（旧样式，带八角底座）；再失败则回退到 `LowPoly`。
相机俯角 60° 不变。辨认靠俯视剪影和阵营色块。造型像不像、易混对分不分得开，**以负责人看图为准**。

## 生成与拍图（Mac 本机）

```bash
B=/Applications/Blender.app/Contents/MacOS/Blender
G=~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot
# 1. 生成十颗 glb：逐颗打印 本体 / 描边三角形数、占地半宽、材质槽。
#    超限时不导出该颗，并以退出码 1 结束。上限：本体（含运行时圆环 96）≤ 1200、合计 ≤ 2400、槽 ≤ 7、半宽 ≤ 0.42、徽记锚点恰 1 个。
"$B" -b --factory-startup --python tools/blender/chibi_pieces.py -- src/godot/parts/pieces_chibi
# 2. 重新导入。改了 glb 必须做，否则 Godot 读的是旧缓存
"$G" --headless --path src/godot --import
dotnet build src/godot/Siege.Godot.csproj -v q -p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config
# 3. 对照图（不能加 --headless）+ 去色 / 小图 + 四阵营并排
"$G" --path src/godot -- "--piece-gallery=$PWD/art/chibi-pieces/gallery.png"
ffmpeg -y -i art/chibi-pieces/gallery.png -vf format=gray art/chibi-pieces/gallery-gray.png
ffmpeg -y -i art/chibi-pieces/gallery.png -vf "format=gray,scale=400:225:flags=lanczos" art/chibi-pieces/gallery-gray-small.png
"$G" --path src/godot -- --piece-gallery-factions "--piece-gallery=$PWD/art/chibi-pieces/gallery-factions.png"
# 4. 60×60 大图对局：种子 9、4 人、第 300 帧 = 第 24 大回合
"$G" --path src/godot -- --auto-demo --rounds=30 --seed=9 --map=link:9:4:l "--screenshot=$PWD/art/chibi-pieces/board-far.png:300"
"$G" --path src/godot -- --auto-demo --rounds=30 --seed=9 --map=link:9:4:l --shot-cell=BD17 --shot-near "--screenshot=$PWD/art/chibi-pieces/board-near.png:300"
# 5. 暂放：同一局第 4 帧暂放匠人 BK13（日志 "[auto-demo] 第 4 帧暂放匠人 BK13"），第 5 帧截图
"$G" --path src/godot -- --auto-demo --rounds=30 --seed=9 --map=link:9:4:l --shot-cell=BK13 --shot-near "--screenshot=$PWD/art/chibi-pieces/staged.png:5"
# 6. 拾取自检（可以 --headless），退出码 0 为通过
"$G" --headless --path src/godot -- --auto-demo --pick-check
```

关于两张对局图的取景：

- `board-far.png` 是无人值守演示开局时对准的距离（14.9），不是相机最远档（28）。大图上 `--overview` 只切换小地图的显隐，命令行没有办法拉到最远档。
- `board-near.png` 用 `--shot-near` 拉到了最近档（距离 8.0）。

## 造型约定

| 棋子 | 轮廓语言 | 造型 | 俯视辨认特征 | 徽记位置 |
|---|---|---|---|---|
| 普通子 | Rounded | 剑士：闭盔（T 字面甲）、阵营色羽缨、左前方压低的圆盾（正对镜头，不被大盔挡住）、右手短剑 | 圆盔 + 羽缨 + 圆盾 | 盾面 |
| 哨兵子 | Crossed | 长矛兵：闭盔、羽缨，身后两根长矛在头顶后上方交叉 | X 形交叉长矛 | 胸前鸢形小盾 |
| 旗手子 | Banner | 旗手：闭盔，左侧竖直旗杆，杆顶向右展开一面尖尾大旗，旗面盖过头顶 | 竖杆 + 头顶大旗面 | 旗面 |
| 匠人 | Tooling | 工匠：露脸，阵营色敞口盔 + 钢色帽檐，阵营色围裙，大锤斜压左肩 | 斜柄 + 左上方深色横置锤头 | 围裙 |
| 铁链子 | Interlock | 链枷兵：闭盔、羽缨，左手鸢形盾，右手短柄链枷（三节相扣深色铁环 + 带刺铁球） | 右上方的深色环链 + 球 | 盾面 |
| 堡垒子 | TowerMass | 石垒：圆石塔，阵营色垛口顶（内为木地板），拱形木门，塔身挂旗 | 高圆塔 + 一圈垛口 | 塔身挂旗 |
| 界碑子 | Slab | 矮宽圆顶石碑，后仰 28°，阵营色碑额，碑面嵌阵营色石板（碑额以下整块） | 矮宽单板 + 圆弧顶 | 碑面石板 |
| 连珠子 | Connection | 两座小圆塔（阵营色尖顶）用一段带垛口的城墙相连 | 双塔 + 连接墙 | 墙面挂旗 |
| 倍增子 | Radial | 修道院：中殿 + 阵营色坡屋顶，正面方钟楼 + 阵营色尖顶，顶上钢色四向放射风向标 | 尖顶 + 放射星标 | 门楣挂牌（后仰 40°） |
| 协同子 | MultiNode | 水晶祭坛：八角石台，阵营色台沿，七根晶体（阵营色提亮） | 多簇晶体 | 台面前沿石板（朝上） |

运行时（`PieceModels`，只对 glb 生效；tscn / LowPoly 回退保持旧样式）：

- **脚下圆环**（第二轮裁决 4，取代 v2 首轮的 0.86 整格方块）：外半径 0.36、环宽 0.05、高 0.02，24 段，只有顶面与外壁（96 三角形，计入本体预算）。合并在 Enamel 槽里，借顶点色乘 0.68 压暗，不增加绘制调用。格子中心和领地着色都露在外面。角色与建筑直接从格面 y = 0 站起（脚本 `G = 0`），环在脚外一圈，不垫脚；建筑底座较宽时环被底座挡住大半，阵营色靠屋顶 / 垛口 / 碑额。
- **卡通材质**：`DiffuseMode = Toon`。Metal / Crystal 槽用卡通高光，其余槽不开高光。
  - 钢甲用深一档的蓝灰 `(128,140,160)`（v2 首轮是 `(168,180,198)`，强光下发白，和金方冠饰挨在一起时分不清）。
  - Cloth 槽在 v2 是木料 / 皮革色，不再是阵营色。
- **描边**：`Line_Hull` 由全部本体零件沿顶点法线外推 0.03 得到。运行时独立成一个 MeshInstance（名为 `Outline`），材质为 Unshaded、`CullMode = Front`、深色 `(28,24,30)`，不投影。每颗棋子因此只多一次绘制调用。
- **徽记**：挂在 glb 的 `Anchor_Emblem` 上，沿法线外移 0.004。锚点的约定是：局部 +Z 为正面法线，+Y 为图案上方，缩放即徽记边长。
  - 第二轮裁决 5 起在所在面内放大（图案外接圆半径约 0.61 × 边长，按此不出面）：盾 0.21（盾半径加到 0.145）、胸前小盾 0.15、旗面 0.26、围裙 0.16、鸢形盾 0.19、塔身挂旗 0.17、城墙挂旗 0.16、门楣 0.15、碑面 0.16、祭坛石板 0.12。挂旗 / 门楣 / 石板随之加大。
  - 墨色：浅色阵营（亮度 > 0.55，只有金方）的徽记改用深褐 `(58,40,24)`，其余阵营仍用共用的米白墨。米白太阳在金色盾面上远景几乎看不见。只作用于 glb 棋子；tscn / LowPoly 回退和 HUD 不变。
- **暂放**：`BoardView.PreviewGhost` 会把描边改成半透明（alpha 0.4），颜色向浅色靠 35%，与发光的本体一起变淡。
- **拾取**：`BoardView.PickPiece.Collect` 跳过 `Outline`，拾取几何仍是本体，不会被外壳撑大。`--pick-check` 本轮实测通过（见下文数据），但它在第 2 帧就跑完、盘上还没有棋子，所以它只证明格子拾取没被改坏，证明不了"点棋子不被描边撑大"——后者靠的是上面这条源码跳过。

## 逐项核对（待负责人看图确认）

| # | 检查项 | 看哪张 |
|---|---|---|
| 1 | 十种棋子都能认出来 | `gallery.png`：上行依次为连珠、铁链、堡垒、界碑、普通；下行依次为匠人、旗手、哨兵、倍增、协同 |
| 2 | 去色后仍可辨 | `gallery-gray.png` |
| 3a | 铁链 / 连珠（上行第 1、2 枚） | `gallery-gray-small.png`：持盾骑士 + 右上深色环链与铁球 vs 双尖顶小塔 + 城墙 |
| 3b | 堡垒 / 界碑（上行第 3、4 枚） | 高圆塔 + 一圈垛口 vs 矮宽圆顶碑 |
| 3c | 匠人 / 旗手（下行第 1、2 枚） | 左上深色锤头 + 斜柄 + 无羽缨的敞口盔 vs 头顶一整面旗 |
| 4 | 四个阵营都认得出，金方冠饰不和钢甲混在一起，金方徽记（深褐太阳）看得清 | `gallery-factions.png`：自上而下依次为红、蓝、金、紫；每行十种，顺序为连珠、铁链、堡垒、界碑、普通、匠人、旗手、哨兵、倍增、协同 |
| 5 | 对局远景可辨、阵营色可认、脚下细圆环不挡格子与领地着色、盾面徽记可辨 | `board-far.png` |
| 6 | 对局近景可辨，描边粗细合适 | `board-near.png` |
| 7 | 暂放时描边跟着变淡（半透明浅色边） | `staged.png`：BK13 的红方匠人 |
| 8 | 删掉 `pieces_chibi/` 后回退到旧造型 | 本轮已验证：日志 10 行 `场景已加载 …：res://parts/pieces/<key>.tscn`，造型为旧样式加八角底座 |

注意：对照图左右两端的高棋子会因透视向外侧倾斜（越高的点离相机越近）。对局中屏幕边缘的棋子也是这样，并不是造型歪了。为此把最高的哨兵子放在了下行正中。

## 数据

三角形数（脚本输出；描边与本体同面数，圆环是运行时并进 Enamel 槽的 96 个）：

| 棋子 | 本体 | 圆环 | 描边 | 合计 | 材质槽（含 Line） |
|---|---:|---:|---:|---:|---:|
| 普通 basic | 936 | 96 | 936 | 1968 | 4 |
| 堡垒 fortress | 504 | 96 | 504 | 1104 | 6 |
| 连珠 line | 264 | 96 | 264 | 624 | 4 |
| 倍增 multiplier | 258 | 96 | 258 | 612 | 6 |
| 协同 synergy | 306 | 96 | 306 | 708 | 4 |
| 匠人 artisan | 766 | 96 | 766 | 1628 | 6 |
| 旗手 bannerman | 784 | 96 | 784 | 1664 | 5 |
| 铁链 chain | 1092 | 96 | 1092 | 2280 | 5 |
| 哨兵 sentry | 856 | 96 | 856 | 1808 | 5 |
| 界碑 boundary | 144 | 96 | 144 | 384 | 4 |

预算断言做过一次变异：把脚本副本里的 `RUNTIME_RING_TRIS` 改成 200，铁链子报"本体 1092 + 圆环 200 > 1200"不导出，退出码 1。

运行时每颗棋子的绘制调用 = 材质槽数（Line 槽即描边）+ 1（徽记）。脚下圆环合并在 Enamel 槽里，不额外增加绘制调用。

`--pick-check`（`--headless --auto-demo --pick-check`，缺省大图）：退出码 0。逐格居中最近 1366/1366、最远 1352 往返一致 + 14 格记为纵深夹取后的高台遮挡、失败 0；至少一种缩放下严格往返 1366/1366；7 个位姿与全局预览全部失败 0；"可落子格 1366，至少在一个位姿下验到 1366；通过"。
