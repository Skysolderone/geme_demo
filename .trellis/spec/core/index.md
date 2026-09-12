# 规则内核开发规范（Siege.Core / Siege.Sim）

> 适用于 `src/Siege.Core`、`src/Siege.Sim`、`tests/Siege.Core.Tests`。
> 表现层规范见 `.trellis/spec/presentation/index.md`。

## Pre-Development Checklist

写第一行代码前：

1. 读本任务的 `prd.md` → `design.md` → 对应 `openspec/changes/<change>/design.md`（含「裁决记录（已确认）」）。
2. 打开对应的 `openspec/changes/<change>/specs/*/spec.md`，确认要实现的 Requirement 与 Scenario 清单。
3. 确认本次改动不触碰下列硬边界（违反即缺陷，不是风格问题）：
   - [边界与依赖](./boundaries.md) — 零 Godot 依赖、公开/私有视图分离、全量重算
   - [确定性](./determinism.md) — 随机子流隔离、整数运算、可复现
   - [坐标](./coordinates.md) — 围棋记法、单一映射
4. 按 [测试组织](./testing.md) 先建测试骨架，再写实现。

## Quality Check

提交前：

- [ ] `dotnet build` 零警告（`TreatWarningsAsErrors` 已开）
- [ ] `dotnet test` 全绿
- [ ] 本次涉及的每个 Scenario 都有对应测试，测试名可追溯到 Requirement 名
- [ ] 没有引入 `double` / `float` 参与任何计分或倍率计算
- [ ] 没有第二处邻接遍历、第二处坐标映射、第二套覆盖语义
- [ ] `Siege.Core` / `Siege.Sim` 没有出现 `Godot.*`

## Guidelines Index

| 文档 | 内容 |
|---|---|
| [边界与依赖](./boundaries.md) | 零 Godot 依赖、视图分离、全量重算、单一实现原则 |
| [确定性](./determinism.md) | 随机子流、整数/定点运算、可复现与重放 |
| [坐标](./coordinates.md) | 围棋记法、映射唯一性、四邻接 |
| [测试组织](./testing.md) | Requirement→测试类、Scenario→测试方法、回归集 |
