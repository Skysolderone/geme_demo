# v2-recalibration 实施记录

## 段 A：停手阈值在 V2 上复核（tasks.md 第 1 组，2026-09-28）

结论：选定 **20**（原 80）。本段共跑 6 批 × 20 = **120 局**：扫档 4 批、冒烟 2 批。没有任何一批超过 20 局，也没有运行 200 局慢测试。

### 1.1 配置文件

- 生成脚本：`research/mkcfg.py`，输出到 `research/configs/pass-{0,20,40,80}.json`。
  - 地图 v5，4 × Standard，九维权重逐玩家写死为当前 `Default`（10/8/6/35/4/20/2/200/25）。
  - SeedStart 1、Count 20、Parallelism 0、TurnLimit 600、ContentSet V2、CarryIn 0、EventRetention SnapshotsOnly；字段集与诊断 `mkcfg.py` 相同。
- `git diff --no-index`：pass-0 / 20 / 40 与 pass-80 两两比对，都只有 `PassThreshold` 一行不同。
- `pass-80.json` 与诊断 `b06-v5-std.json`、`pass-0.json` 与 `b07-v5-std-thr0.json` 用 `cmp` 比对，逐字节相同。

### 1.2 跑局与 config.json 核对

- 命令：`src/Siege.Sim/bin/Release/net8.0/Siege.Sim.exe run --config <cfg> --out sim-out/v2-recalibration/pass-<X>`（Release），没有传 `--difficulty` / `--players`。
  - 四批依次串行跑完，退出码均为 0，`FailedFiles` 为空，每批 20 局。
- 核对脚本 `research/check_and_compare.py`：逐项检查地图、内容集、阈值、带入、截断、种子、局数、人数、难度与每名玩家的九维权重。四批全部 OK。
- `pass-80/config.json` 与 `b06-v5-std/config.json`、`pass-0/config.json` 与 `b07-v5-std-thr0/config.json` 用 `cmp` 比对，逐字节相同。

### 1.3 忠实性对照（与诊断 b06 / b07）

- 诊断 `compare_logs.py` 逐种子比对落子序列、提子、终局原因、结束大回合与名次：pass-80 与 b06 **20/20 一致**，pass-0 与 b07 **20/20 一致**。
- 下表按种子列出 结束大回合 / 总提子 / 胜者，两边逐项相同。终局原因全部是 AllPassed，下表不再逐格重复。

| 种子 | pass-80 = b06 | pass-0 = b07 |
|---|---|---|
| 1 | R7 / 1 / P2 | R9 / 1 / P3 |
| 2 | R7 / 0 / P3 | R7 / 0 / P3 |
| 3 | R9 / 1 / P0 | R11 / 4 / P3 |
| 4 | R8 / 0 / P3 | R8 / 1 / P3 |
| 5 | R9 / 2 / P0 | R9 / 3 / P3 |
| 6 | R14 / 7 / P1 | R14 / 14 / P1 |
| 7 | R9 / 2 / P2 | R14 / 28 / P1 |
| 8 | R11 / 1 / P2 | R7 / 1 / P2 |
| 9 | R7 / 0 / P3 | R8 / 2 / P1 |
| 10 | R10 / 8 / P0 | R10 / 8 / P0 |
| 11 | R8 / 2 / P2 | R9 / 3 / P3 |
| 12 | R7 / 0 / P0 | R7 / 0 / P0 |
| 13 | R7 / 0 / P0 | R7 / 1 / P0 |
| 14 | R10 / 2 / P0 | R14 / 29 / P2 |
| 15 | R7 / 0 / P1 | R7 / 0 / P1 |
| 16 | R8 / 2 / P1 | R9 / 3 / P1 |
| 17 | R7 / 0 / P3 | R8 / 4 / P2 |
| 18 | R8 / 2 / P0 | R9 / 3 / P3 |
| 19 | R8 / 1 / P0 | R8 / 1 / P0 |
| 20 | R7 / 0 / P1 | R8 / 1 / P1 |
| 合计 | 无提子 8/20，总提子 31 | 无提子 3/20，总提子 107 |

结果与 tasks 预期（8/20、3/20；31、107）一致，管道一致，可以进入选档。

### 1.4 四档统计与选档（D2）

统计用诊断 `summarize.py`，口径与诊断相同；各批 `engagement-summary.json` 在各自的数据目录里。

| 档位 | 截断 | 整局无提子 | 已终局局平均结束 R（范围） | R3 领先者胜（分母 = 非截断且第 3 大回合有唯一或并列领先者的局） | 总提子 | Pass 率（全程 / R4+） |
|---|---|---|---|---|---|---|
| 0 | 0/20 | 3/20 | 9.15（7–14） | 15/20 | 107 | 24.4% / 28.9% |
| 20 | 0/20 | 3/20 | 9.25（7–14） | 12/20 | 118 | 24.8% / 28.8% |
| 40 | 0/20 | 5/20 | 9.05（7–14） | 14/20 | 103 | 25.1% / 29.0% |
| 80 | 0/20 | 8/20 | 8.40（7–14） | 13/20 | 31 | 26.8% / 31.3% |

> 段 C 注：上表 R3 列是"并列计入分母"的旧口径。按 D9 口径（唯一领先者，并列局与截断局不计入分母）重标为 0 → 14/19、20 → 11/19、40 → 13/19、80 → 11/18，设计文档与规格已按新口径写，见段 C。R3 不参与阈值选档，结论不变。

选档过程：

1. **截断过滤**：要求截断 ≤ 1/20。四档都是 0/20，候选为 {0, 20, 40, 80}。
2. **无提子带**：
   - 最少的是 0 档和 20 档，都是 3/20，p_min = 0.15。
   - SE = √(0.15 × 0.85 / 20) ≈ 0.0798，进带条件为比例 ≤ 0.2298，即不超过 4 局。
   - 40 档 5/20 = 0.25，80 档 8/20 = 0.40，都出带。候选为 {0, 20}。
3. **结束大回合**：两档的已终局局平均结束大回合为 9.15 和 9.25，都在 [7, 10] 内，距离同为 0，候选仍是 {0, 20}。
4. **离现值 80 最近**：|20 − 80| = 60，|0 − 80| = 80，选 **20**。

说明：

