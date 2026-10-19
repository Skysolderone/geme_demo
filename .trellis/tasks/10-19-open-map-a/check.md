# 10-19-open-map-a 质检记录（段 A）

质检方 2026-10-19；对照 openspec/changes/open-map（proposal O1–O8、design D1–D10 / A1–A8、13 份规格差分）与 `.trellis/spec/core/`。

## 质检项结论

1. **13 份规格差分逐条对照**
   - `link-map-generation`：「换锚」Scenario 由 `初始布局Tests.三加二布局` 的 `seed == 7` 分支钉住（o4 改锚 o2 北贴）；画布区间两处口径并存——`MapValidator` 链接行注释写理论边界 22–30 / 16–20 的推导与种子 1–20 / 1–2000 实测，`初始布局Tests.画布按布局裁出` 注释写"种子 1–20 实测（校验器取理论边界）"。通过。补：`InitialLayout.AttachToFirstRow` 换锚回退原本不检查"共享边 MUST ≥ 4"，加硬守卫（< 4 即抛 `MapGenerationException`；不改任何种子摆位，15 份基线不动）。
   - `simulation-harness`「可复现回放」：「纯 AI 局可凭种子复现」「失败局可复现」「带入对局可回放」由未 Skip 的 `可复现回放Tests`（`SimFixtures.Config` 缺省 `link:1`）在执行；`可复现回放_链接模式Tests` 里与配对无关的「replay命令拒绝旧日志」「回放接口拒绝非链接日志」恢复执行（其余 7 条只测配对，仍 Skip）。
   - `match-telemetry`「战场落败复盘」半边：`战场落败复盘与等待者评估Tests` 6 条恢复（「等待者决赛表现可查」仍 Skip）。改动：「有翻盘手时判本可翻盘」P2 由 6 子削到 1 子（唯一战场上"本战场最高者"= 全部其他在局玩家，原 MP-3 变异不再可分）；「复盘不改对局」决胜一手腿改为 P1 / P2 / P3 三人先 Pass（与 `链接模式下的决胜一手Tests.AfterP1Passed` 同口径）；「分析命令的复盘开关」改为断言不出现等待者评估段（无决赛即 null）。`RealLog`（Easy、turn-limit 600、四人四区种子）实跑收尾；测试断言原文：`TargetsOf` 非空、`missing == 0`、`Reviewed == 目标数 == Consistent`。
   - `settlement-show`「棋盘展开节拍」REMOVED：Godot 锁定路径 `ChooseZone → _board.Build(World.Board()) → FlagsLocked`，无展开演出；`MatchSession.WithLinkBeats` 只在 `Links.Count > linksBefore` 时才生成展开节拍，段 A 永不触发；`LinkShowBeats.Settlements(matchEnded: true)` 不出战场横幅 / 提示条（A6）。`--pick-check` 锁定后全图一轮即退出（A7）。通过。
   - `battlefield`「阶段推进」MODIFIED 的三条 Scenario 原只有旁证：`阶段推进Tests` 补「第4大回合不发生任何事件」（含 map-definition「地图整局不变」：`MapVersion == 0`、`MapFile.ToJson` 与开局相同）「构筑期内整轮Pass即终局」「四人同区_开放地图」。
   - 其余规格差分：turn-sequence 7 条、terrain-edit 3 条 + 同源、information-visibility 2 条、battlefield 其余、elimination-endgame、map-definition、ai-decision、visual-style-baseline 均有同名 / 可追溯测试在执行。
