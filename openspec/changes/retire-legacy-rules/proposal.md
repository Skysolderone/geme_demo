## Why

`retire-legacy-maps` 删掉旧地图与一切"选模式"的入口后（裁决 L6 = C），对局模式枚举 `MatchMode` 与全部按模式分流的 Legacy 规则分支仍留在 Core / Presentation / AI / Sim 里，只服务手写 9×9 夹具（`MatchFixtures`，经 `CreateUnvalidated` / `RestoreUnvalidated` 以缺省 Legacy 建局）。玩家永远到不了这些分支，但每改一条规则都要连带守住"旧模式不变"，主规格里也还留着"Legacy 模式下……""保护期内为出生区、此后为全图"等条文。本 change 把夹具测试迁到链接规则，然后删除 Legacy 规则分支与 `MatchMode`，只留链接模式一套规则。

对应设计文档章节：§3.3（链接档地图，"Core 仍保留旧模式分支"一句）、§4.2（构筑期）、§12（出局、战场结算、终局与名次）、§13（信息公开）、§15.2（专家前瞻的合法范围口径）、§17（日志首部模式字段）、§19 第 6 项（`retire-legacy-rules`）。设计文档历史变更记录不改，新增 v1.26 → v1.27 一行。

## 负责人裁决（2026-10-15，R1–R5 全缺省）

| # | 事项 | 裁决 |
|---|---|---|
| R1 | 测试迁移三分类 | (a) 只测棋盘规则（气、提子、活形、覆盖、计分、同形、改造、信物、势力……）的测试改为直接在 `GameBoard` / 规则层测，不经对局流程，**期望值不变**；(b) 测对局流程且链接模式下同样成立的，改用链接模式夹具（`LinkModeFixtures` 等），**断言含义不变**；(c) 只测旧规则的（出生区保护期、第 4 大回合全图解禁、整轮 Pass / 填满即终局、Legacy 名次算法 `FinalStandings.Compute`、插旗看得见旗数、Legacy 存档字段），随旧规则一起删 |
| R2 | 删除范围 | `MatchMode` 枚举与所有按模式分支、Legacy 终局与名次算法、非盲选插旗路径、Legacy 存档字段与回填、Sim 里读模式的日志与终端分支（L17）、旧存档缺 K 的回填（W13，解释见 R7） |
| R3 | L18 | 删除校验器两条旧规则（中央入口 / 咽喉须可落子、容差与放宽理由）；**地图数据中对应字段保留**，链接档地图导出字节不变、基线不重钉 |
| R4 | 流程 | 先规划 + 迁移清单交负责人过目 → 实施 → 质检逐名比对被删测试、补回丢失覆盖 |
| R5 | 不变量 | 链接模式行为逐字节不变：5 条链接图决策序列基线、5 份 `*-omniscient.txt`、5 份 `*-k0.txt` 全部原样通过；全程不跑批量 |
| R6–R12 | 规划中发现的事项 | **2026-10-15 全按缺省**（见 design.md「Open Questions」）：R6 = A 合法链接档图载体（FlatMap 四人同区为主）；R7 = 窄解——只删 Legacy 存档不写 K 的模式分支，链接存档缺 K 按 0 回填留痕保留，`match-setup`「决赛倒计时配置」不变（据此 R2 中"旧存档缺 K 的回填"收窄为此义）；R8 (b) 类允许按新载体重导字面期望并逐条记表，(a) 类不许改；R9 Core 删 `EndReason.AllPassed` / `BoardFull`，Sim 用字符串常量读旧日志，平衡报告字节不变；R10 = A 存档 / 日志 `Mode` 照写常量 `"Link"`；R11 允许 5 条合并；R12 只被 Legacy 用到的公开成员一并删除 |

## What Changes

