# 10-06-builtin-board-maps（母任务 10-06-board-terrain 的子任务 ②）

> 规格权威：`openspec/changes/builtin-board-maps/`（proposal / design / specs / tasks）。负责人裁决全文见母任务 `prd.md`。影响面调研：`research/builtin-and-defaults.md`。

出生棋盘同尺寸（生成器每张图只抽一次，摆放可转 90°；校验器拒绝不一）；三张内置棋盘图 `siege-4p/3p/2p-board-v1`（`board:` 别名，摘要黄金值守门）；三个入口缺省改为 `siege-4p-board-v1`；选图界面只列内置棋盘图与随机棋盘图、人数 2–4 可调；清理 Godot 通道死代码；依赖缺省地图的既有测试显式钉回 v5。

负责人 2026-10-06 裁决：出生棋盘同尺寸（改生成器）、内置图用别名、每种人数一张、测试钉回 v5；内置图种子由实现方按口径挑候选、出图后负责人选定（段 B 之后暂停等负责人）。

验收：`openspec/changes/builtin-board-maps/tasks.md` 各条的验证项。

约束：
- 工作树 `.claude/worktrees/board-terrain`、分支 `feat/board-terrain`，基于 main `c4a9e23`；不碰主工作树、不切分支。
- 确定性（`determinism.md`）；规格档差异只在 `MapValidator.Rules` 声明表；hud-theme / hud-panels 的源码守门继续生效。
- 同一时间只跑一个 dotnet / Godot；跑局每个配置不超过 20 局。
- 本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"，另加本 change 重定的黄金值（逐条记录）。
- 本机构建 Godot 工程要带 `-p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config`；Godot 在 `~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot`。

分段：A = tasks.md 第 1 组；B = 第 2 组（选种，交负责人）；C = 第 3 组；D = 第 4 组；E = 第 5 组（主会话）。

## 实现记录

### 段 A（2026-10-06，出生棋盘同尺寸）

- 生成器每组只抽一次出生尺寸（各 5–7），摆放枚举 (w,h) 与 (h,w) 两种朝向；校验器新增 `BIRTH_BOARD_SIZE_MISMATCH`（无序对比较，挂在棋盘档专属校验）。新增两个 Scenario 与样本口径测试；7 条变异全红。
- 黄金值重定：board:12345（539 格、46×37）、`decisions-board1-seed1.txt`；v5 决策基线不变。
- 统计（种子 1–20 × 全部合法棋盘数）：4 人可落子 377 / 639 / 799、3 人 265 / 491 / 599、2 人 207 / 318 / 397；全部第 0 次尝试成功；出生尺寸覆盖 5×5 到 7×7 六种无序对。

### 段 B（选种）

- 扫种子 1–200（缺省棋盘数），硬条件：主战场宽高 ≥ 13、公共棋盘不全同形、出生面积 30–42；按可落子格离中位数排序，每种人数 3 个候选，出图 `art/builtin-board-maps/candidates/`。
- **负责人 2026-10-06 选定**：`siege-4p-board-v1` = `board:5`（465 格、39×41、出生 5×7 ×5、主战场 13×14、第二战场 9×12）；`siege-3p-board-v1` = `board:55:p3`（433 格、39×38、出生 6×7 ×4、主战场 13×15、另一块 7×10）；`siege-2p-board-v1` = `board:23:p2`（366 格、32×35、出生 6×6 ×3、主战场 15×13、另一块 7×9）。
