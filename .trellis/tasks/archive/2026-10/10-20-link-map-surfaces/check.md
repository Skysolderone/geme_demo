# 10-20-link-map-surfaces 质检记录（check）

> trellis-check，2026-10-20，HEAD 2a2c039，改动未提交。

## 逐项结论

1. 规格对照：通过。`初始布局Tests`：「新地表按主题投放」「新地表不改其余地形」（1–20 Theory）；`物化状态校验Tests.新地表过多被拒`；`地图静态校验规则Tests`：「中间带新地表被接受」「外圈或核心区的新地表被拒」。全量里都跑了，不在 Skip 清单里。
2. 独立子流：通过。`ForLinkSurfaces` 用了独立域常量 `LinkSurfacesDomain`，`新地表子流与布局子流互不扰动` 在跑；`Layout` 里布局流 `rng` 的消费段没动，叠加用的是另起的流；`GenerateWithoutSurfaces` 是 `internal`，没有生产调用方（`InternalsVisibleTo` 含 Siege.Sim，原则上 Sim 能调到，但目前没有用到，只登记不改）。亲自对照 `export/before|after` 的 link:3 / 11 / 17：不同的字段只有 `Surfaces`，差异格全是 `G` 变成 D / M / P / S，和 `diff-before-after.txt` 一致。另外用现行代码跑 `Siege.Sim map --map link:3`，五块的新地表格数是 2 / 0 / 2 / 3 / 2，和 after 导出相符。
3. 候选规则：已修复 1 处。`OverlayBoard` 里 `middle.Count <= SmallMiddleBand` 原来判了两次（S3 目标格数、F1 免除各一次），收成一个 `bool small`，行为不变。D2 表（`Fits`）、S1（`IsBaseCandidate` 的 core 项）、F2 / S2（各一处 `pool` 退路）各只有一处。中间带口径是 `InMiddleBand` = 棋盘内、不在外圈、不在核心区，对应 6×6 = 7、6×7 = 11、7×7 = 16、8×8 = 27，和 D3 一致。
4. 校验器：通过。`BOARD_SPECIAL_SURFACE` 两条口径（位置：外圈 / 核心区要标出来；数量：大于 `MaxSpecialSurfaces = 4`）；`BOARD_RIM_NOT_FLAT`、`BIRTH_CORE_NOT_FLAT`、`BIRTH_ZONE_SPECIAL_SURFACE` 原样保留，失败信息分得开。
5. 基线与黄金值：通过。亲自用 `git show HEAD:` 对照 seed3：首个分歧是 P0 第 1 大回合，旧落点 L5 J6 J8 变成新落点 M4 L5 J6，J8 挨着新浅滩 K8，和 `rebaseline.txt` B 段相符。`LinkModeFixtures.cs`（link:90008 等）相对 HEAD 零改动；`AI对地表的感知Tests.cs` 只改了注释；`AI改造与主题诊断Tests` 截断从 24 改成 36，用的是 `LinkSimFixtures`（link:1 生成图），不是手写载体。
6. AI 与规则不变：通过。`src/Siege.Core/Ai`、`Scoring`、`Adjacency`、`GameBoard*`、`TerrainEditRules`、`TerrainData`、`LifeShape`、`Siege.Presentation`、`src/godot` 相对 HEAD 都没改；`新地表判断唯一Tests` 在跑。
7. F4：已定性，结论见下节。
8. 文档：已修复。HANDOFF 改了 4 处：① 截图索引"`link:1` 九格新地表"改为十格（F1 / F2 后）；② 后续 change 第 8 项注明"即设计文档 §19 第 9 项，proposal 的'§19 第 8 项'是旧称"；③ 基线一条补"1.3（F1 / F2）再重钉"；④ 第 14 项补 F4 定性。设计文档 v1.30 的 §3.1、§3.3（配对表、格数、S1 / F1 句）、§19 第 9 项、变更记录和规格对得上。`art/link-map-surfaces/README.md` 的十格位置和导出的 link:1 一致（V4 / V8、K14 / M14、P4 / K8、D9 / G9、G12 / E15；棋盘范围也核过）。
9. 构建与测试：结果见末节。

另修：`新地表判断唯一Tests` 允许清单的两条注释——`Program.cs` 补"map 子命令每块新地表格数（只计数输出）"；`MapValidator.cs` 的出生区规则按现行规格改成第 6 条，并注明代码注释还沿用旧编号第 9 条。

## F4 定性

- 用 `git worktree add --detach sim-out/head-2a2c039 2a2c039` 检出改前代码，构建 `src/godot/Siege.Godot.csproj`（Debug，0 警告），先跑 `--import`（退出 0），再跑 `--headless --path src/godot -- --map=link:501492606 --map-select --auto-demo`，**退出 1**。
- 失败在第 4 步「开始」：节点数 167 = 162 + 5，是对的，失败的是 `!IsOverview` 这一项。过程：进入时的图是 24×18、全局预览关；第 2 步"换一张"随机到 link:893157498（26×19），预览自动打开；第 3 步回到进入时的图，预览仍开着；第 4 步失败。
- 结论：**改前同样失败，是既有缺陷，与本 change 无关**（`src/godot` 相对 HEAD 也没有改动）。worktree 已 `remove` 并 `prune`。
- 本次质检在现行代码上跑四条自检，第 3 条（没带 `--map`，进入时随机到 link:149922411，23×19）同样第 4 步失败、退出 1，和上面同类。

## 构建与测试

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`：共 2095 条，通过 2089，跳过 6，失败 0，退出码 0。
- Godot headless 四条：① `--auto-demo` 退出 0；② `--pick-check --seed=5` 退出 0（214/214）；③ `--map-select --auto-demo` 退出 1（F4 同类）；④ `--rounds=0 --seed=5` 退出 0（第 30 大回合终局，四步齐全）。
- 跑局：没有另跑冒烟或批量；只有 Godot 自检 4 次和 F4 复现 1 次。
