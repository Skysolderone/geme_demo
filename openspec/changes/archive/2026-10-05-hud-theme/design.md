## Context

HUD 的样式分两层：取值在 `Siege.Presentation` 的 `UiTheme`（`Style/VisualBaseline.cs:455`，零 Godot 依赖，被 `UI风格约束Tests` 守着），控件工厂在 `src/godot/scripts/Ui.cs`。三个界面根节点（`Hud.cs:92`、`Hud.MapSelect.cs:135`、`Hud.Carry.cs:78`）各自 `Theme = Ui.BuildTheme()`，子节点继承。

现在 `BuildTheme` 只设字体和正文字号，按钮落到 Godot 默认主题上；层级、语义、字号、间距都是在 141 处工厂调用旁边逐处拼出来的。负责人裁决的方向是「克制精修」：样子不变，把层级和状态做出来。

改动前的四张基线截图在本机拍过（对局、地图选择、补给、结算），做完后在同一台机器上重拍对比。

## Goals / Non-Goals

**Goals**
- 取值只有一处（`UiTheme`），能被单元测试守住。
- 样式通过 Godot 的 `Theme` 按控件类型下发，HUD 脚本只说"这是什么"（哪一级面板、哪种语义的按钮、哪一级字号），不说"长什么样"。
- 悬停、按下这些截图拍不到的状态有办法验证。

**Non-Goals**
- 不改面板的锚点、大小与显示的内容。
- 不做界面动效，不用 Tween。
- 不做投影、渐变、高光。

## Decisions

### D1 取值表（初值）

全部放进 `UiTheme`，颜色用 `Rgba`。亮度按 `Rgba.Luma` 的整数算式。

**面板**

| 级 | 底色 | 亮度 | 边框 | 边框亮度 | 边框宽 | 圆角 |
|---|---|---:|---|---:|---:|---:|
| 主面板 | (18, 20, 26, 208) | 20 | (184, 146, 72) | 148 | 1 | 3 |
| 次面板 | (30, 34, 42, 192) | 33 | (96, 86, 62) | 86 | 1 | 3 |
| 提示条 | (18, 23, 31, 214) | 22 | 无 | — | 0 | 6 |

主面板就是现在的 `PanelFill` / `PanelBorder`，一个值都不改。提示条的底色就是回合摘要横幅现在内联写的那个颜色（`Hud.cs:121` 的 0.07 / 0.09 / 0.12 / 0.84 换成 8 位）。

**按钮（默认语义）**

| 状态 | 底色 | 亮度 | 边框 | 文字 |
|---|---|---:|---|---|
| 常态 | (38, 42, 52, 236) | 41 | (74, 78, 90) | `InfoText`（亮度 231） |
| 悬停 | (56, 61, 75, 240) | 61 | (74, 78, 90) | `InfoText` |
| 按下 | (24, 27, 34, 240) | 26 | (74, 78, 90) | `InfoText` |
| 禁用 | (30, 32, 38, 150) | 32 | (52, 54, 60) | (132, 130, 124)（亮度 129） |
| 焦点 | 不填充，只画一圈 1 像素的 `PanelBorder` 金色 | | | |

边框宽 1、圆角 3、左右内边距 10。高度仍是 `ButtonHeightPx` = 30。

**语义与选中**

| | 边框 | 文字 | 底色 |
|---|---|---|---|
| 主操作 | `PanelBorder` 金色 | `PanelBorder` 金色（亮度 148） | 同默认各态 |
| 危险操作 | `DangerText`（236, 96, 80） | `DangerText`（亮度 136） | 同默认各态 |
| 选中 | 三边同默认；底边 2 像素 `PanelBorder` | `PanelBorder` 金色 | 同默认各态 |

禁用时一律用上表"禁用"那一行，不看语义。

核算：悬停 − 常态 = 20，常态 − 按下 = 15（都 ≥ 12）；默认文字对常态 190、对悬停 170（≥ 160）；主操作文字对悬停 87、危险文字对悬停 75（≥ 70）；禁用文字对禁用底 97（≥ 40）。

