# 段 E 实现记录

## 5.1 全量重导（trellis-implement，2026-10-21）

做法：先 `git archive HEAD src/godot/parts/terrain` 留底到 scratchpad，再整目录删除 `src/godot/parts/terrain/` 后重导，目录里天然只剩本次导出的产物（`PartExport.Run` 会把 `materials/` 建回来）。本轮不再还原 `pines_*` / `ruins_*` 的节点名漂移，全部以本次导出为准（任务要求）。

- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：退出 0，0 警告 0 错误。
- `& $G --headless --path src/godot -- --export-parts=res://parts/terrain`：退出 0，`[export-parts] 部件 75 / 75、共享材质 27 → res://parts/terrain`；日志无 error。
- 目录清单：`.tscn` 75 件（23 类：`tile_top` 3、`side_slope` 2、`side_cliff` 3、`liner` 1、`bank_lip` 3、`bridge` 1、`fence_x` / `fence_z` 各 1、`rock` 6、`trees` 4、`desert` 6、`marsh` 6、`crag` 4、`shallows` 6、`pines` 7、`ruins` 4、`rim` 3、`zone_strip` 1、`plate_frame` 2、`plate_corner` 1、`island_layer` 3、`island_spike` 4、`cloud` 3），`materials/*.tres` 27 份；没有 `.import` / `.uid` 或其他文件（`find` 核过，导出前后都没有）。`.tscn` / `.tres` 文件头不带 `uid=`，删后重建不涉及 UID 缓存。
- 引用完整性：75 件 `.tscn` 里 `ext_resource path=` 去重后恰 27 条，与 `materials/` 的 27 个文件名双向一致（每个被引用的材质都存在，每份材质至少被一件引用）；`TerrainParts.All` 的每档都导出成功（75 / 75），目录又是清空后重导的，所以目录文件 = 导出清单，没有多余文件。
- 与 HEAD 比对（`python -I …-c/norm_cmp.py <HEAD 留底> res://parts/terrain src/godot/parts/terrain res://parts/terrain`）：`files=102 raw_same=0 norm_same=91 diff=11 missing=12`。
  - `missing` 12 份 = 删除的孤儿材质（只在 HEAD 一侧）：`matte_465c60_r35`、`matte_60564a_r100`、`matte_6e9656_r100`、`matte_7a7773_r100`、`matte_8e8a86_r100`、`matte_9e9383_r100`、`matte_a8a092_r100`、`matte_b0804e_r100`（段 B，裁决 B4）、`matte_b09c60_r100`、`matte_cbbe8f_r100`、`matte_e8f6fa_r40`、`matte_eee6d4_r100`（均 `.tres`，与段 C 偏离 2 的清单逐一相同）。`matte_308048_r100`、`matte_705436_r100` 仍被 `pines_*` 引用，保留。
  - `diff` 11 件 = `pines_0–6`、`ruins_0–3`：归一后逐行比对，差异行只有 `[node name="@MeshInstance3D@<序号>" …]` 的自动节点名序号（脚本归类后只剩这一种行形），网格、材质、变换不变。按本轮要求以新导出为准，不还原。
  - 其余 91 个文件归一后相同；`raw_same=0` 是因为 `git archive` 出的是 CRLF、Godot 写的是 LF，外加随机 id。
  - 预期的 git 噪音：`git status` 显示 12 个 `D`（上列孤儿材质）与 81 个 `M`（75 件 `.tscn` + 6 份不带 `_vc` 的旧材质 `matte_308048 / 498f5e / 705436 / 837d71 / a0988a / deb03a_r100`）。这些 `M` 的实质内容只有随机 id / `unique_id` / 行尾（以及 `pines` / `ruins` 的节点名序号），见上条归一比对；`_vc` 与 `flat_` 材质原样未变。
