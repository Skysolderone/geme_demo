# 实施说明：big-map

分支 `map/big-map`，worktree `E:/wws/geme_demo-bigmap`。三段顺序进行；每段一个 trellis-implement + 一个 trellis-check。

## 通用

- 构建：仓库根 `dotnet build siege.sln -nologo -v q` 零警告零错误（`TreatWarningsAsErrors` 开着）。
- 快速测试：`dotnet test tests/Siege.Core.Tests/Siege.Core.Tests.csproj --filter "Category!=Slow&Category!=Perf" -nologo -v q`（全套约 25 分钟；开发中先按 `--filter "FullyQualifiedName~LinkMapGeneration"` / `~MatchSetup` / `~TurnSequence` 子集跑，收尾全套一次）。
- 代码理解一律先用 codegraph（`codegraph_explore` / `codegraph_node` / `codegraph_callers` / `codegraph_impact`），不整文件 Read 大文件（`BoardView.cs` 约 2900 行、`MapValidator.cs` 约 950 行、`LinkMapGenerator*.cs` 合计约 2000 行、`MatchFlow*.cs`）。
- 先读 `.trellis/spec/core/index.md` 的 Pre-Development Checklist 与四份规范；再读本任务 `prd.md`（裁决 1–14）与 `design.md`（D1–D10、歧义 15–18）。
- 确定性：生成只用由种子派生的 `RandomStream` 与整数运算；大图新增随机消费放在档位混入后的既有子流里，小图三条子流的取值一个比特都不能变（守门：`link:1:4` 黄金哈希、15 份决策基线）。改派 / 合规旗格 / 落座不消费随机。
- **小图零改动**是本次最硬的验收：任何三段标识的地图、对局、存档、日志、Godot 画面与改前逐字节 / 逐像素一致。小图的黄金哈希 / 15 份基线若对不上，是缺陷，不是"重钉"；只有大图自己的新黄金值才是新钉。
- 子 agent 不提交；回报改动文件、自证输出行、疑虑；发现设计与代码冲突时按 design.md「已知歧义」格式编号追加到 prd.md 末尾，不自作主张改裁决。
- 行尾：看 HEAD（多为 CRLF），未改动行保持原行尾；`git diff --stat` 与 `--ignore-space-at-eol --stat` 一致。
- 不生成新文档；规格直接改 `openspec/specs/*/spec.md`。

## 段 A：Core（D1–D7、D10）

