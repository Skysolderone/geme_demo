# 09-20-map-generator 实施记录

每段追加：改了什么 / 既有测试改写逐条 / 变异验证逐条 / 待决。

## 段 A——生成器与校验闭环（tasks 1.1–1.8，2026-09-20）

基线 1085 项 → 段末 **1171 项全绿**（新增 86），`dotnet build -c Release --no-incremental` 0 警告，`dotnet build src/godot/Siege.Godot.csproj` 通过。未提交。

### 改动文件

| 文件 | 内容 |
|---|---|
| `src/Siege.Core/Board/Maps/MapRandom.cs`（新） | 地图随机源：`ForAttempt(地图种子, 尝试序号)` → `RandomStream`。直接用既有 `SplitMix64` 展开 + `RandomStream` 的 internal 构造器，**没有改 `GameSeed` / `RandomStream` 一个字**，既有子流黄金值不受影响 |
| `src/Siege.Core/Board/Maps/MapGenParameters.cs`（新） | `MapGenParameters`（平台数 5–8 缺省 6、规格档缺省边疆档，`EnsureValid` 越界 / 标准档报错）；`GeneratedMapId`（`Parse` / `Format` / `Normalize` / `IsGenerated` / `IsBareRequest`——标识解析与规范化的唯一实现，段 B 的 `MapCatalog` 调它） |
| `src/Siege.Core/Board/Maps/FrontierMapGenerator.cs`（新） | 公开入口 `Generate(ulong, MapGenParameters?, int maxAttempts = 64)` / `Generate(string 标识)` / `GenerateDetailed`（带尝试序号与各平台方块）；校验闭环（构造 → `MapValidator` → 布局规则自检，任一不过 k+1）；`MapGenerationException`；工作态灌成 `MapData`（写法照 `FrontierMapV1`） |
| `src/Siege.Core/Board/Maps/FrontierMapLayout.cs`（新，internal） | 一次尝试的工作态（二维数组 + 列表，无散列容器）与六步构造 |
| `tests/Siege.Core.Tests/MapGenFixtures.cs`（新） | 样本口径（种子 1–50 × 平台数 5–8）、生成结果缓存、文本图 |
| `tests/Siege.Core.Tests/MapGeneration/`（新，6 个文件） | `地图随机源Tests`、`生成参数与标识Tests`、`生成图布局规则Tests`、`生成校验闭环Tests`、`生成确定性Tests`、`布局速览Tests` |
| `tests/Siege.Core.Tests/TerrainEditing/地形写入口Tests.cs` | 白名单加一行，见下 |

放在 `Board/Maps/` 下是因为守门「规格档只被地图数据文件格式与校验器引用」只放行该目录写 `MapProfile`。`MapCatalog`、v1–v4 JSON、`maps/siege-frontier-v1.json` 均未动。

### 做法要点（与 design D1 的对应）

1. **摆位**：先按 D1 原文做了"逐个拒绝采样 / 枚举合法位置随机取"，实测首次尝试九成以上放不下（25×30 扣掉 2 格外缘、两两隔 2 格、小平台贴广场之后，纵向只剩 Tm+Bm ≤ 15–17 的余量）。改为**三条带**：中间带 = 广场 + 分居两侧的两个最小平台（隔 1 格为主、三成隔 2 格），两条外围带各 1–3 个平台；带的走向（横 / 竖）、平台分带、带内次序、余量分配、广场位置全由随机源定。边长组合带约束重采（Σ边长² ≤ 320−5N；信物总数 14–24；三条带两个方向都摆得下；平台数 7 / 8 时抽样权重偏向小边长），取到即必然摆得下。
2. **缓坡**：开在方块外一格；小平台第一处正对广场中间三行（列），其余平台第一处朝广场方向，五成概率在另一条轴上再开一处。
3. **走廊**：广场 + 各缓坡口为结点，Prim 最小生成树 + 1–2 条冗余边；每条边用带方向状态的整数代价最短路（并线便宜、拐弯加价、贴外圈加价；优先队列键 = 代价×2³²+入队序号，全序无并列），沿中线刻 2 宽（七成）或 3 宽，外拐角补齐；可借道缓坡格（两平台缓坡背靠背时）。
4. **主河**：横 / 竖随机，带噪声代价的最短路，端点只取"笔直往里三格都流得进"的边缘格，不沿外缘两圈流，避开平台 / 缓坡 / 广场；压到走廊即桥。要求桥 ≥ 2 座（相邻桥格算一座）且桥格 ≤ 10，否则换端点重来，8 次不行作废本次尝试。
5. **填充与点缀**：外圈外海、次外圈多半水、内陆岩石、2–4 处小水塘（不挨主河）；平台内撒岩石（每平台 边长−4 起、≤ 20%，数量同时把可落子总数往目标 340–380 拉；每撒一格自检平台内仍连成一块，不合格撤回；不撒四角与缓坡正对格）；总数仍低于目标则在走廊边补空地（优先补凹口，两成林地）；林地 = 广场角 1–2 + 走廊 1–2；栅栏 1–2 段，每放一段自检全图仍一块、否则撤回。
6. **布点**：营帐 = 平台内离缓坡最远格；石碑 = 广场外圈风车形 4 块（旋向随机）；公共信物 = 中心高档 + 桥头优先（每座桥一个、两岸交错、至多 4）+ 走廊上拉开补足 6 个标准档；篝火 = 各平台缓坡口外 2–3 步（广场内不放，放不下改放别处走廊、彼此拉开）；咽喉 = 全部缓坡格 + 桥格。
7. **自检**：构造末尾要求全部可落子格沿气边连成一块（⇒ 无口袋、五类目标必可达）；校验后再查 80%、桥 / 栅栏 / 林地、布点数量、"小平台靠中央"（取 `MapValidator.DistanceTable` 的中央入口一行）。

### 既有测试改写（逐条）

1. `地形写入口Tests.地形写入口之外不得构造改造后的地形`：`new TerrainData(` 白名单加 `Siege.Core.Board.Maps.FrontierMapGenerator`，注释同步改为"地图定义（v4、边疆手工图、边疆档生成器）"。原因：生成器与两张内置图同属"地图定义"，只在 `ToMapData` 一处构造；与 frontier-map 段 B 加 `FrontierMapV1` 同一做法。变异 MG-7 证明这一行是必需的（拿掉即红）。断言与期望值未改。

除此之外没有触及任何既有测试。

### 变异验证（逐条；脚本二进制读写、还原放 finally，跑完全量复绿 1171）

| 编号 | 变异 | 结果 |
|---|---|---|
| MG-1 | `MapRandom` 域分隔常量末位 D→E | 红 1：`地图随机源的黄金值` |
| MG-2 | `FrontierMapLayout.cs` 注释里写一处 `GameSeed` | 红 1：`生成器不见对局种子_对局流程不见地图种子` |
| MG-3 | `Match/MatchFlow.cs` 末尾加一个引用 `MapGenParameters` 的类型 | 红 1：同上 |
| MG-4 | `GenerateDetailed` 循环条件 `<` → `<=`（多试一次） | 红 1：`尝试耗尽即报错并给出原因_不返回地图` |
| MG-5 | `RaiseFences` 里引入 `HashSet<P>` | 红 1：`生成器源码不含散列次序遍历_浮点_时钟与环境` |
| MG-6 | `RaiseFences` 里引入 `double` 局部变量 | 红 1：同上 |
| MG-7 | 白名单里的 `FrontierMapGenerator` 改名 | 红 1：`地形写入口之外不得构造改造后的地形` |
| MG-8 | `PlatformGap` 2 → 1 | 红 2：`平台规整`、`缓坡…` |
| MG-9 | `Format` 的"缺省平台数省略该段"判断改恒假 | 红 1：`标识规范化` |
| MG-10 | 中央入口信物高档 → 标准档 | 红 1：`资源布点` |
| MG-11 | `EnsureValid` 上界写成 `> 9` | 红 1：`平台数越界报错并指出合法范围` |

说明：1.5 要求的"把集合遍历改成依赖哈希次序应红"做不到稳定复现（`Coord` / 值类型的散列集合只增不删时按插入序遍历，字节比对不变红），按 tasks 的退路改为守门扫描（MG-5 / MG-6）。脚本第一版按测试名正则判红，漏了带参数的 Theory 名（MG-8 / MG-10 误报绿），改口径后复跑为红；MG-11 第一版（去掉 `GenerateDetailed` 里的 `EnsureValid`）为绿——因为 `GeneratedMapId.Format` 也调它，属重复防线而非测试缺口，换成上表的变异后为红。

### 种子 1–50 统计（Release，本机）

| 平台数 | 校验通过所用尝试序号分布 | 可落子格 | 单张耗时中位数 / 最大 |
|---|---|---|---|
| 5 | 0→29，1→8，2→9，3→4 | 340–380 | 10 ms / 113 ms（首张含 JIT） |
| 6 | 0→36，1→11，2→2，3→1 | 343–388 | 8 ms / 11 ms |
| 7 | 0→30，1→15，2→3，3→1，4→1 | 342–398 | 8 ms / 13 ms |
| 8 | 0→29，1→14，2→2，3→1，4→2，5→1，9→1 | 345–378 | 9 ms / 17 ms |

200 张无一耗尽 64 次（最坏 9）。另抽种子 1000–1399 看首次尝试：通过率约 54–66%，作废原因几乎全是"主河凑不出 2 座桥 / 桥格过多"，其余为"小平台靠中央"不满足（< 1%）；**没有一次是 `MapValidator` 拒绝**——构造式保证覆盖了校验器的全部拒绝项。

