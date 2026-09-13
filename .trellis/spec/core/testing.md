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

## 新守门测试必须做变异验证

写完一个测试，**改实现的一行让它本该红**，看它是不是真的红。不红就是假测试，删掉重写。

board-core 阶段先后被自审（`邻接实现唯一`，提交 25b5f4d）与 `trellis-check`（`共享气去重` / `共享出生区` / `信物格只记录位置`，提交 b2f6854）抓出 4 个假测试，全部出自同一个作者（主会话），每个都通过了"看起来在验证 Scenario"的自审。变异验证是唯一可靠的过滤器。

### 假测试的常见形状（都在本项目真实出现过）

| 形状 | 实例 | 为什么恒真 |
|---|---|---|
| 比较被测方法与它的委托目标 | `Assert.Equal(Adjacency.Neighbors(...), board.Neighbors(c))`，而后者实现就是 `return Adjacency.Neighbors(...)` | 两边是同一个调用 |
| 同一表达式读两次再比较 | `var a = Map.BirthZones[1]; var b = Map.BirthZones[1]; Assert.Equal(a, b)` | 读的是同一个对象 |
| 对只有两个成员的枚举断言 `is A or B` | `Assert.True(spec.Zone is BirthZone or Contested)` | 枚举没有第三个值 |
| 用例几何上触发不到被测路径 | 用直线相邻的 `D4`+`D5` 测"共享气去重" | 四邻接下直线相邻的两枚棋子**不可能共用邻格**（中间那格就是棋子本身），去重从未发生；要触发必须用拐角串如 `D4-D5-E5`，`E4` 同时邻接 `D4` 与 `E5` |
| 期望值恰好等于哈希表的插入序 | `障碍减少气` 断言 `["F5","E6","G6"]`——单子棋串的邻居本就按字典序插入 `HashSet`，枚举顺序与字典序重合 | 作为集合断言没问题，但作为"排序"守门是假的：去掉 `Order()` 也过。要钉住排序得挑一个插入序与字典序不同的用例，如 `共享气去重` 的拐角串（`F5` 与 `D6` 会换位） |

### 变异验证的记录方式

在测试的注释或提交信息里写清：改了实现哪一行、改成什么、红了几个。没有记录的变异验证等于没做。

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

## 提交前的测试必须用真实退出码把关

`dotnet test ... | tail -1 && git commit` 这种写法，`&&` 看到的是 `tail` 的退出码，永远为 0——红测试会被原样提交。relic-system 收尾时就这样把一条过期断言连同归档一起提交了，靠事后肉眼才发现。

规则：把 `dotnet test` 单独执行并保存退出码（`dotnet test > log; RC=$?`），提交语句用 `[ $RC -eq 0 ] &&` 把关；要看摘要就 `tail` 那个日志文件，不要把 `dotnet test` 接进管道再 `&&`。变异验证同理：先记录基线退出码，变异后看失败数，还原后再确认退出码为 0。

## 命令

```bash
dotnet build                          # 零警告（TreatWarningsAsErrors 已开）
dotnet test                           # 全部测试
dotnet test --filter "FullyQualifiedName~棋串"   # 按名过滤
```