- 第 3 大回合领先者胜、总提子、Pass 率只作记录，不参与选档（D2 / D11）。
- 20 局属于小样本，胜率类指标只看方向。
- 80 是档位上界，80 以上没有 V2 数据；这次复核不算完整的双向扫档。
- 只在 v5 上复核，边疆图 / 生成图没有复核。

### 1.5 条件冒烟（选定 20 ≠ 80，执行）

配置 `research/configs/easy20-pass20.json` 与 `expert20-pass20.json`，由 `mkcfg.py` 生成：v5、V2、种子 1–20、带入 0、阈值 20，权重写死为 Default。

- config.json 核对通过。专家那名玩家落成的 `Search` 为 N 24 / M 32 / K 0 / 阈值 20 / 前瞻宽度 4。
- 判据脚本：`research/smoke.py`。

| 批次 | 截断 | 最长结束 R | 平均结束 R | 第 1 大回合全员一子不落即终局 | 总提子 | 对照 |
|---|---|---|---|---|---|---|
| easy20-pass20（简单 × 4） | 0/20 | 24 | 13.15 | 0 | 273 | b08（阈值 80）：截断 0/20，最长 44，平均 14.25，全员不落 0，提子 331 |
| expert20-pass20（1 专家 + 3 标准） | 0/20 | 18 | 9.65 | 0 | 111 | expert-lookahead/smoke20（阈值 80）：截断 0/20，最长 22，平均 8.95，提子 92 |

判据是"截断 ≤ 1/20，且简单难度不出现第 1 大回合全员一子不落"。两批都满足，**没有回退**，可以继续落地。

### 1.6 审计：依赖走法、且读缺省阈值或缺省权重的测试

用变异做实测审计：还没写死任何测试时，分别把缺省阈值改为 81、20 跑默认套件，两次都红 7 条，名单相同。

- 保真度类（写死 80，黄金值与期望一字不改）：
  - `停手阈值Tests.无子时不受阈值限制` / `有子后恢复阈值` / `同批次内不中途切换`：共用夹具 `LoneStones` 原来读 `DefaultPassThreshold`，改为写死 `PassThreshold = 80`（Scenario 原文就是"停手阈值为 80"）。
  - `专家难度的一层前瞻Tests.前瞻宽度进入记录`：钉的是记录格式。预设序列化时写死 `with { PassThreshold = LookaheadFixtures.PassThreshold }`（80），JSON 字面量一字不改。
  - `专家难度的一层前瞻Tests.旧记录按不前瞻读入`：旧记录字面量不改，比对对象改为 `Hard with { PassThreshold = 80 }`。
- 守门类（跟随新值，见 1.7）：`难度分级Tests.难度名称与次序`、`默认评价权重的校准Tests.默认停手阈值被改动`。
- 已经写死、不用改的：
  - `LookaheadFixtures.PassThreshold = 80` 与 `Weights`：`难度分级Tests.三档旧难度逐步不变`、专家相关测试、`终端专家对局Tests`、`专家前瞻的记录Tests`、`专家前瞻的确定性与耗时Tests` 都用它；只补了一句注释。
  - `SimFixtures.PinPreCalibration`（阈值 20、权重 Eye/Threat 0、V1、冒险 0）：`候选格上限Tests.V4GoldenTurnHash`、`Sample`、`RankedSample`，以及 `各入口按地图标识选图Tests`、`边疆图终端试玩脚本Tests`、`随机子流隔离Tests`、`高部署上限下的候选剪枝Tests` 用的 `PreCalibration*`。
  - 终端脚本：`终端的带入选择弃赛与结算显示Tests.ScriptPassThreshold = 80`。
  - 其余用法只断言"记录落成缺省值"，或者只做同阈值下的等价比较，跟随新值即可，不需要写死：
    - `停手阈值Tests:168/170/308/309`
    - `批量跑局Tests:48/57`
    - `默认评价权重的校准Tests.引用未校准维度产出的数据` / `未显式配置权重的跑局用默认权重表`
    - `校准后截断率达标`：它本来就应跟随缺省值。
- 写死之后的验证：阈值还是 80 时，把实现改为 81 跑默认套件，结果 **红 2**（`难度名称与次序`、`默认停手阈值被改动`），都是守门类，保真度类没有红。
  - 还原方式：二进制还原后逐字节比对一致，并用 `os.utime` 刷新了 mtime。

### 1.7 守门先红

- `默认停手阈值被改动` 改为：
  - 取值 20，四档共用；
  - 口径串必须包含 "v2-recalibration"、`siege-4p-base-v5`、"V2"、"种子 1–20"、"20 局"、"小样本"、"0 / 20 / 40 / 80"、"sim-out/v2-recalibration/pass-"；
  - 选定值必须在档位清单里，并出现"选定 20（"；
  - 每个档位都要匹配 `\d+ / \d+ / \d+\.\d+` 形式的数据；
  - 口径串不得含"种子 1–200"和 `sim-out/ai-eye-pass-`；
  - 源码必须含"内容集 V1"，不得含"= 8 ×"。
  - 旧的"ai-eye 段 D 校准 / 种子 1–200 / 200 局 / {值} / 160"几条断言已删除。
- `规则变更使校准失效`：
  - 改为断言 `PassThresholdCalibrationStatus` 不以 `ScoringExtendedStatus` 结尾，也不包含它；九维的 `EndsWith` 断言保留。
  - 三个扫过档的维度，证据里加上"内容集 V1"。
- `默认权重的校准依据随值一起更新`：`CalibrationStatus` 的期望字面量加上"（内容集 V1）"和"停手阈值另经 v2-recalibration……复核"一段。
- `难度分级Tests.难度名称与次序`：四档预设 `new AiSearchConfig(…, 80)` 改为 20，Expert 那条也一并改了。
- 在旧实现上跑这四个类：**红 4**（`难度名称与次序`、`默认停手阈值被改动`、`规则变更使校准失效`、`默认权重的校准依据随值一起更新`）。
- 兜底 grep（`PassThresholdCalibrationStatus|DefaultPassThreshold|, 80)`，范围 tests/）逐条归类：
  - 守门：`默认评价权重的校准Tests:61/62/65/72/78–80/114`、`难度分级Tests:141–143`（以及同一段的 Expert 行）；
  - 保真度（写死 80）：`停手阈值Tests:200`；
  - 跟随新值：
    - `停手阈值Tests:168/170/308/309`
    - `难度分级Tests:39/85`：断言预设等于缺省值、简单难度眼位贡献大于缺省阈值，20 下仍成立
    - `默认评价权重的校准Tests:152/167/169/187`
    - `批量跑局Tests:48/57`
    - `LookaheadFixtures:28`：只是注释
  - 无关：`弃赛结算Tests:40`，这里的 80 是势力值。

