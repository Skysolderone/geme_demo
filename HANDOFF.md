# 续接说明（HANDOFF）

> 更新于 2026-09-14。仓库：`git@github.com:Skysolderone/geme_demo.git`。
> 读完本文即可在新会话中继续，不需要翻聊天记录。

## 一句话

《围杀 Siege》——2–4 人共享棋盘的回合制策略构筑游戏（围棋式围杀 × 自走棋式征募构筑）的 4 人核心原型。
规则内核是**零 Godot 依赖的 .NET 8 类库**（`src/Siege.Core`），表现层将来用 Godot 4.5 .NET 版（子任务 8）。
首轮原型的唯一产出目标：跑数千局 AI 对局，回答"这套规则平不平衡"（设计文档 §16 六项指标、§17 七个分析方向）。

## 分支状态

| 分支 | 状态 |
|---|---|
| `main` | 全绿：`dotnet test` **447/447**，零警告。只含经过 `trellis-check` 并归档的内容。已推送 |
| `wip/match-flow` | 已于 2026-09-14 合入 main（dc4b7d9），本地与远端均可删，尚未删 |

## 权威来源（按优先级）

1. `2026-09-10-siege-core-gameplay-design-v1.md` —— 玩法设计 v1.0（`Siege-玩法介绍-v1.docx` 是同规则的玩家向介绍稿，冲突以 md 为准）
2. `openspec/specs/` —— 已归档进基线的 16 个能力规格（Requirement / Scenario 是验收基准）
3. `openspec/changes/add-{heuristic-ai,tactical-ui}/` —— 剩余 2 个 change 的 proposal / specs / design（含「裁决记录（已确认）」）
4. `.trellis/spec/core/` —— 编码规范四份：`boundaries.md` `determinism.md` `coordinates.md` `testing.md`（**必读**，里面全是踩过的坑）
5. `.trellis/tasks/09-12-*/` —— 各子任务的 prd / design / implement（执行进度以 `implement.md` 勾选为准）
6. `openspec/ROADMAP.md` —— 依赖顺序、四条全局硬约束、38 条已确认裁决速查

## 进度

| # | 子任务 | 状态 | 测试 |
|---|---|---|---|
| 1 | board-core | 归档 | 140 |
| 2 | batch-deployment | 归档 | +44 |
| 3 | territory-power | 归档 | +82 |
| 4 | relic-system | 归档 | +66 |
| 5 | recruit-hand | 归档 | +50 → 382 |
| 6 | match-flow | 归档（2026-09-14） | +65 → **447** |
| 7 | heuristic-ai | 未开始 | — |
| 8 | tactical-ui | 未开始 | — |

`python ./.trellis/scripts/task.py list` 看实时状态。父任务 `09-12-siege-core-prototype` 6/8。

match-flow 归档时的实测数据（随机占位策略 100 局）：平均 39.6 大回合（2–129），终局全为 AllPassed，
胜者分布 P0/P1/P2/P3 = 21/27/22/30。保护期内整轮 Pass 也触发终局条件 2（裁决 6，保持现状），
所以随机策略下会有第 2 大回合就结束的对局——这是策略问题，启发式 AI 跑局后再看数据。

## 续接第一步：heuristic-ai

```bash
python ./.trellis/scripts/task.py start 09-12-heuristic-ai
```

派发前先读 `.trellis/tasks/09-12-heuristic-ai/prd.md` 末尾「从上游带来的待决项」，把裁决写清再派：

- AI 只能拿只读公开接口（`MatchPublicView` 等），不传 `HandLedger`/`RelicLedger` 本体
- 遥测峰值要加军势字段与"被摧毁"信号
- 覆盖表分配优化留到 Sim 实测后
- 信物生成的校准事实（8% 容差下出生区先锋/军令 ≈ 0.8%/3.7%，24% 种子未收敛）作为 §17 已知输入
- 对局入口是 `MatchFlow` / `MatchRunner`（`src/Siege.Core/Match/`），`ITurnController` 是 AI 要实现的接口

然后 `Agent(trellis-implement)` → `Agent(trellis-check)` → 主会话核实 → update-spec → commit → archive → push（见下）。

## 之后：tactical-ui

需要 **Godot 4.5 .NET 版**，当前 Windows 机器上未安装。`godot/` 单向引用 `Siege.Core`，表现层不得含任何规则计算。
可与 heuristic-ai 并行，但环境先要就绪。

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
派子 agent 前先向用户确认（用户全局规则）。

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
- **红测变绿后整段复审**：第一个红断言挡住后面所有断言；测试注释里的因果声明要用断言钉住
- **持久化守门两条腿**：含 `ImmutableArray` 的 record 用 `Assert.Equal` 恒假，要投影成值比；"存档→恢复→再存档逐字节相等"抓不到漏字段，必须拿活对象逐字段比
- **提交前用真实退出码把关**，`dotnet test | tail` 会吞退出码；提交信息用 `-F`

## 环境

- 当前开发机：**Windows 11**，Git Bash。`python` 可用，`python3` 不可用；`dotnet test` 输出为中文本地化（"已通过!"/"失败!"），grep 时注意
- .NET SDK 8.0.425；`dotnet build` / `dotnet test`（`dotnet test` 不接受 `--no-incremental`）
- Godot 4.5 .NET 版：未安装。子任务 8 前安装（注意要 .NET 版，标准版不含 C#）
- openspec 1.5.0、trellis 0.6.5 已装；skill 与 agent type 在会话启动时注册，新装后需重启会话
- 4 人基准地图：`maps/siege-4p-base-v1.json`（D2 对称、109 可落子格、14 信物格、距离极差 0）

## 可直接粘贴的开场 prompt

```
读 E:\wws\geme_demo\HANDOFF.md，按其中「续接第一步」继续：先读 heuristic-ai 的 prd.md 待决项并把裁决写清，
task.py start 09-12-heuristic-ai，向我确认后派 trellis-implement，再派 trellis-check，
通过后走 update-spec → commit(-F) → archive → push。全程遵守 .trellis/spec/core/ 四份规范，
每个子任务结束向我汇报：测试数、变异验证情况、待决项。设计级的待决问我，常规的自己定并说明。
使用中文，每次回答开头报数（ws:N）。
```
