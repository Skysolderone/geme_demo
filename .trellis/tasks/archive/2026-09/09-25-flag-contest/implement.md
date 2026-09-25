# 实施记录：flag-contest

## 1.1 测试（先红）

先落 API 骨架（`GameSeed.FlagRisk`、`MatchOptions.FlagRisk` = 15、`Assign` 新增必填参数 `flagRisk` 但忽略、`RunConfig.FlagRisk` 可空、分析段 `SharedZoneSection` 空壳），保证红是断言红而不是编译红。

`tests/Siege.Core.Tests/MatchSetup/原型插旗替代路径Tests.cs`（Requirement 类）：
- 既有依赖锁定结果的测试全部改用 `NoRisk = MatchOptions.Immediate with { FlagRisk = 0 }`（6 处 `MatchFlow.Create` + `Zones` 夹具 + 批量侧 `SimFixtures.Config(...) with { FlagRisk = 0 }`）；`区数等于人数时保持现状` 按规格改写成"冒险概率为 0"。
- 新增：`冒险概率为0时锁定结果不变`（v5 + 6 平台图 × 种子 1–12 × 2–4 人 × 无人工 / 每座位 × 每区，共 1000+ 种开局，期望来自测试内对旧两支规则的独立复算）、`冒险加入已有人的出生区`（p = 100，人坐 P1 选 2 号区 → 全员 1 号索引；无人工时 P0 按原规则取区、其余跟进；6 平台图同理）、`冒险抽签可复现且不扰动其他随机`（p = 15 两次 + p = 0 对照，种子 1–12，信物与首回合顺序逐项相同，样本口径：至少一颗种子冒了险）、`冒险抽签的子流名与消费方式是可复现契约`（钉 "flag-risk"、五条子流名互异；v5 / 6 平台 × p 15 / 50 × 种子 1–30 × 三种人工选择，独立复算，样本口径：冒险 ≥ 30 次）、`冒险概率须在0到100之间`、`冒险概率进入日志首部`（非缺省值 37 写入 / 缺省落成 15）。

`tests/Siege.Core.Tests/SimulationHarness/冒险概率记录Tests.cs`（D2）：`跑局配置的冒险概率文本往返`、`首部缺冒险概率的旧日志按0回放`（样本种子 = 缺省 p 下锁定结果与 p = 0 不同的第一颗，样本口径断言）、`命令行冒险概率写入配置记录`（37 写入 config.json 与首部；-1 / 101 / abc 报错、不写输出）。

`tests/Siege.Core.Tests/MatchTelemetry/插旗同区统计Tests.cs`（D3）：构造日志算例（含两类被排除样本：非同区玩家、截断同区局）、无同区时 0 / 无样本、真实跑局 p = 100 / 0 端到端。

实现前实跑：11 红（全部是断言 / 前提失败），22 绿（既有测试 + `冒险概率为0时锁定结果不变`——它描述的就是改动前行为，作为 p = 0 回归守门，由变异 M2a / M2b / M1b 证其会红）。

## 1.2 实现（D1）

- `GameSeed.FlagRisk = "flag-risk"`（第五条子流）；`MatchOptions.DefaultFlagRisk = 15`、`MatchOptions.FlagRisk`（缺省 15，`Default` / `Immediate` 同）。
- `PrototypeZoneAssignment.Assign(map, seed, players, int flagRisk, manual = null)`：`flagRisk` 必填，由 `MatchFlow.PlantPrototype` 传 `Options.FlagRisk`。
  原两支（`Sequential` / `Seeded`）并成一个按编号的循环：`planted`（SortedSet，初值 = 人工选区）非空时先抽 `risk.NextInt(100) < p`，命中则 `planted.ElementAt(risk.NextInt(planted.Count))`；
  否则原样走 zone-pick 空闲表 / 顺排游标（含"跳过人选区"），这两份状态只在这一支推进。冒险只加入已有人的区，不会占新区，所以顺排游标仍正确。