- 可复现：再导一次到 scratchpad 的 `e2`（退出 0，`部件 75 / 75、共享材质 27`），`norm_cmp.py src/godot/parts/terrain res://parts/terrain <e2> <e2>`：`files=102 raw_same=27 norm_same=102 diff=0 missing=0`——仓库与临时导出归一后逐字节相同。
- 退路：先跑一次有资源的 `-- --auto-demo` 取对照（退出 0；`[parts] 资源 136 / 759 / 760 件、程序生成 0 件`），再把 `src/godot/parts/terrain` 整个挪到 scratchpad（不在工程树里改名，免得 Godot 扫到）跑 `-- --auto-demo`：退出 0，`[parts] 本次搭建地形部件：资源 0 件、程序生成 136 件` / `资源 0 件、程序生成 759 件` / `资源 0 件、程序生成 760 件`（三次搭建与有资源时件数逐个相同，全部程序生成）；`[zone]` 两行、`[plates]` 两行与有资源时 `diff` 无输出；`[flow] 自检 5 条，失败 0 条`；error / exception 0 处。随后挪回，`git status --porcelain` 与挪走前逐字节相同。
  - 合入后（`terrain-barriers`）`link:1` 的自证行：`[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`；`[zone] 出生区描边：出生区 1 12 段、出生区 2 12 段、出生区 3 12 段、出生区 4 12 段、出生区 5 12 段；棋盘内岩石 6 格（按棋盘格画）`；`[water] 深水 12 格（架桥 2 格）、河岸石沿 30 件`；`[rim] 底座外圈石沿 84 件`；`[frame] 连体外轮廓边框件 68 件、转角件 8 件（按 8 段逐格边摆放）`。

## 5.4 文档与全量验证（trellis-implement，2026-10-21）

### 文档

- `2026-09-10-siege-core-gameplay-design-v1.md`：文首版本行 v1.33 → **v1.34**（最近一次为 map-elements-v2）；§20 在"可改造目标高亮"之后、"默认 UI"之前加一条 **地图元素部件化与造型精修**（`map-elements-v2`，v1.34）——§20 现有的都是加粗要点条，没有 `###` 小节，按现有体例写成一条，篇幅与"可改造目标高亮"相当；「变更记录」表末追加 `| 2026-10-21 | change \`map-elements-v2\`（v1.33 → v1.34） | … |`，绘制调用"改后"两格写占位"见 `art/map-elements-v2/README.md`"（待主会话 5.2 回填），行末一句撞号由负责人合并时调整。文件是 CRLF，用脚本按 CRLF 写入。
- `HANDOFF.md`（只改 5 处）：文首日期行改为 map-elements-v2 实施待归档（v1.34）；命令行「截图」加 `--shot-near`（配合 `--shot-cell=` 拉到最近缩放，map-elements-v2 新增；单给报错——`GameRoot.cs` 第 237 行核过）；「自检命令」截图清单加 `art/map-elements-v2/README.md`；「后续 change」末段"未开工的 change `map-elements-v2`（0/17）"改为已实施待归档；「权威来源」设计文档版本 v1.33 → v1.34。「现行规则速览（设计文档 v1.33）」标题未改（规则未变）。
- `openspec/changes/map-elements-v2/tasks.md`：勾掉 5.1、5.4（5.2、5.3 留给主会话）。

### 全量验证（同一时间只跑一个 dotnet / Godot 进程；Godot 前已做 Debug 构建）

