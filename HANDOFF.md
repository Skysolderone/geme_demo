# 续接说明（HANDOFF）

> 更新于 2026-09-19（首轮原型 8/8 完成；去围棋化三轮 `terrain-model` / `scoring-sites` / `artisan-terrain-edit` 全部完成：地形进规则、势力 = 据点分 + 军势、匠人与三种地形改造；AI `Safety` 5 → 27 → 35，匠人征募权重 5）。仓库：`git@github.com:Skysolderone/geme_demo.git`。
> 读完本文即可在新会话中继续，不需要翻聊天记录。

## 一句话

《围杀 Siege》——2–4 人共享棋盘的回合制策略构筑游戏（围棋式围杀 × 自走棋式征募构筑）的 4 人核心原型。
规则内核是**零 Godot 依赖的 .NET 8 类库**（`src/Siege.Core`），AI 在 `src/Siege.Core/Ai`，
批量跑局 / 日志 / 分析与终端版在 `src/Siege.Sim`，界面视图模型在 `src/Siege.Presentation`（零 Godot），
3D 表现层在 `src/godot`（Godot 4.7.2 .NET 版）。

## 现在就能玩

> 负责人的终端是 **Windows PowerShell**：带引号的 exe 前要加 `&`，Godot 参数前加 `--%`（下面的命令都按此写）。在 Godot 编辑器里点 ▶ 运行 = 不带任何参数启动，直接进选图界面。

**图形版（推荐）**
```powershell
& "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe" --% --path E:/wws/geme_demo/src/godot
```
不带 `--map=` 启动先进**选图界面**（`map-generator`）：标准图 13×13 / 边疆图 25×30（手工）/ 随机图；随机图可输地图种子回车、点"换一张"、平台数 5–8，背景是该图的全局预览，面板显示完整地图标识（如 `gen:12345`、`gen:12345:p8`），点"开始"进入插旗。带 `--map=<标识>` 则跳过选图；`--auto-demo` / `--pick-check` / `--screenshot` 不带 `--map=` 时缺省 v4 并跳过选图。
点出生区插旗 → 征募 → 左下手牌选类型、点格子暂放 → 右侧看预演 → 右下确认或 Pass。
棋盘四边有围棋记法坐标（v3 为列 A–N 跳过 I，行自下而上 1–13，A1 左下），与对局日志一致。
地形：四角黄色高台是出生区（h=2），浅褐台阶是缓坡（h=1），中央环河岛（h=0）经四座木桥进出；崖壁（高差 2）、深水、栅栏切断气，林地挡覆盖。
按住 1/2/3 看盘面/势力/信物层，4 或点顶部顺序条看顺序层，Tab 切换盘面层的归属／棋串读法，H 开手牌面板，T 切换按住/点击模式。
命令行加 `-- --seed=12345` 复现同一局；`-- --auto-demo` 自动演示；`-- "--screenshot=<路径>.png:90"` 截图；`-- --auto-demo --pick-check` 自检分层拾取（换相机 / 层高 / 地图后必跑）。

**终端版**
```powershell
dotnet run --project src/Siege.Sim -c Release -- play [--difficulty Easy] [--seed N] [--map <标识>]
```

**随机地图（`map-generator`）**：地图标识 `gen:<地图种子>[:p<平台数>]`，同一标识永远同一张图（25×30、平台边长 5–9、平台数 5–8 缺省 6、必过边疆档校验）；裸 `gen` = 随机取一个九位以内的种子并打印完整标识。地图种子与对局种子（`--seed`）互相独立。
```powershell
dotnet run --project src/Siege.Sim -c Release -- play --map gen:12345
```
```powershell
& "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe" --% --path E:/wws/geme_demo/src/godot -- --map=gen:12345
```
看 / 导出某张生成图：`dotnet run --project src/Siege.Sim -c Release -- map --map gen:12345 [--out 文件.json]`（不给 `--out` 不写盘；拒绝覆盖 `maps/` 下的内置图文件）。批量每局换图：`run --map gen:100 --map-per-match …`（第 i 局用 `gen:<100+i>`，报告按平台边长 5–9 统计被选次数与胜率）。
日志首部与存档都带地图内容摘要：生成器一改，旧日志回放 / 旧存档恢复会报"地图不一致"（预期的响亮失败）；`gen:12345` 的导出摘要有黄金值测试，改生成器会先红。种子 1–50 的文本图速览在 `sim-out/mapgen-gallery.txt`（重出：`$env:SIEGE_MAPGEN_GALLERY=1; dotnet test -c Release --filter 布局速览`）。
已量到的数（20 局每局换图，Standard AI）：19 局打满 15 大回合、1 局第 7 大回合碾压；AI 单步均值 0.8 s / 最大 5.6 s；按边长胜率 5→33% / 6→25% / 7→8% / 8→27% / 9→0%（样本小，均与 25% 基线无显著差异）。

