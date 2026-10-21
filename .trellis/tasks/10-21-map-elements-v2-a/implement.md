# 段 A 实现记录

## 0.1 改前基线（主会话，2026-10-21，提交 f484857 之上 + `--shot-near`）

截图（非 headless，`$G = Godot_v4.7.2-stable_mono_win64_console.exe`，均 `--auto-demo --seed=5 --rounds=1`，第 12 帧）存 `art/map-elements-v2/before/`：

| 文件 | 命令附加参数 |
|---|---|
| `link1-overview.png` | `--map=link:1 --overview --perf-sample=4` |
| `link7-overview.png` | `--map=link:7 --overview --perf-sample=4` |
| `link1-S4-near.png` | `--map=link:1 --shot-cell=S4 --shot-near`（崖壁 S4–T4、缓坡 U4） |
| `link1-E8-near.png` | `--map=link:1 --shot-cell=E8 --shot-near`（一格宽河 D8–G8、桥 F8） |
| `link7-Q16-near.png` | `--map=link:7 --shot-cell=Q16 --shot-near`（棋盘内岩石 Q16、U15） |

`--shot-near`（新增，仅截图用）：配合 `--shot-cell=` 把缩放拉到最近；一屏看得全的图上 `--shot-cell=` 单用不会拉近。

`[perf]`（窗口模式，锁定后全局预览静止帧；帧率受窗口前后台影响，只比绘制调用）：

| 图 | 绘制调用 | 对象 | 图元 |
|---|---|---|---|
| `link:1` | 729 | 1979 | 27295 |
| `link:7` | 710 | 2025 | 27419 |

headless `--auto-demo` 自证行（锁定后；`link:1` 缺省，`link:7` 加 `--map=link:7`）：

- `link:1`：`[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`；`[zone] 出生区描边：出生区 1 12 段、出生区 2 12 段、出生区 3 12 段、出生区 4 12 段、出生区 5 12 段；棋盘内岩石 0 格（按棋盘格画）`；`[batch] 地砖与台面归并…：面砖材质 18 份、衬底材质 18 份、描边 / 边框材质 6 份、盒网格 18 份`；`[parts] 资源 27–28 件、程序生成 0 件`。
- `link:7`：`[plates] 连体棋盘（出生区 1+2+3+4+5，227 格）：外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段`；`[zone] … 各 12 段；棋盘内岩石 2 格（按棋盘格画）`；`[batch] … 面砖材质 18 份、衬底材质 18 份、描边 / 边框材质 6 份、盒网格 20 份`；`[parts] 资源 29–32 件、程序生成 0 件`。
- 两图 `[batch] 障碍装饰合批 0 格 … 场景面砖 0 格、MultiMesh 0 个`。
