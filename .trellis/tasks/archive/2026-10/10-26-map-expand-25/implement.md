# 实施计划

一条线、一个 worktree（`E:/wws/geme_demo-island`，分支 `feat/island-map`，基于 main 75511f2），分三段顺序执行；每段一个 trellis-implement 子 agent，段间由主会话验收。子 agent 不提交，提交由主会话在段验收后做。

## 段 A：Core（生成器 / 标识 / 校验器 / 测试）

1. `LinkMapId`：`Parse` → `(Seed, Players)`，`Format(seed, players)`，旧两段格式报错（D1）。`MapCatalog.DefaultId` = `link:1:4`。
2. `MapRandom`：两条流的派生纳入人数（D8）。
3. `LinkMapGenerator`：`Generate(ulong, int)`、缓存键、`Layout` 按 D2–D5 重写；删 `InitialLayout`（若无他用）与 `BirthBoards = 5` 常量；`GenerateWith*` 测试接缝随签名更新；`DraftBoard` 支持传空 `drafted`、去掉 Plateau 分支的调用。
4. `MapValidator`：按 D7 改声明表与规则；删除的规则连同其专属测试一起删。
5. 测试：
   - 夹具（`LinkModeFixtures`、`TestMaps` 等）与所有 `link:<种子>` 字面量改为 `link:<种子>:<人数>`，`Generate(seed)` 改 `Generate(seed, 4)`（或测试原意需要的人数）。
   - 只描述旧"5 块紧贴 3+2"布局的测试（初始布局、接缝、紧贴校验、画布 22–25 区间等）删除，按 D2–D7 写新的生成与校验测试：尺寸 ≥ W(n)、平台数区间、坡道 2–3 且朝外是低地、平台 h=2、气边连通、不放静态信物格（段 A3：依据 relic-redesign R29 删除 R5）、同标识确定性、不同人数不同图、旧格式报错。种子覆盖用 1–20 × n∈{2,3,4}（不跑 200 局级别的批量）。
   - 依赖地图内容的快照 / 期望值（遥测、回放哈希、AI 决策期望等）随新图更新期望值；AI 行为断言因换图失败的，只改夹具或期望值，不调 AI（R8 取消）。实在无法在新图上表达的 AI 断言，改用手写小夹具地图，并在交付说明里逐条列出。
   - 慢测试照旧用原人数（`百局端到端Tests` 断言 4 人，保留 `link:1:4`；实施时未改 2 人）；若全量测试耗时明显增加，在交付说明里给出前后耗时。
6. 验收命令：`dotnet build siege.sln -c Release` 无警告新增；`dotnet test tests/Siege.Core.Tests -c Release` 全绿。

## 段 B：入口（Sim / Presentation / Godot）

1. Sim `play` / `run` / `map`：按 D1 处理 `--players` 与标识人数（缺省、裸 link、不一致报错）；`--map-per-match` 同口径。
2. `MapSelectModel`：人数属性与切换（2/3/4），`CurrentId` 带人数；HUD 加人数选项；开局人数取 `map.MaxPlayers`。
3. Godot 取景按 D9：40×40 岛 + 留白 = 44×44 画布默认能看全。
4. 验收：Sim 跑一局 `run --count 1 --players 2` 与 `--players 4` 成功；Godot 构建通过；`--map-select` 截图自检能显示整岛。

## 段 C：试玩验证 + 检查

1. 主会话启动 Godot 实际开一局 2 人、一局 4 人，截图确认：整岛可见、平台 / 坡道 / 河道 / 桥可辨、保护期只能落自己平台、第 4 回合起可下低地。
2. trellis-check 子 agent 对整条分支做规范与质量检查。
3. 更新 `.trellis/spec/core`（地图相关约定若有）与 PRD 验收勾选；归档任务、提交。

## 风险点

- `MapValidator.cs`（约 1000 行）规则删改面大，守门测试 `规格档分流守门Tests` 限制写法。
- `LinkMapGenerator.Themes.cs` 的主题生成器原为 6–7 边长 + 外圈 h=0 设计，抬升后外圈语义变化（D3）。
- 测试面 173 个文件；期望值大面积变化，需逐条确认是"换图导致"而不是回归。
- `ValidatePockets`（必死口袋）在崖壁环绕的平台上可能频繁报错 → 生成器重抽次数上升；需要时在平台内放宽主题，而不是放宽校验。