**字号阶梯**：`Caption` 13、`Small` 14、`Body` 15、`Title` 17、`Banner` 19。`BodyFontPx` 保留为 `Body` 的别名（现有测试在用）。

**间距阶梯**：`Space0` 0、`Space1` 2、`Space2` 4、`Space3` 6、`Space4` 8、`Space5` 12。

### D2 现有取值怎么并

| 现有 | 处数 | 并入 |
|---|---:|---|
| `BodyFontPx - 2`（13） | 15 | `Caption` |
| `BodyFontPx - 1`（14） | 9 | `Small` |
| `BodyFontPx + 1`（16） | 1（`Hud.Carry.cs:150` 补给点一行） | `Title`（17） |
| `BodyFontPx + 2`（17） | 5 | `Title` |
| `BodyFontPx + 4`（19） | 6 | `Banner` |
| 间距 1 | 2（`Hud.cs` 的 `_rankBody` 与 `AddScroll` 内层列表；初稿误写为 3，段 B 实数） | `Space1`（2） |
| 面板内边距 3（`Hud.cs:302` 征募收起条） | 1 | `Space2`（4） |
| 其余间距与内边距 0 / 2 / 4 / 6 / 8 / 12 | — | 原值对应的那一级 |

会动到像素的只有三类：16 → 17（一行字高 1 像素）、间距 1 → 2（两个列表每行多 1 像素）、征募收起条内边距 3 → 4（条高 38，按钮 30 加上下各 4 正好放下）。实现后在截图上逐个确认没有文字被裁掉、面板没有被撑出锚定的框。

势力栏到账时的放大字号（`Hud.RankFontPx`，tiered-number-show）是演出，由呈现层按档位算，不进阶梯。

### D3 面板归级

| 级 | 面板（创建处） |
|---|---|
| 主面板 | 回合状态（`Hud.cs:191`）、势力排名（`:220`）、手牌（`:234`）、行动（`:255`）、预演（`:263`）、中央面板（`:287`，征募 / 手牌信息 / 结果共用）、地图选择（`Hud.MapSelect.cs:140`）、补给（`Hud.Carry.cs:83`）、结算（`:112`）、弃赛确认（`:121`） |
| 次面板 | 行动顺序条（`Hud.cs:210`）、信息层按钮条（`:245`）、信息层说明（`:275`）、征募收起条（`:302`）、对局中的带入栏（`Hud.Carry.cs:92`） |
| 提示条 | 回合摘要横幅（`Hud.cs:114`） |

判断口径：玩家在上面读主信息或做决定的是主面板；承载一排按钮或一段说明文字的是次面板；临时出现、压在棋盘上的一行字是提示条。

### D4 引擎层怎么下发

- `Ui.BuildTheme()` 在返回的 `Theme` 上按类型写样式盒与颜色：
  - `Button`：`normal` / `hover` / `pressed` / `disabled` / `focus` 五个样式盒，`font_color` / `font_hover_color` / `font_pressed_color` / `font_disabled_color` 等字色，`font_size`。
  - 三个主题变体（`Theme.SetTypeVariation(名字, "Button")`）：主操作、危险操作、选中。按钮用 `ThemeTypeVariation` 指过去。`OptionButton` 在 Godot 里沿类继承链回落到 `Button` 的主题项，不必另写；实现时在总览页或补给界面的截图上确认下拉框确实跟了。
  - `PanelContainer`：`panel` 样式盒为主面板；次面板、提示条各一个主题变体。
  - `LineEdit`：`normal` / `focus` / `read_only` 用按钮常态 / 焦点 / 禁用的取值。
  - `HSeparator`：线色用次面板的边框色。
- 工厂签名：
  - `Ui.Panel(PanelTier tier, int padding, int separation)`：`padding` 与 `separation` 只接受间距阶梯的值（传 `UiTheme.SpaceN`）。面板内边距仍是逐面板不同的，所以样式盒按"级别 + 内边距"生成；同一组合可以复用同一个样式盒对象。
  - `Ui.Action(string text, ButtonKind kind = Default, int minWidth = 0)`。
  - `Ui.Toggle(string text, bool active, int minWidth = 0)`：选中时指到"选中"变体。
  - `Ui.Text(string text, Color? color, int? size, bool wrap)` 的 `size` 只传阶梯里的名字。
  - 新增 `Ui.Banner(...)`（或给 `Ui.Text` 加描边参数）：把回合摘要横幅、演出横幅的描边与提示条样式盒收进工厂。
