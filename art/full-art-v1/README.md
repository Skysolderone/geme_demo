# 浮空国度 · 基础版美术资源 v1

这是一套已经接入游戏的完整可运行资源，覆盖天空背景、棋盘地形、十种棋子、四方阵营表现、手牌图标和 HUD。主视觉是暖日光下的浮空石岛：浅青天空、砂岩与苔绿棋盘、青碧水系、旧金属镶边和石雕棋子。

**版本定位：美术基础版。** 基线标识为 `art-base-v1`，对应游戏 `v0.5.0` 的美术资源提交 `c490828`。后续美术迭代另起版本，保留这一版作为效果与资源范围的对照。

## 运行与查看

- 直接运行 `build/windows/Siege.exe`；同目录的 `Siege.pck` 必须一起保留。
- 在 Godot 中打开 `src/godot/project.godot` 可编辑场景和材质。
- `overview.png` 是四方对局全景；`pieces-gallery.png` 展示十种棋子；`map-select.png`、`opening.png`、`recruit.png`、`recruit-collapsed.png` 展示主要界面。
- `cliff-near.png`、`river-near.png`、`forest-near.png` 是地形近景；`staged-preview.png` 展示匠人暂放与落点提示；`four-factions.png` 展示密集对局。

## 资源清单

| 类别 | 内容 | 位置 |
| --- | --- | --- |
| 天空 | 全新宽幅浮岛背景原画，2109 × 745 | `src/godot/art/sky/floating-realm-panorama.png` |
| 地形 | 75 个 Godot 场景、21 个共享材质，覆盖棋盘、边缘、河流、桥、树、岩、遗迹、栅栏、浮岛等 | `src/godot/parts/terrain_full_art/` |
| 棋子 | 10 个可编辑 Godot 场景，各有独立轮廓与材质槽；运行时按阵营着色 | `src/godot/parts/pieces/` |
| 界面 | 石墨蓝面板、旧金边框、阵营徽记、按钮状态与十种手牌轮廓图标 | `src/godot/scripts/Ui.cs`、`Hud*.cs`、`PieceIcon.cs` |
| 生成源 | 棋子参数化生成器、地形制作说明、天空提示词与界面规范 | `art/full-art-v1/source/` |

## 验收

Windows 导出包已从当前源码重建；导出包实机截图可见新天空和新棋盘，地形资源加载无程序回退，棋子图库加载 **10/10**。`dotnet build` 为 0 警告、0 错误；VisualStyleBaseline 测试通过 **27/27**。暂放棋子采用发光实体轮廓与独立落点高亮，以保证棋形清楚。

背景原画使用内置 imagegen 生成。提示词摘要与来源记录见 `source/sky-background.md`；地形和棋子可从项目内源文件继续编辑。
