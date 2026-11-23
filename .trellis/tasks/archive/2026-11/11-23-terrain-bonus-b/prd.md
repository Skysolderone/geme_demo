# 地形全可落与地形加成（terrain-bonus 段 B）

## Goal

段 A（规则内核，main 0a8c5e1e）已让岩石地表 `Surface.Rock`（字码 K）与未架桥深水可落子，但生成器仍把岩石簇写进 `Obstacles`。段 B 把岩石真正变成可落子地表，`Obstacles` 只剩虚空，并补校验预算与美术。

## 用户裁决（9 项，2026-11-22 已批）

| # | 裁决 |
|---|---|
| 1 | 生成器把岩石障碍改为可落子的岩石地表（`Surface.Rock`） |
| 2 | 虚空进地图文件（文件里能区分虚空与岩石） |
| 3 | 校验预算按实测小样本重钉（不跑长局、不跑 200 局） |
| 4 | 岩石用平板 glb（不挡格心，单位能站上去看得清） |
| 5 | 色板 A 加岩石色：与每个领地着色的差值 ≥ 45，**先出色块图** |
| 6 | 未架桥深水上的单位只加水纹环 |
| 7 | AI 只记录，不改评价 |
| 8 | 不碰 `src/godot/parts/terrain_plateau_temp/`（未提交的用户美术） |
| 9 | 从 main 开新 worktree，派一个实现 agent，做完出对照图停下 |

## Acceptance Criteria

- [ ] `dotnet build` 零警告；快测（`Category!=Slow&Category!=Perf`）全绿。
- [ ] 导出 link:1:2 / link:1:3 / link:1:4 / link:9:4:l 与 main 对比：只有岩石格从障碍变 K + 虚空编码；其余差异逐项列出原因。
- [ ] 每条新增 / 改动 Scenario 有同名测试；新守门做变异验证并记录。
- [ ] Godot 自检退出 0；`--rounds=6` Sim 跑局一次，能看到棋子落在岩石上。
- [ ] 色块图 + 对照图（main vs 段 B）落盘在 `art/terrain-bonus-b/`。

## Notes

- 基线重录：`SIEGE_REBUILD_DECISION_BASELINE=1` 跑一次 → 重新构建 → 其余黄金哈希取失败信息里的实际值。只允许依赖生成地图的黄金值重钉；手写夹具的规则测试变红 = 回归。
- 旧日志 / 存档：日志首部的 `MapFile.Digest` 已会让旧图回放响亮失败，沿用，不另加机制。