**边疆图（多平台大地图验证版，`frontier-map`，进行中）**
```powershell
& "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe" --% --path E:/wws/geme_demo/src/godot -- --map=siege-frontier-v1
```
```powershell
dotnet run --project src/Siege.Sim -c Release -- play --map siege-frontier-v1
```
25×30、可落子 377 格、6 个大小不一的 h=2 平台（9×9 / 8×8 / 7×7 / 6×6 / 5×5 / 5×5），平台即出生区，4 人选 6 个、没人选的是中立争夺区；前三大回合只能落自家平台，之后全图。缺省地图仍是 v4。
相机：鼠标贴窗口边缘推屏，方向键 / WASD 平移，滚轮缩放（俯角恒 60°），空格回自家平台，**M 键 / "全局"按钮切全局预览**（再按回到原画面）；回合横幅右侧显示悬停格坐标。开局自动缩放到"自家平台整个可见"。v4 上整盘一屏看全，最远缩放下相机不动、画面与从前逐像素相同。
图形版命令行现在是严格解析（`--map=` `--cell-limit=` `--seed=` `--rounds=` `--auto-demo` `--pick-check` `--screenshot=`），拼错即报错退出。大图（可落子 > 150）AI 自动启用候选格上限 K=24（`--cell-limit 0` 关闭）。
已量到的数（4 个 Standard AI，K=0 口径 20 局）：**全部打满 15 大回合靠上限收场**、无一局碾压；首次提子平均第 6 大回合；5×5 中央平台胜率明显偏高、9×9 为 0 胜（样本小）。AI 单步 K=0 均值 2.2 s / 最大 11 s → K=24 均值 0.73 s / 最大 2.2 s。**节奏参数（部署额度 / 大回合上限 / 碾压起始）与调图待负责人试玩后裁决**，规格与逐段记录在 `openspec/changes/frontier-map/`、`.trellis/tasks/09-19-frontier-map/implement.md`（含 17 条人工检查清单）。

## 分支状态

| 分支 | 状态 |
|---|---|
| `main` | 全绿：`dotnet test -c Release` **1244/1244**、零警告；`frontier-map`（63307d3 起，含全局预览与地形效果 cf9651b、水面动画 71761d6）与 `map-generator` 段 A–D 均在 main，两个 change 都未归档（待负责人试玩结论），套件约 20–45 秒（含 3 局真实跑局的日志保真度测试）。`artisan-terrain-edit` 方案提交（43208d8）及之前已推送；`strict-cli` 归档与第三轮段 O–E 均为本地提交，**未推送** |
| `wip/match-flow` | 早已合入 main，本地与远端均可删 |

## 权威来源（按优先级）

1. `2026-09-10-siege-core-gameplay-design-v1.md` —— 玩法设计 **v1.3**（文末有变更记录；`Siege-玩法介绍-v1.docx` 是玩家向介绍稿，冲突以 md 为准）
2. `openspec/specs/` —— 已归档进基线的 24 个能力规格（Requirement / Scenario 是验收基准）
3. `openspec/changes/archive/` —— 各 change 的 `design.md` 末尾「裁决记录（已确认）」共 76 条，是设计文档未覆盖部分的判定来源
4. `.trellis/spec/core/` —— 编码规范四份：`boundaries.md` `determinism.md` `coordinates.md` `testing.md`（**必读**，全是踩过的坑）
5. `.trellis/tasks/archive/2026-09/` —— 各子任务的 prd / design / implement
6. `openspec/ROADMAP.md` —— 依赖顺序、四条全局硬约束、裁决速查
7. `art/style-exploration/` —— 视觉基准图与 8 张功能示意（从 docx 解出），`README.md` 是风格基准与人工检查清单