### 1.8 落地

- `AiSearchConfig.DefaultPassThreshold = 20`，XML 注释重写，包括：
  - V2 复核的条件、四档数据、选档四步与带宽数值；
  - 80 为上界、只在 v5 上复核；
  - 冒烟结果；
  - ai-eye（V1、200 局）的历史一段，标注"内容集 V1"；
  - 删掉"= 8 × PowerGain"比例依据。
- `PassThresholdCalibrationStatus` 改写为 V2 口径，不再带 `ScoringExtendedStatus`。
- `EvaluationWeights`：
  - `CalibrationStatus` 与它的注释补上"内容集 V1"和"停手阈值另经 v2-recalibration 复核"；
  - `ScoringExtendedStatus` 的注释去掉"PassThresholdCalibrationStatus 以它结尾"的说法；
  - `Default` 的 XML 注释标注内容集 V1，并说明下文的"停手阈值 80"是 V1 口径；
  - 200 局要求补上"或经负责人裁决的更小规模"；
  - `CalibrationOf` 中 Eye / Safety / Threat 三维补"（内容集 V1）"；
  - 九维取值不变。
- `Siege.Sim/Program.cs` 用法说明："（ai-eye 校准值）"改为"（v2-recalibration 在内容集 V2 上复核的校准值）"。
- `openspec/changes/v2-recalibration/specs/ai-decision/spec.md`：
  - 「默认评价权重的校准」补上复核结果表、选档过程和冒烟结果；
  - 「停手阈值」写明缺省值为 20。
- 变异：还原全部按二进制做，逐字节比对一致并用 `os.utime` 刷新 mtime；除标注处外只跑四个相关测试类。

| 编号 | 改动 | 结果 |
|---|---|---|
| M-A1-81 | 1.6 审计：实现 80 → 81，跑全量套件 | 红 2：难度名称与次序、默认停手阈值被改动 |
| M-A2-21 | 实现 20 → 21 | 红 2：难度名称与次序、默认停手阈值被改动 |
| M-A3 | 口径串漏写"20 局"（"每档 20 局" → "每档局"） | 红 1：默认停手阈值被改动 |
| M-A4 | 口径串末尾补回"；more-pieces-relics 扩展计分后未重扫" | 红 1：规则变更使校准失效 |
| M-A5 | `CalibrationOf(Eye)` 去掉"（内容集 V1）" | 红 1：规则变更使校准失效 |
| M-A6t | 只改测试不改实现：期望 20 → 80 | 红 1：默认停手阈值被改动 |

### 1.9 200 局慢测试只改说明

- `校准后截断率达标_种子1至200` 上方加了注释："停手阈值经 V2 20 局复核变更后（80 → 20），本慢测试的结论未经复核；v2-recalibration 未运行"，上限 10 局不改。
- `AssertTruncation` 的 XML 说明补了同样的内容，另加 V2 对照 `sim-out/v2-recalibration/pass-20`。
- 缩小版的注释补了新口径。
- `git diff` 确认这一处只改了注释，慢测试没有运行。

### 1.10 段 A 回归

- `dotnet build siege.sln`：0 警告。`dotnet build src/godot/Siege.Godot.csproj`：0 警告。
- `dotnet test -c Release`：通过 1754、跳过 6、失败 0，退出码 0。
  - 开工前基线同样是通过 1754、跳过 6。
  - 默认套件里的 `校准后截断率达标` 在新缺省阈值下为绿。
- `git diff tests/` 中的非注释改动只有三类：1.6 的写死阈值、1.7 的守门期望、四档预设的 80 → 20。黄金哈希、JSON 字面量、种子都没有改。
- `openspec validate v2-recalibration --strict`：通过。

### 跑局总账（段 A）

| 目录 | 配置 | 局数 |
|---|---|---|
| sim-out/v2-recalibration/pass-0 | research/configs/pass-0.json | 20 |
| sim-out/v2-recalibration/pass-20 | research/configs/pass-20.json | 20 |
| sim-out/v2-recalibration/pass-40 | research/configs/pass-40.json | 20 |
| sim-out/v2-recalibration/pass-80 | research/configs/pass-80.json | 20 |
| sim-out/v2-recalibration/easy20-pass20 | research/configs/easy20-pass20.json | 20 |
| sim-out/v2-recalibration/expert20-pass20 | research/configs/expert20-pass20.json | 20 |
| 合计 | | 120 |

### 待决 / 交后续段

- 段 B 的 2 人图扫档要在阈值 20 下进行（D9）。诊断 b01–b03 是在阈值 80 / 0 / 40 下跑的，不是阈值 20 的数据。
- 设计文档 §15.2 / §3.3 / §16 的同步属于段 C（3.1 要把 80 改为 20，并删掉"= 8 × 即时势力增量权重 10"）。

## 段 B：2 人图地图专属权重覆盖（tasks.md 第 2 组，2026-09-28）

结论：选定 **Eye 50、EnemyLoss 8**，其余七维等于 `Default`。登记为 `siege-2p-base-v1 → Default with { Eye = 50 }`。

- 扫档共 5 批 × 20 = **100 局**，第 6–8 批未触发。
- 另跑了若干单局对照，不计入批次：机制前后各 5 局（2.3）、种子 1 一局加回放（2.10）、Godot 无头演示 4 次（2.4，每次 2 个大回合）。
- 没有超过 20 局的批次，也没有跑 200 局慢测试。

### 2.1 先写测试（先红）

新增 21 个用例，Scenario 名即方法名：

- `AiDecision/地图专属评价权重覆盖Tests`，共 8 条：
  - 两人图未显式配置权重时取覆盖
  - 未登记的地图取默认表（覆盖 v5、3 人图、`gen:12345` 与边疆图）
  - 显式权重整表优先
  - 覆盖作用于简单难度
  - 覆盖只作用于登记的地图
  - 覆盖表被改动
  - 覆盖不改人类玩家的合法操作
  - 按地图取权重只有一处实现且只由三个入口调用：源码扫描，样本下界为 150 个文件，并配反面命中
