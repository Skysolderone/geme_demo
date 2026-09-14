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

### 变异还原不要用 `git checkout -- 文件`

check 阶段做变异时工作树里还有实现方**未提交**的改动，`git checkout -- file` 会把它们一起冲掉。规则：变异前 `cp` 备份，还原后 `cmp` 逐字节校验；主会话用 Edit 工具做变异也一样——还原后 `git diff -- 文件` 应与变异前一致，而不是为空。

### 带回填兜底的字段，写入路径要用"非回填值"证伪

字段缺失时按公式回填（如生效倍率指数缺失 → `min(数量, 5)`），那么"写入路径漏写"和"回填算出同一个值"在测试里不可区分——往返测试必须写入一个与回填值**不同**的数再读回（cap-multiplier 变异 M-M6 的形状），否则漏写恒真。

### 红测变绿后必须整段复审

一条测试的第一个红断言会挡住后面所有断言，后面那些**从未被执行过**。match-flow 的 `小回合边界存档恢复后状态完全一致` 修好第 30 行的前提错误后，第 61 行才暴露出一个恒假断言（见下）。规则：修红测不是"让它绿"，而是把该测试从头到尾当新测试审一遍，并对新暴露的断言补变异验证。

同类根因：测试注释里"P0 落 E5 使 P2 出局"这种**因果声明必须用断言钉住**（`Assert.Equal(PlayerStatus.Eliminated, match.StateOf(P2).Status)`），否则前提错了（P2 其实在 B8 还有子）只会在很远的下游断言以莫名其妙的数字失败。

## 持久化守门

### 含 `ImmutableArray` 的 record 不能直接 `Assert.Equal`

`ImmutableArray<T>` 是结构体，但它的 `Equals` 是**底层数组引用相等**。所以任何含 `ImmutableArray` 字段的 record（`PlayerPower`、`PowerSnapshot` 等）在 `Assert.Equal(a, b)` 下对两个独立构造的等值对象**恒假**，而对同一对象引用恒真——两种情况都不是在守门。

```csharp
// 恒假：Players 里的 ExclusiveCells / Groups 是 ImmutableArray
Assert.Equal(match.Scoreboard.Latest!.Players, restored.Scoreboard.Latest!.Players);

// 正确：投影成值再比
Assert.Equal(PowerText(match), PowerText(restored));   // 把每个字段拼成确定性字符串
```

规则：比较持久化前后的复合对象，一律投影成基本值/字符串/序列后比较；`Assert.Equal(json, restored.Serialize())` 这种逐字节比对可以并列做，但不能替代字段级比对（见下一条）。

### "存档→恢复→再存档逐字节相等"抓不到漏字段

`Serialize` 漏写一个字段（变异 M-C4：不写 `Protection`），恢复对象的该字段就是默认值，再次 `Serialize` 仍然不写它——两份 JSON 逐字节相等，百局往返测试全绿。只有拿**活对象**的字段和**恢复对象**的字段逐个比才会红。持久化守门测试必须两条腿都有：

| 断言 | 抓什么 |
|---|---|
| `Assert.Equal(match.X, restored.X)` 逐字段（含私有视图、子流消费位置） | 漏写 / 漏读某个字段 |
| `Assert.Equal(match.Serialize(), restored.Serialize())` | 恢复过程本身引入的差异 |
| 恢复后**续跑同样决策**，再比一次面板与存档 | 随机子流位置、快照等"存起来了但没用上"的状态 |

"含 `ImmutableArray`" 应读作"含任何集合字段"：`List<T>`、数组同病，heuristic-ai 阶段 B 因此两条测试假红。

### 逐字节文本比对必须钉住覆盖范围

"串行与并行结果一致"、"凭种子复现"这类测试如果只比对日志文本，而文本首行含种子 / 标识，那么把日志砍到只剩首行，两边照样相等——断言恒真。heuristic-ai 的 check 用变异 M-C5（`DeterministicText` 只输出 header）抓到两条这样的假守门。规则：逐字节比对必须配**行数下界**（或条目计数）与至少一处**字段级投影比对**（快照 / 事件 / 名次逐条），让"内容缺了一大块"也能红。

### 反射闭包守门要对非根类型做变异

信息边界一类的"类型闭包里不得出现 X"测试，闭包展开是否真的深入到字段 / 参数 / 返回类型，只有给**非根类型**加一个泄漏成员（M-C1：`BatchEvaluator` 加 `RelicGenerationRecord? Leak` 属性）看它红不红才知道；只在根类型上加是测不出遍历深度的。

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

## 提交信息用 `-F` 文件，不内联

提交信息里出现英文双引号（如 `"落 1 子规避 Pass"信号`）会截断 shell 的双引号字符串，`git commit -m` 把后半段当 pathspec 报错——而链式命令里后面的归档照跑，把源码吞进别的提交。recruit-hand 收尾时发生过一次，靠 `git reset --soft` 重拆。规则：先把提交信息写进临时文件（heredoc 用引号定界符 `<<'EOF'`），再 `git commit -F 文件`；并且提交与归档之间用 `&&`，不用 `;`。

## 命令

```bash
dotnet build                          # 零警告（TreatWarningsAsErrors 已开）
dotnet test                           # 全部测试
dotnet test --filter "FullyQualifiedName~棋串"   # 按名过滤
```
