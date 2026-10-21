# 段 C 质检记录（trellis-check，2026-10-21，tasks 3.1–3.2）

## 结论

**通过（自动项）**：代码、资源、守门与复跑均符合 design D1 / D7、tasks 3.1–3.2 与 visual-style-baseline 两条需求；自修 2 处（文档 / 注释，未改网格函数，无需重导）。3.3 看图与下列三项待裁决属负责人项，不在本次通过范围内。

## 核对

1. **形状约束**（复跑 `check_parts.gd`，读 `res://parts/terrain` 32 件，结果与实施者表格逐项一致）
   - 沼泽：只有 `Patch` 水洼 + `Frustum(…, 3 棱, 尖顶)` 芦苇叶 + 竖长 `FacetRock` 香蒲穗，无锥形树冠；最高 0.210，r < 0.36 内最高 0.007（水洼），y > 0.02 最近半径 0.391–0.412。
   - 岩台：石沿顶 0.026–0.03（与改前同高、内缘 0.05 宽），块底探到 −0.03 补顶板倒角缝；最高 0.050–0.056 来自两颗压扁碎石（内沿 ≈ 0.376）；r < 0.36 内只有 0.004 裂纹。不读成高一层（边缘加厚 + 顶高远低于一层）。
   - 浅滩：鹅卵石圈半径 0.402，最高 0.023–0.025，r < 0.36 内只有 0.003 水纹；地砖本身未动，齐平。
   - 荒漠：仙人掌顶 0.230（主柱 0.19 + 尖 0.04），侧臂沿格角切向（与径向点积为 0），三组点缀都在格角；y > 0.02 最近半径 0.399–0.416。
   - 林地：三棵在格角（reach 0.33–0.34），树冠内沿 ≈ 0.38，圆柱内无几何，最高 0.247–0.259（3.1 的上限是 0.75；0.25 只约束四种新地表）。
   - 岩石：最高 0.362–0.400 ≤ 0.75，|x| / |z| ≤ 0.407 < 0.45 不出格；无锥形、无绿色。
   - 采样法可信：变换链 `xf * child.transform` 正确；N = 10 时最长相关边的采样间距远小于离 0.36 的余量（最小余量约 0.028）。须两列合看——`nearestHigh` 只统计 y > 0.02，贴地几何进圆由 `innerMaxY` 兜住（现全为 ≤ 0.007 的贴地纹理）。我按代码手算的树冠 / 仙人掌 / 芦苇 / 鹅卵石 / 碎石内沿与岩石高度与脚本一致。
2. **网格函数**
   - `FacetRock` 缺省 `stretch ?? Vector3.One`，逐分量 × 1.0f 在 float 下恒等，段 A / B 调用（`island_*` 等）输出逐值不变；拉伸后法线仍由 `Tri` 按三角面求，正对角缩放不改变"面心·法线"的符号，朝外判定不变。
   - `Frustum`：尖顶分支提示向量化简为 `(b0+b1)/3`、棱台分支为底顶环中点的水平径向，倾斜（lean 是剪切）不改变侧面外法线的水平分量，所以 lean > 底半径的芦苇叶也朝外；顶 Up、底 Down；闭合（插地的底面按设计不建）。
   - `Patch`：从原点出发的星形轮廓，边中点·外法线恒为正，侧面提示可靠；顶面 Up；无底面为设计。
   - `Merge`：六个函数里所有变换只有绕 Y 旋转（`turn`、`QuarterTurn`、`Basis(Up, θ)`）+ 平移，无任何缩放，`AppendFrom` 转法线正确。
3. **确定性**：`LowPoly.cs` / `LowPolyMesh.cs` 无 `Random` / `GetHashCode` / 时间；我另导一次到 scratchpad，与仓库归一比对：本段 32 件 + 13 份 `_vc` 材质 + 段 A / B 部件全部相同，只剩 `pines_*` / `ruins_*`（仅 `@MeshInstance3D@N` 自动序号不同，已人工核）与 12 份仓库孤儿材质——与实施者记录一致，确认"仓库资源 = 当前代码"。
4. **守门与边界**：逐函数比对 `HEAD` 与现版 `LowPoly.cs`——`Piece` / `Body` / `Prism` / `PrismFace` / `Fence` / `Bridge` / `Pines` / `Ruins` 等全部逐字未变，只改 `Rock` / `Trees` / `Desert` / `Marsh` / `Crag` / `Shallows` / `Shaded`（加缺省粗糙度参数，`Fence` / `Bridge` 调用不变）并新增 `Merged` / `QuarterTurn` / `Wrap`；`LowPoly.cs` 55 405 字节（约 4.6 万字符，远超守门的 10 000 字符；implement.md 写的"字符"实为字节，不影响结论）、含 `private static Node3D[] Body(`。`BoardView.cs` / `Visuals.cs` / Core / Presentation / Sim / tests 无改动，`AddPlaceholders`、`AddTint(...)` 字面、`ObstacleArtAt` 分流与 `_decoration` 挂点原样。`Visuals.Matte` 每次 new，`Shaded` 打开顶点色不会污染共享材质。
5. **档数与资源**：`parts/terrain` 文件档数 rock 6 / trees 4 / desert 6 / marsh 6 / crag 4 / shallows 6 = `LowPoly.*Variants`，无残留旧档；pines 7 / ruins 4 的 `.tscn` 相对 `HEAD` 无变化。每件 2–3 个 surface、根下一层各一 `MeshInstance3D`、`material_override` 指向 `_vc` 材质，满足 `NewPartBatch` 的 `IsPlainPart` 形状。
6. **风格**：命名、注释风格与段 B 一致；构建 0 警告即无未用变量。