- `SimulationHarness/各入口的地图专属AI权重Tests`，共 11 条：
  - 未登记的地图首部逐字节不变：Theory × 4，覆盖 v5、3 人图、边疆图与 `gen:12345`
  - 批量跑局在两人图上落成覆盖
  - 显式权重不被覆盖替换
  - 新日志按首部回放
  - 旧日志不套覆盖：夹具现场生成，走 recorded 路径，阈值 80、V2、首部权重为空
  - 终端入口取覆盖
  - 图形版入口取覆盖（源码扫描）
  - 重建玩家列表后按覆盖取值（CLI 加 `--difficulty`）
- `停手阈值Tests.地图覆盖不改阈值`，1 条：Scenario 属于 MODIFIED「停手阈值」。
- `默认评价权重的校准Tests.两人图覆盖表的校准记录随值一起更新`，1 条：2.9 的守门。

要点：

- 需要"覆盖不同于默认表"才能分辨的断言，经纯函数重载注入登记表 `Probe`（Eye 7 / EnemyLoss 3）。重载有两个：`EvaluationWeights.ForMapId(id, explicit, table)` 与 `RunConfig.ResolvedFor(map, table)`，另有 `PlayCommand.Run(mapOverrides:)` 接缝。没有引入可变静态。
- 「未登记的地图首部逐字节不变」的 8 个 SHA-256（首部 + `config.json`，四张图各两个）是在改动 `src/` 之前、用同一份测试配置实跑后钉下的。
- 红的证据分两步：
  1. 编译失败，按错误码计：`ForMapId` 24 处、`MapOverrides` 26 处、`MapOverrideCalibrationOf` 6 处、`ResolvedFor` 双参 8 处、`mapOverrides` 参数 2 处。
  2. 补上只有签名、没有行为的骨架后，断言层红 12 条。另外 8 条在骨架下就是绿的：4 条首部黄金、未登记取默认表、显式不被替换、旧日志不套覆盖、合法操作不变。它们都是"不改变"类 Scenario，符合预期。
- 既有用例未改动。唯一例外是在 `停手阈值Tests` 与 `默认评价权重的校准Tests` 两个类的末尾各追加了一个方法。

### 2.2 机制实现

- `Siege.Core/Ai/EvaluationWeights.cs`：
  - `MapOverrides`：只读 `ImmutableSortedDictionary`，登记项为独立实例；
  - `ForMapId(mapId, explicit = null)`：按"显式 > 覆盖 > 缺省"整表取值的唯一实现；
  - `internal ForMapId(mapId, explicit, table)`：接受登记表参数的纯函数重载；
  - `MapOverrideCalibrationOf(mapId)`：返回登记项的校准口径；
  - `TwoPlayerOverrideCalibrationStatus`：口径为单个字面量。
- `Siege.Sim/Config/RunConfig.cs`：
  - `ResolvedFor(map)` 委托给 `ResolvedFor(map, table)`；
  - 只在地图登记了覆盖、且有未显式配置权重的玩家时，落成 `ForMapId(map.Id, p.Weights, table)`；
  - 这一步放在前瞻宽度那段 `return` 之前，保持幂等；
  - XML 说明已同步。
- `Siege.Sim/Play/PlayCommand.cs`：建 AI 前取 `aiWeights = ForMapId(map.Id, weights, mapOverrides ?? MapOverrides)`。建 AI 仍写在一行里，因为既有守门 `候选格上限Tests.三个入口共用同一处阈值逻辑` 按行扫 `search)`，第一次拆成多行时它红了，已改回。
- `src/godot/scripts/MatchSession.cs`：新增 `Weights = EvaluationWeights.ForMapId(match.Map.Id)`，`ChooseZone` 建 AI 时传 `Weights`，签名不变。`GameRoot.DifficultyText` 的启动日志加打 AI 评价权重。
- `HeuristicTurnController` 的 `weights ?? Default` 回落未改。
- `Program.cs` 用法串补上优先级（配置文件权重 > 地图专属覆盖 > 缺省表，整表生效），并写明 `--difficulty` / `--players` 会丢弃配置文件权重，之后改按覆盖、再按缺省表取值。
- 变异：跑全量套件，占位值 = Default。脚本二进制读写，锚点命中恰为 1，`finally` 中还原，逐字节校验后用 `os.utime` 刷新。

| 编号 | 改动 | 结果 |
|---|---|---|
| M-B2-1 | `HeuristicTurnController` 回落改为 `ForMapId(observe().MapId)` | 红 1：按地图取权重只有一处实现…（占位期行为不可分辨，由源码扫描兜住） |
| M-B2-2 | `ResolvedFor` 去掉"地图有登记"条件 | 红 5：未登记的地图首部逐字节不变 × 4、批量跑局Tests.批量执行并汇总 |
| M-B2-3 | 显式与覆盖逐维合并（Eye / EnemyLoss 等于缺省时取覆盖） | 红 1：显式权重整表优先 |
| M-B2-4 | 终端入口 `aiWeights = weights` | 红 1：终端入口取覆盖 |
| M-B2-5 | 登记表加 `siege-3p-base-v1` | 红 4：未登记的地图取默认表、覆盖只作用于登记的地图、覆盖表被改动、未登记的地图首部逐字节不变（3p） |

### 2.3 机制段回归（占位值 = Default）

- 两处 `dotnet build`：0 警告。`dotnet test -c Release`：通过 1774、跳过 6、失败 0，退出码 0（段 A 末为 1754 + 新增 20）。
- 同一命令 `run --map <图> --seed 1 --count 1` 分别在改动前（`sim-out/v2-recalibration/mech-pre/`）和改动后（`mech-post/`）各跑一次，逐字节比对：
  - v5 / 3 人图 / 边疆图（`--turn-limit 40`）/ `gen:12345`（`--turn-limit 40`）：首部与 `config.json` 逐字节相同；其余各行去掉 `ElapsedMs` / `MajorRoundMs` / `TotalMs` 三个耗时字段后全部一致（174 / 119 / 189 / 178 行）。
  - 2 人图：`config.json` 逐字节相同（`Effective()` 原本就填默认表）；首部只多出 `Players[].Weights`，去掉后相同；其余 70 行去掉耗时后一致，决策序列逐步相同。
