# 实施计划：信物系统重设计

**顺序（R30 / R31 修订 R7）**：加法段现在开工，与岛图线并行；段 A / B 的"减法与接线"部分与段 C 等 feat/island-map 合回 main 后再做。

派工：worktree `E:/wws/geme_demo-relic`，分支 `feat/relic-redesign`（从 main 切）。每段一个 trellis-implement 子 agent，段间主会话验收。

## 加法段（现在做，R31）

硬约束：**现有全部测试不改一行、保持全绿**；新代码只加不删，不接入对局流程 / 入口 / AI / Godot；信物层为空时现有行为逐字节不变。

1. 新命名空间 `Siege.Core.PieceRelics`（与旧 `Siege.Core.Relics` 并存，避免重名）：`RelicKind`（五种）、`RelicKinds`（档位与常量，显式标注未校准）、`RelicToken`（Id / Kind / Origin / Insured）。
2. `GameBoard` 信物层：每格至多 1 个 `RelicToken?`；`Place` 不动信物（落在有信物的空格即挂上）、`RemoveStones` 不动信物（掉落）；`Clone` 复制；新增挂载 API（同格已有信物时拒绝）。新增 `SerializeShape()`（不含信物层）并让 `BoardHistory` 用它；`Serialize()` 仅在信物层非空时追加信物段，空层输出与现在完全相同。
3. 计分与覆盖：`PowerCalculator` 读信物层算战鼓 / 号令 / 百变 / 军旗（design D3）；`CoverageMap` 望台按岩台覆盖（design D3）。空层时结果不变。**旗手子（R23）与同分比较（R24）不在本段改**——会改变现行静态信物格地图上的行为，留到接线段。
4. `RelicSpawner`（design D2）：纯函数式，输入盘面、出生区域接口 `IBirthArea`（本段实现 = 现行出生棋盘）、对局种子、大回合、开局人数、场上未拾取件数，输出本大回合的预告 / 落位；新子流 `relic-spawn`、`relic-drop` 登记到 `.trellis/spec/core/determinism.md` 子流表，不扰动既有子流。
5. 局外新模块（不替换旧 `Carry/`）：`RelicProfile` v2 + `RelicProfileStore`（原子写、损坏备份、高版本不写回、v1 迁移按原价折点、新档案送 3 件、兑换 8 点 / 普通、稀有不可兑换）；`RelicLoadout` 校验（≤5、保险 ≤2、库存足够）；`RelicSettlement` 纯函数实现 design D6 归属（含 R27）与 R19 点数。
6. 新测试（新文件）：AC1（盘面层面）、AC2、AC3（不含旗手子 / 同分）、AC4 校验部分、AC5、AC6；`SerializeShape` 与空层 `Serialize` 不变的守门测试。

验收：`dotnet build -c Release` 无新警告；`dotnet test` 全绿且旧测试零改动（`git diff --stat main -- tests` 只有新增文件）。

## 接线段（2026-10-27 起，main c570f30 已合入分支 4806003）

分两步顺序派工，每步一个 trellis-implement 子 agent，步间主会话验收提交。两步都必须保持 `siege.sln` 与 `src/godot/Siege.Godot.csproj` 都能编译（Godot 侧本段只做让它编译通过的最小改动，界面留给段 C）。

### W1 规则接线 + 删旧信物
1. `IBirthArea` 核对为岛图口径（出生平台 = `MapData.BirthBoards()`），去掉"∪ 出生区"兜底若已无必要。
2. 刷新接入大回合流程：大回合开始时按 D2 预告 / 落位；预告与刷新记录进对局状态、存档、公开视图（R22）；存档往返。
3. 同形：`SettlementDriver`、`BatchRehearsal.DuplicateOf`、`ExpertLookahead` 三处改喂 `SerializeShape()`。
4. 旗手子（R23）、同分比较（R24）。
5. 删除旧 `Siege.Core.Relics`（生成 / 控制 / 揭示 / 快照加成 / 先锋 / 徽记 / 工坊 / 连营犄角 / `ScoringRelicCounts` / `RelicLedger`）、静态信物格（`MapData.RelicCells`、`RelicCellSpec`、`RelicZone`、`MapFile.RelicDto`、校验器 `ValidateRelicCells`）、`SettlementDriver` 揭示步；`EffectSnapshot` 只剩默认基础值。旧信物测试与岛图线夹具（`LinkMapFixtures.WithRelics`、`LinkModeFixtures.RelicMap / CreateWithRelics / StartedWithRelics`）一并删除；依赖它们的其他测试改写。
6. AI：`RelicEstimate` 本步只改到能编译、不读旧信物（估值重写在段 C）。
7. 表现层 / Sim / Godot：按"每格两字符、`/` 分行"自己解析 `Serialize()` 的地方逐处排查，改为容忍 `#` 信物段；旧信物层显示改成显示新信物层的最小版本或暂时移除。

