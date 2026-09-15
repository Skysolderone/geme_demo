# 续接说明（HANDOFF）

> 更新于 2026-09-15（首轮原型 8/8 完成，图形界面可玩；打磨轮：势力碾压胜利 + 部署上限分阶段）。仓库：`git@github.com:Skysolderone/geme_demo.git`。
> 读完本文即可在新会话中继续，不需要翻聊天记录。

## 一句话

《围杀 Siege》——2–4 人共享棋盘的回合制策略构筑游戏（围棋式围杀 × 自走棋式征募构筑）的 4 人核心原型。
规则内核是**零 Godot 依赖的 .NET 8 类库**（`src/Siege.Core`），AI 在 `src/Siege.Core/Ai`，
批量跑局 / 日志 / 分析与终端版在 `src/Siege.Sim`，界面视图模型在 `src/Siege.Presentation`（零 Godot），
3D 表现层在 `src/godot`（Godot 4.7.2 .NET 版）。

## 现在就能玩

**图形版（推荐）**
```bash
"D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe" --path E:/wws/geme_demo/src/godot
```
点出生区插旗 → 征募 → 左下手牌选类型、点格子暂放 → 右侧看预演 → 右下确认或 Pass。
按住 1/2/3/4 看领地/气/势力/信物层，5 或点顶部顺序条看顺序层，H 开手牌面板，T 切换按住/点击模式。
命令行加 `-- --seed=12345` 复现同一局；`-- --auto-demo` 自动演示；`-- "--screenshot=<路径>.png:90"` 截图。

**终端版**
```bash
dotnet run --project src/Siege.Sim -c Release -- play [--difficulty Easy] [--seed N]
```

## 分支状态

| 分支 | 状态 |
|---|---|
| `main` | 全绿：`dotnet test` **622/622**，零警告，套件约 13 秒。已推送 |
| `wip/match-flow` | 早已合入 main，本地与远端均可删 |

## 权威来源（按优先级）

1. `2026-09-10-siege-core-gameplay-design-v1.md` —— 玩法设计 **v1.1**（文末有变更记录；`Siege-玩法介绍-v1.docx` 是玩家向介绍稿，冲突以 md 为准）
2. `openspec/specs/` —— 已归档进基线的 24 个能力规格（Requirement / Scenario 是验收基准）
3. `openspec/changes/archive/` —— 各 change 的 `design.md` 末尾「裁决记录（已确认）」共 76 条，是设计文档未覆盖部分的判定来源
4. `.trellis/spec/core/` —— 编码规范四份：`boundaries.md` `determinism.md` `coordinates.md` `testing.md`（**必读**，全是踩过的坑）
5. `.trellis/tasks/archive/2026-09/` —— 各子任务的 prd / design / implement
6. `openspec/ROADMAP.md` —— 依赖顺序、四条全局硬约束、裁决速查
7. `art/style-exploration/` —— 视觉基准图与 8 张功能示意（从 docx 解出），`README.md` 是风格基准与人工检查清单

## 进度：首轮原型 8/8 完成

| # | 子任务 | 状态 | 测试 |
|---|---|---|---|
| 1 | board-core | 归档 | 140 |
| 2 | batch-deployment | 归档 | +44 |
| 3 | territory-power | 归档 | +82 |
| 4 | relic-system | 归档 | +66 |
| 5 | recruit-hand | 归档 | +50 → 382 |
| 6 | match-flow | 归档 | +65 → 447 |
| 7 | heuristic-ai | 归档 | +50 → 497 |
| 8 | tactical-ui | 归档（2026-09-15） | +86 → **583** |

之后的规则复议与打磨（均已归档）：`cap-multiplier`（倍率封顶，现为 4）、`round-cap`（大回合上限 15）、
`dominance-victory`（势力碾压胜利，候选制，第 7 大回合起）、`growth-pass-1`（基础部署上限分阶段 3/4/5）。测试 **622**。

## 现行终局与关键数值

- 终局优先级：只剩一人 > **势力碾压** > 棋盘填满 > 整轮 Pass > 达大回合上限（15）
- 势力碾压：第 7 大回合起，某参赛玩家势力 ≥ 其余参赛玩家之和 → 成为候选；其余每人各行动一次后仍满足即获胜，跌破即取消；候选与待回应名单公开
- 基础部署上限：第 1–3 大回合 3、第 4–6 大回合 4、第 7 大回合起 5，军令信物在其上叠加
- 倍率：`1.5^min(倍增子数, 4)`，上限 5.0625

## 数据现状（200 局回归，growth-pass-1 之后，种子 1–200，Standard）

| 指标 | 实测 | §16 目标 |
|---|---|---|
| 部署上限中位数 第 1–3 / 4–6 / 7+ | 3 / 4 / 5 | 3 / 3–5 / 5–8 ✓ |
| 第 3 大回合领先者胜率 | 36.5% | ≤ 50% ✓ |
| 峰值军势 平均 / 最高 | 345 / 779 | 数百 ✓ |
| 终局原因 碾压 / 整轮 Pass / 达上限 | 30.5% / 59% / 10.5% | — |
| 终局局平均结束大回合 | 10.72 | 7–10（略超） |
| 首次跨出生区冲突 | 集中在第 6–7 大回合 | 4–5 ✗ |
| 整局从未提子的局 | 15 / 200 | — |
| 倍增子 / 协同子 / 连珠子 / 普通子选择率 | 85.8% / 92.1% / 67.1% / 26.8% | 无唯一最优 ✗ |

