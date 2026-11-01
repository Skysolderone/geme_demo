# 技术设计：big-map

> 裁决编号见 prd.md；本文只写状态模型、计算顺序与边界，不定技术栈。规则内核的硬边界见 `.trellis/spec/core/`。

## D1 标识与档位（裁决 11）

- `LinkMapId` 新增第四段**档位** `MapTier`：`s`（小图，现状）/ `l`（大图）。`Parse` 接受三段（= `s`）与四段；`Format(seed, players, tier)` 在 `tier == s` 时输出**三段**（现有标识、存档、日志、黄金值逐字节不变），`l` 输出四段 `link:<种子>:<人数>:l`。`Normalize` 同理。
- 档位进入 `MapData`：新增 `MapTier Tier`（缺省 `Small`），由生成器写入；`MapValidator` 按档位选预算表；`MatchPublicView` / 日志首部 / 存档地图摘要带档位（旧存档缺字段按 `Small`）。**小图零字节原则**：档位、旗格、`RoundCapIsDefault` 等新字段在 `Small` 上一律不写出（null 不序列化、地图导出文件不加字段），存档里 `RoundCap` 仍存解析后的整数——小图的存档 / 日志 / 地图导出逐字节不变。
- 随机源：`MapRandom.Create` 的 (种子, 人数) 一侧再混入档位常量（新增 `TierDomain`），`Small` 时**不混**（保持旧取值），`Large` 时混——同种子的大图与小图不同构。

## D2 大图生成（裁决 3 / 12）

- 外接矩形恒 150×150（不随人数），`Margin` 2 → 画布 154×154。沿用 `Island` 掩码 → `Carve`（切角 / 海湾按边长比例放大：切角 10–20、海湾深 2–8 长 8–30、每边保留 ≥ 8 格原岸线）→ 中立平台（数量按面积：14–18 块，边长 6–8，`PlateGap` 3，`EdgeGap` 2）→ 低地（河道 6–9 条、每条 2 座桥、1–2 格宽；湖泊 4–9 格 × 25–40 团；岩区 3–6 格 × 25–40 团）→ 新地表叠加照旧。**不放出生座位**：`BirthZones` 为空、`Boards` 只含 `Public` 平台。
- 连通：全部可落子格沿气边恰 1 个连通分量，不通即重抽低地（上限同现状）。
- 预算：单张 ≤ 1 s（Release）。湖泊 / 岩区投放改为"按区块均匀撒点 + 局部尝试"而不是全图随机重试，避免 `MaxClusterAttempts` 在大图上翻倍。
- 校验器 `MapValidator` 大图行：外接矩形 150×150、可落子 12 000–18 000（段 A 按种子 1–20 实测重钉、留 5%）、出生座位 0、中立平台 14–18、连通、每块中立平台规则同小图。小图行一字不改。**现有链接档检查按档位分流**（实施前先列清单）：只对 `Small` 生效的有——出生区 = 出生平台的 3×3 核心区、出生平台边长 6–8 且带主题、出生座位数 = `MaxPlayers`、座位间距 25、画布 / 可落子预算表；两档都生效的有——连通、平台两两不重叠、可落子格都在岛面、新地表只在中间带、每块中立平台的规则；`Large` 基图另要求出生座位数 = 0，落座图另走 D4 的"落座"检查（13×13 Birth 平台不带主题、不查核心区）。

## D3 自由插旗与间距（裁决 2 / 4）

- **合规旗格**（`FlagRules.LegalFlagCells(map)`，纯函数、只读地图）：可落子、高度 0、不是桥格、不在中立平台内；且旗心 9×9 内可落子格 ≥ 40（避免把出生圈压在海湾尖角上）。大图上合规旗格须 ≥ 可落子格的 50%（验收项）。
- `FlagPlanting` 的旗从"座位编号"改为 `Coord`（小图仍是座位编号——两档走两条入口：`PlantAt(player, Coord)` / `Plant(player, zone)`，按 `map.Tier` 只允许其一）。
- **锁定次序与改派**（`LockAll`，大图）：按插旗先后（AI 盲选按 `Players` 次序排在人类之后的既有做法不变）逐个确认：旗与已确认的每一面旗切比雪夫距离 ≥ `FlagGap` 60 则保留；否则改派到"满足与全部已确认旗间距 ≥ 60 的合规旗格里，离原旗最近者"（切比雪夫距离，再按先行后列取第一个）；没有任何合规格时改派到"最小间距最大"的合规格。全程确定性、不消费随机。
- AI 盲选：`PrototypeZoneAssignment.AssignBlind` 大图分支在合规旗格里用 `flag-pick` 子流均匀抽（不看他人旗），改派交给 `LockAll`。段 A 实现并测试，段 C 只调参。
- 事件 `FlagsLocked` 文案带旗格记法；遥测 `LockedFlagCounts` 大图恒每格 1。
- 信息边界不变：锁定前他人的旗格对任何玩家 / AI 不可见（`FlagPlanting` 的公开投影只给"本人是否已插"），旗格比座位编号泄露更多，公开只在锁定那一刻。