布局速览：`sim-out/mapgen-gallery.txt`（种子 1–50、平台数 6，每张：标识 / 尝试序号 / 可落子格 / 信物 / 据点 / 桥数 / 各平台"编号:边长/到中央入口距离" / 栅栏 / 文本图）；同目录 `mapgen-stats.txt`（上表）、`mapgen-samples.txt`（平台数 5 / 6 / 8 各一张）。重出：PowerShell `$env:SIEGE_MAPGEN_GALLERY=1; dotnet test -c Release --filter 布局速览`（`布局速览Tests` 平时直接返回、不写盘）。

### 偏离与待决

1. **摆位算法偏离 D1 第 1 步原文**（拒绝采样 → 三条带 + 约束重采），原因与数据见上。后果：两个最小平台总是分居广场两侧；外围平台总在两条带里。是否接受，或要更"散"的布局，待负责人看速览后定。
2. **边长不是独立均匀的 5–9**：受面积上限、信物预算与可摆性约束；平台数 7 / 8 时以 5–6 为主。边长 9 只在摆得下时出现。
3. **桥的口径**：规格"桥不少于 2 座"按"相邻桥格算一座、至少 2 座"实现；带间只有 2 格宽，主河常顺着缓坡口那一行流，会出现 3–6 格宽的长桥（桥格上限 10）。观感是否可接受待看图。
4. **走廊宽度**：补空地后局部会宽于 3 格（成小块空地）；贴外圈的走廊偶有 1 格宽。规格的"宽 2–3 格"未做成硬断言。
5. **`MapGenParameters.Profile`**（缺省边疆档，标准档报 `NotSupportedException`）是为了让"标准档不可生成"有可测入口而加的，待追认。
6. **尝试上限**是 `Generate` 的方法参数而非 `MapGenParameters` 的字段（它不是地图描述的一部分，不进标识）。
7. **没有钉整张生成图的黄金值**，只钉了随机源派生的黄金值：段 B / C 若按速览反馈调生成器，图必然变；回放安全靠 D5 的内容摘要。生成器定稿后建议补一条"`gen:12345` 导出文本摘要"的黄金值。
8. 规格 Scenario「对局种子不影响地图」「地图种子不扰动对局随机」需要对局入口（`MapCatalog` 接 `gen:`），留给段 B；本段以接口与守门保证（生成器签名里没有对局种子、源码不见 `GameSeed`；既有黄金值测试全绿）。
9. **`布局速览Tests` 作为常驻测试保留**（环境变量门控，平时直接返回，计入 1171）：tasks 1.7 原文是"测试外的小入口或临时测试"；保留是为了负责人看图后调生成器时能一条命令重出速览。它是一条恒绿的非断言测试，是否保留 / 挪到 `Siege.Sim` 的 `map` 子命令（段 B 2.6）待定。
10. **闭环测试与"样本里有重试过的图"耦合**：`生成校验闭环Tests.FirstRetried()` 与 `InRange(worst, 1, 31)` 隐含种子 1–50 × 平台数 5–8 里至少一张用到了第 1 次以上的尝试（现状 200 张里 70 余张）。生成器以后若调到全部第 0 次通过，这两条会红且与规格无关——届时把下界改 0、并扩大搜种子的范围或加一个注入式的失败缝。
11. **流程说明**：随机源 / 参数 / 标识是先定接口再补测试，生成六步是"先跑通 + 统计失败原因 → 再写规格测试"，不是严格的测试先行；补救手段是 11 条变异逐条见红。


## 段 A 检查（2026-09-20）

段末 **1187 项全绿**（1171 → +16），`dotnet build -c Release --no-incremental` 0 警告，`dotnet build src/godot/Siege.Godot.csproj` 通过，`openspec validate map-generator --strict` 通过。未提交。**生成器的产物整体变了**（主河与走廊的次序对调），段 A 记录里的统计表与速览作废，以本节为准。

### 负责人七条处理的落实

1. **三条带**：design D1 第 1 步改写；裁决记录加第 11 条。
2. **长桥**：按建议先做了"顺走廊连压加价 + 连压 ≤ 3 的硬约束"（河道最短路带连压状态）——实测首次尝试通过率只剩 3–17%，`gen:3:p5` 耗尽 64 次：带与带之间只有 2 格、整条都是走廊，走廊网刻好之后河只能顺着走廊流，没有垂直穿过的余地。**改为主河先于走廊**：缓坡 → 主河（只流经空格，经一处"隘口"——两侧 7 格内都有平台 / 广场的空格——贯穿到对边，拐弯加价、多数时候垂直于三条带、端点取离隘口最近的几个，要求两岸都有缓坡）→ 走廊（只能在河道笔直处垂直过河、河上不拐弯、并排那条道一并架桥、桥头一并刻开；新架桥贵、走现成桥便宜）→ 桥不足 2 座时逼一条不走现成桥的走廊。硬约束：每座桥沿河 ≤ 3、桥格 ≤ 10、≥ 2 座（构造末尾与 `CheckLayout` 各查一遍，后者从 `MapData` 只做坐标算术）。规格加 Scenario「桥的长度上限」；design 裁决 12。
3. **走廊宽度 ≥ 2**：两条口径同时成立、无豁免——① 每个过渡带格（平台外的可落子格）属于某个 2×2 过渡带方块；② 拿掉任一过渡带格，每个平台仍能沿气边到中央入口。②是检查中加的：2×2 口径管不到"两个方块只搭一个角"与**栅栏拦掉 2 格宽走廊的一条道**——改前 200 张里 53 张有这种"一子堵死"格（100 张有 1 格宽处，150 张有 > 3 格的桥）。做法：中线不走拓不宽的窄缝；刻完逐格补成 2×2；补空地只补"补上即成 2×2"的格（补不动且可落子 ≥ 310 就到此为止，不再为凑 340–380 长刺）；栅栏只放在 ≥ 3 格宽处，放不下退到广场内；构造末尾终检。规格 bullet 改为"全长至少 2 格宽（主干 2–3 格，局部可更宽）"并加 Scenario「走廊宽度」。
4. 追认，无改动。
5. **黄金值**：`生成确定性Tests.生成图的黄金值_gen12345的导出文本摘要`（尝试序号 8、可落子 334、SHA-256）。
6. **布局速览**：确认门控在任何生成之前返回（第一条语句），不拖慢套件；标题行改为列出每座桥的格数。
7. **闭环注入缝**：`FrontierMapGenerator.GenerateDetailed(seed, parameters, maxAttempts, forcedFailures)`（internal，前 k 次直接判不通过）；两条闭环测试改用它，`InRange(worst, 0, 31)`，不再依赖样本分布。

### 其余检查结论

- **确定性**：生成路径无散列容器 / 浮点 / 时钟 / 环境；排序只有三处且键全序（`Array.Sort(int[])`、`pairs.Sort()` 键 (距离,a,b)、桥头按 (行,列)）；优先队列键 = 代价×2³²+入队序号。守门扫描加固：扫全部 `FrontierMapLayout*.cs`；补 `Immutable*` / `Frozen*` / `ISet` / `AsParallel` / `ToLookup`、`MathF` / `Half` / 浮点字面量、`TimeProvider` / `HashCode` / `RandomNumberGenerator`；生成器里禁止对 `map.*` 的散列容器 foreach 或取 First。
- **隔离**：`src/Siege.Core/Determinism`、`Match` 零 diff。既有测试里没有逐子流的黄金值（只有 SplitMix / xoshiro 参考向量与对局级回归），补了 `地图随机源Tests.对局四条子流的取值不因地图随机源而变`（relic-gen / recruit / setup / zone-pick 首值）。
- **校验**：生成图走的是 `MapValidator.Validate` 全套（测试里另经 `GameBoard.Load`），不是只过自检。已核对边疆档路径含 `ValidatePockets`（必死口袋，逐个连通块、碰到出生区即判）与 `ValidateBirthZones`（保护期容量 `BIRTH_ZONE_TOO_SMALL`，`for` 逐个出生区）；200 张 `PocketExemptions` 为空。最终代码上重出三份速览文件，与统计表逐字节一致（耗时列除外）。
- **实现缺陷（新测试抓到）**：并排那条道在河上架了桥、岸上却因被挡改刻另一侧，留下上不了岸的桥 → 架桥时两头一并刻开。
- **既有守门**：`四邻接Tests.几何邻居枚举只在允许名单内直接调用` 不许 Core 新增 `Adjacency.Neighbors` 调用者 → `CheckBridges` 改成纯坐标算术，未动名单。
- **代码组织**：`FrontierMapLayout.cs` 拆成 8 个 partial 文件（主文件 + Platforms / Ramps / Corridors / River / Fill / Sites / Checks）；拆分前后 200 张图的导出摘要逐张相同。两条守门测试的文件名单改为通配。
- v4 / `siege-frontier-v1` 零变化（既有断言全绿，`MapCatalog`、JSON 未动）。异常路径（耗尽 / 非法标识 / 越界 / 标准档）原测试充分，未改。

### 变异验证（脚本二进制读写、finally 还原，跑完全量复绿 1187）

| 编号 | 变异 | 结果 |
|---|---|---|
| MG-4（复跑） | 循环条件 `<` → `<=` | 红：尝试耗尽 |
| MG-5（复跑，落在 Fill.cs） | 引入 `HashSet<P>` | 红：守门扫描 |
| MG-8（复跑） | `PlatformGap` 2 → 1 | 红 |
| MG-12 | `MaxBridgeSpan` 3 → 6 | 红：桥的长度上限 |
| MG-13 | 去掉 `WidenNarrowCorridors` 与 `CheckCorridorWidth` | 红：走廊宽度、gen:12345 黄金值 |
| MG-14 | 主河拐弯加价 6 → 1 | 红：gen:12345 黄金值（6 → 5 时这张图恰好不变，绿——只钉一张图，挡大改不挡微调） |
| MG-15 | `FindChokeCell` 恒返回无 | 红：走廊宽度（一子堵死口径） |
| MG-16 | 架桥时不刻桥头 | 红：桥的长度上限（两岸不通）、黄金值 |
| MG-17 | 注入缝少判一次 | 红：失败后确定性重试 |

