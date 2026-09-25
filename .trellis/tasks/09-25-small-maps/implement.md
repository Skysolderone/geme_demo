# 09-25-small-maps 实施记录

## 段 A——2 人图与入口（tasks 1.1–1.5）

### 改动文件

| 文件 | 内容 |
|---|---|
| `maps/siege-2p-base-v1.json` | 新增，手工编写的 2 人图（权威数据）。用 scratchpad 的 python 脚本写左下半、绕中心 180° 补齐右上半，脚本不进仓库 |
| `src/Siege.Core/Siege.Core.csproj` | 把上面这份 JSON 作为 `EmbeddedResource` 编进程序集（`LogicalName = Siege.Core.Maps.siege-2p-base-v1.json`），三个入口都不依赖工作目录 |
| `src/Siege.Core/Board/Maps/TwoPlayerBaseMap.cs` | 新增：`Id` 常量 + `Create()`，从嵌入资源经 `MapFile.FromJson` 读出，不另存一份地图 |
| `src/Siege.Core/Board/Maps/MapCatalog.cs` | `Builtins` 加一行 `(TwoPlayerBaseMap.Id, "双人图 9×9", …)`，位置在标准图之后、边疆图之前 |
| `src/Siege.Core/Board/MapSymmetry.cs` | 逐格比对的函数体抽成私有 `Defects(map, rotate, zoneShift)`，C4 与 C2 共用这一份。新增 `Rotate180`、`Rotation180Defects`、`IsC2Symmetric`；`RotationDefects` / `IsC4Symmetric` 的行为不变 |
| `src/Siege.Sim/Play/PlayCommand.cs` | 参数 `playerCount` 改为 `int? playerCountArg`，传 `null` 时取 `map.MaxPlayers` |
| `src/Siege.Sim/Program.cs` | `play`：不给 `--players` 就传 `null`。`run`：命令行没有 `--players`、配置文件也没写 `Players` 时，人数取 `MapCatalog.Resolve(MapIdAt(0)).MaxPlayers`；难度沿用已定的玩家配置。帮助文本同步更新 |
| `tests/.../MapDefinition/两人基准地图Tests.cs` | 新增 7 条测试 |
| `tests/.../SimulationHarness/各入口按地图标识选图Tests.cs` | 新增 2 条测试和一个辅助方法 `RunOne` |
| `tests/.../MapSelection/选图视图模型Tests.cs` | 选项数 3 → 4，第 2 项是 2 人图；原来写死的"随机图 = 下标 2"改成 `Options.Count - 1`；确认开局改为逐项遍历全部选项 |
| `tests/.../MapGeneration/生成确定性Tests.cs` | 钉住的内置表加入 `siege-2p-base-v1` |

未改：`src/godot`。三处建局本来就是 `Math.Min(4, map.MaxPlayers)`，标准档最多 4 人，所以恒等于 `MaxPlayers`；新测试对这一点做了源码扫描。

### 与 v5 写法的偏离

v5 与边疆图都以 C# 类为权威，JSON 由 `map` 子命令导出。本图按 design D1 与负责人指令**以手写 JSON 为权威**：`TwoPlayerBaseMap` 只负责读资源。`Siege.Sim map --map siege-2p-base-v1` 仍会按内置图重新导出 `maps/<id>.json`，实测导出结果与手写文件逐字节相同（手写文件就是 `MapFile.ToJson` 的规范写法），测试 `磁盘上的地图文件是规范写法_与内置图逐项一致` 钉住这一点。

### 地图数字（`sim-out/small-maps/2p-map.txt`）

- 外接 9×9：选奇数边长，C2 才有不动格 E5，中央入口和高档信物可以放在中心；显示名因此写"双人图 9×9"
- 可落子格 61（h0 / h1 / h2 = 29 / 6 / 26）；岩石 14；深水 8，其中桥 2（E3、E7，同时是咽喉）；栅栏 2（E3-F3、D7-E7）；林地 4；土路 4
- 出生区 2 个，各 13 格，全部 h=2，分别在左下角和右上角
- 信物 7 个：出生区 2 + 2（B2、C3 / G7、H8）；公共区 3 个，包括桥头 E4、E6（标准档）和中心 E5（高档，兼中央入口）
- 沿气边距离（出生区 1 / 2）：最近公共信物 3 / 3，中央入口 4 / 4，最近咽喉 2 / 2
- 地图校验通过；不含四种新地表