### W2 带入 / 结算接线 + 删旧补给
1. `Placement.AttachCarried`（R14）：预演合法性、保护期可挂、同格已有信物不能挂。
2. 对局创建读 `RelicLoadout`，带入件进对局状态与存档；出局 / 弃赛按 R17 掉落；结算按 D6 + R19 写档案（`RelicProfileStore` v2）。
3. 先把新代码借用的 `CarryPoints`、`CarryOutSettlement.ResignShare`、`CarryProfile.Parse`、`Supplies.PriceOf` 搬到 `PieceRelics`，再删除旧 `Carry/`（补给、补给档案 v1 只留迁移读入）。
4. AI 带入（R21，`carry-ai` 子流）。
5. 终端：带入选择（选件、勾保险、兑换）、部署挂出参数、结算打印每件去向；Sim `--carry-in 0..5`。
6. Godot：旧补给面板改到能编译的最小版本（新面板在段 C）。

## 段 A：规则核心（RQ1 / RQ2 / RQ3 / RQ7）

1. `GameBoard` 信物层 + `RelicToken` + `Placement.AttachCarried`；Clone / 存档带信物层，同形比较不含信物层。
2. `RelicKind` 与五种效果；`PowerCalculator`、`CoverageMap`（望台）、旗手子、同分比较。
3. `RelicSpawner`（`relic-spawn` / `relic-drop` 子流）、预告、上限、出生区域排除。
4. 删除旧 `Relics/` 生成 / 控制 / 揭示 / 快照加成、先锋、徽记、工坊、`ScoringRelicCounts`、静态信物格；`SettlementDriver` 去掉揭示步。
5. 测试：AC1–AC3、AC7；旧信物测试删除或改写。

验收：`dotnet test` 全绿；同种子两局刷新逐项相同。

## 段 B：带入与结算（RQ4 / RQ5 / RQ6）

1. 档案 v2、v1 迁移、兑换、新档案赠送。
2. `CarryLoadout`（≤5、保险 ≤2）、在途记录、对局内未挂出列表与存档。
3. D6 归属结算：完赛 / 弃赛 / 出局 / 中途退出 / 截断。
4. AI 带入（`carry-ai`）。
5. 终端：带入选择、部署挂出参数、结算打印；Sim `--carry-in 0..5`。
6. 测试：AC4–AC6。

## 段 C：AI 估值与 Godot（RQ8 / RQ9，R42 拆为 C1 / C2 顺序派工，W1 45b35d4 / W2 4953400 已提交）

### C1 AI 与遥测
1. `RelicEstimate` 重写（design D5）：对每个地图信物格估"拾取后势力增量"，对敌方持物棋子估"提走后掉落可拾取"的价值；保留 `EvaluationWeights.Relic` 维度、重标权重；AI 评价版本随之递增（R34）。
2. AI 挂出策略：从 W2 的"按落子顺序"改为"挂在本批势力增量最大的落点"，前几批挂完（D5）；去掉 `HeuristicTurnController` 里的 TODO。
3. 遥测：刷新预告 / 落位、挂出、拾取、掉落、带出进日志（match-telemetry）；日志棋串条目补记信物，恢复 `各棋子势力占比` 的 Base / 协同逐项相等（R37）。
4. 验收：AC8（≤ 20 局小样本有拾取行为，遥测可数，不跑 200 局）；决策基线如重录写明原因与首个分歧点。

### C2 Godot 与规格
1. Godot：带入面板替换 W2 的最小面板（选 ≤5、勾保险、兑换）；暂放棋子时从带入栏点选一件挂上；信物层三种标记（地图信物 / 预告 / 持物棋子，含保险标记）；结算面板列每件去向；文本。只用现有控件 + 简单图标。`--auto-demo` 等自动化模式不读写档案。
2. 终端 `!` / `@N` 语法沿用或与 Godot 对齐，二选一写进规格。
3. openspec 规格与设计文档按 design.md 表格同步（替换 / 修订）。
4. 验收：AC9（Godot 手动走一遍：选图 → 带入 → 开局 → 挂出 → 拾取 → 结算）、AC10。

## 验证命令

```
dotnet build -c Release
dotnet test
```

Godot 走查：选图 → 带入面板 → 开局 → 挂出 → 拾取 → 结算面板。

## 岛图线遗留（feat/island-map 97b7d21，R29 落实）

- 岛图 `MapData.RelicCells` 恒为空，旧信物系统空跑。
- 仍测旧信物的测试改用手写夹具：`tests/Siege.Core.Tests/LinkMapFixtures.WithRelics(map)`、`LinkModeFixtures.RelicMap / CreateWithRelics / StartedWithRelics` —— 拆旧信物系统时随旧测试一起删。
- `RelicMap` 标识也是 `link:1:4` 但摘要不同，不能用于按标识从目录恢复的回放。

## 风险点

- 157 个测试文件引用旧信物：段 A 改动面最大，先删旧测试再补新测试，避免逐个修补。
- `Board.Serialize` 被同形禁则、存档与结算一致性核对共用：按 design D1 拆成 `SerializeShape()`（同形）与 `Serialize()`（含信物层，存档 + 核对），补一条"挂出只在一侧生效时核对抛错"的测试。
- AI 预演调用量大：信物层加入 `Clone` 后注意单步耗时（只做对比，不跑 200 局）。