### 种子 1–50 统计（Release，本机；`sim-out/mapgen-stats.txt`）

| 平台数 | 尝试序号分布 | 可落子格 | 中位数 / 最大耗时 |
|---|---|---|---|
| 5 | 0→10，1→10，2→4，3→6，4→5，5→2，6→3，7→3，8→1，9→3，12→1，13→1，14→1 | 312–378 | 22 ms / 163 ms |
| 6 | 0→15，1→10，2→4，3→3，4→6，5→6，6→1，8→3，11→1，12→1 | 316–407 | 14 ms / 25 ms |
| 7 | 0→15，1→9，2→7，3→7，4→3，5→5，6→2，9→1，11→1 | 326–399 | 13 ms / 23 ms |
| 8 | 0→14，1→10，2→10，3→5，4→2，5→2，6→3，7→1，9→1，13→1，16→1 | 342–394 | 14 ms / 31 ms |

200 张无一耗尽 64 次（最坏 16）；200 张的 1 格宽处 / 一子堵死格 / 超长桥均为 0。单次尝试的构造通过率约 22–35%（改前 54–66%）：作废原因依次是主河流不通（约四成——缓坡 + 缓坡口正好塞满 2 格的带间缝）、走廊过不了河、逼不出第二座桥、已有一子堵死格。

### 待负责人裁决

1. **单次尝试通过率降到 22–35%**（最坏 16 / 64，耗时仍是十几毫秒）。根因是三条带留给河与 2 格宽走廊的空间太紧；要再提高得动摆位（例如给一条带间缝留 4 格以上）。是否接受现状。
2. **河的观感**：河只流经岩石区，下半段常贴着地图一侧绕过外围带（如 `gen:21:p8` 的 W 列），有的图河在岩石里走很长一段才碰到走廊；桥都是垂直的 2–3 格。请看速览定夺。
3. **可落子格下限**：补空地不再长刺后，200 张里 37 张低于原目标 340（最低 312，仍在边疆档 300–420 内）。
4. **栅栏**：只放在 ≥ 3 格宽处，放不下时退到中央广场内部（原实现不在广场内放）。"栅栏不得拦出一子堵死的口子"是否就是想要的口径——若希望栅栏本来就用来造 1 格宽咽喉，需要把口径②对栅栏豁免。
5. 黄金值只钉 `gen:12345` 一张；段 B / C 若再调生成器，更新该值并在记录里写明。

## 段 B——入口、日志、回放、批量（tasks 2.1–2.6，2026-09-20）

基线 1187 项 → 段末 **1212 项全绿**（新增 25），`dotnet build -c Release --no-incremental` 0 警告，`dotnet build src/godot/Siege.Godot.csproj` 0 警告。未提交。**生成器一个字没动**（`gen:12345` 黄金值不变）；`src/Siege.Core/Determinism` 零 diff。

### 改动文件

| 文件 | 内容 |
|---|---|
| `src/Siege.Core/Board/Maps/MapCatalog.cs` | `Resolve` 加生成图分支（内置表之后、文件路径之前）：`gen:<种子>[:p<N>]` → `FrontierMapGenerator.Generate(标识)`；**裸 `gen` 到这里直接抛 `FormatException`**（Core 不读时钟）；未知标识的清单里说明 `gen:<地图种子>[:p<平台数 5–8>]` 写法 |
| `src/Siege.Core/Board/MapFile.cs` | `Digest(MapData)`：导出文本（行尾归一成 `
`）UTF-8 的 SHA-256 大写十六进制——与段 A 黄金值同一算式 |
| `src/Siege.Core/Match/MatchPublicView.cs`、`MatchFlow.cs` | 公开视图加 `MapId`（完整地图标识）与 `Seed`（对局种子文本），**都是字符串、分开两项**；`Publish` 取 `Board.BaseMap.Id` 与 `Seed.ToString()`。视图只有一处构造，既有测试零改动 |
| `src/Siege.Core/Match/MatchFlow.Persistence.cs` | `SavedMapId(json)`：读出存档里的完整地图标识（见下「存档现状」）。`Match` 目录仍不出现任何地图种子类型 |
| `src/Siege.Sim/Program.cs` | `MaterializeMapRequest`（internal）：裸 `gen` → 时钟取种子 → `GeneratedMapId.Format` → 打印完整标识；`play` / `map` / `run` 三处共用，都在 `EnsureRecognized` 之后、`MapCatalog.Resolve` 之前。`run` 在写 `config.json` 之前就把配置里的裸 `gen` 落成完整标识。`map` 加 `--out <文件>`；`run` 加 `--map-per-match`；`replay` 首行分开打印地图与对局种子；地图不一致时不再谈"失败是否复现"；顶层 catch 加 `MapGenerationException` |
| `src/Siege.Sim/Play/PlayCommand.cs` | 非缺省地图那一行的标识取自公开视图；生成图另注明"地图种子只决定地图，与下面的对局种子无关"。缺省地图（v4）的转录一字未变 |
| `src/Siege.Sim/Config/RunConfig.cs` | `MapPerMatch`（`bool`，`WhenWritingDefault` 不写出——v4 的 `config.json` 与首部逐字节不变）+ `MapIdAt(i)`；`Validated` 在换图时要求 `MapId` 是带起始种子的生成图标识，并在开跑前报出格式 / 平台数越界 / 种子上溢。**起始地图种子与平台数就写在 `MapId` 里，不另设字段** |
| `src/Siege.Sim/Running/BatchRunner.cs` | 换图时逐局 `MapCatalog.Resolve(MapIdAt(i))`；`ExecuteToDirectory` 先 `Validated` 再解析第 0 局的图，之后才建目录 |
| `src/Siege.Sim/Running/MatchSession.cs` | 首部写 `MapDigest`（取 `Board.BaseMap`，不是活地形）、换图批次另写 `ZoneSides`；换图配置不给地图就拒绝（会话不知道自己是第几局） |
| `src/Siege.Sim/Logging/MatchLog.cs` | `LogHeader.MapDigest`（`string?`）、`ZoneSides`（`List<int>?`）；旧日志读入为 `null` |
| `src/Siege.Sim/Running/Replayer.cs` | 先重建地图（换图批次按首部 `MapId`，其余按 `Config.MapId`——后者可能是文件路径）→ 比摘要 → 不同即返回首部分歧 + `ReplayResult.MapMismatch`，**不建局不重跑**（`Replayed` 只有一行首部）；首部无摘要则跳过 |
| `src/Siege.Sim/Analysis/BalanceAnalyzer.cs`、`ReportWriter.cs` | `BirthZoneSection.BySide`（`PlatformSideSection`：纳入 / 排除局数、基线、边长 5–9 各一行的 出现数 / 被选 / 胜率 / 显著）；批内任一局带 `MapPerMatch` 即给出，报告第 5 节改列边长、不再列平台编号；其余批次输出逐字不变 |
| `src/godot/scripts/GameRoot.cs` | `--map=gen` 同样在入口取种子并 `GD.Print` 完整标识；启动行写"对局种子"；catch 加 `MapGenerationException`。不做选图界面（段 C） |
| 测试（新，4 个文件 25 例） | `SimulationHarness/各入口支持生成图Tests`（8）、`日志首部地图摘要Tests`（5）、`每局换图Tests`（6）、`MatchSetup/对局配置公开完整地图标识Tests`（6 例，含 map-generation 留给本段的两条 Scenario） |

### 存档现状与取法

**存档只存标识、不存整张地图**：`MatchSaveData.MapId` + 盘面段（棋子与本局改造）；`MatchFlow.Restore(map, json)` 的地图由调用方提供，`RequireMap` 按标识全等把关。`src/` 里没有任何生产调用方（三个入口都不存盘），只有测试在用。取法：调用方 `MapCatalog.Resolve(MatchFlow.SavedMapId(json))` 重建地图再 `Restore`——`MatchFlow` 自己不调目录（边界：对局流程只认 `MapData` 与标识字符串）。测试：`gen:12345:p7` 上跑 6 个小回合 → 存档 → 恢复方只凭 JSON 重建 → 开局地图 / 活地形 / 盘面 / 再存档逐字节相同；给错图（`gen:12345`）抛 `FormatException`。

### 「地图种子不扰动对局随机」的黄金值

`siege-frontier-v1` + 对局种子 42：首回合顺序 `3,0,2,1`、原型选区 `1,0,5,3`、信物生成记录的 SHA-256 `CF10E3AB…E00BBC`。取自提交 `71761d6`（**生成器出现之前**）的干净 worktree（scratchpad，已删除）、同一算式；现工作树逐项相等。另：`sim-out/fb-k24/match-…C9.jsonl`（段 B 之前的边疆图日志）用现二进制 `replay`，只在第 1 行（首部多出 `MapDigest`）分歧。

### 既有测试改写（逐条）

**没有改动任何既有测试。** 首部新增 `MapDigest` 进了确定性文本，但仓库里没有钉"首部逐字节"的测试（既有的首部断言都是 `Contains` 某字段）；`MapPerMatch` 缺省不写出、`ZoneSides` 只在换图批次写，v4 / `siege-frontier-v1` 的首部只多 `MapDigest` 一项（`标准批次的首部不多出换图相关字段` 钉住）。后果与 `ZoneCount` 那次相同：段 B 之前的旧日志 `replay` 会在首部报分歧（不是"地图不一致"，对局逐步相同）——有测试。

