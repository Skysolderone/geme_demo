# 实施计划

一条线：worktree `E:/wws/geme_demo-seats`，分支 feat/seat-map（基于 main 890bdcd）。三段顺序，每段一个 trellis-implement 子 agent，段间主会话验收并提交；子 agent 不提交。

## 段 A：Core

1. `LinkMapGenerator`：长方形岛（E1）、座位固定落位、中立平台（E2）、中央入口（E3）、Layout summary 消费顺序。
2. `MapFile`：链接档公共棋盘带主题（E2）。
3. `MapValidator`：E5 各条；可落子格区间按种子 1–20 × n∈{2,3,4} 实测给出。
4. `FlagPlanting` / `PrototypeZoneAssignment`：撞座位与未选者改派到空座位（E4）；`ZonesMustExceedPlayers` 改口径。
5. 信物刷新区域核对（E6 最后一条）。
6. 测试：生成 / 校验新规则（含反例）、盲选改派、确定性；换图导致的基线 / 哈希重钉，旧值写注释；不调 AI（S7）。
7. 验收：`dotnet build siege.sln -c Release` 0 警告；全量 `dotnet test tests/Siege.Core.Tests -c Release` 失败 0（Sim / Godot 文案类失败可留给段 B，但须逐条列出）。

## 段 B：入口

1. Sim：插旗提示、`map` 输出、终端渲染区分座位 / 中立平台；取景含长方形岛。
2. Godot：选图预览只编号座位、中立平台不同样式；长方形画布取景；插旗只能点座位。
3. 验收：Sim `run --count 1 --players 2 / 4 --turn-limit 120` 成功；Godot 构建 0 错误；`--map-select` 截图；全量测试失败 0。

## 段 C：试玩 + 检查

1. 主会话 Godot 自动演示截图 2 人 / 4 人：整岛可见、座位间战场、保护期只落座位、第 4 回合起可下低地与中立平台。
2. trellis-check 整分支检查。
3. 更新 `.trellis/spec/core`（boundaries / determinism 的链接档描述）、PRD 勾选、归档、提交。

## 风险

- 图变大（约 1500–1900 可落子格）AI 更慢（S7 不处理，只记录耗时）。
- 盲选改派改变 `BlindPicks` 语义，涉及插旗相关金值。
- `MapFile` 主题规则放宽要同时守住"非链接档不得带主题"的旧测试口径（若已无非链接档则简化）。