- `git diff tests/` 没有删除行，既有黄金值一字未改。

### 2.4 图形版人工核对

- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告。
- 无头自检 `--headless --path src/godot --quit-after 3000 -- --auto-demo --rounds=2 --map=<图>`，两张图退出码都是 0，启动日志 `[siege] 地图 …` 一行中：
  - 落地后：`siege-2p-base-v1` 为 `AI 评价权重 EvaluationWeights { … Eye = 50, Threat = 25 }`，等于登记表；`siege-4p-base-v5` 为 `Eye = 200`，等于默认表。
  - 占位期先跑过一次，两张图都是 `Eye = 200`，只能证明接线，不能证明机制。

### 2.5 扫档

- 配置由 `research/mkcfg2p.py` 生成，输出 `research/configs/2p-eye{200,100,50}-el8.json` 与 `2p-eye200-el{16,24}.json`：
  - `siege-2p-base-v1`，2 × Standard，`PassThreshold` 20，其余字段同段 A；
  - 九维逐玩家写死，只改 Eye / EnemyLoss。
- `git diff --no-index` 核对：每份与基线只差 Eye 或 EnemyLoss 的两行；基线与诊断 `b01-2p-std.json` 只差 `PassThreshold` 一行（80 → 20）。
- 命令：`Siege.Sim.exe run --config <cfg> --out sim-out/v2-recalibration/2p-eye<E>-el<L>`（Release），没有传 `--difficulty` / `--players`。五批串行，退出码都是 0，`FailedFiles` 为空，每批 20 局，各约 5 秒。
- `research/sweep2p.py` 逐批核对 `config.json` 的地图、内容集、阈值 20、带入、截断、种子、局数、人数、难度，每名玩家的九维权重（按目录名的期望值）以及 `Search` 为空。结果 **CONFIG ALL OK**。
- 另用诊断 `summarize.py` 为每批写了 `engagement-summary.json`，`table.py` 的输出与下表一致。注意诊断脚本的 R3 列按并列局计入分母的旧口径，五批依次为 13/20、9/20、13/20、13/20、13/20，只作参照。

### 2.6 条件批次：未触发

- e* 为 Eye 50：只有它在 {100, 50} 的无提子带内（13 < 17）。l* 为 EnemyLoss 16：16 / 24 都是 19/20，在第 5 条并列打破中 16 离基线更近。
- 触发线按基线比例计算：p_b = 18/20 = 0.90，SE = √(0.9 × 0.1 / 20) = 0.0671，两档都须 ≤ 0.833，即 ≤ 16 局。e* 为 13/20，达线；l* 为 19/20，高于基线，未达线。
- 只有眼位一维有效，不做组合。批次数 5，未超过 8。

### 2.7 选档（D9）

统计口径（`research/sweep2p.py`）：

- 截断、整局无提子、已终局局平均结束大回合、总提子、Pass 率与诊断 `summarize.py` 相同。
- 第 3 大回合领先者按 D9 口径：取第 3 大回合最后一个快照里总势力的唯一最高者，并列局与截断局都不计入分母。
- 并列局的剔除路径确认走到过：在段 A 的 v5 数据上各批剔除 1–3 局；在本段五批中各剔除 1–2 局。

| (Eye, EnemyLoss) | 截断 | 整局无提子 | 已终局局平均结束 R（范围） | R3 领先者胜（D9） | 总提子 | Pass 率（全程 / R4+） |
|---|---|---|---|---|---|---|
| (200, 8) 基线 | 0/20 | 18/20 | 8.30（3–11） | 11/18（剔除并列 2） | 2 | 17.6% / 26.8% |
| (100, 8) | 0/20 | 17/20 | 7.90（3–10） | 7/18（剔除并列 2） | 3 | 15.4% / 23.7% |
| (50, 8) | 0/20 | 13/20 | 8.35（3–10） | 11/18（剔除并列 2） | 9 | 14.0% / 21.4% |
| (200, 16) | 0/20 | 19/20 | 8.50（3–11） | 12/19（剔除并列 1） | 3 | 17.8% / 26.7% |
| (200, 24) | 0/20 | 19/20 | 8.50（3–11） | 12/19（剔除并列 1） | 3 | 17.2% / 25.7% |

五步：

1. **截断**：五档都是 0/20，全部入选。
2. **无提子带**：p_min = 13/20 = 0.65（Eye 50），SE = √(0.65 × 0.35 / 20) = 0.1067，进带条件为比例 ≤ 0.7567，即 ≤ 15 局。带内只有 (50, 8)。
3. **领先者全胜否决**：(50, 8) 为 11/18，不是 20/20，不剔除。诊断 b09 在阈值 80 下 Eye 50 为 20/20，阈值 20 下没有复现。
4. **结束大回合**：8.35 在 [7, 10] 内，距离为 0。
5. **并列打破**：带内只剩一档，没有用到。

- 选中 (50, 8)，不是基线，所以不走 Open Question 2 的"删除登记"分支。
- 总提子、Pass 率只作记录，不参与选档（裁决 ⑤）。

### 2.8 审计：依赖 2 人图走法的既有测试

- 逐个审查以 2 人图跑 AI 的既有用例：
  - `各入口按地图标识选图Tests.两人图可选…` 与 `参赛人数缺省取地图人数上限…`：4 个小回合截断，只断言人数与地图标识；终端 2 人图用 Easy，输入 `q` 即退出。
  - `两人基准地图Tests`、`出生区信物权重Tests`、`选图视图模型Tests`：不跑 AI。
- 另一类是比对 `Effective().ToJson()` 与落盘配置的测试：`批量跑局Tests:48`、`默认评价权重的校准Tests:183/185` 都跑在 v5 上，不受影响。
- 结论：没有需要写死权重的保真度测试，也没有需要让期望侧经过 `ResolvedFor` 的配置记录测试。
- 实测变异：
  - M-B8-1，在占位期把覆盖表的 Eye 改为 1，跑全量：**红 1**，只有 `覆盖表被改动`（守门）。
  - M-B9-1，落地后把 Eye 从 50 改为 51：同样只红守门。
  - 可见所有保真度测试都不读登记表的具体取值。
- 本段新增的管道用例里，依赖走法的只有两处样本下界（"至少一个落子小回合""≥ 8 个小回合"）。它们在 Eye 200 和 Eye 50 下都成立。

