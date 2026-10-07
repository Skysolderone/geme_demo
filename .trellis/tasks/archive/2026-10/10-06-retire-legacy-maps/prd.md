# 10-06-retire-legacy-maps（母任务 10-06-board-terrain 的子任务 ③）

> 规格权威：`openspec/changes/archive/2026-10-07-retire-legacy-maps/`（proposal / design / specs / tasks）。负责人裁决全文见母任务 `prd.md`。调研：`research/legacy-removal.md`（总览与 Q1–Q12）、`product-code.md`、`tests-and-golden.md`、`saves-and-docs.md`。

删除旧地图（v5 / 2p / 3p / 边疆 / `gen:` / `maps/*.json`）；测试全部迁到棋盘图或合成盘面；黄金值重定且两台机器通用；旧标识明确报错；校验器收口；文档与规格同步。

负责人 2026-10-06 裁决：不可达地形规则本 change 保留、另开 change 定；工坊信物不动、记为已知问题；先修跨机器红测（段 0）；一个 change 内分段，全部做完再看。Q3–Q12 按调研建议（见 design「已知歧义」）。

验收：`openspec/changes/archive/2026-10-07-retire-legacy-maps/tasks.md` 各条的验证项。段 0 之后判据为"全量全绿"。

约束：
- 工作树 `.claude/worktrees/board-terrain`、分支 `feat/board-terrain`，基于 main `7be3f3b`；不碰主工作树、不切分支。
- 确定性（`determinism.md`）；规格档差异只在 `MapValidator.Rules` 声明表；hud 系源码守门继续生效。
- 改写测试不得放宽断言意图；改写 / 新增的断言配变异；黄金值重定逐条记录。
- 三张内置棋盘图的导出摘要必须保持不变（`MapData` / `MapFile` 不动）。
- 同一时间只跑一个 dotnet / Godot；跑局每个配置不超过 20 局。
- 本机构建 Godot 工程要带 `-p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config`；Godot 在 `~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot`。

分段：0 = tasks 第 0 组；A = 第 1 组；B = 第 2 组；C = 第 3 组；D = 第 4 组；E = 第 5 组（主会话）。

## 实现记录

### 段 0（2026-10-06，跨机器红测）

- 11 条红测根因查实：6 条是 `RunConfig.EffectiveParallelism`（实际 CPU 核数）进了被哈希的日志首部（把 `"EffectiveParallelism":28` 插回本机文本后哈希与原黄金值逐一相等）；5 条是缩进 JSON 与 `AppendLine` 按 `Environment.NewLine` 换行（本机 LF 换成 CRLF 后哈希与黄金值相等）。
- 修法：`EffectiveParallelism` 加 `[JsonIgnore]`，同名 JSON 字段改由 `RecordedParallelism` 写出（显式并行度写请求值、为 0 时不写、旧日志原样保留、读配置时丢弃）；测试侧 `SimFixtures.Sha256Lf` 哈希前统一 LF；`.gitattributes` 钉 fixture 行尾。黄金值重定 10 个（行尾 6、核数 4），新旧值记在测试注释。
- 新增守门 `被哈希文本不含机器相关值Tests`（4 条，变异 5 条全红）；`testing.md` 判据改为"全量全绿"并记录根因。
- 全量：缺省、`DOTNET_PROCESSOR_COUNT=8 / 28`（实现方）与 `=3`（主会话）均 2359 通过 / 0 失败 / 9 跳过。Windows 上需复跑一次确认（未验证）。

### 段 A1（2026-10-06，跑局 / 夹具 / 黄金值迁到棋盘图）

- `SimFixtures.Config` → `siege-4p-board-v1`（`Sample` 四局并行，结果不变）；`LookaheadFixtures.BoardConfig`（截断 24）；约 15 处黄金值重定、若干测试换种子 / 截断，新旧值与原因写在测试注释。检查方逐文件核对：反面对照都在、下界随截断同步、无"等于 → 至少"；种子 31 → 11 等理由用独立探针证实。
- 耗时：实现方测 1.82×；检查方同机背靠背 Debug 三次 1.96 / 2.08 / 1.99（最慢：两层加分 68 s、不消费新随机 58 s、缓存开关 50 s）。临界，A2 处理。
- 钉回 v5 的 7 处待处理（预演次数代理、专家前瞻耗时计时、校准截断率、原型插旗顺排、旧日志区数回填、候选格上限两处小图段）。
- 发现产品口径陷阱：显式给 `Search` 的专家 `CandidateCellLimit` 缺省 0（不限制），产品里 `run --config` 显式配置在 465 格图上会全盘枚举；测试夹具 `WithMapCellLimit` 把它补成 24，掩盖了这条路径（规格本身如此，非 bug）。主会话裁决见 A2。

