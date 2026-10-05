# 10-05-hud-panels

> 规格权威：`openspec/changes/hud-panels/`（proposal / design / specs / tasks）。

信息面板重排版（负责人 2026-10-05 裁决为样式工作的下一项）：势力排名按列对齐并标出自家一行，行动顺序改成带徽记的条并用"金字 + 金色底边"标当前行动者，手牌名称与数量分列，修「开局插旗」提示的裁字。按列文字由呈现层新文件 `Hud/HudPanelRows.cs` 给出，引擎层只摆放。显示的信息不增不减；规则、计分、存档、日志不变。

验收：`openspec/changes/hud-panels/tasks.md` 各条的验证项。

约束：
- 在工作树 `.claude/worktrees/hud-panels`、分支 `feat/hud-panels` 上做，基于 main `4066c6a`；不要碰主工作树，不要切分支。
- 另一会话的 `formation-tiers` 正在改 `src/Siege.Presentation/Layers/LayerContents.cs`、`Show/SettlementBeats.cs`、`Style/NumberTiers.cs`、`Preview/PreviewPresentation.cs`、`src/godot/scripts/GameRoot.Reveal.cs` 与 `UI风格约束Tests.cs` 等。本任务 MUST NOT 修改这些文件，只读。
- `hud-theme` 的源码守门继续生效：HUD 脚本里不写死颜色、字号加减、间距数字、样式盒覆盖，不用目标类型 `new(…)`，按钮语义走 `Ui` 工厂。
- 同一时间只跑一个 dotnet / Godot；不跑批量对局；不用 Tween。
- 本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"。
- 本机构建 Godot 工程要带 `-p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config`；Godot 在 `~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot`。拍图脚本先例 `art/hud-theme/shots.sh`。

分段：A = tasks.md 第 1 组；B = 第 2 组；C = 第 3 组（主会话）。

## 实现记录

### 段 A（2026-10-05，呈现层与测试）

- 新增 `src/Siege.Presentation/Hud/HudPanelRows.cs`：`RankRow` / `OrderBar` / `HandRow` 与 `RankRowView`（多 `DeltaIsNegative`）/ `OrderBarView` + `OrderBarEntry` / `HandRowView`（多 `Type`）；`UiTheme.ViewerMarkWidthPx = 3`。禁改清单里的文件未动。
- 主会话补充裁决 D9：有状态的行状态在前、拆分在后（初版丢了拆分，违反信息不减少）；演出未开始明细为空；对局至多 4 人，截图改用边疆图；顺序条保留"行动顺序"标题；演出中状态文字拆出来保持次要色（后两条由段 B 做）。
- 测试 `信息面板分列Tests`：17 条（实现方 16 + 检查方补 1）。全量 2184 = 2164 通过 / 11 失败 / 9 跳过，失败名单与改动前逐条相同。
- 变异：实现方 34 条全红；检查方 6 条，其中 3 条补测试前没抓住（`DeltaIsNegative` 去掉增量门、零增量算负、名次不变也给提示），补测试后全红。
