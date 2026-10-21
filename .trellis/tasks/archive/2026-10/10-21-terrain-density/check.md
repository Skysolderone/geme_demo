# terrain-density 质检记录（trellis-check，2026-10-22）

基线：HEAD ea62f43 + 实施方未提交改动；规格以 change 差分（含 N2 / N3 / N5 已改）为准。

## 逐项结论

| # | 项 | 结论 |
|---|---|---|
| 1 | 规格对照 | 已修复：「换锚」原来没有同名测试（只在 `三加二布局` 的 seed 6 分支里断言"o2 比 o1 高"，没断言规格写的"阻挡来自 o3"），补 `初始布局Tests.换锚`；其余 Scenario 均有同名测试（「全图可落子格不超过 230」= `全图可落子格不超过230`，「出生区是出生棋盘的核心区」7×6 = `棋盘清单Tests` 同名），数字与校验器声明一致 |
| 2 | 随机消费不变 | 通过：`Layout` 代码未动，宽高仍每维一次 `NextInt(max − min + 1)`；主题洗牌 → 逐块地形 → 出生区信物 → 公共信物 → 新地表子流无增删调用；`RejectionBound(2)` / `(3)` 拒绝概率均为 2^-63 量级；`SmallMapGoldenHash` 一行不在 diff 内 |
| 3 | N1 推算口径 | 已修复（补守门）：中间带 7 / 11 / 16，分母 16 − 7 = 9 恒正，6×6 / 6×7 / 7×6 对 (2,3) (2,4) (0,2) 都取下限；原先没有测试守端点推算，补 `主题地形Tests.面积缩放端点随边长上限推算` |
| 4 | 校验器 | 通过：链接行 6–7 / 170–245 / 22–25 / 16–18；8×6 只报 `BOARD_SIZE_OUT_OF_RANGE` 一条；`棋盘被切断` 仍以 `Assert.Single(..., f => f.Code == "BOARD_SPLIT")` 断言切断码与孤岛坐标，另有反面，覆盖未丢（见裁决 C2） |
| 5 | 重钉 | 通过：5 份基线首部只有 digest 变、行数 54 不变，首个分歧都在第 1 条决策行且只是落点坐标（征募段 `R:` 逐字相同）：seed1 P0 G3 → C6；seed2 P3 第三子 T6 → W7；seed3 P2 G3 / D6 / F6 → F4 / C6 / F6；easy P0 D3 → E4；hard P0 G3 / D6 / F6 → E4 匠人搭桥 D4 + F4 / C6（与 notes 2.2 一致）；每个黄金值都有"地图变了"注释；MC-C15b / C15c 补跑见下 |
| 6 | 文档 | 已修复：HANDOFF 10d 与 journal 仍写"归档时规格正文需补改 / N1 是否认可"，改为 N1–N5 已确认、差分已同步；测试条数按本次全量回写 |
| 7 | 构建与测试 | 已修复：首次全量 **红 1**——`规格档分流守门Tests.校验器对规格档的分支只在声明表里`（"声明表区间异常地长"，上限 2000 字符）：红因是 `MapValidator.Rules` 初始化式里追加的 terrain-density 区间注释两行把表区间撑过 2000；notes.md 记的全量结果（2188 / 2181 / 7）不含这一红（那两行注释可能是在那次全量之后才加的，未核）。修法：把表内 open-map / terrain-barriers / terrain-density 三段区间回写记录原样移到 `Rules` 的 summary（`<para>`），表内留一行指向；只动注释，守门上限不放宽 |

## 新增测试与变异

| 变异 | 改动 | 结果 |
|---|---|---|
| MC-C15b | `TrailsInBattlefield` 返回恒 false（`false && …`） | 红 2：`落后者不轻易停手`(False / True) |
| MC-C15c | `power.Of(p).Total > mine` → `>=` | 红 1：`并列最高视为最高` |
| M-N1 | `MaxMiddleArea` 钉成 27（6–8 时的值） | 红 1：`面积缩放端点随边长上限推算(7, 7, top: True)`；改前 `主题地形Tests` 其余 21 条全绿（说明此前无守门） |
| M-A1 | `AttachToFirstRow` 换锚候选 `anchor < 3` → `< 2`（漏掉 o2） | 红 3：`换锚`、`三加二布局`(2) / (6) |

每次变异都已还原并 `cmp` 逐字节比对；还原后 `touch`（`cp -p` 保留旧时间戳会让增量构建沿用变异后的程序集，已发现并重跑确认）。

- `主题地形Tests.面积缩放端点随边长上限推算`（Theory 4 行）：栅栏阵、流种子 1–30；6×6 / 6×7 / 7×6 栅栏边恒 4、散布岩石恒 0；7×7 至少一颗栅栏边 > 4、至少一颗有散布岩石。
- `初始布局Tests.换锚`：link:6 的 o3 西对齐锚 o0、东端伸进 o1 北侧；测试内独立枚举 o4 贴 o1 北侧的每个列偏移都不成立（反面：link:1 同一枚举能放下）；o4 北贴 o2、共享边 ≥ 4、与 o3 不重叠、1 个连通分量。

## 构建与测试（同一时间只一个进程）

- `dotnet build siege.sln -c Release`：0 警告 0 错误；`dotnet build src/godot/Siege.Godot.csproj`（Debug）：0 警告 0 错误（修 MapValidator 后各重建一次）。
- `dotnet test tests/Siege.Core.Tests -c Release`：首跑 2193 / 通过 2185 / 失败 1 / 跳过 7，退出 1（上表第 7 项）；修后重跑 **2193 / 通过 2186 / 跳过 7，退出 0**（= 2188 + 换锚 1 + 端点 4）。
- Godot headless 四条（顺序单跑）：`--auto-demo` 0（可见格 198 / 198，自检 5 条失败 0）；`--auto-demo --pick-check --seed=5` 0（拾取 192/192）；`--map-select --auto-demo` 0（文案"边长 6–7"）；`--auto-demo --rounds=0 --seed=5` 0（自检 7 条失败 0，四步齐全）。

## 改动文件

- `src/Siege.Core/Board/MapValidator.cs`（只挪注释）
- `tests/Siege.Core.Tests/LinkMapGeneration/初始布局Tests.cs`（+ `换锚`、`FitsNorthOfO1`）
- `tests/Siege.Core.Tests/LinkMapGeneration/主题地形Tests.cs`（+ `面积缩放端点随边长上限推算`）
- `HANDOFF.md`（10d 裁决状态、测试条数）、`.trellis/workspace/rubioc/journal-1.md`（裁决状态、测试条数）
- 本文件

## 待负责人裁决

- C1 `MapValidator` 链接行 `PlateList` 的第一个元组（公共棋盘边长）仍是 (6, 8)：链接图没有公共棋盘，是死值。建议：保持不改（改了不影响任何行为，也没有测试能守住），或顺手改为 (6, 7) 求一致——二选一由负责人定，倾向保持。
- C2 「棋盘被切断」载体：N4 说"6–7 合法尺寸造不出"，只对"中间带格与外圈不连通"这种写法成立。合法尺寸下其实能触发 `BOARD_SPLIT`，例如 6×6 河流的唯一桥紧邻一侧塞 1 格岩石（桥只连到外圈那一侧），或 7×7 把核心区外一圈中间带全填成岩石 / 深水（都是推演，没有实测）。现有 8×8 夹具照样断言 `BOARD_SPLIT` 与孤岛坐标，覆盖没丢。建议：维持 N4，不另造夹具。
