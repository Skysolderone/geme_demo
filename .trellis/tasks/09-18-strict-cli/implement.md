# 09-18-strict-cli 实施记录

改了什么 / 既有测试改写 / 变异验证逐条 / 待决。

## 改动文件

| 文件 | 改动 |
|---|---|
| `src/Siege.Sim/Cli/CommandLine.cs` | `_consumed` 集合 + `TryRead` 统一记录；`LegalOptions` / `UnrecognizedOptions` / `EnsureRecognized`；`NearestOption` + `EditDistance`（Levenshtein 滚动两行） |
| `src/Siege.Sim/Program.cs` | 五个子命令加结算调用；`Play` / `Analyze` 把选项读取上提到执行之前；`map` 改为接收 `cli` |
| `tests/Siege.Core.Tests/SimulationHarness/批量跑局Tests.cs` | 新增 5 条守门测试 |

## 实现要点

- **2.1 消费记录**：`Has / Get / GetOrNull / GetInt / GetUInt64 / Flag` 全部走 `private bool TryRead(string key, out string? value)`，进来先 `_consumed.Add(key)`。记录的是"读取方问过这个 key"，与该选项是否真的给出无关——所以 `--count` 没给也算 run 认识它。
- **2.2 结算**：`UnrecognizedOptions(params string[] declaredOptional)` 返回 `_options.Keys` 减去合法集合，`.Order(StringComparer.Ordinal)` 稳定排序（与 `_options` 自身的 Ordinal 比较器一致）。
- **2.3 建议**：`NearestOption` 在按 Ordinal 排序的候选上用严格 `<` 更新最优，得到"距离最小、并列取字典序靠前"的确定性结果；阈值 `MaxSuggestDistance = 3`。
- **2.4 结算位置**（全部在创建输出目录 / 执行对局之前）：
  - `run`：`Flag("serial")` 之后、`config.Validated()` **之前**（未知选项优先于配置校验报出），再 `ExecuteToDirectory`。
  - `analyze`：`dir` / `include-contaminated` / `out` 三个读取全部上提，结算后才 `MatchLog.ReadDirectory` 与写报告。
  - `play`：`players` / `seat` / `max-rounds` 由实参位置提成局部变量，结算后才 `PlayCommand.Run`。
  - `replay`：if/else 之后、`MatchLog.Read` 之前。
  - `map`：`ExportMap(CommandLine cli)` 首行结算，先于 `Directory.CreateDirectory("maps")`。

### 合法选项集合怎么来的（对应任务里"方式自定但要写清"）

合法集合 = `_consumed` ∪ `EnsureRecognized` 的 `declaredOptional` 实参。因为消费在**读取动作**上记录而不是在"选项存在"上记录，所有走无条件读取的子命令天然就有完整集合，无需任何白名单：

| 子命令 | 无条件读取 | 需额外声明 |
|---|---|---|
| `run` | 全部 16 个（`players` / `difficulty` 先经 `Has` 探测，也算读过） | 无 |
| `analyze` | `dir` / `include-contaminated` / `out` | 无 |
| `play` | `seed` / `difficulty` / `players` / `seat` / `max-rounds` | 无 |
| `map` | 无 | 无（任何选项都报错） |
| `replay` | `file` | `"dir"`, `"seed"`（只在没给 `--file` 的分支里读到） |

实跑核对：`run --matches 200` 报出的合法表正是 `--config --count --difficulty --dominance-start --gzip --map --max-rounds --no-catch-up --out --parallel --players --retention --sample-permille --seed --serial --site-values`，与 `PrintUsage` 里 run 的 16 个选项逐一对上。

### 错误信息格式

```
未知选项 --matches。合法选项：--config --count ... --site-values
未知选项 --oot（是否想用 --out？）。合法选项：--dir --include-contaminated --out
未知选项 --alpha、--zeta。合法选项：--count
```

多个未知选项按字典序用 `、` 连接；合法选项恒带 `--` 前缀，空集合打印 `（无）`。

