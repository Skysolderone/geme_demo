## Why

母任务 `board-terrain` 的第二个 change。第一个 change（`board-isolated-gen`）让棋盘档生成器产出互不连通的棋盘组；本 change 把它变成"正式的地形"：登记内置棋盘图、让各入口缺省使用它、选图界面只留棋盘图。

负责人 2026-10-06 裁决：内置图只挑开局公平的图；调研（`.trellis/tasks/10-06-builtin-board-maps/research/builtin-and-defaults.md`）发现现生成器下"所有出生棋盘同尺寸"的种子几乎不存在（种子 1–1000 中 4 人 0 个、3 人 0 个、2 人 15 个），负责人裁决**改生成器：同一张图的出生棋盘同尺寸**；内置图用别名、每种人数一张；依赖缺省地图的既有测试本 change 显式钉回 v5。

## What Changes

- **出生棋盘同尺寸**：生成器每张图只抽一次出生棋盘宽高（各 5–7），每块出生棋盘按该尺寸或其旋转 90° 摆放；校验器拒绝出生棋盘尺寸不一的棋盘档地图。**BREAKING**：所有 `board:` 标识生成的地图再变一次。
- **内置棋盘图**：`siege-4p-board-v1`、`siege-3p-board-v1`、`siege-2p-board-v1`，各是一个固定 `board:` 标识的别名，显示名登记在内置地图表；导出摘要黄金值守门（内容变了必须升号）。种子在实现中按客观指标挑候选、出图请负责人确认后登记。
- **缺省地图**：批量、终端、图形三个入口的缺省改为 `siege-4p-board-v1`；终端版一律打印地图标识。
- **选图界面**：只列三张内置棋盘图与随机棋盘图；随机棋盘图可调人数 2–4（改人数时棋盘数重置为缺省）与棋盘数；旧地图不在界面上，仍可用 `--map=` 指定。修 `TrySelectId` 丢人数的现存问题。
- **Godot 通道死代码清理**：`Visuals.CorridorPath` 与 `BoardView` 的通道染色分支删除；视觉规格去掉"通道"。
- **测试**：依赖缺省地图跑真实对局的约 43 条既有测试显式指定 `siege-4p-base-v5`（行为与耗时不变）；只断言"缺省是 v5"的 9 条改为断言新缺省。

内置棋盘图不登记地图专属 AI 权重（AI 未校准）。旧地图与 `gen:` 在第三个 change 删除。

## Capabilities

### New Capabilities
无。

### Modified Capabilities
- `map-generation`：「棋盘档布局规则」（出生棋盘同尺寸）。
- `map-definition`：新增「内置棋盘图」；修改「棋盘档预算与校验」（出生棋盘同尺寸校验）、「边疆档基准地图」（缺省地图的引用）。
- `map-selection`：「开局选图界面」。
- `simulation-harness`：「各入口按地图标识选图」。
- `visual-style-baseline`：「棋盘台面的可视表现」（去掉通道）。

## Impact

- Core：`BoardMapLayout`（出生尺寸抽一次）、`MapValidator`（同尺寸校验）、`MapCatalog`（内置表、缺省）、`BoardMapParameters`（4 人常量按需删）。
- Sim：`Program.cs` 导出分支、`PlayCommand` 地图行。
- Presentation / Godot：`MapSelectModel`、`Hud.MapSelect`、`GameRoot.MapSelect`（人数控件、内置项、自检步骤）、`Visuals`、`BoardView`。
- 测试：约 52 条按调研 §2.2 处理；黄金值重定（board:12345 生成图、`decisions-board1-seed1.txt`）。
- 文档：`HANDOFF.md`、`README.md`、`docs/index.html` 的缺省地图与命令示例，`.trellis/spec/core/boundaries.md`。

## Non-goals

- 删除旧地图、规则测试改写到新图（第三个 change）。
- AI 校准、终局规则调整。
- 全局预览下双字母列标挤在一起的问题。

## 前置

子任务 ① `board-isolated-gen` 已归档（main `c4a9e23`）。工作树 `.claude/worktrees/board-terrain`、分支 `feat/board-terrain`。