### 1.1 先红

在未登记、未嵌入资源的状态下跑过滤（两人基准地图 | 选图视图模型 | 各入口按地图标识选图）：`Failed 10, Passed 32`。红的是新增 7 条地图测试、2 条入口测试，以及改成 4 项的 `缺省进入选图`。实现后同一过滤 42 条全绿。

### 变异（脚本：二进制读写，锚点按文件实际行尾归一并断言恰命中 1 次；备份名带时间戳，在 finally 里还原；还原后逐字节比对，再 `os.utime`）

| 编号 | 改动 | 红 |
|---|---|---|
| M-S1 | JSON 中 G1 高度 0 → 1（一格破坏 C2） | 1：`两人图旋转对称` |
| M-S2 | `Rotate180` 改成左右镜像 `(w−1−x, y)` | 1：`两人图旋转对称` |
| M-S3 | C2 出生区编号不互换（`zoneShift: 0`） | 1：`两人图旋转对称` |
| M-S4 | 终端缺省人数写死 `?? 4` | 1：`参赛人数缺省取地图人数上限_显式人数照旧` |
| M-S5 | 批量缺省不改写人数（`&& maxPlayers < 0`） | 2：上一条和 `两人图可选_选中确认后建局恰有2名玩家` |
| M-S6 | 配置文件写了 `Players` 也当作未写 | 1：`参赛人数缺省取地图人数上限_显式人数照旧` |
| M-S7 | `src/godot/scripts/GameRoot.cs` 的建局人数 `Min(4, map.MaxPlayers)` 改成 `4`（源码扫描守门） | 1：`参赛人数缺省取地图人数上限_显式人数照旧` |

还原后同一过滤 42 / 42 全绿，失败数不再等于最后一条变异的红数。各条变异也记在对应测试的注释里。改动文件的行尾逐个核对过，均为单一行尾。

### 1.4 Godot（Debug 构建，`Godot_v4.7.2-stable_mono_win64_console.exe`）

| 命令（`--path src/godot`） | 退出码 | 读数 |
|---|---|---|
| `--headless -- --auto-demo --map=siege-2p-base-v1` | 0 | 跑满 4 个大回合后停止；开局对准出生平台 13 / 13 |
| `--headless -- --auto-demo --pick-check --map=siege-2p-base-v1` | 0 | 逐格居中 61 / 61 ×2；7 个位姿失败 0、遮挡 0 |
| `--headless -- --map-select --auto-demo --map=siege-2p-base-v1` | 0 | 选图自检 10 步全过；第 6 步"选中第 2 项内置图"→ siege-2p-base-v1（9×9，2 个出生区，61 格）；最后以 2 人图建局 |
| `-- --auto-demo --rounds=8 --map=… --screenshot=…/2p-default.png:80` | 0 | 1600×900，第 8 大回合 |
| `-- --auto-demo --overview --map=… --screenshot=…/2p-overview.png:10` | 0 | 全局预览 |
| `-- --map-select --map=… --screenshot=…/2p-map-select.png:10` | 0 | 选图界面，预选 2 人图 |

截图只存盘，未读入上下文，待负责人过目（段间）。

### 1.5 20 局冒烟（只报告，不调参）

`Siege.Sim run --map siege-2p-base-v1 --seed 1 --count 20 --difficulty Standard --out sim-out/small-maps/2p-smoke20`，没给 `--players`。`config.json` 核对：2 名 Standard，`FlagRisk` 15，`PassThreshold` 80，`TurnLimit` 600。逐局表见 `2p-smoke20/smoke-table.txt`，分析报告见 `2p-smoke20/analysis.txt`。

| 指标 | 值 |
|---|---|
| 截断 | 0 / 20 |
| 终局原因 | AllPassed ×20 |
| 结束大回合 | 7×5、8×8、9×6、10×1（平均 8.15）；平均 15.75 个小回合 |
| 整局无提子 | 18 / 20（全批只提 3 子） |
| 先手（首回合顺序第一）胜率 | 12 / 20 = 60% |
| 附：第 3 大回合领先者最终胜率 | 18 / 20 = 90%（样本不足 200，结论不可靠） |

