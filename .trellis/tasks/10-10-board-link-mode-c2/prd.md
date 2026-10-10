# 10-10-board-link-mode-c2

> 规格权威：`openspec/changes/board-link-mode/`（proposal 裁决表 C18、C19、P1–P4；tasks.md 3b.1–3b.4）。先例：`openspec/changes/archive/2026-10-03-ai-turn-speed/`（design D1–D3 的允许 / 禁止清单与"决策不变的证据"做法）。

段 C2：AI 提速。冒烟里单个 AI 小回合中位 1266 ms、最大 38 s（board:1 在 ai-turn-speed 之后约 130 ms）。先剖析、后优化，决策逐字节不变；优化后跑 C19 的 10 局小冒烟。

## Acceptance Criteria

- [x] 3b.1 剖析报告：两个局面（必要时加 38 秒小回合的复现）的前五项热点与占比
- [x] 3b.2 优化后链接图决策序列基线、v5 / board:1 基线、全部旧模式黄金值逐字节不变；`dotnet build` 零警告、`dotnet test -c Release` 全绿
- [x] 3b.3 改前 / 改后单小回合耗时中位与最大写进 implement.md
- [x] 3b.4 10 局小冒烟报告（C15 效果、耗时、对局长度）写进 implement.md

约束：子 agent 不得 git commit；批量只跑 3b.4 这一批 10 局；同一时间只跑一个 dotnet；理解代码优先 codegraph；不改 Presentation / godot、权重、规则判定。
