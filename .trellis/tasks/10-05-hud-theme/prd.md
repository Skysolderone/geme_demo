# 10-05-hud-theme

> 规格权威：`openspec/changes/hud-theme/`（proposal / design / specs / tasks）。

HUD 主题统一（负责人 2026-10-05）：范围取「HUD 主题统一」，视觉方向取「克制精修」。面板三级、按钮五态与三种语义加选中态、字号阶梯 13 / 14 / 15 / 17 / 19、间距阶梯 0 / 2 / 4 / 6 / 8 / 12、样式总览页 `--ui-gallery=`。只改 `Siege.Presentation` 的 `UiTheme` 与 `src/godot` 的 `Ui.cs` / `Hud*.cs`（加一个总览页脚本和启动参数）；不改布局、不改显示的内容，`Siege.Core` / `Siege.Sim` 与规则数值不动。

验收：`openspec/changes/hud-theme/tasks.md` 各条的验证项。

约束：
- 在工作树 `.claude/worktrees/hud-theme`、分支 `feat/hud-theme` 上做，基于本地 main `ccafe40`；业务改动由另一会话在主工作树做，不要碰主工作树，不要切分支。
- 同一时间只跑一个 dotnet / Godot；不跑批量对局。
- 取值放 `Siege.Presentation`（整数运算、零 Godot 依赖）并配规格场景测试与变异验证；引擎层不做亮度、对比度计算；引擎层的硬约束用源码扫描守门。
- 不用 Tween，不加动效，不引入 addon。`.claude/skills/godot-ui-control` 可作引擎 API 参考（示例是 GDScript，对应到 C#）；它与本文件或 openspec 冲突时以后者为准。
- 无人值守（零时长）下 `--auto-demo` 帧数不变；`--pick-check` 通过。
- 本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"。
- 本机构建 Godot 工程要带 `-p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config`；Godot 可执行文件在 `~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot`。拍图脚本 `art/hud-theme/shots.sh <工作树根> <输出目录>`。
- 改动前的基线截图在 `art/hud-theme/before/`（对局插旗阶段、地图选择、补给、结算，2026-10-05 本机拍）。

分段：A = tasks.md 第 1 组；B = 第 2 组；C = 第 3 组（主会话）。

## 实现记录

### 段 A（2026-10-05，呈现层取值与测试）

- `VisualBaseline.cs`：`UiTheme` 扩充（D1 全部取值、字号与间距阶梯）；新增 `PanelTier` / `ButtonKind` / `ButtonState`、`UiPanelStyle` / `UiEdge` / `UiButtonStyle`、`PanelStyleOf` / `ButtonStyleOf`。禁用盖过语义、选中、焦点不填充都在查表里实现。`BodyFontPx` 改为 `Body` 的别名，值仍为 15。
- 测试：新增 `面板分级Tests`（4）、`按钮状态与语义Tests`（11）、`字号与间距阶梯Tests`（2）。全量 2163 条 = 2143 通过 / 11 失败 / 9 跳过（改动前 2146 = 2126 / 11 / 9），失败名单逐条相同。
- 变异：实现方 86 条、检查方 8 条（6 条新）、主会话 1 条（悬停底色只亮 8 → 红 2），全部变红，还原后逐字节一致。
- 实现方自定：禁用的选中按钮保留 2 像素底边；选中只对默认语义有定义（其他语义抛异常）；焦点四边 1 像素金色、不带宽底边。
- 段 A 暴露的四处设计缺口由主会话裁决，写入 design D8。
