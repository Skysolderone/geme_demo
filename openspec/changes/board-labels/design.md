## Context

- 坐标标注：`BoardView.BuildCoordinateLabels`（约 2108 行）在四边各画一排 `Label3D`：列字母在近边（`y = 0` 一侧）与远边（`y = height − 1` 一侧），行数字在左（`x = 0`）右（`x = width − 1`）两边，锚点由 `BoardGeometry.ColumnLabelAnchor` / `RowLabelAnchor` 给出。坐标标注做深度测试，揭示条目不做（`NoDepthTest = true`，渲染优先级 4），所以重叠时揭示盖在上面。
- 军势揭示：`BoardView.DrawReveals`（约 1500 行）读 `ShowMask.Reveals`（`RevealDisplay`，含锚格 `Coord`、结果停留进度 `ResultAgePermille` 等）。结果大字画在棋子头顶，算式小字画在结果上方（屏幕上更靠远边）。算式行颜色 `color.Lerp(Colors.White, 0.4f)`，描边 `RevealOutlineSize × 字号 / 结果字号`，字号下限 `RevealFormulaMinFontSize = 64`（约 1548 行）。
- 常驻标注：`BoardView.DrawGroupPower`（约 1796 行）画 `GroupPowerLabels.Of` 给出的标注，位置是锚格中心加 `(0.33, 0.12, 0.34)`（右前角），字色 `FactionColorOf(owner).Lightened(0.75f)`，描边色 `Darkened(0.45f)`，描边宽度 `NumberTierStyle.GroupLabelOutlineSize`（按字号比例）。
- 相机没有旋转绑定：远边恒在屏幕上方，左右不翻转（`viewport-camera`）。揭示条目从棋子头顶向屏幕上方升起，只可能压到远边的列字母和左右两边的行数字。
- `Ui.MutedText => InfoText with { A = 0.62f }`（`src/godot/scripts/Ui.cs`）。

## Goals / Non-Goals

**Goals**：揭示时坐标标注不与揭示条目叠字；算式小字与常驻标注在任何底色上读得清；标注不贴着行数字；`MutedText` 的透明度进 `UiTheme`。

**Non-Goals**：不移动揭示条目；不改揭示节奏与档位；不改坐标标注的位置与字号；不改落子飘字。

## Decisions

### D1 坐标标注让位：呈现层 `Siege.Presentation.Layers.CoordinateLabelYield`（新文件）

- 输入：`ShowMask`（只读 `Reveals`）、棋盘宽高。输出：`CoordinateYieldView`，含三张表 `FarColumns`（列下标 → 显现千分比）、`LeftRows`、`RightRows`（行下标 → 显现千分比）。不在表里的标注完全显示（1000‰）。近边列字母永不进表。
- 让位范围（初值，常量放在同一文件）：
  - 远边：锚格 `y ≥ height − FarRowReach`（`FarRowReach = 3`）时，`|x − 锚格 x| ≤ ColumnSpan`（`ColumnSpan = 2`）的远边列字母让位。算式行最长约 5 格宽，结果居中，左右各 2 列覆盖得住；实现后按截图调。
  - 左边：锚格 `x < SideColumnReach`（`SideColumnReach = 2`）时，左边 `锚格 y ≤ 行 ≤ 锚格 y + RowSpan`（`RowSpan = 3`）的行数字让位；右边对称（`x ≥ width − SideColumnReach`）。
- 显现进度：条目出现后的前 `FadeMs`（150 ms）内从 1000 线性降到 `YieldPermille`（150‰，不完全消失，坐标位置仍可辨），条目存续期间保持；条目消失时直接回到 1000（条目本身最后半程已在淡出）。多个条目同时让同一个标注时取最小值。进度只由遮罩里条目的已显示时长决定（`RevealDisplay` 已有的时间字段；若没有可直接用的"已显示毫秒数"，在 `ShowTimeline` 里补一个只读字段，配测试）。
- 纯函数、整数运算；无揭示条目时三张表都为空。
- 引擎层：`BoardView` 记住坐标标注节点（按"远边列 / 左行 / 右行 + 下标"索引），演出期间每帧按 `CoordinateYieldView` 设 `Modulate.A` 与 `OutlineModulate.A`；遮罩为空时全部复原。引擎层不判断重叠。

### D2 算式行对比

- 算式行颜色与结果相同（删掉向白色的混色）。
- 算式行描边宽度：`max(RevealFormulaMinOutline, 结果描边 × 字号 / 结果字号)`，`RevealFormulaMinOutline = 10`。
- 算式行字号下限 `RevealFormulaMinFontSize` 由 64 调到 72（与一档结果同字号）。
- 三个常量移到呈现层 `NumberTierStyle` 旁（`Style/NumberTiers.cs` 或新的 `Style/BoardLabelStyle.cs`），引擎层引用，便于测试断言下限。

### D3 常驻标注配色与描边

- 呈现层新增 `BoardLabelStyle.GroupLabelColors(PlayerId owner)` → `(Rgba Text, Rgba Outline)`：
  - 描边色 = 坐标标注描边色（18, 19, 23）。为此把 `Visuals.CoordinateLabel` / `CoordinateLabelOutline` 两个颜色搬到呈现层（`BoardLabelStyle`），`Visuals` 改为从呈现层取（值不变）。
  - 字色 = 阵营主色向白色按整数千分比提亮（`GroupLabelLightenPermille = 600`，比现在的 750 略低，保留更多阵营色相）；若某阵营亮度差不到 120，按该阵营单独上调提亮比例直到满足（在测试里钉住四个阵营的结果）。