> **`--matches` 够不着建议阈值**：`EditDistance("matches", "count") = 7 > 3`，`NearestOption` 对它返回 `null`。规格 Scenario 与人工验证 3.1 要求的"提示 `--count`"是靠 D3 的兜底——**合法选项全表**给出的，不是靠建议。所以钉建议逻辑的测试不能用 `--matches`（见变异 M-SC2），改用 `--cout`。

## 新增测试（`tests/Siege.Core.Tests/SimulationHarness/批量跑局Tests.cs`）

| 测试名 | 对应 | 内容 |
|---|---|---|
| `未知选项被拒绝` | spec Scenario / tasks 1.1 | `Program.Main(["run","--out",…,"--matches","200","--seed","1"])` → 退出码非 0、`Directory.Exists(outDir)` 为假、stderr 同时含 `--matches` 与 `--count` |
| `拼错的选项不被当作缺省值` | spec Scenario / tasks 1.2 | `--cout 3` → 消息含 `是否想用 --count`（CommandLine 层 + Program 层各一次），非零退出、无输出目录 |
| `全部选项被读取时正常执行` | tasks 1.3 | `run --out … --seed 1 --count 1 --max-rounds 2 --difficulty Easy --serial` → 退出 0、`summary.json` 存在、1 个 `match-*.jsonl` |
| `开关选项计入消费` | tasks 1.4 | 只 `Has("screenshot")` / 只 `Flag("serial")` 不报未知；**反面控制**：同样输入不读取时必须判为未知（否则前两条恒真） |
| `未知选项按字典序报出` | tasks 2.2 / 2.3（自做） | `--zeta --alpha` 必须按 `alpha, zeta` 报出且消息里 alpha 在前；并钉住 replay 的 `declaredOptional`（给了 `--file` 又给 `--seed` 不算未知） |

`拼错的选项不被当作缺省值` 顺带钉住并列取字典序：`cout→count` 与 `cout→out` 距离都是 1，必须选 `count`。

## 变异验证

方式：Python 脚本，备份名带时间戳后缀、还原写在 `finally`、还原后与"改之前读到的原文"比对；`DOTNET_CLI_UI_LANGUAGE=en`，红绿以退出码为准。基线 `dotnet test -c Release` = **808 通过 / EXIT=0**（改动前），加 5 条后 **813 / EXIT=0**。

| 编号 | 变异 | EXIT | 红 | 红了哪些 |
|---|---|---|---|---|
| M-SC1 | `Program.Run` 里删掉 `cli.EnsureRecognized();` | 1 | 2 | `未知选项被拒绝`、`拼错的选项不被当作缺省值` |
| M-SC2 | `NearestOption` 开头 `return null`（建议恒空） | 1 | 1 | `拼错的选项不被当作缺省值` |
| M-SC3 | `UnrecognizedOptions` 的 `!legal.Contains(k)` 改成恒真（已消费的也算未知） | 1 | 3 | `全部选项被读取时正常执行`、`开关选项计入消费`、`未知选项按字典序报出` |
| M-SC4 | `Has` 改回 `_options.ContainsKey(key)`（不计入消费） | 1 | 1 | `开关选项计入消费` |
| M-SC5（自做） | `UnrecognizedOptions` 去掉 `.Order(StringComparer.Ordinal)` | 1 | 1 | `未知选项按字典序报出` |

M-SC2 只红 1 条是**预期**且已写进测试注释：`未知选项被拒绝` 用的 `--matches` 距 `--count` 是 7，本来就走全表兜底，建议逻辑坏掉不影响它——这正是 1.2 必须换用 `--cout` 的原因。

还原核验：五条全部 `还原一致=True`；另用 `cmp` 对 `Program.cs` 与其变异前备份做了逐字节比对，一致。

> 踩坑记录（testing.md 可补）：变异脚本用 `open(..., 'r')` 读 + `open(..., 'w', newline='')` 写，把 `Program.cs` 的 CRLF 整文件换成了 LF。`git diff` 因 `core.autocrlf=true` 会归一化而看不出来，是 `cmp` 备份才抓到的。已把 `Program.cs` / `CommandLine.cs` 统一回 CRLF 并 `cmp` 复核。**变异脚本读写一律用二进制模式。**

