## Context

三处面板的现状（`src/godot/scripts/Hud.cs`，main `4066c6a`）：

- `RefreshRank`（约 403–476 行）：每名玩家一个 `HBoxContainer`，里面是徽记加一个整行 `Label`（`$"{rank}　{faction.Name}　{row.CompactText}{suffix}"`）。演出期间（`mask.Power` 里有该玩家）拆成几个 `Label`：前缀、滚动数字（字号 `RankFontPx`）、状态、增量、名次提示。面板锚在右上角（1300–1586 × 14–146），宽 286。
- `RefreshOrderBar`（约 389–396 行）：顺序条是一个按钮，文字 `行动顺序：蓝方 > 金方 > 紫方◀ > 红方　[5] 展开顺序层`。面板锚在顶部居中，宽 580。
- `RefreshHand`（约 494–526 行）：每类一个 `Ui.Toggle`，文字 `▶ 普通子 ×5`（选中）或 `　普通子 ×5`；弃牌阶段文字 `弃掉整类：…`。
- 插旗提示：中央面板在插旗阶段被锚到左列（`Hud.cs` 约 753 行），高度写死为 130 或 176 像素（`222f` / `268f` 减 92），三四行换行文字放不下，最后一行被裁掉一半。

视图模型里需要的字段都已经有：`PlayerPowerRowView`（`Total` / `TerritoryScore` / `GroupScore` / `Rank` / `StatusText`）、`PowerDisplay`（演出中间态）、`MatchPublicView.ActionOrder` / `CurrentPlayer`、`OwnHandRowView`（`Name` / `Count` / `PendingGained`）、`ViewerWorld.Viewer`。

另一会话的 `formation-tiers` 正在改 `Layers/LayerContents.cs`、`Show/SettlementBeats.cs`、`Style/NumberTiers.cs` 等呈现层文件，本 change 只读不改它们。

## Goals / Non-Goals

**Goals**
- 三处面板按列排版，自家一行、当前行动者有不靠颜色的标记。
- 按列的文字在呈现层生成，可单元测试；引擎层只摆放。
- 修掉插旗提示的裁字。

**Non-Goals**
- 不加信息，不改面板宽度与锚点（插旗提示的高度除外）。
- 不改演出的节奏和放大规则（`RankFontPx` 不动）。

## Decisions

### D1 呈现层：`Siege.Presentation.Hud.HudPanelRows`（新文件）

纯函数，零 Godot 依赖：

- `RankRowView RankRow(PlayerPowerRowView row, PowerDisplay? display, PlayerId viewer)`，`RankRowView` 含：`Player`、`RankText`（`第 2 名` / `—`）、`NameText`（阵营名）、`PowerValue`（要显示的数：演出中取 `display.Value`，否则 `row.Total`，引擎层用 `Labels.CompactPower` 显示、用 `RankFontPx` 定字号）、`PowerText`（缩写后的文字）、`DetailText`、`DetailIsDelta`（明细是不是演出增量，引擎层据此选增量色）、`IsMuted`（有状态时整行次要）、`IsViewer`。
  - 明细规则：有状态 → 状态文字；演出中处于领地段 / 军势段 → `display.StageText`；处于定格 → `display.Change.DeltaText`；名次变动提示（`display.Change.RankText` 加 ↑ / ↓）在滚动期间接在明细后面，用全角空格隔开；都没有 → `领地 {territory} + 棋串 {CompactPower(groups)}`。
  - 有状态又在演出中（理论上出局者也可能有增量）时：状态在前、增量在后。
  - 有状态、不在演出中时：状态在前、领地与棋串的拆分在后（D9-1）。
  - 行序仍由引擎层现有的 `ShownRank` 逻辑决定（演出前按旧名次），本函数不排序。
- `OrderBarView OrderBar(MatchPublicView view)`：`Entries`（每项 `Player`、`NameText`、`IsCurrent`）、`EmptyText`（顺序未定时为 `未定`，否则为 null）、`HintText`（`[5] 顺序层`）。
- `HandRowView HandRow(OwnHandRowView row, bool discard)`：`NameText`（弃牌阶段为 `弃掉整类：普通子`，否则 `普通子`）、`CountText`（`×5`；有新征募时 `×5（新 1）`）、`Kind`（弃牌阶段 `ButtonKind.Danger`，否则 `Default`）。

现有的 `CompactText`、`OwnHandRowView.Text` 保留（终端版与别处还在用），本 change 不删。

### D2 势力排名的摆放