### 变异验证（脚本二进制读写、finally 还原；每条跑段 B 相关的 9 个测试类，全部跑完后全量复绿 1212）

| 编号 | 变异 | 红 |
|---|---|---|
| MB-1 | `MapCatalog` 去掉 `gen:` 分支 | 16（凡用到生成图的新测试） |
| MB-2 | `MapCatalog` 遇裸 `gen` 自己拿固定种子生成 | 1：裸gen不进规则内核… |
| MB-3 | `MaterializeMapRequest` 原样返回裸 `gen` | 3：入口把裸gen落成…、以裸gen批量跑局…、守门 |
| MB-4 | `map` 子命令对生成图也按 `map.Id` 往 `maps/` 写 | 1：地图子命令打印生成图… |
| MB-5 | `BuildHeader` 不写 `MapDigest` | 4 |
| MB-6 | `Replayer` 去掉摘要比对 | 1：重建出的地图与首部摘要不同… |
| MB-7 | `Replayer` 把"首部没有摘要"当成不一致 | 1：旧日志缺摘要字段… |
| MB-8 | `Publish` 的 `MapId` 写空串 | 5（含既有的 `选边疆图`——终端那一行现在读公开视图） |
| MB-9 | 存档把地图标识的 `:p<N>` 段丢掉 | 1：存档只存地图标识… |
| MB-10 | `BatchRunner` 每局都用第 0 局的图 | 2 |
| MB-11 | `Replayer` 一律按 `Config.MapId` 解析 | 1：小批次每局换图…（第 2、3 局报地图不一致） |
| MB-12 | `SideOf` 少加 1 | 1：首部的平台边长… |
| MB-13 | 边长分组的被选次数误填成出现次数 | 1 |
| MB-14 | `BatchRunner` 里直接调 `FrontierMapGenerator` | 1：守门 |
| MB-15 | `PlayCommand` 里出现 `IsBareRequest(` | 1：守门 |
| MB-16 | 去掉 `MapPerMatch` 的 `WhenWritingDefault` | 1：标准批次的首部不多出… |
| MB-17 | `ZoneSides` 无条件写 | 2 |
| MB-18 | 换图批次的报告仍列平台编号 | 1 |
| MB-19 | `MapFile.Digest` 只哈希标识 | 2 |

19 条全红，无误报绿。

### 2.5 实跑：20 局每局换图

口径：4 名 Standard AI、大回合上限 15、`--serial`、其余缺省（K = 24 自动）；起始地图种子 100、平台数 6；对局种子 201–220（与 frontier-map 3.5 同一批种子）。单次前台命令有 10 分钟上限，分三批：`--map gen:100 --seed 201 --count 5`、`--map gen:105 --seed 206 --count 10`、`--map gen:115 --seed 216 --count 5`（都带 `--map-per-match`），日志合并到 `sim-out/mapgen-rotate20/` 后 `analyze`（原始三批在 `mapgen-rotate20-0..2`）。跑局期间机器上没有构建 / 测试。首部标识依次 `gen:100`…`gen:119`（逐个核对）；抽 `gen:109` 那局 `replay` 一致（278 行）。

| 指标 | 值 |
|---|---|
| 正常终局 | 20 / 20，失败 0 |
| 平均大回合数 | 14.6（292 / 20） |
| 到上限终局比例 | 95%（19 / 20 `MajorRoundLimit`；1 局第 7 大回合势力碾压） |
| AI 单步耗时 | 均值 814 ms，最大 5573 ms（1168 个小回合）；每局约 47 s |

按平台边长（基线 25%；80 个选区样本；120 个平台）：

| 边长 | 出现 | 被选 | 被选率 | 胜率（Wilson 95%） |
|---:|---:|---:|---:|---|
| 5 | 48 | 33 | 69% | 33.3%（11/33，19.8%–50.4%） |
| 6 | 27 | 20 | 74% | 25.0%（5/20，11.2%–46.9%） |
| 7 | 24 | 12 | 50% | 8.3%（1/12，1.5%–35.4%） |
| 8 | 14 | 11 | 79% | 27.3%（3/11，9.7%–56.6%） |
| 9 | 7 | 4 | 57% | 0.0%（0/4，0.0%–49.0%） |

无一显著（样本小）。对照 `siege-frontier-v1` 同种子批（K = 0 口径）：100% 到上限、单步 2075 ms——到上限比例基本没变，耗时差主要来自 K = 24。选区是种子均匀选的（不是 AI 挑的），"被选率"只反映平台数的分布，不是偏好。

### Godot 自检

`--headless -- --auto-demo --map=gen:12345` 退出码 0；`--auto-demo --pick-check --map=gen:12345` 退出码 0；另：`--map=gen`（打印 `gen:<种子>` 后正常跑完）0、`--map=gen:1:p99` 退出码 1 并说明格式与范围、不带 `--map` 的 v4 自动演示 0。

### 偏离与待决

1. **批次配置记录里的"平台数"**：起始地图种子与平台数都写在 `MapId`（如 `gen:100:p7`）里，缺省 6 时按规范化写法省略，没有单独的字段。规格原文"MUST 写明起始地图种子、平台数与每局换图这一事实"——按"标识即完整描述"（D3）判为满足；若要显式字段，加一个只读派生属性即可。
2. **换图批次里首部 `Config.MapId` 是批次的起始标识**，不是本局的图；本局的图是首部 `MapId`，回放按它重建。有意为之（批次配置如实），有测试（MB-11）。
3. **`ZoneSides` 只在换图批次写**；单张生成图的批次仍按平台编号统计（编号在同一张图上可比）。把不同批次的单图日志混在一起分析不会触发边长分组。
4. **公开视图的 `Seed` 是字符串**：规格要求对局种子公开，但视图结构里不放 `GameSeed`（否则 AI 可达类型闭包里多出随机源类型，踩信息边界守门）。表现层要显示直接用。
5. **存档没有地图摘要**：生成器以后改版，旧存档按标识会重建出另一张图，`Restore` 大概率在盘面段报错、也可能静默成功。日志有 D5 兜底，存档没有——要不要给 `MatchSaveData` 也加 `MapDigest`（旧存档缺字段跳过）待定；现在没有任何入口存盘，未做。
6. **`map --out` 对任何地图都可用**（含内置图与文件图），目标路径由用户给，不做"不得指向 `maps/`"的保护；不给 `--out` 时生成图绝不写盘。
7. 变异每条跑的是段 B 相关的 9 个测试类（约 30 秒一条）而不是全量，末尾全量复绿；MB-8 因此仍看到了既有测试 `选边疆图` 变红。
8. 段 A 待决 5 的"段 B 若再调生成器"未发生；未发现生成器缺陷。实跑 20 张图（`gen:100`–`gen:119`）全部首次解析即用、无异常。
9. **一条依赖墙钟的测试**：`以裸gen批量跑局…` 走真实入口 `run --map gen`，在时钟取到的地图种子上生成并跑 1 个大回合（入口行为只能这样测）。段 A 抽样 1000+ 种子无一耗尽 64 次尝试，风险极低；`testing.md` 无禁止条款。其余涉及裸 `gen` 的测试只验标识往返、不生成。
10. 已核 `.trellis/spec/core/determinism.md`：无"Core 不得用哈希 API"一类条款；`MapFile.Digest` 是内容指纹，不参与任何规则计算、不是随机源，位于 `Board/`（生成器守门扫描范围之外）。

## 段 B 检查（2026-09-20）

1212 → **1216 项全绿**（+4：存档摘要 Theory 2 例、旧存档缺摘要 1、`map --out` 权威文件 1；另改写 2 条裸 `gen` 测试与 1 条守门）。`dotnet build -c Release --no-incremental` 0 警告；`dotnet build src/godot/Siege.Godot.csproj` 通过；`openspec validate map-generator --strict` 通过；Godot `--headless -- --auto-demo --map=gen:12345` 退出码 0。未提交。生成器文件零改动（`gen:12345` 黄金值不变）。

### 负责人裁决的落实

| 裁决 | 处理 |
|---|---|
| 1 平台数只写在 `MapId` | 接受，无改动 |
| 2 换图批次 `Config.MapId` 是起始标识 | 已核：`src/` 里读 `Config.MapId` 的只有 `Replayer`（有 `MapPerMatch` 分支）与 `Program` 的跑局横幅；`Analysis/` 零命中，报告 / 分析没有把它当本局地图 |
| 3 公开视图 `Seed` 为字符串 | 接受，无改动 |
| 4 存档加地图摘要 | **已做**：`MatchSaveData.MapDigest`（`MapFile.Digest(Board.BaseMap)`，与日志首部同一算法、同取开局地图）；`RequireMap` 在标识全等之后比摘要，不同抛 `FormatException("地图不一致：…两个摘要…未恢复")`——`Restore` 与 `RestoreUnvalidated` 共用这一处；旧存档缺字段跳过比对，`MatchFlow.MapDigestBackfilled = true`（照 `*Backfilled` 口径），再存档按当前地图补写。`Siege.Core/Match` 仍不出现地图种子类型（只引 `Board.MapFile`），守门绿 |
| 5 `map --out` 不得覆盖权威文件 | **已做**：`Program.RequireNotAuthoritativeMapFile`——目标的父目录名为 `maps`、文件名为 `<内置标识>.json`（都不分大小写、与当前目录无关、文件存在与否都拒）→ `ArgumentException`，退出码 1，先于一切输出、不建目录；`maps/` 下的新文件名与别处的同名文件允许 |
| 6 取地图种子的时钟可注入 | **已做**：`Program.Main(args)` → `internal Execute(args, Func<ulong> mapSeedSource)`，缺省 `ClockMapSeed`（`Program.cs` 里唯一一处 `Stopwatch.GetTimestamp()`）；`play` / `map` / `run` 共用 `MaterializeMapRequest(mapId, output, mapSeedSource)`。测试注入固定值，**不再有依赖墙钟的测试**；不该取种子的测试注入"一调用就抛"的来源。Godot 侧保持读时钟 |

