# formation-tiers 段 C 人工检查清单

change `formation-tiers`（阵型）段 C：算式、军势揭示、热区、数值档位阈值、Godot 接线。取图：macOS / Apple M3，1600×900，无人值守（没有音频节点）。
每张图旁边有同名 `.log`，`[show-tier]` 一行是这张图的自证读数（揭示条目的累计文案、步档位、字号、亮环、轻震都取自遮罩与样式表）。

## 怎么生成

```bash
G=~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot
A=$PWD/art/formation-tiers             # --screenshot= 要给绝对路径；取图不加 --headless
dotnet build src/godot/Siege.Godot.csproj -v q -p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config   # Godot 读 Debug 程序集

# 示例节拍（--reveal-preview=play 总时长 9780 ms；五条示例棋串依次为 C8 / E6 / G8 / J6 / L8）
$G --path src/godot -- --reveal-preview=ladder --map=siege-4p-base-v5 --shot-cell=G7 --shot-zoom=3 --screenshot=$A/ladder.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=540  --map=siege-4p-base-v5 --shot-cell=C8 --shot-zoom=20 --screenshot=$A/only-formation.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=4100 --map=siege-4p-base-v5 --shot-cell=G8 --shot-zoom=20 --screenshot=$A/formation-step.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=4320 --map=siege-4p-base-v5 --shot-cell=G8 --shot-zoom=20 --screenshot=$A/tier3-formation.png:10
$G --path src/godot -- --reveal-preview=play --reveal-at=8320 --map=siege-4p-base-v5 --shot-cell=L8 --shot-zoom=20 --screenshot=$A/tier5-formation.png:10

# 真实对局（新局缺省计分规则 v2）
$G --path src/godot -- --auto-demo --map=siege-4p-base-v5 --shot-show=reveal --screenshot=$A/reveal-real.png:15
$G --path src/godot -- --auto-demo --map=siege-4p-base-v5 --rounds=14 --seed=6 --shot-show=reveal --screenshot=$A/reveal-real-late.png:35
```

示例棋串（`GameRoot.Reveal.cs`，军势是与 ⌊(基础 + 加值) × 1.5^(倍增子 + 阵型)⌋ 自洽的字面量，引擎层不算军势、不取档；档位按新阈值 8 / 16 / 32 / 64）：

| 落点 | 明细 | 算式 | 档 | 步数 |
|---|---|---|---|---|
| C8 | 基础 3、阵型一阶 | `3×1.5 = 4` | 一 | 3 |
| E6 | 基础 5、加值 2、倍增子 1 | `(5+2)×1.5 = 10` | 二 | 4 |
| G8 | 基础 5、加值 2、倍增子 1、阵型二阶 | `(5+2)×1.5×2.25 = 23` | 三 | 5 |
| J6 | 基础 20、加值 4、倍增子 1 | `(20+4)×1.5 = 36` | 四 | 4 |
| L8 | 基础 10、加值 4、倍增子 1、阵型三阶 | `(10+4)×1.5×3.375 = 70` | 五 | 5 |

## 看什么

| # | 条目 | 图 | 看什么 |
|---|---|---|---|
| 1 | 五档阶梯（新阈值） | `ladder.png` | 五条结果同屏：白「= 4」、淡金「= 10」、金「= 23」、橙「= 36」（一圈亮环）、红「= 70」（两圈亮环），字号逐档加大；上方小字行是完整短算式，G8、L8 两条带阵型因子（`(5+2)×1.5×2.25`、`(10+4)×1.5×3.375`），倍增在前、阵型在后 |
| 2 | 揭示多一步阵型 | `formation-step.png` | G8 停在第四步：整行是累计算式「(5+2)×1.5×2.25」，最新一步「×2.25」，二档（log：步档位 2、末步 3 档、未到末步） |
| 3 | 阵型一步之后的结果 | `tier3-formation.png` | 同一条到末步：大字「= 23」（三档、金），算式行「(5+2)×1.5×2.25」挪到上方 |
| 4 | 只有阵型 | `only-formation.png` | C8「3×1.5 = 4」：3 枚普通子在 v2 下不再是「3」，一档白字 |
| 5 | 五档 + 阵型 | `tier5-formation.png` | L8「(10+4)×1.5×3.375」→ 红色「= 70」，两圈亮环、轻震进行中（log：轻震 400‰） |
| 6 | 真实对局里的阵型 | `reveal-real.png` | 第 2 大回合紫方 B11「(6+12)×1.5×2.25 = 60」：倍增子 1 枚 + 阵型二阶，四档橙字一圈亮环；该棋串的常驻标注此刻不画 |
| 7 | 真实对局（后期） | `reveal-real-late.png` | 金方 C11「(8+17)×2.25 = 56」四档、L12「(6+2)×1.5 = 12」二档在淡出。注意：短算式只列因子数值，单看「×2.25」分不出是两枚倍增子还是阵型二阶（完整算式里阵型因子带「阵型」二字，势力层明细可查） |

## 自动验证

- `siege.sln`（Release）与 Godot 工程（Debug）构建 0 警告。
- `--auto-demo`（新局缺省 v2）：`siege-4p-base-v5` 53 帧、`board:1` 53 帧，与 tiered-number-show 记录的 53 帧相同；开局对准之后相机位姿变化 0 次；音频节点 0。帧数由 `--rounds`（跑满 4 个大回合停止）与本机自动落子节奏决定，不是 AI 走法的敏感基线；全仓没有测试或自检钉 53 帧。
- `--auto-demo --pick-check` 在 `siege-4p-base-v5`（105 / 105）、`board:1`（471 / 471）、`siege-frontier-v2`（411 / 411）通过。

## 截图覆盖不到、要实际玩一局确认的

- 五步揭示（带阵型）共 1100 ms（五档 1540 ms），一次结算多条带阵型的棋串更容易触发 1.6 秒压缩，节奏是否偏快。
- 短算式里倍增与阵型因子只写数值（`×1.5×2.25`），玩家能否分辨哪一个是阵型；需要的话可在算式行里给阵型因子换色或加标记（呈现层改动，未做）。
- 新阈值下二档以上明显变少（v2 实测揭示 64% 在一档），低档的白字是否显得过于平淡。
