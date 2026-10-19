# open-map 段 A 截图与人工检查清单

> 截图命令（非 headless，PowerShell）：
> `$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"`
> - 锁定后全图（第 12 帧，第 1 大回合）：`& $G --path src/godot -- --auto-demo --seed=5 --overview --screenshot=E:/wws/geme_demo/art/open-map/locked-full-map.png:12 --rounds=1`
> - 全图解禁后跨棋盘落子（第 120 帧，第 10 大回合）：`& $G --path src/godot -- --auto-demo --seed=5 --overview --screenshot=E:/wws/geme_demo/art/open-map/round4-cross-board.png:120 --rounds=0`
>
> （`--screenshot=路径:帧号`；路径须写绝对路径——相对路径按 Godot 工程目录 `src/godot` 解析。`--overview` 让相机切到全局预览，一屏看到整张地图。）

| 文件 | 看什么 |
|---|---|
| `locked-full-map.png` | 插旗锁定后 5 块棋盘全部展开、没有占位轮廓；接缝处台面连成一片（无边框、无缝隙）；画布底座覆盖 5 块棋盘的包围盒，不画包围盒之外的留白 |
| `round4-cross-board.png` | 第 4 大回合起（图为第 10 大回合）AI / 本机的棋子出现在全部 5 块棋盘上；势力排名栏四人数值都可见、没有"势力不可见"行 |

人工检查清单（负责人试玩）：

1. 插旗阶段仍只有 5 个占位（只有台面边框与编号 / 主题名，没有格线与棋子），可点选。
2. 点一块占位锁定后，5 块棋盘一起展开、相机重定界到整张地图可见（一屏看不全时以本机棋盘为中心）。
3. 第 1–3 大回合：只能在自己的出生棋盘内暂放；靠接缝的棋子的气伸进邻居棋盘（气层可见）；匠人改造目标不越棋盘（E 键轮换时没有邻居棋盘的格 / 边）。
4. 第 4 大回合起：可以在任一棋盘落子，包括无人锁定的棋盘。
5. 整轮 Pass / 填满 / 只剩一人 → 终局横幅（没有"战场胜出 / 落败"横幅、没有倒计时提示条）。
6. M 键全局预览在一屏看不全时仍可用；空格回自己的出生棋盘。
