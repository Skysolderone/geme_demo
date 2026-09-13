# 续接说明（HANDOFF）

> 生成于 2026-09-13。仓库：`git@github.com:Skysolderone/geme_demo.git`。
> 读完本文即可在新会话中继续，不需要翻聊天记录。

## 一句话

《围杀 Siege》——2–4 人共享棋盘的回合制策略构筑游戏（围棋式围杀 × 自走棋式征募构筑）的 4 人核心原型。
规则内核是**零 Godot 依赖的 .NET 8 类库**（`src/Siege.Core`），表现层将来用 Godot 4.5 .NET 版（子任务 8）。
首轮原型的唯一产出目标：跑数千局 AI 对局，回答"这套规则平不平衡"（设计文档 §16 六项指标、§17 七个分析方向）。

## 分支状态

| 分支 | 状态 |
|---|---|
| `main` | 全绿：`dotnet test` **382/382**，零警告。只含经过 `trellis-check` 并归档的内容 |
| `wip/match-flow` | 子任务 6 的**未经 check 的中间态**（444 测试中 443 通过、1 失败）。见下文「续接第一步」 |

## 权威来源（按优先级）

1. `2026-09-10-siege-core-gameplay-design-v1.md` —— 玩法设计 v1.0（`Siege-玩法介绍-v1.docx` 是同规则的玩家向介绍稿，冲突以 md 为准）
2. `openspec/specs/` —— 已归档进基线的 12 个能力规格（Requirement / Scenario 是验收基准）
3. `openspec/changes/add-{match-flow,heuristic-ai,tactical-ui}/` —— 剩余 3 个 change 的 proposal / specs / design（含「裁决记录（已确认）」）
4. `.trellis/spec/core/` —— 编码规范四份：`boundaries.md` `determinism.md` `coordinates.md` `testing.md`（**必读**，里面全是踩过的坑）
5. `.trellis/tasks/09-12-*/` —— 各子任务的 prd / design / implement（执行进度以 `implement.md` 勾选为准）
6. `openspec/ROADMAP.md` —— 依赖顺序、四条全局硬约束、37 条已确认裁决速查

## 进度

| # | 子任务 | 状态 | 测试 |
|---|---|---|---|
| 1 | board-core | 归档 | 140 |
| 2 | batch-deployment | 归档 | +44 |
| 3 | territory-power | 归档 | +82 |
| 4 | relic-system | 归档 | +66 |
| 5 | recruit-hand | 归档 | +50 → **382** |
| 6 | match-flow | `in_progress`，实现在 `wip/match-flow` | +62（1 红） |
| 7 | heuristic-ai | 未开始 | — |
| 8 | tactical-ui | 未开始 | — |

`python3 ./.trellis/scripts/task.py list` 看实时状态。父任务 `09-12-siege-core-prototype` 5/8。

## 续接第一步：完成 match-flow

```bash
git checkout main && git merge --no-ff wip/match-flow   # 或 cherry-pick 6eec2d8；先合到本地，不要 push
python3 ./.trellis/scripts/task.py start 09-12-match-flow
```

然后**派 `trellis-check` 子 agent**（Agent 工具，subagent_type `trellis-check`），prompt 以 `Active task: .trellis/tasks/09-12-match-flow` 开头。要它做的事：

1. 修那 1 个失败：`MatchFlowRegression.对局持久化Tests.小回合边界存档恢复后状态完全一致`（Expected 6 / Actual 5——存档恢复后某个计数少一步，大概率是随机子流消费位置或 Pass 计数）。
2. 审它对四个已归档层的改动（`GameBoard.cs`、`RandomStream.cs`、`HandLedger.cs`+`HandLedgerState.cs`、`RelicLedger.cs`+`RelicLedgerState.cs`，共 +158 行，应为持久化加的状态导出）——这些层的既有测试必须仍然全绿且语义不变。
3. 核对实现方声称的变异验证（工作树里测试注释有记录），自做 5 条变异。
4. **100 局端到端**（`implement.md` 8.4）：确认真的跑了、统计真实（平均大回合数、终局原因分布），无死锁无非法状态。
5. 逐条核对 19 条 Requirement 的 Scenario 与派发时的三条裁决（单线程 + 快照发布；竞争排名代入先手值；`BeginPlayerTurn` 封三次跨层调用）。