| # | 命令 | 退出码 | 关键输出 |
|---|---|---|---|
| 1 | `dotnet build siege.sln -c Release` | 0 | 0 个警告、0 个错误 |
| 2 | `dotnet build src/godot/Siege.Godot.csproj`（Debug，另加 `--no-incremental` 全量重建一次） | 0 / 0 | 0 个警告、0 个错误 |
| 3 | `dotnet test siege.sln` | 0 | `已通过! - 失败: 0，通过: 2153，已跳过: 7，总计: 2160，持续时间: 8 m 17 s`（与 HANDOFF 记的 terrain-barriers 后实测同数；源码扫描守门随之通过） |
| 4 | `-- --auto-demo` | 0 | `[flow] 自检 5 条，失败 0 条`；`[parts] 资源 136 / 759 / 760 件、程序生成 0 件` |
| 5 | `-- --auto-demo --pick-check --seed=5` | 0 | `可落子格 202，至少在一个位姿下验到 202；通过`；逐格居中 202 / 202（最近、最远各 202 / 202，失败 0）；"右下角"位姿 5 格被 R8–V8 高台山脊挡住（合入后新地形，属正常，判定通过） |
| 6 | `-- --map-select --auto-demo` | 0 | `[flow] 自检 5 条，失败 0 条`；9 次搭建全部 `程序生成 0 件` |
| 7 | `-- --auto-demo --rounds=0 --seed=5` | 0 | `[flow] 自检 7 条，失败 0 条，已见步骤：插旗锁定 → 构筑期 → 全图解禁 → 终局；通过`；13 次搭建全部 `程序生成 0 件` |
| 8 | `-- --auto-demo --pick-check --seed=5 --map=link:7` | 0 | `可落子格 212，至少在一个位姿下验到 212；通过`；逐格居中 212 / 212；`[parts] 资源 136 / 794 件、程序生成 0 件` |
| 9 | `openspec validate map-elements-v2 --strict`（勾完 tasks 后跑） | 0 | `Change 'map-elements-v2' is valid` |

5.4 的五份 Godot 日志（加 5.1 的有资源 / 无资源两份共七份）error / exception 均 0 处。

合入后（`terrain-barriers`）的自证行新值（与段 A–D 记录的旧基线不同属正常）：

- `link:1`：`[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`；`[zone] 出生区描边：出生区 1–5 各 12 段；棋盘内岩石 6 格（按棋盘格画）`；`[water] 深水 12 格（架桥 2 格）、河岸石沿 30 件`；`[rim] 底座外圈石沿 84 件`；`[frame] 连体外轮廓边框件 68 件、转角件 8 件（按 8 段逐格边摆放）`；`[batch] … 面砖材质 19 份、衬底材质 19 份、描边 / 边框材质 6 份、盒网格 6 份、地块部件网格 8 份`；场景面砖 0 格、MultiMesh 0 个。
- `link:7`：`[plates] 连体棋盘（出生区 1+2+3+4+5，227 格）：外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段`；`[zone] … 各 12 段；棋盘内岩石 0 格（按棋盘格画）`（合入后 `link:7` 没有棋盘内岩石，与 design Context 表"合入后"一行未列岩区一致；段 C 验收用的 Q16 / U15 岩石已不在，棋盘内岩石现由 `link:1` 的 K4–K8 / O6 岩区承担，`link:1` 计 6 格）；`[water] 深水 17 格（架桥 2 格）、河岸石沿 42 件`；`[rim] 84 件`；`[frame] 边框件 84 件、转角件 10 件（按 10 段逐格边摆放）`。

### 遗留

- `pines_*` / `ruins_*` 的自动节点名序号已随本次导出改变（不再是 HEAD 版本），内容不变；按本轮要求不还原。
- 重导让 75 件 `.tscn` 与 6 份旧材质在 git 里显示为修改，实质只差随机 id / 行尾（见 5.1 归一比对）。
- 变更记录行里的绘制调用"改后"读数是占位，待主会话 5.2 回填；5.3 README 与截图由主会话做。
- 合入后 `[zone]` / `[plates]` 没有"部件化之前"的合入后基线可比（`44f60c4` 只记了绘制调用与全景截图）；本段只证明了有资源与退回程序建模两次逐字相同，变更记录行按此措辞。

## 5.2 / 5.3（主会话）

- 绘制调用（窗口模式锁定后全局预览静止帧，合入后地图）：`link:1` 773 → 615、`link:7` 779 → 622（≈ 0.80 倍，上限 1.2 倍）；改前读数取自 main ba99e84 的临时副本（`art/map-elements-v2/before-barriers/`）。已回填设计文档变更记录。
- 终版截图 `art/map-elements-v2/after/`（全景 2 张、近景 6 张、灰度、重复取图与差异图）；重复取图差异 8331 像素全在水面（99.6% 偏蓝）。
- `link1-K14-near.png`：棋子站在荒漠 K14 上，底座完整可见（规格「新地表装饰不遮挡判读」）。
- 人工检查清单 `art/map-elements-v2/README.md`。