- `PanelTier`、`ButtonKind` 两个枚举放在 `Siege.Presentation.Style`，和 `UiTheme` 在一起；按"语义 + 状态"取一组颜色的查表函数也放在呈现层（这样禁用盖过语义这类规则能被单元测试直接断言）。引擎层只把查到的值翻成 `StyleBoxFlat`。
- 三个根节点仍各自调 `BuildTheme()`；内部可以缓存同一个 `Theme` 对象，也可以每次新建，行为相同。

### D5 HUD 脚本的改法

- 6 处逐处改按钮字色的调用换成语义参数或 `Ui.Toggle`：`Hud.MapSelect.cs:83`、`:112`（选中）、`:239`（主操作）、`Hud.Carry.cs:102`、`:251`（危险）、`:200`（主操作）。
- 回合摘要横幅的内联样式盒（`Hud.cs:118–131`）与两处描边覆盖（`:116–117`、`:147–148`）移进工厂。
- `Hud.cs:186` 给回合摘要设阵营色字（`color.Lightened(0.35f)`）是内容，不是主题，保留。
- `Hud.cs:379` 的 `new Color(1f, 1f, 1f, alpha)` 是整体透明度，改写成 `Colors.White with { A = ... }`，这样"HUD 脚本里没有写死的颜色"的守门不用开例外。
- 地图选择里按钮文字开头的 ● / ○ 是内容，保留；它和选中变体的金色底边并存。

### D6 样式总览页

- 照 `PieceGallery`（`--piece-gallery=<PNG 路径>`）的先例做一个 `UiGallery` 节点和 `--ui-gallery=<PNG 路径>` 参数：不建局，摆好一页后截图退出。
- 内容：三级面板各一个（里面放一行标题和一行正文）；三种语义 × 五种状态共十五个按钮样例；一对选中 / 未选中的可切换按钮；五级字号各一行。
- 悬停、按下、焦点不能靠模拟鼠标得到，样例用 `Panel` 加 `Label` 画：`Panel` 的样式盒从根节点的主题里按（类型或变体，状态名）取出来（`Theme.GetStylebox`），字色同理（`Theme.GetColor`）。这样样例和真按钮用的是同一个对象，不会另写一套。常态、禁用两列同时再放一个真的 `Button`，用来确认"取出来画的"和"真按钮自己画的"一致。
- 页面标注文字（状态名、语义名）用 `Ui.Text`。
- 截图失败、文件没生成或日志里有异常时，无人值守下以非零退出码结束（`.trellis/spec/core/testing.md`「表现层的画面与读数验证」）。

### D7 守门

呈现层单元测试（`tests/Siege.Core.Tests/VisualStyleBaseline/`，每条配变异记录）：
- 面板分级：四个场景里能落到取值上的三条（灰度可辨、提示条无边框、主面板不变）。
- 按钮状态与语义：悬停与按下、读得清、禁用仍可读、禁用盖过语义、选中不只靠颜色。
- 两条阶梯的取值与严格递增。
- 现有「信息密度」不动，继续通过。

引擎层源码扫描守门（`PresentationFixtures.GodotScriptCode` 去注释后扫，每条配"注入一处违例会红"的变异）：
- `Hud.cs` / `Hud.MapSelect.cs` / `Hud.Carry.cs` 与总览页脚本里没有 `new StyleBoxFlat`、没有带数字的 `new Color(`、没有 `AddThemeStyleboxOverride(`、没有 `AddThemeFontSizeOverride(`。
- `src/godot/scripts` 全部脚本里没有 `BodyFontPx` 后面跟加减号的写法。
- 上述三个 HUD 脚本里，`AddThemeConstantOverride("separation"` 的第二个参数不是数字字面量。
- 每一处 `Ui.Panel(` 调用的第一个参数是 `PanelTier.`。
- 扫描正则不以 `\b` 开头匹配复合标识符（同一份规范里的教训）。

