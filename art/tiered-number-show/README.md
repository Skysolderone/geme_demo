# tiered-number-show 人工检查清单

change `tiered-number-show`（数值呈现五档递增）段 B：Godot 接线。取图：macOS / Apple M3，1600×900，无人值守（没有音频节点）。
每张图旁边有同名 `.log`，其中 `[show-tier]` 一行是这张图的自证读数（揭示条目、亮环、轻震、势力栏放大、常驻标注的档位都取自遮罩与样式表）。

## 怎么生成

```bash
G=~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot
A=$PWD/art/tiered-number-show          # --screenshot= 要给绝对路径：相对路径会存盘失败而退出码仍是 0
dotnet build src/godot/Siege.Godot.csproj -v q -p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config   # Godot 读 Debug 程序集

# 逐档（示例节拍，--reveal-at= 是演出内的毫秒数：各档末步开始后约 0.1 秒）
$G --path src/godot -- --reveal-preview=play --reveal-at=70   --map=siege-4p-base-v5 --shot-cell=C8 --shot-zoom=20 --screenshot=$A/tier1.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=1630 --map=siege-4p-base-v5 --shot-cell=E6 --shot-zoom=20 --screenshot=$A/tier2.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=3410 --map=siege-4p-base-v5 --shot-cell=G8 --shot-zoom=20 --screenshot=$A/tier3.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=5250 --map=siege-4p-base-v5 --shot-cell=J6 --shot-zoom=20 --screenshot=$A/tier4.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=7250 --map=siege-4p-base-v5 --shot-cell=L8 --shot-zoom=20 --screenshot=$A/tier5.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=6980 --map=siege-4p-base-v5 --shot-cell=L8 --shot-zoom=20 --screenshot=$A/tier5-building.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=7400 --map=siege-4p-base-v5 --shot-cell=L8 --shot-zoom=20 --screenshot=$A/tier5-after-shake.png:10

# 五档同屏的定格对照（中景 / 开局的最远一档 / 大图全局预览）与灰度
$G --path src/godot -- --reveal-preview=ladder --map=siege-4p-base-v5 --shot-cell=G7 --shot-zoom=3 --screenshot=$A/ladder.png:10
$G --path src/godot -- --reveal-preview=ladder --map=siege-4p-base-v5 --screenshot=$A/ladder-far.png:10
$G --path src/godot -- --reveal-preview=ladder --map=board:1 --overview --screenshot=$A/ladder-overview-board1.png:10
ffmpeg -y -i $A/ladder.png -vf format=gray $A/ladder-gray.png

# 真实对局（自动演示，等到含该节拍的结算按正常时长播到取图点）
$G --path src/godot -- --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --shot-show=reveal --screenshot=$A/reveal-real-tier5.png:35
$G --path src/godot -- --auto-demo --map=siege-4p-base-v5 --shot-show=reveal --screenshot=$A/reveal-real-tier4.png:15
$G --path src/godot -- --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --shot-show=power --screenshot=$A/power-tier5.png:35
$G --path src/godot -- --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --shot-show=power --screenshot=$A/reveal-label-handoff.png:35   # reveal-label-handoff：与上一条同一条命令、同一取图点（上一张检查阶段也已重拍，两张图除水面流动外相同）
$G --path src/godot -- --auto-demo --map=siege-4p-base-v5 --shot-show=power --screenshot=$A/power-tier3.png:40
$G --path src/godot -- --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=1 --screenshot=$A/group-labels.png:74

# 有人看的预览（带揭示音效；循环播放，按住空格 / 鼠标左键提速，滚轮缩放）
$G --path src/godot -- --reveal-preview=play
```

新增启动参数（都经 `LaunchArgs` 严格解析，拼错退出码 1）：

- `--shot-show=reveal`：既有 `--shot-show=` 多一项，等军势揭示节拍里军势最大的那条到末步、步内过三成再取图。
- `--reveal-preview=play|ladder`：只做展示的逐档预览，不建真正的对局、不读写档案、不接受落子与插旗；不与 `--auto-demo` / `--pick-check` / `--shot-show=` / `--map-select` / `--carry-preview=` 同用。
  `play` 按真实时间线循环播放五条示例棋串（一档 3、二档 4+2 = 6、三档 (5+2)×1.5 = 10、四档 (9+3)×1.5 = 18、五档 (20+4)×1.5 = 36）；`ladder` 是五条结果并排的定格。