结论：部署成长、先手滚雪球、数值膨胀、"有人能赢"均已解决；残余两项——**冲突偏晚**（部署提速没有让它自然前移）与**倍增子 / 协同子一家独大**（封顶从 5 降到 4 无效，问题在其它棋子太弱）。

## 可选的下一步（按用户意愿排）

1. **按试玩反馈改界面**：已知待办——棋盘无坐标标注；底部按钮是否放顺序层；插旗未做四家竞争。
2. **冲突时机 change**：保护期缩到 2 大回合，或调整地图出生区距离（growth-pass-1 裁决 9）。
3. **棋子平衡 change**：加强普通子 / 连珠子（基础军势或位置加值），而不是继续削倍增子（growth-pass-1 裁决 8）。
4. **2000 局基线**（约 45 分钟）：
   ```bash
   dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/baseline --seed 1 --count 2000 --difficulty Standard --gzip
   dotnet run --project src/Siege.Sim -c Release -- analyze --dir sim-out/baseline
   ```
5. **美术与音效**：目前全是程序生成几何体，无正式资源。

## 执行流程（每个子任务）

```
opsx:propose 开 change → task.py create + start
  → Agent(trellis-implement)   写代码 + 测试 + 变异验证记录，不 commit，不改 openspec/
  → Agent(trellis-check)       对照规格审 + 自修小问题 + 自做变异，待决写清
  → 主会话核实（真实退出码跑 build/test、自做一条变异）
  → 裁定待决（设计级的问用户，常规的自己定并写明）
  → trellis-update-spec        学到的写进 .trellis/spec/core/
  → git commit -F msgfile      提交与归档之间用 &&
  → task.py archive + openspec archive --yes → 提交 → push
```

派发前把待决项拟成编号裁决 + 派发方式选项，一次向用户确认（用户全局规则）；体量大的任务顺序分段派，
段与段之间落本地 wip 提交（不 push）。

## 规范要点（`.trellis/spec/core/` 的浓缩）

- **零 Godot 依赖**：`Siege.Core` / `Siege.Sim` / `Siege.Presentation` 不得引用 `Godot.*`，csproj 有守门；`src/godot/` 是唯一例外
- **单一实现**：四邻接只在 `Adjacency.Neighbors`；坐标映射只在 `Coord`（表现层的 `Coord`↔3D 只在 `BoardGeometry`）；覆盖只在 `CoverageMap`；结算顺序只在 `SettlementDriver`；乘倍率取整只在 `Multiplier`
- **禁止浮点**：计分 / 倍率 / AI 评价全整数；浮点只允许在 `Siege.Sim/Analysis/` 与表现层渲染
- **随机子流隔离**：`GameSeed.Stream("relic-gen"|"recruit"|"setup"|"ai-P{n}"|"sim-sample")`，不用 `System.Random`
- **围棋记法坐标**：`A1` 左下，列跳过 `I`
- **信息边界**：公开视图结构上不含私有字段；正式 AI 与界面共用同一 `MatchPublicView`；调试入口 `internal`
- **UI 不做规则计算**：表现层只消费富预演与视图模型
- **一切实时重算**：不缓存不增量
- **新守门测试必须做变异验证**并记录；反射闭包守门要对非根类型做变异；**不在 sln 里的工程（`src/godot/`）对守门隐身，必须补源码级扫描**
- **共用状态机会在最外层被复写**：把完整语义暴露成方法，并断言两条入口逐步等价
- **红测变绿后整段复审**；含集合字段的 record 不能直接 `Assert.Equal`；逐字节比对要配行数下界与字段级投影
- **提交前用真实退出码把关**，`dotnet test | tail` 会吞退出码；提交信息用 `-F`

## 环境

- Windows 11，Git Bash，28 逻辑核。`python` 可用，`python3` 不可用；`dotnet test` 输出为中文本地化 GBK，脚本抓失败名用 ASCII `[FAIL]`
- .NET SDK 8.0.425；`dotnet build` / `dotnet test`（不接受 `--no-incremental`）
- Godot **4.7.2 .NET 版**：`D:\software\godot\Godot_v4.7.2-stable_mono_win64\`，命令行用 `..._console.exe`
  - 构建 C#：`--headless --path src/godot --build-solutions --quit`
  - 自动演示：`--headless --path src/godot --quit-after 3000 -- --auto-demo`
  - 自定义参数必须放在 `--` 之后
- 跑局用 Release；输出目录 `sim-out/` 已 gitignore
- 4 人基准地图：`maps/siege-4p-base-v1.json`（D2 对称、109 可落子格、14 信物格、距离极差 0）

## 可直接粘贴的开场 prompt

```
读 E:\wws\geme_demo\HANDOFF.md。首轮原型 8/8 已完成，图形版可玩。
按我的指示继续：改界面 / 跑 2000 局基线 / 开数值调整 change / 第二轮规则。
全程遵守 .trellis/spec/core/ 四份规范；派子 agent 前先把待决项拟成编号裁决向我确认。
每个任务结束汇报：测试数、变异验证情况、待决项。设计级的待决问我，常规的自己定并说明。
使用中文，每次回答开头报数（ws:N）。
```