2. **基线自洽真的在跑**：显式全知五份由 `正式对战AI的信息边界_链接模式Tests.全知配置下与改前相同` 实跑（未 Skip）；K = 0 实跑原在整文件 Skip 的 `决赛倒计时Tests` 里**已停跑**——新增 `决策序列基线Tests.K为0实跑与缺省基线逐字节相同`（5 份，`finalCountdown: 0`、视野缺省）。变异（实跑）：`decisions-link1-seed1-k0.txt` 末字节 +1 → 该条与「全知与K0副本与缺省逐字节相同」各红 1（Failed 2 / 10），还原后 `cmp` 一致、touch 刷新。
3. **人类玩家路径**：图形版 `ToggleStage` / 终端版都经 `StagedBatch.Stage → BatchRehearsal.ValidateShape → context.LegalRange`，上下文由 `MatchFlow.EnterDeploy → LegalRangeFor(当前大回合)` 构造；禁手标记 `ForbiddenMovesOfCurrentPlayer` 与预演 `BatchPreviewBuilder` 读同一个 `batch.Context.LegalRange`。现有测试 `战场内合法落子范围Tests.构筑期内越区落子被拒`（第 2 大回合暂放到相邻棋盘 → `OutOfLegalRange`）在执行。通过。
4. **改造范围同源**：`TerrainEditRules.LegalTargets / Reject` 的 4 处 scope 条件原是手写复制，抽成唯一私有助手 `InScope(scope, cell)`（边两端各调一次）；`IsLegal` 委托 `Reject`。AI 三处（`HeuristicTurnController`）、预演（`BatchPreviewBuilder`）、批次校验（`BatchRehearsal`）都传 `context.EditScope`，专家前瞻两处上下文同样带上。通过（`构筑期内的改造范围Tests.候选枚举与拒绝理由同源` 在跑）。
5. **唯一实现与硬边界**：`src/Siege.Core` / `src/Siege.Sim` 无 `Godot.`；Core 无 `double` / `float`；`ForbiddenCellsFor` 的扣除只在 `PublicRules.LegalRange`（其余三处为显示读取），守门 `合法落子范围的对外契约Tests.禁入扣除只在LegalRangeFor且与预演共用同一查询` 在跑；`InitialLayout` 只做矩形几何、气边仍只读 `Adjacency`（A2 允许新写布局纯函数）。通过。
6. **Skip 清单复核**（154 条）：恢复 14 条——`链接模式存档Tests` 6 条（小回合边界存档续跑一致 / 终局后存档 / 锁定结果可持久化 / 旧存档被拒绝 / 链接图上缺模式字段的存档被拒 / 链接恢复须四人，断言不改）、`战场落败复盘与等待者评估Tests` 6 条、`可复现回放_链接模式Tests` 2 条。判定 Skip 合理：`战场结算Tests.出局者出序`（结算即终局，没有下一大回合）、`链接模式终局名次Tests.对局里战场落败者按结算时的比较量排名`（PowerAtDefeat 恒等于终局值）、`前瞻中的下一名对手Tests.同战场无对手时退化`（4 人唯一战场上名册为空只在终局后出现）、`AI改造与主题诊断Tests.主题表现可查`（依赖"首轮战场"分析，规格已改为整局胜率，实现待段 B 2.2）、`对局日志的记录内容_链接模式Tests.地形可离线重建`（同名 Scenario 由 `地形改造日志与分析Tests` 守）。
7. **构建与测试**：见下。

## 变异记录（质检实跑；`cp` 备份、`cmp` 还原、touch 后重建并确认绿）

- M-C-D3 `PublicRules.LegalRange` 的构筑期分支恒不成立（`majorRound <= BuildProtectionRounds - 100`）→ 只跑 阶段推进 / 战场内合法落子范围 / 构筑期内的改造范围 / 自动演示全流程自检 / 链接模式端到端：红 8（四人同区_开放地图、范围随大回合扩大、真对局走一遍_四步全通过、禁入格不在范围内、第4大回合进入对方棋盘、构筑期内越区落子被拒、全图解禁、插旗锁定与构筑期）、绿 14；还原后同组 22 绿。实施方 notes.md 没有针对 D3 大回合分支的变异，这条补上。
- M-C-K0 `decisions-link1-seed1-k0.txt` 末字节 +1 → `K为0实跑与缺省基线逐字节相同` 与 `全知与K0副本与缺省逐字节相同` 各红 1；还原后全量跑在重建后的二进制上。
- `InitialLayout` 换锚守卫（共享边 < 4 即抛）：边长 6–8 区间内无种子可触发，不可变异验证，只是把规格 MUST 写成硬守卫。

## 其他修复

- 行尾：实施方写出的 49 个 .cs / .md 文件为 LF（仓库约定 CRLF，`core.autocrlf=true` 下 git diff 只给 warning），全部归一为 CRLF（纯行尾转换，内容不变）。
- 新增 `链接模式端到端Tests.从插旗跑到终局_开放地图`（4 种开局 × 第 4 大回合开始时存档恢复续跑，终局与原局逐项相同；替代整文件 Skip 的链接模式端到端）。

## 验证结果

- `dotnet build siege.sln -c Release`：0 警告 0 错误。
- `dotnet build src/godot/Siege.Godot.csproj`：0 警告 0 错误。
- `dotnet test tests/Siege.Core.Tests -c Release`（单进程、`--blame-hang 8m` 未触发）：总 2179、通过 2032、跳过 147（7 条 Perf / Slow + 140 条 `open-map 段 B 删除`）、失败 0，RC = 0，2 m 39 s。较实施方 1.5 记录（2167 / 2006 / 161）：+14 条恢复执行、+12 条新增（K=0 实跑 ×5、阶段推进 ×3、开放地图端到端 ×4），Skip 161 → 147。