## 进度：首轮原型 8/8 完成

| # | 子任务 | 状态 | 测试 |
|---|---|---|---|
| 1 | board-core | 归档 | 140 |
| 2 | batch-deployment | 归档 | +44 |
| 3 | territory-power | 归档 | +82 |
| 4 | relic-system | 归档 | +66 |
| 5 | recruit-hand | 归档 | +50 → 382 |
| 6 | match-flow | 归档 | +65 → 447 |
| 7 | heuristic-ai | 归档 | +50 → 497 |
| 8 | tactical-ui | 归档（2026-09-15） | +86 → **583** |

之后的规则复议与打磨（均已归档）：`cap-multiplier`（倍率封顶）、`round-cap`（大回合上限 15）、
`dominance-victory`（势力碾压胜利，候选制，第 7 大回合起）、`growth-pass-1`（基础部署上限分阶段 3/4/5）、
`multiplier-rebalance`（倍率只放大基础军势、封顶降到 3）、`catch-up-recruit`（落后者征募补偿）、
`denser-map`（4 人基准图改版 v2，可落子 109→85、障碍 12→36）。测试 **658**。

`ai-safety-weight`（AI 安全权重 20→5）、`board-coordinates`（棋盘四边坐标标注）、`merge-board-layer`（领地层与气层合并为盘面层的两种读法）也已归档。测试 **680**。
**已写好规格待执行**：`strict-cli`（未知命令行选项必须报错）。

## 去围棋化改造（2026-09-16 起，三轮）

起因：用户认为玩法"跟围棋太像"，刺眼的是**领地数目计分、空棋盘上铺子的观感、每步只决定往哪放**；**气与提子保留**。参考方向是有高低差、河、桥、栅栏、林地的立体战术沙盘。用户逐条定了 31 条裁决，全文在 `openspec/changes/archive/2026-09-17-terrain-model/design.md`「裁决记录」（含②③轮的，只记录未实现）。

| 轮 | change | 内容 | 状态 |
|---|---|---|---|
| ① | `terrain-model` | 格属性（高度 0/1/2、地表、障碍、预置桥）+ 栅栏边；几何四邻之上导出**气边**与**覆盖关系**两套关系；连珠沿气边；v3 地图；Godot 分层渲染与拾取；设计文档 v1.2 | **已归档**，测试 680 → 729 |
| ② | `scoring-sites` | 空格领地退役，势力 = 据点分 + 军势；12 个据点（营帐 / 篝火 / 石碑 5 / 15 / 45）、与信物分离、占据或唯一覆盖即控制；高地压制加值 +1（进位置加值）；地图 v4；Godot 地标与旗帜；`Safety` 27 | **已完成、已归档、未推送**（段 A1 / A2 / B / C / S-16 / D 全部完成，openspec 与 Trellis 均已归档），测试 729 → 808 |
| ③ | `artisan-terrain-edit` | 第六种棋子「匠人」：落子即改造（搭桥 / 立栅 / 烧林），占 1 额度，指定相邻目标，不可逆，批次内不链式；同形禁则纳入设施 | **已完成、待归档、未推送**（段 O strict-cli 已独立归档；段 A 匠人棋子 / B 改造规则与遥测 / T-11 栅栏目标放宽 / C 界面 / D 扫档 / E 文档全部完成，裁决 T-1～T-14、R-1～R-7），测试 815 → 897；默认 `Safety` 27 → 35、匠人征募权重 5 |

第二轮现状（权威：`openspec/changes/scoring-sites/design.md` 裁决记录 1–22、设计文档 v1.3 §3.3 / §7.4 / §10.1 / §16，逐段记录在 `.trellis/tasks/09-17-scoring-sites/implement.md`）：