### 2.9 守门先红后落地

- 新增 `默认评价权重的校准Tests.两人图覆盖表的校准记录随值一起更新`，钉住以下内容：
  - 键集合恰为 `[siege-2p-base-v1]`；
  - 逐维只有 Eye = 50，其余八维等于 `Default`；
  - 口径包含 "v2-recalibration" / 地图 / "2 名标准难度" / "内容集 V2" / "种子 1–20" / "20 局" / "小样本" / `停手阈值 {DefaultPassThreshold}` / "sim-out/v2-recalibration/2p-eye"；
  - 五个档位各匹配一组 `→ 截断 / 无提子 / 平均结束 / R3 胜/分母`；
  - 包含 `选定 ({Eye}, {EnemyLoss})`，且这个档位在档位清单里；
  - 口径与源码都不含"占位"；
  - 口径是源码中的单个字面量；
  - 源码包含 `Default with { Eye = 50 }`。
- 先红：在占位实现上跑，第一条断言就失败（Expected 50 / Actual 200）。
- 落地：
  - 登记值改为 `Default with { Eye = 50 }`；
  - `TwoPlayerOverrideCalibrationStatus` 换成正式记录，写明条件、五档数据、选定值、"Eye 50 为档位下界"与数据目录；
  - XML 注释写明选档五步、2 × 2 组合未触发的理由、b09 未复现、只在标准难度上扫档；
  - `EvaluationWeights.cs` 中已不含"占位"；
  - 本 change 的 `specs/ai-decision/spec.md`「地图专属评价权重覆盖」补上结果表与选档过程。
- 守门转绿。落地后 `地图专属评价权重覆盖Tests.覆盖表被改动` 中"取值 = Default ⇔ 含占位"的两边同为假，仍然为绿。
- 变异（全量套件）：

| 编号 | 改动 | 结果 |
|---|---|---|
| M-B9-1 | 覆盖表 Eye 50 → 51 | 红 1：两人图覆盖表的校准记录随值一起更新 |
| M-B9-2 | 口径删掉"数据目录 sim-out/v2-recalibration/2p-eye<E>-el<L>" | 红 1：同上 |
| M-B9-3t | 只改测试：期望的差异维由 Eye 换成 EnemyLoss | 红 1：同上（测试不是照抄实现） |

### 2.10 段 B 回归

- `dotnet build siege.sln`：0 警告。`dotnet build src/godot/Siege.Godot.csproj`：0 警告。
- `dotnet test -c Release`：通过 1775、跳过 6、失败 0，退出码 0。1775 = 1754 + 新增 21，没有运行 Slow / Perf。
- `git diff tests/` 没有删除行，既有黄金哈希与期望一字未改。
- 在 `siege-2p-base-v1` 种子 1 上跑一局（`sim-out/v2-recalibration/2p-final-seed1/`，17 个小回合，AllPassed）：首部与 `config.json` 中两名玩家的 Eye 都是 50、EnemyLoss 都是 8，阈值 20。`replay --file` 结果为"回放一致：81 行逐字节相同"。
- `openspec validate v2-recalibration --strict`：通过。

### 跑局总账（段 B）

| 目录 | 配置 | 局数 |
|---|---|---|
| sim-out/v2-recalibration/2p-eye200-el8 | research/configs/2p-eye200-el8.json | 20 |
| sim-out/v2-recalibration/2p-eye100-el8 | research/configs/2p-eye100-el8.json | 20 |
| sim-out/v2-recalibration/2p-eye50-el8 | research/configs/2p-eye50-el8.json | 20 |
| sim-out/v2-recalibration/2p-eye200-el16 | research/configs/2p-eye200-el16.json | 20 |
| sim-out/v2-recalibration/2p-eye200-el24 | research/configs/2p-eye200-el24.json | 20 |
| 批次合计 | | 100（段 A + B = 220，未超过 280） |
| 单局对照（非批次） | mech-pre / mech-post 各 5 局、2p-final-seed1 1 局 + 回放、Godot 无头演示 4 次 | — |

### 待决 / 交后续段

1. **D9 口径与段 A 表格不一致**：按 D9 口径复算段 A 四批的 R3 领先者胜，结果为 0 → 14/19、20 → 11/19、40 → 13/19、80 → 11/18，每批都有并列局被剔除。段 A 的 implement 记录与 spec.md 表格用的是"并列计入分母"的旧口径，分别写作 15/20、12/20、14/20、13/20。段 A 已提交，这里没有回改；这一项不参与阈值选档。是否在段 C 重标口径，交主会话决定。
2. **"20/20 全胜否决"按字面实现**：D9 分母剔除并列局后，本段五批的分母都是 18 或 19，字面意义上的 20/20 已不可能出现。脚本按字面实现，本次没有任何一档在分母内全胜，不影响结果。若负责人的本意是"分母内 100%"，需要改判口径。
3. **覆盖对其他难度同样生效**：覆盖作用于 2 人图上的全部难度，但只在标准难度上扫过档（Open Question 1 按缺省执行，没有补批）。段 C 需要在 §15.2 中注明。
4. **Eye 50 是档位下界**：50 以下没有数据，已写入口径。

## 段 C：设计文档 v1.16 → v1.17 与全量回归（tasks.md 第 3 组，2026-09-28）

结论：设计文档同步到 v1.17，规格与 design / proposal 的领先者口径对齐。本段**没有跑局**，也没有改 `src/` 与 `tests/`。

### 改动清单

