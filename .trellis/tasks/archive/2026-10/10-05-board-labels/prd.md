# 10-05-board-labels

> 规格权威：`openspec/changes/archive/2026-10-05-board-labels/`（已归档，主规格已同步；3.3 负责人 2026-10-06 过目，保持不变，让位残留 150‰ 也保持）。

盘面标注可读性加 `MutedText` 收尾（负责人 2026-10-06："所有都需要"；重叠时坐标标注让位）：军势揭示期间被条目压到的远边列字母 / 左右行数字淡出（呈现层按格子邻域给进度）；算式行与结果同色、描边与字号有下限；常驻标注配色由呈现层给出（深色统一描边、四阵营亮度差 ≥ 120）、最右一列换到左前角；`Ui.MutedText` 的透明度进 `UiTheme`。规则、计分、演出节奏、存档、日志不变。

验收：`openspec/changes/archive/2026-10-05-board-labels/tasks.md` 各条的验证项（全部勾选）。

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

### 段 B（2026-10-06，Godot 接线、守门与截图）

- 接线：`BoardView` 建坐标标注时按下标记住远边列 / 左行 / 右行三排节点；`DrawCoordinateYield(mask)` 在完整刷新与演出逐帧刷新里各调一次，只按 `CoordinateLabelYield.Of(mask, 宽, 高)` 的千分比设字与描边透明度，结果空且上次没让位时不碰节点、上次让过位则整排复原。算式行颜色 / 字号 / 描边改取 `BoardLabelStyle.RevealFormula…Of(步档位)`，删掉向白色混色与本地两常量。常驻标注颜色取 `GroupLabelColors`、描边取 `GroupLabelOutlineFor(style.GroupLabelOutlineSize)`（与 `GroupLabelOutlineOf(档)` 等值；保留对 `GroupLabelOutlineSize` 的读取，`数值档位Tests.引擎层只按呈现层给的档位查样式表` 要求引擎层读这一列）、偏移按 `Corner`。`Visuals.CoordinateLabel*` 改为 `ToColor(BoardLabelStyle.…)`；`Ui.MutedText` 由 `UiTheme.MutedTextAlphaPermille / 1000f` 算出。
- 让位常量未调：远边与左右两组截图里被压的标注都在范围内淡出，近景（拉近三档）与全局预览（边疆图）下没有多让 / 少让。
- 守门 `盘面标注避让与对比Tests.Engine.cs`（同一个 partial 类）4 条 + `次要文字透明度` 补源码扫描；变异 25 条（含 5 条绕过尝试）全部红 1。同步 `双字母列标不重叠Tests.单字母列标不变` 里行数字两句的写法（仍取缺省字号）。
- 全量 2238 = 2218 通过 / 11 失败 / 9 跳过，失败名单与本段基线逐条相同；两处构建 0 警告；`--auto-demo` v5 / board:1 均 53 帧（改前同）；`--pick-check` v5 105/105、board:1 471/471、frontier 411/411。
- 截图 `art/board-labels/before|after/`（`shots.sh`）：远边揭示、拉近三档、左右边揭示、对局中途常驻标注、全局预览（v5 一屏放得下，`--overview` 不生效，改用边疆图）。
- D8 补改：`GroupPowerLabels.Of` 先算全部锚格，`CornerOf(anchor, 宽, leftNeighborLabeled)` 只在最右一列且同一行左邻不是另一条标注的锚格时给左前角；「最右一列换角」改按新条件，新增「左邻也有标注时不换角」；变异 D8-M1 忽略左邻 红 2、D8-M2 左邻判成右邻 红 1、D8-M3 去掉 x > 0 判界 红 3。补拍 `side-reveal-show`（同局面第 26 帧，`--shot-show=power`；before 用 152c95b 的导出快照在 scratchpad 里构建拍摄）：改前右边"413"，改后右边 11–13 行已淡出、"4"单独可读。原 `side-reveal` 拍到的是 N11 条目刚出现、淡出前一刻（约 626‰），保留不删。`mid-labels` after 重拍：N10 仍在左前角（左邻 M10 无标注）。全量 2239 = 2219 / 11 / 9，失败名单与本段基线相同。
