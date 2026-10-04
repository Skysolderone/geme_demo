# 10-04-map-elements-v2

> 规格权威：`openspec/changes/map-elements-v2/`。

地图元素全套精修与部件化：新增程序建模工具 `LowPolyMesh`（SurfaceTool、顶点色当亮度系数），部件目录扩展到全部地图元素（地块、水系与设施、障碍与装饰、外框与底座），`--export-parts` 烘成资源，棋盘一律从资源加载、缺失时退回程序建模。

负责人 2026-10-03 裁决：保持现有明快配色，只精修造型；不用 Blender、不用贴图；格距 / 地砖边长 / 层高 / 水面下沉量 / 拾取 / 规则 / 视图模型 / 合批不变；静止帧绘制调用数不超过改前 1.2 倍。

验收：`tasks.md` 各条的验证项；段 A（地块）完成后先交截图给负责人确认方向，确认后才做段 B–E。

约束：未经负责人同意不得 git commit；每段单独提交、`git commit -F`、不加 Co-Authored-By；不跑批量对局；同一时间只跑一个 dotnet / Godot / 子 agent，派子 agent 前先确认；截图只存盘、比对用脚本。