### 段 A2（2026-10-06 → 10-07，规则测试 / 相机 / 终端 / 信物迁移与候选格上限产品修正）

- **产品（主会话裁决）**：`AiSearchConfig.CandidateCellLimit` 改可空——未写 = 按开局地图缺省（>150 格 24，否则 0），显式 0 = 不限制；显式 Search 未写时优先取跑局级 `--cell-limit`；新日志首部一律写落成值，K 出现之前的老日志重建按 0。删测试夹具 `WithMapCellLimit` / `LegacyV5Config`。检查方补"局中架桥不让上限跳档""两层扫描同样按地图缺省"两条测试。
- **7 处钉 v5**：删预演次数比 ≤4、专家耗时比（棋盘图实测 6.16×）、校准截断率三类断言 → 记入 AI 校准待办；删"批量侧顺排"（棋盘图上不可达，段 C 定代码去留）；旧日志区数回填读入库夹具 `legacy-v5-zonecount-seed1.jsonl.gz`；小图候选格段用合成 9×9。
- **夹具**：信物 `RelicBalanceFixtures`（12×8 自定）；相机 `CameraFixtures.Small` 11×11 自定；`Large` 经检查方发现仍是边疆 v2 平台表的平移版 → 段 B 换成自定布局；`盘面层视图模型带地形` 原照抄 v3 地形坐标，已换。
- **黄金值**：`内容集v1保持旧表` 在合成图重钉并加 v1≠v2 反面对照；G1、一层记录改 2 人图重钉；`PassSample`（阈值写死极大）检查方补与活记录逐项比对。
- **实现方漏报、检查方补列的删除**：v5 决策基线行与 `decisions-siege-4p-base-v5-seed1.txt`；`地图子命令Tests` 桥 / 林地 / 两种栅栏图例 4 条与边疆距离、第 15 行断言（前者在棋盘图上无对应，文本图图例这几种写法失去覆盖）；`选区不扰动其他随机` 的 v4 信物分布黄金值（改后的比较在构造上恒成立、不守门，注释已写明，信物生成由 `地图种子不扰动对局随机` 守）；`对局配置公开完整地图标识` 的边疆选区与信物摘要黄金值（改为测试内独立复算）。
- 全量 2355 通过 / 0 失败 / 8 跳过；两处构建 0 警告。耗时同机 ABBA 2.11×（超 2 倍）：最慢含 gen / 边疆专属测试约 143 s，段 B 删除后复测。

### 段 B（2026-10-07，删除旧地图）

- 迁出 `MapGenerationException`（独立文件）与 `FriendlySeed`（→ `BoardMapId`）；删四张旧图、边疆生成器与 8 个 Layout 分部、`FrontierSurfaces`、`MapGenParameters` / `GeneratedMapId`、`MapSymmetry`、`MapRandom` 边疆随机源（16 个 .cs 共 3263 行），`maps/` 7 个 json 与嵌入资源。地形规则代码一行未动（检查方逐目录核对）。
- `MapCatalog`：`RetiredIds` / `IsRetired`（含裸 `gen` 与 `gen:` 前缀）/ `RetiredMapException`（派生 `FileNotFoundException`，各入口现有 catch 直接接住）；删 `maps/<标识>.json` 隐式回落。`run` / `play` / `replay` / 图形版报"已删除"并列现有地图，退出码 1；`analyze` 照常分析旧日志。每局换图改 `board:`（种子逐局 +1）；覆盖表清空、机制保留；校准口径文字加"已删除、棋盘图上未校准"补注。
- 测试：整删 13 个只测旧图 / `gen:` 的文件，部分删除若干；新增 `已删除地图明确报错Tests`；相机 `Large` 换 23×34 五平台自定布局；检查方补"棋盘生成尝试耗尽报错""终端棋盘生成图提示"两条丢失覆盖。用例 2368 → 2181（2179 通过 / 8 跳过）。
- 全量全绿（缺省与 28 核）；两处构建 0 警告；Godot 缺省 `--auto-demo` / `--pick-check` / 选图自检通过，`--map=siege-4p-base-v5`、`gen:1` 报错退出。耗时同机 ABBA 2.09–2.27×（最慢集中在段 A 迁图后的专家前瞻类与 ConsoleRedirect 串行集合），待负责人定。
- 留给段 C：`FrontierFixtures`（41 处，含"显式路径可加载"样本）、`PocketBase`、标准档合成图随档位删除换底图；批量顺排代码；`map` 子命令"权威文件"导出；`MapFile.cs:36`、`MatchSession.cs:817`、`MapData.cs:24/47` 等过时注释。产品无读档入口，存档"已删除"报错只在 Core API 层。

