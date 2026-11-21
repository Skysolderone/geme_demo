# 设计：卡通棋子（v2）

## 资产管线（沿用 v1）

```
tools/blender/chibi_pieces.py ──Blender -b──▶ src/godot/parts/pieces_chibi/<key>.glb ──Godot --import──▶ .import（入库）
PieceModels.TryBuild：pieces_chibi/<key>.glb → parts/pieces/<key>.tscn → LowPoly
```

- 生成：`/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python tools/blender/chibi_pieces.py -- src/godot/parts/pieces_chibi [key...]`
- `<key>` 与 tscn 同名：basic / fortress / line / multiplier / synergy / artisan / bannerman / chain / sentry / boundary。

## glb 契约（v2）

- 物体名 = `<槽>_<部件名>`。槽 ∈ Stone / Shade / Metal / Enamel / Cloth / Crystal / Ink / **Line**（新增：描边外壳）。
- **Line 槽**：脚本对所有本体零件合并后的网格做反向外壳（Solidify 负厚度 + 翻转法线，或等价做法），厚度 0.03（v2 实测：0.016 在棋盘上只有约 1 px），命名为 `Line_Hull`。运行时材质为 cull front、无光照、深色。
- **锚点**：空物体 `Anchor_Emblem`，它的位置和朝向就是徽记的摆放处（+Z 为徽记正面法线方向），缩放表示徽记尺寸。`MergeScene` 遇到非 MeshInstance 节点时照常递归，需要额外记录这个锚点的变换。
- 不建父子层级；`export_apply=True`；`export_materials="NONE"`。
- 坐标：Blender -Y = Godot +Z = 朝镜头；**角色和建筑从 y = 0 站起**（直接站在格面上；细圆环在脚外一圈，不垫脚。v2 第一轮的薄方块顶面在 0.02，已弃用），占地半宽 ≤ 0.42（不超出格子）；道具可以外伸，但不能遮住相邻格的主体。

## 运行时（PieceModels）

- glb 路径不再追加八角底座、铆钉和印章；改为在本体脚下追加一圈细圆环（外半径约 0.36、环宽约 0.05、高约 0.02，Enamel 槽，顶点色压暗），合并进 Enamel 槽，不增加绘制调用。圆环 24 段、只有顶面与外壁，共 96 个三角形，计入每颗本体 ≤ 1200 的预算（脚本里的 `RUNTIME_RING_TRIS`）。整格实心方块在 v2 看图时太抢眼，并且会和领地着色混在一起，所以弃用。tscn / LowPoly 回退路径保持旧样式（继续追加旧底座和印章）。
- 徽记：沿用 `LowPoly.EmblemMesh(emblem, size)` 和 `Visuals.Flat(ink)`，摆到 `Anchor_Emblem` 的变换上，稍微往法线方向偏一点防止 z-fighting。
- 材质：`MaterialFor` 加卡通版本，用 `DiffuseMode = Toon`、`SpecularMode = Toon` 或 `Disabled`，饱和度上调；颜色继续按槽取。
  - Line 槽：`ShadingMode = Unshaded`、`CullMode = Front`、深色，例如 `Color8(28, 24, 30)`，不投射阴影。描边外壳单独作为一个 MeshInstance，设置 `CastShadow = Off`。
- 暂放半透明（`BoardView.Translucent` 改的是 MaterialOverride）：要确认描边材质也被替换或调透明度。实现时读 `BoardView` 的暂放代码再定。
- 拾取（`BoardView.Picking` 读取网格 / 材质）：要确认描边外壳不会让拾取范围变形或出错。

## 取舍

- 描边用反向外壳而不是后处理：不改全局渲染，棋盘不受影响，每颗只多一次绘制调用。
- 回退造型不改：降低改动面，glb 缺失只是"变回旧样式"。
- 卡通着色只作用于棋子：棋盘风格不统一的问题另开任务。

## 回滚

删掉 `src/godot/parts/pieces_chibi/` 就回到旧造型。PieceModels 的 glb 分支只在加载到 glb 时才会走到。
