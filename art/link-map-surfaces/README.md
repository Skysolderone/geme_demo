# link-map-surfaces 截图与人工检查清单

> 截图命令（非 headless，PowerShell；先 `dotnet build src/godot/Siege.Godot.csproj` 构建 Debug 程序集）：
> `$G = "D:/software/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"`
> - 锁定后整图（`link:1`，第 12 帧，第 1 大回合）：`& $G --path src/godot -- --auto-demo --seed=5 --overview --screenshot=E:/wws/geme_demo/art/link-map-surfaces/locked-full-map.png:12 --rounds=1`
>
> 控制台自证行：`[siege] 截图 …：Ok（1600×900，第 12 帧，第 1 大回合，信息层 关，手牌信息面板 关，中央面板 关）`；`[show] 截图时演出：无`。
> 无人值守缺省地图 `link:1`，其中间带新地表共 10 格（tasks 1.3 实施后裁决 F1 / F2 之后重拍）：岩台 V4 / V8（高台块 R3–W9）、荒漠 K14 / M14（栅栏阵块 J10–O15）、浅滩 K8 + 岩台 P4（混合块 J3–Q9）、浅滩 D9 / G9（河流块 C3–H10）、沼泽 G12 / E15（林地块 C11–H16）。

| 文件 | 看什么 |
|---|---|
| `locked-full-map.png` | 插旗锁定后 5 块棋盘全部展开（全局预览）；上述 10 格各有对应的地砖色与点缀部件（`TerrainParts` 的 desert / marsh / crag / shallows，`src/godot/parts/terrain/`），四种都出现 |

人工检查清单（负责人看图 / 试玩）：

1. 四种新地表的地砖与点缀在灰度下彼此可辨，也与草地、林地、深水可辨（转灰度看一遍）。
2. 新地表只出现在各块棋盘的中间带：外圈与中央 3×3 核心区没有新地表；中间带 > 12 格的大棋盘里核心区四邻也没有（生成器口径 S1），6×6 / 6×7 小棋盘免除这一条（F1，`link:1` 的 K14 / M14 / G12 / E15 就贴着核心区）。
3. 河流块只有浅滩且贴着河道或桥；高台块只有岩台且贴着台边；林地块只有沼泽且贴着林团；栅栏阵块只有荒漠且在栅栏旁；混合块每种至多 1 格（某种找不到主题候选时可补在别处，F2）。
4. 盘面层（按 1）图例只列出图上出现过的地表（`link:1` 四种都列；换一张没有某种地表的图时该种不出现）。
5. 浅滩上的棋子气数、岩台的远一格覆盖、沼泽上的棋子不覆盖、荒漠空格不计领地——预演与势力明细与规则一致（规则未改，只是首次出现在链接档地图上）。
