# map-elements-v2 截图与人工检查清单

> 截图命令（非 headless，PowerShell；先 `dotnet build src/godot/Siege.Godot.csproj` 构建 Debug 程序集）：
> `$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"`
> - 全景：`& $G --path src/godot -- --map=link:<N> --auto-demo --seed=5 --rounds=1 --overview --screenshot=<绝对路径>:12 --perf-sample=4`
> - 近景：`& $G --path src/godot -- --map=link:<N> --auto-demo --seed=5 --rounds=1 --shot-cell=<坐标> --shot-near --screenshot=<绝对路径>:12`（`--shot-near` 为本 change 新增：配合 `--shot-cell=` 拉到最近缩放）
> - 灰度图：Pillow `ImageOps.grayscale`，文件名加 `-gray`。

## 目录

| 目录 | 内容 |
|---|---|
| `before/` | 0.1 改前基线（`terrain-barriers` 合入前的地图） |
| `seg-a/` … `seg-d/` | 各段完成时交负责人过目的截图（合入前的地图） |
| `before-barriers/` | 合入后地图 + 改前画面（main ba99e84 渲染）全景，5.2 的改前读数来源 |
| `after/` | 合入后地图 + 本 change 终版（段 E） |

## 绘制调用（窗口模式，锁定后全局预览静止帧）

| 地图版本 | 图 | 改前 | 改后 | 比值 |
|---|---|---|---|---|
| 合入前 | `link:1` | 729 | 575（段 D） | 0.79 |
| 合入前 | `link:7` | 710 | 578（段 D） | 0.81 |
| 合入后 | `link:1` | 773 | 615 | 0.80 |
| 合入后 | `link:7` | 779 | 622 | 0.80 |

规格「大图渲染开销」要求不超过 1.2 倍，满足。帧率受窗口前后台影响，只比绘制调用。

## 终版截图（`after/`，合入后地图）

| 文件 | 看什么 |
|---|---|
| `link1-overview.png` / `-gray` | 全图：七种地表灰度可辨；底座外圈石沿、外轮廓边框、出生区亮条、浮岛 |
| `link7-overview.png` / `-gray` | 第二张图全景 |
| `link1-R8-near.png` | 高台山脊 R8–V8（高 2 崖壁，带岩缝与岩沿）与缓坡 W8 |
| `link1-E4-near.png` | 横穿棋盘的河 C4–F4、H4 + 桥 G4：河岸石沿，C4 在棋盘外缘收口 |
| `link1-K6-near.png` | 棋盘内岩区 K4–K8：多面岩块，读作"一格岩石"而非林地 |
| `link1-G9-near.png` | 浅滩 G8 / G9 挨着湖泊 D9–F9：浅滩齐平、深水低于地砖 |
| `link1-K14-near.png` | 荒漠 K14 上站着棋子：点缀在格角，棋子底座完整可见；沼泽 D15 / F15 |
| `link7-E10-near.png` | 高台山脊 C10–H10 与坡口 E10（高 1） |
| `link1-overview-repeat.png` / `-diff.png` | 同参数重复取图：差异 8331 像素，全部落在水面（99.6% 偏蓝，差异图只覆盖水格），水面之外逐像素相同 |

## 人工检查清单（规格 Scenario 逐项）

1. **地图元素部件化**：`[parts]` 程序生成 0 件；资源目录挪走后全部程序生成、照常搭建（段 E 5.1 实测）；两次导出归一随机编号后逐字节相同；同图同画面（见上 repeat）；拾取 `link:1` 202 / 202、`link:7` 212 / 212。
2. **地块、侧面与水系的造型**：地砖倒角与缝；草地 3 档色差不盖过明暗交替；崖壁与缓坡可分（`R8-near`）；灰度七种地表可辨（`link1-overview-gray`）；深水是有石沿的沟、一格宽河水面可见宽度 0.88 格、与浅滩可辨（`E4-near`、`G9-near`）。
3. **外框与底座的造型**：坐标标注位置与字形未变、不被石沿遮挡（对照 `before/link1-overview.png`）；`[zone]` 各 12 段、`[plates]` 外轮廓 8 / 10 段、接缝格线 4 段、接缝边框 0 段，与有资源 / 退回程序建模两次逐字相同；段 A–D 期间与 0.1 基线逐字相同；边缘格棋子底座完整（`seg-d/link1-R3-near.png`）；占位轮廓代码未改（`占位轮廓灰度Tests`；自动演示第 1 帧已锁定，截不到插旗阶段）。
4. **新地表的灰度可辨 / 不遮挡判读**：沼泽无锥形树冠、岩台不读成高一层、浅滩齐平；点缀高 ≤ 0.25、在棋子底座（半径 0.36）之外（三角面采样核对，见段 C 记录）；`K14-near` 棋子站在荒漠上不被遮挡。
5. **改造的可视表现**：对局中新架的桥、新立的栅栏与预置的取同一部件（新旧同形），地形指纹变化即重搭。

以上由负责人逐项确认。