### 检查中另外发现并修掉的

1. `BalanceAnalyzer.SideStats` 把 `ZoneStats` 的"被选 / 获胜"计数循环**复制了一份**（重点 4"没有复制一份算式"）→ 抽出 `TallyPicks(log, zoneCount, groupOf, picks, wins)`，各区胜率（键 = 区号）与按边长分组（键 = 该局该区的边长）走同一份；Wilson / 基线 / 显著判定本来就是共用的。既有手算值测试全绿。
2. `Program.Run` 里那句注释说"裸 gen + 每局换图在读入校验时就会报错"，只对**配置文件**成立；命令行 `--map gen --map-per-match` 实际会拿取到的种子当起始种子（打印并记入 `config.json`）。行为合理，注释改成实际行为，并加测试钉住（注入 100 → 首部 `gen:100`、`gen:101`）。
3. 裸 `gen` 落盘的反例补齐（重点 1）：经 `--config` 文件里的 `"MapId": "gen"`、经 `--map gen --map-per-match`、绕过入口直接给 `BatchRunner.ExecuteToDirectory` / `MatchSession.Create`（抛 `FormatException` 且不建输出目录）；`config.json`、每局首部、首部内嵌配置三处都断言没有 `"gen"`。守门另加"`Siege.Core` 源码不出现 `Stopwatch.` / `DateTime.Now` / `Environment.TickCount` / `Random.Shared` / `new Random(`"。

### 已核对、无需改动

- 回放：重建地图 → 比摘要 → 才建局重跑；内置图同样写摘要；旧日志缺字段跳过；改一格后首部报"地图不一致"且 `Replayed` 无小回合；换图批次按首部 `MapId` 重建（MB-6 / 7 / 11 已证）。
- 两条 Scenario 测试确实走 `MatchFlow.Create`（对局入口）；黄金值来自 `71761d6` 一事只能信实施记录，**未独立复算**。
- `MatchPublicView.MapId / Seed` 都是 `string`；AI 可达类型闭包无新增随机源 / 地图种子类型；`地图随机源Tests` 的 Match 目录守门未被绕过（令牌表不含 `MapFile`，存档摘要不触发它）。
- 非法 `gen:` 标识与平台数越界：Sim 三个子命令经顶层 catch 输出"错误：…格式与范围"退出 1；Godot 同（实施方实测 `gen:1:p99` 退出 1）；无静默回落。
- **v4 / `siege-frontier-v1` 零变化（与 `71761d6` 的二进制对照，scratchpad worktree，已删）**：① `analyze` 对 `sim-out/a2-20`、`artisan-final`（v4）、`fb-k24`（边疆图）三个既有目录，新旧报告 `cmp` **逐字节相同**；② 两张图各跑 2 局（种子 42–43、Easy、2 大回合）：`config.json` **逐字节相同**，日志首部去掉 `"MapDigest":"…",` 后逐字节相同，正文只有 `ElapsedMs` / `MajorRoundMs` / `TotalMs` 三个耗时字段不同；新二进制 `replay` 旧日志只在第 1 行分歧。

### 变异验证（脚本二进制读写、finally 还原；每条跑段 B 四个测试类 + 持久化 / 设施存档 / 出生区相关类共 82 例；末尾全量复绿 1216）

| 编号 | 变异 | 红 |
|---|---|---|
| CB-1 | `Serialize` 不写 `MapDigest` | 2（存档摘要 Theory 两例） |
| CB-2 | `RequireMap` 去掉摘要比对 | 2（同上） |
| CB-3 | 旧存档缺摘要当成不一致 | 1：旧存档缺地图摘要… |
| CB-3b | `MapDigestBackfilled` 恒 false | 1：同上 |
| CB-4 | 入口忽略注入的来源、自己读时钟 | 3：入口把裸gen落成…、以裸gen批量跑局…、守门 |
| CB-5 | `map --out` 不拦权威文件 | 1 |
| CB-5b | 权威文件判据区分大小写 | 1 |
| CB-6 | 存档摘要取活地形而不是开局地图 | 1：既有的 `对局存档往返保留改造与匠人权重`（改造后用 `BaseMap` 恢复被误判不一致） |
| CB-7 | `TallyPicks` 不计获胜 | 3：`出生区公平性`、`各区胜率段在六区下输出六行`、按边长分组 |

首轮 CB-5b 时我自己的新测试在变异下往测试工作目录的 `maps/` 留了一个文件，连带让 `地图子命令打印生成图…` 在其后几条变异里变红；已把那条腿改成请求生成图（无副作用）、清掉垃圾并重跑 CB-5b（只红 1 条）。仓库 `maps/` 全程未被触碰（`git status maps` 干净）。

### 仍需负责人裁决

1. **命令行 `--map gen --map-per-match` 允许、配置文件里同样的组合拒绝**——现状保留并写明（配置文件是要复用的记录）。若要两边一致（都拒或都允许），一行改动。
2. `play --map gen`（不给 `--seed`）与 Godot `--map=gen`：地图种子与对局种子都取 `Stopwatch.GetTimestamp()`，相隔微秒，两个数几乎相同。二者用途独立、分开显示分开记录（规格满足），只是观感上像"同一个数"；要不要让地图种子另行打散（例如对时间戳做一次混合）待定，未改。
3. 存档摘要让存档 JSON 多了 `MapDigest` 一项：引入本项之前的存档恢复后**再存档**会与原文本不同（多这一行）。仓库里没有钉"旧存档再存档逐字节"的测试，三个入口也都不存盘，判为可接受。
4. `map --out` 的拦截对"请求的就是该内置图"也生效（`map --map siege-frontier-v1 --out maps/siege-frontier-v1.json` 被拒，提示去掉 `--out`）——不带 `--out` 时本来就会导出权威文件，故不另开口子。


## 段 C——图形版选图界面（tasks 3.1–3.5，2026-09-20）

基线 1216 → **1244** 全绿（+28：视图模型 22、选图守门 4、拾取自检口径 2）；`dotnet build -c Release --no-incremental` 零警告；`dotnet build src/godot/Siege.Godot.csproj --no-incremental` 零警告。生成器未动（`gen:12345` 黄金值相关测试保持绿）。未 commit。

### 改动文件

| 文件 | 改动 |
|---|---|
| `src/Siege.Presentation/MapSelect/MapSelectModel.cs`（新） | 选图视图模型（零 Godot）：清单 = `MapCatalog.BuiltinIds` + "随机图"，缺省选中 `MapCatalog.DefaultId`；状态 = 选项 / 地图种子 / 平台数 / 输入框文本 / 提示 / 完整标识（随机图经 `GeneratedMapId.Format` 规范化）；操作 `Select` / `TrySelectId` / `SubmitSeed` / `Reroll(注入的新种子)` / `AdjustPlatforms` / `Accept` / `RollBack` / `Confirm`；纯函数 `FriendlySeed`（时间戳 → 九位以内）。**只产出标识，不解析、不生成地图、不读时钟** |
| `src/godot/scripts/GameRoot.MapSelect.cs`（新，`GameRoot` 的 partial） | 选图阶段：`BeginMapSelect` / `PreviewSession`（全文件唯一一处 `MapCatalog.Resolve(_select!.CurrentId)`）/ `OnSelectionChanged`（失败 → `RollBack` 成面板提示）/ `ProcessMapSelect` / `StartMatch` / `SelfCheckMapSelect`（无人值守自检） |
| `src/godot/scripts/Hud.MapSelect.cs`（新，`Hud` 的 partial） | 选图面板：三项列表、`LineEdit` 种子输入框（回车 = 确认，另有"生成"按钮）、"换一张"、平台数 − / +、完整标识与一行地图说明、提示、"开始"。靠左上（14,14）360×446；控件只搭一次、刷新改属性（不丢输入焦点）；选图期间对局面板整体隐藏 |
| `src/godot/scripts/GameRoot.cs` | `_Ready`：登记 `--map-select`，未给 `--map=` 且非无人值守（或 `--map-select`）→ 选图阶段，否则原路径；`_Process` 选图分支（不走 `Drive` / 悬停 / 平移）；抽出 `RefreshViews`（`_Process` 与 `BeginCapture` 共用，选图阶段只刷选图面板）；`_Input` 选图阶段不认领相机键；`_UnhandledInput` 选图阶段整体不处理（F12 除外）；`Capture` 多打一行选图读数；**`RunPickCheck` 两处自检前提修正（见下）** |
| `src/godot/project.godot` | 删 `[editor]` 段（只有 `run/main_run_args` 一行，整段删） |
| `src/godot/scripts/*.MapSelect.cs.uid`（新，2 个） | Godot 自动生成；既有 13 个脚本的 `.uid` 都在库里，照此保留 |
| `tests/Siege.Core.Tests/MapSelection/选图视图模型Tests.cs`（新，22 例） | `map-selection` 各 Scenario 的逻辑部分 |
| `tests/Siege.Core.Tests/MapSelection/选图界面守门Tests.cs`（新，4 例） | 不自带清单 / 不自行生成、地图只经目录 / 视图模型不读时钟、新种子只在入口取 / `--map-select` 登记在 `LaunchArgs` |
| `tests/Siege.Core.Tests/ViewportCamera/生成图上的拾取自检口径Tests.cs`（新，2 例） | 拾取自检前提的几何事实 + 例外没有写宽的守门 |

`MapCatalog` 未改：`BuiltinIds` 在段 B 之前就是公开只读清单。

### 做法要点

