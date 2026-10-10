# 10-10-board-link-mode-d

> 规格权威：`openspec/changes/board-link-mode/`（proposal 裁决表 D1–D8、S10、P7、Q4–Q6；design D4 / D8 / D9；specs：`map-selection`、`match-setup`「匿名同时插旗」、`information-visibility`、`viewport-camera`、`settlement-show`、`visual-style-baseline`「未展开区域的可视表现」、`simulation-harness`「各入口按地图标识选图」「各入口支持生成图」中图形版部分、`initiative-order`）。

段 D：Godot / Presentation。对应 tasks.md 4.0、4.0b、4.1–4.6，按 D1 分四小段：
- D-a 收口与数据层：`MapCatalog.Resolve` 只接受 `link:`（旧标识只经 `MapCatalog.Legacy`；段 C 遗留的两处测试路径改走 Legacy）、`LaunchArgs` 只接受 `--map=link:`、图形版以 Link 模式建局并挂配对来源（有人值守真随机 / 无人值守固定脚本，实现在 Presentation 或 Godot）、P7 整理（4.0b）、S10 先手预测（4.0）、新局界面（4.1）。
- D-b 插旗占位与按战场渲染（4.2、4.3）。
- D-c 相机与演出（4.4、4.5）。
- D-d 自检与截图（4.6）。

段 B / C 接线点：渲染读 `MatchFlow.PublishFor(seat)`（`SeatCellView.Ownership` 三态）；插旗读 `Flags.ViewFor(seat)`，盲选下 `FlagPublicView.FlagsAt` 会抛越界，表现层不得再读旗数；`SeatVision` 开关缺省全知；`EndReason.LastBattlefieldSettled`；`MatchFlow.Pairings / Links / Settlements` 事件供演出节拍。

## Acceptance Criteria

- [ ] tasks.md 4.0、4.0b、4.1–4.6 全部勾掉，每小段 `--headless --path src/godot -- --auto-demo` 与 `--auto-demo --pick-check` 退出码 0
- [ ] 旧地图不再由任何入口可达；`dotnet test -c Release` 全绿（Legacy 夹具测试照常）
- [ ] 截图（彩色 + 灰度）在 `art/board-link-mode/`：占位、展开、结算横幅、新局界面
- [ ] 负责人人工试玩一局

约束：子 agent 不得 git commit；不跑批量；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改规则判定、AI 评价与权重；Godot 层不含规则计算（守门测试）。
