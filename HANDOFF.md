# 续接说明（HANDOFF）

> 更新于 2026-09-14（heuristic-ai 归档后）。仓库：`git@github.com:Skysolderone/geme_demo.git`。
> 读完本文即可在新会话中继续，不需要翻聊天记录。

## 一句话

《围杀 Siege》——2–4 人共享棋盘的回合制策略构筑游戏（围棋式围杀 × 自走棋式征募构筑）的 4 人核心原型。
规则内核是**零 Godot 依赖的 .NET 8 类库**（`src/Siege.Core`），AI 在 `src/Siege.Core/Ai`，批量跑局 / 日志 / 分析在 `src/Siege.Sim`，
表现层将来用 Godot 4.5 .NET 版（子任务 8）。
首轮原型的唯一产出目标：跑数千局 AI 对局，回答"这套规则平不平衡"（设计文档 §16 六项指标、§17 七个分析方向）。

## 分支状态

| 分支 | 状态 |
|---|---|
| `main` | 全绿：`dotnet test` **497/497**，零警告，套件约 25 s。只含经过 `trellis-check` 并归档的内容。已推送 |
| `wip/match-flow` | 已合入 main，本地与远端均可删，尚未删 |

## 权威来源（按优先级）

1. `2026-09-10-siege-core-gameplay-design-v1.md` —— 玩法设计 v1.0（`Siege-玩法介绍-v1.docx` 是同规则的玩家向介绍稿，冲突以 md 为准）
2. `openspec/specs/` —— 已归档进基线的 19 个能力规格（Requirement / Scenario 是验收基准）
3. `openspec/changes/add-tactical-ui/` —— 剩下的 1 个 change；`openspec/changes/archive/2026-09-14-add-heuristic-ai/design.md` 的裁决记录 1–17 含 AI / 跑局层的全部判定
4. `.trellis/spec/core/` —— 编码规范四份：`boundaries.md` `determinism.md` `coordinates.md` `testing.md`（**必读**，里面全是踩过的坑）
5. `.trellis/tasks/09-12-*/` 与 `.trellis/tasks/archive/2026-09/` —— 各子任务的 prd / design / implement
6. `openspec/ROADMAP.md` —— 依赖顺序、四条全局硬约束、50 条已确认裁决速查

## 进度

| # | 子任务 | 状态 | 测试 |
|---|---|---|---|
| 1 | board-core | 归档 | 140 |
| 2 | batch-deployment | 归档 | +44 |
| 3 | territory-power | 归档 | +82 |
| 4 | relic-system | 归档 | +66 |
| 5 | recruit-hand | 归档 | +50 → 382 |
| 6 | match-flow | 归档 | +65 → 447 |
| 7 | heuristic-ai | 归档（2026-09-14） | +50 → **497** |
| 8 | tactical-ui | 未开始 | — |

`python ./.trellis/scripts/task.py list` 看实时状态。父任务 `09-12-siege-core-prototype` 7/8。

## 关键实测结论（heuristic-ai，36 局）——**2000 局基线尚未跑**

启发式 AI 互打**不收敛率 75–100%**（跑局层靠 `MaxMajorRounds` 上限收尾），倍增串军势随大回合**指数膨胀**
（第 10 / 20 / 30 大回合单串 ≈ 2×10³ / 3×10⁴ / 8.5×10⁵），倍增子选择率 92%，部署上限第 7 大回合后中位数仍是 3。
裁决 15：2000 局基线推迟到规则复议之后。

根因（已分析，待用户定）：§10.1 `1.5^n` 无上限 → 边际收益永远为正 → 整轮 Pass 永远不触发；
§6.1 按整批终态判合法 → 一批 3 子可同时填两眼，"两眼活"不存在，≤3 气必死 → 绞肉循环；
§5.4 部署上限只靠军令信物（7% / 15%）→ 成长轴打不开。

候选改法（每条独立 openspec change，落上游能力）：
- **A1** 倍率封顶 `1.5^min(n,5)`（territory-power）——建议先做
- **C1** 规则级大回合上限，如第 15 大回合结束按势力排名（match-flow）——建议先做
- **B1** 批次内逐枚判自杀恢复两眼活（batch-deployment）——第二轮，看 A/C 之后是否仍绞肉
- 部署上限成长——等基线数据

## 续接第一步：规则复议