- **预览不另写渲染**：用候选地图建一个尚未插旗的 `MatchSession`，取 `World.Board()`（`DefaultBoardView`）与空的 `ZoneOwners` 交给 `BoardView.Build`——与开局插旗前的盘面同一条路径。点"开始"时按确认的标识**重新建局**（用最后一次成功预览解析出的那份 `MapData`，并断言 `map.Id == Confirm()`，不符即抛——日志首部的地图标识取 `MapData.Id`，于是与界面显示的一致）。
- **候选与已接受分开**：视图模型每次操作返回"标识是否变了"；变了 → 图形版 `MapCatalog.Resolve` + 建预览，成功 `Accept`，抛 `MapGenerationException` / `MapValidationException` / `FormatException` / `FileNotFoundException` 则 `RollBack(原因)`：状态回到上一张成功的图，面板红字显示原因，不崩溃。`Confirm` 只给出已接受的标识。
- **相机**：选图阶段逐帧 `SyncAspect` → 一屏看不全且不在预览态则 `Rig.ToggleOverview()` → `ApplyCameraPose`（写相机节点仍只有这一处）。生成图之间外接矩形相同，`Rig` 保留、一直在预览态；换到 v4 外接矩形变、`Rig` 重建为初始位姿（一屏看全，不切预览）。确认开局时若在预览态再 `ToggleOverview()` 一次回到切入前位姿 = 地图中心 + 最远缩放。
- **地图种子的观感**：`NewMapSeed() = MapSelectModel.FriendlySeed((ulong)Stopwatch.GetTimestamp())`，乘大奇数 + 异或移位两轮后 `% 10^9`（实测如 `302349243`、`502770346`）。只在进入选图与"换一张"时取。命令行裸 `--map=gen` 那一处**未动**（既有守门 `生成器只经目录调用…` 钉着它的原文，且不属本段范围）。
- **`--map-select`**（仅截图 / 自检）：强制进入选图；可再给 `--map=<内置标识或完整 gen 标识>` 预选一项（文件路径 / 写错的标识 → 退出码 1）。配 `--screenshot` 截选图界面；配 `--auto-demo` 则每帧一步把选图操作经与面板事件**同一组处理函数**走一遍（随机图 → 换一张 → 平台数 +1 → 非法种子 → 种子 12345 → 逐个内置图 → 回到进入时那一项 → 开始），每步核对"视图模型标识 == 预览会话的地图标识"、非法种子不换图、开始后处于插旗且已退出预览、面板已撤，任一步不符退出码 1；随后照常自动演示（可再叠 `--pick-check`）。`--map-select --pick-check` 不带 `--auto-demo` → 退出码 1（没有人点"开始"）。

### 3.5 暴露的问题：`--pick-check` 在部分生成图上失败——根因在自检前提，不在拾取几何

在 13 个标识上扫（v4、`siege-frontier-v1`、`gen:12345`、`gen:7`、`gen:987654321:p8`、`gen:1:p5`、`gen:2`、`gen:3:p7`、`gen:4:p8`、`gen:5:p5`、`gen:6`、`gen:20260920:p7`、`gen:424242:p8`），按原口径有 4 张退出码 1（`gen:987654321:p8` 在原口径下实测失败；`gen:1:p5`、`gen:4:p8` 在只修第 1 种之后实测仍失败；`gen:424242:p8` 由"未居中遮挡 1 格"的触发计数推得），两种原因：

1. **① 逐格居中·最远**（`gen:987654321:p8` 的 L28 / M28 / O28 / P28 → 拾到 L27… (h2)；`gen:4:p8` 的 M25 → M24；`gen:424242:p8` 1 格）：该层"全严格"的依据是"格子被移到画面中心 → 射线俯角 60° → h=2 台面向远处只投 0.40 格"。25×30 的图在最远缩放（距离 28）下注视点 Z 被夹在 ±4.522 之内，第 25 / 28 行的格**居中不了**（离注视点 5–8 格），落在画面上部，射线俯角变浅到约 48°–52°，台面遮挡 0.55–0.64 格 > 半格——紧贴 h=2 平台**身后**的 h=0 格心被台面真实遮住，拾到平台才符合规格（"点在某格顶面的屏幕投影内即选中该格"）。同样这几格在 ② 的「最远」「全局预览」位姿里本来就被既有口径记为"遮挡"、不算失败。手工图上没暴露，是因为 `siege-frontier-v1` 远端没有"平台身后紧贴低地可落子格"的摆法；生成器的平台离外缘 ≥ 2 格、走廊可以走在平台与外缘之间，就会出现。缓坡（h=1）、装饰物、描边与此无关：失败格全是 h=0 → h=2。
2. **② 角位姿「画面内可落子格 0」**（`gen:1:p5`、`gen:4:p8` 的右上角）：原条件 `inView > 0` 是为了防"这个位姿什么都没验"，隐含"四个角最近缩放的视野里总有可落子格"；随机图的某个角可以整屏是深水 / 障碍。

**处理（改自检、不改生成器、不改拾取）**：

- ① 的例外只给"夹取后**没能居中**的格"：此时按 ② 的既有口径分类（拾到层数严格更高、且离相机更近 → 记遮挡并打印；其余仍失败）；真正居中的格保持全严格；**另加一条更严的**：每个可落子格至少要在一种缩放下严格往返到自己（`逐格居中·合计`），否则整体失败——免得掉"这一档缩放看不见"，免不掉"哪一档都点不到"。
- ② 的 `inView > 0` 仍对注视地图中心的位姿（中心 / 最近 / 最远 / 全局预览）强制；角位姿 0 格时打印"该角视野内没有可落子格，无可验"、不算失败，覆盖面由既有的"每格至少验到一次"兜住。
- 改后 13 张全部退出码 0；例外只在上述 4 张上触发（未居中遮挡 4 / 1 / 1 格，空角 1 / 1），v4、`siege-frontier-v1`、`gen:12345` 上两类例外触发 0 次——这三张图上自检与改动前同样严。
- 测试：`生成图上的拾取自检口径Tests` 用独立算式钉住几何事实（25×30、最远缩放、第 25 / 28 行：注视点偏离 > 4 格、遮挡 > 0.5 格；最近缩放能居中、遮挡 < 0.5 格；居中恒为 0.404 格），并源码扫描钉住例外的条件原文（变异 MC-17–19）。
- **这是对自检口径的改动，需负责人追认**（见待决 1）。对玩家的含义：这些格在最远缩放 / 全局预览下被平台挡住点不到，拉近或推屏后能点到——与 ② 里既有的"遮挡"是同一现象，不是新缺陷；若不接受，要动的是生成器布局（平台远侧不留紧贴的低地可落子格）或相机（俯角 / 最远上限），都超出本段。

### 既有测试改写（逐条）

无。既有测试一条未改；`各入口支持生成图Tests` / `各入口按地图标识选图Tests` / `Godot层不含规则计算Tests` 对新文件同样生效且保持绿（新文件里不出现生成器类型名、裸 gen 识别、内置图类型名、第二个读命令行处）。

### 变异验证（逐条）

脚本二进制读写、还原放 finally；每条跑 `MapSelection` 两类 + `各入口支持生成图` + `各入口按地图标识选图` + `Godot层不含规则计算` + `生成图上的拾取自检口径` 共 49 例；末尾全量复绿 1244，Godot 工程用还原后的代码 `--no-incremental` 重编零警告。

| 编号 | 变异 | 结果 |
|---|---|---|
| MC-1 | 视图模型清单改成字面量数组 | 红 1：`选图界面不自带地图清单` |
| MC-2 | 缺省选中第 2 项 | 红 3：`缺省进入选图…`、`选中内置图时…`、`生成失败…` |
| MC-3 | `Reroll` 忽略注入值 | 红 2：`换一张…`、`生成失败…` |
| MC-4 | 非法输入把种子置 0 | 红 11：`非法种子…` 全部 11 组输入 |
| MC-5 | 平台数上界放宽到 9 | 红 1：`平台数在5到8之间…` |
| MC-6 | `RollBack` 只置提示不恢复状态 | 红 1：`生成失败…` |
| MC-7 | `FriendlySeed` 原样返回 | 红 1：`时间戳折成便于人读写的种子…` |
| MC-8 | Hud 面板里手写三项标识 | 红 1：`选图界面不自带地图清单` |
| MC-9 | 选图阶段直接调生成器 | 红 2：`选图界面不自行生成地图…` + 既有 `生成器只经目录调用…` |
| MC-10 | 选图阶段绕过目录按文件读图 | 红 1：`选图界面不自行生成地图…` |
| MC-11 | 视图模型自己读时钟取种子 | 红 1：`视图模型不读时钟…` |
| MC-12 | `Confirm` 带走未接受的候选 | 红 1：`确认开局…` |
| MC-13 | 种子输入不先要求全数字（`5:p7` 混进平台数） | 红 1：`非法种子…(5:p7)` |
| MC-14 | `--map-select` 绕过 `LaunchArgs` 直接翻命令行 | 红 2：`选图启动选项登记…` + 既有 `图形版的地图选项登记在严格命令行解析里…` |
| MC-15 | 随机图标识不规范化（恒带 `:p<N>`） | 红 7 |
| MC-16 | "换一张"在图形版另取第二处时钟（不经 `NewMapSeed`） | 红 1：`视图模型不读时钟…` |
| MC-17 | 拾取自检：居中的格也按遮挡放过 | 红 1：`拾取自检的例外没有写宽` |
| MC-18 | 拾取自检：去掉"每格至少一次严格往返" | 红 1：同上 |
| MC-19 | 拾取自检：画面内无格对所有位姿都放过 | 红 1：同上 |

MC-17–19 守的是源码原文（`src/godot` 不在 sln，只能文本扫描），属弱守门；行为侧由 13 张图的实跑佐证。

