> 通用约定：Godot 验证前先 `dotnet build src/godot/Siege.Godot.csproj`（Debug）；同一时间只跑一个 dotnet / Godot 进程；截图与 `--perf-sample` 须非 headless、给绝对路径。每段收尾都要：`dotnet test` 全绿、Godot 四条 headless 自检退出 0、`--pick-check` 在 `link:1` 与 `link:7` 上通过，然后截图交负责人，确认后才进下一段。灰度图用 Pillow 转（`ImageOps.grayscale`）。

## 0. 前置

- [x] 0.1 改前基线：窗口模式下对 `link:1`、`link:7` 各取锁定后全局预览定帧截图（`--auto-demo --seed=5 --overview --rounds=1 --screenshot=…:12`）与一组 `--perf-sample` 读数（绘制调用数、单帧耗时），再取 `link:1` 两张、`link:7` 一张近景（`--shot-near` 为本 change 新增的截图选项：配合 `--shot-cell=` 拉到最近缩放）（`--shot-cell=S4 --shot-near` 崖壁与缓坡、`--shot-cell=E8 --shot-near` 河与桥）；记下 headless 自检里的 `[zone]`、`[plates]`、`[parts]`、`[batch]` 自证行。图存 `art/map-elements-v2/before/`，数字与自证行写进实现记录。验证：4 张图、2 组读数、自证行齐全。

## 1. 段 A——建模工具与地块

- [x] 1.1 `LowPolyMesh`：`BeveledSlab` / `Strata` / `FacetRock` / `Ribbon`，用整数散列做抖动和色差，不用 `Random`。验证：两次 `--export-parts` 到不同目录，产物把资源文件里引擎随机生成的外部资源编号、子资源编号与节点 `unique_id` 归一后逐字节相同（裁决 A2）。
- [x] 1.2 部件目录扩展：`Kind` 加锚点说明；新增 `tile_top`（3 档）、`side_slope`（2）、`side_cliff`（3）、`liner`；需要覆盖材质的部件导出时带中性白材质。验证：`--export-parts` 导出成功，日志列出新增部件。
- [x] 1.3 `AddTileStack` 改为取部件（design D3），材质覆盖为按底色共用的那一份，顶点色当亮度系数用；顶板变体按全图坐标散列。验证：
  - 搭建日志：资源数 = 全部、程序建模 = 0；
  - `[zone]`、`[plates]` 自证行与 0.1 逐字相同；
  - 信息层打开时地砖照常压暗（截图）；
  - 出生区淡染和明暗棋盘格照常；接缝处顶板与格线连成一片；
  - `--pick-check` 在 `link:1`、`link:7` 上通过。
- [ ] 1.4 段 A 截图：`link:1` 全局预览、`S4` 近景（崖壁 S4–T4 与缓坡 U4、K4 同屏）、`link:7` 全局预览，彩色和灰度，放进 `art/map-elements-v2/`。验证：负责人确认地块方向，确认后才进入段 B。

## 2. 段 B——水系与设施

- [x] 2.1 `water_bed`、`bank_lip`：每条"邻格不是深水"的边放一件石沿，邻格不在视图里也算（design D4）；水面板和流纹 shader 不变；邻格判断只按 `Dictionary<Coord, Surface>` 查表，不调 Core 邻接 API。验证：`link:1` 一格宽河 D8–G8 的水面可见宽度不小于 0.78 格（截图量像素）；浅滩 D9 / G9 与深水可辨；`Godot层不含规则计算Tests` 通过。
- [x] 2.2 桥重建（栏杆、桥墩、板缝）、栅栏重建（柱帽、横杆略下垂），外形不超出现有包围盒。验证：桥面仍与地砖齐平、可拾取（`link:1` F8、K6）；栅栏不占格（`link:1` N7–O8 截图）；对局中新架的桥、新立的栅栏与预置的同形。
- [ ] 2.3 段 B 截图：`link:1` 河段近景（`--shot-cell=E8 --shot-near`）、`link:7` 河段近景（`--shot-cell=M7 --shot-near`）、全局预览，彩色和灰度。验证：负责人确认。

## 3. 段 C——障碍与装饰

- [ ] 3.1 用 `LowPolyMesh` 重建 `rock`（4→6，服务棋盘内岩石）与 `trees`；`pines` / `ruins` 按裁决 M3 处理。验证：`link:7` Q16、U15 的岩石读作"一格岩石"、不读成林地；装饰高 ≤ 0.75、林地小树仍在棋子底座之外。
- [ ] 3.2 重建四种地表点缀（`desert` / `marsh` / `crag` / `shallows`），保持 terrain-surfaces 的形状约束：沼泽不用锥形树冠、岩台不读成高一层、浅滩齐平、点缀高 ≤ 0.25 且在底座（半径 0.36）之外。验证：`link:1` 灰度图七种地表逐格可辨（人工；关键格见 design Context 表）。
- [ ] 3.3 段 C 截图：`link:1` 新地表近景（`--shot-cell=K14 --shot-near`、`--shot-cell=V8 --shot-near`）、`link:7` 岩石近景（`--shot-cell=Q16 --shot-near`），彩色和灰度。验证：负责人确认。

## 4. 段 D——外框与底座

- [ ] 4.1 底座外圈石沿 `rim`，坐标标注不移动。验证：`link:1` 单字母列标字形、位置与 0.1 相同（截图对比标注区域）；`双字母列标不重叠Tests`、`棋盘坐标标注Tests` 通过。
- [ ] 4.2 出生区描边改为 `zone_strip`，连体外轮廓边框改为 `plate_frame`（高 ≤ 0.07、宽 ≤ 0.14）；占位轮廓与单块边框分支不动。验证：`[zone]`、`[plates]` 自证行与 0.1 逐字相同（接缝边框 0 段）；边缘格上的棋子底座完整可见；`占位轮廓灰度Tests` 通过；插旗阶段截图占位不变。
- [ ] 4.3 浮岛岩层、垂岩、云团改用部件，瀑布不变。验证：`link:1`、`link:7` 全局预览截图。

## 5. 段 E——收尾

- [ ] 5.1 全量重新导出部件，`src/godot/parts/terrain/` 只留本次导出的产物；把资源目录临时挪走验证退回程序建模（日志：资源 0 件、全部程序生成），再挪回。验证：日志与文件清单。
- [ ] 5.2 改后 `--perf-sample` 读数（同 0.1 的两张图），绘制调用数不超过改前的 1.2 倍。超出就按裁决 M7 优化。验证：前后读数对照表写进实现记录。
- [ ] 5.3 人工检查清单 `art/map-elements-v2/README.md`：截图命令、改前改后对照、灰度图，规格各场景逐项列出；同参数重复取图逐像素相同（水面区除外）。按裁决 M5：若 `terrain-barriers` 已进 main，先同步再重拍。验证：负责人逐项确认。
- [ ] 5.4 设计文档 §20 补一条"地图元素部件化与造型精修"，变更记录加一行（版本号按裁决 M10）；`HANDOFF.md` 更新；`dotnet build` 0 警告、`dotnet test` 全绿、Godot 四条自检退出 0、`openspec validate map-elements-v2 --strict` 通过。验证：命令输出。
