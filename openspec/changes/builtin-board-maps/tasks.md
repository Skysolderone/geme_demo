## 1. 出生棋盘同尺寸（段 A）

- [x] 1.1 生成器每张图只抽一次出生棋盘尺寸、摆放可旋转 90°；校验器拒绝尺寸不一（D1）。验证：「出生棋盘同尺寸」「出生棋盘尺寸不一」两个 Scenario 与既有棋盘档测试；变异记录。
- [x] 1.2 黄金值重定（board:12345、`decisions-board1-seed1.txt`），逐条记录；全量失败名单与改动前相同。

## 2. 选种（段 B，结果交负责人）

- [x] 2.1 按 D2 口径扫种子 1–200，每种人数挑 3 个候选，各出一张全局预览图存 `art/builtin-board-maps/candidates/`，附指标表。验证：主会话交负责人选定。

## 3. 内置图与缺省切换（段 C）

- [x] 3.1 `MapCatalog` 登记三张内置棋盘图与显示名、缺省改为 `siege-4p-board-v1`；摘要黄金值、别名同内容、导出文件名三条守门（D2 / D3）。
- [x] 3.2 终端版一律打印地图标识；Sim `map` 导出走内置名（D3）。
- [x] 3.3 既有测试按 D4 逐条处理并记录；全量失败名单与改动前相同；全量耗时回到改动前量级。

## 4. 选图界面与清理（段 D）

- [ ] 4.1 `MapSelectModel` / `Hud.MapSelect` / `GameRoot.MapSelect` 按 D5 改；自检步骤按新规格；hud-theme / hud-panels 守门继续通过。
- [ ] 4.2 删通道死代码（D6）。
- [ ] 4.3 画面验证（D7）：选图界面三张、新缺省图 `--auto-demo` 帧数与 `--pick-check`、v5 `--auto-demo` 53 帧；存 `art/builtin-board-maps/`。
- [ ] 4.4 两处构建 0 警告；全量失败名单与改动前相同。

## 5. 验收与文档（主会话）

- [ ] 5.1 `openspec validate builtin-board-maps --strict`；main 合进本分支后重跑 4.4。
- [ ] 5.2 设计文档 §3、HANDOFF、README、发布页、`boundaries.md` 的缺省地图与命令示例同步；变更记录升一版。
- [ ] 5.3 负责人看截图。
