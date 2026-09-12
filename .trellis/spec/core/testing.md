# 测试组织

## Requirement → 测试类，Scenario → 测试方法

`openspec/changes/<change>/specs/<capability>/spec.md` 是验收基准。映射方式：

```
Requirement: 棋串构成
  Scenario: 跨类型合并棋串
  Scenario: 敌我相邻不合并
        ↓
tests/Siege.Core.Tests/<Capability>/棋串构成Tests.cs
  [Fact] 跨类型合并棋串()
  [Fact] 敌我相邻不合并()
```

测试名直接用 Scenario 名（中文方法名在 C# 中合法），便于与 openspec 逐条对账。

多组数据的 Scenario 用 `[Theory]` + `[InlineData]`。

## 算例必须来自设计文档

涉及数值的测试，其输入与期望值 MUST 取自 `2026-09-10-siege-core-gameplay-design-v1.md` 或 openspec spec 里写明的算例，并在测试上注明出处：

```csharp
// 设计文档 §10.1：普通子×3 + 堡垒子×1 + 倍增子×2 → 基础 9、倍率 2.25、军势 20
```

不要自己编期望值——编出来的期望值会把实现的 bug 一起固化。

## 强制回归

下列测试是硬约束，不得以"太慢"为由跳过：

| 回归 | 内容 |
|---|---|
| 提子顺序无关性 | 随机化遍历顺序跑同一批次 100 次，结果一致 |
| 预演零副作用 | 任意预演后正式盘面序列化逐字节不变 |
| 随机子流隔离 | 改变征募决策后信物生成逐格不变 |
| 出局保护逐玩家 | 先手行动不误伤未行动玩家 |
| 手牌两段账 | 回合前 4 + 本轮新增 2 → Pass 后剩 4 |
| 倍率取整边界 | n=0..8 各阶，恰为整数的值必须取到该整数 |

## 命令

```bash
dotnet build                          # 零警告（TreatWarningsAsErrors 已开）
dotnet test                           # 全部测试
dotnet test --filter "FullyQualifiedName~棋串"   # 按名过滤
```