- `--reveal-at=<毫秒>`：`play` 配 `--screenshot=` 时必给，以固定 16 ms 一步推进到该时刻取图；须小于示例演出的总时长（8680 ms），否则报错、退出码 1（不存一张没有任何中间态的空图）。

## 看什么

| # | 规格 / 设计条目 | 看哪张图 | 看什么 |
|---|---|---|---|
| 1 | 五档字形逐档加强（D1） | `ladder.png`、`ladder-far.png` | 五条结果同屏：白色小字「3」→ 淡金「= 6」→ 金「= 10」→ 橙「= 18」（一圈亮环）→ 红「= 36」（两圈亮环），字号、描边逐档加大。这张是定格对照，真实演出里条目逐条出现，不会五档同时满显 |
| 2 | 档位不只靠颜色（灰度可辨） | `ladder-gray.png` | 去色之后五档仍凭字号分得开；四、五档另有亮环 |
| 3 | 逐档单看 | `tier1.png` … `tier5.png` | 各档末步开始后约 0.1 秒：结果正处在弹出回落中（log 里"弹出缩放"一至五档 1.05 / 1.09 / 1.14 / 1.22 / 1.39）。四档一圈亮环，五档两圈并轻震（log："轻震 520‰，相机偏移 …"） |
| 4 | 算式逐步长成结果（D2） | `tier5-building.png` → `tier5.png` | 前一张停在第三步：整行是累计算式「(20+4)×1.5」，用的是这一步的档位（四档，橙）；后一张到末步：大字只剩结果「= 36」（五档，红），算式缩成小一号的一行挪到上方 |
| 5 | 轻震只偏移画面（D8） | `tier5-after-shake.log` 的两行 `[shake]` | 轻震开始与结束时注视点、距离读数相同，结束后"相机节点相对位姿的偏移 0"。无人值守下两者任一不符，这一行会写"自检失败"并在取图后以退出码 1 结束。注意这张图在 v5 的最远一档上取：注视点被夹在 (0, 0)，即使偏移被写回相机状态读数也仍是"相同"（检查阶段用变异证实）；要让这一行真能分辨，换一屏看不全的图（把命令里的 `--map=` 换成 `board:1`）。不依赖地图的守门是 `dotnet test` 里的源码扫描 `回到出生平台Tests.轻震不改相机状态` |
| 6 | 真实对局里的五档 | `reveal-real-tier5.png`（reveal-label-handoff 之后重拍） | 蓝方棋串「(12+21)×3.375 = 111」：红色大字浮在标注格棋子上方、两圈亮环；这条棋串的常驻标注此刻不画（log：显现进度 M3 0‰），同一个数只出现一次。同一次结算里先揭示的两条正在交接：J12「1+2 = 3」、N11「6」的结果在淡出，各自的标注按 362‰ / 134‰ 淡入；其余棋串的标注照常。大字盖住了身后一行的棋子——只在停留的两秒内 |
| 7 | 真实对局里的四档 | `reveal-real-tier4.png`（reveal-label-handoff 检查阶段重拍） | 紫方「(6+12)×1.5 = 27」：橙色、一圈亮环；这条棋串的常驻标注此刻不画（log：显现进度 B11 0‰，结果年龄 74‰），格上没有紫色的「27」，其余四条标注照常；顶部摘要与势力栏照旧 |
| 8 | 势力栏按档放大（D5） | `power-tier5.png`、`power-tier3.png` | 前一张：蓝方「军势 +44」五档，段首 200%（取图在段内三成，字号 25 px，正文 15 px），阵营色；金方「军势 −1」一档 130%，危险色——增量颜色不分档。后一张：紫方「领地 +8」三档 160%（21 px）。此刻格上的揭示结果还在停留，被揭示棋串的常驻标注照遮罩画（两张都在 reveal-label-handoff 检查阶段重拍）：前一张与第 11 行同一取图点（M3 0‰ 不画、N11 750‰、J12 980‰）；后一张 E7「2+2 = 4」结果年龄 240‰、标注不画，B2「1+3 = 4」结果年龄 711‰ 正在淡出、标注「4」按 422‰ 半透明 |
| 9 | 常驻标注按档（D6） | `group-labels.png` | 不开信息层：「81」「43」最大（五档 132），「17」（四档）、「12」「11」（三档）、「7」「5」「4」（二档）、「1」「3」最小（一档 88，与改动前相同）；仍是阵营色 |
| 10 | 全局预览用同一张表（A8） | `ladder-overview-board1.png` | 25×30 的大图全局预览下字号不变，相对格子大得多：示例棋串只隔一格，相邻条目互相压住。真实对局里条目逐条出现，但相邻棋串同回合揭示时会有同样的重叠——字号要不要在全局预览下另设一档留给负责人定 |
| 11 | 揭示结果与常驻标注交接（reveal-label-handoff D1） | `reveal-label-handoff.png` | 同一次结算再往后 0.4 秒（势力节拍军势段三成处）。log 的显现进度：M3 0‰、N11 750‰、J12 980‰。M3「= 111」还在停留的前半段（结果年龄 266‰）——只有红色大字，格上没有蓝色的「111」；N11 的标注「6」已大半显出，上方淡金的「6」只剩淡淡一点；J12 的标注「3」几乎完全显示，上浮的算式行已看不见。其余 28 条标注与演出之外相同 |

