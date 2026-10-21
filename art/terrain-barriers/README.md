# terrain-barriers 截图与人工检查清单

> 截图命令（非 headless；先 `dotnet build src/godot/Siege.Godot.csproj` 构建 Debug 程序集）：
> `$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"`
> - 高台：`& $G --path src/godot -- --auto-demo --zone=1 --shot-board=0 --rounds=1 --screenshot=E:/wws/geme_demo/art/terrain-barriers/theme-plateau.png:12`
> - 河流：`& $G --path src/godot -- --auto-demo --zone=4 --shot-board=3 --rounds=1 --screenshot=E:/wws/geme_demo/art/terrain-barriers/theme-river.png:12`
>
> 控制台自证行：`[siege] 截图取景：棋盘清单第 0 块（出生棋盘，6×7，左下角 R3）` / `第 3 块（出生棋盘，6×8，左下角 C3）`；
> `[siege] 截图 …：Ok（1600×900，第 12 帧，第 1 大回合，信息层 关，手牌信息面板 关，中央面板 关）`。
> 无人值守缺省地图 `link:1`（`Siege.Sim map --map link:1` 可核对）：1 号 高台 R3–W9、2 号 栅栏阵 J10–O15、3 号 混合 J3–Q9、4 号 河流 C3–H10、5 号 林地 C11–H16。

| 文件 | 看什么 |
|---|---|
| `theme-plateau.png` | 高台（R3–W9）：第 8 行 R8–V8 是高 2 的山脊、W8 是高 1 的坡口（山脊东端，在外圈），山脊两侧都是高 0；山脊横穿整块棋盘，两侧之间只有坡口一处可走。公共信物在山脊上 S8。 |
| `theme-river.png` | 河流（C3–H10）：第 4 行 C4–H4 是深水河道、G4 是预置桥，河道两端 C4 / H4 在外圈；河道把棋盘分成南侧一行（C3–H3，含公共信物 C3）与核心区一侧。另有湖泊 D8 / D9 / E9 / F9（未架桥深水），浅滩 G8 / G9。 |

人工检查清单（负责人看图）：

1. 隔断线一眼可读：山脊的两道崖壁色带、河道的低水面都横贯整块棋盘，端点落在外圈格上、与相邻棋盘的接缝处没有缺块或穿帮。
2. 通道可辨：坡口（一道色带）与山脊（两道色带）分得开；桥面与地砖齐平、能看出是唯一过河处。
3. 外圈出现深水 / 崖壁（本 change 首次）：外圈的河道端格、山脊端格渲染正常，棋盘外框与接缝格线连续；若有明显缺陷，登记给后续 Godot change（本 change 只要求 headless 自检通过）。
4. 湖泊 / 岩区：大棋盘中间带的一团深水或岩石不切断棋盘；6×6 / 6×7 小棋盘没有湖泊 / 岩区（实施修订 J4）。