- 整个排名用一个 `GridContainer`（6 列：自家标记、徽记、名次、阵营、势力、明细），列宽由最宽的一格决定，各行自然对齐。`h_separation` = `Space3`（6），`v_separation` = `Space1`（2）。
- 自家标记列：本机玩家那一行放一个 3 × 16 像素的 `ColorRect`，颜色 `Ui.PanelBorder`；其他行放同尺寸的空 `Control` 占位。宽度 3 放进 `UiTheme`（`ViewerMarkWidthPx`）。
- 势力列 `HorizontalAlignment.Right`；明细列字号 `Caption`、颜色 `MutedText`（演出增量用现在的增量色：负数 `DangerText`，否则阵营色）。
- 演出期间势力列字号取 `RankFontPx(display)`；段首放大时这一格变宽，整列跟着变宽，这是可接受的（现在也会把整行推宽）。
- 宽度核算（1600 宽、正文 15 号）：标记 3 + 徽记 16 + 名次约 45 + 阵营约 30 + 势力约 40 + 明细（注释 13 号，`领地 17 + 棋串 64` 约 105）+ 5 × 6 间距 ≈ 270，面板内宽 270（286 − 两侧内边距 8）。紧但放得下；出局者的状态 `已出局 · 不再行动` 约 110，也放得下。实现后在 4 人图与边疆图上截图确认；有状态的行更宽，面板向左长出去（D9-1）。

### D3 行动顺序条的摆放

- 仍是 `BuildOrderBar` 里那个 `Ui.Action` 按钮（点了切换顺序层，行为不变），按钮文字置空，里面放一个居中的 `HBoxContainer`，子控件全部 `MouseFilter = Ignore`。
- 每个条目：徽记（14 × 14）加阵营名 `Label`（`Small` 字号）。当前行动者：名字用 `PanelBorder` 金色，条目下方一条 2 像素金色底边（`ColorRect`，宽度随条目）。条目之间放一个次要色的 `›`。末尾 `[5] 顺序层` 用 `Caption`、次要色。
- 顺序未定时只放一个 `未定` 加末尾提示。
- 子控件的颜色与字号都经 `Ui.Text` 或 `UiTheme` 取，不写字面量。

### D4 手牌行的摆放

- 每类仍是一个按钮（`Ui.Toggle` 或弃牌阶段 `Ui.Action(…, ButtonKind.Danger)`），按钮文字置空，里面放一个铺满的 `HBoxContainer`：左边类型名（`ExpandFill`），右边数量（右对齐）。子控件 `MouseFilter = Ignore`。
- 子 `Label` 不会跟着按钮的悬停 / 按下改字色，所以字色由引擎层按"选中 → 金色、弃牌 → 警示色、否则正文色"从 `Ui` 取；底色、边框、选中底边仍由按钮自己的主题样式画。
- 去掉行首的 ▶ 与全角空格占位：选中已有金字和金色底边两种提示。
- 左右内边距与按钮主题一致（`ButtonPaddingXPx`）。

### D5 插旗提示

- 中央面板在插旗阶段的锚定改为只定左、上、右，底边偏移等于顶边（`Ui.Anchor(_centerPanel, 0, 0, 14, 92, 392, 92)`），高度由内容撑开。
- 需要时在换行文字摆好后补一次最小尺寸刷新（Godot 的自动换行 `Label` 在首帧可能还没算出换行后的高度）；以截图为准。

### D6 守门

- 单元测试（`tests/Siege.Core.Tests/VisualStyleBaseline/信息面板分列Tests.cs`）：规格里能落到呈现层的场景（排名按列、出局、大数缩写、本机标记、当前行动者、顺序未定、手牌分列、信息不减少），每条配变异。"信息不减少"用独立算式：把重排前的 `CompactText` 里出现的数字全部抽出来，断言每个都出现在重排后某一列。
- 源码守门：`RefreshRank` / `RefreshOrderBar` / `RefreshHand` 的方法体里不再拼 `CompactText`、`row.Text`、`"▶ "`、`"◀"`；三者都调用 `HudPanelRows`。配注入违例的变异。
- `hud-theme` 的全部源码守门继续通过（不写死颜色、字号、间距，不用目标类型 `new(…)`）。

### D7 画面验证

拍图脚本沿用 `art/hud-theme/shots.sh` 的做法，另加：对局中途一张（有排名数据、轮到某人、手里有几类棋）——用 `--auto-demo` 跑到中途的截图帧，或 `--screenshot=路径:帧` 取一个靠后的帧；边疆图一张（`--map=siege-frontier-v2`，见 D9-4）。改前改后各拍一套，存 `art/hud-panels/before|after/`。插旗提示两种情况（带与不带"地图一屏看不全"一行）都要有图：v5 不带，边疆图带。