- `2026-09-10-siege-core-gameplay-design-v1.md`：
  - 首部 v1.16 → v1.17，"最近一次为 v2-recalibration"。
  - §3.3 2 / 3 人图冒烟：引言标"内容集 V1"（早于 more-pieces-relics，首部无内容集字段，阈值 80）；其后一段标 V1，"AI 权重只在 v5 上校准"改为当时口径，"2 人图几乎不交战……留给后续小图校准"改为诊断归因加地图专属权重（Eye 50，V2 下无提子 18/20 → 13/20）。3 人图仍未单独校准。
  - §15.2「停手阈值」：80 → **20**，删去"= 8 × 即时势力增量权重 10"；补"v2-recalibration 在内容集 V2 上以每档 20 局复核（ai-eye 的 80 为 V1 历史口径）"，以及"不随地图变化，地图覆盖不改阈值"。
  - §15.2「难度分级」下一段："即时收益越不过 80"改为"越不过当时的 80（ai-eye，内容集 V1）"。
  - §15.2「校准口径」：
    - ai-eye 段标"内容集 V1"；
    - "领先者胜率只记录不否决"限定为缺省表的选档；
    - 定值后注明现行阈值为 20；
    - more-pieces-relics 补注之后加"停手阈值此后经 v2-recalibration 复核，不再带补注"。
  - 新增「V2 上的停手阈值复核」一段：条件、四档表（R3 用 D9 口径）、选档四步与带宽、冒烟两批、目录、"小样本 / 80 为上界 / 只在 v5 上复核"、200 局要求放宽与慢测试未运行。
  - 新增「地图专属评价权重」一段：见 3.3 核对。
  - 已知问题：
    - 标题改为"不修，记录在案；第 1–5 条源自 ai-eye，第 6 条源自 engagement-diagnosis"；
    - 第 1 条整条重写；
    - 第 2 / 3 / 4 条的 ai-eye 数据标 V1，补 V2 读数或"阈值 20 下未复核"；
    - 第 5 条末尾指向第 1 条；
    - 新增第 6 条：落后者。
  - §16：
    - "本版规则的 20 局冒烟 53.7%"改为"restore-go-core-rules 规则下的 20 局冒烟（内容集 V1）"；
    - "权重口径"一条中 ai-eye 标 V1，并指向 V2 复核；两张实测表注明为 V1，不与 V2 数据比较；
    - 「当前实测」「life-shape 200 局基线」括注"内容集 V1"。标题未改名，tasks / proposal 仍按原名引用。
  - 变更记录追加 v1.17 一行。
- `openspec/changes/v2-recalibration/specs/ai-decision/spec.md`：
  - 段 A 结果表 R3 列按 D9 口径重标：表头改为"唯一领先者；并列局与截断局不计入分母"，值为 14/19、11/19、13/19、11/18。
  - 覆盖选档第 3 条"MUST NOT 为 20 / 20"改为"MUST NOT 在分母内全胜（胜数等于分母）"，并写明分母口径。
  - 段 B 结果表表头补"与截断局"。
  - Scenario「选档否决领先者全胜」改用分母口径（18 / 18 对 14 / 18）。Requirement 标题一个未改。
- `openspec/changes/v2-recalibration/design.md` D9 第 3 条、`proposal.md` 选档一句：各加括注"段 B 后裁决：指分母内全胜"。
- `research/sweep2p.py`：否决条件从字面上的 `== 20` 改为 `r3[1] > 0 and r3[0] == r3[1]`，日志文字同步。用现有 5 个目录重跑（纯统计，非跑局）：CONFIG ALL OK，否决剔除为空，仍选 (50, 8)，第 5 条未用到。
- 段 A 的 1.4 表下加了一行"段 C 注"，说明旧口径与重标值；正文未改。
- 段 A 的 D9 口径复算：`sweep2p.stats` 作用于 `pass-{0,20,40,80}`，得到 14/19（并列 1）、11/19（并列 1）、13/19（并列 1）、11/18（并列 2），截断局都是 0。与段 B 待决 1 的数值一致。

### 逐项人工核对

- **3.1**：
  - 文中缺省值 20 = `AiSearchConfig.DefaultPassThreshold = 20`；
  - 全文 grep "= 8 ×" 为 0 条；
  - "各档难度共用"与代码中四档预设取 `DefaultPassThreshold` 一致；
  - "不随地图变化"与规格 MODIFIED「停手阈值」末段一致。
- **3.2**：文档 V2 复核段与 `PassThresholdCalibrationStatus`、`DefaultPassThreshold` 的 XML 注释、规格「默认评价权重的校准」逐项比对，下列各项一致：
  - 条件：地图 v5、4 人标准、V2、带入 0、截断 600、种子 1–20、每档 20 局、九维写死为当时缺省表。
  - 四档"截断 / 无提子 / 平均结束"：0 → 0/3/9.15，20 → 0/3/9.25，40 → 0/5/9.05，80 → 0/8/8.40。
  - 带宽 0.080、进带 ≤ 0.230，选定 20，80 为上界，非完整双向扫档，只在 v5 上复核。
  - 冒烟：简单截断 0 / 最长 24、专家截断 0。
  - 目录 `pass-*` / `easy20-pass20` / `expert20-pass20`。
  - "V1 下 80 → 160 无提子陡升"与代码注释一致：32.0% → 73.5%。
  - 代码常量不含 R3。文档与规格的 R3 都是 D9 口径，四个值相同。
- **3.3**：与规格 ADDED 两条 Requirement 逐项比对，全部覆盖：
  - `ai-decision`「地图专属评价权重覆盖」：
    - AI 配置、不进地图、不改合法操作 / 计分 / 终局；
    - 不改阈值与搜索参数；
    - 唯一实现、三入口共用；
    - 优先级 显式 > 覆盖 > 缺省，整表不合并；
    - 全部难度（简单三维、专家模拟对手沿用本人）；
    - 只登记 2 人图，3 人图 / v5 不登记，其他图取缺省；
    - 扫档条件与档位；
    - 五步选档，第 3 步为分母内全胜；
    - 五档表与规格、`TwoPlayerOverrideCalibrationStatus` 逐格相同：18/17/13/19/19，8.30/7.90/8.35/8.50/8.50，11/18、7/18、11/18、12/19、12/19，提子 2/3/9/3/3；
    - 2 × 2 未触发理由（0.90 − 0.067，≤ 16 局）；
    - Eye 50 为下界；只在标准难度扫档。
  - `simulation-harness`「各入口的地图专属 AI 权重」：
    - 批量落成进配置记录与首部；
    - 未登记地图逐字节不变；
    - 回放按首部；
    - 旧日志不套覆盖；
    - 终端 / 图形版取覆盖；
    - `--difficulty` / `--players` 丢权重。
  - 另有 design D4 的"按标识键、可用首部摘要核对"。
  - 2.7 没有走"不登记"分支，不需要改写为"无更优档"。