- **地图 `siege-4p-base-v4`**：地形与 v3 逐格相同，加 12 个据点——营帐 `B3 / L2 / M11 / C12`（出生区内 h=2）、篝火 `J2 / M9 / E12 / B5`（主人河外低地、紧贴邻家崖边，开局归邻家）、石碑 `H5 / J8 / F9 / E6`（岛上，与桥头信物隔栅栏）；各出生区沿气边距离 最近公共信物 5 / 中央入口 7 / 最近咽喉 4 / 最近篝火 6 / 最近石碑 8。v3 JSON 保留为历史，默认不加载。出生区对人显示统一为 1–4（内部与日志 0 起）。
- **计分**：`总势力 = 控制中的据点分 + 棋串军势`，空格归属只作判定与展示；控制 = 占据 > 唯一覆盖 > 争议 > 无人（`SiteControl.Compute`，只读 `CoverageMap`）；高地加值 = 覆盖目标上有高度严格更低的敌子 → 每枚 +1（`PieceEffects.HighGroundBonus`，只走 `CoverageTargets`），不被倍率放大；并列链 势力 → 信物数 → 据点数 → 棋子数。
- **段 C 扫档**（各 200 局，种子 1–200）：分量三档（Safety 5）领先者胜率 90.0 / 95.5 / 92.0%，分量不是杠杆；Safety 九档 + 22 / 25 / 27 加密档，领先者胜率在 20（95.5%）与 30（17.0%）间陡变。负责人拍板分值保持 **5 / 15 / 45**、`EvaluationWeights.Default.Safety` **5 → 27**（裁决 S-15）。
- **确认 200 局**（`sim-out/sites-final/`，当前地图与口径的唯一有效数据）：第 3 大回合领先者胜率 25.5%、不收敛 6.5%（整轮 Pass 182 / 达上限 13 / 碾压 5）、整局无提子 0、终局局平均结束第 7.78 大回合、首次冲突第 4 大回合、据点分占比 22.1%（低于目标 25%–45%，负责人知情接受）、每批次提子 0.29（偏保守）、Pass 率 22.1%、石碑争议 52.4%。
- **第三轮现状**（权威：`openspec/changes/artisan-terrain-edit/design.md` 裁决记录 1–22、设计文档 v1.4 §3.4 / §6 / §16 / §17-11，逐段记录在 `.trellis/tasks/09-18-artisan-terrain-edit/implement.md`）：匠人军势 1、征募权重 5（对局配置）；改造目标格为几何四邻、边至少一端是四邻格（T-11，否则立栅永远提不了子）；改造先于提子、批内不链式、不可逆、同形纳入设施；AI 全枚举改造目标（每局约 19 秒，与第二轮持平）。确认 200 局（`sim-out/artisan-final/`）：领先者胜率 24.0%、整局无提子 0、每批次提子 0.52（上一版 0.29）、据点分占比 31.5%（首次进目标带）、不收敛率 **26.0%**（上一版 6.5%，栅栏使盘面更难填满——已知偏离，终局条件留后续单开一轮）；烧林 1800 局仅 1 次（T-14 接受为冷门动作）。
- **下一步**：`openspec archive artisan-terrain-edit` → 归档 Trellis 任务 → 推送（问负责人）。之后候选：不收敛率 26%（终局条件 / 保护期，去围棋化三轮的 Non-goals）、AI 候选拥挤（T-12，搭桥 30%、烧林 0%）、插旗竞争、落后者无处可下（需在新口径重新统计）。`art/sites-v4/` 与 `art/artisan-v4/` 的截图清单仍待人工目检。

两套关系（规则的地基，改任何邻接相关代码前必读 `.trellis/spec/core/boundaries.md` 与 `coordinates.md`）：

- **气边**（对称）：两格都可落子 ∧ `|Δh| < TerrainData.CliffDrop(2)` ∧ 无栅栏。管连接、棋串、气、围杀、连珠、校验器距离 / 口袋。
- **覆盖关系**（不对称）：目标可落子、非林地、`h_t − h_s < 2`；遇一格宽深水落到对岸；栅栏不挡。管覆盖、归属、唯一覆盖、信物发现。
- 信物揭示补了"首次被占据"（林地信物只能占据揭示）。

## 现行终局与关键数值

