## 1. 呈现层与测试（段 A）

- [x] 1.1 新增 `Layers/CoordinateLabelYield.cs`（D1）；新增 `Style/BoardLabelStyle.cs`（坐标标注两色、常驻标注配色与描边下限、算式行下限，D2 / D3）；`GroupPowerLabel` 加 `Corner`（D4）；`UiTheme.MutedTextAlphaPermille`（D5）。如需"条目已显示毫秒数"，在 `ShowTimeline` 补只读字段。验证：两处构建 0 警告；`Siege.Presentation` 零 Godot 依赖。
- [x] 1.2 规格场景测试（D6 第一条），每条配变异并记录红数；全量失败名单与改动前逐条相同。

## 2. Godot 接线、守门与截图（段 B）

- [ ] 2.1 坐标标注按让位进度画透明度（D1 引擎层部分）；遮罩为空时复原。
- [ ] 2.2 `DrawReveals` 算式行改用结果色与下限（D2）；`DrawGroupPower` 改用呈现层配色、描边下限与角位（D3 / D4）；`Visuals.CoordinateLabel*` 改为从呈现层取；`Ui.MutedText` 从 `UiTheme` 取（D5）。
- [ ] 2.3 源码守门（D6 第二条、D5 的守门）并配变异；hud-theme / hud-panels 守门继续通过。
- [ ] 2.4 画面验证（D7）：改前改后同帧对比存 `art/board-labels/before|after/`，逐张看过并写进实现记录；让位范围按截图调好。
- [ ] 2.5 收尾：两处构建 0 警告；全量失败名单与改动前相同；`--auto-demo` 帧数不变；`--pick-check` 通过。

## 3. 验收与文档（主会话）

- [ ] 3.1 `openspec validate board-labels --strict`；把 main 合进本分支后重跑 2.5。
- [ ] 3.2 设计文档 §20 补一句，变更记录升一版。
- [ ] 3.3 负责人看改前改后对比图。
