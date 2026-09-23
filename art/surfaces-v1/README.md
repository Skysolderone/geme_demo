# v1 新地表 · 人工检查清单

change `terrain-surfaces` 收尾（tasks 6.2）。自动化已钉住数据层与视图模型（规则、差集原因、图例、`ShallowsBeside`、`Scored`、源码守门），
**"看不看得出"只能看图**：下面各项由负责人逐项确认，不符即为缺陷。

## 截图取法（自定义参数放在 `--` 之后；不能加 `--headless`）

```bash
G="D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
"$G" --path src/godot --quit-after 3000 -- --auto-demo --map=<仓库绝对路径>/art/surfaces-v1/surfaces-demo.json "--screenshot=<仓库绝对路径>/art/surfaces-v1/surfaces-v1-demo.png:1"
"$G" --path src/godot --quit-after 3000 -- --auto-demo --overview --map=gen:12345:s1 "--screenshot=<仓库绝对路径>/art/surfaces-v1/surfaces-v1-gen12345-s1.png:1"
python -c "from PIL import Image; Image.open('art/surfaces-v1/surfaces-v1-demo.png').convert('L').save('art/surfaces-v1/surfaces-v1-demo-gray.png')"
```

`surfaces-demo.json` 是 `siege-4p-base-v5` 中岛改出新地表的演示图（只用于截图，不进地图目录）：浅滩 E6–E8、荒漠 G8、沼泽 F7 / G7、岩台 G6；
林地 E5 / J5 / E9 / J9、深水与桥是 v5 原有的。`surfaces-v1-demo-closeup.png` 是中岛局部的彩色（左）/ 灰度（右）并排放大。
截图控制台读数：第 1 帧、信息层关、手牌信息面板关、中央面板关。

## 逐项核对

| # | 检查项（规格出处） | 怎么看 |
|---|---|---|
| 1 | 七种地表同屏各自可辨（visual-style-baseline「新地表在默认视图下可辨」） | `demo.png` / `closeup` 左：草地、林地、深水、荒漠、沼泽、岩台、浅滩逐格指得出 |
| 2 | 灰度下四种新地表可辨（「灰度下四种新地表可辨」） | `demo-gray.png` / `closeup` 右。**重点看浅滩与荒漠**：两者去色后都偏亮，靠点缀区分——浅滩是水纹 + 鹅卵石，荒漠是沙纹 + 仙人掌 + 兽骨 |
| 3 | 沼泽不被读成林地（「沼泽不被读成林地」） | F7 / G7：贴地积水 + 芦苇，没有锥形树冠；对照 E5 / J5 的林地小树 |
| 4 | 浅滩不被读成深水（「浅滩不被读成深水」） | E6–E8：水面与同层地砖齐平、有鹅卵石；对照 D 列低于地砖的深蓝深水 |
| 5 | 岩台不被读成高一层（「岩台不被读成高一层」） | G6：只有内缘矮石沿与裂纹，与相邻 h=0 格之间没有崖壁侧面 |
| 6 | 新地表装饰不遮挡判读（「新地表装饰不遮挡判读」） | 需手动：在新地表格上落子，底座、气点、归属标记不被点缀挡住（点缀都在底座半径 0.36 外，高 ≤ 0.25） |
| 7 | 生成图上新地表的分布（map-generation 投放规则） | `gen12345-s1.png`：新地表只在平台之外，浅滩挨着主河 |
| 8 | 信息层标示（tactical-layers「新地表的规则标示」） | 需手动：按 `1` 开盘面层——图例列出本图的新地表；`Tab` 切到棋串读法，贴着棋串的空浅滩是更小的暗灰点、与亮青气点分开；按 `2` 开势力层，独占的荒漠格淡色、不计入领地分 |

## 已知限制

- 新地表的装饰占用 `BoardView.Build` 的变体计数器，含新地表的地图上其后障碍 / 林地装饰的变体会顺移（纯外观、确定性不变）。
- 第 6、8 项截图里没有，需要真机操作。
- AI 对新地表的感知测试与 200 局平衡对照在 `ai-eye` 归档后补做（裁决 8）。
