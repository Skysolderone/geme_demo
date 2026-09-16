# 实现记录：09-16-board-coordinates

## 改了什么

| 文件 | 改动 |
|---|---|
| `src/godot/scripts/BoardGeometry.cs` | 新增 `ColumnLabelAnchor` / `RowLabelAnchor` 与 `LabelMargin`，锚点由 `Center` 推出 |
| `src/godot/scripts/BoardView.cs` | `BuildCoordinateLabels` 建四边标注；`Label` 工厂 |
| `src/godot/scripts/Visuals.cs` | 标注字色与描边色 |
| `tests/.../VisualStyleBaseline/棋盘坐标标注Tests.cs` | 新建，4 条守门 |
| `tests/.../BatchPreview/Godot层不含规则计算Tests.cs` | 既有守门的精确计数 4 → 6（见下） |
| `art/coord-labels/` | 两张验证截图与人工检查清单 |

## 三次返工

**第一次：字色太暗。** 初版取深灰（74,72,68），想着"比格子暗、比网格线亮"，但棋盘外是深色背景，标注根本读不出来。改为浅色（206,202,190）加深色描边。

**第二次：近大远小。** 标注是 3D 物件，近边的 `A` 胀到压住底部面板，远边的 `A` 小到看不清。坐标是读数不是景物，改用 `FixedSize = true`，近端与远端一样大。

**第三次：全部左右镜像。** 这次最值得记。`BillboardModeEnum.Enabled` 与 `FixedY` 都让 Label3D 渲染文字面的背面——billboard 让节点的 −Z 轴指向相机，而文字画在 +Z 面。四边二十多个标注全是镜像，而我第一眼没看出来：数字里的 `1` `0` `8` 左右对称，字母 `A` 也接近对称，必须盯着 `2` `3` `5` `7` 和 `B` `K` `L` 才能发现。

排查时先试 `FixedY`（无效），再关掉 billboard 加 180° 旋转（字被俯视压扁），最后确认对局相机本身是固定的（`BoardView` 里 `Position = (0, 9.0, 10.4)`，`InputBindings` 没有任何相机旋转绑定），"相机转到背面"这个初稿设想的情形根本不存在。改为绕 X 轴 −90° 平铺在棋盘平面上，像围棋棋盘边缘印刷的坐标。

规格随之修订：「相机旋转时保持正对玩家」改为「平铺在棋盘平面，在对局相机下正向可读」，Scenario「旋转后仍可读」改为「不镜像不倒置」。裁决记在 design.md §2.1。

## 守门与变异

`src/godot/` 用独立解决方案、不进 `siege.sln`，`dotnet test` 对它完全隐身，所以只能做源码文本扫描。

| 编号 | 改了什么 | 红 | 红在哪 |
|---|---|---:|---|
| M-BC1 | `BoardView` 的标注文本改用本地字母表 `"ABCDEFGHIJK"[x]`（含 `I`） | 1 | `表现层不得自带跳过I的列字母表` |
| M-BC2 | `ColumnLabelAnchor` 的偏移方向由 `Z` 改成 `X`（列标注挤到左右两侧） | 1 | `标注锚点由格心推出且四边各在一侧` |
| M-BC3 | `BillboardModeEnum.Disabled` 改回 `Enabled` | 1 | `标注不用billboard` |

第四条守门 `标注文本取自坐标记法而非自行推算` 在写完当场就抓到一处真问题：实现已改为平铺，而 `Label` 方法的 doc 注释里仍留着 `<see cref="BaseMaterial3D.BillboardModeEnum.Enabled"/>` 的说明——注释与实现不一致，测试红。这条不是我事后补的变异，是它自己抓出来的。

## 既有守门的计数更新

`Godot层不含规则计算Tests.坐标映射在Godot侧唯一` 精确断言 `BoardGeometry.cs` 里居中换算出现 4 次，新增两个锚点方法后变成 6，全量测试因此红 1。

这是真实变化不是回归：6 处的构成是 `Center` 的 x / z 各一处、`TryFromWorld` 反算的 x / y 各一处、两个锚点各取一次边界行列。已更新期望值并在注释里写明构成。保留精确值而不改成下界，是因为 `BoardGeometry` 每多一处换算都该有人复审一次，确认它不是第二份映射。

## 结果

全量 `dotnet test` 退出码 **0**，**672 通过**（基线 668，新增 4）。Godot 工程 `--build-solutions` 退出码 0。
人工验证两张截图（`art/coord-labels/`），十项清单逐条核对通过；已知限制两条记在该目录的 README。