## D4 出生区在锁定时物化（裁决 4 / 5 的落地方式）

核心思路：**锁定之后，大图与小图对下游是同一种东西**——`BirthZone` 仍是 int、`Map.BirthBoard(zone)` 仍可用，`LegalRange` / `EditScope` / 座位投影 / 存档 / Godot 归属色全部不改分支。

- `LockFlags`（大图）：按锁定后的玩家次序给每人一个出生区编号 z = 0..n−1，并构造**落座地图**：`BirthZones[z]` = 旗心 9×9 ∩ 可落子，`Boards` = [每人一块 13×13 的 Birth 平台（Origin = 旗 − (6,6)，Theme null）] + 既有 Public 平台；然后把落座图装进对局：`BaseMap` 从此是落座图，地形、障碍不动、战场重导出。锁定时盘上还没有棋子，所以有两条路——`GameBoard.Reseat(seated)` 原地换 `BaseMap`，或在锁定时重建 `GameBoard`；**实施前先用 `codegraph_impact` 列出 `GameBoard.BaseMap` / `MatchFlow.Board` 的持有者**（`SettlementDriver`、`FlagPlanting`、`HandLedger`、`PowerScoreboard`、`BoardHistory`、`StagedBatch`、Sim / Godot 视图），按持有关系选一条并写明理由，不要在实施中途才发现引用没换。落座图再过一次 `MapValidator`（新增"落座"检查：出生平台两两不重叠、不与中立平台重叠、`BirthZones[z] ⊆ 可落子`）。
- 这样：`BirthBoard(z)` = 13×13 → 构筑期落子 / 改造范围（裁决 5）；`BirthZones[z]` = 9×9 出生圈 → 归属色、座位投影、插旗占位（裁决 4）；`BoardPlate.CoreCells()` 对大图出生平台不使用。
- 存档：`MatchSaveData` 新增 `Flags`（每人旗格记法，小图为 null）；恢复时先建基图 → 按旗格重做落座 → 再写盘面。存档地图摘要比对用**落座图**。日志首部记旗格。
- "地图在对局中不变"（open-map O3）改写为"**锁定后**不变"：插旗阶段 → 锁定是唯一一次地图变化，且由 (标识, 旗格) 完全决定。

## D5 构筑期（裁决 5）

`PublicRules.LegalRange` / `EditScope` 不改签名、不改逻辑：第 1–3 大回合 `plate.Contains`（大图 13×13、小图 8×8），第 4 起全图。只补测试与注释。`BuildProtectionRounds` 3 不变。

## D6 对局时长（裁决 6）

- `MatchOptions.RoundCap` 改为 `int?`：null = 按地图档位取缺省（`Small` 100、`Large` 60，常量放 `MatchOptions.DefaultRoundCapFor(MapTier)`）；显式给值优先。`MatchFlow.RoundCap` 返回解析后的值。批量配置 / `--round-cap` / 存档字段语义不变（存的是解析后的值）。
- 到点仍走 `SettleAtRoundEnd` 判定链第 (c) 条，终局原因不变；`MatchPublicView` 新增 `RoundCapIsDefault`，供界面写"第 X / 60 大回合"。
- "填满"分支保留。

## D7 Sim / CLI

- `--map=` 接受四段；`--players` 与标识人数不一致仍报错；`map` 子命令打印档位、外接矩形、可落子数、合规旗格数、生成耗时。
- 批量 `config.json` 的 `MapId` 直通；日志首部 `MapTier`、`Flags`。

## D8 AI 最小适配（裁决 10，段 C）

