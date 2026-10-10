# 10-09-board-link-mode-b

> 规格权威：`openspec/changes/board-link-mode/`（proposal 裁决表含 B1–B6 + design D2b / D3 / D4 / D5 / D6 / D7 / D10 + specs：`battlefield`「配对事件由对局外部提供」「阶段推进」「战场结算」「等待者的行动」、`match-setup`、`turn-sequence`、`elimination-endgame`、`information-visibility`、`initiative-order`、`carry-in-out`、`match-telemetry`「对局日志的记录内容」中的配对 / 链接 / 战场结算记录部分）。

段 B：Core 对局流程。对局模式 Legacy / Link（B1），Link 模式下：盲选插旗与 `flag-pick` 子流、阶段机、配对来源接口与脚本固定配对、链接执行与公共棋盘信物、战场内合法落子范围、战场结算与减员结算、两类终局与名次、按座位视图、存档与日志事件、战场落败的带入带出结算。对应 tasks.md 2.1–2.10。

段 A 接线契约（硬前提）：对 `board.LinkedMap` 调 `LinkMapGenerator.Link(...)` 再 `board.ApplyLink(result.Map)`；同一配对事件内按"含最小出生棋盘编号的一对先"逐对链接；存档恢复先重放链接、再重放改造，`BaseMap` 仍是开局图。

## Acceptance Criteria

- [ ] tasks.md 2.1–2.10 全部勾掉，每个 Scenario 有可追溯测试
- [ ] Legacy 模式下全部既有测试（含黄金值与决策序列基线）不改断言、全绿
- [ ] Link 模式下用脚本固定配对可从插旗走到终局，覆盖四人四区、2+1+1、提前打完不进配对、3+1、四人同区五种开局
- [ ] `dotnet build` 零警告、`dotnet test -c Release` 全绿

约束：子 agent 不得 git commit；不跑批量对局；同一时间只跑一个 dotnet；理解代码优先 codegraph；不改 `Siege.Sim` / `Siege.Presentation` / Godot（入口切换属段 C / D）；不改 AI 评价代码。
