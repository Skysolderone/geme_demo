# 09-12-relic-system — 技术设计

## 权威来源

本任务的架构决策、接口契约、风险取舍与已确认裁决，全部以 **`openspec/changes/add-relic-system/design.md`** 为准。实现前 MUST 完整阅读该文件，重点是：

- **Decisions** — 每条决策的理由与代价，不要绕过
- **接口契约** — 本任务对上游的输入依赖与对下游的输出承诺
- **Risks / Trade-offs** — 已识别的实现陷阱与对应的强制回归
- **裁决记录（已确认）** — 设计文档未覆盖、已由项目负责人逐条确认的判定

本文件只记录 openspec 设计之外、属于本次实现的技术细节（技术栈选型落地、目录结构、测试组织方式等），随实现推进补充。

## 实现侧补充

**技术栈**：Godot 4 / C#。规则内核为零 Godot 依赖的 net8.0 类库，详见父任务 `.trellis/tasks/09-12-siege-core-prototype/design.md`。

**代码位置**：`src/Siege.Core/Relics` —— 信物生成、揭示与控制、效果快照

**测试位置**：`tests/Siege.Core.Tests/` —— 每条 Requirement 一个测试类，每个 Scenario 一个测试方法，测试名直接用 Scenario 名，便于与 openspec 逐条对账。

**零 Godot 依赖。** `Siege.Core` / `Siege.Sim` 不得引用 `Godot.*`，`Siege.Core.csproj` 有编译期守门。

**必读规范**：`.trellis/spec/core/index.md`，尤其是 [边界与依赖](../../spec/core/boundaries.md)、[确定性](../../spec/core/determinism.md)、[坐标](../../spec/core/coordinates.md)、[测试组织](../../spec/core/testing.md)。

## 兼容性与回滚

- 本任务尚无线上形态，回滚即回退提交。
- 若本任务修改了已被下游任务依赖的契约，MUST 同步更新 `openspec/changes/add-relic-system/design.md` 的接口契约表，并在受影响的子任务 `prd.md` 中记录。

## 实现落地记录（2026-09-13）

**目录**

- `src/Siege.Core/Determinism/`：`GameSeed`（对局种子，按名派生子流 `relic-gen` / `recruit` / `setup`）、`RandomStream`（xoshiro256**，只有整数原语：`NextInt`、`NextPermille`、`WeightedPick`）。
- `src/Siege.Core/Relics/`：`RelicContent`（类型 + 强度 + 徽记绑定）、`RelicWeights`（§8.2 权重表、稀有度 = 10000/权重 × 强度）、`RelicGenerationOptions`（容差 80‰、重抽上限 200、同类惩罚 500‰、升级 200‰）、`RelicGenerator`（两阶段生成）、`RelicGenerationRecord`（种子 + 分布 + 收敛标记，`Serialize()` 导出）、`RelicLedger`（揭示 / 控制 / 快照 / 先锋）、`EffectSnapshot`（不可变快照）、`RelicControl`（四态：无人 / 争议 / 控制 / 封锁）、`RelicPublicState`、`RelicRevealEvent`、`DeployLimitPeak`。

**对下游的 API 形态**

| 契约 | 入口 |
|---|---|
| 效果快照 | `RelicLedger.SnapshotFor(player, board, roster, heldTypeCount, majorRound)` → `EffectSnapshot`（展示数 / 免费选取数 / 类型槽 / 部署上限 / `EmblemCounts` / `OverflowTypeCount`；无先手修正字段） |
| 先手修正 | `RelicLedger.ReadInitiativeBonuses(board, roster)` → 每名参赛中玩家的整数 |
| 信物公开状态 | `RelicLedger.PublicStateOf(coord)` / `PublicStates()` → `RelicPublicState`（未揭示时 `Content == null`） |
| 生成记录 | `RelicGenerator.Generate(map, seed[, options])` → `RelicGenerationRecord`；`RelicLedger.Generation` |
| 揭示事件 | `RelicLedger.Reveal(board, majorRound)` 返回本次新揭示；`RelicLedger.RevealEvents` 全量 |
| 结算接线（match-flow） | 第 4 步 `Reveal`、第 5 步 `RecalculateControl(board, roster)`；`SnapshotFor` / `ReadInitiativeBonuses` 内部按当前盘面重算控制，不依赖上次结算缓存 |

