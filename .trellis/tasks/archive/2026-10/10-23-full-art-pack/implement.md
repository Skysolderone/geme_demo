# 实施与验证

1. 盘点全部玩家可见入口，固定资源覆盖清单与新美术语言。
2. 并行制作十棋子、完整地形资源、HUD/图标；主线制作背景、棋盘、光照、水与标记。
3. 构建 Godot 并生成多个阶段的实机截图；逐项检查新资源是否真的加载，而非继续显示旧画面。
4. 检查远近景与灰度判读、预演透明材质、缺资源回退、格心遮挡与拾取。
5. 运行视觉相关测试和构建；只修本次改动导致的问题。
6. 整理 `art/full-art-v1` 的覆盖清单、源文件、截图和已知不足。

## 文件边界

- 棋子代理：`PieceModels.cs`、`parts/pieces/**`、`source/pieces*`。
- 地形代理：`TerrainParts.cs`、`LowPoly*.cs`、`parts/terrain_full_art/**`、`source/terrain*`。
- UI 代理：`Ui.cs`、`Hud*.cs`、`Emblems.cs`、`EmblemIcon.cs`、`UiTheme`。
- 主线：`BoardView.cs`、`Visuals.cs`、背景图与整体验收。