### 段 C（2026-10-07，校验器收口）

- 删 `MapProfile.Frontier`、声明表 Standard / Frontier 两行与只服务旧档的规则（距离失衡拒绝、三项可达性、`ColumnRange`、`BOARDS_NOT_ALLOWED`、容差、必死口袋、出生区新地表禁令、`BIRTH_ZONE_COUNT_MISMATCH`）；逐条论证在棋盘档上不可达或被覆盖（必死口袋：出生区 = 整块 ≥25 格平地出生棋盘 > 8；新地表禁令被 `BOARD_CELL_NOT_FLAT_GRASS` 覆盖，检查方补回棋盘图上"四种新地表都被拒"）。`RetiredProfiles`：标准档 / 缺 Profile 报 `MAP_PROFILE_RETIRED` 并拒绝；文件写边疆档读入即报"已删除"。`MapData` / `MapFile` 不动，三张内置图摘要与导出逐字节不变。
- 检查方修一处真实缺陷：缺 Profile 的旧地图文件让 `run` / `play` 以未处理异常崩溃（退出码 134，`run` 还写出了半份输出）→ 入口接住 `MapValidationException`、`BatchRunner.RequireLoadable` 开跑前校验；补入口级测试。规格档分流守门补"拒绝码必须是白名单内整串字面量"，堵住 `"DEAD_" + "POCKET"` 拼接绕过。
- `map` 子命令只在 `--out` 时落盘（不再写 `maps/` 权威文件）。批量顺排选区代码**保留**（主会话接受：16 个测试文件经 `CreateUnvalidated` 的 4 区合成图依赖它，改它属确定性契约变更）。
- 已知且接受：棋盘档不再校验容差 / 豁免字段（校验器已不读）；`ReportDistances` 的"全部可达"分支在合法棋盘图上不可达；每局换图 K 按第 0 局的图落成（棋盘图最小 2 人约 207 格 > 150，不会跨档）。
- 全量 2147 通过 / 0 失败 / 8 跳过（缺省与 28 核）；两处构建 0 警告；Godot 缺省 `--auto-demo` 通过。耗时同机对 `16d8167` 约 1.95×（噪声大，段 C 与段 B 持平）。

### 段 D / E（2026-10-07，规格与文档、收尾）

- 规格增量 45 条（ADDED 2 / MODIFIED 36 / REMOVED 7），覆盖 10 个能力；设计文档 v1.30；HANDOFF / README / ROADMAP / 四份 art README / `boundaries.md` / `determinism.md` 同步。发布页不改（v0.3.0 包仍是旧图，随下次发版改）。
- 测试方法名对齐新 Scenario（11 处 + 8 处带旧图名的顺手改）；新增 `地形规则的当前来源Tests`（棋盘图无不可达地形 / 合成盘面规则照常 / 棋盘图立栅有合法目标）、`引用已删除地图的旧存档`、`显式搜索配置未写K时取跑局级值`、`校准地图已删除的补注`。src 只改注释。
- 检查方修三处：ai-decision「候选格上限」补"小图缺省 0 不写该项"例外（与 Scenario 小图零变化一致）、`shots.sh` 绝对路径、一处旧注释。D-K2（AttachConfigured 回退）新测试不红但既有测试红 1，回退已有守门。
- 预算两列来历：校验区间（4 人 250–1000 等）始于 board-map / board-isolated-gen；负责人裁决的 300–800 / 225–600 / 150–400 是生成器目标带（`BoardMapGenerator.cs:33`），二者分列、一致。
- 全量 2155 通过 / 0 失败 / 8 跳过（缺省与 28 核）；两处构建 0 警告；Godot 缺省 `--auto-demo` / `--pick-check` / 选图自检通过，`siege-4p-base-v5` 与 `gen:1` 报已删除退出 1；截图 `art/retire-legacy-maps/`（近景拍到的是自杀手叉，"外观与之前相同"无基线未验证）。