### 自验

- `dotnet build siege.sln --no-incremental`：0 警告 0 错误
- `dotnet build src/godot/Siege.Godot.csproj --no-incremental`：0 警告 0 错误
- `dotnet test -c Release`：1434 通过、5 跳过、0 失败（共 1439，比改动前多 9 条）
- `openspec validate small-maps --strict`：通过
- 未跑 200 局，也未跑 Slow / Perf 类测试

### 待决

1. 尺寸取 9×9，不是 10×10，理由是需要中心不动格；显示名随之写"双人图 9×9"。
2. 权威数据是嵌入资源里的 JSON，与 v5 的"C# 为权威"写法不同，见上文。3 人图如果沿用这个做法，在 csproj 里再加一行资源即可。
3. 冒烟结果：整局无提子 18 / 20，全部以 AllPassed 在 7–10 大回合结束，第 3 大回合领先者 90% 胜出。AI 只在 4 人 v5 上校准过，小图上几乎不交战。按 design Risks 记为待决，本段不调参、不改预算区间。
4. 校验器的 2 人预算表没有单个出生区 12–14 格的区间（只有 4 人档有），目前只靠测试守。要不要补进预算表另议。
5. 侧翼陆路：两区之间有一条不经中央和桥的路，沿西北侧走 A4 → A5 / B5（h1）→ A6–C6 → B7 / C7 → C8 / D8 → E8（h1）→ F8（区 2），东南侧对称也有一条。到中央入口的最短路仍是经本方桥的 4 步，校验器口径没问题；但这条路是否符合 D2 的意图，结合"整局无提子 18 / 20"一并请负责人看截图时裁定。

## 段 B——3 人图与收尾（tasks 2.1–2.6）

### 改动文件

| 文件 | 内容 |
|---|---|
| `maps/siege-3p-base-v1.json` | 新增，手工编写的 3 人图（权威数据）。用 scratchpad 的 python 脚本写左半（A–F 列），再沿 F 列镜像补齐右半；脚本同时按气边口径复算规模与三区距离。写出的文件本来就是 `MapFile.ToJson` 的规范写法（CRLF、末尾无换行），`map --map siege-3p-base-v1` 重新导出后 sha1 不变 |
| `src/Siege.Core/Siege.Core.csproj` | 增加一行 `EmbeddedResource`（`LogicalName = Siege.Core.Maps.siege-3p-base-v1.json`） |
| `src/Siege.Core/Board/Maps/ThreePlayerBaseMap.cs` | 新增，写法同 `TwoPlayerBaseMap`：`Id` 常量 + `Create()`，从嵌入资源读出 |
| `src/Siege.Core/Board/Maps/MapCatalog.cs` | `Builtins` 在 2 人图之后加一行 `(ThreePlayerBaseMap.Id, "三人图 11×11", …)` |
| `src/Siege.Core/Board/MapSymmetry.cs` | 私有 `Defects` 的出生区参数由 `int zoneShift` 改为 `Func<int, int> zoneImage`；C4 / C2 分别传 `(z + 1) % n` 与 `(z + n/2) % n`，行为不变。新增 `MirrorVertical`（`(w − 1 − x, y)`）、`MirrorDefects`（出生区 i 的像是 `(n − i) % n`，即区 1 不变、2 ↔ 3）、`IsMirrorSymmetric`；中央入口的报文改为"不在旋转中心 / 镜像中轴上"。镜像检查不接入校验器 |
| `tests/.../MapDefinition/三人基准地图Tests.cs` | 新增 8 条：三个 Scenario（规模、镜像对称、三区公平），另有地形要素、静态校验、出生区容量、内置表登记、磁盘规范写法。三区公平用测试内独立的多源 BFS（只借 `Adjacency.LibertyNeighbors`），并与 `MapValidator.DistanceTable` 逐项对照 |
| `tests/.../SimulationHarness/各入口按地图标识选图Tests.cs` | 新增 `三人图可选_选中确认后建局恰有3名玩家`；缺省人数测试补一条 3 人图（批量入口不给 `--players` 时为 3 人） |
| `tests/.../MapSelection/选图视图模型Tests.cs` | 选项数 4 → 5，第 3 项是 3 人图 |
| `tests/.../MapGeneration/生成确定性Tests.cs` | 钉住的内置表加入 `siege-3p-base-v1` |
| `2026-09-10-siege-core-gameplay-design-v1.md` | v1.12 → v1.13：§3.2 补 C2 与 3 人镜像口径，2 / 3 人区间注明仍是估值；§3.3 标题改为"4 人原型地图与 2 / 3 人基准图"，新增小节写两张图和冒烟表；变更记录加一行 |
| `openspec/changes/small-maps/tasks.md` | 勾选 2.1–2.6 |

