# 10-20-link-map-surfaces

> 规格权威：`openspec/changes/link-map-surfaces/`（proposal 裁决 E1–E9；design D1–D7、S1–S5；tasks 1.1–4.1；specs `link-map-generation`「初始布局」「物化状态校验」、`map-definition`「地图静态校验规则」）。本任务做 **1.1–4.1**；4.2（归档）与提交由负责人线做。

四种新地表（荒漠 / 沼泽 / 岩台 / 浅滩）的规则、AI 感知、Godot 部件都已现成，只是链接档地图不投放。本任务在现有五主题地形与信物全部生成后，用**独立地图随机子流**按主题配对，把每块出生棋盘中间带 2–3 格（混合 2–4 格）高度 0 的空草地改为新地表；现有地形、信物、布局逐格不变。校验器放开中间带、守住外圈与核心区。

## Acceptance Criteria
- [ ] tasks 1.1–4.1 勾选，证据记本目录 `notes.md`
- [ ] `MapRandom.ForLinkSurfaces` 独立子流；`link:1`–`link:20` 与"关掉叠加"的同种子地图对照，只在新地表格的地表取值上不同（对照输出存本目录）
- [ ] 种子 1–20 每块 2–4 格、主题配对正确、全在中间带高度 0 非信物且不在核心区四邻；种子 1–2000 每块格数分布存本目录
- [ ] 校验器 `BOARD_SPECIAL_SURFACE` 新口径；`map-definition` 两条新 Scenario、`link-map-generation`「新地表过多被拒」「新地表按主题投放」「新地表不改其余地形」都有同名测试
- [ ] 5 份决策序列基线与依赖 `link:1` 走法的黄金值重钉，记录首个分歧小回合与落点是否在新地表格或其四邻；`link:90008` 等手写载体测试零改动
- [ ] 冒烟 ≤ 20 局报告（大回合、终局原因、上限结算、五主题平均名次、新地表落子率）；Godot 四条自检退出 0、截图 1 张 + README
- [ ] 设计文档 v1.30、HANDOFF 同步
- [ ] `dotnet build siege.sln -c Release` 与 Godot 构建 0 警告；全量 0 失败、Skip 仍 6

约束：子 agent 不得 git commit / archive；跑局 ≤ 20、不跑批量；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改地表规则语义（`terrain` 规格）、AI 评价与权重、匠人改造规则、布局与主题集；不改 `openspec/` 规格正文；不动飘字动效逻辑；与 design 冲突先记 notes.md 再按 S1–S5 继续。
