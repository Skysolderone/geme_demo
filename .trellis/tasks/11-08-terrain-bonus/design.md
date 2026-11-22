# 设计：地形全可落与地形加成——段 A

## 裁决记录（已确认，2026-11-08，用户"按默认做"）

| # | 裁决 |
|---|---|
| 1 | 岩石与未架桥深水都放开可落；画布外虚空仍不可落（虚空 ≠ 岩石的区分：段 A 新增地表 `Rock`，`Obstacles` 保留为虚空；段 B 生成器改写） |
| 2 | 桥格保留为"深水上的陆地"（无深水修正）；匠人搭桥目标不变 |
| 3 | 保留全部现有限制型效果，在其上叠加加成 |
| 4 | 加成作用在**单位基础军势**（加法，同战鼓口径），逐枚、被倍率放大 |
| 5 | 只按地形（8+1 种地表 + 3 档高度），不做地形 × 单位类型矩阵 |
| 6 | 高度修正 h=1 +1、h=2 +2，与既有"高地压制加值"（位置加值）并存，不得合并 |
| 7 | 岩石：+2、不产生覆盖；深水（未架桥）：−1、空格不是气、不是覆盖目标、覆盖穿过 |
| 8 | 段 A（规则内核）→ 合回 main 后再起段 B（生成器 / 校验器 / 美术），各占独立 worktree；段 A 唯一越界：重钉可落子预算与合规旗格下限 |

## 实现决策（I 编号，与用户裁决 1–8 不混用）

- **I1 常量唯一处** `Siege.Core/Scoring/TerrainEffects.cs`：`UncalibratedSurfaceModifier(Surface)`、`UncalibratedHeightModifier(int h)`、`EffectiveBasePower(PieceType, Surface, int h, bool bridged)`。初值（未校准）：草地 0、土路 0、林地 +1、深水 −1（架桥 0）、荒漠 +1、沼泽 +2、岩台 0、浅滩 +1、岩石 +2；高度 0/+1/+2。
- **I2 接入点唯一**：`PieceEffects.BaseTotal` 逐枚改调 `TerrainEffects.EffectiveBasePower`；`BasePower(PieceType)` 保留为类型基础值（文案与均衡分析仍用它）。`BalanceAnalyzer.PieceShares` 不含地形（注明），不除以 0。
- **I3 深水三态**（`MapData.IsPlayable` 去掉深水条件）：
  | 状态 | 可落 | 空格是气 | 属空区 | 是覆盖目标 | 穿过 | 气边 |
  |---|---|---|---|---|---|---|
  | 空 · 未架桥 | 是 | 否 | 否 | 否 | 是（到对岸） | 有（|Δh|<2、无栅） |
  | 有子 · 未架桥 | — | — | — | 否 | 是 | 有 → 棋串可跨水相连 |
  | 架桥 | 是 | 是 | 是 | 是 | 否 | 有 |
  `MapTables.Flags` 的 `LibertyWhenEmpty` 增加"非未架桥深水"；`Adjacency.Receives` 增加"非未架桥深水"；`CoverageTargets` 第 ② 步不变。
- **I4 岩石** `Surface.Rock`（枚举末尾追加，字码 `K`，显示名"岩石"）：`CoverageTargets` 源为岩石时返回空（与沼泽同一分支）。
- **I5 旗与遗址**：`FlagRules.IsFlagGround` / `FlagCellProblem`、`RelicSite.IsSiteGround` 增加"非未架桥深水"。
- **I6 文案**：`GameBoard.Place` 错误文案只剩"障碍格"；`Labels.PieceEffect(Line)` 去掉"深水"；新增 `Labels.TerrainModifier(Surface)` / `Labels.HeightModifier(int)` 供面板 / 图例。
- **I7 陆地口径（实施修订，取代重钉）**：新增 `MapData.IsLand` / `LandCount`、`Adjacency.LandLibertyNeighbors`、`Battlefields.OfLand`（= 深水可落之前的口径）。地图生成器、`MapValidator`、`FlagRules`、`RelicSite` 全部改走陆地口径，既有地图、旗位、遗址逐格不变，不必重钉预算。对局规则（落子范围、气、覆盖、战场）走 `IsPlayable` / `Battlefields.Of`，含深水。决策基线 / 哈希若因计分变化而红，按既有记录命令重录。
- **I8 校准标注**：`EvaluationWeights.CalibrationStatus` 追加"terrain-bonus 后 PowerGain / Growth 等九维未校准"。
- **I9 规格**：直接改 `openspec/specs/*/spec.md` 的 Requirement / Scenario（项目近期惯例，不建 `openspec/changes/`）。

- **I10 活形**：空的未架桥深水与空浅滩同一口径（不属于空区、不是墙），原「未架桥深水算墙」改为「空的未架桥深水不是墙」。

## 不在范围

生成器改岩石为 `Rock`、虚空语义进地图文件、校验器岛形 / 预算全面重钉、Godot 地表材质与图例美术、AI 对新地形的估值（只记录）。