- 终局优先级：只剩一人 > **势力碾压** > 棋盘填满 > 整轮 Pass > 达大回合上限（15）
- 势力碾压：第 7 大回合起，某参赛玩家势力 ≥ 其余参赛玩家之和 → 成为候选；其余每人各行动一次后仍满足即获胜，跌破即取消；候选与待回应名单公开
- 基础部署上限：第 1–3 大回合 3、第 4–6 大回合 4、第 7 大回合起 5，军令信物在其上叠加
- 倍率：`⌊基础军势 × 1.5^min(倍增子数, 3)⌋ + 位置加值`，倍率只放大基础军势
- 落后者征募补偿：名次 > ⌈人数÷2⌉ → 展示 +1；名次等于最大名次且 > 1 → 免费选取 +1

## 数据现状

### v3 回归（200 局，种子 1–200，Standard，`sim-out/terrain-v3/`）——**已作废**（scoring-sites R-8；当前数据见上方「第二轮现状」`sim-out/sites-final/`）

口径：13×13 + 领地计分未退役 + 覆盖不对称，**与下方 v2 基线不可直接对照**。只记录未调参；三条报警线（第 4 大回合即提子 > 50%、首次提子中位 ≤ 4、整局无提子 > 25%）均未触发。

| 指标 | v3 | v2 基线 |
|---|---|---|
| 首次跨出生区冲突 | 第 5.97 大回合 | 4.75 |
| 首次冲突时占用率 | 61.0% | 61.9% |
| 整局无提子局 | **14.0%**（28/200） | 1.2% |
| 终局局平均结束大回合 | 10.57 | 10.47 |
| 终局原因 整轮Pass / 达上限 / 碾压 | 88% / 10% / 2% | 92.8% / 6.4% / 0.9% |
| 第 3 大回合领先者胜率 | **17.0%**（12.4–22.8，显著低于 25% 基线） | 50.8% |
| 出生区胜率 | 28 / 25 / 25 / 22%，均含 25% | 无显著差异 |
| 自杀手 / Pass 率 | 0.0% / 15.7% | 0.0% / 14.6% |
| AI 耗时 | 14.8 s/局，200 局墙钟 122 s | — |

带进第二轮的三个观察：① 四家高台自留地让"不下山也能活到终局"成立，无提子局 1% → 14%；② 高台领地早期撑势力但不决定胜负，领先者胜率 17%；③ "13×13 会拉长对局"的预期没有成立，主控变量是岛的 21 格而不是总格数。完整记录在 `.trellis/tasks/archive/2026-09/09-16-terrain-model/implement.md`「8.2」。

### v2 基线（2000 局，种子 1–2000，Standard，`sim-out/baseline/`）——历史参考

权重口径 `Safety = 5`。v2 地图已不可加载，此表只作历史参考。

| 指标 | 实测 | §16 目标 |
|---|---|---|
| 部署上限中位数 第 1–3 / 4–6 / 7+ | 3 / 4 / 5 | 3 / 3–5 / 5–8 达标 |
| 势力 开局 / 中期 | 21.3 / 93.5 | 1–99 / 20–150 达标 |
| 首次跨出生区冲突 | 第 4.75 大回合 | 4–5 达标 |
| 首次冲突时盘面占用率 | 61.9% | — |
| 终局局平均结束大回合 | 10.47 | 7–10，超 0.47（+5%） |
| 第 3 大回合领先者胜率 | 50.8%（95% 区间 48.7–53.0） | ≤50%，区间含 50，统计上无法判定超标 |
| 终局原因 整轮Pass / 达上限 / 碾压 | 92.8% / 6.4% / 0.9% | — |
| 整局从未提子的局 | 24 / 2000（1.2%） | — |
| 自杀手尝试率 / Pass 率 | 0.0% / 14.6% | 阈值 25% / 60% 达标 |

唯一实质偏离是对局仍略长（10.47 对 7–10 的上沿）。

### 一条关键教训

