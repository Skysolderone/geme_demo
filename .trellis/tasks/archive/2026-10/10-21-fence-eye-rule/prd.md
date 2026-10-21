# fence-eye-rule：栅栏不参与活形判定

需求、裁决（F1–F8）与验收标准以 `openspec/changes/fence-eye-rule/`（proposal / design / tasks / specs）为准，这里不重复。

- 背景：§6.4 中栅栏既切分空区又算墙，"立栅即造眼"；冒烟中改造 175 / 178 为立栅（R1）。
- 前置：`stagnation-trace` 归档后实施。
- 后续：R2 + R3（河流 / 崖壁横穿棋盘、用地形缩短对局）合并为一个地图 change，排在本 change 之后。
- 诊断依据（2026-10-21，`sim-out/link-map-surfaces-f1f2` 20 局）：第 4 大回合后 72% 落子在他人棋盘、79% 被提子在受害者出生棋盘之外；各圈层落子比例≈面积比例，"地形不在交战区"不成立。