未改：`src/godot`。选图自检按内置表逐项遍历，3 人图自动进入清单。

### 地图数字（`sim-out/small-maps/3p-map.txt`）

- 外接 11×11：宽取奇数，中轴 F 列存在；显示名"三人图 11×11"
- 可落子格 86（h0 / h1 / h2 = 30 / 17 / 39）；岩石 26；深水 12，其中桥 3（D5、H5、F7，都是咽喉）；栅栏 2（E5-E6、G5-G6）；林地 6；土路 11
- 出生区 3 个，各 13 格，全部 h=2。区 1 跨中轴在上方（E11–G11、D10–H10、D9–H9）；区 2 / 3 在左下 / 右下，互为镜像
- 信物 10 个：出生区各 2 个（E10 / G10、B3 / C2、K3 / J2）；公共区 4 个，包括桥头 E5、G5、F6（标准档）和岛心 F5（高档，兼中央入口）
- 沿气边距离（出生区 1 / 2 / 3）：最近公共信物 3 / 3 / 3，中央入口 4 / 4 / 4，最近咽喉 2 / 2 / 2
- 地图校验通过；不含四种新地表

### 2.1 先红

新测试和 `ThreePlayerBaseMap` 已写好，但 JSON 还没嵌入、内置表也没登记。此时跑过滤（三人基准地图 | 两人基准地图 | 四人基准地图 | 基准地图对称性 | 选图视图模型 | 各入口按地图标识选图 | 生成确定性）：`Failed 12, Passed 76`。其中 2 / 4 人对称测试全绿，说明 `MapSymmetry` 的重构没有带坏旧行为。登记后同一过滤 88 条全绿。

### 变异（脚本同段 A：二进制读写、按文件实际行尾归一锚点并断言恰命中 1 次、备份名带时间戳、在 finally 里还原；还原后逐字节比对，再 `os.utime`）

| 编号 | 改动 | 红 |
|---|---|---|
| M-T1 | JSON 中 A9 林地 → 草地（一格破坏镜像） | 1：`三人图镜像对称` |
| M-T2 | `MirrorVertical` 改成绕中心 180° | 1：`三人图镜像对称` |
| M-T3 | 镜像的出生区像改成恒等（`z % zones`，2、3 不互换） | 1：`三人图镜像对称` |
| M-T4 | JSON 中中央入口 F5 → F6（沿中轴上移一格，F5 / F6 的高档与标准档互换，镜像不受影响）。区 1 到中央入口 4 → 3，区 2 / 3 为 4 → 5，极差 2 | 5：`三人图三区公平`、`通过静态校验与人数预算`、`出生区容量`（建局时校验失败），以及两条 3 人图入口测试。`三人图镜像对称` 保持绿 |

还原后同一过滤 88 / 88 全绿，失败数不等于最后一条变异的红数。各条变异也写进了对应测试的注释。

### 2.3 Godot（Debug 构建，`Godot_v4.7.2-stable_mono_win64_console.exe`）