`Safety = 20` 是 v1 空旷地图上凭 5 局与 2 颗种子试出的初值，注释里标着"阶段 B 的首个校准项"，校准从未做过，
而此后每一轮基线都建立在它之上。`denser-map` 改图后它开始主导结果，害得几轮数据的归因都错了
（不收敛率翻倍与滚雪球回潮，一度全记在地图头上）。九档各 200 局扫档后改为 5，全部指标回到目标附近。
**标着"待校准"的常量不能当基线用。**

### 仍未解决

按名次分组统计（第 5 大回合起，120 局）：四家部署上限都是约 5，实际落子 1.61 / 0.92 / 0.68 / 0.75，
Pass 率 22.9% / 42.7% / 52.8% / 45.2%。**落后者不缺棋子额度，缺的是值得落的点**——这解释了为什么
`catch-up-recruit` 的征募端补偿在密地图上失效。补偿要给空间而不是牌，需要独立一轮。

## 下一步（用户已排定的顺序）

用户原话：「先做1.2 然后34567 后续的待定」。

| # | 事项 | 状态 |
|---|---|---|
| 1–2 | catch-up-recruit 收尾与补偿强度定夺 | 已归档 |
| 3 | 冲突偏晚 | 已归档（denser-map，6.52 → 5.46） |
| 4 | 对局偏长 | 已归档（denser-map，终局局 11.15 → 9.43） |
| 5 | 2000 局基线 | 已完成，见上方数据现状 |
| 6 | 棋盘坐标标注 | 已归档。人工检查清单在 `art/coord-labels/README.md` |
| 7 | 插旗竞争（四家抢同一出生区） | 未开始。建议排在"落后者无处可下"之后，它改开局动态，两者会互相干扰 |
| — | **去围棋化第二轮 `scoring-sites`** | 已完成、已归档、未推送，见上方「去围棋化改造」。领先者胜率已由 Safety 27 拉回 25.5%；"落后者无处可下"需在新口径下重新统计再议 |

后续待定：2/3 人地图、联网、带入带出、美术音效。

新增的待办（本轮发现，不在用户清单里）：

- `strict-cli`：未知命令行选项被静默忽略。实测 `--matches 200` 被完全忽略、跑成 1 局却正常写出 summary。规格已写好待执行。
- 落后者"无处可下"：见上方数据现状，需要独立一轮。
- `maps/siege-4p-base-v1.json`、`v2.json` 都已无法加载（v1 出生区 15 格越界、v2 可落子 85 低于新区间 95–110），只能人工比对。
- `play` 终端视图只区分水 / 岩石，不画高度、桥、栅栏（`map` 子命令的文本图是完整的）。
- 保护期规则（前三大回合不得出区）在高台出生后可能多余，裁决为三轮都不动、基线后再议。

2000 局基线的命令（等权重落地后再跑）：
```bash
dotnet run --project src/Siege.Sim -c Release -- run --out sim-out/baseline --seed 1 --count 2000 --difficulty Standard --gzip
dotnet run --project src/Siege.Sim -c Release -- analyze --dir sim-out/baseline
```

## 执行流程（每个子任务）

```
opsx:propose 开 change → task.py create + start
  → Agent(trellis-implement)   写代码 + 测试 + 变异验证记录，不 commit，不改 openspec/
  → Agent(trellis-check)       对照规格审 + 自修小问题 + 自做变异，待决写清
  → 主会话核实（真实退出码跑 build/test、自做一条变异）
  → 裁定待决（设计级的问用户，常规的自己定并写明）
  → trellis-update-spec        学到的写进 .trellis/spec/core/
  → git commit -F msgfile      提交与归档之间用 &&
  → task.py archive + openspec archive --yes → 提交 → push
```

派发前把待决项拟成编号裁决 + 派发方式选项，一次向用户确认（用户全局规则）；体量大的任务顺序分段派，
段与段之间落本地 wip 提交（不 push）。

## 规范要点（`.trellis/spec/core/` 的浓缩）

