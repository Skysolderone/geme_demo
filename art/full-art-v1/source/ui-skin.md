# 全套 HUD 皮肤源规范

运行时图形由 Godot 控件与绘图代码生成，不需要贴图。颜色唯一来源是 `src/Siege.Presentation/Style/VisualBaseline.cs` 的 `UiTheme`；控件样式由 `src/godot/scripts/Ui.cs` 统一创建。

## 视觉语言

- 石墨蓝半透明面板，左侧双线棱与上沿金线，细圆角、向下阴影；棋盘仍是视觉主体。
- 明金色标题配菱形铭牌，段落用低亮度金线分隔。
- 深蓝按钮；悬停时转青色边，按下转暖金色，选中态为金边深橄榄底。确认动作金边，危险动作红边。
- 红、蓝、金、紫阵营色不变。四种徽记仍取 `Emblems.Polygons` 的三角、塔楼、太阳、莲花；HUD 外加八角深色底片与细金短线，棋子底座仍共用原图形。

## 运行时入口

`Ui.PanelStyle`、`Ui.Heading`、`Ui.Separator`、`Ui.Action`、`Ui.Toggle`、`Ui.Primary`、`Ui.Danger`、`Ui.Field` 与 `EmblemIcon._Draw`。`Hud`、`Hud.MapSelect` 和 `Hud.Carry` 均复用这些入口。按钮高度保持 30 像素，正文字号保持 15 像素。

手牌按钮和征募候选左侧绘制 20×20 的棋子轮廓小图标。`PieceIcon.Attach` 从 `PieceStyleTable.For(type).Silhouette` 取唯一类型映射；十种图标分别保留圆头、塔楼、双球连杆、三角锥、水晶群、斜柄工具、竖旗、相扣空环、交叉矛、矮宽碑的轮廓。图标控件不接收鼠标，文字与数字仍由整行按钮绘制，选中和禁用态沿用按钮原行为。

画面核对入口：Godot 非 headless 模式的 `--map-select --screenshot=<PNG>:12` 与 `--map=link:1 --auto-demo --seed=5 --rounds=1 --overview --screenshot=<PNG>:12`。两者均按 1600×900 检查。