**实现侧判断（trellis-check 2026-09-13 逐条核对后的状态）**

- 同区同类惩罚作用在**容差侧**（每多一枚同类型，该区允许偏差收窄 500‰），而不是加在评分上。check 独立复算（种子 0–9999，逐出生区、只计已收敛）：容差侧 19.2% vs 无惩罚 34.4%；评分侧 +500 只降到 31.8%（评分加罚把最常见的双徽记 444 往均值推，反而更易被接受）。裁决 3 的「计入偏离惩罚」在实现里是把偏差按 1/(1−dup×0.5) 放大后再比容差，即偏差乘法惩罚，字面成立。**已核准。**
- 8% 容差下收敛率 76.1%、平均重抽 57.3 次（check 复算一致）。未收敛时保留末态而非回退第一阶段：末态平均最大偏差 26.8%，第一阶段为 64.9%，保留末态明显更公平，且每格内容仍是权重表抽取结果，符合裁决 4。
- 校正后出生区先锋 / 军令占比（已收敛）0.8% / 3.7%，第一阶段 4.9% / 7.0%。check 用整配置拒绝采样算出 8% 容差下的**算法无关条件分布**为先锋 0.6% / 军令 3.2% / 徽记 71%：稀有品被压低是容差本身的必然结果，不是算法造成的。但「最小改动重抽（偏离最大者）」有两个算法侧副作用：(a) 24% 不收敛；(b) 探勘占比被推高到 35.9%（权重表 20%）。同样种子驱动的「在最差出生区内随机挑一格重抽」实测：收敛 100%、平均 22 次、分布 徽记 48 / 探勘 23 / 兵站 15 / 征召 6.9 / 军令 5.3 / 先锋 1.7，更贴近权重表。**待决**：是否把 openspec design.md D1 的「优先替换稀有度偏离最大的那一枚」改为「在偏差最大的出生区内按种子随机挑一枚」；不改容差。
- 公共区 Standard 与 High 档默认升级率相同（200‰），`BudgetOf` 两档都是 720：High 档在生成结果里与 Standard **没有任何区别**，地图数据的三档区分目前是空操作。规格字面（High > 出生区、公共区约 20%）满足。**待决**：是否默认 Standard 150‰ / High 300‰（4 人基准图 4:2 分布下公共区平均仍为 20%），让「中央区、交通咽喉与高风险边缘区承担更高预算」在生成结果里真实存在。
- `EffectSnapshot.AdjustedWeight(type, baseWeight) = baseWeight × (4 + 3 × 徽记数量)`：§9.1 的 `1 + 0.75n` 乘 4 后全为整数，分母 4 对全部类型相同、归一化后消去。**已核准。**
- 弃赛 / 出局玩家控制的信物为 `RelicControlKind.Blocked`；为其调用 `SnapshotFor` 抛 `SiegeRuleException`；名册未列出盘面玩家同样抛出。无名册重载沿用领地层 `PowerCalculator.Compute(board)` 的做法（public + 文档注明只供单元测试）。**已核准。**
- 揭示判据 `OwnershipKind != Neutral`（独占 / 争议 / 直接占据都揭示；障碍格抛出）；控制判据是同一 `CoverageMap.OwnershipOf` 的四态映射；`Relics/` 无任何邻接调用（源码扫描守门）。**已核准。**
- check 补充：`RandomStream` / `SplitMix64` 已用公开参考向量钉死（`随机原语参考向量Tests`）；`NextInt` 用拒绝采样（上界 `2^64 − 2^64 mod range`），不存在取模偏差；`RelicWeights.RarityOf` 的高阶 2 倍计与「直接占据也揭示」此前无守门测试，已补。全套测试 1 秒内跑完（最重的交叉一致性 0.9 s），无需分层。