- 新增 `PrototypeZoneAssignment.RequireValidFlagRisk`（0–100，否则 `ArgumentOutOfRangeException`）；`MatchFlow.Build` 与 `Assign` 共用。
- 终端 `PlayCommand` / Godot `MatchSession` 仍传 `MatchOptions.Immediate`（缺省 15，不加选项）；`PlayCommand.Run` 加测试接缝 `flagRisk`（同 `weights` / `passThreshold` 先例，终端入口不传）。

实现后：1.1 的 33 条全绿。

变异（脚本 `mutate.py`：二进制读写、按文件实际行尾归一锚点并断言命中恰 1 次、try/finally 还原、逐字节比对、`os.utime` 刷新 mtime、备份名带时间戳；统计行按英文输出解析）。M1–M3 跑全量 `dotnet test -c Release`（基线 1425 通过 / 5 跳过）：

| 变异 | 改动 | 结果 |
|---|---|---|
| M1a 冒险抽签改用 ZonePick 子流（新派生实例） | `seed.Stream(GameSeed.FlagRisk)` → `seed.Stream(GameSeed.ZonePick)` | 红 1：冒险抽签的子流名与消费方式是可复现契约 |
| M1b 冒险抽签与原型选区共用同一 ZonePick 实例 | `risk = pick ?? seed.Stream(GameSeed.FlagRisk)` | 红 4：冒险概率为0时锁定结果不变、两条子流契约、对局配置公开完整地图标识Tests.地图种子不扰动对局随机 |
| M2a p = 0 时在原规则之外推进 zone-pick | 已有旗且区数 > 人数上限时，抽签前多调一次 `pick.NextUInt64()` | 红 4：冒险概率为0时锁定结果不变、两条子流契约、地图种子不扰动对局随机 |
| M2b p = 0 时在原规则之外推进顺排游标 | 已有旗且顺排时，抽签前 `next++` | 红 21：冒险概率为0时锁定结果不变、区数等于人数时保持现状、标准图顺排黄金 6 + 批量侧黄金 6、冒险契约、候选格上限 / 停手阈值两条逐步黄金、日志覆盖八类记录、旧日志缺区数、批量执行并汇总、真实跑局的同区经首部进入统计 |
| M3a 之前没有旗也抽签 | `planted.Count > 0 && risk.NextInt(…) < p` 两项对调 | 红 1：冒险抽签的子流名与消费方式是可复现契约（p = 100 的 Scenario 对它是空证，见顾问意见：命中后结果相同，只有子流对齐变了） |
| M3b 之前没有旗也冒险 | 守卫改成 `planted.Count >= 0` | 红 11（无旗时 `NextInt(0)` 抛出）：冒险加入已有人的出生区、冒险契约、首部缺冒险概率的旧日志按0回放、真实跑局同区、批量执行并汇总、校准后截断率达标、各棋子势力占比、对局配置公开完整地图标识 4 条存档测试 |

附加变异（只跑相关测试类：原型插旗替代路径 / 冒险概率记录 / 插旗同区统计 / 停手阈值 / 可复现回放，50 条）：

| 变异 | 改动 | 结果 |
|---|---|---|
| R1 回放缺字段取缺省 15 | `config.FlagRisk ?? MatchOptions.DefaultFlagRisk` | 红 1：首部缺冒险概率的旧日志按0回放 |
| C1 CLI 不读 --flag-risk | 删掉 `Program.Run` 里的 `FlagRisk = …` 一行 | 红 1：命令行冒险概率写入配置记录 |
| A1 同区局的全部玩家都算同区玩家 | `SharedZonePlayers` 条件改成 `zones.Distinct().Count() < zones.Count` | 红 1：同区对局与同区玩家的名次和胜率 |
| E1 入口层自己引用冒险子流（收尾时补的守门） | `PlayCommand` 类体首行加注释 `// 自己派生冒险子流 GameSeed.FlagRisk` | 红 1：三个入口的选区都走内核的唯一实现（只跑该类：基线 27 绿 → 红 1） |