1. **标识**（D1）：`LinkMapId` 加 `MapTier` 枚举（`Small` / `Large`）、四段解析、`Format(seed, players, tier)`（Small 输出三段）；`MapData.Tier`；`MapRandom.Create` 的 Large 混入 `TierDomain`（Small 不混）；`MapCatalog.Resolve` 直通。测试：`链接档地图标识与确定性Tests` 加四段 / 三段等价 / 规范化回三段 / 非法档位。
2. **大图生成**（D2）：`LinkMapGenerator` 按 `tier` 取几何常量（放在一个 `TierGeometry` 记录里：矩形、切角、海湾、平台数、河道数、湖泊 / 岩区团数、`FlagGap`、出生平台边长 13、出生圈 9），Large 不放座位；湖泊 / 岩区投放改"按区块撒点 + 局部尝试"；`MapValidator`：**先用 codegraph 列出全部链接档检查项，逐项标注 Small-only / 两档 / Large-only**（分流清单见 design.md D2 末段：出生区 = 3×3 核心区、出生平台边长 6–8 带主题、座位数 = MaxPlayers、座位间距 25、小图预算表都是 Small-only），再加 Large 行（预算按种子 1–20 实测重钉、留 5%；基图座位数 = 0），Small 行不动。测试：新建 `大图档Tests`（种子 1–20 × 2 / 3 / 4：尺寸、可落子区间、座位 0、平台数、连通、耗时 ≤ 1 s、合规旗格 ≥ 50%）。
3. **合规旗格与自由插旗**（D3）：新建 `Match/FlagRules.cs`（`LegalFlagCells`、`FlagGap`、`Reassign`）；`FlagPlanting` 加 `PlantAt(player, Coord)`，`LockAll` 大图分支按先后改派；锁定前他人旗格不可见（公开投影只给"本人是否已插"，与座位编号时期同一边界）；`PrototypeZoneAssignment.AssignBlind` 大图分支在合规旗格里用 `flag-pick` 均匀抽；`MatchFlow.PlantSequentially` / `PlantPrototype` 接受旗格。测试：`MatchSetup/自由插旗Tests`（合规判定算例、4 旗相距 < 60 的改派确定性、AI 盲选间距 ≥ 60、小图入口拒绝旗格 / 大图入口拒绝座位编号、锁定前旗格不可见）。
4. **锁定时落座**（D4）：**动手前先 `codegraph_impact GameBoard.BaseMap` 与 `MatchFlow.Board` 的持有者**，在"原地 `GameBoard.Reseat`"与"锁定时重建 `GameBoard` 并换掉全部持有者"之间选一条、在回报里写理由；然后 `MatchFlow.LockFlags` 大图构造落座图（`BirthZones` 9×9 ∩ 可落子、`Boards` 前置 13×13 Birth 平台、`MapVersion` 1）并装进对局（地形不动，重导出战场）；落座图过 `MapValidator` 新增"落座"检查（13×13 Birth 平台不带主题、不查核心区）；`MatchSaveData.Flags` 与恢复路径；日志首部 `MapTier` / `Flags`——**Small 上这些字段一律不写出**（null 不序列化），小图存档 / 日志 / 地图导出逐字节不变是守门测试。测试：`MatchSetup/锁定落座Tests`、存档往返（大图与小图各一）、`InformationVisibility` 旗格公开时机。
5. **构筑范围**（D5）：只补测试 `TurnSequence/大图构筑范围Tests`（第 1–3 大回合 ⊆ 13×13、第 4 起全图、改造范围 13×13）。
6. **对局时长**（D6）：`MatchOptions.RoundCap` → `int?` + `DefaultRoundCapFor(tier)`；`MatchFlow.RoundCap` 解析；`MatchPublicView.RoundCapIsDefault`；存档 / 日志 / 批量配置 / `--round-cap` 语义核对。测试：大图缺省 60、小图缺省 100、显式优先、存档往返。
7. **Sim**（D7）：`--map` 四段、`map` 子命令输出档位 / 矩形 / 可落子 / 合规旗格 / 耗时；`run --map=link:7:4:l --count 1 --difficulty Simple --turn-limit 3` 跑通并打印每步耗时（只到构筑期；全图阶段 AI 在大图上没有预筛会跑不动，这是段 C 的事，不要在段 A 里修 AI）。
8. **规格与设计文档**（D10 的 Core 部分）：`link-map-generation`（档位、大图档布局、重写废弃「初始布局」）、`map-selection`（尺寸段，界面选项留给段 B）、`match-setup` / `turn-sequence` / `information-visibility` / 终局时长所在规格；设计文档 §3.2–§4.1 改写、§3.5 大图档、§19 登记、变更记录。
9. **自证**：种子 1–20 × 3 档人数的大图统计（可落子最小 / 中位 / 最大、非可落子占比、合规旗格占比、耗时）；小图守门：`link:1:4` 黄金哈希与 15 份基线逐字节不变；全量快速测试结果。

## 段 B：Godot（D9）

1. 分块 MultiMesh 地砖与按需加载；小图 3×3 区块常驻、截图与改前一致。
2. 大图默认无格 + `LocalGrid`（光标 7×7 + 合法范围；信息层下已加载区块全格）。
3. 插旗界面（可点地面、9×9 预览圈、距离提示）、锁定后镜头落旗心。
4. `Minimap` 与 M 键切换；HUD "第 X / 60 大回合"。
5. `MapSelectModel.Tier` + 面板尺寸选项（缺省小）。
6. 自证：`--pick-check --auto-demo --rounds=1 --seed=5 --map=link:5:4:l --mute` 抽样 2000 格往返一致；大图建场景耗时与 fps 日志行；`link:5:4` 小图截图对比。

## 段 C：AI 最小适配（D8）

1. `HeuristicTurnController` 大图局部预筛（己方棋子 / 旗 ≤ 8 ∪ 对手棋子 ≤ 2；无子时旗心 13×13）；专家前瞻与搭桥候选同一范围。
2. 守门：小图 15 份基线与黄金值逐步不变；大图标准难度 4 人 `--turn-limit 20` 每步 ≤ 3 s。