## 自修

1. `implement.md` 第 21 行材质标签对调了：`matte_9e9383_r100_vc` 是岩台石沿（`TileCrag.Lightened(0.08)` = 158,147,131），`matte_a8a092_r100_vc` 是鹅卵石（`Visuals.Pebble` = 168,160,146）。代码与资源本身正确（`crag_*` 引 9e9383、`shallows_*` 引 a8a092），只改了记录。
2. `TerrainParts.Template` 的注释"程序生成的造型随 variant 连续变化（朝向不按档数取模）"对岩石已不成立，改成限定松树 / 遗迹。纯注释，构建复核 0 警告。

## 复跑（同一时间只一个进程）

- `dotnet build src/godot/Siege.Godot.csproj --no-incremental`（Debug）：0 警告 0 错误。
- Godot `--script check_parts.gd -- res://parts/terrain`：退出 0，数值见上。
- Godot `--export-parts=<scratchpad>`：退出 0，`部件 58 / 58、共享材质 23`，三角形数与实施者记录逐项相同；归一比对见第 3 条。
- Godot `-- --auto-demo`：退出 0；`[plates] 连体棋盘（出生区 1+2+3+4+5，218 格）：外轮廓边框 8 段，接缝格线 4 段，接缝边框 0 段`、`[zone] 出生区描边：出生区 1 12 段、…、出生区 5 12 段；棋盘内岩石 0 格（按棋盘格画）` 与段 A 归档 0.1 基线逐字相同；`[parts] 资源 473–474 件、程序生成 0 件`；`[water] 深水 6 格（架桥 2 格）、河岸石沿 16 件`；`[flow] 自检 5 条，失败 0 条`；日志 error / exception 0。
- Godot `-- --auto-demo --pick-check --seed=5 --map=link:7`：退出 0；`[plates] …（227 格）：外轮廓边框 10 段，接缝格线 4 段，接缝边框 0 段`、`[zone] … 各 12 段；棋盘内岩石 2 格（按棋盘格画）` 与基线相同；`[parts] 资源 497 件、程序生成 0 件`；`[pick-check] 可落子格 218，至少在一个位姿下验到 218；通过`；error / exception 0。
- `dotnet test siege.sln`：退出 0，`Siege.Core.Tests` 失败 0、通过 2100、跳过 6（总 2106，9 分 5 秒），构建无警告；源码扫描守门随之通过。之后只改了 `TerrainParts.Template` 一行注释，Debug 构建复核 0 警告 0 错误，语义无差，未重跑测试。
- 收尾 `git status`：无 `.godot/`、`.import`、资源漂移（导出只写到 scratchpad）。

## 三项待裁决（只判属实与副作用，不替裁）

1. **荒漠 3 个 surface**：属实（`Cactus` / `Bone` / `Ripple` 三个 `Merged`）。D6 是"尽量一个"、任务写 1–2，属软偏离；副作用是每个荒漠格比 2 surface 多 1 次绘制，但较改前 8 个已大降；`NewPartBatch` 对子节点数无限制，不影响合批。去掉沙纹会少一条灰度通道，实施者的理由成立。
2. **trees 3→4 档**：属实（`TreesVariants = 4` + 新 `trees_3.tscn`），超出 3.1 字面范围（只写岩石扩档）；副作用仅 `variant++` 的轮转周期变化，无代码或测试钉 3 档。
3. **岩石略矮**：属实（改前 0.43 → 现 0.362–0.400，仍 ≤ 0.75，水平占位相近），纯体量观感问题，归 3.3 看图（`link:7` Q16、U15）。

## 遗留（非本段阻断）

- 12 份孤儿材质（11 份本段换 `_vc` 后无引用 + 段 B 的 `matte_b0804e_r100`）按 B4 先例留给 5.1 全量重导时清理。
- 信息层打开时 `Refresh` 把装饰朝格心缩到约 0.846，缩后几何会进 0.36 圆——既有压淡机制，规格核默认视图，本段不处理。
- headless 验不到的灰度可辨 / "一格岩石"观感留给 3.3 截图。
- 四种地表的贴地纹理（水洼 0.007、沙纹 / 裂纹 0.004、水纹 0.003）按 terrain-surfaces 既有设计就在底座圆内；规格场景「新地表装饰不遮挡判读」是"棋子位于该格上"，而 3.3 计划的三张近景（K14、V8、Q16）都没放棋子——是否加一张带棋子的沼泽 / 浅滩近景由主会话决定。
