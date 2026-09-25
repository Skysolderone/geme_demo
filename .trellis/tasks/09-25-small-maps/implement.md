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
