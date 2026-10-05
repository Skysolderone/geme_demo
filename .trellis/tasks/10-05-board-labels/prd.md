# 10-05-board-labels

> 规格权威：`openspec/changes/board-labels/`（proposal / design / specs / tasks）。

盘面标注可读性加 `MutedText` 收尾（负责人 2026-10-06："所有都需要"；重叠时坐标标注让位）：军势揭示期间被条目压到的远边列字母 / 左右行数字淡出（呈现层按格子邻域给进度）；算式行与结果同色、描边与字号有下限；常驻标注配色由呈现层给出（深色统一描边、四阵营亮度差 ≥ 120）、最右一列换到左前角；`Ui.MutedText` 的透明度进 `UiTheme`。规则、计分、演出节奏、存档、日志不变。

验收：`openspec/changes/board-labels/tasks.md` 各条的验证项。

约束：
- 在工作树 `.claude/worktrees/board-labels`、分支 `feat/board-labels` 上做，基于 main `f3108b1`；不要碰主工作树，不要切分支。
- 呈现层纯函数、整数运算、零 Godot 依赖；引擎层只翻译与摆放，不判断重叠、不算颜色。
- hud-theme / hud-panels 的源码守门继续生效。
- 同一时间只跑一个 dotnet / Godot；不跑批量对局；不用 Tween。
- 本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"。
- 本机构建 Godot 工程要带 `-p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config`；Godot 在 `~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot`。拍图脚本先例 `art/hud-panels/shots.sh`。

分段：A = tasks.md 第 1 组；B = 第 2 组；C = 第 3 组（主会话）。

## 实现记录

### 段 A（2026-10-06，呈现层与测试）

- 新增 `Layers/CoordinateLabelYield.cs`（远边列 / 左行 / 右行的显现千分比，近边无表，多条目取最小，150 ms 淡到 150‰）与 `Style/BoardLabelStyle.cs`（坐标标注两色、常驻标注配色与描边下限、算式行同色与字号 / 描边下限，另有按原始值的 `…For` 重载供测试钉住下限）；`RevealDisplay` 加末位 `ShownMs`（自第一步开始的毫秒数，与其他时间字段同为压缩后口径）；`GroupPowerLabel` 加 `Corner`，`GroupPowerLabels.Of` 改为必填棋盘宽度；`UiTheme.MutedTextAlphaPermille = 620`。`src/godot` 只为可编译改了三处传参。
- 四阵营常驻标注字色 / 描边亮度差 172 / 172 / 201 / 173（≥ 120），未写逐阵营上调。
- 测试 `盘面标注避让与对比Tests` 10 条（实现 9 + 检查 1）；全量 2234 = 2214 通过 / 11 失败 / 9 跳过，失败名单与改动前逐条相同。
- 变异：实现方 29 条（1 条等价：描边下限在现行样式表上不起作用）；检查方补原始值重载与多步 `ShownMs`、窄矮棋盘断言后 9 条全红；主会话 1 条（远边范围 3 → 2 行 → 红 1）。
