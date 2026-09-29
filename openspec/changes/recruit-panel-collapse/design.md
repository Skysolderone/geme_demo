## Context

`Hud.RefreshCenter` 在 `session.RecruitPanel` 非空、本机回合、`TurnStage.Recruit` 时构建中央征募面板；`H` 切换手牌信息面板（占同一中央位置）。

## Decisions

- **D1 状态模型放 Presentation**：`RecruitPanelCollapse`（Expanded / Collapsed），输入"进入新的征募阶段（按大回合 + 小回合序号识别）"时重置为展开；Godot 只读它决定画面板还是提示条。纯状态、可单测；Godot 侧用源码扫描守门确认经它取状态。
- **D2 按键 `V`**：未被占用（已占用 1–4、Tab、H、Enter、P、Esc、T、E、M、WASD、方向键、空格）。手柄取一个未占用键（实现时核对）。
- **D3 提示条**：顶部居中窄条，文字"征募（已收起）· 按 V 展开 · 展示 N · 还可免费选取 K"，可点击展开；不遮挡棋盘四边坐标标注。
- **D4 与 `H` 的关系**：手牌信息面板优先级不变；收起状态下按 `H` 打开手牌面板、再关闭后回到"收起"状态。

## Risks / Trade-offs

- 玩家收起后忘记还在征募阶段 → 提示条常驻顶部并带"按 V 展开"。
