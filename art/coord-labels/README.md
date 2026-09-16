# 棋盘坐标标注 · 人工检查清单

change `board-coordinates`。标注的正确性是视觉性的，自动化只能钉住"表现层不得自带第二份字母表"
（`tests/Siege.Core.Tests/VisualStyleBaseline/棋盘坐标标注Tests.cs`），其余必须看图逐字核对。

截图用：

```bash
"D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe" --path src/godot --quit-after 6000 -- --auto-demo --seed=12345 "--screenshot=E:/wws/geme_demo/art/coord-labels/board-with-coordinates.png:70"
```

注意不能加 `--headless`，无头模式没有可截取的画面。

## 逐项核对

| # | 检查项 | 怎么看 |
|---|---|---|
| 1 | 四边都有标注 | 上下两边是字母，左右两边是数字 |
| 2 | 列字母跳过 `I` | 从左往右读：A B C D E F G H **J** K L，`H` 之后直接是 `J` |
| 3 | 行数字自下而上 | 最下面一行是 `1`，最上面一行是 `11`，不是反的 |
| 4 | `A1` 在左下角 | 左下角格子的列标注是 `A`、行标注是 `1` |
| 5 | **字形不镜像** | 逐字辨认 `2` `3` `5` `7` 与 `B` `K` `L`。`1` `0` `8` 左右对称，看不出问题，**不能只看这几个** |
| 6 | 字形不倒置 | 数字与字母的上下方向与界面其他文字一致 |
| 7 | 左右两边同一行读数相同 | 任取一行，左边与右边的数字相同 |
| 8 | 近端与远端一样大 | 最下一行的 `A` 与最上一行的 `A` 字号相同（`FixedSize` 生效） |
| 9 | 不遮挡格子判读 | 边缘格上有棋子时，该格是否可落子、归属于谁仍然清晰 |
| 10 | 与日志一致 | 任取三个格子，界面标注与同一局日志里的坐标相同 |

第 5 项是最容易漏的一条：Godot 的 Label3D 在 billboard 下渲染文字面的背面，四边标注会全部左右镜像，
而对称字形完全看不出异常。实现因此改为平铺在棋盘平面（`BillboardModeEnum.Disabled` + 绕 X 轴 −90°），
守门测试 `标注不用billboard` 挡住"将来有人顺手把 billboard 打开"。

## 已知限制

- 左下角的 `A` 与 `1` 被手牌面板部分遮挡。面板是半透明叠加层，标注仍可辨认，未做避让。
- 对局相机固定俯视，没有旋转绑定。**若将来引入相机旋转**，平铺的标注会随视角倒转，需要另行处理；
  不要简单地打开 billboard，那会直接回到镜像。见 `openspec/changes/board-coordinates/design.md` §2.1。

## 存档

| 文件 | 内容 |
|---|---|
| `board-with-coordinates.png` | 种子 12345 第 4 大回合终局，四边标注齐全 |
| `early-board.png` | 种子 777，边缘格有棋子时不遮挡判读 |