## 截图覆盖不到、要实际玩一局确认的

- **音效**：无人值守没有音频节点，这里只有 log 里的提示行（如 `tier5-after-shake.log`：揭示 2 / 3 / 4 / 5 档依次升 2 / 4 / 7 / 12 个半音）。揭示短音的音色、逐步升调的听感、五档高一个八度是否刺耳、提速时的音量，都要用 `--reveal-preview=play` 或真实对局试听。
- **到账声按档升调**：预览里听不到（示例节拍之间隔的是空的势力节拍，原因见下面"决定"第 5 条），只能在真实对局里听；四档以上的军势到账约占一成的行动（design.md 的实测分布：军势增量四档 9%、五档 2%）。
- **节奏**：每步 0.22 秒、四 / 五档末步定格、整拍 1.6 秒上限压缩之后的观感；本机玩家自己的结算是否觉得拖。
- **轻震的强度**：幅度 0.06 格，在开局的最远一档下只有两三个像素，近景下约十个像素；是否够"轻"或太弱。
- **提速**：演出期间按住空格 / 鼠标左键，揭示、亮环、轻震按 4 倍走完（A5）。

## 段 B 做的决定

1. **结果怎么当主角**：未到末步时整行是累计算式，用这一步的档位（逐步变长、逐档变大）；到末步后拆成两行——大字只写这一步的结果（"= 10"，末步档位的字号 / 颜色 / 描边），算式缩成结果字号的 60%（不小于 64）挪到上方、颜色向白提亮。不拆的话五档整条算式按 156 号字排开有五六格宽。只有一步的棋串（无加值无倍率）没有小字行。
2. **落点**：大字的下沿压在标注格棋子头顶（格心上方 0.95），随结果年龄上浮 0.5、后半段淡出；关深度测试、绘制优先级高于落子飘字与常驻标注。标注在屏幕上大小恒定，行距按离相机的纵深折算，任何缩放下两行都贴合。
3. **亮环**：贴格面的扁平圆环（区别于格子轮廓的方环与信物闪光），半径 0.3 → 1.35 格、由粗变细、由实变淡，颜色取末步档位色提亮；第二圈晚 30% 出发。不关深度测试——会被身前的树、岩石和更高的地块挡住一部分。
4. **轻震**：横向往复 3 次 + 纵深方向往复 5 次（幅度减半），按 (1 − 进度)² 衰减；偏移同时加在眼位与注视点上，朝向与俯角不变，不写回相机视图模型。
5. **预览的示例节拍**：五条棋串各一拍军势揭示（单条目，不触发 1.6 秒压缩），拍与拍之间隔一拍空的势力节拍。没有做成"揭示之后接一次同档的势力到账"：音效提示与遮罩按"一次结算至多一拍军势揭示、每名玩家至多一条势力变化"实现，同一名玩家连着五条势力变化在遮罩里只留最后一条。真实结算不会出现这种序列，所以没有去动呈现层。
6. **五档颜色没有改**（A6）：五档红与提子飘字的红几乎同色，但两者不易混——提子飘字是 72 号的"提"字，在提子节拍出现、1.4 秒寿命，等揭示条目走到末步时已经淡完或接近淡完（`reveal-real-tier5.log`：取图时"飘字 0 条"）；揭示结果是 156 号的"= N"，带算式行与亮环。另有一处观察留给负责人：分档色是一条"冷到热"的色带，与阵营色无关，所以蓝方棋串的五档结果是红字（`reveal-real-tier5.png`），金、橙两档落在金方棋串上时与阵营色相近。
7. **揭示音**：三角波 1175 Hz、70 毫秒、快速衰减（比落子"嗒"高一个纯四度）；揭示 / 领地 / 军势三种按档位把播放器音高乘以 2^(半音 / 12)，其余四种不变。
8. **看图后调过的参数**：算式行最小字号 54 → 64（二档的"4+2"在近景下读不清）；亮环线宽 0.13 → 0.20、透明度衰减由线性改为 1 − t²（开局的最远一档下太淡）；示例棋串由排成一行改为上下错开两行、不贴地图边（最远一档下相邻条目互相压住；贴边的一档白色小字像坐标标注）。样式表（字号、颜色、时长）没有动。
9. **名次提示不跟着放大**（检查阶段改）：势力栏那一行里"名次 a → b ↑"另起一个标签、保持正文字号，段首放大只作用于数字与本段增量。排名面板贴右上角向左长，五档 200% 时连名次提示一起放大，这一行会长到压住顶部的行动顺序条（1600 宽下约 100 像素、持续段内前四成）。改动前一档（130%）时名次提示也跟着弹，现在不弹了——这一点与改动前的观感不同，留给负责人确认。四位数以上的势力配五档增量与名次变动时，段首几十毫秒内仍可能蹭到行动顺序条的右端，试玩时留意。

