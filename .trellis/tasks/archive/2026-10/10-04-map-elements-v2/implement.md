# 实施

规格权威在 `openspec/changes/map-elements-v2/`；这里只写执行顺序、本机命令、验证口径与文档产出。

## 执行约定

- 全程在本机（macOS / Apple M3 / Metal）做，前后对照一律用本机读数与截图（负责人 2026-10-04）。
- 主会话直接实现，不派子 agent（负责人要求派发前先确认，未确认）。同一时间只跑一个 dotnet / Godot。
- 每段单独提交，提交前先问负责人；`git commit -F`，不加 Co-Authored-By。
- 段 A 做完停下，交截图给负责人确认方向，确认后才做段 B–E。
- 回滚点：`BoardView.cs` / `TerrainParts.cs` / `LowPoly.cs` / `LowPolyMesh.cs` / `src/godot/parts/terrain/`。

## 本机命令

```bash
G="$HOME/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot"
NUGET="-p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config"   # 仓库里的 nuget.config 写死了 Windows 路径
dotnet build src/godot/Siege.Godot.csproj -v q $NUGET                            # Godot 读 Debug 程序集，验证前必须先构建
"$G" --path src/godot --quit-after 3000 -- --auto-demo --map=<图> --screenshot=<绝对路径.png>:1 [--perf-sample=3] [--overview] [--shot-ownership] [--no-batch]
"$G" --headless --path src/godot -- --auto-demo --pick-check --map=<图>          # 退出码 0 为通过
"$G" --headless --path src/godot -- --export-parts=res://parts/terrain
dotnet test tests/Siege.Core.Tests -v q                                          # 本机基线 11 红（日志黄金哈希），见实现记录
```

截图比对：本机没有 PIL / ImageMagick，用 numpy + 标准库解 PNG 的脚本逐像素比（输出差异像素数、外接矩形、差异图），裁图放大用 ffmpeg。

## 分段步骤与验证

| 段 | tasks | 做什么 | 验证（全部要有命令输出或截图为证） |
|---|---|---|---|
| 0 | 0.1 0.2 | 基线、`--no-batch` | 已做，见实现记录；0.2 的逐像素项待段 C 重跑 |
| A | 1.1 | `LowPolyMesh`（`BeveledSlab` / `Strata` / `FacetRock` / `Prism` / `Ribbon`，整数散列） | 导出时每件部件建两次、顶点数组逐项相同；`--piece-gallery` 前后逐像素相同 |
| A | 1.2 | 部件目录加锚点说明与 `tile_top` / `side_slope` / `side_cliff` / `liner` / `scene_slab` | `--export-parts` 成功，日志列出新增部件 |
| A | 1.3 | `AddTileStack` / `AddSceneSlabs` 取部件，材质按底色共用、顶点色当亮度系数 | `[parts]` 程序建模 = 0；`--shot-ownership` 压暗照常；出生区淡染与明暗格照常；`--pick-check` 三张图通过；绘制调用对照 1.2 倍上限 |
| A | 1.4 | 段 A 截图（v5 近景、七种地表彩色 + 灰度） | 负责人确认方向 |
| B | 2.1 2.2 | `water_bed` / `bank_lip`；桥、栅栏重建 | 水面可见宽度 ≥ 0.78（量像素）；桥齐平可拾取；栅栏不占格 |
| C | 3.1 3.2 | 岩石 / 松树 / 遗迹 / 小树与四种地表点缀重建；岩石缩放烘进顶点 | 合批日志变体数与档数一致；重跑 0.2 的逐像素比对；灰度七种地表可辨 |
| D | 4.1–4.3 | `rim` / `zone_strip` / `plate_frame` / 浮岛部件 | 列标位置不变（标注区域像素比对）；45 列图双字母列标不相接；边缘格棋子底座完整 |
| E | 5.1–5.4 | 全量重导、资源缺失回退、改后读数、清单、设计文档 | 读数对照表；`openspec validate map-elements-v2 --strict`；build 0 警告；测试红数不多于本机基线 |