### Godot 自检结果（最终代码）

| 命令 | v4（不带 `--map`） | `--map=siege-frontier-v1` | `--map=gen:12345` |
|---|---|---|---|
| `--headless -- --auto-demo` | 0 | 0 | 0 |
| `-- --auto-demo --pick-check` | 0（105 格） | 0（377 格） | 0（334 格） |
| `-- --seed=12345 --screenshot=…:60` | 0 | 0 | 0 |

另：`--auto-demo --pick-check` 在 `gen:7`、`gen:987654321:p8` 及另外 8 个 gen 标识上 0；`--headless -- --map-select --auto-demo`（选图自检 9 步全过 → 建局 v4 → 自动演示）0；`-- --map-select --auto-demo --pick-check --map=gen:12345` 0；不带任何参数 `--quit-after 30`：打印"选图界面：当前 siege-4p-base-v4"、0；`--map=siege-frontier-v1` 直接打印"地图 siege-frontier-v1…"（跳过选图）；`--map-select=1`、`--map-select --map=maps/x.json`、`--map-select --map=gen:1:p99` 退出码 1 并说明。全部日志无 `ERROR` / `Exception`（平台描边、装饰物、瀑布在生成图上不报错）。

### v4 截图逐像素比较

命令 `-- --seed=12345 --screenshot=<scratch>/v4-*.png:90`，改动前 5 张、改动后（最终代码）5 张，1600×900。

- **噪声基线**（水面着色器随 TIME 变）：改动前两两之间差异 26803–44809 像素，全部落在水面外接框 (574,283)–(997,645) 内，框外只有 7–9 个像素（x = 998–999 的水面右缘）。
- **改动后 vs 改动前第 1 张**：差异 33062–39140 像素，水面外接框之外 **0 / 5 / 7 / 7 / 7** 个像素（同样在 x = 998–999）——与基线同量级、同位置；HUD、地形、棋盘标注逐像素相同。以"5 张改动前互相差异的并集"为掩膜，掩膜外 27–920 像素；同口径下改动前自身留一法为 50–1535 像素，改动后不高于基线。
- 如实记录：中途一轮（最终代码之前）的 6 张里有 3 张在水面之外多出 150–2618 个差异像素，位置是悬停读数标签 (334,28)–(470,54) 与盘面上一两格——`UpdateHover` 在无人值守模式下不屏蔽鼠标，推断当时指针停在窗口内（未读图，按位置判断）；属既有行为、与本段改动无关（最终一轮 5 张里没有再出现）。没有为此动着色器或悬停逻辑。

### 截图路径（未读入上下文，供负责人过目）

- `sim-out/mapgen-shots/gen-12345-overview.png`、`gen-7-overview.png`、`gen-987654321-p8-overview.png`（`--overview` 全局预览）
- `sim-out/mapgen-shots/map-select.png`（`--map-select --map=gen:12345`，选中随机图；控制台读数：面板 (14,14) 360×446、全局预览开、对局面板隐藏）
- `sim-out/mapgen-shots/map-select-v4.png`（`--map-select`，缺省选中 v4，一屏看全不切预览）

### 人工检查清单（留给负责人；编辑器点运行或不带参数启动）

1. 启动即见选图面板（左上），背景是 v4 盘面；三项列表：`siege-4p-base-v4`、`siege-frontier-v1`、随机图。
2. 三项来回切：背景预览随之更新；v4 一屏看全（初始位姿），另两项是整盘一屏的全局预览；"完整地图标识"与下面一行尺寸 / 出生区数跟着变。
3. 选中随机图：种子输入框、"换一张"、平台数 − / + 可用（选内置图时置灰）。
4. 输入框里敲数字后回车（或点"生成"）→ 预览换成该种子的图；两次启动输入同一种子、同平台数 → 同一张图。
5. 输入 `abc` 回车 → 红字"地图种子须为非负整数…"，预览与完整标识不变，输入框保留 `abc`。
6. "换一张" → 种子变（九位以内）、预览换图、完整标识更新。
7. 平台数 − / +：5–8，到边界按钮置灰；标识在 6 时省略 `:p6`。
8. 输入框里 W / A / S / D、空格、方向键、退格、Home / End 行为正常（相机不动、不触发回家 / 全局预览）；M 键不切预览。
9. 选图阶段点棋盘、滚轮、贴边推屏均无效果；F12 仍可截图。
10. 点"开始" → 面板消失、出现"开局插旗"提示；边疆图 / 随机图相机回到地图中心最远缩放（不是全局预览），可推屏缩放；点出生区插旗正常开局；控制台"地图 <标识>"与面板上的一致，且点"开始"后控制台无 ERROR（真人点按钮走的是按钮信号回调 → 撤面板，这一支自检走不到；面板改为只隐藏 + 帧末释放，不在回调中途摘树）。
11. 面板没有挡住地图主体（1600×900 与拉伸窗口各看一眼；窄窗口下 25×30 的图左缘可能贴近面板）。
12. `project.godot` 已无编辑器运行参数：编辑器点运行即进选图。

### 偏离与待决

1. **`--pick-check` 自检口径的两处修正需追认**（见上节）。不接受的话，替代方案是改生成器布局或相机参数，超出本段且会动 `gen:` 黄金值。
2. **`--map-select` 的语义比派发说明宽**：除配 `--screenshot` 外，还支持 `--map=<标识>` 预选、配 `--auto-demo` 跑选图流程自检。理由：否则"选图 → 重搭预览 → 建局"这条路径完全没有自动化覆盖。
3. **`BoardView.Build` 的调用点从 3 处变 5 处**（+ 选图换预览、+ 确认开局）；`.trellis/spec/core/boundaries.md` 里"全仓只有 3 处"的表述待段 D（4.1）更新。
4. **选项标题直接显示地图标识**（不做"标准图 / 手工边疆图"的中文名）：标识 → 中文名的对照表就是界面自带清单。要中文名的话应加在 `MapCatalog` 的内置表上，另起小改动。
5. 命令行裸 `--map=gen`（Godot）仍用原始时间戳作地图种子，没有改用 `FriendlySeed`：既有守门钉着那一行原文，改它要改既有测试期望。建议段 D 或后续统一（连同 `Siege.Sim` 的 `ClockMapSeed`）。
6. 预览会话用的就是本次启动的对局种子；确认时重新建局，对局随机不受预览次数影响（`MatchSession.Create` 无全局状态）。
7. 无人值守模式下 `UpdateHover` 不屏蔽鼠标（既有行为）会让定帧截图偶发带上悬停读数——影响"逐像素相同"类验收的可重复性，建议后续在 `Unattended` 下跳过悬停。未在本段改。

## 段 C 检查（2026-09-20）

全量 **1244** 全绿（数目不变：新断言并进既有用例）；`dotnet build -c Release --no-incremental` 零警告；`dotnet build src/godot/Siege.Godot.csproj --no-incremental` 零警告；`openspec validate frontier-map --strict` / `map-generator --strict` 通过。生成器布局未动（`gen:12345` 黄金值相关测试保持绿）。未 commit。

### 负责人五条处理的落实

1. **`--pick-check` 口径：接受，但检查中发现例外写宽了，已收紧。**
   - 实现方判据核对：例外 1 用的是注视点与格心的**实际偏差**（不是"失败即算没居中"）；遮挡分类是"层数严格更高 && 水平距离更近"；`inView > 0` 只对注视地图中心的位姿强制。三条属实。
   - **真问题（高）**：原判据把**横向**夹取也算"没居中"。v4 在最近缩放下 B 列横向被夹取（纵深居中），"缩放带 5° 俯角变化"变异在 v4 上 `B5(h0) → B4(h2)` 被记成遮挡，**退出码 0**——frontier-map 段里靠这一格抓住该变异（M-P5），放宽后被放过。`siege-frontier-v1`、`gen:987654321:p8` 上仍是 1。
   - **修**：例外只给"**纵深方向**被夹取、格落在注视点**远侧**"的格（`at.FocusZ - z > 1e-3f`），且只认**正前或斜前一行**（朝相机方向近一行、列差 ≤ 1）的更高更近的格；同一行的左右邻格不算（G-SX 变异日志里见到过 `L22 → M22` 这类纯横向邻格被记成遮挡，物理上不可能，遂再收一层）。依据：台面向远处的遮挡只取决于纵深方向的视线分量（0.70 × 纵深水平距 ÷ 相机高），横向偏移不加深它；近侧的格视线更陡。收紧后 13 张图仍全部退出码 0，例外触发数与实现方记录相同（`gen:987654321:p8` 4 格、`gen:4:p8` 1 格、`gen:424242:p8` 1 格；空角 `gen:1:p5`、`gen:4:p8` 各 1；v4 / `siege-frontier-v1` / `gen:12345` 0 次）。
   - 规格：`frontier-map/specs/viewport-camera/spec.md`「动态相机下的拾取正确」改写两层口径（纵深远侧 + 正前 / 斜前一行 + 每格至少一档严格；角位姿可空、注视中心位姿必须有格；全局预览一档），补 Scenario「夹取后未能居中的格按遮挡分类」「哪一档缩放都点不到则失败」「角位姿空视野」「拾取同层偏移一格会被抓住」，「缩放带俯角变化会被抓住」写明三张图；对玩家的含义（最远缩放下远边几行紧贴高台身后的低地格点不到、拉近即可）写进正文。
   - "拉近后每格都点得到"的保证 = `逐格居中·合计`（每格至少一种缩放下严格往返），13 张图上都是满格。
