# 实施说明：map-coastline

分支 `map/coastline`。三段顺序进行。

## 通用

- 构建：仓库根 `dotnet build siege.sln -nologo -v q` 零警告零错误（`TreatWarningsAsErrors` 开着）。
- 快速测试：`dotnet test tests/Siege.Core.Tests/Siege.Core.Tests.csproj --filter "Category!=Slow&Category!=Perf" -nologo -v q`（全套约 25 分钟；开发中先按 `--filter "FullyQualifiedName~LinkMapGeneration"` 等子集跑，收尾跑全套一次）。
- 代码理解一律先用 codegraph（`codegraph_explore` / `codegraph_node` / `codegraph_callers`），不整文件 Read 大文件（`BoardView.cs` 2900 行、`MapValidator.cs` 950 行）。
- 确定性：生成只用由种子派生的 `RandomStream` 与整数运算；新增随机消费放在独立子流（`MapRandom.ForLinkCoast(seed, players)` 之类，仿 `ForLinkSurfaces`），不打乱既有布局流的消费顺序说明也要同步改注释。
- 子 agent 不提交；回报改动文件、自证输出行、疑虑。
- 行尾：Core 文件看 HEAD（多为 CRLF），未改动行保持原行尾；`git diff --stat` 与 `--ignore-space-at-eol --stat` 一致。

## 段F：Core（生成器 / 校验器 / 规格 / 测试）

1. **岸线**（裁决 1）：`LinkMapGenerator.Island` 的 `Contains` / `OnEdge` / `Cells` 改为按一张"岛形掩码"判定——掩码由矩形切角 + 噪声海湾得到：四角切角直角边 4–7 随机；沿四条边按坐标散列 / 子流抽若干海湾，每个海湾向内 1–4 格、沿边 3–9 格、轮廓用整数噪声咬出不规则边；岛形必须 4 邻连通、不能把座位 + 边距（`EdgeGap`）、坡道朝外格、中立平台 + `PlateGap` 咬掉（先放平台再啃边，或先啃边再放平台但保证保护区——由你定，写明理由）。画布尺寸不变，岛外仍是障碍格。
2. **内陆**（裁决 3）：`TryLowland` 里河道改 1–2 格宽蜿蜒（仍每条恰 2 座桥、桥不相邻）；新增湖泊（未架桥深水团 4–9 格）与岩区（岩石团 3–6 格），数量按人数 / 面积定；都不进平台与保护区；全岛可落子格沿气边连通（`Battlefields` / 既有 BFS）不通即重抽低地。
3. **占比**（裁决 2）：生成后统计非可落子格占岛面比例（岛面 = 掩码内格数），目标 30%±5；达不到就在同一条子流上加湖泊 / 岩区重抽（有上限，超出即整张重抽）。
4. **校验器**：`MapValidator` 链接档预算表按种子 1–20 × 2 / 3 / 4 实测重钉（向外取整，留 5% 余量）；岛与画布尺寸字段保持（外接矩形不变）。若校验器有"岛矩形 / 切角"之类的硬判断（`ISLAND_*`），改成按掩码判。
5. **规格**：`openspec/specs/link-map-generation/spec.md` 加 `### Requirement: 岛形`（海湾、切角、占比、保护区、确定性）并改预算数字；`.trellis/spec/core` 若有相关口径一并改。
6. **测试**：`tests/Siege.Core.Tests/LinkMapGeneration/*` 与 `LinkMapFixtures` / `TestMaps` 里钉死旧岛形或旧格数的断言与快照改成新口径（逐条列出改了什么、为什么）；新增：种子 1–20 × 人数的占比区间、岛形连通、保护区完整、海湾存在（至少 1 处岛缘格被咬掉）。
7. **自证**：一段小程序或测试输出 2 / 3 / 4 人种子 1–20 的占比分布（最小 / 中位 / 最大）、生成耗时（改前 / 改后）；`dotnet test` 全套结果。

## 段G：Godot（虚空识别 / 海湾断崖）

1. `BoardView.IslandCells`：不再按行跨度，改为"画布内不与留白连通的格"——从画布四边的障碍格出发沿 4 邻在障碍格里泛洪，泛到的障碍格是虚空；其余格（可落子 / 深水 / 被包住的障碍）是岛面。
2. `AddFloatingIsland` 的 border 段与断崖沿新岛形落（海湾里也有断崖、瀑布口规则不变）；`IslandAt` 用同一份集合。
3. 内陆岩区仍走障碍装饰（岩石 / 水晶 / 火盆 / 拱门 / 宝箱挑选）；海湾格不铺衬底、不放装饰。
4. 相机可行矩形仍按画布 / 格包围盒算，不动。
5. 自证：`--pick-check --auto-demo --rounds=1 --seed=5 --map=link:5:4 --mute` 退出码 0 且合计 N / N；`--map=link:5:2`、`link:5:3`、`link:5:4` 各一张 `--overview` 截图；运行日志 grep 无资源错误。

## 段H：试玩检查

1. `dotnet run --project src/Siege.Sim -- run --count 1 --players 4 --seed 7`（命令以仓库 README / 既往任务为准）跑到终局。
2. Godot 自动演示 2 / 3 / 4 人各到第 2 大回合截图（默认机位 + 全局）。
3. 记录：AI 每步耗时与改前对比（只记录，不修）。