每段收尾固定动作：构建 0 警告 → 守门测试（红数与本机基线对照）→ `--pick-check` → 截图存 `art/map-elements-v2/` → 勾 `tasks.md` → 把数字写进下面的实现记录 → 向负责人汇报并问是否提交。

## 文档产出规划

| 文档 | 何时写 | 内容 |
|---|---|---|
| 本文件「实现记录」 | 每段收尾 | 读数、比对结果、偏离设计之处及原因 |
| `openspec/changes/map-elements-v2/tasks.md` | 每项完成即勾 | 只勾验证通过的；没通过的留空并在实现记录写明 |
| `openspec/changes/map-elements-v2/design.md` | 实施中发现设计要改时 | 追加「实施修正」小节（如顶点色 8 位上限的处理），不改已定裁决 |
| `art/map-elements-v2/README.md` | 段 A 建骨架并填段 A；之后每段补一节；段 E 定稿（tasks 5.3） | 人工检查清单：按规格场景逐项列出，改前改后对照图、灰度图、读数表、待负责人勾选 |
| 设计文档 §20 与变更记录 | 段 E（tasks 5.4） | 补一条「地图元素部件化与造型精修」，变更记录加一行 |
| `.trellis/spec/` | 段 E 收尾（trellis-update-spec） | 候选条目：非均匀缩放在 MultiMesh 下受光不一致；顶点色是 8 位、亮度系数超过 1 要靠材质增益；跨机器读数不可比 |
| `.trellis/workspace/rubioc/journal-1.md` | 每次提交后 | 一段一条 |
| `PartExport.cs` / `TerrainParts.cs` 的类注释 | 随代码 | 部件清单、锚点约定、覆盖材质约定 |

不新增其他文档。

## 实现记录

### 0.1 改前基线（2026-10-04，HEAD 00a6747）

环境：macOS / Apple M3 / Metal 4.0 Forward+，Godot 4.7.2 .NET，窗口 1600×900，`--auto-demo --map=<图> --screenshot=<png>:1 --perf-sample=3`。
截图与日志在 `art/map-elements-v2/before/`。垂直同步开着（120 Hz），帧间隔被它封顶，只有绘制调用 / 对象 / 图元有区分度。

| 地图 | 绘制调用 | 1.2 倍上限 | 对象 | 图元 | 帧间隔 | 引擎单帧处理 |
|---|---|---|---|---|---|---|
| `siege-4p-base-v5` | 642 | 770 | 1646 | 30753 | 8.54 ms | 10.76 ms |
| `board:1` | 520 | 624 | 2101 | 250949 | 8.33 ms | 9.63 ms |
| `board:1:n10` | 466 | 559 | 2516 | 396993 | 8.37 ms | 10.44 ms |
| `gen:12345:s1` | 1317 | 1580 | 4215 | 98081 | 8.42 ms | 10.85 ms |

前后读数必须在同一台机器上取：board-render-perf 的归档读数是 Windows 上第 10 帧的，与这里不可比。

### 0.2 `--no-batch`

开关已加（`GameRoot` 读选项 → `BoardView.NoBatch`）：障碍不建合批模板、逐格 `TerrainParts.Create`；场景铺面逐格一个 `MeshInstance3D`。严格 CLI 认它（退出码 0）。
`board:1` 合批 520 次绘制调用 / 逐格 731 次。合批那张与 0.1 的 `board1.png` 哈希相同（同参数重复取图逐像素相同）。