- **测试迁移（实施第一步）**：按 `.trellis/tasks/10-15-retire-legacy-rules/migration.md` 逐类迁移。规划口径：134 个测试文件受影响（派发口径 109 个之外，另有 16 个经 `AiFixtures.Round5` / `PresentationFixtures.StartedWithZonedRelics` 间接走 Legacy、8 个只因 `MatchMode` 写法、4 个带出测试经 `CarryFixtures.Result` 走 Legacy 名次算法、1 个名次函数更名）；(a) 21 条、(b) 317 条、(c) 28 条，另合并 5 条、L18 删 2 条。`MatchFixtures.cs` 删除，9×9 地图数据迁入 `TestMaps` 供规则层与地图数据层测试继续使用。
- **BREAKING（Core API）** 删除 `MatchMode` 枚举、`MatchOptions.Mode`、`MatchFlow.Mode`、`MatchPublicView.Mode`、`SeatPublicView.Mode`、`PublicRules.LegalRange` 的模式参数；`MatchFlow.LinkStage` 改为非空。所有 `Mode == Link` 的门变为无条件，所有 `else` / Legacy 分支删除。
- **BREAKING（规则）** 删除 Legacy 规则分支：出生区保护期的 3×3 落子限制与第 4 大回合全图解禁（合法范围恒为"所在战场的可落子格"）、整轮 Pass / 棋盘填满即终局（终局只剩"只剩一名参赛玩家""最后战场结算"）、Legacy 名次算法（`FinalStandings.Compute` 删除，`ComputeLink` 更名为唯一的 `Compute`）、非盲选插旗（看得见旗数、锁定前可改、超时分到旗数最少的区）、Legacy 存档恢复路径（直接恢复盘面与信物账本、`RelicLedger.Restore`）、"旧模式全图可见 / 全员同战场"的按座位视图与表现层分支、AI 中按模式分流的名册与阈值门。
- **BREAKING（存档 / 日志读入）** 恢复与回放只接受链接模式；存档与日志首部继续恒写 `"Mode": "Link"`（R10 缺省，链接局字节不变）。`EndReason.AllPassed` / `BoardFull` 从 Core 删除（R9 缺省），Sim 分析读旧日志时按字符串常量识别。
- **Sim（L17）**：`MatchSession` 日志首部 / 快照 / 事件 / 名次里按模式分流的字段一律按链接口径无条件写出；`BoardRenderer` 删旧模式全图视口与"旧模式恒同战场"分支；`PlayCommand` 删两类旧终局文案；`Replayer` 对非链接日志明确拒绝。
- **W13（R7 缺省窄解）**：删"Legacy 存档不写 K、恢复为 0"的模式分支；链接存档缺 K 时按 0 回填并留痕的规则保留（`match-setup`「决赛倒计时配置」不变）。
- **L18（R3）**：`MapValidator` 删除 `CENTRAL_ENTRANCE_NOT_PLAYABLE`、`CHOKE_NOT_PLAYABLE`、`TOLERANCE_NEGATIVE`、`TOLERANCE_RELAX_WITHOUT_REASON` 四个失败码所在的两条规则；`MapData` / 地图文件的中央入口、咽喉、距离容差、放宽理由字段保留。
- **文档**：`HANDOFF.md`、设计文档（当前章节 + 变更记录 v1.27）、`.trellis/spec/core/boundaries.md` 中 Legacy 相关条目同步。

## Capabilities

### New Capabilities

（无）

### Modified Capabilities

- `ai-decision`：「候选格上限」Scenario「小图不限制」的"9×9 手写测试夹具"改为手写小链接档图；「前瞻中的下一名对手」去掉"链接模式下"限定、Scenario「专家是本大回合末位」的"全图可落子格"改为对手所在战场、「只剩自己时退化」改 4 人局；「前瞻模拟的公平信息」第 3 项"保护期内为其出生区、此后为全图可落子格"改为"所在战场的可落子格"，Scenario「保护期内只在出生区回应」改为「构筑期内只在本战场回应」；「前瞻的近似两层加分」Scenario「按下一大回合的口径枚举」的"全图可落子格"改为专家所在战场；「链接模式下的决胜一手」删"Legacy 模式 MUST NOT 受影响"与 Scenario「旧模式不变」。
- `capture-resolution`：「以整批最终状态判定合法性」第 1 步括号"保护期内为玩家锁定的出生区"改为"玩家所在战场的可落子格"。
- `information-visibility`：「本大回合的 Pass 名单」删"Legacy 模式下该名单恒为空（裁决 P16）"。
- `map-definition`：「地图静态校验规则」写明中央入口 / 咽喉可落子与距离容差两条规则已删除、对应字段只作数据保留（L18），并加一条 Scenario。