check 通过后：`trellis-update-spec`（把学到的写进 `.trellis/spec/core/`）→ 提交（**用 `git commit -F 文件`**）→ `task.py archive 09-12-match-flow` → `openspec archive add-match-flow --yes` → 提交 → push。

## 之后：heuristic-ai 与 tactical-ui

两者可并行。各自 `prd.md` 末尾「从上游带来的待决项」已经攒了几条，派发前先读：

- **heuristic-ai**：AI 只能拿只读公开接口（不传 `HandLedger`/`RelicLedger` 本体）；遥测峰值要加军势字段与"被摧毁"信号；覆盖表分配优化留到 Sim 实测后；信物生成的校准事实（8% 容差下出生区先锋/军令 ≈ 0.8%/3.7%，24% 种子未收敛）作为 §17 已知输入。
- **tactical-ui**：需要 **Godot 4.5 .NET 版**（`~/Desktop/Godot.app` 是标准版，不含 C#，要换）。`godot/` 单向引用 `Siege.Core`，表现层不得含任何规则计算。

## 执行流程（每个子任务）

```
task.py start <task>
  → Agent(trellis-implement)   写代码 + 测试 + 变异验证记录，不 commit，不改 openspec/
  → Agent(trellis-check)       对照规格审 + 自修小问题 + 自做变异，待决写清
  → 主会话核实（真实退出码跑 build/test、抽查一条变异）
  → 裁定待决（设计级的问用户，常规的自己定并写明）
  → trellis-update-spec        学到的写进 .trellis/spec/core/
  → git commit -F msgfile      提交与归档之间用 &&
  → task.py archive + openspec archive --yes → 提交 → push
```

派发 prompt 模板（两类 agent 通用开头）：`Active task: .trellis/tasks/<task>` + "不要 commit、不要改 openspec/" + 指向 `implement.jsonl`/`check.jsonl` + 点名必须一次做对的陷阱 + 要求变异验证记录。

## 规范要点（`.trellis/spec/core/` 的浓缩）

- **零 Godot 依赖**：`Siege.Core`/`Siege.Sim` 不得引用 `Godot.*`，csproj 有守门 + 运行时断言
- **单一实现**：四邻接遍历只在 `Adjacency.Neighbors`；坐标映射只在 `Coord`；覆盖语义只在 `CoverageMap`；结算顺序只在 `SettlementDriver`
- **禁止浮点**：计分/倍率/稀有度全整数；倍率用 `Int128` checked（`checked long` 不够）
- **随机子流隔离**：`GameSeed.Stream("relic-gen"|"recruit"|"setup")`，不用 `System.Random`
- **围棋记法坐标**：`A1` 左下，列跳过 `I`
- **视图分离**：公开视图结构上不存在私有字段，靠反射守门
- **显式名册**：未知玩家一律抛 `SiegeRuleException`
- **一切实时重算**：不缓存不增量
- **新守门测试必须做变异验证**并记录（主会话写的假测试被抓过 4 个）
- **提交前用真实退出码把关**，`dotnet test | tail` 会吞退出码；提交信息用 `-F`

## 环境

- .NET SDK 8.0.412；`dotnet build --no-incremental` / `dotnet test`
- Godot 4.5.stable 标准版在 `~/Desktop/Godot.app`（**无 C#**）；子任务 8 前换 .NET 版
- openspec 1.5.0、trellis 0.6.5 已装；skill 与 agent type 在会话启动时注册，新装后需重启会话
- 4 人基准地图：`maps/siege-4p-base-v1.json`（D2 对称、109 可落子格、14 信物格、距离极差 0）

## 可直接粘贴的开场 prompt

```
读 /Users/rubioc/game_demo/HANDOFF.md，按其中「续接第一步」继续：把 wip/match-flow 合到本地 main，
task.py start 09-12-match-flow，派 trellis-check 子 agent 处理那 1 个失败并审四个已归档层的改动，
通过后走 update-spec → commit(-F) → archive → push。全程遵守 .trellis/spec/core/ 四份规范，
每个子任务结束向我汇报：测试数、变异验证情况、待决项。设计级的待决问我，常规的自己定并说明。
使用中文，每次回答开头报数（Ws：N）。
```