- **3.4**：与 `findings.md` Q3 表逐项比对，一致：
  - V2 0/20，最长 44（b08）；
  - V1 4/20，种子 1/3/10/12（b10 = easy20）；
  - 最后 60 小回合提子 98–100%、棋子数首末相同、落点集中 6–12 格；
  - 盘面键从不严格重复，同形禁则不命中；
  - 种子 18 尾部提子 77%，零事件 95% 上界约 16%；
  - ai-eye 36/200，V1，同形修正前。
  - "不改规则"对应裁决 4。
  - 另补阈值 20 下 easy20-pass20 的 0/20、最长 24，与段 A 1.5 一致。
- **3.5**：与 Q2 结论一致：
  - 可落空间随名次递减、禁入格是主因；
  - Pass 的直接原因是没有正收益点；
  - 阈值只对垫底者有明显影响；
  - 接受、不改（裁决 3）。
  - 数据口径写作 v5、V2、20 局（b06）。
- **3.6**：grep `smoke20|easy20|200 局|baseline200`，改后共 31 行，逐条判定：

| 行 | 内容 | 判定 |
|---|---|---|
| 160 | small-maps `2p-smoke20` / `3p-smoke20` | 已标 V1（153 引言与本行） |
| 738 | ai-eye 校准口径（每档 200 局、每局换图 200 局） | 已标 V1 |
| 749 | V2 复核段中的"不少于 200 局"放宽说明 | 不适用（规格要求陈述，非数据） |
| 773 | 已知问题 1：easy20、ai-eye 200 局 | 已标：V1 / V2 分列 |
| 774 | 已知问题 2：边疆图 81 局（ai-eye） | 已标 V1 |
| 777 | 已知问题 5："200 局复跑延后" | 不适用（进度陈述，非数据） |
| 793 | §16"至少 200 局……≤ 50%"目标 | 不适用（目标陈述） |
| 800 | 当前实测 restore-smoke20 | 已标 V1 |
| 812 | 已知偏离："artisan-terrain-edit 的 200 局确认跑" | 不适用（已声明口径失效、只在变更记录留档） |
| 814 | life-shape baseline200 | 已标 V1 |
| 821 | life-shape 表内"82 / 200 局" | 已标（随 814 表头） |
| 969–987 | 变更记录（dominance-victory … small-maps） | 不适用：历史条目按当时口径留档；均早于 more-pieces-relics，天然属 V1 |
| 988–990 | 变更记录 more-pieces-relics / carry-in-out / expert-lookahead 的 smoke20 | 不适用：已是 V2，且各自带"权重口径"标注 |
| 991 | 本 change 的变更记录 | 不适用（本次新增） |

  另外人工兜底，查了不在 grep 范围内的 V1 数据：§16 的"本版规则的 20 局冒烟 53.7%"（restore-smoke20）与"权重口径"一条中的 ai-eye，均已标 V1。
- **3.7**：变更记录与本 change 产物逐项比对，一致：
  - 阈值 20 与四档数据、冒烟；
  - 覆盖机制要点与 Eye 50 五档结论；
  - R3 D9 重标值；
  - 已知问题改写与第 6 条；
  - V1 标注的位置；
  - 规格口径放宽；
  - 11 个数据目录，220 局。
  - 首部版本 v1.17。
  - 残留兜底：grep `v1\.16|阈值 80|校准值 \*\*80|= 8 ×`，命中的都是历史或已标注处：153 V1 冒烟、692 / 728 / 885 expert-lookahead 的版本号、738 ai-eye 定值、749 b06/b07 对照、769 b09、773 V1 历史。
  - 小图校准兜底：grep `小图|2 人图|两人图|未在小图`（正文，不含变更记录），命中 147 / 157 / 160 / 754 / 756 / 759 / 775，都在 §3.3 或 §15.2 新段落里；§3.2、§16、§19 中没有把 2 人图校准当作待办的旧措辞。
- **代码 / 规格一致性检查**：`AiDifficulty.cs`、`EvaluationWeights.cs`、`Program.cs`、`RunConfig.cs`、`PlayCommand.cs`、Godot `MatchSession.cs`，以及 `.trellis/spec/core/*` 中关于阈值 / 覆盖的表述，与文档一致，无需修改。代码常量不含 R3 的旧口径值。

### 3.8 全量回归

- `dotnet build siege.sln`：0 警告、0 错误。
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告、0 错误。
- `dotnet test -c Release`：通过 1775、跳过 6、失败 0，与段 B 末相同。本段没有改 `src/` 与 `tests/`，也没有运行 Slow / Perf 或 200 局慢测试。
- `git diff tests/`：空，既有黄金值一字未改。
- `openspec validate v2-recalibration --strict`：通过（Change 'v2-recalibration' is valid）。

### 3.9 跑局总账（全 change）

| 段 | 目录（`sim-out/v2-recalibration/`） | 配置（`research/configs/`） | 局数 |
|---|---|---|---|
| A | pass-0 / pass-20 / pass-40 / pass-80 | pass-{0,20,40,80}.json | 4 × 20 |
| A | easy20-pass20 / expert20-pass20 | easy20-pass20.json / expert20-pass20.json | 2 × 20 |
| B | 2p-eye200-el8 / 2p-eye100-el8 / 2p-eye50-el8 / 2p-eye200-el16 / 2p-eye200-el24 | 2p-eye{200,100,50}-el8.json / 2p-eye200-el{16,24}.json | 5 × 20 |
| C | — | — | 0 |
| 合计 | 11 批 | | **220**（≤ 280），没有任何一批超过 20 局 |

- 非批次的单局对照（段 B）：mech-pre / mech-post 各 5 局、2p-final-seed1 一局加回放、Godot 无头演示 4 次。它们不属于校准批次，不计入上表。

### 待决

1. `openspec` 的归档（archive）与 git 提交留给主会话。
2. 覆盖只在标准难度上扫过档，2 人图的简单 / 高难 / 专家没有数据。这是 Open Question 1 按缺省执行的结果，文档已注明；是否补批由负责人决定。
3. V2 上九维权重的完整重扫、2 人图地图 v2（Q1-A）、边疆图复核，都仍是后续事项，文档已如实记录。
4. `src/Siege.Core/Ai/AiDifficulty.cs:41` 的 XML 注释写的是"三档难度的预设都取 DefaultPassThreshold（裁决 R1：三档共用）"，应为四档；同文件第 75 行已是"四档共用"。这是 expert-lookahead 遗留的问题，不是本 change 引入的。本段不改 `src/`，留给主会话或归档时顺手修正。
