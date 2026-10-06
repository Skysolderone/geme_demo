# 10-06-board-isolated-gen（母任务 10-06-board-terrain 的子任务 ①）

> 规格权威：`openspec/changes/board-isolated-gen/`（proposal / design / specs / tasks）。负责人裁决全文见母任务 `prd.md`。影响面调研：`research/disconnected-boards-impact.md`。

棋盘档生成器改为互不连通的棋盘组：取消通道；出生棋盘 5–7（人数 + 1 块），公共棋盘 7–15、最大一块宽高 ≥ 11；支持 2 / 3 / 4 人；预算按人数缩放；信物按公共棋盘较短边 7–8 / 9–10 / 11–15 放 1 / 2 / 3 个；标识 `board:<种子>[:p<人数>][:n<棋盘数>]`。校验器对棋盘档豁免可达性与咽喉、新增"棋盘之外无可落子格"。`标准` / `边疆` 档与 `gen:` 不动。

验收：`openspec/changes/board-isolated-gen/tasks.md` 各条的验证项。

约束：
- 在工作树 `.claude/worktrees/board-terrain`、分支 `feat/board-terrain` 上做，基于 main `c3448ae`；不要碰主工作树，不要切分支。
- 确定性：整数运算、按下标遍历、随机只来自传入的序列（`.trellis/spec/core/determinism.md`）。
- 规格档差异只写在 `MapValidator.Rules` 声明表（`规格档分流守门Tests`）。
- `gen:` 种子 1–50 的导出文件逐字节不变。
- 同一时间只跑一个 dotnet / Godot；跑局每个配置不超过 20 局。
- 本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"，另加本 change 重定的棋盘档黄金值（逐条记录）。
- 本机构建 Godot 工程要带 `-p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config`；Godot 在 `~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot`。

分段：A = tasks.md 第 1 组；B = 第 2 组；C = 第 3 组；D = 第 4 组（主会话）。

## 实现记录

### 段 A（含段 B 校验器，2026-10-06）

- 参数 / 标识（`board:<种子>[:p<人数>][:n<棋盘数>]`）、无通道打包摆法（工作区 60、主战场居中、间隔 2–4、投影重叠 ≥ 1）、信物分档、人数参数化；校验器声明表加 `Reach`（目标不可达拒绝 / 中央入口通路 / 必须标咽喉，棋盘档三项豁免）、预算三行、`SCENERY_CELL_PLAYABLE`，删 `BOARD_ISOLATED` 与通道规则。
- 检查方补"信物格数上限按人数"测试；守门三处调整均未变弱（样本下界 3 → 2 因咽喉 builder 删除、另补扫 `zoneBuilders`）。
- 主会话裁决两处：边长改整组重抽（`MaxSizeDraws = 200`，原实现缩边收敛使高块数档贴上沿、公共棋盘被压小）；人数进入棋盘档随机流（同种子不同人数原先共享公共棋盘）。随后按数据把 2 人棋盘数收窄为 4–5。
- 实测（种子 1–20，重抽后）：4 人 n7 可落子 417 / 455 / 589、n10 651 / 763 / 796；3 人 n6 382 / 446 / 564；2 人 n5 303 / 360 / 400；全部 0 尝试作废；单张最长 44 ms（含 JIT）。
- 黄金值重定：board:12345 生成图（463 格、42×44）与 `decisions-board1-seed1.txt`（board:1 摘要），v5 决策基线不变。

### 段 C（2026-10-06，冒烟与画面）

- 冒烟（`Siege.Sim run`，标准难度）：4 人 board:1–5、3 人 board:1–3:p3、2 人 board:1–3:p2 共 11 局全部走完，无崩溃 / 截断 / 出局；全部整轮 Pass 终局；4 人 52–59 大回合、3 人 54–68、2 人 61–74；终局占用 75–87%；没有空置的棋盘；单局 6–21 秒；AI 每步中位 37–74 ms、最长 383 ms。2 人局提子明显少（被提 22–53 对 4 人 119–174）。
- 截图 `art/board-isolated-gen/`（board:6 的 4 / 3 / 2 人全局预览、4 人 n10、4 人第 11 大回合全局与主战场近景）：棋盘之间只有场景，无通道、无异常空带；块数、主战场、出生棋盘数与 `Siege.Sim map` 的清单一致；保护期后有跨棋盘落子。
- 收尾：两处构建 0 警告；v5 `--auto-demo` 53 帧；`board:1` `--pick-check` 447 / 447 通过。
- 记录给后续：Godot 通道染色成死代码（② 清理）；全局预览下远边双字母列标挤成一串（呈现层，另议）；自动演示会暂放自杀手、回退为 Pass（与地图无关）。
