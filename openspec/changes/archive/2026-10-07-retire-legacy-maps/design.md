## Context

调研：`.trellis/tasks/10-06-retire-legacy-maps/research/legacy-removal.md`（总览、规模、Q1–Q12）、`product-code.md`、`tests-and-golden.md`、`saves-and-docs.md`。

## Goals / Non-Goals

**Goals**：产品里只剩棋盘图；测试全部在棋盘图或合成盘面上；黄金值两台机器通用；旧标识明确报错。

**Non-Goals**：地形规则去留、工坊信物、AI 校准、`MapData` / `MapFile` 字段精简。

## Decisions

### D0 跨机器红测（段 0）

- 日志首部与 `config.json` 不写实际有效并行度（`RunConfig.EffectiveParallelism`）；若需要记录，记请求值或从被哈希的部分剔除。目标：`DOTNET_PROCESSOR_COUNT` 取任何值，11 条红测里的 6 条都绿。
- 其余 5 条：先实测是否行尾（CRLF / LF）；是则在计算哈希前统一为 LF，并在 `.gitattributes` 钉住相关黄金值文件的行尾；不是则查明根因再修。
- 段 0 完成后，判据从"失败名单与改动前相同"改为"全量全绿"（本机），并在 `testing.md` 记录根因。
- 段 0 可单独提交，不依赖删图。

### D1 测试迁移（段 A，旧代码仍在）

- 夹具：`SimFixtures.Config`、`LookaheadFixtures.V5Config` 等改到 `siege-4p-board-v1`，整局类加小回合截断（截断值按耗时定，全量测试总耗时不超过改动前的 2 倍）；需要跑到终局的黄金值改用 `siege-2p-board-v1`。
- 规则测试：只把 v5 当棋盘用的改到内置棋盘图上（坐标平移或重摆局面），能用合成盘面（`LoadUnvalidated` / `CreateUnvalidated`）表达的优先用合成盘面。
- 相机测试"整盘一屏可见的小图"：用合成小棋盘档图或 `siege-2p-board-v1`。
- 终端脚本：按新图坐标重写。
- "与改动前逐步相同"类守门：在新图上重钉为本 change 的基线，保留 v1 / v2、开 / 关之间的反面对照。
- 每处黄金值重定逐条记录"测试 / 旧值 / 新值 / 原因"；不得放宽断言意图。

### D2 删除（段 B）

- 删四张旧图类、边疆生成器全套、新地表投放、`MapGenParameters` / `GeneratedMapId`（迁出 `FriendlySeed`）、`MapSymmetry`、`MapRandom` 的边疆随机源、`maps/*.json` 与 `Siege.Core.csproj` 嵌入资源；`MapGenerationException` 迁到独立文件。
- 每局换图 `--map-per-match` 改为支持 `board:`（`BoardMapId` 现成）。
- AI 覆盖表删除 `siege-2p-base-v1` 条目，`ForMapId` 机制保留；相关测试改用注入表。
- `MapCatalog.Resolve`：已删除的旧标识（v1–v5、2p / 3p、边疆、`gen:` 前缀）报"地图 <标识> 已于 retire-legacy-maps 删除，现有地图：<清单>"；删除 `maps/<标识>.json` 隐式回落，显式文件路径保留。`run` / `play` / `replay` / 图形版 `--map=` 都经此报错；`analyze` 不读地图，不拒绝。
- 删只测旧图本身的测试（约 140 个方法）与对应黄金值。

### D3 校验器收口（段 C）

- 删 `MapProfile.Frontier` 与 `Standard` / `Frontier` 两行声明及专属规则（距离均衡拒绝、咽喉、入口通路、出生区新地表禁令中只服务旧档的部分）；`Standard` 保留为合成测试图的缺省值，校验器对它报"标准档已删除"。
- `MapData` 字段与 `MapFile` 导出格式不动；三张内置棋盘图的摘要黄金值必须保持不变（守门）。

### D4 文档与规格（段 D）

- openspec 增量：删除四张旧图、边疆档生成三条、标准 / 边疆档预算与静态校验中只服务旧档的条目；改写引用旧图的 Scenario（约 25 条，含 `map-generation`「地图种子与确定性」「校验闭环」只能改写不能删）；地形相关能力加一句"当前所有地图都不产生"。
- 设计文档 §3.2、§3.3 重写，升一版；HANDOFF、README、ROADMAP、发布页（"四张内置图"一句已过时）同步；`art/*/README.md` 用旧图的复拍脚本加说明，不改脚本。

## 已知歧义与建议裁决（主会话按调研建议定，评审时可改）

1. `MapData` / `MapFile` 不动（Q3）。
2. 删 `maps/<标识>.json` 隐式回落、保留显式路径（Q4）。
3. 每局换图扩到 `board:`（Q5）。
4. AI 覆盖表清空、机制保留（Q6）。
5. "逐步相同"类在新图重钉并保留反面对照（Q7）。
6. 夹具 4 人图加截断、终局类用 2 人图（Q9）；相机小图用合成图（Q10）。
7. `analyze` 不拒绝旧日志（Q11）；旧图复拍脚本不改、README 加说明（Q12）。

## Risks / Trade-offs

- **BREAKING**：旧存档、旧日志（`replay`）、`gen:` 标识无法加载；`analyze` 仍可分析旧日志。
- 测试迁到 465 格的棋盘图后耗时上升；截断控制在总耗时 2 倍以内。
- 规则测试重摆局面最容易引入假测试：每条重写的断言都要配变异。
- 段 A 改动面最大，检查方要按文件抽查"断言意图是否保持"。
