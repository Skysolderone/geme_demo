# map-elements-v2 段 E：收尾

需求、裁决（M1–M10、O-1–O-4、A1–A3、B1–B5、C1–C3、D1–D4）与验收标准以 `openspec/changes/map-elements-v2/` 为准。

- 范围：tasks 5.1（全量重导、清孤儿材质、退路验证）、5.4（设计文档 §20 条目与变更记录、HANDOFF、全量验证）由 implement agent 做；5.2（perf 对照）、5.3（README 与截图）由主会话做。
- 本分支已合入 main（`terrain-barriers` v1.33，3e29301），链接图已变；验收关键格见 design Context 表"合入后"两行。
- 合入后改前基线（main 的 ba99e84 渲染 + 合入后地图，窗口模式全局预览静止帧）：link:1 绘制调用 773、link:7 779；截图 `art/map-elements-v2/before-barriers/`。
