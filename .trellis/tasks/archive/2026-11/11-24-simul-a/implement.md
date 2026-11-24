# 实施步骤（单个 trellis-implement agent，分三阶段；worktree `E:\wws\geme_demo-simul-a`，分支 simul-a）

通用约束（每阶段都适用）：
- 只在 worktree `E:\wws\geme_demo-simul-a` 里改代码和规格；任务文件在主目录 `E:\wws\geme_demo\.trellis\tasks\11-24-simul-a\`，只读。
- 不 commit；不碰主目录的 `src/godot/parts/terrain_plateau_temp/`（用户未提交的美术）。
- 理解代码优先用 codegraph（`codegraph_explore` / `codegraph_callers` / `codegraph_impact`）。索引建的是主目录，内容与 worktree 起点 a557929f 相同；自己新写的文件直接 Read。不要整篇读大文件，也不要把大文件或媒体内容回传。
- Windows：用 `python` 不用 `python3`；dotnet 8.0.425 已装好。
- 只跑快测：`dotnet test --filter "Category!=Slow&Category!=Perf"`。不跑 200 局、慢测试、长局。
- 新模式面向玩家的文案用"单位 / 整体 / 岛 / 部署"，不写"棋盘 / 棋子"。

## 阶段 A1：规则内核 + 规格 + 测试（做完回传，等主会话 check）

1. 在 worktree 构建一次，跑一次快测，记下基线通过数（应与 main 相同）。
2. 写 `openspec/specs/simultaneous-round/spec.md`：按 design J2 的顺序列 Requirement，每条带 Scenario，覆盖算例 E1–E11。
3. 在 `tests/Siege.Core.Tests/Simul/` 先建测试骨架（类名 = Requirement，方法名 = Scenario），然后实现 `src/Siege.Core/Simul/`：
   1. 校验（J2-3）：八邻禁落、活形双方禁入、只读 S0。
   2. 冲突与 d6（J2-5）：新子流 `simul-dice`，登记进 `.trellis/spec/core/determinism.md`。
   3. 落下与地形改造（J2-6、K2）。
   4. 按顺位围杀与孤立自绝（J2-7）。
   5. 势力与活形领地（J2-8）：先核对现状，再决定是否加修正。
   6. 终局与排名（J2-9）、认输（Q11）。
   7. 回合循环与顺位（J2-1、J2-10）、征召一次生成（K5）。
   8. 对外句柄与 `SimulRoundReport`（J3）。
4. 确定性测试：同种子、同决策逐回合相同；一人改暂放不影响别人的征召候选。
5. 变异验证：至少 4 条（冲突读到本回合新部署、顺位反转、八邻改四邻、占满时把活形格算成空），每条记下测试是否变红。
6. 构建零警告；快测全绿，现有测试与基线零改动（`git diff --stat` 里不应出现现有测试和基线文件）。
7. 回传：改动 / 新增文件清单；共享类上新增的成员（如有）及理由；J2-8 的核对结论；变异结果；快测前后通过数；偏离 J / K 的地方。

## 阶段 A2：贪心 AI + Sim（主会话 SendMessage 续派）

1. 实现 J4 贪心 AI。可以放 `Siege.Core/Simul/` 或 `Siege.Core/Ai/`，但不改现有 AI 的任何路径和基线。
2. Sim 新模式入口（J5），全 AI 对局。
3. 跑 2 / 3 / 4 人各一局 `--rounds=6`，不跑满终局。日志里要能看到冲突、掷骰、围杀分步、势力。
4. 加一条 AI 冒烟测试：小图 2 人跑 3 回合不抛异常，并且每回合至少有一方部署。
5. 回传：命令行用法、三局摘要（每回合部署数、冲突数、掷骰次数、围杀数、势力前三）、单回合 AI 耗时、已知 AI 问题（只记录）。

## 阶段 A3：Godot（先调研回传，主会话确认后再实现）

1. 调研回传：新局面板怎么加选项；渲染与暂放交互复用哪些部分；新会话类的结构；预计改动的文件清单和行数量级。**停下等确认。**
2. 确认后实现：新局面板"新玩法"选项（锁定小图）→ 新会话 → 暂放 / 提交 → AI 决策 → 揭晓 → 冲突文字和飘字 → 结算刷新 → 终局排名。
3. Godot 构建零警告；无人值守自检退出码 0。在自检里加一条新模式探针：开局，跑 1 个回合，断言回合数加 1。
4. 回传：改动文件、自检输出摘要、一张新模式对局截图（落在 `art/simul-a/`，只回传路径，不回传图片内容）。

## 回滚点

- 每阶段由主会话 check 通过后，主会话在 worktree 分支上提交一次。提交信息不带 Co-Authored-By。下一阶段出问题时可以退回上一阶段的提交。
- 合回 main 与推送不在本任务内，等用户另说。
