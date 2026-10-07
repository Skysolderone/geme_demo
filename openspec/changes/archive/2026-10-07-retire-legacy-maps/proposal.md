## Why

母任务 `board-terrain` 的第三个 change：负责人 2026-10-06 裁决所有地图都由互不连通的棋盘组成、旧地图彻底删除、规则测试全部改写到新棋盘图（不保留 v5 作测试夹具）。前两个 change 已让棋盘图成为缺省，旧地图只剩 `--map=` 入口与一批钉在 v5 上的测试。

调研（`.trellis/tasks/10-06-retire-legacy-maps/research/`）：可删产品代码约 3290 行 / 18 个文件；依赖旧图的测试 439 个方法 / 101 个文件；本机 11 条"已知红测"的根因是机器相关值进了被哈希的文本（6 条 CPU 核数、5 条推测行尾），不修则这次重定的黄金值跨机器必红。

负责人 2026-10-06 裁决：不可达的地形规则本 change 保留、另开 change 定；工坊信物在棋盘图上无效果，本 change 不动、记为已知问题；先修跨机器红测；一个 change 内分段做完再看。

## What Changes

- **段 0 跨机器红测**：被哈希的日志首部 / `config.json` 不再含实际 CPU 核数等机器相关值；哈希前统一行尾。两台机器得到同一组黄金值，"全量测试全绿"成为统一判据。
- **段 A 测试迁到棋盘图**（旧代码仍在）：跑局夹具、前瞻夹具、相机夹具、终端脚本、信物生成测试与只把 v5 当棋盘用的规则测试改到内置棋盘图；整局测试加小回合截断或改用 2 人图；在新图上重定黄金值与"与改动前逐步相同"类基线。
- **段 B 删除**：删 `siege-4p-base-v5`、`siege-2p-base-v1`、`siege-3p-base-v1`、`siege-frontier-v2`、`gen:` 边疆档生成器与新地表投放、`MapSymmetry`、`maps/*.json` 与嵌入资源；迁出 `MapGenerationException`、`FriendlySeed`；每局换图扩到 `board:`；2 人旧图的 AI 权重覆盖条目删除（机制保留、表为空）；删只测旧图的测试；`MapCatalog.Resolve` 对已删除的旧标识给出明确报错，删除 `maps/<标识>.json` 隐式回落（显式文件路径保留）。**BREAKING**：旧存档、旧日志、`gen:` 标识无法加载。
- **段 C 校验器收口**：删 `边疆` 档与 `标准` 档的声明行及专属规则；`标准` 只作合成测试图的缺省值，校验器对它明确报"已删除"。`MapData` 字段与 `MapFile` 导出格式不动（保住三张内置棋盘图的摘要）。
- **段 D 文档与规格**：设计文档 §3 重写；openspec 删除 / 改写旧图、边疆档、标准档相关 Requirement；地形规则在规格与设计文档里标注"当前所有地图都不产生，规则保留待用"；HANDOFF、README、ROADMAP、发布页同步。

## Capabilities

### New Capabilities
无。

### Modified Capabilities
- `map-definition`、`map-generation`、`map-selection`、`simulation-harness`、`ai-decision`、`match-telemetry`、`terrain` 等（完整增量在段 D 写成；本提案阶段先登记删除四张旧图的 Requirement）。

## Impact

- Core：`Board/Maps/` 旧图与边疆生成器整删、`MapCatalog`、`MapValidator`、`MapRandom`、`EvaluationWeights`（覆盖表）、`RunConfig`（每局换图）、日志首部。
- Sim / Godot：`Program`、`PlayCommand`、`GameRoot` 的取种子与报错。
- 测试：约 101 个文件；黄金值约 13 组重定、6 组删除。
- 数据与文档：`maps/`、设计文档、openspec、HANDOFF、README、ROADMAP、发布页、`art/*/README.md`。

## Non-goals

- 不可达地形规则（高度、深水、桥、林地、土路、四种新地表）的去留；工坊信物的处理。
- AI 校准。
- `MapData` / `MapFile` 字段精简（会让内置棋盘图升号）。

## 前置

子任务 ② `builtin-board-maps` 已归档（main `7be3f3b`）。工作树 `.claude/worktrees/board-terrain`、分支 `feat/board-terrain`。