**逐像素比对未通过（本机）**：合批与逐格相差 54042 像素、最大通道差 70，全部落在岩石上（`board1-batch-vs-nobatch-diff.png`）。
松树、遗迹、场景铺面 0 差异；岩石三块球里带非均匀缩放 (1, 0.9, 1.1) / (1, 0.9, 1) 的两块受光不同，均匀缩放的第三块逐像素相同。
即 MultiMesh 实例变换里的非均匀缩放与逐节点的法线变换不一致。这是改动前就有的差异（部件与合批代码未动），board-render-perf 在 Windows 上记录的是 0 差异。
处置建议：段 C 用 `FacetRock` 重建岩石时把缩放烘进顶点，部件子节点只留刚体变换，两种画法即一致；届时重跑本项。

### 本机测试基线

`dotnet test tests/Siege.Core.Tests`：2030 过 / 11 红 / 9 跳过。11 条全是日志黄金哈希（`三档旧难度逐步不变`、`未登记的地图首部逐字节不变`、`专家前瞻的记录Tests` 三条、`内容集v1的报告…逐字节相同`），
与 Godot 脚本无关（`src/godot` 不在 `siege.sln`）；扫描 Godot 脚本的守门测试全过。

### 段 A——建模工具与地块（2026-10-04）

改动：新增 `LowPolyMesh.cs`；`LowPoly.cs` 加 `TileTop` / `SideSlope` / `SideCliff` / `Liner` / `SceneSlab`，`Prism` 改为调用 `LowPolyMesh.Prism`；
`TerrainParts.cs` 的 `Kind` 加锚点与覆盖材质标记、登记五类部件、新增 `ShapeOf` / `CountPlaced`；`BoardView.cs` 的 `AddTileStack` / `AddSceneSlabs` 取部件、面砖与衬底材质改 `Visuals.Shaded`；
`PartExport.cs` 加带顶点色材质的命名、逐类日志、确定性自检；`GameRoot.cs` 加 `--shot-zoom=`。新导出 10 个 `.tscn` 与 3 份 `shaded_*.tres`；旧部件重导后只变随机 id，已恢复为 HEAD，不进提交。
与设计的出入记在 `openspec/changes/map-elements-v2/design.md`「实施修正」F-1 – F-8。

验证（截图与日志在 `art/map-elements-v2/stage-a/`，清单在 `art/map-elements-v2/README.md`）：

| tasks | 验证项 | 结果 |
|---|---|---|
| 1.1 | 同一 seed 两次建模顶点数组逐项相同 | 导出自检 53 / 53；两次独立导出网格数据逐字节相同。变异：`Unit` 里混入 `Random.Shared` → 8 件报红、退出码 1；还原后 0 件 |
| 1.1 | 棋子画面不变 | `--piece-gallery` 前后逐字节相同 |
| 1.2 | 导出成功、日志列出新增部件 | 53 / 53；`tile_top` ×3（24 个三角形）、`side_slope` ×2（42）、`side_cliff` ×3（最多 146）、`liner`（10）、`scene_slab`（10） |
| 1.3 | 资源 = 全部、程序建模 = 0 | v5 434 / 0，`board:1` 1976 / 0，`board:1:n10` 3680 / 0，`gen:12345:s1` 1776 / 0 |
| 1.3 | 信息层压暗照常 | `compare-ownership.png`；压暗后地砖平均色与改前相差不到 1 级 |
| 1.3 | 出生区淡染、明暗棋盘格照常 | `compare-v5.png` |
| 1.3 | `--pick-check` | v5 105 / 105，`gen:12345:s1` 370 / 370，`board:1` 471 / 471，退出码 0 |
| 1.4 | 段 A 截图 | 已出；**等负责人确认方向**，tasks 1.4 未勾 |

绘制调用：v5 642 → 699（1.09）、`board:1` 520 → 584（1.12）、`board:1:n10` 466 → 530（1.14）、`gen:12345:s1` 1317 → 1406（1.07），都在 1.2 倍内，但棋盘图余量只剩 30–40 次。
颜色：`board:1` 场景铺面区域改前改后 90000 像素里 20 个差 1 级；地砖区域平均色暗约 1%。
重复取图：`board:1` 0 像素差；v5 只在水面区域（x 574–998、y 283–645）不同。
测试：Godot 工程与 `siege.sln` 构建 0 警告；全量测试失败集合与本机改动前逐条相同（11 条）。