## 验证结果

| 项 | 结果 |
|---|---|
| `dotnet build` | EXIT=0，0 Warning / 0 Error |
| `dotnet test -c Release`（还原后，最终） | **813 通过 / 0 失败 / EXIT=0**（基线 808 + 新增 5） |

## 人工验证

**3.1** `dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/strictcli-probe --matches 200 --seed 1`

- EXIT = **1**
- stdout 空（未打印"跑局：N 局"，即没进执行）
- stderr 首行：`错误：未知选项 --matches。合法选项：--config --count --difficulty --dominance-start --gzip --map --max-rounds --no-catch-up --out --parallel --players --retention --sample-permille --seed --serial --site-values`
- `sim-out/strictcli-probe` **未创建**（`ls` 报 No such file or directory）

**3.2** `dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/strictcli-ok --seed 1 --count 3`

- EXIT = **0**
- 首行：`跑局：3 局，种子 1..3，4 人，并行度 28，大回合上限 15，据点分值 5/15/45，输出 sim-out/strictcli-ok`
- 产物齐全：`config.json`、`summary.json`、3 个 `match-*.jsonl`
- 已 `rm -rf sim-out/strictcli-ok` 清理

**顺带抽验其它子命令**

- `map --foo 1` → EXIT=1，`错误：未知选项 --foo。合法选项：（无）`
- `analyze --dir sim-out/baseline --oot x` → `错误：未知选项 --oot（是否想用 --out？）。合法选项：--dir --include-contaminated --out`（建议路径在真实 CLI 上生效）

## 偏离与待决

1. **【偏离 design.md D2】** D2 写的是"条件分支下未被读到的选项，报错是正确行为"（举例：单独给 `--shots-dir` 应报未知）。派发指令明确要求"声明了但本次没被读到的可选项不能被当成未知"，且 tasks 2.3 的合法集合定义是"已消费 ∪ 声明的可选 key"。本次**按派发指令实现**：`replay` 声明 `"dir"` / `"seed"`，因此 `replay --file x --seed 7` 不再报错（该 `--seed` 确实不产生任何作用）。
   **待主会话裁决**：要么改 design.md D2 与之对齐，要么撤掉 `replay` 的 `declaredOptional` 回到严格口径（改动量：`Program.Replay` 一行 + 一条测试断言）。目前全仓只有 `replay` 一个子命令存在条件读取。
   **已裁决（主会话按 D2 严格口径）→ 检查阶段已把 `declaredOptional` 机制整个删除，详见下方「检查」节结论 1。本条不再待决。**
2. `run` 里 `--out` 缺失时 `GetOrNull("out")` 抛的是 `run 需要 --out <目录>。`，早于结算——即"既没给 `--out` 又打错了别的选项"时先报 `--out`。位置参数误用按 D5 不在范围内，未处理。
3. openspec `tasks.md` 的 1.1–3.2 已全部勾选。
4. 影响面核查：`CommandLine` 在 `src/` 下只有 `Program.cs` 一个消费方（无遗漏的未结算入口）；仓内没有任何 `.ps1` / `.sh` / `.py` 脚本调用 `Siege.Sim`，proposal Impact 里"既有脚本会开始报错"本次无实际命中。

> cmp 口径说明：`Program.cs` 与其 M-SC1 变异前备份在 CRLF 修正**之后**做了逐字节比对，一致；四份 `CommandLine.cs` 备份是在 CRLF 修正**之前**比对一致的（备份为 LF，当前文件已统一为 CRLF，再 cmp 会显示行尾差异，属预期）。内容差异为零，以 `git diff` 为准。

---

# 检查（trellis-check，2026-09-18）

