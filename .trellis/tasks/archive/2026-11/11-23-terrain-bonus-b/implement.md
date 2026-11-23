# 实施步骤（单个 trellis-implement agent，分两阶段，worktree `E:\wws\geme_demo-terrain-bonus-b`，分支 terrain-bonus-b）

## 阶段 1：Core（做完回传，等主会话复核提交）

1. 先在 worktree 跑一遍构建（快测基线可以不跑，main 已知 2112 / 0 / 1）。
2. 导出 main 版 link:1:2 / 1:3 / 1:4 / 9:4:l 地图文件存到 scratch（对照用）。
3. J1 生成器写出改动 + 测试（岩石只在低地、不在平台 / 座位 / 坡道 / 桥）。
4. J3 地图文件 `V` 码 + 往返测试 + 不一致拒绝测试。
5. J2 下游差异逐项核对并记录。
6. J4 校验预算小样本重钉。
7. 规格：`openspec/specs/terrain/spec.md`（第 12 行"段 B 前仍把岩石写成障碍"那句）、map-definition / link-map-generation / board-topology 等涉及岩石 = 障碍的 Requirement / Scenario 直接改，同名测试。
8. 重录基线（只一次，生成器与校验器都定稿之后）：`SIEGE_REBUILD_DECISION_BASELINE=1` 跑一次 → 重新构建 → 其余黄金哈希取失败信息里的实际值。
9. 快测全绿、构建零警告；变异验证 ≥ 3 条（岩石又进障碍、V 码不校验、预算放空）。
10. 回传：改动文件清单、导出对比差异摘要、J2 差异列表、预算样本数字、重钉的黄金值清单、变异结果、偏离 J1–J4 的地方。

## 阶段 2：表现层（主会话用 SendMessage 续派）

J5 全部；Godot 自检退出 0；`--rounds=6 --map=link:9:4:l` 一次；色块图 → 对照图落盘后停下回传。