### D9 段 A 之后的补充裁决（主会话，2026-10-05）

1. **有状态的行不丢拆分**：段 A 初版让有状态的行只显示状态文字，弃赛玩家若领地或棋串不为 0，这两个数在排名栏里就读不到了，违反"重排不得减少信息"。改为状态在前、拆分在后（`已出局 · 不再行动　领地 0 + 棋串 0`）。这一行比别的行宽，面板会和现在一样向左长出去；明细列不截断（推翻 D2 末尾"放不下就截断"的退路，截断同样会丢信息）。"信息不减少"测试要覆盖有状态的行。
2. **演出未开始（Pending）时明细为空**：势力列此时显示旧值，拆分取自结算后快照，两者放在一起对不上；与现在 `Hud.cs` 演出期间不显示拆分一致。接受段 A 的做法。
3. **`RankRowView` 多 `DeltaIsNegative`、`HandRowView` 多 `Type`**：接受。
4. **超过 4 人**：`FactionTable` 只定义 4 方，项目里对局固定为至多 4 人（生成图的 `p5–8` 是平台数，不是人数），D7 的"8 人生成图"截图改为边疆图 `siege-frontier-v2` 一张。

5. **顺序条保留标题**：段 A 的 `OrderBarView` 没有"行动顺序"标题，按钮里只剩条目，属于隐式删除。保留：`HudPanelRows` 加常量 `OrderTitleText = "行动顺序"`，引擎层放在条目之前，用 `Caption`、次要色。
6. **演出中的状态文字保持次要色**：有状态又在演出中时，`DetailText` 把状态和增量放在一起，引擎层只能整段用增量色，旧界面里状态是固定的次要色。`RankRowView` 拆出 `DetailStatusText`（状态部分，可空）与 `DetailRestText`（其余部分），`DetailText` 保留为二者用全角空格连接的结果；引擎层状态部分用次要色，其余部分按 `DetailIsDelta` / `DeltaIsNegative` 取色。由段 B 实现并补测试。

7. **演出中本段增量跟着放大**（段 B 中途）：段 B 初版把明细一律定为注释字号，段首放大时只剩势力数字在弹。领地段 / 军势段时本段增量的字号改为与势力列同为 `RankFontPx(display)`，保留 tiered-number-show 定下的"数字与本段增量一起弹"；定格与平时仍是注释字号。列宽随之变化，可接受。
8. **名次提示不放大**：名次提示单独拆成 `RankRowView.DetailRankHintText` 与单独一个标签，字号恒为 `UiTheme.Body`。旧界面就是这样做的，原因是五档 200% 时这一行会长到压住顶部的行动顺序条。
9. **状态部分**恒为注释字号、次要色；势力列只写数字，不再写"势力"二字（面板标题已是"势力排名"）；弃牌阶段的手牌只用危险语义，不叠加选中态。

## 接口契约

- `HudPanelRows` 只读视图模型，不改任何现有类型。
- 引擎层不再自己拼排名、顺序、手牌的文字。

## 已知歧义与建议裁决

以下是主会话按常规判断定的，评审时可改：

1. 自家标记用行首金色竖条（D2），不用整行底色。整行底色在 `GridContainer` 里画不连续，且会和演出期间的增量色抢眼。
2. 顺序条的当前行动者沿用 `hud-theme` 选中态的"金字 + 金色底边"（D3），去掉 ◀。
3. 手牌去掉 ▶（D4）。
4. ~~明细放不下时截断而不是加宽面板（D2）~~ 已被 D9-1 推翻：不截断。
5. 名次仍写"第 2 名"，不缩成"2"。

## Risks / Trade-offs

- **按钮里放子控件**：子 `Label` 不随按钮状态变色（D4 已说明），悬停时只有底色变。可接受。
- **演出期间列宽跳动**：段首放大让势力列临时变宽，其他行的明细列会跟着右移。现在整行也会被推宽，程度相当；实现后用 `--shot-show=` 类截图看一眼。
- **与 formation-tiers 的冲突**：它如果之后要在排名里加阵型信息，会和本 change 改同一个方法。本 change 不碰它在改的文件；合入 main 时以先合入者为准，后者手工合并。
- 本机 `dotnet test` 原有 11 条 Core / Sim 日志黄金值为红，判据是"失败集合与改动前逐条相同"。