基准：proposal / design D1–D5 / tasks 1.1–3.2、`specs/simulation-harness/spec.md`（含 change 内的 MODIFIED 增量）、`.trellis/spec/core/testing.md`。检查开始时工作树为主会话改完的状态（814 通过）。

## 问题清单

| # | 问题 | 处置 |
|---|---|---|
| 1 | **`replay` 的 `Has("dir") \|\| Has("seed")` 左支零覆盖。** testing.md 明文规定"`A \|\| B` 形状要把 `\|\|` 改成 `&&`（或单独反转 A）做变异"。实测变异 **M-CK4**（删掉 `cli.Has("dir") \|\|`）在主会话版本下 **EXIT=0、红 0**——只给 `--file` + `--dir` 的用例从未存在，左支是假守门。 | **已修**：`replay两种定位方式不得混用` 改为对 `--file+--seed` 与 `--file+--dir` 两个用例各断言一次；重跑 M-CK4 → 红 1。 |
| 2 | **反面用例不钉分支。** 原"只给 `--dir+--seed`"只断言 `DoesNotContain("不能混用")`——任何别的报错（甚至提前崩）都算过。 | **已修**：补 `Assert.Contains("没有种子", …)`，钉住它确实走进了 else 分支。 |
| 3 | **`LegalOptions/UnrecognizedOptions/EnsureRecognized` 的 `params string[] declaredOptional` 在 replay 改动后成了生产零调用的白名单机制**，仅由 `未知选项按字典序报出` 里一段断言"守着"——按 testing.md 的口径，守一段死代码的断言不是守门。 | **已修**：三处签名删除 `declaredOptional`；测试里 `declared` / `mixed` 两段替换为"零白名单"的正面断言（只读 `--file` 时 `--seed` 必为未知）。结论见下。 |
| 4 | **spec 写的是"任一子命令"，但 Main 层守门只覆盖 `run`**；`analyze / map / play / replay` 的结算位置此前只有人工抽验，改回去不会红。且"建议"路径在真实 CLI 上也只有人工记录。 | **已修**：新增 `analyze也在写出报告之前结算`（`analyze --dir <空目录> --oot x` → 非零退出 + 含 `未知选项 --oot` 与 `是否想用 --out` + 无 `report.txt`）。新变异 M-CK2 验证其会红。 |
| 5 | 测试文件里 `replay两种定位方式不得混用` 的 `}` 与下一个 `[Fact]` 之间缺空行（与全文风格不一致；`EnforceCodeStyleInBuild` 不覆盖此项，构建不红）。 | **已修**（纯排版）。 |
| 6 | `implement.md`「偏离与待决 1」仍写着"待主会话裁决"，而主会话已裁决；测试数记的是 813。 | **已修**：待决 1 就地标注已裁决；本节给出最新数字。 |
| 7 | `run` 里 `--out` 缺失的报错早于结算（原记录待决 2）。 | **不修**。`GetOrNull("out")` 本身就是读取动作，报错顺序不影响 D4（未创建任何输出）。位置参数与必填项校验按 D5 不在本 change 范围。 |
| 8 | 测试用 `Console.SetError` 改进程全局状态，而 xunit 跨测试类并行。 | **不修**。`Console.SetError` 内部包 `TextWriter.Synchronized`，并发写不会抛；旁类串进来的文本只会让捕获内容变多，`Contains` 断言不受影响，`DoesNotContain("不能混用")` 的干扰源只可能是本类（本类内串行）。记录在案。 |

## 结论：第 3 项（`LegalOptions(params)` 去留）

**删除，已执行。** 理由：