| 命令（`--path src/godot`） | 退出码 | 读数 |
|---|---|---|
| `--headless -- --auto-demo --map=siege-3p-base-v1` | 0 | 跑满 4 个大回合后停止；开局对准出生平台 13 / 13，位姿变化 0 |
| `--headless -- --auto-demo --pick-check --map=siege-3p-base-v1` | 0 | 逐格居中 86 / 86 ×2，严格往返 86 / 86；7 个位姿失败 0、遮挡 0 |
| `--headless -- --map-select --auto-demo --map=siege-3p-base-v1` | 0 | 选图自检 11 步（0–10）全过。第 7 步"选中第 3 项内置图"得到 siege-3p-base-v1（11×11，3 个出生区，86 格）；进入时与开始时棋盘节点都是 752，游离节点 0；最后以 3 人图建局 |
| `-- --auto-demo --rounds=8 --map=… --screenshot=…/3p-default.png:80` | 0 | 1600×900，第 7 大回合，中央面板关 |
| `-- --auto-demo --overview --map=… --screenshot=…/3p-overview.png:10` | 0 | 全局预览 |
| `-- --map-select --map=… --screenshot=…/3p-map-select.png:10` | 0 | 选图界面，预选 3 人图 |

截图只存盘，没有读进上下文，等负责人过目。

### 2.4 20 局冒烟（只报告，不调参）

命令：`Siege.Sim run --map siege-3p-base-v1 --seed 1 --count 20 --difficulty Standard --out sim-out/small-maps/3p-smoke20`，没给 `--players`。核对 `config.json`：3 名 Standard，`FlagRisk` 15，`PassThreshold` 80，`TurnLimit` 600，`PlayerCount` 3。逐局表在 `3p-smoke20/smoke-table.txt`，分析报告在 `3p-smoke20/analysis.txt`。

| 指标 | 值 |
|---|---|
| 截断 | 0 / 20 |
| 终局原因 | AllPassed ×20 |
| 结束大回合 | 3×1、8×7、9×5、11×3、12×2、13×1、15×1（平均 9.45）；平均 27.6 个小回合 |
| 整局无提子 | 1 / 20（全批共提 107 子；首次提子分布在第 3–8 大回合，多数在 5–7） |
| 先手（首回合顺序第一）胜率 | 5 / 20 = 25% |
| 第 3 大回合领先者最终胜率 | 10 / 16（另有 4 局第 3 大回合势力并列第一，不计；样本不足 200，结论不可靠） |
| 附：胜者分布 | P0 8、P1 7、P2 5 |
| 附：同区插旗 | 4 / 20 局（种子 3、5、6、11） |

种子 6 是唯一的整局无提子局：P0 与 P2 同在出生区 1，第 2 大回合起两人都 Pass，第 3 大回合 P1 也 Pass，整轮 Pass 终局。同一个统计脚本在 2 人图数据上复算得 18 / 20，与段 A 的口径一致。

### 自验

- `dotnet build siege.sln --no-incremental`：0 警告 0 错误
- `dotnet build src/godot/Siege.Godot.csproj --no-incremental`：0 警告 0 错误
- `dotnet test -c Release`：1443 通过、5 跳过、0 失败（共 1448，比段 A 多 9 条）
- `openspec validate small-maps --strict`：通过
- 没有跑 200 局，也没有跑 Slow / Perf 类测试

### 待决

1. 3 人图只有 4 个公共信物，全图共 10 个，落在区间下限。原因是按"每座桥头 1 个 + 岛心 1 个"的 v5 比例放置。如果要放到 11–12 个，只能在两翼加一对镜像信物，而且要保证加了以后三区距离仍然相等。
2. 3 人冒烟里第 3 大回合即终局的一局（种子 6）出现在同区插旗局中，是否属于 flag-contest 的已知现象，请负责人判断。另外先手胜率 5 / 20 偏低，但样本只有 20 局，只报告。
3. 与段 A 相同：校验器的 3 人预算表没有单个出生区 12–14 格的区间，目前只靠测试守住。
4. 镜像的出生区约定写死在 `MapSymmetry.MirrorDefects` 里：区 1 跨中轴，i ↔ n − i。以后如果有别的镜像图用了不同的编号顺序，需要改成由调用方传入映射。
5. 侧翼陆路（与段 A 待决 5 同类）：3 人图两翼各有一条不过桥的侧路。西侧从上区出口 C9 / C10（h1）经 B8 / A8–C8、B7 下到 A6–C6、A5–C5（h1），到达区 2；东侧经 K7 对称到达区 3。区 2 与区 3 之间没有不经中央的直连路，底部 E2、G2 是死胡同。到中央入口的最短路仍然经过本方的桥（4 步），校验器口径没有问题。这两条侧路是否符合 D2 的意图，请负责人看截图时裁定。