## 自动验证

- Godot 工程与 `siege.sln`（Release）构建 0 警告。
- `--auto-demo` 在 `siege-4p-base-v5` 与 `board:1` 上：53 帧 / 53 帧，与改动前相同；除 `[show]` 结算行多了"揭示 N·最高 M 档"一项外，日志逐行相同；开局对准之后相机位姿变化 0 次；音频节点 0。
- `--auto-demo --pick-check` 在 `siege-4p-base-v5`、`board:1`、`siege-frontier-v2` 三张图上通过，输出与改动前逐行相同。
- `dotnet test`：2138 条 = 2118 通过 / 11 失败 / 9 跳过；失败集合与本机基线逐条相同（11 条 Core / Sim 日志黄金值，与本 change 无关）。
- 引擎层（`src/godot` 不在 `siege.sln` 里）的源码扫描守门共四条，变异都变红、记录在各测试的备注里：
  「引擎层的轻震偏移是进度的确定函数」（实现阶段，2 条变异）；检查阶段补「轻震不改相机状态」（偏移写回视图模型 / 另写一次相机节点）、
  「引擎层只按呈现层给的档位查样式表」（亮环圈数写死 / 势力栏放大写死 / 揭示条目与常驻标注恒取一档）、
  「引擎层的音效参数表覆盖每种提示且只有三种按档升调」（参数表缺一行 / 全部种类都升调）——补之前这八条变异 `dotnet test` 全绿。

## reveal-label-handoff：揭示结果与常驻标注不再重复显示（2026-10-05）

负责人反馈"棋子放置后本来就有一个数值，现在重复显示了"。原因：常驻标注取自结算后的势力明细，演出一开始就已经是新军势；军势揭示条目标在同一格上方，末步写出同一个数（改动前的 `reveal-real-tier5.png` 上"= 111"与下方的"111"同时出现）。

改法（`openspec/changes/archive/2026-10-05-reveal-label-handoff/design.md` D1 / D2）：