未做 / 遗留：
- 0.2 的合批 / 逐格逐像素比对仍未通过（岩石），留到段 C。
- 资源缺失退回程序建模的验证在段 E（5.1）。
- 本机 11 条日志黄金哈希红测的原因未查（不是换行符：日志用 `StringBuilder` 拼接，地图文件是 LF）。
- 临时"改前"工作树（HEAD + 截图选项）已移除；要重拍改前图时再建。

### 段 B——水系与设施（2026-10-04）

改动：`LowPolyMesh` 重构为 `Builder`（多形状合一网格，新增 `Box` / `Cone` / `Disc` / `Sheet`；重构后地块网格数值不变，`liner` / `scene_slab` 只有 −0.0 → +0.0）；
`LowPoly` 新增 `WaterBed` / `BankLip`，重建 `Bridge` / `Fence`；`BoardView.AddWater` 取部件、全图水格共用一份水色材质、逐边放石沿，桥按通行方向摆放。
实施修正 F-9 – F-12。

| tasks | 验证项 | 结果 |
|---|---|---|
| 2.1 | 一格宽主河水面可见宽度 ≥ 0.78 | 约 0.88（v5 近景量得水面 88 像素 / 格宽约 100 像素；石沿每侧 0.06） |
| 2.1 | 浅滩与深水可辨 | `stage-b/surfaces-demo-near-G7.png` |
| 2.2 | 桥面齐平、可拾取；栅栏不占格 | `stage-b/bridge-fence-lip-x2.png`；`--pick-check` v5 105 / 105、`gen:12345:s1` 370 / 370、`board:1` 471 / 471 |

绘制调用：v5 617（改前 642）、`board:1` 584、`board:1:n10` 530、`gen:12345:s1` 1069（改前 1317）。测试失败集合与本机基线相同（11 条）。
中途缺陷：`gen:12345:s1` 上水格贴图边时用负坐标构造 `Coord` 抛异常（进程仍以 0 退出、只是没出截图）；已改为先判界再构造，拍图脚本加了"无截图 / 有异常"检查。

### 段 C——障碍与装饰（2026-10-04）

改动：`LowPoly` 的 `Rock` / `Pines` / `Ruins` / `Trees` / `Desert` / `Marsh` / `Crag` / `Shallows` 全部用 `LowPolyMesh.Builder` 重建（`Builder.Within` 给整件部件套总变换）；
档数 rock 4→6、pines 7→8、ruins 4→6；程序建模回退也按档数取模（design O-4）。

| tasks | 验证项 | 结果 |
|---|---|---|
| 0.2 | `board:1` 合批与逐格逐像素相同 | 0 像素差（岩石的缩放已烘进顶点） |
| 3.1 | 合批日志变体数与档数一致 | `board:1` 变体 20 种 = 6 + 8 + 6；v5 15 种（36 格障碍没用到全部档） |
| 3.1 | 装饰高 ≤ 0.75；林地小树在底座之外 | 资源包围盒：rock 0.47、pines 0.75、ruins 0.63；trees 顶 0.28，树心离格心 ≥ 0.31、树冠半径 0.09 |
| 3.2 | 形状约束与灰度可辨 | 沼泽无锥形树冠；岩台石沿 0.03；浅滩点缀顶 0.03（鹅卵石压扁到 0.03 以内）；灰度图见 `stage-c/` |

绘制调用：v5 533、`board:1` 497、`board:1:n10` 468、`gen:12345:s1` 750（改前 642 / 520 / 466 / 1317）。`--pick-check` 三张图通过；测试失败集合与本机基线相同。

### 段 D——外框与底座（2026-10-04）