2. `--map-select`：已写进 `map-selection` 规格（仅截图 / 自检；是"给了地图选项 MUST 跳过选图"的显式例外；配 `--map=` 预选、配 `--auto-demo` 自检），补 Scenario「强制进入选图的自检选项」。
3. 中文显示名：`MapCatalog.Builtins` 加 `Title` 列（"标准图 13×13"、"边疆图 25×30（手工）"），新增 `MapCatalog.BuiltinMaps`（`BuiltinMapInfo(Id, Title)`，与 `BuiltinIds` 同序；`BuiltinIds` 不动）；视图模型 `new MapOption(m.Title, m.Id)`；Hud 本来就读 `Options[i].Title`，未改。守门：界面层（Presentation/MapSelect + godot/scripts）不得出现任何显示名全文，代码行（去掉整行注释）不得出现显示名首词；禁用词取自目录本身。规格同步一句。
4. 地图种子折叠统一：`FriendlySeed` / `FriendlySeedLimit` 从 `MapSelectModel` **移到 Core 的 `GeneratedMapId`**（Sim 不依赖 Presentation；纯函数、不读时钟）；三个取种子处都经它——`GameRoot.MapSelect.NewMapSeed`、`GameRoot._Ready` 裸 `gen`、`Siege.Sim` 的 `ClockMapSeed`。实跑 `--map=gen` → `gen:283789901`。
5. `UpdateHover` 在 `Unattended` 下直接返回（与 headless 同一分支）。

### 既有测试改写（逐条，均为本 change 自己的测试、按新行为更新）

- `各入口支持生成图Tests.生成器只经目录调用_裸gen只在入口最外层取种子`：图形版那条正则由 `GeneratedMapId.Format((ulong)Stopwatch.GetTimestamp()` 改为 `GeneratedMapId.Format(GeneratedMapId.FriendlySeed((ulong)Stopwatch.GetTimestamp())`；新增一条钉 `ClockMapSeed` 经 `FriendlySeed`。改前该测试按新代码为红（确认它确实钉着）。
- `选图界面守门Tests.视图模型不读时钟…`：`MapSelectModel.FriendlySeed(` → `GeneratedMapId.FriendlySeed(`。
- `选图界面守门Tests.选图界面不自带地图清单`：`MapCatalog.BuiltinIds` → `MapCatalog.BuiltinMaps`，加显示名守门。
- `选图视图模型Tests.缺省进入选图…`：标题期望由标识改为目录的显示名；`时间戳折成…` 改指向 `GeneratedMapId.FriendlySeed`。
- `生成图上的拾取自检口径Tests.拾取自检的例外没有写宽`：钉新的三行原文（`beyondFocus` / `adjacent` / `blocked`）。

### 检查中另外补的

- **选图自检加"节点不泄漏"**（`SelfCheckMapSelect`）：进入时与"开始"前各数一次棋盘子树节点数与游离节点数（同为"搭完并刷新过"的状态），重搭 8 次预览后必须相等；建局后再数一次。实测 v4 1131 / 1131、`gen:12345` 4241 / 4241、`siege-frontier-v1` 4218 / 4218，游离 0 / 0。`BoardView.Build` 开头 `Clear(this)`（`RemoveChild` + `QueueFree`）、三张缓存表清空、`Rig` 仅在外接矩形变化时重建——已读代码核对。
- **选图后建局 == 直接 `--map=` 建局**：`--headless --auto-demo --seed=12345` 分别走直接建局与 `--map-select`（自检里重搭 8 次预览、换过 3 张不同的图之后再开始），从"[siege] 地图"行到终局名次（去掉帧号与 `[perf]`）逐行相同——v4、`siege-frontier-v1`、`gen:12345` 三张都 SAME。为此 `StartMatch` 的首行补了"，自动演示模式"后缀（与直接建局同文）。`MapData` 是不可变 record，预览会话不推进，复用同一份 `MapData` 无副作用。

### 已核对、无需改动

- 选图阶段：`_Input` 在 `Selecting` 时不认领相机键；`_UnhandledInput` 除 F12 外整体返回（点击 / 滚轮 / 信息层键）；`_Process` 选图分支不走 `Drive` / `UpdateCamera`（无推屏）/ `UpdateHover`；对局面板 `_root` 隐藏，其上的"全局""回家"按钮点不到。回车 = `TextSubmitted`。生成 / 校验 / 解析失败 → `RollBack` 成红字提示，`_session` 与 `_previewMap` 保持上一张（赋值发生在成功之后）。
- 无人值守未给 `--map=` → v4、跳过选图；`project.godot` 无 `[editor]` 段；`--map-select` 经 `LaunchArgs.Flag` 登记且在 `EnsureRecognized` 之前；相机节点仍只在 `ApplyCameraPose` 写。
- 界面层不自带清单 / 不直接调生成器：守门扫 `Siege.Presentation/MapSelect` + `godot/scripts` 全部文件（含注释），禁标识字面量、内置图类型名、生成器 / `MapFile.` / `new MapData(`；`new MapOption(` 只许出现在视图模型。

### 变异验证

Godot 侧（脚本二进制读写、finally 还原、还原后重编；`--auto-demo --pick-check` 在 v4 / `siege-frontier-v1` / `gen:987654321:p8` 上）：

| 编号 | 变异 | 结果 |
|---|---|---|
| G-P5（收紧前） | `CameraPose.Eye` 俯角随距离线性变化（d=28 → 60°，d=8 → 55°） | **v4 退出码 0**（B5 → B4 记成遮挡）、边疆图 1、gen 图 1 —— 据此收紧例外 |
| G-P5（收紧后） | 同上 | 1 / 1 / 1（v4 `B5 → B4`、边疆图 `R10 → R9`、gen 图最近层 14 格失败 + L28 / M28 / O28 / P28 "哪一档都点不到"） |
| G-SX | `TryPick` 命中点横向偏一格（同层邻格） | 1 / 1 / 1（逐格居中两档 0 格往返一致） |
| G-SZ | `TryPick` 命中点纵深偏一格 | 1 / 1 / 1 |
| G-LEAK | `BoardView.Clear` 不再 `RemoveChild` | `--map-select --auto-demo` 退出码 1（"开始"步节点数不符） |

.NET 侧（每条跑 `MapSelection` 两类 + `各入口支持生成图` + `各入口按地图标识选图` + `Godot层不含规则计算` + `生成图上的拾取自检口径` 共 49 例；末尾全量复绿 1244）：

| 编号 | 变异 | 结果 |
|---|---|---|
| MC-20 | Hud 选图面板自带显示名对照（按下标写"标准图" / "边疆图（手工）"） | 红 1：`选图界面不自带地图清单` |
| MC-21 | 视图模型标题退回显示标识 | 红 1：`缺省进入选图…` |
| MC-22 | 例外判据放回"横向或纵深任一偏差" | 红 1：`拾取自检的例外没有写宽` |
| MC-23 | `ClockMapSeed` 不经 `FriendlySeed` | 红 1：`生成器只经目录调用…` |
| MC-24 | Godot 裸 `gen` 不经 `FriendlySeed` | 红 1：同上 |
| MC-25 | 例外去掉"正前 / 斜前一行"条件 | 红 1：`拾取自检的例外没有写宽` |

### Godot 自检结果（最终代码）

- `--auto-demo --pick-check`：v4（105 格）、`siege-frontier-v1`（377）、`gen:12345`（334）、`gen:987654321:p8`（384，最远层遮挡 4）及另外 9 个 gen 标识，全部退出码 0、日志无 ERROR / Exception。
- `--headless -- --map-select --auto-demo` 0；`-- --map-select --auto-demo --pick-check --map=gen:12345` 0；`--headless -- --auto-demo` 在 v4 / 边疆图 / `gen:12345` 0；不带参数启动打印"选图界面：当前 siege-4p-base-v4"；`--map-select=1`、`--map-select --map=maps/x.json` 退出码 1；`--seed=12345 --screenshot` 0。
- 重截 `sim-out/mapgen-shots/map-select.png`、`map-select-v4.png`（显示中文名后；面板仍是 (14,14) 360×446；未读入上下文）。

### 人工检查清单的增删（对上面「段 C」清单）

- 第 1 条改：三项列表为"标准图 13×13"、"边疆图 25×30（手工）"、"随机图"；"完整地图标识"一栏仍显示 `siege-4p-base-v4` 等标识。
- 第 10 条里"控制台『地图 <标识>』与面板一致"保留；"与直接 `--map=` 建局同一对局"已由自动比对覆盖，不必人工看。
- 新增 13：命令行 `--map=gen` 启动，控制台打印的地图种子为九位以内。
- 新增 14：在一张 25×30 的图上用最远缩放点地图最上面几行里紧贴高台身后的低地格——点到的是高台（预期内）；滚轮拉近后该格能点到。
- 新增 15：连点"换一张"二三十次，帧率与内存无明显爬升（自动化只数了节点，没量显存）。

### 仍需负责人裁决

1. **例外收紧为"纵深远侧 + 正前 / 斜前一行"是检查方在负责人口径之上又加的一层**（负责人原话是"因夹取没能居中的格按遮挡口径分类"）。不收紧则 5° 俯角变异在 v4 上退出码 0，与负责人"三张图都必须退出码 1"冲突，故按后者落实；规格已照此写。请追认。
2. `FriendlySeed` 放进了 `MapGenParameters.cs` 里的 `GeneratedMapId`（标识的唯一实现旁）。该文件属生成器一组，但只加了一个纯函数，不影响任何生成结果；若希望另起文件可再挪。
3. 游离节点计数（`ObjectOrphanNodeCount`）在本机读数恒为 0，无法区分"确实为 0"与"该构建不提供此监视项"；节点泄漏的有效证据是棋盘子树节点数。
4. `.trellis/spec/core/testing.md`「`--pick-check`…」一节与 boundaries.md 的 `BoardView.Build` 调用点数仍是旧表述，留给段 D（4.1）。