画面验证：`--ui-gallery` 一张；对局、地图选择、补给、结算四张与改动前的基线并排看。

### D8 段 A 之后的补充裁决（主会话，2026-10-05）

段 A 实现时提出四处设计缺口，主会话按常规判断定如下：

1. **选中态的底边与其余三边不同色，`StyleBoxFlat` 只有一个边框色。** 引擎层做一个 C# 的 `StyleBox` 子类，重写 `_Draw`：先用一个按 `UiButtonStyle` 翻好的 `StyleBoxFlat`（三边色、宽 1、圆角 3）画底，再在底部叠一条底边色、宽 `SelectedEdgeWidthPx` 的矩形。只有选中变体用它，其余各态仍是 `StyleBoxFlat`。规格不改。如果 Godot 4.7.2 的 C# 下 `StyleBox._Draw` 覆写不生效，退回"四边金色、底边宽 2"，在实现记录里写明并告知负责人（规格的"底边 2、其余 1"仍成立）。
2. **按钮上下内边距**：`UiTheme` 补 `ButtonPaddingYPx = Space2`（4）。按钮高度仍由 `ButtonHeightPx` = 30 的最小尺寸撑开。
3. **提示条内边距**：回合摘要横幅现在的 16 / 3 / 4 不在间距阶梯上，改为左右 `Space5`（12）、上下 `Space2`（4），放进 `UiTheme`（`HintPaddingXPx` / `HintPaddingYPx`）。横幅左右各窄 4 像素、高度多 1 像素。
4. **演出字号**：演出横幅的 `BodyFontPx * 3`（`Hud.cs:144`）改为呈现层常量 `UiTheme.ShowBannerPx = 45`；势力栏到账的 `Hud.RankFontPx`（`Hud.cs:507`）是 tiered-number-show 的按档放大，属于规格里写明不受字号阶梯限制的演出字号，算式不动。D7 的字号扫描因此改为：`src/godot/scripts` 里 `BodyFontPx` 后面跟 `+`、`-`、`*` 的写法只允许出现在 `RankFontPx` 的方法体里（守门用 `MethodBody` 取出该方法体后从扫描文本中剔除），其余位置一律算违例。

## 接口契约

- `UiTheme`（呈现层）是取值的唯一来源；`Ui`（引擎层）是把取值变成 Godot 对象的唯一位置；`Hud*.cs` 只调 `Ui`。
- 呈现层不引用 Godot；引擎层不做亮度、对比度之类的计算。

## 已知歧义与建议裁决

以下都是主会话按常规判断定的初值，评审时可改，改动只落在 `UiTheme` 或 D3 的表上：

1. 具体色值与阈值（D1）。阈值写进了规格，色值没有。
2. 16 号并入 17 而不是 15（D2）：那一行是补给界面的"补给点 / 库存"汇总，本来就是要比正文醒目。
3. 间距 1 并入 2、内边距 3 并入 4（D2）。
4. 面板归级（D3），尤其"信息层说明"和"带入栏"归次面板。
5. 总览页用带路径的参数形式，不做"不带路径时停留供人查看"（D6）。要人工看悬停手感，直接进对局即可。

## Risks / Trade-offs

- **默认按钮从 Godot 自带样式换成自定义样式**，所有按钮的外观都会变一点（底色、边框、圆角）。这是本次的目的，但也意味着四张对比截图上每个按钮都不同，要逐张过目。
- **主题变体名写错不会报错**，按钮会静默落回默认样式。对策：变体名定义成 `Ui` 里的常量，只在一处写字符串；总览页上三种语义并排，写错一眼可见。
- **源码扫描守门钉在写法上**，以后重构 HUD 脚本要同步改测试。
- **与业务会话的冲突面**：`Hud.cs` 改动处多但都是参数级的小改。业务会话如果同时改 `Hud.cs` 里的文案或数值显示，合并时会有逐行冲突，内容上不相干，手工合并即可。合入 main 前先把 main 合进本分支再跑一遍验收。
- 本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"。