- 演出遮罩多一项"被揭示棋串的常驻标注显现进度"（`ShowMask.GroupLabelPermille`：落点 → ‰），由时间线状态纯函数给出。军势揭示节拍将要揭示或正在揭示的棋串：自演出开始起为 0（落子 / 提子 / 信物节拍期间也是），到末步后按结果年龄 a 取 max(0, (a − 500) × 2)——结果停留的前半段保持隐藏，后半段随结果的淡出线性淡入；结果停留满即不再列出。不在表里的落点完全显示；演出播完与零时长下表为空。
- 引擎层只照着画：进度 0 不画，其余按进度取透明度（字与描边同乘）。常驻标注自成一层，演出逐帧刷新时表非空或上一次照着非空的表画过才重画这一层。
- 截图的 `[show-tier] 常驻标注` 一行末尾多一段"遮罩给的显现进度"，图上被揭示棋串此刻画不画、多透明以它为准；再后面是"演出帧里这一层重画 N 次"——取图那一帧走的是完整刷新，这个数用来自证逐帧那条路径也走到了（五张真实对局的图：`reveal-real-tier5` 163 / 163 帧、`reveal-label-handoff` 与 `power-tier5` 190 / 190 帧、`reveal-real-tier4` 96 / 96 帧、`power-tier3` 240 / 240 帧）。计数留在 `BoardView` 里，对外只给这段文案（`GroupLabelRedrawReadout()`，与 `[show-perf]` 一行的 `ShowRedrawReadout()` 同一做法）。

看图：上表第 6、7、8、11 四行。检查阶段按原命令重拍了 `reveal-real-tier4.png`、`power-tier5.png`、`power-tier3.png` 三张与配套 `.log`（修复前这三张上被揭示棋串的结果与标注同时完整显示；修复前的版本在 git 历史里）。重拍前后的 log 除 `[show-tier] 常驻标注` 一行多出的那一段外，只有 `[perf]` 的绘制调用各少 2 次（少画一条标注的字与描边）。`power-tier5.png` 重拍后与 `reveal-label-handoff.png` 是同一条命令的同一取图点，留哪一张由主会话定。

没有重拍的图不受影响：`tier*.png`、`ladder*.png` 是预览模式，盘上没有棋子（各自 log 里"常驻标注 0 条"）；`group-labels.png` 在演出之外取图（遮罩为空），用同一条命令重拍两次与原图三方比对，差异全部落在中央水面（流动层随时间变，同一构建两次运行之间也有约 3 万像素差），标注所在的像素没有差异——标注挪到独立一层不改变演出之外的画面。这几张的 log 是改动前的格式（`[show-tier] 常驻标注` 一行没有末尾那两段）。

已接受的边界（design.md A1–A3）：结算前就存在的棋串（如 31 → 111）在揭示之前不显示旧值；淡入期间上浮的结果与原位的标注都是半透明；最后一条的结果通常被演出结束截断（本例 M3 在演出结束时结果年龄约 620‰、标注约 240‰），演出一结束标注直接完全显示。

自动验证：`siege.sln`（Release）与 Godot 工程构建 0 警告；`dotnet test` 2146 条 = 2126 通过 / 11 失败 / 9 跳过，失败集合与本机基线逐条相同；`--auto-demo`（v5）53 帧、相机位姿变化 0 次、音频节点 0，日志除毫秒数外与改动前逐行相同；`--auto-demo --pick-check` 通过。新增规格场景测试六条与引擎层源码扫描一条（`TacticalLayers/棋串军势常驻标注Tests`）；检查阶段另挑 8 条变异，其中 7 条当时不红，据此补了一条三条目、压缩节拍的守门（`压缩的揭示节拍下显现进度与结果年龄同源`）与源码扫描的两段（标注自成一层：先清后画、只挂这一层；标注列表只在完整刷新里取、全局预览下为空）。补完后 22 条变异全部变红，记录在该测试类里。

## 全部裁决

负责人 2026-10-05 的五项裁决、实施方的十条初值与两处调整，见 `openspec/changes/archive/2026-10-05-tiered-number-show/design.md` 文末「裁决记录」与「已知歧义与建议裁决」（A1–A8）。检查阶段另定：势力栏的名次变动提示不跟着段首放大（五档 200% 时原先会越过行动顺序条约 100 像素）。
