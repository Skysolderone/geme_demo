# 10-06-retire-legacy-maps（母任务 10-06-board-terrain 的子任务 ③）

> 规格权威：`openspec/changes/retire-legacy-maps/`（proposal / design / specs / tasks）。负责人裁决全文见母任务 `prd.md`。调研：`research/legacy-removal.md`（总览与 Q1–Q12）、`product-code.md`、`tests-and-golden.md`、`saves-and-docs.md`。

删除旧地图（v5 / 2p / 3p / 边疆 / `gen:` / `maps/*.json`）；测试全部迁到棋盘图或合成盘面；黄金值重定且两台机器通用；旧标识明确报错；校验器收口；文档与规格同步。

负责人 2026-10-06 裁决：不可达地形规则本 change 保留、另开 change 定；工坊信物不动、记为已知问题；先修跨机器红测（段 0）；一个 change 内分段，全部做完再看。Q3–Q12 按调研建议（见 design「已知歧义」）。

验收：`openspec/changes/retire-legacy-maps/tasks.md` 各条的验证项。段 0 之后判据为"全量全绿"。

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
