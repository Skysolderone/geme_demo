# board-link-mode 截图索引（段 D）

全部 1600×900，非 headless，`--screenshot=` 须给绝对路径（`E:/wws/geme_demo/art/board-link-mode/<名>.png:<帧>`）；每张另有 `-gray.png` 灰度版（PIL `convert("L")`）。命令前缀：`Godot_v4.7.2-stable_mono_win64_console.exe --path src/godot --`。帧号为 `--screenshot` 的取图帧；无人值守下帧序列可复现。

| 文件 | 命令 | 帧 | 看什么 |
|---|---|---|---|
| `new-game` | `--map-select --screenshot=…` | 2 | 新局界面：背景 5 块占位轮廓，面板显示 `link:<种子>`，没有棋盘布局 |
| `flags-placeholders` | `--screenshot=… --overview`（不带 `--auto-demo`，停在插旗） | 2 | 插旗阶段：5 个占位 + 编号，没有任何格 |
| `locked-own-board` | `--auto-demo --seed=5 --screenshot=… --overview` | 3 | 锁定后：自己那块（出生区 1）展开为棋盘，其余 4 块仍是占位 |
| `linked-battlefield` | `--auto-demo --seed=5 --screenshot=… --overview` | 41 | 首轮链接（第 39 帧 1+2）后的合并战场 + 3 块占位；他人战场不显示 |
| `linked-battlefield-closeup` | 同上，不带 `--overview` | 41 | 出生棋盘与新公共棋盘同一套台面 / 地砖 / 格线，无画面差别 |
| `placeholder-closeup` | `--auto-demo --seed=5 --screenshot=… --shot-cell=AX26` | 41 | 占位（出生区 3）近景：只有灰边框与编号（灰度与台面边框差 ≥ 40） |
| `d-c-expand-mid` | `--auto-demo --seed=5 --shot-show=expand --screenshot=…` | 39 | 棋盘展开演出中间帧：公共棋盘与通道升到半高，对方棋盘仍隐藏，相机未拉远 |
| `d-c-field-lost` | `--auto-demo --seed=3 --rounds=10 --shot-show=field --screenshot=…` | 90 | 横幅"战场落败（第 2 名）"，其后才是"红方出局"（第 92 帧结算） |
| `d-c-strip-elsewhere` | `--auto-demo --seed=5 --rounds=12 --screenshot=…` | 110 | 顶部提示条"战场 出生区 5 已结算，胜者 紫方(P3)"（第 104 帧入队，不阻塞） |
| `d-c-overview-field` | `--auto-demo --seed=5 --overview --screenshot=…` | 45 | 全局预览只盖当前战场（合并 1+2 的中心），其余占位不在预览里 |
| `d-d-final-link-overview` | `--auto-demo --rounds=0 --seed=5 --overview --screenshot=…` | 1060 | 决赛链接（第 1052 帧 1+5）后的合并战场全局预览：三块出生棋盘 + 两块公共棋盘 |
| `d-d-match-end` | `--auto-demo --rounds=0 --seed=5 --screenshot=…` | 1200 | 终局面板（第 1190 帧"最后一个战场已结算"）：四人名次，没有重复的战场横幅 |

## 人工试玩检查清单（负责人对照）

1. 占位可辨：插旗与对局中，他人出生棋盘只有灰边框 + 编号，灰度与自己台面边框明显不同；点占位无效果。
2. 展开动画：本机参与链接时依次是 公共棋盘与通道升起 → 相机拉远到合并战场一屏可见 → 对方棋子 / 设施 / 已揭示信物显现 → 横幅"棋盘展开：与 X 交战"，约 2.5 s，按住空格 / 左键提速；他人链接无动画。
3. 横幅顺序：本机战场结算先"战场胜出" / "战场落败（第 N 名）"再出局横幅；决赛结算只有终局横幅，不重复。
4. 他战场数值不可见：势力排名栏、势力层、顺序层、手牌对比里他战场玩家只剩名字 + 在局 / 出局 + "所在战场已结算（胜出）"，无数值、无手牌类型。
5. 相机范围：推屏止于"当前战场 ∪ 5 块占位"的包围盒，空格回家回到自己棋盘；链接后范围扩大、位姿不复位。
6. 全局预览：战场一屏看全时没有"全局 [M]"按钮，链接后出现；预览只盖当前战场。
7. 提示条：他场结算提示依次显示、每条约 3 s 后消失，多条不互相覆盖。
