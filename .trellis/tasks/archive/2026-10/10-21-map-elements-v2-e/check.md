# 段 E 质检（trellis-check，2026-10-21）

对象：提交 `965b238`（tasks 5.1–5.4）。结论：**通过**（自修 2 处，均为文档；未改代码）。

## 核对结果

1. **5.1 部件资源**：`src/godot/parts/terrain/` 下 `.tscn` 75 件，与 `TerrainParts.All` 23 类按档数推出的文件名集合逐一相等（无缺、无多）；75 件里 `ext_resource` 去重 27 条，与 `materials/` 27 份双向一致（无缺失、无孤儿）；`.import` / `.uid` 0 个。未再跑 `norm_cmp.py`（implement 已记两次导出归一后 102 / 102 相同）。
2. **5.4 设计文档**：版本行 v1.34（main = merge-base `ba99e84` 为 v1.33，+1 符合 M10）；变更记录行：裁决 M1–M10、A1–A3、B1–B5、C1–C3、D1–D4、O-1–O-4 齐全，`water_bed` 不加（B1）、`plate_corner`、`trees` 3→4、`rock` 4→6 与 design 一致，绘制调用 773→615 / 779→622 与 README、implement 一致（未引用合入前 729 / 710），末句写明撞号由负责人合并时调整；行长 1192，在同表范围内（最长 1778）。行尾：index LF、工作区 CRLF（autocrlf），`git diff --stat` 只有改动行，无整文件改动。
3. **HANDOFF**：5 处改动；`--shot-near`"配合 `--shot-cell=`、单给报错"与 `GameRoot.cs` 第 235–238 行（`--shot-near 须与 --shot-cell= 同用。`）一致；截图清单含 `art/map-elements-v2/README.md`；状态为已实施待归档；已归档清单原样，未碰其他线内容。
4. **README**：命令参数（`--map=` `--auto-demo` `--seed=` `--rounds=` `--overview` `--screenshot=路径:帧` `--perf-sample=` `--shot-cell=` `--shot-near`）均为 `GameRoot` 现有选项；表格数字与 implement.md 及段 A / D 记录一致；关键格与 `Siege.Sim map` 现输出一致——`link:1`：R8–V8 h2、M8 / W8 h1、C4–F4 / H4 深水 + G4 桥、D8 + D9–F9 湖、P6 + P7 桥、K4–K8 / O6 岩石、G8 G9 P8 浅滩、V7 P5 T4 岩台、K14 M14 荒漠、D15 F15 沼泽；`link:7`：C10–H10 h2、E10 h1。
5. **规格差分**：`openspec validate map-elements-v2 --strict` 通过；Scenario 坐标（R8–V8、M8、C4–F4、G9 / F9）与合入后地图一致；MODIFIED 两条以合入后主规格现行文本为底，只按 M6 改动（单字母列标"画面相同"→"字形、位置与间距相同"；删"决赛链接之后""与删除旧地图之前逐像素相同"，加 1.2 倍，保留"演出期间不整层重建"）。
6. **复跑**：`dotnet build src/godot/Siege.Godot.csproj` 0 警告 0 错误；`-- --auto-demo` 退出 0（`[parts]` 136 / 760 / 759 件、程序生成 0；`[flow]` 5 条失败 0；error / exception 0）；`-- --auto-demo --pick-check --seed=5 --map=link:7` 退出 0（`[parts]` 136 / 794、程序生成 0；212 / 212 通过；"中心" 4 格、"最近" 2 格被 C10–H10 高台遮挡，属遮挡不计失败，与 implement 记的 `link:1` 右下角 5 格同类）。`Siege.Core` / `Presentation` / `Sim` / `tests` 相对 main 无改动；本次只改文档，`dotnet test` 未重跑（沿用 2153 通过 / 7 跳过），测试不读设计文档 / HANDOFF / `art/map-elements-v2/README.md`（tests 里读 README 的只有 `视觉方向基准Tests` 读 `art/style-exploration/README.md`）。

## 自修

1. 设计文档 §20 "地图元素部件化与造型精修"条目 816 → 406 字符（相邻加粗条 340 / 359；implement"与可改造目标高亮相当"不成立）：删去具体数字（23 类 / 75 件 / 27 份、0.92–1.0、≥ 0.78、≤ 0.07 / 0.14，变更记录行已有）；"底座外圈石沿低于坐标标注平面"改为"坐标标注不移动、不被石沿遮挡"（design D5 是"标注平面之外或更低"）。按 CRLF 写回，CRLF 1062 / 仅 LF 0，diff 1 行。
2. `art/map-elements-v2/README.md` 人工清单补第 6 项：MODIFIED 的「双字母列标不重叠」「大图渲染开销」（5.3 要求规格场景逐项列出，原清单缺这两条）。文件仍为 LF（`\r` 0 个），diff 1 行。

## 遗留 / 归档前负责人要做的

1. `art/map-elements-v2/README.md` 清单与 `after/` 截图由负责人逐项确认后再归档。
2. 规格 Scenario「缓坡与崖壁可辨」写的是 M8（高 1），README 的 `link1-R8-near.png` 写"与缓坡 W8"；近景是否拍到 M8 未看图核实（M8 离 R8 五列）。负责人看图时确认；拍不到就把 Scenario 改成 W8 或补一张 M8 近景。
3. v1.34 若与同期线（`expert-tiebreak`、`terrain-density` 等）撞号，合并时调整（变更记录行已写明）。HANDOFF「现行规则速览（设计文档 v1.33）」标题没改（规则未变），要统一版本号可一并改。
4. `openspec archive map-elements-v2` 会把 `visual-style-baseline` 3 条 ADDED、2 条 MODIFIED 并入主规格，归档后跑 `openspec validate --specs --strict`。
5. 已提交内容里 `pines_*` / `ruins_*` 自动节点名序号随重导变化、81 个 `M` 只差随机 id / 行尾（implement 5.1 已记），归档记录保留这句。
6. D8：`art/` 下其他线的旧截图不追溯重拍。
