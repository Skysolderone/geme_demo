# 10-12-board-link-mode-f

> 规格权威：`openspec/changes/board-link-mode/`（proposal 裁决表「拼接 / J1–J6」行；design D2（拼接版）；specs：`link-map-generation` 全部（已整体改写为拼接）、`battlefield`「链接事件」、`settlement-show`「棋盘展开节拍」（已改为拼接）、`visual-style-baseline`「未展开区域的可视表现」）。

段 F：拼接重做。负责人 2026-10-12 人工试玩后裁决：链接时两块棋盘边对边紧贴拼成一块连体棋盘，不生成公共棋盘与通道（取代 2B；R1 / R3 / R4 / R6 作废）。对应 tasks.md F.1–F.5，按 J6 顺序：Core 生成与拼接 → Sim → Godot / Presentation → 自检截图 → 10 局小冒烟，每步回传。

## Acceptance Criteria

- [ ] F.1–F.5 勾掉；`link-map-generation` / `battlefield` 拼接相关每个 Scenario 有可追溯测试
- [ ] Legacy 基线与黄金值逐字节不变；链接图决策序列基线重钉并说明
- [ ] 三条 Godot 自检 + `--auto-demo --rounds=0` 退出码 0；截图重拍并更新 README
- [ ] `dotnet build` 零警告、`dotnet test -c Release` 全绿

约束：子 agent 不得 git commit；批量只跑 F.5 的 10 局；同一时间只跑一个 dotnet / Godot；理解代码优先 codegraph；不改规则判定（气 / 提子 / 覆盖 / 活形 / 计分）、AI 评价与权重。
