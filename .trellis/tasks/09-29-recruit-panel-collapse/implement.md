# 09-29-recruit-panel-collapse 实施记录

规格：`openspec/changes/recruit-panel-collapse/`（tasks 1.1–1.4 全部完成）。不做倒计时。Core / Sim 未改，终端不变。

## 改动

| 文件 | 内容 |
|---|---|
| `src/Siege.Presentation/Hand/RecruitPanelCollapse.cs`（新） | `RecruitPhaseKey(MajorRound, MinorRound)` + `Of(MatchPublicView, me)`（只认本机玩家的征募阶段，小回合 = 行动顺序位置 1 起）；`RecruitPanelCollapse`：`Sync` 换阶段即重置展开、`Toggle(handPanelOpen)` 非本机征募阶段或手牌面板开着时无效、`Present` 原样透传面板并给出提示条文案（D3） |
| `src/godot/scripts/InputBindings.cs` | `RecruitCollapseAction`：键鼠 `V`、手柄 `JoyButton.Back`（此前均未占用） |
| `src/godot/scripts/Hud.cs` | 独立的顶部提示条 `_recruitBar`（`Anchor(0.5, 0, -260, 96, 260, 134)`，整条可点）；征募面板底行加"收起 [V]"；`RefreshCenter` 经 `Present` 决定面板 / 提示条，`BuildRecruit(view.Panel)`；`Refresh` 多一个 `RecruitPanelCollapse` 参数；`CenterPanelOpen` 语义不变 |
| `src/godot/scripts/GameRoot.cs` | `ToggleRecruitCollapse`（V、收起按钮、点提示条同一入口：先 `Sync` 再 `Toggle`）；`RefreshViews` 前 `Sync`；`--shot-recruit-collapsed`（须与 `--auto-demo`、`--screenshot=` 同用，否则退出码 1）：到截图帧后等本机征募且已选一枚，先截展开、再收起截 `-collapsed`，打印 `[recruit-collapse]` 取景自证行（坐标标注投影落在提示条内的个数） |
| `tests/Siege.Core.Tests/HandInfoPanel/征募面板可收起Tests.cs`（新） | 12 条：四个 Scenario + 阶段标识 / 重置 / 无效切换 / D4 + 4 条源码扫描守门（Core/Sim/存档/对局驱动不含收起状态 + Sim IL；Hud 经模型、手牌面板判断先于征募；V 与手柄键不冲突；提示条不压信息层面板 / 顶部条 / 排名面板） |
| `art/recruit-panel-collapse/` | 4 张截图 + README 人工检查清单 |

## 验证

- 1.1 先红：模型为抛 `NotImplementedException` 的桩时 12/12 红（扫描类红在"反面命中 / 找不到锚点"，Core/Sim 泄漏扫描本身未误报）。实现后 12/12 绿。
- 1.2 变异（脚本二进制读写、锚点命中恰 1、`finally` 还原、与原文逐字节比对、`os.utime`；跑 HandInfoPanel 命名空间 25 条）：
  - M-RC1 `Sync` 删 `IsCollapsed = false` → 红 3（新征募阶段默认展开、换一个征募阶段即重置、不在本机征募阶段时收起键无效）
  - M-RC2 `Present` 透传已选全清的副本 → 红 1（展开后状态不变）
  - 还原后 25/25 绿，EXIT 0。
- 1.3 Godot（Debug 构建 0 警告）：`--headless -- --auto-demo` EXIT 0、`--auto-demo --pick-check` EXIT 0（105/105，7 位姿失败 0）。另把 3 个 Godot 脚本临时换回 HEAD 构建跑同两条命令作基线，日志去掉 `[perf]` 耗时后逐行相同；随后逐字节还原、刷新 mtime、重建 Debug。不带新选项的 `--auto-demo --screenshot=…:20`（窗口模式）EXIT 0，仍在第 20 帧取图、无 `[recruit-collapse]` 行（无头模式下同一命令 300 s 未退出，已手动结束；推断是等不到 `FramePostDraw`——该路径本次未改，但未在 HEAD 上复现确认）。截图两对：v5 与边疆图，提示条 (540, 96) 520×38，坐标标注落在提示条内 0 个。
- 1.4 `dotnet build siege.sln` 0 警告；`dotnet build src/godot/Siege.Godot.csproj` 0 警告；`dotnet test -c Release` 1818 通过 / 6 跳过 / 0 失败（EXIT 0）；`openspec validate recruit-panel-collapse --strict` 通过。未跑任何批量对局。

## 待决

- 截图未读进上下文，需负责人按 README 清单过目。
- 手柄键取 `Back`（View / Select），如需别的键再改。
- v5 那对图有 294 个零散差分像素落在面板下缘之外，归因为 `TIME` 着色器是推断，未看图确认。
- `--map-select` / 补给预览与 `--shot-recruit-collapsed` 同给的组合未处理（选图阶段的截图分支不经 `RecruitShotReady`，会在选图界面上走一遍收起并以 1 退出）；不在本任务范围，未改。
