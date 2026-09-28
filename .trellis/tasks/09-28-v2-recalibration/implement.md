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