- design D1 明文拒绝白名单（"白名单要人工同步，漏一个是假阴性，加一个不存在的名字是假阳性"）。`declaredOptional` 就是白名单，只是入口换成了实参。
- `replay` 改为直接报"不能混用"之后，生产代码零调用方。留着它等于给下一个有条件读取的子命令留一条"声明一下就静默放行"的捷径——正是 D2 禁止的行为。
- **它对严格性不承重**：即使没有 `Program.Replay` 里那句"不能混用"，`replay --file x --seed 7` 在 `EnsureRecognized()` 也会以 `未知选项 --seed` 非零退出（`--seed` 在 `--file` 分支下未被消费）。那句检查只是把报错说得更准（区分"你打错了"与"你混用了两种定位方式"），删掉白名单零损失。
- 现在合法集合 = `_consumed`，**唯一来源是读取动作**，与实现不可能脱节。`LegalOptions()` 保留为无参方法（`EnsureRecognized` 组装错误信息要用，且便于测试直接断言合法表）。
- 已在 `openspec/changes/strict-cli/tasks.md` 的 2.3 下补记落地偏差。

## 结论：第 5 项（其它读取命令行的入口）

**零波及。** 全仓读命令行的入口只有两处，彼此不共享任何代码：

1. `src/Siege.Sim/Program.cs` → `Siege.Sim.Cli.CommandLine`（本次改动范围）。`grep "new CommandLine("` 在 `src/` 下唯一命中 `Program.cs:27`，五个子命令全部经 `EnsureRecognized()` 结算，无遗漏入口。
2. `src/godot/scripts/GameRoot.cs:49-51,93-108` → `OS.GetCmdlineUserArgs()` / `OS.GetCmdlineArgs()`，用 `args.Contains("--auto-demo")` / `Contains("--pick-check")` / `StartsWith("--screenshot=")` 自行判定。**不引用 `Siege.Sim`，且 `src/godot` 不在 `siege.sln`**（tactical-ui 裁决 10），本次改动对它不可见。它本身也没有"静默忽略"风险的同类产物（不产出裁决数据），不在本 change 范围。

`grep "Environment.GetCommandLineArgs"` 全仓零命中。

## 结论：第 7 项（既有调用方式是否失效）

把 `README / HANDOFF / .trellis/spec / openspec / 根设计文档 / 已归档 implement.md` 里所有 `dotnet run --project src/Siege.Sim …` 的示例抽出选项名去重，得到：`--count --difficulty --dir --gzip --max-rounds --out --seed`（外加 `--matches`，只出现在 testing.md 与本 change 里作为"反面教材"）。这 7 个全部落在对应子命令的合法表内，**无一条既有调用方式失效**。逐条静态核对 + 抽样实跑：

- `run --out … --seed 1 --count 2000 --difficulty Standard --gzip`（HANDOFF:177）：选项全在 run 的 16 项内。
- `analyze --dir sim-out/baseline`（HANDOFF:178）：在 analyze 的 3 项内。
- `play [--difficulty Easy] [--seed N]`（HANDOFF:27）：在 play 的 5 项内；实跑 `play --foo 1 < /dev/null` → `错误：未知选项 --foo。合法选项：--difficulty --max-rounds --players --seat --seed`，EXIT=1，且**在进入交互之前**就报出。
- `map`（设计文档 §3.3 等）：无选项，实跑 `map --foo 1` → EXIT=1、`合法选项：（无）`，`git status maps/` 干净（未误写导出物）。

## 验证

| 项 | 结果 |
|---|---|
| `dotnet build -c Release` | EXIT=0，0 Warning / 0 Error |
| `dotnet test -c Release`（最终，退出码单独取，不接管道） | **815 通过 / 0 失败 / 0 跳过 / EXIT=0** |

计数口径：主会话交接时 814（= 基线 808 + 实现 5 + 主会话 1），检查阶段新增 `analyze也在写出报告之前结算` → 815。

**实跑（真实 CLI，`--no-build`）**