1. 向用户确认选哪几条改法（上面 A1 / C1 / B1），或用户另有方案。
2. 每条用 `opsx:propose` 开 change（proposal + specs delta + design 裁决），在 `.trellis/tasks/` 建任务，
   走 implement → check → update-spec → commit → archive → push。
3. 规则改完后跑 200 局快速回归看不收敛率与倍率曲线：
   ```bash
   dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/regress --seed 1 --count 200 --difficulty Standard --max-rounds 30 --gzip
   dotnet run --project src/Siege.Sim -c Release -- analyze --dir sim-out/regress
   ```
4. 满意后跑 2000 局基线（`--count 2000`，Standard 约 45 分钟 / 28 核），归档报告，作为后续调参基线。

## 之后：tactical-ui

需要 **Godot 4.5 .NET 版**，当前 Windows 机器上未安装。`godot/` 单向引用 `Siege.Core`，表现层不得含任何规则计算。
人工接管入口已就绪：`MatchRunner.TakeOver / HandBack`；公开视图 `MatchPublicView`。

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
派子 agent 前先把待决项拟成编号裁决 + 派发方式选项，一次向用户确认（用户全局规则）；体量大的任务顺序分段派，
分段之间落本地 wip 提交（不 push）。

## 规范要点（`.trellis/spec/core/` 的浓缩）

- **零 Godot 依赖**：`Siege.Core`/`Siege.Sim` 不得引用 `Godot.*`，csproj 有守门 + 运行时断言
- **单一实现**：四邻接遍历只在 `Adjacency.Neighbors`；坐标映射只在 `Coord`；覆盖语义只在 `CoverageMap`；结算顺序只在 `SettlementDriver`；AI 层不重算提子 / 军势
- **禁止浮点**：计分/倍率/稀有度/AI 评价全整数；倍率用 `Int128` checked；浮点只允许在 `Siege.Sim/Analysis/`
- **随机子流隔离**：`GameSeed.Stream("relic-gen"|"recruit"|"setup"|"ai-P{n}"|"sim-sample")`，不用 `System.Random`
- **围棋记法坐标**：`A1` 左下，列跳过 `I`
- **视图分离 / 信息边界**：公开视图结构上不存在私有字段，靠反射守门；正式 AI 只持 `MatchPublicView` + 本人句柄，调试 AI `internal`
- **显式名册**：未知玩家一律抛 `SiegeRuleException`
- **一切实时重算**：不缓存不增量
- **新守门测试必须做变异验证**并记录；反射闭包守门要对非根类型做变异
- **红测变绿后整段复审**；测试注释里的因果声明要用断言钉住
- **持久化 / 比对守门两条腿**：含集合字段的 record 用 `Assert.Equal` 恒假，要投影成值比；逐字节文本比对必须配行数下界与字段级投影
- **提交前用真实退出码把关**，`dotnet test | tail` 会吞退出码；提交信息用 `-F`

## 环境

- 当前开发机：**Windows 11**，Git Bash，28 逻辑核。`python` 可用，`python3` 不可用；`dotnet test` 输出为中文本地化 GBK（"已通过!"/"失败!"），脚本抓失败名用 ASCII 的 `[FAIL]`
- .NET SDK 8.0.425；`dotnet build` / `dotnet test`（不接受 `--no-incremental`）
- 跑局用 Release：`dotnet run --project src/Siege.Sim -c Release -- run|replay|analyze|map ...`；输出目录 `sim-out/` 已 gitignore；Standard 单局约 6.6 s 串行、并行有效约 1.3 s
- Godot 4.5 .NET 版：未安装。子任务 8 前安装（注意要 .NET 版，标准版不含 C#）
- openspec 1.5.0、trellis 0.6.5 已装；skill 与 agent type 在会话启动时注册，新装后需重启会话
- 4 人基准地图：`maps/siege-4p-base-v1.json`（D2 对称、109 可落子格、14 信物格、距离极差 0）

## 可直接粘贴的开场 prompt

```
读 E:\wws\geme_demo\HANDOFF.md，按其中「续接第一步：规则复议」继续：先向我确认选哪几条改法（A1 / C1 / B1），
每条用 opsx:propose 开 change 并建 trellis 任务，派发前把待决项拟成裁决列表向我确认，
走 implement → check → update-spec → commit(-F) → archive → push；规则改完跑 200 局回归再跑 2000 局基线。
全程遵守 .trellis/spec/core/ 四份规范，每个子任务结束向我汇报：测试数、变异验证情况、待决项。
使用中文，每次回答开头报数（ws:N）。
```
