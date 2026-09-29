# 征募面板可收起 · 人工检查清单

change `recruit-panel-collapse` 1.3。自动化已钉住：状态模型（`tests/Siege.Core.Tests/HandInfoPanel/征募面板可收起Tests.cs`）、
Godot 侧接线 / 按键不冲突 / 提示条不压左侧信息层面板与顶部条（同文件的源码扫描）。画面是否"看得全场"要看图。

出图命令（不能加 `--headless`；到达截图帧后等本机玩家下一次征募、自动演示已选一枚时截，先展开、再经与 V 键同一入口收起）：

```bash
G="D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
"$G" --path src/godot -- --auto-demo --shot-recruit-collapsed "--screenshot=E:/wws/geme_demo/art/recruit-panel-collapse/recruit-panel.png:30"
"$G" --path src/godot -- --auto-demo --map=siege-frontier-v2 --shot-recruit-collapsed "--screenshot=E:/wws/geme_demo/art/recruit-panel-collapse/frontier-recruit-panel.png:30"
```

| 图 | 局面 |
|---|---|
| `recruit-panel.png` / `recruit-panel-collapsed.png` | 缺省图 v5，第 3 大回合红方征募：展示 5、已选 1、还可选 2 |
| `frontier-recruit-panel.png` / `frontier-recruit-panel-collapsed.png` | 边疆图，同上（一屏看不全，只有部分坐标标注在画面内） |

控制台取景自证（`[recruit-collapse]` 行）：收起图提示条 (540, 96) 520×38；坐标标注落在提示条（外扩 20 像素）内 0 个（v5 画面内 52/52，边疆图 39/110）。
数像素（v5 那一对逐像素差分，阈值 8）：中央面板框与提示条框 100% 变化，左侧 x<400、右侧 x>1300、底部 y>700 为 0；
另有 294 个零散像素（x 877–981、y 626–646，平均差 3.9）在面板下缘之外——推断为 `Visuals` 里随 `TIME` 流动的水面 / 瀑布着色器（取图相隔一帧），未看图确认。

## 逐项核对

- [ ] 展开图：中央征募面板与改动前一致，底部一行"完成征募，进入部署"旁多一个"收起 [V]"按钮。
- [ ] 收起图：中央面板消失，棋盘中央区域完整可见。
- [ ] 收起图：顶部居中一条窄提示条，文字"征募（已收起）· 按 V 展开 · 展示 5 · 还可免费选取 2"，在行动顺序条与通知行之下。
- [ ] 提示条不压棋盘四边坐标标注，不压左侧信息层面板位置（x ≤ 392）与右上势力排名。
- [ ] 两张图除面板 / 提示条外局面相同（同一帧、同一相机位姿）。

## 手动试玩（截图覆盖不到的交互）

- [ ] 征募阶段按 V（手柄 Back 键）收起，再按 V 或点提示条展开；已选与剩余选取数不变。
- [ ] 收起期间推屏（WASD / 方向键 / 贴边）、滚轮缩放、空格回家、M 全局预览、1–4 信息层都照常。
- [ ] 收起状态下按 H 打开手牌信息面板，再按 H 关闭 → 回到提示条（不是展开的面板）；手牌面板开着时按 V 无反应。
- [ ] 完成征募进入下一小回合的征募阶段，面板以展开出现。