改动：`LowPoly` 新增 `Rim` / `ZoneStrip` / `PlateFrame` / `IslandLayer` / `IslandSpike` / `Cloud`；`BoardView` 新增 `AddRim`，出生区亮条、台面边框、浮岛岩层 / 垂岩 / 云团改为取部件（`AddShape` 加带变换与不投影的重载）；
`PartExport` 支持不受光材质（`flat_*.tres`）。实施修正 F-13 – F-16。

| tasks | 验证项 | 结果 |
|---|---|---|
| 4.1 | v5 单字母列标字形、位置不变 | 与改前逐像素比对：底边列标带 34040 像素 0 差异；差异图上棋盘与石沿之间整圈无变化（`stage-d/v5-diff-vs-before.png`） |
| 4.1 | 45 列棋盘图双字母列标不相接 | `stage-d/board1-overview.png` / `board1-labels.png` |
| 4.2 | 出生 / 公共可分；边缘格棋子底座完整；v5 无台面边框 | `stage-d/board1-board0.png`、`board1-board0-f60.png`、`v5.png` |
| 4.3 | 全局预览 | `stage-d/board1-overview.png`（v5 一屏看全，`--overview` 不动相机） |

绘制调用：v5 512、`board:1` 472、`board:1:n10` 444、`gen:12345:s1` 728。`--pick-check` 三张图通过；测试失败集合与本机基线相同。
说明：底座之下的岩层与垂岩在 60° 俯角的对局相机与全局预览下都被底座挡住，改前改后都看不到；截图只能验证云团与外圈石沿。

### 段 E——收尾（2026-10-04）

| tasks | 验证项 | 结果 |
|---|---|---|
| 5.1 | 全量重导，目录只留本次产物 | 清空后导出：部件 79 / 79（24 类）、共享材质 25 份；删掉 18 份旧 `matte_*.tres`；`.tscn` 里不再引用它们 |
| 5.1 | 资源缺失退回程序建模 | 移走资源目录：`board:1` 资源 0 / 程序生成 2529，截图与资源版 0 像素差；`gen:12345:s1` 资源 0 / 程序生成 2550，只有水面流纹区域不同；目录已移回 |
| 5.2 | 绘制调用 ≤ 改前 1.2 倍 | 见下表，全部低于改前 |
| 5.3 | 人工清单、重复取图 | `art/map-elements-v2/README.md` 定稿；全量重导前后 `board:1` / `board:1:n10` 0 像素差，v5 只在水面区域不同 |
| 5.4 | 设计文档、构建、测试、校验 | §20 补「地图元素部件化与造型精修」，版本 v1.19 → v1.20，变更记录加一行；构建 0 警告；`openspec validate --strict` 通过；测试见下 |

| 地图 | 绘制调用 改前 → 改后 | 倍数 | 对象 | 图元 改前 → 改后 | 帧间隔 |
|---|---|---|---|---|---|
| `siege-4p-base-v5` | 642 → 512 | 0.80 | 1646 → 1712 | 30753 → 70473 | 8.54 → 8.34 ms |
| `board:1` | 520 → 472 | 0.91 | 2101 → 2258 | 250949 → 234475 | 8.33 → 8.35 ms |
| `board:1:n10` | 466 → 444 | 0.95 | 2516 → 2711 | 396993 → 414059 | 8.37 → 8.34 ms |
| `gen:12345:s1` | 1317 → 728 | 0.55 | 4215 → 4237 | 98081 → 201153 | 8.42 → 8.32 ms |

测试：`dotnet test` 2030 过 / 11 红 / 9 跳过。在干净的 00a6747 工作树上复跑，失败集合与改动后逐条相同——这 11 条在本机上改动前就红，tasks 5.4 的"全绿"在本机上以"失败集合不变"为准；Windows 机上需另行确认。

决策清单与遗留见 `art/map-elements-v2/README.md` 文末。