复核过、不需要增量的能力：`turn-sequence`、`elimination-endgame`、`battlefield`（`board-link-mode` 起已只写链接规则，旧测试按清单删除或合并）；`match-setup`（无 Legacy 条款；「决赛倒计时配置」按 R7 缺省不变）；`match-telemetry`（R9 缺省下平衡报告口径与字节不变）；`visual-style-baseline`（"棋盘清单为空的地图只可能是手写测试夹具"仍成立）；`carry-in-out`（"构筑保护期"在链接模式仍指第 1–3 大回合）；其余能力未引用旧模式。

## Impact

- **Core**：`Match/MatchMode.cs`（删 `MatchMode`，`LinkStage` 保留）、`FlagPlanting.cs`（`MatchOptions.Mode`、`IsBlind`、`LeastPopulatedZone`、`FlagPublicView.FlagCountByZone`）、`MatchFlow.cs`（建局、弃赛、发布、终局检查、`Finish`、`EndMajorRound`、`RequireModeMatchesMap`）、`MatchFlow.Link.cs` / `.Countdown.cs` / `.Persistence.cs` / `.Persistence.Link.cs` / `.Preview.cs` / `.SeatProjection.cs`、`MatchPhases.cs`（`EndReason`）、`MatchPublicView.cs`、`SeatPublicView.cs`、`PublicRules.cs`、`FinalStandings.cs`、`Relics/RelicLedger.cs`（`Restore`）、`Ai/ExpertLookahead.cs`、`Ai/HeuristicTurnController.cs`、`Board/MapValidator.cs`（L18）。实测引用 `MatchMode` / `IsBlind` 的站点 20 个文件、74 行（含注释；派发口径"10 文件 37 处"偏低），另有不写模式名的 Legacy 专用成员（`FinalStandings.Compute`、`RelicLedger.Restore`、`LeastPopulatedZone`、`EndReason` 两个成员），完整清单见 design.md D4。
- **Presentation**：`Visibility/DefaultBoardView.cs`、`Visibility/Worlds.cs`。
- **Sim**：`Running/MatchSession.cs`、`Running/Replayer.cs`、`Play/BoardRenderer.cs`、`Play/PlayCommand.cs`、`Logging/MatchLog.cs`、`Analysis/BalanceAnalyzer.cs` / `ReportWriter.cs`（R9）。
- **Godot**（不在解决方案里，`dotnet test` 看不到编译错误）：`scripts/Names.cs`（两类旧终局文案）。
- **测试**：见迁移清单；`MatchFixtures.cs` 删除，`AiFixtures`、`PresentationFixtures`、`LinkModeFixtures`、`CarryFixtures`、`TestMaps` 改造。
- **数据与文档**：`HANDOFF.md`、设计文档、`.trellis/spec/core/boundaries.md`；`sim-out/` 与 `art/` 不动。

## Non-goals

- **链接模式行为不变**（R5）：拼接、战场结算、决赛倒计时、存档与日志格式、AI 决策逐字节不变；不重钉任何基线。
- **不改 AI**：评价权重、停手阈值、前瞻算法与评价版本号不动；AI 测试只换载体。
- **不改地形规则**：河流、桥、林地、栅栏、高差、新地表规则代码与规格不动（只在手写测试图上摆放）。
- **不动与 `MatchMode` 无关的"旧 / Legacy"**：`ContentSets.Legacy`（内容集 v1 别名）与内容集 / 带入带出 / 匠人权重 / 地图摘要的缺字段回填、`LogResult.LegacyMajorRoundLimit`、`GameBoard.Restore` / `RestoreUnvalidated`（盘面序列化往返）、match-setup「旧存档照常读取」「旧存档按无带入读取」。
- **不改平衡报告口径**（R9 缺省）：终局原因分布仍按旧日志的三类原因名统计；改为链接两类口径登记为后续观察，不在本 change。
- **不删构筑期概念**：`BuildProtectionRounds`（第 1–3 大回合）仍是链接模式的构筑期、带出"保护期内弃赛"与决赛倒计时起点的依据。

## 前置 change

- `retire-legacy-maps`（已归档，`openspec/changes/archive/2026-10-13-retire-legacy-maps/`）：登记本 change（裁决 L6 = C、L17、L18）。
- `final-countdown`（已归档）：登记 W13。
- `ai-seat-vision`（已归档，2026-10-15）：本 change 的 R5 基线（`*-omniscient.txt` 与按座位视野的 5 条基线）由它钉入。
