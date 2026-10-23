# 十种棋子美术源与运行时资源

本包沿用明快的低多边形奇幻棋盘方向。主体是带切面的暖石，凹槽为暮光灰紫，镶边为哑光旧金；珐琅、布面和底座环沿用红、蓝、金、紫四方主色。棋子类型与轮廓的唯一映射仍在 `PieceStyleTable`，阵营主色与三角、塔楼、太阳、莲花徽记仍在 `FactionTable` / `Emblems`。

| 棋子 | Godot 场景 | 远景轮廓 |
|---|---|---|
| 普通子 | `src/godot/parts/pieces/basic.tscn` | 窄身圆头兵，雕刻肩披与金属眉饰 |
| 堡垒子 | `src/godot/parts/pieces/fortress.tscn` | 高石塔、顶圈与四枚独立雉堞 |
| 连珠子 | `src/godot/parts/pieces/line.tscn` | 左右实心双珠与横向金属连梁 |
| 倍增子 | `src/godot/parts/pieces/multiplier.tscn` | 四面石锥与向四方伸出的短芒 |
| 协同子 | `src/godot/parts/pieces/synergy.tscn` | 中心晶核、四枚外倾晶瓣与环形金属托 |
| 匠人 | `src/godot/parts/pieces/artisan.tscn` | 向一侧倾斜的槌柄、横向槌头和斜撑 |
| 旗手子 | `src/godot/parts/pieces/bannerman.tscn` | 全组最高的直立细杆与右侧分叉长旗 |
| 铁链子 | `src/godot/parts/pieces/chain.tscn` | 一正一侧的两枚中空竖环相扣 |
| 哨兵子 | `src/godot/parts/pieces/sentry.tscn` | 两柄交叉成 X 的长矛 |
| 界碑子 | `src/godot/parts/pieces/boundary.tscn` | 低矮宽石板、两侧石墩与双横刻痕 |

可编辑的参数化模型源是 `art/full-art-v1/source/pieces-generate.py`；生成的 `.tscn` 带红方预览材质，可直接在 Godot 编辑器中检查网格层级、变换和材质分层。运行时材质由 `PieceModels` 按阵营重建。修改轮廓请先改生成源再重生成，因为脚本会覆盖这十个场景。命令：

```powershell
python art/full-art-v1/source/pieces-generate.py
```

底座的雕刻石脚、阵营色珐琅带、顶部石沿、旧金铆钉和原有旗帜徽记由 `src/godot/scripts/PieceModels.cs` 构建；徽记网格仍取 `LowPoly.EmblemMesh`，因此 HUD 与盘面同源。场景节点名前缀是材质槽：`Stone_`、`Shade_`、`Metal_`、`Enamel_`、`Cloth_`、`Crystal_`、`Ink_`。加载场景时，同槽几何与对应底座几何各合成一份共享 `ArrayMesh`；每枚棋子再为各槽创建独立 `StandardMaterial3D`。这样暂放透明发光不会串到普通棋子，且底座细节不增加额外绘制调用。若某场景不存在或无法合并，`BoardView.BuildPiece` 仍回退 `LowPoly.Piece`。

验收入口：`--piece-gallery=<PNG 路径>` 与对局盘面共用 `BuildPiece`。图库日志应出现十条 `[piece-models] 场景已加载 ...（10/10）`，没有“程序回退”。人工检查原图、去色原图、400×225 去色缩略图，尤其比较连珠/铁链、堡垒/界碑、匠人/旗手，以及普通/界碑。实局图检查四方主色、徽记、格线和落点。资源为 Godot 原生可编辑场景，本版没有 `.glb`。

## 本轮实机核对

- `dotnet build src/godot/Siege.Godot.csproj --no-restore`：0 警告、0 错误。
- `art/full-art-v1/pieces-gallery.png`：图库一次运行依序打印十条新场景加载记录，末条为 `CrossedSpears ...（10/10）`；截图成功，没有回退记录。`pieces-gallery-gray.png` 和 `pieces-gallery-gray-small.png` 是同图的去色与 400×225 缩略版。
- `art/full-art-v1/four-factions.png`：`link:1`、`--rounds=0`、种子 9、第 118 帧，四方棋子同屏；`four-factions-gray.png` 用于去色检查。此帧在模型未合槽时为 3255 绘制调用，合并本体后 1997，底座合槽并移除多余几何云后的最终运行记录为 1551；上一版满盘参考约 1868。帧率受其他场景资源及运行状态影响，不把这单帧数值当成通用帧率保证。
- 临时移走 `basic.tscn` 后，图库打印 `[piece-models] 程序回退 RoundPawn` 且成功截图；随即恢复场景并删掉回退测试截图。恢复后重跑图库仍打印 `10/10`。
- 暂放材质审查：`MaterialFor` 和徽记每次建棋子都会创建新的 `StandardMaterial3D`，缓存中只有网格，因此预演材质不会改写普通棋子的材质。Godot 的透明通道会让合槽模型的上半部难以辨认；最终改用独立的亮色实体轮廓、发光材质、半透明落点和阵营色边框。`staged-preview.png` 是种子 9、第 5 帧匠人暂放的实机图，棋形与落点均可辨认。
