## 1. 实现

- [x] 1.1 先写测试：`RecruitPanelCollapse` 状态模型（切换、按新征募阶段重置、切换不改征募面板数据）与 `hand-info-panel`「征募面板可收起」四个 Scenario 中可单测部分；源码扫描守门（Hud 经状态模型决定收起、收起状态不出现在 Core / Sim / 存档代码）。验证：先红。
- [x] 1.2 实现 D1–D4：Presentation 状态模型；Godot `InputBindings` 注册 `V` 与手柄键、`Hud` 提示条与"收起"按钮、`GameRoot` 按键处理；截图选项 `--shot-recruit-collapsed`。验证：1.1 转绿；变异——新阶段不重置（"新征募阶段默认展开"应红）、切换时清空已选（"展开后状态不变"应红），逐项应红，还原逐字节校验并刷新 mtime。
- [x] 1.3 Godot（Debug 构建）：`--auto-demo`、`--pick-check` 退出码 0（行为不变）；用截图选项出两张图（展开 / 收起，均在征募阶段）落 `art/recruit-panel-collapse/`，交负责人过目。
- [x] 1.4 全量回归：两处 `dotnet build` 零警告；`dotnet test -c Release` 全绿；`openspec validate recruit-panel-collapse --strict` 通过。不跑任何批量对局。
