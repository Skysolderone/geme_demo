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

### 段 B（2026-10-05，Godot 接线、总览页与源码守门）

- `Ui.cs` 重写：`BuildTheme` 按类型下发按钮五态与字色、三个按钮变体、面板 级别 × 6 档内边距 = 18 个变体（面板只设变体，脚本里没有样式盒覆盖）、提示条与演出横幅两个 Label 变体、`LineEdit`、`HSeparator`；变体名只在 `Ui` 里写一处。新增 `EdgeStripBox : StyleBox` 画选中底边（D8-1 方案生效，未用退路；像素读数：选中按钮底边两行金色、三边中性色）。工厂：`Panel(tier, padding, separation)`、`Action(text, kind, minWidth)`、`Toggle`、`Select`、`HintBanner`、`ShowBanner`。
- `UiTheme` 补 D8 的四个常量，另搬入原写死在 `Hud.cs` 的描边色与描边宽（`TextOutline`、`HintOutlinePx` 6、`ShowBannerOutlinePx` 8，值不变）。
- HUD 三个脚本只改调用处：面板归级 15 处（主 10、次 5）加回合摘要提示条 1 处；字号替换与 D2 逐处一致；间距 14 处全部取阶梯名；按钮语义 6 处。布局、文案、`Hud.RankFontPx` 算式、回合摘要阵营色字未动（检查方逐处核对）。D2 的"间距 1 有 3 处"实为 2 处，已更正 design。
- 总览页 `UiGallery.cs` + `--ui-gallery=<PNG 路径>`：样例从主题取样式盒画，常态 / 禁用列与真按钮比对 0 像素差；无头、写盘失败、拼错参数都以退出码 1 结束。
- 源码守门：`HudScriptScan.cs` 加四组扫描（面板级别、按钮语义、字号与间距、总览页同源）。实现方 28 条变异全红；检查方 15 条（含 7 条绕过尝试，其中 6 条修前 0 红，已补规则，修后全红）；主会话 1 条（补给界面绕过工厂直接设变体名 → 红 2）。
- 回归：全量 2167 = 2147 通过 / 11 失败 / 9 跳过，失败名单与基线逐条相同；两处构建 0 警告；`--auto-demo` 改前改后 53 帧；`--pick-check` 105/105 通过。
- 画面：`art/hud-theme/after/` 五张。对局、地图选择、补给、结算与 before 对比无新增裁字或溢出；补给面板因 16 → 17 号长约 2 像素。对局「开局插旗」面板末行被裁是改动前就有的问题，按"不改布局"未动。回合摘要横幅新内边距与征募收起条不在截图场景里，只经代码核对。
- 遗留（未做）：`Ui.MutedText` 的 `A = 0.62f` 是 hud-theme 之前就有的、唯一未进 `UiTheme` 的样式值；扫描挡不住 `using static UiTheme` 后的裸名运算、`LabelSettings.FontSize`、经局部变量传字号；新规则禁止 HUD 脚本里首参为字面量的目标类型 `new(…)`（含 `Vector2`），业务改动需写明类型。