- `run --out sim-out/check-probe --matches 200 --seed 1` → **EXIT=1**；stdout 无"跑局：N 局"；stderr：`错误：未知选项 --matches。合法选项：--config --count --difficulty --dominance-start --gzip --map --max-rounds --no-catch-up --out --parallel --players --retention --sample-permille --seed --serial --site-values`；`ls -d sim-out/check-probe` → No such file or directory（D4 成立）。
- `run --out sim-out/check-ok --seed 1 --count 2` → **EXIT=0**；首行 `跑局：2 局，种子 1..2，4 人，并行度 28，大回合上限 15，据点分值 5/15/45，输出 sim-out/check-ok`；产物 `config.json` + `summary.json` + 2 个 `match-*.jsonl`；已 `rm -rf` 清理并确认目录不存在。
- 抽验：`replay --file x.jsonl --dir d` → EXIT=1 `不能混用`；`analyze --dir sim-out/baseline --oot x` → EXIT=1 `未知选项 --oot（是否想用 --out？）`；`map --foo 1` → EXIT=1 `合法选项：（无）`。

## 检查阶段变异（脚本二进制读写，备份带时间戳，还原在 `finally`，还原后比对字节串）

| 编号 | 变异 | 时机 | EXIT | 红 | 红了哪些 |
|---|---|---|---|---|---|
| M-CK4 | `Program.Replay`：`if (cli.Has("dir") \|\| cli.Has("seed"))` → `if (cli.Has("seed"))` | **修前** | 0 | **0** | —（暴露左支零覆盖） |
| M-CK4 | 同上 | **修后** | 1 | 1 | `replay两种定位方式不得混用` |
| M-CK1 | `CommandLine.NearestOption`：`distance < bestDistance` → `distance <= bestDistance`（并列改取字典序靠后者） | 修后 | 1 | 1 | `拼错的选项不被当作缺省值` |
| M-CK2 | `Program.Analyze`：删掉 `cli.EnsureRecognized();` | 修后 | 1 | 1 | `analyze也在写出报告之前结算` |

三条全部 `还原一致=True`（字节串比对）。M-CK1 与 M-SC5 不重复：M-SC5 钉的是**未消费选项的报出顺序**，M-CK1 钉的是**建议算法的并列裁决**（`--cout` 到 `--count` 与到 `--out` 距离都是 1，必须取 Ordinal 靠前的 `count`），对应 tasks 2.3 的确定性要求。

> 建议算法确定性核对：阈值 `MaxSuggestDistance = 3`、`legal.Order(Ordinal)` 上用严格 `<` 更新最优 → "距离 ≤ 3 且最小、并列取字典序靠前"三条齐备，且并列情形有测试（`拼错的选项不被当作缺省值`）+ 变异（M-CK1）钉住。未消费选项排序由 `UnrecognizedOptions` 的 `.Order(Ordinal)` 保证，已由 M-SC5 钉住。

## testing.md 增补（派发第 6 项）

"变异脚本必须二进制读写"这条**已实证并写进 `.trellis/spec/core/testing.md`**「批量变异脚本」一节。独立复现（scratchpad 里的一次性 git 仓，`core.autocrlf=true`）：

| 步骤 | 结果 |
|---|---|
| CRLF 文件 → `open(p,'r').read()` → `open(p,'w',newline='').write(s)` | CRLF=0，LF=3（整份文件行尾被换掉） |
| 脚本里"还原后与变异前原文比对"（**文本模式**） | `True`（假绿——两边都被归一） |
| 同一比对改成字节串 | `False`（真差异） |
| `git diff --stat`（`core.autocrlf=true`） | **空输出**，只在 stderr 给一句 `LF will be replaced by CRLF` 的 warning |
| `cmp` 与 CRLF 原文 | 抓到差异（`differ: char 6`） |

即三道防线（脚本自比对 / `git diff` / 编译测试）同时失效，只有二进制读写或 `cmp` 看得见。顺带补记了一条实战踩到的推论：**锚点串里的换行也要写 `\r\n`**，否则 `bytes.replace` 匹配不到而静默不变异（本轮 M-CK2 第一次就因此 anchor count = 0，幸而脚本有 `assert count == 1` 兜住——没有这个断言就是一条"红 0 条"的假结论）。

本轮改动后三份源文件行尾复核：`CommandLine.cs` 162/162、`Program.cs` 291/291、`批量跑局Tests.cs` 340/340（CRLF 数 = LF 数，即全 CRLF），`testing.md` 268/268、`tasks.md` 27/27。
