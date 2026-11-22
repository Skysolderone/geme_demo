# 执行计划

## 环境（本机 Mac）

- Blender：`/Applications/Blender.app/Contents/MacOS/Blender`（5.3）
- Godot：`~/Applications/godot-4.7.2-mono/Godot_mono.app/Contents/MacOS/Godot`
- 构建：`dotnet build src/godot/Siege.Godot.csproj -v q -p:RestoreConfigFile=$HOME/Applications/godot-4.7.2-mono/nuget.config`
- 本机没有 `timeout`；截图不能加 `--headless`；没有 PIL，裁剪和去色用 ffmpeg。

## 步骤（v2）

1. [ ] 读参考图 `art/chibi-pieces/reference.png`，再读 v1 脚本，确定可以复用的部分（图元封装、断言、导出）。
2. [ ] 脚本：新增共用的闭盔骑士身体（T 字面甲、羽缨），5 个角色 + 5 个建筑；生成 `Anchor_Emblem` 和 `Line_Hull`；断言本体 ≤ 1200、合计 ≤ 2400 三角形，材质槽 ≤ 7。
3. [ ] `PieceModels`：glb 路径去掉八角底座，改为薄阵营色方块；徽记挂到锚点；卡通材质；Line 槽描边材质。tscn / LowPoly 回退保持旧样式。
4. [ ] 确认暂放半透明和拾取对描边的影响，并做相应处理。
5. [ ] 生成全部 glb → `Godot --headless --path src/godot --import` → 构建。
6. [ ] 拍图到 `art/chibi-pieces/`（覆盖 v1 的图，`reference.png` 保留）：
   - `gallery.png`、`gallery-gray.png`、`gallery-gray-small.png`
   - `gallery-factions.png`：四个阵营并排。`PieceGallery` 可以加一个参数，例如 `--piece-gallery-factions`，或者用别的等价办法。
   - `board-near.png`、`board-far.png`
   - `staged.png`：暂放状态（如果能用命令行稳定拍到）
   - 更新 `README.md`：命令和人工检查清单。
7. [ ] 回退验证：移走 `pieces_chibi/` → 确认日志回退到 tscn → 放回。
8. [ ] 全量测试 `dotnet test siege.sln`。已知 `平衡分析方向Tests.内容集v1的报告除第12项外钉值` 在 main 上本来就红，不在本任务范围内。

## 检查关卡

- 步骤 2 完成后：主会话看对照图，造型明显不对就退回步骤 2。
- 步骤 5 完成后：**交给用户看图确认**，确认前不提交。

## 回滚点

- 任一步失败：删掉 `src/godot/parts/pieces_chibi/` 即可回到旧造型。