- **零 Godot 依赖**：`Siege.Core` / `Siege.Sim` / `Siege.Presentation` 不得引用 `Godot.*`，csproj 有守门；`src/godot/` 是唯一例外
- **单一实现**：四邻接只在 `Adjacency.Neighbors`；坐标映射只在 `Coord`（表现层的 `Coord`↔3D 只在 `BoardGeometry`）；覆盖只在 `CoverageMap`；结算顺序只在 `SettlementDriver`；乘倍率取整只在 `Multiplier`
- **禁止浮点**：计分 / 倍率 / AI 评价全整数；浮点只允许在 `Siege.Sim/Analysis/` 与表现层渲染
- **随机子流隔离**：`GameSeed.Stream("relic-gen"|"recruit"|"setup"|"ai-P{n}"|"sim-sample")`，不用 `System.Random`
- **围棋记法坐标**：`A1` 左下，列跳过 `I`
- **信息边界**：公开视图结构上不含私有字段；正式 AI 与界面共用同一 `MatchPublicView`；调试入口 `internal`
- **UI 不做规则计算**：表现层只消费富预演与视图模型
- **一切实时重算**：不缓存不增量
- **新守门测试必须做变异验证**并记录；反射闭包守门要对非根类型做变异；**不在 sln 里的工程（`src/godot/`）对守门隐身，必须补源码级扫描**
- **共用状态机会在最外层被复写**：把完整语义暴露成方法，并断言两条入口逐步等价
- **红测变绿后整段复审**；含集合字段的 record 不能直接 `Assert.Equal`；逐字节比对要配行数下界与字段级投影
- **提交前用真实退出码把关**，`dotnet test | tail` 会吞退出码；提交信息用 `-F`
- **标着"待校准/初值/暂定"的常量不是基线**，别在上面盖楼——Safety=20 害得几轮基线的归因都错了
- **调参要双向扫**，只朝一个方向试探会得出方向相反的结论；换了环境（地图、规模）旧调参经验必须重验，被推翻的注释要整段删掉
- **静默忽略的输入会产出口径错误的数据**：产出裁决数据的入口，未识别的输入必须报错退出，不得回退到缺省值

## 环境

- Windows 11，Git Bash，28 逻辑核。`python` 可用，`python3` 不可用；`dotnet test` 输出为中文本地化 GBK，脚本抓失败名用 ASCII `[FAIL]`
- .NET SDK 8.0.425；`dotnet build` / `dotnet test`（不接受 `--no-incremental`）
- Godot **4.7.2 .NET 版**：`D:\software\godot\Godot_v4.7.2-stable_mono_win64\`，命令行用 `..._console.exe`
  - 构建 C#：`--headless --path src/godot --build-solutions --quit`
  - 自动演示：`--headless --path src/godot --quit-after 3000 -- --auto-demo`
  - 自定义参数必须放在 `--` 之后
- 跑局用 Release；输出目录 `sim-out/` 已 gitignore（`out/` 也已加入）。**跑局参数是 `--count` 不是 `--matches`，未知参数会被静默忽略**（见 `strict-cli`）
- 4 人基准地图：`siege-4p-base-v3`（内置于 `FourPlayerBaseMap`，导出物 `maps/siege-4p-base-v3.json`，`dotnet run --project src/Siege.Sim -c Release -- map` 打印文本图）——C4 旋转对称、外接 13×13、可落子 105（h0/h1/h2 = 45/8/52）、岩石 36、深水 32（桥 4 = 咽喉 G4 D7 K7 G10）、栅栏 4、林地 4；四区各 13 格全 h=2 + 2 格缓坡；中央环河岛 21 格；信物 13（出生区 8 + 桥头 4 + 岛心 G7 高档 1）；四区沿气边到公共信物 / 中央入口 / 咽喉距离 5/7/4 极差 0
- 子 agent 的会话里**没有 codegraph 工具**（MCP 未透传），派发时要给出已知落点，否则它会退回全仓遍历

## 可直接粘贴的开场 prompt

```
读 E:\wws\geme_demo\HANDOFF.md。首轮原型 8/8 已完成，图形版可玩。
按我的指示继续：改界面 / 跑 2000 局基线 / 开数值调整 change / 第二轮规则。
全程遵守 .trellis/spec/core/ 四份规范；派子 agent 前先把待决项拟成编号裁决向我确认。
每个任务结束汇报：测试数、变异验证情况、待决项。设计级的待决问我，常规的自己定并说明。
使用中文，每次回答开头报数（ws:N）。
```