- 描边宽度：`max(GroupLabelMinOutline, 现有按字号比例的值)`，`GroupLabelMinOutline = 12`。
- 引擎层 `DrawGroupPower` 只把 `Rgba` 转成 `Color` 再乘透明度，不再调用 `Lightened` / `Darkened`。

### D4 常驻标注换角

- `GroupPowerLabel` 加字段 `Corner`（枚举 `LabelCorner { FrontRight, FrontLeft }`）；`GroupPowerLabels.Of` 需要棋盘宽度来判断最右一列——从 `PowerLayerContent` 能取到宽度就取，取不到就加参数。锚格 `x == width − 1` 时 `FrontLeft`，否则 `FrontRight`。
- 引擎层：`FrontLeft` 时偏移 `(−0.33, 0.12, 0.34)`，`FrontRight` 时照旧。左前角同样在棋子底座半径之外，不与格心飘字相撞。
- 揭示条目的锚格仍用 `AnchorOf`，不受影响。

### D5 `MutedText`

- `UiTheme.MutedTextAlphaPermille = 620`；`Ui.MutedText` 由它算出透明度（引擎层做一次 `/ 1000f` 的换算，这是"翻译"不是另写数值）。
- hud-theme 的源码守门里补一条：`Ui.cs` 里 `MutedText` 的定义引用 `UiTheme.MutedTextAlphaPermille`，不含浮点字面量。

### D6 守门

- 单元测试：规格里的八个 Scenario（D1 的让位范围与进度、D2 的同色与下限、D3 的亮度差与描边色、D4 的换角、D5 的透明度），每条配变异。
- 源码守门：`DrawReveals` 不再出现 `Lerp(Colors.White`；`DrawGroupPower` 不再出现 `Lightened(` / `Darkened(`，标注颜色取自 `BoardLabelStyle`；坐标标注的透明度只取自 `CoordinateLabelYield` 的结果（引擎层不出现与揭示锚格比较坐标的代码）。
- hud-theme / hud-panels 的守门继续全绿。

### D7 画面验证

- 让位：需要一张远边棋串正在揭示、一张左右边棋串正在揭示的截图。用现有 `--reveal-preview` / `--reveal-at=` / `--shot-show=` 一类参数（见 `GameRoot.Reveal.cs` 与 HANDOFF 的参数清单）找到合适的局面与帧；`art/formation-tiers/reveal-real-late.png` 那一帧（第 9 大回合、远边金方揭示 56）是现成的问题样例，优先复现它。改前改后同帧对比。
- 常驻标注：对局中途一张（含黄方小标注与最右一列的标注）。
- 近、远两档缩放各看一次让位效果（`--overview` 下常驻标注不画，但揭示仍在）。
- 截图存 `art/board-labels/before|after/`。

### D8 段 B 之后的补充裁决（主会话，2026-10-06）

1. **换角的条件收窄**：段 B 检查发现，同一行 M、N 两列都有标注时，M 的标注在右前角、N 的标注换到左前角，两个数挤在 M、N 之间读成一个数（M11 = 1、N11 = 4 读成"14"），比原来贴着行数字更糟。改为：锚格在最右一列、且同一行左邻格不是另一条标注的锚格时才换到左前角；左邻也有标注时保持右前角（此时仍可能贴着行数字，但不会与邻格的数连读）。`GroupPowerLabels.Of` 先算出全部锚格再定角位。
2. **截图**：`after/side-reveal.png` 那一帧右侧条目刚出现、淡出尚未开始，证明不了"413"消除。补一组用 `--shot-show=power` 在同一帧取的改前改后对照（`side-reveal-show`）；原组保留并在实现记录里如实写明它拍到的是淡出前一刻。
3. **截图日志入库**：`art/board-labels/` 下与 png 同名的 `.log` 含取景自证行，照 `art/formation-tiers/` 的先例入库。

## 接口契约

- 呈现层给出：坐标标注的显现进度、标注颜色、角位、样式下限。引擎层只翻译与摆放。

## 已知歧义与建议裁决

主会话按常规判断定的初值，评审时可改：
1. 让位不完全消失，保留 150‰（D1），坐标位置仍隐约可辨。
2. 让位范围 远边 3 行 / 左右 2 列、横向 ±2 列、纵向 +3 行（D1），实现后按截图调，调整只改常量。
3. 常驻标注提亮从 75% 降到 60%（D3），换来更多阵营色相；以亮度差 ≥ 120 为硬约束。
4. 算式行字号下限 64 → 72（D2）。

## Risks / Trade-offs

- 让位范围按格子估算，不按屏幕像素算：极端缩放下可能多让或少让一两个标注。可接受，截图在近、远两档各验一次。
- 坐标标注每帧改透明度只在演出期间发生，遮罩为空时不碰，开销可忽略。
- 搬动 `Visuals.CoordinateLabel*` 两个颜色会动到坐标标注相关的现有测试或守门，值不变，按需同步。
- 本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"。
