# implement：link-map-terrain

前置条件：`retire-legacy-maps` 已提交并归档，工作区干净，然后 `opsx:propose link-map-terrain`，再 `task.py start`。
每段派一个 `trellis-implement`（写代码、测试、变异记录，不 commit、不改 openspec/）。派之前把本段待决项拟成编号裁决一次性确认。主会话前台复核后提交。同一时间只跑一个任务。

## 段 A：Core 生成与校验（R1–R5、R7）

- [ ] A1 先红：`BoardTheme`、`BoardPlate.Theme`、`MapFile` 往返（链接档缺 `Theme` 字段要报错）
- [ ] A2 先红后绿：`Battlefields` 的棋盘归属不再取左下角格（AC3）
- [ ] A3 `BirthZones` 改为核心区；用 `codegraph_callers` 全量复核使用者，凡是要整块棋盘的改读 `Boards`（含 `SeatPublicView.cs:146`）
- [ ] A4 生成器：宽高 6–8、主题排列、五个主题的局部生成、岩石、信物（design §2）
- [ ] A5 校验器链接行按 design §3 改写，每条新失败码配一个拒绝算例（AC4）
- [ ] A6 种子 1–20：结构断言（AC1、AC2）+ 全部链接序列穷举 + 区间实测回写
- [ ] A7 旧摘要日志被拒的测试（design §6）；`link:1` 决策序列基线与受影响的黄金值重做，在提交说明里逐条列出

## 段 B：公开视图、Sim 与冒烟（R6、R8、AC5、AC6）

- [ ] B1 `BoardPlaceholder.Theme` 与 `information-visibility` 的 Scenario 测试
- [ ] B2 终端版占位显示主题；`map` 子命令打印主题与地形；`analyze` 按主题统计（棋盘胜率 / 名次、匠人改造次数）
- [ ] B3 冒烟：`run --map link:1 --seed 1 --count 10 --difficulty Standard`，再换 `--map-per-match` 跑 10 局，共 ≤ 20 局；和 F.5 基线对比，主题偏差明显时调 design §2 的初值并回写
- [ ] B4 AI 与地形相关的权重标"未校准"（不调权重）

## 段 C：图形版（AC5、AC7）

- [ ] C1 占位显示主题名；核心区和外圈的视觉区分（如核心区的嵌入亮条改成按核心区画）
- [ ] C2 headless 自检四条；非 headless 截图（五个主题各一张 + 拼接后一张），放进 `art/link-map-terrain/`
- [ ] C3 负责人试玩（可玩优先：段 A 一完成就先给终端版的试玩入口）

## 段 D：收尾

- [ ] D1 设计文档 §3.3 改写，升到 v1.21；HANDOFF 同步
- [ ] D2 `openspec validate link-map-terrain --strict` → 归档 → `task.py archive` → journal

## 验证命令（顺序单跑，看真实退出码）

```powershell
dotnet build siege.sln -c Release
dotnet test -c Release
dotnet run --project src/Siege.Sim -c Release -- map --map link:1 --links 1-4E,2-5E,1-2N
$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
& $G --headless --path src/godot --build-solutions --quit
& $G --headless --path src/godot -- --auto-demo
& $G --headless --path src/godot -- --auto-demo --pick-check --seed=5
& $G --headless --path src/godot -- --map-select --auto-demo
& $G --headless --path src/godot -- --auto-demo --rounds=0 --seed=5
```

## 风险点

- `BirthZones` 语义变化牵连面最大（A3）：漏改一处，表现就是占位尺寸错、取景错、出生区信物报错。所以 A3 要独立成一步，单独复核。
- 6×6 棋盘的中间带只有 7 格，主题可能放不下：生成器必须按面积缩放，并且穷举证明从不抛异常。
- `--pick-check` 的拾取数和取景依赖棋盘尺寸，6–8 的边长会改变 30/30、55/55 这类计数，要按实测回写，并在提交说明里写清楚。