收尾加固：`三个入口的选区都走内核的唯一实现` 的入口层禁用记号加上 `GameSeed.FlagRisk`，反面命中加"唯一实现文件里确有 `GameSeed.FlagRisk`"。
**没有**禁字面量 `"flag-risk"`：它同时是 `Program.cs` 的命令行选项名，第一次加进去时本测试在真实代码上就红了（全量红 1，报 `Program.cs`）——当时 E1 的"红 1"是假红，已删掉该字面量、改用 `GameSeed.FlagRisk` 作变异记号，先确认基线绿再跑 E1。

全部还原后逐字节一致、`.bak` 已删、mtime 已刷新；之后重建 Release 再跑冒烟与最终全量。

## 1.3 黄金哈希 / 探针

写死 p = 0 的位置：`原型插旗替代路径Tests`（`NoRisk`）、`SimFixtures.PinPreCalibration`（`FlagRisk = 0`，`Sample` / `RankedSample` 等全部经它）、`对局配置公开完整地图标识Tests.地图种子不扰动对局随机`、`防死锁硬停Tests`、PlayCommand 脚本测试 5 处（`终端对局`、`终端活形与禁入标示`、`边疆图终端试玩脚本`、`各入口按地图标识选图` ×2 处 3 次调用）。

实现后首次全量（夹具已写死，PlayCommand 等尚未写死）：红 2，均为记录类断言——`批量跑局Tests.批量执行并汇总`、`默认评价权重的校准Tests.引用未校准维度产出的数据`，期望的 config.json / 首部文本少了落成的 `"FlagRisk": 15`（D2 行为变化，按"未配置也落成实际生效值"改期望）。**没有任何黄金哈希分叉。**

探针（脚本同变异：二进制读写、finally 还原、逐字节比对、`os.utime`；全量）：
- P1：`MatchOptions.DefaultFlagRisk` 15 → 100（找出所有跟随缺省 p 的测试）→ 红 7：`冒险概率须在0到100之间`、`批量执行并汇总`（这两条钉缺省值本身，应红）+ 5 条跟随缺省 p、在 p = 15 下只因所用种子恰好没冒险才绿的依赖走法测试：`各入口按地图标识选图Tests.选边疆图`、`终端对局Tests.脚本输入能落子并走到输入耗尽`、`防死锁硬停Tests.规则层无上限时硬停以失败局记录`、`对局配置公开完整地图标识Tests.地图种子不扰动对局随机`、`边疆图终端试玩脚本Tests.脚本走到第五大回合…`。这 5 条随后全部写死 p = 0（另把同样依赖脚本走法、P1 下未红的 `终端活形与禁入标示` 与 `各入口按地图标识选图` 的显式 / 隐式地图那条一并写死）。
- P2：去掉 `PinPreCalibration` 里的 `FlagRisk = 0`（回到缺省 15）→ 红 3：`可复现回放Tests.失败局可复现`、`对局日志的记录内容Tests.日志覆盖八类记录`、`日志首部区数Tests.旧日志缺区数字段按被选到的最大区号回填`——夹具写死是承重的（这些样本种子在 p = 15 下会冒险）。
- 读缺省值的校准测试 `默认评价权重的校准Tests.校准后截断率达标`（种子 1–20、4 名 Standard、缺省配置）按"钉校准值本身的测试才读默认值"不写死；在 p = 15 与 p = 100 下都绿。

写死后全量：通过 1425、跳过 5、失败 0。

## 2.1 配置与记录（D2）

- `RunConfig.FlagRisk`（`int?`，紧跟 `PassThreshold` 声明，首部文本里 `"PassThreshold":…,` 的既有断言不受影响）；`Validated` 拒绝 < 0 / > 100；`ResolvedFor` 未配置落成 15。
- `MatchSession.Create`：`config.FlagRisk ?? (recorded ? 0 : 15)` 进 `MatchOptions.Immediate with { … }`——回放缺字段按 0。
- 日志首部：`LogHeader.Config` 即落成后的配置，p 随之写入（沿用停手阈值先例，不另加顶层字段）。
- CLI：`run --flag-risk <0-100>`；越界由 `Validated` 报"冒险概率须为 0–100…"，非整数由 `int.Parse` 报错，均退出码 1、不建输出目录；用法行已补。

