# 首套棋子模型源

`pieces-generate.py` 是三枚棋子的可编辑几何源，运行后写出 `src/godot/parts/pieces/` 的 Godot 原生 `.tscn` 场景。场景内的 BoxMesh、CylinderMesh、SphereMesh 也能直接在 Godot 编辑器中逐件编辑。本批没有 `.glb`；待实机样板审看后再决定是否增加 Blender / glTF 制作链。

```powershell
python art/art-vertical-slice/source/pieces-generate.py
```

| 轮廓 | 模型特征 | 灰度下的判据 |
|---|---|---|
| 普通子 `basic.tscn` | 窄身、圆头、细颈、少量金属顶饰 | 圆头宽约 0.34，主体上窄下宽 |
| 堡垒子 `fortress.tscn` | 八面石塔、腰箍、顶圈和四块独立雉堞 | 三者中最高、唯一有顶端齿状结构 |
| 界碑子 `boundary.tscn` | 后仰的宽石板、圆肩和两道阵营色横刻痕 | 板宽 0.63，约为普通子圆头的两倍；低矮宽扁 |

场景只含模型本体。阵营底座及旗帜徽记仍由 `LowPoly.Piece` 生成，四方共用同一份模型。各网格节点名前缀是材质槽：`Stone_` 为暖色哑光石材，`Shade_` 为深色凹面，`Metal_` 为低光泽铜饰，`Faction_` 取当前阵营主色，`Ink_` 取徽记浅色。`PieceModels` 在每次实例化时创建独立材质，所以暂放棋子的透明和发光不会改动盘上其他棋子。

实机检查仍走 `--piece-gallery=<PNG>`：它与对局盘面共用 `BoardView.BuildPiece`。请同时比较原尺寸彩色图、去色图及 400×225 去色缩略图，重点看普通子与界碑子；盘面还需检查暂放发光、提子淡出、四方徽记与落点标记。未提供模型的七种棋子继续走原有程序生成路径。
