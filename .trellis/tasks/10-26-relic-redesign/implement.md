# 实施计划：信物系统重设计

**前置**：10-26-map-expand-25 完成并合回 main（R7）。开工前在新 main 上重新核对 design.md 的"出生区域"抽象与静态信物格删除点。

派工：一条线、独立 worktree，三段顺序，每段一个 trellis-implement 子 agent，段间主会话验收（沿用 map-expand-25 的派发方式，派前确认）。

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

## 段 C：AI 估值与 Godot（RQ8 / RQ9）

1. `RelicEstimate` 重写、AI 挂出策略、权重重标。
2. Godot：带入面板、暂放挂出、信物层三种标记、结算面板、文本。
3. 遥测：刷新 / 拾取 / 掉落 / 带出。
4. openspec 规格与设计文档按 design.md 表格同步（替换 / 修订）。
5. 验收：AC8（≤ 20 局小样本）、AC9（Godot 手动走一遍）、AC10。

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
