## 1. 呈现层与测试（段 A）

- [x] 1.1 新增 `src/Siege.Presentation/Hud/HudPanelRows.cs`：`RankRow` / `OrderBar` / `HandRow` 与三个视图记录（design D1）；`UiTheme` 加 `ViewerMarkWidthPx`。不改 `Layers/LayerContents.cs`、`Show/SettlementBeats.cs` 等现有文件。验证：两处构建 0 警告；`Siege.Presentation` 仍不引用 Godot。
- [x] 1.2 `tests/Siege.Core.Tests/VisualStyleBaseline/信息面板分列Tests.cs`：规格里能落到呈现层的场景，"信息不减少"用独立算式（D6）。验证：每条断言配变异并记录红数，还原后逐字节一致；全量测试失败名单与改动前逐条相同。

## 2. Godot 摆放与守门（段 B）

- [x] 2.1 `RefreshRank` 改为 `GridContainer` 六列（D2），演出期间的放大与增量照旧。验证：中途截图与演出截图上各行对齐、自家有竖条、无溢出。
- [x] 2.2 `RefreshOrderBar` 改为按钮内的徽记条（D3），点击仍切换顺序层。验证：截图上当前行动者有金字与底边；`--pick-check` 通过。
- [x] 2.3 `RefreshHand` 改为按钮内左右两列（D4）。验证：截图上名称靠左、数量靠右，选中有金色底边。
- [x] 2.4 插旗提示自适应高度（D5）。验证：v5（不带推屏提示）与边疆图（带推屏提示）两张插旗截图上没有被裁的行。
- [x] 2.5 源码守门（D6 第二条）并配变异；`hud-theme` 的守门继续通过。
- [x] 2.6 画面验证（D7）：改前改后各一套存 `art/hud-panels/before|after/`，逐张看过并写进实现记录。
- [x] 2.7 收尾：两处构建 0 警告；`dotnet test` 失败名单与改动前相同；`--auto-demo` 帧数不变；`--pick-check` 通过。

## 3. 验收与文档（主会话）

- [x] 3.1 `openspec validate hud-panels --strict` 通过；把 main 合进本分支后重跑 2.7。
- [x] 3.2 设计文档 §20 补一句，变更记录升一版。
- [x] 3.3 负责人看改前改后对比图。