- `HeuristicTurnController` 候选格枚举前加**局部预筛**（只在 `map.Tier == Large`）：候选 = `LegalRange` ∩（己方任一棋子或己方旗切比雪夫 ≤ 8 的格 ∪ 对手任一棋子切比雪夫 ≤ 2 的格）；己方没有棋子时 = 旗心 13×13。专家前瞻的模拟对手同一实现。搭桥候选（ai-bridge-candidate）在大图上只在同一预筛范围内枚举。
- 小图不经过预筛：15 份基线与黄金值逐步不变，AI 评价版本**不升**（大图没有基线）。
- 每步耗时目标：标准难度 ≤ 3 s（本机）。只记录，不扫档。

## D9 Godot（裁决 7 / 8 / 9，段 B）

- **分块**：`BoardView` 按 16×16 区块组织地砖——每个区块、每种（材质, 高度）一个 MultiMesh；装饰沿用现有合批但按区块归档；棋子 / 叠加物 / 光标仍逐格节点（只对已加载区块建）。相机注视点所在区块 ± 2 圈为加载集，平移时增量加载 / 卸载；小图 45×45 = 3×3 区块全部常驻，画面与改前一致（截图对比）。
- **无格地面**：`AddBoardPlates` 墨线与格缝刻痕在大图默认不画；新增 `LocalGrid` 节点：部署阶段在光标周围 7×7 与合法落子范围（`LegalRange`）画细线格网；信息层打开时对已加载区块画全图格。小图沿用现状（map-surface 裁决）。
- **插旗界面**：大图插旗阶段地面可点，光标处显示 9×9 预览圈与"距最近旗 N 格"提示（< 60 变红但仍可点，锁定时改派）；锁定后 `BoardCamera` 注视点跳到自己旗心、距离取近景。
- **小地图**：右下角 `Minimap`（2D）：按地图数据一次绘制地形色 → 每次 `Refresh` 叠棋子点与视口框；点击 → 相机注视点；M 键在大图上切小地图显隐（全局预览不再对大图启用）。
- 新局面板：`MapSelectModel` 新增 `Tier` 与 `SelectTier`，`CurrentId` 经 `LinkMapId.Format(seed, players, tier)`；缺省 `Small`。
- 自检：`--pick-check` 大图抽样 2000 格（按坐标散列确定性抽）。

## D10 规格与文档（裁决 14）

直接改 `openspec/specs/`：`link-map-generation`（加「档位」「大图档布局」，重写废弃的「初始布局」为浮空岛 + 大陆两档）、`map-selection`（尺寸选项）、`match-setup` / `turn-sequence`（自由插旗、锁定时落座、构筑范围 13×13）、`elimination-endgame` 或 `battlefield`（对局时长缺省按档位）、`information-visibility`（旗格公开时机同座位）、`ai-decision`（大图局部预筛，段 C）、`viewport-camera` / `tactical-layers` / `visual-style-baseline`（无格地面、局部格网、小地图，段 B）。设计文档 §3.2–§4.1 改写、§3.5「大图档」、§19 登记、变更记录加一条（版本号接最新 +1）。

## 接口契约

| 接口 | 输入 | 输出 / 触发 |
|---|---|---|
| `LinkMapId.Parse` | 三段或四段标识 | (seed, players, tier)；三段 → `Small` |
| `LinkMapGenerator.Generate(seed, players, tier)` | 档位 | `MapData`（`Tier`、`BirthZones` 大图为空） |
| `FlagRules.LegalFlagCells(map)` | 基图 | 合规旗格集合（确定性次序） |
| `FlagPlanting.PlantAt` / `LockAll` | 旗格 | 锁定表 (player → 旗格, zone)；改派确定性 |
| `MatchFlow.LockFlags` | — | 落座图写入 `Board.BaseMap`，`BirthZone` 写入记录，事件 `FlagsLocked` |
| `MatchOptions.RoundCap` | `int?` | `MatchFlow.RoundCap` 解析值（Small 100 / Large 60） |
| `MatchSaveData.Flags` | 旗格记法 | 恢复时重做落座 |

## 已知歧义与建议裁决（实施中发现再追加，编号接 15）

- 15 建议：旗格禁止放在桥格与中立平台内（防止出生圈切进平台规则）；高度 0 限制是为了让 9×9 出生圈不被崖壁切碎。
- 16 建议：大图 `FlagGap` 60 对 4 人在 150×150 上恒可行（四角可行解存在）；若某张图合规旗格因岸线被咬得只剩 < 50%，生成器在同一条岸线子流上重抽（上限 8 次），不是放宽间距。
- 17 建议：小图上也允许四段写法 `link:1:4:s`，但规范化输出回三段。
- 18 建议：落座图的 `MapVersion` 置 1（基图 0），供存档 / 日志区分"锁定前后"；对局规则不读它。