## 2.2 分析（D3）

`BalanceAnalyzer`：新段 `SharedZoneSection`（`BalanceReport.SharedZones`）。同区 = 首部 `Zones` 里 ≥ 2 名玩家同区（区号 < 0 不算），`SharedZonePlayers` 给出同区玩家；
同区对局数 / 占比用全部纳入局（含截断），同区玩家平均名次与胜率（Wilson）只用有名次的局。`ReportWriter` §17 第 11 条"插旗同区"两行输出。只报告。

## 3.1 冒烟（只报告，未调参）

命令：`Siege.Sim run --out sim-out/flag-contest/smoke20 --seed 1 --count 20 --players 4 --difficulty Standard`（v5、默认权重、阈值 80、截断 600、缺省 p）。
已核对 `config.json`：`siege-4p-base-v5`、4 名 Standard、九维默认权重、`PassThreshold` 80、`FlagRisk` 15、`TurnLimit` 600；脚本 `sim-out/flag-contest/smoke.py` 另逐局断言首部 `FlagRisk` = 15。
指标 → `smoke20/smoke-metrics.json`；分析报告 `sim-out/flag-contest/smoke20-report.md`（§17 第 11 条与脚本数字一致）。墙钟 173 s。

| 指标 | 值 |
|---|---|
| 同区对局 | 7 / 20（种子 3、4、5、6、10、11、19；其中种子 6 为三人同区，其余两人同区） |
| 截断 | 1 / 20（种子 10，turn_limit R200，恰是同区局） |
| 终局原因 | AllPassed 19、turn_limit 1 |
| 结束大回合（20 局） | 平均 20、中位 9.5、最短 7、最长 200 |
| 同区玩家名次（6 局有名次，13 人次） | 平均 2.69；胜 3 次（23.1%，95% 区间 8.2%–50.3%） |

逐局同区玩家名次：种子 3 [P1 4, P2 2]、4 [P2 4, P3 3]、5 [P0 3, P2 4]、6 [P0 3, P2 1, P3 4]、11 [P0 3, P1 1]、19 [P1 1, P3 2]；种子 10 截断无名次。
样本仅 20 局，比例区间很宽，不下结论。

## 3.2 文档

- `2026-09-10-siege-core-gameplay-design-v1.md`：版本 v1.11 → v1.12；§4.1 补原型依次插旗的冒险概率一条；变更记录加一行（含冒烟数）。
- 规范真相源同步：`.trellis/spec/core/determinism.md` 子流表四条 → 五条（加 `flag-risk`）；`boundaries.md` 单一实现表"原型插旗路径的 AI 选区"一行注明含冒险概率。

## 3.3 回归

- `dotnet build siege.sln`：0 警告 0 错误
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误
- `dotnet test -c Release`：通过 1425、跳过 5（慢测试 / 计时，按门控默认跳过），失败 0，退出码 0（最终一次 2 m 27 s，在 E1 守门加固之后）
- `openspec validate flag-contest --strict`：valid
- 未跑任何 200 局规模的慢测试。

## 待决

1. 冒险概率不进存档（`MatchSaveData`）：锁定之后它不再起作用；若在插旗阶段存档再恢复，恢复出的局按缺省 15 插旗。是否要入存档（缺字段按 0）由负责人定。公开视图 `MatchPublicView` 也未暴露 p。
2. 冒烟 20 局里同区 7 局（p = 15 时每局"至少一人冒险"的理论概率约 1 − 0.85³ ≈ 39%，量级吻合）；同区玩家胜率 3 / 13、截断的 1 局恰为同区局——样本太小，只报告，是否调 p 或调 AI 同区应对另议。
3. `默认评价权重的校准Tests.校准后截断率达标` 读缺省配置（含缺省 p = 15），按规范属"钉校准值本身"的测试、未写死 p；它在 p = 15 与 p = 100 下都绿。若负责人认为它应与 p 脱钩，可写死 0。
4. 工作树里未跟踪的 `src/godot/scripts/PartExport.cs.uid` / `TerrainParts.cs.uid` 未动。
